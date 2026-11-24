using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Modern8bitTanks
{
    internal sealed class BattleLoadBudget
    {
        private static readonly long BudgetTicks = Stopwatch.Frequency * 4 / 1000;
        private readonly CancellationToken _cancellationToken;
        private long _startedAt = Stopwatch.GetTimestamp();

        internal BattleLoadBudget(CancellationToken cancellationToken) => _cancellationToken = cancellationToken;

        internal UniTask YieldIfSpentAsync() =>
            Stopwatch.GetTimestamp() - _startedAt >= BudgetTicks ? NextFrameAsync() : UniTask.CompletedTask;

        private async UniTask NextFrameAsync()
        {
            await UniTask.NextFrame(cancellationToken: _cancellationToken);
            _startedAt = Stopwatch.GetTimestamp();
        }
    }
}
