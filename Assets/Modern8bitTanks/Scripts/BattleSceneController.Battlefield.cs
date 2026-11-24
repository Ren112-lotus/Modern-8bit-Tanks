using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Modern8bitTanks
{
    public partial class BattleSceneController
    {
        // Covers the diagonal of the largest vehicle viewport and the camera's look-ahead.
        private const int ExteriorPaddingPx = 352;
        private const int ExteriorSampleScale = 2;
        [SerializeField] private SpriteRenderer _exteriorTerrainRenderer;
        private Texture2D _exteriorTexture;
        private Sprite _exteriorSprite;

        internal BattlefieldLayout CurrentLayout { get; private set; }
        private BattlefieldNavigation _battlefieldNavigation;

        private async UniTask PrepareBattlefieldLayoutAsync(BattleLoadBudget budget)
        {
            CurrentLayout = null;
            _battlefieldNavigation = null;
            if (!UsesControlPoints) return;

            int seed = _multiplayer.IsInBattle
                ? _multiplayer.Connection.Room.BattlefieldSeed
                : Random.Range(1, int.MaxValue);
            var theme = spawnAssets.GetBattlefieldTheme(battleMap);
            var plans = spawnAssets.BattlefieldPlans;
            var random = new System.Random(seed);
            string lastPlan = null;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                var plan = plans[random.Next(plans.Length)];
                lastPlan = plan.Name;
                var layout = new BattlefieldLayout(battleMap, random.Next(), plan, theme);
                var navigation = new BattlefieldNavigation(layout);
                await navigation.RebuildAsync(budget);
                if (!HasConnectedDeployment(layout, navigation)) continue;

                CurrentLayout = layout;
                _battlefieldNavigation = navigation;
                return;
            }

            throw new System.InvalidOperationException($"No connected deployment could be generated for {battleMap} ({lastPlan}).");
        }

        private static bool HasConnectedDeployment(BattlefieldLayout layout, BattlefieldNavigation navigation)
        {
            // Opposing spawn slots are rotations of these positions.
            for (int i = 0; i < BattlefieldLayout.DeploymentCount; i++)
                if (!navigation.CanReachControl(layout.Spawn(true, i))) return false;
            return true;
        }

        private Vector2 BattlefieldSpawn(bool west, int index) => CurrentLayout.Spawn(west, index) / PPU;

        private async UniTask RefreshExteriorTerrainAsync(BattleLoadBudget budget)
        {
            ReleaseExteriorTerrain();
            if (_exteriorTerrainRenderer == null || CurrentLayout == null) return;

            int width = (BattlefieldLayout.Width + ExteriorPaddingPx * 2) / ExteriorSampleScale;
            int height = (BattlefieldLayout.Height + ExteriorPaddingPx * 2) / ExteriorSampleScale;
            _exteriorTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Battlefield Exterior",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _exteriorTexture.SetPixels32(await CurrentLayout.ExteriorPixelsAsync(ExteriorPaddingPx, ExteriorSampleScale, budget));
            _exteriorTexture.Apply(false, true);
            _exteriorSprite = Sprite.Create(_exteriorTexture, new Rect(0, 0, width, height),
                new Vector2(0.5f, 0.5f), PPU / ExteriorSampleScale, 0, SpriteMeshType.FullRect);

            _exteriorTerrainRenderer.sharedMaterial = terrainRenderer.sharedMaterial;
            _exteriorTerrainRenderer.sortingLayerID = terrainRenderer.sortingLayerID;
            _exteriorTerrainRenderer.sortingOrder = terrainRenderer.sortingOrder - 1;
            _exteriorTerrainRenderer.sprite = _exteriorSprite;
            _exteriorTerrainRenderer.enabled = true;
        }

        private void ReleaseExteriorTerrain()
        {
            if (_exteriorTerrainRenderer != null)
            {
                _exteriorTerrainRenderer.enabled = false;
                _exteriorTerrainRenderer.sprite = null;
            }
            if (_exteriorSprite != null) Destroy(_exteriorSprite);
            if (_exteriorTexture != null) Destroy(_exteriorTexture);
            _exteriorSprite = null;
            _exteriorTexture = null;
        }

        private async UniTask SpawnProceduralCoverAsync(BattleLoadBudget budget)
        {
            foreach (var placement in CurrentLayout.Cover)
            {
                var prefab = placement.Prefab;
                SpawnMapProp(prefab.kind, placement.Position / PPU, prefab.meta, prefab.stack.sourceSheet,
                    placement.Angle, prefab);
                if (budget != null) await budget.YieldIfSpentAsync();
            }
        }
    }
}
