using UnityEngine;
using UnityEngine.InputSystem;

namespace Modern8bitTanks
{
    public partial class BattleSceneController
    {
        private const float CameraLookAhead = 3.4f;

        private Vector2 _camPos;
        private float _camRollDeg;
        private Vector2 _camKickPos;
        private Vector2 _camKickVel;
        private Vector2 _lastValidMouseWorld;
        private Transform _reticleTf;

        private Vector2 GetMouseWorld2D()
        {
            var mouse = Mouse.current;
            if (mouse == null || _cam == null || _windowbox == null) return _lastValidMouseWorld;

            Vector2 screen = mouse.position.ReadValue();
            if (!_windowbox.TryScreenToViewport(screen, out Vector2 uv))
                return _lastValidMouseWorld;

            float depth = Mathf.Abs(_cam.transform.position.z);
            Vector3 w = _cam.ViewportToWorldPoint(new Vector3(uv.x, uv.y, depth));
            if (float.IsFinite(w.x) && float.IsFinite(w.y))
                _lastValidMouseWorld = new Vector2(w.x, w.y);
            return _lastValidMouseWorld;
        }

        private void UpdateCameraRoll(TankEntity player, float dt)
        {
            if (cameraMode == BattleCameraMode.FixedTop)
            {
                _camRollDeg = Mathf.LerpAngle(_camRollDeg, 0f, Mathf.Clamp01(Time.unscaledDeltaTime * 8.5f));
            }
            else if (player != null && player.alive && _phase == GamePhase.Playing)
            {
                float targetRoll = cameraMode == BattleCameraMode.TurretLock
                    ? (player.turretAngleDeg - 180f)
                    : (player.hullAngleDeg - 180f);
                float err = Mathf.DeltaAngle(_camRollDeg, targetRoll);
                if (cameraMode == BattleCameraMode.TurretLock)
                {
                    // Tiny 3-deg stability deadzone around 12 o'clock so fine weakspot aiming stays rock-solid,
                    // while swinging the turret rotates the 3D sprite-stacked world smoothly behind the gun!
                    if (Mathf.Abs(err) > 3.0f)
                    {
                        float excess = Mathf.Sign(err) * (Mathf.Abs(err) - 3.0f);
                        float maxStep = Mathf.Clamp(Mathf.Abs(excess) * 4.2f, 20f, 175f) * dt;
                        _camRollDeg = Mathf.Repeat(_camRollDeg + Mathf.Clamp(excess, -maxStep, maxStep), 360f);
                    }
                }
                else
                {
                    _camRollDeg = Mathf.LerpAngle(_camRollDeg, targetRoll, Mathf.Clamp01(dt * 7.5f));
                }
            }

            SpriteStackCamera.SetRoll(_camRollDeg);
            if (_cam != null)
                _cam.transform.rotation = Quaternion.Euler(0f, 0f, _camRollDeg);
        }

        private Vector2 ConstrainCameraCenter(Vector2 center, Vector2 halfSize)
        {
            if (UsesControlPoints) return center;

            return new Vector2(
                Mathf.Clamp(center.x, halfSize.x, Mathf.Max(halfSize.x, WorldW - halfSize.x)),
                Mathf.Clamp(center.y, halfSize.y, Mathf.Max(halfSize.y, WorldH - halfSize.y)));
        }

        private void UpdateCameraAndAim(TankEntity player, TankEntity observer, float dt)
        {
            if (_cam == null) return;

            Vector2 halfSize = BattleViewport.HalfSizeFor(observer != null ? observer : player, _cam.aspect)
                / (1f + _killZoomBoost);
            _cam.orthographicSize = halfSize.y;

            Vector2 mouseW = GetMouseWorld2D();
            Vector2 targetCam = _camPos;
            float camSmooth;
            if (_phase == GamePhase.RoundOver)
            {
                targetCam = _killFocusPos;
                camSmooth = Time.unscaledDeltaTime * 3.4f;
            }
            else if (player != null && player.alive)
            {
                Vector2 lookAhead = Vector2.ClampMagnitude((mouseW - player.Position2D) * 0.26f, CameraLookAhead);
                targetCam = player.Position2D + lookAhead;
                camSmooth = dt * 6.5f;
            }
            else
            {
                if (observer != null) targetCam = observer.Position2D;
                camSmooth = dt * 3.8f;
            }
            _camPos = ConstrainCameraCenter(Vector2.Lerp(_camPos, targetCam, Mathf.Clamp01(camSmooth)), halfSize);

            Vector2 springAcc = -_camKickPos * 260f - _camKickVel * 28f;
            _camKickVel += springAcc * dt;
            _camKickPos += _camKickVel * dt;
            _camKickPos = Vector2.ClampMagnitude(_camKickPos, 0.55f);

            Vector2 shakeOff = _shake > 0f ? Random.insideUnitCircle * _shake : Vector2.zero;
            Vector2 totalOff = _camKickPos + shakeOff;
            float snapPpu = PPU * (_windowbox != null ? _windowbox.GameRenderScale : 1);
            float camX = Mathf.Round((_camPos.x + totalOff.x) * snapPpu) / snapPpu;
            float camY = Mathf.Round((_camPos.y + totalOff.y) * snapPpu) / snapPpu;
            _cam.transform.position = new Vector3(camX, camY, -20f);
            _cam.transform.rotation = Quaternion.Euler(0f, 0f, _camRollDeg);

            if (player != null && player.alive && _phase == GamePhase.Playing)
            {
                _aimLine.enabled = true;
                _reticleSr.enabled = true;
                player.GetMuzzlePositions(squash, spacingPx, out Vector2 visMuz, out Vector2 gndMuz);
                Vector2 td = TankEntity.SliceAngleToDir(player.turretAngleDeg);
                Vector2 dir = SpriteStackCamera.Squash(td, squash).normalized;
                Vector2 zLiftVec = visMuz - gndMuz;

                float mapEdge = UsesControlPoints ? BorderPad : 0f;
                float mapDistance = AimDistanceToBoundary(gndMuz, dir, mapEdge);
                Vector2 mapEnd = gndMuz + dir * mapDistance;
                Vector2 pivot = player.GetTurretPivotWorld(squash);
                if (UsesControlPoints && FindPropHit(pivot, gndMuz, out _) != null)
                    mapDistance = 0f;
                else if (FindPropHit(gndMuz, mapEnd, out float hit) != null)
                    mapDistance *= hit;

                float range = Mathf.Min(19.8f, Mathf.Min(AimDistanceToBoundary(gndMuz, dir, BorderPad), mapDistance));
                Vector2 endGnd = gndMuz + dir * range;
                PlayerGroundAim = (gndMuz, gndMuz + dir * mapDistance);
                _aimLine.SetPosition(0, new Vector3(visMuz.x, visMuz.y, -4f));
                _aimLine.SetPosition(1, new Vector3(endGnd.x + zLiftVec.x, endGnd.y + zLiftVec.y, -4f));

                _reticleTf.position = new Vector3(mouseW.x, mouseW.y, -4f);
                _reticleTf.rotation = Quaternion.identity;
                _reticleSr.color = !player.CanFire ? new Color(1f, 0.36f, 0.30f) :
                    player.reloadTimer > 0f ? new Color(0.95f, 0.66f, 0.31f) : new Color(0.61f, 1f, 0.43f);
            }
            else
            {
                PlayerGroundAim = null;
                _aimLine.enabled = false;
                _reticleSr.enabled = false;
            }
        }

        private float AimDistanceToBoundary(Vector2 origin, Vector2 direction, float padding)
        {
            float x = Mathf.Abs(direction.x) < 0.00001f ? float.PositiveInfinity
                : ((direction.x > 0f ? WorldW - padding : padding) - origin.x) / direction.x;
            float y = Mathf.Abs(direction.y) < 0.00001f ? float.PositiveInfinity
                : ((direction.y > 0f ? WorldH - padding : padding) - origin.y) / direction.y;
            return Mathf.Max(0f, Mathf.Min(x, y));
        }
    }
}
