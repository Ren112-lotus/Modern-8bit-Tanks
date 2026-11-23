using UnityEngine;

namespace Modern8bitTanks
{
    public partial class BattleSceneController
    {
        private bool UsesTeamVisibility => _multiplayer.IsInBattle ||
            battleMode is BattleMode.Duel1v1 or BattleMode.Squad2v2 or BattleMode.Battle5v5;

        private float ViewAspect => (float)renderWidth / renderHeight;

        private bool HasTankSight(TankEntity observer, TankEntity target)
        {
            Vector2 from = observer.Position2D;
            Vector2 position = target.Position2D;
            if (HasLineOfSight(from, position)) return true;
            // The visible edge of the hull can peek around cover before its center.
            Vector2 shoulder = Vector2.Perpendicular((position - from).normalized) * (9f / PPU);
            return HasLineOfSight(from, position + shoulder) || HasLineOfSight(from, position - shoulder);
        }

        private void UpdateRadarContacts(MultiplayerMatchTransport transport)
        {
            if (_phase != GamePhase.Playing)
            {
                System.Array.Clear(_visibility.NetworkContacts, 0, _visibility.NetworkContacts.Length);
                return;
            }

            _visibility.RefreshNetworkContacts(_networkTanks, transport, _cam,
                new Vector2(WorldW, WorldH), ViewAspect, renderHeight / PPU * .5f, _hasTankSight);
        }

        internal bool IsTankVisible(TankEntity target) => _visibility.IsVisible(target);
    }
}
