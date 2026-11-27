using System.Collections.Generic;
using UnityEngine;

namespace Modern8bitTanks
{
    public partial class BattleSceneController
    {
        private readonly List<ProjectileData> _projectiles = new();

        private class ProjectileData
        {
            public ushort networkId;
            public ProjectileKind kind;
            public AmmoType ammo;
            public string ownerFaction; // "ally" or "enemy"
            public TankEntity owner;
            public bool playerOwned;
            public Vector2 visualPos;
            public Vector2 groundPos;
            public Vector2 vel;
            public float speed;
            public float penetration;
            public int armorBouncesLeft;
            public float life;
            public float age;
            public GameObject go;
            public SpriteRenderer sr;

            public bool IsHeavy => kind == ProjectileKind.Shell || kind == ProjectileKind.ATGM;
            public bool IsPrimary => kind != ProjectileKind.MG;
            public bool IsGuided => AmmoInfo.IsGuided(ammo);
        }

        private void FirePrimaryWeapon(TankEntity t)
        {
            if (!t.CanFire || _phase != GamePhase.Playing) return;
            string w = t.vehicleDef != null ? t.vehicleDef.weapon : "MainCannon";
            switch (w)
            {
                case "AutocannonATGM":
                    FireAutocannon(t, twinBarrel: false);
                    break;
                case "TwinAutocannonATGM":
                    FireAutocannon(t, twinBarrel: true);
                    break;
                case "ATGM":
                    FireAtgmMissile(t, asSecondary: false);
                    break;
                default:
                    FireMainCannon(t);
                    break;
            }
        }

        private void FireSecondaryWeapon(TankEntity t)
        {
            if (!t.CanFire || _phase != GamePhase.Playing) return;
            if (t.vehicleDef != null && t.vehicleDef.HasSecondaryAtgm)
                FireAtgmMissile(t, asSecondary: true);
            else
                FireMG(t);
        }

        private void FireMainCannon(TankEntity t)
        {
            if (t.reloadTimer > 0f) return;

            AmmoType ammo = t.CurrentAmmo;
            if (ammo == AmmoType.GLATGM)
            {
                FireAtgmMissile(t, asSecondary: false, ammoOverride: AmmoType.GLATGM);
                return;
            }

            t.BeginReload();
            t.TriggerFireKick(ammo == AmmoType.HE ? 4.4f : 3.8f);

            t.GetMuzzlePositions(squash, spacingPx, out Vector2 visMuz, out Vector2 gndMuz);
            Vector2 td = TankEntity.SliceAngleToDir(t.turretAngleDeg);
            Vector2 dir = SpriteStackCamera.Squash(td, squash).normalized;
            t.Position2D -= dir * (0.8f / PPU);

            float speedPx = ammo switch
            {
                AmmoType.APFSDS => 305f,
                AmmoType.APHE => 255f,
                AmmoType.HEATFS => 222f,
                AmmoType.HE => 195f,
                _ => 265f
            };
            float pen = AmmoInfo.Penetration(ProjectileKind.Shell, ammo, t.vehicleDef);
            int bounces = (ammo == AmmoType.APFSDS || ammo == AmmoType.APHE) ? 1 : 0;
            SpawnProjectile(ProjectileKind.Shell, ammo, t, visMuz, gndMuz, dir * (speedPx / PPU), pen, armorBounces: bounces, life: 4.0f);

        }

        private void PlayMainCannonEffects(TankEntity t, Vector2 visMuz, Vector2 gndMuz, Vector2 dir)
        {
            Vector2 perp = new Vector2(-dir.y, dir.x);

            // 1. Crisp HDR muzzle flare (blooms naturally via URP Post-Process!)
            SpawnParticle(visMuz + dir * 0.14f, dir * 1.2f, 0f, 0.055f, 0.34f, new Color(1f, 0.98f, 0.78f), 3.6f);
            SpawnParticle(visMuz + dir * 0.34f, dir * 2.4f, 0f, 0.045f, 0.22f, new Color(1f, 0.72f, 0.25f), 2.8f);

            // 2. Side-venting muzzle blast jets
            for (int side = -1; side <= 1; side += 2)
            {
                for (int k = 0; k < 2; k++)
                {
                    Vector2 ventDir = (perp * side * 0.92f + dir * (0.25f - k * 0.15f)).normalized;
                    SpawnParticle(
                        visMuz,
                        ventDir * Random.Range(2.2f, 3.8f),
                        0f,
                        Random.Range(0.08f, 0.16f),
                        k == 0 ? 0.18f : 0.13f,
                        k == 0 ? new Color(1f, 0.85f, 0.45f) : new Color(0.84f, 0.78f, 0.68f),
                        k == 0 ? 2.2f : 1.0f
                    );
                }
            }

            // 3. Dust kicked up beneath the gun
            for (int i = 0; i < 6; i++)
            {
                float ang = Random.Range(-1.35f, 1.35f);
                Vector2 dustDir = Rotate2D(dir, ang);
                Color dustColor = i % 2 == 0 ? new Color(0.86f, 0.74f, 0.50f, 0.85f) : new Color(0.75f, 0.62f, 0.40f, 0.75f);
                SpawnParticle(gndMuz + Random.insideUnitCircle * 0.22f, dustDir * Random.Range(1.0f, 2.6f), 0.15f, Random.Range(0.28f, 0.55f), Random.Range(0.16f, 0.26f), dustColor, 1.0f);
            }

            // 4. Lingering muzzle gunsmoke
            for (int i = 0; i < 3; i++)
            {
                SpawnParticle(
                    visMuz + dir * (0.08f * i),
                    dir * Random.Range(0.35f, 0.95f) + new Vector2(Random.Range(-0.25f, 0.25f), Random.Range(0.25f, 0.55f)),
                    0.2f,
                    Random.Range(0.45f, 0.80f),
                    Random.Range(0.14f, 0.22f),
                    new Color(0.56f, 0.54f, 0.50f, 0.7f),
                    1.0f
                );
            }

            if (t.isPlayer) _camKickVel -= dir * 4.8f;
            if (_postProcess != null) _postProcess.PulseCannonFire(t.isPlayer);
            AudioClip cannonClip = (t.vehicleDef != null && t.vehicleDef.nation == "USSR") ? _sfxCannonT90 : _sfxCannonAbrams;
            float maxCannonDur = cannonClip == _sfxCannonT90 ? 1.55f : 0f;
            PlaySFXAt(cannonClip, gndMuz, SoundBus.Reverb, t.isPlayer ? 0.80f : 0.72f, maxDuration: maxCannonDur, maxConcurrentForClip: 3);
        }

        private void FireAutocannon(TankEntity t, bool twinBarrel)
        {
            if (t.reloadTimer > 0f) return;
            t.BeginReload();
            t.TriggerFireKick(1.6f);

            float lateralPx = 0f;
            if (twinBarrel)
            {
                t.twinBarrelToggle = 1 - t.twinBarrelToggle;
                lateralPx = t.twinBarrelToggle == 0 ? -2.2f : 2.2f;
            }

            t.GetMuzzlePositions(squash, spacingPx, out Vector2 visMuz, out Vector2 gndMuz, lateralPx);
            Vector2 td = TankEntity.SliceAngleToDir(t.turretAngleDeg);
            Vector2 baseDir = SpriteStackCamera.Squash(td, squash).normalized;
            Vector2 dir = Rotate2D(baseDir, Random.Range(-0.025f, 0.025f));

            AmmoType ammo = t.CurrentAmmo == AmmoType.HEIBelt ? AmmoType.HEIBelt : AmmoType.APBelt;
            float speedPx = ammo == AmmoType.HEIBelt ? 268f : 308f;
            float penetration = AmmoInfo.Penetration(ProjectileKind.Autocannon, ammo, t.vehicleDef);
            SpawnProjectile(ProjectileKind.Autocannon, ammo, t, visMuz, gndMuz, dir * (speedPx / PPU), penetration, armorBounces: 0, life: 2.5f);

        }

        private void PlayAutocannonEffects(TankEntity t, Vector2 visMuz, Vector2 gndMuz, Vector2 dir)
        {
            SpawnParticle(visMuz + dir * 0.12f, dir * 1.4f, 0f, 0.045f, 0.22f, new Color(1f, 0.94f, 0.56f), 3.1f);
            SpawnParticle(gndMuz, dir * 0.9f + Random.insideUnitCircle * 0.4f, 0.12f, 0.22f, 0.28f, new Color(0.76f, 0.68f, 0.52f, 0.55f), 1.0f, _smokePuffSprite);

            if (t.isPlayer) _camKickVel -= dir * 1.45f;
            // Crisp high-tempo autocannon bark (pitched-up cannon + dry punch)
            PlaySFXAt(_sfxCannonAbrams, gndMuz, SoundBus.Dry, t.isPlayer ? 0.48f : 0.40f, centerPan: false, pitchMin: 1.52f, pitchMax: 1.72f);
        }

        private void FireAtgmMissile(TankEntity t, bool asSecondary, AmmoType? ammoOverride = null)
        {
            if (asSecondary)
            {
                if (t.atgmCooldown > 0f) return;
                t.atgmCooldown = t.atgmDuration;
            }
            else
            {
                if (t.reloadTimer > 0f) return;
                t.BeginReload();
            }

            AmmoType ammo = ammoOverride ?? (asSecondary ? AmmoType.TOW : t.CurrentAmmo);
            if (!asSecondary && ammo != AmmoType.TOW && ammo != AmmoType.GLATGM && ammo != AmmoType.HE)
                ammo = AmmoType.TOW;

            t.TriggerFireKick(ammo == AmmoType.GLATGM ? 3.2f : 2.0f);
            float sideOff = asSecondary ? 5.5f : 0f;
            t.GetMuzzlePositions(squash, spacingPx, out Vector2 visMuz, out Vector2 gndMuz, sideOff);
            Vector2 td = TankEntity.SliceAngleToDir(t.turretAngleDeg);
            Vector2 dir = SpriteStackCamera.Squash(td, squash).normalized;

            float penetration = AmmoInfo.Penetration(ProjectileKind.ATGM, ammo, t.vehicleDef, asSecondary);
            float speedPx = ammo == AmmoType.GLATGM ? 232f : (ammo == AmmoType.HE ? 195f : 206f);
            SpawnProjectile(ProjectileKind.ATGM, ammo, t, visMuz, gndMuz, dir * (speedPx / PPU), penetration, armorBounces: 0, life: 5.5f);

        }

        private void PlayAtgmEffects(TankEntity t, AmmoType ammo, Vector2 visMuz, Vector2 gndMuz, Vector2 dir)
        {
            // Rocket backblast smoke & fiery ignition plume
            for (int i = 0; i < 8; i++)
            {
                Vector2 back = Rotate2D(-dir, Random.Range(-0.55f, 0.55f));
                SpawnParticle(
                    visMuz - dir * 0.25f,
                    back * Random.Range(1.4f, 3.6f),
                    0.15f,
                    Random.Range(0.28f, 0.58f),
                    i < 3 ? Random.Range(0.22f, 0.34f) : Random.Range(0.42f, 0.68f),
                    i < 3 ? new Color(1f, 0.72f, 0.24f) : new Color(0.88f, 0.86f, 0.82f, 0.72f),
                    i < 3 ? 4.8f : 1.0f,
                    i < 3 ? _whitePxSprite : _smokePuffSprite
                );
            }

            if (t.isPlayer) _camKickVel -= dir * 2.2f;
            if (_postProcess != null) _postProcess.PulseCannonFire(t.isPlayer);
            // Crisp missile launch ignition (trimmed & concurrency-capped so 6s/2.7s clips never stack into harsh noise!)
            bool heavyLaunch = ammo == AmmoType.GLATGM || ammo == AmmoType.HE;
            AudioClip launchClip = heavyLaunch ? _sfxMissileHeavy : _sfxMissileLaunch;
            float launchVol = heavyLaunch ? (t.isPlayer ? 0.72f : 0.56f) : (t.isPlayer ? 0.78f : 0.62f);
            float launchMaxDur = heavyLaunch ? 1.10f : 0.95f;
            int maxVoices = heavyLaunch ? 1 : 2;
            PlaySFXAt(
                launchClip,
                gndMuz,
                SoundBus.Reverb,
                launchVol,
                centerPan: false,
                pitchMin: 0.95f,
                pitchMax: 1.05f,
                maxDuration: launchMaxDur,
                maxConcurrentForClip: maxVoices,
                minRetriggerSec: 0.10f
            );
        }

        private void FireMG(TankEntity t)
        {
            if (t.mgCooldown > 0f) return;
            t.mgCooldown = 0.11f;

            Vector2 piv = t.GetTurretPivotWorld(squash);
            Vector2 td = TankEntity.SliceAngleToDir(t.turretAngleDeg);
            Vector2 baseDir = SpriteStackCamera.Squash(td, squash).normalized;
            Vector2 dir = Rotate2D(baseDir, Random.Range(-0.06f, 0.06f));

            float dist = 12f / PPU;
            Vector2 gndPos = piv + SpriteStackCamera.Squash(td * dist, squash);
            Vector2 visPos = gndPos + SpriteStackCamera.CamUp * (((t.meta.hullBaseHeight + 2f) * spacingPx) / PPU);

            SpawnProjectile(ProjectileKind.MG, AmmoType.MG, t, visPos, gndPos, dir * (310f / PPU), 0f, armorBounces: 0, life: 0.95f);
        }

        private void PlayMgEffects(TankEntity t, Vector2 visPos, Vector2 gndPos, Vector2 dir)
        {
            SpawnParticle(visPos, dir * 1.5f, 0f, 0.06f, 0.13f, new Color(1f, 0.95f, 0.62f), 2.8f);
            // Coaxial MG stays crisp & dry (no reverb muddiness on rapid bursts!)
            PlaySFXAt(_sfxMg, gndPos, SoundBus.Dry, t.isPlayer ? 0.28f : 0.24f);
        }

        private void SpawnProjectile(ProjectileKind kind, AmmoType ammo, TankEntity owner, Vector2 visPos, Vector2 gndPos, Vector2 vel, float penetration, int armorBounces, float life)
        {
            Vector2 pivot = owner.GetTurretPivotWorld(squash);
            if (UsesControlPoints && FindPropHit(pivot, gndPos, out float muzzleHit) != null)
            {
                Vector2 stop = Vector2.Lerp(pivot, gndPos, muzzleHit) - vel.normalized * 0.025f;
                visPos += stop - gndPos;
                gndPos = stop;
            }
            var sr = CreateEffect($"{kind}_{ammo}");
            var go = sr.gameObject;
            go.transform.position = new Vector3(visPos.x, visPos.y, -2f);
            sr.sprite = _whitePxSprite;
            SetProjectileAppearance(sr, ammo);
            go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg);
            if (_multiplayer.IsInBattle)
                _multiplayer.Transport?.BroadcastWeaponFired(System.Array.IndexOf(_networkTanks, owner),
                    kind, ammo, visPos, gndPos, vel.normalized);

            _projectiles.Add(new ProjectileData
            {
                networkId = ++_nextProjectileId,
                kind = kind,
                ammo = ammo,
                ownerFaction = owner.factionTeam,
                owner = owner,
                playerOwned = owner.isPlayer,
                visualPos = visPos,
                groundPos = gndPos,
                vel = vel,
                speed = vel.magnitude,
                penetration = penetration,
                armorBouncesLeft = armorBounces,
                life = life,
                age = 0f,
                go = go,
                sr = sr
            });
            PresentWeaponEffects(owner, kind, ammo, visPos, gndPos, vel.normalized);
        }

        private void SetProjectileAppearance(SpriteRenderer sr, AmmoType ammo)
        {
            sr.color = ammo switch
            {
                AmmoType.TOW => new Color(1f, 0.38f, 0.14f),
                AmmoType.GLATGM => new Color(0.55f, 0.95f, 1f),
                AmmoType.HEATFS => new Color(1f, 0.56f, 0.18f),
                AmmoType.HE => new Color(1f, 0.68f, 0.16f),
                AmmoType.APHE => new Color(1f, 0.84f, 0.36f),
                AmmoType.APFSDS => new Color(1f, 0.96f, 0.72f),
                AmmoType.HEIBelt => new Color(1f, 0.52f, 0.16f),
                AmmoType.APBelt => new Color(1f, 0.88f, 0.44f),
                _ => new Color(1f, 0.85f, 0.42f)
            };
            sr.sortingOrder = 15000;
            if (_emissiveFxMat != null)
            {
                sr.sharedMaterial = _emissiveFxMat;
                float hdr = ammo switch
                {
                    AmmoType.TOW => 12.5f,
                    AmmoType.GLATGM => 12.0f,
                    AmmoType.HEATFS => 11.0f,
                    AmmoType.HE => 10.5f,
                    AmmoType.APFSDS => 10.0f,
                    AmmoType.APHE => 9.5f,
                    AmmoType.HEIBelt => 8.8f,
                    AmmoType.APBelt => 8.0f,
                    _ => 6.2f
                };
                _fxMpb.SetFloat(EmissionIntensityId, hdr);
                sr.SetPropertyBlock(_fxMpb);
            }
            sr.transform.localScale = ammo switch
            {
                AmmoType.TOW or AmmoType.GLATGM => new Vector3(0.46f, 0.17f, 1f),
                AmmoType.HE => new Vector3(0.36f, 0.18f, 1f),
                AmmoType.HEATFS or AmmoType.APFSDS or AmmoType.APHE => new Vector3(0.34f, 0.15f, 1f),
                AmmoType.APBelt or AmmoType.HEIBelt => new Vector3(0.24f, 0.11f, 1f),
                _ => new Vector3(0.16f, 0.09f, 1f)
            };
        }

        private void UpdateProjectiles(float dt)
        {
            Vector2 listenerPos = GetAudioListenerPos();
            bool hasGuidedMissile = false;
            Vector2 closestMissilePos = listenerPos;
            float closestMissileDistSq = float.MaxValue;

            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                var p = _projectiles[i];
                p.life -= dt;
                p.age += dt;
                bool remove = p.life <= 0f;

                // SACLOS Mouse-Guided TOW / GL-ATGM Missile Steering!
                if (!remove && p.IsGuided && p.age >= 0.07f && p.speed > 0.1f)
                {
                    SteerGuidedProjectile(p, dt);

                    // Hand off smoothly to the single spatialized in-flight motor loop after the initial ignition burst
                    if (p.age >= 0.25f)
                    {
                        float dSq = (p.groundPos - listenerPos).sqrMagnitude;
                        if (dSq < closestMissileDistSq)
                        {
                            closestMissileDistSq = dSq;
                            closestMissilePos = p.groundPos;
                            hasGuidedMissile = true;
                        }
                    }
                }

                int subSteps = p.IsHeavy ? 4 : 3;
                float subDt = dt / subSteps;

                for (int s = 0; s < subSteps && !remove; s++)
                {
                    Vector2 nextGnd = p.groundPos + p.vel * subDt;

                    if (nextGnd.x <= BorderPad || nextGnd.x >= WorldW - BorderPad ||
                        nextGnd.y <= BorderPad || nextGnd.y >= WorldH - BorderPad)
                    {
                        if (p.IsPrimary)
                            EmitExplosion(p.visualPos, p.groundPos, p.ammo == AmmoType.HE, SoundBus.Reverb,
                                p.IsHeavy ? (p.ammo == AmmoType.HE ? 0.78f : 0.56f) : 0f);
                        remove = true;
                        break;
                    }

                    DestructibleProp hitProp = FindPropHit(p.groundPos, nextGnd, out float propFraction);
                    TankEntity hitTank = FindProjectileTankHit(p, nextGnd, out ArmorContact contact);

                    if (hitProp != null && (hitTank == null || propFraction <= contact.Fraction))
                    {
                        Vector2 impact = Vector2.Lerp(p.groundPos, nextGnd, propFraction);
                        ResolveProjectilePropImpact(p, hitProp, impact);
                        remove = true;
                        break;
                    }

                    if (hitTank != null)
                    {
                        Vector2 impact = Vector2.Lerp(p.groundPos, nextGnd, contact.Fraction);
                        remove = ResolveProjectileTankImpact(p, hitTank, contact, impact);
                        continue;
                    }

                    p.groundPos = nextGnd;
                    p.visualPos += p.vel * subDt;
                }

                if (remove)
                {
                    if (p.go != null) Destroy(p.go);
                    _projectiles.RemoveAt(i);
                }
                else
                {
                    p.go.transform.position = new Vector3(p.visualPos.x, p.visualPos.y, -2f);
                    PlayProjectileTrail(p.kind, p.ammo, p.visualPos, p.vel.normalized);
                }
            }

            _audio?.UpdateMissileMotorLoop(hasGuidedMissile && _phase == GamePhase.Playing, closestMissilePos, listenerPos, sfxEnabled, dt, sfxVolume);
        }

        private void SteerGuidedProjectile(ProjectileData p, float dt)
        {
            Vector2 aimTarget = Vector2.zero;
            bool hasTarget = false;
            float turnRateDeg = 220f;

            if (p.playerOwned && _phase == GamePhase.Playing)
            {
                aimTarget = GetMouseWorld2D();
                hasTarget = true;
                turnRateDeg = p.ammo == AmmoType.TOW ? 225f : 185f;
            }
            else if (!p.playerOwned && _multiplayer.IsInBattle && _phase == GamePhase.Playing)
            {
                int slot = _networkTanks == null ? -1 : System.Array.IndexOf(_networkTanks, p.owner);
                if (_multiplayer.Transport != null && _multiplayer.Transport.TryGetControl(slot, out var control))
                {
                    aimTarget = new Vector2(control.AimX, control.AimY);
                    hasTarget = true;
                    turnRateDeg = p.ammo == AmmoType.TOW ? 225f : 185f;
                }
            }
            else if (!p.playerOwned && _phase == GamePhase.Playing)
            {
                // AI missile gently tracks nearest opposing tank (dodgeable behind buildings/smoke!)
                float bestD = float.MaxValue;
                for (int ti = 0; ti < _tanks.Count; ti++)
                {
                    var cand = _tanks[ti];
                    if (cand == null || !cand.alive || cand.factionTeam == p.ownerFaction) continue;
                    float d = (cand.Position2D - p.groundPos).sqrMagnitude;
                    if (d < bestD) { bestD = d; aimTarget = cand.Position2D; hasTarget = true; }
                }
                turnRateDeg = 72f;
            }

            if (hasTarget)
            {
                Vector2 toGoal = aimTarget - p.groundPos;
                if (toGoal.sqrMagnitude > 0.64f)
                {
                    float curAng = Mathf.Atan2(p.vel.y, p.vel.x) * Mathf.Rad2Deg;
                    float wantAng = Mathf.Atan2(toGoal.y, toGoal.x) * Mathf.Rad2Deg;
                    float deltaAng = Mathf.DeltaAngle(curAng, wantAng);
                    // True SACLOS forward-cone guidance: once the missile passes the cursor
                    // (cursor is >68 deg off the nose / behind it), NEVER U-turn back — fly straight ahead!
                    if (Mathf.Abs(deltaAng) <= 68f)
                    {
                        float wobble = p.ammo == AmmoType.TOW ? Mathf.Sin(p.age * 21f) * 3.8f : Mathf.Sin(p.age * 16f) * 2.0f;
                        float newAng = Mathf.MoveTowardsAngle(curAng, wantAng + wobble, turnRateDeg * dt);
                        float rad = newAng * Mathf.Deg2Rad;
                        p.vel = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * p.speed;
                        if (p.go != null) p.go.transform.rotation = Quaternion.Euler(0f, 0f, newAng);
                    }
                }
            }
        }

        private TankEntity FindProjectileTankHit(ProjectileData p, Vector2 nextGnd, out ArmorContact contact)
        {
            TankEntity hitTank = null;
            contact = default;
            if (_phase == GamePhase.Playing)
            {
                foreach (var target in _tanks)
                {
                    if (target == null || !target.alive || target.factionTeam == p.ownerFaction) continue;
                    if (!target.TryGetArmorContact(p.groundPos, nextGnd, squash, out ArmorContact candidate)) continue;
                    if (hitTank != null && candidate.Fraction >= contact.Fraction) continue;
                    hitTank = target;
                    contact = candidate;
                }
            }

            return hitTank;
        }

        private void ResolveProjectilePropImpact(ProjectileData p, DestructibleProp hitProp, Vector2 impact)
        {
            Vector2 visualImpact = p.visualPos + impact - p.groundPos;
            float heMul = p.ammo == AmmoType.HE ? 1.85f : (p.ammo == AmmoType.HEIBelt ? 1.55f : 1.0f);
            float craterR = (p.IsHeavy
                ? (hitProp.meta.ricochetHeavy ? 4.2f : 5.6f)
                : (p.kind == ProjectileKind.Autocannon ? 2.4f : 1.5f)) * heMul;
            var randomState = Random.state;
            if (_multiplayer.IsInBattle && !hitProp.meta.fixedCover)
            {
                int seed = Random.Range(0, int.MaxValue);
                randomState = Random.state;
                _multiplayer.Transport?.BroadcastPropImpact(_props.IndexOf(hitProp), impact, craterR, p.IsHeavy || p.ammo == AmmoType.HEIBelt, seed);
                Random.InitState(seed);
            }
            bool collapsed = hitProp.ApplyHit(impact, craterR, p.IsHeavy || p.ammo == AmmoType.HEIBelt, SpawnDebrisParticle);
            if (_multiplayer.IsInBattle) Random.state = randomState;
            if (collapsed && !_multiplayer.IsInBattle) _battlefieldNavigation?.Rebuild(_props);

            if (collapsed && (hitProp.kind == "supply_crate" || hitProp.kind == "fuel_tank"))
            {
                bool isFuel = hitProp.kind == "fuel_tank";
                Vector2 cPos = hitProp.transform.position;
                EmitBattleEffect(new BattleEffect { Kind = BattleEffectKind.PropBlast, Ground = cPos, Big = isFuel });
                float blastR = isFuel ? 4.6f : 2.9f;
                foreach (var t in _tanks)
                {
                    if (_phase != GamePhase.Playing || t == null || !t.alive) continue;
                    float distance = Vector2.Distance(t.Position2D, cPos);
                    if (distance >= blastR) continue;
                    TankHit damage = t.ApplyBlast(cPos, squash, isFuel, distance < blastR * 0.55f);
                    if (damage.Ignited || damage.Modules != TankModule.None)
                        ShowTankHit(t, damage);
                    if (!t.alive)
                    {
                        ShowTankLoss(t);
                        if (_multiplayer.IsInBattle || t.isPlayer || WillRoundEndAfterKill(t))
                            TriggerSyntheticDecisiveXRay(t, isFuel ? "FUEL" : "BLAST");
                        CheckTeamVictoryOrWave(t);
                    }
                }
            }
            else if (p.ammo == AmmoType.HE)
            {
                EmitExplosion(visualImpact, impact, true, SoundBus.HeavyReverb, 0.84f, 0.34f);
                ApplyHeOverpressureSplash(impact - p.vel.normalized * 0.06f, p.ownerFaction, 2.75f);
            }
            else if (p.IsHeavy)
            {
                EmitExplosion(visualImpact, impact, false, SoundBus.Reverb, 0.56f, 0.14f);
            }
            else if (p.kind == ProjectileKind.Autocannon)
            {
                EmitBattleEffect(new BattleEffect { Kind = BattleEffectKind.PropSpark, Visual = visualImpact,
                    Big = p.ammo == AmmoType.HEIBelt });
            }
        }

        private bool ResolveProjectileTankImpact(ProjectileData p, TankEntity hitTank, ArmorContact contact, Vector2 impact)
        {
            Vector2 visualImpact = impact + (p.visualPos - p.groundPos);
            TankHit hit = hitTank.ApplyArmorHit(contact, p.kind, p.penetration, out TankShotTrace trace, p.ammo);
            bool bounced = hit.Armor == ArmorHit.Ricochet && p.armorBouncesLeft > 0;
            if (bounced)
            {
                p.vel = Vector2.Reflect(p.vel, contact.GroundNormal);
                trace = new TankShotTrace(trace.Entry, trace.Approach,
                    trace.Entry + hitTank.ToCombatDirection(p.vel, squash) * 0.65f);
            }
            EmitBattleEffect(new BattleEffect { Kind = BattleEffectKind.ArmorImpact, Projectile = p.kind,
                Ground = impact, Visual = visualImpact, Normal = contact.GroundNormal, VictimPosition = hitTank.Position2D,
                Hit = hit, Trace = trace, Loss = hitTank.Loss, Bounced = bounced,
                EndsRound = !hitTank.alive && WillRoundEndAfterKill(hitTank) }, hitTank, p.owner);

            if (bounced)
            {
                p.armorBouncesLeft--;
                p.penetration *= 0.76f;
                Vector2 escape = contact.GroundNormal * 0.04f;
                p.groundPos = impact + escape;
                p.visualPos = visualImpact + escape;
                p.sr.color = new Color(1f, 0.42f, 0.22f);
                p.go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(p.vel.y, p.vel.x) * Mathf.Rad2Deg);
                return false;
            }

            if (p.ammo == AmmoType.HE)
                ApplyHeOverpressureSplash(impact, p.ownerFaction, 2.4f, excludeTank: hitTank);

            if (!hitTank.alive)
            {
                ShowTankLoss(hitTank);
                CheckTeamVictoryOrWave(hitTank);
            }
            return true;
        }

        private void PlayProjectileTrail(ProjectileKind kind, AmmoType ammo, Vector2 position, Vector2 direction)
        {
            if (kind == ProjectileKind.ATGM || AmmoInfo.IsGuided(ammo))
            {
                // Signature blazing HDR TOW/ATGM rocket motor + thick white guidance exhaust trail!
                bool ember = Random.value < 0.58f;
                Color flameCol = ammo == AmmoType.GLATGM
                    ? new Color(0.58f, 0.94f, 1f)
                    : new Color(1f, 0.52f, 0.14f);
                SpawnParticle(
                    position - direction * 0.22f,
                    -direction * 0.6f + new Vector2(Random.Range(-0.2f, 0.2f), Random.Range(0.1f, 0.3f)),
                    0.1f,
                    ember ? 0.16f : 0.42f,
                    ember ? 0.16f : 0.22f,
                    ember ? flameCol : new Color(0.92f, 0.91f, 0.88f, 0.68f),
                    ember ? 7.5f : 1.0f,
                    ember ? _whitePxSprite : _smokePuffSprite
                );
            }
            else if (kind == ProjectileKind.Shell)
            {
                Color wakeCol = ammo == AmmoType.HEATFS || ammo == AmmoType.HE
                    ? new Color(1f, 0.58f, 0.18f, 0.88f)
                    : new Color(1f, 0.86f, 0.44f, 0.85f);
                SpawnParticle(
                    position - direction * 0.14f,
                    Vector2.zero,
                    0f,
                    0.065f,
                    0.10f,
                    wakeCol,
                    5.2f
                );
                if (Random.value < 0.45f)
                {
                    SpawnParticle(
                        position - direction * 0.18f,
                        new Vector2(Random.Range(-0.15f, 0.15f), Random.Range(0.05f, 0.25f)),
                        0f,
                        0.18f,
                        0.09f,
                        new Color(0.92f, 0.88f, 0.78f, 0.50f),
                        1.0f
                    );
                }
            }
            else if (kind == ProjectileKind.Autocannon && Random.value < 0.65f)
            {
                Color acCol = ammo == AmmoType.HEIBelt
                    ? new Color(1f, 0.48f, 0.16f, 0.85f)
                    : new Color(1f, 0.76f, 0.28f, 0.80f);
                SpawnParticle(
                    position - direction * 0.10f,
                    Vector2.zero,
                    0f,
                    0.05f,
                    0.08f,
                    acCol,
                    4.5f
                );
            }
        }

        private void ApplyHeOverpressureSplash(Vector2 origin, string ownerFaction, float blastRadius, TankEntity excludeTank = null)
        {
            foreach (var t in _tanks)
            {
                if (_phase != GamePhase.Playing || t == null || !t.alive || t == excludeTank) continue;
                if (t.factionTeam == ownerFaction) continue;
                float dist = Vector2.Distance(t.Position2D, origin);
                if (dist >= blastRadius) continue;
                if (UsesControlPoints && FindPropHit(origin, t.Position2D, out _, fixedOnly: true) != null) continue;
                TankHit splash = t.ApplyBlast(origin, squash, fuel: false, close: dist < blastRadius * 0.55f);
                if (splash.Modules != TankModule.None || splash.Ignited)
                    ShowTankHit(t, splash);
                if (!t.alive)
                {
                    ShowTankLoss(t);
                    if (_multiplayer.IsInBattle || t.isPlayer || WillRoundEndAfterKill(t))
                        TriggerSyntheticDecisiveXRay(t, "HE");
                    CheckTeamVictoryOrWave(t);
                }
            }
        }

        private DestructibleProp FindPropHit(Vector2 from, Vector2 to, out float fraction, bool fixedOnly = false)
        {
            DestructibleProp nearest = null;
            fraction = 1f;
            foreach (var prop in _props)
            {
                if (prop == null || (fixedOnly && prop.meta?.fixedCover != true)
                    || !prop.TryHitSegment(from, to, out float hit) || (nearest != null && hit >= fraction)) continue;
                fraction = hit;
                nearest = prop;
            }
            return nearest;
        }
    }
}
