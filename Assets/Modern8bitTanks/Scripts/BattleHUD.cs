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
    /// Pixel-aligned combat HUD for Battle.unity:
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
        [SerializeField] private Image _allyAccent;
        [SerializeField] private Image _enemyAccent;
        [SerializeField] private OnlineTankMarkers _onlineTankMarkers;

        // Reload, ammunition and smoke controls
        [SerializeField] private Image[] _ammoSlotBgs;
        [SerializeField] private Text[] _ammoSlotKeys;
        [SerializeField] private UnityEngine.UI.Image[] _ammoSlotIcons;
        [SerializeField, Tooltip("Sprites in AmmoType enum order.")]
        private Sprite[] _ammoTypeSprites;
        private static readonly string[] AmmoSlotKeys = { "1", "2", "3" };
        private static readonly Color AmmoIconIdle = new(0.77f, 0.77f, 0.77f, 1f);
        private static readonly Color AmmoIconCooling = new(0.62f, 0.62f, 0.62f, 0.64f);
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

        // Tactical minimap
        private Texture2D _radarTex;
        private Color32[] _radarPixels;
        private float _nextOnlineRadarUpdate;
        private int _radarWidth;
        private int _radarHeight;
        private bool[] _wallRadarMask;
        private BattlefieldLayout _wallRadarLayout;

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
            _W = PixelWindowbox.RenderWidth;
            _H = PixelWindowbox.RenderHeight;

            _font = Resources.Load<Font>("Fonts/Silkscreen-Regular")
                    ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _titleFont = Resources.Load<Font>("Fonts/PressStart2P-Regular") ?? _font;

            ForceFontPointFilter(_font);
            ForceFontPointFilter(_titleFont);
            Font.textureRebuilt += OnFontTextureRebuilt;

            InitializeLayout();
            _controlPointHud.SetVisible(_mgr.UsesControlPoints);
            if (_multiplayer.IsInBattle)
            {
                _allyAccent.color = _allyNameText.color = _allyWinsText.color = OnlineTeamColors.Friendly;
                _enemyAccent.color = _enemyNameText.color = _enemyWinsText.color = OnlineTeamColors.Hostile;
            }
            UiText.Prepare(_pixelCanvas, () =>
            {
                RefreshMenuLabels();
                RefreshBriefingLabels();
            });
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
            _waveToastText.text = UiText.Get(message);
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
            RefreshBriefingVisibility();
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
            RefreshBriefingVisibility();
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
            RefreshBriefingVisibility();
        }

        private void RefreshMenuLabels()
        {
            if (_mgr == null) return;
            if (_btnNextRivalLabel != null)
                _btnNextRivalLabel.text = UiText.Get(_mgr.battleMode == BattleMode.FiringRange ? "RESET RANGE" : "NEXT RIVAL");
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
                _btnControlModeLabel.text = UiText.Get(_mgr.arcadeWASD ? "DRIVE: ARCADE WASD" : "DRIVE: TANK STEER");
            if (_btnAiLabel != null)
                _btnAiLabel.text = UiText.Get(_mgr.aiEnabled ? "ENEMY AI: ON" : "ENEMY AI: OFF");
            if (_btnPostProcessLabel != null)
                _btnPostProcessLabel.text = UiText.Get(_mgr.postProcessEnabled ? "GFX: HD-2D" : "GFX: RETRO");
            if (_btnDisplayModeLabel != null)
                _btnDisplayModeLabel.text = UiText.Get(_mgr.integerWindowbox ? "VIEW: INT WINDOWBOX" : "VIEW: 4:3");
        }

        public void SpawnPopup(Vector2 worldPos, string label, Color color)
        {
            if (_popupLayer == null) return;
            var txt = Instantiate(popupPrefab, _popupLayer, false);
            txt.text = UiText.Get(label);
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
            _resultTitle.text = UiText.Get(playerWon ? "VICTORY" : "DEFEATED");
            _resultTitle.color = playerWon ? AbramsGold : DangerRed;
            _resultSubtitle.text = UiText.Get(subtitle);
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

            _allyWinsText.text = (_mgr.IsLocalAlpha ? winsAlly : winsEnemy).ToString();
            _enemyWinsText.text = (_mgr.IsLocalAlpha ? winsEnemy : winsAlly).ToString();
            UpdateControlHud();
            if (_onlineTankMarkers.gameObject.activeSelf != _mgr.ShowOnlineTankMarkers)
                _onlineTankMarkers.gameObject.SetActive(_mgr.ShowOnlineTankMarkers);
            if (_onlineTankMarkers.gameObject.activeSelf)
                _onlineTankMarkers.Render(cam, tanks, _mgr);

            _centerBadgeText.text = mode switch
            {
                BattleMode.FiringRange => "RNG",
                BattleMode.SurvivalWave => $"W{currentWave}",
                BattleMode.Battle5v5 => _multiplayer.IsInBattle ? "VS" : "5v5",
                BattleMode.Squad2v2 => "2v2",
                _ => "VS"
            };

            int allyAlive = 0, enemyAlive = 0;
            if (tanks != null)
            {
                foreach (var t in tanks)
                {
                    if (t == null || !t.alive) continue;
                    if ((t.factionTeam == "ally") == _mgr.IsLocalAlpha) allyAlive++;
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
                if (_allyNameText != null) _allyNameText.text = pDef != null ? TrimHudName(pDef.name, 7) : UiText.Get("YOU");
                if (_enemyNameText != null) _enemyNameText.text = UiText.Format("TGT x{0}", enemyAlive);
                if (_allyActiveText != null) _allyActiveText.text = "";
                if (_enemyActiveText != null) _enemyActiveText.text = "";
            }
            else if (mode == BattleMode.Battle5v5 || mode == BattleMode.Squad2v2)
            {
                if (_allyNameText != null) _allyNameText.text = UiText.Format("ALLY x{0}", allyAlive);
                if (_enemyNameText != null) _enemyNameText.text = UiText.Format("x{0} FOE", enemyAlive);
                if (_allyActiveText != null) _allyActiveText.text = "";
                if (_enemyActiveText != null) _enemyActiveText.text = "";
            }
            else if (mode == BattleMode.SurvivalWave)
            {
                if (_allyNameText != null) _allyNameText.text = pDef != null ? TrimHudName(pDef.name, 7) : UiText.Get("YOU");
                if (_enemyNameText != null) _enemyNameText.text = UiText.Format("FOE x{0}", enemyAlive);
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
                _reloadStatusText.text = UiText.Get("LOST");
                _reloadStatusText.color = DangerRed;
            }
            else if (!player.CanFire)
            {
                _reloadFill.rectTransform.anchorMax = new Vector2(0f, 1f);
                _reloadFill.color = DangerRed;
                _reloadStatusText.text = UiText.Get(player.Repairing ? "FIXING" : "GUN OUT");
                _reloadStatusText.color = player.Repairing ? AbramsGold : DangerRed;
            }
            else if (player.reloadTimer > 0f)
            {
                float prog = Mathf.Clamp01(1f - player.reloadTimer / Mathf.Max(0.01f, player.ActiveReloadDuration));
                _reloadFill.rectTransform.anchorMax = new Vector2(Mathf.Round(prog * ReloadTrackWidth) / ReloadTrackWidth, 1f);
                _reloadFill.color = AbramsGold;
                _reloadStatusText.text = player.reloadDuration < 0.4f ? UiText.Get("FIRE") : $"{player.reloadTimer:F1}s";
                _reloadStatusText.color = AbramsGold;
            }
            else
            {
                _reloadFill.rectTransform.anchorMax = new Vector2(1f, 1f);
                _reloadFill.color = ReadyGreen;
                _reloadStatusText.text = (pDef != null && pDef.weapon == "ATGM") ? "ATGM" : UiText.Get("READY");
                _reloadStatusText.color = ReadyGreen;
            }

            if (player != null) UpdateAmmoSlots(player);

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
                    _smokeStatusText.text = UiText.Get("SMK[Q]");
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

        private void UpdateAmmoSlots(TankEntity player)
        {
            AmmoType[] loadout = player.AmmoLoadout;
            bool hasSecondary = player.vehicleDef != null && player.vehicleDef.HasSecondaryAtgm;
            for (int i = 0; i < _ammoSlotIcons.Length; i++)
            {
                var icon = _ammoSlotIcons[i];
                var label = _ammoSlotKeys[i];
                if (i < loadout.Length)
                {
                    bool selected = player.selectedAmmoSlot == i;
                    icon.sprite = _ammoTypeSprites[(int)loadout[i]];
                    icon.enabled = true;
                    icon.color = selected ? Color.white : AmmoIconIdle;
                    label.text = AmmoSlotKeys[i];
                    label.color = selected ? AbramsGold : TextMuted;
                    _ammoSlotBgs[i].color = selected ? PanelBgActive : PanelBg;
                }
                else if (i == BattleSceneController.SecondaryWeaponSlotIndex && hasSecondary)
                {
                    bool ready = player.atgmCooldown <= 0f;
                    icon.sprite = _ammoTypeSprites[(int)AmmoType.TOW];
                    icon.enabled = true;
                    icon.color = ready ? Color.white : AmmoIconCooling;
                    label.text = ready ? AmmoSlotKeys[i] : $"{Mathf.CeilToInt(player.atgmCooldown)}s";
                    label.color = ready ? SmokeCyan : TextMuted;
                    _ammoSlotBgs[i].color = ready ? PanelBgActive : PanelBg;
                }
                else
                {
                    icon.enabled = false;
                    label.text = string.Empty;
                    _ammoSlotBgs[i].color = PanelBg;
                }
            }
        }

        private void UpdateControlHud()
        {
            bool visible = _mgr.UsesControlPoints;
            _controlPointHud.SetVisible(visible);
            if (!visible) return;
            int localTeam = _mgr.IsLocalAlpha ? 1 : -1;
            Vector2 scores = _mgr.ControlScores;
            _controlPointHud.Render(_mgr.ControlPoint, localTeam == 1 ? scores : new Vector2(scores.y, scores.x),
                _mgr.ControlTeamColor(localTeam), _mgr.ControlTeamColor(-localTeam), _mgr.ControlPointColor());
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
            var mapScale = new Vector2((_radarWidth - 1) / worldW, (_radarHeight - 1) / worldH);
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
            var layout = _mgr.CurrentLayout;
            Color32 bldgCol = new Color32(138, 134, 128, 255);
            if (layout != null && _wallRadarLayout != layout)
            {
                _wallRadarLayout = layout;
                _wallRadarMask ??= new bool[_radarWidth * _radarHeight];
                for (int y = 0; y < _radarHeight; y++)
                for (int x = 0; x < _radarWidth; x++)
                    _wallRadarMask[y * _radarWidth + x] = layout.IsWallAt(new Vector2(
                        x * BattlefieldLayout.Width / (float)(_radarWidth - 1),
                        y * BattlefieldLayout.Height / (float)(_radarHeight - 1)));
            }
            for (int y = 0; y < _radarHeight; y++)
            {
                for (int x = 0; x < _radarWidth; x++)
                {
                    int i = y * _radarWidth + x;
                    bool line = x % 17 == 0 || y % 14 == 0;
                    _radarPixels[i] = layout != null && _wallRadarMask[i] ? bldgCol : line ? grid : bg;
                }
            }
            if (layout != null) PlotRadarBoundary(mapScale, Color32.Lerp(grid, BattlefieldLayout.BoundaryColor, 0.55f));

            Color32 propCol = new Color32(148, 126, 90, 255);
            Color32 rockCol = new Color32(126, 114, 96, 255);
            Color32 treeCol = new Color32(72, 146, 62, 255);
            Color32 explosiveCol = new Color32(232, 74, 50, 255);
            foreach (var prop in props)
            {
                if (prop == null || !prop.alive) continue;
                Vector2 position = Vector2.Scale(prop.transform.position, mapScale);
                int px = Mathf.Clamp(Mathf.RoundToInt(position.x), 1, _radarWidth - 2);
                int py = Mathf.Clamp(Mathf.RoundToInt(position.y), 1, _radarHeight - 2);
                if (prop.meta != null && prop.meta.fixedCover)
                {
                    if (layout != null) continue;
                    int halfWidth = Mathf.FloorToInt(prop.meta.width / prop.pixelsPerUnit * mapScale.x * 0.5f);
                    int halfHeight = Mathf.FloorToInt(prop.meta.length * prop.stack.squash / prop.pixelsPerUnit * mapScale.y * 0.5f);
                    PlotRadarRect(px, py, halfWidth, halfHeight, bldgCol);
                    continue;
                }
                bool isHuge = prop.meta != null && prop.meta.heavyStructure;
                Color32 pCol = (prop.kind == "supply_crate" || prop.kind == "fuel_tank" || prop.kind == "ammo_depot") ? explosiveCol
                    : (prop.kind == "palm_tree" || prop.kind == "pine_cluster" || prop.kind == "broadleaf_grove" || prop.kind == "desert_scrub" ||
                       prop.kind == "dead_tree" || prop.kind == "autumn_tree" || prop.kind == "bush_cluster" || prop.kind == "fallen_log" || prop.kind == "tree_stump" ? treeCol
                    : (prop.kind.StartsWith("city_") || prop.kind == "forest_cabin" || prop.kind == "desert_compound" || prop.kind == "cargo_container" || prop.kind == "radar_station" ||
                       prop.kind == "guard_booth" || prop.kind == "water_tower" || prop.kind == "concrete_wall" || prop.kind == "military_tent" ||
                       prop.kind == "wrecked_truck" || prop.kind == "steel_silo" || prop.kind == "street_kiosk" ? bldgCol
                    : (prop.kind == "desert_bunker" || prop.kind == "desert_rock" || prop.kind == "desert_mesa" || prop.kind == "forest_boulder" || prop.kind == "moss_rocks" || prop.kind == "rock_arch" ? rockCol : propCol)));
                int radius = isHuge ? 1 : (prop.kind == "desert_bunker" || prop.kind == "desert_rock" ? 1 : 0);
                PlotDot(px, py, radius, pCol);
            }

            if (smokes != null)
            {
                Color32 smkCol = new Color32(195, 202, 208, 255);
                foreach (var sm in smokes)
                {
                    Vector2 position = Vector2.Scale(sm.pos, mapScale);
                    int sx = Mathf.Clamp(Mathf.RoundToInt(position.x), 2, _radarWidth - 3);
                    int sy = Mathf.Clamp(Mathf.RoundToInt(position.y), 2, _radarHeight - 3);
                    PlotDot(sx, sy, 2, smkCol);
                }
            }

            if (player != null && player.alive && _mgr.PlayerGroundAim is { } aim)
                PlotRadarAim(Vector2.Scale(aim.Start, mapScale), Vector2.Scale(aim.End, mapScale));

            if (layout != null)
            {
                Vector2Int position = Vector2Int.RoundToInt(Vector2.Scale(_mgr.ControlPointPosition, mapScale));
                Color32 color = _mgr.ControlPointColor();
                for (int y = -3; y <= 3; y++)
                for (int x = -3; x <= 3; x++)
                {
                    int distanceSquared = x * x + y * y;
                    if (distanceSquared >= 6 && distanceSquared <= 12)
                        PlotRadarPixel(position.x + x, position.y + y, color);
                }
            }

            foreach (var t in tanks)
            {
                if (t == null || t == player || !t.alive || !_mgr.IsTankVisible(t)) continue;
                Vector2 contact = Vector2.Scale(t.Position2D, mapScale);
                int tx = Mathf.Clamp(Mathf.RoundToInt(contact.x), 1, _radarWidth - 2);
                int ty = Mathf.Clamp(Mathf.RoundToInt(contact.y), 1, _radarHeight - 2);
                bool ally = (t.factionTeam == "ally") == _mgr.IsLocalAlpha;
                Color32 tc = _multiplayer.IsInBattle ? OnlineTeamColors.ForRelation(ally)
                    : ally ? new Color32(82, 214, 255, 255) : new Color32(255, 92, 74, 255);
                PlotDot(tx, ty, 1, tc);
            }

            if (player != null && player.alive)
            {
                Vector2 position = Vector2.Scale(player.Position2D, mapScale);
                Vector2 heading = SpriteStackCamera.Squash(TankEntity.SliceAngleToDir(player.hullAngleDeg), _mgr.squash);
                PlotRadarPlayer(Vector2Int.RoundToInt(position), Vector2.Scale(heading, mapScale).normalized);
            }

            _radarTex.SetPixels32(_radarPixels);
            _radarTex.Apply(false, false);
        }

        private void PlotRadarBoundary(Vector2 mapScale, Color32 color)
        {
            var min = Vector2Int.RoundToInt(mapScale * (BattleSceneController.BorderPadPx / BattleSceneController.PPU));
            int right = _radarWidth - 1 - min.x;
            int top = _radarHeight - 1 - min.y;
            for (int x = min.x; x <= right; x++)
            {
                PlotRadarPixel(x, min.y, color);
                PlotRadarPixel(x, top, color);
            }
            for (int y = min.y + 1; y < top; y++)
            {
                PlotRadarPixel(min.x, y, color);
                PlotRadarPixel(right, y, color);
            }
        }

        private void PlotRadarAim(Vector2 start, Vector2 end)
        {
            Vector2 delta = end - start;
            int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y)));
            var color = new Color32(218, 166, 74, 255);
            for (int i = 0; i < steps; i++)
            {
                Vector2 point = start + delta * (i / (float)steps);
                PlotRadarPixel(Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.y), color);
            }
            PlotRadarPixel(Mathf.RoundToInt(end.x), Mathf.RoundToInt(end.y), new Color32(255, 230, 138, 255));
        }

        private void PlotRadarPlayer(Vector2Int center, Vector2 forward)
        {
            Vector2 side = Vector2.Perpendicular(forward);
            var trackColor = new Color32(200, 149, 66, 255);
            var hullColor = new Color32(255, 216, 106, 255);
            var noseColor = new Color32(255, 249, 202, 255);
            if (_multiplayer.IsInBattle)
            {
                hullColor = OnlineTeamColors.Friendly;
                trackColor = Color32.Lerp(hullColor, Color.black, 0.25f);
                noseColor = Color32.Lerp(hullColor, Color.white, 0.6f);
            }
            for (int y = -4; y <= 4; y++)
            for (int x = -4; x <= 4; x++)
            {
                var offset = new Vector2(x, y);
                float along = Vector2.Dot(offset, forward);
                float across = Mathf.Abs(Vector2.Dot(offset, side));
                if (across <= 0.8f && along > 1.5f && along <= 3.4f)
                    PlotRadarPixel(center.x + x, center.y + y, noseColor);
                else if (across <= 1.3f && along >= -1.8f && along <= 1.8f)
                    PlotRadarPixel(center.x + x, center.y + y, hullColor);
                else if (across > 1.3f && across <= 2.7f && along >= -2.4f && along <= 1.6f)
                    PlotRadarPixel(center.x + x, center.y + y, trackColor);
            }
        }

        private void PlotRadarPixel(int x, int y, Color32 color)
        {
            if (x >= 0 && x < _radarWidth && y >= 0 && y < _radarHeight)
                _radarPixels[y * _radarWidth + x] = color;
        }

        private void PlotDot(int cx, int cy, int r, Color32 col) => PlotRadarRect(cx, cy, r, r, col);

        private void PlotRadarRect(int cx, int cy, int halfWidth, int halfHeight, Color32 col)
        {
            for (int y = cy - halfHeight; y <= cy + halfHeight; y++)
            for (int x = cx - halfWidth; x <= cx + halfWidth; x++)
                PlotRadarPixel(x, y, col);
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
            if (_briefingTexture != null) Destroy(_briefingTexture);
        }
    }
}
