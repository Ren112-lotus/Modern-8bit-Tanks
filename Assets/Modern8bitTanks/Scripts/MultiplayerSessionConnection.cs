using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Modern8bitTanks
{
    /// <summary>Owns one client-hosted network session; the lobby supplies the join code.</summary>
    public sealed class MultiplayerSessionConnection : MonoBehaviour
    {
        public const int MaxPlayers = 8;
        private ISession _session;
        private bool _busy;

        public bool IsConnected => _session != null;
        public string JoinCode => _session?.Code;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private static async Task InitializeAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync(new InitializationOptions().SetProfile(Guid.NewGuid().ToString("N").Substring(0, 24)));
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            if (NetworkManager.Singleton == null)
                throw new InvalidOperationException("A configured NetworkManager is required before starting a session.");
            if (NetworkManager.Singleton.NetworkConfig.NetworkTransport is not UnityTransport transport)
                throw new InvalidOperationException("The NetworkManager requires Unity Transport.");
            transport.UseWebSockets = true;
        }

        public async Task<string> CreateAsync()
        {
            if (_busy || _session != null) throw new InvalidOperationException("A session is already active or starting.");
            _busy = true;
            try
            {
                await InitializeAsync();
                var options = new SessionOptions { MaxPlayers = MaxPlayers, IsPrivate = true }
                    .WithRelayNetwork()
                    .WithNetworkOptions(new NetworkOptions { RelayProtocol = RelayProtocol.WSS });
                _session = await MultiplayerService.Instance.CreateSessionAsync(options);
                return _session.Code;
            }
            finally { _busy = false; }
        }

        public async Task JoinAsync(string code)
        {
            if (_busy || _session != null) throw new InvalidOperationException("A session is already active or starting.");
            if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("A join code is required.", nameof(code));
            _busy = true;
            try
            {
                await InitializeAsync();
                var options = new JoinSessionOptions()
                    .WithNetworkOptions(new NetworkOptions { RelayProtocol = RelayProtocol.WSS });
                _session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant(), options);
            }
            finally { _busy = false; }
        }

        public async Task LeaveAsync()
        {
            if (_busy) throw new InvalidOperationException("A session operation is already in progress.");
            if (_session == null) return;
            _busy = true;
            try
            {
                await _session.LeaveAsync();
                _session = null;
            }
            finally { _busy = false; }
        }
    }
}
