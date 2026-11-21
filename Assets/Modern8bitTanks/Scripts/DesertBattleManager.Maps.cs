using System.Collections.Generic;
using UnityEngine;

namespace Modern8bitTanks
{
    public partial class DesertBattleManager
    {
        private readonly Dictionary<string, Texture2D> _propTexCache = new();
        private PropsMetaFile _propsMeta;
        private Transform _mapPropsRoot;
        private BattleMap _loadedMap = BattleMap.Desert;

        public static string GetMapDisplayName(BattleMap map) => map switch
        {
            BattleMap.City => "CITY",
            BattleMap.Forest => "FOREST",
            _ => "DESERT"
        };

        /// <summary>
        /// Generates crisp pixel-art battlefield terrain for Desert, City, or Forest at any resolution
        /// (2048x1360 for expanded Battle maps, or smaller preview for Hangar turntable!).
        /// </summary>
        public static Color32[] GenerateMapPixels(BattleMap map, int w = WorldWidthPx, int h = WorldHeightPx)
        {
            return map switch
            {
                BattleMap.City => GenerateCityPixels(w, h),
                BattleMap.Forest => GenerateForestPixels(w, h),
                _ => GenerateDesertPixels(w, h)
            };
        }

        public static Color32[] GenerateDesertPixels(int w = WorldWidthPx, int h = WorldHeightPx)
        {
            var px = new Color32[w * h];
            Color32 sandBase   = new Color32(217, 183, 124, 255);
            Color32 sandLight  = new Color32(228, 198, 142, 255);
            Color32 sandShade  = new Color32(201, 163, 102, 255);
            Color32 sandDeep   = new Color32(188, 147,  87, 255);
            Color32 roadCol    = new Color32(182, 149,  98, 255);
            Color32 padCol     = new Color32(174, 164, 144, 255);
            Color32 padEdge    = new Color32(124, 114,  96, 255);
            Color32 groveEarth = new Color32(162, 148,  96, 255);
            Color32 groveDry   = new Color32(184, 158, 108, 255);
            Color32 canyonDark = new Color32(122,  91,  58, 255);
            Color32 canyonTop  = new Color32(156, 120,  78, 255);
            Color32 canyonLip  = new Color32( 77,  56,  34, 255);

            float cx = w * 0.5f;
            float cy = h * 0.5f;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color32 c;
                    if (x < BorderPadPx || x >= w - BorderPadPx || y < BorderPadPx || y >= h - BorderPadPx)
                    {
                        bool isLip = x == BorderPadPx - 1 || x == w - BorderPadPx || y == BorderPadPx - 1 || y == h - BorderPadPx;
                        bool isOuter = x < 4 || x >= w - 4 || y < 4 || y >= h - 4;
                        c = isLip ? canyonLip : (isOuter ? canyonDark : canyonTop);
                    }
                    else
                    {
                        float wave =
                            Mathf.Sin(x * 0.013f + y * 0.020f) +
                            0.55f * Mathf.Sin(x * 0.028f - y * 0.014f + 1.3f) +
                            0.25f * Mathf.Cos(y * 0.042f);
                        c = wave > 0.72f ? sandLight :
                            wave < -1.1f ? sandDeep :
                            wave < -0.68f ? sandShade : sandBase;

                        // Wide East-West highway + North-South crossroad + North & South canyon trails
                        bool mainRoad = Mathf.Abs(y - cy) < 34 || Mathf.Abs(x - cx) < 36;
                        bool northTrail = Mathf.Abs(y - (h * 0.80f + Mathf.Sin(x * 0.006f) * 32f)) < 20;
                        bool southTrail = Mathf.Abs(y - (h * 0.20f - Mathf.Sin(x * 0.006f) * 28f)) < 18;
                        if (mainRoad || northTrail || southTrail)
                            c = Color32.Lerp(c, roadCol, 0.38f);

                        // Contested Central Outpost Concrete Foundation Pad
                        if (Mathf.Abs(x - cx) <= 108 && Mathf.Abs(y - cy) <= 78)
                        {
                            bool isPadEdge = Mathf.Abs(x - cx) >= 104 || Mathf.Abs(y - cy) >= 74;
                            bool isTileLine = ((x % 28) == 0) || ((y % 28) == 0);
                            c = isPadEdge ? padEdge : (isTileLine ? Color32.Lerp(padCol, padEdge, 0.45f) : padCol);
                        }

                        // Southern Dry Palm Grove Basin (no water!)
                        float odx = (x - cx) / 140f;
                        float ody = (y - h * 0.19f) / 68f;
                        float od = odx * odx + ody * ody;
                        if (od < 0.72f) c = Color32.Lerp(c, groveEarth, 0.44f);
                        else if (od < 1.32f) c = Color32.Lerp(c, groveDry, 0.32f);
                    }
                    px[y * w + x] = c;
                }
            }

            int tuftCount = Mathf.RoundToInt((w * h) / 1750f);
            for (int i = 0; i < tuftCount; i++)
            {
                int gx = ((i * 227 + 61) % (w - 64)) + 32;
                int gy = ((i * 151 + 97) % (h - 64)) + 32;
                if (Mathf.Abs(gx - cx) < 114 && Mathf.Abs(gy - cy) < 84) continue;
                Color32 col = (i % 6 == 0) ? new Color32(114, 128, 68, 255) : new Color32(158, 130, 90, 255);
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                        px[(gy + dy) * w + (gx + dx)] = col;
            }
            return px;
        }

        public static Color32[] GenerateCityPixels(int w, int h)
        {
            var px = new Color32[w * h];
            Color32 asphalt      = new Color32( 78,  80,  84, 255);
            Color32 asphaltDark  = new Color32( 66,  68,  72, 255);
            Color32 laneDash     = new Color32(198, 186, 124, 255);
            Color32 sidewalk     = new Color32(156, 152, 144, 255);
            Color32 curb         = new Color32(118, 114, 108, 255);
            Color32 courtyard    = new Color32(138, 134, 126, 255);
            Color32 cobbleA      = new Color32(152, 144, 132, 255);
            Color32 cobbleB      = new Color32(134, 126, 116, 255);
            Color32 borderWall   = new Color32( 88,  86,  82, 255);
            Color32 borderDark   = new Color32( 58,  56,  54, 255);

            float cx = w * 0.5f;
            float cy = h * 0.5f;
            float[] roadYs = { h * 0.24f, h * 0.50f, h * 0.76f };
            float[] roadXs = { w * 0.24f, w * 0.50f, w * 0.76f };

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (x < BorderPadPx || x >= w - BorderPadPx || y < BorderPadPx || y >= h - BorderPadPx)
                    {
                        bool lip = x == BorderPadPx - 1 || x == w - BorderPadPx || y == BorderPadPx - 1 || y == h - BorderPadPx;
                        px[y * w + x] = lip ? borderDark : borderWall;
                        continue;
                    }

                    // Check distance to nearest horizontal / vertical city boulevard
                    float minDy = 9999f;
                    for (int i = 0; i < roadYs.Length; i++)
                    {
                        float d = Mathf.Abs(y - roadYs[i]);
                        if (d < minDy) minDy = d;
                    }
                    float minDx = 9999f;
                    for (int i = 0; i < roadXs.Length; i++)
                    {
                        float d = Mathf.Abs(x - roadXs[i]);
                        if (d < minDx) minDx = d;
                    }

                    // Central Grand Cobblestone Plaza
                    float pdx = Mathf.Abs(x - cx);
                    float pdy = Mathf.Abs(y - cy);
                    if (pdx <= 116 && pdy <= 86)
                    {
                        bool edge = pdx >= 111 || pdy >= 81;
                        bool tile = (((x / 10) + (y / 10)) & 1) == 0;
                        bool ring = Mathf.Abs(Mathf.Sqrt(pdx * pdx + pdy * pdy) - 48f) < 3.5f;
                        px[y * w + x] = edge ? curb : (ring ? sidewalk : (tile ? cobbleA : cobbleB));
                        continue;
                    }

                    const float roadHalf = 42f;
                    const float walkHalf = 54f;
                    bool onRoadH = minDy < roadHalf;
                    bool onRoadV = minDx < roadHalf;

                    Color32 c;
                    if (onRoadH || onRoadV)
                    {
                        bool grain = (((x * 13 + y * 29) & 15) == 0);
                        c = grain ? asphaltDark : asphalt;
                        // Dashed yellow-white center lane markings (outside intersections)
                        bool dashH = onRoadH && !onRoadV && minDy < 1.6f && ((x % 28) < 13);
                        bool dashV = onRoadV && !onRoadH && minDx < 1.6f && ((y % 28) < 13);
                        if (dashH || dashV) c = laneDash;
                    }
                    else if (minDy < walkHalf || minDx < walkHalf)
                    {
                        bool isCurb = (minDy < roadHalf + 3f) || (minDx < roadHalf + 3f);
                        bool joint = ((x % 16) == 0) || ((y % 16) == 0);
                        c = isCurb ? curb : (joint ? Color32.Lerp(sidewalk, curb, 0.35f) : sidewalk);
                    }
                    else
                    {
                        // Interior urban block concrete courtyard / gravel yard
                        bool grid = ((x % 32) == 0) || ((y % 32) == 0);
                        c = grid ? Color32.Lerp(courtyard, curb, 0.3f) : courtyard;
                    }
                    px[y * w + x] = c;
                }
            }

            // Scattered urban rubble chips & weathered patches
            int specks = Mathf.RoundToInt((w * h) / 2200f);
            for (int i = 0; i < specks; i++)
            {
                int gx = ((i * 257 + 43) % (w - 64)) + 32;
                int gy = ((i * 173 + 89) % (h - 64)) + 32;
                Color32 col = (i % 3 == 0) ? new Color32(104, 102, 98, 255) : new Color32(168, 162, 152, 255);
                px[gy * w + gx] = col;
                px[gy * w + gx + 1] = col;
            }
            return px;
        }

        public static Color32[] GenerateForestPixels(int w, int h)
        {
            var px = new Color32[w * h];
            Color32 grassBase  = new Color32( 88, 118,  62, 255);
            Color32 grassLight = new Color32(106, 136,  72, 255);
            Color32 mossShade  = new Color32( 70,  98,  52, 255);
            Color32 pineDeep   = new Color32( 56,  80,  44, 255);
            Color32 dirtRoad   = new Color32(138, 112,  76, 255);
            Color32 mudEdge    = new Color32(108,  88,  58, 255);
            Color32 campClear  = new Color32(126, 108,  76, 255);
            Color32 borderPine = new Color32( 38,  58,  32, 255);
            Color32 borderEdge = new Color32( 28,  42,  24, 255);

            float cx = w * 0.5f;
            float cy = h * 0.5f;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (x < BorderPadPx || x >= w - BorderPadPx || y < BorderPadPx || y >= h - BorderPadPx)
                    {
                        bool lip = x == BorderPadPx - 1 || x == w - BorderPadPx || y == BorderPadPx - 1 || y == h - BorderPadPx;
                        px[y * w + x] = lip ? borderEdge : borderPine;
                        continue;
                    }

                    float wave =
                        Mathf.Sin(x * 0.011f + y * 0.016f) +
                        0.55f * Mathf.Cos(x * 0.024f - y * 0.019f + 0.9f) +
                        0.30f * Mathf.Sin(x * 0.041f + y * 0.033f);
                    Color32 c = wave > 0.65f ? grassLight :
                        wave < -0.85f ? pineDeep :
                        wave < -0.35f ? mossShade : grassBase;

                    // Winding main dirt logging road (East-West) + North-South forest trail
                    float roadY = cy + Mathf.Sin(x * 0.0045f) * 76f;
                    float roadX = cx + Mathf.Cos(y * 0.0055f) * 68f;
                    float dyR = Mathf.Abs(y - roadY);
                    float dxR = Mathf.Abs(x - roadX);
                    float dRoad = Mathf.Min(dyR, dxR);
                    if (dRoad < 28f)
                        c = Color32.Lerp(c, dirtRoad, 0.78f);
                    else if (dRoad < 38f)
                        c = Color32.Lerp(c, mudEdge, 0.45f);

                    // Central Timber Clearing
                    float cdx = (x - cx) / 138f;
                    float cdy = (y - cy) / 96f;
                    float cd = cdx * cdx + cdy * cdy;
                    if (cd < 0.72f) c = Color32.Lerp(c, campClear, 0.52f);
                    else if (cd < 1.18f) c = Color32.Lerp(c, grassLight, 0.35f);

                    px[y * w + x] = c;
                }
            }

            // Scattered wildflowers, ferns, mossy rocks, & fallen pine needles
            int tuftCount = Mathf.RoundToInt((w * h) / 1450f);
            for (int i = 0; i < tuftCount; i++)
            {
                int gx = ((i * 239 + 53) % (w - 64)) + 32;
                int gy = ((i * 163 + 79) % (h - 64)) + 32;
                Color32 col = (i % 7 == 0) ? new Color32(124, 146, 76, 255)
                    : (i % 5 == 0) ? new Color32(106, 108, 102, 255)
                    : new Color32(52, 74, 38, 255);
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                        px[(gy + dy) * w + (gx + dx)] = col;
            }
            return px;
        }

        private void LoadPropsMetaIfNeeded()
        {
            if (_propsMeta == null)
                _propsMeta = MetaLoader.LoadPropsMeta();
        }

        private Texture2D GetPropSheet(string kind)
        {
            if (_propTexCache.TryGetValue(kind, out Texture2D tex))
                return tex;
            tex = Resources.Load<Texture2D>($"Props/{kind}_slices");
            _propTexCache[kind] = tex;
            return tex;
        }

        private void ClearAndSpawnMapProps(BattleMap map)
        {
            LoadPropsMetaIfNeeded();
            _loadedMap = map;

            // Hide any legacy pre-placed scene props so each biome builds its full expanded 2048x1360 layout cleanly!
            var sceneProps = FindObjectsByType<DestructibleProp>(FindObjectsInactive.Include);
            for (int i = 0; i < sceneProps.Length; i++)
            {
                var sp = sceneProps[i];
                if (sp == null) continue;
                if (sp.isDynamicSpawned)
                    Destroy(sp.gameObject);
                else
                    sp.gameObject.SetActive(false);
            }
            _props.Clear();

            if (_mapPropsRoot != null) Destroy(_mapPropsRoot.gameObject);
            _mapPropsRoot = new GameObject($"MapProps_{map}").transform;

            if (battleMode == BattleMode.FiringRange)
            {
                BuildFiringRangeLayout(map);
                return;
            }

            switch (map)
            {
                case BattleMap.City:
                    BuildCityLayout();
                    break;
                case BattleMap.Forest:
                    BuildForestLayout();
                    break;
                default:
                    BuildDesertLayout();
                    break;
            }
        }

        /// <summary>
        /// Proving Ground / Firing Range layout: wide-open graduated target lanes (West -> East)
        /// with biome backdrops on the flanks + destructible cover test bay on the South!
        /// </summary>
        private void BuildFiringRangeLayout(BattleMap map)
        {
            // Biome scenic flanks along far North & South edges (never blocks any target lane!)
            if (map == BattleMap.City)
            {
                SpawnMapProp("city_factory", 0.34f, 0.90f, 0f);
                SpawnMapProp("city_apartment", 0.56f, 0.90f, 0f);
                SpawnMapProp("city_blockhouse", 0.78f, 0.90f, 0f);
                SpawnMapProp("city_blockhouse", 0.34f, 0.11f, 180f);
                SpawnMapProp("city_apartment", 0.62f, 0.11f, 0f);
            }
            else if (map == BattleMap.Forest)
            {
                SpawnMapProp("pine_cluster", 0.24f, 0.88f, 0f);
                SpawnMapProp("forest_boulder", 0.48f, 0.90f, 8f);
                SpawnMapProp("pine_cluster", 0.72f, 0.88f, 20f);
                SpawnMapProp("forest_cabin", 0.28f, 0.12f, 0f);
                SpawnMapProp("pine_cluster", 0.56f, 0.12f, -15f);
                SpawnMapProp("forest_boulder", 0.80f, 0.13f, -10f);
            }
            else
            {
                SpawnMapProp("desert_mesa", 0.30f, 0.90f, 6f);
                SpawnMapProp("desert_compound", 0.58f, 0.90f, 0f);
                SpawnMapProp("desert_mesa", 0.82f, 0.90f, -10f);
                SpawnMapProp("palm_tree", 0.25f, 0.14f, 15f);
                SpawnMapProp("palm_tree", 0.31f, 0.12f, -20f);
                SpawnMapProp("desert_mesa", 0.64f, 0.11f, 12f);
            }

            // Firing-line range markers & observation towers flanking the player bay
            SpawnMapProp("watch_tower", 0.14f, 0.74f, 0f);
            SpawnMapProp("watch_tower", 0.14f, 0.26f, 0f);
            SpawnMapProp("sandbag_wall", 0.20f, 0.58f, 90f);
            SpawnMapProp("sandbag_wall", 0.20f, 0.42f, 90f);

            // Destructible Cover & Explosive Test Bay (South-Center: test HE/crumbling/chain blasts!)
            SpawnMapProp("fuel_tank", 0.36f, 0.21f, 0f);
            SpawnMapProp("supply_crate", 0.41f, 0.21f, 0f);
            SpawnMapProp("hesco_barrier", 0.46f, 0.21f, 0f);
            SpawnMapProp("desert_bunker", 0.52f, 0.21f, 0f);
            SpawnMapProp("tank_trap", 0.58f, 0.21f, 0f);
        }

        // =====================================================================
        // FIRING RANGE / PROVING GROUND TARGETS (6 Non-Hostile Graduated Targets!)
        // =====================================================================
        private VehicleDef PickRangeTarget(VehicleDef playerDef, string preferredRole, int tierOffset)
        {
            if (_allVehicles == null || _allVehicles.Length == 0) return SelectedEnemyVehicle;
            string rivalNation = (playerDef != null && playerDef.nation == "USSR") ? "USA" : "USSR";
            int desiredTier = Mathf.Clamp((playerDef != null ? playerDef.tier : 11) + tierOffset,
                VehicleDef.LowestTier, VehicleDef.LastRegularTier);

            VehicleDef best = null;
            int bestScore = int.MaxValue;
            for (int i = 0; i < _allVehicles.Length; i++)
            {
                var v = _allVehicles[i];
                if (v == null || v.nation != rivalNation) continue;
                int score = Mathf.Abs(v.tier - desiredTier) * 10 + (v.role == preferredRole ? 0 : 6);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = v;
                }
            }
            return best ?? SelectedEnemyVehicle;
        }

        private void SpawnFiringRangeUnits(VehicleDef playerDef)
        {
            _rangeKills = 0;

            // Player spawns on the wide-open West firing bay facing East (90 deg)
            if (_sceneAbrams != null)
            {
                _sceneAbrams.gameObject.SetActive(true);
                _sceneAbrams.isPlayer = true;
                _sceneAbrams.factionTeam = "ally";
                ApplyVehicleToTank(_sceneAbrams, playerDef);
                _sceneAbrams.CaptureSpawnAndReset(new Vector2(WorldW * 0.14f, WorldH * 0.50f), 90f);
                _tanks.Add(_sceneAbrams);
            }

            // 6 Graduated Proving-Ground Targets (Close Light, Frontal MBT, Broadside MBT, Rear Engine MBT, Long-Range Heavy, Moving Patrol!)
            var specs = new (VehicleDef def, Vector2 pos, float angle, bool moving)[]
            {
                (PickRangeTarget(playerDef, "LIGHT", -1), new Vector2(WorldW * 0.30f, WorldH * 0.66f), -135f, false), // 1. Close Angled Light/IFV
                (PickRangeTarget(playerDef, "MBT",    0), new Vector2(WorldW * 0.40f, WorldH * 0.50f),  -90f, false), // 2. Mid Frontal Armor MBT
                (PickRangeTarget(playerDef, "MBT",    0), new Vector2(WorldW * 0.42f, WorldH * 0.34f),    0f, false), // 3. Mid Broadside Side-Armor MBT
                (PickRangeTarget(playerDef, "SUPPORT",0), new Vector2(WorldW * 0.55f, WorldH * 0.66f),   90f, false), // 4. Mid-Long Rear Engine Exposure
                (PickRangeTarget(playerDef, "MBT",   +1), new Vector2(WorldW * 0.64f, WorldH * 0.46f), -118f, false), // 5. Long-Range Top-Tier Angled MBT
                (PickRangeTarget(playerDef, "LIGHT",  0), new Vector2(WorldW * 0.78f, WorldH * 0.50f),  180f, true),  // 6. Far-East Moving Patrol Target!
            };

            for (int i = 0; i < specs.Length; i++)
            {
                var sp = specs[i];
                TankEntity t;
                if (i == 0 && _sceneT90 != null)
                {
                    _sceneT90.gameObject.SetActive(true);
                    _sceneT90.isPlayer = false;
                    _sceneT90.factionTeam = "enemy";
                    ApplyVehicleToTank(_sceneT90, sp.def);
                    _sceneT90.CaptureSpawnAndReset(sp.pos, sp.angle);
                    _tanks.Add(_sceneT90);
                    t = _sceneT90;
                }
                else
                {
                    t = SpawnExtraTank(sp.def, "enemy", sp.pos, sp.angle, isPlayerTank: false);
                }

                if (t != null)
                {
                    _rangeTargets.Add(new RangeTargetSlot
                    {
                        tank = t,
                        spawnPos = sp.pos,
                        spawnAngle = sp.angle,
                        isMovingPatrol = sp.moving,
                        patrolDir = 1f,
                        respawnTimer = 0f
                    });
                }
            }
        }

        private void UpdateFiringRangeTargets(float dt)
        {
            float minY = WorldH * 0.24f;
            float maxY = WorldH * 0.76f;

            for (int i = 0; i < _rangeTargets.Count; i++)
            {
                var slot = _rangeTargets[i];
                if (slot == null || slot.tank == null) continue;

                if (!slot.tank.alive)
                {
                    if (slot.respawnTimer <= 0f) slot.respawnTimer = 4.5f;
                    slot.respawnTimer -= Time.unscaledDeltaTime;
                    if (slot.respawnTimer <= 0f)
                    {
                        slot.tank.CaptureSpawnAndReset(slot.spawnPos, slot.spawnAngle);
                        AddPopup(slot.spawnPos + Vector2.up * 1.4f, "TARGET RESET", new Color(0.62f, 0.88f, 0.98f));
                    }
                    continue;
                }

                if (slot.isMovingPatrol)
                {
                    // Smooth North <-> South lead-target patrol (never shoots back!)
                    float y = slot.tank.Position2D.y;
                    if (y >= maxY) slot.patrolDir = -1f;
                    else if (y <= minY) slot.patrolDir = 1f;

                    float targetHull = slot.patrolDir > 0f ? 180f : 0f;
                    slot.tank.hullAngleDeg = Mathf.MoveTowardsAngle(slot.tank.hullAngleDeg, targetHull, 140f * dt);
                    slot.tank.turretAngleDeg = Mathf.MoveTowardsAngle(slot.tank.turretAngleDeg,
                        LimitGunTraverse(slot.tank, -90f), 90f * dt);

                    float spd = (44f / PPU) * slot.tank.DriveFactor;
                    slot.tank.moveSpeed = spd;
                    Vector2 dir = TankEntity.SliceAngleToDir(slot.tank.hullAngleDeg);
                    slot.tank.Position2D += SpriteStackCamera.Squash(dir * (spd * dt), squash);
                    slot.tank.treadAccum += spd * dt;
                }
                else
                {
                    slot.tank.moveSpeed = 0f;
                }
            }
        }

        private void SpawnMapProp(string kind, float normX, float normY, float angleDeg = 0f)
        {
            PropMetaEntry meta = _propsMeta != null ? _propsMeta.GetByKind(kind) : null;
            Texture2D sheet = GetPropSheet(kind);
            if (meta == null || sheet == null) return;

            var go = new GameObject($"Prop_{kind}_{_props.Count:00}");
            go.transform.SetParent(_mapPropsRoot, false);
            go.transform.position = new Vector3(normX * WorldW, normY * WorldH, 0f);

            var prop = go.AddComponent<DestructibleProp>();
            prop.ConfigureRuntime(kind, meta, sheet, angleDeg, PPU, _stackFlashMat);
            _props.Add(prop);
        }

        // =====================================================================
        // HANDCRAFTED EXPANDED 2048x1360 TACTICAL MAP LAYOUTS (LARGE OBSTACLES!)
        // =====================================================================
        private void BuildDesertLayout()
        {
            // 1. Towering Sandstone Mesas (HUGE natural flank cover!)
            SpawnMapProp("desert_mesa", 0.27f, 0.80f, 8f);
            SpawnMapProp("desert_mesa", 0.73f, 0.80f, -12f);
            SpawnMapProp("desert_mesa", 0.25f, 0.22f, -6f);
            SpawnMapProp("desert_mesa", 0.75f, 0.22f, 10f);

            // 2. Massive Fortified Desert Compounds (major mid-lane strongholds!)
            SpawnMapProp("desert_compound", 0.36f, 0.62f, 0f);
            SpawnMapProp("desert_compound", 0.64f, 0.38f, 180f);

            // 3. Central Outpost Bunkers, Watch Towers & Fuel Depots
            SpawnMapProp("desert_bunker", 0.44f, 0.54f, 0f);
            SpawnMapProp("desert_bunker", 0.56f, 0.46f, 180f);
            SpawnMapProp("watch_tower", 0.50f, 0.72f, 0f);
            SpawnMapProp("watch_tower", 0.50f, 0.29f, 0f);
            SpawnMapProp("fuel_tank", 0.46f, 0.45f, 0f);
            SpawnMapProp("fuel_tank", 0.54f, 0.55f, 0f);

            // 4. Dry Southern Palm Grove (kept lush palm trees — ZERO water!)
            SpawnMapProp("palm_tree", 0.43f, 0.19f, 10f);
            SpawnMapProp("palm_tree", 0.48f, 0.14f, -20f);
            SpawnMapProp("palm_tree", 0.53f, 0.21f, 35f);
            SpawnMapProp("palm_tree", 0.58f, 0.15f, -15f);
            SpawnMapProp("palm_tree", 0.46f, 0.24f, 50f);
            SpawnMapProp("palm_tree", 0.55f, 0.24f, -40f);
            SpawnMapProp("desert_rock", 0.50f, 0.17f, 12f);
            SpawnMapProp("desert_rock", 0.39f, 0.84f, -18f);
            SpawnMapProp("desert_rock", 0.61f, 0.84f, 22f);

            // 5. Tactical Mid-Field Cover (HESCO, Ruins, Sandbags, Explosive Crates, Tank Traps)
            SpawnMapProp("desert_ruin", 0.34f, 0.36f, 15f);
            SpawnMapProp("desert_ruin", 0.66f, 0.64f, -15f);
            SpawnMapProp("hesco_barrier", 0.22f, 0.56f, 0f);
            SpawnMapProp("hesco_barrier", 0.78f, 0.44f, 0f);
            SpawnMapProp("sandbag_wall", 0.30f, 0.50f, 90f);
            SpawnMapProp("sandbag_wall", 0.70f, 0.50f, 90f);
            SpawnMapProp("supply_crate", 0.42f, 0.68f, 0f);
            SpawnMapProp("supply_crate", 0.58f, 0.32f, 0f);
            SpawnMapProp("tank_trap", 0.23f, 0.42f, 12f);
            SpawnMapProp("tank_trap", 0.77f, 0.58f, -12f);
        }

        private void BuildCityLayout()
        {
            // 1. Massive Industrial Factories anchoring the Northern District!
            SpawnMapProp("city_factory", 0.37f, 0.87f, 0f);
            SpawnMapProp("city_factory", 0.63f, 0.87f, 0f);

            // 2. Towering 3-Story European Apartment Blocks creating real city boulevards & flanking alleys!
            SpawnMapProp("city_apartment", 0.37f, 0.63f, 0f);
            SpawnMapProp("city_apartment", 0.63f, 0.63f, 0f);
            SpawnMapProp("city_apartment", 0.37f, 0.37f, 0f);
            SpawnMapProp("city_apartment", 0.63f, 0.37f, 0f);
            SpawnMapProp("city_apartment", 0.37f, 0.13f, 0f);
            SpawnMapProp("city_apartment", 0.63f, 0.13f, 0f);

            // 3. Fortified Corner Townhouses / Blockhouses framing West & East urban approaches!
            SpawnMapProp("city_blockhouse", 0.17f, 0.64f, 0f);
            SpawnMapProp("city_blockhouse", 0.17f, 0.36f, 0f);
            SpawnMapProp("city_blockhouse", 0.83f, 0.64f, 180f);
            SpawnMapProp("city_blockhouse", 0.83f, 0.36f, 180f);
            SpawnMapProp("city_blockhouse", 0.50f, 0.87f, 0f);
            SpawnMapProp("city_blockhouse", 0.50f, 0.13f, 180f);

            // 4. Street Barricades, Anti-Tank Traps, Fuel Tanks & Sandbags along avenues & central plaza
            SpawnMapProp("city_barricade", 0.45f, 0.50f, 90f);
            SpawnMapProp("city_barricade", 0.55f, 0.50f, 90f);
            SpawnMapProp("city_barricade", 0.50f, 0.60f, 0f);
            SpawnMapProp("city_barricade", 0.50f, 0.40f, 0f);
            SpawnMapProp("city_barricade", 0.24f, 0.76f, 0f);
            SpawnMapProp("city_barricade", 0.76f, 0.24f, 0f);

            SpawnMapProp("fuel_tank", 0.43f, 0.76f, 0f);
            SpawnMapProp("fuel_tank", 0.57f, 0.24f, 0f);
            SpawnMapProp("supply_crate", 0.29f, 0.50f, 0f);
            SpawnMapProp("supply_crate", 0.71f, 0.50f, 0f);
            SpawnMapProp("tank_trap", 0.24f, 0.56f, 15f);
            SpawnMapProp("tank_trap", 0.76f, 0.44f, -15f);
            SpawnMapProp("sandbag_wall", 0.47f, 0.44f, 0f);
            SpawnMapProp("sandbag_wall", 0.53f, 0.56f, 0f);
        }

        private void BuildForestLayout()
        {
            // 1. Massive Mossy Granite Cliff Ridges (immovable tactical bastions!)
            SpawnMapProp("forest_boulder", 0.34f, 0.76f, 12f);
            SpawnMapProp("forest_boulder", 0.66f, 0.76f, -14f);
            SpawnMapProp("forest_boulder", 0.34f, 0.24f, -10f);
            SpawnMapProp("forest_boulder", 0.66f, 0.24f, 16f);
            SpawnMapProp("forest_boulder", 0.50f, 0.50f, 5f);

            // 2. Sturdy Woodland Log Cabins / Farmhouses
            SpawnMapProp("forest_cabin", 0.43f, 0.64f, 0f);
            SpawnMapProp("forest_cabin", 0.57f, 0.36f, 180f);
            SpawnMapProp("forest_cabin", 0.22f, 0.58f, 14f);
            SpawnMapProp("forest_cabin", 0.78f, 0.42f, -14f);

            // 3. Dense Towering Pine Tree Clusters forming rich woodland ambush corridors!
            SpawnMapProp("pine_cluster", 0.20f, 0.82f, 0f);
            SpawnMapProp("pine_cluster", 0.26f, 0.72f, 25f);
            SpawnMapProp("pine_cluster", 0.80f, 0.82f, -20f);
            SpawnMapProp("pine_cluster", 0.74f, 0.72f, 15f);
            SpawnMapProp("pine_cluster", 0.20f, 0.18f, 35f);
            SpawnMapProp("pine_cluster", 0.26f, 0.29f, -12f);
            SpawnMapProp("pine_cluster", 0.80f, 0.18f, -30f);
            SpawnMapProp("pine_cluster", 0.74f, 0.29f, 20f);
            SpawnMapProp("pine_cluster", 0.48f, 0.82f, 8f);
            SpawnMapProp("pine_cluster", 0.52f, 0.18f, -18f);
            SpawnMapProp("pine_cluster", 0.33f, 0.48f, 40f);
            SpawnMapProp("pine_cluster", 0.67f, 0.52f, -35f);

            // 4. Forest Fire-Lookout Towers, Timber Supply Depots & Field Defenses
            SpawnMapProp("watch_tower", 0.42f, 0.34f, 0f);
            SpawnMapProp("watch_tower", 0.58f, 0.66f, 0f);
            SpawnMapProp("fuel_tank", 0.46f, 0.60f, 0f);
            SpawnMapProp("fuel_tank", 0.54f, 0.40f, 0f);
            SpawnMapProp("supply_crate", 0.38f, 0.56f, 0f);
            SpawnMapProp("supply_crate", 0.62f, 0.44f, 0f);
            SpawnMapProp("sandbag_wall", 0.26f, 0.44f, 90f);
            SpawnMapProp("sandbag_wall", 0.74f, 0.56f, 90f);
            SpawnMapProp("tank_trap", 0.44f, 0.44f, 20f);
            SpawnMapProp("tank_trap", 0.56f, 0.56f, -20f);
        }
    }
}
