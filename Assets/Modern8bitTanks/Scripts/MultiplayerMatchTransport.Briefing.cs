using System;
using Unity.Netcode;
using UnityEngine;

namespace Modern8bitTanks
{
    public sealed partial class MultiplayerMatchTransport
    {
        private const string BriefingInkMessage = "m8t.briefing.ink.v1";
        private const int InkPayloadBytes = sizeof(byte) + 2 * sizeof(ushort);
        private const int MaxPointsPerPlayer = BriefingInk.MaxPoints / MultiplayerSessionConnection.PlayersPerTeam;
        private readonly BriefingInk[] _roundInk = new BriefingInk[MultiplayerSessionConnection.MaxPlayers];
        private bool _briefingDrawingClosed;

        internal event Action BriefingDrawingChanged;

        internal BriefingInk GetBriefingInk(int slot) => IsBriefingSlot(slot) && IsLocalTeam(slot) ? _roundInk[slot] : null;

        internal void CloseBriefingDrawing() => _briefingDrawingClosed = true;

        internal void BeginBriefingDrawing()
        {
            if (Ready) UseRound(_room.Round);
        }

        internal bool SendBriefingDrawing(BriefingInkAction action, Vector2 point)
        {
            if (!Ready || !UseRound(_room.Round) || _briefingDrawingClosed) return false;
            int slot = _room.LocalSlot;
            if (!IsBriefingSlot(slot)) return false;

            ushort x = (ushort)Mathf.RoundToInt(Mathf.Clamp01(point.x) * ushort.MaxValue);
            ushort y = (ushort)Mathf.RoundToInt(Mathf.Clamp01(point.y) * ushort.MaxValue);
            if (IsHost) return AcceptBriefingDrawing(slot, action, x, y);
            if (!ApplyBriefingDrawing(slot, action, x, y)) return false;

            using var writer = CreateRoundWriter(InkPayloadBytes);
            WriteInk(writer, action, x, y);
            _network.CustomMessagingManager.SendNamedMessage(BriefingInkMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
            return true;
        }

        private void OnBriefingDrawing(ulong sender, FastBufferReader reader)
        {
            if (!Ready || (!IsHost && sender != NetworkManager.ServerClientId) ||
                !ReadRound(ref reader, InkPayloadBytes + (IsHost ? 0 : sizeof(byte))) || _briefingDrawingClosed) return;

            reader.ReadValueSafe(out byte operation);
            reader.ReadValueSafe(out ushort x);
            reader.ReadValueSafe(out ushort y);
            var action = (BriefingInkAction)operation;
            if (IsHost)
            {
                int slot = _room.GetSlot(sender);
                if (IsBriefingSlot(slot)) AcceptBriefingDrawing(slot, action, x, y);
                return;
            }

            reader.ReadValueSafe(out byte authorSlot);
            if (IsBriefingSlot(authorSlot) && authorSlot != _room.LocalSlot && IsLocalTeam(authorSlot))
                ApplyBriefingDrawing(authorSlot, action, x, y);
        }

        private bool AcceptBriefingDrawing(int slot, BriefingInkAction action, ushort x, ushort y)
        {
            if (!ApplyBriefingDrawing(slot, action, x, y)) return false;
            using var writer = CreateRoundWriter(InkPayloadBytes + sizeof(byte));
            WriteInk(writer, action, x, y);
            writer.WriteValueSafe((byte)slot);
            const int teamSize = MultiplayerSessionConnection.PlayersPerTeam;
            int first = MultiplayerSessionConnection.IsAlphaSlot(slot) ? 0 : teamSize;
            for (int teammate = first; teammate < first + teamSize; teammate++)
            {
                if (teammate == slot || !_room.TryGetMemberAtSlot(teammate, out var member) ||
                    member.ClientId == NetworkManager.ServerClientId) continue;
                _network.CustomMessagingManager.SendNamedMessage(BriefingInkMessage, member.ClientId, writer, NetworkDelivery.ReliableSequenced);
            }
            return true;
        }

        private static void WriteInk(FastBufferWriter writer, BriefingInkAction action, ushort x, ushort y)
        {
            writer.WriteValueSafe((byte)action);
            writer.WriteValueSafe(x);
            writer.WriteValueSafe(y);
        }

        private bool ApplyBriefingDrawing(int slot, BriefingInkAction action, ushort x, ushort y)
        {
            var ink = _roundInk[slot] ??= new BriefingInk();
            Vector2 point = new(x / (float)ushort.MaxValue, y / (float)ushort.MaxValue);
            if (!ink.Apply(action, point, MaxPointsPerPlayer)) return false;
            if (IsLocalTeam(slot)) BriefingDrawingChanged?.Invoke();
            return true;
        }

        private bool IsBriefingSlot(int slot) => _room != null && slot is >= 0 and < MultiplayerSessionConnection.MaxPlayers &&
            (_room.ExpectedMask & (1 << slot)) != 0;

        private bool IsLocalTeam(int slot)
        {
            int local = _room.LocalSlot;
            return local >= 0 && MultiplayerSessionConnection.IsAlphaSlot(local) == MultiplayerSessionConnection.IsAlphaSlot(slot);
        }

        private void ResetBriefingDrawing()
        {
            Array.Clear(_roundInk, 0, _roundInk.Length);
            _briefingDrawingClosed = false;
        }
    }
}
