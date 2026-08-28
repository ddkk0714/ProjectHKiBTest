using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.Serialization;

[RequireComponent(typeof(BoxCollider2D))]
public class ChunkData : MonoBehaviour
{
    [Tooltip("Chunk의 활성 범위를 나타내는 2D 경계입니다.")]
    public BoxCollider2D boundary;
    public bool Active { get; private set; }

    [Tooltip("Chunk 활성 상태에 따라 함께 켜고 끌 오브젝트들입니다.")]
    [SerializeField] private Transform[] _transforms;

    [Tooltip("이 Chunk 범위에 연결된 모든 역할별 이벤트 트리거입니다.")]
    [NaughtyAttributes.ReadOnly]
    [SerializeField, FormerlySerializedAs("triggers")]
    private EventTriggerBase[] _triggers;

    private bool _toggle;

    /// <summary>
    /// 에디터 테스트용으로 Chunk 활성 상태를 번갈아 전환합니다.
    /// 실제 런타임 활성화 정책은 ChunkManager가 담당합니다.
    /// </summary>
    [NaughtyAttributes.Button("toggle")]
    public void Toggle()
    {
        _toggle = !_toggle;

        if (_toggle) DeactivateChunk();
        else ActivateChunk();
    }

    /// <summary>
    /// 자식 TilemapCollider2D의 Bounds를 현재 BoxCollider2D 경계에 반영합니다.
    /// 지원하지 않는 구성은 경계를 임의로 변경하지 않고 로그를 남깁니다.
    /// </summary>
    [NaughtyAttributes.Button("autogenerate boundary")]
    public void GenerateBoundary()
    {
        boundary = GetComponent<BoxCollider2D>();
        TilemapCollider2D wall = GetComponentInChildren<TilemapCollider2D>();
        if (wall)
        {
            boundary.size = wall.bounds.size;
            boundary.offset = wall.bounds.center - this.transform.position;//+ wall.transform.position;
        }
        else
        {
            Debug.Log("Failed to generate boundary: TilemapCollider2D is supported only");
        }
    }

    /// <summary>
    /// 경계 안의 Collider와 자식 계층에서 모든 EventTriggerBase를 찾아 Chunk를 연결합니다.
    /// 영역, 상호작용, 공격, 사망 트리거가 같은 Chunk 정책을 사용하게 합니다.
    /// </summary>
    [NaughtyAttributes.Button("assign triggers")]
    public void AssignTriggers()
    {
        if (!boundary)
        {
            Debug.LogError("Chunk Boundary가 연결되지 않았습니다.", this);
            return;
        }

        Collider2D[] colliders = new Collider2D[10000];
        HashSet<EventTriggerBase> triggers = new(GetComponentsInChildren<EventTriggerBase>(true));
        int length = boundary.OverlapCollider(new ContactFilter2D { useTriggers = true }, colliders);
        for (int i = 0; i < length; i++)
        {
            if (!colliders[i]) continue;

            EventTriggerBase trigger = colliders[i].GetComponentInParent<EventTriggerBase>();
            if (trigger) triggers.Add(trigger);
        }

        _triggers = new EventTriggerBase[triggers.Count];
        triggers.CopyTo(_triggers);
        for (int i = 0; i < _triggers.Length; i++)
            _triggers[i].SetChunkData(this);
    }

    /// <summary>
    /// 생성된 Chunk를 ChunkManager에 등록합니다.
    /// GameManager 초기화 이후 Scene이 준비되는 기존 순서를 유지합니다.
    /// </summary>
    public void Awake()
    {
        GameManager.instance.chunkManager.RegisterChunkData(this);
    }

    /// <summary>
    /// Chunk의 초기 상태를 비활성으로 설정합니다.
    /// 하위 클래스는 추가 초기화 후 기반 구현을 호출할 수 있습니다.
    /// </summary>
    public virtual void Initialize()
    {
        DeactivateChunk();
    }

    /// <summary>
    /// Chunk를 비활성 상태로 기록하고 지정된 Transform들을 끕니다.
    /// 트리거는 공통 기반 클래스의 Chunk 판정으로 실행을 중단합니다.
    /// </summary>
    public virtual void DeactivateChunk()
    {
        Active = false;
        for (int i = 0; i < _transforms.Length; i++)
            _transforms[i].gameObject.SetActive(false);
    }

    /// <summary>
    /// Chunk를 활성 상태로 기록하고 지정된 Transform들을 켭니다.
    /// 다시 활성화된 역할별 트리거는 각자 감지 상태를 재구성합니다.
    /// </summary>
    public virtual void ActivateChunk()
    {
        Active = true;
        for (int i = 0; i < _transforms.Length; i++)
            _transforms[i].gameObject.SetActive(true);
    }
}
