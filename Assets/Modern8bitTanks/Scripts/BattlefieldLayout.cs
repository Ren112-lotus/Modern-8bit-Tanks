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

        private enum ZoneArchetype
        {
            // Artificial (grid-aligned, right-angle, functional sets & defensive lines)
            DesertOutpost,
            DesertSupplyDepot,
            DesertCheckpoint,
            CityIndustrial,
            CityResidential,
            CityCommand,
            CityCheckpoint,
            ForestLoggingCamp,
            ForestOutpost,
            ForestFieldPosition,
            // Natural (organic clusters, free rotation, parent-to-child scatter)
            DesertMesaOutcrop,
            DesertPalmGrove,
            DesertRockCluster,
            CityPark,
            ForestWoodland,
            ForestBoulderField,
            ForestSmallGrove
        }

        private readonly BattleMap _map;
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
            _westBase = new Vector2(128, ControlPoint.y + random.Next(-17, 18));
            float reflection = random.Next(2) == 0 ? 1f : -1f;

            // Pass 1: Register connectors, ridges, and corner detours first so semantic districts respect them.
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

            // Pass 2: Assign meaningful artificial / natural zone archetypes to main districts (seeded variation).
            var archetypes = AssignDistrictArchetypes(map, mainDistrictIndices.Count, random);
            for (int m = 0; m < mainDistrictIndices.Count; m++)
            {
                var district = plan.Districts[mainDistrictIndices[m]];
                var archetype = archetypes[m];
                bool natural = IsNaturalArchetype(archetype);

                Vector2 center = district.Center * PlanScale + new Vector2(random.Next(-10, 11), random.Next(-12, 13));
                center.y = ControlPoint.y + (center.y - ControlPoint.y) * reflection;

                float baseAngle = district.Angle + (natural ? random.Next(-12, 13) : random.Next(-5, 6));
                if (map == BattleMap.City)
                    baseAngle = Mathf.Round(district.Angle / 90f) * 90f;
                else if (!natural)
                    baseAngle = Mathf.Round(baseAngle / 15f) * 15f; // Crisp military orientation for compounds/camps

                float orientedAngle = baseAngle * reflection;
                if (district.GroundSize.x > 0f && district.GroundSize.y > 0f)
                {
                    if (archetype == ZoneArchetype.CityPark)
                        _parkGround.Add(new DistrictGround(center, district.GroundSize, Direction(orientedAngle), false));
                    else if (!natural)
                        _districtGround.Add(new DistrictGround(center, district.GroundSize, Direction(orientedAngle), false));
                }

                if (natural)
                    PopulateNaturalDistrict(district, archetype, center, baseAngle, reflection, theme, assets, random);
                else
                    PopulateArtificialDistrict(district, archetype, center, baseAngle, reflection, map, theme, assets, random);
            }

            // Pass 3: Organically scatter natural foothill props around large ridges (scree rocks, trees, scrub).
            PopulateRidgeFoothills(map, ridgeAnchors, assets, random);
        }

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
            ZoneArchetype.DesertMesaOutcrop or
            ZoneArchetype.DesertPalmGrove or
            ZoneArchetype.DesertRockCluster or
            ZoneArchetype.CityPark or
            ZoneArchetype.ForestWoodland or
            ZoneArchetype.ForestBoulderField or
            ZoneArchetype.ForestSmallGrove;

        private static ZoneArchetype[] AssignDistrictArchetypes(BattleMap map, int count, System.Random random)
        {
            // Keep deliberate balance of artificial installations and natural clusters per biome.
            ZoneArchetype[] template = map switch
            {
                BattleMap.City => random.Next(2) == 0
                    ? new[]
                    {
                        ZoneArchetype.CityIndustrial,
                        ZoneArchetype.CityResidential,
                        ZoneArchetype.CityCommand,
                        ZoneArchetype.CityPark,
                        ZoneArchetype.CityCheckpoint,
                        ZoneArchetype.CityCheckpoint
                    }
                    : new[]
                    {
                        ZoneArchetype.CityResidential,
                        ZoneArchetype.CityIndustrial,
                        ZoneArchetype.CityPark,
                        ZoneArchetype.CityCommand,
                        ZoneArchetype.CityCheckpoint,
                        ZoneArchetype.CityCheckpoint
                    },
                BattleMap.Forest => random.Next(2) == 0
                    ? new[]
                    {
                        ZoneArchetype.ForestLoggingCamp,
                        ZoneArchetype.ForestWoodland,
                        ZoneArchetype.ForestBoulderField,
                        ZoneArchetype.ForestOutpost,
                        ZoneArchetype.ForestSmallGrove,
                        ZoneArchetype.ForestFieldPosition
                    }
                    : new[]
                    {
                        ZoneArchetype.ForestWoodland,
                        ZoneArchetype.ForestLoggingCamp,
                        ZoneArchetype.ForestOutpost,
                        ZoneArchetype.ForestBoulderField,
                        ZoneArchetype.ForestSmallGrove,
                        ZoneArchetype.ForestFieldPosition
                    },
                _ => random.Next(2) == 0
                    ? new[]
                    {
                        ZoneArchetype.DesertOutpost,
                        ZoneArchetype.DesertMesaOutcrop,
                        ZoneArchetype.DesertSupplyDepot,
                        ZoneArchetype.DesertPalmGrove,
                        ZoneArchetype.DesertCheckpoint,
                        ZoneArchetype.DesertRockCluster
                    }
                    : new[]
                    {
                        ZoneArchetype.DesertSupplyDepot,
                        ZoneArchetype.DesertPalmGrove,
                        ZoneArchetype.DesertOutpost,
                        ZoneArchetype.DesertMesaOutcrop,
                        ZoneArchetype.DesertCheckpoint,
                        ZoneArchetype.DesertRockCluster
                    }
            };

            var result = new ZoneArchetype[count];
            for (int i = 0; i < count; i++)
                result[i] = template[i % template.Length];
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

            // Corner detours get a small thematic accent on the flank: artificial checkpoint or natural verge.
            if (map == BattleMap.Forest)
            {
                ScatterNaturalRing(center, 46f, 82f, 2, new[] { "pine_cluster", "moss_rocks", "broadleaf_grove" }, assets, random);
            }
            else if (map == BattleMap.Desert)
            {
                ScatterNaturalRing(center, 44f, 78f, 2, new[] { "desert_rock", "desert_scrub" }, assets, random);
            }
            else
            {
                float alignedAngle = Mathf.Round(angle * reflection / 90f) * 90f;
                Vector2 side = Direction(alignedAngle);
                TryPlaceMirrored(GetProp(assets, "tank_trap"), center + side * 46f + Vector2.Perpendicular(side) * 28f, alignedAngle, 8f);
                TryPlaceMirrored(GetProp(assets, "supply_crate"), center - side * 48f - Vector2.Perpendicular(side) * 26f, alignedAngle, 8f);
            }
        }

        // =====================================================================
        // ARTIFICIAL ZONES: Axis-aligned, right-angle, functional installations
        // =====================================================================
        private void PopulateArtificialDistrict(BattlefieldDistrict district, ZoneArchetype archetype, Vector2 center,
            float baseAngle, float reflection, BattleMap map, BattlefieldTheme theme, BattleSpawnAssets assets, System.Random random)
        {
            Vector2 right = Direction(baseAngle);
            float axisAngle = baseAngle * reflection;
            Vector2 axisRight = Direction(axisAngle);
            Vector2 axisUp = Vector2.Perpendicular(axisRight);
            // Direction from this district toward center control point (enemy-facing side for defensive lines!)
            Vector2 toFront = (ControlPoint - center).normalized;
            Vector2 frontAxis = Mathf.Abs(Vector2.Dot(toFront, axisRight)) >= Mathf.Abs(Vector2.Dot(toFront, axisUp))
                ? axisRight * Mathf.Sign(Vector2.Dot(toFront, axisRight))
                : axisUp * Mathf.Sign(Vector2.Dot(toFront, axisUp));
            Vector2 frontPerp = Vector2.Perpendicular(frontAxis);
            float frontLineAngle = Mathf.Atan2(frontPerp.y, frontPerp.x) * Mathf.Rad2Deg;

            Vector2? primaryAnchor = null;

            // Place the structural spine from the plan, snapped cleanly to right angles on the zone axis.
            foreach (var piece in district.Cover)
            {
                Vector2 offset = right * piece.Offset.x + Vector2.Perpendicular(right) * piece.Offset.y;
                offset.y *= reflection;
                Vector2 position = Vector2Int.RoundToInt(center + offset);

                float localSnap = Mathf.Round(piece.Angle / 90f) * 90f;
                float rotation = axisAngle + localSnap * reflection;
                if (map == BattleMap.City)
                    rotation = Mathf.Round(rotation / 90f) * 90f;

                DestructibleProp prefab = ResolveArtificialSlot(archetype, piece.Kind, theme, assets, random);
                if (prefab == null) continue;

                if (piece.Kind == BattlefieldCoverKind.Wall)
                {
                    // Keep authored sight walls intact for lane sightline control; snap to city grid in urban maps.
                    float wallRot = map == BattleMap.City
                        ? Mathf.Round(((baseAngle + piece.Angle) * reflection) / 90f) * 90f
                        : (baseAngle + piece.Angle) * reflection;
                    AddMirrored(prefab, position, wallRot);
                }
                else
                {
                    if (TryPlaceMirrored(prefab, position, rotation, clearance: 6f, checkConnectors: false))
                    {
                        if (primaryAnchor == null || piece.Kind == BattlefieldCoverKind.Landmark)
                            primaryAnchor = position;
                    }
                }
            }

            Vector2 anchor = primaryAnchor ?? center;

            // Add cohesive, right-angle functional assemblies suited to the installation's role!
            switch (archetype)
            {
                case ZoneArchetype.DesertOutpost:
                    // Watch tower or radar on rear corner + aligned fuel & ammo dump + front HESCO/sandbag line
                    PlaceAlignedAccent(GetProp(assets, random.Next(2) == 0 ? "radar_station" : "watch_tower"),
                        anchor, -frontAxis * 50f + frontPerp * 34f, -frontAxis * 48f - frontPerp * 34f, axisAngle);
                    PlaceParallelPair(GetProp(assets, "fuel_tank"), GetProp(assets, "supply_crate"),
                        anchor - frontAxis * 48f - frontPerp * 28f, axisAngle, spacing: 26f);
                    PlaceDefensiveLine(GetProp(assets, "hesco_barrier"), GetProp(assets, "tank_trap"),
                        center + frontAxis * 64f, frontPerp, frontLineAngle, count: 3, step: 30f);
                    break;

                case ZoneArchetype.DesertSupplyDepot:
                    // Parallel shipping containers + neatly stacked fuel/crates + HESCO perimeter
                    PlaceParallelPair(GetProp(assets, "cargo_container"), GetProp(assets, "cargo_container"),
                        anchor + frontPerp * 52f, axisAngle, spacing: 26f);
                    PlaceParallelPair(GetProp(assets, "fuel_tank"), GetProp(assets, "supply_crate"),
                        anchor - frontPerp * 48f, axisAngle, spacing: 24f);
                    PlaceDefensiveLine(GetProp(assets, "sandbag_wall"), GetProp(assets, "hesco_barrier"),
                        center + frontAxis * 62f, frontPerp, frontLineAngle, count: 2, step: 42f);
                    break;

                case ZoneArchetype.DesertCheckpoint:
                    // Ruined outpost with straight sandbag + anti-tank trap roadblock
                    PlaceDefensiveLine(GetProp(assets, "sandbag_wall"), GetProp(assets, "tank_trap"),
                        center + frontAxis * 42f, frontPerp, frontLineAngle, count: 3, step: 32f);
                    PlaceAlignedAccent(GetProp(assets, "watch_tower"),
                        center, -frontAxis * 38f + frontPerp * 32f, -frontAxis * 38f - frontPerp * 32f, axisAngle);
                    break;

                case ZoneArchetype.CityIndustrial:
                    // Factory yard: parallel cargo containers + fuel depot + crate row
                    PlaceParallelPair(GetProp(assets, "cargo_container"), GetProp(assets, "cargo_container"),
                        anchor + axisRight * 62f, axisAngle, spacing: 26f);
                    PlaceParallelPair(GetProp(assets, "fuel_tank"), GetProp(assets, "supply_crate"),
                        anchor - axisRight * 58f, axisAngle + 90f, spacing: 24f);
                    PlaceDefensiveLine(GetProp(assets, "city_barricade"), GetProp(assets, "tank_trap"),
                        center + frontAxis * 66f, frontPerp, frontLineAngle, count: 2, step: 48f);
                    break;

                case ZoneArchetype.CityResidential:
                    // Urban block: corner barricades & sandbag street defenses aligned with the block
                    PlaceDefensiveLine(GetProp(assets, "city_barricade"), GetProp(assets, "sandbag_wall"),
                        center + frontAxis * 64f, frontPerp, frontLineAngle, count: 2, step: 48f);
                    PlaceAlignedAccent(GetProp(assets, "supply_crate"),
                        anchor, -frontAxis * 46f + frontPerp * 36f, -frontAxis * 46f - frontPerp * 36f, axisAngle);
                    break;

                case ZoneArchetype.CityCommand:
                    // Fortified command square: radar station + HESCO & anti-tank traps
                    PlaceAlignedAccent(GetProp(assets, "radar_station"),
                        anchor, -frontAxis * 48f + frontPerp * 36f, -frontAxis * 48f - frontPerp * 36f, axisAngle);
                    PlaceDefensiveLine(GetProp(assets, "hesco_barrier"), GetProp(assets, "tank_trap"),
                        center + frontAxis * 60f, frontPerp, frontLineAngle, count: 3, step: 30f);
                    PlaceAlignedAccent(GetProp(assets, "supply_crate"),
                        anchor, -frontAxis * 44f - frontPerp * 34f, -frontAxis * 44f + frontPerp * 34f, axisAngle);
                    break;

                case ZoneArchetype.CityCheckpoint:
                    // Street checkpoint: barricade + anti-tank obstacles + crate
                    PlaceDefensiveLine(GetProp(assets, "city_barricade"), GetProp(assets, "tank_trap"),
                        center + frontAxis * 38f, frontPerp, frontLineAngle, count: 2, step: 38f);
                    PlaceAlignedAccent(GetProp(assets, "sandbag_wall"),
                        center, -frontAxis * 34f, -frontAxis * 34f + frontPerp * 26f, frontLineAngle);
                    break;

                case ZoneArchetype.ForestLoggingCamp:
                    // Timber camp: cabin flanked by neatly parallel timber stacks + watch tower + fuel/crates
                    PlaceParallelPair(GetProp(assets, "timber_stack"), GetProp(assets, "timber_stack"),
                        anchor + axisRight * 56f, axisAngle, spacing: 24f);
                    PlaceAlignedAccent(GetProp(assets, "watch_tower"),
                        anchor, -frontAxis * 46f - frontPerp * 34f, -frontAxis * 46f + frontPerp * 34f, axisAngle);
                    PlaceParallelPair(GetProp(assets, "supply_crate"), GetProp(assets, "fuel_tank"),
                        anchor - frontAxis * 46f + frontPerp * 30f, axisAngle, spacing: 26f);
                    break;

                case ZoneArchetype.ForestOutpost:
                    // Hidden forest bunker/radar outpost + straight sandbag/tank-trap firing line
                    PlaceAlignedAccent(GetProp(assets, "radar_station"),
                        anchor, -frontAxis * 46f + frontPerp * 34f, -frontAxis * 46f - frontPerp * 34f, axisAngle);
                    PlaceDefensiveLine(GetProp(assets, "sandbag_wall"), GetProp(assets, "tank_trap"),
                        center + frontAxis * 56f, frontPerp, frontLineAngle, count: 3, step: 32f);
                    PlaceAlignedAccent(GetProp(assets, "timber_stack"),
                        anchor, -frontAxis * 44f - frontPerp * 32f, -frontAxis * 44f + frontPerp * 32f, axisAngle);
                    break;

                case ZoneArchetype.ForestFieldPosition:
                    // Forward field entrenchment
                    PlaceDefensiveLine(GetProp(assets, "sandbag_wall"), GetProp(assets, "tank_trap"),
                        center + frontAxis * 26f, frontPerp, frontLineAngle, count: 2, step: 34f);
                    PlaceAlignedAccent(GetProp(assets, "supply_crate"),
                        center, -frontAxis * 28f, -frontAxis * 28f + frontPerp * 24f, axisAngle);
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
                ZoneArchetype.DesertOutpost => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, "desert_compound"),
                    BattlefieldCoverKind.Building => GetProp(assets, "desert_bunker"),
                    _ => GetProp(assets, "hesco_barrier")
                },
                ZoneArchetype.DesertSupplyDepot => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, "desert_bunker"),
                    BattlefieldCoverKind.Building => GetProp(assets, "cargo_container"),
                    _ => GetProp(assets, "hesco_barrier")
                },
                ZoneArchetype.DesertCheckpoint => kind switch
                {
                    BattlefieldCoverKind.Landmark or BattlefieldCoverKind.Building => GetProp(assets, "desert_ruin"),
                    _ => GetProp(assets, "sandbag_wall")
                },
                ZoneArchetype.CityIndustrial => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, "city_factory"),
                    BattlefieldCoverKind.Building => GetProp(assets, random.Next(2) == 0 ? "city_blockhouse" : "cargo_container"),
                    _ => GetProp(assets, "city_barricade")
                },
                ZoneArchetype.CityResidential => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, "city_apartment"),
                    BattlefieldCoverKind.Building => GetProp(assets, "city_blockhouse"),
                    _ => GetProp(assets, "city_barricade")
                },
                ZoneArchetype.CityCommand => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, "city_blockhouse"),
                    BattlefieldCoverKind.Building => GetProp(assets, "city_apartment"),
                    _ => GetProp(assets, "hesco_barrier")
                },
                ZoneArchetype.CityCheckpoint => kind switch
                {
                    BattlefieldCoverKind.Landmark or BattlefieldCoverKind.Building => GetProp(assets, "city_blockhouse"),
                    _ => GetProp(assets, "city_barricade")
                },
                ZoneArchetype.ForestLoggingCamp => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, "forest_cabin"),
                    BattlefieldCoverKind.Building => GetProp(assets, "timber_stack"),
                    _ => GetProp(assets, "sandbag_wall")
                },
                ZoneArchetype.ForestOutpost => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, "desert_bunker"),
                    BattlefieldCoverKind.Building => GetProp(assets, "forest_cabin"),
                    _ => GetProp(assets, "hesco_barrier")
                },
                ZoneArchetype.ForestFieldPosition => kind switch
                {
                    BattlefieldCoverKind.Landmark or BattlefieldCoverKind.Building => GetProp(assets, "sandbag_wall"),
                    _ => GetProp(assets, "tank_trap")
                },
                _ => theme.GetCover(kind)
            };
        }

        private void PlaceParallelPair(DestructibleProp first, DestructibleProp second, Vector2 center, float angle, float spacing)
        {
            Vector2 dir = Direction(angle);
            Vector2 perp = Vector2.Perpendicular(dir);
            Vector2 half = perp * (spacing * 0.5f);
            // Try the intended grid slot first; if blocked by a wall or lane, shift along the same right-angle axis.
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
        // NATURAL ZONES: Organic clusters, free rotation, parent-child scatter
        // =====================================================================
        private void PopulateNaturalDistrict(BattlefieldDistrict district, ZoneArchetype archetype, Vector2 center,
            float baseAngle, float reflection, BattlefieldTheme theme, BattleSpawnAssets assets, System.Random random)
        {
            Vector2 right = Direction(baseAngle);
            var anchorPoints = new List<Vector2>();

            // Place natural anchors organically (no rigid 90-degree alignment!).
            foreach (var piece in district.Cover)
            {
                Vector2 offset = right * piece.Offset.x + Vector2.Perpendicular(right) * piece.Offset.y;
                offset.y *= reflection;
                // Add organic positional jitter so natural landmarks don't sit on a rigid blueprint grid.
                Vector2 jitter = piece.Kind == BattlefieldCoverKind.Wall
                    ? Vector2.zero
                    : new Vector2(random.Next(-14, 15), random.Next(-14, 15));
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
                if (TryPlaceMirrored(prefab, position, freeAngle, clearance: 6f, checkConnectors: false))
                    anchorPoints.Add(position);
            }

            if (anchorPoints.Count == 0) anchorPoints.Add(center);

            // Grow organic satellite clusters around each natural parent anchor!
            switch (archetype)
            {
                case ZoneArchetype.DesertMesaOutcrop:
                    // Sandstone mesa flanked by talus boulders & dry desert scrub
                    foreach (var anchor in anchorPoints)
                        ScatterNaturalRing(anchor, 44f, 84f, 2, new[] { "desert_rock", "desert_scrub", "desert_scrub" }, assets, random);
                    break;

                case ZoneArchetype.DesertPalmGrove:
                    // Clustered palms growing together in a dry basin, ringed with scrub & rocks
                    ScatterNaturalRing(center, 34f, 80f, 4, new[] { "palm_tree", "palm_tree", "desert_scrub", "desert_rock" }, assets, random);
                    break;

                case ZoneArchetype.DesertRockCluster:
                    ScatterNaturalRing(center, 30f, 68f, 3, new[] { "desert_rock", "desert_scrub" }, assets, random);
                    break;

                case ZoneArchetype.CityPark:
                    // Soft urban green pocket: clustered broadleaf trees & pines inside the park lawn
                    ScatterNaturalRing(center, 38f, 84f, 4, new[] { "broadleaf_grove", "broadleaf_grove", "pine_cluster", "moss_rocks" }, assets, random);
                    break;

                case ZoneArchetype.ForestWoodland:
                    // Mixed conifer + broadleaf grove with mossy stones along the forest edge
                    ScatterNaturalRing(center, 46f, 96f, 4, new[] { "pine_cluster", "broadleaf_grove", "pine_cluster", "moss_rocks" }, assets, random);
                    break;

                case ZoneArchetype.ForestBoulderField:
                    // Granite boulders with scree moss rocks & flanking pines
                    foreach (var anchor in anchorPoints)
                        ScatterNaturalRing(anchor, 42f, 80f, 2, new[] { "moss_rocks", "moss_rocks", "pine_cluster" }, assets, random);
                    break;

                case ZoneArchetype.ForestSmallGrove:
                    ScatterNaturalRing(center, 34f, 72f, 3, new[] { "broadleaf_grove", "pine_cluster", "moss_rocks" }, assets, random);
                    break;
            }
        }

        private DestructibleProp ResolveNaturalSlot(ZoneArchetype archetype, BattlefieldCoverKind kind,
            BattleSpawnAssets assets, System.Random random)
        {
            return archetype switch
            {
                ZoneArchetype.DesertMesaOutcrop => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, "desert_mesa"),
                    BattlefieldCoverKind.Building => GetProp(assets, "desert_rock"),
                    _ => GetProp(assets, "desert_scrub")
                },
                ZoneArchetype.DesertPalmGrove => kind switch
                {
                    BattlefieldCoverKind.Landmark or BattlefieldCoverKind.Building => GetProp(assets, "palm_tree"),
                    _ => GetProp(assets, random.Next(2) == 0 ? "desert_scrub" : "desert_rock")
                },
                ZoneArchetype.DesertRockCluster => kind switch
                {
                    BattlefieldCoverKind.Landmark or BattlefieldCoverKind.Building => GetProp(assets, "desert_rock"),
                    _ => GetProp(assets, "desert_scrub")
                },
                ZoneArchetype.CityPark => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, "broadleaf_grove"),
                    BattlefieldCoverKind.Building => GetProp(assets, "pine_cluster"),
                    _ => GetProp(assets, "moss_rocks")
                },
                ZoneArchetype.ForestWoodland => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, "pine_cluster"),
                    BattlefieldCoverKind.Building => GetProp(assets, "broadleaf_grove"),
                    _ => GetProp(assets, "moss_rocks")
                },
                ZoneArchetype.ForestBoulderField => kind switch
                {
                    BattlefieldCoverKind.Landmark => GetProp(assets, "forest_boulder"),
                    BattlefieldCoverKind.Building => GetProp(assets, "moss_rocks"),
                    _ => GetProp(assets, "pine_cluster")
                },
                ZoneArchetype.ForestSmallGrove => kind switch
                {
                    BattlefieldCoverKind.Landmark or BattlefieldCoverKind.Building => GetProp(assets, "broadleaf_grove"),
                    _ => GetProp(assets, "moss_rocks")
                },
                _ => null
            };
        }

        private void PopulateRidgeFoothills(BattleMap map, List<(Vector2 Position, float Radius, float Angle)> ridgeAnchors,
            BattleSpawnAssets assets, System.Random random)
        {
            if (ridgeAnchors.Count == 0) return;
            string[] pool = map switch
            {
                BattleMap.Forest => new[] { "pine_cluster", "broadleaf_grove", "moss_rocks", "moss_rocks" },
                BattleMap.Desert => new[] { "desert_rock", "desert_scrub", "desert_rock", "palm_tree" },
                _ => System.Array.Empty<string>()
            };
            if (pool.Length == 0) return;

            foreach (var ridge in ridgeAnchors)
            {
                ScatterNaturalRing(ridge.Position, ridge.Radius + 18f, ridge.Radius + 58f, 3, pool, assets, random);
            }
        }

        private void ScatterNaturalRing(Vector2 anchor, float minRadius, float maxRadius, int count,
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
