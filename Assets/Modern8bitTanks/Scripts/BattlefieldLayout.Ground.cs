using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Modern8bitTanks
{
    internal sealed partial class BattlefieldLayout
    {
        private Color32[] _ground;

        private static float SmoothMin(float a, float b, float k)
        {
            float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;
            return Mathf.Min(a, b) - h * h * k * 0.25f;
        }

        private static void FoldPolylineDistance(float sd, ref float min1, ref float min2)
        {
            if (sd < min1) { min2 = min1; min1 = sd; }
            else if (sd < min2) { min2 = sd; }
        }

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
            float min1 = 1e6f, min2 = 1e6f;
            int curPid = -1;
            float curPolySd = 1e6f;
            float bestDash = 0f;
            float bestCrosswalk = 0f;
            float bestStopBar = 0f;

            // Find closest true intersection by Chebyshev margin outside its junction box (so crosswalks sit cleanly outside wider avenues!)
            float bestInterMargin = 999f;
            float nearInterBox = 999f;
            float nearInterRadius = 28f;
            for (int k = 0; k < _intersections.Count; k++)
            {
                var inter = _intersections[k];
                Vector2 d = point - inter.Position;
                float box = Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y));
                float margin = box - inter.Radius;
                if (margin < bestInterMargin)
                {
                    bestInterMargin = margin;
                    nearInterBox = box;
                    nearInterRadius = inter.Radius;
                }
            }

            const float sidewalkOuter = 14f;
            const float cullMargin = sidewalkOuter + 14f;

            for (int i = 0; i < _roads.Count; i++)
            {
                var seg = _roads[i];
                if (seg.PolylineId != curPid)
                {
                    if (curPid >= 0 && curPolySd < 1e5f)
                        FoldPolylineDistance(curPolySd, ref min1, ref min2);
                    curPid = seg.PolylineId;
                    curPolySd = 1e6f;
                }

                float bound = seg.HalfWidth + cullMargin;
                if (point.x < seg.MinX - bound || point.x > seg.MaxX + bound ||
                    point.y < seg.MinY - bound || point.y > seg.MaxY + bound) continue;

                float dist = seg.DistanceTo(point, out float segAlong, out float signedPerp, out bool insideSpan);
                float sd = dist - seg.HalfWidth;
                if (sd < curPolySd) curPolySd = sd;

                if (insideSpan && sd <= 0f)
                {
                    float w = Mathf.SmoothStep(0f, 1f, -sd * 0.55f);
                    float hw = seg.HalfWidth;
                    float totalAlong = seg.AlongStart + segAlong;

                    // Continuous yellow/ivory dashed center line stopped cleanly before crosswalks & central plaza
                    if (nearInterBox > nearInterRadius + 20f && Vector2.Distance(point, ControlPoint) > 78f)
                    {
                        if (Mathf.Abs(signedPerp) < (seg.IsPrimary ? 1.45f : 1.15f) && Mathf.Repeat(totalAlong + 8f, 34f) < 17f)
                            bestDash = Mathf.Max(bestDash, w);
                    }

                    // Razor-straight white zebra crosswalk & stop bar right outside every true multi-street junction!
                    if (nearInterRadius < 50f)
                    {
                        if (nearInterBox >= nearInterRadius + 5f && nearInterBox <= nearInterRadius + 14f && Mathf.Abs(signedPerp) < hw - 3.5f)
                        {
                            if (Mathf.Repeat(Mathf.Abs(signedPerp) + 2f, 7f) < 3.5f)
                                bestCrosswalk = Mathf.Max(bestCrosswalk, w);
                        }
                        else if (nearInterBox >= nearInterRadius + 16f && nearInterBox <= nearInterRadius + 18.2f && Mathf.Abs(signedPerp) < hw - 2.5f)
                        {
                            bestStopBar = Mathf.Max(bestStopBar, w);
                        }
                    }
                }
            }
            if (curPid >= 0 && curPolySd < 1e5f)
                FoldPolylineDistance(curPolySd, ref min1, ref min2);

            if (min1 > sidewalkOuter + 6f) return terrain;

            // Smooth-Min across the 2 closest distinct streets gives realistic curved turning-radius curbs without bowing opposite curbs!
            float unifiedSd = min2 < 1e5f ? SmoothMin(min1, min2, 10.5f) : min1;
            if (unifiedSd >= sidewalkOuter) return terrain;

            // Gently fade center dashes inside any multi-street junction fillet
            if (min2 < 14f)
                bestDash *= Mathf.SmoothStep(5f, 14f, min2);

            Color32 color = terrain;
            if (unifiedSd < sidewalkOuter)
            {
                float sw = Mathf.SmoothStep(0f, 1f, (sidewalkOuter - unifiedSd) * 0.35f);
                bool tileJoint = Mathf.Repeat(point.x, 16f) < 1.1f || Mathf.Repeat(point.y, 16f) < 1.1f;
                var sidewalkCol = new Color32(158, 154, 145, 255);
                var curbCol = new Color32(106, 104, 98, 255);
                Color32 swCol = tileJoint ? Color32.Lerp(sidewalkCol, curbCol, 0.32f) : sidewalkCol;
                swCol = Color32.Lerp(swCol, dark, 0.08f * grain + 0.08f * patch);
                color = Color32.Lerp(color, swCol, sw * 0.90f);
                if (unifiedSd > 0f && unifiedSd <= 3.2f)
                {
                    float curb = 1f - unifiedSd / 3.2f;
                    color = Color32.Lerp(color, curbCol, curb * 0.80f);
                }
            }

            if (unifiedSd <= 0.5f)
            {
                float bestAsphalt = Mathf.SmoothStep(0f, 1f, (0.5f - unifiedSd) * 0.55f);
                var asphaltBase = new Color32(68, 72, 78, 255);
                var asphaltDark = new Color32(54, 58, 64, 255);
                Color32 road = Color32.Lerp(asphaltBase, asphaltDark, 0.25f * grain + 0.22f * patch);
                color = Color32.Lerp(color, road, bestAsphalt * 0.96f);
                if (bestDash > 0f)
                    color = Color32.Lerp(color, new Color32(214, 196, 126, 255), bestDash * 0.85f);
                if (bestCrosswalk > 0f)
                    color = Color32.Lerp(color, new Color32(206, 204, 194, 255), bestCrosswalk * 0.72f);
                if (bestStopBar > 0f)
                    color = Color32.Lerp(color, new Color32(206, 204, 194, 255), bestStopBar * 0.55f);
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
            float min1 = 1e6f, min2 = 1e6f;
            int curPid = -1;
            float curPolySd = 1e6f;
            float bestBridgeRoad = 0f;
            float bestRut = 0f;
            float bestPlank = 0f;
            float bestBridgeRail = 0f;

            const float shoulderOuter = 10f;
            const float cullMargin = shoulderOuter + 18f;
            float edgeWobble = inWater ? 0f : (patch - 0.5f) * 4.5f;

            for (int i = 0; i < _roads.Count; i++)
            {
                var seg = _roads[i];
                if (seg.PolylineId != curPid)
                {
                    if (curPid >= 0 && curPolySd < 1e5f)
                        FoldPolylineDistance(curPolySd, ref min1, ref min2);
                    curPid = seg.PolylineId;
                    curPolySd = 1e6f;
                }

                float bound = seg.HalfWidth + cullMargin;
                if (point.x < seg.MinX - bound || point.x > seg.MaxX + bound ||
                    point.y < seg.MinY - bound || point.y > seg.MaxY + bound) continue;

                float dist = seg.DistanceTo(point, out float segAlong, out float signedPerp, out bool insideSpan);
                float effectiveW = inWater ? seg.HalfWidth + 2.5f : seg.HalfWidth;
                float sd = dist + edgeWobble - effectiveW;
                if (sd < curPolySd) curPolySd = sd;

                if (inWater && sd < 0f)
                {
                    float w = Mathf.SmoothStep(0f, 1f, -sd * 0.45f);
                    bestBridgeRoad = Mathf.Max(bestBridgeRoad, w);
                    if (Mathf.Abs(signedPerp) > effectiveW - 3.2f)
                        bestBridgeRail = Mathf.Max(bestBridgeRail, w);
                    else if (Mathf.Repeat(seg.AlongStart + segAlong, 6f) < 4.5f)
                        bestPlank = Mathf.Max(bestPlank, w);
                }
                else if (!inWater && insideSpan && sd < 0f)
                {
                    float w = Mathf.SmoothStep(0f, 1f, -sd * 0.42f);
                    float rutTarget = seg.HalfWidth * 0.42f;
                    float rutDist = Mathf.Abs(Mathf.Abs(signedPerp) - rutTarget);
                    if (rutDist < 3.0f)
                        bestRut = Mathf.Max(bestRut, (1f - rutDist / 3.0f) * w);
                }
            }
            if (curPid >= 0 && curPolySd < 1e5f)
                FoldPolylineDistance(curPolySd, ref min1, ref min2);

            if (inWater)
            {
                if (bestBridgeRoad <= 0f) return terrain;
                var rail = new Color32(66, 48, 32, 255);
                var timber = new Color32(146, 108, 68, 255);
                var timberGap = new Color32(82, 60, 38, 255);
                Color32 bridge = bestBridgeRail > 0.35f ? rail
                    : (bestPlank > 0f ? Color32.Lerp(timber, timberGap, grain * 0.22f) : timberGap);
                return Color32.Lerp(terrain, bridge, bestBridgeRoad * 0.94f);
            }

            if (min1 > shoulderOuter + 12f) return terrain;

            // Smooth-Min flares Y-forks & T-junctions into natural bell-mouthed compacted dirt clearings!
            float unifiedSd = min2 < 1e5f ? SmoothMin(min1, min2, 18f) : min1;
            if (unifiedSd >= shoulderOuter) return terrain;

            // Fade dual tire ruts cleanly inside forks so ruts never X-cross messily!
            if (min2 < 14f)
                bestRut *= Mathf.SmoothStep(4f, 14f, min2);

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
            float bestShoulder = Mathf.SmoothStep(0f, 1f, (shoulderOuter - unifiedSd) / shoulderOuter);
            if (bestShoulder > 0f)
                color = Color32.Lerp(color, shoulderCol, bestShoulder * 0.48f);

            if (unifiedSd < 0f)
            {
                float bestRoad = Mathf.SmoothStep(0f, 1f, -unifiedSd * 0.42f);
                Color32 dirt = Color32.Lerp(roadBase, verge, (grain - 0.5f) * 0.14f);
                color = Color32.Lerp(color, dirt, bestRoad * 0.88f);
                if (bestRut > 0f)
                    color = Color32.Lerp(color, rutCol, bestRut * 0.50f);
            }

            return color;
        }

        // =====================================================================
        // DESERT GROUND: CONNECTED CONVOY HIGHWAY + CANYON TRACKS
        // =====================================================================
        private Color32 PaintDesertRoadNetwork(Vector2 point, Color32 terrain, Color32 earth, Color32 dark,
            Color32 verge, float patch, float grain)
        {
            float min1 = 1e6f, min2 = 1e6f;
            int curPid = -1;
            float curPolySd = 1e6f;
            float priSd = 1e6f;
            float bestRut = 0f;
            float bestDash = 0f;

            const float shoulderOuter = 10f;
            const float cullMargin = shoulderOuter + 20f;

            for (int i = 0; i < _roads.Count; i++)
            {
                var seg = _roads[i];
                if (seg.PolylineId != curPid)
                {
                    if (curPid >= 0 && curPolySd < 1e5f)
                        FoldPolylineDistance(curPolySd, ref min1, ref min2);
                    curPid = seg.PolylineId;
                    curPolySd = 1e6f;
                }

                float bound = seg.HalfWidth + cullMargin;
                if (point.x < seg.MinX - bound || point.x > seg.MaxX + bound ||
                    point.y < seg.MinY - bound || point.y > seg.MaxY + bound) continue;

                float dist = seg.DistanceTo(point, out float segAlong, out float signedPerp, out bool insideSpan);
                float drift = (patch - 0.5f) * (seg.IsPrimary ? 3.5f : 5.0f);
                float sd = dist + drift - seg.HalfWidth;
                if (sd < curPolySd) curPolySd = sd;
                if (seg.IsPrimary && sd < priSd) priSd = sd;

                if (insideSpan && sd < 0f)
                {
                    float w = Mathf.SmoothStep(0f, 1f, -sd * 0.42f);
                    if (seg.IsPrimary && Mathf.Abs(signedPerp) < 1.25f &&
                        Mathf.Repeat(seg.AlongStart + segAlong + 6f, 40f) < 16f &&
                        Vector2.Distance(point, ControlPoint) > 78f)
                        bestDash = Mathf.Max(bestDash, w);

                    float rutTarget = seg.HalfWidth * 0.44f;
                    float rutDist = Mathf.Abs(Mathf.Abs(signedPerp) - rutTarget);
                    if (rutDist < 2.8f)
                        bestRut = Mathf.Max(bestRut, (1f - rutDist / 2.8f) * w);
                }
            }
            if (curPid >= 0 && curPolySd < 1e5f)
                FoldPolylineDistance(curPolySd, ref min1, ref min2);

            if (min1 > shoulderOuter + 12f) return terrain;

            float unifiedSd = min2 < 1e5f ? SmoothMin(min1, min2, 18f) : min1;
            if (unifiedSd >= shoulderOuter) return terrain;

            // Fade highway dashes & tire ruts cleanly inside junction merges!
            if (min2 < 16f)
            {
                float fade = Mathf.SmoothStep(4f, 16f, min2);
                bestDash *= fade;
                bestRut *= fade;
            }

            Color32 color = terrain;
            float bestShoulder = Mathf.SmoothStep(0f, 1f, (shoulderOuter - unifiedSd) / shoulderOuter);
            if (bestShoulder > 0f)
                color = Color32.Lerp(color, new Color32(158, 128, 86, 255), bestShoulder * 0.46f);

            if (unifiedSd < 0f)
            {
                float bestRoad = Mathf.SmoothStep(0f, 1f, -unifiedSd * 0.42f);
                // Smooth windblown gravel apron transition where secondary desert tracks meet primary tarmac!
                float priBlend = Mathf.SmoothStep(26f, -4f, priSd);
                Color32 trackCol = Color32.Lerp(new Color32(134, 104, 68, 255), dark, 0.16f * grain);
                var tarmac = new Color32(114, 104, 88, 255);
                var sandDust = new Color32(162, 136, 98, 255);
                Color32 highwayCol = Color32.Lerp(tarmac, sandDust, Mathf.Clamp01(patch * 0.42f + grain * 0.15f));
                Color32 surface = Color32.Lerp(trackCol, highwayCol, priBlend);

                color = Color32.Lerp(color, surface, bestRoad * 0.90f);
                if (bestDash > 0f)
                    color = Color32.Lerp(color, new Color32(220, 200, 140, 255), bestDash * 0.62f);
                if (bestRut > 0f)
                    color = Color32.Lerp(color, dark, bestRut * 0.36f);
            }

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
