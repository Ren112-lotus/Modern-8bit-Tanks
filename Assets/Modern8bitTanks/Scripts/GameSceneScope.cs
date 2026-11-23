using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Modern8bitTanks
{
    [DefaultExecutionOrder(-5000)]
    public sealed class GameSceneScope : LifetimeScope
    {
        [SerializeField] private Camera worldCamera;
        [SerializeField] private HangarController hangar;
        [SerializeField] private DesertBattleManager battle;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponent(worldCamera);
            if (hangar != null) builder.RegisterComponent(hangar);
            if (battle != null) builder.RegisterComponent(battle);
        }
    }
}
