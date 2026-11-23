using Unity.Netcode;
using UnityEngine;

namespace Modern8bitTanks
{
    internal enum BattleEffectKind : byte { ArmorImpact, Splash, TankLoss, LossReplay, Explosion, PropBlast, PropSpark }

    // An authoritative contact is rendered locally on every peer using the same effect code.
    internal struct BattleEffect : INetworkSerializable
    {
        public BattleEffectKind Kind;
        public byte Attacker, Victim;
        public ProjectileKind Projectile;
        public TankLoss Loss;
        public TankHit Hit;
        public TankShotTrace Trace;
        public Vector2 Visual, Ground, Normal, VictimPosition;
        public bool Big, Bounced, EndsRound;
        public byte ReplayReason;
        public SoundBus Bus;
        public float Volume, Shake;
        public int Seed;

        public void NetworkSerialize<T>(BufferSerializer<T> io) where T : IReaderWriter
        {
            io.SerializeValue(ref Kind);
            io.SerializeValue(ref Attacker);
            io.SerializeValue(ref Victim);
            io.SerializeValue(ref Visual);
            io.SerializeValue(ref Ground);
            io.SerializeValue(ref Seed);

            switch (Kind)
            {
                case BattleEffectKind.ArmorImpact:
                case BattleEffectKind.Splash:
                case BattleEffectKind.LossReplay:
                    io.SerializeValue(ref VictimPosition);
                    io.SerializeValue(ref Normal);
                    io.SerializeValue(ref Projectile);
                    io.SerializeValue(ref Loss);
                    io.SerializeValue(ref Bounced);
                    io.SerializeValue(ref EndsRound);
                    io.SerializeValue(ref ReplayReason);
                    SerializeHit(io);
                    break;
                case BattleEffectKind.TankLoss:
                    io.SerializeValue(ref Loss);
                    break;
                case BattleEffectKind.Explosion:
                    io.SerializeValue(ref Big);
                    io.SerializeValue(ref Bus);
                    io.SerializeValue(ref Volume);
                    io.SerializeValue(ref Shake);
                    break;
                case BattleEffectKind.PropBlast:
                case BattleEffectKind.PropSpark:
                    io.SerializeValue(ref Big);
                    break;
            }
        }

        private void SerializeHit<T>(BufferSerializer<T> io) where T : IReaderWriter
        {
            var armor = Hit.Armor;
            byte modules = (byte)Hit.Modules, struck = (byte)Hit.StruckModules, crew = (byte)Hit.KnockedOut;
            byte crewLost = (byte)Hit.CrewLost;
            var ignited = Hit.Ignited;
            var ammo = Hit.Ammo;
            float penetration = Hit.PenetrationValue, effective = Hit.EffectiveArmor, angle = Hit.ImpactAngleDeg;
            Vector2 entry = Trace.Entry, approach = Trace.Approach, end = Trace.End;
            io.SerializeValue(ref armor);
            io.SerializeValue(ref modules);
            io.SerializeValue(ref struck);
            io.SerializeValue(ref crew);
            io.SerializeValue(ref crewLost);
            io.SerializeValue(ref ignited);
            io.SerializeValue(ref ammo);
            io.SerializeValue(ref penetration);
            io.SerializeValue(ref effective);
            io.SerializeValue(ref angle);
            io.SerializeValue(ref entry);
            io.SerializeValue(ref approach);
            io.SerializeValue(ref end);
            if (!io.IsReader) return;
            Hit = new TankHit(armor, (TankModule)modules, crewLost, ignited, (TankModule)struck, (TankCrew)crew,
                penetration, effective, angle, ammo);
            Trace = new TankShotTrace(entry, approach, end);
        }
    }
}
