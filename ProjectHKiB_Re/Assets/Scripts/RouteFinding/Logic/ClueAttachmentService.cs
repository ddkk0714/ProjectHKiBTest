using System.Collections.Generic;
using UnityEngine;

// 단서 첨부물(ClueAttachment)의 실제 에셋을 Resources에서 로드하고 표시용 정보를 뽑아주는 조회 계층.
// MapGraph와 같은 "읽기 전용 데이터 레이어" 역할이지만, 씬 배치가 필요 없는 정적 클래스다.
//
// ▸ 캐시: 같은 경로를 카드 전환마다 다시 Resources.Load 하지 않도록 결과를 보관한다. 실패(null)도
//   같이 캐시해서, 경로가 틀린 첨부물이 있어도 매번 다시 뒤지거나 콘솔을 경고로 도배하지 않는다
//   (경고는 경로당 한 번만).
// ▸ 실패해도 예외를 던지지 않는다 — 카드 쪽에서 sprite/clip이 null이면 "(파일 없음)"으로 표시한다.
public static class ClueAttachmentService
{
    private static readonly Dictionary<string, Sprite> SpriteCache = new();
    private static readonly Dictionary<string, AudioClip> ClipCache = new();

    // 사진 첨부물 로드. Sprite로 임포트된 텍스처가 아니면(Texture2D) 런타임에 Sprite로 감싸준다 —
    // 콘텐츠 작업자가 임포트 설정(Texture Type)을 Sprite로 바꿔놓는 걸 잊어도 그림이 나오게 하기 위함.
    public static Sprite LoadSprite(string resourcePath)
    {
        if (string.IsNullOrWhiteSpace(resourcePath)) return null;
        if (SpriteCache.TryGetValue(resourcePath, out var cached)) return cached;

        var sprite = Resources.Load<Sprite>(resourcePath);
        if (sprite == null)
        {
            var tex = Resources.Load<Texture2D>(resourcePath);
            if (tex != null)
                sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }
        if (sprite == null)
            Debug.LogWarning($"[ClueAttachmentService] 이미지를 찾을 수 없습니다: Resources/{resourcePath}");

        SpriteCache[resourcePath] = sprite;
        return sprite;
    }

    public static AudioClip LoadAudio(string resourcePath)
    {
        if (string.IsNullOrWhiteSpace(resourcePath)) return null;
        if (ClipCache.TryGetValue(resourcePath, out var cached)) return cached;

        var clip = Resources.Load<AudioClip>(resourcePath);
        if (clip == null)
            Debug.LogWarning($"[ClueAttachmentService] 오디오를 찾을 수 없습니다: Resources/{resourcePath}");

        ClipCache[resourcePath] = clip;
        return clip;
    }

    // 첨부물 옆에 붙는 작은 아이콘. 맵 참조는 그 맵의 아이콘(MapNodeData.iconPath)을 쓰고,
    // 사진은 자기 자신을 축소해 쓰며, 소리는 아이콘이 없다(카드가 텍스트 배지로 대체).
    public static Sprite ResolveIcon(ClueAttachment attachment)
    {
        if (attachment == null) return null;
        switch (attachment.kind)
        {
            case ClueAttachmentKind.Image:
                return LoadSprite(attachment.resourcePath);
            case ClueAttachmentKind.MapRef:
                var node = ResolveMapNode(attachment);
                return node != null ? LoadSprite(node.iconPath) : null;
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
    public static void ClearCache()
    {
        SpriteCache.Clear();
        ClipCache.Clear();
    }
}
