using UnityEngine;

namespace Modern8bitTanks
{
    public partial class BattleSceneController
    {
        private const string ControlVictorySubtitle = "CAPTURE VICTORY";

        private SpriteRenderer _controlMarker;
        private bool _wonByControl;

        internal bool UsesControlPoints => battleMode is BattleMode.Duel1v1 or BattleMode.Squad2v2 or BattleMode.Battle5v5;
        internal Vector2 ControlScores => _controlPoints.Scores;
        internal ControlPointState ControlPoint => _controlPoints.State;
        internal Vector2 ControlPointPosition => _controlPoints.Position;

        internal Color ControlPointColor()
        {
            if (ControlPoint.Contested) return new Color(1f, 0.83f, 0.42f);
            int team = ControlPoint.Owner;
            if (team == 0) return new Color(0.87f, 0.87f, 0.79f);
            return ControlTeamColor(team);
        }

        internal Color ControlTeamColor(int team)
        {
            int localTeam = _multiplayer.IsInBattle && !MultiplayerSessionConnection.IsAlphaSlot(_multiplayer.LocalSlot) ? -1 : 1;
            return team == localTeam ? new Color(0.32f, 0.87f, 1f) : new Color(1f, 0.36f, 0.29f);
        }

        private void ResetControlPoint()
        {
            _controlPoints.Reset(UsesControlPoints ? CurrentLayout.ControlPoint / PPU : Vector2.zero);
            ResetControlPointVoiceTracking();
            _controlMarker = null;
            if (!UsesControlPoints) return;
            Vector2 position = ControlPointPosition;
            _controlMarker = Instantiate(spawnAssets.ControlPointMarker,
                new Vector3(position.x, position.y, 4.9f), Quaternion.identity, _fxRoot);
        }

        private void UpdateControlPoint(float dt)
        {
            sbyte winner = _controlPoints.Advance(_tanks, dt, squash);
            if (winner != 0)
            {
                FinishRound(winner == 1 ? "ally" : "enemy", ControlVictorySubtitle, ControlPointPosition, wonByControl: true);
                return;
            }
            SyncControlPointVoice();
        }

        private Vector2 GetControlSteering(TankEntity tank)
        {
            Vector2 position = tank.Position2D;
            if ((ControlPointPosition - position).sqrMagnitude <= 1.5f * 1.5f) return Vector2.zero;

            Vector2 heading = TankEntity.SliceAngleToDir(tank.hullAngleDeg);
            _battlefieldNavigation.TryGetSteering(position * PPU, heading, out Vector2 steering);
            return steering;
        }
    }
}
