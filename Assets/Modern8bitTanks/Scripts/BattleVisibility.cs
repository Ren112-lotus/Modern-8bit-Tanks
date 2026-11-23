using System;
using System.Collections.Generic;
using UnityEngine;

namespace Modern8bitTanks
{
    internal sealed class BattleVisibility
    {
        private readonly MultiplayerSessionState _multiplayer;
        private readonly HashSet<TankEntity> _visibleTanks = new();

        public BattleVisibility(MultiplayerSessionState multiplayer) => _multiplayer = multiplayer;

        internal byte[] NetworkContacts { get; } = new byte[MultiplayerSessionConnection.MaxPlayers];

        internal bool IsVisible(TankEntity tank) => _visibleTanks.Contains(tank);

        private static bool CanSpot(TankEntity tank) => tank != null && tank.alive && tank.gameObject.activeInHierarchy;

        internal static TankEntity FindObserver(IReadOnlyList<TankEntity> tanks, TankEntity player)
        {
            if (CanSpot(player)) return player;
            if (player == null) return null;

            foreach (var tank in tanks)
                if (CanSpot(tank) && tank.factionTeam == player.factionTeam)
                    return tank;

            return null;
        }

        internal void Refresh(IReadOnlyList<TankEntity> tanks, TankEntity[] networkTanks, TankEntity player,
            bool limitVisibility, Camera camera, float aspect, Func<TankEntity, TankEntity, bool> hasLineOfSight)
        {
            _visibleTanks.Clear();
            var observer = limitVisibility ? FindObserver(tanks, player) : null;

            foreach (var tank in tanks)
            {
                if (tank == null || !tank.gameObject.activeInHierarchy) continue;
                if (!limitVisibility || CanTeamSee(tanks, networkTanks, player, observer, tank, camera, aspect, hasLineOfSight))
                    _visibleTanks.Add(tank);
            }
        }

        private bool CanTeamSee(IReadOnlyList<TankEntity> tanks, TankEntity[] networkTanks, TankEntity player,
            TankEntity observer, TankEntity target, Camera camera, float aspect, Func<TankEntity, TankEntity, bool> hasLineOfSight)
        {
            if (player == null) return false;
            if (target.factionTeam == player.factionTeam) return true;

            if (camera != null && CanObserve(observer, target, camera.transform.position, camera.orthographicSize, aspect, hasLineOfSight))
                return true;

            if (_multiplayer.IsInBattle)
                return HasSharedContact(networkTanks, player, target);

            if (camera == null) return false;
            foreach (var teammate in tanks)
            {
                if (!CanSpot(teammate) || teammate == observer || teammate.factionTeam != player.factionTeam) continue;
                if (CanObserve(teammate, target, teammate.Position2D, camera.orthographicSize, aspect, hasLineOfSight))
                    return true;
            }
            return false;
        }

        internal void RefreshNetworkContacts(TankEntity[] tanks, MultiplayerMatchTransport transport, Camera camera,
            Vector2 fieldSize, float aspect, float baseHalfHeight, Func<TankEntity, TankEntity, bool> hasLineOfSight)
        {
            Array.Clear(NetworkContacts, 0, NetworkContacts.Length);
            if (camera == null || tanks == null) return;

            for (int observerSlot = 0; observerSlot < tanks.Length; observerSlot++)
            {
                var observer = tanks[observerSlot];
                if (!CanSpot(observer) || !transport.IsOccupied(observerSlot) ||
                    !TryGetNetworkView(transport, observerSlot, observer, camera, fieldSize, aspect, baseHalfHeight,
                        out var center, out var halfHeight)) continue;

                for (int slot = 0; slot < tanks.Length; slot++)
                {
                    var target = tanks[slot];
                    if (!CanSpot(target) || target.factionTeam == observer.factionTeam) continue;
                    if (CanObserve(observer, target, center, halfHeight, aspect, hasLineOfSight))
                        NetworkContacts[observerSlot] |= (byte)(1 << slot);
                }
            }
        }

        private static bool TryGetNetworkView(MultiplayerMatchTransport transport, int slot, TankEntity observer,
            Camera camera, Vector2 fieldSize, float aspect, float baseHalfHeight, out Vector2 center, out float halfHeight)
        {
            center = camera.transform.position;
            halfHeight = camera.orthographicSize;
            if (observer.isPlayer) return true;
            if (!transport.TryGetControl(slot, out var control)) return false;

            center = control.ViewCenter;
            halfHeight = control.ViewHalfHeight;
            if (!(halfHeight >= baseHalfHeight / 1.49f && halfHeight <= baseHalfHeight / .75f) ||
                !float.IsFinite(center.x) || !float.IsFinite(center.y)) return false;

            // Bound remote view reports to the observer's camera follow range.
            float width = halfHeight * aspect;
            Vector2 position = observer.Position2D;
            var clampedPosition = new Vector2(Mathf.Clamp(position.x, width, Mathf.Max(width, fieldSize.x - width)),
                Mathf.Clamp(position.y, halfHeight, Mathf.Max(halfHeight, fieldSize.y - halfHeight)));
            center = clampedPosition + Vector2.ClampMagnitude(center - clampedPosition, 4.6f);
            return true;
        }

        private static bool CanObserve(TankEntity observer, TankEntity target, Vector2 viewCenter, float halfHeight,
            float aspect, Func<TankEntity, TankEntity, bool> hasLineOfSight)
        {
            if (!CanSpot(observer)) return false;
            Vector2 offset = target.Position2D - viewCenter;
            return Mathf.Abs(offset.x) <= halfHeight * aspect && Mathf.Abs(offset.y) <= halfHeight && hasLineOfSight(observer, target);
        }

        private bool HasSharedContact(TankEntity[] tanks, TankEntity player, TankEntity target)
        {
            if (tanks == null) return false;
            int targetSlot = Array.IndexOf(tanks, target);
            if (targetSlot < 0) return false;

            for (int slot = 0; slot < tanks.Length; slot++)
            {
                var teammate = tanks[slot];
                if (slot == _multiplayer.LocalSlot || !CanSpot(teammate) || teammate.factionTeam != player.factionTeam) continue;
                if ((NetworkContacts[slot] & (1 << targetSlot)) != 0) return true;
            }
            return false;
        }
    }
}
