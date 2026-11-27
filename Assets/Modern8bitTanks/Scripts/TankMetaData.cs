using System;
using UnityEngine;

namespace Modern8bitTanks
{
    public enum AmmoType
    {
        APFSDS,   // High-velocity fin-stabilized sabot dart (max kinetic penetration)
        APHE,     // Armor-piercing high-explosive (detonates inside hull with 360-deg shrapnel!)
        HEATFS,   // Angle-insensitive shaped-charge jet (high fire & ammo-cookoff lethality!)
        HE,       // High-explosive overpressure blast (huge crater, wrecks cover & light armor!)
        TOW,      // SACLOS Mouse-Guided Anti-Tank Guided Missile!
        GLATGM,   // Barrel-launched SACLOS Mouse-Guided ATGM (Refleks / Shillelagh!)
        APBelt,   // Autocannon AP-T high-velocity belt
        HEIBelt,  // Autocannon HEI-T explosive/incendiary belt
        MG        // Coaxial machine gun
    }

    public static class AmmoInfo
    {
        public static string ShortLabel(AmmoType a) => a switch
        {
            AmmoType.APFSDS => "APFS",
            AmmoType.APHE => "APHE",
            AmmoType.HEATFS => "HEAT",
            AmmoType.HE => "HE",
            AmmoType.TOW => "TOW",
            AmmoType.GLATGM => "ATGM",
            AmmoType.APBelt => "AP-T",
            AmmoType.HEIBelt => "HEI",
            _ => "MG"
        };

        public static string FullLabel(AmmoType a) => a switch
        {
            AmmoType.APFSDS => "APFSDS",
            AmmoType.APHE => "APHE",
            AmmoType.HEATFS => "HEAT-FS",
            AmmoType.HE => "HE",
            AmmoType.TOW => "TOW",
            AmmoType.GLATGM => "GL-ATGM",
            AmmoType.APBelt => "AP-T",
            AmmoType.HEIBelt => "HEI-T",
            _ => "MG"
        };

        public static bool IsGuided(AmmoType a) => a == AmmoType.TOW || a == AmmoType.GLATGM;
        public static bool IsChemical(AmmoType a) => a == AmmoType.HEATFS || a == AmmoType.TOW || a == AmmoType.GLATGM;
        public static bool IsExplosiveSplash(AmmoType a) => a == AmmoType.HE || a == AmmoType.HEIBelt;

        internal static float Penetration(ProjectileKind kind, AmmoType ammo, VehicleDef vehicle, bool secondaryAtgm = false)
        {
            switch (kind)
            {
                case ProjectileKind.Shell:
                    float multiplier = ammo switch
                    {
                        AmmoType.APFSDS => 1.08f,
                        AmmoType.APHE => 0.96f,
                        AmmoType.HEATFS => 1.02f,
                        AmmoType.HE => 0.36f,
                        _ => 1f
                    };
                    return (vehicle?.penetration ?? 80f) * multiplier;
                case ProjectileKind.Autocannon:
                    return (vehicle?.penetration ?? 36f) * (ammo == AmmoType.HEIBelt ? 0.56f : 1.06f);
                case ProjectileKind.ATGM:
                    float basePen = secondaryAtgm ? 82f + Mathf.Min(vehicle?.tier ?? 8, 10) : vehicle?.penetration ?? 88f;
                    return basePen * (ammo == AmmoType.HE ? 0.42f : 1.08f);
                default:
                    return 0f;
            }
        }
    }

    [Serializable]
    public class TankMetaEntry
    {
        public string name;
        public int hullSlices;
        public int hullBaseHeight;
        public int hullWidth;
        public int hullLength;
        public float hullPivotOffsetY;
        public int turretSlices;
        public int turretWidth;
        public int turretLength;
        public int muzzleDist;
        public int muzzleHeight; // Height above the turret base in slices. Zero uses the default.
    }

    [Serializable]
    public class PropMetaEntry
    {
        public int slices;
        public int width;
        public int length;
        public bool destructible;
        public bool ricochetHeavy;
        public bool heavyStructure;
        public bool fixedCover;
        public bool roundedFootprint;
    }

    /// <summary>
    /// Simple wrappers because Unity's JsonUtility does not parse root dictionary keys directly.
    /// </summary>
    [Serializable]
    public class TanksMetaFile
    {
        public TankMetaEntry abrams;
        public TankMetaEntry t90;
    }

    [Serializable]
    public class PropsMetaFile
    {
        public PropMetaEntry sandbag_wall;
        public PropMetaEntry hesco_barrier;
        public PropMetaEntry desert_ruin;
        public PropMetaEntry supply_crate;
        public PropMetaEntry desert_bunker;
        public PropMetaEntry watch_tower;
        public PropMetaEntry fuel_tank;
        public PropMetaEntry palm_tree;
        public PropMetaEntry desert_rock;
        public PropMetaEntry tank_trap;
        // Large biome structures & monumental cover
        public PropMetaEntry city_apartment;
        public PropMetaEntry city_factory;
        public PropMetaEntry city_blockhouse;
        public PropMetaEntry city_barricade;
        public PropMetaEntry pine_cluster;
        public PropMetaEntry forest_boulder;
        public PropMetaEntry forest_cabin;
        public PropMetaEntry desert_mesa;
        public PropMetaEntry desert_compound;
        // Additional artificial & natural props
        public PropMetaEntry cargo_container;
        public PropMetaEntry radar_station;
        public PropMetaEntry timber_stack;
        public PropMetaEntry desert_scrub;
        public PropMetaEntry broadleaf_grove;
        public PropMetaEntry moss_rocks;

        public PropMetaEntry GetByKind(string kind)
        {
            return kind switch
            {
                "sandbag_wall" => sandbag_wall,
                "hesco_barrier" => hesco_barrier,
                "desert_ruin" => desert_ruin,
                "supply_crate" => supply_crate,
                "desert_bunker" => desert_bunker,
                "watch_tower" => watch_tower,
                "fuel_tank" => fuel_tank,
                "palm_tree" => palm_tree,
                "desert_rock" => desert_rock,
                "tank_trap" => tank_trap,
                "city_apartment" => city_apartment,
                "city_factory" => city_factory,
                "city_blockhouse" => city_blockhouse,
                "city_barricade" => city_barricade,
                "pine_cluster" => pine_cluster,
                "forest_boulder" => forest_boulder,
                "forest_cabin" => forest_cabin,
                "desert_mesa" => desert_mesa,
                "desert_compound" => desert_compound,
                "cargo_container" => cargo_container,
                "radar_station" => radar_station,
                "timber_stack" => timber_stack,
                "desert_scrub" => desert_scrub,
                "broadleaf_grove" => broadleaf_grove,
                "moss_rocks" => moss_rocks,
                _ => desert_rock
            };
        }
    }

    [Serializable]
    public class VehicleDef
    {
        public string id;
        public string name;
        public string nation; // "USA", "GER", "USSR", "JPN"
        public const int LowestTier = -5;
        public const int ModernTier = 9;
        public const int LastRegularTier = 13;
        public const int SpecialTier = LastRegularTier + 1;
        public int tier;      // Historical matchmaking brackets through Special.
        public string tab;    // "T1_5", "MBT", "LIGHT", "SUPPORT", "HISTORIC"
        public string role;   // "MBT", "LIGHT", "SUPPORT", "SPECIAL", "HEAVY"
        public string weapon; // "MainCannon", "AutocannonATGM", "ATGM", "TwinAutocannonATGM"
        public string engine; // "Turbine", "Diesel", "Gasoline"
        public int year;
        public int historyColumn; // 1-based pre-Tier I position; zero for the numbered tree.
        public int historyRow; // 0 main, 1 upper, 2 lower.
        public string treeFrom;
        public int fixedGunTraverse;
        public int wheelAxles;
        public bool frontEngine;
        public float speed;
        public float hullTurnSpeed;   // deg/sec hull traverse rate
        public float turretTurnSpeed; // deg/sec turret / gun traverse rate
        public float cameraHeightPixels; // Base vertical battle view in world pixels.
        public int cameraWidthPixels; // Legacy catalog value at 16:9.
        public float penetration; // Gameplay rating shared with armor, in arbitrary units.
        public int crewCount;
        public float reload;
        public float armorFront;
        public int hullSlices;
        public int hullBaseHeight;
        public int hullWidth;
        public int hullLength;
        public float hullPivotOffsetY;
        public int turretSlices;
        public int turretWidth;
        public int turretLength;
        public int muzzleDist;
        public int muzzleHeight;

        public bool IsSpecial => tier >= SpecialTier || role == "SPECIAL";
        public string TierBadge => historyColumn > 0 ? $"H{tier + 6}" : (IsSpecial ? "SP" : $"T{tier}");
        public string TierRoman => historyColumn > 0 ? TierBadge : RomanTier(tier);

        public static string RomanTier(int value) => value switch
        {
            1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V",
            6 => "VI", 7 => "VII", 8 => "VIII", 9 => "IX", 10 => "X",
            11 => "XI", 12 => "XII", 13 => "XIII",
            _ => "SP"
        };

        public string NationLabel => nation switch
        {
            "GER" => "GERMANY",
            "USSR" => "USSR / RU",
            "JPN" => "JAPAN",
            _ => "USA"
        };

        public string WeaponLabel => weapon switch
        {
            "AutocannonATGM" => "AC+ATGM",
            "TwinAutocannonATGM" => "2xAC+ATGM",
            "ATGM" => "ATGM",
            _ => "CANNON"
        };

        public int CrewCapacity => crewCount == 3 ? 3 : 4;

        public bool HasSecondaryAtgm => weapon == "AutocannonATGM" || weapon == "TwinAutocannonATGM";

        public float EffectiveHullTurnSpeed => hullTurnSpeed > 0f ? hullTurnSpeed : ComputeDefaultHullTurnSpeed();
        public float EffectiveTurretTurnSpeed => turretTurnSpeed > 0f ? turretTurnSpeed : ComputeDefaultTurretTurnSpeed();

        internal const float DefaultCameraHeightPixels = 245.25f;

        public float EffectiveCameraHeightPixels
        {
            get
            {
                if (cameraHeightPixels > 0f) return cameraHeightPixels;
                return cameraWidthPixels > 0 ? cameraWidthPixels * (9f / 16f) : DefaultCameraHeightPixels;
            }
        }

        private float ComputeDefaultHullTurnSpeed()
        {
            float baseTurn = role switch
            {
                "LIGHT" => 176f,
                "HEAVY" => 88f,
                "TD" => 118f,
                "SUPPORT" => 152f,
                "SPECIAL" => 170f,
                "MEDIUM" => 116f,
                _ => 140f
            };
            float spdAdj = (speed - 55f) * 1.15f;
            float eraAdj = historyColumn > 0 ? -18f : (tier >= ModernTier ? 14f : 0f);
            float wheelPen = IsWheeled ? -24f : 0f;
            return Mathf.Clamp(Mathf.Round(baseTurn + spdAdj + eraAdj + wheelPen), 56f, 215f);
        }

        private float ComputeDefaultTurretTurnSpeed()
        {
            if (IsTurretless)
                return Mathf.Clamp(Mathf.Round(115f + (tier >= ModernTier ? 28f : 0f)), 95f, 155f);

            float baseTur = role switch
            {
                "LIGHT" => 260f,
                "HEAVY" => 110f,
                "TD" => 140f,
                "SUPPORT" => 275f,
                "SPECIAL" => 315f,
                "MEDIUM" => 148f,
                _ => 210f
            };
            if (weapon == "AutocannonATGM" || weapon == "TwinAutocannonATGM") baseTur += 42f;
            float eraAdj = historyColumn > 0 ? -32f : (tier >= ModernTier ? 65f : (tier >= 5 ? 18f : -12f));
            return Mathf.Clamp(Mathf.Round(baseTur + eraAdj), 85f, 360f);
        }

        public bool IsWheeled => WheelAxleCount > 0;

        public int WheelAxleCount => wheelAxles > 0 ? wheelAxles : id switch
        {
            "ru_9p148" => 2,
            "us_m1128" or "de_luchs" or "de_boxer" => 4,
            _ => 0
        };

        public bool IsFrontEngine => frontEngine || id == "us_m10_booker" || id == "us_m901" ||
                                     id == "us_m2" || id == "us_m2a2" || id == "us_m3a3" || id == "us_m2a4" ||
                                     id == "us_m1128" || id == "de_wiesel_mk" || id == "de_wiesel_tow" ||
                                     id == "de_marder" || id == "de_puma" || id == "de_kf41_lynx" ||
                                     id == "de_boxer" || id == "ru_bmp1" || id == "ru_bmp2" || id == "ru_9p149";

        public bool IsUnmannedTurret => id == "us_m1e3" || id == "us_m1128" || id == "us_m901" ||
                                        id == "de_puma" || id == "de_boxer" || id == "ru_t14" || id == "ru_bmpt";

        public bool IsTurretless => fixedGunTraverse > 0 || id == "de_rakjpz2" || id == "de_jaguar2" || id == "de_jaguar1" ||
                                    id == "de_wiesel_tow" || id == "ru_9p148" || id == "ru_9p149";

        public bool HasGunLaunchedAtgm => id == "ru_t64b" || id == "ru_t72b3" || id == "ru_t80bvm" ||
                                          id == "ru_t90a" || id == "ru_t90m" || id == "ru_t14" ||
                                          id == "ru_2s25m" || id == "us_m551" || id == "us_m60a2" ||
                                          id == "us_m1e3" || id == "de_kf51";

        public AmmoType[] GetAmmoLoadout()
        {
            if (weapon == "AutocannonATGM" || weapon == "TwinAutocannonATGM")
                return new[] { AmmoType.APBelt, AmmoType.HEIBelt };
            if (weapon == "ATGM")
                return new[] { AmmoType.TOW, AmmoType.HE };
            if (HasGunLaunchedAtgm)
                return tier >= ModernTier
                    ? new[] { AmmoType.APFSDS, AmmoType.HEATFS, AmmoType.GLATGM }
                    : new[] { AmmoType.HEATFS, AmmoType.HE, AmmoType.GLATGM };
            if (historyColumn > 0)
                return new[] { AmmoType.APHE, AmmoType.HE };
            return tier >= ModernTier
                ? new[] { AmmoType.APFSDS, AmmoType.HEATFS, AmmoType.HE }
                : new[] { AmmoType.APHE, AmmoType.HEATFS, AmmoType.HE };
        }

        public TankMetaEntry ToTankMeta()
        {
            return new TankMetaEntry
            {
                name = id,
                hullSlices = hullSlices,
                hullBaseHeight = hullBaseHeight,
                hullWidth = hullWidth,
                hullLength = hullLength,
                hullPivotOffsetY = hullPivotOffsetY,
                turretSlices = turretSlices,
                turretWidth = turretWidth,
                turretLength = turretLength,
                muzzleDist = muzzleDist,
                muzzleHeight = muzzleHeight
            };
        }
    }

    [Serializable]
    public class VehicleCatalog
    {
        public VehicleDef[] vehicles;
    }

    public static class MetaLoader
    {
        public static VehicleCatalog LoadVehicleCatalog()
        {
            var ta = Resources.Load<TextAsset>("Data/vehicles_catalog");
            if (ta != null)
            {
                var parsed = JsonUtility.FromJson<VehicleCatalog>(ta.text);
                if (parsed != null && parsed.vehicles != null && parsed.vehicles.Length > 0)
                    return parsed;
            }
            return new VehicleCatalog { vehicles = Array.Empty<VehicleDef>() };
        }

        public static TanksMetaFile LoadTanksMeta()
        {
            var ta = Resources.Load<TextAsset>("Data/tanks_meta");
            if (ta != null)
            {
                var parsed = JsonUtility.FromJson<TanksMetaFile>(ta.text);
                if (parsed != null && parsed.abrams != null && parsed.t90 != null)
                    return parsed;
            }
            // Safe fallback defaults matching build_tanks.py
            return new TanksMetaFile
            {
                abrams = new TankMetaEntry
                {
                    name = "abrams", hullSlices = 11, hullBaseHeight = 11,
                    hullWidth = 24, hullLength = 40, hullPivotOffsetY = 0.5f,
                    turretSlices = 7, turretWidth = 25, turretLength = 57, muzzleDist = 27
                },
                t90 = new TankMetaEntry
                {
                    name = "t90", hullSlices = 13, hullBaseHeight = 10,
                    hullWidth = 26, hullLength = 43, hullPivotOffsetY = 2.0f,
                    turretSlices = 6, turretWidth = 27, turretLength = 63, muzzleDist = 30
                }
            };
        }

        public static PropsMetaFile LoadPropsMeta()
        {
            var ta = Resources.Load<TextAsset>("Data/props_meta");
            if (ta != null)
            {
                var parsed = JsonUtility.FromJson<PropsMetaFile>(ta.text);
                if (parsed != null && parsed.sandbag_wall != null)
                    return parsed;
            }
            return new PropsMetaFile
            {
                sandbag_wall = new PropMetaEntry { slices = 10, width = 32, length = 14, destructible = true },
                hesco_barrier = new PropMetaEntry { slices = 13, width = 22, length = 22, destructible = true, ricochetHeavy = true },
                desert_ruin = new PropMetaEntry { slices = 14, width = 36, length = 14, destructible = true },
                supply_crate = new PropMetaEntry { slices = 11, width = 18, length = 16, destructible = true },
                desert_bunker = new PropMetaEntry { slices = 14, width = 34, length = 26, destructible = true, ricochetHeavy = true },
                watch_tower = new PropMetaEntry { slices = 22, width = 22, length = 22, destructible = true },
                fuel_tank = new PropMetaEntry { slices = 13, width = 30, length = 18, destructible = true },
                palm_tree = new PropMetaEntry { slices = 20, width = 28, length = 28, destructible = true },
                desert_rock = new PropMetaEntry { slices = 15, width = 32, length = 24, destructible = true, ricochetHeavy = true },
                tank_trap = new PropMetaEntry { slices = 12, width = 18, length = 18, destructible = true, ricochetHeavy = true },
                cargo_container = new PropMetaEntry { slices = 14, width = 38, length = 18, destructible = true, ricochetHeavy = true },
                radar_station = new PropMetaEntry { slices = 22, width = 24, length = 24, destructible = true },
                timber_stack = new PropMetaEntry { slices = 12, width = 34, length = 18, destructible = true },
                desert_scrub = new PropMetaEntry { slices = 12, width = 22, length = 22, destructible = true, roundedFootprint = true },
                broadleaf_grove = new PropMetaEntry { slices = 22, width = 44, length = 42, destructible = true, heavyStructure = true, roundedFootprint = true },
                moss_rocks = new PropMetaEntry { slices = 13, width = 28, length = 24, destructible = true, ricochetHeavy = true, roundedFootprint = true }
            };
        }
    }
}
