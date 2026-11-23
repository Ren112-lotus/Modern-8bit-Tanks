using System;

using UnityEngine;

namespace Modern8bitTanks
{
    public sealed class BattleSpawnAssets : ScriptableObject
    {
        [SerializeField] private SpriteRenderer spriteEffect;
        [SerializeField] private DestructibleProp fallbackCover;
        [SerializeField] private DestructibleProp[] coverPrefabs;
        [SerializeField] private SpriteRenderer controlPointMarker;
        [SerializeField] private LaneMapAssets[] laneMaps;

        internal SpriteRenderer ControlPointMarker => controlPointMarker;

        internal LaneMapAssets GetLaneMap(BattleMap map)
        {
            foreach (var layout in laneMaps)
                if (layout.Map == map) return layout;

            throw new InvalidOperationException($"No lane map assets are assigned for {map}.");
        }

        public SpriteRenderer SpriteEffect => spriteEffect;

        public DestructibleProp GetCover(string kind)
        {
            foreach (var prefab in coverPrefabs)
            {
                if (prefab.kind == kind) return prefab;
            }
            return fallbackCover;
        }
    }
    [Serializable]
    internal sealed class LaneMapAssets
    {
        [SerializeField] private BattleMap map;
        [SerializeField] private DestructibleProp cover;
        [SerializeField] private LaneWallPlacement[] walls;

        internal BattleMap Map => map;
        internal DestructibleProp Cover => cover;
        internal LaneWallPlacement[] Walls => walls;
    }

    [Serializable]
    internal struct LaneWallPlacement
    {
        [SerializeField] private Vector2 positionPixels;
        [SerializeField] private DestructibleProp prefab;

        internal Vector2 PositionPixels => positionPixels;
        internal DestructibleProp Prefab => prefab;
    }

}
