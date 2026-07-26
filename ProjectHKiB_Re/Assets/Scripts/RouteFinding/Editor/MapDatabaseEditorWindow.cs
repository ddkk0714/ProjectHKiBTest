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
        private enum Tab { Maps, Connections, Clues }
        private Tab _tab = Tab.Maps;

        // ─── 데이터 ──────────────────────────────────────────────
        private MapDatabase  _db;
        private ClueDatabase _clueDb;
        private string _dbPath   = "";
        private string _cluePath = "";
        private bool   _dirty;

        // ─── UI 상태 ─────────────────────────────────────────────
        private int     _selMap  = -1;
        private int     _selConn = -1;
        private int     _selClue = -1;
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

            _dbPath   = FindAbsPath("map_database");
            _cluePath = FindAbsPath("clues");

            if (File.Exists(_dbPath))
            {
                _db = JsonUtility.FromJson<MapDatabase>(File.ReadAllText(_dbPath));
                _db.maps        = _db.maps        ?? Array.Empty<MapNodeData>();
                _db.connections = _db.connections ?? Array.Empty<MapConnectionData>();
                foreach (var m in _db.maps)
                {
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
                }
            }

            _dirty = false;
            Repaint();
        }

        private void SaveAll()
        {
            if (!string.IsNullOrEmpty(_dbPath))
                File.WriteAllText(_dbPath, JsonUtility.ToJson(_db, prettyPrint: true));
            if (!string.IsNullOrEmpty(_cluePath))
                File.WriteAllText(_cluePath, JsonUtility.ToJson(_clueDb, prettyPrint: true));
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
                                                    $"단서  ({_clueDb.clues.Length})";
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

            // 4단계(2026-07-14) — NPC/시스템 코멘트. 플레이어가 입력하는 게 아니라 콘텐츠 작업자가
            // 여기서 직접 채워 넣는 대사 데이터다(Clue_System.md 1-4장 확정 사항).
            EditorGUILayout.Space(4f);
            _foldComments = EditorGUILayout.Foldout(_foldComments,
                $"코멘트 (NPC/시스템)  ({cl.comments.Length}개)", true, EditorStyles.foldoutHeader);
            if (_foldComments)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("플레이어 입력이 아니라 NPC/시스템이 다는 코멘트 — 카드에서 타이프라이터 연출로 출력됨.", MessageType.None);
                int removeComment = -1;
                for (int i = 0; i < cl.comments.Length; i++)
                {
                    var cm = cl.comments[i];
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label($"[{i}]", GUILayout.Width(28f));
                    cm.author = EditorGUILayout.TextField("작성자", cm.author ?? "");
                    if (GUILayout.Button("−", GUILayout.Width(22f))) removeComment = i;
                    EditorGUILayout.EndHorizontal();
                    cm.createdAt = EditorGUILayout.TextField("시간 (선택, 비우면 숨김)", cm.createdAt ?? "");
                    EditorGUILayout.LabelField("내용");
                    cm.text = EditorGUILayout.TextArea(cm.text ?? "", GUILayout.MinHeight(36f));
                    EditorGUILayout.EndVertical();
                }
                if (removeComment >= 0) ArrayUtility.RemoveAt(ref cl.comments, removeComment);
                if (GUILayout.Button("+ 코멘트 추가", GUILayout.ExpandWidth(false)))
                    ArrayUtility.Add(ref cl.comments, new CodexComment { author = "", text = "", createdAt = "" });
                EditorGUI.indentLevel--;
            }

            if (EditorGUI.EndChangeCheck()) _dirty = true;
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
                    });
                    _selClue = _clueDb.clues.Length - 1;
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
