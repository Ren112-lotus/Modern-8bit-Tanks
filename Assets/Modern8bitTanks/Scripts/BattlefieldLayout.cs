using System.Collections.Generic;
using UnityEngine;

namespace Modern8bitTanks
{
    /// <summary>Shared coordinates for terrain, deployment, AI and the tactical radar.</summary>
    internal sealed class BattlefieldLayout
    {
        public const int Width = 1280;
        public const int Height = 832;
        private static readonly Vector2 MapSize = new(Width, Height);
        private static readonly Vector2[] DeploymentOffsets =
        {
            new(-28, 0), new(0, 54), new(25, 25), new(25, -25), new(-5, -53)
        };

        private readonly BattleMap _map;
        private readonly int _seed;
        private readonly Vector2 _westBase;
        private readonly List<CoverPlacement> _cover = new();
        private Color32[] _ground;

        internal BattlefieldLayout(BattleMap map, int seed, DestructibleProp block, DestructibleProp wall)
        {
            _map = map;
            _seed = seed;
            var random = new System.Random(seed);
            _westBase = new Vector2(128, ControlPoint.y + random.Next(-29, 30));

            int outer = random.Next(245, 275);
            int inner = random.Next(463, 497);
            AddWallSections(random, wall, outer);
            AddWallSections(random, wall, inner);

            // Short dividers create corners between openings and near the objective.
            int crosswalls = random.Next(2, 4);
            for (int i = 0; i < crosswalls; i++)
            {
                float y = (i + 1) * Height / (crosswalls + 1f) + random.Next(-53, 54);
                int x = random.Next(2) == 0 ? outer + 36 : inner - 36;
                AddPair(wall, new Vector2(x, y), 0f);
            }
            float side = random.Next(2) == 0 ? -1 : 1;
            AddPair(wall, new Vector2(553 + random.Next(-9, 10), ControlPoint.y + side * random.Next(74, 99)), 90f);
            AddPair(wall, new Vector2(568 + random.Next(-9, 10), ControlPoint.y - side * random.Next(98, 127)), 0f);

            AddLooseCover(random, block);
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
            internal readonly Vector2 HalfSize;

            internal CoverPlacement(DestructibleProp prefab, Vector2 position, float angle)
            {
                Prefab = prefab;
                Position = position;
                Angle = angle;
                HalfSize = angle % 180f == 0f
                    ? new Vector2(prefab.meta.width, prefab.meta.length) * 0.5f
                    : new Vector2(prefab.meta.length, prefab.meta.width) * 0.5f;
            }

            internal bool Contains(Vector2 point, float clearance)
            {
                Vector2 delta = point - Position;
                return Mathf.Abs(delta.x) < HalfSize.x + clearance && Mathf.Abs(delta.y) < HalfSize.y + clearance;
            }
        }

        private void AddPair(DestructibleProp prefab, Vector2 position, float angle)
        {
            _cover.Add(new CoverPlacement(prefab, position, angle));
            _cover.Add(new CoverPlacement(prefab, Mirror(position), angle + 180f));
        }

        private void AddWallSections(System.Random random, DestructibleProp wall, int x)
        {
            const int slots = 11;
            const int firstY = 56;
            const int stride = 72;

            // Leave two to four openings, with independent positions in successive walls.
            int gates = random.Next(2, 5);
            int gateMask = 0;
            var candidates = new List<int>();
            for (int i = 0; i < gates; i++)
            {
                candidates.Clear();
                for (int slot = 1; slot < slots - 1; slot++)
                    if ((gateMask & (7 << (slot - 1))) == 0) candidates.Add(slot);
                if (candidates.Count == 0) break;
                gateMask |= 1 << candidates[random.Next(candidates.Count)];
            }

            int column = x;
            for (int i = 0; i < slots; i++)
            {
                if ((gateMask & (1 << i)) != 0)
                {
                    column = x + random.Next(-16, 17);
                    continue;
                }

                // Pull the wall ends back from each opening to leave turning room.
                int y = firstY + stride * i;
                if (i > 0 && (gateMask & (1 << (i - 1))) != 0) y += 10;
                if ((gateMask & (1 << (i + 1))) != 0) y -= 10;
                AddPair(wall, new Vector2(column, y), 90f);
            }
        }

        private void AddLooseCover(System.Random random, DestructibleProp block)
        {
            const float spawnClearance = 112f;
            const float objectiveClearance = 93f;
            int pairs = 0;
            for (int attempt = 0; attempt < 220 && pairs < 8; attempt++)
            {
                var position = new Vector2(random.Next(94, 584), random.Next(71, Height - 71));
                if ((position - _westBase).sqrMagnitude < spawnClearance * spawnClearance ||
                    (position - ControlPoint).sqrMagnitude < objectiveClearance * objectiveClearance)
                    continue;

                // Keep the openings and spaces alongside walls wide enough to turn through.
                bool clear = true;
                foreach (var cover in _cover)
                {
                    if (!cover.Contains(position, 72f)) continue;
                    clear = false;
                    break;
                }
                if (!clear) continue;

                AddPair(block, position, random.Next(2) * 90f);
                pairs++;
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
            var (earth, dark, verge, concrete) = _map switch
            {
                BattleMap.City => ((Color32)new Color(0.54f, 0.49f, 0.43f), (Color32)new Color(0.31f, 0.31f, 0.29f),
                    (Color32)new Color(0.69f, 0.64f, 0.54f), (Color32)new Color(0.52f, 0.54f, 0.52f)),
                BattleMap.Forest => ((Color32)new Color(0.36f, 0.47f, 0.27f), (Color32)new Color(0.15f, 0.23f, 0.15f),
                    (Color32)new Color(0.51f, 0.46f, 0.29f), (Color32)new Color(0.48f, 0.49f, 0.40f)),
                _ => ((Color32)new Color(0.69f, 0.51f, 0.31f), (Color32)new Color(0.44f, 0.30f, 0.19f),
                    (Color32)new Color(0.73f, 0.61f, 0.40f), (Color32)new Color(0.59f, 0.55f, 0.46f))
            };

            var pixels = new Color32[Width * Height];
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width / 2; x++)
            {
                int i = y * Width + x;
                int frame = Mathf.Min(Mathf.Min(x, Width - 1 - x), Mathf.Min(y, Height - 1 - y));
                float patch = Mathf.PerlinNoise(x * 0.011f + (_seed & 255) * 0.17f, y * 0.014f);
                float grain = Grain(x, y);
                Color32 color = Color32.Lerp(dark, earth, Mathf.Clamp01(patch * 0.88f + grain * 0.18f + 0.08f));

                color = PaintTerrain(x, y, color, dark, verge, patch, grain);

                Vector2 point = new(x + 0.5f, y + 0.5f);
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
                if (frame < BattleSceneController.BorderPadPx)
                    color = Color32.Lerp(dark, color, frame < 9 ? 0.10f : 0.20f + grain * 0.18f);
                pixels[i] = color;
                pixels[pixels.Length - 1 - i] = color;
            }
            _ground = pixels;
            return _ground;
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
