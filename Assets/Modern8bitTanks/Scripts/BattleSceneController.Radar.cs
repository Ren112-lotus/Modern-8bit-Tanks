using UnityEngine;

namespace Modern8bitTanks
{
    public partial class BattleSceneController
    {
        private System.Func<int, TankEntity, BattleViewport?> _getNetworkViewport;

        internal (Vector2 Start, Vector2 End)? PlayerGroundAim { get; private set; }

        private BattleViewport CameraViewport => new BattleViewport(_cam.transform.position,
            new Vector2(_cam.orthographicSize * _cam.aspect, _cam.orthographicSize), _camRollDeg);

        private bool HasTankSight(TankEntity observer, TankEntity target)
        {
            Vector2 from = observer.Position2D;
            Vector2 position = target.Position2D;
            if (HasLineOfSight(from, position)) return true;
            // The visible edge of the hull can peek around cover before its center.
            Vector2 shoulder = Vector2.Perpendicular((position - from).normalized) * (9f / PPU);
            return HasLineOfSight(from, position + shoulder) || HasLineOfSight(from, position - shoulder);
        }

        private void UpdateRadarContacts()
        {
            if (_phase != GamePhase.Playing)
            {
                System.Array.Clear(_visibility.NetworkContacts, 0, _visibility.NetworkContacts.Length);
                return;
            }

            _visibility.RefreshNetworkContacts(_networkTanks, _getNetworkViewport, _hasTankSight);
        }

        private BattleViewport? GetNetworkViewport(int slot, TankEntity observer)
        {
            if (observer.isPlayer) return CameraViewport;
            if (!_multiplayer.Transport.TryGetControl(slot, out var control)) return null;

            Vector2 halfSize = BattleViewport.HalfSizeFor(observer, _cam.aspect);
            Vector2 center = ConstrainCameraCenter(observer.Position2D, halfSize);

            // Allow camera follow, smoothing and recoil offsets from the host's tank position.
            const float followAllowance = CameraLookAhead + 2.6f;
            center += Vector2.ClampMagnitude(control.ViewCenter - center, followAllowance);
            return new BattleViewport(center, halfSize, control.ViewRoll);
        }

        internal bool IsTankVisible(TankEntity target) => _visibility.IsVisible(target);
    }
}
