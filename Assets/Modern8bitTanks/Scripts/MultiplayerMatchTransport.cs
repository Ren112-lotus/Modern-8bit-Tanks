using System;
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

        public struct ShotState
        {
            public ushort Id;
            public Vector2 Position, Velocity;
            public ProjectileKind Kind;
            public AmmoType Ammo;
        }

        public const int MaxProjectiles = 32;
        public const float SnapshotInterval = 1f / 20f;
        private const float ControlInterval = 1f / 30f;
        private const float ControlHeartbeat = 0.15f;
        private const byte ActionMask = 0xC0;
        private const int TankStateBytes = 48;
        private const int ControlBytes = 2 * sizeof(byte) + 2 * sizeof(float);
        private const int ControlStateBytes = 3 * sizeof(float) + sizeof(sbyte) + 2 * sizeof(bool);
        private const int RoundBytes = sizeof(ushort);

        private const string ControlMessage = "m8t.control.v3";
        private const string ActionMessage = "m8t.actions.v2";
        private const string StateMessage = "m8t.state.v4";
        private const string PropMessage = "m8t.prop.v1";
        private const string SmokeMessage = "m8t.smoke.v1";
        private const string WeaponMessage = "m8t.weapon.v1";
        private const string ReadyMessage = "m8t.ready.v1";
        private const string EffectMessage = "m8t.effect.v1";
        private readonly Control[] _controls = new Control[MultiplayerSessionConnection.MaxPlayers];
        private readonly float[] _lastControl = new float[MultiplayerSessionConnection.MaxPlayers];
        private readonly float[] _pendingActionTimes = new float[MultiplayerSessionConnection.MaxPlayers];
        private readonly Control[] _pendingActions = new Control[MultiplayerSessionConnection.MaxPlayers];
        private readonly TankState[] _receivedTanks = new TankState[MultiplayerSessionConnection.MaxPlayers];
        private readonly ShotState[] _receivedProjectiles = new ShotState[MaxProjectiles];
        private readonly byte[] _receivedSpots = new byte[MultiplayerSessionConnection.MaxPlayers];
        private Control _lastSentControl;
        private Control _queuedActions = new() { Ammo = -1 };
        private float _nextControlTime;
        private float _lastSendTime = float.NegativeInfinity;
        private bool _sendingControls;
        private ushort _round;
        private NetworkManager _network;
        private MultiplayerRoom _room;
        public bool IsHost => _network != null && _network.IsServer;
        public bool Ready => _network != null && _network.IsListening;
        public float RoundTripSeconds => Ready ? _network.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId) * .001f : 0f;
        public bool IsOccupied(int slot) => _room != null && _room.IsOccupied(slot);
        private readonly bool[] _sceneReady = new bool[MultiplayerSessionConnection.MaxPlayers];
        public bool AllExpectedPresent
        {
            get
            {
                if (_room == null || _room.ExpectedMask == 0) return false;
                for (var i = 0; i < _sceneReady.Length; i++)
                    if ((_room.ExpectedMask & (1 << i)) != 0 && IsOccupied(i) && !_sceneReady[i]) return false;
                return true;
            }
        }
        internal event Action<TankState[], ShotState[], int, byte, float, byte[], Vector2, ControlPointState, Vector2, bool> StateReceived;
        internal event Action<BattleEffect> EffectReceived;
        public event Action<int, Vector2, float, bool, int> PropImpactReceived;
        public event Action<int> SmokeReceived;
        public event Action<int, ProjectileKind, AmmoType, Vector2, Vector2, Vector2> WeaponFired;

        public void Initialize(NetworkManager network, MultiplayerRoom room)
        {
            if (network == null || !network.IsListening || room == null)
                throw new InvalidOperationException("The session is not ready for gameplay.");
            if (_network == network && _room == room) return;
            Shutdown();
            _network = network;
            _room = room;
            Array.Fill(_lastControl, float.NegativeInfinity);
            Array.Fill(_pendingActions, new Control { Ammo = -1 });
            network.CustomMessagingManager.RegisterNamedMessageHandler(ControlMessage, OnControl);
            network.CustomMessagingManager.RegisterNamedMessageHandler(ActionMessage, OnActions);
            network.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage, OnState);
            network.CustomMessagingManager.RegisterNamedMessageHandler(PropMessage, OnPropImpact);
            network.CustomMessagingManager.RegisterNamedMessageHandler(SmokeMessage, OnSmoke);
            network.CustomMessagingManager.RegisterNamedMessageHandler(WeaponMessage, OnWeaponFired);
            network.CustomMessagingManager.RegisterNamedMessageHandler(ReadyMessage, OnReady);
            network.CustomMessagingManager.RegisterNamedMessageHandler(EffectMessage, OnEffect);
        }

        public void SendControl(Control control)
        {
            if (!Ready || IsHost || !UseRound(_room.Round)) return;

            _queuedActions.Buttons |= (byte)(control.Buttons & ActionMask);
            if (control.Ammo >= 0) _queuedActions.Ammo = control.Ammo;
            control.Buttons &= unchecked((byte)~ActionMask);
            control.Ammo = -1;

            float now = Time.unscaledTime;
            bool buttonsChanged = !_sendingControls || control.Buttons != _lastSentControl.Buttons || control.Arcade != _lastSentControl.Arcade;
            if (now < _nextControlTime && !buttonsChanged) return;
            _nextControlTime = now + ControlInterval;

            float dx = control.AimX - _lastSentControl.AimX;
            float dy = control.AimY - _lastSentControl.AimY;
            if (buttonsChanged || dx * dx + dy * dy >= 0.0004f || now - _lastSendTime >= ControlHeartbeat)
            {
                SendContinuousControl(control);
                _lastSendTime = now;
                _lastSentControl = control;
                _sendingControls = true;
            }

            if (_queuedActions.Buttons == 0 && _queuedActions.Ammo < 0) return;
            using var writer = CreateRoundWriter(2);
            writer.WriteValueSafe(_queuedActions.Buttons);
            writer.WriteValueSafe(_queuedActions.Ammo);
            _network.CustomMessagingManager.SendNamedMessage(ActionMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
            _queuedActions = new Control { Ammo = -1 };
        }

        public void StopSendingControls()
        {
            _queuedActions = new Control { Ammo = -1 };
            if (!Ready || !_sendingControls) return;

            _lastSentControl.Buttons = 0;
            SendContinuousControl(_lastSentControl);
            _sendingControls = false;
            _nextControlTime = 0f;
        }

        private void SendContinuousControl(Control control)
        {
            using var writer = CreateRoundWriter(ControlBytes);
            writer.WriteValueSafe(control.Buttons);
            writer.WriteValueSafe(control.Arcade);
            writer.WriteValueSafe(control.AimX);
            writer.WriteValueSafe(control.AimY);
            _network.CustomMessagingManager.SendNamedMessage(ControlMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.UnreliableSequenced);
        }

        public void MarkSceneReady()
        {
            if (!Ready || !UseRound(_room.Round)) return;
            if (IsHost)
            {
                var slot = _room.LocalSlot;
                if (slot >= 0) _sceneReady[slot] = true;
                return;
            }
            using var writer = CreateRoundWriter(0);
            _network.CustomMessagingManager.SendNamedMessage(ReadyMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
        }

        private void OnReady(ulong sender, FastBufferReader reader)
        {
            if (!IsHost || !ReadRound(ref reader, 0)) return;
            var slot = _room.GetSlot(sender);
            if (slot >= 0) _sceneReady[slot] = true;
        }

        public bool TryGetControl(int slot, out Control control)
        {
            control = default;
            if (!IsHost || slot < 0 || slot >= _sceneReady.Length || !IsOccupied(slot) || Time.unscaledTime - _lastControl[slot] > 0.5f) return false;
            control = _controls[slot];
            return true;
        }

        public bool TryConsumeControl(int slot, out Control control)
        {
            if (!TryGetControl(slot, out control)) return false;
            if (Time.unscaledTime - _pendingActionTimes[slot] <= 0.5f)
            {
                control.Buttons |= _pendingActions[slot].Buttons;
                control.Ammo = _pendingActions[slot].Ammo;
            }
            _pendingActions[slot] = new Control { Ammo = -1 };
            return true;
        }

        internal void Broadcast(TankState[] tanks, ShotState[] projectiles, int projectileCount, byte result,
            byte[] spots, Vector2 focus, ControlPointState controlPoint, Vector2 controlScores, bool wonByControl)
        {
            if (!Ready || !IsHost) return;
            byte presentMask = 0;
            for (int i = 0; i < tanks.Length; i++)
                if (tanks[i].Present) presentMask |= (byte)(1 << i);

            using var writer = CreateRoundWriter(1024);
            writer.WriteValueSafe(Time.unscaledTime);
            writer.WriteValueSafe(result);
            foreach (byte observerSpots in spots) writer.WriteValueSafe(observerSpots);
            writer.WriteValueSafe(focus);
            writer.WriteValueSafe(controlScores);
            writer.WriteValueSafe(wonByControl);
            writer.WriteValueSafe(controlPoint.Progress);
            writer.WriteValueSafe(controlPoint.Owner);
            writer.WriteValueSafe(controlPoint.Contested);
            writer.WriteValueSafe(presentMask);
            foreach (var tank in tanks)
            {
                if (!tank.Present) continue;
                writer.WriteValueSafe(tank.Alive);
                writer.WriteValueSafe(tank.X); writer.WriteValueSafe(tank.Y);
                writer.WriteValueSafe(tank.Hull); writer.WriteValueSafe(tank.Turret);
                writer.WriteValueSafe(tank.MoveSpeed); writer.WriteValueSafe(tank.Reload);
                writer.WriteValueSafe(tank.FireTime); writer.WriteValueSafe(tank.RepairTime);
                writer.WriteValueSafe(tank.ActiveReload);
                writer.WriteValueSafe(tank.Crew); writer.WriteValueSafe(tank.Modules);
                writer.WriteValueSafe(tank.Ammo); writer.WriteValueSafe(tank.Loss);
                writer.WriteValueSafe(tank.Extinguisher);
            }
            writer.WriteValueSafe((byte)projectileCount);
            for (int i = 0; i < projectileCount; i++)
            {
                var shot = projectiles[i];
                writer.WriteValueSafe(shot.Id);
                writer.WriteValueSafe(shot.Position.x); writer.WriteValueSafe(shot.Position.y);
                writer.WriteValueSafe((short)Mathf.RoundToInt(shot.Velocity.x * 256f));
                writer.WriteValueSafe((short)Mathf.RoundToInt(shot.Velocity.y * 256f));
                writer.WriteValueSafe((byte)shot.Kind); writer.WriteValueSafe((byte)shot.Ammo);
            }
            _network.CustomMessagingManager.SendNamedMessageToAll(StateMessage, writer, NetworkDelivery.UnreliableSequenced);
        }

        private void OnControl(ulong sender, FastBufferReader reader)
        {
            if (!IsHost || !ReadRound(ref reader, ControlBytes)) return;
            var slot = _room.GetSlot(sender);
            if (slot < 0) return;
            reader.ReadValueSafe(out byte buttons);
            reader.ReadValueSafe(out byte arcade);
            reader.ReadValueSafe(out float aimX);
            reader.ReadValueSafe(out float aimY);
            if (!float.IsFinite(aimX) || !float.IsFinite(aimY)) return;
            _controls[slot] = new Control
            {
                Buttons = (byte)(buttons & ~ActionMask), Arcade = arcade, Ammo = -1, AimX = aimX, AimY = aimY
            };
            _lastControl[slot] = Time.unscaledTime;
        }

        private void OnActions(ulong sender, FastBufferReader reader)
        {
            if (!IsHost || !ReadRound(ref reader, 2)) return;
            var slot = _room.GetSlot(sender);
            if (slot < 0 || !_sceneReady[slot]) return;
            reader.ReadValueSafe(out byte buttons);
            reader.ReadValueSafe(out sbyte ammo);
            _pendingActionTimes[slot] = Time.unscaledTime;
            _pendingActions[slot].Buttons |= (byte)(buttons & ActionMask);
            if (ammo is >= 0 and < 3) _pendingActions[slot].Ammo = ammo;
        }

        private void OnState(ulong sender, FastBufferReader reader)
        {
            if (_network == null || _network.IsServer || sender != NetworkManager.ServerClientId
                || !ReadRound(ref reader, 15 + _receivedSpots.Length + ControlStateBytes)) return;
            reader.ReadValueSafe(out float hostTime);
            reader.ReadValueSafe(out byte result);
            for (int observer = 0; observer < _receivedSpots.Length; observer++)
                reader.ReadValueSafe(out _receivedSpots[observer]);
            reader.ReadValueSafe(out Vector2 focus);
            reader.ReadValueSafe(out Vector2 controlScores);
            reader.ReadValueSafe(out bool wonByControl);
            reader.ReadValueSafe(out float progress);
            reader.ReadValueSafe(out sbyte owner);
            reader.ReadValueSafe(out bool contested);
            var controlPoint = new ControlPointState { Progress = progress, Owner = owner, Contested = contested };
            reader.ReadValueSafe(out byte presentMask);
            var tanks = _receivedTanks;
            for (int i = 0; i < tanks.Length; i++)
            {
                tanks[i].Present = (presentMask & (1 << i)) != 0;
                if (!tanks[i].Present) continue;
                if (!reader.TryBeginRead(TankStateBytes)) return;
                reader.ReadValueSafe(out tanks[i].Alive);
                reader.ReadValueSafe(out tanks[i].X); reader.ReadValueSafe(out tanks[i].Y);
                reader.ReadValueSafe(out tanks[i].Hull); reader.ReadValueSafe(out tanks[i].Turret);
                reader.ReadValueSafe(out tanks[i].MoveSpeed); reader.ReadValueSafe(out tanks[i].Reload);
                reader.ReadValueSafe(out tanks[i].FireTime); reader.ReadValueSafe(out tanks[i].RepairTime);
                reader.ReadValueSafe(out tanks[i].ActiveReload);
                reader.ReadValueSafe(out tanks[i].Crew); reader.ReadValueSafe(out tanks[i].Modules);
                reader.ReadValueSafe(out tanks[i].Ammo); reader.ReadValueSafe(out tanks[i].Loss);
                reader.ReadValueSafe(out tanks[i].Extinguisher);
            }
            if (!reader.TryBeginRead(1)) return;
            reader.ReadValueSafe(out byte count);
            if (count > MaxProjectiles || !reader.TryBeginRead(count * 16)) return;
            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out ushort id);
                reader.ReadValueSafe(out float x);
                reader.ReadValueSafe(out float y);
                reader.ReadValueSafe(out short vx);
                reader.ReadValueSafe(out short vy);
                reader.ReadValueSafe(out byte kind);
                reader.ReadValueSafe(out byte ammo);
                _receivedProjectiles[i] = new ShotState { Id = id, Position = new Vector2(x, y), Velocity = new Vector2(vx, vy) / 256f,
                    Kind = (ProjectileKind)kind, Ammo = (AmmoType)ammo };
            }
            StateReceived?.Invoke(tanks, _receivedProjectiles, count, result, hostTime, _receivedSpots, focus,
                controlPoint, controlScores, wonByControl);
        }

        public void BroadcastPropImpact(int index, Vector2 position, float radius, bool heavy, int seed)
        {
            if (!Ready || !IsHost) return;
            using var writer = CreateRoundWriter(21);
            writer.WriteValueSafe(index); writer.WriteValueSafe(position.x); writer.WriteValueSafe(position.y);
            writer.WriteValueSafe(radius); writer.WriteValueSafe(heavy); writer.WriteValueSafe(seed);
            _network.CustomMessagingManager.SendNamedMessageToAll(PropMessage, writer, NetworkDelivery.ReliableSequenced);
        }

        private void OnPropImpact(ulong sender, FastBufferReader reader)
        {
            if (_network == null || _network.IsServer || sender != NetworkManager.ServerClientId || !ReadRound(ref reader, 21)) return;
            reader.ReadValueSafe(out int index); reader.ReadValueSafe(out float x); reader.ReadValueSafe(out float y);
            reader.ReadValueSafe(out float radius); reader.ReadValueSafe(out bool heavy); reader.ReadValueSafe(out int seed);
            PropImpactReceived?.Invoke(index, new Vector2(x, y), radius, heavy, seed);
        }

        public void BroadcastSmoke(int slot)
        {
            if (!Ready || !IsHost || slot < 0 || slot >= _sceneReady.Length) return;
            using var writer = CreateRoundWriter(1);
            writer.WriteValueSafe((byte)slot);
            _network.CustomMessagingManager.SendNamedMessageToAll(SmokeMessage, writer, NetworkDelivery.ReliableSequenced);
        }

        private void OnSmoke(ulong sender, FastBufferReader reader)
        {
            if (_network == null || _network.IsServer || sender != NetworkManager.ServerClientId || !ReadRound(ref reader, 1)) return;
            reader.ReadValueSafe(out byte slot);
            if (slot < _sceneReady.Length) SmokeReceived?.Invoke(slot);
        }

        public void BroadcastWeaponFired(int slot, ProjectileKind kind, AmmoType ammo, Vector2 muzzle, Vector2 ground, Vector2 direction)
        {
            if (!Ready || !IsHost || slot < 0 || slot >= _sceneReady.Length) return;
            using var writer = CreateRoundWriter(27);
            writer.WriteValueSafe((byte)slot);
            writer.WriteValueSafe((byte)kind);
            writer.WriteValueSafe((byte)ammo);
            writer.WriteValueSafe(muzzle);
            writer.WriteValueSafe(ground);
            writer.WriteValueSafe(direction);
            _network.CustomMessagingManager.SendNamedMessageToAll(WeaponMessage, writer, NetworkDelivery.Unreliable);
        }

        private void OnWeaponFired(ulong sender, FastBufferReader reader)
        {
            if (_network == null || _network.IsServer || sender != NetworkManager.ServerClientId || !ReadRound(ref reader, 27)) return;
            reader.ReadValueSafe(out byte slot);
            reader.ReadValueSafe(out byte kind);
            reader.ReadValueSafe(out byte ammo);
            reader.ReadValueSafe(out Vector2 muzzle);
            reader.ReadValueSafe(out Vector2 ground);
            reader.ReadValueSafe(out Vector2 direction);
            if (slot < _sceneReady.Length) WeaponFired?.Invoke(slot, (ProjectileKind)kind, (AmmoType)ammo, muzzle, ground, direction);
        }

        internal void PublishEffect(BattleEffect effect)
        {
            if (!Ready || !IsHost || _room.ExpectedMask == 0) return;
            using var writer = CreateRoundWriter(128);
            writer.WriteNetworkSerializable(effect);
            _network.CustomMessagingManager.SendNamedMessageToAll(EffectMessage, writer, NetworkDelivery.ReliableSequenced);
        }

        private void OnEffect(ulong sender, FastBufferReader reader)
        {
            if (_network == null || IsHost || sender != NetworkManager.ServerClientId || !ReadRound(ref reader, 0)) return;
            reader.ReadNetworkSerializable(out BattleEffect effect);
            EffectReceived?.Invoke(effect);
        }

        private FastBufferWriter CreateRoundWriter(int payloadBytes)
        {
            var writer = new FastBufferWriter(RoundBytes + payloadBytes, Allocator.Temp);
            writer.WriteValueSafe(_room.Round);
            return writer;
        }

        private bool ReadRound(ref FastBufferReader reader, int payloadBytes)
        {
            if (!reader.TryBeginRead(RoundBytes + payloadBytes)) return false;
            reader.ReadValueSafe(out ushort round);
            return UseRound(round);
        }

        private bool UseRound(ushort round)
        {
            if (_room.ExpectedMask == 0 || _room.Round != round) return false;
            if (_round == round) return true;
            _round = round;
            Array.Clear(_sceneReady, 0, _sceneReady.Length);
            Array.Fill(_lastControl, float.NegativeInfinity);
            Array.Fill(_pendingActionTimes, float.NegativeInfinity);
            Array.Fill(_pendingActions, new Control { Ammo = -1 });
            _queuedActions = new Control { Ammo = -1 };
            _sendingControls = false;
            _nextControlTime = 0f;
            return true;
        }

        private void OnDestroy() => Shutdown();

        internal void Shutdown()
        {
            var messages = _network != null ? _network.CustomMessagingManager : null;
            _network = null;
            _room = null;
            if (messages == null) return;
            messages.UnregisterNamedMessageHandler(ControlMessage);
            messages.UnregisterNamedMessageHandler(ActionMessage);
            messages.UnregisterNamedMessageHandler(StateMessage);
            messages.UnregisterNamedMessageHandler(PropMessage);
            messages.UnregisterNamedMessageHandler(SmokeMessage);
            messages.UnregisterNamedMessageHandler(WeaponMessage);
            messages.UnregisterNamedMessageHandler(ReadyMessage);
            messages.UnregisterNamedMessageHandler(EffectMessage);
        }
    }
}
