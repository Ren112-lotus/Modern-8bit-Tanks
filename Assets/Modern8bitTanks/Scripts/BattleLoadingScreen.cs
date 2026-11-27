using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    /// <summary>
    /// Military 8-bit tactical FCS loading screen displayed when entering the Battle scene.
    /// Uses the game's existing MilitaryUI sprites, map backdrops, and selected vehicle slice sheets.
    /// </summary>
    public sealed class BattleLoadingScreen : MonoBehaviour
    {
        private const int RefWidth = PixelWindowbox.RenderWidth;   // 480
        private const int RefHeight = PixelWindowbox.RenderHeight; // 360
        private const int ProgressSegments = 24;

        private static readonly string[] TipKeys =
        {
            "LOADING TIP 1",
            "LOADING TIP 2",
            "LOADING TIP 3",
            "LOADING TIP 4",
            "LOADING TIP 5",
            "LOADING TIP 6"
        };

        // Authentic Modern-8bit-Tanks Military FCS Palette (matches BattleHUD & HangarController)
        private static readonly Color VoidBg = new(0.05f, 0.06f, 0.05f, 1f);
        private static readonly Color BackdropDim = new(0.56f, 0.56f, 0.52f, 1f);
        private static readonly Color VignetteTint = new(0.04f, 0.05f, 0.04f, 0.42f);
        private static readonly Color PanelTint = new(0.92f, 0.94f, 0.88f, 0.97f);
        private static readonly Color HeaderTint = new(0.88f, 0.90f, 0.84f, 1f);
        private static readonly Color RadarBg = new(0.05f, 0.08f, 0.07f, 0.96f);
        private static readonly Color RadarGrid = new(0.16f, 0.27f, 0.22f, 0.55f);
        private static readonly Color RadarBorder = new(0.28f, 0.36f, 0.28f, 0.92f);
        private static readonly Color SpecCardBg = new(0.06f, 0.08f, 0.06f, 0.86f);
        private static readonly Color BarTrackBg = new(0.05f, 0.06f, 0.05f, 0.95f);
        private static readonly Color BarSegmentOff = new(0.14f, 0.17f, 0.13f, 0.90f);
        private static readonly Color AbramsGold = new(0.96f, 0.78f, 0.38f, 1f);
        private static readonly Color ReadyGreen = new(0.56f, 0.94f, 0.42f, 1f);
        private static readonly Color TextLight = new(0.93f, 0.92f, 0.86f, 1f);
        private static readonly Color TextMuted = new(0.68f, 0.70f, 0.62f, 1f);
        private static readonly Color ScanBeamColor = new(0.56f, 0.94f, 0.42f, 0.22f);

        private Canvas _canvas;
        private CanvasGroup _rootGroup;
        private RectTransform _panelRect;
        private Font _font;

        // Dynamic visuals
        private RawImage _mapBackdropImage;
        private RawImage _scanlinesImage;
        private RawImage _hazardTopImage;
        private RawImage _hazardBottomImage;
        private Image _crosshairImage;
        private RectTransform _crosshairRect;
        private Image _radarBorderImage;
        private RectTransform _scanBeamRect;

        // Live composite preview of the player's selected tank
        private RawImage _hullPreviewImage;
        private RectTransform _hullPreviewRect;
        private RawImage _turretPreviewImage;
        private RectTransform _turretPreviewRect;
        private Texture2D _hullPreviewTex;
        private Texture2D _turretPreviewTex;

        // Text elements
        private Text _headingText;
        private Text _theaterText;
        private Text _vehicleNameText;
        private Text _vehicleMetaText;
        private Text _armorStatText;
        private Text _weaponStatText;
        private readonly Text[] _checkLabels = new Text[3];
        private readonly Text[] _checkStates = new Text[3];
        private Text _statusText;
        private Text _percentText;
        private Text _tipText;

        // Segmented tactical progress bar
        private readonly Image[] _barSegments = new Image[ProgressSegments];

        // State
        private float _progress;
        private float _animTime;
        private bool _ready;
        private bool _turretless;
        private float _baseHullAngle = 90f; // SliceAngleToDir(90) = (+1, 0) -> faces right on the tactical monitor

        private VehicleDef _currentVehicle;
        private string _mapKey = "DESERT";
        private string _modeKey = "1v1 DUEL";
        private string _tipKey = "LOADING TIP 1";

        private static VehicleCatalogIndex _vehicleCatalog;
        private static readonly string[] CheckKeys = { "LOADING CHECK ARMOR", "LOADING CHECK AMMO", "LOADING CHECK FCS" };
        private static readonly float[] CheckThresholds = { 0.30f, 0.65f, 0.96f };

        public bool IsVisible => gameObject.activeSelf;

        public static BattleLoadingScreen Create()
        {
            var go = new GameObject("BattleLoadingScreen");
            DontDestroyOnLoad(go);
            var screen = go.AddComponent<BattleLoadingScreen>();
            screen.BuildUi();
            screen.gameObject.SetActive(false);
            return screen;
        }

        private void OnFontTextureRebuilt(Font f)
        {
            if (f == _font) ForceFontPointFilter(f);
        }

        private static void ForceFontPointFilter(Font f)
        {
            if (f != null && f.material != null && f.material.mainTexture != null)
                f.material.mainTexture.filterMode = FilterMode.Point;
        }

        private void BuildUi()
        {
            _font = Resources.Load<Font>("Fonts/Silkscreen-Regular")
                    ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            ForceFontPointFilter(_font);
            Font.textureRebuilt += OnFontTextureRebuilt;

            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 1000;
            _canvas.pixelPerfect = true;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            gameObject.AddComponent<GraphicRaycaster>();
            _rootGroup = gameObject.AddComponent<CanvasGroup>();
            _rootGroup.alpha = 0f;
            _rootGroup.blocksRaycasts = true;
            _rootGroup.interactable = false;

            // Full-screen solid base (blocks clicks & prevents any edge gap)
            var voidBg = CreateSolidImage(transform, "VoidBackground", VoidBg, raycast: true);
            StretchFull(voidBg.rectTransform);

            // Dimmed map terrain backdrop
            var mapGo = new GameObject("MapBackdrop", typeof(RectTransform), typeof(RawImage));
            mapGo.transform.SetParent(transform, false);
            _mapBackdropImage = mapGo.GetComponent<RawImage>();
            _mapBackdropImage.color = BackdropDim;
            _mapBackdropImage.raycastTarget = false;
            StretchFull(_mapBackdropImage.rectTransform);

            // Dark tactical vignette overlay
            var dim = CreateSolidImage(transform, "BackdropDim", VignetteTint);
            StretchFull(dim.rectTransform);

            // Subtle CRT scanline texture across screen
            if (MilitaryUiSprites.Scanlines != null)
            {
                var scanGo = new GameObject("Scanlines", typeof(RectTransform), typeof(RawImage));
                scanGo.transform.SetParent(transform, false);
                _scanlinesImage = scanGo.GetComponent<RawImage>();
                _scanlinesImage.texture = MilitaryUiSprites.Scanlines.texture;
                _scanlinesImage.color = new Color(1f, 1f, 1f, 0.16f);
                _scanlinesImage.raycastTarget = false;
                _scanlinesImage.uvRect = new Rect(0f, 0f, 32f, 45f);
                StretchFull(_scanlinesImage.rectTransform);
            }

            // Centered 480x360 retro composition container
            var root = CreateContainer(transform, "StageRoot", RefWidth, RefHeight);

            // Main armored briefing panel (380 x 244)
            const float panelW = 380f;
            const float panelH = 244f;
            var panelShadow = CreateSolidImage(root, "PanelShadow", new Color(0f, 0f, 0f, 0.55f));
            panelShadow.rectTransform.sizeDelta = new Vector2(panelW + 4f, panelH + 4f);
            panelShadow.rectTransform.anchoredPosition = new Vector2(2f, -2f);

            var panelImg = CreateSpriteImage(root, "ArmorPanel", MilitaryUiSprites.ArmorPanel, panelW, panelH, PanelTint, sliced: true);
            _panelRect = panelImg.rectTransform;
            _panelRect.anchoredPosition = Vector2.zero;

            // Top & bottom scrolling hazard stripes
            _hazardTopImage = CreateHazardStrip(_panelRect, "HazardTop", panelW - 12f, 6f, new Vector2(0f, panelH * 0.5f - 8f));
            _hazardBottomImage = CreateHazardStrip(_panelRect, "HazardBottom", panelW - 12f, 6f, new Vector2(0f, -panelH * 0.5f + 8f));

            // Header bar
            const float headerW = panelW - 16f;
            var headerImg = CreateSpriteImage(_panelRect, "HeaderBar", MilitaryUiSprites.ArmorHeaderBar, headerW, 24f, HeaderTint, sliced: true);
            var headerRt = headerImg.rectTransform;
            headerRt.anchoredPosition = new Vector2(0f, 94f);

            _crosshairImage = CreateSpriteImage(headerRt, "HeaderCrosshair", MilitaryUiSprites.IconCrosshair, 14f, 14f, AbramsGold, sliced: false);
            _crosshairRect = _crosshairImage.rectTransform;
            _crosshairRect.anchoredPosition = new Vector2(-headerW * 0.5f + 14f, 0f);

            _headingText = CreatePixelText(headerRt, "HeadingText", "", 16, TextLight, 210f, 20f, TextAnchor.MiddleLeft);
            _headingText.rectTransform.anchoredPosition = new Vector2(-headerW * 0.5f + 132f, 0f);
            AddDropShadow(_headingText.gameObject);

            _theaterText = CreatePixelText(headerRt, "TheaterText", "", 8, AbramsGold, 150f, 16f, TextAnchor.MiddleRight);
            _theaterText.rectTransform.anchoredPosition = new Vector2(headerW * 0.5f - 82f, 0f);
            AddDropShadow(_theaterText.gameObject);

            // =================================================================
            // Left Column: Tactical Vehicle Scan Monitor (176 x 122)
            // =================================================================
            const float monitorW = 176f;
            const float monitorH = 122f;
            var monitorFrame = CreateSolidImage(_panelRect, "MonitorBorder", RadarBorder);
            _radarBorderImage = monitorFrame;
            monitorFrame.rectTransform.sizeDelta = new Vector2(monitorW + 2f, monitorH + 2f);
            monitorFrame.rectTransform.anchoredPosition = new Vector2(-92f, 14f);

            var monitorClip = CreateContainer(monitorFrame.rectTransform, "MonitorClip", monitorW, monitorH);
            monitorClip.gameObject.AddComponent<RectMask2D>();

            var monitorBg = CreateSolidImage(monitorClip, "MonitorBg", RadarBg);
            StretchFull(monitorBg.rectTransform);

            // Subtle tactical radar grid lines
            for (int gx = -64; gx <= 64; gx += 32)
            {
                var vLine = CreateSolidImage(monitorClip, $"GridV_{gx}", RadarGrid);
                vLine.rectTransform.sizeDelta = new Vector2(1f, monitorH);
                vLine.rectTransform.anchoredPosition = new Vector2(gx, 0f);
            }
            for (int gy = -32; gy <= 32; gy += 32)
            {
                var hLine = CreateSolidImage(monitorClip, $"GridH_{gy}", RadarGrid);
                hLine.rectTransform.sizeDelta = new Vector2(monitorW, 1f);
                hLine.rectTransform.anchoredPosition = new Vector2(0f, gy);
            }

            // Corner HUD brackets inside monitor
            BuildCornerBrackets(monitorClip, monitorW, monitorH, AbramsGold);

            // Composite pixel-art tank preview (hull + rotating turret)
            var tankAnchor = CreateContainer(monitorClip, "TankPreviewAnchor", 128f, 128f);
            tankAnchor.anchoredPosition = new Vector2(0f, -2f);

            var hullGo = new GameObject("HullPreview", typeof(RectTransform), typeof(RawImage));
            hullGo.transform.SetParent(tankAnchor, false);
            _hullPreviewImage = hullGo.GetComponent<RawImage>();
            _hullPreviewImage.raycastTarget = false;
            _hullPreviewRect = _hullPreviewImage.rectTransform;
            _hullPreviewRect.anchorMin = new Vector2(0.5f, 0.5f);
            _hullPreviewRect.anchorMax = new Vector2(0.5f, 0.5f);
            _hullPreviewRect.pivot = new Vector2(0.5f, 0.5f);

            var turretGo = new GameObject("TurretPreview", typeof(RectTransform), typeof(RawImage));
            turretGo.transform.SetParent(tankAnchor, false);
            _turretPreviewImage = turretGo.GetComponent<RawImage>();
            _turretPreviewImage.raycastTarget = false;
            _turretPreviewRect = _turretPreviewImage.rectTransform;
            _turretPreviewRect.anchorMin = new Vector2(0.5f, 0.5f);
            _turretPreviewRect.anchorMax = new Vector2(0.5f, 0.5f);
            _turretPreviewRect.pivot = new Vector2(0.5f, 0.5f);

            // Sweeping FCS scan beam
            var beam = CreateSolidImage(monitorClip, "ScanBeam", ScanBeamColor);
            _scanBeamRect = beam.rectTransform;
            _scanBeamRect.sizeDelta = new Vector2(monitorW, 6f);
            _scanBeamRect.anchoredPosition = new Vector2(0f, monitorH * 0.5f);

            var beamLine = CreateSolidImage(_scanBeamRect, "ScanBeamCore", new Color(0.56f, 0.94f, 0.42f, 0.72f));
            beamLine.rectTransform.sizeDelta = new Vector2(monitorW, 1f);
            beamLine.rectTransform.anchoredPosition = new Vector2(0f, -2f);

            // =================================================================
            // Right Column: Vehicle Specs & Pre-Flight Checklist (176 x 122)
            // =================================================================
            const float infoW = 176f;
            const float infoH = 122f;
            var infoBorder = CreateSolidImage(_panelRect, "InfoBorder", RadarBorder);
            infoBorder.rectTransform.sizeDelta = new Vector2(infoW + 2f, infoH + 2f);
            infoBorder.rectTransform.anchoredPosition = new Vector2(92f, 14f);

            var infoBg = CreateSolidImage(infoBorder.rectTransform, "InfoBg", SpecCardBg);
            infoBg.rectTransform.sizeDelta = new Vector2(infoW, infoH);
            var infoRt = infoBg.rectTransform;

            _vehicleNameText = CreatePixelText(infoRt, "VehicleName", "", 8, AbramsGold, infoW - 14f, 14f, TextAnchor.MiddleLeft);
            _vehicleNameText.rectTransform.anchoredPosition = new Vector2(0f, 47f);
            AddDropShadow(_vehicleNameText.gameObject);

            _vehicleMetaText = CreatePixelText(infoRt, "VehicleMeta", "", 8, TextMuted, infoW - 14f, 12f, TextAnchor.MiddleLeft);
            _vehicleMetaText.rectTransform.anchoredPosition = new Vector2(0f, 33f);

            // Stat row 1: Armor
            var armorIcon = CreateSpriteImage(infoRt, "ArmorIcon", MilitaryUiSprites.IconArmor, 10f, 10f, TextMuted, sliced: false);
            armorIcon.rectTransform.anchoredPosition = new Vector2(-infoW * 0.5f + 12f, 16f);
            _armorStatText = CreatePixelText(infoRt, "ArmorStat", "", 8, TextLight, infoW - 28f, 12f, TextAnchor.MiddleLeft);
            _armorStatText.rectTransform.anchoredPosition = new Vector2(6f, 16f);

            // Stat row 2: Firepower / Weapon
            var shellIcon = CreateSpriteImage(infoRt, "ShellIcon", MilitaryUiSprites.IconShell, 10f, 10f, TextMuted, sliced: false);
            shellIcon.rectTransform.anchoredPosition = new Vector2(-infoW * 0.5f + 12f, 2f);
            _weaponStatText = CreatePixelText(infoRt, "WeaponStat", "", 8, TextLight, infoW - 28f, 12f, TextAnchor.MiddleLeft);
            _weaponStatText.rectTransform.anchoredPosition = new Vector2(6f, 2f);

            // Divider line
            var div = CreateSolidImage(infoRt, "Divider", RadarBorder);
            div.rectTransform.sizeDelta = new Vector2(infoW - 14f, 1f);
            div.rectTransform.anchoredPosition = new Vector2(0f, -10f);

            // Pre-flight system checklist (3 rows)
            for (int i = 0; i < 3; i++)
            {
                float y = -22f - i * 14f;
                _checkLabels[i] = CreatePixelText(infoRt, $"CheckLabel_{i}", "", 8, TextMuted, 118f, 12f, TextAnchor.MiddleLeft);
                _checkLabels[i].rectTransform.anchoredPosition = new Vector2(-22f, y);

                _checkStates[i] = CreatePixelText(infoRt, $"CheckState_{i}", "[..]", 8, AbramsGold, 44f, 12f, TextAnchor.MiddleRight);
                _checkStates[i].rectTransform.anchoredPosition = new Vector2(60f, y);
            }

            // =================================================================
            // Bottom Section: Tactical Segmented Progress Bar + Tip
            // =================================================================
            const float barTotalW = 360f;
            _statusText = CreatePixelText(_panelRect, "StatusText", "", 8, TextMuted, 260f, 12f, TextAnchor.MiddleLeft);
            _statusText.rectTransform.anchoredPosition = new Vector2(-50f, -59f);

            _percentText = CreatePixelText(_panelRect, "PercentText", "0%", 8, AbramsGold, 80f, 12f, TextAnchor.MiddleRight);
            _percentText.rectTransform.anchoredPosition = new Vector2(140f, -59f);

            var barBorder = CreateSolidImage(_panelRect, "BarBorder", RadarBorder);
            barBorder.rectTransform.sizeDelta = new Vector2(barTotalW + 2f, 14f);
            barBorder.rectTransform.anchoredPosition = new Vector2(0f, -74f);

            var barTrack = CreateSolidImage(barBorder.rectTransform, "BarTrack", BarTrackBg);
            barTrack.rectTransform.sizeDelta = new Vector2(barTotalW, 12f);

            // 24 crisp military segments inside the bar
            float innerW = barTotalW - 6f;
            float gap = 2f;
            float segW = Mathf.Floor((innerW - gap * (ProgressSegments - 1)) / ProgressSegments);
            float usedW = segW * ProgressSegments + gap * (ProgressSegments - 1);
            float startX = -usedW * 0.5f + segW * 0.5f;
            for (int i = 0; i < ProgressSegments; i++)
            {
                var seg = CreateSolidImage(barTrack.rectTransform, $"Seg_{i}", BarSegmentOff);
                seg.rectTransform.sizeDelta = new Vector2(segW, 8f);
                seg.rectTransform.anchoredPosition = new Vector2(startX + i * (segW + gap), 0f);
                _barSegments[i] = seg;
            }

            // Tactical tip strip
            var tipStar = CreateSpriteImage(_panelRect, "TipIcon", MilitaryUiSprites.IconStar, 10f, 10f, AbramsGold, sliced: false);
            tipStar.rectTransform.anchoredPosition = new Vector2(-172f, -97f);

            _tipText = CreatePixelText(_panelRect, "TipText", "", 8, TextLight, 342f, 18f, TextAnchor.MiddleLeft);
            _tipText.rectTransform.anchoredPosition = new Vector2(8f, -97f);
            AddDropShadow(_tipText.gameObject);
        }

        private static RawImage CreateHazardStrip(Transform parent, string name, float width, float height, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = pos;
            var img = go.GetComponent<RawImage>();
            img.texture = MilitaryUiSprites.HazardTape != null ? MilitaryUiSprites.HazardTape.texture : null;
            img.color = new Color(0.95f, 0.85f, 0.45f, 0.85f);
            img.raycastTarget = false;
            img.uvRect = new Rect(0f, 0f, width / 16f, 1f);
            return img;
        }

        private static void BuildCornerBrackets(RectTransform parent, float w, float h, Color color)
        {
            float hx = w * 0.5f - 5f;
            float hy = h * 0.5f - 5f;
            Color c = new(color.r, color.g, color.b, 0.72f);
            float[] sx = { -1f, 1f, -1f, 1f };
            float[] sy = { 1f, 1f, -1f, -1f };
            for (int i = 0; i < 4; i++)
            {
                var hBar = CreateSolidImage(parent, $"BracketH_{i}", c);
                hBar.rectTransform.sizeDelta = new Vector2(8f, 1f);
                hBar.rectTransform.anchoredPosition = new Vector2(sx[i] * (hx - 3.5f), sy[i] * hy);

                var vBar = CreateSolidImage(parent, $"BracketV_{i}", c);
                vBar.rectTransform.sizeDelta = new Vector2(1f, 8f);
                vBar.rectTransform.anchoredPosition = new Vector2(sx[i] * hx, sy[i] * (hy - 3.5f));
            }
        }

        public void Prepare(BattleSessionState sessionState, MultiplayerSessionState multiplayer)
        {
            bool online = multiplayer != null && multiplayer.IsInBattle;
            BattleMap map = online ? BattleMap.Desert : (sessionState != null ? sessionState.SelectedBattleMap : BattleMap.Desert);
            BattleMode mode = sessionState != null ? sessionState.SelectedBattleMode : BattleMode.Duel1v1;
            string vehicleId = sessionState != null ? sessionState.SelectedPlayerVehicleId : null;

            if (online && multiplayer.Connection != null && multiplayer.Connection.Room != null && multiplayer.LocalSlot >= 0)
            {
                string onlineVehicle = multiplayer.Connection.Room.GetVehicleId(multiplayer.LocalSlot);
                if (!string.IsNullOrEmpty(onlineVehicle)) vehicleId = onlineVehicle;
            }

            _currentVehicle = ResolveVehicleDef(vehicleId);
            _turretless = _currentVehicle != null && _currentVehicle.fixedGunTraverse > 0;
            _mapKey = map switch
            {
                BattleMap.City => "CITY",
                BattleMap.Forest => "FOREST",
                _ => "DESERT"
            };
            _modeKey = online ? "ONLINE BATTLE" : mode switch
            {
                BattleMode.Squad2v2 => "2v2 SQUAD",
                BattleMode.Battle5v5 => "5v5 COMPANY",
                BattleMode.SurvivalWave => "SURVIVAL",
                BattleMode.FiringRange => "FIRING RANGE",
                _ => "1v1 DUEL"
            };
            _tipKey = TipKeys[UnityEngine.Random.Range(0, TipKeys.Length)];

            ApplyMapBackdrop(map);
            BuildVehiclePreview(_currentVehicle);
            ResetAnimationState();
            RefreshLabels();
            UiText.Prepare(_canvas, RefreshLabels);
        }

        private void ApplyMapBackdrop(BattleMap map)
        {
            string resName = map switch
            {
                BattleMap.City => "Hangar/CityBackdrop",
                BattleMap.Forest => "Hangar/ForestBackdrop",
                _ => "Hangar/DesertBackdrop"
            };
            Texture2D tex = Resources.Load<Texture2D>(resName);
            _mapBackdropImage.texture = tex;
            _mapBackdropImage.color = tex != null ? BackdropDim : VoidBg;
        }

        private void BuildVehiclePreview(VehicleDef def)
        {
            ReleasePreviewTextures();
            if (def == null)
            {
                _hullPreviewImage.enabled = false;
                _turretPreviewImage.enabled = false;
                return;
            }

            var (hullSheet, turretSheet) = VehicleSliceSheets.Load(def.id);

            _hullPreviewTex = CompositeTopDownSlices(hullSheet, def.hullSlices);
            _turretPreviewTex = CompositeTopDownSlices(turretSheet, def.turretSlices);

            float maxDim = 1f;
            if (_hullPreviewTex != null) maxDim = Mathf.Max(maxDim, Mathf.Max(_hullPreviewTex.width, _hullPreviewTex.height));
            if (_turretPreviewTex != null) maxDim = Mathf.Max(maxDim, Mathf.Max(_turretPreviewTex.width, _turretPreviewTex.height));

            // Keep crisp integer 2x pixel scale when it fits inside the tactical monitor, otherwise fit cleanly
            float scale = maxDim <= 48f ? 2f : Mathf.Min(2f, 96f / maxDim);

            if (_hullPreviewTex != null)
            {
                _hullPreviewImage.texture = _hullPreviewTex;
                _hullPreviewImage.enabled = true;
                _hullPreviewRect.sizeDelta = new Vector2(_hullPreviewTex.width * scale, _hullPreviewTex.height * scale);
                _hullPreviewRect.anchoredPosition = Vector2.zero;
                _hullPreviewRect.localRotation = Quaternion.Euler(0f, 0f, _baseHullAngle);
            }
            else
            {
                _hullPreviewImage.enabled = false;
            }

            if (_turretPreviewTex != null)
            {
                _turretPreviewImage.texture = _turretPreviewTex;
                _turretPreviewImage.enabled = true;
                _turretPreviewRect.sizeDelta = new Vector2(_turretPreviewTex.width * scale, _turretPreviewTex.height * scale);
                // Hull faces right (-90 deg rotates sprite +Y toward +X), so local +Y pivot offset maps to +X
                _turretPreviewRect.anchoredPosition = new Vector2(def.hullPivotOffsetY * scale, 0f);
                _turretPreviewRect.localRotation = Quaternion.Euler(0f, 0f, _baseHullAngle);
            }
            else
            {
                _turretPreviewImage.enabled = false;
            }
        }

        private static Texture2D CompositeTopDownSlices(Texture2D sheet, int sliceCount)
        {
            if (sheet == null || !sheet.isReadable || sliceCount <= 0) return null;
            int sw = sheet.width / sliceCount;
            int sh = sheet.height;
            if (sw <= 0 || sh <= 0) return null;

            Color32[] src = sheet.GetPixels32();
            Color32[] dst = new Color32[sw * sh];
            int fullW = sheet.width;

            for (int z = 0; z < sliceCount; z++)
            {
                // Subtle height-based tactical shading so lower slices read deeper than upper armor decks
                float height01 = sliceCount > 1 ? (float)z / (sliceCount - 1) : 1f;
                float shade = Mathf.Lerp(0.76f, 1.04f, height01);
                int xBase = z * sw;

                for (int y = 0; y < sh; y++)
                {
                    int srcRow = y * fullW + xBase;
                    int dstRow = y * sw;
                    for (int x = 0; x < sw; x++)
                    {
                        Color32 c = src[srcRow + x];
                        if (c.a < 16) continue;
                        dst[dstRow + x] = new Color32(
                            (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * shade), 0, 255),
                            (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * shade), 0, 255),
                            (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * shade), 0, 255),
                            255);
                    }
                }
            }

            var tex = new Texture2D(sw, sh, TextureFormat.RGBA32, false)
            {
                name = $"{sheet.name}_LoadingComposite",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            tex.SetPixels32(dst);
            tex.Apply(false, true);
            return tex;
        }

        public async UniTask FadeInAsync(float duration = 0.18f)
        {
            gameObject.SetActive(true);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Mathf.Min(0.05f, Time.unscaledDeltaTime);
                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
                _rootGroup.alpha = 1f - (1f - t) * (1f - t);
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            _rootGroup.alpha = 1f;
        }

        public void SetProgress(float value)
        {
            _progress = Mathf.Clamp01(value);
            UpdateProgressVisuals();
        }

        public async UniTask CelebrateAndFadeOutAsync(float holdDuration = 0.22f, float fadeDuration = 0.22f)
        {
            _progress = 1f;
            _ready = true;
            UpdateProgressVisuals();
            RefreshLabels();

            float hold = 0f;
            while (hold < holdDuration)
            {
                hold += Mathf.Min(0.05f, Time.unscaledDeltaTime);
                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Mathf.Min(0.05f, Time.unscaledDeltaTime);
                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, fadeDuration));
                _rootGroup.alpha = 1f - t * t;
                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            _rootGroup.alpha = 0f;
            gameObject.SetActive(false);
        }

        public void HideImmediate()
        {
            _rootGroup.alpha = 0f;
            gameObject.SetActive(false);
        }

        private void ResetAnimationState()
        {
            _progress = 0f;
            _animTime = 0f;
            _ready = false;
            if (_radarBorderImage != null) _radarBorderImage.color = RadarBorder;
            UpdateProgressVisuals();
        }

        private void RefreshLabels()
        {
            if (_headingText != null)
            {
                if (_ready)
                {
                    _headingText.text = UiText.Get("SORTIE READY");
                    _headingText.color = ReadyGreen;
                }
                else
                {
                    int dots = (Mathf.FloorToInt(_animTime * 3.0f) % 3) + 1;
                    _headingText.text = UiText.Get("SORTIE LOADING") + new string('.', dots);
                    _headingText.color = TextLight;
                }
            }

            if (_theaterText != null)
                _theaterText.text = $"{UiText.Get(_mapKey)}  /  {UiText.Get(_modeKey)}";

            if (_currentVehicle != null)
            {
                string displayName = string.IsNullOrEmpty(_currentVehicle.name)
                    ? _currentVehicle.id.ToUpperInvariant()
                    : _currentVehicle.name.ToUpperInvariant();
                _vehicleNameText.text = displayName;
                _vehicleMetaText.text = $"{_currentVehicle.NationLabel}  /  TIER {_currentVehicle.TierRoman}  /  {_currentVehicle.role}";
                _armorStatText.text = UiText.Format("LOADING ARMOR {0}", Mathf.RoundToInt(_currentVehicle.armorFront * 100f));
                _weaponStatText.text = UiText.Format("LOADING PEN {0} / {1}", Mathf.RoundToInt(_currentVehicle.penetration), _currentVehicle.WeaponLabel);
            }
            else
            {
                _vehicleNameText.text = "";
                _vehicleMetaText.text = "";
                _armorStatText.text = "";
                _weaponStatText.text = "";
            }

            for (int i = 0; i < 3; i++)
            {
                if (_checkLabels[i] != null)
                    _checkLabels[i].text = UiText.Get(CheckKeys[i]);
            }

            if (_tipText != null)
                _tipText.text = UiText.Get(_tipKey);

            UpdateChecklistAndStatus();
        }

        private void UpdateProgressVisuals()
        {
            int litCount = Mathf.RoundToInt(_progress * ProgressSegments);
            Color litColor = _ready ? ReadyGreen : AbramsGold;
            for (int i = 0; i < ProgressSegments; i++)
            {
                if (_barSegments[i] != null)
                    _barSegments[i].color = i < litCount ? litColor : BarSegmentOff;
            }

            if (_percentText != null)
            {
                _percentText.text = $"{Mathf.RoundToInt(_progress * 100f)}%";
                _percentText.color = _ready ? ReadyGreen : AbramsGold;
            }

            if (_radarBorderImage != null)
                _radarBorderImage.color = _ready ? ReadyGreen : RadarBorder;

            UpdateChecklistAndStatus();
        }

        private void UpdateChecklistAndStatus()
        {
            int activeStage = 0;
            for (int i = 0; i < 3; i++)
            {
                bool done = _ready || _progress >= CheckThresholds[i];
                if (done) activeStage = Mathf.Min(2, i + 1);
                if (_checkStates[i] != null)
                {
                    _checkStates[i].text = done ? "[OK]" : "[..]";
                    _checkStates[i].color = done ? ReadyGreen : AbramsGold;
                }
                if (_checkLabels[i] != null)
                    _checkLabels[i].color = done ? TextLight : TextMuted;
            }

            if (_statusText != null)
            {
                _statusText.text = _ready ? UiText.Get("SORTIE READY") : UiText.Get(CheckKeys[activeStage]);
                _statusText.color = _ready ? ReadyGreen : TextMuted;
            }
        }

        private void Update()
        {
            float dt = Mathf.Min(0.05f, Time.unscaledDeltaTime);
            _animTime += dt;

            // Scroll tactical hazard tapes
            float hazardSpeed = _ready ? 0.15f : 0.75f;
            if (_hazardTopImage != null)
            {
                Rect uv = _hazardTopImage.uvRect;
                uv.x = (_animTime * hazardSpeed) % 1f;
                _hazardTopImage.uvRect = uv;
            }
            if (_hazardBottomImage != null)
            {
                Rect uv = _hazardBottomImage.uvRect;
                uv.x = (1f - (_animTime * hazardSpeed) % 1f);
                _hazardBottomImage.uvRect = uv;
            }

            // Rotate header crosshair slowly
            if (_crosshairRect != null)
            {
                float speed = _ready ? 0f : -60f;
                _crosshairRect.localRotation = Quaternion.Euler(0f, 0f, _ready ? 0f : _animTime * speed);
                if (_crosshairImage != null)
                    _crosshairImage.color = _ready ? ReadyGreen : AbramsGold;
            }

            // Sweep turret smoothly across the tactical radar monitor
            if (_turretPreviewRect != null && _turretPreviewImage.enabled)
            {
                float sweep = _turretless ? 0f : Mathf.Sin(_animTime * 1.9f) * 20f;
                if (_ready) sweep = 0f;
                _turretPreviewRect.localRotation = Quaternion.Euler(0f, 0f, _baseHullAngle + sweep);
            }

            // Sweep vertical FCS scan beam top -> bottom across the monitor
            if (_scanBeamRect != null)
            {
                float cycle = (_animTime * 0.75f) % 1f;
                float y = Mathf.Lerp(64f, -64f, cycle);
                _scanBeamRect.anchoredPosition = new Vector2(0f, y);
            }

            // Animate header ellipsis while loading
            if (!_ready && _headingText != null)
            {
                int dots = (Mathf.FloorToInt(_animTime * 3.0f) % 3) + 1;
                string expected = UiText.Get("SORTIE LOADING") + new string('.', dots);
                if (_headingText.text != expected)
                    _headingText.text = expected;
            }
        }

        private static VehicleDef ResolveVehicleDef(string vehicleId)
        {
            if (string.IsNullOrEmpty(vehicleId)) return null;
            _vehicleCatalog ??= VehicleCatalogIndex.Load();
            return _vehicleCatalog.Find(vehicleId);
        }

        private void ReleasePreviewTextures()
        {
            if (_hullPreviewTex != null)
            {
                Destroy(_hullPreviewTex);
                _hullPreviewTex = null;
            }
            if (_turretPreviewTex != null)
            {
                Destroy(_turretPreviewTex);
                _turretPreviewTex = null;
            }
        }

        private static RectTransform CreateContainer(Transform parent, string name, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        private static Image CreateSolidImage(Transform parent, string name, Color color, bool raycast = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        private static Image CreateSpriteImage(Transform parent, string name, Sprite sprite, float width, float height, Color tint, bool sliced)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.type = sliced && sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            img.pixelsPerUnitMultiplier = 1f;
            img.color = tint;
            img.raycastTarget = false;
            return img;
        }

        private Text CreatePixelText(Transform parent, string name, string content, int fontSize, Color color, float width, float height, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);

            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.text = content;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static void AddDropShadow(GameObject go)
        {
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(1f, -1f);
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private void OnDestroy()
        {
            Font.textureRebuilt -= OnFontTextureRebuilt;
            ReleasePreviewTextures();
        }
    }
}
