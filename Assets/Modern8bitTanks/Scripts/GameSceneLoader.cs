using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Modern8bitTanks
{
    public sealed class GameSceneLoader
    {
        private bool _loading;

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
                await SceneManager.LoadSceneAsync(scene);
            }
            finally
            {
                _loading = false;
            }
        }
    }
}
