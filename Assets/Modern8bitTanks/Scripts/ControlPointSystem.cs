using System.Collections.Generic;
using UnityEngine;

namespace Modern8bitTanks
{
    internal struct ControlPointState
    {
        public float Progress;
        public sbyte Owner;
        public bool Contested;
    }

    internal sealed class ControlPointSystem
    {
        internal const int ScoreToWin = 25;
        internal const int RadiusPixels = 60;
        private const float CaptureSeconds = 6f;
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

        internal sbyte Advance(IReadOnlyList<TankEntity> tanks, float deltaTime)
        {
            bool allyHere = false;
            bool enemyHere = false;
            foreach (var tank in tanks)
            {
                if (tank == null || !tank.alive || !tank.gameObject.activeInHierarchy ||
                    (tank.Position2D - Position).sqrMagnitude > Radius * Radius) continue;

                if (tank.factionTeam == "ally") allyHere = true;
                else enemyHere = true;
                if (allyHere && enemyHere) break;
            }

            var state = State;
            state.Contested = allyHere && enemyHere;

            if (!state.Contested)
            {
                int target = allyHere ? 1 : enemyHere ? -1 : state.Owner;
                state.Progress = Mathf.MoveTowards(state.Progress, target, deltaTime / CaptureSeconds);

                if (state.Owner != 0 && state.Progress * state.Owner <= 0f)
                    state.Owner = 0;
                if (Mathf.Abs(state.Progress) == 1f)
                    state.Owner = (sbyte)state.Progress;
            }

            State = state;
            if (state.Contested) return 0;

            var scores = Scores;
            if (state.Owner == 1 && !enemyHere)
            {
                scores.x = Mathf.Min(ScoreToWin, scores.x + deltaTime);
                Scores = scores;
                if (scores.x >= ScoreToWin) return 1;
            }
            else if (state.Owner == -1 && !allyHere)
            {
                scores.y = Mathf.Min(ScoreToWin, scores.y + deltaTime);
                Scores = scores;
                if (scores.y >= ScoreToWin) return -1;
            }

            return 0;
        }
    }
}
