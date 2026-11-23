using UnityEngine;

namespace Modern8bitTanks
{
    public partial class BattleSceneController
    {
        internal BattlefieldLayout CurrentLayout { get; private set; }

        private void PrepareBattlefieldLayout()
        {
            CurrentLayout = null;
            if (!UsesControlPoints) return;

            int seed = _multiplayer.IsInBattle
                ? _multiplayer.Connection.Room.BattlefieldSeed
                : Random.Range(1, int.MaxValue);
            CurrentLayout = new BattlefieldLayout(battleMap, seed);
        }

        private Vector2 LaneSpawn(bool west, int index) => CurrentLayout.Spawn(west, index) / PPU;

        private void SpawnProceduralCover()
        {
            foreach (var placement in CurrentLayout.PlaceCover(spawnAssets.ProceduralCover))
            {
                var prefab = placement.Prefab;
                SpawnMapProp(prefab.kind, placement.Position / PPU, prefab.meta, prefab.stack.sourceSheet,
                    placement.Angle, prefab);
            }
        }
    }
}
