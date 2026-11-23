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
    /// - Shows ONLY the player's selected tank DEAD-CENTER on a live 360° sunlit desert turntable
    /// - True Horizontal-Scrolling WoT Tech Tree (Tier I -> V trunk forking into VI -> X MBT+SP / LIGHT / SUPPORT)
    ///   with Mouse Wheel, Click-Drag, Arrow Keys, & Pixel Scrollbar
    /// - Supports 1v1, 2v2, 5v5, & Survival Wave modes -> launches cleanly into DesertBattle.unity!
    /// - Strictly native 8px / 16px HintedRaster pixel fonts on EVEN pixel rects (zero blurry text!)
    /// </summary>
    public partial class HangarController : MonoBehaviour
    {
        public const float PPU = 16f;
        private float WorldW => DesertBattleManager.WorldWidthPx / PPU;
        private float WorldH => DesertBattleManager.WorldHeightPx / PPU;

        [Header("Scene References")]
        public SpriteRenderer desertGroundRenderer;
        public TankEntity showcaseTank;

        [Header("Display Settings")]
        public int renderWidth = 480;
        public int renderHeight = 270;
        [Range(0.45f, 1.0f)] public float squash = 0.80f;
        [Range(0f, 2.5f)] public float spacingPx = 0.72f;
        [Range(1.0f, 3.0f)] public float showcaseZoom = 2.0f;
        private float _currentShowcaseZoom = 2.0f;

        // Horizontal WoT Tech Tree Layout Constants (all EVEN integer pixels!)
        private const int ViewX = 8;
        private const int ViewY = 70;
        private const int ViewW = 464;
        private const int ViewH = 170;
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

        [Inject]
        private void Construct(Camera worldCamera, BattleSessionState sessionState, GameSceneLoader sceneLoader,
            PixelWindowbox windowbox, BattlePostProcess postProcess, DesertAtmosphere atmosphere)
        {
            _cam = worldCamera;
            _sessionState = sessionState;
            _sceneLoader = sceneLoader;
            _windowbox = windowbox;
            _postProcess = postProcess;
            _atmosphere = atmosphere;
        }
        private PixelWindowbox _windowbox;
        private BattlePostProcess _postProcess;
        private DesertAtmosphere _atmosphere;

        private Font _font;
        private Font _titleFont;
        private Sprite _whitePxSprite;
        private Material _stackFlashMat;
        private Material _emissiveFxMat;
        private Material _groundHd2dMat;

        private VehicleDef[] _allVehicles = Array.Empty<VehicleDef>();
        private readonly Dictionary<string, VehicleDef> _vehicleMap = new();
        private readonly Dictionary<string, Texture2D> _hullTexCache = new();
        private readonly Dictionary<string, Texture2D> _turretTexCache = new();

        private string _treeNation = "USA";
        private bool _treeOpen = false;
        private bool _settingsOpen = false;
        private bool _xrayMode = false;
        private bool _draggingTank;
        private float _lastDragX;
        private float _manualAngleOffset;
        private GameObject _hangarXrayWrap;
        private TankCutawayGraphic _hangarXrayGraphic;
        private Image _btnXrayBg;
        private Text _hangarXrayBadge;

        // Horizontal Tree Scroll state
        private float _treeScrollX = 0f;
        private float _targetScrollX = 0f;
        private bool _draggingTree;
        private float _treeDragStartScreenX;
        private float _treeDragStartScroll;
        private float _treeDragAccumDist;
        private RectTransform _treeContentRt;
        private RectTransform _scrollThumbRt;
        private const int ScrollTrackX = 32;
        private const int ScrollTrackW = 416;

        // UI Roots
        private Canvas _pixelCanvas;
        private RectTransform _hudRoot;
        private GameObject _garageVisualGroup;
        private GameObject _garageHitsGroup;
        private GameObject _treeVisualGroup;
        private GameObject _treeHitsGroup;
        private GameObject _settingsVisualGroup;
        private GameObject _settingsHitsGroup;

        // Settings UI Labels & Preview Audio
        private Text _setSfxLabel;
        private Text _setBgmLabel;
        private Text _setDriveLabel;
        private Text _setAiLabel;
        private Text _setGfxLabel;
        private Text _setViewLabel;
        [SerializeField] private AudioSource _previewAudio;
        private AudioClip _previewSfxClip;

        // Garage Tactical Command UI References
        private class StatBarSlot
        {
            public Image[] pips;
            public Text valueText;
            public Color fillColor;
        }

        private Text _garageTopBadge;
        private Text _garageIndexBadge;
        private Image _garageNationStripe;
        private Image _garageSpecAccent;
        private Text _garageYearBadge;
        private Text _garageTankName;
        private Image _garageNameUnderline;
        private Text _garageCrewLine;
        private Text _garageEngineLine;
        private Text _garageLayoutLine;
        private Text _garageRoleLine;
        private Text _garageWeaponLine;
        private Text _garageAmmoLine;
        private Text _garageSpecialLine;
        private Text _garageModeLine;
        private Text _garageMapLine;
        private Text _garageMatchLine;
        private Text _garageNationBtnLabel;
        private StatBarSlot _statArm;
        private StatBarSlot _statPen;
        private StatBarSlot _statRld;
        private StatBarSlot _statSpd;
        private StatBarSlot _statTrn;
        private StatBarSlot _statTur;

        // Tech Tree UI
        private static readonly (string Code, string Label)[] TreeNations =
        {
            ("USA", "USA"), ("GER", "GERMANY"), ("USSR", "USSR / RU"), ("JPN", "JAPAN")
        };
        private readonly Dictionary<string, Image> _nationButtons = new();
        private readonly Dictionary<string, GameObject> _nationLinks = new();
        private Text _treeDetailTitle;
        private Text _treeDetailStats;

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
        private static readonly Color PanelGlass = new Color(0.92f, 0.94f, 0.88f, 0.90f);
        private static readonly Color PanelGlassDeep = new Color(0.86f, 0.89f, 0.82f, 0.96f);
        private static readonly Color PanelGlassModal = new Color(0.95f, 0.97f, 0.92f, 0.98f);
        private static readonly Color TreeBackdrop = new Color(0.05f, 0.06f, 0.05f, 0.96f);
        private static readonly Color BrassDim = new Color(0.48f, 0.41f, 0.25f, 1f);
        private static readonly Color PanelBorder = new Color(0.42f, 0.38f, 0.26f, 1f);
        private static readonly Color Hairline = new Color(0.14f, 0.16f, 0.13f, 0.92f);
        private static readonly Color GridLine = new Color(0.24f, 0.23f, 0.18f, 0.55f);
        private static readonly Color PipEmpty = new Color(0.09f, 0.11f, 0.09f, 0.96f);
        private static readonly Color GoldColor = new Color(0.96f, 0.78f, 0.34f, 1f);
        private static readonly Color GoldHi = new Color(1.00f, 0.93f, 0.66f, 1f);
        private static readonly Color GoldShadow = new Color(0.56f, 0.40f, 0.11f, 1f);
        private static readonly Color SpecialBorder = new Color(0.96f, 0.76f, 0.28f, 1f);
        private static readonly Color BtnBg = new Color(0.26f, 0.29f, 0.23f, 0.98f);
        private static readonly Color BtnHover = new Color(0.40f, 0.42f, 0.32f, 1f);
        private static readonly Color BtnSelected = new Color(0.54f, 0.43f, 0.19f, 1f);
        private static readonly Color TextLight = new Color(0.96f, 0.95f, 0.89f, 1f);
        private static readonly Color TextMuted = new Color(0.68f, 0.70f, 0.60f, 1f);
        private static readonly Color LightCyan = new Color(0.52f, 0.88f, 0.94f, 1f);
        private static readonly Color SupportAmber = new Color(0.96f, 0.64f, 0.30f, 1f);
        private static readonly Color MbtGreen = new Color(0.58f, 0.88f, 0.44f, 1f);
        private static readonly Color DarkInk = new Color(0.08f, 0.07f, 0.04f, 1f);

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

            _windowbox.Initialize(_cam, renderWidth, renderHeight, _sessionState.IntegerWindowbox, useHd2d: _sessionState.PostProcessEnabled);
            _windowbox.SetTiltShiftParams(1.15f, 0.52f, 0.24f);

            _postProcess.postProcessEnabled = _sessionState.PostProcessEnabled;
            // Attach URP Post-Processing to PresentCamera so Bloom/Vignette/ACES run AFTER the 480x270 RT at full screen res!
            _postProcess.Initialize(_windowbox.PresentCamera);

            LoadResourcesAndCatalog();
            SetupDesertBackdrop();
            SetupCenterShowcaseTank();

            _atmosphere.Initialize(WorldW, WorldH, _whitePxSprite, _emissiveFxMat);
            _sessionState.SelectedBattleMapChanged.Subscribe(RefreshMapBackdropTheme).AddTo(this);

            BuildPixelCanvas();
            BuildTitleAndDeployGroups();
            BuildGarageUI();
            BuildTechTreeUI();
            BuildSettingsUI();
            BuildTitleUI();
            BuildDeployModalUI();
            BuildOnlineRoomUI();
            BuildTitleStage();

            VehicleDef cur = GetSelectedVehicle();
            if (cur != null) _treeNation = cur.nation;
            _currentShowcaseZoom = GetAdaptiveShowcaseZoom(cur);
            ApplySelectedTankToTurntable();
            SetSettingsOpen(false);
            SetDeployModalOpen(false);
            SetTechTreeOpen(false);
            // Show the Sprite-Stacking Title Screen on initial launch; return straight to Garage after battles!
            SetTitleOpen(!_sessionState.HasEnteredGarage);
            var connection = MultiplayerSessionConnection.Active;
            if (connection != null && connection.IsReturningToLobby) connection.ReadyInLobby();
            _onlineError = MultiplayerSessionConnection.TakeNotice();
            if (_onlineError != null || (connection != null && connection.Room != null && !connection.IsLeaving)) SetOnlineRoomOpen(true);
        }

        private void LoadResourcesAndCatalog()
        {
            _font = Resources.Load<Font>("Fonts/Silkscreen-Regular")
                    ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _titleFont = Resources.Load<Font>("Fonts/PressStart2P-Regular") ?? _font;
            ForceFontPointFilter(_font);
            ForceFontPointFilter(_titleFont);
            Font.textureRebuilt += OnFontTextureRebuilt;

            var whiteTex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            whiteTex.SetPixel(0, 0, Color.white);
            whiteTex.Apply();
            _whitePxSprite = Sprite.Create(whiteTex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

            Shader fxSh = Resources.Load<Shader>("Shaders/URPEmissiveEffect")
                          ?? Shader.Find("Modern8bitTanks/URPEmissiveEffect")
                          ?? Shader.Find("Sprites/Default");
            _emissiveFxMat = new Material(fxSh);

            Shader stackSh = Resources.Load<Shader>("Shaders/URPSpriteStackFlash")
                             ?? Shader.Find("Modern8bitTanks/URPSpriteStackFlash")
                             ?? Shader.Find("Sprites/Default");
            _stackFlashMat = new Material(stackSh);

            Shader groundSh = Resources.Load<Shader>("Shaders/URPHd2dDesertGround")
                              ?? Shader.Find("Modern8bitTanks/URPHd2dDesertGround");
            if (groundSh != null) _groundHd2dMat = new Material(groundSh);

            VehicleCatalog cat = MetaLoader.LoadVehicleCatalog();
            _allVehicles = cat != null && cat.vehicles != null ? cat.vehicles : Array.Empty<VehicleDef>();
            _vehicleMap.Clear();
            foreach (var v in _allVehicles)
            {
                if (v != null && !string.IsNullOrEmpty(v.id))
                    _vehicleMap[v.id] = v;
            }
            if (!_vehicleMap.ContainsKey(_sessionState.SelectedPlayerVehicleId) && _allVehicles.Length > 0)
                _sessionState.SelectedPlayerVehicleId = _allVehicles[0].id;

            _previewSfxClip = Resources.Load<AudioClip>("SFX/reload_clank") ?? TankAudioSynthesizer.CreateReloadClank();
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

        private void SetupDesertBackdrop()
        {
            desertGroundRenderer.enabled = true;
            desertGroundRenderer.transform.position = new Vector3(WorldW * 0.5f, WorldH * 0.5f, 5f);
            desertGroundRenderer.sortingOrder = -30000;
            if (_groundHd2dMat != null) desertGroundRenderer.sharedMaterial = _groundHd2dMat;
        }

        private void RefreshMapBackdropTheme(BattleMap map)
        {
            desertGroundRenderer.sprite = Resources.Load<Sprite>($"Hangar/{map}Backdrop");
            _postProcess?.ApplyMapTheme(map);
            _atmosphere?.ApplyMapTheme(map);
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

            if (!_hullTexCache.TryGetValue(def.id, out Texture2D hullTex))
            {
                hullTex = Resources.Load<Texture2D>($"Vehicles/{def.id}_hull_slices");
                _hullTexCache[def.id] = hullTex;
            }
            if (!_turretTexCache.TryGetValue(def.id, out Texture2D turretTex))
            {
                turretTex = Resources.Load<Texture2D>($"Vehicles/{def.id}_turret_slices");
                _turretTexCache[def.id] = turretTex;
            }

            Vector2 center = new Vector2(WorldW * 0.5f, WorldH * 0.5f + 0.15f);
            showcaseTank.ApplyVehicleDef(def, hullTex, turretTex, _stackFlashMat);
            showcaseTank.CaptureSpawnAndReset(center, 35f + _manualAngleOffset);
            showcaseTank.SetAssemblyDrop(1f);
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
            if (MultiplayerSessionConnection.Active != null) return;
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
            float dt = Mathf.Min(0.04f, Time.deltaTime);
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            if (_windowbox != null)
                _windowbox.integerScale = _sessionState.IntegerWindowbox;

            if (_choosingOnlineVehicle && (MultiplayerSessionConnection.Active == null || MultiplayerSessionConnection.Active.IsLeaving))
                SetOnlineVehicleSelection(false);

            if (_onlineOpen)
            {
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
                UpdateTitleStage(dt);
                return;
            }

            // --- HORIZONTAL SCROLLING FOR WoT TECH TREE ---
            if (_settingsOpen || _deployOpen)
            {
                _draggingTank = false;
                _draggingTree = false;
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
                                      && uv.y * renderHeight >= ViewY - 12
                                      && uv.y * renderHeight <= ViewY + ViewH;

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
                        float scaleToVirtual = renderWidth / Mathf.Max(1f, _windowbox.HitOverlayRect.rect.width);
                        _targetScrollX = Mathf.Clamp(_treeDragStartScroll - dxScreen * scaleToVirtual, 0f, MaxScrollX);
                    }
                }

                _treeScrollX = Mathf.Lerp(_treeScrollX, _targetScrollX, 1f - Mathf.Exp(-14f * dt));
                if (Mathf.Abs(_treeScrollX - _targetScrollX) < 0.3f) _treeScrollX = _targetScrollX;
                ApplyTreeScrollVisuals();
                _draggingTank = false;
            }
            else if (mouse != null)
            {
                // Garage view: drag mouse to inspect your tank from any angle
                bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                if (!overUI && mouse.leftButton.wasPressedThisFrame)
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

            // Smoothly spin the player's centered showcase tank (or live 360 X-Ray hologram)!
            if (showcaseTank != null)
            {
                if (!_draggingTank)
                    _manualAngleOffset = Mathf.Repeat(_manualAngleOffset + 24f * dt, 360f);
                showcaseTank.hullAngleDeg = Mathf.Repeat(35f + _manualAngleOffset, 360f);
                showcaseTank.turretAngleDeg = showcaseTank.hullAngleDeg + Mathf.Sin(Time.time * 0.95f) *
                    Mathf.Min(28f, showcaseTank.vehicleDef?.fixedGunTraverse is > 0 ? showcaseTank.vehicleDef.fixedGunTraverse : 28f);
                if (_xrayMode && !_treeOpen)
                {
                    if (showcaseTank.gameObject.activeSelf) showcaseTank.gameObject.SetActive(false);
                    if (_hangarXrayGraphic != null)
                        _hangarXrayGraphic.ShowHangarInspect(showcaseTank, showcaseTank.hullAngleDeg, showcaseTank.turretAngleDeg, _currentShowcaseZoom);
                }
                else
                {
                    if (!showcaseTank.gameObject.activeSelf) showcaseTank.gameObject.SetActive(true);
                    showcaseTank.SyncVisuals(squash, spacingPx);
                }
            }

            // Adaptive crisp showcase zoom (automatically frames small tankettes close-up & MBTs at crisp 2x pixels!)
            float targetZoom = GetAdaptiveShowcaseZoom(GetSelectedVehicle());
            _currentShowcaseZoom = Mathf.Lerp(_currentShowcaseZoom, targetZoom, 1f - Mathf.Exp(-10f * dt));
            if (Mathf.Abs(_currentShowcaseZoom - targetZoom) < 0.006f) _currentShowcaseZoom = targetZoom;

            // Center camera squarely on the showcase turntable
            if (_cam != null)
            {
                float orthoSize = ((renderHeight / PPU) * 0.5f) / _currentShowcaseZoom;
                _cam.orthographicSize = orthoSize;
                float camX = Mathf.Round((WorldW * 0.5f) * PPU) / PPU;
                float camY = Mathf.Round((WorldH * 0.5f) * PPU) / PPU;
                _cam.transform.position = new Vector3(camX, camY, -20f);
                _cam.transform.rotation = Quaternion.identity;
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
                    slot.hitRt.anchorMin = new Vector2(clipX0 / renderWidth, sy0 / renderHeight);
                    slot.hitRt.anchorMax = new Vector2(clipX1 / renderWidth, sy1 / renderHeight);
                    slot.hitRt.offsetMin = Vector2.zero;
                    slot.hitRt.offsetMax = Vector2.zero;
                }
            }
        }

        // ============================================================
        // 100% Pixel-Perfect UI (Strictly 8px / 16px Fonts & EVEN Rects!)
        // ============================================================
        private void BuildPixelCanvas()
        {
            var canvasGo = new GameObject("HangarPixelCanvas") { layer = 5 };
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

            _garageVisualGroup = new GameObject("GarageVisuals") { layer = 5 };
            _garageVisualGroup.transform.SetParent(_hudRoot, false);
            Stretch(_garageVisualGroup.AddComponent<RectTransform>());

            _treeVisualGroup = new GameObject("TechTreeVisuals") { layer = 5 };
            _treeVisualGroup.transform.SetParent(_hudRoot, false);
            Stretch(_treeVisualGroup.AddComponent<RectTransform>());

            _settingsVisualGroup = new GameObject("SettingsVisuals") { layer = 5 };
            _settingsVisualGroup.transform.SetParent(_hudRoot, false);
            Stretch(_settingsVisualGroup.AddComponent<RectTransform>());

            _garageHitsGroup = CreateHitGroup("GarageHits");
            _treeHitsGroup = CreateHitGroup("TreeHits");
            _settingsHitsGroup = CreateHitGroup("SettingsHits");
        }

        private GameObject CreateHitGroup(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_windowbox.HitOverlayRect, false);
            Stretch(go.AddComponent<RectTransform>());
            return go;
        }

        private void BuildGarageUI()
        {
            Transform vp = _garageVisualGroup.transform;
            Transform hp = _garageHitsGroup.transform;
            int W = renderWidth;
            int H = renderHeight;

            // =================================================================
            // 1. TOP ARMORED COMMAND RAIL (y = 246..270, h = 24)
            // =================================================================
            MilitaryUiSprites.CreateTiledSprite(vp, "TopSteelRail", MilitaryUiSprites.ArmorHeaderBar, 0, H - 24, W, 24, PanelGlassDeep);
            CreatePixelSolid(vp, "TopRailHighlight", 0, H - 1, W, 1, new Color(0.44f, 0.48f, 0.38f, 0.85f));
            CreatePixelSolid(vp, "TopRule", 0, H - 25, W, 1, new Color(0.06f, 0.07f, 0.05f, 1f));
            CreatePixelSolid(vp, "TopRuleBevel", 0, H - 24, W, 1, new Color(0.36f, 0.40f, 0.30f, 0.90f));

            _garageTitleButton = CreatePixelButton(vp, hp, "TITLE", 6, H - 21, 46, 18, BtnBg, PanelBorder, TextMuted, 8, () =>
            {
                if (_choosingOnlineVehicle) ConfirmOnlineVehicle();
                else SetTitleOpen(true);
            });
            CreatePixelButton(vp, hp, "SETTINGS", 56, H - 21, 66, 18, BtnBg, LightCyan, TextLight, 8, () => SetSettingsOpen(true));

            // Dead-Centered Riveted Nation + Tier Armored Plaque (x = 132..348, centered at 240!)
            var pill = MilitaryUiSprites.CreateSlicedSprite(vp, "NationPlaque", MilitaryUiSprites.ArmorPanel, 132, H - 22, 216, 20, new Color(0.92f, 0.95f, 0.88f, 0.96f), out _);
            _garageNationStripe = CreatePixelSolid(pill, "NationStripe", 4, 3, 3, 14, GoldColor).GetComponent<Image>();
            MilitaryUiSprites.CreateIcon(pill, "PlaqueStar", MilitaryUiSprites.IconStar, 10, 5, GoldColor);
            _garageTopBadge = CreatePixelText(pill, "USA • TIER X • MBT", 8, GoldColor, TextAnchor.MiddleCenter, 18, 1, 190, 18);

            // Military stencil fleet serial readout on the top-right (e.g. UNIT 04/31)
            _garageIndexBadge = CreatePixelText(vp, "UNIT 01/31", 8, TextMuted, TextAnchor.MiddleRight, 376, H - 21, 96, 18);

            // =================================================================
            // 2. LEFT RIVETED ARMOR TELEMETRY PLATE (x = 6..138, y = 50..238)
            // =================================================================
            var leftCard = CreateTacticalCard(vp, "SpecCard", 6, 50, 132, 188, PanelGlass, BrassDim, GoldColor, out _garageSpecAccent, withHazardTape: true);
            MilitaryUiSprites.CreateIcon(leftCard, "ArmorIcon", MilitaryUiSprites.IconArmor, 7, 165, GoldColor);
            CreatePixelText(leftCard, "SPECS", 8, GoldColor, TextAnchor.MiddleLeft, 19, 162, 68, 14);
            _garageYearBadge = CreatePixelText(leftCard, "1999", 8, TextMuted, TextAnchor.MiddleRight, 86, 162, 38, 14);
            CreateWeldSeam(leftCard, 6, 160, 120);

            // 6 Crisp 10-Pip Recessed Tank-Dashboard LED Stat Gauges!
            _statArm = CreateStatBarRow(leftCard, "ARM", 140, LightCyan);
            _statPen = CreateStatBarRow(leftCard, "PEN", 122, GoldColor);
            _statRld = CreateStatBarRow(leftCard, "RLD", 104, SupportAmber);
            _statSpd = CreateStatBarRow(leftCard, "SPD", 86, MbtGreen);
            _statTrn = CreateStatBarRow(leftCard, "TRN", 68, new Color(0.76f, 0.90f, 0.44f, 1f));
            _statTur = CreateStatBarRow(leftCard, "TUR", 50, new Color(0.56f, 0.80f, 0.96f, 1f));

            CreateWeldSeam(leftCard, 6, 45, 120);
            _garageCrewLine = CreatePixelText(leftCard, "CREW 4 • TRACKED", 8, TextLight, TextAnchor.MiddleLeft, 8, 30, 116, 12);
            _garageEngineLine = CreatePixelText(leftCard, "ENG: TURBINE", 8, TextMuted, TextAnchor.MiddleLeft, 8, 18, 116, 12);
            _garageLayoutLine = CreatePixelText(leftCard, "REAR ENG • MANNED", 8, LightCyan, TextAnchor.MiddleLeft, 8, 6, 116, 12);

            // =================================================================
            // 3. CENTER ARMORED DEPOT BAY (x = 138..342, dead-centered at x = 240!)
            // =================================================================
            // Riveted Steel Vehicle Designation Plaque at top of bay
            var namePlate = MilitaryUiSprites.CreateSlicedSprite(vp, "NamePlate", MilitaryUiSprites.ArmorPanel, 146, 214, 188, 24, new Color(0.92f, 0.94f, 0.88f, 0.92f), out _);
            _garageNameUnderline = CreatePixelSolid(namePlate, "NameUnderline", 12, 2, 164, 2, GoldColor).GetComponent<Image>();
            _garageTankName = CreatePixelText(namePlate, "M1A2 SEP v2", 16, GoldColor, TextAnchor.MiddleCenter, 4, 3, 180, 18, useTitleFont: false);

            // Tank Gunner-Optics Rangefinder Reticle framing the rotating tank!
            Color opticsCol = new Color(0.92f, 0.78f, 0.36f, 0.42f);
            Color opticsTick = new Color(0.92f, 0.78f, 0.36f, 0.28f);
            CreateCornerBracket(vp, 150, 200, true, true, opticsCol, 10);
            CreateCornerBracket(vp, 330, 200, false, true, opticsCol, 10);
            CreateCornerBracket(vp, 150, 62, true, false, opticsCol, 10);
            CreateCornerBracket(vp, 330, 62, false, false, opticsCol, 10);
            // Subtle horizontal & vertical gunner rangefinder mil-dot ticks
            CreatePixelSolid(vp, "OptTickL", 148, 131, 6, 1, opticsTick);
            CreatePixelSolid(vp, "OptTickR", 326, 131, 6, 1, opticsTick);
            CreatePixelSolid(vp, "OptTickT", 240, 198, 1, 5, opticsTick);
            CreatePixelSolid(vp, "OptTickB", 240, 60, 1, 5, opticsTick);

            // Centered Live 360 X-Ray CRT Cutaway Monitor (164 x 138 EVEN!)
            const int xw = 164, xh = 138;
            _hangarXrayWrap = new GameObject("HangarXRayOverlay") { layer = 5 };
            _hangarXrayWrap.transform.SetParent(vp, false);
            var xRt = _hangarXrayWrap.AddComponent<RectTransform>();
            SetPixelRect(xRt, (W - xw) / 2, 64, xw, xh);

            var xPanel = CreateTacticalCard(xRt, "XRayFrame", 0, 0, xw, xh, new Color(0.55f, 0.85f, 0.90f, 0.94f), LightCyan, LightCyan, out _, withHazardTape: false);
            _hangarXrayBadge = CreatePixelText(xPanel, "FCS X-RAY  •  DRAG 360", 8, LightCyan, TextAnchor.MiddleCenter, 2, xh - 17, xw - 4, 12);

            var bgGo = new GameObject("XRayHologram") { layer = 5 };
            bgGo.transform.SetParent(xPanel, false);
            var bgImg = bgGo.AddComponent<RawImage>();
            SetPixelRect(bgImg.rectTransform, 5, 5, xw - 10, xh - 24);
            bgImg.raycastTarget = false;

            var fgGo = new GameObject("XRayCutaway") { layer = 5 };
            fgGo.transform.SetParent(xPanel, false);
            _hangarXrayGraphic = fgGo.AddComponent<TankCutawayGraphic>();
            SetPixelRect(_hangarXrayGraphic.rectTransform, 5, 5, xw - 10, xh - 24);
            _hangarXrayGraphic.raycastTarget = false;
            _hangarXrayGraphic.AttachBackdrop(bgImg, xw - 10, xh - 24);
            MilitaryUiSprites.CreateTiledSprite(xPanel, "XRayScanlines", MilitaryUiSprites.Scanlines, 5, 5, xw - 10, xh - 24, Color.white);
            _hangarXrayWrap.SetActive(false);

            // =================================================================
            // 4. RIGHT RIVETED ARMAMENT & OPERATION PLATE (x = 342..474, y = 50..238)
            // =================================================================
            var rightCard = CreateTacticalCard(vp, "MissionCard", 342, 50, 132, 188, PanelGlass, BrassDim, GoldColor, out _, withHazardTape: true);
            MilitaryUiSprites.CreateIcon(rightCard, "ShellIcon", MilitaryUiSprites.IconShell, 8, 165, SupportAmber);
            CreatePixelText(rightCard, "ARMAMENT", 8, GoldColor, TextAnchor.MiddleLeft, 18, 162, 104, 14);
            CreateWeldSeam(rightCard, 6, 160, 120);

            _garageRoleLine = CreatePixelText(rightCard, "MAIN BATTLE TANK", 8, TextLight, TextAnchor.MiddleLeft, 8, 145, 116, 12);
            _garageWeaponLine = CreatePixelText(rightCard, "WPN: CANNON", 8, SupportAmber, TextAnchor.MiddleLeft, 8, 131, 116, 12);
            _garageAmmoLine = CreatePixelText(rightCard, "APFS / HEAT / HE", 8, TextMuted, TextAnchor.MiddleLeft, 8, 117, 116, 12);
            _garageSpecialLine = CreatePixelText(rightCard, "SMOKE [Q] • COAX", 8, MbtGreen, TextAnchor.MiddleLeft, 8, 103, 116, 12);

            CreateWeldSeam(rightCard, 6, 97, 120);
            MilitaryUiSprites.CreateIcon(rightCard, "OpIcon", MilitaryUiSprites.IconCrosshair, 7, 82, GoldColor);
            CreatePixelText(rightCard, "OPERATION", 8, GoldColor, TextAnchor.MiddleLeft, 19, 80, 104, 12);
            _garageModeLine = CreatePixelText(rightCard, "MODE: 1v1 DUEL", 8, TextLight, TextAnchor.MiddleLeft, 8, 66, 116, 12);
            _garageMapLine = CreatePixelText(rightCard, "MAP: DESERT", 8, GoldColor, TextAnchor.MiddleLeft, 8, 52, 116, 12);
            _garageMatchLine = CreatePixelText(rightCard, "TIER X MATCH", 8, TextMuted, TextAnchor.MiddleLeft, 8, 38, 116, 12);

            // Stamped-steel X-Ray Inspection Toggle right inside the Mission Card
            CreatePixelButton(vp, hp, "X-RAY VIEW [X]", 350, 58, 116, 20, BtnBg, LightCyan, LightCyan, 8, () => SetXRayMode(!_xrayMode), out _btnXrayBg);

            // =================================================================
            // 5. BOTTOM ARMORED COMMAND DECK (y = 0..46, h = 46)
            // =================================================================
            MilitaryUiSprites.CreateTiledSprite(vp, "BotSteelDeck", MilitaryUiSprites.ArmorHeaderBar, 0, 0, W, 42, PanelGlassDeep);
            MilitaryUiSprites.CreateTiledSprite(vp, "BotHazardRail", MilitaryUiSprites.HazardTape, 0, 42, W, 4, new Color(1f, 1f, 1f, 0.90f));
            CreatePixelSolid(vp, "BotRuleTop", 0, 46, W, 1, new Color(0.06f, 0.07f, 0.05f, 1f));
            CreatePixelSolid(vp, "BotRuleSub", 0, 41, W, 1, new Color(0.06f, 0.07f, 0.05f, 1f));

            CreatePixelButton(vp, hp, "TECH TREE [T]", 8, 7, 106, 28, BtnBg, GoldColor, GoldColor, 8, () => SetTechTreeOpen(true));

            // Center Fleet Quick-Switch Cluster: [<] [USA (31)] [>]
            CreatePixelButton(vp, hp, "<", 122, 7, 24, 28, BtnBg, PanelBorder, TextLight, 8, () => CycleSelectedInCurrentNation(-1));
            _garageNationBtnLabel = CreatePixelButton(vp, hp, "USA (31)", 150, 7, 94, 28, BtnBg, BrassDim, TextLight, 8, CycleNationInGarage);
            CreatePixelButton(vp, hp, ">", 248, 7, 24, 28, BtnBg, PanelBorder, TextLight, 8, () => CycleSelectedInCurrentNation(1));

            // Heavy Brass Ordnance Launch Switch (with Hazard Caution End-Caps!)
            CreatePixelButton(vp, hp, "ONLINE", 280, 7, 76, 28, BtnBg, LightCyan, LightCyan, 8, () =>
            {
                if (_choosingOnlineVehicle) ConfirmOnlineVehicle();
                else SetOnlineRoomOpen(true);
            });
            _garageDeployButton = CreatePrimaryCtaButton(vp, hp, "BATTLE!", 364, 7, 108, 28, () => SetDeployModalOpen(true, fromTree: false));
        }

        private void BuildTechTreeUI()
        {
            Transform vp = _treeVisualGroup.transform;
            Transform hp = _treeHitsGroup.transform;
            int W = renderWidth;
            int H = renderHeight;

            // Full-screen translucent WoT Tech Tree Backdrop
            CreatePixelSolid(vp, "TreeBg", 0, 0, W, H, TreeBackdrop);
            CreatePixelSolid(vp, "TreeTopRule", 0, H - 27, W, 1, PanelBorder);
            CreatePixelSolid(vp, "TreeBotRule", 0, 54, W, 1, PanelBorder);

            for (int i = 0; i < TreeNations.Length; i++)
            {
                var (nation, title) = TreeNations[i];
                CreatePixelButton(vp, hp, GetNationTabLabel(nation, title), 8 + 89 * i, H - 24, 86, 20,
                    BtnBg, PanelBorder, TextLight, 8, () =>
                    {
                        _treeNation = nation;
                        RefreshAllUI();
                        ScrollTreeToSelected();
                    }, out var background);
                _nationButtons.Add(nation, background);
            }
            CreatePixelButton(vp, hp, "GARAGE [ESC]", W - 112, H - 24, 104, 20,
                BtnBg, GoldColor, GoldColor, 8, () => SetTechTreeOpen(false));

            // ----------------------------------------------------------
            // Clipped Horizontal Scroll Viewport (RectMask2D!)
            // ----------------------------------------------------------
            var viewGo = new GameObject("TreeViewport") { layer = 5 };
            viewGo.transform.SetParent(vp, false);
            var viewRt = viewGo.AddComponent<RectTransform>();
            SetPixelRect(viewRt, ViewX, ViewY, ViewW, ViewH);
            viewGo.AddComponent<RectMask2D>();

            var contentGo = new GameObject("TreeContent") { layer = 5 };
            contentGo.transform.SetParent(viewRt, false);
            _treeContentRt = contentGo.AddComponent<RectTransform>();
            SetPixelRect(_treeContentRt, 0, 0, ContentW, ViewH);

            string[] eras = { "FIRST TANKS", "INTERWAR", "1930s", "EARLY WAR",
                "MID WAR", "1943 / 44", "LATE WAR", "TRANSITION" };
            for (int col = 0; col < HistoryColumns + VehicleDef.SpecialTier; col++)
            {
                int x = PadX + col * ColStep;
                int tier = col - HistoryColumns + 1;
                if (col > 0)
                    CreatePixelSolid(_treeContentRt, $"GridCol_{col}", x - ColGap / 2, 4, 1, ViewH - 8, GridLine);
                string label = col < HistoryColumns ? eras[col] :
                    tier == VehicleDef.SpecialTier ? "SPECIAL" : $"TIER {VehicleDef.RomanTier(tier)}";
                Color color = tier == 1 || tier == VehicleDef.SpecialTier ? GoldColor : TextMuted;
                CreatePixelText(_treeContentRt, label, 8, color, TextAnchor.MiddleCenter, x, ViewH - 22, NodeW, 16);
            }

            foreach (var (nation, _) in TreeNations)
            {
                var links = new GameObject($"TreeLinks_{nation}", typeof(RectTransform)) { layer = 5 };
                links.transform.SetParent(_treeContentRt, false);
                SetPixelRect((RectTransform)links.transform, 0, 0, ContentW, ViewH);
                _nationLinks.Add(nation, links);
            }
            foreach (var vehicle in _allVehicles)
            {
                int x = PadX + TreeColumn(vehicle) * ColStep;
                Color border = TreeLineColor(vehicle);
                var slot = CreateTreeNodeSlot(_treeContentRt, hp, x, TreeRow(vehicle), border);
                slot.vehicleId = vehicle.id;
                _treeNodes.Add(vehicle.id, slot);
            }

            foreach (var vehicle in _allVehicles)
            {
                VehicleDef parent = TreeParent(vehicle);
                if (parent != null)
                    ConnectTreeNodes(_nationLinks[vehicle.nation].transform, _treeNodes[parent.id],
                        _treeNodes[vehicle.id], TreeLineColor(vehicle));
            }

            // Horizontal Scrollbar & Quick Arrows (y = 56..66)
            CreatePixelButton(vp, hp, "<", 8, 55, 20, 12, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _targetScrollX = Mathf.Clamp(_targetScrollX - ColStep * 2f, 0f, MaxScrollX);
            });
            CreatePixelSolid(vp, "ScrollTrack", ScrollTrackX, 58, ScrollTrackW, 6, new Color(0.14f, 0.14f, 0.12f, 1f));
            _scrollThumbRt = CreatePixelSolid(vp, "ScrollThumb", ScrollTrackX, 58, 120, 6, GoldColor);
            CreatePixelButton(vp, hp, ">", W - 28, 55, 20, 12, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _targetScrollX = Mathf.Clamp(_targetScrollX + ColStep * 2f, 0f, MaxScrollX);
            });

            // Bottom Selected Vehicle Detail Bar (y = 0..54)
            _treeDetailTitle = CreatePixelText(vp, "", 8, GoldColor, TextAnchor.MiddleLeft, 10, 32, 336, 16);
            _treeDetailStats = CreatePixelText(vp, "", 8, TextLight, TextAnchor.MiddleLeft, 10, 16, 336, 16);
            CreatePixelButton(vp, hp, "WWI / WWII", 10, 2, 80, 12, BtnBg, PanelBorder, TextLight, 8, () => JumpTreeTo(0));
            CreatePixelButton(vp, hp, "I - V", 94, 2, 68, 12, BtnBg, PanelBorder, TextLight, 8, () => JumpTreeTo(HistoryColumns));
            CreatePixelButton(vp, hp, "VI - VIII", 166, 2, 76, 12, BtnBg, PanelBorder, TextLight, 8, () => JumpTreeTo(HistoryColumns + 5));
            CreatePixelButton(vp, hp, "IX - XIII", 246, 2, 84, 12, BtnBg, PanelBorder, TextLight, 8,
                () => JumpTreeTo(HistoryColumns + VehicleDef.ModernTier - 1));

            CreatePixelButton(vp, hp, "VIEW TANK", 352, 28, 118, 20, BtnBg, PanelBorder, TextLight, 8, () => SetTechTreeOpen(false));
            _treeDeployButton = CreatePrimaryCtaButton(vp, hp, "BATTLE!", 352, 4, 118, 20, () => SetDeployModalOpen(true, fromTree: true));
        }

        private static int TreeColumn(VehicleDef vehicle) =>
            vehicle.historyColumn > 0 ? vehicle.historyColumn - 1 : HistoryColumns + vehicle.tier - 1;

        private void JumpTreeTo(int column) => _targetScrollX = Mathf.Clamp(column * ColStep, 0f, MaxScrollX);

        private static int TreeRow(VehicleDef vehicle)
        {
            if (vehicle.historyColumn > 0)
                return vehicle.historyRow == 1 ? 108 : vehicle.historyRow == 2 ? 16 : 62;
            return vehicle.tab switch
            {
                "MBT" => 108,
                "LIGHT" => vehicle.tier < VehicleDef.ModernTier ? 108 : 62,
                "SUPPORT" => 16,
                _ => 62
            };
        }

        private static Color TreeLineColor(VehicleDef vehicle)
        {
            if (vehicle.IsSpecial) return SpecialBorder;
            return vehicle.tab switch
            {
                "LIGHT" => LightCyan,
                "SUPPORT" => SupportAmber,
                "MBT" => MbtGreen,
                _ => vehicle.role == "TD" ? SupportAmber : vehicle.role == "LIGHT" ? LightCyan : PanelBorder
            };
        }

        private VehicleDef TreeParent(VehicleDef vehicle)
        {
            if (!string.IsNullOrEmpty(vehicle.treeFrom))
                return _vehicleMap.TryGetValue(vehicle.treeFrom, out var linked) && linked.nation == vehicle.nation
                    ? linked : null;

            if (vehicle.historyColumn > 0) return null;
            VehicleDef previous = null;
            foreach (var other in _allVehicles)
            {
                if (other.nation != vehicle.nation || other.historyColumn > 0 || other.tier >= vehicle.tier)
                    continue;
                if (other.tab == vehicle.tab && (previous == null || previous.tier < other.tier))
                    previous = other;
            }

            if (previous != null || vehicle.tab == "T1_5") return previous;
            foreach (var other in _allVehicles)
                if (other.nation == vehicle.nation && other.tab == "T1_5" && other.tier < vehicle.tier &&
                    (previous == null || previous.tier < other.tier))
                    previous = other;
            return previous;
        }

        private static void ConnectTreeNodes(Transform parent, TreeNodeSlot source, TreeNodeSlot target, Color color)
        {
            int start = source.contentX + NodeW;
            int end = target.contentX;
            if (start >= end) return;
            int y1 = source.contentY + NodeH / 2;
            int y2 = target.contentY + NodeH / 2;
            if (y1 == y2)
            {
                CreatePixelSolid(parent, "Link", start, y1, end - start, 2, color);
                return;
            }

            int elbow = start + ColGap / 2;
            CreatePixelSolid(parent, "Link", start, y1, elbow - start + 2, 2, color);
            CreatePixelSolid(parent, "Elbow", elbow, Math.Min(y1, y2), 2, Math.Abs(y2 - y1) + 2, color);
            CreatePixelSolid(parent, "Link", elbow, y2, end - elbow, 2, color);
        }

        private TreeNodeSlot CreateTreeNodeSlot(Transform contentParent, Transform hitParent, int contentX, int contentY, Color border)
        {
            var box = CreatePixelBox(contentParent, $"Node_{contentX}_{contentY}", contentX, contentY, NodeW, NodeH, BtnBg, border, out Image fillImg);
            var topTxt = CreatePixelText(box, "", 8, TextMuted, TextAnchor.MiddleLeft, 4, 16, NodeW - 8, 12);
            var nameTxt = CreatePixelText(box, "", 8, TextLight, TextAnchor.MiddleLeft, 4, 2, NodeW - 8, 12);

            var hitGo = new GameObject($"HitNode_{contentX}_{contentY}");
            hitGo.transform.SetParent(hitParent, false);
            var hitRt = hitGo.AddComponent<RectTransform>();

            var hitImg = hitGo.AddComponent<Image>();
            hitImg.color = new Color(0f, 0f, 0f, 0f);
            hitImg.raycastTarget = true;

            var slot = new TreeNodeSlot
            {
                visualGo = box.gameObject,
                hitGo = hitGo,
                hitRt = hitRt,
                borderImg = box.GetComponent<Image>(),
                fillImg = fillImg,
                topText = topTxt,
                nameText = nameTxt,
                idleBorder = border,
                contentX = contentX,
                contentY = contentY
            };

            var btn = hitGo.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() =>
            {
                // Ignore accidental clicks at the end of a horizontal drag
                if (_treeDragAccumDist > 7f) return;
                if (!string.IsNullOrEmpty(slot.vehicleId))
                    SelectVehicle(slot.vehicleId, closeTreeIfSame: true);
            });

            var trigger = hitGo.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                if (slot.fillImg != null && slot.vehicleId != _sessionState.SelectedPlayerVehicleId)
                    slot.fillImg.color = BtnHover;
            });
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ =>
            {
                if (slot.fillImg != null && slot.vehicleId != _sessionState.SelectedPlayerVehicleId)
                    slot.fillImg.color = BtnBg;
            });
            trigger.triggers.Add(exit);

            return slot;
        }

        private void SetXRayMode(bool on)
        {
            _xrayMode = on;
            if (_hangarXrayWrap != null) _hangarXrayWrap.SetActive(on && !_treeOpen);
            if (_btnXrayBg != null) _btnXrayBg.color = on ? new Color(0.22f, 0.54f, 0.62f, 1f) : BtnBg;
            if (showcaseTank != null) showcaseTank.gameObject.SetActive(!on || _treeOpen);
            RefreshAllUI();
        }

        private void BuildSettingsUI()
        {
            Transform vp = _settingsVisualGroup.transform;
            Transform hp = _settingsHitsGroup.transform;
            int W = renderWidth;
            int H = renderHeight;

            CreatePixelSolid(vp, "SettingsDim", 0, 0, W, H, new Color(0.02f, 0.03f, 0.03f, 0.78f));

            int cw = 248;
            int ch = 198;
            int cx = (W - cw) / 2;
            int cy = (H - ch) / 2;
            CreateTacticalCard(vp, "SettingsCard", cx, cy, cw, ch, PanelGlassModal, LightCyan, LightCyan, out _);
            CreatePixelText(vp, "SYSTEM SETTINGS", 8, GoldColor, TextAnchor.MiddleCenter, cx, cy + ch - 22, cw, 16, useTitleFont: true);
            CreatePixelSolid(vp, "SetHdrRule", cx + 12, cy + ch - 24, cw - 24, 1, Hairline);

            // Row 1: SE (SFX) Volume [-] [SE: 100%] [+]
            CreatePixelButton(vp, hp, "-", cx + 12, cy + 148, 24, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _sessionState.StepSfxVolume(-0.1f);
                PlayHangarPreviewSfx();
                RefreshSettingsLabels();
            });
            _setSfxLabel = CreatePixelButton(vp, hp, "SE: 100%", cx + 40, cy + 148, cw - 80, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _sessionState.ToggleSfxEnabled();
                PlayHangarPreviewSfx();
                RefreshSettingsLabels();
            });
            CreatePixelButton(vp, hp, "+", cx + cw - 36, cy + 148, 24, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _sessionState.StepSfxVolume(+0.1f);
                PlayHangarPreviewSfx();
                RefreshSettingsLabels();
            });

            // Row 2: BGM Volume [-] [BGM: 60%] [+]
            CreatePixelButton(vp, hp, "-", cx + 12, cy + 126, 24, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _sessionState.StepBgmVolume(-0.1f);
                RefreshSettingsLabels();
            });
            _setBgmLabel = CreatePixelButton(vp, hp, "BGM: 60%", cx + 40, cy + 126, cw - 80, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _sessionState.ToggleBgmEnabled();
                RefreshSettingsLabels();
            });
            CreatePixelButton(vp, hp, "+", cx + cw - 36, cy + 126, 24, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _sessionState.StepBgmVolume(+0.1f);
                RefreshSettingsLabels();
            });

            // Row 3: Drive Controls
            _setDriveLabel = CreatePixelButton(vp, hp, "", cx + 12, cy + 104, cw - 24, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _sessionState.ArcadeWASD = !_sessionState.ArcadeWASD;
                _sessionState.SaveSettings();
                RefreshSettingsLabels();
            });

            // Row 4: Enemy AI
            _setAiLabel = CreatePixelButton(vp, hp, "", cx + 12, cy + 82, cw - 24, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _sessionState.AiEnabled = !_sessionState.AiEnabled;
                _sessionState.SaveSettings();
                RefreshSettingsLabels();
            });

            // Row 5: Graphics Mode (applies live in Hangar too!)
            _setGfxLabel = CreatePixelButton(vp, hp, "", cx + 12, cy + 60, cw - 24, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _sessionState.PostProcessEnabled = !_sessionState.PostProcessEnabled;
                _sessionState.SaveSettings();
                _postProcess?.SetEnabled(_sessionState.PostProcessEnabled);
                _windowbox?.SetHd2dMode(_sessionState.PostProcessEnabled);
                RefreshSettingsLabels();
            });

            // Row 6: Display Scaling (applies live in Hangar too!)
            _setViewLabel = CreatePixelButton(vp, hp, "", cx + 12, cy + 38, cw - 24, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                _sessionState.IntegerWindowbox = !_sessionState.IntegerWindowbox;
                _sessionState.SaveSettings();
                if (_windowbox != null) _windowbox.integerScale = _sessionState.IntegerWindowbox;
                RefreshSettingsLabels();
            });

            // Bottom: Close
            CreatePrimaryCtaButton(vp, hp, "CLOSE [ESC]", cx + 12, cy + 10, cw - 24, 20, () =>
            {
                SetSettingsOpen(false);
            });

            RefreshSettingsLabels();
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
                _setDriveLabel.text = _sessionState.ArcadeWASD ? "DRIVE: ARCADE WASD" : "DRIVE: TANK STEER";
            if (_setAiLabel != null)
                _setAiLabel.text = _sessionState.AiEnabled ? "ENEMY AI: ON" : "ENEMY AI: OFF";
            if (_setGfxLabel != null)
                _setGfxLabel.text = _sessionState.PostProcessEnabled ? "GFX: HD-2D" : "GFX: RETRO";
            if (_setViewLabel != null)
                _setViewLabel.text = _sessionState.IntegerWindowbox ? "VIEW: INT WINDOWBOX" : "VIEW: 16:9 LETTERBOX";
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
                    _garageIndexBadge.text = $"UNIT {(idx >= 0 ? idx + 1 : 1):00}/{nationList.Length:00}";
                }

                // 6 Segmented Pixel Stat Bars
                UpdateStatBar(_statArm, sel.armorFront / 1.25f, $"{Mathf.RoundToInt(sel.armorFront * 100f)}");
                UpdateStatBar(_statPen, sel.penetration / 155f, $"{Mathf.RoundToInt(sel.penetration)}");
                float rldScore = 1f - Mathf.Clamp01((sel.reload - 0.16f) / 2.04f);
                UpdateStatBar(_statRld, rldScore, $"{sel.reload:F2}s");
                UpdateStatBar(_statSpd, sel.speed / 84f, $"{Mathf.RoundToInt(sel.speed)}");
                UpdateStatBar(_statTrn, sel.EffectiveHullTurnSpeed / 210f, $"{Mathf.RoundToInt(sel.EffectiveHullTurnSpeed)}");
                UpdateStatBar(_statTur, sel.EffectiveTurretTurnSpeed / 355f, $"{Mathf.RoundToInt(sel.EffectiveTurretTurnSpeed)}");

                // Hull & Drivetrain mini telemetry
                string gearStr = sel.IsWheeled ? $"{sel.WheelAxleCount * 2}x{sel.WheelAxleCount * 2} WHEEL" : "TRACKED";
                string engStr = sel.IsFrontEngine ? "FRONT ENG" : "REAR ENG";
                string turStr = sel.IsTurretless ? "CASEMATE" : (sel.IsUnmannedTurret ? "UNMANNED" : "MANNED");
                if (_garageCrewLine != null) _garageCrewLine.text = $"CREW {sel.CrewCapacity}  •  {gearStr}";
                if (_garageEngineLine != null) _garageEngineLine.text = $"ENG: {(string.IsNullOrEmpty(sel.engine) ? "DIESEL" : sel.engine.ToUpper())}";
                if (_garageLayoutLine != null) _garageLayoutLine.text = $"{engStr} • {turStr}";

                // Right Card: Armament & Operation
                if (_garageRoleLine != null) _garageRoleLine.text = FormatRoleFull(sel.role);
                if (_garageWeaponLine != null) _garageWeaponLine.text = $"WPN: {sel.WeaponLabel}";
                if (_garageAmmoLine != null) _garageAmmoLine.text = FormatAmmoSummary(sel);
                if (_garageSpecialLine != null)
                {
                    if (sel.HasSecondaryAtgm) _garageSpecialLine.text = "RMB: GUIDED ATGM";
                    else if (sel.weapon == "ATGM") _garageSpecialLine.text = "SACLOS MISSILE";
                    else _garageSpecialLine.text = "SMOKE [Q] • COAX MG";
                }

                string mapTag = DesertBattleManager.GetMapDisplayName(_sessionState.SelectedBattleMap);
                string modeShort = _sessionState.SelectedBattleMode switch
                {
                    BattleMode.FiringRange => "FIRING RANGE",
                    BattleMode.Battle5v5 => "5v5 COMPANY",
                    BattleMode.Squad2v2 => "2v2 SQUAD",
                    BattleMode.SurvivalWave => "SURVIVAL",
                    _ => "1v1 DUEL"
                };
                if (_garageModeLine != null) _garageModeLine.text = $"MODE: {modeShort}";
                if (_garageMapLine != null)
                {
                    _garageMapLine.text = $"MAP: {mapTag}";
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
                        ? "6 TARGETS • TEST"
                        : $"TIER {sel.TierRoman} BRACKET";
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

        private static RectTransform CreateTacticalCard(Transform parent, string name, int x, int y, int w, int h, Color bg, Color border, Color topAccent, bool withHazardTape = true)
            => CreateTacticalCard(parent, name, x, y, w, h, bg, border, topAccent, out _, withHazardTape);

        private static RectTransform CreateTacticalCard(Transform parent, string name, int x, int y, int w, int h, Color bg, Color border, Color topAccent, out Image accentImg, bool withHazardTape = true)
        {
            // 9-sliced Riveted Rolled-Steel Armor Plate with forged corner bolts & beveled edges!
            var box = MilitaryUiSprites.CreateSlicedSprite(parent, name, MilitaryUiSprites.ArmorPanel, x, y, w, h, bg, out _);
            accentImg = CreatePixelSolid(box, "TopAccent", 4, h - 4, Mathf.Max(1, w - 8), 2, topAccent).GetComponent<Image>();
            if (withHazardTape && w >= 48)
            {
                MilitaryUiSprites.CreateTiledSprite(box, "HazardStrip", MilitaryUiSprites.HazardTape, 6, h - 8, Mathf.Max(8, w - 12), 3, new Color(1f, 1f, 1f, 0.88f));
            }
            return box;
        }

        private static void CreateWeldSeam(Transform parent, int x, int y, int w)
        {
            CreatePixelSolid(parent, "WeldDk", x, y, w, 1, new Color(0.06f, 0.08f, 0.06f, 0.95f));
            CreatePixelSolid(parent, "WeldHi", x, y - 1, w, 1, new Color(0.36f, 0.40f, 0.30f, 0.65f));
        }

        private static void CreateCornerBracket(Transform parent, int x, int y, bool left, bool top, Color col, int arm)
        {
            int hx = left ? x : x - arm + 1;
            int hy = y;
            int vx = x;
            int vy = top ? y - arm + 1 : y;
            CreatePixelSolid(parent, "BrH", hx, hy, arm, 1, col);
            CreatePixelSolid(parent, "BrV", vx, vy, 1, arm, col);
        }

        private StatBarSlot CreateStatBarRow(Transform parent, string label, int y, Color fillColor)
        {
            CreatePixelText(parent, label, 8, TextMuted, TextAnchor.MiddleLeft, 8, y, 26, 14);
            // Recessed dark armored gauge channel housing the 10 LED pips
            CreatePixelSolid(parent, $"GaugeTrack_{label}", 34, y + 3, 53, 8, new Color(0.05f, 0.06f, 0.05f, 0.96f));
            CreatePixelSolid(parent, $"GaugeLip_{label}", 34, y + 2, 53, 1, new Color(0.36f, 0.40f, 0.31f, 0.55f));
            const int pipCount = 10;
            var pips = new Image[pipCount];
            for (int i = 0; i < pipCount; i++)
            {
                var p = CreatePixelSolid(parent, $"Pip_{label}_{i}", 36 + i * 5, y + 4, 4, 6, PipEmpty);
                pips[i] = p.GetComponent<Image>();
            }
            var valTxt = CreatePixelText(parent, "0", 8, TextLight, TextAnchor.MiddleRight, 88, y, 36, 14);
            return new StatBarSlot { pips = pips, valueText = valTxt, fillColor = fillColor };
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

        private Text CreatePrimaryCtaButton(
            Transform visualParent, Transform hitParent, string label,
            int x, int y, int w, int h, Action onClick)
        {
            // Heavy Brass Ordnance Launch Switch with black-and-amber hazard caution end-caps!
            Color idleTint = new Color(0.92f, 0.90f, 0.85f, 1f);
            var outer = MilitaryUiSprites.CreateSlicedSprite(visualParent, "Cta_" + label, MilitaryUiSprites.OrdnanceButton, x, y, w, h, idleTint, out Image fillImg);
            var txt = CreatePixelText(outer, label, 8, DarkInk, TextAnchor.MiddleCenter, 0, 0, w, h, useTitleFont: true);
            var sh = txt.GetComponent<Shadow>();
            if (sh != null) sh.effectColor = new Color(1.0f, 0.94f, 0.68f, 0.55f);

            var hitGo = new GameObject("Hit_" + label);
            hitGo.transform.SetParent(hitParent, false);
            var hitRt = hitGo.AddComponent<RectTransform>();
            hitRt.anchorMin = new Vector2((float)x / renderWidth, (float)y / renderHeight);
            hitRt.anchorMax = new Vector2((float)(x + w) / renderWidth, (float)(y + h) / renderHeight);
            hitRt.offsetMin = Vector2.zero;
            hitRt.offsetMax = Vector2.zero;

            var hitImg = hitGo.AddComponent<Image>();
            hitImg.color = new Color(0f, 0f, 0f, 0f);
            hitImg.raycastTarget = true;

            var btn = hitGo.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.OnClickAsObservable().Subscribe(_ => onClick?.Invoke()).AddTo(btn);

            var trigger = hitGo.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => { if (fillImg != null) fillImg.color = Color.white; });
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => { if (fillImg != null) fillImg.color = idleTint; });
            trigger.triggers.Add(exit);

            return txt;
        }

        private Text CreatePixelText(Transform parent, string content, int fontSize, Color col, TextAnchor align, int x, int y, int w, int h, bool useTitleFont = false)
        {
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
            sh.effectColor = new Color(0f, 0f, 0f, 0.90f);
            sh.effectDistance = new Vector2(1f, -1f);
            return t;
        }

        private Text CreatePixelButton(
            Transform visualParent, Transform hitParent, string label,
            int x, int y, int w, int h,
            Color bg, Color border, Color textColor, int fontSize, Action onClick,
            bool useTitleFont = false)
        {
            return CreatePixelButton(visualParent, hitParent, label, x, y, w, h, bg, border, textColor, fontSize, onClick, out _, useTitleFont);
        }

        private Text CreatePixelButton(
            Transform visualParent, Transform hitParent, string label,
            int x, int y, int w, int h,
            Color bg, Color border, Color textColor, int fontSize, Action onClick,
            out Image fillImgOut, bool useTitleFont = false)
        {
            // Stamped-steel riveted military switch button!
            var visualBox = MilitaryUiSprites.CreateSlicedSprite(visualParent, "Btn_" + label, MilitaryUiSprites.SteelButton, x, y, w, h, bg, out Image fillImg);
            fillImgOut = fillImg;

            // Add a crisp 2px military status stripe on the left when button uses a special accent color (Cyan/Gold/Green)
            bool hasAccent = border.a > 0.5f && border != PanelBorder && border != BrassDim && w >= 40;
            if (hasAccent)
                CreatePixelSolid(visualBox, "StatusBar", 2, 3, 2, Mathf.Max(2, h - 6), border);

            var txt = CreatePixelText(visualBox, label, fontSize, textColor, TextAnchor.MiddleCenter, hasAccent ? 2 : 0, 0, hasAccent ? w - 2 : w, h, useTitleFont);

            var hitGo = new GameObject("Hit_" + label);
            hitGo.transform.SetParent(hitParent, false);
            var hitRt = hitGo.AddComponent<RectTransform>();
            hitRt.anchorMin = new Vector2((float)x / renderWidth, (float)y / renderHeight);
            hitRt.anchorMax = new Vector2((float)(x + w) / renderWidth, (float)(y + h) / renderHeight);
            hitRt.offsetMin = Vector2.zero;
            hitRt.offsetMax = Vector2.zero;

            var hitImg = hitGo.AddComponent<Image>();
            hitImg.color = new Color(0f, 0f, 0f, 0f);
            hitImg.raycastTarget = true;

            var btn = hitGo.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.OnClickAsObservable().Subscribe(_ => onClick?.Invoke()).AddTo(btn);

            Color idleBg = (bg == BtnSelected) ? BtnBg : bg;
            var trigger = hitGo.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                if (fillImg != null && fillImg.color != BtnSelected)
                    fillImg.color = idleBg == GoldColor ? Color.Lerp(idleBg, Color.white, 0.2f) : BtnHover;
            });
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ =>
            {
                if (fillImg != null && fillImg.color != BtnSelected)
                    fillImg.color = idleBg;
            });
            trigger.triggers.Add(exit);

            return txt;
        }

        private void OnDestroy()
        {
            Font.textureRebuilt -= OnFontTextureRebuilt;
            if (_groundHd2dMat != null) Destroy(_groundHd2dMat);
        }
    }
}
