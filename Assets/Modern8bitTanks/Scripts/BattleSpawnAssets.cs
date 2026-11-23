using UnityEngine;

namespace Modern8bitTanks
{
    public sealed class BattleSpawnAssets : ScriptableObject
    {
        [SerializeField] private SpriteRenderer spriteEffect;
        [SerializeField] private DestructibleProp fallbackCover;
        [SerializeField] private DestructibleProp[] coverPrefabs;
        [SerializeField] private SpriteRenderer controlPointMarker;
        [SerializeField] private DestructibleProp[] proceduralCover;

        internal SpriteRenderer ControlPointMarker => controlPointMarker;
        internal DestructibleProp[] ProceduralCover => proceduralCover;

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
}
