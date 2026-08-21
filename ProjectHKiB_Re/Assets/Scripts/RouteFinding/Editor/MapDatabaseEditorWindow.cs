using System;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace RouteFinding.Editor
{
    // Unity 메뉴 RouteFinding > 맵 DB 편집기 로 열 수 있는 개발자 창.
    // map_database.json 과 clues.json 을 GUI로 열람·편집·저장한다.
    // Ctrl+S 로 즉시 저장. 미저장 상태는 상단에 표시됨.
    public class MapDatabaseEditorWindow : EditorWindow
    {
        private enum Tab { Maps, Connections, Clues, Internet }
        private Tab _tab = Tab.Maps;

        // ─── 데이터 ──────────────────────────────────────────────
        private MapDatabase      _db;
        private ClueDatabase     _clueDb;
        private InternetDatabase _netDb;
        private string _dbPath   = "";
        private string _cluePath = "";
        private string _netPath  = "";
        private bool   _dirty;

        // ─── UI 상태 ─────────────────────────────────────────────
        private int     _selMap  = -1;
        private int     _selConn = -1;
        private int     _selClue = -1;
        private int     _selSite = -1;
        private Vector2 _listScroll;
        private Vector2 _detailScroll;

        // 폴드아웃
        private bool _foldEvents       = true;
        private bool _foldClueIds      = true;
        private bool _foldWavePaths    = true;
        private bool _foldEnemies      = true;
        private bool _foldRequiredGears = true;
        private bool _foldKeywords = true;
        private bool _foldComments = true;
        private bool _foldAttachments = true;

        // 인터넷 탭 — 사이트 하나에 게시글이 여러 개 들어가므로, 게시글은 각각 펼침 상태를 기억한다
        // (그 안의 하위 섹션 폴드아웃은 게시글별로 나누지 않고 전체 공용으로 둔다 — 상태를 게시글마다
        // 따로 들고 있을 만큼 얻는 게 없다).
        private bool _foldSiteUnlock = true;
        private bool _foldPostGrants = true;
        private bool _foldPostUnlock = true;
        private bool _foldPostAttachments;
        private bool _foldPostComments = true;
        private readonly System.Collections.Generic.HashSet<string> _expandedPostIds = new();

        // ─── 색상 ────────────────────────────────────────────────
        private static readonly Color ColDirty    = new(1.00f, 0.85f, 0.35f);
        private static readonly Color ColHeader   = new(0.18f, 0.22f, 0.30f);
        private static readonly Color ColSelected = new(0.25f, 0.43f, 0.78f, 0.55f);
        private static readonly Color ColSep      = new(0.45f, 0.45f, 0.45f, 0.35f);

        // ─── 진입점 ──────────────────────────────────────────────

        [MenuItem("RouteFinding/맵 DB 편집기")]
        public static void Open()
        {
            var w = GetWindow<MapDatabaseEditorWindow>("맵 DB 편집기");
            w.minSize = new Vector2(700f, 480f);
            w.Show();
        }

        private void OnEnable() => LoadAll();

        // ─── 파일 IO ─────────────────────────────────────────────

        private void LoadAll()
        {
            _db     = new MapDatabase  { maps = Array.Empty<MapNodeData>(), connections = Array.Empty<MapConnectionData>() };
            _clueDb = new ClueDatabase { clues = Array.Empty<ClueData>() };
            _netDb  = new InternetDatabase { sites = Array.Empty<InternetSite>() };

            _dbPath   = FindAbsPath("map_database");
            _cluePath = FindAbsPath("clues");
            _netPath  = FindAbsPath("internet");

            if (File.Exists(_dbPath))
            {
                _db = JsonUtility.FromJson<MapDatabase>(File.ReadAllText(_dbPath));
                _db.maps        = _db.maps        ?? Array.Empty<MapNodeData>();
                _db.connections = _db.connections ?? Array.Empty<MapConnectionData>();
                foreach (var m in _db.maps)
                {
                    m.iconPath = m.iconPath ?? "";
                    m.events  = m.events  ?? Array.Empty<MapEventFlag>();
                    m.clueIds = m.clueIds ?? Array.Empty<string>();
                    m.wavePaths     = m.wavePaths     ?? Array.Empty<string>();
                    m.enemyGroups   = m.enemyGroups   ?? Array.Empty<EnemyGroupEntry>();
                    m.requiredGears = m.requiredGears ?? Array.Empty<EmotionColor>();
                }
            }
            if (File.Exists(_cluePath))
            {
                _clueDb = JsonUtility.FromJson<ClueDatabase>(File.ReadAllText(_cluePath));
                _clueDb.clues = _clueDb.clues ?? Array.Empty<ClueData>();
                foreach (var cl in _clueDb.clues)
                {
                    cl.requiredEventKey = cl.requiredEventKey ?? "";
                    cl.timestamp     = cl.timestamp     ?? "";
                    cl.content       = cl.content       ?? "";
                    cl.source        = cl.source        ?? "";
                    cl.codexMapGuid  = cl.codexMapGuid  ?? "";
                    cl.keywords      = cl.keywords      ?? Array.Empty<string>();
                    cl.comments      = cl.comments      ?? Array.Empty<CodexComment>();
                    cl.attachments   = cl.attachments   ?? Array.Empty<ClueAttachment>();
                    foreach (var at in cl.attachments)
                    {
                        at.label        = at.label        ?? "";
                        at.resourcePath = at.resourcePath ?? "";
                        at.mapGuid      = at.mapGuid      ?? "";
                    }
                }
            }

            if (File.Exists(_netPath))
            {
                _netDb = JsonUtility.FromJson<InternetDatabase>(File.ReadAllText(_netPath));
                _netDb.sites = _netDb.sites ?? Array.Empty<InternetSite>();
                foreach (var site in _netDb.sites)
                {
                    site.iconPath = site.iconPath ?? "";
                    site.unlock   = site.unlock ?? new InternetUnlockCondition();
                    NormalizeUnlock(site.unlock);
                    site.posts    = site.posts ?? Array.Empty<InternetPost>();
                    foreach (var post in site.posts)
                    {
                        post.title        = post.title ?? "";
                        post.author       = post.author ?? "";
                        post.postedAt     = post.postedAt ?? "";
                        post.body         = post.body ?? "";
                        post.grantClueIds = post.grantClueIds ?? Array.Empty<string>();
                        post.unlock       = post.unlock ?? new InternetUnlockCondition();
                        NormalizeUnlock(post.unlock);
                        post.attachments  = post.attachments ?? Array.Empty<ClueAttachment>();
                        foreach (var at in post.attachments)
                        {
                            at.label        = at.label        ?? "";
                            at.resourcePath = at.resourcePath ?? "";
                            at.mapGuid      = at.mapGuid      ?? "";
                        }
                        post.comments = post.comments ?? Array.Empty<CodexComment>();
                    }
                }
            }

            _dirty = false;
            Repaint();
        }

        private static void NormalizeUnlock(InternetUnlockCondition u)
        {
            u.requiredClueIds   = u.requiredClueIds   ?? Array.Empty<string>();
            u.requiredEventKeys = u.requiredEventKeys ?? Array.Empty<string>();
        }

        private void SaveAll()
        {
            if (!string.IsNullOrEmpty(_dbPath))
                File.WriteAllText(_dbPath, JsonUtility.ToJson(_db, prettyPrint: true));
            if (!string.IsNullOrEmpty(_cluePath))
                File.WriteAllText(_cluePath, JsonUtility.ToJson(_clueDb, prettyPrint: true));
            if (!string.IsNullOrEmpty(_netPath))
                File.WriteAllText(_netPath, JsonUtility.ToJson(_netDb, prettyPrint: true));
            AssetDatabase.Refresh();
            _dirty = false;
        }

        private static string FindAbsPath(string filename)
        {
            foreach (var g in AssetDatabase.FindAssets(filename + " t:TextAsset"))
            {
                var ap = AssetDatabase.GUIDToAssetPath(g);
                if (ap.EndsWith(filename + ".json", StringComparison.OrdinalIgnoreCase))
                    return Path.GetFullPath(
                        Path.Combine(Application.dataPath, "..", ap));
            }
            return "";
        }

        // ─── OnGUI 최상위 ────────────────────────────────────────

        private void OnGUI()
        {
            // Ctrl+S 저장
            if (Event.current.type == EventType.KeyDown &&
                Event.current.control && Event.current.keyCode == KeyCode.S)
            {
                SaveAll();
                Event.current.Use();
            }

            DrawToolbar();

            EditorGUILayout.BeginHorizontal();

            // 목록 패널 (좌 220px)
            GUILayout.BeginVertical(GUILayout.Width(220f));
            DrawList();
            GUILayout.EndVertical();

            // 구분선
            var sepR = GUILayoutUtility.GetRect(2f, 0f, GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(sepR, ColSep);

            // 상세 편집 패널
            GUILayout.BeginVertical();
            DrawDetail();
            GUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        // ─── 툴바 ────────────────────────────────────────────────

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Toggle(_tab == Tab.Maps,        "맵 노드",  EditorStyles.toolbarButton, GUILayout.Width(72f))) _tab = Tab.Maps;
            if (GUILayout.Toggle(_tab == Tab.Connections, "연결",     EditorStyles.toolbarButton, GUILayout.Width(52f))) _tab = Tab.Connections;
            if (GUILayout.Toggle(_tab == Tab.Clues,       "단서",     EditorStyles.toolbarButton, GUILayout.Width(52f))) _tab = Tab.Clues;
            if (GUILayout.Toggle(_tab == Tab.Internet,    "인터넷",   EditorStyles.toolbarButton, GUILayout.Width(60f))) _tab = Tab.Internet;

            GUILayout.FlexibleSpace();

            if (_dirty)
            {
                var c = GUI.color; GUI.color = ColDirty;
                GUILayout.Label("● 미저장", EditorStyles.toolbarButton);
                GUI.color = c;
            }

            if (GUILayout.Button("불러오기", EditorStyles.toolbarButton, GUILayout.Width(64f)))
            {
                if (!_dirty || EditorUtility.DisplayDialog(
                        "확인", "저장하지 않은 변경이 있습니다. 다시 불러오시겠습니까?", "불러오기", "취소"))
                    LoadAll();
            }

            var bg = GUI.backgroundColor;
            GUI.backgroundColor = _dirty ? ColDirty : bg;
            if (GUILayout.Button("저장  Ctrl+S", EditorStyles.toolbarButton, GUILayout.Width(84f)))
                SaveAll();
            GUI.backgroundColor = bg;

            EditorGUILayout.EndHorizontal();

            // 파일 경로 한 줄
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label(
                "DB: "   + (string.IsNullOrEmpty(_dbPath)   ? "❌ map_database.json 없음" : ShortPath(_dbPath)),
                EditorStyles.miniLabel);
            GUILayout.Label(
                "단서: " + (string.IsNullOrEmpty(_cluePath) ? "❌ clues.json 없음"        : ShortPath(_cluePath)),
                EditorStyles.miniLabel);
            GUILayout.Label(
                "인터넷: " + (string.IsNullOrEmpty(_netPath) ? "❌ internet.json 없음"    : ShortPath(_netPath)),
                EditorStyles.miniLabel);
            if (GUILayout.Button("탐색기", EditorStyles.miniButton, GUILayout.Width(50f)))
                EditorUtility.RevealInFinder(string.IsNullOrEmpty(_dbPath) ? Application.dataPath : _dbPath);
            EditorGUILayout.EndHorizontal();
        }

        // ─── 목록 패널 ───────────────────────────────────────────

        private void DrawList()
        {
            // 탭 헤더 + 추가 버튼
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            string hdr = _tab == Tab.Maps        ? $"맵 노드  ({_db.maps.Length})"        :
                         _tab == Tab.Connections  ? $"연결  ({_db.connections.Length})"    :
                         _tab == Tab.Clues        ? $"단서  ({_clueDb.clues.Length})"      :
                                                    $"사이트  ({_netDb.sites.Length})";
            GUILayout.Label(hdr, EditorStyles.toolbarButton, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("+", EditorStyles.toolbarButton, GUILayout.Width(26f)))
            {
                AddItem();
                _dirty = true;
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

            switch (_tab)
            {
                case Tab.Maps:
                    for (int i = 0; i < _db.maps.Length; i++)
                        DrawListRow(i, _db.maps[i].nodeName, ref _selMap);
                    break;
                case Tab.Connections:
                    for (int i = 0; i < _db.connections.Length; i++)
                    {
                        var c = _db.connections[i];
                        DrawListRow(i, $"{NodeName(c.fromGuid)} → {NodeName(c.toGuid)}", ref _selConn);
                    }
                    break;
                case Tab.Clues:
                    for (int i = 0; i < _clueDb.clues.Length; i++)
                        DrawListRow(i, _clueDb.clues[i].name, ref _selClue);
                    break;
                case Tab.Internet:
                    for (int i = 0; i < _netDb.sites.Length; i++)
                    {
                        var s = _netDb.sites[i];
                        int postCount = s.posts != null ? s.posts.Length : 0;
                        DrawListRow(i, $"{s.name}  ({postCount})", ref _selSite);
                    }
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawListRow(int idx, string label, ref int sel)
        {
            var row = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            if (idx == sel) EditorGUI.DrawRect(row, ColSelected);

            // 항목 선택
            if (GUI.Button(new Rect(row.x, row.y, row.width - 24f, row.height),
                    "  " + label,
                    idx == sel ? EditorStyles.whiteLabel : EditorStyles.label))
                sel = idx;

            // 삭제 버튼
            if (GUI.Button(new Rect(row.xMax - 22f, row.y + 1f, 20f, row.height - 2f), "×"))
            {
                RemoveItem(idx);
                if (sel >= idx) sel = Mathf.Max(-1, sel - 1);
                _dirty = true;
                GUIUtility.ExitGUI();
            }
        }

        // ─── 상세 편집 패널 ───────────────────────────────────────

        private void DrawDetail()
        {
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll, GUILayout.ExpandWidth(true));

            switch (_tab)
            {
                case Tab.Maps:
                    if (_selMap >= 0 && _selMap < _db.maps.Length)
                        DrawMapDetail(_db.maps[_selMap]);
                    else
                        EditorGUILayout.HelpBox("← 목록에서 맵을 선택하거나 [+] 로 추가하세요.", MessageType.Info);
                    break;
                case Tab.Connections:
                    if (_selConn >= 0 && _selConn < _db.connections.Length)
                        DrawConnDetail(_db.connections[_selConn]);
                    else
                        EditorGUILayout.HelpBox("← 목록에서 연결을 선택하거나 [+] 로 추가하세요.", MessageType.Info);
                    break;
                case Tab.Clues:
                    if (_selClue >= 0 && _selClue < _clueDb.clues.Length)
                        DrawClueDetail(_clueDb.clues[_selClue]);
                    else
                        EditorGUILayout.HelpBox("← 목록에서 단서를 선택하거나 [+] 로 추가하세요.", MessageType.Info);
                    break;
                case Tab.Internet:
                    if (string.IsNullOrEmpty(_netPath))
                        EditorGUILayout.HelpBox(
                            "internet.json 을 찾을 수 없습니다. clues.json 과 같은 Resources 폴더에 만들어 주세요\n" +
                            "(내용은 {\"sites\":[]} 한 줄이면 충분합니다).", MessageType.Warning);
                    else if (_selSite >= 0 && _selSite < _netDb.sites.Length)
                        DrawSiteDetail(_netDb.sites[_selSite]);
                    else
                        EditorGUILayout.HelpBox("← 목록에서 사이트를 선택하거나 [+] 로 추가하세요.", MessageType.Info);
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        // ─── 맵 노드 편집 ─────────────────────────────────────────

        private void DrawMapDetail(MapNodeData n)
        {
            SectionHeader("맵 노드 편집");
            EditorGUI.BeginChangeCheck();

            // 기본 정보
            ReadonlyField("GUID", n.guid);
            n.nodeName    = TF("이름",    n.nodeName);
            n.sceneName   = TF("씬 이름", n.sceneName);
            n.iconPath    = ResourcePathField<Sprite>("아이콘 (선택)", n.iconPath);
            n.description = TA("설명",    n.description);

            EditorGUILayout.Space(4f);
            n.graphPosition  = EditorGUILayout.Vector2Field("그래프 좌표", n.graphPosition);
            n.isStartNode    = EditorGUILayout.Toggle("시작 지점 (집)",  n.isStartNode);
            n.startsWithClue = EditorGUILayout.Toggle("초기 단서 보유", n.startsWithClue);

            // 이벤트 플래그 배열
            EditorGUILayout.Space(6f);
            _foldEvents = EditorGUILayout.Foldout(_foldEvents,
                $"이벤트 플래그  ({n.events.Length}개)", true, EditorStyles.foldoutHeader);
            if (_foldEvents)
            {
                EditorGUI.indentLevel++;
                int removeEvent = -1;
                for (int i = 0; i < n.events.Length; i++)
                {
                    var ev = n.events[i];
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label($"[{i}]", GUILayout.Width(28f));
                    ev.key   = EditorGUILayout.TextField(ev.key, GUILayout.ExpandWidth(true));
                    ev.value = EditorGUILayout.Toggle(ev.value, GUILayout.Width(20f));
                    if (GUILayout.Button("−", GUILayout.Width(22f))) removeEvent = i;
                    EditorGUILayout.EndHorizontal();
                }
                if (removeEvent >= 0) ArrayUtility.RemoveAt(ref n.events, removeEvent);
                if (GUILayout.Button("+ 이벤트 플래그 추가", GUILayout.ExpandWidth(false)))
                    ArrayUtility.Add(ref n.events, new MapEventFlag { key = "event_key", value = false });
                EditorGUI.indentLevel--;
            }

            // 단서 ID 배열
            EditorGUILayout.Space(4f);
            _foldClueIds = EditorGUILayout.Foldout(_foldClueIds,
                $"획득 가능 단서 ID  ({n.clueIds.Length}개)", true, EditorStyles.foldoutHeader);
            if (_foldClueIds)
            {
                EditorGUI.indentLevel++;
                int removeClue = -1;
                for (int i = 0; i < n.clueIds.Length; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label($"[{i}]", GUILayout.Width(28f));
                    n.clueIds[i] = EditorGUILayout.TextField(n.clueIds[i]);
                    if (GUILayout.Button("−", GUILayout.Width(22f))) removeClue = i;
                    EditorGUILayout.EndHorizontal();
                }
                if (removeClue >= 0) ArrayUtility.RemoveAt(ref n.clueIds, removeClue);
                if (GUILayout.Button("+ 단서 ID 추가", GUILayout.ExpandWidth(false)))
                    ArrayUtility.Add(ref n.clueIds, "");
                EditorGUI.indentLevel--;
            }

            // 2026-07-14 — 전투 데이터(웨이브 경로/적 구성/필수 장비)가 연결에서 맵으로 이동.
            // 웨이브 경로 배열
            EditorGUILayout.Space(6f);
            _foldWavePaths = EditorGUILayout.Foldout(_foldWavePaths,
                $"웨이브 경로  ({n.wavePaths.Length}개)", true, EditorStyles.foldoutHeader);
            if (_foldWavePaths)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("Resources/ 이후 상대 경로  예) RouteFinding/Waves/wave_01", MessageType.None);
                int removeWave = -1;
                for (int i = 0; i < n.wavePaths.Length; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label($"[{i}]", GUILayout.Width(28f));
                    n.wavePaths[i] = EditorGUILayout.TextField(n.wavePaths[i]);
                    if (GUILayout.Button("−", GUILayout.Width(22f))) removeWave = i;
                    EditorGUILayout.EndHorizontal();
                }
                if (removeWave >= 0) ArrayUtility.RemoveAt(ref n.wavePaths, removeWave);
                if (GUILayout.Button("+ 웨이브 경로 추가", GUILayout.ExpandWidth(false)))
                    ArrayUtility.Add(ref n.wavePaths, "");
                EditorGUI.indentLevel--;
            }

            // 적 구성 배열
            EditorGUILayout.Space(4f);
            _foldEnemies = EditorGUILayout.Foldout(_foldEnemies,
                $"적 구성  ({n.enemyGroups.Length}그룹)", true, EditorStyles.foldoutHeader);
            if (_foldEnemies)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("감정 색상",    GUILayout.Width(140f));
                EditorGUILayout.LabelField("규모",          GUILayout.Width(80f));
                EditorGUILayout.LabelField("수량",          GUILayout.Width(50f));
                EditorGUILayout.EndHorizontal();
                int removeEnemy = -1;
                for (int i = 0; i < n.enemyGroups.Length; i++)
                {
                    var eg = n.enemyGroups[i];
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label($"[{i}]", GUILayout.Width(28f));
                    eg.emotionType = (EmotionColor)EditorGUILayout.EnumPopup(eg.emotionType, GUILayout.Width(140f));
                    eg.scale       = (EnemyScale)EditorGUILayout.EnumPopup(eg.scale,         GUILayout.Width(80f));
                    eg.count       = EditorGUILayout.IntField(eg.count,                        GUILayout.Width(50f));
                    if (GUILayout.Button("−", GUILayout.Width(22f))) removeEnemy = i;
                    EditorGUILayout.EndHorizontal();
                }
                if (removeEnemy >= 0) ArrayUtility.RemoveAt(ref n.enemyGroups, removeEnemy);
                if (GUILayout.Button("+ 적 그룹 추가", GUILayout.ExpandWidth(false)))
                    ArrayUtility.Add(ref n.enemyGroups,
                        new EnemyGroupEntry { emotionType = EmotionColor.SadnessBlue, scale = EnemyScale.Small, count = 1 });
                EditorGUI.indentLevel--;
            }

            // 필수 장비 배열 (비어있으면 진입 제한 없음)
            EditorGUILayout.Space(4f);
            _foldRequiredGears = EditorGUILayout.Foldout(_foldRequiredGears,
                $"필수 장비  ({n.requiredGears.Length}개)", true, EditorStyles.foldoutHeader);
            if (_foldRequiredGears)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("비어있으면 제한 없음. 모두 충족해야 이 맵에 진입 가능 (그룹 단위 비교).", MessageType.None);
                int removeGear = -1;
                for (int i = 0; i < n.requiredGears.Length; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label($"[{i}]", GUILayout.Width(28f));
                    n.requiredGears[i] = (EmotionColor)EditorGUILayout.EnumPopup(n.requiredGears[i], GUILayout.Width(140f));
                    if (GUILayout.Button("−", GUILayout.Width(22f))) removeGear = i;
                    EditorGUILayout.EndHorizontal();
                }
                if (removeGear >= 0) ArrayUtility.RemoveAt(ref n.requiredGears, removeGear);
                if (GUILayout.Button("+ 필수 장비 추가", GUILayout.ExpandWidth(false)))
                    ArrayUtility.Add(ref n.requiredGears, EmotionColor.SadnessBlue);
                EditorGUI.indentLevel--;
            }

            if (EditorGUI.EndChangeCheck()) _dirty = true;
        }

        // ─── 연결 편집 ────────────────────────────────────────────

        private void DrawConnDetail(MapConnectionData c)
        {
            SectionHeader("연결 편집");
            EditorGUI.BeginChangeCheck();

            ReadonlyField("GUID", c.guid);
            c.fromGuid       = NodeGuidPopup("출발 맵", c.fromGuid);
            c.toGuid         = NodeGuidPopup("도착 맵",  c.toGuid);
            c.startsWithClue = EditorGUILayout.Toggle("초기 단서 보유", c.startsWithClue);
            EditorGUILayout.HelpBox(
                "전투 관련 데이터(웨이브 경로/적 구성/필수 장비)는 2026-07-14부로 맵 쪽으로 이동했습니다 — " +
                "도착 맵(위 드롭다운) 편집 화면에서 설정하세요.", MessageType.Info);

            if (EditorGUI.EndChangeCheck()) _dirty = true;
        }

        // ─── 단서 편집 ────────────────────────────────────────────

        private void DrawClueDetail(ClueData cl)
        {
            SectionHeader("단서 편집");
            EditorGUI.BeginChangeCheck();

            cl.id          = TF("ID",   cl.id);
            cl.name        = TF("이름", cl.name);
            cl.description = TA("설명", cl.description);

            EditorGUILayout.Space(4f);
            cl.targetMapGuid        = NodeGuidPopup("대상 맵",  cl.targetMapGuid,        allowEmpty: true);
            cl.targetConnectionGuid = ConnGuidPopup("대상 연결", cl.targetConnectionGuid, allowEmpty: true);

            EditorGUILayout.Space(4f);
            cl.requiredEventKey = TF("필요 이벤트 키", cl.requiredEventKey);
            EditorGUILayout.HelpBox(
                "출발 맵(이 단서가 '획득 가능 단서 ID'에 등록된 맵)을 방문해야 획득 가능.\n" +
                "비어있으면 방문만으로 획득. 값이 있으면 해당 맵에서 " +
                "RouteModule.Instance.Progress.SetEventFlag(맵GUID, 이 키)가 호출된 후에만 획득.",
                MessageType.None);

            // ─── 도감(Codex) 전용 필드 ──────────────────────────
            EditorGUILayout.Space(8f);
            SectionHeader("도감 카드 정보");

            cl.type      = (ClueType)EditorGUILayout.EnumPopup("타입", cl.type);
            cl.timestamp = TF("시간 (표시용, 비우면 숨김)", cl.timestamp);
            cl.content   = TA("도감 본문", cl.content);
            cl.source    = TF("출처", cl.source);
            cl.codexMapGuid = NodeGuidPopup("도감 분류 맵 (없으면 '기타')", cl.codexMapGuid, allowEmpty: true);

            EditorGUILayout.Space(4f);
            _foldKeywords = EditorGUILayout.Foldout(_foldKeywords,
                $"키워드  ({cl.keywords.Length}개)", true, EditorStyles.foldoutHeader);
            if (_foldKeywords)
            {
                EditorGUI.indentLevel++;
                int removeKw = -1;
                for (int i = 0; i < cl.keywords.Length; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label($"[{i}]", GUILayout.Width(28f));
                    cl.keywords[i] = EditorGUILayout.TextField(cl.keywords[i]);
                    if (GUILayout.Button("−", GUILayout.Width(22f))) removeKw = i;
                    EditorGUILayout.EndHorizontal();
                }
                if (removeKw >= 0) ArrayUtility.RemoveAt(ref cl.keywords, removeKw);
                if (GUILayout.Button("+ 키워드 추가", GUILayout.ExpandWidth(false)))
                    ArrayUtility.Add(ref cl.keywords, "");
                EditorGUI.indentLevel--;
            }

            // 첨부물(2026-08-11) — 사진/소리/맵 참조. 도감 카드와 인터넷 게시글 본문에 표시된다.
            EditorGUILayout.Space(4f);
            DrawAttachmentList(ref cl.attachments, ref _foldAttachments, "첨부물 (사진/소리/맵)",
                "사진/소리는 Resources 폴더 안의 에셋만 쓸 수 있습니다 — 오브젝트 칸에 끌어다 놓으면 경로가 자동으로 채워집니다.\n" +
                "맵 첨부는 그 맵의 아이콘과 이름을 보여주고, 누르면 지도에서 해당 맵으로 이동합니다 (아이콘은 맵 노드 편집 화면에서 지정).");

            // 4단계(2026-07-14) — NPC/시스템 코멘트. 플레이어가 입력하는 게 아니라 콘텐츠 작업자가
            // 여기서 직접 채워 넣는 대사 데이터다(Clue_System.md 1-4장 확정 사항).
            EditorGUILayout.Space(4f);
            DrawCommentList(ref cl.comments, ref _foldComments, "코멘트 (NPC/시스템)",
                "플레이어 입력이 아니라 NPC/시스템이 다는 코멘트 — 카드에서 타이프라이터 연출로 출력됨.");

            if (EditorGUI.EndChangeCheck()) _dirty = true;
        }

        // 단서 카드와 인터넷 게시글이 같은 편집 UI를 쓴다(둘 다 ClueAttachment[] / CodexComment[]).
        private void DrawAttachmentList(ref ClueAttachment[] arr, ref bool fold, string title, string help)
        {
            fold = EditorGUILayout.Foldout(fold, $"{title}  ({arr.Length}개)", true, EditorStyles.foldoutHeader);
            if (!fold) return;

            EditorGUI.indentLevel++;
            if (!string.IsNullOrEmpty(help)) EditorGUILayout.HelpBox(help, MessageType.None);

            int removeAt = -1;
            for (int i = 0; i < arr.Length; i++)
            {
                var at = arr[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"[{i}]", GUILayout.Width(28f));
                at.kind = (ClueAttachmentKind)EditorGUILayout.EnumPopup(at.kind, GUILayout.Width(90f));
                GUILayout.Label(ClueAttachmentConfig.GetDisplayName(at.kind), EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("−", GUILayout.Width(22f))) removeAt = i;
                EditorGUILayout.EndHorizontal();

                at.label = TF("표시 이름 (비우면 자동)", at.label);

                switch (at.kind)
                {
                    case ClueAttachmentKind.Image:
                        at.resourcePath = ResourcePathField<Sprite>("이미지", at.resourcePath);
                        break;
                    case ClueAttachmentKind.Audio:
                        at.resourcePath = ResourcePathField<AudioClip>("오디오", at.resourcePath);
                        break;
                    case ClueAttachmentKind.MapRef:
                        at.mapGuid = NodeGuidPopup("맵", at.mapGuid, allowEmpty: true);
                        break;
                }

                EditorGUILayout.EndVertical();
            }
            if (removeAt >= 0) ArrayUtility.RemoveAt(ref arr, removeAt);
            if (GUILayout.Button("+ 첨부물 추가", GUILayout.ExpandWidth(false)))
                ArrayUtility.Add(ref arr,
                    new ClueAttachment { kind = ClueAttachmentKind.Image, label = "", resourcePath = "", mapGuid = "" });
            EditorGUI.indentLevel--;
        }

        private void DrawCommentList(ref CodexComment[] arr, ref bool fold, string title, string help)
        {
            fold = EditorGUILayout.Foldout(fold, $"{title}  ({arr.Length}개)", true, EditorStyles.foldoutHeader);
            if (!fold) return;

            EditorGUI.indentLevel++;
            if (!string.IsNullOrEmpty(help)) EditorGUILayout.HelpBox(help, MessageType.None);

            int removeAt = -1;
            for (int i = 0; i < arr.Length; i++)
            {
                var cm = arr[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"[{i}]", GUILayout.Width(28f));
                cm.author = EditorGUILayout.TextField("작성자", cm.author ?? "");
                if (GUILayout.Button("−", GUILayout.Width(22f))) removeAt = i;
                EditorGUILayout.EndHorizontal();
                cm.createdAt = EditorGUILayout.TextField("시간 (선택, 비우면 숨김)", cm.createdAt ?? "");
                EditorGUILayout.LabelField("내용");
                cm.text = EditorGUILayout.TextArea(cm.text ?? "", GUILayout.MinHeight(36f));
                EditorGUILayout.EndVertical();
            }
            if (removeAt >= 0) ArrayUtility.RemoveAt(ref arr, removeAt);
            if (GUILayout.Button("+ 코멘트 추가", GUILayout.ExpandWidth(false)))
                ArrayUtility.Add(ref arr, new CodexComment { author = "", text = "", createdAt = "" });
            EditorGUI.indentLevel--;
        }

        // ─── 인터넷 편집 ──────────────────────────────────────────
        // 사이트 하나를 고르면 그 안의 게시글까지 이 화면에서 전부 편집한다(사이트 → 게시글 2단 구조라
        // 목록 패널을 2단으로 만드는 대신 상세 패널 안에서 게시글을 접었다 펴는 방식으로 처리).

        private void DrawSiteDetail(InternetSite site)
        {
            SectionHeader("사이트 편집");
            EditorGUI.BeginChangeCheck();

            site.id   = TF("ID", site.id);
            site.name = TF("이름", site.name);
            site.iconPath = ResourcePathField<Sprite>("아이콘 (선택)", site.iconPath);

            EditorGUILayout.Space(4f);
            DrawUnlock(site.unlock, ref _foldSiteUnlock, "사이트 잠금 조건",
                "전부 비우면 처음부터 보입니다. 조건이 있으면 전부 만족해야 목록에 나오고, 그 전에는 '??? (잠김)'으로만 표시됩니다.");

            EditorGUILayout.Space(8f);
            SectionHeader($"게시글  ({site.posts.Length}개)");

            int removePost = -1;
            for (int i = 0; i < site.posts.Length; i++)
            {
                var post = site.posts[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                bool expanded = _expandedPostIds.Contains(post.id);
                if (GUILayout.Button(expanded ? "▾" : "▸", EditorStyles.miniButton, GUILayout.Width(22f)))
                {
                    if (!_expandedPostIds.Remove(post.id)) _expandedPostIds.Add(post.id);
                }
                GUILayout.Label($"[{i}] {post.title}", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("−", GUILayout.Width(22f))) removePost = i;
                EditorGUILayout.EndHorizontal();

                if (expanded) DrawPostBody(post);

                EditorGUILayout.EndVertical();
            }
            if (removePost >= 0) ArrayUtility.RemoveAt(ref site.posts, removePost);

            if (GUILayout.Button("+ 게시글 추가", GUILayout.ExpandWidth(false)))
            {
                var post = new InternetPost
                {
                    id           = "post-" + NewGuid(),
                    title        = "새 게시글",
                    author       = "익명",
                    postedAt     = "",
                    body         = "",
                    grantClueIds = Array.Empty<string>(),
                    unlock       = new InternetUnlockCondition
                    {
                        requiredClueIds   = Array.Empty<string>(),
                        requiredEventKeys = Array.Empty<string>(),
                    },
                    attachments  = Array.Empty<ClueAttachment>(),
                    comments     = Array.Empty<CodexComment>(),
                };
                ArrayUtility.Add(ref site.posts, post);
                _expandedPostIds.Add(post.id);
            }

            if (EditorGUI.EndChangeCheck()) _dirty = true;
        }

        private void DrawPostBody(InternetPost post)
        {
            EditorGUI.indentLevel++;

            post.id       = TF("ID (세이브의 읽음 표시 키)", post.id);
            post.title    = TF("제목", post.title);
            post.author   = TF("작성자", post.author);
            post.postedAt = TF("작성 시각 (표시용 텍스트)", post.postedAt);
            post.body     = TA("본문", post.body);

            EditorGUILayout.Space(4f);
            _foldPostGrants = EditorGUILayout.Foldout(_foldPostGrants,
                $"열람 시 획득할 단서  ({post.grantClueIds.Length}개)", true, EditorStyles.foldoutHeader);
            if (_foldPostGrants)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(
                    "게시글을 열면 여기 적힌 단서가 즉시 획득됩니다(도감 등록·지도 공개까지 자동).\n" +
                    "인터넷 전용 단서는 어느 맵의 '획득 가능 단서 ID'에도 넣지 마세요 — 넣으면 도감에 '??? (미발견)' 빈칸이 생깁니다.",
                    MessageType.None);

                int removeGrant = -1;
                for (int i = 0; i < post.grantClueIds.Length; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    post.grantClueIds[i] = ClueIdPopup($"[{i}]", post.grantClueIds[i]);
                    if (GUILayout.Button("−", GUILayout.Width(22f))) removeGrant = i;
                    EditorGUILayout.EndHorizontal();
                }
                if (removeGrant >= 0) ArrayUtility.RemoveAt(ref post.grantClueIds, removeGrant);
                if (GUILayout.Button("+ 단서 추가", GUILayout.ExpandWidth(false)))
                    ArrayUtility.Add(ref post.grantClueIds, "");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4f);
            DrawUnlock(post.unlock, ref _foldPostUnlock, "게시글 잠금 조건",
                "전부 비우면 사이트가 열려 있는 한 항상 보입니다. 잠긴 게시글은 목록에 아예 나오지 않습니다(제목 자체가 스포일러가 될 수 있어서).");

            EditorGUILayout.Space(4f);
            DrawAttachmentList(ref post.attachments, ref _foldPostAttachments, "게시글 장식용 첨부",
                "단서가 가진 첨부(사진/소리/맵)는 여기 넣지 않습니다 — 단서 탭에서 그 단서에 붙이면 게시글 본문에도 같이 나옵니다.\n" +
                "여기에는 단서와 무관한 분위기용 첨부만 넣으세요.");

            EditorGUILayout.Space(4f);
            DrawCommentList(ref post.comments, ref _foldPostComments, "댓글",
                "게시글에 달린 댓글 — 도감 코멘트와 같은 데이터 형식을 씁니다.");

            EditorGUI.indentLevel--;
        }

        private void DrawUnlock(InternetUnlockCondition u, ref bool fold, string title, string help)
        {
            if (u == null) return;

            string summary = u.IsEmpty ? "조건 없음" :
                $"단서 {u.requiredClueIds.Length} · 이벤트 {u.requiredEventKeys.Length} · 시간 {u.minGameTime:0}s";
            fold = EditorGUILayout.Foldout(fold, $"{title}  ({summary})", true, EditorStyles.foldoutHeader);
            if (!fold) return;

            EditorGUI.indentLevel++;
            if (!string.IsNullOrEmpty(help)) EditorGUILayout.HelpBox(help, MessageType.None);

            EditorGUILayout.LabelField("필요 단서 (전부 획득해야 열림)");
            int removeClue = -1;
            for (int i = 0; i < u.requiredClueIds.Length; i++)
            {
                EditorGUILayout.BeginHorizontal();
                u.requiredClueIds[i] = ClueIdPopup($"[{i}]", u.requiredClueIds[i]);
                if (GUILayout.Button("−", GUILayout.Width(22f))) removeClue = i;
                EditorGUILayout.EndHorizontal();
            }
            if (removeClue >= 0) ArrayUtility.RemoveAt(ref u.requiredClueIds, removeClue);
            if (GUILayout.Button("+ 필요 단서 추가", GUILayout.ExpandWidth(false)))
                ArrayUtility.Add(ref u.requiredClueIds, "");

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("필요 이벤트 (맵 + 이벤트 키)");
            int removeEvent = -1;
            for (int i = 0; i < u.requiredEventKeys.Length; i++)
            {
                // 저장 형식은 "mapGuid:eventKey" 한 문자열이지만, 손으로 치면 콜론을 빠뜨리기 쉬워
                // 맵은 드롭다운, 키는 텍스트로 나눠 받고 여기서 합친다.
                string raw = u.requiredEventKeys[i] ?? "";
                int sep = raw.IndexOf(':');
                string mapGuid = sep > 0 ? raw.Substring(0, sep) : "";
                string eventKey = sep >= 0 ? raw.Substring(sep + 1) : raw;

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"[{i}]", GUILayout.Width(28f));
                mapGuid = NodeGuidPopup("", mapGuid, allowEmpty: true);
                eventKey = EditorGUILayout.TextField(eventKey);
                if (GUILayout.Button("−", GUILayout.Width(22f))) removeEvent = i;
                EditorGUILayout.EndHorizontal();

                u.requiredEventKeys[i] = string.IsNullOrEmpty(mapGuid) && string.IsNullOrEmpty(eventKey)
                    ? "" : mapGuid + ":" + eventKey;
            }
            if (removeEvent >= 0) ArrayUtility.RemoveAt(ref u.requiredEventKeys, removeEvent);
            if (GUILayout.Button("+ 필요 이벤트 추가", GUILayout.ExpandWidth(false)))
                ArrayUtility.Add(ref u.requiredEventKeys, "");

            EditorGUILayout.Space(2f);
            u.minGameTime = EditorGUILayout.FloatField("최소 게임 시간 (초, 0이면 조건 없음)", u.minGameTime);

            EditorGUI.indentLevel--;
        }

        // 단서 이름 드롭다운 → 선택된 단서 ID 반환. 손으로 ID를 적다 틀리면 게시글이 아무것도
        // 주지 않는 채로 조용히 넘어가므로(런타임 경고만 뜬다) 목록에서 고르게 한다.
        private string ClueIdPopup(string label, string curId)
        {
            var clues = _clueDb.clues;
            var opts = new string[clues.Length + 1];
            opts[0] = "(없음)";
            int cur = 0;
            for (int i = 0; i < clues.Length; i++)
            {
                opts[i + 1] = $"{clues[i].name}  [{Sg(clues[i].id)}]";
                if (clues[i].id == curId) cur = i + 1;
            }

            // 목록에 없는 ID(오타·삭제된 단서)는 조용히 "(없음)"으로 바뀌면 안 된다 — 그대로 보여준다.
            if (cur == 0 && !string.IsNullOrEmpty(curId))
            {
                ArrayUtility.Add(ref opts, $"⚠ 없는 단서: {curId}");
                cur = opts.Length - 1;
            }

            int sel = string.IsNullOrEmpty(label)
                ? EditorGUILayout.Popup(cur, opts)
                : EditorGUILayout.Popup(label, cur, opts);
            if (sel == 0) return "";
            return sel - 1 < clues.Length ? clues[sel - 1].id : curId;
        }

        // ─── 추가 / 삭제 ─────────────────────────────────────────

        private void AddItem()
        {
            switch (_tab)
            {
                case Tab.Maps:
                    ArrayUtility.Add(ref _db.maps, new MapNodeData
                    {
                        guid          = NewGuid(),
                        nodeName      = "새 맵",
                        description   = "",
                        sceneName     = "",
                        iconPath      = "",
                        graphPosition = Vector2.zero,
                        events        = Array.Empty<MapEventFlag>(),
                        clueIds       = Array.Empty<string>(),
                        wavePaths     = Array.Empty<string>(),
                        enemyGroups   = Array.Empty<EnemyGroupEntry>(),
                        requiredGears = Array.Empty<EmotionColor>(),
                    });
                    _selMap = _db.maps.Length - 1;
                    break;

                case Tab.Connections:
                    ArrayUtility.Add(ref _db.connections, new MapConnectionData
                    {
                        guid     = NewGuid(),
                        fromGuid = _db.maps.Length > 0 ? _db.maps[0].guid : "",
                        toGuid   = _db.maps.Length > 1 ? _db.maps[1].guid : "",
                    });
                    _selConn = _db.connections.Length - 1;
                    break;

                case Tab.Clues:
                    ArrayUtility.Add(ref _clueDb.clues, new ClueData
                    {
                        id                   = NewGuid(),
                        name                 = "새 단서",
                        description          = "",
                        targetMapGuid        = "",
                        targetConnectionGuid = "",
                        requiredEventKey     = "",
                        type                 = ClueType.EventHint,
                        timestamp            = "",
                        content              = "",
                        source               = "",
                        codexMapGuid         = "",
                        keywords             = Array.Empty<string>(),
                        comments             = Array.Empty<CodexComment>(),
                        attachments          = Array.Empty<ClueAttachment>(),
                    });
                    _selClue = _clueDb.clues.Length - 1;
                    break;

                case Tab.Internet:
                    ArrayUtility.Add(ref _netDb.sites, new InternetSite
                    {
                        id       = "site-" + NewGuid(),
                        name     = "새 사이트",
                        iconPath = "",
                        unlock   = new InternetUnlockCondition
                        {
                            requiredClueIds   = Array.Empty<string>(),
                            requiredEventKeys = Array.Empty<string>(),
                        },
                        posts    = Array.Empty<InternetPost>(),
                    });
                    _selSite = _netDb.sites.Length - 1;
                    break;
            }
        }

        private void RemoveItem(int idx)
        {
            switch (_tab)
            {
                case Tab.Maps:        ArrayUtility.RemoveAt(ref _db.maps,        idx); break;
                case Tab.Connections: ArrayUtility.RemoveAt(ref _db.connections, idx); break;
                case Tab.Clues:       ArrayUtility.RemoveAt(ref _clueDb.clues,   idx); break;
                case Tab.Internet:    ArrayUtility.RemoveAt(ref _netDb.sites,    idx); break;
            }
        }

        // ─── GUI 유틸 ─────────────────────────────────────────────

        private void SectionHeader(string title)
        {
            var r = GUILayoutUtility.GetRect(0f, 26f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, ColHeader);
            GUI.Label(new Rect(r.x + 8f, r.y + 1f, r.width, r.height), title, EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);
        }

        private static void ReadonlyField(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            EditorGUILayout.SelectableLabel(value, EditorStyles.miniLabel, GUILayout.Height(18f));
            EditorGUILayout.EndHorizontal();
        }

        private static string TF(string label, string value) =>
            EditorGUILayout.TextField(label, value ?? "");

        private static string TA(string label, string value)
        {
            EditorGUILayout.LabelField(label);
            return EditorGUILayout.TextArea(value ?? "", GUILayout.MinHeight(54f));
        }

        // 첨부물로 쓸 에셋을 자동 복사해 넣는 곳. Resources.Load는 Resources 폴더 안의 에셋만 볼 수
        // 있는데(그게 유니티 규칙이다), 작업자가 쓰고 싶은 사진/소리는 보통 Assets/Images 같은 바깥
        // 폴더에 있다 — 그래서 밖의 에셋을 끌어다 놓으면 여기로 복사할지 물어보고 경로를 채워 준다.
        private const string CopyTargetResourcesDir = "Assets/Resources/RouteFinding/Attachments";

        // "Resources 상대 경로" 문자열 필드 + 에셋 오브젝트 칸을 한 줄에 같이 보여준다.
        // 경로를 직접 칠 수도 있고, 에셋을 끌어다 놓으면 Resources/ 이후 경로(확장자 제외)로 변환해
        // 넣어준다 — JSON에는 에셋 참조를 담을 수 없어 경로가 유일한 연결 고리라, 손으로 적다 틀리는
        // 사고(대소문자/확장자 포함 등)를 막는 게 목적이다.
        private static string ResourcePathField<T>(string label, string path) where T : UnityEngine.Object
        {
            EditorGUILayout.BeginHorizontal();
            string newPath = EditorGUILayout.TextField(label, path ?? "");
            var current = string.IsNullOrWhiteSpace(newPath) ? null : Resources.Load<T>(newPath);
            var picked = EditorGUILayout.ObjectField(current, typeof(T), false, GUILayout.Width(120f)) as T;
            EditorGUILayout.EndHorizontal();

            if (picked != current)
            {
                string assetPath = picked == null ? "" : AssetDatabase.GetAssetPath(picked);
                newPath = ToResourcesPath(assetPath);

                // Resources 밖의 에셋 — 작업자에게 복사할지 물어본다(취소하면 경로를 비운 채로 둔다).
                if (picked != null && string.IsNullOrEmpty(newPath))
                    newPath = CopyIntoResources<T>(assetPath);
            }

            // 경로는 있는데 로드가 안 되면(오타 등) 런타임에도 "(파일 없음)"으로 뜬다 — 미리 알려준다.
            if (!string.IsNullOrWhiteSpace(newPath) && Resources.Load<T>(newPath) == null)
                EditorGUILayout.HelpBox($"Resources/{newPath} 을(를) 찾을 수 없습니다.", MessageType.Warning);

            return newPath;
        }

        // Resources 밖의 에셋을 CopyTargetResourcesDir로 복사하고 그 Resources 상대 경로를 돌려준다.
        // 원본은 건드리지 않는다 — 다른 곳에서 이미 참조하고 있을 수 있어서 이동이 아니라 복사다.
        // 실패하거나 작업자가 취소하면 빈 문자열.
        private static string CopyIntoResources<T>(string assetPath) where T : UnityEngine.Object
        {
            string fileName = Path.GetFileName(assetPath);
            if (!EditorUtility.DisplayDialog(
                    "Resources 폴더 밖의 에셋",
                    $"{fileName} 은(는) Resources 폴더 밖에 있어 게임 실행 중에는 불러올 수 없습니다.\n\n" +
                    $"{CopyTargetResourcesDir}/ 로 복사해서 쓸까요?\n(원본은 그대로 남습니다)",
                    "복사해서 사용", "취소"))
                return "";

            EnsureFolder(CopyTargetResourcesDir);

            string dest = AssetDatabase.GenerateUniqueAssetPath($"{CopyTargetResourcesDir}/{fileName}");
            if (!AssetDatabase.CopyAsset(assetPath, dest))
            {
                Debug.LogError($"[맵 DB 편집기] 복사에 실패했습니다: {assetPath} → {dest}");
                return "";
            }
            AssetDatabase.ImportAsset(dest);

            // 사진은 임포트 타입이 Sprite가 아니면 Resources.Load<Sprite>가 못 찾는다(런타임에 Texture2D
            // 폴백이 있긴 하지만, 여기서 맞춰두면 편집기 미리보기부터 정상적으로 뜬다).
            if (typeof(T) == typeof(Sprite) &&
                AssetImporter.GetAtPath(dest) is TextureImporter importer &&
                importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.SaveAndReimport();
            }

            Debug.Log($"[맵 DB 편집기] 첨부용으로 복사했습니다: {assetPath} → {dest}");
            return ToResourcesPath(dest);
        }

        // "Assets/A/B/C"처럼 중간 폴더가 없어도 한 단계씩 만들어 준다(AssetDatabase.CreateFolder는
        // 부모가 이미 있어야만 동작한다).
        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            var parts = folderPath.Split('/');
            string cur = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        // Assets/…/Resources/Foo/bar.png → Foo/bar. Resources 밖이면 빈 문자열
        // (경고를 여기서 찍지 않는다 — OnGUI는 이벤트마다 다시 도는데 그때마다 콘솔에 쌓인다).
        private static string ToResourcesPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return "";

            const string marker = "/Resources/";
            int i = assetPath.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "";

            string rel = assetPath.Substring(i + marker.Length);
            int dot = rel.LastIndexOf('.');
            return dot >= 0 ? rel.Substring(0, dot) : rel;
        }

        // 노드 이름 드롭다운 → 선택된 GUID 반환
        private string NodeGuidPopup(string label, string curGuid, bool allowEmpty = false)
        {
            var maps = _db.maps;
            if (maps.Length == 0 && !allowEmpty)
            {
                EditorGUILayout.LabelField(label, "(맵 없음 — 먼저 맵을 추가하세요)");
                return curGuid;
            }

            int off  = allowEmpty ? 1 : 0;
            var opts = new string[maps.Length + off];
            int cur  = allowEmpty ? 0 : 0;
            if (allowEmpty) opts[0] = "(없음)";
            for (int i = 0; i < maps.Length; i++)
            {
                opts[i + off] = $"{maps[i].nodeName}  [{Sg(maps[i].guid)}]";
                if (maps[i].guid == curGuid) cur = i + off;
            }
            int sel = EditorGUILayout.Popup(label, cur, opts);
            if (allowEmpty && sel == 0) return "";
            int mi = sel - off;
            return (mi >= 0 && mi < maps.Length) ? maps[mi].guid : curGuid;
        }

        // 연결 이름 드롭다운 → 선택된 GUID 반환
        private string ConnGuidPopup(string label, string curGuid, bool allowEmpty = false)
        {
            var conns = _db.connections;
            if (conns.Length == 0 && !allowEmpty)
            {
                EditorGUILayout.LabelField(label, "(연결 없음)");
                return curGuid;
            }

            int off  = allowEmpty ? 1 : 0;
            var opts = new string[conns.Length + off];
            int cur  = allowEmpty ? 0 : 0;
            if (allowEmpty) opts[0] = "(없음)";
            for (int i = 0; i < conns.Length; i++)
            {
                opts[i + off] = $"{NodeName(conns[i].fromGuid)} → {NodeName(conns[i].toGuid)}  [{Sg(conns[i].guid)}]";
                if (conns[i].guid == curGuid) cur = i + off;
            }
            int sel = EditorGUILayout.Popup(label, cur, opts);
            if (allowEmpty && sel == 0) return "";
            int ci = sel - off;
            return (ci >= 0 && ci < conns.Length) ? conns[ci].guid : curGuid;
        }

        // GUID → 노드 이름 역조회
        private string NodeName(string guid)
        {
            if (_db?.maps == null || string.IsNullOrEmpty(guid)) return "?";
            foreach (var n in _db.maps)
                if (n.guid == guid) return n.nodeName;
            return Sg(guid);
        }

        private static string NewGuid()    => Guid.NewGuid().ToString("N").Substring(0, 16);
        private static string Sg(string g) => string.IsNullOrEmpty(g) ? "?" :
                                              (g.Length > 8 ? g.Substring(0, 8) + "…" : g);
        private static string ShortPath(string p) => p.Length > 52 ? "…" + p.Substring(p.Length - 51) : p;
    }
}
