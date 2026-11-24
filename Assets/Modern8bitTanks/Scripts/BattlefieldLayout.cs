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
        private static readonly Vector2 MapSize = new(Width, Height);
        private static readonly Vector2[] DeploymentOffsets =
        {
            new(-28, 0), new(0, 54), new(25, 25), new(25, -25), new(-5, -53)
        };

        private readonly BattleMap _map;
        private readonly int _seed;
        private readonly Vector2 _westBase;
        private readonly List<CoverPlacement> _cover = new();
        private readonly List<DistrictGround> _districtGround = new();
        private readonly List<DistrictGround> _ridgeGround = new();
        private Color32[] _ground;

        internal BattlefieldLayout(BattleMap map, int seed, BattlefieldPlan plan, BattlefieldTheme theme)
        {
            _map = map;
            _seed = seed;
            var random = new System.Random(seed);
            _westBase = new Vector2(128, ControlPoint.y + random.Next(-17, 18));
            float reflection = random.Next(2) == 0 ? 1f : -1f;

            foreach (var district in plan.Districts)
            {
                Vector2 center = district.Center * PlanScale + new Vector2(random.Next(-10, 11), random.Next(-12, 13));
                float angle = district.Angle + random.Next(-6, 7);
                if (map == BattleMap.City) angle = Mathf.Round(district.Angle / 90f) * 90f;
                Vector2 right = Direction(angle);
                center.y = ControlPoint.y + (center.y - ControlPoint.y) * reflection;
                bool isConnector = district.Cover.Length == 0;
                Vector2 groundSize = isConnector ? district.GroundSize * PlanScale : district.GroundSize;
                if (groundSize.x > 0f && groundSize.y > 0f)
                    _districtGround.Add(new DistrictGround(center, groundSize, Direction(angle * reflection), isConnector));

                foreach (var piece in district.Cover)
                {
                    Vector2 offset = right * piece.Offset.x + Vector2.Perpendicular(right) * piece.Offset.y;
                    offset.y *= reflection;
                    Vector2 position = Vector2Int.RoundToInt(center + offset);
                    float rotation = (angle + piece.Angle) * reflection;
                    if (map == BattleMap.City && piece.Kind != BattlefieldCoverKind.Barricade)
                        rotation = Mathf.Round(rotation / 90f) * 90f;
                    var prefab = theme.GetCover(piece.Kind);
                    if (piece.Kind is BattlefieldCoverKind.Ridge or BattlefieldCoverKind.RidgeVariant)
                    {
                        if (prefab == null) continue;
                        var size = new Vector2(prefab.meta.width + RidgeFootMargin * 2f,
                            prefab.meta.length + RidgeFootMargin * 2f);
                        _ridgeGround.Add(new DistrictGround(position, size, Direction(rotation), false));
                    }
                    _cover.Add(new CoverPlacement(prefab, position, rotation));
                    _cover.Add(new CoverPlacement(prefab, Mirror(position), rotation + 180f));
                }
            }
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

        internal bool IsWallAt(Vector2 position)
        {
            foreach (var cover in _cover)
                if (cover.Prefab.meta.fixedCover && cover.Contains(position, 0f))
                    return true;
            return false;
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
                    color = _map == BattleMap.City
                        ? PaintCityDistricts(point, color, concrete, dark, verge, patch, grain)
                        : PaintRidgeFeet(point, PaintNaturalDistricts(point, color, earth, verge, patch),
                            earth, dark, verge, patch, grain);
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
            Color32 color = Color32.Lerp(dark, earth, Mathf.Clamp01(patch * 0.88f + grain * 0.18f + 0.08f));
            return PaintTerrain(x, y, color, dark, verge, patch, grain);
        }

        private (Color32 Earth, Color32 Dark, Color32 Verge, Color32 Concrete) TerrainPalette() =>
            _map switch
            {
                BattleMap.City => ((Color32)new Color(0.44f, 0.43f, 0.39f), (Color32)new Color(0.31f, 0.33f, 0.33f),
                    (Color32)new Color(0.68f, 0.64f, 0.54f), (Color32)new Color(0.51f, 0.54f, 0.53f)),
                BattleMap.Forest => ((Color32)new Color(0.36f, 0.47f, 0.27f), (Color32)new Color(0.15f, 0.23f, 0.15f),
                    (Color32)new Color(0.51f, 0.46f, 0.29f), (Color32)new Color(0.48f, 0.49f, 0.40f)),
                _ => ((Color32)new Color(0.69f, 0.51f, 0.31f), (Color32)new Color(0.44f, 0.30f, 0.19f),
                    (Color32)new Color(0.73f, 0.61f, 0.40f), (Color32)new Color(0.59f, 0.55f, 0.46f))
            };

        private Color32 PaintDeploymentAndPlaza(Vector2 point, Color32 terrain, Color32 concrete, Color32 dark,
            Color32 trim, float patch, float grain)
        {
            float west = PadDistance(point - _westBase, 92f, 90f);
            float east = PadDistance(point - Mirror(_westBase), 92f, 90f);
            float plaza = Vector2.Distance(point, ControlPoint) / 70f;
            float edge = Mathf.Min(Mathf.Min(west, east), plaza);
            if (edge >= 1f) return terrain;

            bool city = _map == BattleMap.City;
            bool joint = city && (Mathf.Repeat(point.x + 13f, 27f) < 1f || Mathf.Repeat(point.y + 7f, 27f) < 1f);
            Color32 surface = Color32.Lerp(concrete, dark, 0.12f * grain + 0.17f * patch + (joint ? 0.14f : 0f));
            if (city && edge is > 0.91f and < 0.96f) surface = Color32.Lerp(surface, trim, 0.45f);
            float blend = Mathf.SmoothStep(0f, 1f, (1f - edge) * (city ? 21f : 4f)) * (city ? 0.9f : 0.54f);
            return Color32.Lerp(terrain, surface, blend);
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
                coverage = Mathf.Max(coverage, weight * (district.IsConnector ? 0.19f : 0.34f));
            }

            Color32 soil = Color32.Lerp(earth, verge, 0.14f + patch * 0.17f);
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
                // Break up the edge of the scree with differently sized pockets of exposed ground.
                float pockets = Mathf.PerlinNoise(point.x * 0.043f + 14f, point.y * 0.052f);
                float falloff = 1f - normalized.magnitude + (pockets - 0.5f) * 0.26f;
                float weight = Mathf.SmoothStep(0f, 1f, falloff * 2.8f);
                deposit = Mathf.Max(deposit, weight * (0.24f + pockets * 0.76f));
            }
            if (deposit <= 0f) return terrain;

            float chips = Grain((int)point.x / 3, (int)point.y / 3);
            Color32 soil = Color32.Lerp(earth, verge, 0.14f + patch * 0.26f);
            Color32 result = Color32.Lerp(terrain, soil, deposit * 0.64f);
            if (chips > 0.72f && grain > 0.32f)
                return Color32.Lerp(result, verge, deposit * (chips - 0.72f) * 1.5f);
            if (chips < 0.11f)
                return Color32.Lerp(result, dark, deposit * (0.11f - chips) * 3.2f);
            return result;
        }

        private Color32 PaintCityDistricts(Vector2 point, Color32 terrain, Color32 concrete, Color32 dark,
            Color32 trim, float patch, float grain)
        {
            float street = 0f;
            float yard = 0f;
            float curb = 0f;
            float streetMark = 0f;
            foreach (var district in _districtGround)
            {
                if ((point - district.Center).sqrMagnitude > district.RadiusSquared) continue;
                Vector2 local = district.ToLocal(point);
                float inset = Mathf.Min(district.HalfSize.x - Mathf.Abs(local.x), district.HalfSize.y - Mathf.Abs(local.y));
                if (inset <= 0f) continue;

                float weight = Mathf.SmoothStep(0f, 1f, inset / 6f);
                if (district.IsConnector)
                {
                    street = Mathf.Max(street, weight);
                    if (Mathf.Abs(local.y) < 1.2f && Mathf.Repeat(local.x + 11f, 43f) < 21f)
                        streetMark = Mathf.Max(streetMark, weight);
                }
                else
                {
                    yard = Mathf.Max(yard, weight);
                    float strip = Mathf.Clamp01(1f - Mathf.Abs(inset - 4.5f) / 2.5f);
                    curb = Mathf.Max(curb, strip);
                }
            }

            Color32 asphalt = Color32.Lerp(terrain, dark, street * 0.49f);
            asphalt = Color32.Lerp(asphalt, trim, streetMark * 0.46f * (1f - yard));
            bool joint = Mathf.Repeat(point.x + 12f, 34f) < 1.2f || Mathf.Repeat(point.y + 8f, 30f) < 1.2f;
            Color32 paving = Color32.Lerp(concrete, dark, 0.06f * grain + 0.17f * patch + (joint ? 0.13f : 0f));
            Color32 color = Color32.Lerp(asphalt, paving, yard * 0.87f);
            return Color32.Lerp(color, trim, curb * (1f - street) * 0.60f);
        }

        private Color32 PaintTerrain(int x, int y, Color32 surface, Color32 low, Color32 high, float patch, float grain)
        {
            float cluster = Grain(x / 4, y / 4);
            switch (_map)
            {
                case BattleMap.City:
                    return Color32.Lerp(surface, high, 0.027f * patch + 0.026f * cluster);
                case BattleMap.Forest:
                    if (cluster > 0.71f && grain > 0.37f)
                        return Color32.Lerp(surface, high, (cluster - 0.70f) * 0.93f * patch);
                    if (cluster < 0.14f)
                        return Color32.Lerp(surface, low, (0.18f - cluster) * 1.3f);
                    break;
                default:
                    float ripple = Mathf.Sin(x * 0.039f + y * 0.30f + patch * 12f);
                    if (ripple > 0.65f && patch > 0.43f)
                        return Color32.Lerp(surface, high, (ripple - 0.65f) * patch * 0.64f);
                    break;
            }
            return grain > 0.96f && cluster < 0.18f ? Color32.Lerp(surface, low, 0.42f) : surface;
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
