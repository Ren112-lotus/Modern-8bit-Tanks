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

        internal bool CanSeeHull(TankEntity target, Vector2 observer, Func<Vector2, Vector2, bool> hasLineOfSight)
        {
            Vector2 position = target.Position2D;
            var meta = target.meta;
            if (meta == null) return Contains(position) && hasLineOfSight(observer, position);

            float unitPerPixel = 1f / Mathf.Max(1f, target.pixelsPerUnit);
            float squash = target.hullStack != null ? target.hullStack.squash : 0.8f;
            float spacing = target.hullStack != null ? target.hullStack.spacingPx : 0.72f;
            Vector2 forward = TankEntity.SliceAngleToDir(target.hullAngleDeg);
            Vector2 along = ProjectHullAxis(forward, squash) * (meta.hullLength * 0.5f * unitPerPixel);
            Vector2 across = ProjectHullAxis(Vector2.Perpendicular(forward), squash) * (meta.hullWidth * 0.5f * unitPerPixel);
            Vector2 center = ToView(position - Center);
            Vector2 extent = new(Mathf.Abs(along.x) + Mathf.Abs(across.x), Mathf.Abs(along.y) + Mathf.Abs(across.y));
            // Higher hull slices can enter at the bottom edge before their ground footprint does.
            float lift = Mathf.Max(0, meta.hullSlices - 1) * spacing * unitPerPixel;
            float bottom = -HalfSize.y - lift;
            if (center.x + extent.x < -HalfSize.x || center.x - extent.x > HalfSize.x ||
                center.y + extent.y < bottom || center.y - extent.y > HalfSize.y) return false;

            bool centerInView = Mathf.Abs(center.x) <= HalfSize.x && center.y >= bottom && center.y <= HalfSize.y;
            if (centerInView && hasLineOfSight(observer, position)) return true;

            Span<Vector2> hull = stackalloc Vector2[8];
            Span<Vector2> clipped = stackalloc Vector2[8];
            hull[0] = center - along - across;
            hull[1] = center + along - across;
            hull[2] = center + along + across;
            hull[3] = center - along + across;
            int count = Clip(hull[..4], clipped, Vector2.right, HalfSize.x);
            count = Clip(clipped[..count], hull, Vector2.left, HalfSize.x);
            count = Clip(hull[..count], clipped, Vector2.up, HalfSize.y);
            count = Clip(clipped[..count], hull, Vector2.down, -bottom);
            if (count == 0) return false;

            Vector2 centroid = Vector2.zero;
            for (int i = 0; i < count; i++) centroid += hull[i];
            centroid /= count;
            if ((!centerInView || (centroid - center).sqrMagnitude > 0.000001f) && hasLineOfSight(observer, FromView(centroid))) return true;

            for (int i = 0; i < count; i++)
            {
                Vector2 corner = hull[i];
                // Move a little inward so sight samples lie on the hull rather than just outside its edge.
                Vector2 edge = (corner + hull[(i + 1) % count]) * 0.5f;
                if (hasLineOfSight(observer, FromView(Vector2.Lerp(corner, centroid, 0.075f))) ||
                    hasLineOfSight(observer, FromView(Vector2.Lerp(edge, centroid, 0.075f)))) return true;
            }
            return false;
        }

        private Vector2 ToView(Vector2 offset) => new(Vector2.Dot(offset, _right), Vector2.Dot(offset, _up));

        private Vector2 FromView(Vector2 point) => Center + _right * point.x + _up * point.y;

        private Vector2 ProjectHullAxis(Vector2 axis, float squash)
        {
            Vector2 projected = ToView(axis);
            projected.y *= squash;
            return projected;
        }

        private static int Clip(ReadOnlySpan<Vector2> polygon, Span<Vector2> result, Vector2 normal, float boundary)
        {
            if (polygon.IsEmpty) return 0;
            int written = 0;
            Vector2 previous = polygon[^1];
            float previousDistance = Vector2.Dot(previous, normal) - boundary;
            foreach (Vector2 point in polygon)
            {
                float distance = Vector2.Dot(point, normal) - boundary;
                if ((previousDistance < 0f && distance > 0f) || (previousDistance > 0f && distance < 0f))
                    result[written++] = Vector2.Lerp(previous, point, previousDistance / (previousDistance - distance));
                if (distance <= 0f) result[written++] = point;
                previous = point;
                previousDistance = distance;
            }
            return written;
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
            Func<Vector2, Vector2, bool> hasLineOfSight)
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
            Func<Vector2, Vector2, bool> hasLineOfSight)
        {
            if (player == null) return false;
            if (target.factionTeam == player.factionTeam) return true;

            if (CanSpot(observer) && viewport.CanSeeHull(target, observer.Position2D, hasLineOfSight)) return true;

            if (_multiplayer.IsInBattle)
                return HasSharedContact(networkTanks, player, observer, target);

            foreach (var teammate in tanks)
            {
                if (!CanSpot(teammate) || teammate == observer || teammate.factionTeam != player.factionTeam) continue;
                var teammateView = new BattleViewport(teammate.Position2D, BattleViewport.HalfSizeFor(teammate, aspect));
                if (teammateView.CanSeeHull(target, teammate.Position2D, hasLineOfSight))
                    return true;
            }
            return false;
        }

        internal void RefreshNetworkContacts(TankEntity[] tanks, Func<int, TankEntity, BattleViewport?> getViewport,
            Func<Vector2, Vector2, bool> hasLineOfSight)
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
                    if (viewport.CanSeeHull(target, observer.Position2D, hasLineOfSight))
                        NetworkContacts[observerSlot] |= (ushort)(1 << slot);
                }
            }
        }

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
