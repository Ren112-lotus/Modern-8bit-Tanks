using System.Collections.Generic;
using UnityEngine;

namespace Modern8bitTanks
{
    public partial class DesertBattleManager
    {
        private readonly byte[] _radarSpots = new byte[MultiplayerSessionConnection.MaxPlayers];

        private readonly HashSet<TankEntity> _visibleTanks = new();

        private bool UsesTeamVisibility => MultiplayerSessionConnection.Online ||
            battleMode is BattleMode.Duel1v1 or BattleMode.Squad2v2 or BattleMode.Battle5v5;

        private static bool CanSpot(TankEntity tank) => tank != null && tank.alive && tank.gameObject.activeInHierarchy;

        private TankEntity FindLocalObserver(TankEntity player)
        {
            if (CanSpot(player)) return player;
            if (player == null) return null;

            foreach (var tank in _tanks)
            {
                if (CanSpot(tank) && tank.factionTeam == player.factionTeam)
                    return tank;
            }

            return null;
        }

        private bool CanObserveTank(TankEntity observer, TankEntity target, Vector2 viewCenter, float halfHeight)
        {
            if (!CanSpot(observer)) return false;

            Vector2 from = observer.Position2D;
            Vector2 position = target.Position2D;
            Vector2 offset = position - viewCenter;
            float aspect = (float)renderWidth / renderHeight;
            if (Mathf.Abs(offset.x) > halfHeight * aspect || Mathf.Abs(offset.y) > halfHeight) return false;

            foreach (var smoke in _smokeClouds)
                if ((position - smoke.pos).sqrMagnitude < smoke.radius * smoke.radius) return false;

            return HasLineOfSight(from, position);
        }

        private void UpdateRadarContacts(MultiplayerMatchTransport transport)
        {
            System.Array.Clear(_radarSpots, 0, _radarSpots.Length);
            if (_phase != GamePhase.Playing || _cam == null || _networkTanks == null) return;
            for (int observerSlot = 0; observerSlot < _networkTanks.Length; observerSlot++)
            {
                var observer = _networkTanks[observerSlot];
                if (!CanSpot(observer) || !transport.IsOccupied(observerSlot)) continue;
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
                    if (!CanSpot(target) || (slot < MultiplayerSessionConnection.MaxPlayers / 2) == alpha) continue;
                    if (!CanObserveTank(observer, target, center, halfHeight)) continue;
                    _radarSpots[observerSlot] |= (byte)(1 << slot);
                }
            }
        }

        private bool IsVisibleToLocalTeam(TankEntity target, TankEntity player, TankEntity observer)
        {
            if (!UsesTeamVisibility || _phase != GamePhase.Playing) return true;
            if (player == null) return false;
            if (target.factionTeam == player.factionTeam) return true;

            if (_cam != null && CanObserveTank(observer, target, _cam.transform.position, _cam.orthographicSize))
                return true;

            if (MultiplayerSessionConnection.Online)
                return HasSharedNetworkContact(target, player);

            if (_cam == null) return false;
            foreach (var teammate in _tanks)
            {
                if (!CanSpot(teammate) || teammate == observer || teammate.factionTeam != player.factionTeam) continue;
                if (CanObserveTank(teammate, target, teammate.Position2D, _cam.orthographicSize)) return true;
            }

            return false;
        }

        private bool HasSharedNetworkContact(TankEntity target, TankEntity player)
        {
            if (_networkTanks == null) return false;
            int targetSlot = System.Array.IndexOf(_networkTanks, target);
            if (targetSlot < 0) return false;

            int localSlot = MultiplayerSessionConnection.LocalSlot;
            for (int slot = 0; slot < _networkTanks.Length; slot++)
            {
                var teammate = _networkTanks[slot];
                if (slot == localSlot || !CanSpot(teammate) || teammate.factionTeam != player.factionTeam) continue;
                if ((_radarSpots[slot] & (1 << targetSlot)) != 0) return true;
            }

            return false;
        }

        internal bool IsTankVisible(TankEntity target) => _visibleTanks.Contains(target);
    }
}
