using System.Collections.Generic;
using UnityEngine;

namespace Modern8bitTanks
{
    /// <summary>Shared coordinates for terrain, deployment, AI and the tactical radar.</summary>
    internal sealed class BattlefieldLayout
    {
        public const int Width = 1280;
        public const int Height = 832;
        private const int WestBase = 0;
        private const int EastBase = 1;
        private const int Center = 2;
        private const int CoverPairCount = 14;
        private const float CoverGap = 20f;
        private static readonly Vector2 MapSize = new(Width, Height);
        private static readonly Vector2[] DeploymentOffsets =
        {
            new(-28, 0), new(0, 54), new(25, 25), new(25, -25), new(-5, -53)
        };

        private readonly BattleMap _map;
        private readonly int _seed;
        private readonly Vector2[] _nodes;
        private readonly Road[] _roads;
        private readonly float[] _distanceToCenter;
        private Color32[] _ground;

        private readonly struct Road
        {
            public readonly int Start, End;
            public readonly float Length, HalfWidth, StartDistance;
            public readonly Vector2 Direction;

            public Road(int start, int end, Vector2 delta, float halfWidth, float startDistance)
            {
                Start = start;
                End = end;
                Length = delta.magnitude;
                Direction = delta / Length;
                HalfWidth = halfWidth;
                StartDistance = startDistance;
            }
        }

        internal BattlefieldLayout(BattleMap map, int seed)
        {
            _map = map;
            _seed = seed;
            var random = new System.Random(seed);
            var center = MapSize * 0.5f;
            var west = new Vector2(130, center.y + random.Next(-32, 33));
            _nodes = new Vector2[19];
            _nodes[WestBase] = west;
            _nodes[EastBase] = Mirror(west);
            _nodes[Center] = center;

            // Each approach has a west half and a 180-degree rotated east half.
            SetPair(3, 5, new Vector2(302 + random.Next(-34, 35), west.y + random.Next(-56, 57)));
            SetPair(4, 6, new Vector2(478 + random.Next(-36, 37), center.y + random.Next(-56, 57)));
            SetFlank(7, 10, 1);
            SetFlank(13, 16, -1);

            void SetPair(int westIndex, int eastIndex, Vector2 position)
            {
                _nodes[westIndex] = position;
                _nodes[eastIndex] = Mirror(position);
            }

            void SetFlank(int westIndex, int eastIndex, int direction)
            {
                SetPair(westIndex, eastIndex,
                    new Vector2(random.Next(210, 280), center.y + direction * random.Next(169, 225)));
                SetPair(westIndex + 1, eastIndex + 1,
                    new Vector2(random.Next(365, 440), center.y + direction * random.Next(211, 265)));
                SetPair(westIndex + 2, eastIndex + 2,
                    new Vector2(random.Next(510, 568), center.y + direction * random.Next(149, 201)));
            }

            var roads = new List<Road>();
            float middleWidth = map == BattleMap.City ? 42f : 38f;
            float flankWidth = map == BattleMap.Forest ? 35f : 38f;
            AddRoute(roads, middleWidth, WestBase, 3, 4, Center);
            AddRoute(roads, middleWidth, EastBase, 5, 6, Center);
            AddRoute(roads, flankWidth, WestBase, 7, 8, 9, Center);
            AddRoute(roads, flankWidth, EastBase, 10, 11, 12, Center);
            AddRoute(roads, flankWidth, WestBase, 13, 14, 15, Center);
            AddRoute(roads, flankWidth, EastBase, 16, 17, 18, Center);
            _roads = roads.ToArray();

            _distanceToCenter = new float[_nodes.Length];
            System.Array.Fill(_distanceToCenter, float.PositiveInfinity);
            _distanceToCenter[Center] = 0f;
            // Every pass propagates the shortest path over at least one more connection.
            for (int pass = 1; pass < _nodes.Length; pass++)
            {
                foreach (var road in _roads)
                {
                    _distanceToCenter[road.Start] = Mathf.Min(_distanceToCenter[road.Start], _distanceToCenter[road.End] + road.Length);
                    _distanceToCenter[road.End] = Mathf.Min(_distanceToCenter[road.End], _distanceToCenter[road.Start] + road.Length);
                }
            }
        }

        public Vector2 ControlPoint => _nodes[Center];

        private static Vector2 Mirror(Vector2 point) => MapSize - point;

        public Vector2 Spawn(bool west, int index)
        {
            Vector2 position = _nodes[WestBase] + DeploymentOffsets[index];
            return west ? position : Mirror(position);
        }

        private void AddRoute(List<Road> roads, float halfWidth, params int[] points)
        {
            float distance = 0f;
            for (int i = 1; i < points.Length; i++)
            {
                int a = points[i - 1], b = points[i];
                var road = new Road(a, b, _nodes[b] - _nodes[a], halfWidth, distance);
                roads.Add(road);
                distance += road.Length;
            }
        }

        private float RoadOffset(Road road, Vector2 point, out float along)
        {
            Vector2 relative = point - _nodes[road.Start];
            along = Mathf.Clamp(Vector2.Dot(relative, road.Direction), 0, road.Length);
            return (relative - road.Direction * along).magnitude;
        }

        public bool IsRoad(Vector2 point, float margin = 0f)
        {
            foreach (var road in _roads)
                if (RoadOffset(road, point, out _) < road.HalfWidth + margin) return true;
            return false;
        }

        internal readonly struct CoverPlacement
        {
            internal readonly DestructibleProp Prefab;
            internal readonly Vector2 Position;
            internal readonly float Angle;
            internal readonly float Radius;

            internal CoverPlacement(DestructibleProp prefab, Vector2 position, float angle, float radius)
            {
                Prefab = prefab;
                Position = position;
                Angle = angle;
                Radius = radius;
            }
        }

        internal List<CoverPlacement> PlaceCover(IReadOnlyList<DestructibleProp> prefabs)
        {
            var random = new System.Random(unchecked(_seed ^ 0x54127A83));
            var placements = new List<CoverPlacement>(CoverPairCount * 2);
            for (int attempt = 0; attempt < 1200 && placements.Count < CoverPairCount * 2; attempt++)
            {
                var prefab = prefabs[(placements.Count / 2) % prefabs.Count];
                float radius = new Vector2(prefab.meta.width, prefab.meta.length).magnitude * 0.5f;
                var position = new Vector2(random.Next(72, Width / 2), random.Next(72, Height - 72));
                var opposite = Mirror(position);
                float pairGap = 2f * radius + CoverGap;
                if ((position - opposite).sqrMagnitude < pairGap * pairGap ||
                    !CanPlaceCover(position, radius) || !CanPlaceCover(opposite, radius) ||
                    !HasCoverSpacing(placements, position, radius))
                    continue;

                float angle = random.Next(0, 8) * 45f;
                placements.Add(new CoverPlacement(prefab, position, angle, radius));
                placements.Add(new CoverPlacement(prefab, opposite, (angle + 180f) % 360f, radius));
            }
            return placements;
        }

        private bool CanPlaceCover(Vector2 position, float radius)
        {
            float edge = BattleSceneController.BorderPadPx + radius;
            return position.x > edge && position.x < Width - edge && position.y > edge && position.y < Height - edge
                && Vector2.Distance(position, ControlPoint) > 82f + radius
                && Vector2.Distance(position, _nodes[WestBase]) > 103f + radius
                && Vector2.Distance(position, _nodes[EastBase]) > 103f + radius
                && !IsRoad(position, radius + 8f);
        }

        private static bool HasCoverSpacing(List<CoverPlacement> placements, Vector2 position, float radius)
        {
            // Every existing placement already has its mirrored counterpart.
            foreach (var other in placements)
            {
                float distance = radius + other.Radius + CoverGap;
                if ((other.Position - position).sqrMagnitude < distance * distance) return false;
            }
            return true;
        }

        internal BattleLaneRoute CreateFlankRoute(bool west, int deploymentSlot)
        {
            int first = deploymentSlot is 1 or 2 ? 7 : 13;
            if (!west) first += 3;

            var waypoints = new Vector2[4];
            for (int i = 0; i < 3; i++)
                waypoints[i] = _nodes[first + i] / BattleSceneController.PPU;
            waypoints[3] = ControlPoint / BattleSceneController.PPU;
            return new BattleLaneRoute(waypoints);
        }

        public Vector2 StepTowardControl(Vector2 position)
        {
            Road nearest = _roads[0];
            float nearestDistance = float.PositiveInfinity, along = 0f;
            foreach (var road in _roads)
            {
                float distance = RoadOffset(road, position, out float projection);
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearest = road;
                along = projection;
            }

            bool towardEnd = _distanceToCenter[nearest.End] + nearest.Length - along <= _distanceToCenter[nearest.Start] + along;
            int next = towardEnd ? nearest.End : nearest.Start;
            float remaining = towardEnd ? nearest.Length - along : along;
            const float lookAhead = 30f;
            if (remaining > lookAhead)
                return _nodes[nearest.Start] + nearest.Direction * (along + (towardEnd ? lookAhead : -lookAhead));
            if (next == Center || Vector2.Distance(position, _nodes[next]) > lookAhead + 6f)
                return _nodes[next];

            // Advance into the next road before reaching a junction so tanks can keep turning.
            foreach (var road in _roads)
            {
                int other = road.Start == next ? road.End : road.End == next ? road.Start : -1;
                if (other < 0 || _distanceToCenter[other] + road.Length > _distanceToCenter[next] + 0.1f) continue;
                return Vector2.MoveTowards(_nodes[next], _nodes[other], lookAhead - remaining + 12f);
            }
            return _nodes[next];
        }

        public Color32[] GroundPixels()
        {
            if (_ground != null) return _ground;
            var (earth, dark, roadColor, verge, concrete) = _map switch
            {
                BattleMap.City => ((Color32)new Color(0.54f, 0.49f, 0.43f), (Color32)new Color(0.31f, 0.31f, 0.29f),
                    (Color32)new Color(0.27f, 0.31f, 0.34f), (Color32)new Color(0.69f, 0.64f, 0.54f), (Color32)new Color(0.52f, 0.54f, 0.52f)),
                BattleMap.Forest => ((Color32)new Color(0.36f, 0.47f, 0.27f), (Color32)new Color(0.15f, 0.23f, 0.15f),
                    (Color32)new Color(0.48f, 0.39f, 0.26f), (Color32)new Color(0.51f, 0.46f, 0.29f), (Color32)new Color(0.48f, 0.49f, 0.40f)),
                _ => ((Color32)new Color(0.69f, 0.51f, 0.31f), (Color32)new Color(0.44f, 0.30f, 0.19f),
                    (Color32)new Color(0.65f, 0.55f, 0.38f), (Color32)new Color(0.73f, 0.61f, 0.40f), (Color32)new Color(0.59f, 0.55f, 0.46f))
            };

            var pixels = new Color32[Width * Height];
            float[] roadEdges = new float[pixels.Length];
            float[] tracks = new float[pixels.Length];
            float[] roadLengths = new float[pixels.Length];
            System.Array.Fill(roadEdges, float.PositiveInfinity);
            foreach (var road in _roads)
            {
                Vector2 a = _nodes[road.Start], b = _nodes[road.End];
                float spread = road.HalfWidth + 15f;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - spread));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - spread));
                int x1 = Mathf.Min(Width / 2 - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + spread));
                int y1 = Mathf.Min(Height - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + spread));
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float offset = RoadOffset(road, new Vector2(x + 0.5f, y + 0.5f), out float along);
                    int i = y * Width + x;
                    float edge = offset - road.HalfWidth;
                    if (edge >= roadEdges[i]) continue;
                    roadEdges[i] = edge;
                    tracks[i] = offset;
                    roadLengths[i] = along + road.StartDistance;
                }
            }

            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width / 2; x++)
            {
                int i = y * Width + x;
                int frame = Mathf.Min(Mathf.Min(x, Width - 1 - x), Mathf.Min(y, Height - 1 - y));
                float patch = Mathf.PerlinNoise(x * 0.011f + (_seed & 255) * 0.17f, y * 0.014f);
                float grain = Grain(x, y);
                Color32 color = Color32.Lerp(dark, earth, Mathf.Clamp01(patch * 0.88f + grain * 0.18f + 0.08f));

                float edge = roadEdges[i] + (patch - 0.5f) * (_map == BattleMap.City ? 3f : 13f);
                if (edge < 10f)
                {
                    Color32 surface = Color32.Lerp(roadColor, dark, patch * 0.16f + grain * 0.13f);
                    if (_map == BattleMap.City && tracks[i] < 1.6f && roadLengths[i] % 31f < 14f && grain < 0.90f)
                        surface = Color32.Lerp(surface, verge, 0.64f);
                    if (_map != BattleMap.City && Mathf.Abs(tracks[i] - 17f) < 3.4f)
                        surface = Color32.Lerp(surface, dark, 0.07f + patch * 0.18f);
                    color = edge < 0f ? surface : Color32.Lerp(verge, color, edge * 0.1f);
                }
                else
                {
                    color = PaintOffRoad(x, y, color, dark, verge, patch, grain);
                }

                Vector2 point = new(x + 0.5f, y + 0.5f);
                float west = PadDistance(point - _nodes[WestBase], 92f, 90f);
                float east = PadDistance(point - _nodes[EastBase], 92f, 90f);
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

        private Color32 PaintOffRoad(int x, int y, Color32 surface, Color32 low, Color32 high, float patch, float grain)
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
