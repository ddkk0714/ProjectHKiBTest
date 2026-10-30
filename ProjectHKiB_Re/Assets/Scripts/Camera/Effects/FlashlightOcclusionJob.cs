using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// 손전등 1D 그림자 맵(각도 칸별로 처음 만나는 그림자 판까지 거리)을 칸마다 병렬로 구한다.
/// FirstFootprintEntry는 WallVisualGrid에도 같은 것이 있다 — 고치면 양쪽 다.
/// </summary>
[BurstCompile]
internal struct FlashlightOcclusionJob : IJobParallelFor
{
    public struct Grid
    {
        public float MinX, MinY, Cell;
        public int Width, Height, Offset; // Offset: Clear 안에서 이 격자가 시작하는 위치
    }

    [ReadOnly] public NativeArray<Grid> Grids;
    [ReadOnly] public NativeArray<byte> Clear;
    public int GridCount;

    public float2 Origin;
    public float Cos, Sin;              // 빛 그림 회전
    public float2 BoundsMin, BoundsMax; // 빛 그림 로컬 범위
    public int Bins;

    // 칸 i: 첫 판 진입 거리(없으면 NoWall).
    public const int Rows = 1;
    public const float NoWall = 1000f;
    [NativeDisableParallelForRestriction, WriteOnly] public NativeArray<float> Distances;

    private const float Nudge = 0.0001f;
    private const float Margin = 0.5f;

    public void Execute(int i)
    {
        float angle = (i + 0.5f) / Bins * math.PI * 2f - math.PI;
        float2 dir = new(math.sin(angle), math.cos(angle));
        float2 local = new(Cos * dir.x + Sin * dir.y, -Sin * dir.x + Cos * dir.y);
        float reach = RayExitAabb(float2.zero, local, BoundsMin, BoundsMax) + Margin;

        float first = NoWall;
        for (int g = 0; g < GridCount; g++)
        {
            float hit = FirstFootprintEntry(Grids[g], dir, 0f, reach);
            if (hit >= 0f) first = math.min(first, hit);
        }
        Distances[i] = first;
    }

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

    // 칸 번호는 시작할 때만 t에서 구한다(WallVisualGrid.Walk와 같은 이유).
    private Walk BeginWalk(in Grid g, float2 dir, float t)
    {
        float2 p = Origin + dir * (t + Nudge);
        var w = new Walk
        {
            X = (int)math.floor((p.x - g.MinX) / g.Cell),
            Y = (int)math.floor((p.y - g.MinY) / g.Cell),
            StepX = dir.x > 0f ? 1 : -1,
            StepY = dir.y > 0f ? 1 : -1,
        };
        float ax = math.abs(dir.x), ay = math.abs(dir.y);
        w.DeltaX = ax > 1e-6f ? g.Cell / ax : float.PositiveInfinity;
        w.DeltaY = ay > 1e-6f ? g.Cell / ay : float.PositiveInfinity;
        w.NextX = ax > 1e-6f ? (g.MinX + (dir.x > 0f ? w.X + 1 : w.X) * g.Cell - Origin.x) / dir.x : float.PositiveInfinity;
        w.NextY = ay > 1e-6f ? (g.MinY + (dir.y > 0f ? w.Y + 1 : w.Y) * g.Cell - Origin.y) / dir.y : float.PositiveInfinity;
        w.NextX = math.max(w.NextX, t);
        w.NextY = math.max(w.NextY, t);
        return w;
    }

    private static bool InGrid(in Grid g, int x, int y) => x >= 0 && y >= 0 && x < g.Width && y < g.Height;
    private static int MaxSteps(in Grid g) => 4 * (g.Width + g.Height) + 16;

    // start에서 이미 바닥면 안이면 그 바닥면을 빠져나간 뒤의 다음 바닥면을 찾는다.
    private float FirstFootprintEntry(in Grid g, float2 dir, float start, float maxDistance)
    {
        float2 gridMin = new(g.MinX, g.MinY), gridMax = new(g.MinX + g.Width * g.Cell, g.MinY + g.Height * g.Cell);
        float tEnter = start, tLeave = maxDistance;
        for (int axis = 0; axis < 2; axis++)
        {
            float o = Origin[axis], d = dir[axis];
            if (math.abs(d) < 1e-6f)
            {
                if (o < gridMin[axis] || o > gridMax[axis]) return -1f;
                continue;
            }
            float t1 = (gridMin[axis] - o) / d, t2 = (gridMax[axis] - o) / d;
            tEnter = math.max(tEnter, math.min(t1, t2));
            tLeave = math.min(tLeave, math.max(t1, t2));
        }
        if (tEnter > tLeave) return -1f;

        float t = tEnter;
        Walk w = BeginWalk(g, dir, t);
        bool leaving = tEnter <= start && InGrid(g, w.X, w.Y) && Clear[g.Offset + w.Y * g.Width + w.X] == 0;
        for (int guard = MaxSteps(g); guard > 0 && t <= tLeave; guard--)
        {
            if (InGrid(g, w.X, w.Y))
            {
                int clear = Clear[g.Offset + w.Y * g.Width + w.X];
                if (leaving)
                {
                    if (clear == 0) { t = w.Step(); continue; }
                    leaving = false;
                }
                if (clear == 0) return t;
                if (clear >= 2)
                {
                    t += (clear - 1) * g.Cell;
                    w = BeginWalk(g, dir, t);
                    continue;
                }
            }
            else if (t > tEnter + g.Cell) return -1f;
            t = w.Step();
        }
        return -1f;
    }

    private static float RayExitAabb(float2 origin, float2 dir, float2 min, float2 max)
    {
        float tNear = 0f, tFar = float.MaxValue;
        for (int axis = 0; axis < 2; axis++)
        {
            float o = origin[axis], d = dir[axis];
            if (math.abs(d) < 1e-5f)
            {
                if (o < min[axis] || o > max[axis]) return 0f;
                continue;
            }
            float t1 = (min[axis] - o) / d, t2 = (max[axis] - o) / d;
            tNear = math.max(tNear, math.min(t1, t2));
            tFar = math.min(tFar, math.max(t1, t2));
        }
        return tFar >= tNear ? tFar : 0f;
    }
}

/// <summary>
/// 광원 주변 벽 지도(DarknessManager.UpdateWallMap)를 한 열씩 아래에서 위로 병렬로 채운다. 칸 좌표는 격자 칸 단위 정수 —
/// 모든 격자의 MinX/MinY가 칸 경계에 맞춰져 있다.
/// </summary>
[BurstCompile]
internal struct FlashlightWallMapJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<FlashlightOcclusionJob.Grid> Grids;
    [ReadOnly] public NativeArray<byte> Clear, Visual;
    public int GridCount, X0, Y0, Side;
    // 0 = 바닥, 1~254 = 벽 앞면(그림자 판 안)이고 값은 그 열의 벽 밑선까지 칸 수(맨 아래 칸이 1), 255 = 판 밖 벽 그림(top 등).
    [NativeDisableParallelForRestriction, WriteOnly] public NativeArray<byte> Pixels;

    public void Execute(int col)
    {
        int x = X0 + col, run = 0;
        for (int row = 0; row < Side; row++)
        {
            int y = Y0 + row;
            bool face = false, visual = false;
            for (int g = 0; g < GridCount && !face; g++)
            {
                FlashlightOcclusionJob.Grid grid = Grids[g];
                int gx = x - (int)math.round(grid.MinX / grid.Cell), gy = y - (int)math.round(grid.MinY / grid.Cell);
                if (gx < 0 || gy < 0 || gx >= grid.Width || gy >= grid.Height) continue;
                int i = grid.Offset + gy * grid.Width + gx;
                face = Clear[i] == 0;
                visual |= Visual[i] != 0;
            }
            run = face ? math.min(run + 1, 254) : 0;
            Pixels[row * Side + col] = face ? (byte)run : visual ? (byte)255 : (byte)0;
        }
    }
}
