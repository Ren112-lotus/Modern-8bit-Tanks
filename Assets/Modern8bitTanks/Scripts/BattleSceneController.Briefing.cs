using UnityEngine;
using UnityEngine.InputSystem;

namespace Modern8bitTanks
{
    public partial class BattleSceneController
    {
        internal const int BriefingMapScale = 4;
        private bool _briefingConfirmed;

        internal void ConfirmBriefing()
        {
            if (_phase != GamePhase.Briefing || _briefingConfirmed) return;
            _briefingConfirmed = true;
            if (_multiplayer.IsInBattle)
            {
                _hud.SetBriefingReady();
                _multiplayer.Transport.MarkBriefingReady();
                return;
            }
            BeginBattle();
        }

        private void HandleBriefingInput(Keyboard keyboard)
        {
            if (_phase == GamePhase.Briefing && !_hud.IsMenuOpen && keyboard != null &&
                (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
                ConfirmBriefing();
        }

        private void BeginBattle()
        {
            _phase = GamePhase.Playing;
            _hud.HideBriefing();
            StartPlayerEngineAudio(GetPlayerTank());
            _hud.ShowWaveToast("BATTLE START", 2f);
            NotifyVoiceBattleStart();
        }

        private void ShowBattleBriefing()
        {
            bool west = !_multiplayer.IsInBattle || _multiplayer.LocalSlot < MultiplayerSessionConnection.MaxPlayers / 2;
            _hud.ShowBriefing(GetMapDisplayName(battleMap), BuildBriefingMap(west));
        }

        private Color32[] BuildBriefingMap(bool west)
        {
            const int scale = BriefingMapScale;
            int width = BattlefieldLayout.Width / scale;
            int height = BattlefieldLayout.Height / scale;
            var pixels = new Color32[width * height];
            Vector2 ownSpawn = CurrentLayout.Spawn(west, 0);
            Vector2 opposingSpawn = CurrentLayout.Spawn(!west, 0);
            var background = new Color32(17, 23, 22, 255);
            var road = new Color32(111, 122, 105, 255);
            var wall = new Color32(220, 214, 186, 255);
            var block = new Color32(170, 161, 132, 255);
            var own = new Color32(111, 208, 239, 255);
            var opposing = new Color32(240, 119, 100, 255);
            var objective = new Color32(250, 213, 117, 255);

            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int groundX = x * scale + scale / 2;
                int groundY = y * scale + scale / 2;
                Vector2 position = new(groundX, groundY);
                Vector2 world = position / PPU;
                bool onRoad = CurrentLayout.IsRoad(position);
                Color32 terrain = _cleanGroundPixels[groundY * BattlefieldLayout.Width + groundX];
                Color32 color = Color32.Lerp(terrain, onRoad ? road : background, onRoad ? 0.63f : 0.56f);
                if (x % 32 == 0 || y % 32 == 0)
                    color = Color32.Lerp(color, background, 0.21f);

                foreach (var prop in _props)
                {
                    if (!prop.IsSolidAt(world)) continue;
                    color = prop.meta.fixedCover ? wall : block;
                    break;
                }

                float radius = Vector2.Distance(position, CurrentLayout.ControlPoint);
                if (Mathf.Abs(radius - ControlPointSystem.RadiusPixels) < scale || radius < 14f)
                    color = objective;
                if ((ownSpawn - position).sqrMagnitude < 24f * 24f) color = own;
                if ((opposingSpawn - position).sqrMagnitude < 24f * 24f) color = opposing;
                pixels[y * width + x] = color;
            }
            return pixels;
        }
    }
}
