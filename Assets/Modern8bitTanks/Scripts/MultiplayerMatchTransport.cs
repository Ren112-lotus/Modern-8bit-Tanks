using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Modern8bitTanks
{
    /// <summary>Transports player controls to the host and authoritative battle state to clients.</summary>
    public sealed partial class MultiplayerMatchTransport : MonoBehaviour
    {
        public struct Control
        {
            public byte Buttons;
            public byte Arcade;
            public sbyte Ammo;
            public float AimX, AimY;
            public Vector2 ViewCenter;
            public float ViewRoll;
        }

        public const int MaxProjectiles = 32;
        public const float SnapshotInterval = 1f / 20f;
        private const float ControlInterval = 1f / 30f;
        private const float ControlHeartbeat = 0.15f;
        private const byte ActionMask = 0xC0;
        private const int ControlBytes = 2 * sizeof(byte) + 5 * sizeof(float);
        private const int RoundBytes = sizeof(ushort);
        private const string ControlMessage = "m8t.control.v4";
        private const string ActionMessage = "m8t.actions.v2";
        private const string StateMessage = "m8t.state.v5";
        private const string PropMessage = "m8t.prop.v1";
        private const string SmokeMessage = "m8t.smoke.v1";
        private const string WeaponMessage = "m8t.weapon.v1";
        private const string ReadyMessage = "m8t.ready.v1";
        private const string EffectMessage = "m8t.effect.v1";
        private readonly Control[] _controls = new Control[MultiplayerSessionConnection.MaxPlayers];
        private readonly float[] _lastControl = new float[MultiplayerSessionConnection.MaxPlayers];
        private readonly float[] _pendingActionTimes = new float[MultiplayerSessionConnection.MaxPlayers];
        private readonly Control[] _pendingActions = new Control[MultiplayerSessionConnection.MaxPlayers];
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
        private readonly bool[] _briefingReady = new bool[MultiplayerSessionConnection.MaxPlayers];
        public bool AllExpectedReady
        {
            get
            {
                if (_room == null || _room.ExpectedMask == 0) return false;
                for (var i = 0; i < _briefingReady.Length; i++)
                    if ((_room.ExpectedMask & (1 << i)) != 0 && IsOccupied(i) && !_briefingReady[i]) return false;
                return true;
            }
        }
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
            network.CustomMessagingManager.RegisterNamedMessageHandler(BriefingInkMessage, OnBriefingDrawing);
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
            bool viewChanged = (control.ViewCenter - _lastSentControl.ViewCenter).sqrMagnitude >= 0.0004f ||
                Mathf.Abs(Mathf.DeltaAngle(control.ViewRoll, _lastSentControl.ViewRoll)) >= 0.5f;
            if (buttonsChanged || viewChanged || dx * dx + dy * dy >= 0.0004f || now - _lastSendTime >= ControlHeartbeat)
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
            writer.WriteValueSafe(control.ViewCenter);
            writer.WriteValueSafe(control.ViewRoll);
            _network.CustomMessagingManager.SendNamedMessage(ControlMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.UnreliableSequenced);
        }

        public void MarkBriefingReady()
        {
            if (!Ready || !UseRound(_room.Round)) return;
            if (IsHost)
            {
                var slot = _room.LocalSlot;
                if (slot >= 0) _briefingReady[slot] = true;
                return;
            }
            using var writer = CreateRoundWriter(0);
            _network.CustomMessagingManager.SendNamedMessage(ReadyMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
        }

        private void OnReady(ulong sender, FastBufferReader reader)
        {
            if (!IsHost || !ReadRound(ref reader, 0)) return;
            var slot = _room.GetSlot(sender);
            if (slot >= 0) _briefingReady[slot] = true;
        }

        public bool TryGetControl(int slot, out Control control)
        {
            control = default;
            if (!IsHost || slot < 0 || slot >= _briefingReady.Length || !IsOccupied(slot) || Time.unscaledTime - _lastControl[slot] > 0.5f) return false;
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

        private void OnControl(ulong sender, FastBufferReader reader)
        {
            if (!IsHost || !ReadRound(ref reader, ControlBytes)) return;
            var slot = _room.GetSlot(sender);
            if (slot < 0) return;
            reader.ReadValueSafe(out byte buttons);
            reader.ReadValueSafe(out byte arcade);
            reader.ReadValueSafe(out float aimX);
            reader.ReadValueSafe(out float aimY);
            reader.ReadValueSafe(out Vector2 viewCenter);
            reader.ReadValueSafe(out float viewRoll);
            if (!float.IsFinite(aimX) || !float.IsFinite(aimY) ||
                !float.IsFinite(viewCenter.x) || !float.IsFinite(viewCenter.y) || !float.IsFinite(viewRoll)) return;
            _controls[slot] = new Control
            {
                Buttons = (byte)(buttons & ~ActionMask), Arcade = arcade, Ammo = -1, AimX = aimX, AimY = aimY,
                ViewCenter = viewCenter, ViewRoll = Mathf.Repeat(viewRoll, 360f)
            };
            _lastControl[slot] = Time.unscaledTime;
        }

        private void OnActions(ulong sender, FastBufferReader reader)
        {
            if (!IsHost || !ReadRound(ref reader, 2)) return;
            var slot = _room.GetSlot(sender);
            if (slot < 0 || !_briefingReady[slot]) return;
            reader.ReadValueSafe(out byte buttons);
            reader.ReadValueSafe(out sbyte ammo);
            _pendingActionTimes[slot] = Time.unscaledTime;
            _pendingActions[slot].Buttons |= (byte)(buttons & ActionMask);
            if (ammo is >= 0 and < 3) _pendingActions[slot].Ammo = ammo;
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
            if (!Ready || !IsHost || slot < 0 || slot >= _briefingReady.Length) return;
            using var writer = CreateRoundWriter(1);
            writer.WriteValueSafe((byte)slot);
            _network.CustomMessagingManager.SendNamedMessageToAll(SmokeMessage, writer, NetworkDelivery.ReliableSequenced);
        }

        private void OnSmoke(ulong sender, FastBufferReader reader)
        {
            if (_network == null || _network.IsServer || sender != NetworkManager.ServerClientId || !ReadRound(ref reader, 1)) return;
            reader.ReadValueSafe(out byte slot);
            if (slot < _briefingReady.Length) SmokeReceived?.Invoke(slot);
        }

        public void BroadcastWeaponFired(int slot, ProjectileKind kind, AmmoType ammo, Vector2 muzzle, Vector2 ground, Vector2 direction)
        {
            if (!Ready || !IsHost || slot < 0 || slot >= _briefingReady.Length) return;
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
            if (slot < _briefingReady.Length) WeaponFired?.Invoke(slot, (ProjectileKind)kind, (AmmoType)ammo, muzzle, ground, direction);
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
            ResetBriefingDrawing();
            Array.Clear(_briefingReady, 0, _briefingReady.Length);
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
            ResetBriefingDrawing();
            if (messages == null) return;
            messages.UnregisterNamedMessageHandler(ControlMessage);
            messages.UnregisterNamedMessageHandler(ActionMessage);
            messages.UnregisterNamedMessageHandler(StateMessage);
            messages.UnregisterNamedMessageHandler(PropMessage);
            messages.UnregisterNamedMessageHandler(SmokeMessage);
            messages.UnregisterNamedMessageHandler(WeaponMessage);
            messages.UnregisterNamedMessageHandler(ReadyMessage);
            messages.UnregisterNamedMessageHandler(EffectMessage);
            messages.UnregisterNamedMessageHandler(BriefingInkMessage);
        }
    }
}
