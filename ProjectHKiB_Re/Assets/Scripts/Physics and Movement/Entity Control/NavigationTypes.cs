using System;
using System.Collections.Generic;
using UnityEngine;

namespace EntityControl
{
    public enum NavigationStatus
    {
        Idle,
        Planning,
        Following,
        Arrived,
        Blocked,
        Displaced,
        Failed
    }

    public enum NavigationLinkType
    {
        Walk,
        Slope,
        Stair,
        Jump,
        Drop
    }

    [Serializable]
    public sealed class NavigationNode
    {
        public int Id;
        public Vector2Int Cell;
        public Vector3 Position;
        public ZCollider2D Surface;
        public float Clearance = float.PositiveInfinity;
    }

    [Serializable]
    public struct NavigationPathPoint
    {
        public int NodeId;
        public Vector3 Position;
        public NavigationLinkType LinkType;

        public NavigationPathPoint(int nodeId, Vector3 position, NavigationLinkType linkType)
        {
            NodeId = nodeId;
            Position = position;
            LinkType = linkType;
        }
    }

    public sealed class NavigationPathResult
    {
        public readonly bool Succeeded;
        public readonly List<NavigationPathPoint> Path;
        public readonly string FailureReason;

        private NavigationPathResult(bool succeeded, List<NavigationPathPoint> path, string failureReason)
        {
            Succeeded = succeeded;
            Path = path;
            FailureReason = failureReason;
        }

        public static NavigationPathResult Success(List<NavigationPathPoint> path)
            => new(true, path, null);

        public static NavigationPathResult Failure(string reason)
            => new(false, null, reason);
    }
}
