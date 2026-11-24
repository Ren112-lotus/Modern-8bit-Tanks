using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Modern8bitTanks
{
    public sealed class GameSceneLoader
    {
        private readonly BattleSessionState _sessionState;
        private readonly MultiplayerSessionState _multiplayer;
        private BattleLoadingScreen _loadingScreen;
        private bool _loading;

        public bool IsLoading => _loading;

        public GameSceneLoader(BattleSessionState sessionState, MultiplayerSessionState multiplayer)
        {
            _sessionState = sessionState;
            _multiplayer = multiplayer;
        }

        public void HideLoadingOverlay()
        {
            if (_loadingScreen != null) _loadingScreen.HideImmediate();
        }

        public async UniTask LoadAsync(string scene)
        {
            if (_loading) return;
            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                Debug.LogError($"Scene '{scene}' is missing from the build settings.");
                return;
            }

            _loading = true;
            try
            {
                if (scene == BattleSessionState.BattleSceneName)
                {
                    await LoadBattleWithScreenAsync(scene);
                }
                else
                {
                    if (_loadingScreen != null) _loadingScreen.HideImmediate();
                    await SceneManager.LoadSceneAsync(scene);
                }
            }
            finally
            {
                _loading = false;
            }
        }

        private async UniTask LoadBattleWithScreenAsync(string scene)
        {
            if (_loadingScreen == null)
                _loadingScreen = BattleLoadingScreen.Create();

            bool isOnline = _multiplayer != null && _multiplayer.IsInBattle;
            float minDisplayTime = isOnline ? 0.72f : 1.15f;

            _loadingScreen.Prepare(_sessionState, _multiplayer);
            try
            {
                await _loadingScreen.FadeInAsync(0.20f);

                AsyncOperation operation = SceneManager.LoadSceneAsync(scene);
                if (operation == null)
                {
                    _loadingScreen.HideImmediate();
                    return;
                }

                operation.allowSceneActivation = false;
                float elapsed = 0f;
                float shownProgress = 0f;

                while (operation.progress < 0.9f || elapsed < minDisplayTime)
                {
                    elapsed += Mathf.Min(0.05f, Time.unscaledDeltaTime);
                    float loadProgress = Mathf.Clamp01(operation.progress / 0.9f);
                    float paceProgress = Mathf.Clamp01(elapsed / minDisplayTime);
                    float target = Mathf.Min(loadProgress, paceProgress) * 0.96f;
                    shownProgress = Mathf.MoveTowards(shownProgress, target, Time.unscaledDeltaTime * 2.4f);
                    _loadingScreen.SetProgress(shownProgress);
                    await UniTask.Yield(PlayerLoopTiming.Update);
                }

                _loadingScreen.SetProgress(1f);
                operation.allowSceneActivation = true;
                await operation;

                // Allow the new Battle scene to initialize and present a clean frame behind the overlay.
                await UniTask.Yield(PlayerLoopTiming.Update);
                await UniTask.Yield(PlayerLoopTiming.Update);

                await _loadingScreen.CelebrateAndFadeOutAsync(
                    holdDuration: isOnline ? 0.16f : 0.28f,
                    fadeDuration: 0.24f);
            }
            catch
            {
                if (_loadingScreen != null) _loadingScreen.HideImmediate();
                throw;
            }
        }
    }
}
