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
            bool limitVisibility, Func<TankEntity, TankEntity, bool> hasLineOfSight)
        {
            _visibleTanks.Clear();

            foreach (var tank in tanks)
            {
                if (tank == null || !tank.gameObject.activeInHierarchy) continue;
                if (!limitVisibility || CanTeamSee(tanks, networkTanks, player, tank, hasLineOfSight))
                    _visibleTanks.Add(tank);
            }
        }

        private bool CanTeamSee(IReadOnlyList<TankEntity> tanks, TankEntity[] networkTanks, TankEntity player,
            TankEntity target, Func<TankEntity, TankEntity, bool> hasLineOfSight)
        {
            if (player == null) return false;
            if (target.factionTeam == player.factionTeam) return true;

            if (_multiplayer.IsInBattle)
                return HasSharedContact(networkTanks, player, target);

            foreach (var teammate in tanks)
            {
                if (!CanSpot(teammate) || teammate.factionTeam != player.factionTeam) continue;
                if (CanObserve(teammate, target, hasLineOfSight))
                    return true;
            }
            return false;
        }

        internal void RefreshNetworkContacts(TankEntity[] tanks, Func<TankEntity, TankEntity, bool> hasLineOfSight)
        {
            Array.Clear(NetworkContacts, 0, NetworkContacts.Length);

            for (int observerSlot = 0; observerSlot < tanks.Length; observerSlot++)
            {
                var observer = tanks[observerSlot];
                if (!CanSpot(observer)) continue;

                for (int slot = 0; slot < tanks.Length; slot++)
                {
                    var target = tanks[slot];
                    if (target == null || !target.gameObject.activeInHierarchy || target.factionTeam == observer.factionTeam) continue;
                    if (CanObserve(observer, target, hasLineOfSight))
                        NetworkContacts[observerSlot] |= (byte)(1 << slot);
                }
            }
        }

        internal static float SightRange(TankEntity observer) =>
            (observer.vehicleDef?.EffectiveSightRangePixels ?? VehicleDef.DefaultSightRangePixels) / BattleSceneController.PPU;

        internal static bool IsWithinSightRange(TankEntity observer, TankEntity target)
        {
            float range = SightRange(observer);
            return (target.Position2D - observer.Position2D).sqrMagnitude <= range * range;
        }

        private static bool CanObserve(TankEntity observer, TankEntity target, Func<TankEntity, TankEntity, bool> hasLineOfSight) =>
            IsWithinSightRange(observer, target) && hasLineOfSight(observer, target);

        private bool HasSharedContact(TankEntity[] tanks, TankEntity player, TankEntity target)
        {
            if (tanks == null) return false;
            int targetSlot = Array.IndexOf(tanks, target);
            if (targetSlot < 0) return false;

            for (int slot = 0; slot < tanks.Length; slot++)
            {
                var teammate = tanks[slot];
                if (!CanSpot(teammate) || teammate.factionTeam != player.factionTeam) continue;
                if ((NetworkContacts[slot] & (1 << targetSlot)) != 0) return true;
            }
            return false;
        }
    }
}
