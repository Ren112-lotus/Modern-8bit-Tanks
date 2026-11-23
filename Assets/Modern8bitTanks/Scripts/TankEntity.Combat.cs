using System;
using UnityEngine;

namespace Modern8bitTanks
{
    public enum ProjectileKind { Shell, Autocannon, ATGM, MG }

    [Flags]
    public enum TankModule
    {
        None = 0,
        LeftRunningGear = 1 << 0,
        RightRunningGear = 1 << 1,
        Engine = 1 << 2,
        Traverse = 1 << 3,
        Weapon = 1 << 4,
        Ammo = 1 << 5
    }

    public enum ArmorHit { Ricochet, Blocked, Penetrated }
    public enum TankLoss { None, Crew, Fire, Ammo }

    [Flags]
    public enum TankCrew
    {
        None = 0,
        Driver = 1 << 0,
        Gunner = 1 << 1,
        Commander = 1 << 2,
        Loader = 1 << 3
    }

    public static class TankDamageLayout
    {
        // Unit hull coordinates (-1..+1 in X=right, Y=forward) shared by the damage resolver and X-Ray cutaway.
        public static Vector2 Engine(bool front) => front ? new Vector2(0.34f, 0.56f) : new Vector2(0f, -0.68f);
        public static Vector2 Traverse(bool turretless) => turretless ? new Vector2(0f, 0.16f) : new Vector2(0f, 0.04f);
        public static Vector2 Weapon(bool turretless) => turretless ? new Vector2(0f, 0.34f) : new Vector2(0f, 0.36f);
        public static Vector2 Ammo(bool unmannedTurret, bool frontEngine)
        {
            if (unmannedTurret) return frontEngine ? new Vector2(0f, -0.44f) : new Vector2(0f, -0.34f);
            return frontEngine ? new Vector2(-0.30f, -0.46f) : new Vector2(0.36f, -0.38f);
        }

        public static Vector2 CrewSeat(int index, bool unmannedTurret, bool frontEngine)
        {
            if (unmannedTurret)
            {
                // Armored hull crew capsule (T-14 Armata, M1E3, BMPT, Stryker MGS, Puma, Boxer, M901)
                if (frontEngine)
                {
                    return index switch
                    {
                        0 => new Vector2(-0.34f, 0.50f), // Driver front-left beside engine
                        1 => new Vector2(-0.24f, 0.06f), // Gunner hull left-center
                        2 => new Vector2(0.24f, 0.06f),  // Commander hull right-center
                        _ => new Vector2(0f, -0.22f)
                    };
                }
                return index switch
                {
                    0 => new Vector2(-0.34f, 0.56f), // Driver front-left capsule
                    1 => new Vector2(0f, 0.56f),     // Gunner front-center capsule
                    2 => new Vector2(0.34f, 0.56f),  // Commander front-right capsule
                    _ => new Vector2(0f, 0.28f)
                };
            }

            // Classic manned turret layout
            return index switch
            {
                0 => frontEngine ? new Vector2(-0.36f, 0.56f) : new Vector2(-0.24f, 0.58f), // Driver
                1 => new Vector2(0.20f, 0.18f),  // Gunner
                2 => new Vector2(-0.26f, -0.10f),// Commander
                _ => new Vector2(0.26f, -0.18f)  // Loader
            };
        }

        // Legacy static accessors kept for compatibility
        public static readonly Vector2 Driver = new Vector2(-0.25f, 0.59f);
        public static readonly Vector2 Gunner = new Vector2(0.14f, 0.20f);
        public static readonly Vector2 Commander = new Vector2(-0.28f, -0.13f);
        public static readonly Vector2 Loader = new Vector2(0.28f, -0.22f);
    }

    public readonly struct TankShotTrace
    {
        public readonly Vector2 Entry;
        public readonly Vector2 Approach;
        public readonly Vector2 End;

        public TankShotTrace(Vector2 entry, Vector2 approach, Vector2 end)
        {
            Entry = entry;
            Approach = approach;
            End = end;
        }
    }

    public readonly struct ArmorContact
    {
        public readonly float Fraction;
        public readonly Vector2 GroundNormal;
        public readonly Vector2 LocalPoint;
        public readonly Vector2 LocalDirection;
        public readonly Vector2 LocalNormal;

        public ArmorContact(float fraction, Vector2 groundNormal, Vector2 localPoint, Vector2 localDirection, Vector2 localNormal)
        {
            Fraction = fraction;
            GroundNormal = groundNormal;
            LocalPoint = localPoint;
            LocalDirection = localDirection;
            LocalNormal = localNormal;
        }
    }

    public readonly struct TankHit
    {
        public readonly ArmorHit Armor;
        public readonly TankModule Modules;
        public readonly TankModule StruckModules;
        public readonly TankCrew KnockedOut;
        public readonly int CrewLost;
        public readonly bool Ignited;
        public readonly float PenetrationValue;
        public readonly float EffectiveArmor;
        public readonly float ImpactAngleDeg;
        public readonly AmmoType Ammo;

        public TankHit(ArmorHit armor, TankModule modules = TankModule.None, int crewLost = 0, bool ignited = false,
                       TankModule struckModules = TankModule.None, TankCrew knockedOut = TankCrew.None,
                       float penetrationValue = 0f, float effectiveArmor = 0f, float impactAngleDeg = 0f,
                       AmmoType ammo = AmmoType.APFSDS)
        {
            Armor = armor;
            Modules = modules;
            StruckModules = struckModules;
            KnockedOut = knockedOut;
            CrewLost = crewLost;
            Ignited = ignited;
            PenetrationValue = penetrationValue;
            EffectiveArmor = effectiveArmor;
            ImpactAngleDeg = impactAngleDeg;
            Ammo = ammo;
        }
    }

    public partial class TankEntity
    {
        private const float BurnDuration = 9f;
        private const TankModule RunningGear = TankModule.LeftRunningGear | TankModule.RightRunningGear;
        public TankCrew ActiveCrew { get; private set; }
        public bool FrontEngine => vehicleDef != null && vehicleDef.IsFrontEngine;
        public bool IsWheeled => vehicleDef != null && vehicleDef.IsWheeled;
        public int WheelAxleCount => vehicleDef != null ? vehicleDef.WheelAxleCount : 0;
        public bool UnmannedTurret => vehicleDef != null && vehicleDef.IsUnmannedTurret;
        public bool Turretless => vehicleDef != null && vehicleDef.IsTurretless;

        public TankModule DisabledModules { get; private set; }
        public TankLoss Loss { get; private set; }
        public float FireTime { get; private set; }
        public float RepairTime { get; private set; }
        public float ActiveReloadDuration { get; private set; }
        public bool ExtinguisherReady { get; private set; }

        internal void ApplyRemoteCombatState(MultiplayerMatchTransport.TankState state, bool applySpeed)
        {
            alive = state.Alive;
            if (applySpeed) moveSpeed = state.MoveSpeed;
            reloadTimer = state.Reload;
            FireTime = state.FireTime;
            RepairTime = state.RepairTime;
            ActiveReloadDuration = state.ActiveReload;
            ActiveCrew = (TankCrew)state.Crew;
            DisabledModules = (TankModule)state.Modules;
            selectedAmmoSlot = state.Ammo;
            Loss = (TankLoss)state.Loss;
            ExtinguisherReady = state.Extinguisher;
        }
        public int CrewCapacity => vehicleDef?.CrewCapacity ?? 4;
        public int CrewRemaining => (HasCrew(TankCrew.Driver) ? 1 : 0) + (HasCrew(TankCrew.Gunner) ? 1 : 0)
                                  + (HasCrew(TankCrew.Commander) ? 1 : 0) + (HasCrew(TankCrew.Loader) ? 1 : 0);
        public bool OnFire => FireTime > 0f;
        public bool Repairing => RepairTime > 0f;
        public bool CanFire => alive && !Repairing && !HasDisabled(TankModule.Weapon);
        public float DriveFactor => !alive || Repairing || HasDisabled(TankModule.Engine | RunningGear)
            ? 0f : HasCrew(TankCrew.Driver) ? 1f : 0.60f;
        public float TurnFactor => !alive || Repairing || HasDisabled(TankModule.Engine)
            ? 0f : (DisabledModules & RunningGear) switch
            {
                RunningGear => 0f,
                TankModule.LeftRunningGear or TankModule.RightRunningGear => 0.40f,
                _ => HasCrew(TankCrew.Driver) ? 1f : 0.60f
            };
        public float TraverseFactor => !alive || Repairing || HasDisabled(TankModule.Traverse)
            ? 0f : HasCrew(TankCrew.Gunner) ? 1f : 0.55f;

        private Vector2 CombatHalfSize => new Vector2(
            Mathf.Max(1f, meta != null ? meta.hullWidth * 0.5f : 11f),
            Mathf.Max(1f, meta != null ? meta.hullLength * 0.5f : 19f)) / Mathf.Max(1f, pixelsPerUnit);

        internal bool HullOverlapsCircle(Vector2 center, float radius, float squash)
        {
            Vector2 offset = center - Position2D;
            Vector2 halfSize = CombatHalfSize;
            float reach = radius + halfSize.magnitude;
            if (offset.sqrMagnitude > reach * reach) return false;

            Vector2 forward = SliceAngleToDir(hullAngleDeg);
            Vector2 right = new Vector2(-forward.y, forward.x);
            Vector2 groundOffset = SpriteStackCamera.Unsquash(offset, squash);
            if (Mathf.Abs(Vector2.Dot(groundOffset, right)) <= halfSize.x &&
                Mathf.Abs(Vector2.Dot(groundOffset, forward)) <= halfSize.y) return true;

            Vector2 width = SpriteStackCamera.Squash(right * halfSize.x, squash);
            Vector2 length = SpriteStackCamera.Squash(forward * halfSize.y, squash);
            float radiusSquared = radius * radius;
            return EdgeDistanceSquared(offset - width, length) <= radiusSquared
                || EdgeDistanceSquared(offset + width, length) <= radiusSquared
                || EdgeDistanceSquared(offset - length, width) <= radiusSquared
                || EdgeDistanceSquared(offset + length, width) <= radiusSquared;
        }

        private static float EdgeDistanceSquared(Vector2 offset, Vector2 halfEdge)
        {
            float along = Mathf.Clamp(Vector2.Dot(offset, halfEdge) / halfEdge.sqrMagnitude, -1f, 1f);
            return (offset - halfEdge * along).sqrMagnitude;
        }

        public bool HasDisabled(TankModule modules) => (DisabledModules & modules) != 0;
        private bool HasCrew(TankCrew crew) => (ActiveCrew & crew) != 0;

        private void ResetCombat()
        {
            ActiveCrew = TankCrew.Driver | TankCrew.Gunner | TankCrew.Commander;
            if (CrewCapacity == 4) ActiveCrew |= TankCrew.Loader;
            Loss = TankLoss.None;
            RestockAndRepair();
            ActiveReloadDuration = reloadDuration;
        }

        public void RestockAndRepair()
        {
            if (!alive) return;
            DisabledModules = TankModule.None;
            FireTime = 0f;
            RepairTime = 0f;
            ExtinguisherReady = true;
        }

        public void BeginReload()
        {
            bool loaderLost = CrewCapacity == 4 && !HasCrew(TankCrew.Loader);
            bool extraDuty = !HasCrew(TankCrew.Gunner) || !HasCrew(TankCrew.Driver);
            ActiveReloadDuration = reloadDuration * (loaderLost || extraDuty || HasDisabled(TankModule.Ammo) ? 1.65f : 1f);
            reloadTimer = ActiveReloadDuration;
        }

        public void RequestService()
        {
            if (!alive) return;
            if (OnFire)
            {
                if (ExtinguisherReady)
                {
                    ExtinguisherReady = false;
                    FireTime = 0f;
                }
                return;
            }

            if (Repairing) RepairTime = 0f;
            else if (DisabledModules != TankModule.None && Mathf.Abs(moveSpeed) <= 0.08f)
                RepairTime = Mathf.Lerp(3.5f, 6f, 1f - (CrewRemaining - 2f) / 2f);
        }

        public void TickCombat(float dt, bool autoService)
        {
            if (!alive) return;
            if (OnFire)
            {
                FireTime = Mathf.Max(0f, FireTime - dt);
                if (!OnFire) SetLoss(TankLoss.Fire);
                else if (autoService && ExtinguisherReady && FireTime <= BurnDuration - 1.25f)
                    RequestService();
                return;
            }

            if (autoService && !Repairing && DisabledModules != TankModule.None)
            {
                moveSpeed = 0f;
                RequestService();
            }
            if (!Repairing) return;
            RepairTime = Mathf.Max(0f, RepairTime - dt);
            if (!Repairing) DisabledModules = TankModule.None;
        }

        public bool TryGetArmorContact(Vector2 from, Vector2 to, float squash, out ArmorContact contact)
        {
            contact = default;
            Vector2 forward = SliceAngleToDir(hullAngleDeg);
            Vector2 right = new Vector2(-forward.y, forward.x);
            Vector2 offset = SpriteStackCamera.Unsquash(from - Position2D, squash);
            Vector2 delta = SpriteStackCamera.Unsquash(to - from, squash);
            if (delta.sqrMagnitude < 0.00000001f) return false;

            Vector2 start = new Vector2(Vector2.Dot(offset, right), Vector2.Dot(offset, forward));
            Vector2 step = new Vector2(Vector2.Dot(delta, right), Vector2.Dot(delta, forward));
            Vector2 half = CombatHalfSize;
            Vector2 normal = Vector2.zero;
            float entry = 0f, exit = 1f;
            if (!ClipAxis(start.x, step.x, half.x, Vector2.right, ref entry, ref exit, ref normal) ||
                !ClipAxis(start.y, step.y, half.y, Vector2.up, ref entry, ref exit, ref normal))
                return false;

            if (normal == Vector2.zero)
            {
                // The hull may have moved across the projectile since the previous step.
                normal = Mathf.Abs(step.x) > Mathf.Abs(step.y)
                    ? new Vector2(-Mathf.Sign(step.x), 0f) : new Vector2(0f, -Mathf.Sign(step.y));
            }
            Vector2 worldNormal = SpriteStackCamera.Unsquash(right * normal.x + forward * normal.y, squash);
            contact = new ArmorContact(entry, worldNormal.normalized, start + step * entry, step.normalized, normal);
            return true;
        }

        private static bool ClipAxis(float start, float step, float half, Vector2 axis, ref float entry, ref float exit, ref Vector2 normal)
        {
            if (Mathf.Abs(step) < 0.00001f) return Mathf.Abs(start) <= half;
            float a = (-half - start) / step;
            float b = (half - start) / step;
            float near = Mathf.Min(a, b);
            if (near >= entry)
            {
                entry = near;
                normal = -axis * Mathf.Sign(step);
            }
            exit = Mathf.Min(exit, Mathf.Max(a, b));
            return entry <= exit;
        }

        public Vector2 ToCombatDirection(Vector2 worldDirection, float squash)
        {
            worldDirection = SpriteStackCamera.Unsquash(worldDirection, squash);
            Vector2 forward = SliceAngleToDir(hullAngleDeg);
            Vector2 right = new Vector2(-forward.y, forward.x);
            Vector2 half = CombatHalfSize;
            return new Vector2(Vector2.Dot(worldDirection, right) / half.x,
                               Vector2.Dot(worldDirection, forward) / half.y).normalized;
        }

        public TankHit ApplyArmorHit(ArmorContact contact, ProjectileKind kind, float penetration)
            => ApplyArmorHit(contact, kind, penetration, out _, AmmoType.APFSDS);

        public TankHit ApplyArmorHit(ArmorContact contact, ProjectileKind kind, float penetration, out TankShotTrace trace, AmmoType ammo = AmmoType.APFSDS)
        {
            Vector2 half = CombatHalfSize;
            Vector2 entry = new Vector2(contact.LocalPoint.x / half.x, contact.LocalPoint.y / half.y);
            Vector2 direction = new Vector2(contact.LocalDirection.x / half.x, contact.LocalDirection.y / half.y).normalized;
            trace = new TankShotTrace(entry, direction, entry);
            if (!alive || kind == ProjectileKind.MG) return new TankHit(ArmorHit.Blocked, ammo: ammo);

            bool isChemical = AmmoInfo.IsChemical(ammo);
            bool isHe = AmmoInfo.IsExplosiveSplash(ammo);
            bool isAphe = ammo == AmmoType.APHE;

            float incidence = Mathf.Clamp01(-Vector2.Dot(contact.LocalNormal, contact.LocalDirection));
            float impactAngleDeg = Mathf.Acos(Mathf.Clamp(incidence, 0f, 1f)) * Mathf.Rad2Deg;
            bool front = contact.LocalNormal.y > 0f;
            float armorRating = vehicleDef != null && vehicleDef.armorFront > 0f ? vehicleDef.armorFront : 0.72f;
            float frontal = Mathf.Lerp(22f, 72f, Mathf.InverseLerp(0.91f, 0.60f, armorRating));
            float plate = frontal * (front ? 1f : contact.LocalNormal.y < 0f ? 0.46f : 0.58f);
            // Chemical shaped charges (HEAT-FS / TOW / GL-ATGM) & HE ignore most oblique armor sloping!
            float angleFactor = (isChemical || isHe) ? Mathf.Lerp(0.78f, 1f, incidence) : incidence;
            float effectiveArmor = plate / Mathf.Max(0.25f, angleFactor);

            // Shaped charges & HE fuse on contact instead of bouncing except at extreme grazing angles
            float ricochetLimit = isHe ? 0.08f : (isChemical ? 0.18f : (front ? 0.48f : 0.32f));
            if ((kind == ProjectileKind.Shell || isChemical) && !isHe && incidence < ricochetLimit)
                return new TankHit(ArmorHit.Ricochet, penetrationValue: penetration, effectiveArmor: effectiveArmor, impactAngleDeg: impactAngleDeg, ammo: ammo);

            float residual = penetration - effectiveArmor;

            // Running gear sits outside the armored compartment.
            TankModule modules = TankModule.None;
            if (Mathf.Abs(entry.x) > 0.72f && Mathf.Abs(entry.y) < 0.92f && (incidence > 0.36f || isHe))
                modules = entry.x < 0f ? TankModule.LeftRunningGear : TankModule.RightRunningGear;

            // High-Explosive (HE / HEI-T) Overpressure & Surface Blast!
            if (isHe)
            {
                bool bigHe = ammo == AmmoType.HE;
                // Overpressure punches straight into light/thin armor (IFVs, ATGM carriers, scout cars)
                if (bigHe && (armorRating >= 0.78f || residual > 0f))
                {
                    trace = new TankShotTrace(entry, direction, entry + direction * 0.55f);
                    TankModule heMods = modules | TankModule.Weapon | TankModule.Engine;
                    TankCrew heCrew = TankCrew.Driver | TankCrew.Gunner;
                    return ApplyCombatDamage(ArmorHit.Penetrated, heMods, heCrew, ignite: true, cookoff: armorRating >= 0.84f,
                        penetration, effectiveArmor, impactAngleDeg, ammo);
                }
                // Against heavy MBTs, HE blast strips tracks/gun barrel & can start engine fires without piercing!
                if (bigHe)
                {
                    if (front && entry.y > 0.15f) modules |= TankModule.Weapon;
                    else if (entry.y < -0.25f) modules |= TankModule.Engine;
                    else modules |= (entry.x < 0f ? TankModule.LeftRunningGear : TankModule.RightRunningGear);
                }
                else if (residual <= 0f && Mathf.Abs(entry.x) > 0.55f)
                {
                    modules |= (entry.x < 0f ? TankModule.LeftRunningGear : TankModule.RightRunningGear);
                }
                if (residual <= 0f)
                    return ApplyCombatDamage(ArmorHit.Blocked, modules, ignite: bigHe && (modules & TankModule.Engine) != 0,
                        penetrationValue: penetration, effectiveArmor: effectiveArmor, impactAngleDeg: impactAngleDeg, ammo: ammo);
            }

            if (residual <= 0f)
                return ApplyCombatDamage(ArmorHit.Blocked, modules, penetrationValue: penetration, effectiveArmor: effectiveArmor, impactAngleDeg: impactAngleDeg, ammo: ammo);

            // Sweep a finite path through normalized gameplay compartments.
            float reach = isAphe
                ? Mathf.Lerp(0.32f, 0.92f, Mathf.Clamp01(residual / 50f)) // APHE penetrates then detonates INSIDE!
                : Mathf.Lerp(0.22f, 2.05f, Mathf.Clamp01(residual / 66f));
            Vector2 path = direction * reach;
            Vector2 detonatePt = entry + path;
            trace = new TankShotTrace(entry, direction, detonatePt);
            TankModule current = DisabledModules;

            float modBonus = isAphe ? 0.14f : (isChemical ? 0.06f : 0f);
            if (Crosses(entry, path, TankDamageLayout.Engine(FrontEngine), (FrontEngine ? 0.28f : 0.40f) + modBonus)) modules |= TankModule.Engine;
            if (Crosses(entry, path, TankDamageLayout.Traverse(Turretless), 0.29f + modBonus)) modules |= TankModule.Traverse;
            if (Crosses(entry, path, TankDamageLayout.Weapon(Turretless), 0.24f + modBonus)) modules |= TankModule.Weapon;
            if (Crosses(entry, path, TankDamageLayout.Ammo(UnmannedTurret, FrontEngine), 0.27f + modBonus)) modules |= TankModule.Ammo;

            bool heavy = kind == ProjectileKind.Shell || kind == ProjectileKind.ATGM;
            float spread = isAphe ? 0.46f : (isChemical ? 0.30f : (heavy ? 0.26f : 0.16f));
            TankCrew crew = TankCrew.None;
            if (Crosses(entry, path, TankDamageLayout.CrewSeat(0, UnmannedTurret, FrontEngine), spread)) crew |= TankCrew.Driver;
            if (Crosses(entry, path, TankDamageLayout.CrewSeat(1, UnmannedTurret, FrontEngine), spread)) crew |= TankCrew.Gunner;
            if (Crosses(entry, path, TankDamageLayout.CrewSeat(2, UnmannedTurret, FrontEngine), spread)) crew |= TankCrew.Commander;
            if (CrewCapacity == 4 && Crosses(entry, path, TankDamageLayout.CrewSeat(3, UnmannedTurret, FrontEngine), spread)) crew |= TankCrew.Loader;

            float cookoffThresh = isChemical ? 24f : (isAphe ? 22f : 38f);
            float igniteThresh = isChemical ? 12f : (isAphe ? 16f : 21f);
            bool cookoff = (modules & TankModule.Ammo) != 0 && (residual >= cookoffThresh || (current & TankModule.Ammo) != 0);
            bool ignite = (modules & TankModule.Engine) != 0 && residual >= igniteThresh;
            return ApplyCombatDamage(ArmorHit.Penetrated, modules, crew, ignite, cookoff, penetration, effectiveArmor, impactAngleDeg, ammo);
        }

        public TankHit ApplyBlast(Vector2 origin, float squash, bool fuel, bool close)
        {
            Vector2 offset = origin - Position2D;
            offset.y /= Mathf.Max(0.1f, squash);
            Vector2 fwd = SliceAngleToDir(hullAngleDeg);
            Vector2 right = new Vector2(-fwd.y, fwd.x);
            TankModule side = Vector2.Dot(right, offset) < 0f ? TankModule.LeftRunningGear : TankModule.RightRunningGear;
            bool engineHit = fuel && close;
            return ApplyCombatDamage(ArmorHit.Blocked, side | (engineHit ? TankModule.Engine : TankModule.None), ignite: engineHit);
        }

        private static bool Crosses(Vector2 entry, Vector2 path, Vector2 center, float radius)
        {
            float t = Mathf.Clamp01(Vector2.Dot(center - entry, path) / path.sqrMagnitude);
            return (entry + path * t - center).sqrMagnitude <= radius * radius;
        }

        private TankHit ApplyCombatDamage(ArmorHit armor, TankModule modules, TankCrew crew = TankCrew.None, bool ignite = false, bool cookoff = false,
                                          float penetrationValue = 0f, float effectiveArmor = 0f, float impactAngleDeg = 0f,
                                          AmmoType ammo = AmmoType.APFSDS)
        {
            int before = CrewRemaining;
            TankModule changed = modules & ~DisabledModules;
            DisabledModules |= modules;
            TankCrew knockedOut = ActiveCrew & crew;
            ActiveCrew &= ~crew;
            bool startedFire = ignite && !OnFire;
            if (startedFire)
            {
                FireTime = BurnDuration;
                RepairTime = 0f;
            }
            if (DriveFactor <= 0f) moveSpeed = 0f;
            if (cookoff) SetLoss(TankLoss.Ammo);
            else if (CrewRemaining < 2) SetLoss(TankLoss.Crew);
            return new TankHit(armor, changed, before - CrewRemaining, startedFire, modules, knockedOut,
                               penetrationValue, effectiveArmor, impactAngleDeg, ammo);
        }

        private void SetLoss(TankLoss loss)
        {
            Loss = loss;
            alive = false;
            FireTime = 0f;
            RepairTime = 0f;
            moveSpeed = 0f;
            HullTurnInput = 0f;
            if (hullStack != null) hullStack.charred = true;
            if (turretStack != null) turretStack.charred = true;
        }
    }
}
