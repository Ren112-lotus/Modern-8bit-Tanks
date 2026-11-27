using System.Collections.Generic;
using UnityEngine;
using static Modern8bitTanks.BattlefieldRouteGrid;

namespace Modern8bitTanks
{
    internal sealed partial class BattlefieldLayout
    {
        private readonly List<RoadSegment> _roads = new();
        private readonly List<RoadIntersection> _intersections = new();
        private readonly List<RoadSegment> _waterStreams = new();
        private int _nextPolylineId;

        private void BuildObjectAwareRoadNetwork(BattleMap map, float reflection,
            List<(Vector2 Center, Vector2 GroundSize, ZoneArchetype Archetype)> artificialAnchors,
            List<DistrictGround> corridorHints, System.Random random)
        {
            bool city = map == BattleMap.City;
            float primaryHalfW = city ? 28f : (map == BattleMap.Desert ? 25f : 23f);
            float secondaryHalfW = city ? 21f : 17f;
            float turnPenalty = city ? 4.2f : 0.80f;

            // Build obstacle & terrain cost grids reflecting ALL placed cover, ridges, parks & groves.
            var strictBlocked = new bool[RouteCount];
            var relaxedBlocked = new bool[RouteCount];
            var baseCost = new float[RouteCount];
            var roadMask = new bool[RouteCount];
            var nearRoadMask = new bool[RouteCount];
            float strictClear = city ? 20f : 17f;
            float relaxedClear = 11f;
            float borderKeep = BattleSceneController.BorderPadPx + 28f;

            for (int gy = 0; gy < RouteRows; gy++)
            {
                for (int gx = 0; gx < RouteCols; gx++)
                {
                    int idx = gy * RouteCols + gx;
                    Vector2 p = RouteCellCenter(gx, gy);

                    if (p.x < borderKeep || p.x > Width - borderKeep || p.y < borderKeep || p.y > Height - borderKeep)
                    {
                        strictBlocked[idx] = true;
                        relaxedBlocked[idx] = true;
                        continue;
                    }

                    bool hitStrict = false;
                    bool hitRelaxed = false;
                    bool nearCover = false;
                    for (int c = 0; c < _cover.Count; c++)
                    {
                        var cov = _cover[c];
                        if (cov.Contains(p, relaxedClear))
                        {
                            hitRelaxed = true;
                            hitStrict = true;
                            break;
                        }
                        if (!hitStrict && cov.Contains(p, strictClear))
                            hitStrict = true;
                        else if (!nearCover && cov.Contains(p, strictClear + 20f))
                            nearCover = true;
                    }

                    strictBlocked[idx] = hitStrict;
                    relaxedBlocked[idx] = hitRelaxed;

                    float cost = 1.0f;
                    if (nearCover) cost += 3.6f;

                    // Discourage roads from cutting directly through parks or deep natural woodlands.
                    if (IsInsideAnyDistrict(_parkGround, p) || IsInsideAnyDistrict(_groveGround, p))
                        cost += 2.6f;

                    // Gently prefer natural open alley corridors between plan districts.
                    if (IsInsideAnyDistrict(corridorHints, p))
                        cost *= 0.80f;

                    // In natural biomes, add subtle Perlin meander cost so routes bend organically.
                    if (!city)
                    {
                        float n = Mathf.PerlinNoise(p.x * 0.0045f + (_seed & 255) * 0.13f, p.y * 0.0045f + 3.7f);
                        cost += n * 0.55f;
                    }

                    baseCost[idx] = cost;
                }
            }

            // =========================================================================
            // FREE-FORM OBJECT-DRIVEN SYMMETRIC ROAD GRAPH (NO FIXED 3-LANE TEMPLATE!)
            // Nodes: 0 = WestBase, 1 = EastBase, 2 = ControlPoint,
            //        3..3+k-1 = real West artificial facilities, 3+k..3+2k-1 = East mirrors
            // =========================================================================
            var westFacs = new List<Vector2>();
            foreach (var site in artificialAnchors)
            {
                Vector2 access = FindFacilityAccessPoint(site.Center, site.GroundSize, strictBlocked, relaxedBlocked);
                if (Vector2.Distance(access, _westBase) < 145f || Vector2.Distance(access, ControlPoint) < 135f)
                    continue;
                bool tooClose = false;
                for (int i = 0; i < westFacs.Count; i++)
                    if (Vector2.Distance(access, westFacs[i]) < 155f) { tooClose = true; break; }
                if (!tooClose) westFacs.Add(access);
            }

            int k = westFacs.Count;
            var nodes = new List<Vector2>(3 + k * 2) { _westBase, Mirror(_westBase), ControlPoint };
            nodes.AddRange(westFacs);
            for (int i = 0; i < k; i++) nodes.Add(Mirror(westFacs[i]));
            int nCnt = nodes.Count;

            int MirrorNode(int u) => u == 0 ? 1 : (u == 1 ? 0 : (u == 2 ? 2 : (u < 3 + k ? u + k : u - k)));

            var adj = new HashSet<int>[nCnt];
            var dsu = new int[nCnt];
            for (int i = 0; i < nCnt; i++) { adj[i] = new HashSet<int>(); dsu[i] = i; }

            int Find(int x)
            {
                while (dsu[x] != x) { dsu[x] = dsu[dsu[x]]; x = dsu[x]; }
                return x;
            }
            void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra != rb) dsu[ra] = rb;
            }
            int GraphDist(int src, int dst)
            {
                if (src == dst) return 0;
                var q = new Queue<(int node, int dist)>();
                var vis = new bool[nCnt];
                vis[src] = true;
                q.Enqueue((src, 0));
                while (q.Count > 0)
                {
                    var (u, d) = q.Dequeue();
                    foreach (int w in adj[u])
                    {
                        if (w == dst) return d + 1;
                        if (!vis[w]) { vis[w] = true; q.Enqueue((w, d + 1)); }
                    }
                }
                return 99;
            }

            bool CommitSymmetricChain(int[] seq, bool isPrimary, float[] costGrid, bool avoidExisting, float maxNearOverlap)
            {
                var combined = new List<int>();
                for (int i = 0; i < seq.Length - 1; i++)
                {
                    int u = seq[i], v = seq[i + 1];
                    if (u == v || v == MirrorNode(u)) return false;
                    var leg = RouteCellsBetween(nodes[u], nodes[v], !city, turnPenalty,
                        strictBlocked, relaxedBlocked, costGrid, roadMask, nearRoadMask,
                        avoidExistingRoad: avoidExisting, nearPenalty: 5.8f, shareCost: 0.35f, avoidRoadPenalty: 8.0f);
                    if (leg == null || leg.Count < 2) return false;
                    int startIdx = (combined.Count > 0 && leg[0] == combined[^1]) ? 1 : 0;
                    for (int c = startIdx; c < leg.Count; c++) combined.Add(leg[c]);
                }

                combined = CollapseBacktrackLoops(combined, lookBack: 14);
                if (combined.Count < 2) return false;

                // Reject redundant loop routes that hug or retrace an already-routed road!
                if (maxNearOverlap < 0.99f && combined.Count > 10)
                {
                    int bad = 0;
                    int midCount = combined.Count - 8;
                    for (int i = 4; i < combined.Count - 4; i++)
                    {
                        int c = combined[i];
                        if (roadMask[c] || nearRoadMask[c]) bad++;
                    }
                    if (bad / (float)Mathf.Max(1, midCount) > maxNearOverlap)
                        return false;
                }

                var worldPts = new List<Vector2>(combined.Count);
                for (int i = 0; i < combined.Count; i++)
                {
                    int c = combined[i];
                    int mc = MirrorRouteCell(c);
                    roadMask[c] = true;
                    roadMask[mc] = true;
                    MarkNearRoad(c, nearRoadMask, radius: 4);
                    MarkNearRoad(mc, nearRoadMask, radius: 4);
                    worldPts.Add(RouteCellCenter(c % RouteCols, c / RouteCols));
                }
                Vector2 exactA = nodes[seq[0]];
                Vector2 exactB = nodes[seq[^1]];
                if (Vector2.Distance(worldPts[0], exactA) < 36f) worldPts[0] = exactA;
                if (Vector2.Distance(worldPts[^1], exactB) < 36f) worldPts[^1] = exactB;

                var smoothed = SimplifyAndSmoothPath(worldPts, orthogonal: city, checkClearance: 12f);

                // Snap secondary road endpoints precisely onto the centerline of any existing road they join,
                // eliminating gaps, notches, and protruding triangles at T-junctions & Y-forks!
                if (!isPrimary && smoothed.Count >= 2)
                {
                    SnapPolylineEndpointsToExistingRoads(smoothed, city);
                }

                AddMirroredRoadPolyline(smoothed, isPrimary ? primaryHalfW : secondaryHalfW, isPrimary);

                // Update intermediate graph node positions onto the actual smoothed curve so later branches
                // connect dead-center onto Chaikin-smoothed highways rather than the pre-smoothed corner!
                for (int i = 1; i < seq.Length - 1; i++)
                {
                    int midNode = seq[i];
                    if (midNode <= 2) continue;
                    Vector2 snapped = ClosestPointOnPolyline(smoothed, nodes[midNode]);
                    nodes[midNode] = snapped;
                    nodes[MirrorNode(midNode)] = Mirror(snapped);
                }

                for (int i = 0; i < seq.Length - 1; i++)
                {
                    int u = seq[i], v = seq[i + 1];
                    int mu = MirrorNode(u), mv = MirrorNode(v);
                    adj[u].Add(v); adj[v].Add(u);
                    adj[mu].Add(mv); adj[mv].Add(mu);
                    Union(u, v); Union(mu, mv);
                }
                return true;
            }

            // Base entrances from left/right map edge
            AddMirroredRoadPolyline(new[] { new Vector2(0f, _westBase.y), _westBase }, primaryHalfW, true);

            // Stage 1: Pick an emergent Primary Spine driven by actual placed facilities & seed!
            // 0 = DirectHub (W -> C -> E)
            // 1 = SCurve    (W -> Facility -> C -> Mirror(Facility) -> E — single sweeping winding highway!)
            // 2 = TwinArc   (W -> NorthFac -> Mirror(SouthFac) -> E — 2 sweeping flank highways, no center straight!)
            // 3 = Perimeter (W -> outer NorthFac -> Mirror(outer SouthFac) -> E — urban ring / valley sweep!)
            var northFacs = new List<int>();
            var southFacs = new List<int>();
            for (int u = 3; u < 3 + k; u++)
            {
                if (nodes[u].y > ControlPoint.y + 100f) northFacs.Add(u);
                else if (nodes[u].y < ControlPoint.y - 100f) southFacs.Add(u);
            }

            var stylePool = new List<int> { 0 };
            if (k >= 1) { stylePool.Add(1); stylePool.Add(1); }
            if (northFacs.Count > 0 && southFacs.Count > 0)
            {
                stylePool.Add(2); stylePool.Add(2); stylePool.Add(3);
                if (city) stylePool.Add(3);
            }
            int spineStyle = stylePool[random.Next(stylePool.Count)];

            bool primaryOk = false;
            if (spineStyle == 1 && k >= 1)
            {
                int fIdx = 3 + random.Next(k);
                primaryOk = CommitSymmetricChain(new[] { 0, fIdx, 2 }, isPrimary: true, baseCost, avoidExisting: false, maxNearOverlap: 1.0f);
            }
            else if ((spineStyle == 2 || spineStyle == 3) && northFacs.Count > 0 && southFacs.Count > 0)
            {
                int fa = spineStyle == 3
                    ? northFacs[0]
                    : northFacs[random.Next(northFacs.Count)];
                if (spineStyle == 3)
                {
                    foreach (int u in northFacs) if (nodes[u].y > nodes[fa].y) fa = u;
                }
                int fb = spineStyle == 3
                    ? southFacs[0]
                    : southFacs[random.Next(southFacs.Count)];
                if (spineStyle == 3)
                {
                    foreach (int u in southFacs) if (nodes[u].y < nodes[fb].y) fb = u;
                }
                float[] northHemiCost = BuildHemisphereCostGrid(baseCost, +1f);
                primaryOk = CommitSymmetricChain(new[] { 0, fa, MirrorNode(fb), 1 }, isPrimary: true, northHemiCost, avoidExisting: false, maxNearOverlap: 1.0f);
            }

            if (!primaryOk)
            {
                spineStyle = 0;
                if (!CommitSymmetricChain(new[] { 0, 2 }, isPrimary: true, baseCost, avoidExisting: false, maxNearOverlap: 1.0f))
                {
                    AddMirroredRoadPolyline(new[] { _westBase, ControlPoint }, primaryHalfW, true);
                    Union(0, 2); Union(1, 2);
                }
            }

            // Facilities already sitting directly alongside a routed road join that component (no awkward micro-stubs!)
            for (int fId = 3; fId < 3 + k; fId++)
            {
                if (Find(fId) == Find(0)) continue;
                int cell = WorldToRouteCell(nodes[fId]);
                int gx = cell % RouteCols, gy = cell / RouteCols;
                bool nearRoad = false;
                for (int dy = -3; dy <= 3 && !nearRoad; dy++)
                for (int dx = -3; dx <= 3 && !nearRoad; dx++)
                {
                    int nx = gx + dx, ny = gy + dy;
                    if (nx > 0 && nx < RouteCols - 1 && ny > 0 && ny < RouteRows - 1 && roadMask[ny * RouteCols + nx])
                        nearRoad = true;
                }
                if (nearRoad)
                {
                    int bestConn = 0;
                    float bestSq = float.PositiveInfinity;
                    for (int u = 0; u < nCnt; u++)
                    {
                        if (Find(u) != Find(0)) continue;
                        float dSq = (nodes[u] - nodes[fId]).sqrMagnitude;
                        if (dSq < bestSq) { bestSq = dSq; bestConn = u; }
                    }
                    Union(fId, bestConn);
                    Union(MirrorNode(fId), MirrorNode(bestConn));
                }
            }

            // Stage 2: Symmetric MST — guarantee ControlPoint, Bases, and EVERY real artificial facility are connected!
            var allPairs = new List<(float dist, int u, int v)>();
            var seenPairs = new HashSet<(int, int)>();
            for (int u = 0; u < nCnt; u++)
            {
                for (int v = u + 1; v < nCnt; v++)
                {
                    if (v == MirrorNode(u)) continue;
                    int mu = MirrorNode(u), mv = MirrorNode(v);
                    var key1 = u < v ? (u, v) : (v, u);
                    var key2 = mu < mv ? (mu, mv) : (mv, mu);
                    var canon = (key1.Item1 < key2.Item1 || (key1.Item1 == key2.Item1 && key1.Item2 <= key2.Item2)) ? key1 : key2;
                    if (!seenPairs.Add(canon)) continue;
                    float dist = Vector2.Distance(nodes[u], nodes[v]);
                    if (dist < 130f || dist > 950f) continue;
                    allPairs.Add((dist, u, v));
                }
            }
            allPairs.Sort((a, b) => a.dist.CompareTo(b.dist));

            foreach (var (_, u, v) in allPairs)
            {
                if (Find(u) == Find(v)) continue;
                if (!CommitSymmetricChain(new[] { u, v }, isPrimary: false, baseCost, avoidExisting: true, maxNearOverlap: 0.70f))
                    CommitSymmetricChain(new[] { u, v }, isPrimary: false, baseCost, avoidExisting: false, maxNearOverlap: 1.0f);
            }

            // Stage 3: Organic Non-Overlapping Loop Closure (free-form density per biome & seed!)
            int extraBudget = map switch
            {
                BattleMap.City => random.Next(2, 4),   // 2..3 urban cross-streets forming authentic blocks
                BattleMap.Desert => random.Next(0, 3), // 0..2 desert bypasses / oasis cutbacks
                _ => random.Next(0, 2)                 // 0..1 scenic forest loop (leaves wild woods open!)
            };

            int added = 0;
            // First offer clean loop closure to any degree-1 facility if a genuinely distinct road exists
            for (int fId = 3; fId < 3 + k && added < extraBudget; fId++)
            {
                if (adj[fId].Count != 1) continue;
                var cands = new List<(float dist, int other)>();
                foreach (var (dist, u, v) in allPairs)
                {
                    int other = u == fId ? v : (v == fId ? u : -1);
                    if (other < 0 || adj[fId].Contains(other) || other == MirrorNode(fId)) continue;
                    if (dist >= 200f && dist <= 780f && GraphDist(fId, other) >= 3)
                        cands.Add((dist, other));
                }
                cands.Sort((a, b) => a.dist.CompareTo(b.dist));
                int tryLim = Mathf.Min(2, cands.Count);
                for (int c = 0; c < tryLim; c++)
                {
                    if (CommitSymmetricChain(new[] { fId, cands[c].other }, isPrimary: false, baseCost, avoidExisting: true, maxNearOverlap: 0.26f))
                    {
                        added++;
                        break;
                    }
                }
            }

            // Shuffle remaining candidate pairs for organic cross-links (preserving non-3-lane silhouette!)
            for (int i = allPairs.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (allPairs[i], allPairs[j]) = (allPairs[j], allPairs[i]);
            }
            foreach (var (dist, u, v) in allPairs)
            {
                if (added >= extraBudget) break;
                if (adj[u].Contains(v) || v == MirrorNode(u)) continue;
                if (dist < 240f || dist > 760f || GraphDist(u, v) < 3) continue;
                // Preserve S-Curve / Twin-Arc identity by not forcing a straight WestBase<->ControlPoint line
                if ((spineStyle == 1 || spineStyle == 2) &&
                    ((u == 0 && v == 2) || (u == 2 && v == 0) || (u == 1 && v == 2) || (u == 2 && v == 1)))
                    continue;
                if (CommitSymmetricChain(new[] { u, v }, isPrimary: false, baseCost, avoidExisting: true, maxNearOverlap: 0.24f))
                    added++;
            }

            // Detect real street intersections / junctions for crisp City crosswalks & dash breaks!
            RebuildRoadIntersections(city);
        }

        private float[] BuildHemisphereCostGrid(float[] baseCost, float signY)
        {
            var hc = (float[])baseCost.Clone();
            for (int gy = 0; gy < RouteRows; gy++)
            {
                float py = (gy + 0.5f) * RouteCell;
                int rowOff = gy * RouteCols;
                for (int gx = 0; gx < RouteCols; gx++)
                {
                    float px = (gx + 0.5f) * RouteCell;
                    if (Mathf.Abs(px - ControlPoint.x) < 620f)
                    {
                        if (signY > 0f && py < ControlPoint.y + 90f) hc[rowOff + gx] += 6.5f;
                        else if (signY < 0f && py > ControlPoint.y - 90f) hc[rowOff + gx] += 6.5f;
                    }
                }
            }
            return hc;
        }

        private static List<int> CollapseBacktrackLoops(List<int> cells, int lookBack = 14)
        {
            // Eliminate any short U-turn stubs or micro-loops where chained legs meet a waypoint!
            var stack = new List<int>(cells.Count);
            for (int i = 0; i < cells.Count; i++)
            {
                int c = cells[i];
                int revisit = -1;
                int start = Mathf.Max(0, stack.Count - lookBack);
                for (int j = stack.Count - 1; j >= start; j--)
                {
                    if (stack[j] == c) { revisit = j; break; }
                }
                if (revisit >= 0)
                    stack.RemoveRange(revisit + 1, stack.Count - (revisit + 1));
                else
                    stack.Add(c);
            }
            return stack;
        }

        private void BuildObjectAwareForestStream(float reflection, List<Vector2> wetlandHints, System.Random random)
        {
            float streamHalfW = _scenario == RegionalScenario.ForestWetlandGrove ? 22f : 16.5f;

            // Build stream cost grid: avoid all cover & artificial yards, favor wild forest & wetlands,
            // and cross roads cleanly rather than running lengthwise inside a road!
            var blocked = new bool[RouteCount];
            var cost = new float[RouteCount];
            for (int gy = 0; gy < RouteRows; gy++)
            {
                for (int gx = 0; gx < RouteCols; gx++)
                {
                    int idx = gy * RouteCols + gx;
                    Vector2 p = RouteCellCenter(gx, gy);

                    // Keep stream cleanly on the west half (mirrored to east half) and clear of bases/plaza
                    if (p.x < 260f || p.x > ControlPoint.x - 95f ||
                        Vector2.Distance(p, _westBase) < BaseClearRadius + 24f ||
                        Vector2.Distance(p, ControlPoint) < ControlClearRadius + 28f)
                    {
                        blocked[idx] = true;
                        continue;
                    }

                    bool hitCover = false;
                    for (int c = 0; c < _cover.Count; c++)
                    {
                        if (_cover[c].Contains(p, 12f))
                        {
                            hitCover = true;
                            break;
                        }
                    }
                    blocked[idx] = hitCover;

                    float cVal = 1.0f;
                    if (IsInsideAnyDistrict(_districtGround, p) || IsInsideAnyDistrict(_farmGround, p))
                        cVal += 9.0f; // Stay out of man-made cabins, farms & camps
                    if (IsInsideAnyDistrict(_groveGround, p))
                        cVal *= 0.65f; // Flow gracefully through natural woodlands
                    if (IsOnRoad(p, 8f, primaryOnly: false))
                        cVal += 2.6f; // Allow clean perpendicular bridge crossings, discourage riding along roads
                    cVal += Mathf.PerlinNoise(p.x * 0.006f + 11.3f, p.y * 0.006f + (_seed & 127) * 0.21f) * 0.8f;
                    cost[idx] = cVal;
                }
            }

            // Anchor north & south valley endpoints (pulled subtly toward any wetland grove!)
            float biasX = wetlandHints.Count > 0 ? Mathf.Clamp(wetlandHints[0].x, 420f, 760f) : 630f;
            float northX = Mathf.Clamp(biasX + random.Next(-90, 91), 380f, 800f);
            float southX = Mathf.Clamp(biasX + random.Next(-110, 111), 380f, 800f);
            Vector2 startPt = new(northX, reflection >= 0f ? Height - 32f : 32f);
            Vector2 endPt = new(southX, reflection >= 0f ? 32f : Height - 32f);

            int startCell = FindNearestOpenCell(startPt, blocked);
            int endCell = FindNearestOpenCell(endPt, blocked);
            var cellPath = RunGridAStar(startCell, endCell, allowDiagonal: true, turnPenalty: 0.45f, blocked, cost, null, null);

            if (cellPath != null && cellPath.Count >= 3)
            {
                var pts = new List<Vector2>(cellPath.Count + 2)
                {
                    new Vector2(RouteCellCenter(cellPath[0] % RouteCols, cellPath[0] / RouteCols).x,
                        reflection >= 0f ? Height : 0f)
                };
                for (int i = 0; i < cellPath.Count; i++)
                    pts.Add(RouteCellCenter(cellPath[i] % RouteCols, cellPath[i] / RouteCols));
                int last = cellPath[^1];
                pts.Add(new Vector2(RouteCellCenter(last % RouteCols, last / RouteCols).x,
                    reflection >= 0f ? 0f : Height));

                pts = SimplifyAndSmoothPath(pts, orthogonal: false, checkClearance: 10f);
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    _waterStreams.Add(new RoadSegment(pts[i], pts[i + 1], streamHalfW, true));
                    _waterStreams.Add(new RoadSegment(Mirror(pts[i]), Mirror(pts[i + 1]), streamHalfW, true));
                }
            }
        }

        private static List<int> RouteCellsBetween(Vector2 from, Vector2 to, bool allowDiagonal, float turnPenalty,
            bool[] strictBlocked, bool[] relaxedBlocked, float[] baseCost, bool[] roadMask, bool[] nearRoadMask,
            bool avoidExistingRoad = false, float nearPenalty = 5.8f, float shareCost = 0.35f, float avoidRoadPenalty = 8.0f)
        {
            int startCell = FindNearestOpenCell(from, strictBlocked);
            int goalCell = FindNearestOpenCell(to, strictBlocked);
            List<int> cellPath = null;
            if (startCell >= 0 && goalCell >= 0)
                cellPath = RunGridAStar(startCell, goalCell, allowDiagonal, turnPenalty, strictBlocked, baseCost,
                    roadMask, nearRoadMask, avoidExistingRoad, nearPenalty, shareCost, avoidRoadPenalty, from, to, 96f);

            if (cellPath == null)
            {
                startCell = FindNearestOpenCell(from, relaxedBlocked);
                goalCell = FindNearestOpenCell(to, relaxedBlocked);
                if (startCell >= 0 && goalCell >= 0)
                    cellPath = RunGridAStar(startCell, goalCell, allowDiagonal, turnPenalty, relaxedBlocked, baseCost,
                        roadMask, nearRoadMask, avoidExistingRoad, nearPenalty, shareCost, avoidRoadPenalty, from, to, 96f);
            }

            return (cellPath != null && cellPath.Count >= 2) ? cellPath : null;
        }

        private List<Vector2> SimplifyAndSmoothPath(List<Vector2> pts, bool orthogonal, float checkClearance)
        {
            if (pts.Count <= 2) return pts;

            // Step 1: Merge consecutive collinear segments (essential for crisp City 90-degree boulevards!)
            var reduced = new List<Vector2> { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector2 prev = reduced[^1];
                Vector2 cur = pts[i];
                Vector2 next = pts[i + 1];
                Vector2 d1 = (cur - prev).normalized;
                Vector2 d2 = (next - cur).normalized;
                if (Vector2.Dot(d1, d2) > 0.995f) continue;
                reduced.Add(cur);
            }
            reduced.Add(pts[^1]);

            if (orthogonal)
                return reduced;

            // Step 2 (Forest/Desert): Collapse tiny staircase zig-zags if the direct chord stays clear of cover.
            var cleaned = new List<Vector2> { reduced[0] };
            int idx = 0;
            while (idx < reduced.Count - 1)
            {
                int best = idx + 1;
                int lookLimit = Mathf.Min(reduced.Count - 1, idx + 6);
                for (int probe = lookLimit; probe > idx + 1; probe--)
                {
                    if (IsChordClearOfCover(reduced[idx], reduced[probe], checkClearance))
                    {
                        best = probe;
                        break;
                    }
                }
                cleaned.Add(reduced[best]);
                idx = best;
            }

            // Step 3 (Forest/Desert): 2 passes of safe Chaikin corner-cutting for natural sweeping curves!
            var current = cleaned;
            for (int pass = 0; pass < 2; pass++)
            {
                if (current.Count < 3) break;
                var nextList = new List<Vector2>(current.Count * 2) { current[0] };
                for (int i = 0; i < current.Count - 1; i++)
                {
                    Vector2 a = current[i];
                    Vector2 b = current[i + 1];
                    if ((b - a).sqrMagnitude < 28f * 28f)
                    {
                        if (i > 0) nextList.Add(a);
                        continue;
                    }
                    Vector2 q = Vector2.Lerp(a, b, 0.25f);
                    Vector2 r = Vector2.Lerp(a, b, 0.75f);
                    if (i > 0) nextList.Add(IsPointClearOfCover(q, checkClearance) ? q : a);
                    if (i < current.Count - 2) nextList.Add(IsPointClearOfCover(r, checkClearance) ? r : b);
                }
                nextList.Add(current[^1]);
                current = nextList;
            }
            return current;
        }

        private bool IsPointClearOfCover(Vector2 p, float clearance)
        {
            for (int i = 0; i < _cover.Count; i++)
                if (_cover[i].Contains(p, clearance)) return false;
            return true;
        }

        private bool IsChordClearOfCover(Vector2 a, Vector2 b, float clearance)
        {
            float dist = Vector2.Distance(a, b);
            int samples = Mathf.Max(2, Mathf.CeilToInt(dist / 12f));
            for (int i = 1; i < samples; i++)
                if (!IsPointClearOfCover(Vector2.Lerp(a, b, i / (float)samples), clearance))
                    return false;
            return true;
        }

        private void SnapPolylineEndpointsToExistingRoads(List<Vector2> pts, bool city)
        {
            if (_roads.Count == 0 || pts.Count < 2) return;
            SnapSingleEndpoint(pts, atStart: true, city);
            SnapSingleEndpoint(pts, atStart: false, city);
        }

        private void SnapSingleEndpoint(List<Vector2> pts, bool atStart, bool city)
        {
            int endIdx = atStart ? 0 : pts.Count - 1;
            int prevIdx = atStart ? 1 : pts.Count - 2;
            Vector2 endPt = pts[endIdx];
            Vector2 prevPt = pts[prevIdx];
            // Never disturb map-edge or base/plaza center anchors
            if (endPt.x < 16f || endPt.x > Width - 16f ||
                Vector2.Distance(endPt, _westBase) < 28f ||
                Vector2.Distance(endPt, Mirror(_westBase)) < 28f ||
                Vector2.Distance(endPt, ControlPoint) < 28f)
                return;

            float bestDist = 56f;
            Vector2 bestSnap = endPt;
            bool found = false;
            RoadSegment hitSeg = default;

            for (int i = 0; i < _roads.Count; i++)
            {
                var seg = _roads[i];
                float d = seg.DistanceTo(endPt, out float along, out _);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestSnap = seg.A + seg.Dir * along;
                    hitSeg = seg;
                    found = true;
                }
            }
            if (!found) return;

            if (city)
            {
                // Preserve crisp 90-degree City T-junctions by aligning the endpoint orthogonally onto the hit street!
                Vector2 segDir = hitSeg.Dir;
                if (Mathf.Abs(segDir.x) > 0.7f)
                {
                    float snappedY = hitSeg.A.y;
                    float x = Mathf.Clamp(prevPt.x, hitSeg.MinX, hitSeg.MaxX);
                    if (Mathf.Abs(x - endPt.x) < 40f)
                        bestSnap = new Vector2(x, snappedY);
                }
                else if (Mathf.Abs(segDir.y) > 0.7f)
                {
                    float snappedX = hitSeg.A.x;
                    float y = Mathf.Clamp(prevPt.y, hitSeg.MinY, hitSeg.MaxY);
                    if (Mathf.Abs(y - endPt.y) < 40f)
                        bestSnap = new Vector2(snappedX, y);
                }
            }

            pts[endIdx] = bestSnap;
        }

        private static Vector2 ClosestPointOnPolyline(List<Vector2> pts, Vector2 target)
        {
            Vector2 best = pts[0];
            float bestSq = float.PositiveInfinity;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector2 a = pts[i];
                Vector2 ab = pts[i + 1] - a;
                float lenSq = ab.sqrMagnitude;
                float t = lenSq > 0.001f ? Mathf.Clamp01(Vector2.Dot(target - a, ab) / lenSq) : 0f;
                Vector2 c = a + ab * t;
                float dSq = (target - c).sqrMagnitude;
                if (dSq < bestSq) { bestSq = dSq; best = c; }
            }
            return best;
        }

        private void RebuildRoadIntersections(bool city)
        {
            _intersections.Clear();
            _intersections.Add(new RoadIntersection(ControlPoint, 72f));
            if (!city) return;

            // Collect ONLY true street-to-street junctions (two DIFFERENT polylines meeting orthogonally),
            // never plain 90-degree bends within a single street!
            for (int i = 0; i < _roads.Count; i++)
            {
                var s1 = _roads[i];
                for (int j = i + 1; j < _roads.Count; j++)
                {
                    var s2 = _roads[j];
                    if (s1.PolylineId == s2.PolylineId) continue;
                    if (Mathf.Abs(Vector2.Dot(s1.Dir, s2.Dir)) > 0.35f) continue;

                    if (TrySegmentIntersection(s1.A, s1.B, s2.A, s2.B, 18f, out Vector2 hit))
                        AddUniqueIntersection(hit, Mathf.Max(s1.HalfWidth, s2.HalfWidth));
                }
            }
        }

        private void AddUniqueIntersection(Vector2 pt, float radius)
        {
            for (int i = 0; i < _intersections.Count; i++)
            {
                if ((_intersections[i].Position - pt).sqrMagnitude < 32f * 32f)
                {
                    if (radius > _intersections[i].Radius)
                        _intersections[i] = new RoadIntersection(_intersections[i].Position, radius);
                    return;
                }
            }
            _intersections.Add(new RoadIntersection(pt, radius));
        }

        private static bool TrySegmentIntersection(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2, float pad, out Vector2 hit)
        {
            hit = Vector2.zero;
            Vector2 d1 = a2 - a1;
            Vector2 d2 = b2 - b1;
            float cross = d1.x * d2.y - d1.y * d2.x;
            if (Mathf.Abs(cross) < 0.001f) return false;
            Vector2 diff = b1 - a1;
            float t = (diff.x * d2.y - diff.y * d2.x) / cross;
            float u = (diff.x * d1.y - diff.y * d1.x) / cross;
            float len1 = Mathf.Max(1f, d1.magnitude);
            float len2 = Mathf.Max(1f, d2.magnitude);
            float margin1 = pad / len1;
            float margin2 = pad / len2;
            if (t >= -margin1 && t <= 1f + margin1 && u >= -margin2 && u <= 1f + margin2)
            {
                hit = a1 + d1 * Mathf.Clamp01(t);
                return true;
            }
            return false;
        }

        private Vector2 FindFacilityAccessPoint(Vector2 center, Vector2 groundSize, bool[] strictBlocked, bool[] relaxedBlocked)
        {
            Vector2 toCenter = (ControlPoint - center).normalized;
            float off = Mathf.Clamp(Mathf.Min(groundSize.x, groundSize.y) * 0.44f, 40f, 80f);
            Vector2[] candidates =
            {
                center + toCenter * off,
                center + Vector2.Perpendicular(toCenter) * off,
                center - Vector2.Perpendicular(toCenter) * off,
                center - toCenter * off,
                center
            };
            foreach (var cand in candidates)
            {
                int cell = WorldToRouteCell(cand);
                if (!strictBlocked[cell]) return RouteCellCenter(cell % RouteCols, cell / RouteCols);
            }
            int open = FindNearestOpenCell(candidates[0], relaxedBlocked);
            return open >= 0 ? RouteCellCenter(open % RouteCols, open / RouteCols) : center;
        }

        private static bool IsInsideAnyDistrict(List<DistrictGround> list, Vector2 point)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var d = list[i];
                if ((point - d.Center).sqrMagnitude > d.RadiusSquared) continue;
                Vector2 local = d.ToLocal(point);
                if (Mathf.Abs(local.x) < d.HalfSize.x && Mathf.Abs(local.y) < d.HalfSize.y)
                    return true;
            }
            return false;
        }

        private void PopulateRoadsideProps(BattleMap map, BattleSpawnAssets assets, System.Random random)
        {
            if (_roads.Count == 0) return;

            string[] props = map switch
            {
                BattleMap.City => _scenario switch
                {
                    RegionalScenario.CityRailFreight => new[] { "guard_booth", "oil_drum_stack", "city_barricade", "abandoned_car" },
                    RegionalScenario.CityDowntownAvenue => new[] { "bus_stop", "street_kiosk", "abandoned_car", "city_barricade" },
                    _ => new[] { "guard_booth", "city_barricade", "tank_trap", "hesco_barrier" }
                },
                BattleMap.Forest => new[] { "sandbag_wall", "guard_booth", "timber_stack", "wrecked_truck" },
                _ => new[] { "sandbag_wall", "hesco_barrier", "abandoned_car", "oil_drum_stack" }
            };

            int placed = 0;
            int maxRoadside = map == BattleMap.City ? 5 : 3;
            for (int i = 0; i < _roads.Count && placed < maxRoadside; i++)
            {
                var seg = _roads[i];
                if (seg.Length < 88f) continue;
                Vector2 mid = (seg.A + seg.B) * 0.5f;
                // Keep west half only (TryPlaceMirrored automatically mirrors onto east half!)
                if (mid.x > ControlPoint.x - 40f) continue;

                // Skip intersections & stream bridges
                bool nearInter = false;
                for (int k = 0; k < _intersections.Count; k++)
                    if (Vector2.Distance(mid, _intersections[k].Position) < _intersections[k].Radius + 42f) { nearInter = true; break; }
                if (nearInter || IsInWater(mid, 30f)) continue;

                float roadAngle = Mathf.Atan2(seg.Dir.y, seg.Dir.x) * Mathf.Rad2Deg;
                if (map == BattleMap.City) roadAngle = Mathf.Round(roadAngle / 90f) * 90f;
                Vector2 perp = Vector2.Perpendicular(seg.Dir);
                float side = (placed % 2 == 0) ? 1f : -1f;
                string kind = props[(placed + random.Next(props.Length)) % props.Length];
                var prefab = GetProp(assets, kind);
                if (prefab == null) continue;

                float offset = seg.HalfWidth + Mathf.Min(prefab.meta.width, prefab.meta.length) * 0.5f + 12f;
                if (TryPlaceMirrored(prefab, mid + perp * (side * offset), roadAngle, clearance: 7f, avoidRoads: true))
                    placed++;
                else if (TryPlaceMirrored(prefab, mid - perp * (side * offset), roadAngle + 180f, clearance: 7f, avoidRoads: true))
                    placed++;
            }
        }

        private void AddMirroredRoadPolyline(IReadOnlyList<Vector2> points, float halfWidth, bool isPrimary)
        {
            // Store each polyline's segments contiguously with its own PolylineId and continuous arc length
            // so the pixel renderer computes exact Smooth-Min junctions and unbroken center dashes!
            int pidA = _nextPolylineId++;
            float alongA = 0f;
            for (int i = 0; i < points.Count - 1; i++)
                alongA += AddRoadSegment(points[i], points[i + 1], halfWidth, isPrimary, alongA, pidA);

            int pidB = _nextPolylineId++;
            float alongB = 0f;
            for (int i = 0; i < points.Count - 1; i++)
                alongB += AddRoadSegment(Mirror(points[i]), Mirror(points[i + 1]), halfWidth, isPrimary, alongB, pidB);
        }

        private float AddRoadSegment(Vector2 a, Vector2 b, float halfWidth, bool isPrimary, float alongStart, int polylineId)
        {
            float len = Vector2.Distance(a, b);
            if (len < 1f) return 0f;
            _roads.Add(new RoadSegment(a, b, halfWidth, isPrimary, alongStart, polylineId));
            return len;
        }

        private bool TryNudgeOffRoads(ref Vector2 pos, float margin)
        {
            Vector2[] offsets =
            {
                new(0, 48), new(0, -48), new(48, 0), new(-48, 0),
                new(0, 78), new(0, -78), new(78, 0), new(-78, 0),
                new(52, 52), new(-52, 52), new(52, -52), new(-52, -52)
            };
            foreach (var off in offsets)
            {
                Vector2 candidate = pos + off;
                if (!IsOnRoad(candidate, margin, primaryOnly: false) && !IsInWater(candidate, margin * 0.7f))
                {
                    pos = candidate;
                    return true;
                }
            }
            return false;
        }

        private bool IsOnRoad(Vector2 point, float margin, bool primaryOnly)
        {
            for (int i = 0; i < _roads.Count; i++)
            {
                var seg = _roads[i];
                if (primaryOnly && !seg.IsPrimary) continue;
                float pad = seg.HalfWidth + margin;
                if (point.x < seg.MinX - pad || point.x > seg.MaxX + pad ||
                    point.y < seg.MinY - pad || point.y > seg.MaxY + pad) continue;
                if (seg.DistanceTo(point, out _, out _) < pad)
                    return true;
            }
            return false;
        }

        private bool IsInWater(Vector2 point, float margin)
        {
            for (int i = 0; i < _waterStreams.Count; i++)
            {
                var seg = _waterStreams[i];
                float pad = seg.HalfWidth + margin;
                if (point.x < seg.MinX - pad || point.x > seg.MaxX + pad ||
                    point.y < seg.MinY - pad || point.y > seg.MaxY + pad) continue;
                if (seg.DistanceTo(point, out _, out _) < pad)
                    return true;
            }
            return false;
        }

        private readonly struct RoadIntersection
        {
            internal readonly Vector2 Position;
            internal readonly float Radius;

            internal RoadIntersection(Vector2 position, float radius)
            {
                Position = position;
                Radius = radius;
            }
        }

        private readonly struct RoadSegment
        {
            internal readonly Vector2 A;
            internal readonly Vector2 B;
            internal readonly Vector2 Dir;
            internal readonly float Length;
            internal readonly float HalfWidth;
            internal readonly bool IsPrimary;
            internal readonly float AlongStart;
            internal readonly int PolylineId;
            internal readonly float MinX, MaxX, MinY, MaxY;

            internal RoadSegment(Vector2 a, Vector2 b, float halfWidth, bool isPrimary, float alongStart = 0f, int polylineId = 0)
            {
                A = a;
                B = b;
                Vector2 delta = b - a;
                Length = delta.magnitude;
                Dir = Length > 0.001f ? delta / Length : Vector2.right;
                HalfWidth = halfWidth;
                IsPrimary = isPrimary;
                AlongStart = alongStart;
                PolylineId = polylineId;
                MinX = Mathf.Min(a.x, b.x);
                MaxX = Mathf.Max(a.x, b.x);
                MinY = Mathf.Min(a.y, b.y);
                MaxY = Mathf.Max(a.y, b.y);
            }

            internal float DistanceTo(Vector2 p, out float along, out float signedPerp) =>
                DistanceTo(p, out along, out signedPerp, out _);

            internal float DistanceTo(Vector2 p, out float along, out float signedPerp, out bool insideSpan)
            {
                Vector2 ap = p - A;
                float proj = Vector2.Dot(ap, Dir);
                insideSpan = proj >= 0f && proj <= Length;
                along = Mathf.Clamp(proj, 0f, Length);
                Vector2 closest = A + Dir * along;
                Vector2 diff = p - closest;
                signedPerp = Vector2.Dot(diff, Vector2.Perpendicular(Dir));
                return diff.magnitude;
            }
        }
    }
}
