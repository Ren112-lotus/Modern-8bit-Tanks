using VContainer;
using VContainer.Unity;

namespace Modern8bitTanks
{
    public sealed class GameLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<BattleSessionState>(Lifetime.Singleton);
            builder.Register<GameSceneLoader>(Lifetime.Singleton);
        }
    }
}
