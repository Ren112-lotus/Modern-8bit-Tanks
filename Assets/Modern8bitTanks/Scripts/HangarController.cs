using Cysharp.Threading.Tasks;
using VContainer;
using R3;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    /// <summary>
    /// Dedicated Garage / Hangar & Horizontal-Scrolling World-of-Tanks Tech Tree Scene (Hangar.unity):
    /// - Shows the selected tank on a rotating turntable with the selected map as its backdrop
    /// - True Horizontal-Scrolling WoT Tech Tree (Tier I -> V trunk forking into VI -> X MBT+SP / LIGHT / SUPPORT)
    ///   with Mouse Wheel, Click-Drag, Arrow Keys, & Pixel Scrollbar
    /// - Supports 1v1, 2v2, 5v5, & Survival Wave modes -> launches into Battle.unity.
    /// - Strictly native 8px / 16px HintedRaster pixel fonts on EVEN pixel rects (zero blurry text!)
    /// </summary>
    public partial class HangarController : MonoBehaviour
    {
        public const float PPU = 16f;
        private float WorldW => BattleSceneController.WorldWidthPx / PPU;
        private float WorldH => BattleSceneController.WorldHeightPx / PPU;

        [Header("Scene References")]
        [UnityEngine.Serialization.FormerlySerializedAs("desertGroundRenderer")]
        public SpriteRenderer mapBackdropRenderer;
        [SerializeField] private Sprite _garageBackdrop;
        public TankEntity showcaseTank;

        [Header("Display Settings")]
        [Range(0.45f, 1.0f)] public float squash = 0.80f;
        [Range(0f, 2.5f)] public float spacingPx = 0.72f;
        [Range(1.0f, 3.0f)] public float showcaseZoom = 2.0f;
        private Camera _cam;
        private BattleSessionState _sessionState;
        private GameSceneLoader _sceneLoader;
        private MultiplayerSessionState _multiplayer;

        [Inject]
        private void Construct(Camera worldCamera, BattleSessionState sessionState, GameSceneLoader sceneLoader,
            MultiplayerSessionState multiplayer,
            PixelWindowbox windowbox, BattlePostProcess postProcess, MapAtmosphere atmosphere)
        {
            _cam = worldCamera;
            _sessionState = sessionState;
            _sceneLoader = sceneLoader;
            _multiplayer = multiplayer;
            _windowbox = windowbox;
            _postProcess = postProcess;
            _atmosphere = atmosphere;
        }
        private PixelWindowbox _windowbox;
        private BattlePostProcess _postProcess;
        private MapAtmosphere _atmosphere;

        private Font _font;
        private Font _titleFont;
        [SerializeField] private Material _stackFlashMat;
        [SerializeField] private Material _groundHd2dMat;

        private VehicleCatalogIndex _vehicleCatalog = VehicleCatalogIndex.Empty;
        private readonly VehicleSliceSheets _vehicleSheets = new();
        private float _maxViewHeightPixels;

        private bool _settingsOpen = false;
        [SerializeField] private GameObject _hangarXrayWrap;
        [SerializeField] private TankCutawayGraphic _hangarXrayGraphic;
        [SerializeField] private Image _btnXrayBg;
        [SerializeField] private Text _hangarXrayBadge;

        [SerializeField] private RectTransform _treeContentRt;
        [SerializeField] private RectTransform _scrollThumbRt;
        // UI Roots
        [SerializeField] private Canvas _pixelCanvas;
        [SerializeField] private RectTransform _menuHitRoot;
        [SerializeField] private RawImage _hangarXrayBackdrop;
        [SerializeField] private GameObject _garageVisualGroup;
        [SerializeField] private GameObject _garageHitsGroup;
        [SerializeField] private GameObject _treeVisualGroup;
        [SerializeField] private GameObject _treeHitsGroup;
        [SerializeField] private GameObject _settingsVisualGroup;
        [SerializeField] private GameObject _settingsHitsGroup;

        // Settings UI Labels & Preview Audio
        [SerializeField] private Text _setSfxLabel;
        [SerializeField] private Text _setBgmLabel;
        [SerializeField] private Text _setDriveLabel;
        [SerializeField] private Text _setAiLabel;
        [SerializeField] private Text _setGfxLabel;
        [SerializeField] private Text _setViewLabel;
        [SerializeField] private AudioSource _previewAudio;
        private AudioClip _previewSfxClip;

        // Garage Tactical Command UI References
        [Serializable]
        private class StatBarSlot
        {
            public Image[] pips;
            public Text valueText;
            public Color fillColor;
        }

        [SerializeField] private Text _garageTopBadge;
        [SerializeField] private Text _garageIndexBadge;
        [SerializeField] private Image _garageNationStripe;
        [SerializeField] private Image _garageSpecAccent;
        [SerializeField] private Text _garageYearBadge;
        [SerializeField] private Text _garageTankName;
        [SerializeField] private Image _garageNameUnderline;
        [SerializeField] private Text _garageCrewLine;
        [SerializeField] private Text _garageEngineLine;
        [SerializeField] private Text _garageLayoutLine;
        [SerializeField] private Text _garageRoleLine;
        [SerializeField] private Text _garageWeaponLine;
        [SerializeField] private Text _garageAmmoLine;
        [SerializeField] private Text _garageSpecialLine;
        [SerializeField] private Text _garageModeLine;
        [SerializeField] private Text _garageMapLine;
        [SerializeField] private Text _garageMatchLine;
        [SerializeField] private Text _garageNationBtnLabel;
        [SerializeField] private StatBarSlot _statArm;
        [SerializeField] private StatBarSlot _statPen;
        [SerializeField] private StatBarSlot _statRld;
        [SerializeField] private StatBarSlot _statSpd;
        [SerializeField] private StatBarSlot _statTrn;
        [SerializeField] private StatBarSlot _statTur;
        [SerializeField] private StatBarSlot _statView;

        [SerializeField] private Text _treeDetailTitle;
        [SerializeField] private Text _treeDetailStats;

        // Gritty Military Olive-Drab Steel & Brass Warfare Palette
        private static readonly Color PanelBorder = new Color(0.42f, 0.38f, 0.26f, 1f);
        private static readonly Color PipEmpty = new Color(0.09f, 0.11f, 0.09f, 0.96f);
        private static readonly Color GoldColor = new Color(0.96f, 0.78f, 0.34f, 1f);
        private static readonly Color GoldHi = new Color(1.00f, 0.93f, 0.66f, 1f);
        private static readonly Color SpecialBorder = new Color(0.96f, 0.76f, 0.28f, 1f);
        private static readonly Color BtnBg = new Color(0.26f, 0.29f, 0.23f, 0.98f);
        private static readonly Color BtnHover = new Color(0.40f, 0.42f, 0.32f, 1f);
        private static readonly Color BtnSelected = new Color(0.54f, 0.43f, 0.19f, 1f);
        private static readonly Color TextLight = new Color(0.96f, 0.95f, 0.89f, 1f);
        private static readonly Color TextMuted = new Color(0.68f, 0.70f, 0.60f, 1f);
        private static readonly Color LightCyan = new Color(0.52f, 0.88f, 0.94f, 1f);
        private static readonly Color SupportAmber = new Color(0.96f, 0.64f, 0.30f, 1f);
        private static readonly Color MbtGreen = new Color(0.58f, 0.88f, 0.44f, 1f);

        private static Color GetNationAccentColor(string nation) => nation switch
        {
            "USA" => new Color(0.36f, 0.68f, 0.92f, 1f),
            "GER" => new Color(0.90f, 0.70f, 0.28f, 1f),
            "USSR" => new Color(0.88f, 0.36f, 0.30f, 1f),
            "JPN" => new Color(0.92f, 0.38f, 0.42f, 1f),
            _ => GoldColor
        };

        private void Awake()
        {
            Time.timeScale = 1f;
            SpriteStackCamera.SetRoll(0f);

            _cam.orthographic = true;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.11f, 0.09f, 0.06f, 1f);
            squash = 0.80f;
            spacingPx = 0.72f;

            _windowbox.Initialize(_cam, _sessionState.IntegerWindowbox, useHd2d: _sessionState.PostProcessEnabled);
            _windowbox.SetTiltShiftParams(1.15f, 0.52f, 0.24f);

            _postProcess.postProcessEnabled = _sessionState.PostProcessEnabled;
            // Apply post-processing at screen resolution after the low-resolution hangar render.
            _postProcess.Initialize(_windowbox.PresentCamera);

            LoadResourcesAndCatalog();
            SetupMapBackdrop();
            SetupCenterShowcaseTank();

            _atmosphere.SetVisible(false);
            _sessionState.SelectedBattleMapChanged.Subscribe(RefreshMapBackdropTheme).AddTo(this);

            InitializeMenus();
            InitializeOnlineProfile();
            InitializeTechTree();
            InitializeTitleStage();

            VehicleDef cur = GetSelectedVehicle();
            if (cur != null) _treeNation = cur.nation;
            _currentShowcaseZoom = GetAdaptiveShowcaseZoom(cur);
            ApplySelectedTankToTurntable();
            SetSettingsOpen(false);
            SetDeployModalOpen(false);
            SetTechTreeOpen(false);
            // Show the Sprite-Stacking Title Screen on initial launch; return straight to Garage after battles!
            SetTitleOpen(!_sessionState.HasEnteredGarage);
            var connection = _multiplayer.Connection;
            if (connection != null && connection.IsReturningToLobby) connection.ReadyInLobby();
            _onlineError = _multiplayer.TakeNotice();
            if (_onlineError != null || (connection != null && connection.Room != null && !connection.IsLeaving)) SetOnlineRoomOpen(true);
            UiText.Prepare(_pixelCanvas, () =>
            {
                _armorPreviewLabelsDirty = true;
                RefreshSettingsLabels();
                _onlineModalUi?.RefreshLocalizedLabels();
                RefreshAllUI();
                if (_onlineOpen)
                {
                    _nextOnlineRefresh = 0f;
                    RefreshOnlineRoom();
                }
            });
        }

        private void LoadResourcesAndCatalog()
        {
            _font = Resources.Load<Font>("Fonts/Silkscreen-Regular")
                    ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _titleFont = Resources.Load<Font>("Fonts/PressStart2P-Regular") ?? _font;
            ForceFontPointFilter(_font);
            ForceFontPointFilter(_titleFont);
            Font.textureRebuilt += OnFontTextureRebuilt;

            _vehicleCatalog = VehicleCatalogIndex.Load();
            _maxViewHeightPixels = 1f;
            foreach (var v in _vehicleCatalog.Vehicles)
            {
                if (v == null || string.IsNullOrEmpty(v.id)) continue;
                _maxViewHeightPixels = Mathf.Max(_maxViewHeightPixels, v.EffectiveCameraHeightPixels);
            }
            if (_vehicleCatalog.Find(_sessionState.SelectedPlayerVehicleId) == null && _vehicleCatalog.Vehicles.Length > 0)
                _sessionState.SelectedPlayerVehicleId = _vehicleCatalog.Vehicles[0].id;

            _previewSfxClip = Resources.Load<AudioClip>("SFX/reload_clank");
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

        private void SelectBattleMap(BattleMap map)
        {
            _sessionState.SelectedBattleMap = map;
            RefreshAllUI();
        }

        private VehicleDef GetSelectedVehicle() => _vehicleCatalog.FindOrFirst(_sessionState.SelectedPlayerVehicleId);

        private void SelectVehicle(string id, bool closeTreeIfSame = false)
        {
            if (_vehicleCatalog.Find(id) == null) return;
            bool wasSame = _sessionState.SelectedPlayerVehicleId == id;
            _sessionState.SelectedPlayerVehicleId = id;
            ApplySelectedTankToTurntable();
            if (closeTreeIfSame && wasSame)
                SetTechTreeOpen(false);
        }

        private void CycleNationInGarage()
        {
            VehicleDef cur = GetSelectedVehicle();
            string curNation = cur != null ? cur.nation : "USA";
            int nIdx = 0;
            for (int i = 0; i < TreeNations.Length; i++)
                if (TreeNations[i].Code == curNation) { nIdx = i; break; }
            string nextNation = TreeNations[(nIdx + 1) % TreeNations.Length].Code;
            _treeNation = nextNation;

            // Pick a flagship modern or mid-tier tank of that nation (preferring highest tier non-special MBT)
            VehicleDef pick = null;
            foreach (var v in _vehicleCatalog.Vehicles)
            {
                if (v == null || v.nation != nextNation) continue;
                if (pick == null) pick = v;
                else if (v.role == "MBT" && !v.IsSpecial && (pick.role != "MBT" || v.tier > pick.tier)) pick = v;
            }
            if (pick != null) SelectVehicle(pick.id);
        }

        private void CycleSelectedInCurrentNation(int dir)
        {
            VehicleDef cur = GetSelectedVehicle();
            if (cur == null) return;
            var list = new List<VehicleDef>();
            foreach (var v in _vehicleCatalog.Vehicles)
                if (v != null && v.nation == cur.nation) list.Add(v);
            if (list.Count == 0) return;

            int idx = list.FindIndex(v => v.id == cur.id);
            if (idx < 0) idx = 0;
            idx = (idx + dir + list.Count) % list.Count;
            SelectVehicle(list[idx].id);
        }

        public void LaunchBattleScene()
        {
            if (_multiplayer.Connection != null) return;
            _sceneLoader.LoadAsync(BattleSessionState.BattleSceneName).Forget();
        }

        private void Update()
        {
            if (_sceneLoader != null && _sceneLoader.IsLoading) return;
            float dt = Mathf.Min(0.04f, Time.deltaTime);
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            bool fireArmorPreview = false;

            if (_windowbox != null)
                _windowbox.integerScale = _sessionState.IntegerWindowbox;

            if (_choosingOnlineVehicle && (_multiplayer.Connection == null || _multiplayer.Connection.IsLeaving))
                SetOnlineVehicleSelection(false);

            if (_onlineOpen)
            {
                _armorPreviewPress = null;
                HandleOnlineKeyboard(kb);
                RefreshOnlineRoom();
                if (_titleOpen) UpdateTitleStage(dt);
                return;
            }

            HandleMenuKeyboard(kb);

            // Live 3D Sprite-Stacking Title Screen Stage
            if (_titleOpen)
            {
                _draggingTank = false;
                _draggingTree = false;
                _armorPreviewPress = null;
                UpdateTitleStage(dt);
                return;
            }

            // --- HORIZONTAL SCROLLING FOR WoT TECH TREE ---
            if (_settingsOpen || _deployOpen)
            {
                _draggingTank = false;
                _draggingTree = false;
                _armorPreviewPress = null;
            }
            else if (_treeOpen)
            {
                UpdateTreeScroll(kb, mouse, dt);
                _draggingTank = false;
                _armorPreviewPress = null;
            }
            else if (mouse != null)
            {
                fireArmorPreview = ReadShowcasePointer(mouse);
            }

            UpdateShowcase(kb, mouse, fireArmorPreview, dt);
        }

        private void RefreshAllUI()
        {
            VehicleDef sel = GetSelectedVehicle();
            if (sel != null)
            {
                string nationName = sel.NationLabel;
                Color nationAccent = GetNationAccentColor(sel.nation);

                if (_garageNationStripe != null) _garageNationStripe.color = nationAccent;
                if (_garageSpecAccent != null) _garageSpecAccent.color = nationAccent;
                if (_garageTopBadge != null)
                    _garageTopBadge.text = $"{nationName}  •  TIER {sel.TierRoman}  •  {sel.role}";
                if (_garageYearBadge != null)
                    _garageYearBadge.text = sel.year > 0 ? sel.year.ToString() : sel.TierBadge;

                if (_garageTankName != null)
                {
                    _garageTankName.text = sel.name.ToUpper();
                    _garageTankName.color = sel.IsSpecial ? new Color(1f, 0.90f, 0.45f) : GoldColor;
                }
                if (_garageNameUnderline != null)
                    _garageNameUnderline.color = sel.IsSpecial ? GoldHi : nationAccent;

                if (_garageIndexBadge != null)
                {
                    var nationList = Array.FindAll(_vehicleCatalog.Vehicles, v => v != null && v.nation == sel.nation);
                    int idx = Array.FindIndex(nationList, v => v.id == sel.id);
                    _garageIndexBadge.text = UiText.Format("UNIT {0:00}/{1:00}", idx >= 0 ? idx + 1 : 1, nationList.Length);
                }

                UpdateStatBar(_statArm, sel.armorFront / 1.25f, $"{Mathf.RoundToInt(sel.armorFront * 100f)}");
                UpdateStatBar(_statPen, sel.penetration / 155f, $"{Mathf.RoundToInt(sel.penetration)}");
                float rldScore = 1f - Mathf.Clamp01((sel.reload - 0.16f) / 2.04f);
                UpdateStatBar(_statRld, rldScore, $"{sel.reload:F2}s");
                UpdateStatBar(_statSpd, sel.speed / 84f, $"{Mathf.RoundToInt(sel.speed)}");
                UpdateStatBar(_statTrn, sel.EffectiveHullTurnSpeed / 210f, $"{Mathf.RoundToInt(sel.EffectiveHullTurnSpeed)}");
                UpdateStatBar(_statTur, sel.EffectiveTurretTurnSpeed / 355f, $"{Mathf.RoundToInt(sel.EffectiveTurretTurnSpeed)}");
                Vector2 viewSize = BattleViewport.SizeInPixelsFor(sel, PixelWindowbox.AspectRatio);
                UpdateStatBar(_statView, viewSize.y / _maxViewHeightPixels,
                    $"{Mathf.RoundToInt(viewSize.x)}×{Mathf.RoundToInt(viewSize.y)}");

                // Hull & Drivetrain mini telemetry
                string gearStr = sel.IsWheeled ? UiText.Format("{0}x{0} WHEEL", sel.WheelAxleCount * 2) : UiText.Get("TRACKED");
                string engStr = UiText.Get(sel.IsFrontEngine ? "FRONT ENG" : "REAR ENG");
                string turStr = UiText.Get(sel.IsTurretless ? "CASEMATE" : (sel.IsUnmannedTurret ? "UNMANNED" : "MANNED"));
                if (_garageCrewLine != null) _garageCrewLine.text = UiText.Format("CREW {0}  •  {1}", sel.CrewCapacity, gearStr);
                if (_garageEngineLine != null)
                    _garageEngineLine.text = UiText.Format("ENG: {0}", UiText.Get(string.IsNullOrEmpty(sel.engine) ? "DIESEL" : sel.engine.ToUpperInvariant()));
                if (_garageLayoutLine != null) _garageLayoutLine.text = $"{engStr} • {turStr}";

                // Right Card: Armament & Operation
                if (_garageRoleLine != null) _garageRoleLine.text = UiText.Get(FormatRoleFull(sel.role));
                if (_garageWeaponLine != null) _garageWeaponLine.text = UiText.Format("WPN: {0}", sel.WeaponLabel);
                if (_garageAmmoLine != null) _garageAmmoLine.text = FormatAmmoSummary(sel);
                if (_garageSpecialLine != null)
                {
                    if (sel.HasSecondaryAtgm) _garageSpecialLine.text = UiText.Get("GUIDED ATGM [3]");
                    else if (sel.weapon == "ATGM") _garageSpecialLine.text = UiText.Get("SACLOS MISSILE");
                    else _garageSpecialLine.text = UiText.Get("SMOKE [Q] • COAX MG");
                }

                string mapTag = BattleSceneController.GetMapDisplayName(_sessionState.SelectedBattleMap);
                string modeShort = _sessionState.SelectedBattleMode switch
                {
                    BattleMode.FiringRange => "FIRING RANGE",
                    BattleMode.Battle5v5 => "5v5 COMPANY",
                    BattleMode.Squad2v2 => "2v2 SQUAD",
                    BattleMode.SurvivalWave => "SURVIVAL",
                    _ => "1v1 DUEL"
                };
                if (_garageModeLine != null) _garageModeLine.text = UiText.Format("MODE: {0}", UiText.Get(modeShort));
                if (_garageMapLine != null)
                {
                    _garageMapLine.text = UiText.Format("MAP: {0}", UiText.Get(mapTag));
                    _garageMapLine.color = _sessionState.SelectedBattleMap switch
                    {
                        BattleMap.Forest => MbtGreen,
                        BattleMap.City => LightCyan,
                        _ => GoldColor
                    };
                }
                if (_garageMatchLine != null)
                {
                    _garageMatchLine.text = _sessionState.SelectedBattleMode == BattleMode.FiringRange
                        ? UiText.Get("6 TARGETS • TEST")
                        : UiText.Format("TIER {0} BRACKET", sel.TierRoman);
                    _garageMatchLine.color = _sessionState.SelectedBattleMode == BattleMode.FiringRange ? LightCyan : TextMuted;
                }

                if (_garageNationBtnLabel != null)
                {
                    int nCount = Array.FindAll(_vehicleCatalog.Vehicles, v => v != null && v.nation == sel.nation).Length;
                    _garageNationBtnLabel.text = $"{sel.nation} ({nCount})";
                    _garageNationBtnLabel.color = nationAccent;
                }
            }

            RefreshDeployModalUI();
            RefreshTechTreeUI(sel);
        }

        private static void SetPixelRect(RectTransform rt, int x, int y, int w, int h)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void UpdateStatBar(StatBarSlot slot, float norm01, string formatted)
        {
            if (slot == null) return;
            if (slot.valueText != null) slot.valueText.text = formatted;
            if (slot.pips == null) return;
            int lit = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(norm01) * slot.pips.Length), 1, slot.pips.Length);
            for (int i = 0; i < slot.pips.Length; i++)
            {
                if (slot.pips[i] != null)
                    slot.pips[i].color = i < lit ? slot.fillColor : PipEmpty;
            }
        }

        private static string FormatRoleFull(string role) => role switch
        {
            "MBT" => "MAIN BATTLE TANK",
            "LIGHT" => "LIGHT RECON",
            "HEAVY" => "HEAVY BREAKTHRU",
            "TD" => "TANK DESTROYER",
            "SUPPORT" => "FIRE SUPPORT",
            "SPECIAL" => "SPECIAL PROTOTYPE",
            "MEDIUM" => "MEDIUM TANK",
            _ => string.IsNullOrEmpty(role) ? "ARMORED UNIT" : role.ToUpper()
        };

        private static string FormatAmmoSummary(VehicleDef def)
        {
            if (def == null) return "";
            return def.weapon switch
            {
                "AutocannonATGM" => "AP-T / HEI / ATGM",
                "TwinAutocannonATGM" => "2xAP-T / ATGM",
                "ATGM" => "TOW / GUIDED",
                _ => def.tier >= VehicleDef.ModernTier ? "APFS / HEAT / HE" : (def.tier >= 5 ? "APFS / APHE / HE" : "APHE / HEAT / HE")
            };
        }

        private void OnDestroy()
        {
            Font.textureRebuilt -= OnFontTextureRebuilt;
        }
    }
}
