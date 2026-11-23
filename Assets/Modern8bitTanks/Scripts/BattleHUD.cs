using System;
using System.Collections.Generic;
using UnityEngine;
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
        private BattleSceneController _mgr;
        private PixelWindowbox _windowbox;
        private Font _font;
        private Font _titleFont;
        private int _W;
        private int _H;

        [SerializeField] private Canvas _pixelCanvas;
        [SerializeField] private RectTransform _menuHitRoot;
        [SerializeField] private RawImage _radarImage;

        private GameObject _resultHitsGroup;
        private GameObject _menuHitsGroup;
        [SerializeField] private GameObject _settingsHitsGroup;

        // Top Scoreboard
        [SerializeField] private Text _allyNameText;
        [SerializeField] private Text _enemyNameText;
        [SerializeField] private Text _allyWinsText;
        [SerializeField] private Text _enemyWinsText;
        [SerializeField] private Text _allyActiveText;
        [SerializeField] private Text _enemyActiveText;
        [SerializeField] private Text _centerBadgeText;

        // Bottom-Center Sleek War Thunder Action Bar + [1][2][3] Ammo Selector
        [SerializeField] private Image[] _ammoSlotBgs;
        [SerializeField] private Text[] _ammoSlotTexts;
        [SerializeField] private Image _reloadFill;
        [SerializeField] private Text _reloadStatusText;
        [SerializeField] private Image _smokeFill;
        [SerializeField] private Text _smokeStatusText;
        private const int ReloadTrackWidth = 54;
        private const int SmokeTrackWidth = 42;

        // Anti-Occlusion Panel Groups & HUD Density Cycle ([H] key)
        [SerializeField] private RectTransform _topScoreRect;
        [SerializeField] private CanvasGroup _topScoreCg;
        [SerializeField] private RectTransform _radarRect;
        [SerializeField] private CanvasGroup _radarCg;
        [SerializeField] private RectTransform _weaponRect;
        [SerializeField] private CanvasGroup _weaponCg;
        private int _hudDensityMode; // 0 = FULL, 1 = SLIM, 2 = MINIMAL
        private readonly List<Vector2> _occluderPointsPx = new(16);

        // Wave / Matchup announcement toast
        [SerializeField] private ControlPointHud _controlPointHud;
        [SerializeField] private GameObject _waveToastGo;
        [SerializeField] private Text _waveToastText;
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
        [SerializeField] private GameObject _settingsOverlay;
        [SerializeField] private Text _btnSfxLabel;
        [SerializeField] private Text _btnBgmLabel;
        [SerializeField] private Text _btnControlModeLabel;
        [SerializeField] private Text _btnAiLabel;
        [SerializeField] private Text _btnPostProcessLabel;
        [SerializeField] private Text _btnDisplayModeLabel;

        public bool IsSettingsOpen => _settingsOverlay != null && _settingsOverlay.activeSelf;
        public bool IsMenuOpen => (_menuOverlay != null && _menuOverlay.activeSelf) || IsSettingsOpen;

        // Floating popups
        [SerializeField] private RectTransform _popupLayer;
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
        private static readonly Color AbramsGold = new Color(0.96f, 0.78f, 0.38f, 1f);
        private static readonly Color SmokeCyan = new Color(0.56f, 0.86f, 0.96f, 1f);
        private static readonly Color DangerRed = new Color(0.96f, 0.30f, 0.22f, 1f);
        private static readonly Color ReadyGreen = new Color(0.56f, 0.94f, 0.42f, 1f);
        private static readonly Color TextMuted = new Color(0.70f, 0.70f, 0.60f, 1f);

        private MultiplayerSessionState _multiplayer;

        public void Initialize(BattleSceneController manager, PixelWindowbox windowbox, MultiplayerSessionState multiplayer)
        {
            _mgr = manager;
            _multiplayer = multiplayer;
            _windowbox = windowbox;
            _W = windowbox.renderWidth;
            _H = windowbox.renderHeight;

            _font = Resources.Load<Font>("Fonts/Silkscreen-Regular")
                    ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _titleFont = Resources.Load<Font>("Fonts/PressStart2P-Regular") ?? _font;

            ForceFontPointFilter(_font);
            ForceFontPointFilter(_titleFont);
            Font.textureRebuilt += OnFontTextureRebuilt;

            InitializeLayout();
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
            var txt = Instantiate(popupPrefab, _popupLayer, false);
            txt.text = label;
            txt.color = color;
            var rt = txt.rectTransform;

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
                    if (t == null || !t.alive || !_mgr.IsTankVisible(t)) continue;
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
            if (_multiplayer.IsInBattle)
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
                if (t == null || !t.alive || !_mgr.IsTankVisible(t)) continue;
                int tx = Mathf.Clamp(Mathf.RoundToInt((t.Position2D.x / worldW) * (RadarW - 1)), 1, RadarW - 2);
                int ty = Mathf.Clamp(Mathf.RoundToInt((t.Position2D.y / worldH) * (RadarH - 1)), 1, RadarH - 2);
                Color32 tc = t.isPlayer
                    ? new Color32(255, 232, 118, 255)
                    : (player != null && _multiplayer.IsInBattle ? t.factionTeam == player.factionTeam : t.factionTeam == "ally")
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
        private static void SetPixelRect(RectTransform rt, int x, int y, int w, int h)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private void OnDestroy()
        {
            Font.textureRebuilt -= OnFontTextureRebuilt;
            if (_radarTex != null) Destroy(_radarTex);
        }
    }
}
