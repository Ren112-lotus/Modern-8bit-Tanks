using System;
using System.Globalization;
using UnityEngine;

namespace Modern8bitTanks
{
    /// <summary>Application-scoped multiplayer state shared by the hangar and battle scenes.</summary>
    public sealed class MultiplayerSessionState
    {
        internal const int MaxDisplayNameLength = 12;
        private const string DisplayNamePreference = "M8T.OnlineDisplayName";

        public MultiplayerSessionConnection Connection { get; private set; }
        public bool IsInBattle => Connection != null && Connection.IsInBattle;
        public int LocalSlot => Connection != null ? Connection.LocalSlot : -1;
        public MultiplayerMatchTransport Transport => Connection != null ? Connection.Transport : null;
        internal string Notice { get; set; }
        internal string DisplayName => NormalizeDisplayName(PlayerPrefs.GetString(DisplayNamePreference));

        internal string SaveDisplayName(string value)
        {
            string name = NormalizeDisplayName(value);
            if (PlayerPrefs.GetString(DisplayNamePreference) != name)
            {
                PlayerPrefs.SetString(DisplayNamePreference, name);
                PlayerPrefs.Save();
            }
            return name;
        }

        internal static string NormalizeDisplayName(string value)
        {
            ReadOnlySpan<char> input = value.AsSpan().Trim();
            Span<char> output = stackalloc char[MaxDisplayNameLength];
            int length = 0;
            for (int i = 0; i < input.Length && length < output.Length; i++)
            {
                char character = input[i];
                if (char.IsHighSurrogate(character) && i + 1 < input.Length && char.IsLowSurrogate(input[i + 1]))
                {
                    if (length + 2 > output.Length) break;
                    output[length++] = character;
                    output[length++] = input[++i];
                }
                else if (char.GetUnicodeCategory(character) is not (UnicodeCategory.Surrogate or UnicodeCategory.Control or
                         UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.Format))
                    output[length++] = character;
            }
            return ((ReadOnlySpan<char>)output[..length]).Trim().ToString();
        }

        internal void Attach(MultiplayerSessionConnection connection) => Connection = connection;

        internal void Detach(MultiplayerSessionConnection connection)
        {
            if (ReferenceEquals(Connection, connection)) Connection = null;
        }

        internal string TakeNotice()
        {
            var notice = Notice;
            Notice = null;
            return notice;
        }
    }
}
