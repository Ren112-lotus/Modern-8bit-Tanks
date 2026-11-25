using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    public sealed class OnlineTankMarkers : MaskableGraphic
    {
        private static readonly Color32 Outline = new(22, 26, 31, 255);
        private readonly List<(Vector2 Position, Color32 Color)> _markers = new(MultiplayerSessionConnection.MaxPlayers - 1);

        internal void Render(Camera camera, List<TankEntity> tanks, BattleSceneController battle)
        {
            _markers.Clear();
            Rect area = rectTransform.rect;
            Vector2 up = camera.transform.up;
            foreach (var tank in tanks)
            {
                if (tank == null || tank.isPlayer || !tank.alive || !battle.IsTankVisible(tank)) continue;

                var meta = tank.meta;
                Vector2 forward = TankEntity.SliceAngleToDir(tank.hullAngleDeg);
                float hullHalfHeight = (Mathf.Abs(Vector2.Dot(forward, up)) * meta.hullLength +
                    Mathf.Abs(Vector2.Dot(Vector2.Perpendicular(forward), up)) * meta.hullWidth) * battle.squash * 0.5f;
                float hullTop = (hullHalfHeight + meta.hullBaseHeight * battle.spacingPx) / tank.pixelsPerUnit;
                float turretTop = Vector2.Dot(tank.GetTurretPivotWorld(battle.squash) - tank.Position2D, up) +
                    (meta.hullBaseHeight + meta.turretSlices) * battle.spacingPx / tank.pixelsPerUnit;
                Vector2 position = tank.Position2D + up * Mathf.Max(hullTop, turretTop);
                Vector3 viewport = camera.WorldToViewportPoint(position);
                Vector2 point = Rect.NormalizedToPoint(area, viewport);
                point.x = Mathf.Round(point.x);
                point.y = Mathf.Round(point.y) + 2f;
                if (point.x < area.xMin + 5f || point.x > area.xMax - 5f ||
                    point.y < area.yMin || point.y > area.yMax - 6f) continue;

                bool friendly = (tank.factionTeam == "ally") == battle.IsLocalAlpha;
                _markers.Add((point, OnlineTeamColors.ForRelation(friendly)));
            }
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            foreach (var marker in _markers)
            {
                AddTriangle(mesh, marker.Position, 5, 6, Outline);
                AddTriangle(mesh, marker.Position + Vector2.up * 2f, 3, 3, marker.Color);
            }
        }

        private static void AddTriangle(VertexHelper mesh, Vector2 tip, int halfWidth, int height, Color32 color)
        {
            int first = mesh.currentVertCount;
            mesh.AddVert(tip, color, Vector2.zero);
            mesh.AddVert(tip + new Vector2(-halfWidth, height), color, Vector2.zero);
            mesh.AddVert(tip + new Vector2(halfWidth, height), color, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2);
        }
    }
}
