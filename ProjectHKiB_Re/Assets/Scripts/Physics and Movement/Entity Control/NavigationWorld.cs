using System;
using System.Collections.Generic;
using UnityEngine;

namespace EntityControl
{
    [Serializable]
    public sealed class NavigationWorld
    {
        [Header("Bake Bounds")]
        public Vector2 bottomLeft = new(-20f, -20f);
        public Vector2 topRight = new(20f, 20f);
        [Min(0.1f)] public float cellSize = 1f;
        public float minZ = -10f;
        public float maxZ = 10f;

        [Header("Surface Sampling")]
        public LayerMask floorLayer;
        public LayerMask wallLayer;
        [Range(4, 128)] public int sampleBufferSize = 32;
        [Min(0.001f)] public float duplicateHeightTolerance = 0.05f;
        public bool allowDiagonal = true;
        public bool preventDiagonalCornerCutting = true;

        private readonly List<NavigationNode> _nodes = new();
        private readonly Dictionary<Vector2Int, List<NavigationNode>> _nodesByCell = new();
        private Collider2D[] _sampleBuffer;
        private readonly RaycastHit2D[] _passageBuffer = new RaycastHit2D[16];

        public IReadOnlyList<NavigationNode> Nodes => _nodes;
        public bool IsBuilt => _nodes.Count > 0;

        public int Width => Mathf.Max(1, Mathf.FloorToInt((topRight.x - bottomLeft.x) / cellSize) + 1);
        public int Height => Mathf.Max(1, Mathf.FloorToInt((topRight.y - bottomLeft.y) / cellSize) + 1);

        public void Build()
        {
            _nodes.Clear();
            _nodesByCell.Clear();
            _sampleBuffer = new Collider2D[Mathf.Max(4, sampleBufferSize)];

            int id = 0;
            Vector2 sampleSize = Vector2.one * Mathf.Max(0.05f, cellSize * 0.2f);

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
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

                        NavigationNode node = new()
                        {
                            Id = id++,
                            Cell = cell,
                            Position = new Vector3(point.x, point.y, surfaceZ),
                            Surface = surface
                        };

                        if (cellNodes == null)
                        {
                            cellNodes = new List<NavigationNode>(2);
                            _nodesByCell.Add(cell, cellNodes);
                        }
                        cellNodes.Add(node);
                        _nodes.Add(node);
                    }
                }
            }

            foreach (List<NavigationNode> cellNodes in _nodesByCell.Values)
            {
                cellNodes.Sort((a, b) => a.Position.z.CompareTo(b.Position.z));
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

        public Vector2 CellToWorld(Vector2Int cell)
            => bottomLeft + new Vector2(cell.x * cellSize, cell.y * cellSize);

        public Vector2Int WorldToCell(Vector2 position)
            => new(
                Mathf.RoundToInt((position.x - bottomLeft.x) / cellSize),
                Mathf.RoundToInt((position.y - bottomLeft.y) / cellSize));

        public bool IsCellInBounds(Vector2Int cell)
            => cell.x >= 0 && cell.y >= 0 && cell.x < Width && cell.y < Height;

        public bool TryGetClosestNode(Vector3 position, NavigationAgentProfile profile, out NavigationNode result)
        {
            result = null;
            float best = float.MaxValue;
            Vector2Int centerCell = WorldToCell(position);

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
    }
}
