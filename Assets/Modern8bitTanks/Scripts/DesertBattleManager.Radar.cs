using UnityEngine;

namespace Modern8bitTanks
{
    public partial class DesertBattleManager
    {
        private readonly byte[] _radarSpots = new byte[MultiplayerSessionConnection.MaxPlayers];

        private bool CanSeeOnRadar(TankEntity observer, TankEntity target, Vector2 center, float halfHeight)
        {
            if (observer == null || !observer.alive || !observer.gameObject.activeSelf) return false;
            Vector2 offset = target.Position2D - center;
            float aspect = (float)renderWidth / renderHeight;
            if (Mathf.Abs(offset.x) > halfHeight * aspect || Mathf.Abs(offset.y) > halfHeight) return false;
            foreach (var smoke in _smokeClouds)
                if ((target.Position2D - smoke.pos).sqrMagnitude < smoke.radius * smoke.radius) return false;
            return HasLineOfSight(observer.Position2D, target.Position2D);
        }

        private void UpdateRadarContacts(MultiplayerMatchTransport transport)
        {
            System.Array.Clear(_radarSpots, 0, _radarSpots.Length);
            if (_phase != GamePhase.Playing || _cam == null) return;
            for (int observerSlot = 0; observerSlot < _networkTanks.Length; observerSlot++)
            {
                var observer = _networkTanks[observerSlot];
                if (observer == null || !observer.alive || !transport.IsOccupied(observerSlot)) continue;
                Vector2 center;
                float halfHeight;
                if (observer.isPlayer)
                {
                    center = _cam.transform.position;
                    halfHeight = _cam.orthographicSize;
                }
                else
                {
                    if (!transport.TryGetControl(observerSlot, out var control)) continue;
                    center = control.ViewCenter;
                    halfHeight = control.ViewHalfHeight;
                    float baseHeight = renderHeight / PPU * .5f;
                    if (!(halfHeight >= baseHeight / 1.49f && halfHeight <= baseHeight / .75f) ||
                        !float.IsFinite(center.x) || !float.IsFinite(center.y)) continue;

                    // The viewport follows the vehicle, with a bounded aim offset and world-edge clamp.
                    float width = halfHeight * renderWidth / renderHeight;
                    var clampedPosition = new Vector2(Mathf.Clamp(observer.Position2D.x, width, Mathf.Max(width, WorldW - width)),
                        Mathf.Clamp(observer.Position2D.y, halfHeight, Mathf.Max(halfHeight, WorldH - halfHeight)));
                    center = clampedPosition + Vector2.ClampMagnitude(center - clampedPosition, 4.6f);
                }

                bool alpha = observerSlot < MultiplayerSessionConnection.MaxPlayers / 2;
                for (int slot = 0; slot < _networkTanks.Length; slot++)
                {
                    var target = _networkTanks[slot];
                    if (target == null || !target.alive || (slot < MultiplayerSessionConnection.MaxPlayers / 2) == alpha) continue;
                    if (!CanSeeOnRadar(observer, target, center, halfHeight)) continue;
                    _radarSpots[observerSlot] |= (byte)(1 << slot);
                }
            }
        }

        internal bool IsRadarContactVisible(TankEntity target, TankEntity player)
        {
            if (!MultiplayerSessionConnection.Online) return true;
            if (player == null || _networkTanks == null) return false;
            if (target.factionTeam == player.factionTeam) return true;
            int slot = System.Array.IndexOf(_networkTanks, target);
            if (slot < 0 || _phase != GamePhase.Playing) return false;
            int localSlot = MultiplayerSessionConnection.LocalSlot;
            int teamSize = MultiplayerSessionConnection.MaxPlayers / 2;
            int firstTeammate = localSlot < teamSize ? 0 : teamSize;
            for (int observer = firstTeammate; observer < firstTeammate + teamSize; observer++)
            {
                var teammate = _networkTanks[observer];
                if (observer == localSlot || teammate == null || !teammate.alive) continue;
                if ((_radarSpots[observer] & (1 << slot)) != 0) return true;
            }
            return _cam != null && CanSeeOnRadar(player, target, _cam.transform.position, _cam.orthographicSize);
        }
    }
}
