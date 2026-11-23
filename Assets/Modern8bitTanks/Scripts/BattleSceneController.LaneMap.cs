using UnityEngine;

namespace Modern8bitTanks
{
    public partial class BattleSceneController
    {
        private static readonly Vector2[] CoverAnchors =
        {
            new(301, 520), new(460, 591), new(622, 572), new(895, 572), new(1043, 528),
            new(257, 133), new(377, 79), new(567, 250), new(702, 290), new(796, 327),
            new(902, 362), new(1022, 149), new(1147, 256), new(515, 355),
            new(100, 632), new(139, 743), new(413, 782), new(825, 777), new(1166, 730)
        };

        private BattlefieldLayout CurrentLayout => BattlefieldLayout.For(battleMap);
        private Vector2 LaneSpawn(bool west, int index) => CurrentLayout.Spawn(west, index) / PPU;
        internal bool IsLaneRoute(float x, float y) =>
            CurrentLayout.IsRoad(new Vector2(x * BattlefieldLayout.Width, y * BattlefieldLayout.Height));

        private void BuildLaneLayout(BattleMap map)
        {
            var layout = spawnAssets.GetLaneMap(map);
            foreach (var wall in layout.Walls)
            {
                var prefab = wall.Prefab;
                SpawnMapProp(prefab.kind, wall.PositionPixels / PPU, prefab.meta, prefab.stack.sourceSheet, 0f, prefab);
            }

            var (feature, cover, detail) = map switch
            {
                BattleMap.City => ("city_factory", "city_apartment", "city_blockhouse"),
                BattleMap.Forest => ("forest_boulder", "pine_cluster", "forest_cabin"),
                _ => ("desert_mesa", "desert_compound", "desert_bunker")
            };
            var laneCover = layout.Cover;
            string[] kinds = { feature, cover, laneCover.kind, detail, laneCover.kind };
            var random = new System.Random((int)map * 1117 + 283);

            for (int i = 0; i < CoverAnchors.Length; i++)
            {
                string kind = kinds[(i + (int)map) % kinds.Length];
                var prefab = kind == laneCover.kind ? laneCover : null;
                PropMetaEntry meta = prefab != null ? prefab.meta : _propsMeta?.GetByKind(kind);
                Texture2D texture = prefab != null ? prefab.stack.sourceSheet : GetPropSheet(kind);
                if (meta == null || texture == null) continue;
                float radius = Mathf.Max(meta.width, meta.length) * 0.52f;
                for (int attempt = 0; attempt < 34; attempt++)
                {
                    Vector2 jitter = new(random.Next(-54, 55), random.Next(-50, 51));
                    Vector2 position = CoverAnchors[i] + jitter;
                    if (!CurrentLayout.CanPlaceCover(position, radius) || !HasCoverSpacing(position, radius))
                        continue;
                    float angle = map == BattleMap.City ? random.Next(0, 4) * 90f : random.Next(-31, 32);
                    SpawnMapProp(kind, position / PPU, meta, texture, angle, prefab);
                    break;
                }
            }
        }

        private bool HasCoverSpacing(Vector2 pixels, float radius)
        {
            foreach (var prop in _props)
            {
                if (Vector2.Distance(pixels, (Vector2)prop.transform.position * PPU) < radius + prop.approxRadiusUnits * PPU + 16f)
                    return false;
            }
            return true;
        }
    }
}
