using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Modern8bitTanks
{
    public partial class BattleSceneController
    {
        internal const int BriefingMapScale = 6;
        private bool _briefingConfirmed;
        private bool _waitForPrimaryRelease;

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
            if (_phase != GamePhase.Briefing || _hud.IsMenuOpen || keyboard == null) return;

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                ConfirmBriefing();
            else if (keyboard.zKey.wasPressedThisFrame)
                _hud.UndoBriefingStroke();
            else if (keyboard.cKey.wasPressedThisFrame)
                _hud.ClearBriefingSketch();
        }

        private void BeginBattle()
        {
            _waitForPrimaryRelease = Mouse.current?.leftButton.isPressed == true;
            _phase = GamePhase.Playing;
            _multiplayer.Transport?.CloseBriefingDrawing();
            _hud.HideBriefing();
            StartPlayerEngineAudio(GetPlayerTank());
            _hud.ShowWaveToast("BATTLE START", 2f);
            NotifyVoiceBattleStart();
        }

        private async UniTask ShowBattleBriefingAsync(BattleLoadBudget budget)
        {
            _hud.ShowBriefing(GetMapDisplayName(battleMap), await BuildBriefingMapAsync(IsLocalAlpha, budget));
        }

        private async UniTask<Color32[]> BuildBriefingMapAsync(bool west, BattleLoadBudget budget)
        {
            const int scale = BriefingMapScale;
            int width = BattlefieldLayout.Width / scale;
            int height = BattlefieldLayout.Height / scale;
            var pixels = new Color32[width * height];
            Vector2 ownSpawn = CurrentLayout.Spawn(west, 0);
            Vector2 opposingSpawn = CurrentLayout.Spawn(!west, 0);
            var background = new Color32(17, 23, 22, 255);
            var wall = new Color32(220, 214, 186, 255);
            var structure = new Color32(183, 178, 154, 255);
            var block = new Color32(145, 139, 115, 255);
            var own = _multiplayer.IsInBattle ? OnlineTeamColors.Friendly : new Color32(111, 208, 239, 255);
            var opposing = _multiplayer.IsInBattle ? OnlineTeamColors.Hostile : new Color32(240, 119, 100, 255);
            var objective = new Color32(250, 213, 117, 255);

            // Rasterize each prop only around its footprint. Earlier props still take priority.
            const float samplesPerUnit = PPU / scale;
            for (int i = _props.Count - 1; i >= 0; i--)
            {
                var prop = _props[i];
                if (prop == null || prop.stack == null) continue;

                Vector2 center = (Vector2)prop.transform.position * samplesPerUnit - Vector2.one * 0.5f;
                float radius = prop.SpriteRadius * samplesPerUnit * Mathf.Max(1f, prop.stack.squash);
                int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radius));
                int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radius));
                int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(center.x + radius));
                int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(center.y + radius));
                Color32 color = prop.meta.fixedCover ? wall : prop.meta.heavyStructure ? structure : block;
                for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 world = new(x * scale + scale / 2, y * scale + scale / 2);
                    if (prop.IsSolidAt(world / PPU)) pixels[y * width + x] = color;
                }
                if (budget != null) await budget.YieldIfSpentAsync();
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int groundX = x * scale + scale / 2;
                    int groundY = y * scale + scale / 2;
                    Vector2 position = new(groundX, groundY);
                    Color32 terrain = _cleanGroundPixels[groundY * BattlefieldLayout.Width + groundX];
                    Color32 color = Color32.Lerp(terrain, background, 0.42f);
                    if (x % 32 == 0 || y % 32 == 0)
                        color = Color32.Lerp(color, background, 0.21f);
                    int edge = Mathf.Min(Mathf.Min(groundX, BattlefieldLayout.Width - groundX),
                        Mathf.Min(groundY, BattlefieldLayout.Height - groundY));
                    if (edge >= BorderPadPx && edge < BorderPadPx + scale)
                        color = BattlefieldLayout.BoundaryColor;

                    Color32 cover = pixels[y * width + x];
                    if (cover.a != 0) color = cover;

                    float radius = Vector2.Distance(position, CurrentLayout.ControlPoint);
                    if (Mathf.Abs(radius - ControlPointSystem.RadiusPixels) < scale || radius < 14f)
                        color = objective;
                    if ((ownSpawn - position).sqrMagnitude < 24f * 24f) color = own;
                    if ((opposingSpawn - position).sqrMagnitude < 24f * 24f) color = opposing;
                    pixels[y * width + x] = color;
                }
                if (budget != null) await budget.YieldIfSpentAsync();
            }
            return pixels;
        }
    }
}
