using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Modern8bitTanks
{
    [DefaultExecutionOrder(-5000)]
    public sealed class GameSceneScope : LifetimeScope
    {
        [SerializeField] private Camera worldCamera;
        [SerializeField] private PixelWindowbox presentation;
        [SerializeField] private HangarController hangar;
        [SerializeField] private DesertBattleManager battle;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponent(worldCamera);
            builder.RegisterComponent(presentation);
            builder.RegisterComponent(presentation.GetComponent<BattlePostProcess>());
            builder.RegisterComponent(presentation.GetComponent<DesertAtmosphere>());
            if (hangar != null) builder.RegisterComponent(hangar);
            if (battle != null) builder.RegisterComponent(battle);
        }
    }
}
