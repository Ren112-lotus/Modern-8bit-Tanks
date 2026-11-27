using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;

namespace Modern8bitTanks
{
    /// <summary>Shared coordinates for terrain, deployment, AI and the tactical radar.</summary>
    internal sealed class BattlefieldLayout
    {
        public const int Width = 1920;
        public const int Height = 1248;
        internal static readonly Color32 BoundaryColor = new(226, 218, 178, 255);
        private const float PlanScale = 1.5f;
        private const float RidgeFootMargin = 36f;
        private const float ControlClearRadius = 88f;
        private const float BaseClearRadius = 112f;
        private static readonly Vector2 MapSize = new(Width, Height);
        private static readonly Vector2[] DeploymentOffsets =
        {
            new(-28, 0), new(0, 54), new(25, 25), new(25, -25), new(-5, -53)
        };

        /// <summary>
        /// Match-level cohesive theater selected per seed so each battle uses a unified
        /// thematic subset of the prop catalog instead of scattering unrelated assets together.
        /// </summary>
        private enum RegionalScenario
        {
            DesertAirbase,
            DesertOasisRoute,
            DesertCanyonFront,
            CityRailFreight,
            CityDowntownAvenue,
            CityCommandCitadel,
            ForestRuralValley,
            ForestAutumnHighland,
            ForestWetlandGrove,
            ForestAlpineRidge
        }

        private enum ZoneArchetype
        {
            // Artificial installations (axis-locked, right-angle grids & functional sets)
            AirDefenseBase,
            CommandTentCamp,
            RoadCheckpoint,
            SolarOutpost,
            FreightRailYard,
            SupplyDepotYard,
            PowerUtilityWorks,
            ArtilleryBattery,
            UrbanFuelAvenue,
            UrbanResidentialBlock,
            RuralWindmillFarm,
            TimberLoggingCamp,
            BattleWreckScar,

            // Natural formations (free rotation, curved drifts, hierarchical parent-child clusters)
            DesertBadlands,
            DesertOasisBasin,
            DesertCanyonSpires,
            CityBotanicalPark,
            CityMemorialPark,
            AncientWoodland,
            AutumnHighlandWoods,
            WetlandMangroveGrove,
            AlpineSnowWoods,
            RockyOutcropField
        }

        private readonly BattleMap _map;
        private readonly RegionalScenario _scenario;
        private readonly int _seed;
        private readonly Vector2 _westBase;
        private readonly List<CoverPlacement> _cover = new();
        private readonly List<DistrictGround> _districtGround = new();
        private readonly List<DistrictGround> _parkGround = new();
        private readonly List<DistrictGround> _groveGround = new();
        private readonly List<DistrictGround> _farmGround = new();
        private readonly List<DistrictGround> _ridgeGround = new();
        private readonly List<RoadSegment> _roads = new();
        private readonly List<Vector2> _intersections = new();
        private readonly List<RoadSegment> _waterStreams = new();
        private Color32[] _ground;

        internal BattlefieldLayout(BattleMap map, int seed, BattlefieldPlan plan, BattlefieldTheme theme, BattleSpawnAssets assets)
        {
            _map = map;
            _seed = seed;
            var random = new System.Random(seed);
            _scenario = PickRegionalScenario(map, random);
            _westBase = map == BattleMap.City
                ? new Vector2(128, ControlPoint.y)
                : new Vector2(128, ControlPoint.y + random.Next(-17, 18));
            float reflection = random.Next(2) == 0 ? 1f : -1f;

            // Pass 1: Place ridges, corner detours, and collect soft corridor hints from the plan.
            var mainDistrictIndices = new List<int>();
            var ridgeAnchors = new List<(Vector2 Position, float Radius, float Angle)>();
            var corridorHints = new List<DistrictGround>();
            var artificialAnchors = new List<(Vector2 Center, Vector2 GroundSize, ZoneArchetype Archetype)>();
            var wetlandHints = new List<Vector2>();

            for (int i = 0; i < plan.Districts.Length; i++)
            {
                var district = plan.Districts[i];
                Vector2 center = district.Center * PlanScale + new Vector2(random.Next(-10, 11), random.Next(-12, 13));
                float rawAngle = district.Angle + random.Next(-6, 7);
                if (map == BattleMap.City) rawAngle = Mathf.Round(district.Angle / 90f) * 90f;
                center.y = ControlPoint.y + (center.y - ControlPoint.y) * reflection;

                if (district.Cover.Length == 0)
                {
                    // Use plan connector gaps as soft low-cost hints for object-aware road routing.
                    Vector2 hintSize = district.GroundSize * PlanScale;
                    if (hintSize.x > 0f && hintSize.y > 0f)
                        corridorHints.Add(new DistrictGround(center, hintSize, Direction(rawAngle * reflection), true));
                    continue;
                }

                if (IsRidgeDistrict(district))
                {
                    PlaceRidgeDistrict(district, center, rawAngle, reflection, theme, ridgeAnchors);
                    continue;
                }

                if (IsCornerDetourDistrict(district))
                {
                    PlaceCornerDetourDistrict(district, center, rawAngle, reflection, map, theme, assets, random);
                    continue;
                }

                mainDistrictIndices.Add(i);
            }

            // Pass 2: Place all scenario-coherent artificial installations & natural formations FIRST.
            var archetypes = AssignDistrictArchetypes(_scenario, mainDistrictIndices.Count, random);
            for (int m = 0; m < mainDistrictIndices.Count; m++)
            {
                var district = plan.Districts[mainDistrictIndices[m]];
                var archetype = archetypes[m];
                bool natural = IsNaturalArchetype(archetype);

                Vector2 center = district.Center * PlanScale + new Vector2(random.Next(-10, 11), random.Next(-12, 13));
                center.y = ControlPoint.y + (center.y - ControlPoint.y) * reflection;

                float baseAngle = district.Angle + (natural ? random.Next(-14, 15) : random.Next(-5, 6));
                if (map == BattleMap.City)
                    baseAngle = 0f;
                else if (!natural)
                    baseAngle = Mathf.Round(baseAngle / 15f) * 15f;

                float orientedAngle = baseAngle * reflection;
                if (district.GroundSize.x > 0f && district.GroundSize.y > 0f)
                {
                    if (archetype is ZoneArchetype.CityBotanicalPark or ZoneArchetype.CityMemorialPark)
                        _parkGround.Add(new DistrictGround(center, district.GroundSize, Direction(orientedAngle), false));
                    else if (archetype == ZoneArchetype.RuralWindmillFarm)
                    {
                        _districtGround.Add(new DistrictGround(center, district.GroundSize * 0.85f, Direction(orientedAngle), false));
                        _farmGround.Add(new DistrictGround(center + Direction(orientedAngle) * 24f, district.GroundSize * 0.92f, Direction(orientedAngle), false));
                    }
                    else if (natural && map == BattleMap.Forest)
                        _groveGround.Add(new DistrictGround(center, district.GroundSize * 1.12f, Direction(orientedAngle), false));
                    else if (!natural)
                        _districtGround.Add(new DistrictGround(center, district.GroundSize, Direction(orientedAngle), false));
                }

                if (natural)
                {
                    PopulateNaturalDistrict(district, archetype, center, baseAngle, reflection, theme, assets, random);
                    if (archetype == ZoneArchetype.WetlandMangroveGrove)
                        wetlandHints.Add(center);
                }
                else
                {
                    PopulateArtificialDistrict(district, archetype, center, baseAngle, reflection, map, theme, assets, random);
                    artificialAnchors.Add((center, district.GroundSize, archetype));
                }
            }

            // Pass 3: Generate an object-aware, fully connected road network via weighted A* routing
            // that physically weaves around placed cover, ridges & groves to link bases & artificial sites!
            BuildObjectAwareRoadNetwork(map, reflection, artificialAnchors, corridorHints, random);

            // Pass 4: Route the Forest woodland brook around buildings/camps & across roads (creating bridges).
            if (map == BattleMap.Forest)
                BuildObjectAwareForestStream(reflection, wetlandHints, random);

            // Pass 5: Contextual post-road dressing (roadside furniture aligned to actual routed streets,
            // ridge foothill scree, and woodland stream-bank flora — all strictly respecting roads & streams).
            PopulateRoadsideProps(map, assets, random);
            PopulateRidgeFoothills(_scenario, ridgeAnchors, assets, random);
            if (map == BattleMap.Forest)
                PopulateStreamBanks(_scenario, assets, random);
        }

        // =====================================================================
        // OBJECT-AWARE A* ROAD & FOREST STREAM ROUTING
        // =====================================================================
        private const int RouteCell = 16;
        private const int RouteCols = Width / RouteCell;   // 120
        private const int RouteRows = Height / RouteCell;  // 78
        private const int RouteCount = RouteCols * RouteRows;

        private static readonly int[] DirX4 = { 1, -1, 0, 0 };
        private static readonly int[] DirY4 = { 0, 0, 1, -1 };
        private static readonly int[] DirX8 = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] DirY8 = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private static readonly float[] DirCost8 = { 1f, 1f, 1f, 1f, 1.414f, 1.414f, 1.414f, 1.414f };

        private static Vector2 RouteCellCenter(int gx, int gy) =>
            new((gx + 0.5f) * RouteCell, (gy + 0.5f) * RouteCell);

        private static int WorldToRouteCell(Vector2 pos)
        {
            int gx = Mathf.Clamp(Mathf.FloorToInt(pos.x / RouteCell), 1, RouteCols - 2);
            int gy = Mathf.Clamp(Mathf.FloorToInt(pos.y / RouteCell), 1, RouteRows - 2);
            return gy * RouteCols + gx;
        }

        private static int MirrorRouteCell(int cell)
        {
            int gx = cell % RouteCols;
            int gy = cell / RouteCols;
            return (RouteRows - 1 - gy) * RouteCols + (RouteCols - 1 - gx);
        }

        private void BuildObjectAwareRoadNetwork(BattleMap map, float reflection,
            List<(Vector2 Center, Vector2 GroundSize, ZoneArchetype Archetype)> artificialAnchors,
            List<DistrictGround> corridorHints, System.Random random)
        {
            bool city = map == BattleMap.City;
            float primaryHalfW = city ? 28f : (map == BattleMap.Desert ? 25f : 23f);
            float secondaryHalfW = city ? 21f : 17f;
            float turnPenalty = city ? 4.2f : 0.85f;

            // Build obstacle & terrain cost grids reflecting ALL placed cover, ridges, parks & groves.
            var strictBlocked = new bool[RouteCount];
            var relaxedBlocked = new bool[RouteCount];
            var baseCost = new float[RouteCount];
            var allRoadMask = new bool[RouteCount];
            var primaryMask = new bool[RouteCount];
            var primaryNearMask = new bool[RouteCount];
            var flankNearMask = new bool[RouteCount];
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
                        cost += 2.4f;

                    // Gently prefer natural open alley corridors between plan districts.
                    if (IsInsideAnyDistrict(corridorHints, p))
                        cost *= 0.78f;

                    // In natural biomes, add subtle Perlin meander cost so routes bend organically.
                    if (!city)
                    {
                        float n = Mathf.PerlinNoise(p.x * 0.0045f + (_seed & 255) * 0.13f, p.y * 0.0045f + 3.7f);
                        cost += n * 0.55f;
                    }

                    baseCost[idx] = cost;
                }
            }

            // 1. PRIMARY TRUNK HIGHWAY: Map Edge -> West Base -> Control Point (mirrored seamlessly to East Base!)
            AddMirroredRoadPolyline(new[] { new Vector2(0f, _westBase.y), _westBase }, primaryHalfW, true);
            var primaryCost = (float[])baseCost.Clone();
            for (int gy = 0; gy < RouteRows; gy++)
            {
                float py = (gy + 0.5f) * RouteCell;
                float bandPen = Mathf.Max(0f, (Mathf.Abs(py - ControlPoint.y) - 150f) / 140f) * 1.4f;
                if (bandPen <= 0f) continue;
                int rowOff = gy * RouteCols;
                for (int gx = 0; gx < RouteCols; gx++)
                    primaryCost[rowOff + gx] += bandPen;
            }
            var primaryCells = RouteCellsBetween(_westBase, ControlPoint, !city, turnPenalty,
                strictBlocked, relaxedBlocked, primaryCost, null, null);
            if (primaryCells != null && primaryCells.Count >= 2)
                CommitMirroredCellPath(primaryCells, _westBase, ControlPoint, primaryHalfW, isPrimary: true, city: city,
                    allRoadMask, primaryMask, primaryNearMask, flankNearMask);
            else
                AddMirroredRoadPolyline(new[] { _westBase, ControlPoint }, primaryHalfW, true);

            // Clear primaryNearMask right around bases & center plaza so flank roads and cross-avenue join cleanly.
            Vector2 eastBase = Mirror(_westBase);
            for (int gy = 0; gy < RouteRows; gy++)
            {
                for (int gx = 0; gx < RouteCols; gx++)
                {
                    int idx = gy * RouteCols + gx;
                    if (!primaryNearMask[idx]) continue;
                    Vector2 p = RouteCellCenter(gx, gy);
                    if (Vector2.Distance(p, _westBase) < 145f ||
                        Vector2.Distance(p, eastBase) < 145f ||
                        Vector2.Distance(p, ControlPoint) < 120f)
                        primaryNearMask[idx] = false;
                }
            }

            // 2. CONTINUOUS NORTH & SOUTH FLANK HIGHWAYS CHAINING ARTIFICIAL FACILITIES!
            // Skip midline facilities (|y - cp.y| < 155) already served directly by the Primary Trunk.
            var northSites = new List<Vector2>();
            var southSites = new List<Vector2>();
            foreach (var site in artificialAnchors)
            {
                if (Mathf.Abs(site.Center.y - ControlPoint.y) < 155f) continue;
                Vector2 access = FindCorridorAccessPoint(site.Center, site.GroundSize, strictBlocked, relaxedBlocked, preferInner: true);
                if (Mathf.Abs(access.y - ControlPoint.y) < 150f) continue;
                if (access.y > ControlPoint.y) northSites.Add(access);
                else southSites.Add(access);
            }
            northSites.Sort((a, b) => a.x.CompareTo(b.x));
            southSites.Sort((a, b) => a.x.CompareTo(b.x));
            northSites = FilterSpacedWaypoints(northSites, 120f);
            southSites = FilterSpacedWaypoints(southSites, 120f);

            // Compute natural centerline seam altitude matching actual northern/southern facilities!
            float sumOff = 0f;
            int cntOff = 0;
            foreach (var s in northSites) { sumOff += Mathf.Abs(s.y - ControlPoint.y); cntOff++; }
            foreach (var s in southSites) { sumOff += Mathf.Abs(s.y - ControlPoint.y); cntOff++; }
            float seamOff = Mathf.Clamp(cntOff > 0 ? sumOff / cntOff : 325f, 255f, 390f);

            Vector2 northSeam = FindCorridorAccessPoint(
                new Vector2(ControlPoint.x, ControlPoint.y + seamOff), new Vector2(64f, 64f), strictBlocked, relaxedBlocked, preferInner: false);
            Vector2 southSeam = Mirror(northSeam);

            if (northSites.Count == 0)
                northSites.Add(FindCorridorAccessPoint(new Vector2(Width * 0.32f, ControlPoint.y + seamOff), new Vector2(64f, 64f), strictBlocked, relaxedBlocked, preferInner: false));
            if (southSites.Count == 0)
                southSites.Add(FindCorridorAccessPoint(new Vector2(Width * 0.32f, ControlPoint.y - seamOff), new Vector2(64f, 64f), strictBlocked, relaxedBlocked, preferInner: false));

            // Hemisphere-guided flank costs prevent flank roads from hijacking or hugging the central trunk!
            float[] northCost = BuildFlankCostGrid(baseCost, primaryMask, primaryNearMask, +1f);
            float[] southCost = BuildFlankCostGrid(baseCost, primaryMask, primaryNearMask, -1f);

            var northWaypoints = new List<Vector2>(northSites.Count + 2) { _westBase };
            northWaypoints.AddRange(northSites);
            northWaypoints.Add(northSeam);
            RouteAndCommitChain(northWaypoints, !city, turnPenalty, strictBlocked, relaxedBlocked,
                northCost, flankNearMask, secondaryHalfW, city, allRoadMask, primaryMask, primaryNearMask);

            var southWaypoints = new List<Vector2>(southSites.Count + 2) { _westBase };
            southWaypoints.AddRange(southSites);
            southWaypoints.Add(southSeam);
            RouteAndCommitChain(southWaypoints, !city, turnPenalty, strictBlocked, relaxedBlocked,
                southCost, flankNearMask, secondaryHalfW, city, allRoadMask, primaryMask, primaryNearMask);

            // 3. CENTRAL CROSS-AVENUE: North Seam -> Control Point (mirrored to South Seam -> Control Point!)
            var crossCost = (float[])baseCost.Clone();
            for (int gy = 0; gy < RouteRows; gy++)
            {
                int rowOff = gy * RouteCols;
                for (int gx = 0; gx < RouteCols; gx++)
                {
                    float px = (gx + 0.5f) * RouteCell;
                    crossCost[rowOff + gx] += Mathf.Max(0f, (Mathf.Abs(px - ControlPoint.x) - 110f) / 90f) * 1.8f;
                }
            }
            var crossCells = RouteCellsBetween(northSeam, ControlPoint, !city, turnPenalty,
                strictBlocked, relaxedBlocked, crossCost, allRoadMask, flankNearMask, nearPenalty: 4.0f, shareCost: 0.45f);
            if (crossCells != null && crossCells.Count >= 2)
                CommitMirroredCellPath(crossCells, northSeam, ControlPoint, secondaryHalfW, isPrimary: false, city: city,
                    allRoadMask, primaryMask, primaryNearMask, flankNearMask);

            // Detect real street intersections / junctions for crisp City crosswalks & dash breaks!
            RebuildRoadIntersections(city);
        }

        private float[] BuildFlankCostGrid(float[] baseCost, bool[] primaryMask, bool[] primaryNearMask, float signY)
        {
            var fc = (float[])baseCost.Clone();
            for (int gy = 0; gy < RouteRows; gy++)
            {
                float py = (gy + 0.5f) * RouteCell;
                int rowOff = gy * RouteCols;
                for (int gx = 0; gx < RouteCols; gx++)
                {
                    int idx = rowOff + gx;
                    float px = (gx + 0.5f) * RouteCell;
                    if (primaryMask[idx] && Vector2.Distance(new Vector2(px, py), _westBase) > 140f)
                        fc[idx] += 6.5f;
                    else if (primaryNearMask[idx])
                        fc[idx] += 5.0f;

                    if (px > _westBase.x + 150f)
                    {
                        if (signY > 0f && py < ControlPoint.y + 120f) fc[idx] += 7.0f;
                        else if (signY < 0f && py > ControlPoint.y - 120f) fc[idx] += 7.0f;
                    }
                }
            }
            return fc;
        }

        private static List<Vector2> FilterSpacedWaypoints(List<Vector2> sortedByX, float minDeltaX)
        {
            var result = new List<Vector2>(sortedByX.Count);
            for (int i = 0; i < sortedByX.Count; i++)
            {
                if (result.Count == 0 || sortedByX[i].x - result[^1].x >= minDeltaX)
                    result.Add(sortedByX[i]);
            }
            return result;
        }

        private void RouteAndCommitChain(List<Vector2> waypoints, bool allowDiagonal, float turnPenalty,
            bool[] strictBlocked, bool[] relaxedBlocked, float[] costGrid, bool[] flankNearMask,
            float halfWidth, bool city, bool[] allRoadMask, bool[] primaryMask, bool[] primaryNearMask)
        {
            var combined = new List<int>();
            for (int i = 0; i < waypoints.Count - 1; i++)
            {
                var leg = RouteCellsBetween(waypoints[i], waypoints[i + 1], allowDiagonal, turnPenalty,
                    strictBlocked, relaxedBlocked, costGrid, null, flankNearMask, nearPenalty: 4.5f, shareCost: 0.35f);
                if (leg == null || leg.Count < 2) continue;
                int startIdx = (combined.Count > 0 && leg[0] == combined[^1]) ? 1 : 0;
                for (int k = startIdx; k < leg.Count; k++)
                    combined.Add(leg[k]);
            }
            if (combined.Count < 2) return;
            combined = CollapseBacktrackLoops(combined);
            CommitMirroredCellPath(combined, waypoints[0], waypoints[^1], halfWidth, isPrimary: false, city: city,
                allRoadMask, primaryMask, primaryNearMask, flankNearMask);
        }

        private static List<int> CollapseBacktrackLoops(List<int> cells)
        {
            // Eliminate any 1-to-3 cell U-turn stubs caused where chained legs meet a waypoint!
            var stack = new List<int>(cells.Count);
            for (int i = 0; i < cells.Count; i++)
            {
                int c = cells[i];
                int revisit = -1;
                int lookBack = Mathf.Max(0, stack.Count - 6);
                for (int j = stack.Count - 1; j >= lookBack; j--)
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
            bool[] strictBlocked, bool[] relaxedBlocked, float[] baseCost, bool[] shareRoadMask, bool[] nearRoadMask,
            float nearPenalty = 5.5f, float shareCost = 0.35f)
        {
            int startCell = FindNearestOpenCell(from, strictBlocked);
            int goalCell = FindNearestOpenCell(to, strictBlocked);
            List<int> cellPath = null;
            if (startCell >= 0 && goalCell >= 0)
                cellPath = RunGridAStar(startCell, goalCell, allowDiagonal, turnPenalty, strictBlocked, baseCost,
                    shareRoadMask, nearRoadMask, nearPenalty, shareCost);

            if (cellPath == null)
            {
                startCell = FindNearestOpenCell(from, relaxedBlocked);
                goalCell = FindNearestOpenCell(to, relaxedBlocked);
                if (startCell >= 0 && goalCell >= 0)
                    cellPath = RunGridAStar(startCell, goalCell, allowDiagonal, turnPenalty, relaxedBlocked, baseCost,
                        shareRoadMask, nearRoadMask, nearPenalty, shareCost);
            }

            return (cellPath != null && cellPath.Count >= 2) ? cellPath : null;
        }

        private void CommitMirroredCellPath(List<int> cellPath, Vector2 exactFrom, Vector2 exactTo,
            float halfWidth, bool isPrimary, bool city,
            bool[] allRoadMask, bool[] primaryMask, bool[] primaryNearMask, bool[] flankNearMask)
        {
            if (cellPath == null || cellPath.Count < 2) return;
            var worldPts = new List<Vector2>(cellPath.Count);
            for (int i = 0; i < cellPath.Count; i++)
            {
                int c = cellPath[i];
                int mc = MirrorRouteCell(c);
                allRoadMask[c] = true;
                allRoadMask[mc] = true;
                if (isPrimary)
                {
                    primaryMask[c] = true;
                    primaryMask[mc] = true;
                    MarkNearRoad(c, primaryNearMask, radius: 4);
                    MarkNearRoad(mc, primaryNearMask, radius: 4);
                }
                else
                {
                    MarkNearRoad(c, flankNearMask, radius: 3);
                    MarkNearRoad(mc, flankNearMask, radius: 3);
                }
                worldPts.Add(RouteCellCenter(c % RouteCols, c / RouteCols));
            }
            if (Vector2.Distance(worldPts[0], exactFrom) < 36f) worldPts[0] = exactFrom;
            if (Vector2.Distance(worldPts[^1], exactTo) < 36f) worldPts[^1] = exactTo;
            var pts = SimplifyAndSmoothPath(worldPts, orthogonal: city, checkClearance: 12f);
            AddMirroredRoadPolyline(pts, halfWidth, isPrimary);
        }

        private static void MarkNearRoad(int cell, bool[] nearRoadMask, int radius)
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

        private void RebuildRoadIntersections(bool city)
        {
            _intersections.Clear();
            _intersections.Add(ControlPoint);
            if (!city) return;

            // Collect every true orthogonal junction (L-corner, T-junction, or 4-way crossing) between City streets!
            for (int i = 0; i < _roads.Count; i++)
            {
                var s1 = _roads[i];
                for (int j = i + 1; j < _roads.Count; j++)
                {
                    var s2 = _roads[j];
                    if (Mathf.Abs(Vector2.Dot(s1.Dir, s2.Dir)) > 0.35f) continue;

                    // Check segment-segment intersection or endpoint touch within 18px
                    if (TrySegmentIntersection(s1.A, s1.B, s2.A, s2.B, 16f, out Vector2 hit))
                        AddUniqueIntersection(hit);
                }
            }
        }

        private void AddUniqueIntersection(Vector2 pt)
        {
            for (int i = 0; i < _intersections.Count; i++)
                if ((_intersections[i] - pt).sqrMagnitude < 28f * 28f) return;
            _intersections.Add(pt);
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

        private static List<int> RunGridAStar(int start, int goal, bool allowDiagonal, float turnPenalty,
            bool[] blocked, float[] baseCost, bool[] roadMask, bool[] nearRoadMask,
            float nearPenalty = 5.5f, float shareCost = 0.35f)
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

            // Flat binary min-heap for fast A* (~0.4ms per route)
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
                    float terrainMult = (roadMask != null && roadMask[next])
                        ? shareCost
                        : (nearRoadMask != null && nearRoadMask[next] ? baseCost[next] + nearPenalty : baseCost[next]);
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

        private static int FindNearestOpenCell(Vector2 pos, bool[] blocked)
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

        private Vector2 FindCorridorAccessPoint(Vector2 center, Vector2 groundSize, bool[] strictBlocked, bool[] relaxedBlocked, bool preferInner)
        {
            // Pick a clean roadside point along the horizontal corridor edge so flank roads sweep smoothly past each facility!
            float signY = center.y >= ControlPoint.y ? 1f : -1f;
            float offY = Mathf.Clamp(groundSize.y * 0.45f, 44f, 84f);
            float offX = Mathf.Clamp(groundSize.x * 0.45f, 44f, 84f);
            Vector2[] candidates = preferInner
                ? new[]
                {
                    new Vector2(center.x, center.y - signY * offY),
                    new Vector2(center.x, center.y + signY * offY),
                    new Vector2(center.x + offX, center.y),
                    new Vector2(center.x - offX, center.y),
                    center
                }
                : new[]
                {
                    new Vector2(center.x, center.y + signY * offY),
                    new Vector2(center.x, center.y - signY * offY),
                    new Vector2(center.x + offX, center.y),
                    new Vector2(center.x - offX, center.y),
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
            for (int i = 0; i < _roads.Count && placed < maxRoadside; i += 2)
            {
                var seg = _roads[i];
                if (seg.Length < 88f) continue;
                Vector2 mid = (seg.A + seg.B) * 0.5f;
                // Keep west half only (TryPlaceMirrored automatically mirrors onto east half!)
                if (mid.x > ControlPoint.x - 40f) continue;

                // Skip intersections & stream bridges
                bool nearInter = false;
                for (int k = 0; k < _intersections.Count; k++)
                    if (Vector2.Distance(mid, _intersections[k]) < 68f) { nearInter = true; break; }
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
            for (int i = 0; i < points.Count - 1; i++)
            {
                AddRoadSegment(points[i], points[i + 1], halfWidth, isPrimary);
                AddRoadSegment(Mirror(points[i]), Mirror(points[i + 1]), halfWidth, isPrimary);
            }
        }

        private void AddRoadSegment(Vector2 a, Vector2 b, float halfWidth, bool isPrimary)
        {
            if ((b - a).sqrMagnitude < 1f) return;
            _roads.Add(new RoadSegment(a, b, halfWidth, isPrimary));
        }

        private static RegionalScenario PickRegionalScenario(BattleMap map, System.Random random) => map switch
        {
            BattleMap.City => random.Next(3) switch
            {
                0 => RegionalScenario.CityRailFreight,
                1 => RegionalScenario.CityDowntownAvenue,
                _ => RegionalScenario.CityCommandCitadel
            },
            BattleMap.Forest => random.Next(4) switch
            {
                0 => RegionalScenario.ForestRuralValley,
                1 => RegionalScenario.ForestAutumnHighland,
                2 => RegionalScenario.ForestWetlandGrove,
                _ => RegionalScenario.ForestAlpineRidge
            },
            _ => random.Next(3) switch
            {
                0 => RegionalScenario.DesertAirbase,
                1 => RegionalScenario.DesertOasisRoute,
                _ => RegionalScenario.DesertCanyonFront
            }
        };

        private static bool IsRidgeDistrict(BattlefieldDistrict district)
        {
            if (district.Cover == null || district.Cover.Length == 0) return false;
            foreach (var piece in district.Cover)
                if (piece.Kind is not (BattlefieldCoverKind.Ridge or BattlefieldCoverKind.RidgeVariant))
                    return false;
            return true;
        }

        private static bool IsCornerDetourDistrict(BattlefieldDistrict district) =>
            district.Center.x < 170f && (district.Center.y < 160f || district.Center.y > 680f);

        private static bool IsNaturalArchetype(ZoneArchetype archetype) => archetype is
            ZoneArchetype.DesertBadlands or
            ZoneArchetype.DesertOasisBasin or
            ZoneArchetype.DesertCanyonSpires or
            ZoneArchetype.CityBotanicalPark or
            ZoneArchetype.CityMemorialPark or
            ZoneArchetype.AncientWoodland or
            ZoneArchetype.AutumnHighlandWoods or
            ZoneArchetype.WetlandMangroveGrove or
            ZoneArchetype.AlpineSnowWoods or
            ZoneArchetype.RockyOutcropField;

        private static ZoneArchetype[] AssignDistrictArchetypes(RegionalScenario scenario, int count, System.Random random)
        {
            ZoneArchetype[] major = scenario switch
            {
                RegionalScenario.DesertAirbase => new[]
                {
                    ZoneArchetype.AirDefenseBase,
                    ZoneArchetype.DesertBadlands,
                    ZoneArchetype.CommandTentCamp,
                    ZoneArchetype.RockyOutcropField
                },
                RegionalScenario.DesertOasisRoute => new[]
                {
                    ZoneArchetype.SolarOutpost,
                    ZoneArchetype.DesertOasisBasin,
                    ZoneArchetype.SupplyDepotYard,
                    ZoneArchetype.UrbanFuelAvenue
                },
                RegionalScenario.DesertCanyonFront => new[]
                {
                    ZoneArchetype.ArtilleryBattery,
                    ZoneArchetype.DesertCanyonSpires,
                    ZoneArchetype.SupplyDepotYard,
                    ZoneArchetype.DesertBadlands
                },
                RegionalScenario.CityRailFreight => new[]
                {
                    ZoneArchetype.FreightRailYard,
                    ZoneArchetype.PowerUtilityWorks,
                    ZoneArchetype.UrbanResidentialBlock,
                    ZoneArchetype.CityBotanicalPark
                },
                RegionalScenario.CityDowntownAvenue => new[]
                {
                    ZoneArchetype.UrbanFuelAvenue,
                    ZoneArchetype.UrbanResidentialBlock,
                    ZoneArchetype.CityBotanicalPark,
                    ZoneArchetype.UrbanResidentialBlock
                },
                RegionalScenario.CityCommandCitadel => new[]
                {
                    ZoneArchetype.AirDefenseBase,
                    ZoneArchetype.CommandTentCamp,
                    ZoneArchetype.UrbanResidentialBlock,
                    ZoneArchetype.CityMemorialPark
                },
                RegionalScenario.ForestRuralValley => new[]
                {
                    ZoneArchetype.RuralWindmillFarm,
                    ZoneArchetype.AncientWoodland,
                    ZoneArchetype.TimberLoggingCamp,
                    ZoneArchetype.ArtilleryBattery
                },
                RegionalScenario.ForestAutumnHighland => new[]
                {
                    ZoneArchetype.PowerUtilityWorks,
                    ZoneArchetype.AutumnHighlandWoods,
                    ZoneArchetype.RockyOutcropField,
                    ZoneArchetype.CommandTentCamp
                },
                RegionalScenario.ForestWetlandGrove => new[]
                {
                    ZoneArchetype.WetlandMangroveGrove,
                    ZoneArchetype.TimberLoggingCamp,
                    ZoneArchetype.SupplyDepotYard,
                    ZoneArchetype.ArtilleryBattery
                },
                _ => new[] // ForestAlpineRidge
                {
                    ZoneArchetype.AlpineSnowWoods,
                    ZoneArchetype.AirDefenseBase,
                    ZoneArchetype.RockyOutcropField,
                    ZoneArchetype.TimberLoggingCamp
                }
            };

            for (int i = major.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (major[i], major[j]) = (major[j], major[i]);
            }

            ZoneArchetype rearFlank = scenario switch
            {
                RegionalScenario.DesertAirbase or RegionalScenario.CityCommandCitadel or RegionalScenario.ForestAutumnHighland => ZoneArchetype.RoadCheckpoint,
                RegionalScenario.DesertOasisRoute => ZoneArchetype.DesertOasisBasin,
                RegionalScenario.DesertCanyonFront => ZoneArchetype.RockyOutcropField,
                RegionalScenario.CityRailFreight or RegionalScenario.CityDowntownAvenue => ZoneArchetype.RoadCheckpoint,
                RegionalScenario.ForestRuralValley => ZoneArchetype.AncientWoodland,
                RegionalScenario.ForestWetlandGrove => ZoneArchetype.WetlandMangroveGrove,
                _ => ZoneArchetype.AlpineSnowWoods
            };

            ZoneArchetype forwardPocket = scenario switch
            {
                RegionalScenario.DesertAirbase or RegionalScenario.DesertCanyonFront or RegionalScenario.CityDowntownAvenue or RegionalScenario.ForestAutumnHighland
                    => ZoneArchetype.BattleWreckScar,
                RegionalScenario.DesertOasisRoute => ZoneArchetype.DesertCanyonSpires,
                RegionalScenario.CityRailFreight or RegionalScenario.CityCommandCitadel => ZoneArchetype.RoadCheckpoint,
                RegionalScenario.ForestRuralValley or RegionalScenario.ForestWetlandGrove => ZoneArchetype.BattleWreckScar,
                _ => ZoneArchetype.RockyOutcropField
            };

            var result = new ZoneArchetype[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = i < major.Length ? major[i]
                    : i == major.Length ? rearFlank
                    : forwardPocket;
            }
            return result;
        }

        private void PlaceRidgeDistrict(BattlefieldDistrict district, Vector2 center, float angle, float reflection,
            BattlefieldTheme theme, List<(Vector2 Position, float Radius, float Angle)> ridgeAnchors)
        {
            Vector2 right = Direction(angle);
            foreach (var piece in district.Cover)
            {
                var prefab = theme.GetCover(piece.Kind);
                if (prefab == null) continue;
                Vector2 offset = right * piece.Offset.x + Vector2.Perpendicular(right) * piece.Offset.y;
                offset.y *= reflection;
                Vector2 position = Vector2Int.RoundToInt(center + offset);
                float rotation = (angle + piece.Angle) * reflection;
                float approxRadius = Mathf.Max(prefab.meta.width, prefab.meta.length) * 0.5f;

                // Keep ridges cleanly off the main road corridors & stream channel.
                if (IsOnRoad(position, approxRadius * 0.65f, primaryOnly: false) || IsInWater(position, approxRadius * 0.45f))
                {
                    if (!TryNudgeOffRoads(ref position, approxRadius * 0.65f)) continue;
                }

                var size = new Vector2(prefab.meta.width + RidgeFootMargin * 2f,
                    prefab.meta.length + RidgeFootMargin * 2f);
                _ridgeGround.Add(new DistrictGround(position, size, Direction(rotation), false));
                AddMirrored(prefab, position, rotation);
                ridgeAnchors.Add((position, approxRadius, rotation));
            }
        }

        private void PlaceCornerDetourDistrict(BattlefieldDistrict district, Vector2 center, float angle, float reflection,
            BattleMap map, BattlefieldTheme theme, BattleSpawnAssets assets, System.Random random)
        {
            Vector2 right = Direction(angle);
            if (district.GroundSize.x > 0f && district.GroundSize.y > 0f)
                _districtGround.Add(new DistrictGround(center, district.GroundSize, Direction((map == BattleMap.City ? 0f : angle) * reflection), false));

            foreach (var piece in district.Cover)
            {
                Vector2 offset = right * piece.Offset.x + Vector2.Perpendicular(right) * piece.Offset.y;
                offset.y *= reflection;
                Vector2 position = Vector2Int.RoundToInt(center + offset);
                float rotation = map == BattleMap.City ? 0f : (angle + piece.Angle) * reflection;
                var prefab = theme.GetCover(piece.Kind);
                if (prefab == null) continue;
                float r = Mathf.Max(prefab.meta.width, prefab.meta.length) * 0.5f;
                if (IsOnRoad(position, r * 0.55f, primaryOnly: false))
                    TryNudgeOffRoads(ref position, r * 0.55f);
                TryPlaceMirrored(prefab, position, rotation, clearance: 6f, avoidRoads: true);
            }

            if (map == BattleMap.City)
            {
                string propA = _scenario == RegionalScenario.CityDowntownAvenue ? "abandoned_car" : "guard_booth";
                string propB = _scenario == RegionalScenario.CityRailFreight ? "oil_drum_stack" : "tank_trap";
                TryPlaceMirrored(GetProp(assets, propA), center + new Vector2(46f, 28f), 0f, 8f);
                TryPlaceMirrored(GetProp(assets, propB), center + new Vector2(-48f, -26f), 0f, 8f);
            }
            else
            {
                string[] vergePool = _scenario switch
                {
                    RegionalScenario.DesertAirbase => new[] { "rock_pillar", "desert_rock", "desert_scrub" },
                    RegionalScenario.DesertOasisRoute => new[] { "cactus_cluster", "palm_tree", "desert_scrub" },
                    RegionalScenario.DesertCanyonFront => new[] { "cactus_cluster", "dead_tree", "desert_rock" },
                    RegionalScenario.ForestRuralValley => new[] { "birch_clump", "hay_bale", "bush_cluster" },
                    RegionalScenario.ForestAutumnHighland => new[] { "autumn_tree", "basalt_rocks", "dead_tree" },
                    RegionalScenario.ForestWetlandGrove => new[] { "bamboo_grove", "reed_marsh", "moss_rocks" },
                    _ => new[] { "snow_pine", "basalt_rocks", "dead_tree" }
                };
                ScatterOrganicDrift(center, 46f, 84f, 2, vergePool, assets, random);
            }
        }

        // =====================================================================
        // ARTIFICIAL ZONES: Strict axis-locked blueprints & functional sets
        // =====================================================================
        private void PopulateArtificialDistrict(BattlefieldDistrict district, ZoneArchetype archetype, Vector2 center,
            float baseAngle, float reflection, BattleMap map, BattlefieldTheme theme, BattleSpawnAssets assets, System.Random random)
        {
            Vector2 right = Direction(baseAngle);
            float axisAngle = baseAngle * reflection;
            Vector2 axisRight = Direction(axisAngle);
            Vector2 axisUp = Vector2.Perpendicular(axisRight);

            Vector2 toFront = (ControlPoint - center).normalized;
            Vector2 frontAxis = Mathf.Abs(Vector2.Dot(toFront, axisRight)) >= Mathf.Abs(Vector2.Dot(toFront, axisUp))
                ? axisRight * Mathf.Sign(Vector2.Dot(toFront, axisRight))
                : axisUp * Mathf.Sign(Vector2.Dot(toFront, axisUp));
            Vector2 frontPerp = Vector2.Perpendicular(frontAxis);
            float frontLineAngle = Mathf.Atan2(frontPerp.y, frontPerp.x) * Mathf.Rad2Deg;
            float frontAimAngle = Mathf.Atan2(frontAxis.y, frontAxis.x) * Mathf.Rad2Deg;

            Vector2? primaryAnchor = null;

            foreach (var piece in district.Cover)
            {
                Vector2 offset = right * piece.Offset.x + Vector2.Perpendicular(right) * piece.Offset.y;
                offset.y *= reflection;
                Vector2 position = Vector2Int.RoundToInt(center + offset);

                if (piece.Kind == BattlefieldCoverKind.Wall)
                {
                    var wallPrefab = theme.GetCover(piece.Kind);
                    if (wallPrefab != null)
                    {
                        float wallRot = map == BattleMap.City
                            ? Mathf.Round(((baseAngle + piece.Angle) * reflection) / 90f) * 90f
                            : (baseAngle + piece.Angle) * reflection;
                        float wr = Mathf.Max(wallPrefab.meta.width, wallPrefab.meta.length) * 0.45f;
                        // Never drop a permanent wall across an asphalt boulevard or main highway!
                        if (!IsOnRoad(position, wr, primaryOnly: false) || TryNudgeOffRoads(ref position, wr))
                            TryPlaceMirrored(wallPrefab, position, wallRot, clearance: 4f, avoidRoads: true);
                    }
                    continue;
                }

                float localSnap = Mathf.Round(piece.Angle / 90f) * 90f;
                float rotation = archetype == ZoneArchetype.BattleWreckScar
                    ? axisAngle + random.Next(-28, 29)
                    : axisAngle + localSnap * reflection;
                if (map == BattleMap.City && archetype != ZoneArchetype.BattleWreckScar)
                    rotation = Mathf.Round(rotation / 90f) * 90f;

                DestructibleProp prefab = ResolveArtificialSlot(archetype, piece.Kind, theme, assets, random);
                if (prefab == null) continue;

                if (prefab.kind is "sam_launcher" or "field_artillery")
                    rotation = frontAimAngle;

                float radius = Mathf.Max(prefab.meta.width, prefab.meta.length) * 0.5f;
                bool allowRoadshoulder = archetype is ZoneArchetype.RoadCheckpoint or ZoneArchetype.BattleWreckScar;
                if (!allowRoadshoulder && IsOnRoad(position, radius * 0.55f, primaryOnly: false))
                    TryNudgeOffRoads(ref position, radius * 0.55f);

                if (TryPlaceMirrored(prefab, position, rotation, clearance: 6f, avoidRoads: !allowRoadshoulder))
                {
                    if (primaryAnchor == null || piece.Kind == BattlefieldCoverKind.Landmark)
                        primaryAnchor = position;
                }
            }

            Vector2 anchor = primaryAnchor ?? center;

            switch (archetype)
            {
                case ZoneArchetype.AirDefenseBase:
                    PlaceFiringEmplacement(GetProp(assets, "sam_launcher"), GetProp(assets, "ammo_depot"),
                        anchor + frontAxis * 42f - frontPerp * 26f, frontAxis, frontAimAngle);
                    PlaceUtilityRow(GetProp(assets, "generator_unit"), GetProp(assets, "radio_mast"),
                        anchor - frontAxis * 48f + frontPerp * 32f, axisAngle, spacing: 32f);
                    PlaceDefensiveLine(GetProp(assets, "concrete_wall"), GetProp(assets, "hesco_barrier"),
                        center + frontAxis * 66f, frontPerp, frontLineAngle, count: 2, step: 48f);
                    break;

                case ZoneArchetype.CommandTentCamp:
                    PlaceParallelRow(GetProp(assets, "military_tent"), GetProp(assets, "military_tent"),
                        anchor + frontPerp * 46f, axisAngle, spacing: 34f);
                    PlaceUtilityRow(GetProp(assets, "ammo_depot"), GetProp(assets, "radio_mast"),
                        anchor - frontAxis * 46f - frontPerp * 30f, axisAngle, spacing: 32f);
                    PlaceDefensiveLine(GetProp(assets, "sandbag_wall"), GetProp(assets, "tank_trap"),
                        center + frontAxis * 60f, frontPerp, frontLineAngle, count: 3, step: 32f);
                    break;

                case ZoneArchetype.RoadCheckpoint:
                    PlaceCheckpointAssembly(GetProp(assets, "checkpoint_gate"), GetProp(assets, "guard_booth"),
                        GetProp(assets, "concrete_wall"), center + frontAxis * 34f, frontPerp, frontLineAngle);
                    PlaceAlignedAccent(GetProp(assets, _map == BattleMap.City ? "city_barricade" : "hesco_barrier"),
                        center, -frontAxis * 34f + frontPerp * 26f, -frontAxis * 34f - frontPerp * 26f, frontLineAngle);
                    break;

                case ZoneArchetype.SolarOutpost:
                    PlaceParallelRow(GetProp(assets, "solar_array"), GetProp(assets, "solar_array"),
                        anchor + axisRight * 50f, axisAngle, spacing: 30f);
                    PlaceAlignedAccent(GetProp(assets, "water_tower"),
                        anchor, -frontAxis * 48f + frontPerp * 36f, -frontAxis * 48f - frontPerp * 36f, axisAngle);
                    PlaceAlignedAccent(GetProp(assets, "radar_station"),
                        anchor, -frontAxis * 46f - frontPerp * 34f, -frontAxis * 46f + frontPerp * 34f, axisAngle);
                    PlaceDefensiveLine(GetProp(assets, "hesco_barrier"), GetProp(assets, "sandbag_wall"),
                        center + frontAxis * 60f, frontPerp, frontLineAngle, count: 2, step: 42f);
                    break;

                case ZoneArchetype.FreightRailYard:
                    PlaceParallelRow(GetProp(assets, "rail_boxcar"), GetProp(assets, "cargo_container"),
                        anchor + axisRight * 58f, axisAngle, spacing: 26f);
                    PlaceUtilityRow(GetProp(assets, "forklift_wreck"), GetProp(assets, "supply_crate"),
                        anchor - axisRight * 54f, axisAngle, spacing: 26f);
                    PlaceAlignedAccent(GetProp(assets, "steel_silo"),
                        anchor, -frontAxis * 52f + frontPerp * 36f, -frontAxis * 52f - frontPerp * 36f, axisAngle);
                    break;

                case ZoneArchetype.SupplyDepotYard:
                    PlaceParallelRow(GetProp(assets, "cargo_container"), GetProp(assets, "cargo_container"),
                        anchor + frontPerp * 50f, axisAngle, spacing: 26f);
                    PlaceUtilityRow(GetProp(assets, "oil_drum_stack"), GetProp(assets, "fuel_tank"),
                        anchor - frontPerp * 48f, axisAngle, spacing: 32f);
                    PlaceAlignedAccent(GetProp(assets, _map == BattleMap.Forest ? "watch_tower" : "forklift_wreck"),
                        anchor, -frontAxis * 44f + frontPerp * 26f, -frontAxis * 44f - frontPerp * 26f, axisAngle);
                    PlaceDefensiveLine(GetProp(assets, "sandbag_wall"), GetProp(assets, "hesco_barrier"),
                        center + frontAxis * 60f, frontPerp, frontLineAngle, count: 2, step: 42f);
                    break;

                case ZoneArchetype.PowerUtilityWorks:
                    PlaceUtilityRow(GetProp(assets, "transformer_substation"), GetProp(assets, "generator_unit"),
                        anchor + frontPerp * 48f, axisAngle, spacing: 36f);
                    PlaceAlignedAccent(GetProp(assets, _map == BattleMap.City ? "water_tower" : "radar_station"),
                        anchor, -frontAxis * 48f - frontPerp * 36f, -frontAxis * 48f + frontPerp * 36f, axisAngle);
                    PlaceAlignedAccent(GetProp(assets, "oil_drum_stack"),
                        anchor, -frontAxis * 42f + frontPerp * 32f, -frontAxis * 42f - frontPerp * 32f, axisAngle);
                    PlaceDefensiveLine(GetProp(assets, "concrete_wall"), GetProp(assets, "tank_trap"),
                        center + frontAxis * 62f, frontPerp, frontLineAngle, count: 2, step: 48f);
                    break;

                case ZoneArchetype.ArtilleryBattery:
                    PlaceFiringEmplacement(GetProp(assets, "field_artillery"), GetProp(assets, "ammo_depot"),
                        anchor + frontAxis * 34f, frontAxis, frontAimAngle);
                    PlaceAlignedAccent(GetProp(assets, "watch_tower"),
                        anchor, -frontAxis * 48f + frontPerp * 34f, -frontAxis * 48f - frontPerp * 34f, axisAngle);
                    PlaceDefensiveLine(GetProp(assets, "sandbag_wall"), GetProp(assets, "tank_trap"),
                        center + frontAxis * 62f, frontPerp, frontLineAngle, count: 3, step: 34f);
                    break;

                case ZoneArchetype.UrbanFuelAvenue:
                    PlaceRoadsideStrip(
                        GetProp(assets, "fuel_pump_island"),
                        GetProp(assets, _map == BattleMap.City ? "bus_stop" : "abandoned_car"),
                        GetProp(assets, _map == BattleMap.City ? "street_kiosk" : "oil_drum_stack"),
                        anchor, axisRight, axisUp, axisAngle);
                    PlaceAlignedAccent(GetProp(assets, "abandoned_car"),
                        center, frontAxis * 46f + frontPerp * 24f, frontAxis * 46f - frontPerp * 24f, axisAngle);
                    break;

                case ZoneArchetype.UrbanResidentialBlock:
                    PlaceDefensiveLine(GetProp(assets, "brick_wall"), GetProp(assets, "city_barricade"),
                        center + frontAxis * 62f, frontPerp, frontLineAngle, count: 2, step: 46f);
                    PlaceAlignedAccent(GetProp(assets, random.Next(2) == 0 ? "street_kiosk" : "bus_stop"),
                        anchor, -frontAxis * 46f + frontPerp * 36f, -frontAxis * 46f - frontPerp * 36f, axisAngle);
                    PlaceAlignedAccent(GetProp(assets, "abandoned_car"),
                        anchor, -frontAxis * 44f - frontPerp * 34f, -frontAxis * 44f + frontPerp * 34f, axisAngle + 90f);
                    break;

                case ZoneArchetype.RuralWindmillFarm:
                    PlaceUtilityRow(GetProp(assets, "wooden_shed"), GetProp(assets, "hay_bale"),
                        anchor + axisRight * 50f, axisAngle, spacing: 34f);
                    PlaceAlignedAccent(GetProp(assets, "stone_well"),
                        anchor, -frontAxis * 38f + frontPerp * 32f, -frontAxis * 38f - frontPerp * 32f, axisAngle);
                    PlaceDefensiveLine(GetProp(assets, "brick_wall"), GetProp(assets, "hay_bale"),
                        center + frontAxis * 56f, frontPerp, frontLineAngle, count: 2, step: 44f);
                    break;

                case ZoneArchetype.TimberLoggingCamp:
                    PlaceParallelRow(GetProp(assets, "timber_stack"), GetProp(assets, "timber_stack"),
                        anchor + axisRight * 54f, axisAngle, spacing: 24f);
                    PlaceUtilityRow(GetProp(assets, "wooden_shed"), GetProp(assets, _scenario == RegionalScenario.ForestRuralValley ? "forklift_wreck" : "fuel_tank"),
                        anchor - frontAxis * 46f + frontPerp * 30f, axisAngle, spacing: 32f);
                    PlaceAlignedAccent(GetProp(assets, _scenario == RegionalScenario.ForestAlpineRidge ? "steel_silo" : "watch_tower"),
                        anchor, -frontAxis * 46f - frontPerp * 36f, -frontAxis * 46f + frontPerp * 36f, axisAngle);
                    break;

                case ZoneArchetype.BattleWreckScar:
                    PlaceWreckSetpiece(anchor, frontAxis, frontPerp, frontLineAngle, assets, random);
                    break;
            }
        }

        private DestructibleProp ResolveArtificialSlot(ZoneArchetype archetype, BattlefieldCoverKind kind,
            BattlefieldTheme theme, BattleSpawnAssets assets, System.Random random)
        {
            if (kind == BattlefieldCoverKind.Wall)
                return theme.GetCover(kind);

            return archetype switch
            {
                ZoneArchetype.AirDefenseBase => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, "radar_dome"),
                    BattlefieldCoverKind.Building => GetProp(assets, _map == BattleMap.City ? "city_blockhouse" : (_map == BattleMap.Forest ? "water_tower" : "desert_compound")),
                    _ => GetProp(assets, "hesco_barrier")
                },
                ZoneArchetype.CommandTentCamp => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, _map == BattleMap.City ? "city_blockhouse" : (_map == BattleMap.Forest ? "forest_cabin" : "desert_bunker")),
                    BattlefieldCoverKind.Building => GetProp(assets, "military_tent"),
                    _ => GetProp(assets, "sandbag_wall")
                },
                ZoneArchetype.RoadCheckpoint => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, _map == BattleMap.City ? "city_blockhouse" : (_map == BattleMap.Forest ? "forest_cabin" : "desert_ruin")),
                    BattlefieldCoverKind.Building => GetProp(assets, "guard_booth"),
                    _ => GetProp(assets, _map == BattleMap.City ? "city_barricade" : "sandbag_wall")
                },
                ZoneArchetype.SolarOutpost => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, "desert_compound"),
                    BattlefieldCoverKind.Building => GetProp(assets, "solar_array"),
                    _ => GetProp(assets, "hesco_barrier")
                },
                ZoneArchetype.FreightRailYard => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, "city_factory"),
                    BattlefieldCoverKind.Building => GetProp(assets, "rail_boxcar"),
                    _ => GetProp(assets, "city_barricade")
                },
                ZoneArchetype.SupplyDepotYard => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, _map == BattleMap.Forest ? "forest_cabin" : "desert_bunker"),
                    BattlefieldCoverKind.Building => GetProp(assets, "cargo_container"),
                    _ => GetProp(assets, "hesco_barrier")
                },
                ZoneArchetype.PowerUtilityWorks => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, _map == BattleMap.City ? "steel_silo" : "desert_bunker"),
                    BattlefieldCoverKind.Building => GetProp(assets, "transformer_substation"),
                    _ => GetProp(assets, "concrete_wall")
                },
                ZoneArchetype.ArtilleryBattery => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, _map == BattleMap.Desert ? "desert_bunker" : "field_artillery"),
                    BattlefieldCoverKind.Building => GetProp(assets, "field_artillery"),
                    _ => GetProp(assets, "sandbag_wall")
                },
                ZoneArchetype.UrbanFuelAvenue => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, _map == BattleMap.City ? "city_apartment" : "desert_ruin"),
                    BattlefieldCoverKind.Building => GetProp(assets, "fuel_pump_island"),
                    _ => GetProp(assets, _map == BattleMap.City ? "city_barricade" : "sandbag_wall")
                },
                ZoneArchetype.UrbanResidentialBlock => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, "city_apartment"),
                    BattlefieldCoverKind.Building => GetProp(assets, "city_blockhouse"),
                    _ => GetProp(assets, "brick_wall")
                },
                ZoneArchetype.RuralWindmillFarm => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, "windmill_tower"),
                    BattlefieldCoverKind.Building => GetProp(assets, "wooden_shed"),
                    _ => GetProp(assets, "hay_bale")
                },
                ZoneArchetype.TimberLoggingCamp => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, "forest_cabin"),
                    BattlefieldCoverKind.Building => GetProp(assets, "timber_stack"),
                    _ => GetProp(assets, "sandbag_wall")
                },
                ZoneArchetype.BattleWreckScar => kind switch
                {
                    BattlefieldCoverKind.Landmark or BattlefieldCoverKind.Building => GetProp(assets, PickScenarioWreck(random)),
                    _ => GetProp(assets, "crater_boulders")
                },
                _ => theme.GetCover(kind)
            };
        }

        private string PickScenarioWreck(System.Random random) => _scenario switch
        {
            RegionalScenario.DesertAirbase or RegionalScenario.ForestAutumnHighland => random.Next(2) == 0 ? "crashed_heli" : "wrecked_tank",
            RegionalScenario.CityDowntownAvenue => random.Next(2) == 0 ? "wrecked_tank" : "wrecked_truck",
            RegionalScenario.ForestWetlandGrove => "wrecked_truck",
            _ => random.Next(2) == 0 ? "wrecked_tank" : "wrecked_truck"
        };

        private void PlaceParallelRow(DestructibleProp first, DestructibleProp second, Vector2 center, float angle, float spacing)
        {
            Vector2 dir = Direction(angle);
            Vector2 perp = Vector2.Perpendicular(dir);
            Vector2 half = perp * (spacing * 0.5f);
            Vector2[] candidates =
            {
                center,
                center + dir * 28f,
                center - dir * 28f,
                center + perp * 26f,
                center - perp * 26f
            };
            foreach (var c in candidates)
            {
                bool a = TryPlaceMirrored(first, c + half, angle, 7f);
                bool b = TryPlaceMirrored(second, c - half, angle, 7f);
                if (a || b) return;
            }
        }

        private void PlaceUtilityRow(DestructibleProp first, DestructibleProp second, Vector2 center, float angle, float spacing)
        {
            Vector2 dir = Direction(angle);
            Vector2 perp = Vector2.Perpendicular(dir);
            Vector2 half = dir * (spacing * 0.5f);
            Vector2[] candidates =
            {
                center,
                center + perp * 28f,
                center - perp * 28f
            };
            foreach (var c in candidates)
            {
                bool a = TryPlaceMirrored(first, c - half, angle, 7f);
                bool b = TryPlaceMirrored(second, c + half, angle, 7f);
                if (a || b) return;
            }
        }

        private void PlaceCheckpointAssembly(DestructibleProp gate, DestructibleProp booth, DestructibleProp wingWall,
            Vector2 center, Vector2 lineDir, float lineAngle)
        {
            bool placedGate = TryPlaceMirrored(gate, center, lineAngle, 7f);
            float sideOffset = placedGate ? 38f : 26f;
            TryPlaceMirrored(booth, center - lineDir * sideOffset, lineAngle, 7f);
            TryPlaceMirrored(wingWall, center + lineDir * (sideOffset + 6f), lineAngle, 7f);
        }

        private void PlaceFiringEmplacement(DestructibleProp weapon, DestructibleProp ammo, Vector2 pos,
            Vector2 frontAxis, float aimAngle)
        {
            if (TryPlaceMirrored(weapon, pos, aimAngle, 8f))
                TryPlaceMirrored(ammo, pos - frontAxis * 34f, aimAngle, 7f);
            else
                TryPlaceMirrored(ammo, pos - frontAxis * 22f, aimAngle, 7f);
        }

        private void PlaceRoadsideStrip(DestructibleProp primary, DestructibleProp sideA, DestructibleProp sideB,
            Vector2 anchor, Vector2 axisRight, Vector2 axisUp, float angle)
        {
            TryPlaceMirrored(primary, anchor + axisUp * 44f, angle, 7f);
            TryPlaceMirrored(sideA, anchor + axisUp * 44f + axisRight * 44f, angle, 7f);
            TryPlaceMirrored(sideB, anchor + axisUp * 44f - axisRight * 44f, angle, 7f);
        }

        private void PlaceWreckSetpiece(Vector2 center, Vector2 frontAxis, Vector2 frontPerp, float frontLineAngle,
            BattleSpawnAssets assets, System.Random random)
        {
            var wreck = GetProp(assets, PickScenarioWreck(random));
            TryPlaceMirrored(wreck, center, frontLineAngle + random.Next(-35, 36), 8f);
            TryPlaceMirrored(GetProp(assets, "crater_boulders"),
                center + frontAxis * 34f + frontPerp * random.Next(-22, 23), random.Next(0, 360), 8f);
            TryPlaceMirrored(GetProp(assets, "sandbag_wall"),
                center - frontAxis * 30f + frontPerp * 24f, frontLineAngle, 7f);
        }

        private void PlaceAlignedAccent(DestructibleProp prefab, Vector2 origin, Vector2 primaryOffset, Vector2 fallbackOffset, float angle)
        {
            if (TryPlaceMirrored(prefab, origin + primaryOffset, angle, 8f)) return;
            TryPlaceMirrored(prefab, origin + fallbackOffset, angle, 8f);
        }

        private void PlaceDefensiveLine(DestructibleProp primary, DestructibleProp flank, Vector2 lineCenter,
            Vector2 lineDir, float angle, int count, float step)
        {
            float halfSpan = (count - 1) * step * 0.5f;
            for (int i = 0; i < count; i++)
            {
                bool isEnd = count >= 3 && (i == 0 || i == count - 1);
                var prefab = isEnd && flank != null ? flank : primary;
                Vector2 pos = lineCenter + lineDir * (-halfSpan + i * step);
                TryPlaceMirrored(prefab, pos, angle, 7f);
            }
        }

        // =====================================================================
        // NATURAL ZONES: Organic clusters, free rotation, curved drifts & layering
        // =====================================================================
        private void PopulateNaturalDistrict(BattlefieldDistrict district, ZoneArchetype archetype, Vector2 center,
            float baseAngle, float reflection, BattlefieldTheme theme, BattleSpawnAssets assets, System.Random random)
        {
            Vector2 right = Direction(baseAngle);
            var anchorPoints = new List<Vector2>();

            foreach (var piece in district.Cover)
            {
                Vector2 offset = right * piece.Offset.x + Vector2.Perpendicular(right) * piece.Offset.y;
                offset.y *= reflection;
                Vector2 jitter = piece.Kind == BattlefieldCoverKind.Wall
                    ? Vector2.zero
                    : new Vector2(random.Next(-16, 17), random.Next(-16, 17));
                Vector2 position = Vector2Int.RoundToInt(center + offset + jitter);

                if (piece.Kind == BattlefieldCoverKind.Wall)
                {
                    var wallPrefab = theme.GetCover(piece.Kind);
                    if (wallPrefab != null)
                    {
                        float wallRot = _map == BattleMap.City
                            ? Mathf.Round(((baseAngle + piece.Angle) * reflection) / 90f) * 90f
                            : (baseAngle + piece.Angle) * reflection;
                        float wr = Mathf.Max(wallPrefab.meta.width, wallPrefab.meta.length) * 0.45f;
                        if (!IsOnRoad(position, wr, primaryOnly: false) || TryNudgeOffRoads(ref position, wr))
                            TryPlaceMirrored(wallPrefab, position, wallRot, clearance: 4f, avoidRoads: true);
                    }
                    anchorPoints.Add(position);
                    continue;
                }

                DestructibleProp prefab = ResolveNaturalSlot(archetype, piece.Kind, assets, random);
                if (prefab == null) continue;
                float r = Mathf.Max(prefab.meta.width, prefab.meta.length) * 0.5f;
                if (IsOnRoad(position, r * 0.55f, primaryOnly: false))
                    TryNudgeOffRoads(ref position, r * 0.55f);

                float freeAngle = random.Next(0, 360);
                if (TryPlaceMirrored(prefab, position, freeAngle, clearance: 7f, avoidRoads: true))
                    anchorPoints.Add(position);
            }

            if (anchorPoints.Count == 0) anchorPoints.Add(center);

            switch (archetype)
            {
                case ZoneArchetype.DesertBadlands:
                    PlaceGeologicalOutcrop(anchorPoints, new[] { "cliff_overhang", "rock_pillar", "desert_rock" },
                        new[] { "desert_scrub", "desert_rock", "dead_tree" }, assets, random);
                    break;

                case ZoneArchetype.DesertOasisBasin:
                    PlaceBasinCluster(center, "oasis_spring",
                        new[] { "palm_tree", "palm_tree", "cactus_cluster" },
                        new[] { "desert_rock", "desert_scrub", "desert_scrub" }, assets, random);
                    break;

                case ZoneArchetype.DesertCanyonSpires:
                    PlaceGeologicalOutcrop(anchorPoints, new[] { "rock_arch", "rock_pillar", "cliff_overhang" },
                        new[] { "cactus_cluster", "desert_rock", "dead_tree", "desert_scrub" }, assets, random);
                    break;

                case ZoneArchetype.CityBotanicalPark:
                    if (_scenario == RegionalScenario.CityDowntownAvenue)
                        PlaceOrganicGrove(center, new[] { "cherry_tree", "weeping_willow", "broadleaf_grove" },
                            new[] { "bush_cluster", "stone_well", "moss_rocks" }, assets, random);
                    else
                        PlaceOrganicGrove(center, new[] { "broadleaf_grove", "broadleaf_grove", "dead_tree" },
                            new[] { "bush_cluster", "moss_rocks" }, assets, random);
                    break;

                case ZoneArchetype.CityMemorialPark:
                    PlaceOrganicGrove(center, new[] { "cypress_tree", "cypress_tree", "broadleaf_grove" },
                        new[] { "stone_monolith", "bush_cluster", "pine_cluster" }, assets, random);
                    break;

                case ZoneArchetype.AncientWoodland:
                    PlaceOrganicGrove(center, new[] { "hollow_oak", "birch_clump", "broadleaf_grove", "pine_cluster" },
                        new[] { "fallen_log", "tree_stump", "bush_cluster", "moss_rocks" }, assets, random);
                    break;

                case ZoneArchetype.AutumnHighlandWoods:
                    PlaceOrganicGrove(center, new[] { "autumn_tree", "autumn_tree", "birch_clump", "dead_tree" },
                        new[] { "fallen_log", "tree_stump", "bush_cluster", "basalt_rocks" }, assets, random);
                    break;

                case ZoneArchetype.WetlandMangroveGrove:
                    PlaceBasinCluster(center, "reed_marsh",
                        new[] { "weeping_willow", "mangrove_roots", "bamboo_grove", "cherry_tree" },
                        new[] { "moss_rocks", "fallen_log", "bush_cluster" }, assets, random);
                    break;

                case ZoneArchetype.AlpineSnowWoods:
                    PlaceOrganicGrove(center, new[] { "snow_pine", "snow_pine", "pine_cluster", "dead_tree" },
                        new[] { "basalt_rocks", "fallen_log", "tree_stump" }, assets, random);
                    break;

                case ZoneArchetype.RockyOutcropField:
                    if (_map == BattleMap.Desert)
                        PlaceGeologicalOutcrop(anchorPoints, new[] { "rock_pillar", "cliff_overhang", "stone_monolith" },
                            new[] { "desert_rock", "desert_scrub", "dead_tree" }, assets, random);
                    else if (_scenario == RegionalScenario.ForestAutumnHighland)
                        PlaceGeologicalOutcrop(anchorPoints, new[] { "basalt_rocks", "limestone_karst", "stone_monolith" },
                            new[] { "autumn_tree", "dead_tree", "moss_rocks" }, assets, random);
                    else
                        PlaceGeologicalOutcrop(anchorPoints, new[] { "basalt_rocks", "limestone_karst", "forest_boulder" },
                            new[] { "snow_pine", "moss_rocks", "dead_tree" }, assets, random);
                    break;
            }
        }

        private DestructibleProp ResolveNaturalSlot(ZoneArchetype archetype, BattlefieldCoverKind kind,
            BattleSpawnAssets assets, System.Random random)
        {
            return archetype switch
            {
                ZoneArchetype.DesertBadlands => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, "desert_mesa"),
                    BattlefieldCoverKind.Building => GetProp(assets, random.Next(2) == 0 ? "cliff_overhang" : "rock_pillar"),
                    _ => GetProp(assets, "desert_scrub")
                },
                ZoneArchetype.DesertOasisBasin => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, "oasis_spring"),
                    BattlefieldCoverKind.Building => GetProp(assets, "palm_tree"),
                    _ => GetProp(assets, random.Next(2) == 0 ? "cactus_cluster" : "desert_rock")
                },
                ZoneArchetype.DesertCanyonSpires => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, random.Next(2) == 0 ? "rock_arch" : "rock_pillar"),
                    BattlefieldCoverKind.Building => GetProp(assets, "cliff_overhang"),
                    _ => GetProp(assets, random.Next(2) == 0 ? "cactus_cluster" : "stone_monolith")
                },
                ZoneArchetype.CityBotanicalPark => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, _scenario == RegionalScenario.CityDowntownAvenue ? "cherry_tree" : "broadleaf_grove"),
                    BattlefieldCoverKind.Building => GetProp(assets, _scenario == RegionalScenario.CityDowntownAvenue ? "weeping_willow" : "broadleaf_grove"),
                    _ => GetProp(assets, "bush_cluster")
                },
                ZoneArchetype.CityMemorialPark => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, "stone_monolith"),
                    BattlefieldCoverKind.Building => GetProp(assets, "cypress_tree"),
                    _ => GetProp(assets, "bush_cluster")
                },
                ZoneArchetype.AncientWoodland => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, "hollow_oak"),
                    BattlefieldCoverKind.Building => GetProp(assets, random.Next(2) == 0 ? "birch_clump" : "broadleaf_grove"),
                    _ => GetProp(assets, random.Next(2) == 0 ? "fallen_log" : "bush_cluster")
                },
                ZoneArchetype.AutumnHighlandWoods => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, "autumn_tree"),
                    BattlefieldCoverKind.Building => GetProp(assets, random.Next(2) == 0 ? "birch_clump" : "autumn_tree"),
                    _ => GetProp(assets, random.Next(2) == 0 ? "fallen_log" : "tree_stump")
                },
                ZoneArchetype.WetlandMangroveGrove => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, random.Next(2) == 0 ? "weeping_willow" : "mangrove_roots"),
                    BattlefieldCoverKind.Building => GetProp(assets, random.Next(2) == 0 ? "bamboo_grove" : "reed_marsh"),
                    _ => GetProp(assets, "moss_rocks")
                },
                ZoneArchetype.AlpineSnowWoods => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, "snow_pine"),
                    BattlefieldCoverKind.Building => GetProp(assets, random.Next(2) == 0 ? "snow_pine" : "pine_cluster"),
                    _ => GetProp(assets, "basalt_rocks")
                },
                ZoneArchetype.RockyOutcropField => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, _map == BattleMap.Desert ? "rock_pillar" : "limestone_karst"),
                    BattlefieldCoverKind.Building => GetProp(assets, _map == BattleMap.Desert ? "cliff_overhang" : "basalt_rocks"),
                    _ => GetProp(assets, _map == BattleMap.Desert ? "desert_rock" : "stone_monolith")
                },
                _ => null
            };
        }

        private void PlaceOrganicGrove(Vector2 center, string[] canopyPool, string[] underbrushPool,
            BattleSpawnAssets assets, System.Random random)
        {
            ScatterCurvedDrift(center, 34f, 76f, 3, canopyPool, assets, random);
            ScatterOrganicDrift(center, 46f, 94f, 3, underbrushPool, assets, random);
        }

        private void PlaceGeologicalOutcrop(List<Vector2> anchors, string[] formationPool, string[] screePool,
            BattleSpawnAssets assets, System.Random random)
        {
            int limit = Mathf.Min(anchors.Count, 2);
            for (int i = 0; i < limit; i++)
            {
                ScatterOrganicDrift(anchors[i], 38f, 72f, 1, formationPool, assets, random);
                ScatterOrganicDrift(anchors[i], 42f, 84f, 2, screePool, assets, random);
            }
        }

        private void PlaceBasinCluster(Vector2 center, string focalKind, string[] bankTrees, string[] outerVerge,
            BattleSpawnAssets assets, System.Random random)
        {
            TryPlaceMirrored(GetProp(assets, focalKind), center + new Vector2(random.Next(-12, 13), random.Next(-12, 13)),
                random.Next(0, 360), 8f);
            ScatterOrganicDrift(center, 36f, 72f, 3, bankTrees, assets, random);
            ScatterOrganicDrift(center, 52f, 92f, 2, outerVerge, assets, random);
        }

        private void PopulateRidgeFoothills(RegionalScenario scenario, List<(Vector2 Position, float Radius, float Angle)> ridgeAnchors,
            BattleSpawnAssets assets, System.Random random)
        {
            if (ridgeAnchors.Count == 0) return;
            string[] pool = scenario switch
            {
                RegionalScenario.DesertAirbase => new[] { "desert_rock", "desert_scrub", "rock_pillar" },
                RegionalScenario.DesertOasisRoute => new[] { "desert_rock", "cactus_cluster", "desert_scrub", "palm_tree" },
                RegionalScenario.DesertCanyonFront => new[] { "cliff_overhang", "desert_rock", "cactus_cluster", "dead_tree" },
                RegionalScenario.ForestRuralValley => new[] { "birch_clump", "moss_rocks", "bush_cluster", "fallen_log" },
                RegionalScenario.ForestAutumnHighland => new[] { "autumn_tree", "basalt_rocks", "dead_tree", "moss_rocks" },
                RegionalScenario.ForestWetlandGrove => new[] { "bamboo_grove", "limestone_karst", "moss_rocks", "bush_cluster" },
                RegionalScenario.ForestAlpineRidge => new[] { "snow_pine", "basalt_rocks", "dead_tree", "pine_cluster" },
                _ => System.Array.Empty<string>()
            };
            if (pool.Length == 0) return;

            foreach (var ridge in ridgeAnchors)
                ScatterOrganicDrift(ridge.Position, ridge.Radius + 18f, ridge.Radius + 60f, 3, pool, assets, random);
        }

        private void PopulateStreamBanks(RegionalScenario scenario, BattleSpawnAssets assets, System.Random random)
        {
            if (_waterStreams.Count == 0) return;
            string[] bankPool = scenario switch
            {
                RegionalScenario.ForestRuralValley => new[] { "weeping_willow", "birch_clump", "reed_marsh", "moss_rocks" },
                RegionalScenario.ForestAutumnHighland => new[] { "autumn_tree", "basalt_rocks", "dead_tree", "moss_rocks" },
                RegionalScenario.ForestWetlandGrove => new[] { "mangrove_roots", "weeping_willow", "reed_marsh", "bamboo_grove" },
                _ => new[] { "snow_pine", "basalt_rocks", "moss_rocks", "dead_tree" }
            };

            // Step along the west stream half (even indices are west, odd are mirrored east twins).
            for (int i = 2; i < _waterStreams.Count; i += 4)
            {
                var seg = _waterStreams[i];
                Vector2 mid = (seg.A + seg.B) * 0.5f;
                Vector2 perp = Vector2.Perpendicular(seg.Dir);
                float side = (i % 8 == 2) ? 1f : -1f;
                Vector2 bankPos = mid + perp * (side * (seg.HalfWidth + 24f + random.Next(0, 14)));
                var prefab = GetProp(assets, bankPool[(i / 4) % bankPool.Length]);
                TryPlaceMirrored(prefab, bankPos, random.Next(0, 360), clearance: 9f, avoidRoads: true);
            }
        }

        private void ScatterCurvedDrift(Vector2 center, float minRadius, float maxRadius, int count,
            string[] kindPool, BattleSpawnAssets assets, System.Random random)
        {
            if (kindPool == null || kindPool.Length == 0) return;
            float startDeg = (float)(random.NextDouble() * 360.0);
            float sweepDir = random.Next(2) == 0 ? 1f : -1f;
            for (int i = 0; i < count; i++)
            {
                string kind = kindPool[(i + random.Next(kindPool.Length)) % kindPool.Length];
                var prefab = GetProp(assets, kind);
                if (prefab == null) continue;

                for (int attempt = 0; attempt < 6; attempt++)
                {
                    float theta = (startDeg + sweepDir * (i * 58f + random.Next(-18, 19)) + attempt * 39f) * Mathf.Deg2Rad;
                    float radius = Mathf.Lerp(minRadius, maxRadius, (float)random.NextDouble());
                    Vector2 pos = Vector2Int.RoundToInt(center + new Vector2(Mathf.Cos(theta), Mathf.Sin(theta)) * radius);
                    if (TryPlaceMirrored(prefab, pos, random.Next(0, 360), clearance: 9f, avoidRoads: true))
                        break;
                }
            }
        }

        private void ScatterOrganicDrift(Vector2 anchor, float minRadius, float maxRadius, int count,
            string[] kindPool, BattleSpawnAssets assets, System.Random random)
        {
            if (kindPool == null || kindPool.Length == 0) return;
            float baseAngle = (float)(random.NextDouble() * 360.0);
            for (int i = 0; i < count; i++)
            {
                string kind = kindPool[(i + random.Next(kindPool.Length)) % kindPool.Length];
                var prefab = GetProp(assets, kind);
                if (prefab == null) continue;

                for (int attempt = 0; attempt < 6; attempt++)
                {
                    float theta = (baseAngle + i * 137.5f + random.Next(-32, 33) + attempt * 43f) * Mathf.Deg2Rad;
                    float radius = Mathf.Lerp(minRadius, maxRadius, (float)random.NextDouble());
                    Vector2 pos = Vector2Int.RoundToInt(anchor + new Vector2(Mathf.Cos(theta), Mathf.Sin(theta)) * radius);
                    float rot = random.Next(0, 360);
                    if (TryPlaceMirrored(prefab, pos, rot, clearance: 9f, avoidRoads: true))
                        break;
                }
            }
        }

        private static DestructibleProp GetProp(BattleSpawnAssets assets, string kind)
        {
            if (assets == null || string.IsNullOrEmpty(kind)) return null;
            var prefab = assets.GetCover(kind);
            return prefab != null && prefab.kind == kind ? prefab : null;
        }

        private void AddMirrored(DestructibleProp prefab, Vector2 position, float rotation)
        {
            _cover.Add(new CoverPlacement(prefab, position, rotation));
            _cover.Add(new CoverPlacement(prefab, Mirror(position), rotation + 180f));
        }

        private bool TryPlaceMirrored(DestructibleProp prefab, Vector2 position, float rotation,
            float clearance = 8f, bool avoidRoads = true)
        {
            if (prefab == null || prefab.meta == null) return false;
            Vector2 pos = Vector2Int.RoundToInt(position);
            Vector2 mirrored = Mirror(pos);
            float radius = Mathf.Max(prefab.meta.width, prefab.meta.length) * 0.5f;

            // 1. Keep well inside playable battlefield boundaries
            float edge = BattleSceneController.BorderPadPx + radius + 14f;
            if (pos.x < edge || pos.x > Width - edge || pos.y < edge || pos.y > Height - edge)
                return false;

            // 2. Keep deployment bases and central capture plaza completely unobstructed
            if (Vector2.Distance(pos, _westBase) < BaseClearRadius + radius ||
                Vector2.Distance(pos, Mirror(_westBase)) < BaseClearRadius + radius ||
                Vector2.Distance(pos, ControlPoint) < ControlClearRadius + radius)
                return false;

            // 3. Do not overlap our own 180-degree mirrored twin
            if (Vector2.Distance(pos, mirrored) < radius * 2f + clearance)
                return false;

            // 4. Keep roads clear & drivable (strong road identity + guaranteed pathing!)
            if (avoidRoads && IsOnRoad(pos, radius * 0.62f, primaryOnly: false))
                return false;
            if (!avoidRoads && IsOnRoad(pos, radius * 0.30f, primaryOnly: true))
                return false;

            // 5. Keep stream water channel open except for natural wetland flora
            bool isWetlandProp = prefab.kind is "reed_marsh" or "mangrove_roots" or "moss_rocks" or "oasis_spring";
            if (!isWetlandProp && IsInWater(pos, radius * 0.45f))
                return false;

            // 6. Strict non-overlap against every already-placed cover piece
            foreach (var existing in _cover)
            {
                if (existing.Contains(pos, radius + clearance) || existing.Contains(mirrored, radius + clearance))
                    return false;
            }

            AddMirrored(prefab, pos, rotation);
            return true;
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

        private static Vector2 Direction(float angle)
        {
            float radians = angle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        internal Vector2 ControlPoint => MapSize * 0.5f;
        internal IReadOnlyList<CoverPlacement> Cover => _cover;
        internal static int DeploymentCount => DeploymentOffsets.Length;

        private static Vector2 Mirror(Vector2 point) => MapSize - point;

        internal Vector2 Spawn(bool west, int index)
        {
            Vector2 position = _westBase + DeploymentOffsets[index];
            return west ? position : Mirror(position);
        }

        internal readonly struct CoverPlacement
        {
            internal readonly DestructibleProp Prefab;
            internal readonly Vector2 Position;
            internal readonly float Angle;
            private readonly Vector2 _right;
            private readonly Vector2 _halfSize;

            internal CoverPlacement(DestructibleProp prefab, Vector2 position, float angle)
            {
                Prefab = prefab;
                Position = position;
                Angle = angle;
                _right = Direction(Mathf.Repeat(angle, 180f));
                _halfSize = new Vector2(prefab.meta.width, prefab.meta.length) * 0.5f;
            }

            internal bool Contains(Vector2 point, float clearance)
            {
                Vector2 delta = point - Position;
                float x = Vector2.Dot(delta, _right);
                float y = Vector2.Dot(delta, Vector2.Perpendicular(_right));
                if (Prefab.meta.roundedFootprint)
                {
                    var normalized = new Vector2(x / (_halfSize.x + clearance), y / (_halfSize.y + clearance));
                    return normalized.sqrMagnitude < 1f;
                }
                return Mathf.Abs(x) < _halfSize.x + clearance && Mathf.Abs(y) < _halfSize.y + clearance;
            }
        }

        private readonly struct DistrictGround
        {
            internal readonly Vector2 Center;
            internal readonly Vector2 HalfSize;
            internal readonly Vector2 Right;
            internal readonly float RadiusSquared;
            internal readonly bool IsConnector;

            internal DistrictGround(Vector2 center, Vector2 size, Vector2 right, bool isConnector)
            {
                Center = center;
                HalfSize = size * 0.5f;
                Right = right;
                RadiusSquared = HalfSize.sqrMagnitude;
                IsConnector = isConnector;
            }

            internal Vector2 ToLocal(Vector2 point)
            {
                Vector2 delta = point - Center;
                return new Vector2(Vector2.Dot(delta, Right), Vector2.Dot(delta, Vector2.Perpendicular(Right)));
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
            internal readonly float MinX, MaxX, MinY, MaxY;

            internal RoadSegment(Vector2 a, Vector2 b, float halfWidth, bool isPrimary)
            {
                A = a;
                B = b;
                Vector2 delta = b - a;
                Length = delta.magnitude;
                Dir = Length > 0.001f ? delta / Length : Vector2.right;
                HalfWidth = halfWidth;
                IsPrimary = isPrimary;
                MinX = Mathf.Min(a.x, b.x);
                MaxX = Mathf.Max(a.x, b.x);
                MinY = Mathf.Min(a.y, b.y);
                MaxY = Mathf.Max(a.y, b.y);
            }

            internal float DistanceTo(Vector2 p, out float along, out float signedPerp)
            {
                Vector2 ap = p - A;
                float proj = Vector2.Dot(ap, Dir);
                along = Mathf.Clamp(proj, 0f, Length);
                Vector2 closest = A + Dir * along;
                Vector2 diff = p - closest;
                signedPerp = Vector2.Dot(diff, Vector2.Perpendicular(Dir));
                return diff.magnitude;
            }
        }

        internal bool IsWallAt(Vector2 position)
        {
            foreach (var cover in _cover)
                if (cover.Prefab.meta.fixedCover && cover.Contains(position, 0f))
                    return true;
            return false;
        }

        internal bool IsRoadAt(Vector2 position) => IsOnRoad(position, 0f, primaryOnly: false);

        internal bool IsWaterAt(Vector2 position) => IsInWater(position, 0f);

        // =====================================================================
        // GROUND PIXEL ART RENDERER (CONNECTED ROADS, FOREST CHARACTER, STREAMS)
        // =====================================================================
        internal async UniTask<Color32[]> GroundPixelsAsync(BattleLoadBudget budget, System.Action<float> progress)
        {
            if (_ground != null) return _ground;
            var (earth, dark, verge, concrete) = TerrainPalette();
            var pixels = new Color32[Width * Height];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width / 2; x++)
                {
                    int i = y * Width + x;
                    Color32 color = SampleTerrain(x, y, earth, dark, verge, out float patch, out float grain);

                    Vector2 point = new(x + 0.5f, y + 0.5f);
                    if (_map == BattleMap.City)
                    {
                        color = PaintCityBlocks(point, color, concrete, dark, verge, patch, grain);
                        color = PaintCityParks(point, color, dark, patch, grain);
                        color = PaintCityRoadNetwork(point, color, dark, verge, patch, grain);
                    }
                    else if (_map == BattleMap.Forest)
                    {
                        color = PaintForestFloorDetails(point, color, earth, dark, verge, patch, grain);
                        color = PaintRidgeFeet(point, color, earth, dark, verge, patch, grain);
                        color = PaintForestStream(point, color, dark, patch, grain, out bool inWater);
                        color = PaintForestRoadNetwork(point, color, dark, verge, patch, grain, inWater);
                    }
                    else
                    {
                        color = PaintNaturalDistricts(point, color, earth, verge, patch);
                        color = PaintRidgeFeet(point, color, earth, dark, verge, patch, grain);
                        color = PaintDesertRoadNetwork(point, color, earth, dark, verge, patch, grain);
                    }

                    color = PaintDeploymentAndPlaza(point, color, concrete, dark, verge, patch, grain);

                    int edgeX = Mathf.Min(x, Width - 1 - x);
                    int edgeY = Mathf.Min(y, Height - 1 - y);
                    int edge = Mathf.Min(edgeX, edgeY);
                    int distanceToBoundary = edge - BattleSceneController.BorderPadPx;
                    if (distanceToBoundary < 0) color = ShadeExterior(color, dark);
                    if (distanceToBoundary is >= -1 and <= 2)
                    {
                        int along = edgeX < edgeY ? y : x;
                        color = distanceToBoundary >= 0 && distanceToBoundary <= 1 && along % 25 < 16
                            ? BoundaryColor
                            : Color32.Lerp(color, dark, 0.47f);
                    }

                    pixels[i] = color;
                    pixels[pixels.Length - 1 - i] = color;
                }
                if ((y & 31) == 31) progress?.Invoke((y + 1) / (float)Height);
                if (budget != null) await budget.YieldIfSpentAsync();
            }
            _ground = pixels;
            return _ground;
        }

        internal async UniTask<Color32[]> ExteriorPixelsAsync(int padding, int scale, BattleLoadBudget budget)
        {
            int width = (Width + padding * 2) / scale;
            int height = (Height + padding * 2) / scale;
            var pixels = new Color32[width * height];
            var (earth, dark, verge, _) = TerrainPalette();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width / 2; x++)
                {
                    int worldX = x * scale + scale / 2 - padding;
                    int worldY = y * scale + scale / 2 - padding;
                    if (worldX >= 0 && worldY >= 0 && worldY < Height) continue;

                    Color32 color = ShadeExterior(SampleTerrain(worldX, worldY, earth, dark, verge, out _, out _), dark);
                    int i = y * width + x;
                    pixels[i] = color;
                    pixels[pixels.Length - 1 - i] = color;
                }

                if (budget != null) await budget.YieldIfSpentAsync();
            }
            return pixels;
        }

        private static Color32 ShadeExterior(Color32 color, Color32 dark) => Color32.Lerp(color, dark, 0.48f);

        private Color32 SampleTerrain(int x, int y, Color32 earth, Color32 dark, Color32 verge,
            out float patch, out float grain)
        {
            patch = Mathf.PerlinNoise(x * 0.011f + (_seed & 255) * 0.17f, y * 0.014f);
            grain = Grain(x, y);
            Color32 color = Color32.Lerp(dark, earth, Mathf.Clamp01(patch * 0.85f + grain * 0.14f + 0.12f));
            return PaintTerrain(x, y, color, dark, verge, patch, grain);
        }

        private (Color32 Earth, Color32 Dark, Color32 Verge, Color32 Concrete) TerrainPalette() =>
            _scenario switch
            {
                RegionalScenario.ForestRuralValley => (
                    new Color32(104, 134, 68, 255),
                    new Color32(62, 86, 46, 255),
                    new Color32(146, 152, 86, 255),
                    new Color32(138, 126, 98, 255)),
                RegionalScenario.ForestAutumnHighland => (
                    new Color32(118, 114, 62, 255),
                    new Color32(68, 66, 40, 255),
                    new Color32(168, 114, 58, 255),
                    new Color32(134, 124, 98, 255)),
                RegionalScenario.ForestWetlandGrove => (
                    new Color32(82, 116, 68, 255),
                    new Color32(46, 74, 52, 255),
                    new Color32(116, 132, 74, 255),
                    new Color32(122, 122, 98, 255)),
                RegionalScenario.ForestAlpineRidge => (
                    new Color32(98, 118, 96, 255),
                    new Color32(58, 76, 70, 255),
                    new Color32(198, 208, 212, 255),
                    new Color32(134, 138, 134, 255)),
                _ => _map switch
                {
                    BattleMap.City => ((Color32)new Color(0.47f, 0.46f, 0.43f), (Color32)new Color(0.34f, 0.35f, 0.36f),
                        (Color32)new Color(0.70f, 0.66f, 0.56f), (Color32)new Color(0.55f, 0.56f, 0.54f)),
                    _ => ((Color32)new Color(0.71f, 0.54f, 0.34f), (Color32)new Color(0.47f, 0.34f, 0.22f),
                        (Color32)new Color(0.78f, 0.66f, 0.44f), (Color32)new Color(0.61f, 0.57f, 0.48f))
                }
            };

        private Color32 PaintDeploymentAndPlaza(Vector2 point, Color32 terrain, Color32 concrete, Color32 dark,
            Color32 trim, float patch, float grain)
        {
            float west = PadDistance(point - _westBase, 92f, 90f);
            float east = PadDistance(point - Mirror(_westBase), 92f, 90f);
            float plaza = Vector2.Distance(point, ControlPoint) / 72f;
            float edge = Mathf.Min(Mathf.Min(west, east), plaza);
            if (edge >= 1f) return terrain;

            bool city = _map == BattleMap.City;
            bool forest = _map == BattleMap.Forest;
            bool joint = city && (Mathf.Repeat(point.x + 13f, 27f) < 1f || Mathf.Repeat(point.y + 7f, 27f) < 1f);
            Color32 padBase = forest ? new Color32(142, 120, 86, 255) : concrete;
            Color32 surface = Color32.Lerp(padBase, dark, 0.10f * grain + 0.14f * patch + (joint ? 0.14f : 0f));
            if (city && edge is > 0.90f and < 0.96f) surface = Color32.Lerp(surface, trim, 0.52f);
            if (city && plaza is > 0.58f and < 0.64f) surface = Color32.Lerp(surface, trim, 0.42f); // Roundabout ring
            float blend = Mathf.SmoothStep(0f, 1f, (1f - edge) * (city ? 20f : 5f)) * (city ? 0.92f : 0.66f);
            return Color32.Lerp(terrain, surface, blend);
        }

        // =====================================================================
        // CITY GROUND: BLOCKS, PARKS & CONNECTED ASPHALT STREETS WITH CROSSWALKS
        // =====================================================================
        private Color32 PaintCityBlocks(Vector2 point, Color32 terrain, Color32 concrete, Color32 dark,
            Color32 trim, float patch, float grain)
        {
            float yard = 0f;
            float curb = 0f;
            foreach (var district in _districtGround)
            {
                if ((point - district.Center).sqrMagnitude > district.RadiusSquared) continue;
                Vector2 local = district.ToLocal(point);
                float inset = Mathf.Min(district.HalfSize.x - Mathf.Abs(local.x), district.HalfSize.y - Mathf.Abs(local.y));
                if (inset <= 0f) continue;

                yard = Mathf.Max(yard, Mathf.SmoothStep(0f, 1f, inset / 6f));
                float strip = Mathf.Clamp01(1f - Mathf.Abs(inset - 4.5f) / 2.5f);
                curb = Mathf.Max(curb, strip);
            }
            if (yard <= 0f) return terrain;

            bool joint = Mathf.Repeat(point.x + 12f, 32f) < 1.2f || Mathf.Repeat(point.y + 8f, 32f) < 1.2f;
            Color32 paving = Color32.Lerp(concrete, dark, 0.05f * grain + 0.15f * patch + (joint ? 0.12f : 0f));
            Color32 color = Color32.Lerp(terrain, paving, yard * 0.85f);
            return Color32.Lerp(color, trim, curb * 0.52f);
        }

        private Color32 PaintCityRoadNetwork(Vector2 point, Color32 terrain, Color32 dark,
            Color32 trim, float patch, float grain)
        {
            float bestAsphalt = 0f;
            float bestSidewalk = 0f;
            float bestCurb = 0f;
            float bestDash = 0f;
            float bestCrosswalk = 0f;

            // Check Chebyshev (box) distance to nearest intersection so zebra crosswalks are razor-straight rectangles!
            float minInterBox = 999f;
            for (int k = 0; k < _intersections.Count; k++)
            {
                Vector2 d = point - _intersections[k];
                float box = Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y));
                if (box < minInterBox) minInterBox = box;
            }

            for (int i = 0; i < _roads.Count; i++)
            {
                var seg = _roads[i];
                float outer = seg.HalfWidth + 14f;
                if (point.x < seg.MinX - outer || point.x > seg.MaxX + outer ||
                    point.y < seg.MinY - outer || point.y > seg.MaxY + outer) continue;

                float dist = seg.DistanceTo(point, out float along, out float signedPerp);
                if (dist >= outer) continue;

                float hw = seg.HalfWidth;
                if (dist <= hw)
                {
                    float w = Mathf.SmoothStep(0f, 1f, (hw - dist) * 0.55f);
                    bestAsphalt = Mathf.Max(bestAsphalt, w);

                    // Clean yellow/ivory dashed center line outside intersections & central plaza
                    if (minInterBox > hw + 15f && Vector2.Distance(point, ControlPoint) > 78f)
                    {
                        if (Mathf.Abs(signedPerp) < (seg.IsPrimary ? 1.45f : 1.15f) && Mathf.Repeat(along + 8f, 34f) < 17f)
                            bestDash = Mathf.Max(bestDash, w);
                    }

                    // Razor-straight white zebra crosswalk stripes right outside every real street junction!
                    if (minInterBox >= hw + 5f && minInterBox <= hw + 14f && Mathf.Abs(signedPerp) < hw - 3.5f)
                    {
                        if (Mathf.Repeat(Mathf.Abs(signedPerp) + 2f, 7f) < 3.5f)
                            bestCrosswalk = Mathf.Max(bestCrosswalk, w);
                    }
                }
                else
                {
                    float swInset = outer - dist;
                    float sw = Mathf.SmoothStep(0f, 1f, swInset * 0.35f);
                    bestSidewalk = Mathf.Max(bestSidewalk, sw);
                    if (dist <= hw + 3.2f)
                        bestCurb = Mathf.Max(bestCurb, 1f - (dist - hw) / 3.2f);
                }
            }

            if (bestAsphalt <= 0f && bestSidewalk <= 0f) return terrain;

            Color32 color = terrain;
            if (bestSidewalk > 0f)
            {
                bool tileJoint = Mathf.Repeat(point.x, 16f) < 1.1f || Mathf.Repeat(point.y, 16f) < 1.1f;
                var sidewalkCol = new Color32(158, 154, 145, 255);
                var curbCol = new Color32(106, 104, 98, 255);
                Color32 sw = tileJoint ? Color32.Lerp(sidewalkCol, curbCol, 0.32f) : sidewalkCol;
                sw = Color32.Lerp(sw, dark, 0.08f * grain + 0.08f * patch);
                color = Color32.Lerp(color, sw, bestSidewalk * 0.90f);
                if (bestCurb > 0f)
                    color = Color32.Lerp(color, curbCol, bestCurb * 0.78f);
            }

            if (bestAsphalt > 0f)
            {
                var asphaltBase = new Color32(68, 72, 78, 255);
                var asphaltDark = new Color32(54, 58, 64, 255);
                Color32 road = Color32.Lerp(asphaltBase, asphaltDark, 0.25f * grain + 0.22f * patch);
                color = Color32.Lerp(color, road, bestAsphalt * 0.96f);
                if (bestDash > 0f)
                    color = Color32.Lerp(color, new Color32(214, 196, 126, 255), bestDash * 0.85f);
                if (bestCrosswalk > 0f)
                    color = Color32.Lerp(color, new Color32(204, 202, 192, 255), bestCrosswalk * 0.68f);
            }

            return color;
        }

        // =====================================================================
        // FOREST GROUND: RICH MEADOWS, LEAFY GROVES, FARMS, BROOK & DIRT ROADS
        // =====================================================================
        private Color32 PaintForestFloorDetails(Vector2 point, Color32 terrain, Color32 earth, Color32 dark,
            Color32 verge, float patch, float grain)
        {
            Color32 color = terrain;

            // 1. Sunlit open glades / meadow light dappled across the forest floor
            float glade = Mathf.PerlinNoise(point.x * 0.0055f + (_seed & 127) * 0.31f, point.y * 0.0065f + 7.3f);
            if (glade > 0.56f)
            {
                float w = Mathf.SmoothStep(0.56f, 0.78f, glade);
                Color32 sunlit = _scenario switch
                {
                    RegionalScenario.ForestAutumnHighland => new Color32(152, 134, 72, 255),
                    RegionalScenario.ForestWetlandGrove => new Color32(104, 138, 76, 255),
                    RegionalScenario.ForestAlpineRidge => new Color32(126, 142, 118, 255),
                    _ => new Color32(126, 156, 78, 255)
                };
                color = Color32.Lerp(color, sunlit, w * 0.45f);
            }

            // 2. Deep organic canopy floor under natural forest groves (leaf litter, ferns, snow drifts!)
            float groveWeight = 0f;
            foreach (var grove in _groveGround)
            {
                if ((point - grove.Center).sqrMagnitude > grove.RadiusSquared) continue;
                Vector2 local = grove.ToLocal(point);
                var norm = new Vector2(local.x / grove.HalfSize.x, local.y / grove.HalfSize.y);
                float d = norm.magnitude + (patch - 0.5f) * 0.24f;
                groveWeight = Mathf.Max(groveWeight, Mathf.SmoothStep(0f, 1f, (1f - d) * 2.2f));
            }
            if (groveWeight > 0f)
            {
                Color32 canopyFloor = _scenario switch
                {
                    RegionalScenario.ForestAutumnHighland => Color32.Lerp(new Color32(138, 82, 42, 255), new Color32(174, 114, 48, 255), grain),
                    RegionalScenario.ForestWetlandGrove => new Color32(46, 78, 50, 255),
                    RegionalScenario.ForestAlpineRidge => Color32.Lerp(new Color32(196, 208, 214, 255), new Color32(226, 234, 238, 255), patch),
                    _ => new Color32(56, 84, 44, 255)
                };
                float strength = _scenario == RegionalScenario.ForestAlpineRidge ? 0.74f : 0.58f;
                color = Color32.Lerp(color, canopyFloor, groveWeight * strength);
            }

            // 3. Cleared compacted earth/woodchip pads under artificial forest camps & cabins
            float campCoverage = 0f;
            foreach (var district in _districtGround)
            {
                if ((point - district.Center).sqrMagnitude > district.RadiusSquared) continue;
                Vector2 local = district.ToLocal(point);
                var norm = new Vector2(local.x / district.HalfSize.x, local.y / district.HalfSize.y);
                float d = norm.magnitude + (patch - 0.5f) * 0.16f;
                campCoverage = Mathf.Max(campCoverage, Mathf.SmoothStep(0f, 1f, (1f - d) * 2.4f));
            }
            if (campCoverage > 0f)
            {
                var campDirt = new Color32(132, 112, 80, 255);
                color = Color32.Lerp(color, Color32.Lerp(campDirt, dark, grain * 0.16f), campCoverage * 0.56f);
            }

            // 4. Plowed golden-tan agricultural furrow strips around the Rural Windmill Farm!
            if (_farmGround.Count > 0)
            {
                foreach (var farm in _farmGround)
                {
                    if ((point - farm.Center).sqrMagnitude > farm.RadiusSquared) continue;
                    Vector2 local = farm.ToLocal(point);
                    float inset = Mathf.Min(farm.HalfSize.x - Mathf.Abs(local.x), farm.HalfSize.y - Mathf.Abs(local.y));
                    if (inset <= 0f) continue;
                    float w = Mathf.SmoothStep(0f, 1f, inset / 10f);
                    bool ridge = Mathf.Repeat(local.y + 400f, 10f) < 5f;
                    Color32 fieldCol = ridge ? new Color32(158, 134, 78, 255) : new Color32(124, 100, 62, 255);
                    color = Color32.Lerp(color, fieldCol, w * 0.62f);
                }
            }

            // 5. Crisp alpine snow-drift patches on cool shaded ridges
            if (_scenario == RegionalScenario.ForestAlpineRidge)
            {
                float drift = Mathf.PerlinNoise(point.x * 0.016f + 4.1f, point.y * 0.016f + 9.7f);
                if (drift > 0.62f)
                {
                    float sw = Mathf.SmoothStep(0.62f, 0.76f, drift);
                    color = Color32.Lerp(color, new Color32(218, 226, 232, 255), sw * 0.72f);
                }
            }

            // 6. Subtle wildflowers / fallen leaves / clover pixel accents (tasteful, never noisy!)
            float micro = Grain((int)point.x / 3, (int)point.y / 3);
            if (micro > 0.965f && groveWeight < 0.35f && campCoverage < 0.2f)
            {
                Color32 accent = _scenario switch
                {
                    RegionalScenario.ForestAutumnHighland => new Color32(198, 122, 48, 255),
                    RegionalScenario.ForestAlpineRidge => new Color32(226, 234, 238, 255),
                    RegionalScenario.ForestWetlandGrove => new Color32(142, 168, 84, 255),
                    _ => ((int)point.x + (int)point.y) % 2 == 0
                        ? new Color32(214, 198, 112, 255)
                        : new Color32(196, 178, 186, 255)
                };
                color = Color32.Lerp(color, accent, 0.75f);
            }

            return color;
        }

        private Color32 PaintForestStream(Vector2 point, Color32 terrain, Color32 dark,
            float patch, float grain, out bool inWater)
        {
            inWater = false;
            if (_waterStreams.Count == 0) return terrain;

            float bestWater = 0f;
            float bestBank = 0f;
            float bestCenter = 0f;

            for (int i = 0; i < _waterStreams.Count; i++)
            {
                var seg = _waterStreams[i];
                float bankOuter = seg.HalfWidth + 11f;
                if (point.x < seg.MinX - bankOuter || point.x > seg.MaxX + bankOuter ||
                    point.y < seg.MinY - bankOuter || point.y > seg.MaxY + bankOuter) continue;

                float dist = seg.DistanceTo(point, out _, out _);
                float wobble = (patch - 0.5f) * 6.5f;
                float adjDist = dist + wobble;
                if (adjDist >= bankOuter) continue;

                if (adjDist < seg.HalfWidth)
                {
                    float w = Mathf.SmoothStep(0f, 1f, (seg.HalfWidth - adjDist) * 0.45f);
                    bestWater = Mathf.Max(bestWater, w);
                    bestCenter = Mathf.Max(bestCenter, Mathf.Clamp01(1f - adjDist / seg.HalfWidth));
                }
                else
                {
                    float b = Mathf.SmoothStep(0f, 1f, (bankOuter - adjDist) / 11f);
                    bestBank = Mathf.Max(bestBank, b);
                }
            }

            if (bestWater <= 0f && bestBank <= 0f) return terrain;

            Color32 color = terrain;
            if (bestBank > 0f)
            {
                Color32 wetBank = _scenario == RegionalScenario.ForestAlpineRidge
                    ? new Color32(126, 134, 136, 255)
                    : new Color32(94, 88, 66, 255);
                var pebble = new Color32(138, 134, 122, 255);
                Color32 bankCol = grain > 0.76f ? pebble : wetBank;
                color = Color32.Lerp(color, bankCol, bestBank * 0.74f);
            }

            if (bestWater > 0.05f)
            {
                inWater = true;
                Color32 shallow = _scenario == RegionalScenario.ForestWetlandGrove
                    ? new Color32(58, 96, 86, 255)
                    : (_scenario == RegionalScenario.ForestAlpineRidge
                        ? new Color32(74, 124, 146, 255)
                        : new Color32(62, 114, 122, 255));
                Color32 deep = _scenario == RegionalScenario.ForestWetlandGrove
                    ? new Color32(36, 66, 62, 255)
                    : new Color32(38, 78, 98, 255);
                Color32 water = Color32.Lerp(shallow, deep, bestCenter * 0.72f);
                // Crisp 8-bit water shimmer ripples
                float ripple = Mathf.Sin(point.x * 0.14f + point.y * 0.22f + patch * 6f);
                if (ripple > 0.86f && bestCenter > 0.25f)
                    water = Color32.Lerp(water, new Color32(174, 212, 222, 255), 0.55f);
                color = Color32.Lerp(color, water, bestWater * 0.92f);
            }

            return color;
        }

        private Color32 PaintForestRoadNetwork(Vector2 point, Color32 terrain, Color32 dark,
            Color32 verge, float patch, float grain, bool inWater)
        {
            float bestRoad = 0f;
            float bestShoulder = 0f;
            float bestRut = 0f;
            float bestPlank = 0f;
            float bestBridgeRail = 0f;

            for (int i = 0; i < _roads.Count; i++)
            {
                var seg = _roads[i];
                float outer = seg.HalfWidth + 10f;
                if (point.x < seg.MinX - outer || point.x > seg.MaxX + outer ||
                    point.y < seg.MinY - outer || point.y > seg.MaxY + outer) continue;

                float dist = seg.DistanceTo(point, out float along, out float signedPerp);
                float edgeWobble = inWater ? 0f : (patch - 0.5f) * 4.5f;
                float adj = dist + edgeWobble;
                float effectiveW = inWater ? seg.HalfWidth + 2.5f : seg.HalfWidth;
                if (adj >= outer) continue;

                if (adj < effectiveW)
                {
                    float w = Mathf.SmoothStep(0f, 1f, (effectiveW - adj) * 0.42f);
                    bestRoad = Mathf.Max(bestRoad, w);
                    float rutTarget = seg.HalfWidth * 0.42f;
                    float rutDist = Mathf.Abs(Mathf.Abs(signedPerp) - rutTarget);
                    if (rutDist < 3.0f)
                        bestRut = Mathf.Max(bestRut, (1f - rutDist / 3.0f) * w);
                    if (inWater)
                    {
                        if (Mathf.Abs(signedPerp) > effectiveW - 3.2f)
                            bestBridgeRail = Mathf.Max(bestBridgeRail, w);
                        else if (Mathf.Repeat(along, 6f) < 4.5f)
                            bestPlank = Mathf.Max(bestPlank, w);
                    }
                }
                else
                {
                    bestShoulder = Mathf.Max(bestShoulder, Mathf.SmoothStep(0f, 1f, (outer - adj) / 10f));
                }
            }

            if (bestRoad <= 0f && bestShoulder <= 0f) return terrain;

            Color32 roadBase = _scenario switch
            {
                RegionalScenario.ForestAutumnHighland => new Color32(146, 118, 84, 255),
                RegionalScenario.ForestWetlandGrove => new Color32(128, 108, 76, 255),
                RegionalScenario.ForestAlpineRidge => new Color32(138, 134, 124, 255),
                _ => new Color32(154, 126, 86, 255)
            };
            Color32 rutCol = Color32.Lerp(roadBase, dark, 0.42f);
            Color32 shoulderCol = Color32.Lerp(roadBase, dark, 0.28f);

            Color32 color = terrain;
            if (bestShoulder > 0f && !inWater)
                color = Color32.Lerp(color, shoulderCol, bestShoulder * 0.48f);

            if (bestRoad > 0f)
            {
                if (inWater)
                {
                    // Sturdy wooden timber bridge with dark guardrails spanning the woodland creek!
                    var rail = new Color32(66, 48, 32, 255);
                    var timber = new Color32(146, 108, 68, 255);
                    var timberGap = new Color32(82, 60, 38, 255);
                    Color32 bridge = bestBridgeRail > 0.35f ? rail
                        : (bestPlank > 0f ? Color32.Lerp(timber, timberGap, grain * 0.22f) : timberGap);
                    color = Color32.Lerp(color, bridge, bestRoad * 0.94f);
                }
                else
                {
                    Color32 dirt = Color32.Lerp(roadBase, verge, (grain - 0.5f) * 0.14f);
                    color = Color32.Lerp(color, dirt, bestRoad * 0.88f);
                    if (bestRut > 0f)
                        color = Color32.Lerp(color, rutCol, bestRut * 0.52f);
                }
            }

            return color;
        }

        // =====================================================================
        // DESERT GROUND: CONNECTED CONVOY HIGHWAY + CANYON TRACKS
        // =====================================================================
        private Color32 PaintDesertRoadNetwork(Vector2 point, Color32 terrain, Color32 earth, Color32 dark,
            Color32 verge, float patch, float grain)
        {
            float bestPrimary = 0f;
            float bestTrack = 0f;
            float bestShoulder = 0f;
            float bestRut = 0f;
            float bestDash = 0f;

            for (int i = 0; i < _roads.Count; i++)
            {
                var seg = _roads[i];
                float outer = seg.HalfWidth + 10f;
                if (point.x < seg.MinX - outer || point.x > seg.MaxX + outer ||
                    point.y < seg.MinY - outer || point.y > seg.MaxY + outer) continue;

                float dist = seg.DistanceTo(point, out float along, out float signedPerp);
                float drift = (patch - 0.5f) * (seg.IsPrimary ? 3.5f : 5.0f);
                float adj = dist + drift;
                if (adj >= outer) continue;

                if (adj < seg.HalfWidth)
                {
                    float w = Mathf.SmoothStep(0f, 1f, (seg.HalfWidth - adj) * 0.42f);
                    if (seg.IsPrimary)
                    {
                        bestPrimary = Mathf.Max(bestPrimary, w);
                        if (Mathf.Abs(signedPerp) < 1.2f && Mathf.Repeat(along + 6f, 40f) < 16f && Vector2.Distance(point, ControlPoint) > 78f)
                            bestDash = Mathf.Max(bestDash, w);
                    }
                    else
                    {
                        bestTrack = Mathf.Max(bestTrack, w);
                    }

                    float rutTarget = seg.HalfWidth * 0.44f;
                    float rutDist = Mathf.Abs(Mathf.Abs(signedPerp) - rutTarget);
                    if (rutDist < 2.8f)
                        bestRut = Mathf.Max(bestRut, (1f - rutDist / 2.8f) * w);
                }
                else
                {
                    bestShoulder = Mathf.Max(bestShoulder, Mathf.SmoothStep(0f, 1f, (outer - adj) / 10f));
                }
            }

            if (bestPrimary <= 0f && bestTrack <= 0f && bestShoulder <= 0f) return terrain;

            Color32 color = terrain;
            if (bestShoulder > 0f)
                color = Color32.Lerp(color, new Color32(158, 128, 86, 255), bestShoulder * 0.46f);

            if (bestTrack > 0f)
            {
                Color32 trackCol = Color32.Lerp(new Color32(134, 104, 68, 255), dark, 0.16f * grain);
                color = Color32.Lerp(color, trackCol, bestTrack * 0.84f);
            }

            if (bestPrimary > 0f)
            {
                // Sun-baked sand-dusted military tarmac/gravel supply highway
                var tarmac = new Color32(114, 104, 88, 255);
                var sandDust = new Color32(162, 136, 98, 255);
                Color32 road = Color32.Lerp(tarmac, sandDust, Mathf.Clamp01(patch * 0.42f + grain * 0.15f));
                color = Color32.Lerp(color, road, bestPrimary * 0.90f);
                if (bestDash > 0f)
                    color = Color32.Lerp(color, new Color32(220, 200, 140, 255), bestDash * 0.60f);
            }

            if (bestRut > 0f)
                color = Color32.Lerp(color, dark, bestRut * 0.38f);

            return color;
        }

        private Color32 PaintNaturalDistricts(Vector2 point, Color32 terrain, Color32 earth, Color32 verge, float patch)
        {
            float coverage = 0f;
            foreach (var district in _districtGround)
            {
                if ((point - district.Center).sqrMagnitude > district.RadiusSquared) continue;
                Vector2 local = district.ToLocal(point);
                var normalized = new Vector2(local.x / district.HalfSize.x, local.y / district.HalfSize.y);
                float distance = normalized.magnitude + (patch - 0.5f) * 0.18f;
                float weight = Mathf.SmoothStep(0f, 1f, (1f - distance) * 2.3f);
                coverage = Mathf.Max(coverage, weight * 0.42f);
            }

            Color32 soil = Color32.Lerp(earth, verge, 0.16f + patch * 0.18f);
            return Color32.Lerp(terrain, soil, coverage);
        }

        private Color32 PaintRidgeFeet(Vector2 point, Color32 terrain, Color32 earth, Color32 dark, Color32 verge,
            float patch, float grain)
        {
            float deposit = 0f;
            foreach (var ridge in _ridgeGround)
            {
                if ((point - ridge.Center).sqrMagnitude > ridge.RadiusSquared) continue;

                Vector2 local = ridge.ToLocal(point);
                var normalized = new Vector2(local.x / ridge.HalfSize.x, local.y / ridge.HalfSize.y);
                float pockets = Mathf.PerlinNoise(point.x * 0.043f + 14f, point.y * 0.052f);
                float falloff = 1f - normalized.magnitude + (pockets - 0.5f) * 0.26f;
                float weight = Mathf.SmoothStep(0f, 1f, falloff * 2.8f);
                deposit = Mathf.Max(deposit, weight * (0.24f + pockets * 0.76f));
            }
            if (deposit <= 0f) return terrain;

            float chips = Grain((int)point.x / 3, (int)point.y / 3);
            Color32 soil = Color32.Lerp(earth, verge, 0.14f + patch * 0.26f);
            Color32 result = Color32.Lerp(terrain, soil, deposit * 0.60f);
            if (chips > 0.72f && grain > 0.32f)
                return Color32.Lerp(result, verge, deposit * (chips - 0.72f) * 1.4f);
            if (chips < 0.11f)
                return Color32.Lerp(result, dark, deposit * (0.11f - chips) * 2.4f);
            return result;
        }

        private Color32 PaintCityParks(Vector2 point, Color32 terrain, Color32 dark, float patch, float grain)
        {
            if (_parkGround.Count == 0) return terrain;
            float lawn = 0f;
            float border = 0f;
            foreach (var park in _parkGround)
            {
                if ((point - park.Center).sqrMagnitude > park.RadiusSquared) continue;
                Vector2 local = park.ToLocal(point);
                float inset = Mathf.Min(park.HalfSize.x - Mathf.Abs(local.x), park.HalfSize.y - Mathf.Abs(local.y));
                if (inset <= 0f) continue;
                lawn = Mathf.Max(lawn, Mathf.SmoothStep(0f, 1f, inset / 8f));
                border = Mathf.Max(border, Mathf.Clamp01(1f - Mathf.Abs(inset - 3.5f) / 2.5f));
            }
            if (lawn <= 0f) return terrain;
            var parkGrass = new Color32(88, 122, 70, 255);
            var parkRim = new Color32(158, 154, 142, 255);
            Color32 turf = Color32.Lerp(parkGrass, dark, 0.14f * grain + 0.12f * (1f - patch));
            Color32 blended = Color32.Lerp(terrain, turf, lawn * 0.88f);
            return Color32.Lerp(blended, parkRim, border * 0.62f);
        }

        private Color32 PaintTerrain(int x, int y, Color32 surface, Color32 low, Color32 high, float patch, float grain)
        {
            float cluster = Grain(x / 4, y / 4);
            switch (_map)
            {
                case BattleMap.City:
                    return Color32.Lerp(surface, high, 0.025f * patch + 0.022f * cluster);
                case BattleMap.Forest:
                    // Soft organic grass blade texture instead of harsh dark blotches
                    if (cluster > 0.76f && grain > 0.42f)
                        return Color32.Lerp(surface, high, (cluster - 0.76f) * 0.55f * patch);
                    if (cluster < 0.16f)
                        return Color32.Lerp(surface, low, (0.16f - cluster) * 0.48f);
                    return surface;
                default:
                    float ripple = Mathf.Sin(x * 0.039f + y * 0.30f + patch * 12f);
                    if (ripple > 0.65f && patch > 0.43f)
                        return Color32.Lerp(surface, high, (ripple - 0.65f) * patch * 0.64f);
                    break;
            }
            return grain > 0.96f && cluster < 0.18f ? Color32.Lerp(surface, low, 0.35f) : surface;
        }

        private static float PadDistance(Vector2 point, float halfWidth, float halfHeight)
        {
            float x = Mathf.Abs(point.x) / halfWidth, y = Mathf.Abs(point.y) / halfHeight;
            return Mathf.Max(Mathf.Max(x, y), (x + y) * 0.61f);
        }

        internal static float Grain(int x, int y)
        {
            unchecked
            {
                uint n = (uint)(x * 374761393 + y * 668265263);
                n = (n ^ (n >> 13)) * 1274126177u;
                return ((n ^ (n >> 16)) & 255u) / 255f;
            }
        }
    }
}
