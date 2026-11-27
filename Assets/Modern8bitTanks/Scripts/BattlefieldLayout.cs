using System.Collections.Generic;
using UnityEngine;

namespace Modern8bitTanks
{
    /// <summary>Shared coordinates for terrain, deployment, AI and the tactical radar.</summary>
    internal sealed partial class BattlefieldLayout
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

        private readonly BattleMap _map;
        private readonly RegionalScenario _scenario;
        private readonly int _seed;
        private readonly Vector2 _westBase;
        private readonly List<CoverPlacement> _cover = new();
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

        internal bool IsWallAt(Vector2 position)
        {
            foreach (var cover in _cover)
                if (cover.Prefab.meta.fixedCover && cover.Contains(position, 0f))
                    return true;
            return false;
        }

        internal bool IsRoadAt(Vector2 position) => IsOnRoad(position, 0f, primaryOnly: false);

        internal bool IsWaterAt(Vector2 position) => IsInWater(position, 0f);
    }
}
