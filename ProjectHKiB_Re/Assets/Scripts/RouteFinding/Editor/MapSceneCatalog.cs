using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RouteFinding.Editor
{
    // 맵 DB 편집기의 "씬 이름" 드롭다운에 채울 목록 — 프로젝트의 MapDataSO가 가진
    // mapAddressableID를 모은다. 노드의 sceneName이 이 값과 일치해야 실제 씬이 로드된다
    // (RouteFindingMapBridge가 MapDataRegistry.Find(node.sceneName)로 잇는다).
    //
    // [왜 MapDataSO를 직접 참조하지 않고 문자열로 찾는가]
    // RouteFinding은 외부 시스템을 컴파일 타임에 참조하지 않는 구조를 지켜 왔다 — 양쪽을 아는
    // 코드는 폴더 밖 RouteFindingMapBridge 하나뿐이다(그 클래스 주석 참고). 편집기 도구라고
    // 그 경계를 깨면 RouteFinding만 떼어낼 때 이 파일이 걸린다. 그래서 타입 이름 문자열로
    // 검색하고 SerializedObject로 필드를 읽는다 — MapDataSO가 없는 프로젝트에서는 그냥
    // 빈 목록이 되고 컴파일은 통과한다.
    public static class MapSceneCatalog
    {
        public readonly struct Entry
        {
            public readonly string Address;   // MapDataSO.mapAddressableID (= 씬 이름)
            public readonly string AssetName; // 어느 MapDataSO에서 왔는지 — 중복 주소를 눈으로 가리기 위함

            public Entry(string address, string assetName)
            {
                Address = address;
                AssetName = assetName;
            }
        }

        private static List<Entry> _entries;

        /// <summary>알려진 맵 주소 목록. 창을 열거나 데이터를 다시 불러올 때 갱신된다.</summary>
        public static IReadOnlyList<Entry> Entries
        {
            get
            {
                if (_entries == null) Refresh();
                return _entries;
            }
        }

        public static bool Contains(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return false;

            foreach (Entry e in Entries)
            {
                if (e.Address == address) return true;
            }
            return false;
        }

        /// <summary>
        /// 에셋을 다시 훑는다. 매 OnGUI마다 FindAssets를 돌리면 창이 눈에 띄게 느려지므로
        /// 캐시해 두고, 창을 열 때/불러오기를 누를 때/새로고침 버튼을 누를 때만 부른다.
        /// </summary>
        public static void Refresh()
        {
            _entries = new List<Entry>();

            foreach (string guid in AssetDatabase.FindAssets("t:MapDataSO"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (asset == null) continue;

                var so = new SerializedObject(asset);
                SerializedProperty prop = so.FindProperty("mapAddressableID");
                if (prop == null || prop.propertyType != SerializedPropertyType.String) continue;
                if (string.IsNullOrWhiteSpace(prop.stringValue)) continue;

                _entries.Add(new Entry(prop.stringValue, asset.name));
            }

            _entries.Sort((a, b) => string.CompareOrdinal(a.Address, b.Address));
        }

        public static void Invalidate() => _entries = null;
    }
}
