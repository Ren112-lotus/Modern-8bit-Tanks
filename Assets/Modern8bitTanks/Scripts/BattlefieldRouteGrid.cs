using System.Collections.Generic;
using UnityEngine;

namespace Modern8bitTanks
{
    internal static class BattlefieldRouteGrid
    {
        internal const int RouteCell = 16;
        internal const int RouteCols = BattlefieldLayout.Width / RouteCell;
        internal const int RouteRows = BattlefieldLayout.Height / RouteCell;
        internal const int RouteCount = RouteCols * RouteRows;

        private static readonly int[] DirX4 = { 1, -1, 0, 0 };
        private static readonly int[] DirY4 = { 0, 0, 1, -1 };
        private static readonly int[] DirX8 = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] DirY8 = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private static readonly float[] DirCost8 = { 1f, 1f, 1f, 1f, 1.414f, 1.414f, 1.414f, 1.414f };

        internal static Vector2 RouteCellCenter(int gx, int gy) =>
            new((gx + 0.5f) * RouteCell, (gy + 0.5f) * RouteCell);

        internal static int WorldToRouteCell(Vector2 pos)
        {
            int gx = Mathf.Clamp(Mathf.FloorToInt(pos.x / RouteCell), 1, RouteCols - 2);
            int gy = Mathf.Clamp(Mathf.FloorToInt(pos.y / RouteCell), 1, RouteRows - 2);
            return gy * RouteCols + gx;
        }

        internal static int MirrorRouteCell(int cell)
        {
            int gx = cell % RouteCols;
            int gy = cell / RouteCols;
            return (RouteRows - 1 - gy) * RouteCols + (RouteCols - 1 - gx);
        }

        internal static void MarkNearRoad(int cell, bool[] nearRoadMask, int radius)
        {
            int cx = cell % RouteCols;
            int cy = cell / RouteCols;
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = cx + dx;
                int ny = cy + dy;
                if (nx > 0 && nx < RouteCols - 1 && ny > 0 && ny < RouteRows - 1)
                    nearRoadMask[ny * RouteCols + nx] = true;
            }
        }

        internal static List<int> RunGridAStar(int start, int goal, bool allowDiagonal, float turnPenalty,
            bool[] blocked, float[] baseCost, bool[] roadMask, bool[] nearRoadMask,
            bool avoidExistingRoad = false, float nearPenalty = 5.8f, float shareCost = 0.35f, float avoidRoadPenalty = 8.0f,
            Vector2? freeA = null, Vector2? freeB = null, float freeRadius = 96f)
        {
            if (start == goal) return new List<int> { start };

            int dirCount = allowDiagonal ? 8 : 4;
            int[] dx = allowDiagonal ? DirX8 : DirX4;
            int[] dy = allowDiagonal ? DirY8 : DirY4;

            var gScore = new float[RouteCount];
            var cameFrom = new int[RouteCount];
            var cameDir = new sbyte[RouteCount];
            var closed = new bool[RouteCount];
            System.Array.Fill(gScore, float.PositiveInfinity);
            System.Array.Fill(cameFrom, -1);
            System.Array.Fill(cameDir, (sbyte)-1);

            int goalX = goal % RouteCols;
            int goalY = goal / RouteCols;

            // Binary min-heap ordered by estimated route cost.
            var heapNode = new int[RouteCount * 2];
            var heapPrio = new float[RouteCount * 2];
            int heapSize = 0;

            void Push(int node, float prio)
            {
                if (heapSize >= heapNode.Length) return;
                int i = heapSize++;
                heapNode[i] = node;
                heapPrio[i] = prio;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (heapPrio[p] <= heapPrio[i]) break;
                    (heapNode[p], heapNode[i]) = (heapNode[i], heapNode[p]);
                    (heapPrio[p], heapPrio[i]) = (heapPrio[i], heapPrio[p]);
                    i = p;
                }
            }

            int Pop()
            {
                int top = heapNode[0];
                int last = --heapSize;
                if (last > 0)
                {
                    heapNode[0] = heapNode[last];
                    heapPrio[0] = heapPrio[last];
                    int i = 0;
                    while (true)
                    {
                        int l = (i << 1) + 1;
                        int r = l + 1;
                        int best = i;
                        if (l < heapSize && heapPrio[l] < heapPrio[best]) best = l;
                        if (r < heapSize && heapPrio[r] < heapPrio[best]) best = r;
                        if (best == i) break;
                        (heapNode[i], heapNode[best]) = (heapNode[best], heapNode[i]);
                        (heapPrio[i], heapPrio[best]) = (heapPrio[best], heapPrio[i]);
                        i = best;
                    }
                }
                return top;
            }

            gScore[start] = 0f;
            Push(start, Heuristic(start % RouteCols, start / RouteCols, goalX, goalY, allowDiagonal));

            while (heapSize > 0)
            {
                int cur = Pop();
                if (closed[cur]) continue;
                if (cur == goal) break;
                closed[cur] = true;

                int cx = cur % RouteCols;
                int cy = cur / RouteCols;
                sbyte prevDir = cameDir[cur];
                float curG = gScore[cur];

                for (int d = 0; d < dirCount; d++)
                {
                    int nx = cx + dx[d];
                    int ny = cy + dy[d];
                    if (nx <= 0 || nx >= RouteCols - 1 || ny <= 0 || ny >= RouteRows - 1) continue;
                    int next = ny * RouteCols + nx;
                    if (closed[next] || blocked[next]) continue;

                    // Prevent diagonal corner-cutting through solid cover
                    if (d >= 4 && (blocked[cy * RouteCols + nx] || blocked[ny * RouteCols + cx]))
                        continue;

                    float step = allowDiagonal ? DirCost8[d] : 1f;
                    bool nearEnd = false;
                    if (freeA.HasValue || freeB.HasValue)
                    {
                        float frSq = freeRadius * freeRadius;
                        Vector2 np = RouteCellCenter(nx, ny);
                        if ((freeA.HasValue && (np - freeA.Value).sqrMagnitude < frSq) ||
                            (freeB.HasValue && (np - freeB.Value).sqrMagnitude < frSq))
                            nearEnd = true;
                    }
                    float terrainMult;
                    if (!nearEnd && roadMask != null && roadMask[next])
                        terrainMult = avoidExistingRoad ? baseCost[next] + avoidRoadPenalty : shareCost;
                    else if (!nearEnd && nearRoadMask != null && nearRoadMask[next])
                        terrainMult = baseCost[next] + nearPenalty;
                    else
                        terrainMult = baseCost[next];
                    float turn = (prevDir >= 0 && prevDir != d) ? turnPenalty : 0f;
                    float tentative = curG + step * terrainMult + turn;

                    if (tentative < gScore[next])
                    {
                        gScore[next] = tentative;
                        cameFrom[next] = cur;
                        cameDir[next] = (sbyte)d;
                        float h = Heuristic(nx, ny, goalX, goalY, allowDiagonal) * 0.85f;
                        Push(next, tentative + h);
                    }
                }
            }

            if (cameFrom[goal] < 0) return null;
            var path = new List<int>();
            for (int c = goal; c >= 0; c = cameFrom[c])
                path.Add(c);
            path.Reverse();
            return path;
        }

        private static float Heuristic(int x, int y, int gx, int gy, bool allowDiagonal)
        {
            int dx = Mathf.Abs(x - gx);
            int dy = Mathf.Abs(y - gy);
            return allowDiagonal ? (dx + dy) + (1.414f - 2f) * Mathf.Min(dx, dy) : dx + dy;
        }

        internal static int FindNearestOpenCell(Vector2 pos, bool[] blocked)
        {
            int start = WorldToRouteCell(pos);
            if (!blocked[start]) return start;
            int sx = start % RouteCols;
            int sy = start / RouteCols;
            for (int r = 1; r <= 8; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                    int nx = sx + dx;
                    int ny = sy + dy;
                    if (nx <= 1 || nx >= RouteCols - 2 || ny <= 1 || ny >= RouteRows - 2) continue;
                    int idx = ny * RouteCols + nx;
                    if (!blocked[idx]) return idx;
                }
            }
            return -1;
        }
    }
}
