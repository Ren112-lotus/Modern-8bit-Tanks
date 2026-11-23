using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    public enum BattleMode
    {
        Duel1v1,
        Squad2v2,
        Battle5v5,
        SurvivalWave,
        FiringRange
    }

    public struct RadarSmokeInfo
    {
        public Vector2 pos;
        public float radius;
    }

    /// <summary>
    /// 100% Pixel-Perfect (480x270) Combat HUD for DesertBattle.unity:
    /// - Strictly native 8px / 16px HintedRaster pixel fonts on EVEN-sized integer pixel boxes (zero blur!)
    /// - Top Scoreboard showing Player vs Random Same-Tier Rival
    /// - Bottom-Left Primary Weapon / ATGM / Smoke [Q] HUD
    /// - Top-Right Tactical Radar Minimap
    /// - Wave / Matchup Toast, Floating Armor/Crit/Ricochet Popups, Slow-Fade Result Banner, & ESC Pause Menu
    /// </summary>
    public partial class BattleHUD : MonoBehaviour
    {
        private DesertBattleManager _mgr;
        private PixelWindowbox _windowbox;
        private Font _font;
        private Font _titleFont;
        private int _W;
        private int _H;

        private Canvas _pixelCanvas;
        private RectTransform _hudRoot;
        private GameObject _combatHudGroup;
        private RectTransform _hitCamLayer;

        private GameObject _hudHitsGroup;
        private GameObject _resultHitsGroup;
        private GameObject _menuHitsGroup;
        private GameObject _settingsHitsGroup;

        // Top Scoreboard
        private Text _allyNameText;
        private Text _enemyNameText;
        private Text _allyWinsText;
        private Text _enemyWinsText;
        private Text _allyActiveText;
        private Text _enemyActiveText;
        private Text _centerBadgeText;

        // Bottom-Center Sleek War Thunder Action Bar + [1][2][3] Ammo Selector
        private Image[] _ammoSlotBgs;
        private Text[] _ammoSlotTexts;
        private Image _reloadFill;
        private Text _reloadStatusText;
        private Image _smokeFill;
        private Text _smokeStatusText;
        private const int ReloadTrackWidth = 54;
        private const int SmokeTrackWidth = 42;

        // Anti-Occlusion Panel Groups & HUD Density Cycle ([H] key)
        private RectTransform _topScoreRect;
        private CanvasGroup _topScoreCg;
        private RectTransform _radarRect;
        private CanvasGroup _radarCg;
        private RectTransform _weaponRect;
        private CanvasGroup _weaponCg;
        private int _hudDensityMode; // 0 = FULL, 1 = SLIM, 2 = MINIMAL
        private readonly List<Vector2> _occluderPointsPx = new(16);

        // Wave / Matchup announcement toast
        private GameObject _waveToastGo;
        private Text _waveToastText;
        private float _waveToastTimer;

        // Top-Right Minimalist Tactical Minimap (68x44, no header bar!)
        private Texture2D _radarTex;
        private Color32[] _radarPixels;
        private float _nextOnlineRadarUpdate;
        private const int RadarW = 68;
        private const int RadarH = 44;

        // Result Overlay (smooth fade-in after kill aftermath)
        private GameObject _resultOverlay;
        private CanvasGroup _resultCanvasGroup;
        private float _resultFadeAlpha;
        private bool _resultShowing;
        private Text _resultTitle;
        private Text _resultSubtitle;

        // Pause Menu & Dedicated Settings Screen
        private GameObject _menuOverlay;
        private Text _btnNextRivalLabel;
        private GameObject _settingsOverlay;
        private Text _btnSfxLabel;
        private Text _btnBgmLabel;
        private Text _btnControlModeLabel;
        private Text _btnAiLabel;
        private Text _btnPostProcessLabel;
        private Text _btnDisplayModeLabel;

        public bool IsSettingsOpen => _settingsOverlay != null && _settingsOverlay.activeSelf;
        public bool IsMenuOpen => (_menuOverlay != null && _menuOverlay.activeSelf) || IsSettingsOpen;

        // Floating popups
        private RectTransform _popupLayer;
        private readonly List<HudPopup> _popups = new();

        private class HudPopup
        {
            public Vector2 worldPos;
            public float life;
            public float maxLife;
            public RectTransform rt;
            public Text text;
            public Color baseColor;
        }

        // Gritty Military Armored Warfare FCS Palette
        private static readonly Color PanelBg = new Color(0.06f, 0.08f, 0.06f, 0.80f);
        private static readonly Color PanelBgActive = new Color(0.18f, 0.20f, 0.14f, 0.90f);
        private static readonly Color ArmorTint = new Color(0.88f, 0.92f, 0.84f, 0.88f);
        private static readonly Color ArmorModalTint = new Color(0.95f, 0.97f, 0.92f, 0.98f);
        private static readonly Color PanelBorder = new Color(0.42f, 0.39f, 0.28f, 0.92f);
        private static readonly Color BtnBg = new Color(0.26f, 0.29f, 0.23f, 0.98f);
        private static readonly Color BtnHover = new Color(0.42f, 0.43f, 0.32f, 1f);
        private static readonly Color AbramsGold = new Color(0.96f, 0.78f, 0.38f, 1f);
        private static readonly Color T90Green = new Color(0.54f, 0.86f, 0.38f, 1f);
        private static readonly Color SmokeCyan = new Color(0.56f, 0.86f, 0.96f, 1f);
        private static readonly Color DangerRed = new Color(0.96f, 0.30f, 0.22f, 1f);
        private static readonly Color ReadyGreen = new Color(0.56f, 0.94f, 0.42f, 1f);
        private static readonly Color TextLight = new Color(0.96f, 0.95f, 0.89f, 1f);
        private static readonly Color TextMuted = new Color(0.70f, 0.70f, 0.60f, 1f);
        private static readonly Color DarkInk = new Color(0.08f, 0.07f, 0.04f, 1f);

        public void Initialize(DesertBattleManager manager, PixelWindowbox windowbox)
        {
            _mgr = manager;
            _windowbox = windowbox;
            _W = windowbox.renderWidth;
            _H = windowbox.renderHeight;

            _font = Resources.Load<Font>("Fonts/Silkscreen-Regular")
                    ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _titleFont = Resources.Load<Font>("Fonts/PressStart2P-Regular") ?? _font;

            ForceFontPointFilter(_font);
            ForceFontPointFilter(_titleFont);
            Font.textureRebuilt += OnFontTextureRebuilt;

            BuildPixelHudCanvas();
            BuildHitGroups();
            BuildCombatHud();
            BuildResultOverlay();
            BuildHitCameraLayer();
            BuildPauseMenuOverlay();
            BuildSettingsOverlay();
        }

        private void OnFontTextureRebuilt(Font f)
        {
            if (f == _font || f == _titleFont) ForceFontPointFilter(f);
        }

        private static void ForceFontPointFilter(Font f)
        {
            if (f != null && f.material != null && f.material.mainTexture != null)
                f.material.mainTexture.filterMode = FilterMode.Point;
        }

        private void BuildPixelHudCanvas()
        {
            var canvasGo = new GameObject("PixelPerfectHudCanvas") { layer = 5 };
            canvasGo.transform.SetParent(transform, false);

            _pixelCanvas = canvasGo.AddComponent<Canvas>();
            _pixelCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            _pixelCanvas.worldCamera = _windowbox.HudCamera;
            _pixelCanvas.planeDistance = 10f;
            _pixelCanvas.pixelPerfect = true;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            _hudRoot = canvasGo.GetComponent<RectTransform>();

            var popGo = new GameObject("PopupLayer") { layer = 5 };
            popGo.transform.SetParent(_hudRoot, false);
            _popupLayer = popGo.AddComponent<RectTransform>();
            Stretch(_popupLayer);

            _combatHudGroup = new GameObject("CombatHudGroup") { layer = 5 };
            _combatHudGroup.transform.SetParent(_hudRoot, false);
            Stretch(_combatHudGroup.AddComponent<RectTransform>());
        }

        private void BuildHitGroups()
        {
            _hudHitsGroup = CreateHitGroup("HudHits");
            _resultHitsGroup = CreateHitGroup("ResultHits");
            _menuHitsGroup = CreateHitGroup("MenuHits");
            _settingsHitsGroup = CreateHitGroup("SettingsHits");
            _resultHitsGroup.SetActive(false);
            _menuHitsGroup.SetActive(false);
            _settingsHitsGroup.SetActive(false);
        }

        private GameObject CreateHitGroup(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_windowbox.HitOverlayRect, false);
            Stretch(go.AddComponent<RectTransform>());
            return go;
        }

        private void BuildCombatHud()
        {
            Transform parent = _combatHudGroup.transform;

            // 1. Riveted Armored Top-Center Battle Scoreboard (184 x 18 px EVEN!)
            int bx = (_W - 184) / 2;
            int by = _H - 20;
            _topScoreRect = MilitaryUiSprites.CreateSlicedSprite(parent, "TopScoreboard", MilitaryUiSprites.ArmorPanel, bx, by, 184, 18, ArmorTint, out _);
            _topScoreCg = _topScoreRect.gameObject.AddComponent<CanvasGroup>();
            _topScoreCg.blocksRaycasts = false;

            // Bold faction war stripes on left (Ally Gold) & right (Hostile Red)
            CreatePixelSolid(_topScoreRect, "AllyAccent", 3, 3, 3, 12, AbramsGold);
            CreatePixelSolid(_topScoreRect, "EnemyAccent", 178, 3, 3, 12, DangerRed);

            _allyNameText = CreatePixelText(_topScoreRect, "ALLY", 8, AbramsGold, TextAnchor.MiddleLeft, 8, 4, 56, 10);
            _allyActiveText = CreatePixelText(_topScoreRect, "", 8, TextMuted, TextAnchor.MiddleRight, 49, 4, 22, 10);
            _allyWinsText = CreatePixelText(_topScoreRect, "0", 8, AbramsGold, TextAnchor.MiddleRight, 70, 4, 12, 10, useTitleFont: true);

            _centerBadgeText = CreatePixelText(_topScoreRect, "VS", 8, TextMuted, TextAnchor.MiddleCenter, 82, 4, 20, 10);

            _enemyWinsText = CreatePixelText(_topScoreRect, "0", 8, DangerRed, TextAnchor.MiddleLeft, 102, 4, 12, 10, useTitleFont: true);
            _enemyActiveText = CreatePixelText(_topScoreRect, "", 8, TextMuted, TextAnchor.MiddleLeft, 112, 4, 22, 10);
            _enemyNameText = CreatePixelText(_topScoreRect, "FOE", 8, DangerRed, TextAnchor.MiddleRight, 119, 4, 56, 10);

            // 2. Riveted Armored Tactical Minimap Housing (76 x 52 EVEN with CRT scanlines!)
            int rw = RadarW + 8;
            int rh = RadarH + 8;
            _radarRect = MilitaryUiSprites.CreateSlicedSprite(parent, "RadarPanel", MilitaryUiSprites.ArmorPanel, _W - rw - 4, _H - rh - 4, rw, rh, ArmorTint, out _);
            _radarCg = _radarRect.gameObject.AddComponent<CanvasGroup>();
            _radarCg.blocksRaycasts = false;

            _radarTex = new Texture2D(RadarW, RadarH, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _radarPixels = new Color32[RadarW * RadarH];

            var mapGo = new GameObject("RadarImg") { layer = 5 };
            mapGo.transform.SetParent(_radarRect, false);
            var raw = mapGo.AddComponent<RawImage>();
            raw.texture = _radarTex;
            raw.raycastTarget = false;
            SetPixelRect(mapGo.GetComponent<RectTransform>(), 4, 4, RadarW, RadarH);
            MilitaryUiSprites.CreateTiledSprite(_radarRect, "RadarScanlines", MilitaryUiSprites.Scanlines, 4, 4, RadarW, RadarH, Color.white);

            // 3. Bottom-Left War Thunder X-Ray Damage Doll & Auto-Docking Live Hit Camera
            BuildDamageHud(parent, rh);

            // 4. Riveted Armored FCS Bottom-Center Weapon & Ammo Console (252 x 22 EVEN!)
            const int pw = 252;
            _weaponRect = MilitaryUiSprites.CreateSlicedSprite(parent, "WeaponPanel", MilitaryUiSprites.ArmorPanel, (_W - pw) / 2, 4, pw, 22, ArmorTint, out _);
            _weaponCg = _weaponRect.gameObject.AddComponent<CanvasGroup>();
            _weaponCg.blocksRaycasts = false;

            // Left: Primary Reload Bar + Status
            _reloadStatusText = CreatePixelText(_weaponRect, "READY", 8, ReadyGreen, TextAnchor.MiddleCenter, 6, 9, ReloadTrackWidth, 9);
            var reloadTrack = CreatePixelSolid(_weaponRect, "ReloadTrack", 6, 4, ReloadTrackWidth, 4, new Color(0.04f, 0.05f, 0.04f, 0.96f));
            _reloadFill = CreateFillBar(reloadTrack, ReadyGreen);

            CreatePixelSolid(_weaponRect, "Sep1", 63, 4, 1, 14, new Color(0.08f, 0.09f, 0.07f, 0.95f));

            // Middle: 3 Armored Ammo Rack Bays ([1] [2] [3] or [1] [2] + R:TOW!)
            _ammoSlotBgs = new Image[3];
            _ammoSlotTexts = new Text[3];
            for (int i = 0; i < 3; i++)
            {
                int sx = 67 + i * 44;
                var slotBox = CreatePixelBox(_weaponRect, $"AmmoSlot_{i + 1}", sx, 4, 42, 14, PanelBg, PanelBorder, out Image bgImg);
                _ammoSlotBgs[i] = bgImg;
                _ammoSlotTexts[i] = CreatePixelText(slotBox, $"{i + 1}---", 8, TextLight, TextAnchor.MiddleCenter, 0, 1, 42, 12);
            }

            CreatePixelSolid(_weaponRect, "Sep2", 200, 4, 1, 14, new Color(0.08f, 0.09f, 0.07f, 0.95f));

            // Right: Smoke [Q] Bar
            _smokeStatusText = CreatePixelText(_weaponRect, "SMK[Q]", 8, SmokeCyan, TextAnchor.MiddleCenter, 204, 9, SmokeTrackWidth, 9);
            var smokeTrack = CreatePixelSolid(_weaponRect, "SmokeTrack", 204, 4, SmokeTrackWidth, 4, new Color(0.04f, 0.05f, 0.04f, 0.96f));
            _smokeFill = CreateFillBar(smokeTrack, SmokeCyan);

            // Wave / Matchup Alert Banner (riveted steel plate with hazard caution border!)
            var toast = MilitaryUiSprites.CreateSlicedSprite(parent, "WaveToast", MilitaryUiSprites.ArmorPanel, (_W - 220) / 2, _H - 42, 220, 20, ArmorModalTint, out _);
            MilitaryUiSprites.CreateTiledSprite(toast, "ToastHazL", MilitaryUiSprites.HazardTape, 4, 4, 12, 12, new Color(1f, 1f, 1f, 0.90f));
            MilitaryUiSprites.CreateTiledSprite(toast, "ToastHazR", MilitaryUiSprites.HazardTape, 204, 4, 12, 12, new Color(1f, 1f, 1f, 0.90f));
            _waveToastGo = toast.gameObject;
            _waveToastText = CreatePixelText(toast, "ENGAGE!", 8, AbramsGold, TextAnchor.MiddleCenter, 18, 2, 184, 16, useTitleFont: true);
            _waveToastGo.SetActive(false);

            // No direct play-screen buttons (ESC opens Pause/Hangar Menu!)
            if (_hudHitsGroup != null) _hudHitsGroup.SetActive(false);
        }

        public void CycleHudDensity()
        {
            _hudDensityMode = (_hudDensityMode + 1) % 3;
            string label = _hudDensityMode switch
            {
                1 => "HUD: SLIM [H]",
                2 => "HUD: MINIMAL [H]",
                _ => "HUD: FULL [H]"
            };
            ShowWaveToast(label, 1.15f);
        }

        public void ShowWaveToast(string message, float duration = 2.1f)
        {
            if (_waveToastGo == null) return;
            _waveToastText.text = message;
            _waveToastTimer = duration;
            _waveToastGo.SetActive(true);
        }

        private void BuildResultOverlay()
        {
            var dim = CreatePixelSolid(_hudRoot, "ResultOverlay", 0, 0, _W, _H, new Color(0.03f, 0.03f, 0.03f, 0.56f));
            _resultOverlay = dim.gameObject;
            _resultCanvasGroup = _resultOverlay.AddComponent<CanvasGroup>();
            _resultCanvasGroup.alpha = 0f;

            int cw = 244;
            int ch = 92;
            int cx = (_W - cw) / 2;
            int cy = 60; // Lowered slightly so Match-End X-Ray Hit Camera (top-left Y:172..266) NEVER overlaps!
            var card = MilitaryUiSprites.CreateSlicedSprite(dim, "ResultCard", MilitaryUiSprites.ArmorPanel, cx, cy, cw, ch, ArmorModalTint, out _);
            MilitaryUiSprites.CreateTiledSprite(card, "ResultHazard", MilitaryUiSprites.HazardTape, 6, ch - 8, cw - 12, 3, new Color(1f, 1f, 1f, 0.92f));

            _resultTitle = CreatePixelText(card, "VICTORY", 16, AbramsGold, TextAnchor.MiddleCenter, 0, 54, cw, 24, useTitleFont: true);
            _resultSubtitle = CreatePixelText(card, "VICTORY", 8, TextMuted, TextAnchor.MiddleCenter, 0, 36, cw, 14);

            if (MultiplayerSessionConnection.Online)
            {
                if (MultiplayerSessionConnection.Active.IsHost)
                    CreatePixelButton(card, _resultHitsGroup.transform, "RETURN TO ROOM", cx + 10, cy + 9, cw - 20, 22,
                        AbramsGold, AbramsGold, DarkInk, 8, _mgr.ReturnToOnlineRoom, isParentOffset: true, parentX: cx, parentY: cy, useTitleFont: true);
                else
                {
                    CreatePixelText(card, "WAITING FOR HOST", 8, SmokeCyan, TextAnchor.MiddleLeft, 12, 9, cw - 120, 22);
                    CreatePixelButton(card, _resultHitsGroup.transform, "LEAVE ROOM", cx + cw - 100, cy + 9, 90, 22,
                        BtnBg, PanelBorder, TextLight, 8, _mgr.ReturnToHangar, isParentOffset: true, parentX: cx, parentY: cy);
                }
            }
            else
            {
                CreatePixelButton(card, _resultHitsGroup.transform, "NEXT BATTLE (R)", cx + 10, cy + 9, 132, 22,
                    AbramsGold, AbramsGold, DarkInk, 8, _mgr.ResetRound, isParentOffset: true, parentX: cx, parentY: cy, useTitleFont: true);
                CreatePixelButton(card, _resultHitsGroup.transform, "HANGAR", cx + 148, cy + 9, 86, 22,
                    BtnBg, PanelBorder, TextLight, 8, _mgr.ReturnToHangar, isParentOffset: true, parentX: cx, parentY: cy);
            }

            _resultOverlay.SetActive(false);
        }

        private void BuildPauseMenuOverlay()
        {
            var dim = CreatePixelSolid(_hudRoot, "PauseMenuOverlay", 0, 0, _W, _H, new Color(0.02f, 0.03f, 0.02f, 0.68f));
            _menuOverlay = dim.gameObject;

            int cw = 204;
            int ch = 134;
            int cx = (_W - cw) / 2;
            int cy = (_H - ch) / 2;
            var card = MilitaryUiSprites.CreateSlicedSprite(dim, "MenuCard", MilitaryUiSprites.ArmorPanel, cx, cy, cw, ch, ArmorModalTint, out _);
            MilitaryUiSprites.CreateTiledSprite(card, "MenuHazard", MilitaryUiSprites.HazardTape, 6, ch - 8, cw - 12, 3, new Color(1f, 1f, 1f, 0.90f));
            CreatePixelText(card, "COMMAND MENU", 8, AbramsGold, TextAnchor.MiddleCenter, 0, ch - 25, cw, 16, useTitleFont: true);

            bool online = MultiplayerSessionConnection.Online;
            bool onlineHost = online && MultiplayerSessionConnection.Active.IsHost;
            CreatePixelButton(card, _menuHitsGroup.transform, "RESUME", cx + 12, cy + (online ? 78 : 84), cw - 24, 20, AbramsGold, AbramsGold, DarkInk, 8, () =>
            {
                CloseAllMenus();
            }, isParentOffset: true, parentX: cx, parentY: cy, useTitleFont: true);

            CreatePixelButton(card, _menuHitsGroup.transform, "SETTINGS", cx + 12, cy + (online ? 50 : 60), cw - 24, 20, BtnBg, SmokeCyan, SmokeCyan, 8, () =>
            {
                SetSettingsOpen(true);
            }, isParentOffset: true, parentX: cx, parentY: cy);

            if (!online)
            {
                _btnNextRivalLabel = CreatePixelButton(card, _menuHitsGroup.transform, "NEXT RIVAL", cx + 12, cy + 36, cw - 24, 20, BtnBg, PanelBorder, TextLight, 8, () =>
                {
                    CloseAllMenus();
                    _mgr.ResetRound();
                }, isParentOffset: true, parentX: cx, parentY: cy);
            }
            else if (onlineHost)
            {
                CreatePixelButton(card, _menuHitsGroup.transform, "RETURN TO ROOM", cx + 12, cy + 29, cw - 24, 18,
                    BtnBg, SmokeCyan, SmokeCyan, 8, _mgr.ReturnToOnlineRoom, isParentOffset: true, parentX: cx, parentY: cy);
            }

            string exitLabel = onlineHost ? "CLOSE ROOM" : online ? "LEAVE ROOM" : "BACK TO HANGAR";
            CreatePixelButton(card, _menuHitsGroup.transform, exitLabel, cx + 12, cy + (onlineHost ? 7 : online ? 22 : 12), cw - 24, 20, BtnBg, AbramsGold, AbramsGold, 8, () =>
            {
                CloseAllMenus();
                _mgr.ReturnToHangar();
            }, isParentOffset: true, parentX: cx, parentY: cy);

            _menuOverlay.SetActive(false);
        }

        private void BuildSettingsOverlay()
        {
            var dim = CreatePixelSolid(_hudRoot, "SettingsOverlay", 0, 0, _W, _H, new Color(0.02f, 0.03f, 0.02f, 0.76f));
            _settingsOverlay = dim.gameObject;

            int cw = 246;
            int ch = 198;
            int cx = (_W - cw) / 2;
            int cy = (_H - ch) / 2;
            var card = MilitaryUiSprites.CreateSlicedSprite(dim, "SettingsCard", MilitaryUiSprites.ArmorPanel, cx, cy, cw, ch, ArmorModalTint, out _);
            MilitaryUiSprites.CreateTiledSprite(card, "SetHazard", MilitaryUiSprites.HazardTape, 6, ch - 8, cw - 12, 3, new Color(1f, 1f, 1f, 0.88f));
            CreatePixelText(card, "SYSTEM SETTINGS", 8, AbramsGold, TextAnchor.MiddleCenter, 0, ch - 25, cw, 16, useTitleFont: true);

            Transform hits = _settingsHitsGroup.transform;

            // Row 1: SE (SFX) Volume [-] [SE: 100%] [+]
            CreatePixelButton(card, hits, "-", cx + 12, cy + 148, 24, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _mgr.StepSfxVolume(-0.1f);
                RefreshMenuLabels();
            }, isParentOffset: true, parentX: cx, parentY: cy);

            _btnSfxLabel = CreatePixelButton(card, hits, "SE: 100%", cx + 40, cy + 148, cw - 80, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _mgr.ToggleSfxEnabled();
                RefreshMenuLabels();
            }, isParentOffset: true, parentX: cx, parentY: cy);

            CreatePixelButton(card, hits, "+", cx + cw - 36, cy + 148, 24, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _mgr.StepSfxVolume(+0.1f);
                RefreshMenuLabels();
            }, isParentOffset: true, parentX: cx, parentY: cy);

            // Row 2: BGM Volume [-] [BGM: 60%] [+]
            CreatePixelButton(card, hits, "-", cx + 12, cy + 126, 24, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _mgr.StepBgmVolume(-0.1f);
                RefreshMenuLabels();
            }, isParentOffset: true, parentX: cx, parentY: cy);

            _btnBgmLabel = CreatePixelButton(card, hits, "BGM: 60%", cx + 40, cy + 126, cw - 80, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _mgr.ToggleBgmEnabled();
                RefreshMenuLabels();
            }, isParentOffset: true, parentX: cx, parentY: cy);

            CreatePixelButton(card, hits, "+", cx + cw - 36, cy + 126, 24, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _mgr.StepBgmVolume(+0.1f);
                RefreshMenuLabels();
            }, isParentOffset: true, parentX: cx, parentY: cy);

            // Row 3: Drive Controls
            _btnControlModeLabel = CreatePixelButton(card, hits, "", cx + 12, cy + 104, cw - 24, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _mgr.arcadeWASD = !_mgr.arcadeWASD;
                _mgr.SyncSettingsToSession();
                RefreshMenuLabels();
            }, isParentOffset: true, parentX: cx, parentY: cy);

            // Row 4: Enemy AI
            _btnAiLabel = CreatePixelButton(card, hits, "", cx + 12, cy + 82, cw - 24, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _mgr.aiEnabled = !_mgr.aiEnabled;
                _mgr.SyncSettingsToSession();
                RefreshMenuLabels();
            }, isParentOffset: true, parentX: cx, parentY: cy);

            // Row 5: Graphics Mode
            _btnPostProcessLabel = CreatePixelButton(card, hits, "", cx + 12, cy + 60, cw - 24, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _mgr.TogglePostProcess();
                _mgr.SyncSettingsToSession();
                RefreshMenuLabels();
            }, isParentOffset: true, parentX: cx, parentY: cy);

            // Row 6: Display Scaling
            _btnDisplayModeLabel = CreatePixelButton(card, hits, "", cx + 12, cy + 38, cw - 24, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _mgr.integerWindowbox = !_mgr.integerWindowbox;
                _mgr.SyncSettingsToSession();
                RefreshMenuLabels();
            }, isParentOffset: true, parentX: cx, parentY: cy);

            // Bottom: Back to Pause Menu
            CreatePixelButton(card, hits, "BACK [ESC]", cx + 12, cy + 10, cw - 24, 20, AbramsGold, AbramsGold, DarkInk, 8, () =>
            {
                SetSettingsOpen(false, reopenPauseMenu: true);
            }, isParentOffset: true, parentX: cx, parentY: cy);

            RefreshMenuLabels();
            _settingsOverlay.SetActive(false);
        }

        public void HandleEscapeKey()
        {
            if (IsSettingsOpen)
                SetSettingsOpen(false, reopenPauseMenu: true);
            else
                ToggleMenu();
        }

        public void ToggleMenu()
        {
            if (IsMenuOpen)
                CloseAllMenus();
            else
                SetMenuOpen(true);
        }

        private void CloseAllMenus()
        {
            if (_menuOverlay != null) _menuOverlay.SetActive(false);
            if (_menuHitsGroup != null) _menuHitsGroup.SetActive(false);
            if (_settingsOverlay != null) _settingsOverlay.SetActive(false);
            if (_settingsHitsGroup != null) _settingsHitsGroup.SetActive(false);
            if (_resultHitsGroup != null) _resultHitsGroup.SetActive(_resultShowing);
        }

        private void SetMenuOpen(bool open)
        {
            if (_menuOverlay == null) return;
            if (_settingsOverlay != null) _settingsOverlay.SetActive(false);
            if (_settingsHitsGroup != null) _settingsHitsGroup.SetActive(false);
            _menuOverlay.SetActive(open);
            if (_menuHitsGroup != null) _menuHitsGroup.SetActive(open);
            if (_resultHitsGroup != null) _resultHitsGroup.SetActive(!open && _resultShowing);
            if (open) RefreshMenuLabels();
        }

        private void SetSettingsOpen(bool open, bool reopenPauseMenu = false)
        {
            if (_settingsOverlay == null) return;
            _settingsOverlay.SetActive(open);
            if (_settingsHitsGroup != null) _settingsHitsGroup.SetActive(open);

            bool showPause = !open && reopenPauseMenu;
            if (_menuOverlay != null) _menuOverlay.SetActive(showPause);
            if (_menuHitsGroup != null) _menuHitsGroup.SetActive(showPause);
            if (_resultHitsGroup != null) _resultHitsGroup.SetActive(!open && !showPause && _resultShowing);

            if (open || showPause) RefreshMenuLabels();
        }

        private void RefreshMenuLabels()
        {
            if (_mgr == null) return;
            if (_btnNextRivalLabel != null)
                _btnNextRivalLabel.text = _mgr.battleMode == BattleMode.FiringRange ? "RESET RANGE" : "NEXT RIVAL";
            if (_btnSfxLabel != null)
            {
                _btnSfxLabel.text = BattleSessionState.FormatVolumeLabel("SE", _mgr.sfxEnabled, _mgr.sfxVolume);
                _btnSfxLabel.color = (_mgr.sfxEnabled && _mgr.sfxVolume > 0.001f) ? ReadyGreen : TextMuted;
            }
            if (_btnBgmLabel != null)
            {
                _btnBgmLabel.text = BattleSessionState.FormatVolumeLabel("BGM", _mgr.bgmEnabled, _mgr.bgmVolume);
                _btnBgmLabel.color = (_mgr.bgmEnabled && _mgr.bgmVolume > 0.001f) ? SmokeCyan : TextMuted;
            }
            if (_btnControlModeLabel != null)
                _btnControlModeLabel.text = _mgr.arcadeWASD ? "DRIVE: ARCADE WASD" : "DRIVE: TANK STEER";
            if (_btnAiLabel != null)
                _btnAiLabel.text = _mgr.aiEnabled ? "ENEMY AI: ON" : "ENEMY AI: OFF";
            if (_btnPostProcessLabel != null)
                _btnPostProcessLabel.text = _mgr.postProcessEnabled ? "GFX: HD-2D" : "GFX: RETRO";
            if (_btnDisplayModeLabel != null)
                _btnDisplayModeLabel.text = _mgr.integerWindowbox ? "VIEW: INT WINDOWBOX" : "VIEW: 16:9 LETTERBOX";
        }

        public void SpawnPopup(Vector2 worldPos, string label, Color color)
        {
            if (_popupLayer == null) return;
            var go = new GameObject("Popup") { layer = 5 };
            go.transform.SetParent(_popupLayer, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(128f, 16f); // EVEN size!

            var txt = go.AddComponent<Text>();
            txt.font = _titleFont != null ? _titleFont : _font;
            txt.fontSize = 8; // Native 8px crisp — NEVER 7!
            txt.fontStyle = FontStyle.Normal;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.text = label;
            txt.color = color;
            txt.raycastTarget = false;

            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.95f);
            shadow.effectDistance = new Vector2(1f, -1f);

            _popups.Add(new HudPopup
            {
                worldPos = worldPos,
                life = 0.92f,
                maxLife = 0.92f,
                rt = rt,
                text = txt,
                baseColor = color
            });
        }

        public void ClearPopups()
        {
            foreach (var p in _popups)
                if (p.rt != null) Destroy(p.rt.gameObject);
            _popups.Clear();
        }

        public void ShowResult(bool playerWon, string subtitle)
        {
            if (_resultOverlay == null) return;
            _resultTitle.text = playerWon ? "VICTORY" : "DEFEATED";
            _resultTitle.color = playerWon ? AbramsGold : DangerRed;
            _resultSubtitle.text = subtitle;
            _resultShowing = true;
            _resultFadeAlpha = 0f;
            if (_resultCanvasGroup != null) _resultCanvasGroup.alpha = 0f;
            _resultOverlay.SetActive(true);
            if (_resultHitsGroup != null) _resultHitsGroup.SetActive(true);
        }

        public void HideResult()
        {
            _resultShowing = false;
            _resultFadeAlpha = 0f;
            if (_resultCanvasGroup != null) _resultCanvasGroup.alpha = 0f;
            if (_resultOverlay != null) _resultOverlay.SetActive(false);
            if (_resultHitsGroup != null) _resultHitsGroup.SetActive(false);
        }

        public void UpdateHUD(
            TankEntity player,
            int winsAlly,
            int winsEnemy,
            int currentWave,
            BattleMode mode,
            List<DestructibleProp> props,
            List<TankEntity> tanks,
            List<RadarSmokeInfo> smokes,
            Camera cam,
            float worldW,
            float worldH,
            float dt)
        {
            if (_waveToastTimer > 0f)
            {
                _waveToastTimer -= Time.unscaledDeltaTime;
                if (_waveToastTimer <= 0f && _waveToastGo != null)
                    _waveToastGo.SetActive(false);
            }

            if (_resultShowing && _resultCanvasGroup != null && _resultFadeAlpha < 1f)
            {
                _resultFadeAlpha = Mathf.MoveTowards(_resultFadeAlpha, 1f, Time.unscaledDeltaTime * 2.1f);
                _resultCanvasGroup.alpha = Mathf.SmoothStep(0f, 1f, _resultFadeAlpha);
            }

            _allyWinsText.text = winsAlly.ToString();
            _enemyWinsText.text = winsEnemy.ToString();

            _centerBadgeText.text = mode switch
            {
                BattleMode.FiringRange => "RNG",
                BattleMode.SurvivalWave => $"W{currentWave}",
                BattleMode.Battle5v5 => "5v5",
                BattleMode.Squad2v2 => "2v2",
                _ => "VS"
            };

            int allyAlive = 0, enemyAlive = 0;
            if (tanks != null)
            {
                foreach (var t in tanks)
                {
                    if (t == null || !t.alive) continue;
                    if (t.factionTeam == "ally") allyAlive++;
                    else enemyAlive++;
                }
            }

            UpdateVehicleStatus(player);

            // Smart Top-Down Anti-Occlusion Proximity Fade (checks cursor + living enemies + player!)
            _occluderPointsPx.Clear();
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && _windowbox != null && _windowbox.TryScreenToViewport(mouse.position.ReadValue(), out Vector2 mUv))
                _occluderPointsPx.Add(new Vector2(mUv.x * _W, mUv.y * _H));
            if (cam != null && tanks != null)
            {
                foreach (var t in tanks)
                {
                    if (t == null || !t.alive) continue;
                    Vector3 vp = cam.WorldToViewportPoint(new Vector3(t.Position2D.x, t.Position2D.y, 0f));
                    if (vp.z > 0f && vp.x >= -0.05f && vp.x <= 1.05f && vp.y >= -0.05f && vp.y <= 1.05f)
                        _occluderPointsPx.Add(new Vector2(vp.x * _W, vp.y * _H));
                }
            }

            float hitCamProx = _hitCameraDecisive
                ? 1f
                : UpdatePanelAntiOcclusion(_hitCameraRectOrNull(), null, 10f, 0.24f, 0.96f, dt, returnTargetOnly: true);
            UpdateHitCamera(hitCamProx);

            bool showTop = _hudDensityMode == 0;
            bool showRadar = _hudDensityMode <= 1;
            if (_topScoreRect != null) _topScoreRect.gameObject.SetActive(showTop);
            if (_radarRect != null) _radarRect.gameObject.SetActive(showRadar);

            UpdatePanelAntiOcclusion(_topScoreRect, _topScoreCg, 12f, 0.20f, 0.92f, dt);
            UpdatePanelAntiOcclusion(_radarRect, _radarCg, 12f, 0.22f, 0.92f, dt);
            UpdatePanelAntiOcclusion(_vehicleStatusCard, _vehicleStatusCg, 12f, 0.22f, 0.94f, dt);
            UpdatePanelAntiOcclusion(_weaponRect, _weaponCg, 10f, 0.22f, 0.94f, dt);

            VehicleDef pDef = _mgr.SelectedPlayerVehicle;
            VehicleDef eDef = _mgr.SelectedEnemyVehicle;
            if (mode == BattleMode.FiringRange)
            {
                if (_allyNameText != null) _allyNameText.text = pDef != null ? TrimHudName(pDef.name, 7) : "YOU";
                if (_enemyNameText != null) _enemyNameText.text = $"TGT x{enemyAlive}";
                if (_allyActiveText != null) _allyActiveText.text = "";
                if (_enemyActiveText != null) _enemyActiveText.text = "";
            }
            else if (mode == BattleMode.Battle5v5 || mode == BattleMode.Squad2v2)
            {
                if (_allyNameText != null) _allyNameText.text = $"ALLY x{allyAlive}";
                if (_enemyNameText != null) _enemyNameText.text = $"x{enemyAlive} FOE";
                if (_allyActiveText != null) _allyActiveText.text = "";
                if (_enemyActiveText != null) _enemyActiveText.text = "";
            }
            else if (mode == BattleMode.SurvivalWave)
            {
                if (_allyNameText != null) _allyNameText.text = pDef != null ? TrimHudName(pDef.name, 7) : "YOU";
                if (_enemyNameText != null) _enemyNameText.text = $"FOE x{enemyAlive}";
                if (_allyActiveText != null) _allyActiveText.text = "";
                if (_enemyActiveText != null) _enemyActiveText.text = "";
            }
            else
            {
                if (_allyNameText != null && pDef != null)
                    _allyNameText.text = TrimHudName(pDef.name, 8);
                if (_enemyNameText != null && eDef != null)
                    _enemyNameText.text = TrimHudName(eDef.name, 8);
                if (_allyActiveText != null) _allyActiveText.text = "";
                if (_enemyActiveText != null) _enemyActiveText.text = "";
            }

            // Round the reload bar to the HUD pixel grid.
            if (player == null || !player.alive)
            {
                _reloadFill.rectTransform.anchorMax = new Vector2(0f, 1f);
                _reloadFill.color = DangerRed;
                _reloadStatusText.text = "LOST";
                _reloadStatusText.color = DangerRed;
            }
            else if (!player.CanFire)
            {
                _reloadFill.rectTransform.anchorMax = new Vector2(0f, 1f);
                _reloadFill.color = DangerRed;
                _reloadStatusText.text = player.Repairing ? "FIXING" : "GUN OUT";
                _reloadStatusText.color = player.Repairing ? AbramsGold : DangerRed;
            }
            else if (player.reloadTimer > 0f)
            {
                float prog = Mathf.Clamp01(1f - player.reloadTimer / Mathf.Max(0.01f, player.ActiveReloadDuration));
                _reloadFill.rectTransform.anchorMax = new Vector2(Mathf.Round(prog * ReloadTrackWidth) / ReloadTrackWidth, 1f);
                _reloadFill.color = AbramsGold;
                _reloadStatusText.text = player.reloadDuration < 0.4f ? "FIRE" : $"{player.reloadTimer:F1}s";
                _reloadStatusText.color = AbramsGold;
            }
            else
            {
                _reloadFill.rectTransform.anchorMax = new Vector2(1f, 1f);
                _reloadFill.color = ReadyGreen;
                _reloadStatusText.text = (pDef != null && pDef.weapon == "ATGM") ? "ATGM" : "READY";
                _reloadStatusText.color = ReadyGreen;
            }

            // Middle: War Thunder [1] [2] [3] Ammo Slots (+ Secondary Mouse-Guided TOW [RMB] slot for IFVs!)
            if (_ammoSlotTexts != null && player != null)
            {
                AmmoType[] loadout = player.AmmoLoadout;
                bool hasSecAtgm = pDef != null && pDef.HasSecondaryAtgm;
                for (int i = 0; i < 3; i++)
                {
                    if (i < loadout.Length)
                    {
                        AmmoType a = loadout[i];
                        bool sel = player.selectedAmmoSlot == i;
                        _ammoSlotTexts[i].text = $"{i + 1}{AmmoInfo.ShortLabel(a)}";
                        Color activeCol = AmmoInfo.IsGuided(a) ? SmokeCyan
                            : (AmmoInfo.IsExplosiveSplash(a) || a == AmmoType.HEATFS) ? new Color(1f, 0.64f, 0.28f, 1f)
                            : AbramsGold;
                        _ammoSlotTexts[i].color = sel ? activeCol : TextMuted;
                        _ammoSlotBgs[i].color = sel ? PanelBgActive : PanelBg;
                    }
                    else if (i == 2 && hasSecAtgm)
                    {
                        bool ready = player.atgmCooldown <= 0f;
                        _ammoSlotTexts[i].text = ready ? "R:TOW" : $"T:{player.atgmCooldown:F0}s";
                        _ammoSlotTexts[i].color = ready ? SmokeCyan : TextMuted;
                        _ammoSlotBgs[i].color = ready ? PanelBgActive : PanelBg;
                    }
                    else
                    {
                        _ammoSlotTexts[i].text = "---";
                        _ammoSlotTexts[i].color = new Color(0.35f, 0.38f, 0.40f, 0.6f);
                        _ammoSlotBgs[i].color = PanelBg;
                    }
                }
            }

            // Smoke Launcher [Q] Cooldown
            if (player != null && player.alive)
            {
                if (player.smokeCooldown > 0f)
                {
                    float sProg = Mathf.Clamp01(1f - player.smokeCooldown / Mathf.Max(0.01f, player.smokeDuration));
                    _smokeFill.rectTransform.anchorMax = new Vector2(Mathf.Round(sProg * SmokeTrackWidth) / SmokeTrackWidth, 1f);
                    _smokeFill.color = TextMuted;
                    _smokeStatusText.text = $"{player.smokeCooldown:F0}s";
                    _smokeStatusText.color = TextMuted;
                }
                else
                {
                    _smokeFill.rectTransform.anchorMax = new Vector2(1f, 1f);
                    _smokeFill.color = SmokeCyan;
                    _smokeStatusText.text = "SMK[Q]";
                    _smokeStatusText.color = SmokeCyan;
                }
            }

            UpdateRadarTexture(player, props, tanks, smokes, worldW, worldH);

            if (cam != null)
            {
                for (int i = _popups.Count - 1; i >= 0; i--)
                {
                    var p = _popups[i];
                    p.life -= dt;
                    if (p.life <= 0f)
                    {
                        if (p.rt != null) Destroy(p.rt.gameObject);
                        _popups.RemoveAt(i);
                        continue;
                    }
                    p.worldPos += SpriteStackCamera.CamUp * (1.05f * dt);
                    Vector3 vp = cam.WorldToViewportPoint(new Vector3(p.worldPos.x, p.worldPos.y, 0f));
                    p.rt.anchoredPosition = new Vector2(Mathf.Round(vp.x * _W), Mathf.Round(vp.y * _H));
                    Color c = p.baseColor;
                    c.a = Mathf.Clamp01(p.life / (p.maxLife * 0.45f));
                    p.text.color = c;
                }
            }
        }

        private void UpdateRadarTexture(TankEntity player, List<DestructibleProp> props, List<TankEntity> tanks,
            List<RadarSmokeInfo> smokes, float worldW, float worldH)
        {
            if (_radarTex == null) return;
            if (MultiplayerSessionConnection.Online)
            {
                if (Time.unscaledTime < _nextOnlineRadarUpdate) return;
                _nextOnlineRadarUpdate = Time.unscaledTime + MultiplayerMatchTransport.SnapshotInterval;
            }
            BattleMap curMap = _mgr != null ? _mgr.battleMap : BattleMap.Desert;
            Color32 bg = curMap switch
            {
                BattleMap.City => new Color32(28, 30, 34, 255),
                BattleMap.Forest => new Color32(22, 34, 22, 255),
                _ => new Color32(36, 31, 22, 255)
            };
            Color32 grid = curMap switch
            {
                BattleMap.City => new Color32(42, 45, 50, 255),
                BattleMap.Forest => new Color32(32, 48, 30, 255),
                _ => new Color32(50, 43, 30, 255)
            };
            for (int y = 0; y < RadarH; y++)
            {
                for (int x = 0; x < RadarW; x++)
                {
                    bool line = (x % 17 == 0) || (y % 14 == 0);
                    _radarPixels[y * RadarW + x] = line ? grid : bg;
                }
            }

            Color32 propCol = new Color32(148, 126, 90, 255);
            Color32 bldgCol = new Color32(138, 134, 128, 255);
            Color32 rockCol = new Color32(126, 114, 96, 255);
            Color32 treeCol = new Color32(72, 146, 62, 255);
            Color32 explosiveCol = new Color32(232, 74, 50, 255);
            foreach (var prop in props)
            {
                if (prop == null || !prop.alive) continue;
                int px = Mathf.Clamp(Mathf.RoundToInt((prop.transform.position.x / worldW) * (RadarW - 1)), 1, RadarW - 2);
                int py = Mathf.Clamp(Mathf.RoundToInt((prop.transform.position.y / worldH) * (RadarH - 1)), 1, RadarH - 2);
                bool isHuge = prop.meta != null && prop.meta.heavyStructure;
                Color32 pCol = (prop.kind == "supply_crate" || prop.kind == "fuel_tank") ? explosiveCol
                    : (prop.kind == "palm_tree" || prop.kind == "pine_cluster" ? treeCol
                    : (prop.kind.StartsWith("city_") || prop.kind == "forest_cabin" || prop.kind == "desert_compound" ? bldgCol
                    : (prop.kind == "desert_bunker" || prop.kind == "desert_rock" || prop.kind == "desert_mesa" || prop.kind == "forest_boulder" ? rockCol : propCol)));
                int radius = isHuge ? 1 : (prop.kind == "desert_bunker" || prop.kind == "desert_rock" ? 1 : 0);
                PlotDot(px, py, radius, pCol);
            }

            if (smokes != null)
            {
                Color32 smkCol = new Color32(195, 202, 208, 255);
                foreach (var sm in smokes)
                {
                    int sx = Mathf.Clamp(Mathf.RoundToInt((sm.pos.x / worldW) * (RadarW - 1)), 2, RadarW - 3);
                    int sy = Mathf.Clamp(Mathf.RoundToInt((sm.pos.y / worldH) * (RadarH - 1)), 2, RadarH - 3);
                    PlotDot(sx, sy, 2, smkCol);
                }
            }

            foreach (var t in tanks)
            {
                if (t == null || !t.alive || !_mgr.IsRadarContactVisible(t, player)) continue;
                int tx = Mathf.Clamp(Mathf.RoundToInt((t.Position2D.x / worldW) * (RadarW - 1)), 1, RadarW - 2);
                int ty = Mathf.Clamp(Mathf.RoundToInt((t.Position2D.y / worldH) * (RadarH - 1)), 1, RadarH - 2);
                Color32 tc = t.isPlayer
                    ? new Color32(255, 232, 118, 255)
                    : (player != null && MultiplayerSessionConnection.Online ? t.factionTeam == player.factionTeam : t.factionTeam == "ally")
                        ? new Color32(82, 214, 255, 255)
                        : new Color32(255, 92, 74, 255);
                PlotDot(tx, ty, 1, tc);
            }

            _radarTex.SetPixels32(_radarPixels);
            _radarTex.Apply(false, false);
        }

        private void PlotDot(int cx, int cy, int r, Color32 col)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                int y = cy + dy;
                if (y < 0 || y >= RadarH) continue;
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = cx + dx;
                    if (x < 0 || x >= RadarW) continue;
                    _radarPixels[y * RadarW + x] = col;
                }
            }
        }

        private RectTransform _hitCameraRectOrNull() => _hitCameraCard != null && _hitCameraCard.gameObject.activeSelf ? _hitCameraCard : null;

        private float UpdatePanelAntiOcclusion(RectTransform rt, CanvasGroup cg, float padPx, float minAlpha, float maxAlpha, float dt, bool returnTargetOnly = false)
        {
            if (rt == null || !rt.gameObject.activeSelf) return maxAlpha;
            Vector2 pos = rt.anchoredPosition;
            Vector2 sz = rt.sizeDelta;
            bool occluded = false;
            for (int i = 0; i < _occluderPointsPx.Count; i++)
            {
                Vector2 p = _occluderPointsPx[i];
                if (p.x >= pos.x - padPx && p.x <= pos.x + sz.x + padPx &&
                    p.y >= pos.y - padPx && p.y <= pos.y + sz.y + padPx)
                {
                    occluded = true;
                    break;
                }
            }

            float target = occluded ? minAlpha : maxAlpha;
            if (returnTargetOnly || cg == null) return target;
            cg.alpha = Mathf.MoveTowards(cg.alpha, target, Time.unscaledDeltaTime * 5.5f);
            return cg.alpha;
        }

        private static string TrimHudName(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string up = s.ToUpper();
            return up.Length <= maxLen ? up : up.Substring(0, maxLen);
        }

        // ============================================================
        // Exact Integer-Pixel uGUI Helpers (Strictly 8px / 16px!)
        // ============================================================
        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void SetPixelRect(RectTransform rt, int x, int y, int w, int h)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static RectTransform CreatePixelSolid(Transform parent, string name, int x, int y, int w, int h, Color col)
        {
            var go = new GameObject(name) { layer = 5 };
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            SetPixelRect(rt, x, y, w, h);
            var img = go.AddComponent<Image>();
            img.color = col;
            img.raycastTarget = false;
            return rt;
        }

        private static RectTransform CreatePixelBox(Transform parent, string name, int x, int y, int w, int h, Color bg, Color border)
        {
            return CreatePixelBox(parent, name, x, y, w, h, bg, border, out _);
        }

        private static RectTransform CreatePixelBox(Transform parent, string name, int x, int y, int w, int h, Color bg, Color border, out Image fillImg)
        {
            var outer = CreatePixelSolid(parent, name, x, y, w, h, border);
            var inner = CreatePixelSolid(outer, "Inner", 1, 1, Mathf.Max(1, w - 2), Mathf.Max(1, h - 2), bg);
            fillImg = inner.GetComponent<Image>();
            return outer;
        }

        private static Image CreateFillBar(RectTransform track, Color color)
        {
            var go = new GameObject("Fill") { layer = 5 };
            go.transform.SetParent(track, false);
            Stretch(go.AddComponent<RectTransform>());
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private Text CreatePixelText(Transform parent, string content, int fontSize, Color col, TextAnchor align, int x, int y, int w, int h, bool useTitleFont = false)
        {
            // Enforce strict native multiple of 8px so pixel glyphs NEVER blur!
            int crispSize = fontSize >= 16 ? 16 : 8;
            var go = new GameObject("Txt_" + content) { layer = 5 };
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            SetPixelRect(rt, x, y, w, h);

            var t = go.AddComponent<Text>();
            t.font = (useTitleFont && _titleFont != null) ? _titleFont : _font;
            t.fontSize = crispSize;
            t.fontStyle = FontStyle.Normal;
            t.color = col;
            t.alignment = align;
            t.text = content;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;

            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.88f);
            sh.effectDistance = new Vector2(1f, -1f);
            return t;
        }

        private Text CreatePixelButton(
            Transform visualParent, Transform hitParent, string label,
            int screenX, int screenY, int w, int h,
            Color bg, Color border, Color textColor, int fontSize, Action onClick,
            bool isParentOffset = false, int parentX = 0, int parentY = 0, bool useTitleFont = false)
        {
            int localX = isParentOffset ? screenX - parentX : screenX;
            int localY = isParentOffset ? screenY - parentY : screenY;

            bool isOrdnanceCta = bg == AbramsGold;
            Color idleTint = isOrdnanceCta ? new Color(0.92f, 0.90f, 0.85f, 1f) : bg;
            Sprite btnSprite = isOrdnanceCta ? MilitaryUiSprites.OrdnanceButton : MilitaryUiSprites.SteelButton;
            var visualBox = MilitaryUiSprites.CreateSlicedSprite(visualParent, "Btn_" + label, btnSprite, localX, localY, w, h, idleTint, out Image fillImg);
            bool hasAccent = !isOrdnanceCta && border.a > 0.5f && border != PanelBorder && w >= 40;
            if (hasAccent)
                CreatePixelSolid(visualBox, "StatusBar", 2, 3, 2, Mathf.Max(2, h - 6), border);

            var txt = CreatePixelText(visualBox, label, fontSize, textColor, TextAnchor.MiddleCenter, hasAccent ? 2 : 0, 0, hasAccent ? w - 2 : w, h, useTitleFont);
            if (isOrdnanceCta)
            {
                var sh = txt.GetComponent<Shadow>();
                if (sh != null) sh.effectColor = new Color(1.0f, 0.94f, 0.68f, 0.55f);
            }

            var hitGo = new GameObject("Hit_" + label);
            hitGo.transform.SetParent(hitParent, false);
            var hitRt = hitGo.AddComponent<RectTransform>();
            hitRt.anchorMin = new Vector2((float)screenX / _W, (float)screenY / _H);
            hitRt.anchorMax = new Vector2((float)(screenX + w) / _W, (float)(screenY + h) / _H);
            hitRt.offsetMin = Vector2.zero;
            hitRt.offsetMax = Vector2.zero;

            var hitImg = hitGo.AddComponent<Image>();
            hitImg.color = new Color(0f, 0f, 0f, 0f);
            hitImg.raycastTarget = true;

            var btn = hitGo.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var trigger = hitGo.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                if (fillImg != null)
                    fillImg.color = isOrdnanceCta ? Color.white : BtnHover;
            });
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ =>
            {
                if (fillImg != null)
                    fillImg.color = idleTint;
            });
            trigger.triggers.Add(exit);

            return txt;
        }

        private void OnDestroy()
        {
            Font.textureRebuilt -= OnFontTextureRebuilt;
            if (_radarTex != null) Destroy(_radarTex);
        }
    }
}
