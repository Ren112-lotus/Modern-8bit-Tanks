using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Modern8bitTanks
{
    /// <summary>
    /// Catalog-driven tank entity with:
    /// - Independent Hull & Turret SpriteStackRenderers
    /// - Hydraulic gun recoil + torsion-bar suspension dip
    /// - Smoke Grenade cooldown
    /// - Ballistic Turret Toss physics on kill/cook-off
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(SortingGroup))]
    public partial class TankEntity : MonoBehaviour
    {
        [Header("Tank Configuration (Prefab)")]
        public string tankType = "abrams"; // Legacy prefab tag or VehicleDef.id
        public string factionTeam = "ally"; // "ally" (player side) or "enemy" (hostile side)
        internal string OnlinePlayerName { get; set; }
        public VehicleDef vehicleDef;
        public TankMetaEntry meta;
        public SpriteStackRenderer hullStack;
        public SpriteStackRenderer turretStack;
        public float pixelsPerUnit = 16f;
        public float initialAngleDeg = 90f;
        public float reloadDuration = 1.15f;
        public float smokeDuration = 9.0f;
        public float atgmDuration = 3.5f;

        [Header("Mobility & Traverse (deg/sec)")]
        public float hullTurnSpeed = 145f;
        public float turretTurnSpeed = 240f;

        [Header("Runtime State")]
        public bool isPlayer;
        public bool isExtraSpawned; // True for tanks instantiated for the current battle or wave
        public float hullAngleDeg;
        public float turretAngleDeg;
        public float moveSpeed;
        public float recoilPx;
        public float suspensionOffset = 1f;
        private float _suspensionVel;
        public float reloadTimer;
        public float mgCooldown;
        public float atgmCooldown;
        public float smokeCooldown;
        public int twinBarrelToggle;
        public int selectedAmmoSlot;
        public bool alive = true;

        public AmmoType[] AmmoLoadout => vehicleDef != null
            ? vehicleDef.GetAmmoLoadout()
            : new[] { AmmoType.APFSDS, AmmoType.HEATFS, AmmoType.HE };

        public AmmoType CurrentAmmo
        {
            get
            {
                var arr = AmmoLoadout;
                int idx = Mathf.Clamp(selectedAmmoSlot, 0, arr.Length - 1);
                return arr[idx];
            }
        }
        public float treadAccum;
        public float dustTimer;
        public float smokeTimer;

        // Telemetry for audio & feedback
        public bool JustFinishedReloading { get; private set; }
        public float TurretTurnRateDeg { get; private set; }
        public float HullTurnRateDeg { get; private set; }
        public float HullTurnInput { get; set; }
        private bool _wasReloading;
        private float _prevTurretAngle;
        private float _prevHullAngle;

        // Turret Toss state
        public bool turretTossed;
        private Vector2 _turretFlyPos;
        private Vector2 _turretFlyVel;
        private float _turretHeightSlices;
        private float _turretHeightVel;
        private float _turretSpinVel;
        private bool _turretBounced;
        private bool _turretSettled;
        private float _turretSmokeTimer;

        // AI memory
        [HideInInspector] public float aiStrafeDir = 1f;
        [HideInInspector] public float aiStrafeTimer = 2f;
        [HideInInspector] public float aiFireDelay = 0.45f;
        [HideInInspector] public SortingGroup sortingGroup;

        private Vector3 _savedSpawnPos;
        private bool _spawnCaptured;

        private void OnEnable()
        {
            sortingGroup = GetComponent<SortingGroup>();
            if (hullStack == null || turretStack == null)
            {
                var stacks = GetComponentsInChildren<SpriteStackRenderer>(true);
                foreach (var s in stacks)
                {
                    if (s.name.Contains("Hull")) hullStack = s;
                    else if (s.name.Contains("Turret")) turretStack = s;
                }
            }

            if (!Application.isPlaying)
            {
                hullAngleDeg = initialAngleDeg;
                turretAngleDeg = initialAngleDeg;
                SyncStackPose();
            }
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                hullAngleDeg = initialAngleDeg;
                turretAngleDeg = initialAngleDeg;
                SyncStackPose();
            }
        }

        private void SyncStackPose()
        {
            if (hullStack == null || turretStack == null || meta == null) return;
            hullStack.angleDeg = hullAngleDeg;
            turretStack.angleDeg = turretAngleDeg;
            turretStack.baseZOffsetSlices = meta.hullBaseHeight;

            Vector2 hd = SliceAngleToDir(hullAngleDeg);
            float squash = hullStack.squash;
            Vector2 pivOffPx = hd * meta.hullPivotOffsetY;
            turretStack.transform.localPosition = new Vector3(
                pivOffPx.x / Mathf.Max(1f, pixelsPerUnit),
                (pivOffPx.y * squash) / Mathf.Max(1f, pixelsPerUnit),
                0f
            );

            if (sortingGroup != null)
                sortingGroup.sortingOrder = Mathf.Clamp(Mathf.RoundToInt(-transform.position.y * 100f), -10000, 10000);
        }

        /// <summary>
        /// Configures and spawns this tank with the selected catalog vehicle.
        /// </summary>
        public bool TrySpawnAsVehicle(VehicleDef def, Texture2D hullSheet, Texture2D turretSheet,
            Vector2 position, float angleDeg, Material material = null)
        {
            if (def == null || hullStack == null || turretStack == null ||
                !CanRenderSheet(hullSheet, def.hullSlices) || !CanRenderSheet(turretSheet, def.turretSlices))
            {
                Debug.LogError($"Cannot spawn vehicle '{def?.id}': missing tank stacks or invalid slice textures.", this);
                return false;
            }

            vehicleDef = def;
            tankType = def.id;
            meta = def.ToTankMeta();
            // Gently boost micro-tankettes (< 31px long, e.g. Wiesel / MS-1 / ASU-57) so their pixels & 1px gun barrels
            // never crush on 480x270 while preserving unmistakable relative size next to heavy MBTs!
            float hullLenPx = def.hullLength > 0 ? def.hullLength : 38f;
            float microBoost = hullLenPx < 31f ? Mathf.Clamp(31f / Mathf.Max(18f, hullLenPx), 1.0f, 1.18f) : 1.0f;
            pixelsPerUnit = 16f / microBoost;

            Material stackMaterial = material != null ? material : hullStack.sliceMaterial;
            hullStack.SetStackSource(hullSheet, meta.hullSlices, pixelsPerUnit, stackMaterial, 0, 0f, groundShadow: true);
            turretStack.SetStackSource(turretSheet, meta.turretSlices, pixelsPerUnit, stackMaterial,
                meta.hullSlices * 2 + 4, meta.hullBaseHeight, groundShadow: false);

            CaptureSpawnAndReset(position, angleDeg);
            return true;
        }

        private static bool CanRenderSheet(Texture2D sheet, int slices)
        {
            return sheet != null && sheet.isReadable && slices > 0 && sheet.width % slices == 0;
        }

        public void CaptureSpawnAndReset(Vector2? overridePos = null, float? overrideAngle = null)
        {
            if (!_spawnCaptured)
            {
                _savedSpawnPos = transform.position;
                _spawnCaptured = true;
            }

            sortingGroup = GetComponent<SortingGroup>();
            transform.position = overridePos.HasValue
                ? new Vector3(overridePos.Value.x, overridePos.Value.y, 0f)
                : _savedSpawnPos;

            if (vehicleDef != null)
            {
                reloadDuration = vehicleDef.reload;
                hullTurnSpeed = vehicleDef.EffectiveHullTurnSpeed;
                turretTurnSpeed = vehicleDef.EffectiveTurretTurnSpeed;
            }

            float ang = overrideAngle ?? initialAngleDeg;
            hullAngleDeg = ang;
            turretAngleDeg = ang;
            _prevTurretAngle = ang;
            _prevHullAngle = ang;
            HullTurnRateDeg = 0f;
            TurretTurnRateDeg = 0f;
            moveSpeed = 0f;
            recoilPx = 0f;
            suspensionOffset = 1f;
            _suspensionVel = 0f;
            reloadTimer = 0f;
            _wasReloading = false;
            JustFinishedReloading = false;
            mgCooldown = 0f;
            atgmCooldown = 0f;
            smokeCooldown = 0f;
            twinBarrelToggle = 0;
            selectedAmmoSlot = 0;
            alive = true;
            ResetCombat();
            treadAccum = 0f;
            dustTimer = 0f;
            smokeTimer = 0f;
            HullTurnInput = 0f;

            // Reset Turret Toss
            turretTossed = false;
            _turretFlyPos = Vector2.zero;
            _turretFlyVel = Vector2.zero;
            _turretHeightSlices = meta != null ? meta.hullBaseHeight : 10f;
            _turretHeightVel = 0f;
            _turretSpinVel = 0f;
            _turretBounced = false;
            _turretSettled = false;

            aiStrafeDir = UnityEngine.Random.value < 0.5f ? 1f : -1f;
            aiStrafeTimer = 2f;
            aiFireDelay = 0.45f;

            SyncStackPose();
            hullStack?.ResetDamageVisuals();
            turretStack?.ResetDamageVisuals();
        }

        public Vector2 Position2D
        {
            get => new Vector2(transform.position.x, transform.position.y);
            set => transform.position = new Vector3(value.x, value.y, 0f);
        }

        public static Vector2 SliceAngleToDir(float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad));
        }

        public static float TargetDeltaToSliceAngle(Vector2 delta, float squash)
        {
            Vector2 u = SpriteStackCamera.Unsquash(delta, squash);
            return Mathf.Atan2(u.x, -u.y) * Mathf.Rad2Deg;
        }

        public Vector2 GetTurretPivotWorld(float squash)
        {
            Vector2 hd = SliceAngleToDir(hullAngleDeg);
            float off = meta.hullPivotOffsetY / pixelsPerUnit;
            return Position2D + SpriteStackCamera.Squash(hd * off, squash);
        }

        public void GetMuzzlePositions(float squash, float spacingPx, out Vector2 visualMuzzle, out Vector2 groundMuzzle, float lateralOffsetPx = 0f)
        {
            Vector2 piv = GetTurretPivotWorld(squash);
            Vector2 td = SliceAngleToDir(turretAngleDeg);
            Vector2 perp = new Vector2(-td.y, td.x);
            float dist = (meta.muzzleDist - recoilPx) / pixelsPerUnit;
            float lat = lateralOffsetPx / pixelsPerUnit;
            Vector2 off = td * dist + perp * lat;
            groundMuzzle = piv + SpriteStackCamera.Squash(off, squash);
            int muzzleHeight = meta.muzzleHeight > 0 ? meta.muzzleHeight : 2;
            float liftY = ((meta.hullBaseHeight + muzzleHeight) * spacingPx) / pixelsPerUnit;
            visualMuzzle = groundMuzzle + SpriteStackCamera.CamUp * liftY;
        }

        public void ShowPenetration(Vector2 impactGroundPos, bool heavy, Action<Vector2, Color> onDebris)
        {
            if (hullStack != null)
                hullStack.CrumbleAtWorldPoint(impactGroundPos, heavy ? 3.2f : 1.5f, zStart: 3, zEnd: -1, onDebris);
            if (turretStack != null) turretStack.hitFlash = 0.8f;
        }

        public void TriggerFireKick(float recoilAmountPx = 3.8f)
        {
            recoilPx = recoilAmountPx;
            _suspensionVel = -2.4f;
        }

        /// <summary>
        /// Detaches the turret stack and launches it spinning through the air in a ballistic arc!
        /// </summary>
        public void TriggerTurretToss()
        {
            if (turretTossed || turretStack == null) return;
            turretTossed = true;
            _turretBounced = false;
            _turretSettled = false;

            Vector2 hd = SliceAngleToDir(hullAngleDeg);
            _turretFlyPos = SpriteStackCamera.Squash(hd * (meta.hullPivotOffsetY / pixelsPerUnit), hullStack.squash);
            float throwAng = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float throwSpd = UnityEngine.Random.Range(1.4f, 2.8f);
            _turretFlyVel = new Vector2(Mathf.Cos(throwAng) * throwSpd, Mathf.Sin(throwAng) * throwSpd * 0.7f);
            _turretHeightSlices = meta.hullBaseHeight;
            _turretHeightVel = UnityEngine.Random.Range(42f, 58f); // High dramatic loft in slice-pixels/sec!
            _turretSpinVel = (UnityEngine.Random.value < 0.5f ? 1f : -1f) * UnityEngine.Random.Range(240f, 420f);
        }

        public void TriggerArmorRicochetFlash()
        {
            hullStack.hitFlash = 0.65f;
            turretStack.hitFlash = 0.65f;
        }

        /// <summary>
        /// Sequences Sprite-Stacking slices slowly descending from overhead:
        /// hull slices assemble first (0.00..0.68), then turret slices glide down onto the turret ring (0.32..1.00)!
        /// </summary>
        public void SetAssemblyDrop(float progress01, float dropHeightPx = 42f)
        {
            float p = Mathf.Clamp01(progress01);
            if (hullStack != null)
            {
                hullStack.dropHeightPx = dropHeightPx;
                hullStack.dropProgress = p >= 0.999f ? 1f : Mathf.Clamp01(p / 0.68f);
            }
            if (turretStack != null)
            {
                turretStack.dropHeightPx = dropHeightPx * 1.12f;
                turretStack.dropProgress = p >= 0.999f ? 1f : Mathf.Clamp01((p - 0.30f) / 0.70f);
            }
        }

        public void SyncVisuals(
            float squash,
            float spacingPx,
            Action<Vector2> onTurretBounce = null,
            Action<Vector2> onTurretTrail = null)
        {
            float dt = Time.deltaTime;

            // Reload timer + exact completion trigger for metallic breech-lock SFX
            JustFinishedReloading = false;
            if (reloadTimer > 0f)
            {
                reloadTimer = Mathf.Max(0f, reloadTimer - dt);
                if (reloadTimer <= 0f && _wasReloading)
                {
                    JustFinishedReloading = true;
                    _wasReloading = false;
                }
            }
            if (reloadTimer > 0f) _wasReloading = true;

            mgCooldown = Mathf.Max(0f, mgCooldown - dt);
            atgmCooldown = Mathf.Max(0f, atgmCooldown - dt);
            smokeCooldown = Mathf.Max(0f, smokeCooldown - dt);

            // Track hull & turret turn velocities for dust FX and servo audio
            HullTurnRateDeg = dt > 0.0001f ? Mathf.DeltaAngle(_prevHullAngle, hullAngleDeg) / dt : 0f;
            _prevHullAngle = hullAngleDeg;
            TurretTurnRateDeg = dt > 0.0001f ? Mathf.Abs(Mathf.DeltaAngle(_prevTurretAngle, turretAngleDeg)) / dt : 0f;
            _prevTurretAngle = turretAngleDeg;

            // Hydraulic gun recoil return
            recoilPx = Mathf.Lerp(recoilPx, 0f, 1f - Mathf.Exp(-7.5f * dt));
            if (recoilPx < 0.03f) recoilPx = 0f;

            // Damped torsion-bar suspension spring
            float springForce = (1f - suspensionOffset) * 140f - _suspensionVel * 14f;
            _suspensionVel += springForce * dt;
            suspensionOffset = Mathf.Clamp(suspensionOffset + _suspensionVel * dt, 0.84f, 1.08f);

            hullStack.hitFlash = Mathf.Max(0f, hullStack.hitFlash - dt * 4.5f);
            turretStack.hitFlash = Mathf.Max(0f, turretStack.hitFlash - dt * 4.5f);

            float effectiveSpacing = spacingPx * suspensionOffset;
            hullStack.squash = squash;
            hullStack.spacingPx = effectiveSpacing;
            hullStack.angleDeg = hullAngleDeg;
            hullStack.metallicWear = 1.0f;
            hullStack.ApplyVisuals();

            turretStack.squash = squash;
            turretStack.spacingPx = turretTossed ? spacingPx : effectiveSpacing;
            turretStack.metallicWear = 1.0f;

            if (turretTossed)
            {
                if (!_turretSettled)
                {
                    _turretFlyPos += _turretFlyVel * dt;
                    _turretHeightVel -= 105f * dt; // Gravity in slice-pixels/s^2
                    _turretHeightSlices += _turretHeightVel * dt;
                    turretAngleDeg = Mathf.Repeat(turretAngleDeg + _turretSpinVel * dt, 360f);

                    _turretSmokeTimer -= dt;
                    if (_turretSmokeTimer <= 0f && onTurretTrail != null)
                    {
                        _turretSmokeTimer = 0.045f;
                        Vector2 worldT = Position2D + _turretFlyPos + SpriteStackCamera.CamUp * ((_turretHeightSlices * spacingPx) / pixelsPerUnit);
                        onTurretTrail(worldT);
                    }

                    if (_turretHeightSlices <= 0f)
                    {
                        _turretHeightSlices = 0f;
                        if (!_turretBounced)
                        {
                            _turretBounced = true;
                            _turretHeightVel = 18f;
                            _turretFlyVel *= 0.45f;
                            _turretSpinVel *= 0.42f;
                            onTurretBounce?.Invoke(Position2D + _turretFlyPos);
                        }
                        else
                        {
                            _turretSettled = true;
                            _turretHeightVel = 0f;
                            _turretFlyVel = Vector2.zero;
                            _turretSpinVel = 0f;
                        }
                    }
                }
                turretStack.baseZOffsetSlices = _turretHeightSlices;
                turretStack.transform.localPosition = new Vector3(_turretFlyPos.x, _turretFlyPos.y, -0.05f);
            }
            else
            {
                turretStack.baseZOffsetSlices = meta.hullBaseHeight;
                Vector2 hd = SliceAngleToDir(hullAngleDeg);
                Vector2 td = SliceAngleToDir(turretAngleDeg);
                Vector2 pivOff = SpriteStackCamera.Squash((hd * meta.hullPivotOffsetY - td * recoilPx) / pixelsPerUnit, squash);
                turretStack.transform.localPosition = new Vector3(pivOff.x, pivOff.y, 0f);
            }

            turretStack.angleDeg = turretAngleDeg;
            turretStack.ApplyVisuals();

            sortingGroup.sortingOrder = SpriteStackCamera.DepthOrder(Position2D);

        }
    }
}
