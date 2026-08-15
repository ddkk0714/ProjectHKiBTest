using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

// 단서 첨부물(ClueAttachment)의 실제 에셋을 Addressables에서 로드하고 표시용 정보를 뽑아주는 조회 계층.
// MapGraph와 같은 "읽기 전용 데이터 레이어" 역할이지만, 씬 배치가 필요 없는 정적 클래스다.
//
// ▸ 주소 체계: 데이터(clues.json/internet.json/map_database.json)에는 Addressable **주소 문자열**만
//   남는다 — MapDataSO.mapAddressableID와 같은 방식이다. 에셋의 소유권은 Addressables 그룹에 있고,
//   Resources 폴더에 둘 필요가 없다(Resources는 폴더 전체가 항상 빌드에 실린다).
//
// ▸ 캐시: 같은 주소를 카드 전환마다 다시 로드하지 않도록 핸들과 결과를 함께 보관한다. 실패(null)도
//   같이 캐시해서, 주소가 틀린 첨부물이 있어도 매번 다시 뒤지거나 콘솔을 경고로 도배하지 않는다
//   (경고는 주소당 한 번만). 핸들을 들고 있는 이유는 ClearCache에서 Release해 주기 위해서다.
//
// ▸ 실패해도 예외를 던지지 않는다 — 카드 쪽에서 sprite/clip이 null이면 "(파일 없음)"으로 표시한다.
//   그래서 등록되지 않은 주소를 LoadAssetAsync에 그대로 넘기지 않고(InvalidKeyException + 콘솔 에러가
//   난다) LoadResourceLocations로 먼저 존재를 확인한다.
//
// ▸ 동기 로드인 이유: 호출부(CodexCardView/InternetPostView/NoteRouteGraphView)가 반환된 에셋으로
//   그 자리에서 행 높이를 계산한다 — 사진이 있으면 미리보기만큼 행이 길어진다. 비동기로 바꾸면 세 뷰
//   모두 "나중에 도착하면 다시 레이아웃" 경로가 필요해진다. 첨부물은 로컬 그룹의 작은 에셋이라
//   WaitForCompletion 비용이 Resources.Load와 크게 다르지 않다. 원격 카탈로그로 옮기게 되면 이 결정을
//   먼저 다시 봐야 한다.
public static class ClueAttachmentService
{
    private static readonly Dictionary<string, Sprite> SpriteCache = new();
    private static readonly Dictionary<string, AudioClip> ClipCache = new();

    // Release 대상 핸들. 실패한 로드는 여기 들어오지 않는다(핸들이 유효하지 않음).
    private static readonly List<AsyncOperationHandle> Handles = new();

    // Sprite.Create로 감싼 폴백들 — 어느 핸들의 소유도 아니라 ClearCache에서 직접 파괴해야 한다.
    private static readonly List<Sprite> FallbackSprites = new();

    // 사진 첨부물 로드. Sprite로 임포트된 텍스처가 아니면(Texture2D) 런타임에 Sprite로 감싸준다 —
    // 콘텐츠 작업자가 임포트 설정(Texture Type)을 Sprite로 바꿔놓는 걸 잊어도 그림이 나오게 하기 위함.
    public static Sprite LoadSprite(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        if (SpriteCache.TryGetValue(address, out var cached)) return cached;

        var sprite = LoadSync<Sprite>(address);
        if (sprite == null)
        {
            var tex = LoadSync<Texture2D>(address);
            if (tex != null)
            {
                sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                FallbackSprites.Add(sprite);
            }
        }
        if (sprite == null)
            Debug.LogWarning($"[ClueAttachmentService] 이미지를 찾을 수 없습니다: Addressable 주소 '{address}'");

        SpriteCache[address] = sprite;
        return sprite;
    }

    public static AudioClip LoadAudio(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        if (ClipCache.TryGetValue(address, out var cached)) return cached;

        var clip = LoadSync<AudioClip>(address);
        if (clip == null)
            Debug.LogWarning($"[ClueAttachmentService] 오디오를 찾을 수 없습니다: Addressable 주소 '{address}'");

        ClipCache[address] = clip;
        return clip;
    }

    // 주소가 등록돼 있는지 먼저 보고, 있을 때만 로드한다. 없으면 조용히 null —
    // 등록 안 된 키를 LoadAssetAsync에 넘기면 InvalidKeyException이 콘솔 에러로 찍혀서,
    // "(파일 없음)"으로 부드럽게 처리한다는 이 클래스의 규칙이 깨진다.
    private static T LoadSync<T>(string address) where T : Object
    {
        if (!Application.isPlaying) return null; // 에디터 도구는 AssetDatabase로 직접 찾는다

        var locHandle = Addressables.LoadResourceLocationsAsync(address, typeof(T));
        IList<IResourceLocation> locations = locHandle.WaitForCompletion();
        bool found = locations != null && locations.Count > 0;
        Addressables.Release(locHandle);
        if (!found) return null;

        var handle = Addressables.LoadAssetAsync<T>(address);
        T asset = handle.WaitForCompletion();
        if (handle.Status != AsyncOperationStatus.Succeeded || asset == null)
        {
            Addressables.Release(handle);
            return null;
        }

        Handles.Add(handle);
        return asset;
    }

    // 첨부물 옆에 붙는 작은 아이콘. 맵 참조는 그 맵의 아이콘(MapNodeData.iconAddress)을 쓰고,
    // 사진은 자기 자신을 축소해 쓰며, 소리는 아이콘이 없다(카드가 텍스트 배지로 대체).
    public static Sprite ResolveIcon(ClueAttachment attachment)
    {
        if (attachment == null) return null;
        switch (attachment.kind)
        {
            case ClueAttachmentKind.Image:
                return LoadSprite(attachment.address);
            case ClueAttachmentKind.MapRef:
                var node = ResolveMapNode(attachment);
                return node != null ? LoadSprite(node.iconAddress) : null;
            default:
                return null;
        }
    }

    // 카드에 쓸 표시 이름 — label이 비어 있으면 맵 이름, 그것도 없으면 종류 기본 이름으로 대체한다.
    public static string ResolveLabel(ClueAttachment attachment)
    {
        if (attachment == null) return "";
        if (!string.IsNullOrWhiteSpace(attachment.label)) return attachment.label;

        if (attachment.kind == ClueAttachmentKind.MapRef)
        {
            var node = ResolveMapNode(attachment);
            if (node != null) return node.nodeName;
        }
        return ClueAttachmentConfig.GetDisplayName(attachment.kind);
    }

    public static MapNodeData ResolveMapNode(ClueAttachment attachment)
    {
        if (attachment == null || string.IsNullOrEmpty(attachment.mapGuid)) return null;
        return MapGraph.Instance != null ? MapGraph.Instance.GetNode(attachment.mapGuid) : null;
    }

    // 에디터에서 clues.json/에셋을 고쳐 다시 재생할 때처럼, 캐시된 실패 결과를 버려야 할 때 호출.
    // 캐시를 비우는 김에 Addressables 핸들도 함께 놓아준다 — 안 놓으면 참조 카운트가 남아
    // 에셋이 메모리에서 내려가지 않는다.
    public static void ClearCache()
    {
        SpriteCache.Clear();
        ClipCache.Clear();

        // 파괴 순서 주의 — 폴백 스프라이트를 먼저 버린다. 그 원본 Texture2D를 들고 있는 게
        // 아래에서 Release할 핸들이라, 반대로 하면 이미 내려간 텍스처를 가리키는 스프라이트가 남는다.
        foreach (var sprite in FallbackSprites)
        {
            if (sprite != null) Object.Destroy(sprite);
        }
        FallbackSprites.Clear();

        foreach (var handle in Handles)
        {
            if (handle.IsValid()) Addressables.Release(handle);
        }
        Handles.Clear();
    }
}
