using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace RouteFinding.Codex
{
    // 도감 우측 상세 카드 — 타입 배지/시간/내용/출처/맵/키워드를 표시한다.
    // 내용이 넘칠 수 있어(특히 4단계 코멘트 추가 이후) ScrollRect로 감싸 드래그/휠 스크롤을 지원한다.
    // 3단계: 유저 메모(CodexEntry.userEntryGuid가 있는 항목)에는 편집/삭제 버튼을 추가로 보여준다.
    // 2단계(노트): 정식 단서(clueId가 있는 항목)에는 "노트에 핀" 버튼 + 핀 시 관련 단서 추천 목록을 보여준다
    // (NoteSystem_기획서.md "노트 편입 규칙 2" 참고 — CodexPanel이 CodexFilterService.GroupByKeyword로
    // 추천을 계산해 ShowSuggestions에 넘긴다. 이 뷰는 표시·클릭 이벤트 발행만 담당).
    // 4단계: NPC/시스템 코멘트(CodexEntry.comments) — 플레이어 입력창이 아니라 이미 정해진 코멘트가
    // 타이프라이터 연출로 출력되는 읽기 전용 영역이다(Clue_System.md 1-4장 확정 사항).
    //
    // 추천/코멘트 행은 UiRowPool로 재사용한다 — _suggestionRowTemplate/_commentRowTemplate에 프리팹을
    // 지정하면 Prefab Mode에서 실제 행 하나를 자유롭게 디자인할 수 있고, 비워두면 아래 스타일 값으로
    // 만든 기본 템플릿을 그대로 쓴다.
    //
    // 프리팹 재사용(CodexPanel 참고): Init()은 처음 생성할 때만 호출되고, 프리팹을 재사용할 때는
    // Bind()가 이름으로 기존 자식을 재탐색해 참조를 복원하고 버튼 onClick을 다시 연결한다
    // (Instantiate는 private 필드와 런타임에 AddListener한 콜백을 보존하지 않기 때문).
    public class CodexCardView : MonoBehaviour
    {
        public event Action<CodexEntry> OnEditRequested;
        public event Action<CodexEntry> OnDeleteRequested;
        public event Action<CodexEntry> OnPinRequested;
        public event Action<CodexEntry> OnSuggestionAddRequested;
        public event Action<string> OnKeywordClicked; // 6-4단계 — 키워드 태그 클릭
        public event Action<string> OnMapRefClicked;  // 첨부물(맵 참조)의 "지도" 버튼 — 인자는 맵 GUID

        private TextMeshProUGUI _titleTmp;
        private GameObject _typeBadgeGO;
        private TextMeshProUGUI _typeBadgeTmp;
        private TextMeshProUGUI _timestampTmp;
        private TextMeshProUGUI _contentTmp;
        private TextMeshProUGUI _sourceTmp;
        private TextMeshProUGUI _mapTmp;
        private TextMeshProUGUI _keywordsTmp;
        private CodexKeywordLinkHandler _keywordLinkHandler; // 6-4단계 — _keywordsTmp에 붙는 클릭 감지기
        private GameObject _editRowGO;

        private GameObject _pinRowGO;
        private Button _pinBtn;
        private TextMeshProUGUI _pinBtnLabel;
        private RectTransform _suggestionsRT;
        private GameObject _suggestionsGO;

        private GameObject _attachmentsSectionGO;
        private RectTransform _attachmentsRT;

        private GameObject _commentsSectionGO;
        private RectTransform _commentsRT;
        private readonly List<Coroutine> _typewriterCoroutines = new();

        // 첨부 소리 재생용 — 노트 그래프와 같은 헬퍼를 쓴다(ClueAttachmentAudioPlayer 참고).
        private ClueAttachmentAudioPlayer _audio;

        private TMP_FontAsset _font;
        private CodexEntry _currentEntry;

        private const float TypewriterSecondsPerChar = 0.02f;

        [Header("행 템플릿 (선택 — 비워두면 아래 스타일 값으로 기본 템플릿 생성)")]
        [SerializeField] private GameObject _suggestionRowTemplate;
        [SerializeField] private GameObject _commentRowTemplate;
        [SerializeField] private GameObject _attachmentRowTemplate;

        // 추천/코멘트 행은 ShowSuggestions()/RefreshComments()마다 UiRowPool에서 재사용된다 —
        // 커스텀 템플릿을 안 쓸 때의 기본 템플릿 스타일로 쓰인다.
        [Header("기본 템플릿 스타일 (프리팹 미지정 시)")]
        [SerializeField] private Color _colMuted = new(0.55f, 0.60f, 0.65f);
        [SerializeField] private Color _colBadge = new(0.25f, 0.42f, 0.72f);
        [SerializeField] private Color _colGreenBtn = new(0.15f, 0.32f, 0.19f);
        [SerializeField] private float _dynamicRowFontSize = 7f;
        [SerializeField] private float _suggestionRowHeight = 12f;
        [SerializeField] private float _commentRowHeight = 28f;
        [Tooltip("첨부물 한 줄(아이콘 + 이름 + 버튼)의 높이 — 사진 첨부는 여기에 미리보기 높이가 더 붙는다")]
        [SerializeField] private float _attachmentRowHeight = 14f;
        [Tooltip("사진 첨부의 미리보기 높이(가로는 원본 비율 유지)")]
        [SerializeField] private float _imagePreviewHeight = 60f;

        private UiRowPool _suggestionPool;
        private UiRowPool _commentPool;
        private UiRowPool _attachmentPool;

        public void Init(RectTransform parent, TMP_FontAsset font)
        {
            _font = font;
            var content = BuildScrollContent(parent);

            var headerRow = NewRect(content, "HeaderRow");
            var headerLe = headerRow.gameObject.AddComponent<LayoutElement>();
            headerLe.preferredHeight = 12f;
            headerLe.flexibleWidth = 1f;
            var hlg = headerRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            _titleTmp = MakeTMP(headerRow, font, "", 8f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, id: "TitleLabel");
            _titleTmp.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var badgeRT = NewRect(headerRow, "TypeBadge");
            _typeBadgeGO = badgeRT.gameObject;
            var badgeLe = badgeRT.gameObject.AddComponent<LayoutElement>();
            badgeLe.preferredWidth = 34f;
            badgeLe.flexibleWidth = 0f;
            AddImg(badgeRT, _colBadge);
            var badgeTxtRT = NewRect(badgeRT, "Text");
            StretchFull(badgeTxtRT);
            _typeBadgeTmp = badgeTxtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) _typeBadgeTmp.font = font;
            _typeBadgeTmp.fontSize = 7f;
            _typeBadgeTmp.alignment = TextAlignmentOptions.Center;
            _typeBadgeTmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            _typeBadgeTmp.color = Color.white;

            _timestampTmp = MakeTMP(content, font, "", 7f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, height: 12f, id: "TimestampLabel");
            _timestampTmp.color = _colMuted;

            MakeSep(content);

            _contentTmp = MakeTMP(content, font, "", 7f, FontStyles.Normal, TextAlignmentOptions.TopLeft, height: 50f, id: "ContentLabel");
            _contentTmp.enableWordWrapping = true;

            MakeSep(content);

            MakeLabel(content, font, "출처");
            _sourceTmp = MakeTMP(content, font, "", 7f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, height: 12f, id: "SourceValue");

            MakeLabel(content, font, "맵");
            _mapTmp = MakeTMP(content, font, "", 7f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, height: 12f, id: "MapValue");

            MakeLabel(content, font, "키워드");
            _keywordsTmp = MakeTMP(content, font, "", 7f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, height: 12f, id: "KeywordsValue");
            _keywordsTmp.enableWordWrapping = true;
            _keywordLinkHandler = _keywordsTmp.gameObject.AddComponent<CodexKeywordLinkHandler>();
            _keywordLinkHandler.Text = _keywordsTmp;
            _keywordLinkHandler.OnLinkClicked = kw => OnKeywordClicked?.Invoke(kw);

            BuildAttachmentsSection(content, font);
            BuildEditRow(content, font);
            BuildPinRow(content, font);
            BuildSuggestionsArea(content, font);
            BuildCommentsSection(content, font);
            EnsurePools();

            ShowEmpty();
        }

        // 프리팹 재사용 시 호출 — Init()이 다시 만들지 않고, 이미 존재하는 자식들을 이름으로 재탐색해
        // 참조를 복원하고 버튼 클릭 콜백을 다시 연결한다.
        public void Bind(RectTransform existingRoot)
        {
            _titleTmp = FindDeepChild<TextMeshProUGUI>(existingRoot, "TitleLabel");

            var badgeTF = FindDeepTransform(existingRoot, "TypeBadge");
            _typeBadgeGO = badgeTF?.gameObject;
            _typeBadgeTmp = badgeTF != null ? badgeTF.GetComponentInChildren<TextMeshProUGUI>() : null;

            _timestampTmp = FindDeepChild<TextMeshProUGUI>(existingRoot, "TimestampLabel");
            _contentTmp   = FindDeepChild<TextMeshProUGUI>(existingRoot, "ContentLabel");
            _sourceTmp    = FindDeepChild<TextMeshProUGUI>(existingRoot, "SourceValue");
            _mapTmp       = FindDeepChild<TextMeshProUGUI>(existingRoot, "MapValue");
            _keywordsTmp  = FindDeepChild<TextMeshProUGUI>(existingRoot, "KeywordsValue");
            if (_keywordsTmp != null)
            {
                _keywordLinkHandler = _keywordsTmp.gameObject.GetComponent<CodexKeywordLinkHandler>()
                                      ?? _keywordsTmp.gameObject.AddComponent<CodexKeywordLinkHandler>();
                _keywordLinkHandler.Text = _keywordsTmp;
                _keywordLinkHandler.OnLinkClicked = kw => OnKeywordClicked?.Invoke(kw);
            }

            var editRowTF = FindDeepTransform(existingRoot, "EditRow");
            _editRowGO = editRowTF?.gameObject;
            if (editRowTF != null)
            {
                FindDeepTransform(editRowTF, "Btn_편집")?.GetComponent<Button>()?.onClick.AddListener(() => OnEditRequested?.Invoke(_currentEntry));
                FindDeepTransform(editRowTF, "Btn_삭제")?.GetComponent<Button>()?.onClick.AddListener(() => OnDeleteRequested?.Invoke(_currentEntry));
            }

            // Bind()는 font 인자를 받지 않는다 — 이미 프리팹에 구워진 라벨의 font를 그대로 재사용한다.
            _font = _titleTmp != null ? _titleTmp.font : null;

            // 첨부 영역(2026-08-11 신설)은 그 이전에 저장된 프리팹/씬 패널에는 아예 없다. 패널 전체를
            // 파괴하고 다시 만들면 작업자가 프리팹에서 손본 디자인이 통째로 날아가므로, 없는 영역만
            // 제자리에서 만들어 끼워 넣는다(편집 행 바로 앞 = Init()이 만드는 순서와 같은 위치).
            var attachmentsTF = FindDeepTransform(existingRoot, "AttachmentsSection");
            if (attachmentsTF == null)
            {
                var contentTF = FindDeepTransform(existingRoot, "Content") as RectTransform;
                if (contentTF != null)
                {
                    BuildAttachmentsSection(contentTF, _font);
                    if (_editRowGO != null)
                        _attachmentsSectionGO.transform.SetSiblingIndex(_editRowGO.transform.GetSiblingIndex());
                }
                else Debug.LogWarning("[CodexCardView] Bind: Content를 찾지 못해 첨부 영역을 만들지 못했습니다.");
            }
            else
            {
                _attachmentsSectionGO = attachmentsTF.gameObject;
                _attachmentsRT = attachmentsTF as RectTransform;
            }

            var pinRowTF = FindDeepTransform(existingRoot, "PinRow");
            _pinRowGO = pinRowTF?.gameObject;
            _pinBtn = pinRowTF?.GetComponent<Button>();
            _pinBtnLabel = pinRowTF != null ? FindDeepChild<TextMeshProUGUI>(pinRowTF, "Text") : null;
            _pinBtn?.onClick.AddListener(() => OnPinRequested?.Invoke(_currentEntry));

            var suggestionsTF = FindDeepTransform(existingRoot, "Suggestions");
            _suggestionsGO = suggestionsTF?.gameObject;
            _suggestionsRT = suggestionsTF as RectTransform;

            var commentsTF = FindDeepTransform(existingRoot, "CommentsSection");
            _commentsSectionGO = commentsTF?.gameObject;
            _commentsRT = commentsTF as RectTransform;

            EnsurePools();
            ShowEmpty();
        }

        private void EnsurePools()
        {
            _suggestionPool ??= new UiRowPool(_suggestionRowTemplate, BuildSuggestionRowTemplate);
            _commentPool ??= new UiRowPool(_commentRowTemplate, BuildCommentRowTemplate);
            _attachmentPool ??= new UiRowPool(_attachmentRowTemplate, BuildAttachmentRowTemplate);
        }

        // 카드 내용이 영역보다 길어질 수 있어(코멘트 등) 드래그/휠 스크롤이 되도록 감싼다.
        private static RectTransform BuildScrollContent(RectTransform parent)
        {
            var scroll = parent.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 8f;

            var vp = NewRect(parent, "Viewport");
            StretchFull(vp);
            vp.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = vp;

            var content = NewRect(vp, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            scroll.content = content;

            var csf = content.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.spacing = 4f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            return content;
        }

        // 유저 메모(userEntryGuid가 있는 항목)에만 보이는 편집/삭제 버튼 — 정식 단서(ClueData)는 읽기 전용.
        private void BuildEditRow(RectTransform parent, TMP_FontAsset font)
        {
            var row = NewRect(parent, "EditRow");
            _editRowGO = row.gameObject;
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 12f;
            le.flexibleWidth = 1f;
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            MakeSmallBtn(row, font, "편집", () => OnEditRequested?.Invoke(_currentEntry));
            MakeSmallBtn(row, font, "삭제", () => OnDeleteRequested?.Invoke(_currentEntry));
        }

        // 정식 단서(clueId가 있는 항목)에만 보이는 "노트에 핀" 버튼. 유저 메모는 clueId가 없어
        // NoteEntry로 표현할 수 없으므로 이 버튼 자체가 안 보인다(ShowEntry 참고).
        private void BuildPinRow(RectTransform parent, TMP_FontAsset font)
        {
            var row = NewRect(parent, "PinRow");
            _pinRowGO = row.gameObject;
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 12f;
            le.flexibleWidth = 1f;

            var img = AddImg(row, _colGreenBtn);
            _pinBtn = row.gameObject.AddComponent<Button>();
            _pinBtn.targetGraphic = img;
            _pinBtn.transition = Selectable.Transition.None;
            _pinBtn.onClick.AddListener(() => OnPinRequested?.Invoke(_currentEntry));

            var txtRT = NewRect(row, "Text");
            StretchFull(txtRT);
            _pinBtnLabel = txtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) _pinBtnLabel.font = font;
            _pinBtnLabel.fontSize = _dynamicRowFontSize;
            _pinBtnLabel.alignment = TextAlignmentOptions.Center;
            _pinBtnLabel.verticalAlignment = VerticalAlignmentOptions.Middle;
            _pinBtnLabel.color = Color.white;
        }

        // 핀 직후 CodexPanel이 계산한 "관련 단서 추천"(키워드 공유 기준) 목록을 보여준다.
        // 추천은 자동으로 담기지 않고, 항목마다 개별 "추가" 버튼을 눌러야 노트에 들어간다
        // (NoteSystem_기획서.md 규칙 2 — 실수로 대량 편입되는 것 방지).
        private void BuildSuggestionsArea(RectTransform parent, TMP_FontAsset font)
        {
            var area = NewRect(parent, "Suggestions");
            _suggestionsGO = area.gameObject;
            _suggestionsRT = area;
            var le = area.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 12f;
            le.flexibleWidth = 1f;

            var vlg = area.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var header = MakeTMP(area, font, "관련 단서 추천", 7f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, height: 12f, id: "SuggestionsHeader");
            header.color = _colMuted;

            area.gameObject.SetActive(false);
        }

        // CodexPanel.HandlePinRequested가 핀 성공 직후 호출 — suggestions는 이미 핀된 항목을 제외하고 넘어온다.
        public void ShowSuggestions(List<CodexEntry> suggestions)
        {
            bool hasAny = suggestions != null && suggestions.Count > 0;
            _suggestionsGO.SetActive(hasAny);
            if (!hasAny) { _suggestionPool.EndPass(); return; }

            foreach (var s in suggestions)
                PopulateSuggestionRow(_suggestionPool.Get(_suggestionsRT), s);
            _suggestionPool.EndPass();

            _suggestionsGO.GetComponent<LayoutElement>().preferredHeight = 12f + suggestions.Count * (_suggestionRowHeight + 2f);
        }

        private void PopulateSuggestionRow(GameObject row, CodexEntry suggestion)
        {
            // 직속 자식만 찾는다(Transform.Find는 재귀 아님) — BtnAdd 안에도 "Text"가 있어 이름이 겹친다.
            var tmp = row.transform.Find("Text")?.GetComponent<TextMeshProUGUI>();
            if (tmp != null) tmp.text = suggestion.title;

            var addBtn = row.transform.Find("BtnAdd")?.GetComponent<Button>();
            if (addBtn != null)
            {
                addBtn.onClick.RemoveAllListeners();
                addBtn.onClick.AddListener(() => OnSuggestionAddRequested?.Invoke(suggestion));
            }
        }

        private GameObject BuildSuggestionRowTemplate()
        {
            var row = NewRect(null, "SuggestionRow");
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = _suggestionRowHeight;
            le.flexibleWidth = 1f;
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 3f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var textRT = NewRect(row, "Text");
            var textLe = textRT.gameObject.AddComponent<LayoutElement>();
            textLe.flexibleWidth = 1f;
            var tmp = textRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.fontSize = _dynamicRowFontSize;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.overflowMode = TextOverflowModes.Ellipsis;

            var addBtnRT = NewRect(row, "BtnAdd");
            var addLe = addBtnRT.gameObject.AddComponent<LayoutElement>();
            addLe.preferredWidth = 26f;
            addLe.flexibleWidth = 0f;
            var addImg = AddImg(addBtnRT, _colGreenBtn);
            var addBtn = addBtnRT.gameObject.AddComponent<Button>();
            addBtn.targetGraphic = addImg;
            addBtn.transition = Selectable.Transition.None;

            var addTxtRT = NewRect(addBtnRT, "Text");
            StretchFull(addTxtRT);
            var addTmp = addTxtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) addTmp.font = _font;
            addTmp.text = "추가";
            addTmp.fontSize = _dynamicRowFontSize;
            addTmp.alignment = TextAlignmentOptions.Center;
            addTmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            addTmp.color = Color.white;

            row.gameObject.SetActive(false);
            return row.gameObject;
        }

        // ─── 첨부물 (사진 / 소리 / 맵 아이콘·이름) ─────────────────

        // 첨부 영역 자체(구분선 + "첨부" 헤더)는 정적으로 한 번만 만든다 — 실제 첨부 행만
        // UiRowPool로 재사용된다(코멘트 영역과 동일한 구조).
        private void BuildAttachmentsSection(RectTransform parent, TMP_FontAsset font)
        {
            var section = NewRect(parent, "AttachmentsSection");
            _attachmentsSectionGO = section.gameObject;
            _attachmentsRT = section;
            var le = section.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 25f;
            le.flexibleWidth = 1f;

            var vlg = section.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            MakeSep(section);
            var header = MakeTMP(section, font, "첨부", 7f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, height: 12f, id: "AttachmentsHeader");
            header.color = _colMuted;

            section.gameObject.SetActive(false);
        }

        // ShowEntry가 카드를 바꿀 때마다 호출. 행 높이가 종류마다 달라(사진은 미리보기가 붙는다)
        // 각 행이 실제로 쓴 높이를 합산해 영역 높이를 잡는다.
        private void RefreshAttachments(ClueAttachment[] attachments)
        {
            StopAudio(); // 카드를 바꾸면 이전 카드에서 재생 중이던 소리는 멈춘다

            bool hasAny = attachments != null && attachments.Length > 0;
            _attachmentsSectionGO?.SetActive(hasAny);
            if (!hasAny || _attachmentsRT == null) { _attachmentPool?.EndPass(); return; }

            float total = 17f; // 구분선(1) + 헤더(12) + spacing*2(4)
            foreach (var a in attachments)
            {
                if (a == null) continue;
                total += PopulateAttachmentRow(_attachmentPool.Get(_attachmentsRT), a) + 2f;
            }
            _attachmentPool.EndPass();

            _attachmentsSectionGO.GetComponent<LayoutElement>().preferredHeight = total;
        }

        // 한 행을 첨부물 하나로 채우고, 그 행이 차지한 높이를 돌려준다.
        // 에셋을 못 찾아도(경로 오타 등) 행을 숨기지 않고 "(파일 없음)"으로 표시한다 — 조용히 사라지면
        // 콘텐츠 작업자가 첨부를 붙였는지조차 확인할 수 없기 때문.
        private float PopulateAttachmentRow(GameObject row, ClueAttachment a)
        {
            var head = row.transform.Find("Head");
            var iconImg   = head != null ? head.Find("Icon")?.GetComponent<Image>() : null;
            var labelTmp  = head != null ? head.Find("Text")?.GetComponent<TextMeshProUGUI>() : null;
            var btnTF     = head != null ? head.Find("BtnAction") : null;
            var btn       = btnTF != null ? btnTF.GetComponent<Button>() : null;
            var btnLabel  = btnTF != null ? btnTF.Find("Text")?.GetComponent<TextMeshProUGUI>() : null;
            var previewTF = row.transform.Find("Preview");
            var previewImg = previewTF != null ? previewTF.GetComponent<Image>() : null;

            btn?.onClick.RemoveAllListeners();

            string label = ClueAttachmentService.ResolveLabel(a);
            bool showBtn = false, showPreview = false;
            Sprite icon = ClueAttachmentService.ResolveIcon(a);
            bool missing = false;

            switch (a.kind)
            {
                case ClueAttachmentKind.Image:
                {
                    var sprite = ClueAttachmentService.LoadSprite(a.address);
                    missing = sprite == null;
                    if (!missing && previewImg != null)
                    {
                        previewImg.sprite = sprite;
                        previewImg.color = Color.white;
                        showPreview = true;
                    }
                    break;
                }
                case ClueAttachmentKind.Audio:
                {
                    var clip = ClueAttachmentService.LoadAudio(a.address);
                    missing = clip == null;
                    showBtn = !missing;
                    if (showBtn)
                    {
                        if (btnLabel != null) btnLabel.text = PlayLabel;
                        btn?.onClick.AddListener(() => ToggleAudio(a, btnLabel));
                    }
                    break;
                }
                case ClueAttachmentKind.MapRef:
                {
                    var node = ClueAttachmentService.ResolveMapNode(a);
                    missing = node == null;
                    showBtn = !missing;
                    if (showBtn)
                    {
                        if (btnLabel != null) btnLabel.text = "지도";
                        string guid = a.mapGuid;
                        btn?.onClick.AddListener(() => OnMapRefClicked?.Invoke(guid));
                    }
                    break;
                }
            }

            if (labelTmp != null)
            {
                string kindTag = ClueAttachmentConfig.GetDisplayName(a.kind);
                labelTmp.text = missing
                    ? $"[{kindTag}] {label}  <color=#C86A6A>(파일 없음)</color>"
                    : $"[{kindTag}] {label}";
            }

            if (iconImg != null)
            {
                iconImg.gameObject.SetActive(icon != null);
                if (icon != null)
                {
                    iconImg.sprite = icon;
                    iconImg.color = Color.white;
                }
            }
            btnTF?.gameObject.SetActive(showBtn);
            previewTF?.gameObject.SetActive(showPreview);

            // 행 높이는 종류마다 다르다(사진만 미리보기가 붙는다). 바깥 목록이 자식 높이를 제어하든
            // 안 하든 같은 결과가 나오도록 LayoutElement와 실제 크기를 함께 맞춘다.
            float height = _attachmentRowHeight + (showPreview ? _imagePreviewHeight + 2f : 0f);
            var le = row.GetComponent<LayoutElement>();
            if (le != null) le.preferredHeight = height;
            var rowRT = row.GetComponent<RectTransform>();
            if (rowRT != null) rowRT.sizeDelta = new Vector2(rowRT.sizeDelta.x, height);
            return height;
        }

        private GameObject BuildAttachmentRowTemplate()
        {
            var rowRT = NewRect(null, "AttachmentRow");
            var le = rowRT.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = _attachmentRowHeight;
            le.flexibleWidth = 1f;

            // 행 안쪽만은 childControlHeight를 켠다 — 한 행이 머리줄(고정 높이)과 사진 미리보기(가변)로
            // 나뉘고, 사진이 없는 첨부는 미리보기를 통째로 끄기 때문에 자식 높이를 LayoutElement로
            // 제어할 수 있어야 한다.
            var vlg = rowRT.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var head = NewRect(rowRT, "Head");
            var headLe = head.gameObject.AddComponent<LayoutElement>();
            headLe.preferredHeight = _attachmentRowHeight;
            headLe.flexibleWidth = 1f;
            var hlg = head.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 3f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var iconRT = NewRect(head, "Icon");
            var iconLe = iconRT.gameObject.AddComponent<LayoutElement>();
            iconLe.preferredWidth = _attachmentRowHeight - 2f;
            iconLe.flexibleWidth = 0f;
            var iconImg = iconRT.gameObject.AddComponent<Image>();
            iconImg.preserveAspect = true;

            var textRT = NewRect(head, "Text");
            var textLe = textRT.gameObject.AddComponent<LayoutElement>();
            textLe.flexibleWidth = 1f;
            var tmp = textRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.fontSize = _dynamicRowFontSize;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.overflowMode = TextOverflowModes.Ellipsis;

            var btnRT = NewRect(head, "BtnAction");
            var btnLe = btnRT.gameObject.AddComponent<LayoutElement>();
            btnLe.preferredWidth = 34f;
            btnLe.flexibleWidth = 0f;
            var btnImg = AddImg(btnRT, _colBadge);
            var btn = btnRT.gameObject.AddComponent<Button>();
            btn.targetGraphic = btnImg;
            btn.transition = Selectable.Transition.None;

            var btnTxtRT = NewRect(btnRT, "Text");
            StretchFull(btnTxtRT);
            var btnTmp = btnTxtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) btnTmp.font = _font;
            btnTmp.fontSize = _dynamicRowFontSize;
            btnTmp.alignment = TextAlignmentOptions.Center;
            btnTmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            btnTmp.color = Color.white;

            var previewRT = NewRect(rowRT, "Preview");
            var previewLe = previewRT.gameObject.AddComponent<LayoutElement>();
            previewLe.preferredHeight = _imagePreviewHeight;
            previewLe.flexibleWidth = 1f;
            var previewImg = previewRT.gameObject.AddComponent<Image>();
            previewImg.preserveAspect = true;

            rowRT.gameObject.SetActive(false);
            return rowRT.gameObject;
        }

        // ─── 첨부 소리 재생 ────────────────────────────────────────

        private const string PlayLabel = "▶ 재생";
        private const string StopLabel = "■ 정지";

        private void ToggleAudio(ClueAttachment a, TextMeshProUGUI btnLabel)
        {
            var clip = ClueAttachmentService.LoadAudio(a.address);
            if (clip == null) return;

            if (_audio == null) _audio = ClueAttachmentAudioPlayer.AttachTo(gameObject);
            _audio.Toggle(clip, playing =>
            {
                if (btnLabel != null) btnLabel.text = playing ? StopLabel : PlayLabel;
            });
        }

        // 카드를 바꿀 때·도감을 닫을 때(CodexPanel.CloseWindowContent) 호출.
        public void StopAudio() => _audio?.Stop();

        // ─── 4단계: NPC/시스템 코멘트 (타이프라이터 연출) ─────────

        // 코멘트 영역 자체(구분선 + "코멘트" 헤더)는 정적으로 한 번만 만든다 — 실제 코멘트 행만
        // UiRowPool로 재사용된다.
        private void BuildCommentsSection(RectTransform parent, TMP_FontAsset font)
        {
            var section = NewRect(parent, "CommentsSection");
            _commentsSectionGO = section.gameObject;
            _commentsRT = section;
            var le = section.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 25f;
            le.flexibleWidth = 1f;

            var vlg = section.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            MakeSep(section);
            var header = MakeTMP(section, font, "코멘트", 7f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, height: 12f, id: "CommentsHeader");
            header.color = _colMuted;

            section.gameObject.SetActive(false);
        }

        // ShowEntry가 카드를 바꿀 때마다 호출 — 이전 카드의 타이프라이터 코루틴을 멈추고 새로 채운다.
        private void RefreshComments(CodexComment[] comments)
        {
            foreach (var co in _typewriterCoroutines)
                if (co != null) StopCoroutine(co);
            _typewriterCoroutines.Clear();

            bool hasAny = comments != null && comments.Length > 0;
            _commentsSectionGO?.SetActive(hasAny);
            if (!hasAny) { _commentPool?.EndPass(); return; }

            foreach (var c in comments)
                PopulateCommentRow(_commentPool.Get(_commentsRT), c);
            _commentPool.EndPass();

            // 구분선(1) + 헤더(12) + spacing*2(4) + 코멘트 행(N * (행높이+spacing)) — 근사 계산.
            _commentsSectionGO.GetComponent<LayoutElement>().preferredHeight = 17f + comments.Length * (_commentRowHeight + 2f);
        }

        private void PopulateCommentRow(GameObject row, CodexComment comment)
        {
            var tmp = row.GetComponent<TextMeshProUGUI>();
            if (tmp == null) return;

            tmp.text = "";
            bool hasTime = !string.IsNullOrEmpty(comment.createdAt);
            string full = (hasTime ? $"[{comment.createdAt}] " : "") + $"{comment.author}: {comment.text}";
            _typewriterCoroutines.Add(StartCoroutine(TypewriterReveal(tmp, full)));
        }

        private GameObject BuildCommentRowTemplate()
        {
            var rowRT = NewRect(null, "CommentRow");
            var le = rowRT.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = _commentRowHeight; // 코멘트 본문이 길 수 있어 다른 상세 필드보다 넉넉하게
            le.flexibleWidth = 1f;

            var tmp = rowRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.fontSize = _dynamicRowFontSize;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.enableWordWrapping = true;

            rowRT.gameObject.SetActive(false);
            return rowRT.gameObject;
        }

        // 플레이어 입력창이 아니라, 이미 정해진 코멘트가 타이핑되듯 출력되는 연출(Clue_System.md 1-4장).
        private IEnumerator TypewriterReveal(TextMeshProUGUI tmp, string full)
        {
            var sb = new StringBuilder();
            foreach (var ch in full)
            {
                sb.Append(ch);
                tmp.text = sb.ToString();
                yield return new WaitForSeconds(TypewriterSecondsPerChar);
            }
        }

        public void ShowEmpty()
        {
            _currentEntry = null;
            _titleTmp.text = "← 좌측에서 단서를 선택하세요";
            _typeBadgeTmp.text = "";
            _typeBadgeGO.SetActive(false);
            _timestampTmp.gameObject.SetActive(false);
            _contentTmp.text = "";
            _sourceTmp.text = "";
            _mapTmp.text = "";
            _keywordsTmp.text = "";
            _editRowGO.SetActive(false);
            _pinRowGO?.SetActive(false);
            _suggestionsGO?.SetActive(false);
            RefreshAttachments(null);
            RefreshComments(null);
        }

        public void ShowEntry(CodexEntry e)
        {
            _currentEntry = e;
            _titleTmp.text = e.title;

            bool hasType = !string.IsNullOrEmpty(e.typeLabel);
            _typeBadgeGO.SetActive(hasType);
            if (hasType) _typeBadgeTmp.text = e.typeLabel;

            bool hasTime = !string.IsNullOrEmpty(e.timestamp);
            _timestampTmp.gameObject.SetActive(hasTime);
            if (hasTime) _timestampTmp.text = e.timestamp;

            _contentTmp.text = e.content;
            _sourceTmp.text = string.IsNullOrEmpty(e.source) ? "-" : e.source;
            _mapTmp.text = string.IsNullOrEmpty(e.mapCategory) ? "기타" : e.mapCategory;
            _keywordsTmp.text = BuildKeywordsText(e.keywords);

            _editRowGO.SetActive(!string.IsNullOrEmpty(e.userEntryGuid));

            // 유저 메모는 clueId가 없어 핀 대상이 아니다 — 버튼 자체를 숨긴다.
            bool pinnable = !string.IsNullOrEmpty(e.clueId);
            _pinRowGO?.SetActive(pinnable);
            if (pinnable && _pinBtn != null)
            {
                bool pinned = NoteModule.Instance != null && NoteModule.Instance.IsPinned(e.clueId);
                _pinBtn.interactable = !pinned;
                if (_pinBtnLabel != null) _pinBtnLabel.text = pinned ? "노트에 핀됨" : "노트에 핀";
            }

            // 카드를 다른 항목으로 바꾸면 이전 항목의 추천 목록은 의미가 없으므로 접는다.
            _suggestionsGO?.SetActive(false);

            RefreshAttachments(e.attachments);
            RefreshComments(e.comments);
        }

        // [버그 수정, 2026-07-21] 이 카드가 어떤 단서를 보여준 채로 남아있는 동안, 노트 쪽에서 그 단서의
        // 핀이 풀리면(예: 노트 그래프에서 삭제) 카드의 "노트에 핀" 버튼은 그 사실을 모른 채 계속
        // interactable=false("노트에 핀됨")로 남아있었다 — 도감을 닫았다 다시 열어도 ShowEntry가 그
        // 항목에 대해 다시 호출되지 않는 한(예: 같은 항목이 이미 선택돼 있던 채로 재오픈) 갱신될 계기가
        // 없어서, 한 번 삭제한 단서를 도감에서 다시 꺼낼 방법이 없어 보였다. CodexPanel.Open()이 열 때마다
        // 이 메서드를 호출해 지금 보여주고 있는 항목의 핀 상태만 가볍게 다시 확인한다(카드 전체를
        // 다시 그리지 않음 — ShowEntry를 통째로 재호출하면 스크롤 위치·펼침 상태 등이 불필요하게 리셋됨).
        public void RefreshPinState()
        {
            if (_currentEntry == null || _pinBtn == null) return;
            if (string.IsNullOrEmpty(_currentEntry.clueId)) return; // 핀 대상 아님(유저 메모)

            bool pinned = NoteModule.Instance != null && NoteModule.Instance.IsPinned(_currentEntry.clueId);
            _pinBtn.interactable = !pinned;
            if (_pinBtnLabel != null) _pinBtnLabel.text = pinned ? "노트에 핀됨" : "노트에 핀";
        }

        // 6-4단계 — 키워드마다 TMP <link> 태그를 씌운다. 별도 버튼 GameObject를 여러 개 만들지 않고도
        // (줄바꿈 자동 처리를 TMP의 기본 워드랩에 그대로 맡길 수 있어서) 한 텍스트 블록 안에서
        // 키워드별 클릭을 구분할 수 있다 — CodexKeywordLinkHandler가 TMP_TextUtilities.FindIntersectingLink로
        // 클릭 위치가 어느 링크(키워드) 위인지 찾아낸다.
        private static string BuildKeywordsText(string[] keywords)
        {
            if (keywords == null || keywords.Length == 0) return "-";

            var sb = new StringBuilder();
            for (int i = 0; i < keywords.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                var kw = keywords[i];
                sb.Append("<link=\"").Append(kw).Append("\"><u><color=#8FC1E3>").Append(kw).Append("</color></u></link>");
            }
            return sb.ToString();
        }

        private void MakeLabel(RectTransform parent, TMP_FontAsset font, string text)
        {
            MakeTMP(parent, font, text, 7f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, height: 12f).color = _colMuted;
        }

        private static TextMeshProUGUI MakeTMP(RectTransform parent, TMP_FontAsset font, string text,
            float fontSize, FontStyles style, TextAlignmentOptions align, float height = 16f, string id = null)
        {
            var rt = NewRect(parent, id ?? "Lbl");
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleWidth = 1f;
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.alignment = align;
            tmp.color = Color.white;
            return tmp;
        }

        private static void MakeSmallBtn(RectTransform parent, TMP_FontAsset font, string label, Action onClick)
        {
            var rt = NewRect(parent, "Btn_" + label);
            var img = AddImg(rt, new Color(0.17f, 0.21f, 0.30f));
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var txtRT = NewRect(rt, "Text");
            StretchFull(txtRT);
            var tmp = txtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = label;
            tmp.fontSize = 7f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            tmp.color = Color.white;
        }

        private static void MakeSep(RectTransform parent)
        {
            var rt = NewRect(parent, "Sep");
            rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 1f;
            AddImg(rt, new Color(1f, 1f, 1f, 0.10f));
        }

        private static RectTransform NewRect(Transform parent, string name)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            return go.AddComponent<RectTransform>();
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static Image AddImg(RectTransform rt, Color col)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = col;
            return img;
        }

        private static T FindDeepChild<T>(Transform parent, string childName) where T : Component
        {
            var t = FindDeepTransform(parent, childName);
            return t != null ? t.GetComponent<T>() : null;
        }

        private static Transform FindDeepTransform(Transform parent, string childName)
        {
            foreach (Transform child in parent)
            {
                if (child.name == childName) return child;
                var found = FindDeepTransform(child, childName);
                if (found != null) return found;
            }
            return null;
        }
    }

    // 키워드 태그(TMP <link>) 클릭 감지 전용 — CodexCardView._keywordsTmp가 붙은 GameObject에 얹힌다.
    // [버그 수정, 2026-07-21] 원래 CodexCardView 안의 private 중첩 클래스였는데, 유니티가 private 중첩
    // MonoBehaviour를 프리팹에 제대로 직렬화하지 못해("The referenced script... is missing!") 도감
    // 프리팹 저장이 막히는 문제가 있었다 — 최상위 public 클래스로 분리해 해결. 드래그(ScrollRect)와 같은
    // 오브젝트 계층에 있어도 uGUI가 클릭/드래그를 배타적으로 처리하므로 서로 간섭하지 않는다
    // (NoteRouteGraphView의 ClickToToggle/NodeDragHandle 조합과 같은 이유).
    public class CodexKeywordLinkHandler : MonoBehaviour, IPointerClickHandler
    {
        public TextMeshProUGUI Text;
        public Action<string> OnLinkClicked;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Text == null) return;
            int linkIndex = TMP_TextUtilities.FindIntersectingLink(Text, eventData.position, eventData.pressEventCamera);
            if (linkIndex == -1) return;
            OnLinkClicked?.Invoke(Text.textInfo.linkInfo[linkIndex].GetLinkID());
        }
    }
}
