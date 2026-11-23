using System;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Modern8bitTanks
{
    public sealed class MultiplayerSessionConnection : MonoBehaviour
    {
        public const int MaxPlayers = 8;
        private const float RoomSyncTimeout = 15f;

        [SerializeField] private NetworkManager _network;
        [SerializeField] private MultiplayerMatchTransport _matchTransport;
        [SerializeField] private MultiplayerRoom _roomPrefab;

        private BattleSessionState _sessionState;
        private ISession _session;
        private bool _connecting = true;
        private bool _connectionLost;
        private bool _leaving;
        private bool _starting;
        private bool _loadingBattle;
        private bool _returnToHangar;
        private bool _unlockingLobby;
        private AsyncOperation _sceneLoad;
        private int _localSlot = -1;

        public static MultiplayerSessionConnection Active { get; private set; }
        public static bool Online => Active != null && Active._loadingBattle;
        public static int LocalSlot => Active != null ? Active._localSlot : -1;
        private static string _lastNotice;
        public MultiplayerRoom Room { get; private set; }
        public bool IsLeaving => _leaving;
        public bool IsHost => _network != null && _network.IsHost;
        public bool IsStarting => _starting || _unlockingLobby || (Room != null && Room.ExpectedMask != 0);
        public bool IsReturningToLobby => !_loadingBattle && _returnToHangar && !_leaving;
        public string JoinCode => _session?.Code;

        public async UniTask ConnectAsync(BattleSessionState sessionState, string code = null)
        {
            if (Active != null || NetworkManager.Singleton != null)
                throw new InvalidOperationException("A room is already open.");

            _lastNotice = null;
            var connection = Instantiate(this);
            DontDestroyOnLoad(connection.gameObject);
            connection._sessionState = sessionState;
            Active = connection;
            try
            {
                var network = connection._network;
                network.NetworkConfig.ProtocolVersion = 4;
                network.AddNetworkPrefab(connection._roomPrefab.gameObject);
                network.ConnectionApprovalCallback = connection.ApproveConnection;
                network.OnClientDisconnectCallback += connection.OnDisconnected;
                network.OnTransportFailure += connection.OnTransportFailure;

                if (UnityServices.State != ServicesInitializationState.Initialized)
                    await UnityServices.InitializeAsync(new InitializationOptions().SetProfile(Guid.NewGuid().ToString("N")[..24]));
                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();

                var networkOptions = new NetworkOptions { RelayProtocol = RelayProtocol.WSS };
                if (code == null)
                {
                    var options = new SessionOptions { MaxPlayers = MaxPlayers, IsPrivate = true }
                        .WithRelayNetwork()
                        .WithNetworkOptions(networkOptions);
                    connection._session = await MultiplayerService.Instance.CreateSessionAsync(options);
                    Instantiate(connection._roomPrefab).NetworkObject.Spawn();
                }
                else
                {
                    var options = new JoinSessionOptions().WithNetworkOptions(networkOptions);
                    connection._session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code, options);
                }

                var deadline = Time.realtimeSinceStartup + RoomSyncTimeout;
                await UniTask.WaitUntil(() => connection == null || connection._connectionLost ||
                    !connection._network.IsListening || (connection.Room != null && connection.Room.LocalSlot >= 0) ||
                    Time.realtimeSinceStartup >= deadline);

                if (connection == null || connection._connectionLost || connection.Room == null || connection.Room.LocalSlot < 0)
                    throw new InvalidOperationException("Could not synchronize the room. Check the code and try again.");
                connection.Room.SetVehicleSelection(sessionState.SelectedPlayerVehicleId, choosing: false);
                connection._connecting = false;
            }
            catch
            {
                if (connection != null) await connection.LeaveAsync();
                throw;
            }
        }

        public static string TakeNotice()
        {
            var notice = _lastNotice;
            _lastNotice = null;
            return notice;
        }

        internal void AttachRoom(MultiplayerRoom room)
        {
            Room = room;
            _matchTransport.Initialize(_network, room);
        }

        public async UniTask StartMatchAsync()
        {
            if (!IsHost || IsStarting || Room == null || !Room.CanDeploy || _leaving) return;
            _starting = true;
            var host = _session.AsHost();
            try
            {
                host.IsLocked = true;
                await host.SavePropertiesAsync();
                if (_leaving) return;
                if (Room != null && Room.TryDeploy()) return;

                host.IsLocked = false;
                await host.SavePropertiesAsync();
                throw new InvalidOperationException("At least one player per team is needed.");
            }
            catch
            {
                host.IsLocked = false;
                throw;
            }
            finally
            {
                _starting = false;
            }
        }

        private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            bool isLocalHost = request.ClientNetworkId == NetworkManager.ServerClientId;
            response.Approved = isLocalHost || (!_leaving && !IsStarting && Room != null && Room.PlayerCount < MaxPlayers);
            response.CreatePlayerObject = false;
            if (!response.Approved) response.Reason = "The room is full or has already started.";
        }

        private void Update()
        {
            if (_leaving || Room == null) return;
            if (_sceneLoad != null && !_sceneLoad.isDone) return;
            _sceneLoad = null;
            if (_loadingBattle && Room.ExpectedMask == 0)
            {
                _loadingBattle = false;
                Time.timeScale = 1f;
                _sessionState.HasEnteredGarage = true;
                _sceneLoad = SceneManager.LoadSceneAsync(BattleSessionState.HangarSceneName);
                return;
            }
            if (_loadingBattle || Room.ExpectedMask == 0 || Room.LocalSlot < 0) return;
            _localSlot = Room.LocalSlot;
            _loadingBattle = true;
            _returnToHangar = true;
            _sceneLoad = SceneManager.LoadSceneAsync(BattleSessionState.BattleSceneName);
        }

        internal void ReadyInLobby()
        {
            if (!IsReturningToLobby || Room == null || Room.ExpectedMask != 0) return;
            _returnToHangar = false;
            _localSlot = -1;
            Room.SetVehicleSelection(_sessionState.SelectedPlayerVehicleId, choosing: false);
        }

        public async UniTask ReturnToRoomAsync()
        {
            if (!IsHost || _leaving || !_loadingBattle || _unlockingLobby || Room == null) return;
            var host = _session.AsHost();
            _unlockingLobby = true;
            Room.ReturnToLobby();
            try
            {
                host.IsLocked = false;
                await host.SavePropertiesAsync();
            }
            catch (Exception exception)
            {
                if (!_leaving) _lastNotice = "ROOM REJOIN MAY REQUIRE A REMATCH.";
                Debug.LogWarning($"Could not reopen the room for new players: {exception.Message}");
            }
            finally { _unlockingLobby = false; }
        }

        public async UniTask LeaveAsync(string notice = null)
        {
            if (_leaving) return;
            _leaving = true;
            _lastNotice = notice;
            var session = _session;
            _session = null;
            if (_network != null && _network.IsListening) _network.Shutdown();

            if (_returnToHangar)
            {
                Time.timeScale = 1f;
                _sessionState.HasEnteredGarage = true;
                if (_loadingBattle) _ = SceneManager.LoadSceneAsync(BattleSessionState.HangarSceneName);
            }

            try
            {
                if (session != null)
                {
                    if (session.IsHost) await session.AsHost().DeleteAsync();
                    else await session.LeaveAsync();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not close the multiplayer session: {exception.Message}");
            }
            finally
            {
                if (Active == this) Active = null;
                Destroy(gameObject);
            }
        }

        private void OnDisconnected(ulong clientId)
        {
            if (!IsHost && (clientId == NetworkManager.ServerClientId || clientId == _network.LocalClientId))
                HandleConnectionLossAsync("Connection to the host was lost.").Forget();
        }

        private void OnTransportFailure()
        {
            HandleConnectionLossAsync("The network connection was lost.").Forget();
        }

        private async UniTask HandleConnectionLossAsync(string message)
        {
            if (_connecting) _connectionLost = true;
            else await LeaveAsync(message);
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
        }
    }
}
