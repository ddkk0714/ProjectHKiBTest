using System;
using System.Collections.Generic;
using UnityEngine;

namespace EntityControl
{
    /// <summary>
    /// NavigationAgent의 현재 처리 단계다.
    /// StateMachine에서는 NavigationStatusDecision으로 이 값을 검사해
    /// 도착, 실패, 막힘 이후의 상태 전이를 구성한다.
    /// </summary>
    public enum NavigationStatus
    {
        Idle,       // 목적지가 없고 이동하지 않는 상태
        Planning,   // NavigationManager의 경로 계산 결과를 기다리는 상태
        Following,  // 계산된 경로를 따라 이동하는 상태
        Arrived,    // 목적지 허용 반경 안에 도착한 상태
        Blocked,    // 예약 충돌 또는 끼임으로 진행하지 못하는 상태
        Displaced,  // 넉백·낙하 등으로 현재 경로에서 벗어난 상태
        Failed      // 시작점/목적지 노드 또는 유효 경로를 찾지 못한 상태
    }

    /// <summary>
    /// 두 NavigationNode 사이를 어떤 방식으로 통과해야 하는지 나타낸다.
    /// Agent Profile의 이동 능력에 따라 사용할 수 있는 링크가 달라진다.
    /// </summary>
    public enum NavigationLinkType
    {
        Walk,
        Slope,
        Stair,
        Jump,
        Drop
    }

    [Serializable]
    /// <summary>
    /// 월드의 한 XY 셀에서 발견한 특정 높이의 보행 표면이다.
    /// 같은 Cell에 서로 다른 높이의 노드가 여러 개 존재할 수 있다.
    /// </summary>
    public sealed class NavigationNode
    {
        public int Id;                         // A* 배열과 예약 시스템에서 사용하는 월드 내 고유 ID
        public Vector2Int Cell;                // NavigationWorld 내부의 XY 격자 좌표
        public Vector3 Position;               // XY 중심과 Surface 상단 Z를 합친 논리 위치
        public ZCollider2D Surface;            // 이 노드를 생성한 실제 바닥
        public float Clearance = float.PositiveInfinity; // 바로 위 표면까지의 수직 여유 공간
    }

    [Serializable]
    /// <summary>
    /// Agent가 순서대로 따라갈 경로의 한 지점이다.
    /// LinkType은 이전 지점에서 이 지점으로 진입할 때 사용할 이동 방식이다.
    /// </summary>
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

    /// <summary>
    /// 비동기 경로 요청의 성공 여부, 경로, 실패 원인을 묶어 Callback에 전달한다.
    /// struct이므로 결과 객체 자체는 GC allocation을 만들지 않는다.
    /// Path는 NavigationManager의 재사용 버퍼이므로 Callback 안에서만 읽고,
    /// 이후에도 필요하면 Agent처럼 자신의 List에 즉시 복사해야 한다.
    /// 실패 시 Path는 null일 수 있으므로 반드시 Succeeded를 먼저 확인한다.
    /// </summary>
    public readonly struct NavigationPathResult
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

        /// <summary>유효한 경로 계산 결과를 만든다.</summary>
        public static NavigationPathResult Success(List<NavigationPathPoint> path)
            => new(true, path, null);

        /// <summary>실패 사유를 포함한 결과를 만든다.</summary>
        public static NavigationPathResult Failure(string reason)
            => new(false, null, reason);
    }
}
