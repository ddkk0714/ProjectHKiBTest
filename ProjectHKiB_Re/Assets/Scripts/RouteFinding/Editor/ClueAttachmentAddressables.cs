using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace RouteFinding.Editor
{
    // 단서 첨부물/아이콘이 쓰는 Addressable 주소를 편집기에서 다루는 도구 모음.
    // 맵 DB 편집기(MapDatabaseEditorWindow)의 주소 필드와 검증 버튼이 여기를 통해 움직인다.
    //
    // 런타임(ClueAttachmentService)은 Addressables 카탈로그로 주소를 푼다. 하지만 편집기에서는
    // 카탈로그가 아직 빌드되지 않았을 수 있고 재생 중도 아니라, AddressableAssetSettings(= 등록
    // 장부)를 직접 뒤져 에셋을 찾는다 — MapDataRegistrySOEditor.Validate와 같은 방식이다.
    public static class ClueAttachmentAddressables
    {
        // 새로 등록되는 첨부물이 들어갈 그룹. 없으면 만든다.
        public const string GroupName = "ClueAttachments";

        // Resources 밖으로 옮길 때 제안하는 위치. 종류별로 프로젝트의 기존 폴더를 따른다.
        private const string ImageMoveDir = "Assets/Images/ClueAttachments";
        private const string AudioMoveDir = "Assets/Audio/ClueAttachments";

        // 주소 → 에셋 경로 캐시. OnGUI는 이벤트마다 다시 도는데 그때마다 전 그룹을 훑으면
        // 첨부물이 몇 개만 있어도 편집기가 눈에 띄게 느려진다.
        private static Dictionary<string, string> _addressToPath;
        private static bool _hooked;

        // ─── 조회 ────────────────────────────────────────────────

        /// <summary>등록된 Addressable 주소로 에셋을 찾는다. 없으면 null.</summary>
        public static T LoadByAddress<T>(string address) where T : Object
        {
            if (string.IsNullOrWhiteSpace(address)) return null;

            EnsureLookup();
            return _addressToPath.TryGetValue(address, out string path)
                ? AssetDatabase.LoadAssetAtPath<T>(path)
                : null;
        }

        public static bool IsRegistered(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return false;

            EnsureLookup();
            return _addressToPath.ContainsKey(address);
        }

        private static void EnsureLookup()
        {
            if (!_hooked)
            {
                // 그룹 창에서 주소를 고치거나 엔트리를 지워도 캐시가 따라오게 한다.
                AddressableAssetSettings.OnModificationGlobal += (_, _, _) => _addressToPath = null;
                _hooked = true;
            }
            if (_addressToPath != null) return;

            _addressToPath = new Dictionary<string, string>();

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return; // 프로젝트에 Addressable 설정이 아직 없다 — 호출부가 경고를 띄운다

            var buffer = new List<AddressableAssetEntry>();
            foreach (AddressableAssetGroup group in settings.groups)
            {
                if (group == null) continue;

                buffer.Clear();
                group.GatherAllAssets(buffer, true, true, false);
                foreach (AddressableAssetEntry entry in buffer)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.address)) continue;
                    _addressToPath[entry.address] = entry.AssetPath;
                }
            }
        }

        public static void InvalidateLookup() => _addressToPath = null;

        // ─── 등록 ────────────────────────────────────────────────

        /// <summary>
        /// 에셋이 Addressable로 등록돼 있으면 그 주소를, 아니면 작업자에게 물어보고 등록한 뒤 주소를
        /// 돌려준다. 취소하면 빈 문자열 — 주소를 비워두면 런타임에 "(파일 없음)"으로 뜨므로,
        /// 등록을 건너뛴 첨부물이 조용히 정상인 척하지 않는다.
        /// </summary>
        public static string EnsureAddressable(Object asset, bool wantSprite)
        {
            if (asset == null) return "";

            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(assetPath)) return "";

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                EditorUtility.DisplayDialog(
                    "Addressable 설정 없음",
                    "이 프로젝트에 Addressable 설정이 아직 없습니다.\n\n" +
                    "Window → Asset Management → Addressables → Groups에서 먼저 초기화하세요.",
                    "확인");
                return "";
            }

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            AddressableAssetEntry existing = settings.FindAssetEntry(guid);
            if (existing != null)
            {
                // 이미 등록돼 있다 — 주소만 가져다 쓴다. 남이 정해둔 주소를 말없이 바꾸지 않는다.
                if (wantSprite) EnsureSpriteImport(assetPath);
                return existing.address;
            }

            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            string address = MakeUniqueAddress(fileName);

            if (!EditorUtility.DisplayDialog(
                    "Addressable 미등록 에셋",
                    $"{Path.GetFileName(assetPath)} 은(는) Addressable로 등록돼 있지 않아 " +
                    $"게임 실행 중에는 불러올 수 없습니다.\n\n" +
                    $"'{GroupName}' 그룹에 주소 '{address}' 로 등록할까요?",
                    "등록해서 사용", "취소"))
                return "";

            AddressableAssetGroup group = settings.FindGroup(GroupName) ?? CreateGroup(settings);
            AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group);
            if (entry == null)
            {
                Debug.LogError($"[맵 DB 편집기] Addressable 등록에 실패했습니다: {assetPath}");
                return "";
            }

            entry.address = address;
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
            InvalidateLookup();

            if (wantSprite) EnsureSpriteImport(assetPath);
            OfferMoveOutOfResources(assetPath, wantSprite);

            Debug.Log($"[맵 DB 편집기] Addressable로 등록했습니다: {assetPath} → 주소 '{address}' ({GroupName})");
            return entry.address;
        }

        /// <summary>
        /// 주소와 같은 이름의 에셋을 프로젝트에서 찾아 그 주소로 등록한다(맵 DB 편집기의 검증 →
        /// 자동 등록 경로). 후보가 없거나 둘 이상이면 등록하지 않고 false — 잘못 고르면 엉뚱한
        /// 에셋이 조용히 붙어서, 아예 못 찾은 것보다 알아채기 어렵다.
        /// </summary>
        public static bool TryRegisterByName(string address, bool isAudio)
        {
            if (string.IsNullOrWhiteSpace(address)) return false;

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[첨부물] Addressable 설정이 없어 자동 등록을 건너뜁니다. " +
                               "Window → Asset Management → Addressables → Groups에서 초기화하세요.");
                return false;
            }

            // t:Texture는 Sprite 임포트 여부와 무관하게 잡힌다 — 임포트 설정은 등록 뒤에 맞춘다.
            string filter = isAudio ? $"\"{address}\" t:AudioClip" : $"\"{address}\" t:Texture";
            var matches = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets(filter))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == address) matches.Add(path);
            }

            if (matches.Count == 0)
            {
                Debug.LogWarning($"[첨부물] 주소 '{address}'와 이름이 같은 에셋을 찾지 못했습니다 — 수동으로 연결하세요.");
                return false;
            }
            if (matches.Count > 1)
            {
                Debug.LogWarning($"[첨부물] 주소 '{address}'와 이름이 같은 에셋이 여러 개라 자동 등록하지 않았습니다:\n" +
                                 string.Join("\n", matches));
                return false;
            }

            string assetPath = matches[0];
            string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);

            AddressableAssetEntry existing = settings.FindAssetEntry(assetGuid);
            if (existing != null)
            {
                // 이미 다른 주소로 등록된 에셋이다. 남의 주소를 말없이 갈아엎지 않는다 —
                // 어느 쪽이 맞는지는 콘텐츠 쪽 결정이라 사람에게 넘긴다.
                Debug.LogWarning($"[첨부물] '{assetPath}'는 이미 주소 '{existing.address}'로 등록돼 있습니다. " +
                                 $"JSON의 '{address}'를 그 주소로 바꾸거나, Groups 창에서 주소를 맞추세요.");
                return false;
            }

            AddressableAssetGroup group = settings.FindGroup(GroupName) ?? CreateGroup(settings);
            AddressableAssetEntry entry = settings.CreateOrMoveEntry(assetGuid, group);
            if (entry == null)
            {
                Debug.LogError($"[첨부물] Addressable 등록에 실패했습니다: {assetPath}");
                return false;
            }

            entry.address = address;
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
            InvalidateLookup();

            if (!isAudio) EnsureSpriteImport(assetPath);
            OfferMoveOutOfResources(assetPath, !isAudio);

            Debug.Log($"[첨부물] 자동 등록: {assetPath} → 주소 '{address}' ({GroupName})");
            return true;
        }

        private static AddressableAssetGroup CreateGroup(AddressableAssetSettings settings)
        {
            // PrototypeMaps 그룹과 같은 구성(번들 + 콘텐츠 갱신 스키마)으로 만든다.
            return settings.CreateGroup(
                GroupName,
                setAsDefaultGroup: false,
                readOnly: false,
                postEvent: true,
                schemasToCopy: null,
                typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        }

        // 주소가 겹치면 Addressables가 어느 쪽을 줄지 알 수 없다 — 파일 이름이 같은 에셋이
        // 여러 폴더에 있는 건 흔한 일이라 여기서 미리 갈라 둔다.
        private static string MakeUniqueAddress(string baseAddress)
        {
            EnsureLookup();
            if (!_addressToPath.ContainsKey(baseAddress)) return baseAddress;

            for (int i = 2; ; i++)
            {
                string candidate = $"{baseAddress}_{i}";
                if (!_addressToPath.ContainsKey(candidate)) return candidate;
            }
        }

        // 사진은 임포트 타입이 Sprite가 아니면 편집기 미리보기(LoadAssetAtPath<Sprite>)가 비어 보인다.
        // 런타임에는 Texture2D 폴백이 있지만, 여기서 맞춰두면 편집기부터 정상적으로 뜬다.
        private static void EnsureSpriteImport(string assetPath)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer) return;
            if (importer.textureType == TextureImporterType.Sprite) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.SaveAndReimport();
        }

        // Addressable로 등록했는데 에셋이 아직 Resources 폴더 안에 있으면, 그 에셋은 빌드에 두 번
        // 실린다(Resources 폴더 통째 + Addressable 번들). 옮길지 물어본다 — MoveAsset은 GUID를
        // 유지하므로 방금 만든 엔트리는 그대로 살아 있다.
        private static void OfferMoveOutOfResources(string assetPath, bool isImage)
        {
            if (assetPath.IndexOf("/Resources/", System.StringComparison.OrdinalIgnoreCase) < 0) return;

            string targetDir = isImage ? ImageMoveDir : AudioMoveDir;
            if (!EditorUtility.DisplayDialog(
                    "Resources 폴더 안의 에셋",
                    $"{Path.GetFileName(assetPath)} 은(는) Resources 폴더 안에 있습니다.\n\n" +
                    "Resources 폴더는 통째로 빌드에 실리므로, Addressable로 등록한 뒤에는 같은 에셋이 " +
                    "두 번 들어갑니다.\n\n" +
                    $"{targetDir}/ 로 옮길까요?\n(주소와 참조는 그대로 유지됩니다)",
                    "옮기기", "그대로 두기"))
                return;

            EnsureFolder(targetDir);

            string dest = AssetDatabase.GenerateUniqueAssetPath($"{targetDir}/{Path.GetFileName(assetPath)}");
            string error = AssetDatabase.MoveAsset(assetPath, dest);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError($"[맵 DB 편집기] 이동에 실패했습니다: {assetPath} → {dest} ({error})");
                return;
            }

            InvalidateLookup();
            Debug.Log($"[맵 DB 편집기] Resources 밖으로 옮겼습니다: {assetPath} → {dest}");
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
    }
}
