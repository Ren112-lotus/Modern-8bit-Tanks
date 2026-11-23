using System.Collections.Generic;
using UnityEngine;

namespace Modern8bitTanks
{
    internal struct ControlPointState
    {
        public sbyte Owner;
        public bool Contested;
    }

    internal sealed class ControlPointSystem
    {
        internal const int ScoreToWin = 25;
        internal const int RadiusPixels = 60;
        private const float Radius = RadiusPixels / BattleSceneController.PPU;

        internal Vector2 Position { get; private set; }
        internal ControlPointState State { get; private set; }
        internal Vector2 Scores { get; private set; }

        internal void Reset(Vector2 position)
        {
            Position = position;
            ApplySnapshot(default, Vector2.zero);
        }

        internal void ApplySnapshot(ControlPointState state, Vector2 scores)
        {
            State = state;
            Scores = scores;
        }

        internal sbyte Advance(IReadOnlyList<TankEntity> tanks, float deltaTime, float squash)
        {
            bool allyHere = false;
            bool enemyHere = false;
            foreach (var tank in tanks)
            {
                if (tank == null || !tank.alive || !tank.gameObject.activeInHierarchy ||
                    !tank.HullOverlapsCircle(Position, Radius, squash)) continue;

                if (tank.factionTeam == "ally") allyHere = true;
                else enemyHere = true;
                if (allyHere && enemyHere) break;
            }

            sbyte owner = allyHere == enemyHere ? (sbyte)0 : allyHere ? (sbyte)1 : (sbyte)-1;
            State = new ControlPointState { Owner = owner, Contested = allyHere && enemyHere };
            if (owner == 0) return 0;

            int index = owner == 1 ? 0 : 1;
            Vector2 scores = Scores;
            scores[index] = Mathf.Min(ScoreToWin, scores[index] + deltaTime);
            Scores = scores;
            return scores[index] >= ScoreToWin ? owner : (sbyte)0;
        }
    }
}
