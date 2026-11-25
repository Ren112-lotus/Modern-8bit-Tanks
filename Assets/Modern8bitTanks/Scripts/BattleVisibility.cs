using System;
using System.Collections.Generic;
using UnityEngine;

namespace Modern8bitTanks
{
    internal readonly struct BattleViewport
    {
        internal Vector2 Center { get; }
        internal Vector2 HalfSize { get; }
        private readonly Vector2 _right;
        private readonly Vector2 _up;

        internal BattleViewport(Vector2 center, Vector2 halfSize, float rollDegrees = 0f)
        {
            Center = center;
            HalfSize = halfSize;
            float radians = rollDegrees * Mathf.Deg2Rad;
            _right = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            _up = Vector2.Perpendicular(_right);
        }

        internal static Vector2 SizeInPixelsFor(VehicleDef vehicle, float aspect)
        {
            float height = vehicle?.EffectiveCameraHeightPixels ?? VehicleDef.DefaultCameraHeightPixels;
            return new Vector2(height * aspect, height);
        }

        internal static Vector2 HalfSizeFor(TankEntity tank, float aspect) =>
            SizeInPixelsFor(tank != null ? tank.vehicleDef : null, aspect) / (2f * BattleSceneController.PPU);

        internal bool Contains(Vector2 worldPosition)
        {
            Vector2 offset = worldPosition - Center;
            return Mathf.Abs(Vector2.Dot(offset, _right)) <= HalfSize.x &&
                Mathf.Abs(Vector2.Dot(offset, _up)) <= HalfSize.y;
        }
    }

    internal sealed class BattleVisibility
    {
        private readonly MultiplayerSessionState _multiplayer;
        private readonly HashSet<TankEntity> _visibleTanks = new();

        public BattleVisibility(MultiplayerSessionState multiplayer) => _multiplayer = multiplayer;

        internal ushort[] NetworkContacts { get; } = new ushort[MultiplayerSessionConnection.MaxPlayers];

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
            TankEntity observer, BattleViewport viewport, float aspect, bool limitVisibility,
            Func<TankEntity, TankEntity, bool> hasLineOfSight)
        {
            _visibleTanks.Clear();

            foreach (var tank in tanks)
            {
                if (tank == null || !tank.gameObject.activeInHierarchy) continue;
                if (!limitVisibility || CanTeamSee(tanks, networkTanks, player, observer, tank, viewport, aspect, hasLineOfSight))
                    _visibleTanks.Add(tank);
            }
        }

        private bool CanTeamSee(IReadOnlyList<TankEntity> tanks, TankEntity[] networkTanks, TankEntity player,
            TankEntity observer, TankEntity target, BattleViewport viewport, float aspect,
            Func<TankEntity, TankEntity, bool> hasLineOfSight)
        {
            if (player == null) return false;
            if (target.factionTeam == player.factionTeam) return true;

            if (CanSpot(observer) && CanObserve(observer, target, viewport, hasLineOfSight)) return true;

            if (_multiplayer.IsInBattle)
                return HasSharedContact(networkTanks, player, observer, target);

            foreach (var teammate in tanks)
            {
                if (!CanSpot(teammate) || teammate == observer || teammate.factionTeam != player.factionTeam) continue;
                var teammateView = new BattleViewport(teammate.Position2D, BattleViewport.HalfSizeFor(teammate, aspect));
                if (CanObserve(teammate, target, teammateView, hasLineOfSight))
                    return true;
            }
            return false;
        }

        internal void RefreshNetworkContacts(TankEntity[] tanks, Func<int, TankEntity, BattleViewport?> getViewport,
            Func<TankEntity, TankEntity, bool> hasLineOfSight)
        {
            Array.Clear(NetworkContacts, 0, NetworkContacts.Length);

            for (int observerSlot = 0; observerSlot < tanks.Length; observerSlot++)
            {
                var observer = tanks[observerSlot];
                if (!CanSpot(observer) || getViewport(observerSlot, observer) is not BattleViewport viewport) continue;

                for (int slot = 0; slot < tanks.Length; slot++)
                {
                    var target = tanks[slot];
                    if (target == null || !target.gameObject.activeInHierarchy || target.factionTeam == observer.factionTeam) continue;
                    if (CanObserve(observer, target, viewport, hasLineOfSight))
                        NetworkContacts[observerSlot] |= (ushort)(1 << slot);
                }
            }
        }

        private static bool CanObserve(TankEntity observer, TankEntity target, BattleViewport viewport,
            Func<TankEntity, TankEntity, bool> hasLineOfSight) =>
            viewport.Contains(target.Position2D) && hasLineOfSight(observer, target);

        private bool HasSharedContact(TankEntity[] tanks, TankEntity player, TankEntity observer, TankEntity target)
        {
            if (tanks == null) return false;
            int targetSlot = Array.IndexOf(tanks, target);
            if (targetSlot < 0) return false;

            for (int slot = 0; slot < tanks.Length; slot++)
            {
                var teammate = tanks[slot];
                if (!CanSpot(teammate) || teammate == observer || teammate.factionTeam != player.factionTeam) continue;
                if ((NetworkContacts[slot] & (1 << targetSlot)) != 0) return true;
            }
            return false;
        }
    }
}
