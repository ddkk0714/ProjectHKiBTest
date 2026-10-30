using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// 벽 하나의 그림자 판(바닥면)과 벽 그림을 1/16 칸으로 래스터화한 격자. 광선은 정수 DDA로 걷는다.
/// </summary>
internal sealed class WallVisualGrid
{
    // 스프라이트 PPU(16)와 같게 — 타일 경계가 칸 경계에 정확히 걸린다.
    public const float GridCell = 1f / 16f;
    private static int _lastVersion;
    private static readonly List<Vector2[]> PolygonBuffer = new();
    private static readonly List<Rect> RectBuffer = new();

    public float MinX, MinY, Cell;
    public int Width, Height;
    // 칸마다 가장 가까운 바닥면 칸까지의 체비셰프 칸 거리(255에서 자름). 빈 공간을 건너뛰는 데 쓴다.
    public byte[] Clear;
    // 1 = 벽 그림(렌더러) 또는 바닥면. 그림자 판 그림자는 여기에 지지 않는다(벽 앞면 음영은 셰이더가 따로 한다).
    public byte[] Visual;
    public int Version;

    private bool InGrid(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    // 정수 칸 DDA. 칸 번호를 t에서 다시 계산하면 축에 거의 나란한 광선이 부동소수 오차로 같은 칸에 머물러
    // 무한 루프가 된다. 칸 번호는 정수로만 옮긴다.
    private struct Walk
    {
        public int X, Y, StepX, StepY;
        public float NextX, NextY, DeltaX, DeltaY;

        public float Step()
        {
            if (NextX < NextY) { float t = NextX; NextX += DeltaX; X += StepX; return t; }
            float ty = NextY; NextY += DeltaY; Y += StepY; return ty;
        }
    }

    // 바닥면 경계 위의 점은 바깥 칸으로 반올림될 수 있어 광선 방향으로 살짝 밀어 넣고 칸을 고른다.
    private const float Nudge = 0.0001f;
    private Walk BeginWalk(Vector2 origin, Vector2 dir, float t)
    {
        Vector2 p = origin + dir * (t + Nudge);
        var w = new Walk
        {
            X = Mathf.FloorToInt((p.x - MinX) / Cell),
            Y = Mathf.FloorToInt((p.y - MinY) / Cell),
            StepX = dir.x > 0f ? 1 : -1,
            StepY = dir.y > 0f ? 1 : -1,
        };
        float ax = Mathf.Abs(dir.x), ay = Mathf.Abs(dir.y);
        w.DeltaX = ax > 1e-6f ? Cell / ax : float.PositiveInfinity;
        w.DeltaY = ay > 1e-6f ? Cell / ay : float.PositiveInfinity;
        w.NextX = ax > 1e-6f ? (MinX + (dir.x > 0f ? w.X + 1 : w.X) * Cell - origin.x) / dir.x : float.PositiveInfinity;
        w.NextY = ay > 1e-6f ? (MinY + (dir.y > 0f ? w.Y + 1 : w.Y) * Cell - origin.y) / dir.y : float.PositiveInfinity;
        // 오차로 경계가 t보다 뒤에 잡혀도 거리가 줄어들지 않게.
        w.NextX = Mathf.Max(w.NextX, t);
        w.NextY = Mathf.Max(w.NextY, t);
        return w;
    }

    private int MaxSteps => 4 * (Width + Height) + 16;

    public bool InFootprint(Vector2 p)
    {
        int x = Mathf.FloorToInt((p.x - MinX) / Cell), y = Mathf.FloorToInt((p.y - MinY) / Cell);
        return InGrid(x, y) && Clear[y * Width + x] == 0;
    }

    /// <summary>p에서 dir로 나아가 바닥면을 벗어나는 거리. p가 이미 밖이면 0, limit 안에 못 벗어나면 limit.</summary>
    public float FootprintExitDistance(Vector2 p, Vector2 dir, float limit)
    {
        float t = 0f;
        Walk w = BeginWalk(p, dir, 0f);
        for (int guard = MaxSteps; guard > 0 && t < limit; guard--)
        {
            if (!InGrid(w.X, w.Y) || Clear[w.Y * Width + w.X] != 0) return t;
            t = w.Step();
        }
        return limit;
    }

    /// <summary>
    /// 광선이 처음 바닥면에 들어가는 거리. 없으면 -1.
    /// 광원이 이미 바닥면 안이면 그 바닥면을 빠져나간 뒤의 다음 바닥면을 찾는다(무시하면 맵 전체 그림자가 꺼진다).
    /// </summary>
    public float FirstFootprintEntry(Vector2 origin, Vector2 dir, float maxDistance)
    {
        Vector2 gridMin = new(MinX, MinY), gridMax = new(MinX + Width * Cell, MinY + Height * Cell);
        float tEnter = 0f, tLeave = maxDistance;
        for (int axis = 0; axis < 2; axis++)
        {
            float o = origin[axis], d = dir[axis];
            if (Mathf.Abs(d) < 1e-6f)
            {
                if (o < gridMin[axis] || o > gridMax[axis]) return -1f;
                continue;
            }
            float t1 = (gridMin[axis] - o) / d, t2 = (gridMax[axis] - o) / d;
            tEnter = Mathf.Max(tEnter, Mathf.Min(t1, t2));
            tLeave = Mathf.Min(tLeave, Mathf.Max(t1, t2));
        }
        if (tEnter > tLeave) return -1f;

        float t = tEnter;
        Walk w = BeginWalk(origin, dir, t);
        bool leaving = tEnter <= 0f && InGrid(w.X, w.Y) && Clear[w.Y * Width + w.X] == 0;
        for (int guard = MaxSteps; guard > 0 && t <= tLeave; guard--)
        {
            if (InGrid(w.X, w.Y))
            {
                int clear = Clear[w.Y * Width + w.X];
                if (leaving)
                {
                    if (clear == 0) { t = w.Step(); continue; }
                    leaving = false;
                }
                if (clear == 0) return t;
                // 체비셰프 거리 k 칸이면 (k-1)칸 이내 어디에도 바닥면이 없다 — 유클리드 거리로 그만큼 건너뛰어도 안전하다.
                if (clear >= 2)
                {
                    t += (clear - 1) * Cell;
                    w = BeginWalk(origin, dir, t);
                    continue;
                }
            }
            else if (t > tEnter + Cell) return -1f; // 격자를 빠져나갔다(들어오는 첫 칸은 오차로 밖일 수 있어 봐준다)
            t = w.Step();
        }
        return -1f;
    }

    public float SqrDistanceTo(Vector2 p)
    {
        float dx = Mathf.Max(MinX - p.x, 0f, p.x - (MinX + Width * Cell));
        float dy = Mathf.Max(MinY - p.y, 0f, p.y - (MinY + Height * Cell));
        return dx * dx + dy * dy;
    }

    public static WallVisualGrid Build(WallShadowCaster wall)
    {
        PolygonBuffer.Clear();
        RectBuffer.Clear();
        wall.GetFootprintPolygons(PolygonBuffer);
        if (PolygonBuffer.Count == 0) return null;
        wall.GetVisualRects(RectBuffer);

        Vector2 lo = new(float.MaxValue, float.MaxValue), hi = new(float.MinValue, float.MinValue);
        foreach (Vector2[] polygon in PolygonBuffer)
            foreach (Vector2 p in polygon)
            {
                lo = Vector2.Min(lo, p);
                hi = Vector2.Max(hi, p);
            }
        foreach (Rect r in RectBuffer)
        {
            lo = Vector2.Min(lo, r.min);
            hi = Vector2.Max(hi, r.max);
        }

        float cell = GridCell;
        float minX = Mathf.Floor(lo.x / cell) * cell;
        float minY = Mathf.Floor(lo.y / cell) * cell;
        int width = Mathf.CeilToInt((hi.x - minX) / cell) + 1;
        int height = Mathf.CeilToInt((hi.y - minY) / cell) + 1;
        int size = width * height;

        // 바닥면 칠하기·벽 그림 칠하기·빈 칸 거리는 칸 수가 많아(맵 하나에 백만 칸 단위) Burst로 계산한다.
        int pointCount = 0, maxPolygon = 0;
        foreach (Vector2[] polygon in PolygonBuffer)
        {
            pointCount += polygon.Length;
            maxPolygon = Mathf.Max(maxPolygon, polygon.Length);
        }
        var points = new NativeArray<float2>(pointCount, Allocator.TempJob);
        var polygons = new NativeArray<int2>(PolygonBuffer.Count, Allocator.TempJob);
        for (int k = 0, at = 0; k < PolygonBuffer.Count; k++)
        {
            Vector2[] polygon = PolygonBuffer[k];
            polygons[k] = new int2(at, polygon.Length);
            foreach (Vector2 v in polygon) points[at++] = v;
        }
        var rects = new NativeArray<int4>(RectBuffer.Count, Allocator.TempJob);
        for (int k = 0; k < RectBuffer.Count; k++)
        {
            Rect r = RectBuffer[k];
            rects[k] = new int4(
                Mathf.Max(0, Mathf.CeilToInt((r.xMin - minX) / cell - 0.5f)),
                Mathf.Max(0, Mathf.CeilToInt((r.yMin - minY) / cell - 0.5f)),
                Mathf.Min(width - 1, Mathf.CeilToInt((r.xMax - minX) / cell - 0.5f) - 1),
                Mathf.Min(height - 1, Mathf.CeilToInt((r.yMax - minY) / cell - 0.5f) - 1));
        }
        var crossings = new NativeArray<float>(maxPolygon, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        var footprintData = new NativeArray<byte>(size, Allocator.TempJob);
        var visualData = new NativeArray<byte>(size, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        var clearData = new NativeArray<byte>(size, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        new FillJob
        {
            Width = width, Height = height, MinX = minX, MinY = minY, Cell = cell,
            Points = points, Polygons = polygons, Rects = rects, Crossings = crossings,
            Footprint = footprintData, Visual = visualData, Clear = clearData,
        }.Run();
        byte[] visual = visualData.ToArray(), clear = clearData.ToArray();
        points.Dispose();
        polygons.Dispose();
        rects.Dispose();
        crossings.Dispose();
        footprintData.Dispose();
        visualData.Dispose();
        clearData.Dispose();

        return new WallVisualGrid
        {
            MinX = minX, MinY = minY, Cell = cell,
            Width = width, Height = height,
            Clear = clear, Visual = visual,
            Version = ++_lastVersion,
        };
    }

    [BurstCompile]
    private struct FillJob : IJob
    {
        public int Width, Height;
        public float MinX, MinY, Cell;
        [ReadOnly] public NativeArray<float2> Points;
        [ReadOnly] public NativeArray<int2> Polygons; // 판마다 Points 시작 위치, 꼭짓점 수
        [ReadOnly] public NativeArray<int4> Rects;    // 벽 그림 칸 범위 x0, y0, x1, y1(끝 포함)
        public NativeArray<float> Crossings;          // 줄 하나의 교차점(꼭짓점 수만큼)
        public NativeArray<byte> Footprint, Visual, Clear;

        public void Execute()
        {
            // 바닥면: 판마다 칸 중심 기준 스캔라인 채우기(판 하나는 짝홀 규칙, 판끼리는 합집합).
            for (int p = 0; p < Polygons.Length; p++)
            {
                int start = Polygons[p].x, count = Polygons[p].y;
                float pMin = float.MaxValue, pMax = float.MinValue;
                for (int k = 0; k < count; k++)
                {
                    pMin = math.min(pMin, Points[start + k].y);
                    pMax = math.max(pMax, Points[start + k].y);
                }
                int y0 = math.max(0, (int)math.floor((pMin - MinY) / Cell));
                int y1 = math.min(Height - 1, (int)math.ceil((pMax - MinY) / Cell));
                for (int y = y0; y <= y1; y++)
                {
                    float wy = MinY + (y + 0.5f) * Cell;
                    int n = 0;
                    for (int k = 0; k < count; k++)
                    {
                        float2 a = Points[start + k], b = Points[start + (k + 1) % count];
                        if ((a.y <= wy) == (b.y <= wy)) continue;
                        float c = a.x + (wy - a.y) * (b.x - a.x) / (b.y - a.y);
                        int m = n++;
                        for (; m > 0 && Crossings[m - 1] > c; m--) Crossings[m] = Crossings[m - 1];
                        Crossings[m] = c;
                    }
                    for (int k = 0; k + 1 < n; k += 2)
                    {
                        int from = math.max(0, (int)math.ceil((Crossings[k] - MinX) / Cell - 0.5f));
                        int to = math.min(Width - 1, (int)math.ceil((Crossings[k + 1] - MinX) / Cell - 0.5f) - 1);
                        for (int x = from; x <= to; x++) Footprint[y * Width + x] = 1;
                    }
                }
            }

            for (int i = 0; i < Footprint.Length; i++)
            {
                Visual[i] = Footprint[i];
                Clear[i] = Footprint[i] != 0 ? (byte)0 : byte.MaxValue;
            }
            for (int k = 0; k < Rects.Length; k++)
            {
                int4 r = Rects[k];
                for (int y = r.y; y <= r.w; y++)
                    for (int x = r.x; x <= r.z; x++) Visual[y * Width + x] = 1;
            }

            // 체비셰프 거리: 앞쪽 이웃 → 뒤쪽 이웃 두 번 훑기.
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int i = y * Width + x, d = Clear[i];
                    if (d == 0) continue;
                    if (x > 0) d = math.min(d, Clear[i - 1] + 1);
                    if (y > 0)
                    {
                        d = math.min(d, Clear[i - Width] + 1);
                        if (x > 0) d = math.min(d, Clear[i - Width - 1] + 1);
                        if (x < Width - 1) d = math.min(d, Clear[i - Width + 1] + 1);
                    }
                    Clear[i] = (byte)math.min(d, 255);
                }
            for (int y = Height - 1; y >= 0; y--)
                for (int x = Width - 1; x >= 0; x--)
                {
                    int i = y * Width + x, d = Clear[i];
                    if (d == 0) continue;
                    if (x < Width - 1) d = math.min(d, Clear[i + 1] + 1);
                    if (y < Height - 1)
                    {
                        d = math.min(d, Clear[i + Width] + 1);
                        if (x < Width - 1) d = math.min(d, Clear[i + Width + 1] + 1);
                        if (x > 0) d = math.min(d, Clear[i + Width - 1] + 1);
                    }
                    Clear[i] = (byte)math.min(d, 255);
                }
        }
    }
}
