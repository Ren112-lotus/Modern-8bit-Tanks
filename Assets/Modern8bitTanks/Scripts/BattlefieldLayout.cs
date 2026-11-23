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
                Vector2 right = Direction(angle);
                center.y = ControlPoint.y + (center.y - ControlPoint.y) * reflection;
                bool isConnector = district.Cover.Length == 0;
                Vector2 groundSize = isConnector ? district.GroundSize * PlanScale : district.GroundSize;
                _districtGround.Add(new DistrictGround(center, groundSize, Direction(angle * reflection), isConnector));

                foreach (var piece in district.Cover)
                {
                    Vector2 offset = right * piece.Offset.x + Vector2.Perpendicular(right) * piece.Offset.y;
                    offset.y *= reflection;
                    Vector2 position = Vector2Int.RoundToInt(center + offset);
                    float rotation = (angle + piece.Angle) * reflection;
                    var prefab = theme.GetCover(piece.Kind);
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

            internal float Sample(Vector2 point, out Vector2 local)
            {
                Vector2 delta = point - Center;
                local = new Vector2(Vector2.Dot(delta, Right), Vector2.Dot(delta, Vector2.Perpendicular(Right)));
                return PadDistance(local, HalfSize.x, HalfSize.y);
            }
        }

        internal bool IsWallAt(Vector2 position)
        {
            foreach (var cover in _cover)
                if (cover.Prefab.meta.fixedCover && cover.Contains(position, 0f))
                    return true;
            return false;
        }

        public Color32[] GroundPixels()
        {
            if (_ground != null) return _ground;
            var (earth, dark, verge, concrete) = TerrainPalette();
            var pixels = new Color32[Width * Height];
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width / 2; x++)
            {
                int i = y * Width + x;
                Color32 color = SampleTerrain(x, y, earth, dark, verge, out float patch, out float grain);

                Vector2 point = new(x + 0.5f, y + 0.5f);
                color = PaintDistrict(point, color, concrete, dark, verge, patch, grain);
                float west = PadDistance(point - _westBase, 92f, 90f);
                float east = PadDistance(point - Mirror(_westBase), 92f, 90f);
                float plaza = (point - ControlPoint).sqrMagnitude;
                if (west < 1f || east < 1f || plaza < 70f * 70f)
                {
                    bool joint = (x + 13) % 27 == 0 || (y + 7) % 27 == 0;
                    bool rim = (west > 0.92f && west < 1f) || (east > 0.92f && east < 1f)
                        || (plaza >= 67f * 67f && plaza < 70f * 70f);
                    color = Color32.Lerp(concrete, dark, 0.12f * grain + 0.17f * patch + (joint ? 0.19f : 0f));
                    if (rim) color = Color32.Lerp(color, verge, 0.66f);
                }
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
            _ground = pixels;
            return _ground;
        }

        internal Color32[] ExteriorPixels(int padding, int scale)
        {
            int width = (Width + padding * 2) / scale;
            int height = (Height + padding * 2) / scale;
            var pixels = new Color32[width * height];
            var (earth, dark, verge, _) = TerrainPalette();

            for (int y = 0; y < height; y++)
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
                BattleMap.City => ((Color32)new Color(0.54f, 0.49f, 0.43f), (Color32)new Color(0.31f, 0.31f, 0.29f),
                    (Color32)new Color(0.69f, 0.64f, 0.54f), (Color32)new Color(0.52f, 0.54f, 0.52f)),
                BattleMap.Forest => ((Color32)new Color(0.36f, 0.47f, 0.27f), (Color32)new Color(0.15f, 0.23f, 0.15f),
                    (Color32)new Color(0.51f, 0.46f, 0.29f), (Color32)new Color(0.48f, 0.49f, 0.40f)),
                _ => ((Color32)new Color(0.69f, 0.51f, 0.31f), (Color32)new Color(0.44f, 0.30f, 0.19f),
                    (Color32)new Color(0.73f, 0.61f, 0.40f), (Color32)new Color(0.59f, 0.55f, 0.46f))
            };

        private Color32 PaintDistrict(Vector2 point, Color32 terrain, Color32 concrete, Color32 dark,
            Color32 border, float patch, float grain)
        {
            foreach (var district in _districtGround)
            {
                if ((point - district.Center).sqrMagnitude > district.RadiusSquared) continue;
                float edge = district.Sample(point, out Vector2 local);
                if (edge >= 1f) continue;

                bool seam = Mathf.Repeat(local.x + 12f, 34f) < 1.15f ||
                    Mathf.Repeat(local.y + 8f, 30f) < 1.15f;
                float weathering = grain * 0.06f + patch * 0.19f;
                if (_map == BattleMap.City && seam) weathering += 0.14f;
                Color32 floor = Color32.Lerp(concrete, dark, weathering);
                if (_map == BattleMap.City && !district.IsConnector &&
                    Mathf.Abs(local.x) < district.HalfSize.x * 0.72f &&
                    Mathf.Abs(Mathf.Abs(local.y) - district.HalfSize.y * 0.76f) < 1.4f &&
                    Mathf.Repeat(local.x, 22f) < 13f)
                    floor = Color32.Lerp(floor, border, 0.53f);

                float strength = _map == BattleMap.City ? 0.83f : 0.68f;
                if (district.IsConnector) strength *= 0.74f;
                return Color32.Lerp(terrain, floor, Mathf.Clamp01((1f - edge) * 9f) * strength);
            }

            return terrain;
        }

        private Color32 PaintTerrain(int x, int y, Color32 surface, Color32 low, Color32 high, float patch, float grain)
        {
            float cluster = Grain(x / 4, y / 4);
            switch (_map)
            {
                case BattleMap.City:
                    bool paved = Grain(x / 157, y / 137) > 0.36f;
                    bool seam = y % 31 == 0 || (x + (y / 31 & 1) * 21) % 44 == 0;
                    if (paved) surface = Color32.Lerp(surface, high, seam ? 0.02f : 0.13f);
                    if (paved && seam && grain > 0.18f) return Color32.Lerp(surface, low, 0.27f);
                    break;
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
