using System;
using System.Collections.Generic;
using UnityEngine;

namespace EntityControl
{
    [Serializable]
    /// <summary>
    /// 지정한 Scene 영역을 XY 격자로 샘플링해 Z 높이를 포함한 NavigationNode를 만든다.
    /// NavigationManager의 world 필드에서 범위와 Layer를 설정하고 RebuildWorld로 생성한다.
    /// 런타임 지형이 바뀌면 Manager.RebuildWorld()를 다시 호출해야 한다.
    /// </summary>
    public sealed class NavigationWorld
    {
        [Header("Bake Bounds")]
        // bottomLeft~topRight의 XY 영역만 노드를 생성한다. 큰 영역/작은 cellSize는 Build 비용을 높인다.
        public Vector2 bottomLeft = new(-20f, -20f);
        public Vector2 topRight = new(20f, 20f);
        [Min(0.1f)] public float cellSize = 1f;
        public float minZ = -10f;
        public float maxZ = 10f;

        [Header("Surface Sampling")]
        // floorLayer는 노드 생성 대상, wallLayer는 노드 사이 통과 가능 여부 검사 대상이다.
        public LayerMask floorLayer;
        public LayerMask wallLayer;
        [Range(4, 128)] public int sampleBufferSize = 32;
        [Min(0.001f)] public float duplicateHeightTolerance = 0.05f;
        public bool allowDiagonal = true;
        public bool preventDiagonalCornerCutting = true;

        // _nodes는 ID 순서의 전체 목록, _nodesByCell은 같은 XY의 다층 표면 검색용 인덱스다.
        private readonly List<NavigationNode> _nodes = new(256);
        private readonly Dictionary<Vector2Int, List<NavigationNode>> _nodesByCell = new(256);
        // Rebuild 시 이전 Node/셀 List를 재사용해 동적 월드 갱신의 GC spike를 줄인다.
        private readonly Stack<NavigationNode> _nodePool = new(256);
        private readonly Stack<List<NavigationNode>> _cellListPool = new(256);
        private Collider2D[] _sampleBuffer;
        private readonly RaycastHit2D[] _passageBuffer = new RaycastHit2D[16];
        private static readonly Comparison<NavigationNode> NodeHeightComparison = CompareNodeHeight;

        public IReadOnlyList<NavigationNode> Nodes => _nodes;
        public bool IsBuilt => _nodes.Count > 0;

        public int Width => Mathf.Max(1, Mathf.FloorToInt((topRight.x - bottomLeft.x) / cellSize) + 1);
        public int Height => Mathf.Max(1, Mathf.FloorToInt((topRight.y - bottomLeft.y) / cellSize) + 1);

        /// <summary>
        /// 현재 Physics2D/ZCollider 배치를 샘플링하여 전체 노드와 셀 인덱스를 다시 만든다.
        /// Scene 로딩 후 Collider가 활성화된 시점에 호출해야 하며, 기존 경로/예약은 Manager가 함께 정리한다.
        /// </summary>
        public void Build()
        {
            // 1) 이전 월드 객체를 Pool로 돌려보내고 NonAlloc 샘플 버퍼를 필요한 경우에만 확장한다.
            for (int i = 0; i < _nodes.Count; i++)
            {
                // Pool이 이전 Scene Collider를 불필요하게 붙잡지 않도록 참조를 끊는다.
                _nodes[i].Surface = null;
                _nodePool.Push(_nodes[i]);
            }
            foreach (List<NavigationNode> oldCellNodes in _nodesByCell.Values)
            {
                oldCellNodes.Clear();
                _cellListPool.Push(oldCellNodes);
            }
            _nodes.Clear();
            _nodesByCell.Clear();
            int requiredSampleSize = Mathf.Max(4, sampleBufferSize);
            if (_sampleBuffer == null || _sampleBuffer.Length < requiredSampleSize)
                _sampleBuffer = new Collider2D[requiredSampleSize];

            int id = 0;
            Vector2 sampleSize = Vector2.one * Mathf.Max(0.05f, cellSize * 0.2f);

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    // 2) 셀 중심과 겹치는 Floor Collider를 찾고 ZCollider의 실제 표면 높이를 계산한다.
                    Vector2Int cell = new(x, y);
                    Vector2 point = CellToWorld(cell);
                    int count = Physics2D.OverlapBoxNonAlloc(point, sampleSize, 0f, _sampleBuffer, floorLayer);
                    List<NavigationNode> cellNodes = null;

                    for (int i = 0; i < count; i++)
                    {
                        if (!ZPhysics2D.TryGet(_sampleBuffer[i], out ZCollider2D surface)) continue;

                        float surfaceZ = surface.Zmax(point);
                        if (float.IsNaN(surfaceZ) || float.IsInfinity(surfaceZ)) continue;
                        if (surfaceZ < minZ || surfaceZ > maxZ) continue;

                        // 같은 셀/거의 같은 높이의 중복 Collider는 하나의 노드로 취급한다.
                        bool duplicate = false;
                        if (cellNodes != null)
                        {
                            for (int n = 0; n < cellNodes.Count; n++)
                            {
                                if (Mathf.Abs(cellNodes[n].Position.z - surfaceZ) <= duplicateHeightTolerance)
                                {
                                    duplicate = true;
                                    break;
                                }
                            }
                        }
                        if (duplicate) continue;

                        // 서로 다른 높이라면 같은 XY 셀에도 별도 노드를 추가한다(다리 위/아래 등).
                        NavigationNode node = _nodePool.Count > 0
                            ? _nodePool.Pop()
                            : new NavigationNode();
                        node.Id = id++;
                        node.Cell = cell;
                        node.Position = new Vector3(point.x, point.y, surfaceZ);
                        node.Surface = surface;
                        node.Clearance = float.PositiveInfinity;

                        if (cellNodes == null)
                        {
                            cellNodes = _cellListPool.Count > 0
                                ? _cellListPool.Pop()
                                : new List<NavigationNode>(2);
                            _nodesByCell.Add(cell, cellNodes);
                        }
                        cellNodes.Add(node);
                        _nodes.Add(node);
                    }
                }
            }

            foreach (List<NavigationNode> cellNodes in _nodesByCell.Values)
            {
                // 3) 아래에서 위 순서로 정렬하고 다음 표면의 하단까지를 머리 위 여유 공간으로 기록한다.
                cellNodes.Sort(NodeHeightComparison);
                for (int i = 0; i < cellNodes.Count - 1; i++)
                {
                    NavigationNode current = cellNodes[i];
                    NavigationNode above = cellNodes[i + 1];
                    current.Clearance = Mathf.Max(
                        0f,
                        above.Surface.Zmin(current.Position) - current.Position.z);
                }
            }
        }

        /// <summary>내비게이션 셀 좌표를 XY 월드 중심으로 변환한다.</summary>
        public Vector2 CellToWorld(Vector2Int cell)
            => bottomLeft + new Vector2(cell.x * cellSize, cell.y * cellSize);

        /// <summary>XY 월드 좌표에서 가장 가까운 내비게이션 셀을 구한다.</summary>
        public Vector2Int WorldToCell(Vector2 position)
            => new(
                Mathf.RoundToInt((position.x - bottomLeft.x) / cellSize),
                Mathf.RoundToInt((position.y - bottomLeft.y) / cellSize));

        /// <summary>셀이 설정된 Bake Bounds 안에 있는지 확인한다.</summary>
        public bool IsCellInBounds(Vector2Int cell)
            => cell.x >= 0 && cell.y >= 0 && cell.x < Width && cell.y < Height;

        /// <summary>
        /// 위치 주변 2셀 범위에서 Agent 높이가 들어갈 수 있는 가장 가까운 노드를 찾는다.
        /// 경로 시작점/목적지를 그래프에 투영할 때 사용한다.
        /// </summary>
        public bool TryGetClosestNode(Vector3 position, NavigationAgentProfile profile, out NavigationNode result)
        {
            result = null;
            float best = float.MaxValue;
            Vector2Int centerCell = WorldToCell(position);

            // 가까운 셀부터 확장해 첫 유효 반경 안에서 수평+수직 점수가 가장 낮은 노드를 선택한다.
            for (int radius = 0; radius <= 2 && result == null; radius++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    for (int y = -radius; y <= radius; y++)
                    {
                        Vector2Int cell = centerCell + new Vector2Int(x, y);
                        if (!_nodesByCell.TryGetValue(cell, out List<NavigationNode> nodes)) continue;

                        for (int i = 0; i < nodes.Count; i++)
                        {
                            if (nodes[i].Clearance + duplicateHeightTolerance < profile.height) continue;
                            float vertical = Mathf.Abs(nodes[i].Position.z - position.z);
                            float horizontal = Vector2.Distance(nodes[i].Position, position);
                            float score = horizontal + vertical * 2f;
                            if (score >= best) continue;

                            best = score;
                            result = nodes[i];
                        }
                    }
                }
            }

            return result != null;
        }

        /// <summary>
        /// 중심 반경 안의 임의 노드를 반환한다. Wander/Flee 목적지 후보 선택에 사용한다.
        /// 반환된 노드가 현재 Agent와 연결되어 있다는 보장은 없으며 최종 검증은 A*가 담당한다.
        /// </summary>
        public bool TryGetRandomNode(Vector3 center, float radius, out NavigationNode result)
        {
            result = null;
            if (_nodes.Count == 0) return false;

            int start = UnityEngine.Random.Range(0, _nodes.Count);
            float radiusSq = radius * radius;
            for (int i = 0; i < _nodes.Count; i++)
            {
                NavigationNode candidate = _nodes[(start + i) % _nodes.Count];
                if (((Vector2)(candidate.Position - center)).sqrMagnitude > radiusSq) continue;
                result = candidate;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Agent Profile의 이동 능력과 벽 충돌을 고려해 한 노드에서 이동 가능한 이웃을 채운다.
        /// results는 호출자가 재사용하며 호출 시작 시 Clear된다.
        /// </summary>
        public void GetNeighbours(
            NavigationNode from,
            NavigationAgentProfile profile,
            List<(NavigationNode Node, NavigationLinkType Link, float Cost)> results)
        {
            results.Clear();

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    // 1) 인접 셀과 대각선/코너 통과 설정을 검사한다.
                    if (dx == 0 && dy == 0) continue;
                    bool diagonal = dx != 0 && dy != 0;
                    if (diagonal && !allowDiagonal) continue;

                    Vector2Int nextCell = from.Cell + new Vector2Int(dx, dy);
                    if (!_nodesByCell.TryGetValue(nextCell, out List<NavigationNode> candidates)) continue;
                    if (diagonal && preventDiagonalCornerCutting &&
                        (!HasAnyNode(from.Cell + new Vector2Int(dx, 0)) ||
                         !HasAnyNode(from.Cell + new Vector2Int(0, dy)))) continue;

                    for (int i = 0; i < candidates.Count; i++)
                    {
                        // 2) 높이와 Surface 속성으로 링크 종류를 정한 뒤 벽 Cast로 실제 통로를 검증한다.
                        NavigationNode to = candidates[i];
                        if (!TryClassifyLink(from, to, profile, out NavigationLinkType link, out float extraCost))
                            continue;
                        if (!IsPassageClear(from, to, profile)) continue;

                        float distance = Vector3.Distance(from.Position, to.Position);
                        results.Add((to, link, distance + extraCost));
                    }
                }
            }
        }

        private bool HasAnyNode(Vector2Int cell)
            => _nodesByCell.TryGetValue(cell, out List<NavigationNode> nodes) && nodes.Count > 0;

        /// <summary>셀 내 다층 노드를 아래에서 위 순서로 정렬하는 캐시된 Comparison이다.</summary>
        private static int CompareNodeHeight(NavigationNode a, NavigationNode b)
            => a.Position.z.CompareTo(b.Position.z);

        /// <summary>Agent의 반경/높이를 적용한 Z-aware CircleCast로 두 노드 사이의 벽을 검사한다.</summary>
        private bool IsPassageClear(
            NavigationNode from,
            NavigationNode to,
            NavigationAgentProfile profile)
        {
            if (wallLayer.value == 0) return true;

            Vector2 origin = from.Position;
            Vector2 delta = (Vector2)(to.Position - from.Position);
            float distance = delta.magnitude;
            if (distance <= PhysicsManager.EPSILON) return true;

            float bottom = Mathf.Min(from.Position.z, to.Position.z);
            float top = Mathf.Max(from.Position.z, to.Position.z) + profile.height;
            int hitCount = ZPhysics2D.CircleCastNonAlloc(
                origin,
                profile.radius,
                delta / distance,
                _passageBuffer,
                distance,
                wallLayer,
                bottom + profile.maxStepUp + PhysicsManager.EPSILON,
                top);

            return hitCount == 0;
        }

        /// <summary>
        /// 두 표면의 높이 차이와 경사/계단 속성을 Walk, Slope, Stair, Jump, Drop 중 하나로 분류한다.
        /// Profile이 해당 이동을 허용하지 않으면 false를 반환한다.
        /// </summary>
        private static bool TryClassifyLink(
            NavigationNode from,
            NavigationNode to,
            NavigationAgentProfile profile,
            out NavigationLinkType link,
            out float extraCost)
        {
            float deltaZ = to.Position.z - from.Position.z;
            bool slope = from.Surface != null && (from.Surface.useSlopeDU || from.Surface.useSlopeRL) ||
                         to.Surface != null && (to.Surface.useSlopeDU || to.Surface.useSlopeRL);
            bool stair = from.Surface != null && from.Surface.isStair ||
                         to.Surface != null && to.Surface.isStair;

            // 계단은 하나의 연속된 경사 표면이므로 셀 사이 높이 차이가 maxStepUp보다 클 수 있다.
            // 같은 계단 영역 안의 이동을 Jump보다 먼저 Stair로 확정해 불필요한 ZVelocity를 방지한다.
            // 계단 가장자리에서 계단 밖의 높은 발판으로 이동하는 링크는 여기에 해당하지 않아
            // 아래의 일반 Jump 판정을 그대로 사용할 수 있다.
            if (IsContinuousStairTransition(from, to))
            {
                link = NavigationLinkType.Stair;
                extraCost = profile.stairCost;
                return profile.canUseStairs;
            }

            if (deltaZ <= profile.maxStepUp && deltaZ >= -profile.maxStepDown)
            {
                if (stair)
                {
                    link = NavigationLinkType.Stair;
                    extraCost = profile.stairCost;
                    return profile.canUseStairs;
                }

                if (slope)
                {
                    link = NavigationLinkType.Slope;
                    extraCost = profile.slopeCost;
                    return profile.canUseSlopes;
                }

                link = NavigationLinkType.Walk;
                extraCost = 0f;
                return true;
            }

            if (deltaZ > profile.maxStepUp && profile.canJump && deltaZ <= profile.maxJumpHeight)
            {
                if (Vector2.Distance(from.Position, to.Position) > profile.maxJumpDistance)
                {
                    link = default;
                    extraCost = 0f;
                    return false;
                }
                link = NavigationLinkType.Jump;
                extraCost = profile.jumpCost;
                return true;
            }

            if (deltaZ < -profile.maxStepDown && profile.canDrop && -deltaZ <= profile.maxDropHeight)
            {
                link = NavigationLinkType.Drop;
                extraCost = profile.dropCost;
                return true;
            }

            link = default;
            extraCost = 0f;
            return false;
        }

        /// <summary>
        /// 두 노드 중 계단 표면 하나가 양쪽 XY 위치를 모두 포함하는지 검사한다.
        /// 동일 계단 내부 이동과 계단에서 외부 발판으로 수행하는 실제 점프를 구분하기 위해 사용한다.
        /// </summary>
        private static bool IsContinuousStairTransition(NavigationNode from, NavigationNode to)
        {
            return IsPointInsideStair(from.Surface, to.Position) ||
                   IsPointInsideStair(to.Surface, from.Position);
        }

        /// <summary>Collider2D.ClosestPoint를 사용해 추가 할당 없이 XY 위치의 계단 영역 포함 여부를 구한다.</summary>
        private static bool IsPointInsideStair(ZCollider2D surface, Vector2 point)
        {
            if (surface == null || !surface.isStair) return false;
            return (surface.ClosestPoint(point) - point).sqrMagnitude <= PhysicsManager.EPSILON;
        }
    }
}
