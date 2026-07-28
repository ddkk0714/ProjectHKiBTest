using System;
using System.Collections.Generic;
using UnityEngine;

namespace EntityControl
{
    public class NavigationManager : MonoBehaviour
    {
        private sealed class PathRequest
        {
            public Vector3 Start;
            public Vector3 Destination;
            public NavigationAgentProfile Profile;
            public Action<NavigationPathResult> Callback;
            public int AgentId;
        }

        private struct Reservation
        {
            public int AgentId;
            public int Priority;
            public float ExpiresAt;
        }

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
        [SerializeField, Min(1)] private int maxPathsPerFrame = 2;
        [SerializeField] private bool buildOnAwake = true;
        [SerializeField] private bool drawNodes;
        [SerializeField] private bool drawConnections;

        private readonly Queue<PathRequest> _requests = new();
        private readonly Dictionary<int, Reservation> _reservations = new();
        private readonly List<(NavigationNode Node, NavigationLinkType Link, float Cost)> _neighbours = new(16);

        public NavigationWorld World => world;
        public bool IsReady => world != null && world.IsBuilt;

        private void Awake()
        {
            if (buildOnAwake) RebuildWorld();
        }

        private void Update()
        {
            int count = Mathf.Min(maxPathsPerFrame, _requests.Count);
            for (int i = 0; i < count; i++)
            {
                PathRequest request = _requests.Dequeue();
                request.Callback?.Invoke(FindPath(request.Start, request.Destination, request.Profile, request.AgentId));
            }

            if (_reservations.Count > 0)
                RemoveExpiredReservations();
        }

        [NaughtyAttributes.Button]
        public void RebuildWorld()
        {
            world.Build();
            _requests.Clear();
            _reservations.Clear();
        }

        public void RequestPath(
            Vector3 start,
            Vector3 destination,
            NavigationAgentProfile profile,
            Action<NavigationPathResult> callback,
            int agentId = 0)
        {
            if (profile == null)
            {
                callback?.Invoke(NavigationPathResult.Failure("NavigationAgentProfile is missing."));
                return;
            }

            if (!IsReady)
            {
                callback?.Invoke(NavigationPathResult.Failure("NavigationWorld has not been built."));
                return;
            }

            _requests.Enqueue(new PathRequest
            {
                Start = start,
                Destination = destination,
                Profile = profile,
                Callback = callback,
                AgentId = agentId
            });
        }

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

        public void ReleaseReservations(int agentId)
        {
            if (_reservations.Count == 0) return;

            List<int> remove = null;
            foreach (KeyValuePair<int, Reservation> pair in _reservations)
            {
                if (pair.Value.AgentId != agentId) continue;
                remove ??= new List<int>();
                remove.Add(pair.Key);
            }

            if (remove == null) return;
            for (int i = 0; i < remove.Count; i++)
                _reservations.Remove(remove[i]);
        }

        private NavigationPathResult FindPath(
            Vector3 startPosition,
            Vector3 destination,
            NavigationAgentProfile profile,
            int agentId)
        {
            if (!world.TryGetClosestNode(startPosition, profile, out NavigationNode start))
                return NavigationPathResult.Failure("No navigation node was found near the start position.");
            if (!world.TryGetClosestNode(destination, profile, out NavigationNode goal))
                return NavigationPathResult.Failure("No navigation node was found near the destination.");

            if (start.Id == goal.Id)
            {
                return NavigationPathResult.Success(new List<NavigationPathPoint>
                {
                    new(goal.Id, goal.Position, NavigationLinkType.Walk)
                });
            }

            int nodeCount = world.Nodes.Count;
            float[] costs = new float[nodeCount];
            int[] parents = new int[nodeCount];
            NavigationLinkType[] parentLinks = new NavigationLinkType[nodeCount];
            bool[] closed = new bool[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                costs[i] = float.MaxValue;
                parents[i] = -1;
            }

            List<OpenEntry> open = new(nodeCount);
            costs[start.Id] = 0f;
            HeapPush(open, new OpenEntry(start.Id, Heuristic(start, goal)));

            while (open.Count > 0)
            {
                OpenEntry entry = HeapPop(open);
                if (closed[entry.NodeId]) continue;

                NavigationNode current = world.Nodes[entry.NodeId];
                if (current.Id == goal.Id)
                    return NavigationPathResult.Success(ReconstructPath(start.Id, goal.Id, parents, parentLinks));

                closed[current.Id] = true;
                world.GetNeighbours(current, profile, _neighbours);

                for (int i = 0; i < _neighbours.Count; i++)
                {
                    var neighbour = _neighbours[i];
                    if (closed[neighbour.Node.Id]) continue;

                    float reservationCost = IsReservedByOther(neighbour.Node.Id, agentId)
                        ? profile.reservedNodePathCost
                        : 0f;
                    float nextCost = costs[current.Id] + neighbour.Cost + reservationCost;
                    if (nextCost >= costs[neighbour.Node.Id]) continue;

                    costs[neighbour.Node.Id] = nextCost;
                    parents[neighbour.Node.Id] = current.Id;
                    parentLinks[neighbour.Node.Id] = neighbour.Link;
                    float score = nextCost + Heuristic(neighbour.Node, goal);
                    HeapPush(open, new OpenEntry(neighbour.Node.Id, score));
                }
            }

            return NavigationPathResult.Failure("No traversable path was found.");
        }

        private List<NavigationPathPoint> ReconstructPath(
            int startId,
            int goalId,
            int[] parents,
            NavigationLinkType[] parentLinks)
        {
            List<NavigationPathPoint> path = new();
            int current = goalId;

            while (current != startId && current >= 0)
            {
                NavigationNode node = world.Nodes[current];
                path.Add(new NavigationPathPoint(current, node.Position, parentLinks[current]));
                current = parents[current];
            }

            path.Reverse();
            SimplifyPath(path);
            return path;
        }

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

        private static float Heuristic(NavigationNode a, NavigationNode b)
        {
            Vector3 delta = b.Position - a.Position;
            return new Vector3(delta.x, delta.y, delta.z * 2f).magnitude;
        }

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

        private void RemoveExpiredReservations()
        {
            List<int> remove = null;
            foreach (KeyValuePair<int, Reservation> pair in _reservations)
            {
                if (pair.Value.ExpiresAt > Time.time) continue;
                remove ??= new List<int>();
                remove.Add(pair.Key);
            }

            if (remove == null) return;
            for (int i = 0; i < remove.Count; i++)
                _reservations.Remove(remove[i]);
        }

        private bool IsReservedByOther(int nodeId, int agentId)
        {
            return _reservations.TryGetValue(nodeId, out Reservation reservation) &&
                   reservation.ExpiresAt > Time.time &&
                   reservation.AgentId != agentId;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!drawNodes || world == null || !world.IsBuilt) return;

            Gizmos.color = Color.cyan;
            NavigationAgentProfile previewProfile = ScriptableObject.CreateInstance<NavigationAgentProfile>();
            for (int i = 0; i < world.Nodes.Count; i++)
            {
                NavigationNode node = world.Nodes[i];
                Gizmos.DrawSphere(node.Position, 0.06f);

                if (!drawConnections) continue;
                world.GetNeighbours(node, previewProfile, _neighbours);
                for (int n = 0; n < _neighbours.Count; n++)
                    Gizmos.DrawLine(node.Position, _neighbours[n].Node.Position);
            }
            DestroyImmediate(previewProfile);
        }
#endif
    }
}
