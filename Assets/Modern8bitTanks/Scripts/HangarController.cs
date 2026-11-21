using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
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
        [Range(0.45f, 1.0f)] public float squash = 0.75f;
        [Range(0f, 2.5f)] public float spacingPx = 0.65f;
        [Range(1.0f, 2.5f)] public float showcaseZoom = 1.65f;

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
        private AudioSource _previewAudio;
        private AudioClip _previewSfxClip;

        // Garage UI Labels
        private Text _garageTopBadge;
        private Text _garageTankName;
        private Text _garageSpecLine;
        private Text _garageMatchLine;
        private Image _btnMapDesertBg;
        private Image _btnMapCityBg;
        private Image _btnMapForestBg;
        private Texture2D _hangarBackdropTex;
        private const int HangarGroundW = 640;
        private const int HangarGroundH = 400;

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

        // Crisp 8-bit Military Palette
        private static readonly Color TreeBackdrop = new Color(0.05f, 0.06f, 0.05f, 0.96f);
        private static readonly Color PanelBorder = new Color(0.38f, 0.33f, 0.24f, 1f);
        private static readonly Color GridLine = new Color(0.24f, 0.22f, 0.18f, 0.55f);
        private static readonly Color LineColor = new Color(0.58f, 0.52f, 0.40f, 1f);
        private static readonly Color GoldColor = new Color(0.96f, 0.78f, 0.40f, 1f);
        private static readonly Color SpecialBorder = new Color(0.94f, 0.74f, 0.28f, 1f);
        private static readonly Color BtnBg = new Color(0.12f, 0.13f, 0.11f, 0.96f);
        private static readonly Color BtnHover = new Color(0.22f, 0.21f, 0.16f, 0.98f);
        private static readonly Color BtnSelected = new Color(0.28f, 0.23f, 0.12f, 1f);
        private static readonly Color TextLight = new Color(0.96f, 0.95f, 0.90f, 1f);
        private static readonly Color TextMuted = new Color(0.68f, 0.66f, 0.56f, 1f);
        private static readonly Color LightCyan = new Color(0.54f, 0.86f, 0.92f, 1f);
        private static readonly Color SupportAmber = new Color(0.96f, 0.64f, 0.34f, 1f);
        private static readonly Color MbtGreen = new Color(0.60f, 0.88f, 0.44f, 1f);
        private static readonly Color DarkInk = new Color(0.08f, 0.07f, 0.04f, 1f);

        private void Awake()
        {
            Time.timeScale = 1f;
            SpriteStackCamera.SetRoll(0f);

            _cam = Camera.main;
            if (_cam == null)
            {
                var cGo = new GameObject("Main Camera");
                cGo.tag = "MainCamera";
                _cam = cGo.AddComponent<Camera>();
                cGo.AddComponent<AudioListener>();
            }
            _cam.orthographic = true;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.11f, 0.09f, 0.06f, 1f);
            squash = 0.75f;
            spacingPx = 0.65f;

            _windowbox = GetComponent<PixelWindowbox>();
            if (_windowbox == null) _windowbox = gameObject.AddComponent<PixelWindowbox>();
            _windowbox.Initialize(_cam, renderWidth, renderHeight, BattleSessionState.IntegerWindowbox, useHd2d: BattleSessionState.PostProcessEnabled);
            _windowbox.SetTiltShiftParams(1.15f, 0.52f, 0.24f);

            _postProcess = GetComponent<BattlePostProcess>();
            if (_postProcess == null) _postProcess = gameObject.AddComponent<BattlePostProcess>();
            _postProcess.postProcessEnabled = BattleSessionState.PostProcessEnabled;
            // Attach URP Post-Processing to PresentCamera so Bloom/Vignette/ACES run AFTER the 480x270 RT at full screen res!
            _postProcess.Initialize(_windowbox.PresentCamera != null ? _windowbox.PresentCamera : _cam);

            LoadResourcesAndCatalog();
            SetupDesertBackdrop();
            SetupCenterShowcaseTank();

            _atmosphere = GetComponent<DesertAtmosphere>();
            if (_atmosphere == null) _atmosphere = gameObject.AddComponent<DesertAtmosphere>();
            _atmosphere.Initialize(_cam, WorldW, WorldH, _whitePxSprite, _emissiveFxMat);
            _atmosphere.ApplyMapTheme(BattleSessionState.SelectedBattleMap);

            EnsureEventSystem();
            BuildPixelCanvas();
            BuildTitleAndDeployGroups();
            BuildGarageUI();
            BuildTechTreeUI();
            BuildSettingsUI();
            BuildTitleUI();
            BuildDeployModalUI();
            BuildTitleStage();

            VehicleDef cur = GetSelectedVehicle();
            if (cur != null) _treeNation = cur.nation;
            ApplySelectedTankToTurntable();
            SetSettingsOpen(false);
            SetDeployModalOpen(false);
            SetTechTreeOpen(false);
            // Show the Sprite-Stacking Title Screen on initial launch; return straight to Garage after battles!
            SetTitleOpen(!BattleSessionState.HasEnteredGarage);
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
            if (!_vehicleMap.ContainsKey(BattleSessionState.SelectedPlayerVehicleId) && _allVehicles.Length > 0)
                BattleSessionState.SelectedPlayerVehicleId = _allVehicles[0].id;

            _previewSfxClip = Resources.Load<AudioClip>("SFX/reload_clank") ?? TankAudioSynthesizer.CreateReloadClank();
            var prevGo = new GameObject("HangarUiPreviewAudio");
            prevGo.transform.SetParent(transform, false);
            _previewAudio = prevGo.AddComponent<AudioSource>();
            _previewAudio.playOnAwake = false;
            _previewAudio.spatialBlend = 0f;
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
            if (desertGroundRenderer == null)
            {
                var gObj = GameObject.Find("DesertGround");
                if (gObj != null) desertGroundRenderer = gObj.GetComponent<SpriteRenderer>();
            }
            if (desertGroundRenderer == null)
            {
                var gGo = new GameObject("DesertGround");
                desertGroundRenderer = gGo.AddComponent<SpriteRenderer>();
            }
            desertGroundRenderer.enabled = true;
            desertGroundRenderer.transform.position = new Vector3(WorldW * 0.5f, WorldH * 0.5f, 5f);
            desertGroundRenderer.sortingOrder = -30000;
            if (_groundHd2dMat != null) desertGroundRenderer.sharedMaterial = _groundHd2dMat;

            _hangarBackdropTex = new Texture2D(HangarGroundW, HangarGroundH, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            desertGroundRenderer.sprite = Sprite.Create(
                _hangarBackdropTex,
                new Rect(0, 0, HangarGroundW, HangarGroundH),
                new Vector2(0.5f, 0.5f),
                PPU
            );
            RefreshMapBackdropTheme();
        }

        private void RefreshMapBackdropTheme()
        {
            BattleMap map = BattleSessionState.SelectedBattleMap;
            if (_hangarBackdropTex != null)
            {
                _hangarBackdropTex.SetPixels32(DesertBattleManager.GenerateMapPixels(map, HangarGroundW, HangarGroundH));
                _hangarBackdropTex.Apply(false, false);
            }
            _postProcess?.ApplyMapTheme(map);
            _atmosphere?.ApplyMapTheme(map);
        }

        private void SelectBattleMap(BattleMap map)
        {
            BattleSessionState.SelectedBattleMap = map;
            RefreshMapBackdropTheme();
            RefreshAllUI();
        }

        private void SetupCenterShowcaseTank()
        {
            if (showcaseTank == null)
                showcaseTank = FindAnyObjectByType<TankEntity>();

            if (showcaseTank == null)
            {
                var tGo = new GameObject("ShowcaseTank");
                showcaseTank = tGo.AddComponent<TankEntity>();
            }

            if (showcaseTank.hullStack != null && showcaseTank.hullStack.sliceMaterial != null)
                _stackFlashMat = showcaseTank.hullStack.sliceMaterial;

            Vector2 center = new Vector2(WorldW * 0.5f, WorldH * 0.5f + 0.35f);
            showcaseTank.transform.position = new Vector3(center.x, center.y, 0f);
            showcaseTank.isPlayer = false;
            showcaseTank.factionTeam = "ally";
        }

        private VehicleDef GetSelectedVehicle()
        {
            if (_vehicleMap.TryGetValue(BattleSessionState.SelectedPlayerVehicleId, out var def))
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

            Vector2 center = new Vector2(WorldW * 0.5f, WorldH * 0.5f + 0.35f);
            showcaseTank.ApplyVehicleDef(def, hullTex, turretTex, _stackFlashMat);
            showcaseTank.CaptureSpawnAndReset(center, 35f + _manualAngleOffset);
            RefreshAllUI();
        }

        private void SelectVehicle(string id, bool closeTreeIfSame = false)
        {
            if (!_vehicleMap.ContainsKey(id)) return;
            bool wasSame = BattleSessionState.SelectedPlayerVehicleId == id;
            BattleSessionState.SelectedPlayerVehicleId = id;
            ApplySelectedTankToTurntable();
            if (closeTreeIfSame && wasSame)
                SetTechTreeOpen(false);
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
            SceneManager.LoadScene(BattleSessionState.BattleSceneName);
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
                _windowbox.integerScale = BattleSessionState.IntegerWindowbox;

            if (kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame)
                {
                    if (_settingsOpen) SetSettingsOpen(false);
                    else if (_deployOpen) SetDeployModalOpen(false);
                    else if (_treeOpen) SetTechTreeOpen(false);
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
                        _hangarXrayGraphic.ShowHangarInspect(showcaseTank, showcaseTank.hullAngleDeg, showcaseTank.turretAngleDeg, showcaseZoom);
                }
                else
                {
                    if (!showcaseTank.gameObject.activeSelf) showcaseTank.gameObject.SetActive(true);
                    showcaseTank.SyncVisuals(squash, spacingPx);
                }
            }

            // Center camera squarely on the showcase turntable
            if (_cam != null)
            {
                float orthoSize = ((renderHeight / PPU) * 0.5f) / showcaseZoom;
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
        private void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();
        }

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

            // Top slim header bar (y = 242..270, h = 28) — spacious & zero duplicate buttons!
            CreatePixelSolid(vp, "TopShade", 0, H - 28, W, 28, new Color(0.05f, 0.05f, 0.04f, 0.84f));
            CreatePixelSolid(vp, "TopRule", 0, H - 29, W, 1, PanelBorder);

            CreatePixelButton(vp, hp, "TITLE", 6, H - 24, 54, 20, BtnBg, PanelBorder, TextMuted, 8, () => SetTitleOpen(true));
            CreatePixelButton(vp, hp, "SETTINGS", 64, H - 24, 72, 20, BtnBg, LightCyan, TextLight, 8, () => SetSettingsOpen(true));
            _garageTopBadge = CreatePixelText(vp, "8-BIT TANKS", 8, GoldColor, TextAnchor.MiddleCenter, 140, H - 24, 144, 20, useTitleFont: false);

            // Top-Right Live Biome Map Selector: DESERT / CITY / FOREST!
            CreatePixelButton(vp, hp, "DESERT", 288, H - 24, 60, 20, BtnSelected, GoldColor, TextLight, 8, () => SelectBattleMap(BattleMap.Desert), out _btnMapDesertBg);
            CreatePixelButton(vp, hp, "CITY", 352, H - 24, 52, 20, BtnBg, PanelBorder, TextLight, 8, () => SelectBattleMap(BattleMap.City), out _btnMapCityBg);
            CreatePixelButton(vp, hp, "FOREST", 408, H - 24, 66, 20, BtnBg, MbtGreen, TextLight, 8, () => SelectBattleMap(BattleMap.Forest), out _btnMapForestBg);

            // Centered Live 360 X-Ray Inspection Overlay (160 x 136 EVEN!)
            const int xw = 160, xh = 136;
            _hangarXrayWrap = new GameObject("HangarXRayOverlay") { layer = 5 };
            _hangarXrayWrap.transform.SetParent(vp, false);
            var xRt = _hangarXrayWrap.AddComponent<RectTransform>();
            SetPixelRect(xRt, (W - xw) / 2, 92, xw, xh);

            var xPanel = CreatePixelBox(xRt, "XRayFrame", 0, 0, xw, xh, new Color32(10, 20, 25, 185), new Color32(72, 168, 178, 225));
            _hangarXrayBadge = CreatePixelText(xPanel, "X-RAY INTERNAL VIEW  DRAG 360", 8, LightCyan, TextAnchor.MiddleCenter, 2, xh - 14, xw - 4, 12);

            var bgGo = new GameObject("XRayHologram") { layer = 5 };
            bgGo.transform.SetParent(xPanel, false);
            var bgImg = bgGo.AddComponent<RawImage>();
            SetPixelRect(bgImg.rectTransform, 4, 4, xw - 8, xh - 20);
            bgImg.raycastTarget = false;

            var fgGo = new GameObject("XRayCutaway") { layer = 5 };
            fgGo.transform.SetParent(xPanel, false);
            _hangarXrayGraphic = fgGo.AddComponent<TankCutawayGraphic>();
            SetPixelRect(_hangarXrayGraphic.rectTransform, 4, 4, xw - 8, xh - 20);
            _hangarXrayGraphic.raycastTarget = false;
            _hangarXrayGraphic.AttachBackdrop(bgImg, xw - 8, xh - 20);
            _hangarXrayWrap.SetActive(false);

            // Bottom Garage Console (y = 0..64, h = 64) — center 178px wide-open for the player's tank!
            CreatePixelSolid(vp, "BotShade", 0, 0, W, 64, new Color(0.05f, 0.05f, 0.04f, 0.88f));
            CreatePixelSolid(vp, "BotRule", 0, 64, W, 1, PanelBorder);

            _garageTankName = CreatePixelText(vp, "M1A2 SEP v2", 16, GoldColor, TextAnchor.MiddleCenter, 0, 68, W, 20, useTitleFont: false);
            _garageSpecLine = CreatePixelText(vp, "", 8, TextLight, TextAnchor.MiddleCenter, 8, 44, W - 16, 16);
            _garageMatchLine = CreatePixelText(vp, "", 8, TextMuted, TextAnchor.MiddleCenter, 8, 30, W - 16, 14);

            // Bottom Controls Row (y = 6..26, h = 20 EVEN!) — Clean Garage console: BATTLE opens Mode Selection!
            CreatePixelButton(vp, hp, "<", 6, 6, 22, 20, BtnBg, PanelBorder, TextLight, 8, () => CycleSelectedInCurrentNation(-1));
            CreatePixelButton(vp, hp, ">", 30, 6, 22, 20, BtnBg, PanelBorder, TextLight, 8, () => CycleSelectedInCurrentNation(1));
            CreatePixelButton(vp, hp, "TECH TREE [T]", 58, 6, 98, 20, BtnBg, GoldColor, GoldColor, 8, () => SetTechTreeOpen(true));
            CreatePixelButton(vp, hp, "X-RAY [X]", 160, 6, 78, 20, BtnBg, LightCyan, LightCyan, 8, () => SetXRayMode(!_xrayMode), out _btnXrayBg);
            CreatePixelButton(vp, hp, "BATTLE (ENTER)", 244, 6, 230, 20, GoldColor, GoldColor, DarkInk, 8, () => SetDeployModalOpen(true, fromTree: false), useTitleFont: true);
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
            CreatePixelButton(vp, hp, "BATTLE!", 352, 4, 118, 20, GoldColor, GoldColor, DarkInk, 8, () => SetDeployModalOpen(true, fromTree: true), useTitleFont: true);
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
                if (slot.fillImg != null && slot.vehicleId != BattleSessionState.SelectedPlayerVehicleId)
                    slot.fillImg.color = BtnHover;
            });
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ =>
            {
                if (slot.fillImg != null && slot.vehicleId != BattleSessionState.SelectedPlayerVehicleId)
                    slot.fillImg.color = BtnBg;
            });
            trigger.triggers.Add(exit);

            return slot;
        }

        private void SetXRayMode(bool on)
        {
            _xrayMode = on;
            if (_hangarXrayWrap != null) _hangarXrayWrap.SetActive(on && !_treeOpen);
            if (_btnXrayBg != null) _btnXrayBg.color = on ? new Color(0.10f, 0.28f, 0.32f, 1f) : BtnBg;
            if (showcaseTank != null) showcaseTank.gameObject.SetActive(!on || _treeOpen);
            RefreshAllUI();
        }

        private void BuildSettingsUI()
        {
            Transform vp = _settingsVisualGroup.transform;
            Transform hp = _settingsHitsGroup.transform;
            int W = renderWidth;
            int H = renderHeight;

            CreatePixelSolid(vp, "SettingsDim", 0, 0, W, H, new Color(0.03f, 0.03f, 0.03f, 0.72f));

            int cw = 240;
            int ch = 194;
            int cx = (W - cw) / 2;
            int cy = (H - ch) / 2;
            CreatePixelBox(vp, "SettingsCard", cx, cy, cw, ch, new Color(0.08f, 0.09f, 0.09f, 0.98f), LightCyan);
            CreatePixelText(vp, "SETTINGS", 8, GoldColor, TextAnchor.MiddleCenter, cx, cy + ch - 22, cw, 16, useTitleFont: true);

            // Row 1: SE (SFX) Volume [-] [SE: 100%] [+]
            CreatePixelButton(vp, hp, "-", cx + 12, cy + 148, 24, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                BattleSessionState.StepSfxVolume(-0.1f);
                PlayHangarPreviewSfx();
                RefreshSettingsLabels();
            });
            _setSfxLabel = CreatePixelButton(vp, hp, "SE: 100%", cx + 40, cy + 148, 160, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                BattleSessionState.ToggleSfxEnabled();
                PlayHangarPreviewSfx();
                RefreshSettingsLabels();
            });
            CreatePixelButton(vp, hp, "+", cx + 204, cy + 148, 24, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                BattleSessionState.StepSfxVolume(+0.1f);
                PlayHangarPreviewSfx();
                RefreshSettingsLabels();
            });

            // Row 2: BGM Volume [-] [BGM: 60%] [+]
            CreatePixelButton(vp, hp, "-", cx + 12, cy + 126, 24, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                BattleSessionState.StepBgmVolume(-0.1f);
                RefreshSettingsLabels();
            });
            _setBgmLabel = CreatePixelButton(vp, hp, "BGM: 60%", cx + 40, cy + 126, 160, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                BattleSessionState.ToggleBgmEnabled();
                RefreshSettingsLabels();
            });
            CreatePixelButton(vp, hp, "+", cx + 204, cy + 126, 24, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                BattleSessionState.StepBgmVolume(+0.1f);
                RefreshSettingsLabels();
            });

            // Row 3: Drive Controls
            _setDriveLabel = CreatePixelButton(vp, hp, "", cx + 12, cy + 104, cw - 24, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                BattleSessionState.ArcadeWASD = !BattleSessionState.ArcadeWASD;
                BattleSessionState.SaveSettings();
                RefreshSettingsLabels();
            });

            // Row 4: Enemy AI
            _setAiLabel = CreatePixelButton(vp, hp, "", cx + 12, cy + 82, cw - 24, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                BattleSessionState.AiEnabled = !BattleSessionState.AiEnabled;
                BattleSessionState.SaveSettings();
                RefreshSettingsLabels();
            });

            // Row 5: Graphics Mode (applies live in Hangar too!)
            _setGfxLabel = CreatePixelButton(vp, hp, "", cx + 12, cy + 60, cw - 24, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                BattleSessionState.PostProcessEnabled = !BattleSessionState.PostProcessEnabled;
                BattleSessionState.SaveSettings();
                _postProcess?.SetEnabled(BattleSessionState.PostProcessEnabled);
                _windowbox?.SetHd2dMode(BattleSessionState.PostProcessEnabled);
                RefreshSettingsLabels();
            });

            // Row 6: Display Scaling (applies live in Hangar too!)
            _setViewLabel = CreatePixelButton(vp, hp, "", cx + 12, cy + 38, cw - 24, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                BattleSessionState.IntegerWindowbox = !BattleSessionState.IntegerWindowbox;
                BattleSessionState.SaveSettings();
                if (_windowbox != null) _windowbox.integerScale = BattleSessionState.IntegerWindowbox;
                RefreshSettingsLabels();
            });

            // Bottom: Close
            CreatePixelButton(vp, hp, "CLOSE [ESC]", cx + 12, cy + 10, cw - 24, 20, GoldColor, GoldColor, DarkInk, 8, () =>
            {
                SetSettingsOpen(false);
            });

            RefreshSettingsLabels();
        }

        private void PlayHangarPreviewSfx()
        {
            if (_previewAudio == null || _previewSfxClip == null) return;
            if (!BattleSessionState.SfxEnabled || BattleSessionState.SfxVolume <= 0.001f) return;
            _previewAudio.Stop();
            _previewAudio.clip = _previewSfxClip;
            _previewAudio.volume = Mathf.Clamp01(0.62f * BattleSessionState.SfxVolume);
            _previewAudio.pitch = 1.0f;
            _previewAudio.Play();
        }

        private void RefreshSettingsLabels()
        {
            if (_setSfxLabel != null)
            {
                _setSfxLabel.text = BattleSessionState.FormatVolumeLabel("SE", BattleSessionState.SfxEnabled, BattleSessionState.SfxVolume);
                _setSfxLabel.color = (BattleSessionState.SfxEnabled && BattleSessionState.SfxVolume > 0.001f) ? MbtGreen : TextMuted;
            }
            if (_setBgmLabel != null)
            {
                _setBgmLabel.text = BattleSessionState.FormatVolumeLabel("BGM", BattleSessionState.BgmEnabled, BattleSessionState.BgmVolume);
                _setBgmLabel.color = (BattleSessionState.BgmEnabled && BattleSessionState.BgmVolume > 0.001f) ? LightCyan : TextMuted;
            }
            if (_setDriveLabel != null)
                _setDriveLabel.text = BattleSessionState.ArcadeWASD ? "DRIVE: ARCADE WASD" : "DRIVE: TANK STEER";
            if (_setAiLabel != null)
                _setAiLabel.text = BattleSessionState.AiEnabled ? "ENEMY AI: ON" : "ENEMY AI: OFF";
            if (_setGfxLabel != null)
                _setGfxLabel.text = BattleSessionState.PostProcessEnabled ? "GFX: HD-2D" : "GFX: RETRO";
            if (_setViewLabel != null)
                _setViewLabel.text = BattleSessionState.IntegerWindowbox ? "VIEW: INT WINDOWBOX" : "VIEW: 16:9 LETTERBOX";
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
                if (_garageTopBadge != null)
                    _garageTopBadge.text = $"{nationName} T-{sel.TierRoman} {sel.role}";
                if (_garageTankName != null)
                {
                    _garageTankName.text = sel.name.ToUpper();
                    _garageTankName.color = sel.IsSpecial ? new Color(1f, 0.88f, 0.42f) : GoldColor;
                }
                if (_garageSpecLine != null)
                    _garageSpecLine.text = $"CREW {sel.CrewCapacity}  SPD {(int)sel.speed}  TRN {(int)sel.EffectiveHullTurnSpeed}  TUR {(int)sel.EffectiveTurretTurnSpeed}  PEN {(int)sel.penetration}  RLD {sel.reload:F2}s  {sel.WeaponLabel}";
                if (_garageMatchLine != null)
                {
                    if (_xrayMode)
                    {
                        string engStr = sel.IsFrontEngine ? "FRONT ENG" : "REAR ENG";
                        string turStr = sel.IsTurretless ? "CASEMATE" : (sel.IsUnmannedTurret ? "UNMANNED TURRET" : "MANNED TURRET");
                        string gearStr = sel.IsWheeled ? $"{sel.WheelAxleCount * 2}x{sel.WheelAxleCount * 2} WHEELS" : "TRACKED";
                        _garageMatchLine.text = $"X-RAY: {engStr}  •  {turStr}  •  {gearStr}";
                        _garageMatchLine.color = LightCyan;
                    }
                    else
                    {
                        string mapTag = DesertBattleManager.GetMapDisplayName(BattleSessionState.SelectedBattleMap);
                        string modeHint = BattleSessionState.SelectedBattleMode switch
                        {
                            BattleMode.FiringRange => $"{mapTag}  •  FIRING RANGE (TEST DRIVE / 6 TARGETS)",
                            BattleMode.Battle5v5 => $"{mapTag}  •  5v5 TEAM BATTLE (TIER {sel.TierRoman})",
                            BattleMode.Squad2v2 => $"{mapTag}  •  2v2 SQUAD (TIER {sel.TierRoman})",
                            BattleMode.SurvivalWave => $"{mapTag}  •  SURVIVAL WAVE (TIER {sel.TierRoman})",
                            _ => $"{mapTag}  •  1v1 DUEL (RANDOM TIER {sel.TierRoman} RIVAL)"
                        };
                        _garageMatchLine.text = modeHint;
                        _garageMatchLine.color = BattleSessionState.SelectedBattleMode == BattleMode.FiringRange ? LightCyan : TextMuted;
                    }
                }

                if (_treeDetailTitle != null)
                    _treeDetailTitle.text = $"{sel.TierRoman}  {sel.name.ToUpper()}  ({sel.role} / {sel.WeaponLabel})";
                if (_treeDetailStats != null)
                    _treeDetailStats.text = $"SPD {(int)sel.speed}  TRN {(int)sel.EffectiveHullTurnSpeed}  TUR {(int)sel.EffectiveTurretTurnSpeed}  PEN {(int)sel.penetration}  RLD {sel.reload:F2}s  {sel.engine.ToUpper()}";
            }

            RefreshDeployModalUI();

            if (_btnMapDesertBg != null) _btnMapDesertBg.color = BattleSessionState.SelectedBattleMap == BattleMap.Desert ? BtnSelected : BtnBg;
            if (_btnMapCityBg != null) _btnMapCityBg.color = BattleSessionState.SelectedBattleMap == BattleMap.City ? BtnSelected : BtnBg;
            if (_btnMapForestBg != null) _btnMapForestBg.color = BattleSessionState.SelectedBattleMap == BattleMap.Forest ? BtnSelected : BtnBg;

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

                bool selected = vehicle.id == BattleSessionState.SelectedPlayerVehicleId;
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
            var visualBox = CreatePixelBox(visualParent, "Btn_" + label, x, y, w, h, bg, border, out Image fillImg);
            fillImgOut = fillImg;
            var txt = CreatePixelText(visualBox, label, fontSize, textColor, TextAnchor.MiddleCenter, 0, 0, w, h, useTitleFont);

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
            btn.onClick.AddListener(() => onClick?.Invoke());

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
            CleanupTitleStage();
            if (_groundHd2dMat != null) Destroy(_groundHd2dMat);
            if (_hangarBackdropTex != null) Destroy(_hangarBackdropTex);
        }
    }
}
