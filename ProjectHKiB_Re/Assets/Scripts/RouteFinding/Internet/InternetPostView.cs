using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace RouteFinding.Internet
{
    // 인터넷 창 우측의 게시글 본문 뷰 — 제목/작성자/본문 + 첨부 + 획득 단서 + 댓글.
    // 도감 카드(CodexCardView)와 같은 구조·같은 헬퍼(ClueAttachmentService, ClueAttachmentAudioPlayer,
    // UiRowPool)를 쓰지만, 보여주는 대상이 CodexEntry가 아니라 InternetPost라 별도 컴포넌트다.
    //
    // ★ 첨부는 게시글이 아니라 단서에 붙어 있다(기획서 3.3장). 그래서 이 뷰는
    //   "게시글 자신의 장식용 첨부 + 이 글이 준 단서들의 첨부"를 합쳐서 그린다 —
    //   단서를 아직 획득하지 않았다면(잠긴 글을 디버그로 열었다든가) 그 단서의 첨부는 빠진다.
    //
    // 프리팹 재사용 규약은 CodexCardView와 동일하다: Init()은 처음 만들 때만, 이미 만들어진
    // 계층을 재사용할 때는 Bind()가 이름으로 자식을 되찾고 콜백을 다시 연결한다.
    public class InternetPostView : MonoBehaviour
    {
        public event Action<string> OnMapRefClicked; // 맵 첨부의 "지도" 버튼 — 인자는 맵 GUID

        private TextMeshProUGUI _titleTmp;
        private TextMeshProUGUI _metaTmp;
        private TextMeshProUGUI _bodyTmp;

        private GameObject _attachmentsSectionGO;
        private RectTransform _attachmentsRT;
        private GameObject _cluesSectionGO;
        private RectTransform _cluesRT;
        private TextMeshProUGUI _commentsHeaderTmp;
        private GameObject _commentsSectionGO;
        private RectTransform _commentsRT;

        private RectTransform _contentRT;
        private ClueAttachmentAudioPlayer _audio;
        private TMP_FontAsset _font;
        private InternetPost _currentPost;

        // 지금 보여주고 있는 게시글 — 목록의 선택 하이라이트를 다시 그릴 때 패널이 참조한다.
        public string CurrentPostId => _currentPost != null ? _currentPost.id : "";

        [Header("행 템플릿 (선택 — 비워두면 아래 스타일 값으로 기본 템플릿 생성)")]
        [SerializeField] private GameObject _attachmentRowTemplate;
        [SerializeField] private GameObject _clueRowTemplate;
        [SerializeField] private GameObject _commentRowTemplate;

        [Header("기본 템플릿 스타일 (프리팹 미지정 시)")]
        [SerializeField] private Color _colMuted = new(0.55f, 0.60f, 0.65f);
        [SerializeField] private Color _colBadge = new(0.25f, 0.42f, 0.72f);
        [SerializeField] private float _rowFontSize = 7f;
        [SerializeField] private float _attachmentRowHeight = 14f;
        [SerializeField] private float _imagePreviewHeight = 60f;
        [SerializeField] private float _clueRowHeight = 12f;
        [SerializeField] private float _commentRowHeight = 24f;
        [Tooltip("본문 영역의 최소 높이 — 실제 높이는 글자 수에 맞춰 늘어난다")]
        [SerializeField] private float _minBodyHeight = 40f;

        private UiRowPool _attachmentPool;
        private UiRowPool _cluePool;
        private UiRowPool _commentPool;

        private const string PlayLabel = "▶ 재생";
        private const string StopLabel = "■ 정지";

        public void Init(RectTransform parent, TMP_FontAsset font)
        {
            _font = font;
            _contentRT = BuildScrollContent(parent);

            _titleTmp = MakeTMP(_contentRT, font, "", 8f, FontStyles.Bold, TextAlignmentOptions.TopLeft, height: 14f, id: "PostTitle");
            _titleTmp.enableWordWrapping = true;

            _metaTmp = MakeTMP(_contentRT, font, "", 7f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, height: 10f, id: "PostMeta");
            _metaTmp.color = _colMuted;

            MakeSep(_contentRT);

            _bodyTmp = MakeTMP(_contentRT, font, "", 7f, FontStyles.Normal, TextAlignmentOptions.TopLeft, height: _minBodyHeight, id: "PostBody");
            _bodyTmp.enableWordWrapping = true;

            BuildAttachmentsSection(_contentRT, font);
            BuildCluesSection(_contentRT, font);
            BuildCommentsSection(_contentRT, font);
            EnsurePools();

            ShowEmpty();
        }

        public void Bind(RectTransform existingRoot)
        {
            _contentRT = FindDeepTransform(existingRoot, "Content") as RectTransform;
            _titleTmp = FindDeepChild<TextMeshProUGUI>(existingRoot, "PostTitle");
            _metaTmp  = FindDeepChild<TextMeshProUGUI>(existingRoot, "PostMeta");
            _bodyTmp  = FindDeepChild<TextMeshProUGUI>(existingRoot, "PostBody");
            _font = _titleTmp != null ? _titleTmp.font : null;

            var attachmentsTF = FindDeepTransform(existingRoot, "PostAttachments");
            _attachmentsSectionGO = attachmentsTF?.gameObject;
            _attachmentsRT = attachmentsTF as RectTransform;

            var cluesTF = FindDeepTransform(existingRoot, "PostClues");
            _cluesSectionGO = cluesTF?.gameObject;
            _cluesRT = cluesTF as RectTransform;

            var commentsTF = FindDeepTransform(existingRoot, "PostComments");
            _commentsSectionGO = commentsTF?.gameObject;
            _commentsRT = commentsTF as RectTransform;
            _commentsHeaderTmp = commentsTF != null ? FindDeepChild<TextMeshProUGUI>(commentsTF, "PostCommentsHeader") : null;

            EnsurePools();
            ShowEmpty();
        }

        private void EnsurePools()
        {
            _attachmentPool ??= new UiRowPool(_attachmentRowTemplate, BuildAttachmentRowTemplate);
            _cluePool ??= new UiRowPool(_clueRowTemplate, BuildClueRowTemplate);
            _commentPool ??= new UiRowPool(_commentRowTemplate, BuildCommentRowTemplate);
        }

        // ─── 표시 ────────────────────────────────────────────────

        public void ShowEmpty()
        {
            _currentPost = null;
            if (_titleTmp != null) _titleTmp.text = "← 좌측에서 게시글을 선택하세요";
            if (_metaTmp != null) _metaTmp.gameObject.SetActive(false);
            if (_bodyTmp != null) _bodyTmp.text = "";
            RefreshAttachments(null);
            RefreshClues(null);
            RefreshComments(null);
        }

        // acquiredNow: 이번 열람으로 새로 얻은 단서(획득 표시용). 이미 갖고 있던 단서는 여기 없지만
        // "이 글이 준 단서" 목록에는 그대로 나온다 — 다시 읽었을 때 무엇을 준 글인지 알 수 있어야 한다.
        public void ShowPost(InternetPost post, List<ClueData> acquiredNow)
        {
            _currentPost = post;
            if (post == null) { ShowEmpty(); return; }

            _titleTmp.text = post.title;

            bool hasMeta = !string.IsNullOrEmpty(post.author) || !string.IsNullOrEmpty(post.postedAt);
            _metaTmp.gameObject.SetActive(hasMeta);
            if (hasMeta)
            {
                string author = string.IsNullOrEmpty(post.author) ? "익명" : post.author;
                _metaTmp.text = string.IsNullOrEmpty(post.postedAt) ? author : $"{author}  ·  {post.postedAt}";
            }

            _bodyTmp.text = post.body ?? "";
            ResizeBody();

            RefreshAttachments(CollectAttachments(post));
            RefreshClues(BuildClueRows(post, acquiredNow));
            RefreshComments(post.comments);
        }

        // 본문은 길이가 제각각이라 고정 높이로는 잘리거나 빈 공간이 생긴다. 레이아웃을 한 번 강제로
        // 확정시켜 TMP가 실제 폭 기준의 preferredHeight를 계산하게 한 뒤 그 값을 LayoutElement에 넣는다
        // (게시글을 고를 때만 실행되므로 매 프레임 비용이 아니다).
        private void ResizeBody()
        {
            if (_bodyTmp == null || _contentRT == null) return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRT);
            var le = _bodyTmp.GetComponent<LayoutElement>();
            if (le != null) le.preferredHeight = Mathf.Max(_minBodyHeight, _bodyTmp.preferredHeight);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRT);
        }

        // 게시글 장식용 첨부 + 이 글이 준(그리고 이미 획득한) 단서들의 첨부를 순서대로 합친다.
        private static ClueAttachment[] CollectAttachments(InternetPost post)
        {
            var list = new List<ClueAttachment>();
            if (post.attachments != null)
                foreach (var a in post.attachments) if (a != null) list.Add(a);

            var graph = MapGraph.Instance;
            var progress = RouteModule.Instance != null ? RouteModule.Instance.Progress : null;
            if (graph != null && post.grantClueIds != null)
            {
                foreach (var clueId in post.grantClueIds)
                {
                    if (progress != null && !progress.IsClueAcquired(clueId)) continue;
                    var clue = graph.GetClue(clueId);
                    if (clue?.attachments == null) continue;
                    foreach (var a in clue.attachments) if (a != null) list.Add(a);
                }
            }
            return list.ToArray();
        }

        // ─── 첨부물 ───────────────────────────────────────────────

        private void BuildAttachmentsSection(RectTransform parent, TMP_FontAsset font)
        {
            var section = MakeSection(parent, font, "PostAttachments", "첨부", out _attachmentsRT);
            _attachmentsSectionGO = section;
        }

        private void RefreshAttachments(ClueAttachment[] attachments)
        {
            StopAudio();

            bool hasAny = attachments != null && attachments.Length > 0;
            _attachmentsSectionGO?.SetActive(hasAny);
            if (!hasAny || _attachmentsRT == null) { _attachmentPool?.EndPass(); return; }

            float total = SectionHeaderHeight;
            foreach (var a in attachments)
                total += PopulateAttachmentRow(_attachmentPool.Get(_attachmentsRT), a) + 2f;
            _attachmentPool.EndPass();

            _attachmentsSectionGO.GetComponent<LayoutElement>().preferredHeight = total;
        }

        // CodexCardView.PopulateAttachmentRow와 같은 규칙(같은 템플릿 구조·같은 실패 표시)이다.
        private float PopulateAttachmentRow(GameObject row, ClueAttachment a)
        {
            var head = row.transform.Find("Head");
            var iconImg  = head != null ? head.Find("Icon")?.GetComponent<Image>() : null;
            var labelTmp = head != null ? head.Find("Text")?.GetComponent<TextMeshProUGUI>() : null;
            var btnTF    = head != null ? head.Find("BtnAction") : null;
            var btn      = btnTF != null ? btnTF.GetComponent<Button>() : null;
            var btnLabel = btnTF != null ? btnTF.Find("Text")?.GetComponent<TextMeshProUGUI>() : null;
            var previewTF  = row.transform.Find("Preview");
            var previewImg = previewTF != null ? previewTF.GetComponent<Image>() : null;

            btn?.onClick.RemoveAllListeners();

            string label = ClueAttachmentService.ResolveLabel(a);
            Sprite icon = ClueAttachmentService.ResolveIcon(a);
            bool showBtn = false, showPreview = false, missing = false;

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
                    missing = ClueAttachmentService.LoadAudio(a.address) == null;
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
                    missing = ClueAttachmentService.ResolveMapNode(a) == null;
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
            iconRT.gameObject.AddComponent<Image>().preserveAspect = true;

            var textRT = NewRect(head, "Text");
            textRT.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var tmp = textRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.fontSize = _rowFontSize;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.overflowMode = TextOverflowModes.Ellipsis;

            var btnRT = NewRect(head, "BtnAction");
            var btnLe = btnRT.gameObject.AddComponent<LayoutElement>();
            btnLe.preferredWidth = 34f;
            btnLe.flexibleWidth = 0f;
            var btn = btnRT.gameObject.AddComponent<Button>();
            btn.targetGraphic = AddImg(btnRT, _colBadge);
            btn.transition = Selectable.Transition.None;

            var btnTxtRT = NewRect(btnRT, "Text");
            StretchFull(btnTxtRT);
            var btnTmp = btnTxtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) btnTmp.font = _font;
            btnTmp.fontSize = _rowFontSize;
            btnTmp.alignment = TextAlignmentOptions.Center;
            btnTmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            btnTmp.color = Color.white;

            var previewRT = NewRect(rowRT, "Preview");
            var previewLe = previewRT.gameObject.AddComponent<LayoutElement>();
            previewLe.preferredHeight = _imagePreviewHeight;
            previewLe.flexibleWidth = 1f;
            previewRT.gameObject.AddComponent<Image>().preserveAspect = true;

            rowRT.gameObject.SetActive(false);
            return rowRT.gameObject;
        }

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

        // 게시글을 바꿀 때·창을 닫을 때(InternetPanel.CloseWindowContent) 호출.
        public void StopAudio() => _audio?.Stop();

        // ─── 이 글이 준 단서 ──────────────────────────────────────

        private void BuildCluesSection(RectTransform parent, TMP_FontAsset font)
        {
            _cluesSectionGO = MakeSection(parent, font, "PostClues", "이 글에서 얻은 단서", out _cluesRT);
        }

        // 표시 문자열만 만들어 넘긴다 — 단서 자체는 도감이 소유하므로 여기서는 "무엇을 얻었는지"만 알린다.
        private static List<string> BuildClueRows(InternetPost post, List<ClueData> acquiredNow)
        {
            var rows = new List<string>();
            var graph = MapGraph.Instance;
            if (post.grantClueIds == null || graph == null) return rows;

            foreach (var clueId in post.grantClueIds)
            {
                var clue = graph.GetClue(clueId);
                if (clue == null) continue;

                bool isNew = acquiredNow != null && acquiredNow.Contains(clue);
                rows.Add(isNew ? $"{clue.name}  <color=#8FE3A0>(획득!)</color>" : clue.name);
            }
            return rows;
        }

        private void RefreshClues(List<string> rows)
        {
            bool hasAny = rows != null && rows.Count > 0;
            _cluesSectionGO?.SetActive(hasAny);
            if (!hasAny || _cluesRT == null) { _cluePool?.EndPass(); return; }

            foreach (var text in rows)
            {
                var row = _cluePool.Get(_cluesRT);
                var tmp = row.GetComponent<TextMeshProUGUI>();
                if (tmp != null) tmp.text = "· " + text;
            }
            _cluePool.EndPass();

            _cluesSectionGO.GetComponent<LayoutElement>().preferredHeight =
                SectionHeaderHeight + rows.Count * (_clueRowHeight + 2f);
        }

        private GameObject BuildClueRowTemplate() => BuildTextRowTemplate("ClueRow", _clueRowHeight, false);

        // ─── 댓글 ─────────────────────────────────────────────────

        private void BuildCommentsSection(RectTransform parent, TMP_FontAsset font)
        {
            _commentsSectionGO = MakeSection(parent, font, "PostComments", "댓글", out _commentsRT);
            _commentsHeaderTmp = FindDeepChild<TextMeshProUGUI>(_commentsSectionGO.transform, "PostCommentsHeader");
        }

        // 도감 카드와 달리 타이프라이터 연출을 쓰지 않는다 — 게시글은 댓글이 여러 개 붙는 게 기본이라
        // 하나씩 타이핑되면 다 읽는 데만 시간이 걸린다(연출은 4단계에서 필요하면 다시 판단).
        private void RefreshComments(CodexComment[] comments)
        {
            bool hasAny = comments != null && comments.Length > 0;
            _commentsSectionGO?.SetActive(hasAny);
            if (!hasAny || _commentsRT == null) { _commentPool?.EndPass(); return; }

            if (_commentsHeaderTmp != null) _commentsHeaderTmp.text = $"댓글 {comments.Length}";

            foreach (var c in comments)
            {
                var row = _commentPool.Get(_commentsRT);
                var tmp = row.GetComponent<TextMeshProUGUI>();
                if (tmp == null) continue;

                string time = string.IsNullOrEmpty(c.createdAt) ? "" : $"  <color=#7A8290>{c.createdAt}</color>";
                tmp.text = $"└ <b>{c.author}</b>{time}\n   {c.text}";
            }
            _commentPool.EndPass();

            _commentsSectionGO.GetComponent<LayoutElement>().preferredHeight =
                SectionHeaderHeight + comments.Length * (_commentRowHeight + 2f);
        }

        private GameObject BuildCommentRowTemplate() => BuildTextRowTemplate("CommentRow", _commentRowHeight, true);

        // ─── UI 헬퍼 ─────────────────────────────────────────────

        // 구분선(1) + 헤더(12) + spacing 2회(4) — 섹션 높이 계산의 고정 부분.
        private const float SectionHeaderHeight = 17f;

        // "구분선 + 헤더 + (풀로 채우는 행들)" 구조의 섹션 하나. 첨부/단서/댓글이 전부 같은 모양이다.
        private GameObject MakeSection(RectTransform parent, TMP_FontAsset font, string id, string title, out RectTransform sectionRT)
        {
            var section = NewRect(parent, id);
            sectionRT = section;
            var le = section.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = SectionHeaderHeight;
            le.flexibleWidth = 1f;

            var vlg = section.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            MakeSep(section);
            var header = MakeTMP(section, font, title, 7f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, height: 12f, id: id + "Header");
            header.color = _colMuted;

            section.gameObject.SetActive(false);
            return section.gameObject;
        }

        private GameObject BuildTextRowTemplate(string name, float height, bool wrap)
        {
            var rowRT = NewRect(null, name);
            var le = rowRT.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleWidth = 1f;

            var tmp = rowRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.fontSize = _rowFontSize;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.enableWordWrapping = wrap;
            if (!wrap) tmp.overflowMode = TextOverflowModes.Ellipsis;

            rowRT.gameObject.SetActive(false);
            return rowRT.gameObject;
        }

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
