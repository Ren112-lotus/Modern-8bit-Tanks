namespace Modern8bitTanks
{
    /// <summary>Application-scoped multiplayer state shared by the hangar and battle scenes.</summary>
    public sealed class MultiplayerSessionState
    {
        public MultiplayerSessionConnection Connection { get; private set; }
        public bool IsInBattle => Connection != null && Connection.IsInBattle;
        public int LocalSlot => Connection != null ? Connection.LocalSlot : -1;
        public MultiplayerMatchTransport Transport => Connection != null ? Connection.Transport : null;
        internal string Notice { get; set; }

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
