using UnityEngine;

namespace Modern8bitTanks
{
    public sealed class BattleSpawnAssets : ScriptableObject
    {
        [SerializeField] private SpriteRenderer spriteEffect;
        [SerializeField] private DestructibleProp fallbackCover;
        [SerializeField] private DestructibleProp[] coverPrefabs;
        [SerializeField] private SpriteRenderer controlPointMarker;
        [SerializeField] private BattlefieldTheme desertTheme;
        [SerializeField] private BattlefieldTheme cityTheme;
        [SerializeField] private BattlefieldTheme forestTheme;
        [SerializeField] private BattlefieldPlan[] battlefieldPlans;

        internal SpriteRenderer ControlPointMarker => controlPointMarker;
        internal BattlefieldPlan[] BattlefieldPlans => battlefieldPlans;

        internal BattlefieldTheme GetBattlefieldTheme(BattleMap map) => map switch
        {
            BattleMap.City => cityTheme,
            BattleMap.Forest => forestTheme,
            _ => desertTheme
        };

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
