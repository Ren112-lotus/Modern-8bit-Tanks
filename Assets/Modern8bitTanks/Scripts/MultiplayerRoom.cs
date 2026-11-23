using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Modern8bitTanks
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class MultiplayerRoom : NetworkBehaviour
    {
        private const int TeamSize = MultiplayerSessionConnection.MaxPlayers / 2;

        public struct Member : INetworkSerializable, IEquatable<Member>
        {
            public ulong ClientId;
            public byte Slot;
            public FixedString64Bytes VehicleId;
            public bool ChoosingVehicle;
            public bool InLobby;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref ClientId);
                serializer.SerializeValue(ref Slot);
                serializer.SerializeValue(ref VehicleId);
                serializer.SerializeValue(ref ChoosingVehicle);
                serializer.SerializeValue(ref InLobby);
            }

            public bool Equals(Member other) => ClientId == other.ClientId && Slot == other.Slot &&
                VehicleId.Equals(other.VehicleId) && ChoosingVehicle == other.ChoosingVehicle && InLobby == other.InLobby;
        }

        private NetworkList<Member> _members;
        private readonly NetworkVariable<int> _battlefieldSeed = new();
        private readonly NetworkVariable<byte> _expectedMask = new();
        private readonly NetworkVariable<ushort> _round = new();
        private HashSet<FixedString64Bytes> _vehicleIds;

        public int ExpectedMask => _expectedMask.Value;
        internal int BattlefieldSeed => _battlefieldSeed.Value;
        public ushort Round => _round.Value;
        public int PlayerCount => _members.Count;
        public int LocalSlot => GetSlot(NetworkManager.LocalClientId);
        public bool CanDeploy
        {
            get
            {
                if (ExpectedMask != 0 || CountTeam(true) == 0 || CountTeam(false) == 0) return false;
                foreach (var member in _members)
                    if (!member.InLobby || member.ChoosingVehicle || member.VehicleId.IsEmpty) return false;
                return true;
            }
        }

        public int CountTeam(bool alpha)
        {
            var count = 0;
            foreach (var member in _members)
                if ((member.Slot < TeamSize) == alpha) count++;
            return count;
        }

        private void Awake()
        {
            _members = new NetworkList<Member>();
        }

        public override void OnNetworkSpawn()
        {
            DontDestroyOnLoad(gameObject);
            if (IsServer)
            {
                _vehicleIds = new HashSet<FixedString64Bytes>();
                foreach (var vehicle in MetaLoader.LoadVehicleCatalog().vehicles)
                    if (vehicle != null && !string.IsNullOrEmpty(vehicle.id)) _vehicleIds.Add(new FixedString64Bytes(vehicle.id));
                foreach (var clientId in NetworkManager.ConnectedClientsIds) AddClient(clientId);
                NetworkManager.OnClientConnectedCallback += AddClient;
                NetworkManager.OnClientDisconnectCallback += RemoveClient;
            }
            if (NetworkManager.TryGetComponent<MultiplayerSessionConnection>(out var connection))
                connection.AttachRoom(this);
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= AddClient;
                NetworkManager.OnClientDisconnectCallback -= RemoveClient;
                if (NetworkManager.TryGetComponent<MultiplayerSessionConnection>(out var connection))
                    connection.DetachRoom(this);
            }
        }

        public int GetSlot(ulong clientId)
        {
            foreach (var member in _members)
                if (member.ClientId == clientId) return member.Slot;
            return -1;
        }

        public string GetVehicleId(int slot)
        {
            foreach (var member in _members)
                if (member.Slot == slot) return member.VehicleId.IsEmpty ? null : member.VehicleId.ToString();
            return null;
        }

        public bool IsLocalChoosingVehicle
        {
            get
            {
                foreach (var member in _members)
                    if (member.ClientId == NetworkManager.LocalClientId) return member.ChoosingVehicle;
                return false;
            }
        }

        internal void SetVehicleSelection(string vehicleId, bool choosing) =>
            SelectVehicleRpc(Round, new FixedString64Bytes(vehicleId), choosing);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SelectVehicleRpc(ushort round, FixedString64Bytes vehicleId, bool choosing, RpcParams rpcParams = default)
        {
            if (round != Round || ExpectedMask != 0 || !_vehicleIds.Contains(vehicleId)) return;
            for (int i = 0; i < _members.Count; i++)
            {
                var member = _members[i];
                if (member.ClientId != rpcParams.Receive.SenderClientId) continue;
                member.ChoosingVehicle = choosing;
                member.InLobby = true;
                if (!choosing) member.VehicleId = vehicleId;
                if (!_members[i].Equals(member)) _members[i] = member;
                return;
            }
        }

        public bool IsOccupied(int slot) => (OccupiedMask() & (1 << slot)) != 0;

        private void AddClient(ulong clientId)
        {
            if (GetSlot(clientId) >= 0) return;
            if (ExpectedMask == 0 && PlayerCount < MultiplayerSessionConnection.MaxPlayers)
            {
                var firstSlot = CountTeam(true) <= CountTeam(false) ? 0 : TeamSize;
                var occupied = OccupiedMask();
                for (var slot = firstSlot; slot < firstSlot + TeamSize; slot++)
                {
                    if ((occupied & (1 << slot)) != 0) continue;
                    _members.Add(new Member { ClientId = clientId, Slot = (byte)slot });
                    return;
                }
            }
            NetworkManager.DisconnectClient(clientId);
        }

        private void RemoveClient(ulong clientId)
        {
            for (var i = 0; i < _members.Count; i++)
            {
                if (_members[i].ClientId != clientId) continue;
                _members.RemoveAt(i);
                return;
            }
        }

        private byte OccupiedMask()
        {
            byte mask = 0;
            foreach (var member in _members)
                mask |= (byte)(1 << member.Slot);
            return mask;
        }

        internal bool TryDeploy()
        {
            if (!IsServer || !CanDeploy) return false;
            for (int i = 0; i < _members.Count; i++)
            {
                var member = _members[i];
                member.InLobby = false;
                _members[i] = member;
            }
            _battlefieldSeed.Value = UnityEngine.Random.Range(1, int.MaxValue);
            _round.Value++;
            _expectedMask.Value = OccupiedMask();
            return true;
        }

        internal void ReturnToLobby()
        {
            if (IsServer) _expectedMask.Value = 0;
        }
    }
}
