using System;
using Unity.Netcode;
using UnityEngine;

namespace Modern8bitTanks
{
    public sealed partial class MultiplayerMatchTransport
    {
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

        internal enum RoundState : byte
        {
            Waiting = 0,
            AlphaWon = 1,
            BravoWon = 2,
            Playing = 3
        }

        // The receive buffers are reused. Consumers read them during the event callback.
        internal struct Snapshot
        {
            internal TankState[] Tanks;
            internal ShotState[] Projectiles;
            internal int ProjectileCount;
            internal RoundState RoundState;
            internal float HostTime;
            internal ushort[] Contacts;
            internal Vector2 Focus;
            internal ControlPointState ControlPoint;
            internal Vector2 ControlScores;
            internal bool WonByControl;
        }

        private const int TankStateBytes = 2 * sizeof(bool) + 9 * sizeof(float) + 2 * sizeof(int) + 2 * sizeof(byte);
        private const int ShotStateBytes = sizeof(ushort) + 2 * sizeof(float) + 2 * sizeof(short) + 2 * sizeof(byte);
        private const int SnapshotHeaderBytes = 6 * sizeof(float) + sizeof(byte) + sizeof(sbyte) +
            2 * sizeof(bool) + (MultiplayerSessionConnection.MaxPlayers + 1) * sizeof(ushort);
        private const int MaxSnapshotBytes = SnapshotHeaderBytes + sizeof(byte) +
            MultiplayerSessionConnection.MaxPlayers * TankStateBytes + MaxProjectiles * ShotStateBytes;

        private readonly TankState[] _receivedTanks = new TankState[MultiplayerSessionConnection.MaxPlayers];
        private readonly ShotState[] _receivedProjectiles = new ShotState[MaxProjectiles];
        private readonly ushort[] _receivedSpots = new ushort[MultiplayerSessionConnection.MaxPlayers];

        internal event Action<Snapshot> StateReceived;

        internal void Broadcast(in Snapshot snapshot)
        {
            if (!Ready || !IsHost) return;
            using var writer = CreateRoundWriter(MaxSnapshotBytes);
            WriteSnapshot(writer, snapshot);
            _network.CustomMessagingManager.SendNamedMessageToAll(StateMessage, writer, NetworkDelivery.UnreliableSequenced);
        }

        private static void WriteSnapshot(FastBufferWriter writer, in Snapshot snapshot)
        {
            ushort presentMask = 0;
            for (int i = 0; i < snapshot.Tanks.Length; i++)
                if (snapshot.Tanks[i].Present) presentMask |= (ushort)(1 << i);

            writer.WriteValueSafe(snapshot.HostTime);
            writer.WriteValueSafe((byte)snapshot.RoundState);
            foreach (ushort observerSpots in snapshot.Contacts) writer.WriteValueSafe(observerSpots);
            writer.WriteValueSafe(snapshot.Focus);
            writer.WriteValueSafe(snapshot.ControlScores);
            writer.WriteValueSafe(snapshot.WonByControl);
            // v5 includes this legacy float before the signed owner value.
            writer.WriteValueSafe((float)snapshot.ControlPoint.Owner);
            writer.WriteValueSafe(snapshot.ControlPoint.Owner);
            writer.WriteValueSafe(snapshot.ControlPoint.Contested);
            writer.WriteValueSafe(presentMask);
            foreach (var tank in snapshot.Tanks)
                if (tank.Present) WriteTankState(writer, tank);

            writer.WriteValueSafe((byte)snapshot.ProjectileCount);
            for (int i = 0; i < snapshot.ProjectileCount; i++)
                WriteShotState(writer, snapshot.Projectiles[i]);
        }

        private void OnState(ulong sender, FastBufferReader reader)
        {
            if (_network == null || _network.IsServer || sender != NetworkManager.ServerClientId
                || !ReadRound(ref reader, SnapshotHeaderBytes)) return;

            if (TryReadSnapshot(ref reader, out var snapshot))
                StateReceived?.Invoke(snapshot);
        }

        private bool TryReadSnapshot(ref FastBufferReader reader, out Snapshot snapshot)
        {
            snapshot = default;
            reader.ReadValueSafe(out snapshot.HostTime);
            reader.ReadValueSafe(out byte roundState);
            snapshot.RoundState = (RoundState)roundState;
            for (int observer = 0; observer < _receivedSpots.Length; observer++)
                reader.ReadValueSafe(out _receivedSpots[observer]);
            reader.ReadValueSafe(out snapshot.Focus);
            reader.ReadValueSafe(out snapshot.ControlScores);
            reader.ReadValueSafe(out snapshot.WonByControl);
            reader.ReadValueSafe(out float _);
            reader.ReadValueSafe(out snapshot.ControlPoint.Owner);
            reader.ReadValueSafe(out snapshot.ControlPoint.Contested);
            reader.ReadValueSafe(out ushort presentMask);

            for (int i = 0; i < _receivedTanks.Length; i++)
            {
                ref var tank = ref _receivedTanks[i];
                tank.Present = (presentMask & (1 << i)) != 0;
                if (!tank.Present) continue;
                if (!reader.TryBeginRead(TankStateBytes)) return false;
                ReadTankState(ref reader, ref tank);
            }

            if (!reader.TryBeginRead(sizeof(byte))) return false;
            reader.ReadValueSafe(out byte count);
            if (count > MaxProjectiles || !reader.TryBeginRead(count * ShotStateBytes)) return false;
            for (int i = 0; i < count; i++)
                _receivedProjectiles[i] = ReadShotState(ref reader);

            snapshot.Tanks = _receivedTanks;
            snapshot.Projectiles = _receivedProjectiles;
            snapshot.ProjectileCount = count;
            snapshot.Contacts = _receivedSpots;
            return true;
        }

        private static void WriteTankState(FastBufferWriter writer, in TankState tank)
        {
            writer.WriteValueSafe(tank.Alive);
            writer.WriteValueSafe(tank.X);
            writer.WriteValueSafe(tank.Y);
            writer.WriteValueSafe(tank.Hull);
            writer.WriteValueSafe(tank.Turret);
            writer.WriteValueSafe(tank.MoveSpeed);
            writer.WriteValueSafe(tank.Reload);
            writer.WriteValueSafe(tank.FireTime);
            writer.WriteValueSafe(tank.RepairTime);
            writer.WriteValueSafe(tank.ActiveReload);
            writer.WriteValueSafe(tank.Crew);
            writer.WriteValueSafe(tank.Modules);
            writer.WriteValueSafe(tank.Ammo);
            writer.WriteValueSafe(tank.Loss);
            writer.WriteValueSafe(tank.Extinguisher);
        }

        private static void ReadTankState(ref FastBufferReader reader, ref TankState tank)
        {
            reader.ReadValueSafe(out tank.Alive);
            reader.ReadValueSafe(out tank.X);
            reader.ReadValueSafe(out tank.Y);
            reader.ReadValueSafe(out tank.Hull);
            reader.ReadValueSafe(out tank.Turret);
            reader.ReadValueSafe(out tank.MoveSpeed);
            reader.ReadValueSafe(out tank.Reload);
            reader.ReadValueSafe(out tank.FireTime);
            reader.ReadValueSafe(out tank.RepairTime);
            reader.ReadValueSafe(out tank.ActiveReload);
            reader.ReadValueSafe(out tank.Crew);
            reader.ReadValueSafe(out tank.Modules);
            reader.ReadValueSafe(out tank.Ammo);
            reader.ReadValueSafe(out tank.Loss);
            reader.ReadValueSafe(out tank.Extinguisher);
        }

        private static void WriteShotState(FastBufferWriter writer, in ShotState shot)
        {
            writer.WriteValueSafe(shot.Id);
            writer.WriteValueSafe(shot.Position.x);
            writer.WriteValueSafe(shot.Position.y);
            writer.WriteValueSafe((short)Mathf.RoundToInt(shot.Velocity.x * 256f));
            writer.WriteValueSafe((short)Mathf.RoundToInt(shot.Velocity.y * 256f));
            writer.WriteValueSafe((byte)shot.Kind);
            writer.WriteValueSafe((byte)shot.Ammo);
        }

        private static ShotState ReadShotState(ref FastBufferReader reader)
        {
            reader.ReadValueSafe(out ushort id);
            reader.ReadValueSafe(out float x);
            reader.ReadValueSafe(out float y);
            reader.ReadValueSafe(out short vx);
            reader.ReadValueSafe(out short vy);
            reader.ReadValueSafe(out byte kind);
            reader.ReadValueSafe(out byte ammo);
            return new ShotState
            {
                Id = id,
                Position = new Vector2(x, y),
                Velocity = new Vector2(vx, vy) / 256f,
                Kind = (ProjectileKind)kind,
                Ammo = (AmmoType)ammo
            };
        }
    }
}
