using UnityEngine;

namespace Modern8bitTanks
{
    public partial class BattleSceneController
    {
        internal BattlefieldLayout CurrentLayout { get; private set; }
        private BattlefieldNavigation _battlefieldNavigation;

        private void PrepareBattlefieldLayout()
        {
            CurrentLayout = null;
            _battlefieldNavigation = null;
            if (!UsesControlPoints) return;

            int seed = _multiplayer.IsInBattle
                ? _multiplayer.Connection.Room.BattlefieldSeed
                : Random.Range(1, int.MaxValue);
            var palette = spawnAssets.ProceduralCover;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                var layout = new BattlefieldLayout(battleMap, unchecked(seed + attempt), palette[0], palette[1]);
                var navigation = new BattlefieldNavigation(layout);
                navigation.Rebuild();
                if (!HasConnectedDeployment(layout, navigation)) continue;

                CurrentLayout = layout;
                _battlefieldNavigation = navigation;
                return;
            }

            throw new System.InvalidOperationException("Generated cover does not leave a reachable capture point.");
        }

        private static bool HasConnectedDeployment(BattlefieldLayout layout, BattlefieldNavigation navigation)
        {
            // Opposing spawn slots are rotations of these positions.
            for (int i = 0; i < BattlefieldLayout.DeploymentCount; i++)
                if (!navigation.CanReachControl(layout.Spawn(true, i))) return false;
            return true;
        }

        private Vector2 BattlefieldSpawn(bool west, int index) => CurrentLayout.Spawn(west, index) / PPU;

        private void SpawnProceduralCover()
        {
            foreach (var placement in CurrentLayout.Cover)
            {
                var prefab = placement.Prefab;
                SpawnMapProp(prefab.kind, placement.Position / PPU, prefab.meta, prefab.stack.sourceSheet,
                    placement.Angle, prefab);
            }
        }
    }
}
