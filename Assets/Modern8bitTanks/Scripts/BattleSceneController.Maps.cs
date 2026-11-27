using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;

namespace Modern8bitTanks
{
    public partial class BattleSceneController
    {
        private readonly Dictionary<string, Texture2D> _propTexCache = new();
        private PropsMetaFile _propsMeta;
        [SerializeField] private Transform _mapPropsRoot;

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
        public static Color32[] GenerateMapPixels(BattleMap map, int w = WorldWidthPx, int h = WorldHeightPx) =>
            GenerateMapPixelsAsync(map, w, h, null, null).GetAwaiter().GetResult();

        private static UniTask<Color32[]> GenerateMapPixelsAsync(BattleMap map, int w, int h,
            BattleLoadBudget budget, System.Action<float> progress)
        {
            return map switch
            {
                BattleMap.City => GenerateCityPixelsAsync(w, h, budget, progress),
                BattleMap.Forest => GenerateForestPixelsAsync(w, h, budget, progress),
                _ => GenerateDesertPixelsAsync(w, h, budget, progress)
            };
        }

        public static Color32[] GenerateDesertPixels(int w = WorldWidthPx, int h = WorldHeightPx) =>
            GenerateDesertPixelsAsync(w, h, null, null).GetAwaiter().GetResult();

        private static async UniTask<Color32[]> GenerateDesertPixelsAsync(int w, int h,
            BattleLoadBudget budget, System.Action<float> progress)
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

                        // Wide East-West military convoy highway + North-South crossroad + canyon supply trails
                        float dyMain = Mathf.Abs(y - cy);
                        float dxMain = Mathf.Abs(x - cx);
                        bool onMainH = dyMain < 28f;
                        bool onMainV = dxMain < 28f;
                        bool onNorthTrail = Mathf.Abs(y - (h * 0.80f + Mathf.Sin(x * 0.006f) * 30f)) < 17f;
                        bool onSouthTrail = Mathf.Abs(y - (h * 0.20f - Mathf.Sin(x * 0.006f) * 28f)) < 17f;
                        if (onMainH || onMainV)
                        {
                            Color32 tarmac = new Color32(124, 112, 92, 255);
                            c = Color32.Lerp(c, tarmac, 0.72f);
                            bool rut = (onMainH && Mathf.Abs(dyMain - 12f) < 2.2f) || (onMainV && Mathf.Abs(dxMain - 12f) < 2.2f);
                            if (rut) c = Color32.Lerp(c, canyonDark, 0.32f);
                            bool dash = (onMainH && !onMainV && dyMain < 1.3f && (x % 36) < 16)
                                || (onMainV && !onMainH && dxMain < 1.3f && (y % 36) < 16);
                            if (dash) c = Color32.Lerp(c, sandLight, 0.65f);
                        }
                        else if (onNorthTrail || onSouthTrail)
                        {
                            c = Color32.Lerp(c, new Color32(142, 112, 74, 255), 0.72f);
                        }

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
                if ((y & 31) == 31) progress?.Invoke((y + 1) / (float)h);
                if (budget != null) await budget.YieldIfSpentAsync();
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

        public static Color32[] GenerateCityPixels(int w, int h) =>
            GenerateCityPixelsAsync(w, h, null, null).GetAwaiter().GetResult();

        private static async UniTask<Color32[]> GenerateCityPixelsAsync(int w, int h,
            BattleLoadBudget budget, System.Action<float> progress)
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
                        float boxInter = Mathf.Max(minDx, minDy);
                        bool nearInter = minDx < roadHalf + 14f && minDy < roadHalf + 14f;
                        // Dashed yellow-white center lane markings (outside intersections)
                        bool dashH = onRoadH && !nearInter && minDy < 1.6f && ((x % 28) < 13);
                        bool dashV = onRoadV && !nearInter && minDx < 1.6f && ((y % 28) < 13);
                        if (dashH || dashV) c = laneDash;
                        // Crisp white zebra crosswalk stripes right outside every 4-way intersection
                        bool xwalkH = onRoadH && !onRoadV && boxInter >= roadHalf + 2f && boxInter <= roadHalf + 11f && minDy < roadHalf - 4f && (((int)minDy % 7) < 3);
                        bool xwalkV = onRoadV && !onRoadH && boxInter >= roadHalf + 2f && boxInter <= roadHalf + 11f && minDx < roadHalf - 4f && (((int)minDx % 7) < 3);
                        if (xwalkH || xwalkV) c = Color32.Lerp(c, new Color32(208, 206, 196, 255), 0.72f);
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
                if ((y & 31) == 31) progress?.Invoke((y + 1) / (float)h);
                if (budget != null) await budget.YieldIfSpentAsync();
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

        public static Color32[] GenerateForestPixels(int w, int h) =>
            GenerateForestPixelsAsync(w, h, null, null).GetAwaiter().GetResult();

        private static async UniTask<Color32[]> GenerateForestPixelsAsync(int w, int h,
            BattleLoadBudget budget, System.Action<float> progress)
        {
            var px = new Color32[w * h];
            Color32 grassBase   = new Color32(100, 132,  66, 255);
            Color32 grassSunlit = new Color32(128, 158,  78, 255);
            Color32 mossShade   = new Color32( 74, 106,  54, 255);
            Color32 pineDeep    = new Color32( 58,  86,  46, 255);
            Color32 dirtRoad    = new Color32(154, 126,  86, 255);
            Color32 rutDark     = new Color32(114,  90,  58, 255);
            Color32 mudEdge     = new Color32(118,  98,  66, 255);
            Color32 campClear   = new Color32(138, 118,  84, 255);
            Color32 brookShallow = new Color32( 62, 116, 124, 255);
            Color32 brookDeep    = new Color32( 38,  78,  98, 255);
            Color32 bankStone    = new Color32(102,  94,  72, 255);
            Color32 bridgeWood   = new Color32(146, 108,  68, 255);
            Color32 bridgeRail   = new Color32( 66,  48,  32, 255);
            Color32 borderPine   = new Color32( 38,  58,  32, 255);
            Color32 borderEdge   = new Color32( 28,  42,  24, 255);

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
                    Color32 c = wave > 0.62f ? grassSunlit :
                        wave < -0.82f ? pineDeep :
                        wave < -0.28f ? mossShade : grassBase;

                    // Twin winding woodland brooks curving through West & East valleys
                    float streamW = w * 0.30f + Mathf.Sin(y * 0.0062f + 0.5f) * 58f;
                    float streamE = w * 0.70f - Mathf.Sin(y * 0.0062f + 0.5f) * 58f;
                    float dStream = Mathf.Min(Mathf.Abs(x - streamW), Mathf.Abs(x - streamE));
                    bool inBrook = dStream < 18f;
                    if (inBrook)
                    {
                        c = Color32.Lerp(brookDeep, brookShallow, dStream / 18f);
                        if (Mathf.Sin(x * 0.16f + y * 0.24f) > 0.86f && dStream < 12f)
                            c = Color32.Lerp(c, new Color32(174, 212, 222, 255), 0.52f);
                    }
                    else if (dStream < 26f)
                    {
                        c = Color32.Lerp(c, bankStone, (26f - dStream) / 8f * 0.68f);
                    }

                    // Connected winding dirt highway + North-South crossroad + North & South forest trails
                    float roadY = cy + Mathf.Sin(x * 0.0045f) * 68f;
                    float roadX = cx + Mathf.Cos(y * 0.0055f) * 54f;
                    float trailN = h * 0.80f + Mathf.Sin(x * 0.005f) * 24f;
                    float trailS = h * 0.20f - Mathf.Sin(x * 0.005f) * 24f;
                    float dyMain = Mathf.Abs(y - roadY);
                    float dxMain = Mathf.Abs(x - roadX);
                    float dyTrail = Mathf.Min(Mathf.Abs(y - trailN), Mathf.Abs(y - trailS));
                    float dRoad = Mathf.Min(Mathf.Min(dyMain, dxMain), dyTrail + 6f);
                    if (dRoad < 24f)
                    {
                        if (inBrook)
                        {
                            // Timber bridge crossing over the woodland brook!
                            bool rail = dRoad > 20.5f;
                            bool plank = (((int)(x + y)) % 6) < 4;
                            c = rail ? bridgeRail : (plank ? bridgeWood : rutDark);
                        }
                        else
                        {
                            c = Color32.Lerp(c, dirtRoad, 0.86f);
                            bool rut = Mathf.Abs(dRoad - 10.5f) < 2.4f;
                            if (rut) c = Color32.Lerp(c, rutDark, 0.48f);
                        }
                    }
                    else if (dRoad < 32f && !inBrook)
                    {
                        c = Color32.Lerp(c, mudEdge, 0.45f);
                    }

                    // Central Timber Clearing
                    float cdx = (x - cx) / 132f;
                    float cdy = (y - cy) / 92f;
                    float cd = cdx * cdx + cdy * cdy;
                    if (cd < 0.72f) c = Color32.Lerp(c, campClear, 0.62f);
                    else if (cd < 1.18f) c = Color32.Lerp(c, grassSunlit, 0.35f);

                    px[y * w + x] = c;
                }
                if ((y & 31) == 31) progress?.Invoke((y + 1) / (float)h);
                if (budget != null) await budget.YieldIfSpentAsync();
            }

            // Scattered wildflowers, clover & mossy stone accents
            int tuftCount = Mathf.RoundToInt((w * h) / 1600f);
            for (int i = 0; i < tuftCount; i++)
            {
                int gx = ((i * 239 + 53) % (w - 64)) + 32;
                int gy = ((i * 163 + 79) % (h - 64)) + 32;
                Color32 col = (i % 9 == 0) ? new Color32(214, 198, 112, 255)
                    : (i % 7 == 0) ? new Color32(138, 166, 84, 255)
                    : (i % 5 == 0) ? new Color32(118, 118, 108, 255)
                    : new Color32(62, 92, 46, 255);
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
            if (tex == null)
                tex = Resources.Load<Texture2D>($"Props/prop_{kind}_slices");
            _propTexCache[kind] = tex;
            return tex;
        }

        private async UniTask ClearAndSpawnMapPropsAsync(BattleMap map, BattleLoadBudget budget)
        {
            LoadPropsMetaIfNeeded();

            // The runtime layout supplies all combat cover.
            foreach (var prop in _props)
            {
                if (prop == null) continue;
                prop.gameObject.SetActive(false);
                if (prop.isDynamicSpawned) Destroy(prop.gameObject);
            }
            _props.Clear();

            if (battleMode == BattleMode.FiringRange)
            {
                BuildFiringRangeLayout(map);
                return;
            }

            if (UsesControlPoints)
            {
                await SpawnProceduralCoverAsync(budget);
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
            if (AllVehicles.Length == 0) return SelectedEnemyVehicle;
            string rivalNation = (playerDef != null && playerDef.nation == "USSR") ? "USA" : "USSR";
            int desiredTier = Mathf.Clamp((playerDef != null ? playerDef.tier : 11) + tierOffset,
                VehicleDef.LowestTier, VehicleDef.LastRegularTier);

            VehicleDef best = null;
            int bestScore = int.MaxValue;
            for (int i = 0; i < AllVehicles.Length; i++)
            {
                var v = AllVehicles[i];
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

            TryDeployTank(_playerTank, playerDef, "ally",
                new Vector2(WorldW * 0.14f, WorldH * 0.50f), 90f, isPlayerTank: true);

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
                TankEntity t = i == 0 && TryDeployTank(_primaryEnemyTank, sp.def, "enemy", sp.pos, sp.angle, isPlayerTank: false)
                    ? _primaryEnemyTank
                    : SpawnTankFromPrefab(sp.def, "enemy", sp.pos, sp.angle, isPlayerTank: false);

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
            SpawnMapProp(kind, new Vector2(normX * WorldW, normY * WorldH), meta, sheet, angleDeg);
        }

        private void SpawnMapProp(string kind, Vector2 position, PropMetaEntry meta, Texture2D sheet, float angleDeg,
            DestructibleProp prefab = null)
        {
            var prop = Instantiate(prefab != null ? prefab : spawnAssets.GetCover(kind), _mapPropsRoot, false);
            prop.name = $"Prop_{kind}_{_props.Count:00}";
            prop.transform.position = position;
            prop.ConfigureRuntime(kind, meta, sheet, angleDeg, PPU, _stackFlashMat);
            _props.Add(prop);
        }

        // =====================================================================
        // SURVIVAL 2048x1360 TACTICAL MAP LAYOUTS (COHESIVE SCENARIO VARIATION)
        // Artificial facilities stay axis-aligned; natural clusters scatter organically.
        // =====================================================================
        private void SpawnSurvivalNaturalCluster(float cx, float cy, float rx, float ry, int count, string[] pool, System.Random rng)
        {
            float baseDeg = (float)(rng.NextDouble() * 360.0);
            for (int i = 0; i < count; i++)
            {
                float theta = (baseDeg + i * 137.5f + rng.Next(-25, 26)) * Mathf.Deg2Rad;
                float scale = 0.42f + (float)rng.NextDouble() * 0.58f;
                float nx = Mathf.Clamp(cx + Mathf.Cos(theta) * rx * scale, 0.14f, 0.86f);
                float ny = Mathf.Clamp(cy + Mathf.Sin(theta) * ry * scale, 0.10f, 0.90f);
                string kind = pool[i % pool.Length];
                SpawnMapProp(kind, nx, ny, rng.Next(0, 360));
            }
        }

        private void BuildDesertLayout()
        {
            var rng = new System.Random(UnityEngine.Random.Range(1, int.MaxValue));
            int theme = rng.Next(3); // 0: Airbase, 1: Oasis Highway, 2: Canyon Artillery Front

            // 1. Towering Natural Flanks (organic free rotations)
            string flankRock = theme == 0 ? "desert_mesa" : (theme == 1 ? "rock_arch" : "rock_pillar");
            string subRock = theme == 2 ? "cliff_overhang" : "desert_mesa";
            SpawnMapProp(flankRock, 0.27f, 0.81f, rng.Next(0, 360));
            SpawnMapProp(flankRock, 0.73f, 0.81f, rng.Next(0, 360));
            SpawnMapProp(subRock, 0.24f, 0.21f, rng.Next(0, 360));
            SpawnMapProp(subRock, 0.76f, 0.21f, rng.Next(0, 360));

            // 2. Aligned Artificial Strongholds & Theme-Specific Functional Facilities
            if (theme == 0)
            {
                // Desert Air Defense Base
                SpawnMapProp("radar_dome", 0.36f, 0.64f, 0f);
                SpawnMapProp("radar_dome", 0.64f, 0.36f, 180f);
                SpawnMapProp("sam_launcher", 0.42f, 0.56f, 0f);
                SpawnMapProp("sam_launcher", 0.58f, 0.44f, 180f);
                SpawnMapProp("generator_unit", 0.31f, 0.66f, 0f);
                SpawnMapProp("generator_unit", 0.69f, 0.34f, 180f);
                SpawnMapProp("military_tent", 0.34f, 0.42f, 0f);
                SpawnMapProp("military_tent", 0.34f, 0.37f, 0f);
                SpawnMapProp("military_tent", 0.66f, 0.58f, 180f);
                SpawnMapProp("military_tent", 0.66f, 0.63f, 180f);
                SpawnMapProp("ammo_depot", 0.45f, 0.46f, 0f);
                SpawnMapProp("ammo_depot", 0.55f, 0.54f, 180f);
                SpawnMapProp("radio_mast", 0.50f, 0.73f, 0f);
                SpawnMapProp("watch_tower", 0.50f, 0.28f, 0f);
                SpawnMapProp("checkpoint_gate", 0.27f, 0.50f, 90f);
                SpawnMapProp("guard_booth", 0.27f, 0.56f, 90f);
                SpawnMapProp("checkpoint_gate", 0.73f, 0.50f, 90f);
                SpawnMapProp("guard_booth", 0.73f, 0.44f, 90f);
                SpawnMapProp("concrete_wall", 0.38f, 0.50f, 90f);
                SpawnMapProp("concrete_wall", 0.62f, 0.50f, 90f);
                SpawnMapProp("crashed_heli", 0.50f, 0.50f, 24f);
                SpawnMapProp("crater_boulders", 0.47f, 0.54f, 110f);
            }
            else if (theme == 1)
            {
                // Desert Oasis Route & Solar Outpost
                SpawnMapProp("desert_compound", 0.36f, 0.63f, 0f);
                SpawnMapProp("desert_compound", 0.64f, 0.37f, 180f);
                SpawnMapProp("solar_array", 0.33f, 0.55f, 0f);
                SpawnMapProp("solar_array", 0.33f, 0.51f, 0f);
                SpawnMapProp("solar_array", 0.67f, 0.45f, 180f);
                SpawnMapProp("solar_array", 0.67f, 0.49f, 180f);
                SpawnMapProp("water_tower", 0.42f, 0.68f, 0f);
                SpawnMapProp("radar_station", 0.58f, 0.32f, 180f);
                SpawnMapProp("fuel_pump_island", 0.46f, 0.50f, 0f);
                SpawnMapProp("fuel_pump_island", 0.54f, 0.50f, 180f);
                SpawnMapProp("abandoned_car", 0.46f, 0.44f, 0f);
                SpawnMapProp("abandoned_car", 0.54f, 0.56f, 180f);
                SpawnMapProp("cargo_container", 0.34f, 0.36f, 0f);
                SpawnMapProp("cargo_container", 0.34f, 0.33f, 0f);
                SpawnMapProp("cargo_container", 0.66f, 0.64f, 180f);
                SpawnMapProp("cargo_container", 0.66f, 0.67f, 180f);
                SpawnMapProp("oil_drum_stack", 0.40f, 0.35f, 0f);
                SpawnMapProp("oil_drum_stack", 0.60f, 0.65f, 180f);
                SpawnMapProp("hesco_barrier", 0.24f, 0.56f, 0f);
                SpawnMapProp("hesco_barrier", 0.76f, 0.44f, 0f);
                SpawnMapProp("sandbag_wall", 0.30f, 0.50f, 90f);
                SpawnMapProp("sandbag_wall", 0.70f, 0.50f, 90f);
            }
            else
            {
                // Desert Canyon Artillery Front
                SpawnMapProp("desert_bunker", 0.37f, 0.62f, 0f);
                SpawnMapProp("desert_bunker", 0.63f, 0.38f, 180f);
                SpawnMapProp("field_artillery", 0.43f, 0.55f, 0f);
                SpawnMapProp("field_artillery", 0.57f, 0.45f, 180f);
                SpawnMapProp("ammo_depot", 0.39f, 0.54f, 0f);
                SpawnMapProp("ammo_depot", 0.61f, 0.46f, 180f);
                SpawnMapProp("cargo_container", 0.33f, 0.39f, 0f);
                SpawnMapProp("forklift_wreck", 0.38f, 0.39f, 0f);
                SpawnMapProp("cargo_container", 0.67f, 0.61f, 180f);
                SpawnMapProp("forklift_wreck", 0.62f, 0.61f, 180f);
                SpawnMapProp("watch_tower", 0.50f, 0.72f, 0f);
                SpawnMapProp("watch_tower", 0.50f, 0.28f, 0f);
                SpawnMapProp("concrete_wall", 0.29f, 0.50f, 90f);
                SpawnMapProp("concrete_wall", 0.71f, 0.50f, 90f);
                SpawnMapProp("wrecked_tank", 0.50f, 0.50f, -18f);
                SpawnMapProp("crater_boulders", 0.53f, 0.54f, 200f);
                SpawnMapProp("tank_trap", 0.24f, 0.42f, 0f);
                SpawnMapProp("tank_trap", 0.76f, 0.58f, 0f);
            }

            // 3. Organic Natural Southern Basin & Canyon Verge Clusters
            if (theme == 1)
            {
                SpawnMapProp("oasis_spring", 0.50f, 0.18f, rng.Next(0, 360));
                SpawnSurvivalNaturalCluster(0.50f, 0.18f, 0.10f, 0.06f, 6,
                    new[] { "palm_tree", "palm_tree", "cactus_cluster", "desert_rock", "desert_scrub", "palm_tree" }, rng);
            }
            else
            {
                SpawnSurvivalNaturalCluster(0.50f, 0.18f, 0.11f, 0.06f, 6,
                    theme == 0
                        ? new[] { "rock_pillar", "desert_rock", "desert_scrub", "dead_tree", "desert_rock", "desert_scrub" }
                        : new[] { "cactus_cluster", "cliff_overhang", "dead_tree", "desert_rock", "desert_scrub", "stone_monolith" }, rng);
            }
            SpawnSurvivalNaturalCluster(0.40f, 0.84f, 0.06f, 0.04f, 3, new[] { "desert_rock", "desert_scrub", "cactus_cluster" }, rng);
            SpawnSurvivalNaturalCluster(0.60f, 0.84f, 0.06f, 0.04f, 3, new[] { "desert_rock", "desert_scrub", "cactus_cluster" }, rng);
        }

        private void BuildCityLayout()
        {
            var rng = new System.Random(UnityEngine.Random.Range(1, int.MaxValue));
            int theme = rng.Next(3); // 0: Rail Freight Yard, 1: Downtown Avenue, 2: Command Citadel

            // 1. Core City Architecture (strict 90-degree urban grid!)
            if (theme == 0)
            {
                // Industrial Freight Rail & Power Yard
                SpawnMapProp("city_factory", 0.37f, 0.87f, 0f);
                SpawnMapProp("city_factory", 0.63f, 0.87f, 0f);
                SpawnMapProp("rail_boxcar", 0.28f, 0.86f, 0f);
                SpawnMapProp("rail_boxcar", 0.28f, 0.82f, 0f);
                SpawnMapProp("rail_boxcar", 0.72f, 0.86f, 0f);
                SpawnMapProp("rail_boxcar", 0.72f, 0.82f, 0f);
                SpawnMapProp("steel_silo", 0.50f, 0.84f, 0f);
                SpawnMapProp("water_tower", 0.50f, 0.14f, 0f);

                SpawnMapProp("city_blockhouse", 0.37f, 0.63f, 0f);
                SpawnMapProp("city_blockhouse", 0.63f, 0.37f, 180f);
                SpawnMapProp("transformer_substation", 0.63f, 0.63f, 0f);
                SpawnMapProp("generator_unit", 0.68f, 0.63f, 0f);
                SpawnMapProp("transformer_substation", 0.37f, 0.37f, 180f);
                SpawnMapProp("generator_unit", 0.32f, 0.37f, 180f);

                SpawnMapProp("cargo_container", 0.37f, 0.15f, 0f);
                SpawnMapProp("cargo_container", 0.37f, 0.12f, 0f);
                SpawnMapProp("forklift_wreck", 0.43f, 0.14f, 0f);
                SpawnMapProp("cargo_container", 0.63f, 0.15f, 0f);
                SpawnMapProp("oil_drum_stack", 0.58f, 0.14f, 0f);

                SpawnMapProp("checkpoint_gate", 0.44f, 0.50f, 90f);
                SpawnMapProp("guard_booth", 0.44f, 0.56f, 90f);
                SpawnMapProp("checkpoint_gate", 0.56f, 0.50f, 90f);
                SpawnMapProp("guard_booth", 0.56f, 0.44f, 90f);
                SpawnMapProp("concrete_wall", 0.50f, 0.60f, 0f);
                SpawnMapProp("concrete_wall", 0.50f, 0.40f, 0f);
            }
            else if (theme == 1)
            {
                // Downtown Commercial Avenue & City Park
                SpawnMapProp("city_apartment", 0.37f, 0.86f, 0f);
                SpawnMapProp("city_apartment", 0.63f, 0.86f, 0f);
                SpawnMapProp("city_apartment", 0.37f, 0.63f, 0f);
                SpawnMapProp("city_apartment", 0.63f, 0.37f, 180f);
                SpawnMapProp("city_blockhouse", 0.37f, 0.37f, 0f);
                SpawnMapProp("city_blockhouse", 0.63f, 0.63f, 180f);
                SpawnMapProp("city_apartment", 0.37f, 0.14f, 0f);
                SpawnMapProp("city_apartment", 0.63f, 0.14f, 0f);

                SpawnMapProp("fuel_pump_island", 0.50f, 0.84f, 0f);
                SpawnMapProp("abandoned_car", 0.50f, 0.78f, 0f);
                SpawnMapProp("bus_stop", 0.27f, 0.76f, 0f);
                SpawnMapProp("street_kiosk", 0.24f, 0.66f, 0f);
                SpawnMapProp("bus_stop", 0.73f, 0.24f, 180f);
                SpawnMapProp("street_kiosk", 0.76f, 0.34f, 180f);
                SpawnMapProp("brick_wall", 0.44f, 0.50f, 90f);
                SpawnMapProp("brick_wall", 0.56f, 0.50f, 90f);
                SpawnMapProp("abandoned_car", 0.46f, 0.42f, 0f);
                SpawnMapProp("abandoned_car", 0.54f, 0.58f, 180f);
                SpawnMapProp("wrecked_tank", 0.50f, 0.50f, 14f);
                SpawnMapProp("crater_boulders", 0.53f, 0.47f, 120f);

                // Small organic botanical park on Southern Plaza
                SpawnMapProp("stone_well", 0.50f, 0.16f, 0f);
                SpawnSurvivalNaturalCluster(0.50f, 0.16f, 0.08f, 0.05f, 4,
                    new[] { "cherry_tree", "weeping_willow", "bush_cluster", "moss_rocks" }, rng);
            }
            else
            {
                // Urban Command Citadel & Air Defense Square
                SpawnMapProp("city_blockhouse", 0.37f, 0.86f, 0f);
                SpawnMapProp("city_blockhouse", 0.63f, 0.86f, 0f);
                SpawnMapProp("radar_dome", 0.50f, 0.83f, 0f);
                SpawnMapProp("city_apartment", 0.37f, 0.63f, 0f);
                SpawnMapProp("city_apartment", 0.63f, 0.37f, 180f);
                SpawnMapProp("sam_launcher", 0.63f, 0.63f, 180f);
                SpawnMapProp("sam_launcher", 0.37f, 0.37f, 0f);
                SpawnMapProp("military_tent", 0.26f, 0.62f, 0f);
                SpawnMapProp("ammo_depot", 0.26f, 0.56f, 0f);
                SpawnMapProp("military_tent", 0.74f, 0.38f, 180f);
                SpawnMapProp("ammo_depot", 0.74f, 0.44f, 180f);
                SpawnMapProp("radio_mast", 0.37f, 0.16f, 0f);
                SpawnMapProp("generator_unit", 0.63f, 0.16f, 0f);
                SpawnMapProp("concrete_wall", 0.44f, 0.50f, 90f);
                SpawnMapProp("concrete_wall", 0.56f, 0.50f, 90f);
                SpawnMapProp("hesco_barrier", 0.50f, 0.60f, 0f);
                SpawnMapProp("hesco_barrier", 0.50f, 0.40f, 0f);
                SpawnMapProp("crashed_heli", 0.50f, 0.50f, -22f);

                // Formal Memorial Park on South-Center
                SpawnMapProp("stone_monolith", 0.50f, 0.15f, 0f);
                SpawnSurvivalNaturalCluster(0.50f, 0.15f, 0.08f, 0.04f, 4,
                    new[] { "cypress_tree", "cypress_tree", "bush_cluster", "broadleaf_grove" }, rng);
            }

            // Outer approach corner townhouses & street traps
            SpawnMapProp("city_blockhouse", 0.17f, 0.65f, 0f);
            SpawnMapProp("city_blockhouse", 0.17f, 0.35f, 0f);
            SpawnMapProp("city_blockhouse", 0.83f, 0.65f, 180f);
            SpawnMapProp("city_blockhouse", 0.83f, 0.35f, 180f);
            SpawnMapProp("tank_trap", 0.24f, 0.55f, 0f);
            SpawnMapProp("tank_trap", 0.76f, 0.45f, 0f);
        }

        private void BuildForestLayout()
        {
            var rng = new System.Random(UnityEngine.Random.Range(1, int.MaxValue));
            int theme = rng.Next(3); // 0: Rural Windmill Valley, 1: Autumn Highland, 2: Wetland Bamboo Front

            // 1. Massive Natural Rock Bastions (organic angles)
            string rockHero = theme == 0 ? "forest_boulder" : (theme == 1 ? "basalt_rocks" : "limestone_karst");
            SpawnMapProp(rockHero, 0.34f, 0.77f, rng.Next(0, 360));
            SpawnMapProp(rockHero, 0.66f, 0.77f, rng.Next(0, 360));
            SpawnMapProp(rockHero, 0.34f, 0.23f, rng.Next(0, 360));
            SpawnMapProp(rockHero, 0.66f, 0.23f, rng.Next(0, 360));

            // 2. Aligned Artificial Woodland Camps / Farmsteads / Outposts
            if (theme == 0)
            {
                // Rural Windmill Valley & Sawmill
                SpawnMapProp("windmill_tower", 0.43f, 0.64f, 0f);
                SpawnMapProp("wooden_shed", 0.38f, 0.64f, 0f);
                SpawnMapProp("hay_bale", 0.38f, 0.59f, 0f);
                SpawnMapProp("stone_well", 0.47f, 0.60f, 0f);
                SpawnMapProp("forest_cabin", 0.57f, 0.36f, 180f);
                SpawnMapProp("timber_stack", 0.62f, 0.35f, 180f);
                SpawnMapProp("timber_stack", 0.62f, 0.38f, 180f);
                SpawnMapProp("forklift_wreck", 0.53f, 0.38f, 180f);
                SpawnMapProp("field_artillery", 0.44f, 0.46f, 0f);
                SpawnMapProp("field_artillery", 0.56f, 0.54f, 180f);
                SpawnMapProp("watch_tower", 0.42f, 0.33f, 0f);
                SpawnMapProp("watch_tower", 0.58f, 0.67f, 0f);
                SpawnMapProp("sandbag_wall", 0.26f, 0.44f, 90f);
                SpawnMapProp("sandbag_wall", 0.74f, 0.56f, 90f);
            }
            else if (theme == 1)
            {
                // Autumn Highland Radar & Ambush
                SpawnMapProp("forest_cabin", 0.43f, 0.64f, 0f);
                SpawnMapProp("radar_station", 0.38f, 0.66f, 0f);
                SpawnMapProp("transformer_substation", 0.47f, 0.62f, 0f);
                SpawnMapProp("forest_cabin", 0.57f, 0.36f, 180f);
                SpawnMapProp("radio_mast", 0.62f, 0.34f, 0f);
                SpawnMapProp("generator_unit", 0.53f, 0.38f, 180f);
                SpawnMapProp("concrete_wall", 0.28f, 0.46f, 90f);
                SpawnMapProp("concrete_wall", 0.72f, 0.54f, 90f);
                SpawnMapProp("wrecked_tank", 0.50f, 0.50f, 22f);
                SpawnMapProp("crater_boulders", 0.47f, 0.47f, 140f);
                SpawnMapProp("crashed_heli", 0.42f, 0.34f, -15f);
                SpawnMapProp("oil_drum_stack", 0.58f, 0.66f, 0f);
            }
            else
            {
                // Wetland Bamboo Camp & River Crossing
                SpawnMapProp("military_tent", 0.42f, 0.64f, 0f);
                SpawnMapProp("military_tent", 0.42f, 0.59f, 0f);
                SpawnMapProp("ammo_depot", 0.47f, 0.61f, 0f);
                SpawnMapProp("forest_cabin", 0.58f, 0.36f, 180f);
                SpawnMapProp("wooden_shed", 0.63f, 0.36f, 180f);
                SpawnMapProp("oil_drum_stack", 0.53f, 0.38f, 0f);
                SpawnMapProp("field_artillery", 0.45f, 0.45f, 0f);
                SpawnMapProp("field_artillery", 0.55f, 0.55f, 180f);
                SpawnMapProp("wrecked_truck", 0.50f, 0.50f, -16f);
                SpawnMapProp("sandbag_wall", 0.26f, 0.45f, 90f);
                SpawnMapProp("sandbag_wall", 0.74f, 0.55f, 90f);
                SpawnMapProp("watch_tower", 0.58f, 0.66f, 0f);
            }

            // 3. Organic Woodland / Autumn / Wetland Natural Clusters (free angles, layered sizing)
            string[] grovePool = theme switch
            {
                0 => new[] { "hollow_oak", "birch_clump", "broadleaf_grove", "fallen_log", "bush_cluster", "tree_stump" },
                1 => new[] { "autumn_tree", "birch_clump", "dead_tree", "stone_monolith", "fallen_log", "basalt_rocks" },
                _ => new[] { "weeping_willow", "mangrove_roots", "bamboo_grove", "reed_marsh", "cherry_tree", "moss_rocks" }
            };
            SpawnSurvivalNaturalCluster(0.22f, 0.78f, 0.08f, 0.09f, 4, grovePool, rng);
            SpawnSurvivalNaturalCluster(0.78f, 0.78f, 0.08f, 0.09f, 4, grovePool, rng);
            SpawnSurvivalNaturalCluster(0.22f, 0.22f, 0.08f, 0.09f, 4, grovePool, rng);
            SpawnSurvivalNaturalCluster(0.78f, 0.22f, 0.08f, 0.09f, 4, grovePool, rng);
            SpawnSurvivalNaturalCluster(0.50f, 0.83f, 0.07f, 0.05f, 3, grovePool, rng);
            SpawnSurvivalNaturalCluster(0.50f, 0.17f, 0.07f, 0.05f, 3, grovePool, rng);
        }
    }
}
