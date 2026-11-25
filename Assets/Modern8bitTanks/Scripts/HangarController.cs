using Cysharp.Threading.Tasks;
using VContainer;
using R3;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
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
        private float _currentShowcaseZoom = 2.0f;

        // Horizontal WoT Tech Tree Layout Constants (all EVEN integer pixels!)
        private const int ViewX = 8;
        private const int ViewY = 70;
        private const int ViewW = 464;
        private const int ViewH = 260;
        private const int NodeW = 104;
        private const int NodeH = 30;
        private const int ColGap = 20;
        private const int ColStep = NodeW + ColGap; // 124px per Tier column!
        private const int PadX = 16;
        private const int HistoryColumns = 8;
        private const int ContentW = PadX * 2 + (HistoryColumns + VehicleDef.SpecialTier) * ColStep - ColGap;
        private const float MaxScrollX = ContentW - ViewW;

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

        private VehicleDef[] _allVehicles = Array.Empty<VehicleDef>();
        private readonly Dictionary<string, VehicleDef> _vehicleMap = new();
        private float _maxViewHeightPixels;
        private readonly Dictionary<string, Texture2D> _hullTexCache = new();
        private readonly Dictionary<string, Texture2D> _turretTexCache = new();

        private string _treeNation = "USA";
        private bool _treeOpen = false;
        private bool _settingsOpen = false;
        private bool _xrayMode = false;
        private bool _draggingTank;
        private float _lastDragX;
        private float _manualAngleOffset;
        [SerializeField] private GameObject _hangarXrayWrap;
        [SerializeField] private TankCutawayGraphic _hangarXrayGraphic;
        [SerializeField] private Image _btnXrayBg;
        [SerializeField] private Text _hangarXrayBadge;

        // Horizontal Tree Scroll state
        private float _treeScrollX = 0f;
        private float _targetScrollX = 0f;
        private bool _draggingTree;
        private float _treeDragStartScreenX;
        private float _treeDragStartScroll;
        private float _treeDragAccumDist;
        [SerializeField] private RectTransform _treeContentRt;
        [SerializeField] private RectTransform _scrollThumbRt;
        private const int ScrollTrackX = 32;
        private const int ScrollTrackW = 416;

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

        // Tech Tree UI
        private static readonly (string Code, string Label)[] TreeNations =
        {
            ("USA", "USA"), ("GER", "GERMANY"), ("USSR", "USSR / RU"), ("JPN", "JAPAN")
        };
        private readonly Dictionary<string, Image> _nationButtons = new();
        private readonly Dictionary<string, GameObject> _nationLinks = new();
        [SerializeField] private Text _treeDetailTitle;
        [SerializeField] private Text _treeDetailStats;

        private class TreeNodeSlot
        {
            public GameObject visualGo;
            public GameObject hitGo;
            public RectTransform hitRt;
            public Image borderImg;
            public Image fillImg;
            public Text topText;
            public Text nameText;
            public string vehicleId;
            public Color idleBorder;
            public int contentX;
            public int contentY; // local inside viewport [0..ViewH]
        }

        private readonly Dictionary<string, TreeNodeSlot> _treeNodes = new();

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

        private float GetAdaptiveShowcaseZoom(VehicleDef def)
        {
            if (def == null) return showcaseZoom;
            float len = def.hullLength > 0 ? def.hullLength : 38f;
            if (len <= 26f) return 2.50f; // Micro tankettes (Wiesel, MS-1, M50, ASU-57): crystal-clear close framing!
            if (len <= 34f) return 2.25f; // Light tanks (Stuart, Ha-Go, Pz II, Sheridan)
            if (len <= 44f) return 2.00f; // Standard MBTs: exact 2x pixel scale!
            return 1.80f;                 // Massive heavies (Tiger II, A7V, M103)
        }

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
                RefreshAllUI();
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

            VehicleCatalog cat = MetaLoader.LoadVehicleCatalog();
            _allVehicles = cat != null && cat.vehicles != null ? cat.vehicles : Array.Empty<VehicleDef>();
            _vehicleMap.Clear();
            _maxViewHeightPixels = 1f;
            foreach (var v in _allVehicles)
            {
                if (v == null || string.IsNullOrEmpty(v.id)) continue;
                _vehicleMap[v.id] = v;
                _maxViewHeightPixels = Mathf.Max(_maxViewHeightPixels, v.EffectiveCameraHeightPixels);
            }
            if (!_vehicleMap.ContainsKey(_sessionState.SelectedPlayerVehicleId) && _allVehicles.Length > 0)
                _sessionState.SelectedPlayerVehicleId = _allVehicles[0].id;

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

        private void SetupMapBackdrop()
        {
            mapBackdropRenderer.enabled = true;
            mapBackdropRenderer.transform.position = new Vector3(WorldW * 0.5f, WorldH * 0.5f, 5f);
            mapBackdropRenderer.sortingOrder = -30000;
            if (_groundHd2dMat != null) mapBackdropRenderer.sharedMaterial = _groundHd2dMat;
        }

        private void RefreshMapBackdropTheme(BattleMap map)
        {
            if (_titleOpen)
            {
                mapBackdropRenderer.sprite = Resources.Load<Sprite>($"Hangar/{map}Backdrop");
                mapBackdropRenderer.transform.localScale = Vector3.one;
            }
            else
            {
                mapBackdropRenderer.sprite = _garageBackdrop;
                FitGarageBackdrop();
            }

            _postProcess?.ApplyMapTheme(_titleOpen ? map : BattleMap.City);
        }

        private void FitGarageBackdrop()
        {
            float height = _cam.orthographicSize * 2f;
            Vector3 size = _garageBackdrop.bounds.size;
            float scale = Mathf.Max(height * _cam.aspect / size.x, height / size.y);
            mapBackdropRenderer.transform.localScale = new Vector3(scale, scale, 1f);
        }

        private void SelectBattleMap(BattleMap map)
        {
            _sessionState.SelectedBattleMap = map;
            RefreshAllUI();
        }

        private void SetupCenterShowcaseTank()
        {

            if (showcaseTank.hullStack != null && showcaseTank.hullStack.sliceMaterial != null)
                _stackFlashMat = showcaseTank.hullStack.sliceMaterial;

            Vector2 center = new Vector2(WorldW * 0.5f, WorldH * 0.5f + 0.15f);
            showcaseTank.transform.position = new Vector3(center.x, center.y, 0f);
            showcaseTank.isPlayer = false;
            showcaseTank.factionTeam = "ally";
        }

        private VehicleDef GetSelectedVehicle()
        {
            if (_vehicleMap.TryGetValue(_sessionState.SelectedPlayerVehicleId, out var def))
                return def;
            return _allVehicles.Length > 0 ? _allVehicles[0] : null;
        }

        private void ApplySelectedTankToTurntable()
        {
            VehicleDef def = GetSelectedVehicle();
            if (def == null || showcaseTank == null) return;

            Vector2 center = new Vector2(WorldW * 0.5f, WorldH * 0.5f + 0.15f);
            if (!showcaseTank.TrySpawnAsVehicle(def, LoadCachedHull(def.id), LoadCachedTurret(def.id),
                center, 35f + _manualAngleOffset, _stackFlashMat))
                return;

            showcaseTank.SetAssemblyDrop(1f);
            ClearArmorPreviewHistory();
            // A larger vehicle must fit as soon as it replaces the previous model.
            _currentShowcaseZoom = Mathf.Min(_currentShowcaseZoom, GetAdaptiveShowcaseZoom(def));
            RefreshAllUI();
        }

        private void SelectVehicle(string id, bool closeTreeIfSame = false)
        {
            if (!_vehicleMap.ContainsKey(id)) return;
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
            foreach (var v in _allVehicles)
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
            foreach (var v in _allVehicles)
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

        private void ScrollTreeToSelected(bool instant = false)
        {
            VehicleDef sel = GetSelectedVehicle();
            if (sel == null || sel.nation != _treeNation)
            {
                _targetScrollX = 0f;
            }
            else
            {
                int col = TreeColumn(sel);
                float nodeCenterX = PadX + col * ColStep + NodeW * 0.5f;
                _targetScrollX = Mathf.Clamp(nodeCenterX - ViewW * 0.5f, 0f, MaxScrollX);
            }
            if (instant)
            {
                _treeScrollX = _targetScrollX;
                ApplyTreeScrollVisuals();
            }
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
                if (kb != null && kb.escapeKey.wasPressedThisFrame) CloseOnlineRoom();
                RefreshOnlineRoom();
                if (_titleOpen) UpdateTitleStage(dt);
                return;
            }

            if (kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame)
                {
                    if (_settingsOpen) SetSettingsOpen(false);
                    else if (_deployOpen) SetDeployModalOpen(false);
                    else if (_treeOpen) SetTechTreeOpen(false);
                    else if (_choosingOnlineVehicle) ConfirmOnlineVehicle();
                    else SetSettingsOpen(true);
                }
                else if (kb.oKey.wasPressedThisFrame && !_treeOpen && !_deployOpen)
                {
                    SetSettingsOpen(!_settingsOpen);
                }
                else if (!_settingsOpen)
                {
                    if (_titleOpen)
                    {
                        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
                            BeginEnterGarageFromTitle();
                    }
                    else if (_deployOpen)
                    {
                        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                            LaunchBattleScene();
                    }
                    else
                    {
                        if (kb.tKey.wasPressedThisFrame)
                            SetTechTreeOpen(!_treeOpen);
                        else if (kb.xKey.wasPressedThisFrame && !_treeOpen)
                            SetXRayMode(!_xrayMode);
                        else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                            SetDeployModalOpen(true, fromTree: _treeOpen);
                        else if (!_treeOpen && kb.leftArrowKey.wasPressedThisFrame)
                            CycleSelectedInCurrentNation(-1);
                        else if (!_treeOpen && kb.rightArrowKey.wasPressedThisFrame)
                            CycleSelectedInCurrentNation(1);
                    }
                }
            }

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
                if (kb != null)
                {
                    float keyDir = 0f;
                    if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) keyDir -= 1f;
                    if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) keyDir += 1f;
                    if (Mathf.Abs(keyDir) > 0.01f)
                        _targetScrollX = Mathf.Clamp(_targetScrollX + keyDir * 360f * dt, 0f, MaxScrollX);
                }

                if (mouse != null)
                {
                    Vector2 wheel = mouse.scroll.ReadValue();
                    float wheelDelta = Mathf.Abs(wheel.x) > 0.01f ? wheel.x : -wheel.y;
                    if (Mathf.Abs(wheelDelta) > 0.01f)
                    {
                        float step = Mathf.Sign(wheelDelta) * 86f;
                        _targetScrollX = Mathf.Clamp(_targetScrollX + step, 0f, MaxScrollX);
                    }

                    // Click-drag horizontal panning inside tree viewport
                    Vector2 screenPos = mouse.position.ReadValue();
                    bool inTreeBand = _windowbox.TryScreenToViewport(screenPos, out Vector2 uv)
                                      && uv.y * PixelWindowbox.RenderHeight >= ViewY - 12
                                      && uv.y * PixelWindowbox.RenderHeight <= ViewY + ViewH;

                    if (mouse.leftButton.wasPressedThisFrame && inTreeBand)
                    {
                        _draggingTree = true;
                        _treeDragStartScreenX = screenPos.x;
                        _treeDragStartScroll = _targetScrollX;
                        _treeDragAccumDist = 0f;
                    }
                    if (mouse.leftButton.wasReleasedThisFrame)
                        _draggingTree = false;

                    if (_draggingTree)
                    {
                        float dxScreen = screenPos.x - _treeDragStartScreenX;
                        _treeDragAccumDist = Mathf.Max(_treeDragAccumDist, Mathf.Abs(dxScreen));
                        float scaleToVirtual = PixelWindowbox.RenderWidth / Mathf.Max(1f, _windowbox.HitOverlayRect.rect.width);
                        _targetScrollX = Mathf.Clamp(_treeDragStartScroll - dxScreen * scaleToVirtual, 0f, MaxScrollX);
                    }
                }

                _treeScrollX = Mathf.Lerp(_treeScrollX, _targetScrollX, 1f - Mathf.Exp(-14f * dt));
                if (Mathf.Abs(_treeScrollX - _targetScrollX) < 0.3f) _treeScrollX = _targetScrollX;
                ApplyTreeScrollVisuals();
                _draggingTank = false;
                _armorPreviewPress = null;
            }
            else if (mouse != null)
            {
                // Garage view: drag mouse to inspect your tank from any angle
                bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                if (_xrayMode)
                {
                    if (_hangarXrayGraphic.HangarShotBusy)
                    {
                        _armorPreviewPress = null;
                        _draggingTank = false;
                    }
                    else
                    {
                        fireArmorPreview = ReadArmorPreviewClick(mouse, overUI);
                    }
                }
                else if (!overUI && mouse.leftButton.wasPressedThisFrame)
                {
                    _lastDragX = mouse.position.ReadValue().x;
                    _draggingTank = float.IsFinite(_lastDragX);
                }
                if (mouse.leftButton.wasReleasedThisFrame)
                    _draggingTank = false;
                if (_draggingTank)
                {
                    float curX = mouse.position.ReadValue().x;
                    if (float.IsFinite(curX))
                    {
                        float dx = curX - _lastDragX;
                        _lastDragX = curX;
                        _manualAngleOffset = Mathf.Repeat(_manualAngleOffset + dx * 0.45f, 360f);
                    }
                }
            }

            // Adaptive crisp showcase zoom (automatically frames small tankettes close-up & MBTs at crisp 2x pixels!)
            float targetZoom = GetAdaptiveShowcaseZoom(GetSelectedVehicle());
            _currentShowcaseZoom = Mathf.Lerp(_currentShowcaseZoom, targetZoom, 1f - Mathf.Exp(-10f * dt));
            if (Mathf.Abs(_currentShowcaseZoom - targetZoom) < 0.006f) _currentShowcaseZoom = targetZoom;

            // Center camera squarely on the showcase turntable
            if (_cam != null)
            {
                float orthoSize = ((PixelWindowbox.RenderHeight / PPU) * 0.5f) / _currentShowcaseZoom;
                _cam.orthographicSize = orthoSize;
                FitGarageBackdrop();
                float camX = Mathf.Round((WorldW * 0.5f) * PPU) / PPU;
                float camY = Mathf.Round((WorldH * 0.5f) * PPU) / PPU;
                _cam.transform.position = new Vector3(camX, camY, -20f);
                _cam.transform.rotation = Quaternion.identity;
            }

            if (showcaseTank != null)
            {
                bool inspect = _xrayMode && !_treeOpen;
                if (inspect && fireArmorPreview)
                    ResetArmorPreviewDamage();

                if (!_draggingTank && !inspect)
                    _manualAngleOffset = Mathf.Repeat(_manualAngleOffset + 24f * dt, 360f);
                showcaseTank.hullAngleDeg = Mathf.Repeat(35f + _manualAngleOffset, 360f);
                if (!inspect || !_hangarXrayGraphic.HangarShotBusy)
                    showcaseTank.turretAngleDeg = showcaseTank.hullAngleDeg + Mathf.Sin(Time.time * 0.95f) *
                        Mathf.Min(28f, showcaseTank.vehicleDef?.fixedGunTraverse is > 0 ? showcaseTank.vehicleDef.fixedGunTraverse : 28f);

                if (showcaseTank.gameObject.activeSelf == inspect)
                    showcaseTank.gameObject.SetActive(!inspect);
                showcaseTank.SyncVisuals(squash, spacingPx);
                if (inspect)
                {
                    _hangarXrayGraphic.ShowHangarInspect(showcaseTank, _cam, _windowbox.HudCamera);
                    UpdateArmorPreview(kb, mouse, fireArmorPreview, dt);
                }
            }
        }

        private void ApplyTreeScrollVisuals()
        {
            int scrollInt = Mathf.RoundToInt(_treeScrollX);
            if (_treeContentRt != null)
                _treeContentRt.anchoredPosition = new Vector2(-scrollInt, 0f);

            // Sync pixel scrollbar thumb
            if (_scrollThumbRt != null)
            {
                int thumbW = Mathf.RoundToInt(ScrollTrackW * ((float)ViewW / ContentW));
                int travel = ScrollTrackW - thumbW;
                int thumbX = ScrollTrackX + Mathf.RoundToInt((scrollInt / Mathf.Max(1f, MaxScrollX)) * travel);
                _scrollThumbRt.anchoredPosition = new Vector2(thumbX, 58);
                _scrollThumbRt.sizeDelta = new Vector2(thumbW, 6);
            }

            // Dynamically sync every visible node's click hitbox clipped cleanly to the scroll viewport!
            foreach (var slot in _treeNodes.Values)
            {
                if (slot == null || slot.hitGo == null) continue;
                bool activeItem = _vehicleMap[slot.vehicleId].nation == _treeNation;
                if (!activeItem)
                {
                    slot.hitGo.SetActive(false);
                    continue;
                }

                float sx0 = ViewX + slot.contentX - scrollInt;
                float sx1 = sx0 + NodeW;
                float clipX0 = Mathf.Max(ViewX, sx0);
                float clipX1 = Mathf.Min(ViewX + ViewW, sx1);

                if (clipX1 - clipX0 < 4f)
                {
                    slot.hitGo.SetActive(false);
                }
                else
                {
                    slot.hitGo.SetActive(true);
                    float sy0 = ViewY + slot.contentY;
                    float sy1 = sy0 + NodeH;
                    slot.hitRt.anchorMin = new Vector2(clipX0 / PixelWindowbox.RenderWidth, sy0 / PixelWindowbox.RenderHeight);
                    slot.hitRt.anchorMax = new Vector2(clipX1 / PixelWindowbox.RenderWidth, sy1 / PixelWindowbox.RenderHeight);
                    slot.hitRt.offsetMin = Vector2.zero;
                    slot.hitRt.offsetMax = Vector2.zero;
                }
            }
        }

        // ============================================================
        // 100% Pixel-Perfect UI (Strictly 8px / 16px Fonts & EVEN Rects!)
        // ============================================================
        private void SetXRayMode(bool on)
        {
            if (!on) ResetArmorPreviewDamage();
            _xrayMode = on;
            if (_hangarXrayWrap != null) _hangarXrayWrap.SetActive(on && !_treeOpen);
            if (_btnXrayBg != null) _btnXrayBg.color = on ? new Color(0.22f, 0.54f, 0.62f, 1f) : BtnBg;
            if (showcaseTank != null) showcaseTank.gameObject.SetActive(!on || _treeOpen);
            RefreshAllUI();
        }

        private void PlayHangarPreviewSfx()
        {
            if (_previewAudio == null || _previewSfxClip == null) return;
            if (!_sessionState.SfxEnabled || _sessionState.SfxVolume <= 0.001f) return;
            _previewAudio.Stop();
            _previewAudio.clip = _previewSfxClip;
            _previewAudio.volume = Mathf.Clamp01(0.62f * _sessionState.SfxVolume);
            _previewAudio.pitch = 1.0f;
            _previewAudio.Play();
        }

        private void RefreshSettingsLabels()
        {
            if (_setSfxLabel != null)
            {
                _setSfxLabel.text = BattleSessionState.FormatVolumeLabel("SE", _sessionState.SfxEnabled, _sessionState.SfxVolume);
                _setSfxLabel.color = (_sessionState.SfxEnabled && _sessionState.SfxVolume > 0.001f) ? MbtGreen : TextMuted;
            }
            if (_setBgmLabel != null)
            {
                _setBgmLabel.text = BattleSessionState.FormatVolumeLabel("BGM", _sessionState.BgmEnabled, _sessionState.BgmVolume);
                _setBgmLabel.color = (_sessionState.BgmEnabled && _sessionState.BgmVolume > 0.001f) ? LightCyan : TextMuted;
            }
            if (_setDriveLabel != null)
                _setDriveLabel.text = UiText.Get(_sessionState.ArcadeWASD ? "DRIVE: ARCADE WASD" : "DRIVE: TANK STEER");
            if (_setAiLabel != null)
                _setAiLabel.text = UiText.Get(_sessionState.AiEnabled ? "ENEMY AI: ON" : "ENEMY AI: OFF");
            if (_setGfxLabel != null)
                _setGfxLabel.text = UiText.Get(_sessionState.PostProcessEnabled ? "GFX: HD-2D" : "GFX: RETRO");
            if (_setViewLabel != null)
                _setViewLabel.text = UiText.Get(_sessionState.IntegerWindowbox ? "VIEW: INT WINDOWBOX" : "VIEW: 4:3");
        }

        private void SetSettingsOpen(bool open)
        {
            _settingsOpen = open;
            if (_settingsVisualGroup != null) _settingsVisualGroup.SetActive(open);
            SyncActiveHitGroups();
            if (open) RefreshSettingsLabels();
        }

        private void SetTechTreeOpen(bool open)
        {
            if (open && _settingsOpen) SetSettingsOpen(false);
            if (open && _deployOpen) SetDeployModalOpen(false);
            if (open) ResetArmorPreviewDamage();
            _treeOpen = open;
            if (_treeVisualGroup != null) _treeVisualGroup.SetActive(open && !_titleOpen);
            if (_garageVisualGroup != null) _garageVisualGroup.SetActive(!open && !_titleOpen);
            if (_hangarXrayWrap != null) _hangarXrayWrap.SetActive(_xrayMode && !open && !_titleOpen);
            if (showcaseTank != null) showcaseTank.gameObject.SetActive(!_xrayMode || open || _titleOpen);
            SyncActiveHitGroups();
            if (open)
            {
                VehicleDef cur = GetSelectedVehicle();
                if (cur != null) _treeNation = cur.nation;
                RefreshAllUI();
                ScrollTreeToSelected(instant: true);
            }
            else
            {
                RefreshAllUI();
            }
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
                    var nationList = Array.FindAll(_allVehicles, v => v != null && v.nation == sel.nation);
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
                    int nCount = Array.FindAll(_allVehicles, v => v != null && v.nation == sel.nation).Length;
                    _garageNationBtnLabel.text = $"{sel.nation} ({nCount})";
                    _garageNationBtnLabel.color = nationAccent;
                }

                if (_treeDetailTitle != null)
                    _treeDetailTitle.text = $"{sel.TierRoman}  {sel.name.ToUpper()}  ({sel.role} / {sel.WeaponLabel})";
                if (_treeDetailStats != null)
                    _treeDetailStats.text = $"SPD {(int)sel.speed}  TRN {(int)sel.EffectiveHullTurnSpeed}  TUR {(int)sel.EffectiveTurretTurnSpeed}  PEN {(int)sel.penetration}  RLD {sel.reload:F2}s  {sel.engine.ToUpper()}";
            }

            RefreshDeployModalUI();

            foreach (var (nation, button) in _nationButtons)
                button.color = _treeNation == nation ? BtnSelected : BtnBg;
            foreach (var (nation, links) in _nationLinks)
                links.SetActive(_treeNation == nation);

            foreach (var (id, slot) in _treeNodes)
            {
                VehicleDef vehicle = _vehicleMap[id];
                bool visible = vehicle.nation == _treeNation;
                slot.visualGo.SetActive(visible);
                if (!visible) continue;

                bool selected = vehicle.id == _sessionState.SelectedPlayerVehicleId;
                string tag = vehicle.year > 0 ? vehicle.year.ToString() : vehicle.TierRoman;
                slot.topText.text = $"{tag}  {vehicle.role}";
                slot.topText.color = selected || vehicle.IsSpecial ? GoldColor : TextMuted;
                slot.nameText.text = FormatTreeShortName(vehicle.name, 15);
                slot.nameText.color = selected ? GoldColor : TextLight;
                slot.fillImg.color = selected ? BtnSelected : BtnBg;
                slot.borderImg.color = selected ? GoldColor : slot.idleBorder;
            }
            if (_treeOpen) ApplyTreeScrollVisuals();
        }

        private string GetNationTabLabel(string nation, string title)
        {
            int count = Array.FindAll(_allVehicles, vehicle => vehicle != null && vehicle.nation == nation).Length;
            return $"{title} ({count})";
        }

        private static string FormatTreeShortName(string raw, int maxChars)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string s = raw
                .Replace("Kanonenjagdpanzer", "KanJPz")
                .Replace("M41 Walker Bulldog", "M41 Bulldog")
                .Replace("M41 (Bundeswehr)", "M41 Bundeswehr")
                .Replace("Ru 251 Prototype", "Ru 251")
                .Replace("Spahpanzer ", "")
                .Replace("Raketenjagdpanzer ", "RAKJPZ ")
                .Replace("M1128 Stryker MGS", "STRYKER MGS")
                .Replace("BMPT Terminator", "BMPT TERM.")
                .Replace("2S25M Sprut-SDM1", "SPRUT-SDM1")
                .Replace("2S25 Sprut-SD", "SPRUT-SD")
                .ToUpper();
            return s.Length <= maxChars ? s : s.Substring(0, maxChars);
        }

        // ============================================================
        // Exact Integer-Pixel uGUI Builders (Strictly 8px / 16px!)
        // ============================================================
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
