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
        private const float ControlClearRadius = 86f;
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
            // Artificial installations (axis-locked, right-angle grids & functional assemblies)
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
        private readonly List<DistrictGround> _ridgeGround = new();
        private Color32[] _ground;

        internal BattlefieldLayout(BattleMap map, int seed, BattlefieldPlan plan, BattlefieldTheme theme, BattleSpawnAssets assets)
        {
            _map = map;
            _seed = seed;
            var random = new System.Random(seed);
            _scenario = PickRegionalScenario(map, random);
            _westBase = new Vector2(128, ControlPoint.y + random.Next(-17, 18));
            float reflection = random.Next(2) == 0 ? 1f : -1f;

            // Pass 1: Register connector lanes, ridges, and corner detours first so main zones respect them.
            var mainDistrictIndices = new List<int>();
            var ridgeAnchors = new List<(Vector2 Position, float Radius, float Angle)>();

            for (int i = 0; i < plan.Districts.Length; i++)
            {
                var district = plan.Districts[i];
                Vector2 center = district.Center * PlanScale + new Vector2(random.Next(-10, 11), random.Next(-12, 13));
                float rawAngle = district.Angle + random.Next(-6, 7);
                if (map == BattleMap.City) rawAngle = Mathf.Round(district.Angle / 90f) * 90f;
                center.y = ControlPoint.y + (center.y - ControlPoint.y) * reflection;

                bool isConnector = district.Cover.Length == 0;
                if (isConnector)
                {
                    Vector2 groundSize = district.GroundSize * PlanScale;
                    if (groundSize.x > 0f && groundSize.y > 0f)
                        _districtGround.Add(new DistrictGround(center, groundSize, Direction(rawAngle * reflection), true));
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

            // Pass 2: Assign scenario-coherent artificial & natural archetypes to each main district.
            // Large districts (0..3) become major facilities/biomes; rear (4) becomes support/staging;
            // compact forward pocket (5) becomes a contested checkpoint, battle wreck, or natural outcrop.
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
                    baseAngle = Mathf.Round(district.Angle / 90f) * 90f;
                else if (!natural)
                    baseAngle = Mathf.Round(baseAngle / 15f) * 15f;

                float orientedAngle = baseAngle * reflection;
                if (district.GroundSize.x > 0f && district.GroundSize.y > 0f)
                {
                    if (archetype is ZoneArchetype.CityBotanicalPark or ZoneArchetype.CityMemorialPark)
                        _parkGround.Add(new DistrictGround(center, district.GroundSize, Direction(orientedAngle), false));
                    else if (!natural)
                        _districtGround.Add(new DistrictGround(center, district.GroundSize, Direction(orientedAngle), false));
                }

                if (natural)
                    PopulateNaturalDistrict(district, archetype, center, baseAngle, reflection, theme, assets, random);
                else
                    PopulateArtificialDistrict(district, archetype, center, baseAngle, reflection, map, theme, assets, random);
            }

            // Pass 3: Scatter scenario-matched natural scree, trees & undergrowth around large ridge bases.
            PopulateRidgeFoothills(_scenario, ridgeAnchors, assets, random);
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
            // First 4 slots correspond to the 4 major strategic zones (shuffled in pairs for seeded replayability);
            // Slot 4 is the rear staging flank; Slot 5 is the compact forward contested zone near the control point.
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

            // Shuffle major zones deterministically so artificial & natural territories swap lanes across seeds.
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
                RegionalScenario.CityRailFreight => ZoneArchetype.RoadCheckpoint,
                RegionalScenario.CityDowntownAvenue => ZoneArchetype.RoadCheckpoint,
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
                var size = new Vector2(prefab.meta.width + RidgeFootMargin * 2f,
                    prefab.meta.length + RidgeFootMargin * 2f);
                _ridgeGround.Add(new DistrictGround(position, size, Direction(rotation), false));
                AddMirrored(prefab, position, rotation);
                float approxRadius = Mathf.Max(prefab.meta.width, prefab.meta.length) * 0.5f;
                ridgeAnchors.Add((position, approxRadius, rotation));
            }
        }

        private void PlaceCornerDetourDistrict(BattlefieldDistrict district, Vector2 center, float angle, float reflection,
            BattleMap map, BattlefieldTheme theme, BattleSpawnAssets assets, System.Random random)
        {
            Vector2 right = Direction(angle);
            if (district.GroundSize.x > 0f && district.GroundSize.y > 0f)
                _districtGround.Add(new DistrictGround(center, district.GroundSize, Direction(angle * reflection), false));

            foreach (var piece in district.Cover)
            {
                Vector2 offset = right * piece.Offset.x + Vector2.Perpendicular(right) * piece.Offset.y;
                offset.y *= reflection;
                Vector2 position = Vector2Int.RoundToInt(center + offset);
                float rotation = (angle + piece.Angle) * reflection;
                if (map == BattleMap.City && piece.Kind != BattlefieldCoverKind.Barricade)
                    rotation = Mathf.Round(rotation / 90f) * 90f;

                var prefab = theme.GetCover(piece.Kind);
                if (prefab == null) continue;
                AddMirrored(prefab, position, rotation);
            }

            // Corner detours receive a crisp thematic accent matching the current RegionalScenario.
            if (map == BattleMap.City)
            {
                float alignedAngle = Mathf.Round(angle * reflection / 90f) * 90f;
                Vector2 side = Direction(alignedAngle);
                string propA = _scenario == RegionalScenario.CityDowntownAvenue ? "abandoned_car" : "guard_booth";
                string propB = _scenario == RegionalScenario.CityRailFreight ? "oil_drum_stack" : "tank_trap";
                TryPlaceMirrored(GetProp(assets, propA), center + side * 46f + Vector2.Perpendicular(side) * 28f, alignedAngle, 8f);
                TryPlaceMirrored(GetProp(assets, propB), center - side * 48f - Vector2.Perpendicular(side) * 26f, alignedAngle, 8f);
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

            // Compute threat-facing cardinal axis pointing toward the center control point.
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
                        AddMirrored(wallPrefab, position, wallRot);
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

                // Orient directional weapon emplacements directly toward the battle center.
                if (prefab.kind is "sam_launcher" or "field_artillery")
                    rotation = frontAimAngle;

                if (TryPlaceMirrored(prefab, position, rotation, clearance: 6f, checkConnectors: false))
                {
                    if (primaryAnchor == null || piece.Kind == BattlefieldCoverKind.Landmark)
                        primaryAnchor = position;
                }
            }

            Vector2 anchor = primaryAnchor ?? center;

            // Assemble domain-specific artificial structures with orderly geometry.
            switch (archetype)
            {
                case ZoneArchetype.AirDefenseBase:
                    // SAM launcher oriented toward center + generator/power support + concrete/HESCO front screen
                    PlaceFiringEmplacement(GetProp(assets, "sam_launcher"), GetProp(assets, "ammo_depot"),
                        anchor + frontAxis * 42f - frontPerp * 26f, frontAxis, frontAimAngle);
                    PlaceUtilityRow(GetProp(assets, "generator_unit"), GetProp(assets, "radio_mast"),
                        anchor - frontAxis * 48f + frontPerp * 32f, axisAngle, spacing: 32f);
                    PlaceDefensiveLine(GetProp(assets, "concrete_wall"), GetProp(assets, "hesco_barrier"),
                        center + frontAxis * 66f, frontPerp, frontLineAngle, count: 2, step: 48f);
                    break;

                case ZoneArchetype.CommandTentCamp:
                    // Parallel field tents + ammo dump & radio mast + sandbag/tank-trap perimeter
                    PlaceParallelRow(GetProp(assets, "military_tent"), GetProp(assets, "military_tent"),
                        anchor + frontPerp * 46f, axisAngle, spacing: 34f);
                    PlaceUtilityRow(GetProp(assets, "ammo_depot"), GetProp(assets, "radio_mast"),
                        anchor - frontAxis * 46f - frontPerp * 30f, axisAngle, spacing: 32f);
                    PlaceDefensiveLine(GetProp(assets, "sandbag_wall"), GetProp(assets, "tank_trap"),
                        center + frontAxis * 60f, frontPerp, frontLineAngle, count: 3, step: 32f);
                    break;

                case ZoneArchetype.RoadCheckpoint:
                    // Guard booth flanking a roadblock checkpoint gate + anti-tank obstacles
                    PlaceCheckpointAssembly(GetProp(assets, "checkpoint_gate"), GetProp(assets, "guard_booth"),
                        GetProp(assets, "concrete_wall"), center + frontAxis * 34f, frontPerp, frontLineAngle);
                    PlaceAlignedAccent(GetProp(assets, _map == BattleMap.City ? "city_barricade" : "hesco_barrier"),
                        center, -frontAxis * 34f + frontPerp * 26f, -frontAxis * 34f - frontPerp * 26f, frontLineAngle);
                    break;

                case ZoneArchetype.SolarOutpost:
                    // Parallel south-facing photovoltaic arrays + water tower + radar dish + HESCO guard line
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
                    // Parallel freight rail boxcars + shipping containers + forklift on loading apron
                    PlaceParallelRow(GetProp(assets, "rail_boxcar"), GetProp(assets, "cargo_container"),
                        anchor + axisRight * 58f, axisAngle, spacing: 26f);
                    PlaceUtilityRow(GetProp(assets, "forklift_wreck"), GetProp(assets, "supply_crate"),
                        anchor - axisRight * 54f, axisAngle, spacing: 26f);
                    PlaceAlignedAccent(GetProp(assets, "steel_silo"),
                        anchor, -frontAxis * 52f + frontPerp * 36f, -frontAxis * 52f - frontPerp * 36f, axisAngle);
                    break;

                case ZoneArchetype.SupplyDepotYard:
                    // Parallel container bays + hazardous fuel/oil drum yard + forklift/crates
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
                    // High-voltage transformer bank + diesel generator + water tower / oil drums behind concrete walls
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
                    // Towed field howitzer facing battle center + ammo depot behind + watch tower + sandbags
                    PlaceFiringEmplacement(GetProp(assets, "field_artillery"), GetProp(assets, "ammo_depot"),
                        anchor + frontAxis * 34f, frontAxis, frontAimAngle);
                    PlaceAlignedAccent(GetProp(assets, "watch_tower"),
                        anchor, -frontAxis * 48f + frontPerp * 34f, -frontAxis * 48f - frontPerp * 34f, axisAngle);
                    PlaceDefensiveLine(GetProp(assets, "sandbag_wall"), GetProp(assets, "tank_trap"),
                        center + frontAxis * 62f, frontPerp, frontLineAngle, count: 3, step: 34f);
                    break;

                case ZoneArchetype.UrbanFuelAvenue:
                    // Roadside fuel pump island + bus stop / kiosk + parked civilian car aligned along the avenue
                    PlaceRoadsideStrip(
                        GetProp(assets, "fuel_pump_island"),
                        GetProp(assets, _map == BattleMap.City ? "bus_stop" : "abandoned_car"),
                        GetProp(assets, _map == BattleMap.City ? "street_kiosk" : "oil_drum_stack"),
                        anchor, axisRight, axisUp, axisAngle);
                    PlaceAlignedAccent(GetProp(assets, "abandoned_car"),
                        center, frontAxis * 46f + frontPerp * 24f, frontAxis * 46f - frontPerp * 24f, axisAngle);
                    break;

                case ZoneArchetype.UrbanResidentialBlock:
                    // European apartment/blockhouse yard framed by brick courtyard walls, kiosk & street barricades
                    PlaceDefensiveLine(GetProp(assets, "brick_wall"), GetProp(assets, "city_barricade"),
                        center + frontAxis * 62f, frontPerp, frontLineAngle, count: 2, step: 46f);
                    PlaceAlignedAccent(GetProp(assets, random.Next(2) == 0 ? "street_kiosk" : "bus_stop"),
                        anchor, -frontAxis * 46f + frontPerp * 36f, -frontAxis * 46f - frontPerp * 36f, axisAngle);
                    PlaceAlignedAccent(GetProp(assets, "abandoned_car"),
                        anchor, -frontAxis * 44f - frontPerp * 34f, -frontAxis * 44f + frontPerp * 34f, axisAngle + 90f);
                    break;

                case ZoneArchetype.RuralWindmillFarm:
                    // Windmill / cabin farmstead with wooden shed, stone well, brick wall & neat rolled hay bales
                    PlaceUtilityRow(GetProp(assets, "wooden_shed"), GetProp(assets, "hay_bale"),
                        anchor + axisRight * 50f, axisAngle, spacing: 34f);
                    PlaceAlignedAccent(GetProp(assets, "stone_well"),
                        anchor, -frontAxis * 38f + frontPerp * 32f, -frontAxis * 38f - frontPerp * 32f, axisAngle);
                    PlaceDefensiveLine(GetProp(assets, "brick_wall"), GetProp(assets, "hay_bale"),
                        center + frontAxis * 56f, frontPerp, frontLineAngle, count: 2, step: 44f);
                    break;

                case ZoneArchetype.TimberLoggingCamp:
                    // Sawmill clearing: cabin + shed + parallel timber stacks + watch tower / forklift
                    PlaceParallelRow(GetProp(assets, "timber_stack"), GetProp(assets, "timber_stack"),
                        anchor + axisRight * 54f, axisAngle, spacing: 24f);
                    PlaceUtilityRow(GetProp(assets, "wooden_shed"), GetProp(assets, _scenario == RegionalScenario.ForestRuralValley ? "forklift_wreck" : "fuel_tank"),
                        anchor - frontAxis * 46f + frontPerp * 30f, axisAngle, spacing: 32f);
                    PlaceAlignedAccent(GetProp(assets, _scenario == RegionalScenario.ForestAlpineRidge ? "steel_silo" : "watch_tower"),
                        anchor, -frontAxis * 46f - frontPerp * 36f, -frontAxis * 46f + frontPerp * 36f, axisAngle);
                    break;

                case ZoneArchetype.BattleWreckScar:
                    // Contested war-torn set-piece: downed helicopter or burned tank flanked by shell crater & hasty cover
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
                        AddMirrored(wallPrefab, position, wallRot);
                    }
                    anchorPoints.Add(position);
                    continue;
                }

                DestructibleProp prefab = ResolveNaturalSlot(archetype, piece.Kind, assets, random);
                if (prefab == null) continue;
                float freeAngle = random.Next(0, 360);
                if (TryPlaceMirrored(prefab, position, freeAngle, clearance: 7f, checkConnectors: false))
                    anchorPoints.Add(position);
            }

            if (anchorPoints.Count == 0) anchorPoints.Add(center);

            // Grow organic ecological layers (canopy grove, geological talus, or water-edge basin).
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

        /// <summary>
        /// Organic woodland placement: curved drift of canopy trees surrounded by low undergrowth, logs & stumps.
        /// </summary>
        private void PlaceOrganicGrove(Vector2 center, string[] canopyPool, string[] underbrushPool,
            BattleSpawnAssets assets, System.Random random)
        {
            ScatterCurvedDrift(center, 34f, 76f, 3, canopyPool, assets, random);
            ScatterOrganicDrift(center, 46f, 94f, 3, underbrushPool, assets, random);
        }

        /// <summary>
        /// Geological outcrop placement: rugged formations anchored near parents with downslope talus & scrub.
        /// </summary>
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

        /// <summary>
        /// Water/basin cluster: focal pond or marsh in the hollow, moisture-loving trees hugging the rim, outer stones.
        /// </summary>
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
            {
                ScatterOrganicDrift(ridge.Position, ridge.Radius + 18f, ridge.Radius + 60f, 3, pool, assets, random);
            }
        }

        private void ScatterCurvedDrift(Vector2 center, float minRadius, float maxRadius, int count,
            string[] kindPool, BattleSpawnAssets assets, System.Random random)
        {
            if (kindPool == null || kindPool.Length == 0) return;
            // Curved arc (natural tree line or windbreak) with organic wobble & free rotation.
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
                    if (TryPlaceMirrored(prefab, pos, random.Next(0, 360), clearance: 9f, checkConnectors: true))
                        break;
                }
            }
        }

        private void ScatterOrganicDrift(Vector2 anchor, float minRadius, float maxRadius, int count,
            string[] kindPool, BattleSpawnAssets assets, System.Random random)
        {
            if (kindPool == null || kindPool.Length == 0) return;
            // Golden-angle stepping + organic wobble distributes natural props in lifelike irregular clumps.
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
                    if (TryPlaceMirrored(prefab, pos, rot, clearance: 9f, checkConnectors: true))
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
            float clearance = 8f, bool checkConnectors = true)
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

            // 4. Optional road/connector lane preservation for secondary props
            if (checkConnectors && IsInsideConnectorLane(pos, radius * 0.55f))
                return false;

            // 5. Strict non-overlap against every already-placed cover piece
            foreach (var existing in _cover)
            {
                if (existing.Contains(pos, radius + clearance) || existing.Contains(mirrored, radius + clearance))
                    return false;
            }

            AddMirrored(prefab, pos, rotation);
            return true;
        }

        private bool IsInsideConnectorLane(Vector2 point, float margin)
        {
            foreach (var d in _districtGround)
            {
                if (!d.IsConnector) continue;
                Vector2 local = d.ToLocal(point);
                if (Mathf.Abs(local.x) < d.HalfSize.x - 4f && Mathf.Abs(local.y) < d.HalfSize.y * 0.65f + margin)
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
                        ? PaintCityParks(point, PaintCityDistricts(point, color, concrete, dark, verge, patch, grain), dark, patch, grain)
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
            _scenario switch
            {
                RegionalScenario.ForestAutumnHighland => (
                    (Color32)new Color(0.42f, 0.42f, 0.25f),
                    (Color32)new Color(0.20f, 0.21f, 0.14f),
                    (Color32)new Color(0.60f, 0.43f, 0.25f),
                    (Color32)new Color(0.49f, 0.48f, 0.40f)),
                RegionalScenario.ForestWetlandGrove => (
                    (Color32)new Color(0.31f, 0.44f, 0.28f),
                    (Color32)new Color(0.13f, 0.22f, 0.17f),
                    (Color32)new Color(0.44f, 0.45f, 0.29f),
                    (Color32)new Color(0.45f, 0.47f, 0.40f)),
                RegionalScenario.ForestAlpineRidge => (
                    (Color32)new Color(0.44f, 0.50f, 0.46f),
                    (Color32)new Color(0.21f, 0.27f, 0.26f),
                    (Color32)new Color(0.70f, 0.74f, 0.75f),
                    (Color32)new Color(0.52f, 0.54f, 0.53f)),
                _ => _map switch
                {
                    BattleMap.City => ((Color32)new Color(0.44f, 0.43f, 0.39f), (Color32)new Color(0.31f, 0.33f, 0.33f),
                        (Color32)new Color(0.68f, 0.64f, 0.54f), (Color32)new Color(0.51f, 0.54f, 0.53f)),
                    BattleMap.Forest => ((Color32)new Color(0.36f, 0.47f, 0.27f), (Color32)new Color(0.15f, 0.23f, 0.15f),
                        (Color32)new Color(0.51f, 0.46f, 0.29f), (Color32)new Color(0.48f, 0.49f, 0.40f)),
                    _ => ((Color32)new Color(0.69f, 0.51f, 0.31f), (Color32)new Color(0.44f, 0.30f, 0.19f),
                        (Color32)new Color(0.73f, 0.61f, 0.40f), (Color32)new Color(0.59f, 0.55f, 0.46f))
                }
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
            var parkGrass = new Color32(84, 114, 68, 255);
            var parkRim = new Color32(154, 150, 138, 255);
            Color32 turf = Color32.Lerp(parkGrass, dark, 0.16f * grain + 0.14f * (1f - patch));
            Color32 blended = Color32.Lerp(terrain, turf, lawn * 0.86f);
            return Color32.Lerp(blended, parkRim, border * 0.62f);
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
