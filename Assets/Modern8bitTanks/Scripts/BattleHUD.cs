using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
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
        private const int RadarW = 68;
        private const int RadarH = 44;

        // Result Overlay (smooth fade-in after kill aftermath)
        private GameObject _resultOverlay;
        private CanvasGroup _resultCanvasGroup;
        private float _resultFadeAlpha;
        private bool _resultShowing;
        private Text _resultTitle;
        private Text _resultSubtitle;

        // Pause / Options Modal
        private GameObject _menuOverlay;
        private Text _btnControlModeLabel;
        private Text _btnAiLabel;
        private Text _btnSfxLabel;
        private Text _btnPostProcessLabel;
        private Text _btnDisplayModeLabel;

        public bool IsMenuOpen => _menuOverlay != null && _menuOverlay.activeSelf;

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

        // Crisp War Thunder Minimalist Dark-Glass Palette
        private static readonly Color PanelBg = new Color(0.05f, 0.06f, 0.07f, 0.72f);
        private static readonly Color PanelBgActive = new Color(0.11f, 0.13f, 0.14f, 0.78f);
        private static readonly Color PanelBorder = new Color(0.30f, 0.35f, 0.38f, 0.80f);
        private static readonly Color BtnBg = new Color(0.13f, 0.14f, 0.12f, 0.94f);
        private static readonly Color BtnHover = new Color(0.24f, 0.22f, 0.16f, 0.98f);
        private static readonly Color AbramsGold = new Color(0.95f, 0.78f, 0.44f, 1f);
        private static readonly Color T90Green = new Color(0.52f, 0.84f, 0.36f, 1f);
        private static readonly Color SmokeCyan = new Color(0.62f, 0.86f, 0.96f, 1f);
        private static readonly Color DangerRed = new Color(0.96f, 0.30f, 0.22f, 1f);
        private static readonly Color ReadyGreen = new Color(0.56f, 0.94f, 0.42f, 1f);
        private static readonly Color TextLight = new Color(0.96f, 0.95f, 0.90f, 1f);
        private static readonly Color TextMuted = new Color(0.70f, 0.68f, 0.58f, 1f);
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

            EnsureEventSystem();
            BuildPixelHudCanvas();
            BuildHitGroups();
            BuildCombatHud();
            BuildResultOverlay();
            BuildHitCameraLayer();
            BuildPauseMenuOverlay();
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

        private void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();
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
            _resultHitsGroup.SetActive(false);
            _menuHitsGroup.SetActive(false);
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

            // 1. Ultra-Slim Top-Center Score & Ticket Bar (176 x 16 px EVEN, tucked cleanly at top edge!)
            int bx = (_W - 176) / 2;
            int by = _H - 18;
            _topScoreRect = CreatePixelBox(parent, "TopScoreboard", bx, by, 176, 16, PanelBg, PanelBorder);
            _topScoreCg = _topScoreRect.gameObject.AddComponent<CanvasGroup>();
            _topScoreCg.blocksRaycasts = false;

            // Thin team color bars on left (Ally Gold) & right (Enemy Red/Green)
            CreatePixelSolid(_topScoreRect, "AllyAccent", 1, 1, 2, 14, AbramsGold);
            CreatePixelSolid(_topScoreRect, "EnemyAccent", 173, 1, 2, 14, DangerRed);

            _allyNameText = CreatePixelText(_topScoreRect, "ALLY", 8, AbramsGold, TextAnchor.MiddleLeft, 5, 3, 56, 10);
            _allyActiveText = CreatePixelText(_topScoreRect, "", 8, TextMuted, TextAnchor.MiddleRight, 46, 3, 22, 10);
            _allyWinsText = CreatePixelText(_topScoreRect, "0", 8, AbramsGold, TextAnchor.MiddleRight, 66, 3, 12, 10, useTitleFont: true);

            _centerBadgeText = CreatePixelText(_topScoreRect, "VS", 8, TextMuted, TextAnchor.MiddleCenter, 78, 3, 20, 10);

            _enemyWinsText = CreatePixelText(_topScoreRect, "0", 8, DangerRed, TextAnchor.MiddleLeft, 98, 3, 12, 10, useTitleFont: true);
            _enemyActiveText = CreatePixelText(_topScoreRect, "", 8, TextMuted, TextAnchor.MiddleLeft, 108, 3, 22, 10);
            _enemyNameText = CreatePixelText(_topScoreRect, "FOE", 8, DangerRed, TextAnchor.MiddleRight, 115, 3, 56, 10);

            // 2. Clean Borderless-Header Top-Right Tactical Minimap (72 x 48 EVEN!)
            int rw = RadarW + 4;
            int rh = RadarH + 4;
            _radarRect = CreatePixelBox(parent, "RadarPanel", _W - rw - 4, _H - rh - 4, rw, rh, PanelBg, PanelBorder);
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
            SetPixelRect(mapGo.GetComponent<RectTransform>(), 2, 2, RadarW, RadarH);

            // 3. Bottom-Left War Thunder X-Ray Damage Doll & Auto-Docking Live Hit Camera
            BuildDamageHud(parent, rh);

            // 4. Sleek Bottom-Center War Thunder Action Bar + [1][2][3] Ammo Slots (246 x 18 EVEN — NO menu buttons!)
            const int pw = 246;
            _weaponRect = CreatePixelBox(parent, "WeaponPanel", (_W - pw) / 2, 4, pw, 18, PanelBg, PanelBorder);
            _weaponCg = _weaponRect.gameObject.AddComponent<CanvasGroup>();
            _weaponCg.blocksRaycasts = false;

            // Left: Primary Reload Bar + Status
            _reloadStatusText = CreatePixelText(_weaponRect, "READY", 8, ReadyGreen, TextAnchor.MiddleCenter, 4, 7, ReloadTrackWidth, 9);
            var reloadTrack = CreatePixelSolid(_weaponRect, "ReloadTrack", 4, 3, ReloadTrackWidth, 3, new Color(0.04f, 0.05f, 0.06f, 0.92f));
            _reloadFill = CreateFillBar(reloadTrack, ReadyGreen);

            CreatePixelSolid(_weaponRect, "Sep1", 60, 3, 1, 12, PanelBorder);

            // Middle: 3 EVEN-sized War Thunder Ammo Slots ([1] [2] [3] or [1] [2] + R:TOW!)
            _ammoSlotBgs = new Image[3];
            _ammoSlotTexts = new Text[3];
            for (int i = 0; i < 3; i++)
            {
                int sx = 64 + i * 44;
                var slotBox = CreatePixelBox(_weaponRect, $"AmmoSlot_{i + 1}", sx, 2, 42, 14, PanelBg, PanelBorder, out Image bgImg);
                _ammoSlotBgs[i] = bgImg;
                _ammoSlotTexts[i] = CreatePixelText(slotBox, $"{i + 1}---", 8, TextLight, TextAnchor.MiddleCenter, 0, 1, 42, 12);
            }

            CreatePixelSolid(_weaponRect, "Sep2", 196, 3, 1, 12, PanelBorder);

            // Right: Smoke [Q] Bar
            _smokeStatusText = CreatePixelText(_weaponRect, "SMK[Q]", 8, SmokeCyan, TextAnchor.MiddleCenter, 200, 7, SmokeTrackWidth, 9);
            var smokeTrack = CreatePixelSolid(_weaponRect, "SmokeTrack", 200, 3, SmokeTrackWidth, 3, new Color(0.04f, 0.05f, 0.06f, 0.92f));
            _smokeFill = CreateFillBar(smokeTrack, SmokeCyan);

            // Wave / Matchup Toast Banner (top-center cleanly below slim score strip, 212 x 16 EVEN!)
            var toast = CreatePixelBox(parent, "WaveToast", (_W - 212) / 2, _H - 38, 212, 16, new Color(0.06f, 0.07f, 0.08f, 0.86f), AbramsGold);
            _waveToastGo = toast.gameObject;
            _waveToastText = CreatePixelText(toast, "ENGAGE!", 8, AbramsGold, TextAnchor.MiddleCenter, 0, 0, 212, 16, useTitleFont: true);
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

            int cw = 236;
            int ch = 86;
            int cx = (_W - cw) / 2;
            int cy = 62; // Lowered slightly so Match-End X-Ray Hit Camera (top-left Y:172..266) NEVER overlaps!
            var card = CreatePixelBox(dim, "ResultCard", cx, cy, cw, ch, new Color(0.09f, 0.10f, 0.09f, 0.96f), AbramsGold);

            _resultTitle = CreatePixelText(card, "VICTORY", 16, AbramsGold, TextAnchor.MiddleCenter, 0, 52, cw, 24, useTitleFont: true);
            _resultSubtitle = CreatePixelText(card, "VICTORY", 8, TextMuted, TextAnchor.MiddleCenter, 0, 34, cw, 14);

            CreatePixelButton(card, _resultHitsGroup.transform, "NEXT BATTLE (R)", cx + 10, cy + 8, 126, 20, AbramsGold, AbramsGold, DarkInk, 8, () =>
            {
                _mgr.ResetRound();
            }, isParentOffset: true, parentX: cx, parentY: cy);

            CreatePixelButton(card, _resultHitsGroup.transform, "HANGAR", cx + 142, cy + 8, 84, 20, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _mgr.ReturnToHangar();
            }, isParentOffset: true, parentX: cx, parentY: cy);

            _resultOverlay.SetActive(false);
        }

        private void BuildPauseMenuOverlay()
        {
            var dim = CreatePixelSolid(_hudRoot, "PauseMenuOverlay", 0, 0, _W, _H, new Color(0.03f, 0.03f, 0.03f, 0.64f));
            _menuOverlay = dim.gameObject;

            int cw = 216;
            int ch = 180;
            int cx = (_W - cw) / 2;
            int cy = (_H - ch) / 2;
            var card = CreatePixelBox(dim, "MenuCard", cx, cy, cw, ch, new Color(0.09f, 0.10f, 0.09f, 0.98f), PanelBorder);
            CreatePixelText(card, "TACTICAL MENU", 8, AbramsGold, TextAnchor.MiddleCenter, 0, ch - 20, cw, 16, useTitleFont: true);

            _btnControlModeLabel = CreatePixelButton(card, _menuHitsGroup.transform, "", cx + 10, cy + 138, cw - 20, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _mgr.arcadeWASD = !_mgr.arcadeWASD;
                _mgr.SyncSettingsToSession();
                RefreshMenuLabels();
            }, isParentOffset: true, parentX: cx, parentY: cy);

            _btnAiLabel = CreatePixelButton(card, _menuHitsGroup.transform, "", cx + 10, cy + 116, cw - 20, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _mgr.aiEnabled = !_mgr.aiEnabled;
                _mgr.SyncSettingsToSession();
                RefreshMenuLabels();
            }, isParentOffset: true, parentX: cx, parentY: cy);

            _btnSfxLabel = CreatePixelButton(card, _menuHitsGroup.transform, "", cx + 10, cy + 94, cw - 20, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _mgr.sfxEnabled = !_mgr.sfxEnabled;
                _mgr.SyncSettingsToSession();
                RefreshMenuLabels();
            }, isParentOffset: true, parentX: cx, parentY: cy);

            _btnPostProcessLabel = CreatePixelButton(card, _menuHitsGroup.transform, "", cx + 10, cy + 72, cw - 20, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _mgr.TogglePostProcess();
                _mgr.SyncSettingsToSession();
                RefreshMenuLabels();
            }, isParentOffset: true, parentX: cx, parentY: cy);

            _btnDisplayModeLabel = CreatePixelButton(card, _menuHitsGroup.transform, "", cx + 10, cy + 50, cw - 20, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _mgr.integerWindowbox = !_mgr.integerWindowbox;
                _mgr.SyncSettingsToSession();
                RefreshMenuLabels();
            }, isParentOffset: true, parentX: cx, parentY: cy);

            CreatePixelButton(card, _menuHitsGroup.transform, "BACK TO HANGAR", cx + 10, cy + 28, cw - 20, 18, BtnBg, AbramsGold, AbramsGold, 8, () =>
            {
                SetMenuOpen(false);
                _mgr.ReturnToHangar();
            }, isParentOffset: true, parentX: cx, parentY: cy);

            CreatePixelButton(card, _menuHitsGroup.transform, "RESUME", cx + 10, cy + 6, 94, 18, AbramsGold, AbramsGold, DarkInk, 8, ToggleMenu, isParentOffset: true, parentX: cx, parentY: cy);
            CreatePixelButton(card, _menuHitsGroup.transform, "NEXT RIVAL", cx + 112, cy + 6, 94, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                SetMenuOpen(false);
                _mgr.ResetRound();
            }, isParentOffset: true, parentX: cx, parentY: cy);

            RefreshMenuLabels();
            _menuOverlay.SetActive(false);
        }

        public void ToggleMenu()
        {
            SetMenuOpen(!IsMenuOpen);
        }

        private void SetMenuOpen(bool open)
        {
            if (_menuOverlay == null) return;
            _menuOverlay.SetActive(open);
            if (_menuHitsGroup != null) _menuHitsGroup.SetActive(open);
            if (open) RefreshMenuLabels();
        }

        private void RefreshMenuLabels()
        {
            if (_mgr == null) return;
            if (_btnControlModeLabel != null)
                _btnControlModeLabel.text = _mgr.arcadeWASD ? "DRIVE: ARCADE WASD" : "DRIVE: TANK STEER";
            if (_btnAiLabel != null)
                _btnAiLabel.text = _mgr.aiEnabled ? "ENEMY AI: ON" : "ENEMY AI: OFF";
            if (_btnSfxLabel != null)
                _btnSfxLabel.text = _mgr.sfxEnabled ? "AUDIO: ON" : "AUDIO: OFF";
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

            UpdateRadarTexture(props, tanks, smokes, worldW, worldH);

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

        private void UpdateRadarTexture(List<DestructibleProp> props, List<TankEntity> tanks, List<RadarSmokeInfo> smokes, float worldW, float worldH)
        {
            if (_radarTex == null) return;
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
                if (t == null || !t.alive) continue;
                int tx = Mathf.Clamp(Mathf.RoundToInt((t.Position2D.x / worldW) * (RadarW - 1)), 1, RadarW - 2);
                int ty = Mathf.Clamp(Mathf.RoundToInt((t.Position2D.y / worldH) * (RadarH - 1)), 1, RadarH - 2);
                Color32 tc = t.isPlayer
                    ? new Color32(255, 232, 118, 255)
                    : (t.factionTeam == "ally"
                        ? new Color32(82, 214, 255, 255)
                        : new Color32(255, 92, 74, 255));
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

            var visualBox = CreatePixelBox(visualParent, "Btn_" + label, localX, localY, w, h, bg, border, out Image fillImg);
            var txt = CreatePixelText(visualBox, label, fontSize, textColor, TextAnchor.MiddleCenter, 0, 0, w, h, useTitleFont);

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
                    fillImg.color = bg == AbramsGold ? Color.Lerp(bg, Color.white, 0.2f) : BtnHover;
            });
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ =>
            {
                if (fillImg != null)
                    fillImg.color = bg;
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
