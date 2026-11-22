using System;
using System.Runtime.InteropServices;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Modern8bitTanks
{
    /// <summary>Starts an online session when the WebGL player is launched by the room page.</summary>
    public sealed class MultiplayerWebBootstrap : MonoBehaviour
    {
        [Serializable]
        private sealed class LaunchOptions
        {
            public string role;
            public string sessionCode;
            public int slot = -1;
            public int expectedMask;
        }

        public static int LocalSlot { get; private set; } = -1;
        public static int ExpectedMask { get; private set; }
        public static bool Online => LocalSlot >= 0;

        internal static void NotifyDisconnected()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            TanksNotifyRoom("error", "ホストとの接続が切れました。");
#endif
        }

        internal static void RequestExit()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            TanksNotifyRoom("leave", "");
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern string TanksGetLaunchOptions();
        [DllImport("__Internal")] private static extern void TanksNotifyRoom(string kind, string value);
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var raw = TanksGetLaunchOptions();
            if (string.IsNullOrEmpty(raw)) return;
            LaunchOptions options;
            try { options = JsonUtility.FromJson<LaunchOptions>(raw); }
            catch (ArgumentException) { TanksNotifyRoom("error", "起動情報を読み取れませんでした。"); return; }
            if (options == null || options.slot < 0 || options.slot >= MultiplayerSessionConnection.MaxPlayers ||
                (options.role != "host" && options.role != "client") || options.expectedMask < 1 || options.expectedMask > 255 ||
                (options.expectedMask & (1 << options.slot)) == 0)
            {
                TanksNotifyRoom("error", "ルームの起動情報が正しくありません。");
                return;
            }
            var root = new GameObject("MultiplayerSession");
            var boot = root.AddComponent<MultiplayerWebBootstrap>();
            boot.Begin(options);
#endif
        }

        private async void Begin(LaunchOptions options)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            DontDestroyOnLoad(gameObject);
            try
            {
                if (NetworkManager.Singleton != null)
                    throw new InvalidOperationException("ネットワークが既に開始されています。");
                var transport = gameObject.AddComponent<UnityTransport>();
                var network = gameObject.AddComponent<NetworkManager>();
                network.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
                var session = gameObject.AddComponent<MultiplayerSessionConnection>();
                LocalSlot = options.slot;
                ExpectedMask = options.expectedMask;
                if (options.role == "host")
                {
                    var code = await session.CreateAsync();
                    gameObject.AddComponent<MultiplayerMatchTransport>().Initialize(network, options.slot);
                    TanksNotifyRoom("session-created", code);
                }
                else
                {
                    await session.JoinAsync(options.sessionCode);
                    gameObject.AddComponent<MultiplayerMatchTransport>().Initialize(network, options.slot);
                    TanksNotifyRoom("session-joined", "");
                }
                SceneManager.LoadScene(BattleSessionState.BattleSceneName);
            }
            catch (Exception ex)
            {
                LocalSlot = -1;
                TanksNotifyRoom("error", ex.Message);
                Destroy(gameObject);
            }
#endif
        }
    }
}
