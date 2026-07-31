using System;
using System.Collections.Generic;
using UnityEngine;

namespace EntityControl
{
    /// <summary>
    /// Scene의 NavigationWorld, A* 요청 큐, 군중용 노드 예약을 통합 관리한다.
    /// Scene에 하나를 배치하고 World 범위/Layer를 설정한 뒤 Agent에 참조한다.
    /// Agent가 많아도 한 프레임의 경로 계산 수를 제한해 프레임 급락을 방지한다.
    /// </summary>
    public class NavigationManager : MonoBehaviour
    {
        // 값 타입으로 보관해 요청마다 PathRequest 객체가 GC heap에 생성되지 않게 한다.
        private struct PathRequest
        {
            public Vector3 Start;
            public Vector3 Destination;
            public NavigationAgentProfile Profile;
            public Action<int, NavigationPathResult> Callback;
            public int AgentId;
            public int RequestToken;
        }

        // 특정 노드에 먼저 진입하려는 Agent와 예약 만료 시각을 기록한다.
        private struct Reservation
        {
            public int AgentId;
            public int Priority;
            public float ExpiresAt;
        }

        // A* Open Heap에 저장하는 노드 ID와 f-score.
        private struct OpenEntry
        {
            public int NodeId;
            public float Score;

            public OpenEntry(int nodeId, float score)
            {
                NodeId = nodeId;
                Score = score;
            }
        }

        [SerializeField] private NavigationWorld world = new();
        // 값이 클수록 경로 응답은 빨라지지만 같은 프레임의 CPU 비용이 증가한다.
        [SerializeField, Min(1)] private int maxPathsPerFrame = 2;
        // 동적 지형에서는 자동 Build 후 지형 변경 시 RebuildWorld를 별도로 호출한다.
        [SerializeField] private bool buildOnAwake = true;
        // 선택된 Manager의 Scene Gizmo 표시 옵션. 런타임 경로 계산에는 영향을 주지 않는다.
        [SerializeField] private bool drawNodes;
        [SerializeField] private bool drawConnections;
        // Connection Gizmo 전용 설정. CreateInstance를 매 OnDrawGizmosSelected마다 호출하지 않도록 Asset을 받는다.
        [SerializeField] private NavigationAgentProfile gizmoProfile;

        // 요청/예약/이웃 목록과 A* 작업 메모리는 초기 할당 후 계속 재사용한다.
        private readonly Queue<PathRequest> _requests = new(128);
        private readonly Dictionary<int, Reservation> _reservations = new(256);
        private readonly List<(NavigationNode Node, NavigationLinkType Link, float Cost)> _neighbours = new(32);
        private readonly List<OpenEntry> _open = new(128);
        private readonly List<NavigationPathPoint> _pathResultBuffer = new(32);
        private readonly List<int> _reservationRemovalBuffer = new(32);

        // World 노드 수가 기존 용량보다 커질 때만 다시 할당하는 A* 배열.
        private float[] _costs = Array.Empty<float>();
        private int[] _parents = Array.Empty<int>();
        private NavigationLinkType[] _parentLinks = Array.Empty<NavigationLinkType>();
        private bool[] _closed = Array.Empty<bool>();

        public NavigationWorld World => world;
        public bool IsReady => world != null && world.IsBuilt;

        /// <summary>설정에 따라 Collider가 활성화된 현재 Scene에서 NavigationWorld를 생성한다.</summary>
        private void Awake()
        {
            if (buildOnAwake) RebuildWorld();
        }

        /// <summary>경로 요청 예산을 처리하고 만료된 군중 예약을 정리한다.</summary>
        private void Update()
        {
            // 1) Queue에서 이번 프레임 예산만큼만 A*를 실행한다.
            int count = Mathf.Min(maxPathsPerFrame, _requests.Count);
            for (int i = 0; i < count; i++)
            {
                PathRequest request = _requests.Dequeue();
                NavigationPathResult result =
                    FindPath(request.Start, request.Destination, request.Profile, request.AgentId);
                request.Callback?.Invoke(request.RequestToken, result);
            }

            // 2) Agent가 비활성화되거나 갱신하지 않은 오래된 예약을 제거한다.
            if (_reservations.Count > 0)
                RemoveExpiredReservations();
        }

        [NaughtyAttributes.Button]
        /// <summary>
        /// Inspector 버튼 또는 런타임 코드에서 전체 월드를 다시 샘플링한다.
        /// 기존 요청과 노드 예약이 모두 취소되므로 지형 변경이 끝난 시점에 호출한다.
        /// </summary>
        public void RebuildWorld()
        {
            world.Build();
            // 첫 실제 경로 요청에서 큰 배열을 할당하지 않도록 월드 생성 시 A* 용량을 미리 확보한다.
            EnsureSearchCapacity(world.Nodes.Count);
            _requests.Clear();
            _reservations.Clear();
        }

        /// <summary>
        /// 경로 계산 작업을 Queue에 추가한다. 결과는 이후 Update에서 callback으로 전달된다.
        /// 같은 Agent의 오래된 결과를 무시하는 책임은 NavigationAgentModule의 requestToken 비교가 담당한다.
        /// agentId는 다른 Agent가 예약한 노드에 추가 비용을 부여할 때 사용한다.
        /// callback의 Path는 재사용 버퍼이므로 callback 실행 중 자신의 List에 복사해야 한다.
        /// </summary>
        public void RequestPath(
            Vector3 start,
            Vector3 destination,
            NavigationAgentProfile profile,
            Action<int, NavigationPathResult> callback,
            int agentId = 0,
            int requestToken = 0)
        {
            if (profile == null)
            {
                callback?.Invoke(requestToken, NavigationPathResult.Failure("NavigationAgentProfile is missing."));
                return;
            }

            if (!IsReady)
            {
                callback?.Invoke(requestToken, NavigationPathResult.Failure("NavigationWorld has not been built."));
                return;
            }

            _requests.Enqueue(new PathRequest
            {
                Start = start,
                Destination = destination,
                Profile = profile,
                Callback = callback,
                AgentId = agentId,
                RequestToken = requestToken
            });
        }

        /// <summary>
        /// 다음 경유점 노드를 짧은 시간 예약한다.
        /// 비어 있거나 만료되었거나 자신의 예약이거나 더 높은 priority이면 예약에 성공한다.
        /// Agent는 이동하는 동안 매 프레임 예약을 갱신해야 한다.
        /// </summary>
        public bool TryReserveNode(
            int nodeId,
            int agentId,
            int priority,
            float duration)
        {
            if (!_reservations.TryGetValue(nodeId, out Reservation current) ||
                current.ExpiresAt <= Time.time ||
                current.AgentId == agentId ||
                priority > current.Priority)
            {
                _reservations[nodeId] = new Reservation
                {
                    AgentId = agentId,
                    Priority = priority,
                    ExpiresAt = Time.time + duration
                };
                return true;
            }

            return false;
        }

        /// <summary>
        /// Agent가 보유한 모든 노드 예약을 해제한다.
        /// 경로 변경, 도착, Disable, Destroy 시 호출한다.
        /// </summary>
        public void ReleaseReservations(int agentId)
        {
            if (_reservations.Count == 0) return;

            _reservationRemovalBuffer.Clear();
            foreach (KeyValuePair<int, Reservation> pair in _reservations)
            {
                if (pair.Value.AgentId != agentId) continue;
                _reservationRemovalBuffer.Add(pair.Key);
            }

            for (int i = 0; i < _reservationRemovalBuffer.Count; i++)
                _reservations.Remove(_reservationRemovalBuffer[i]);
        }

        /// <summary>NavigationWorld에서 시작/목적지를 투영하고 A*로 최저 비용 경로를 계산한다.</summary>
        private NavigationPathResult FindPath(
            Vector3 startPosition,
            Vector3 destination,
            NavigationAgentProfile profile,
            int agentId)
        {
            // 1) 임의 월드 좌표를 실제 그래프 노드에 투영한다.
            if (!world.TryGetClosestNode(startPosition, profile, out NavigationNode start))
                return NavigationPathResult.Failure("No navigation node was found near the start position.");
            if (!world.TryGetClosestNode(destination, profile, out NavigationNode goal))
                return NavigationPathResult.Failure("No navigation node was found near the destination.");

            if (start.Id == goal.Id)
            {
                _pathResultBuffer.Clear();
                _pathResultBuffer.Add(new NavigationPathPoint(
                    goal.Id, goal.Position, NavigationLinkType.Walk));
                return NavigationPathResult.Success(_pathResultBuffer);
            }

            // 2) 노드 ID를 인덱스로 사용하는 재사용 A* 작업 배열을 초기화한다.
            int nodeCount = world.Nodes.Count;
            EnsureSearchCapacity(nodeCount);
            _open.Clear();
            for (int i = 0; i < nodeCount; i++)
            {
                _costs[i] = float.MaxValue;
                _parents[i] = -1;
                _parentLinks[i] = default;
                _closed[i] = false;
            }

            _costs[start.Id] = 0f;
            HeapPush(_open, new OpenEntry(start.Id, Heuristic(start, goal)));

            // 3) f-score가 가장 낮은 노드부터 확장한다.
            while (_open.Count > 0)
            {
                OpenEntry entry = HeapPop(_open);
                if (_closed[entry.NodeId]) continue;

                NavigationNode current = world.Nodes[entry.NodeId];
                if (current.Id == goal.Id)
                {
                    ReconstructPath(start.Id, goal.Id);
                    return NavigationPathResult.Success(_pathResultBuffer);
                }

                _closed[current.Id] = true;
                // Profile로 통과 가능한 이웃만 얻고 예약된 노드에는 우회 유도 비용을 더한다.
                world.GetNeighbours(current, profile, _neighbours);

                for (int i = 0; i < _neighbours.Count; i++)
                {
                    var neighbour = _neighbours[i];
                    if (_closed[neighbour.Node.Id]) continue;

                    float reservationCost = IsReservedByOther(neighbour.Node.Id, agentId)
                        ? profile.reservedNodePathCost
                        : 0f;
                    float nextCost = _costs[current.Id] + neighbour.Cost + reservationCost;
                    if (nextCost >= _costs[neighbour.Node.Id]) continue;

                    _costs[neighbour.Node.Id] = nextCost;
                    _parents[neighbour.Node.Id] = current.Id;
                    _parentLinks[neighbour.Node.Id] = neighbour.Link;
                    float score = nextCost + Heuristic(neighbour.Node, goal);
                    HeapPush(_open, new OpenEntry(neighbour.Node.Id, score));
                }
            }

            return NavigationPathResult.Failure("No traversable path was found.");
        }

        /// <summary>goal에서 parent를 역추적해 시작→목적지 순서의 경로를 복원한다.</summary>
        private void ReconstructPath(int startId, int goalId)
        {
            _pathResultBuffer.Clear();
            int current = goalId;

            while (current != startId && current >= 0)
            {
                NavigationNode node = world.Nodes[current];
                _pathResultBuffer.Add(
                    new NavigationPathPoint(current, node.Position, _parentLinks[current]));
                current = _parents[current];
            }

            _pathResultBuffer.Reverse();
            SimplifyPath(_pathResultBuffer);
        }

        /// <summary>
        /// A* 배열이 현재 World 노드 수를 담지 못할 때만 확장한다.
        /// 같은 크기의 World에서 반복 경로 요청은 배열 GC allocation을 발생시키지 않는다.
        /// </summary>
        private void EnsureSearchCapacity(int nodeCount)
        {
            if (_costs.Length >= nodeCount) return;

            int capacity = Mathf.NextPowerOfTwo(Mathf.Max(4, nodeCount));
            _costs = new float[capacity];
            _parents = new int[capacity];
            _parentLinks = new NavigationLinkType[capacity];
            _closed = new bool[capacity];
            if (_open.Capacity < capacity) _open.Capacity = capacity;
            if (_pathResultBuffer.Capacity < capacity) _pathResultBuffer.Capacity = capacity;
        }

        /// <summary>
        /// 완전히 같은 방향으로 이어지는 Walk 중간점을 제거한다.
        /// Jump/Drop/Slope/Stair 지점은 이동 의미가 있으므로 제거하지 않는다.
        /// </summary>
        private static void SimplifyPath(List<NavigationPathPoint> path)
        {
            for (int i = path.Count - 2; i > 0; i--)
            {
                NavigationPathPoint previous = path[i - 1];
                NavigationPathPoint current = path[i];
                NavigationPathPoint next = path[i + 1];
                if (previous.LinkType != NavigationLinkType.Walk ||
                    current.LinkType != NavigationLinkType.Walk ||
                    next.LinkType != NavigationLinkType.Walk) continue;

                Vector2 a = ((Vector2)(current.Position - previous.Position)).normalized;
                Vector2 b = ((Vector2)(next.Position - current.Position)).normalized;
                if (Vector2.Dot(a, b) > 0.999f)
                    path.RemoveAt(i);
            }
        }

        /// <summary>Z 차이에 가중치를 둔 A* 휴리스틱 거리다.</summary>
        private static float Heuristic(NavigationNode a, NavigationNode b)
        {
            Vector3 delta = b.Position - a.Position;
            return new Vector3(delta.x, delta.y, delta.z * 2f).magnitude;
        }

        /// <summary>Open List 최소 힙에 항목을 추가한다.</summary>
        private static void HeapPush(List<OpenEntry> heap, OpenEntry item)
        {
            heap.Add(item);
            int index = heap.Count - 1;
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (heap[parent].Score <= heap[index].Score) break;
                (heap[parent], heap[index]) = (heap[index], heap[parent]);
                index = parent;
            }
        }

        /// <summary>Open List에서 score가 가장 낮은 항목을 제거해 반환한다.</summary>
        private static OpenEntry HeapPop(List<OpenEntry> heap)
        {
            OpenEntry result = heap[0];
            int last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);

            int index = 0;
            while (index < heap.Count)
            {
                int left = index * 2 + 1;
                int right = left + 1;
                if (left >= heap.Count) break;
                int smallest = right < heap.Count && heap[right].Score < heap[left].Score ? right : left;
                if (heap[index].Score <= heap[smallest].Score) break;
                (heap[index], heap[smallest]) = (heap[smallest], heap[index]);
                index = smallest;
            }

            return result;
        }

        /// <summary>현재 시각을 지난 노드 예약을 Dictionary에서 제거한다.</summary>
        private void RemoveExpiredReservations()
        {
            _reservationRemovalBuffer.Clear();
            foreach (KeyValuePair<int, Reservation> pair in _reservations)
            {
                if (pair.Value.ExpiresAt > Time.time) continue;
                _reservationRemovalBuffer.Add(pair.Key);
            }

            for (int i = 0; i < _reservationRemovalBuffer.Count; i++)
                _reservations.Remove(_reservationRemovalBuffer[i]);
        }

        /// <summary>A* 비용 계산을 위해 유효한 타 Agent 예약인지 검사한다.</summary>
        private bool IsReservedByOther(int nodeId, int agentId)
        {
            return _reservations.TryGetValue(nodeId, out Reservation reservation) &&
                   reservation.ExpiresAt > Time.time &&
                   reservation.AgentId != agentId;
        }

#if UNITY_EDITOR
        /// <summary>선택된 Manager의 생성 노드와 연결을 Scene View에 표시한다.</summary>
        private void OnDrawGizmosSelected()
        {
            if (!drawNodes || world == null || !world.IsBuilt) return;

            Gizmos.color = Color.cyan;
            for (int i = 0; i < world.Nodes.Count; i++)
            {
                NavigationNode node = world.Nodes[i];
                Gizmos.DrawSphere(node.Position, 0.06f);

                if (!drawConnections || gizmoProfile == null) continue;
                world.GetNeighbours(node, gizmoProfile, _neighbours);
                for (int n = 0; n < _neighbours.Count; n++)
                    Gizmos.DrawLine(node.Position, _neighbours[n].Node.Position);
            }
        }
#endif
    }
}
