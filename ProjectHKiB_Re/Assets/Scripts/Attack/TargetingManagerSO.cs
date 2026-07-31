using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "TargetingManager", menuName = "Scriptable Objects/Manager/TargetingManager", order = 3)]
public class TargetingManagerSO : ScriptableObject
{
    private const int MAXTARGETCOUNT = 10;
    private readonly float maxRotation = 20;
    private readonly float maxMaintainRotation = 30;
    private readonly float maintainRadiusCoeff = 4;
    private class TargetInfo
    {
        public Transform target;
        public float priority;
        public TargetInfo(Transform target, float priority)
        {
            this.target = target;
            this.priority = priority;
        }
    }

    public bool CheckCurrentTargetDistance(Transform currentTarget, Vector3 centerPos, float radius)
    {
        return Vector3.Distance(currentTarget.position, centerPos) < radius;
    }

    public Transform PositianalTarget(Vector3 centerPos, float radius, LayerMask[] targetLayers, Transform currentTarget)
    {
        if (currentTarget && Vector3.Distance(currentTarget.transform.position, centerPos) < radius) return currentTarget;
        LayerMask targets = 0;
        for (int i = 0; i < targetLayers.Length; i++)
            targets |= targetLayers[i];

        Collider2D[] col = new Collider2D[MAXTARGETCOUNT];
        int colLength = Physics2D.OverlapCircleNonAlloc(centerPos, radius, col, targets);
        if (colLength.Equals(0))
            return null;
        col = new ArraySegment<Collider2D>(col, 0, colLength).ToArray();
        Array.Sort(col, (a, b) => Vector3.Distance(a.transform.position, centerPos).CompareTo(Vector3.Distance(b.transform.position, centerPos)));

        //Debug.DrawLine(centerPos, col[0].transform.position, Color.yellow, 0.4f);
        return col[0].transform;
    }

    private readonly List<Transform> _targets = new(32);
    public Transform DirectionalTarget(Vector3 centerPos, float radius, Vector2 lookingDir, LayerMask[] targetLayers, Transform currentTarget)
    {
        if (currentTarget
        && Vector3.Angle(currentTarget.position - centerPos, lookingDir) < maxMaintainRotation
        && radius * maintainRadiusCoeff > Vector3.Distance(currentTarget.position, centerPos))
        {
#if UNITY_EDITOR
            Debug.DrawLine(centerPos, centerPos + Quaternion.Euler(0, 0, maxMaintainRotation) * (maintainRadiusCoeff * radius * (Vector3)lookingDir.normalized), Color.red, 0.4f);
            Debug.DrawLine(centerPos, centerPos + Quaternion.Euler(0, 0, -maxMaintainRotation) * (maintainRadiusCoeff * radius * (Vector3)lookingDir.normalized), Color.red, 0.4f);
            Debug.DrawLine(centerPos, currentTarget.position, Color.cyan, 0.4f);
#endif
            return currentTarget;
        }

        Debug.DrawLine(centerPos, centerPos + Quaternion.Euler(0, 0, maxRotation) * ((Vector3)lookingDir.normalized * radius), Color.red, 0.4f);
        Debug.DrawLine(centerPos, centerPos + Quaternion.Euler(0, 0, -maxRotation) * ((Vector3)lookingDir.normalized * radius), Color.red, 0.4f);

        _targets.Clear();
        Collider2D[] col = new Collider2D[MAXTARGETCOUNT];
        int i;
        int j;
        for (i = 0; i < targetLayers.Length; i++)
        {
            int colLength = Physics2D.OverlapCircleNonAlloc(centerPos, radius, col, targetLayers[i]);
            if (colLength < 1) continue;
            if (colLength.Equals(1))
            {
                if (Vector3.Angle(col[0].transform.position - centerPos, lookingDir) < maxRotation)
                    _targets.Add(col[0].transform);
                continue;
            }

            float GetPriority(Vector3 targetPos)
                => Vector3.Angle(targetPos - centerPos, lookingDir) / maxRotation
                * Vector3.Distance(targetPos, centerPos) / radius;

            col = new ArraySegment<Collider2D>(col, 0, colLength).ToArray();
            Array.Sort(col, (a, b) => GetPriority(a.transform.position).CompareTo(GetPriority(b.transform.position)));
            for (j = 0; j < colLength; j++)
            {
                if (Vector3.Angle(col[j].transform.position - centerPos, lookingDir) < maxRotation)
                {
                    Debug.DrawLine(centerPos, col[j].transform.position, Color.red - Color.red * 0.2f * i, 0.4f);
                    _targets.Add(col[j].transform);
                    break;
                }
            }
        }

        if (_targets.Count < 1)
            return null;
        Debug.DrawLine(centerPos, _targets[0].position, Color.green, 0.4f);
        return _targets[0];
    }
}