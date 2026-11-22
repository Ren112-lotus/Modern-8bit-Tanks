using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Modern8bitTanks
{
    /// <summary>Transports player controls to the host and authoritative battle state to clients.</summary>
    public sealed class MultiplayerMatchTransport : MonoBehaviour
    {
        public struct Control
        {
            public byte Buttons;
            public byte Arcade;
            public sbyte Ammo;
            public float AimX, AimY;
        }

        public struct TankState
        {
            public bool Present, Alive;
            public float X, Y, Hull, Turret;
            public float MoveSpeed, Reload, FireTime, RepairTime, ActiveReload;
            public int Crew, Modules;
            public byte Ammo, Loss;
            public bool Extinguisher;
        }

        private const string HelloMessage = "m8t.hello.v1";
        private const string ControlMessage = "m8t.control.v1";
        private const string StateMessage = "m8t.state.v1";
        private const string PropMessage = "m8t.prop.v1";
        private const string SmokeMessage = "m8t.smoke.v1";
        private const string ReadyMessage = "m8t.ready.v1";
        private readonly Dictionary<ulong, int> _slots = new();
        private readonly Control[] _controls = new Control[MultiplayerSessionConnection.MaxPlayers];
        private readonly float[] _lastControl = new float[MultiplayerSessionConnection.MaxPlayers];
        private NetworkManager _network;
        public static MultiplayerMatchTransport Instance { get; private set; }
        public bool IsHost => _network != null && _network.IsServer;
        public bool Ready => _network != null && _network.IsListening;
        public readonly bool[] Occupied = new bool[MultiplayerSessionConnection.MaxPlayers];
        private readonly bool[] _sceneReady = new bool[MultiplayerSessionConnection.MaxPlayers];
        public bool AllExpectedPresent
        {
            get
            {
                var expected = MultiplayerWebBootstrap.ExpectedMask;
                for (var i = 0; i < Occupied.Length; i++)
                    if ((expected & (1 << i)) != 0 && (!Occupied[i] || !_sceneReady[i])) return false;
                return true;
            }
        }
        public event Action<TankState[], Vector2[], byte> StateReceived;
        public event Action<int, Vector2, float, bool, int> PropImpactReceived;
        public event Action<int> SmokeReceived;

        public void Initialize(NetworkManager network, int slot)
        {
            if (Instance != null && Instance != this) throw new InvalidOperationException("A match transport already exists.");
            if (network == null || !network.IsListening || slot < 0 || slot >= Occupied.Length)
                throw new InvalidOperationException("The session is not ready for gameplay.");
            Instance = this;
            _network = network;
            network.CustomMessagingManager.RegisterNamedMessageHandler(HelloMessage, OnHello);
            network.CustomMessagingManager.RegisterNamedMessageHandler(ControlMessage, OnControl);
            network.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage, OnState);
            network.CustomMessagingManager.RegisterNamedMessageHandler(PropMessage, OnPropImpact);
            network.CustomMessagingManager.RegisterNamedMessageHandler(SmokeMessage, OnSmoke);
            network.CustomMessagingManager.RegisterNamedMessageHandler(ReadyMessage, OnReady);
            network.OnClientDisconnectCallback += OnDisconnect;
            if (network.IsServer)
            {
                _slots[network.LocalClientId] = slot;
                Occupied[slot] = true;
            }
            else
            {
                using var writer = new FastBufferWriter(1, Allocator.Temp);
                writer.WriteValueSafe((byte)slot);
                network.CustomMessagingManager.SendNamedMessage(HelloMessage, NetworkManager.ServerClientId, writer);
            }
        }

        public void SendControl(Control control)
        {
            if (!Ready) return;
            if (IsHost)
            {
                if (_slots.TryGetValue(_network.LocalClientId, out var slot)) StoreControl(slot, control);
                return;
            }
            using var writer = new FastBufferWriter(11, Allocator.Temp);
            writer.WriteValueSafe(control.Buttons);
            writer.WriteValueSafe(control.Arcade);
            writer.WriteValueSafe(control.Ammo);
            writer.WriteValueSafe(control.AimX);
            writer.WriteValueSafe(control.AimY);
            _network.CustomMessagingManager.SendNamedMessage(ControlMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.UnreliableSequenced);
        }

        public void MarkSceneReady()
        {
            if (!Ready) return;
            if (IsHost)
            {
                if (_slots.TryGetValue(_network.LocalClientId, out var slot)) _sceneReady[slot] = true;
                return;
            }
            using var writer = new FastBufferWriter(1, Allocator.Temp);
            writer.WriteValueSafe((byte)1);
            _network.CustomMessagingManager.SendNamedMessage(ReadyMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
        }

        private void OnReady(ulong sender, FastBufferReader reader)
        {
            if (IsHost && _slots.TryGetValue(sender, out var slot) && reader.TryBeginRead(1)) _sceneReady[slot] = true;
        }

        public bool TryGetControl(int slot, out Control control)
        {
            control = default;
            if (!IsHost || slot < 0 || slot >= Occupied.Length || !Occupied[slot] || Time.unscaledTime - _lastControl[slot] > 0.5f) return false;
            control = _controls[slot];
            return true;
        }

        public void Broadcast(TankState[] tanks, Vector2[] projectiles, byte result)
        {
            if (!Ready || !IsHost || tanks == null || tanks.Length != Occupied.Length) return;
            var count = Math.Min(projectiles?.Length ?? 0, 32);
            using var writer = new FastBufferWriter(1024, Allocator.Temp);
            writer.WriteValueSafe(result);
            foreach (var tank in tanks)
            {
                writer.WriteValueSafe(tank.Present);
                writer.WriteValueSafe(tank.Alive);
                writer.WriteValueSafe(tank.X); writer.WriteValueSafe(tank.Y);
                writer.WriteValueSafe(tank.Hull); writer.WriteValueSafe(tank.Turret);
                writer.WriteValueSafe(tank.MoveSpeed); writer.WriteValueSafe(tank.Reload);
                writer.WriteValueSafe(tank.FireTime); writer.WriteValueSafe(tank.RepairTime); writer.WriteValueSafe(tank.ActiveReload);
                writer.WriteValueSafe(tank.Crew); writer.WriteValueSafe(tank.Modules);
                writer.WriteValueSafe(tank.Ammo); writer.WriteValueSafe(tank.Loss); writer.WriteValueSafe(tank.Extinguisher);
            }
            writer.WriteValueSafe((byte)count);
            for (var i = 0; i < count; i++) { writer.WriteValueSafe(projectiles[i].x); writer.WriteValueSafe(projectiles[i].y); }
            _network.CustomMessagingManager.SendNamedMessageToAll(StateMessage, writer, NetworkDelivery.UnreliableSequenced);
        }

        private void OnHello(ulong sender, FastBufferReader reader)
        {
            if (!IsHost || _slots.ContainsKey(sender) || !reader.TryBeginRead(1)) return;
            reader.ReadValueSafe(out byte slot);
            if (slot >= Occupied.Length || Occupied[slot] || (MultiplayerWebBootstrap.ExpectedMask != 0 && (MultiplayerWebBootstrap.ExpectedMask & (1 << slot)) == 0))
            {
                _network.DisconnectClient(sender);
                return;
            }
            _slots[sender] = slot;
            Occupied[slot] = true;
        }

        private void OnControl(ulong sender, FastBufferReader reader)
        {
            if (!IsHost || !_slots.TryGetValue(sender, out var slot) || !reader.TryBeginRead(11)) return;
            reader.ReadValueSafe(out byte buttons); reader.ReadValueSafe(out byte arcade); reader.ReadValueSafe(out sbyte ammo);
            reader.ReadValueSafe(out float aimX); reader.ReadValueSafe(out float aimY);
            if (float.IsNaN(aimX) || float.IsInfinity(aimX) || float.IsNaN(aimY) || float.IsInfinity(aimY)) return;
            StoreControl(slot, new Control { Buttons = buttons, Arcade = arcade, Ammo = ammo, AimX = aimX, AimY = aimY });
        }

        private void StoreControl(int slot, Control value)
        {
            _controls[slot] = value;
            _lastControl[slot] = Time.unscaledTime;
        }

        private void OnState(ulong sender, FastBufferReader reader)
        {
            if (_network == null || _network.IsServer || sender != NetworkManager.ServerClientId || !reader.TryBeginRead(1 + Occupied.Length * 49 + 1)) return;
            reader.ReadValueSafe(out byte result);
            var tanks = new TankState[Occupied.Length];
            for (var i = 0; i < tanks.Length; i++)
            {
                reader.ReadValueSafe(out tanks[i].Present); reader.ReadValueSafe(out tanks[i].Alive);
                reader.ReadValueSafe(out tanks[i].X); reader.ReadValueSafe(out tanks[i].Y);
                reader.ReadValueSafe(out tanks[i].Hull); reader.ReadValueSafe(out tanks[i].Turret);
                reader.ReadValueSafe(out tanks[i].MoveSpeed); reader.ReadValueSafe(out tanks[i].Reload);
                reader.ReadValueSafe(out tanks[i].FireTime); reader.ReadValueSafe(out tanks[i].RepairTime); reader.ReadValueSafe(out tanks[i].ActiveReload);
                reader.ReadValueSafe(out tanks[i].Crew); reader.ReadValueSafe(out tanks[i].Modules);
                reader.ReadValueSafe(out tanks[i].Ammo); reader.ReadValueSafe(out tanks[i].Loss); reader.ReadValueSafe(out tanks[i].Extinguisher);
            }
            reader.ReadValueSafe(out byte count);
            if (count > 32 || !reader.TryBeginRead(count * 8)) return;
            var projectiles = new Vector2[count];
            for (var i = 0; i < count; i++) { reader.ReadValueSafe(out float x); reader.ReadValueSafe(out float y); projectiles[i] = new Vector2(x, y); }
            StateReceived?.Invoke(tanks, projectiles, result);
        }

        public void BroadcastPropImpact(int index, Vector2 position, float radius, bool heavy, int seed)
        {
            if (!Ready || !IsHost) return;
            using var writer = new FastBufferWriter(21, Allocator.Temp);
            writer.WriteValueSafe(index); writer.WriteValueSafe(position.x); writer.WriteValueSafe(position.y);
            writer.WriteValueSafe(radius); writer.WriteValueSafe(heavy); writer.WriteValueSafe(seed);
            _network.CustomMessagingManager.SendNamedMessageToAll(PropMessage, writer, NetworkDelivery.ReliableSequenced);
        }

        private void OnPropImpact(ulong sender, FastBufferReader reader)
        {
            if (_network == null || _network.IsServer || sender != NetworkManager.ServerClientId || !reader.TryBeginRead(21)) return;
            reader.ReadValueSafe(out int index); reader.ReadValueSafe(out float x); reader.ReadValueSafe(out float y);
            reader.ReadValueSafe(out float radius); reader.ReadValueSafe(out bool heavy); reader.ReadValueSafe(out int seed);
            PropImpactReceived?.Invoke(index, new Vector2(x, y), radius, heavy, seed);
        }

        public void BroadcastSmoke(int slot)
        {
            if (!Ready || !IsHost || slot < 0 || slot >= Occupied.Length) return;
            using var writer = new FastBufferWriter(1, Allocator.Temp);
            writer.WriteValueSafe((byte)slot);
            _network.CustomMessagingManager.SendNamedMessageToAll(SmokeMessage, writer, NetworkDelivery.ReliableSequenced);
        }

        private void OnSmoke(ulong sender, FastBufferReader reader)
        {
            if (_network == null || _network.IsServer || sender != NetworkManager.ServerClientId || !reader.TryBeginRead(1)) return;
            reader.ReadValueSafe(out byte slot);
            if (slot < Occupied.Length) SmokeReceived?.Invoke(slot);
        }

        private void OnDisconnect(ulong id)
        {
            if (_network != null && !_network.IsServer && (id == NetworkManager.ServerClientId || id == _network.LocalClientId))
                MultiplayerWebBootstrap.NotifyDisconnected();
            if (!_slots.TryGetValue(id, out var slot)) return;
            Occupied[slot] = false;
            _sceneReady[slot] = false;
            _slots.Remove(id);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_network == null) return;
            _network.OnClientDisconnectCallback -= OnDisconnect;
            if (_network.CustomMessagingManager == null) return;
            _network.CustomMessagingManager.UnregisterNamedMessageHandler(HelloMessage);
            _network.CustomMessagingManager.UnregisterNamedMessageHandler(ControlMessage);
            _network.CustomMessagingManager.UnregisterNamedMessageHandler(StateMessage);
            _network.CustomMessagingManager.UnregisterNamedMessageHandler(PropMessage);
            _network.CustomMessagingManager.UnregisterNamedMessageHandler(SmokeMessage);
            _network.CustomMessagingManager.UnregisterNamedMessageHandler(ReadyMessage);
        }
    }
}
