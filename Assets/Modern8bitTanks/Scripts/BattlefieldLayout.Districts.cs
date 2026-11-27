using System.Collections.Generic;
using UnityEngine;

namespace Modern8bitTanks
{
    internal sealed partial class BattlefieldLayout
    {
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

        private readonly List<DistrictGround> _districtGround = new();
        private readonly List<DistrictGround> _parkGround = new();
        private readonly List<DistrictGround> _groveGround = new();
        private readonly List<DistrictGround> _farmGround = new();
        private readonly List<DistrictGround> _ridgeGround = new();

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
    }
}
