using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
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

        private TextMeshProUGUI _titleTmp;
        private GameObject _typeBadgeGO;
        private TextMeshProUGUI _typeBadgeTmp;
        private TextMeshProUGUI _timestampTmp;
        private TextMeshProUGUI _contentTmp;
        private TextMeshProUGUI _sourceTmp;
        private TextMeshProUGUI _mapTmp;
        private TextMeshProUGUI _keywordsTmp;
        private GameObject _editRowGO;

        private GameObject _pinRowGO;
        private Button _pinBtn;
        private TextMeshProUGUI _pinBtnLabel;
        private RectTransform _suggestionsRT;
        private GameObject _suggestionsGO;

        private GameObject _commentsSectionGO;
        private RectTransform _commentsRT;
        private readonly List<Coroutine> _typewriterCoroutines = new();

        private TMP_FontAsset _font;
        private CodexEntry _currentEntry;

        private const float TypewriterSecondsPerChar = 0.02f;

        [Header("행 템플릿 (선택 — 비워두면 아래 스타일 값으로 기본 템플릿 생성)")]
        [SerializeField] private GameObject _suggestionRowTemplate;
        [SerializeField] private GameObject _commentRowTemplate;

        // 추천/코멘트 행은 ShowSuggestions()/RefreshComments()마다 UiRowPool에서 재사용된다 —
        // 커스텀 템플릿을 안 쓸 때의 기본 템플릿 스타일로 쓰인다.
        [Header("기본 템플릿 스타일 (프리팹 미지정 시)")]
        [SerializeField] private Color _colMuted = new(0.55f, 0.60f, 0.65f);
        [SerializeField] private Color _colBadge = new(0.25f, 0.42f, 0.72f);
        [SerializeField] private Color _colGreenBtn = new(0.15f, 0.32f, 0.19f);
        [SerializeField] private float _dynamicRowFontSize = 7f;
        [SerializeField] private float _suggestionRowHeight = 12f;
        [SerializeField] private float _commentRowHeight = 28f;

        private UiRowPool _suggestionPool;
        private UiRowPool _commentPool;

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

            var editRowTF = FindDeepTransform(existingRoot, "EditRow");
            _editRowGO = editRowTF?.gameObject;
            if (editRowTF != null)
            {
                FindDeepTransform(editRowTF, "Btn_편집")?.GetComponent<Button>()?.onClick.AddListener(() => OnEditRequested?.Invoke(_currentEntry));
                FindDeepTransform(editRowTF, "Btn_삭제")?.GetComponent<Button>()?.onClick.AddListener(() => OnDeleteRequested?.Invoke(_currentEntry));
            }

            // Bind()는 font 인자를 받지 않는다 — 이미 프리팹에 구워진 라벨의 font를 그대로 재사용한다.
            _font = _titleTmp != null ? _titleTmp.font : null;

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
            _keywordsTmp.text = (e.keywords == null || e.keywords.Length == 0) ? "-" : string.Join(", ", e.keywords);

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

            RefreshComments(e.comments);
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
}
