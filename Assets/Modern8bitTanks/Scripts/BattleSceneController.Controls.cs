using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Modern8bitTanks
{
    public partial class BattleSceneController
    {
        private TankEntity FindNearestOpposingTank(TankEntity self)
        {
            TankEntity best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < _tanks.Count; i++)
            {
                var other = _tanks[i];
                if (other == null || !other.alive || other.factionTeam == self.factionTeam) continue;
                float d = Vector2.Distance(self.Position2D, other.Position2D);
                if (d < bestDist) { bestDist = d; best = other; }
            }
            return best;
        }

        private void UpdatePlayer(TankEntity t, float dt)
        {
            if (!t.alive || _phase != GamePhase.Playing) return;
            if (!TryReadTankControl(out var input)) return;
            ApplyTankControl(t, dt, input);
        }

        private bool TryReadTankControl(out TankControlInput input)
        {
            input = default;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null) return false;

            bool up = kb.wKey.isPressed || kb.upArrowKey.isPressed;
            bool down = kb.sKey.isPressed || kb.downArrowKey.isPressed;
            bool left = kb.aKey.isPressed || kb.leftArrowKey.isPressed;
            bool right = kb.dKey.isPressed || kb.rightArrowKey.isPressed;
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            bool primaryHeld = mouse != null && mouse.leftButton.isPressed;
            _waitForPrimaryRelease &= primaryHeld;
            input = new TankControlInput
            {
                Up = up, Down = down, Left = left, Right = right,
                Repair = kb.fKey.wasPressedThisFrame,
                AimWorld = GetMouseWorld2D(),
                FirePrimary = (!overUI && primaryHeld && !_waitForPrimaryRelease) || kb.spaceKey.isPressed,
                FireSecondary = !overUI && mouse != null && mouse.rightButton.isPressed,
                Smoke = kb.qKey.wasPressedThisFrame || kb.leftShiftKey.wasPressedThisFrame,
                AmmoSlot = kb.digit1Key.wasPressedThisFrame ? 0 : kb.digit2Key.wasPressedThisFrame ? 1 : kb.digit3Key.wasPressedThisFrame ? 2 : -1,
                Arcade = arcadeWASD
            };
            return true;
        }

        private struct TankControlInput
        {
            public bool Up, Down, Left, Right, Repair, FirePrimary, FireSecondary, Smoke, Arcade;
            public int AmmoSlot;
            public Vector2 AimWorld;
        }

        private void ApplyTankControl(TankEntity t, float dt, TankControlInput input)
        {
            if (!t.alive || _phase != GamePhase.Playing) return;
            bool driving = input.Up || input.Down || input.Left || input.Right;
            if (input.Repair || (t.Repairing && driving)) t.RequestService();

            ApplyTankMotion(t, dt, input);

            int wantAmmo = input.AmmoSlot;
            bool primaryAmmoSlot = wantAmmo >= 0 && wantAmmo < t.AmmoLoadout.Length;
            bool fireFromSecondarySlot = !primaryAmmoSlot && wantAmmo == SecondaryWeaponSlotIndex
                && t.vehicleDef?.HasSecondaryAtgm == true;

            if (input.FirePrimary) FirePrimaryWeapon(t);
            if (input.FireSecondary || fireFromSecondarySlot) FireSecondaryWeapon(t);
            if (input.Smoke) DeploySmokeScreen(t);

            if (primaryAmmoSlot && wantAmmo != t.selectedAmmoSlot)
            {
                t.selectedAmmoSlot = wantAmmo;
                if (t.isPlayer) ShowAmmoSelection(t);
            }
        }

        private void ShowAmmoSelection(TankEntity t)
        {
            AmmoType chosen = t.CurrentAmmo;
            PlaySFXAt(_sfxReloadClank, t.Position2D, SoundBus.Dry, 0.55f, centerPan: true);
            string hint = AmmoInfo.IsGuided(chosen) ? " (MOUSE GUIDED)"
                : chosen == AmmoType.HE ? " (OVERPRESSURE)"
                : chosen == AmmoType.HEATFS ? " (SHAPED CHARGE)"
                : chosen == AmmoType.APHE ? " (INTERNAL BLAST)" : "";
            _hud?.ShowWaveToast($"[{t.selectedAmmoSlot + 1}] {AmmoInfo.FullLabel(chosen)}{hint}", 1.45f);
        }

        private void ApplyTankMotion(TankEntity t, float dt, TankControlInput input)
        {
            float rawSpd = t.vehicleDef != null ? t.vehicleDef.speed : 64f;
            float maxSpeed = rawSpd / PPU * t.DriveFactor;
            float turnDegPerSec = t.hullTurnSpeed * t.TurnFactor;
            float turretDegPerSec = t.turretTurnSpeed * t.TraverseFactor;
            float initialHullAngle = t.hullAngleDeg;
            t.HullTurnInput = 0f;

            if (input.Arcade)
            {
                Vector2 inp = Vector2.zero;
                if (input.Left) inp.x -= 1f;
                if (input.Right) inp.x += 1f;
                if (input.Up) inp.y += 1f;
                if (input.Down) inp.y -= 1f;

                if (inp.sqrMagnitude > 0.01f)
                {
                    inp.Normalize();
                    // Align WASD screen input with the rotated camera basis (W always moves UP on screen!)
                    Vector2 worldInp = (SpriteStackCamera.CamRight * inp.x + SpriteStackCamera.CamUp * inp.y).normalized;
                    float targetHull = TankEntity.TargetDeltaToSliceAngle(worldInp, squash);
                    t.hullAngleDeg = Mathf.MoveTowardsAngle(t.hullAngleDeg, targetHull, turnDegPerSec * 1.45f * dt);
                    t.Position2D += worldInp * (maxSpeed * dt);
                    t.moveSpeed = maxSpeed;
                    t.treadAccum += maxSpeed * dt;
                }
                else
                {
                    t.moveSpeed = 0f;
                }
            }
            else
            {
                float throttle = 0f;
                float steer = 0f;
                if (input.Up) throttle += 1f;
                if (input.Down) throttle -= 0.72f;
                if (input.Left) steer += 1f;
                if (input.Right) steer -= 1f;

                t.HullTurnInput = steer * t.TurnFactor;
                if (Mathf.Abs(steer) > 0.01f)
                {
                    t.hullAngleDeg = Mathf.Repeat(t.hullAngleDeg + steer * turnDegPerSec * dt, 360f);
                    t.treadAccum += Mathf.Abs(steer * turnDegPerSec * dt) * 0.0085f;
                }

                float targetSpd = throttle * maxSpeed;
                t.moveSpeed = Mathf.Lerp(t.moveSpeed, targetSpd, Mathf.Clamp01(dt * 8f));
                if (Mathf.Abs(t.moveSpeed) > 0.08f)
                {
                    Vector2 dir = TankEntity.SliceAngleToDir(t.hullAngleDeg);
                    t.Position2D += SpriteStackCamera.Squash(dir * (t.moveSpeed * dt), squash);
                    t.treadAccum += Mathf.Abs(t.moveSpeed) * dt;
                }
            }

            Vector2 mouseWorld = input.AimWorld;
            Vector2 piv = t.GetTurretPivotWorld(squash);
            float desiredTurret = TankEntity.TargetDeltaToSliceAngle(mouseWorld - piv, squash);
            if (t.HasDisabled(TankModule.Traverse))
                t.turretAngleDeg += Mathf.DeltaAngle(initialHullAngle, t.hullAngleDeg);
            else
                t.turretAngleDeg = Mathf.MoveTowardsAngle(t.turretAngleDeg,
                    LimitGunTraverse(t, desiredTurret), turretDegPerSec * dt);
            t.turretAngleDeg = LimitGunTraverse(t, t.turretAngleDeg);

        }

        private static float LimitGunTraverse(TankEntity tank, float angle)
        {
            int limit = tank.vehicleDef?.fixedGunTraverse ?? 0;
            return limit > 0 ? tank.hullAngleDeg + Mathf.Clamp(Mathf.DeltaAngle(tank.hullAngleDeg, angle), -limit, limit)
                : angle;
        }

        private void UpdateAI(TankEntity t, TankEntity target, float dt)
        {
            if (!t.alive || !target.alive || !aiEnabled || _phase != GamePhase.Playing) return;

            Vector2 piv = t.GetTurretPivotWorld(squash);
            Vector2 delta = target.Position2D - piv;
            float dist = delta.magnitude;

            Vector2 halfSize = BattleViewport.HalfSizeFor(t, _cam.aspect);
            var viewport = new BattleViewport(t.Position2D, halfSize);
            bool targetVisible = viewport.Contains(target.Position2D) && HasLineOfSight(t.Position2D, target.Position2D);
            float preferredDistance = Mathf.Min(19.5f, halfSize.y * 0.85f);
            float retreatDistance = Mathf.Min(8f, preferredDistance * 0.75f);

            float aiTurretDegPerSec = t.turretTurnSpeed * t.TraverseFactor;
            if (targetVisible)
            {
                float desiredTurret = TankEntity.TargetDeltaToSliceAngle(delta, squash);
                t.turretAngleDeg = Mathf.MoveTowardsAngle(t.turretAngleDeg, desiredTurret, aiTurretDegPerSec * 0.72f * dt);
            }
            else
            {
                // Slowly sweep turret while flanking around cover/smoke
                t.turretAngleDeg = Mathf.MoveTowardsAngle(t.turretAngleDeg, t.hullAngleDeg, aiTurretDegPerSec * 0.40f * dt);
            }

            t.aiStrafeTimer -= dt;
            if (t.aiStrafeTimer <= 0f)
            {
                t.aiStrafeTimer = Random.Range(1.8f, 3.8f);
                t.aiStrafeDir = Random.value < 0.5f ? 1f : -1f;
            }

            Vector2 u = delta / Mathf.Max(0.01f, dist);
            Vector2 move = Vector2.zero;
            bool pursuingControl = UsesControlPoints && (!targetVisible || dist > 20f);
            if (pursuingControl)
            {
                move = GetControlSteering(t);
            }
            else
            {
                if (dist > preferredDistance) move += u;
                else if (dist < retreatDistance) move -= u;
                move += new Vector2(-u.y, u.x) * (t.aiStrafeDir * 0.95f);
            }

            foreach (var prop in _props)
            {
                if (prop == null || !prop.alive) continue;
                if (prop.meta.fixedCover)
                {
                    Vector2 avoid = t.Position2D;
                    prop.ResolveTankPush(ref avoid, 1.35f, squash);
                    move += (avoid - t.Position2D) * 1.4f;
                    continue;
                }
                Vector2 bDiff = t.Position2D - (Vector2)prop.transform.position;
                float bd = bDiff.magnitude;
                if (bd < prop.approxRadiusUnits + 2.0f && bd > 0.01f)
                    move += (bDiff / bd) * 2.4f;
            }

            // Avoid crowding allied tanks in 2v2 / Wave mode
            foreach (var other in _tanks)
            {
                if (other == null || other == t || !other.alive) continue;
                Vector2 oDiff = t.Position2D - other.Position2D;
                float od = oDiff.magnitude;
                if (od < 3.6f && od > 0.01f)
                    move += (oDiff / od) * 1.6f;
            }

            if (t.Position2D.x < BorderPad + 2.8f) move.x += 1.5f;
            if (t.Position2D.x > WorldW - BorderPad - 2.8f) move.x -= 1.5f;
            if (t.Position2D.y < BorderPad + 2.8f) move.y += 1.5f;
            if (t.Position2D.y > WorldH - BorderPad - 2.8f) move.y -= 1.5f;

            t.moveSpeed = 0f;
            if (move.sqrMagnitude > 0.02f)
            {
                bool aimWithHull = !pursuingControl && t.vehicleDef?.fixedGunTraverse is > 0 && targetVisible && dist < 38f;
                float targetHull = TankEntity.TargetDeltaToSliceAngle(aimWithHull ? delta : move.normalized, squash);
                float initialHullAngle = t.hullAngleDeg;
                float aiHullDegPerSec = t.hullTurnSpeed * 0.85f * t.TurnFactor;
                t.hullAngleDeg = Mathf.MoveTowardsAngle(t.hullAngleDeg, targetHull, aiHullDegPerSec * dt);
                float turnedDeg = Mathf.Abs(Mathf.DeltaAngle(initialHullAngle, t.hullAngleDeg));
                if (t.HasDisabled(TankModule.Traverse))
                    t.turretAngleDeg += Mathf.DeltaAngle(initialHullAngle, t.hullAngleDeg);
                t.moveSpeed = ((t.vehicleDef != null ? t.vehicleDef.speed : 60f) * 0.68f) / PPU * t.DriveFactor;
                if ((aimWithHull && dist >= retreatDistance && dist <= preferredDistance)
                    || (pursuingControl && Mathf.Abs(Mathf.DeltaAngle(t.hullAngleDeg, targetHull)) > 55f))
                    t.moveSpeed = 0f;
                Vector2 dir = TankEntity.SliceAngleToDir(t.hullAngleDeg) * (aimWithHull && dist < retreatDistance ? -1f : 1f);
                t.Position2D += SpriteStackCamera.Squash(dir * (t.moveSpeed * dt), squash);
                t.treadAccum += (Mathf.Abs(t.moveSpeed) * dt) + (turnedDeg * 0.0085f);
            }

            t.turretAngleDeg = LimitGunTraverse(t, t.turretAngleDeg);
            if (targetVisible && dist < 38f)
            {
                float desiredTurret = TankEntity.TargetDeltaToSliceAngle(delta, squash);
                float aimErr = Mathf.Abs(Mathf.DeltaAngle(t.turretAngleDeg, desiredTurret));
                if (aimErr < 8f)
                {
                    bool isAuto = t.vehicleDef != null && (t.vehicleDef.weapon == "AutocannonATGM" || t.vehicleDef.weapon == "TwinAutocannonATGM");
                    t.aiFireDelay -= dt;
                    if (t.aiFireDelay <= 0f)
                    {
                        FirePrimaryWeapon(t);
                        t.aiFireDelay = isAuto ? Random.Range(0.14f, 0.26f) : Random.Range(0.32f, 0.68f);
                    }
                    else if (t.vehicleDef != null && t.vehicleDef.HasSecondaryAtgm && t.atgmCooldown <= 0f && dist > 6.5f && Random.value < 0.08f)
                    {
                        FireSecondaryWeapon(t);
                    }
                    else if (!isAuto && dist < 10.5f && Random.value < 0.10f)
                    {
                        FireSecondaryWeapon(t);
                    }
                }
            }
        }

        private void UpdateOfflineOpponents(float dt)
        {
            if (_multiplayer.IsInBattle) return;

            if (battleMode == BattleMode.FiringRange)
            {
                UpdateFiringRangeTargets(dt);
            }
            else
            {
                foreach (var t in _tanks)
                {
                    if (t == null || !t.alive || t.isPlayer) continue;
                    TankEntity nearestHostile = FindNearestOpposingTank(t);
                    if (nearestHostile != null) UpdateAI(t, nearestHostile, dt);
                }
            }
        }

        private void ResolveTankMovementCollisions()
        {
            foreach (var t in _tanks)
            {
                if (t == null || !t.gameObject.activeSelf) continue;
                ResolveTankCollisions(t);
            }

            // Tank-vs-tank separation
            for (int i = 0; i < _tanks.Count; i++)
            {
                for (int j = i + 1; j < _tanks.Count; j++)
                {
                    if (_tanks[i] == null || _tanks[j] == null || !_tanks[i].gameObject.activeSelf || !_tanks[j].gameObject.activeSelf) continue;
                    Vector2 diff = _tanks[j].Position2D - _tanks[i].Position2D;
                    float dist = diff.magnitude;
                    float minSep = 24f / PPU;
                    if (dist > 0.001f && dist < minSep)
                    {
                        Vector2 push = (diff / dist) * ((minSep - dist) * 0.5f);
                        _tanks[i].Position2D -= push;
                        _tanks[j].Position2D += push;
                    }
                }
            }
        }

        private void ResolveTankCollisions(TankEntity t)
        {
            float rad = 12f / PPU;
            Vector2 p = t.Position2D;
            p.x = Mathf.Clamp(p.x, BorderPad + rad, WorldW - BorderPad - rad);
            p.y = Mathf.Clamp(p.y, BorderPad + rad, WorldH - BorderPad - rad);

            foreach (var prop in _props)
            {
                if (prop == null || !prop.alive) continue;
                prop.ResolveTankPush(ref p, rad, squash);
            }
            p.x = Mathf.Clamp(p.x, BorderPad + rad, WorldW - BorderPad - rad);
            p.y = Mathf.Clamp(p.y, BorderPad + rad, WorldH - BorderPad - rad);
            t.Position2D = p;
        }

        private bool HasLineOfSight(Vector2 a, Vector2 b)
        {
            if (FindPropHit(a, b, out _) != null) return false;
            Vector2 delta = b - a;
            float lengthSq = Mathf.Max(delta.sqrMagnitude, 0.000001f);
            foreach (var smoke in _smokeClouds)
            {
                float t = Mathf.Clamp01(Vector2.Dot(smoke.pos - a, delta) / lengthSq);
                if ((a + delta * t - smoke.pos).sqrMagnitude < smoke.radius * smoke.radius) return false;
            }
            return true;
        }
    }
}
