using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    /// <summary>
    /// Cute 8-bit storybook loading screen displayed when entering the Battle scene.
    /// Lives across scene transitions via DontDestroyOnLoad and animates using unscaled time.
    /// </summary>
    public sealed class BattleLoadingScreen : MonoBehaviour
    {
        private const int RefWidth = PixelWindowbox.RenderWidth;   // 480
        private const int RefHeight = PixelWindowbox.RenderHeight; // 360
        private const int MaxParticles = 16;

        private static readonly string[] TipKeys =
        {
            "LOADING TIP 1",
            "LOADING TIP 2",
            "LOADING TIP 3",
            "LOADING TIP 4",
            "LOADING TIP 5",
            "LOADING TIP 6"
        };

        private static readonly Color OutlineBrown = new(0.337f, 0.239f, 0.204f, 1f);
        private static readonly Color TextBrown = new(0.353f, 0.247f, 0.208f, 1f);
        private static readonly Color TextSoft = new(0.490f, 0.376f, 0.322f, 1f);
        private static readonly Color ReadyCoral = new(0.898f, 0.353f, 0.471f, 1f);
        private static readonly Color ShadowTint = new(0.337f, 0.239f, 0.204f, 0.22f);

        private struct MapTheme
        {
            public Color BackdropSky;
            public Color BackdropGround;
            public Color StageSky;
            public Color StageHorizon;
            public Color StageGround;
            public Color StageRoad;
            public Color RoadDash;
            public Color BadgeTint;
            public Color FillTint;
        }

        private sealed class FloatingParticle
        {
            public Image Image;
            public RectTransform Rect;
            public bool Active;
            public float Age;
            public float Lifetime;
            public Vector2 Position;
            public Vector2 Velocity;
            public float StartScale;
            public float EndScale;
            public float SpinSpeed;
        }

        private sealed class DriftingCloud
        {
            public RectTransform Rect;
            public float Speed;
            public float BaseY;
            public float Phase;
            public float MinX;
            public float MaxX;
        }

        private sealed class TwinkleStar
        {
            public RectTransform Rect;
            public Image Image;
            public float Phase;
            public float Speed;
            public float BaseScale;
        }

        private Canvas _canvas;
        private CanvasGroup _rootGroup;
        private RectTransform _cardRect;
        private Font _font;
        private Font _titleFont;

        // Loaded pixel sprites
        private Sprite _tankFrame0;
        private Sprite _tankFrame1;
        private Sprite _tankHappy;
        private Sprite _puffSprite;
        private Sprite _heartSprite;
        private Sprite _starSprite;
        private Sprite _cloudSprite;
        private Sprite _flagSprite;
        private Sprite _decorDesert;
        private Sprite _decorCity;
        private Sprite _decorForest;
        private Sprite _panelSprite;
        private Sprite _barFrameSprite;
        private Sprite _barFillSprite;

        // Dynamic UI references
        private Image _backdropSkyImage;
        private Image _backdropGroundImage;
        private Image _stageSkyImage;
        private Image _stageHorizonImage;
        private Image _stageGroundImage;
        private Image _stageRoadImage;
        private readonly List<Image> _roadDashImages = new();
        private Image _badgePanelImage;
        private Image _barFillImage;
        private RectTransform _barFillRect;
        private float _barMaxFillWidth;

        private Text _headingText;
        private Text _badgeText;
        private Text _percentText;
        private Text _tipText;

        private RectTransform _tankRect;
        private Image _tankImage;
        private RectTransform _flagRect;
        private RectTransform _titleHeartLeft;
        private RectTransform _titleHeartRight;
        private readonly Image[] _decorImages = new Image[3];

        private readonly List<DriftingCloud> _clouds = new();
        private readonly List<TwinkleStar> _stars = new();
        private readonly FloatingParticle[] _particles = new FloatingParticle[MaxParticles];

        // Animation state
        private float _progress;
        private float _animTime;
        private float _puffTimer;
        private int _puffCounter;
        private bool _celebrating;
        private float _celebrateTime;
        private float _tankStartX;
        private float _tankEndX;
        private float _tankBaseY;

        private string _vehicleDisplayName = "";
        private string _mapKey = "DESERT";
        private string _modeKey = "1v1 DUEL";
        private string _tipKey = "LOADING TIP 1";

        private static Dictionary<string, string> _vehicleNameCache;

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

        private void LoadAssets()
        {
            _font = Resources.Load<Font>("Fonts/Silkscreen-Regular")
                    ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _titleFont = Resources.Load<Font>("Fonts/PressStart2P-Regular") ?? _font;
            ForceFontPointFilter(_font);
            ForceFontPointFilter(_titleFont);
            Font.textureRebuilt += OnFontTextureRebuilt;

            _tankFrame0 = Resources.Load<Sprite>("Loading/ChibiTank_0");
            _tankFrame1 = Resources.Load<Sprite>("Loading/ChibiTank_1");
            _tankHappy = Resources.Load<Sprite>("Loading/ChibiTank_Happy");
            _puffSprite = Resources.Load<Sprite>("Loading/CutePuff");
            _heartSprite = Resources.Load<Sprite>("Loading/CuteHeart");
            _starSprite = Resources.Load<Sprite>("Loading/CuteStar");
            _cloudSprite = Resources.Load<Sprite>("Loading/CuteCloud");
            _flagSprite = Resources.Load<Sprite>("Loading/GoalFlag");
            _decorDesert = Resources.Load<Sprite>("Loading/CuteDecor_Desert");
            _decorCity = Resources.Load<Sprite>("Loading/CuteDecor_City");
            _decorForest = Resources.Load<Sprite>("Loading/CuteDecor_Forest");
            _panelSprite = Resources.Load<Sprite>("Loading/CutePanel");
            _barFrameSprite = Resources.Load<Sprite>("Loading/CuteBarFrame");
            _barFillSprite = Resources.Load<Sprite>("Loading/CuteBarFill");
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

        private void BuildUi()
        {
            LoadAssets();

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

            // Full-screen pastel backdrop (blocks clicks & fills any aspect ratio cleanly)
            _backdropSkyImage = CreateSolidImage(transform, "BackdropSky", Color.white, raycast: true);
            StretchFull(_backdropSkyImage.rectTransform);

            // Soft full-width pastel hill band at the bottom
            _backdropGroundImage = CreateSolidImage(transform, "BackdropGround", Color.white);
            var bgGroundRt = _backdropGroundImage.rectTransform;
            bgGroundRt.anchorMin = new Vector2(0f, 0f);
            bgGroundRt.anchorMax = new Vector2(1f, 0.28f);
            bgGroundRt.offsetMin = Vector2.zero;
            bgGroundRt.offsetMax = Vector2.zero;

            // Centered 480x360 retro composition container
            var rootRt = CreateContainer(transform, "StageRoot", RefWidth, RefHeight);

            // Ambient twinkling stars & floating clouds behind card
            BuildAmbientSky(rootRt);

            // Soft drop shadow behind main storybook card
            const float cardW = 356f;
            const float cardH = 242f;
            var cardShadow = CreateSpriteImage(rootRt, "CardShadow", _panelSprite, cardW, cardH, ShadowTint, sliced: true);
            cardShadow.rectTransform.anchoredPosition = new Vector2(4f, -4f);

            // Main rounded pastel card
            var cardImg = CreateSpriteImage(rootRt, "StorybookCard", _panelSprite, cardW, cardH, Color.white, sliced: true);
            _cardRect = cardImg.rectTransform;
            _cardRect.anchoredPosition = Vector2.zero;

            // Header row: [Heart]  DEPLOYING...  [Heart]
            _headingText = CreatePixelText(_cardRect, "HeadingText", "", 16, TextBrown, 280f, 24f, TextAnchor.MiddleCenter);
            _headingText.rectTransform.anchoredPosition = new Vector2(0f, 94f);
            AddOutlineShadow(_headingText.gameObject);

            var hl = CreateSpriteImage(_cardRect, "HeartLeft", _heartSprite, 20f, 20f, Color.white, sliced: false);
            _titleHeartLeft = hl.rectTransform;
            _titleHeartLeft.anchoredPosition = new Vector2(-132f, 95f);

            var hr = CreateSpriteImage(_cardRect, "HeartRight", _heartSprite, 20f, 20f, Color.white, sliced: false);
            _titleHeartRight = hr.rectTransform;
            _titleHeartRight.anchoredPosition = new Vector2(132f, 95f);

            // Vehicle / Map / Mode pastel pill badge
            _badgePanelImage = CreateSpriteImage(_cardRect, "InfoBadge", _barFrameSprite, 300f, 20f, Color.white, sliced: true);
            _badgePanelImage.rectTransform.anchoredPosition = new Vector2(0f, 66f);

            _badgeText = CreatePixelText(_badgePanelImage.rectTransform, "BadgeText", "", 8, TextBrown, 288f, 16f, TextAnchor.MiddleCenter);
            _badgeText.rectTransform.anchoredPosition = Vector2.zero;

            // Central cute diorama stage frame
            const float stageW = 316f;
            const float stageH = 104f;
            var stageBorder = CreateSpriteImage(_cardRect, "DioramaFrame", _barFrameSprite, stageW + 6f, stageH + 6f, Color.white, sliced: true);
            stageBorder.rectTransform.anchoredPosition = new Vector2(0f, -2f);

            var stageClip = CreateContainer(stageBorder.rectTransform, "DioramaClip", stageW, stageH);
            stageClip.anchoredPosition = Vector2.zero;
            stageClip.gameObject.AddComponent<RectMask2D>();

            // Sky inside diorama
            _stageSkyImage = CreateSolidImage(stageClip, "StageSky", Color.white);
            StretchFull(_stageSkyImage.rectTransform);

            // Soft horizon strip
            _stageHorizonImage = CreateSolidImage(stageClip, "StageHorizon", Color.white);
            var horRt = _stageHorizonImage.rectTransform;
            horRt.anchorMin = new Vector2(0f, 0f);
            horRt.anchorMax = new Vector2(1f, 0f);
            horRt.pivot = new Vector2(0.5f, 0f);
            horRt.sizeDelta = new Vector2(0f, 44f);
            horRt.anchoredPosition = Vector2.zero;

            // Ground strip
            _stageGroundImage = CreateSolidImage(stageClip, "StageGround", Color.white);
            var grRt = _stageGroundImage.rectTransform;
            grRt.anchorMin = new Vector2(0f, 0f);
            grRt.anchorMax = new Vector2(1f, 0f);
            grRt.pivot = new Vector2(0.5f, 0f);
            grRt.sizeDelta = new Vector2(0f, 28f);
            grRt.anchoredPosition = Vector2.zero;

            // Cute road track strip
            _stageRoadImage = CreateSolidImage(stageClip, "StageRoad", Color.white);
            var rdRt = _stageRoadImage.rectTransform;
            rdRt.anchorMin = new Vector2(0f, 0f);
            rdRt.anchorMax = new Vector2(1f, 0f);
            rdRt.pivot = new Vector2(0.5f, 0f);
            rdRt.sizeDelta = new Vector2(0f, 12f);
            rdRt.anchoredPosition = new Vector2(0f, 4f);

            // Dashed center markings along the road
            _roadDashImages.Clear();
            for (int i = 0; i < 16; i++)
            {
                var dash = CreateSolidImage(rdRt, $"Dash_{i}", Color.white);
                dash.rectTransform.sizeDelta = new Vector2(12f, 2f);
                dash.rectTransform.anchoredPosition = new Vector2(-142f + i * 22f, 0f);
                _roadDashImages.Add(dash);
            }

            // Mini clouds drifting inside diorama window
            AddCloud(stageClip, "StageCloud_0", -90f, 30f, 14f, -190f, 190f, 0.85f);
            AddCloud(stageClip, "StageCloud_1", 70f, 36f, 9f, -190f, 190f, 0.70f);

            // Map-themed cute props along the background edge of the road (strict integer 1x / 2x pixel scales!)
            float[] decorX = { -116f, -14f, 68f };
            float[] decorSize = { 48f, 24f, 48f };
            float[] decorY = { 24f, 34f, 24f };
            for (int i = 0; i < _decorImages.Length; i++)
            {
                float sz = decorSize[i];
                var decor = CreateSpriteImage(stageClip, $"Decor_{i}", _decorDesert, sz, sz, Color.white, sliced: false);
                decor.rectTransform.anchorMin = new Vector2(0.5f, 0f);
                decor.rectTransform.anchorMax = new Vector2(0.5f, 0f);
                decor.rectTransform.pivot = new Vector2(0.5f, 0f);
                decor.rectTransform.anchoredPosition = new Vector2(decorX[i], decorY[i]);
                _decorImages[i] = decor;
            }

            // Goal Flag standing proudly on the right side of the road
            var flagImg = CreateSpriteImage(stageClip, "GoalFlag", _flagSprite, 36f, 56f, Color.white, sliced: false);
            _flagRect = flagImg.rectTransform;
            _flagRect.anchorMin = new Vector2(0.5f, 0f);
            _flagRect.anchorMax = new Vector2(0.5f, 0f);
            _flagRect.pivot = new Vector2(0.35f, 0f);
            _flagRect.anchoredPosition = new Vector2(128f, 12f);

            // Particle layer (exhaust puffs, floating hearts & stars)
            var particleLayer = CreateContainer(stageClip, "Particles", stageW, stageH);
            particleLayer.anchoredPosition = Vector2.zero;
            for (int i = 0; i < MaxParticles; i++)
            {
                var pImg = CreateSpriteImage(particleLayer, $"Particle_{i}", _puffSprite, 16f, 16f, Color.white, sliced: false);
                pImg.gameObject.SetActive(false);
                _particles[i] = new FloatingParticle
                {
                    Image = pImg,
                    Rect = pImg.rectTransform
                };
            }

            // Hero Chibi Tank (72x56 = crisp 2x integer scale of 36x28)
            _tankImage = CreateSpriteImage(stageClip, "ChibiTank", _tankFrame0, 72f, 56f, Color.white, sliced: false);
            _tankRect = _tankImage.rectTransform;
            _tankRect.anchorMin = new Vector2(0.5f, 0f);
            _tankRect.anchorMax = new Vector2(0.5f, 0f);
            _tankRect.pivot = new Vector2(0.5f, 0f);
            _tankStartX = -114f;
            _tankEndX = 96f;
            _tankBaseY = 8f;
            _tankRect.anchoredPosition = new Vector2(_tankStartX, _tankBaseY);

            // Candy progress bar + percentage readout
            const float barW = 276f;
            const float barH = 18f;
            var barFrame = CreateSpriteImage(_cardRect, "ProgressBarFrame", _barFrameSprite, barW, barH, Color.white, sliced: true);
            barFrame.rectTransform.anchoredPosition = new Vector2(-20f, -72f);

            _barMaxFillWidth = barW - 8f;
            _barFillImage = CreateSpriteImage(barFrame.rectTransform, "ProgressBarFill", _barFillSprite, 0f, barH - 6f, Color.white, sliced: true);
            _barFillRect = _barFillImage.rectTransform;
            _barFillRect.anchorMin = new Vector2(0f, 0.5f);
            _barFillRect.anchorMax = new Vector2(0f, 0.5f);
            _barFillRect.pivot = new Vector2(0f, 0.5f);
            _barFillRect.anchoredPosition = new Vector2(4f, 0f);

            _percentText = CreatePixelText(_cardRect, "PercentText", "0%", 8, TextBrown, 40f, 16f, TextAnchor.MiddleRight);
            _percentText.rectTransform.anchoredPosition = new Vector2(138f, -72f);

            // Bottom cute tip row
            var tipStar = CreateSpriteImage(_cardRect, "TipStar", _starSprite, 14f, 14f, Color.white, sliced: false);
            tipStar.rectTransform.anchoredPosition = new Vector2(-148f, -98f);

            _tipText = CreatePixelText(_cardRect, "TipText", "", 8, TextSoft, 286f, 20f, TextAnchor.MiddleLeft);
            _tipText.rectTransform.anchoredPosition = new Vector2(10f, -98f);
        }

        private void BuildAmbientSky(RectTransform root)
        {
            // Big fluffy clouds floating across the outer pastel sky
            AddCloud(root, "BgCloud_0", -170f, 134f, 12f, -290f, 290f, 1.5f);
            AddCloud(root, "BgCloud_1", 120f, 144f, 8f, -290f, 290f, 1.25f);
            AddCloud(root, "BgCloud_2", -30f, -142f, 10f, -290f, 290f, 1.15f);

            // Twinkling pixel stars around the card corners
            Vector2[] starPositions =
            {
                new(-206f, 122f),
                new(208f, 114f),
                new(-214f, -44f),
                new(212f, -66f),
                new(-182f, -138f),
                new(192f, -136f)
            };
            for (int i = 0; i < starPositions.Length; i++)
            {
                var starImg = CreateSpriteImage(root, $"BgStar_{i}", _starSprite, 16f, 16f, Color.white, sliced: false);
                starImg.rectTransform.anchoredPosition = starPositions[i];
                _stars.Add(new TwinkleStar
                {
                    Rect = starImg.rectTransform,
                    Image = starImg,
                    Phase = i * 1.15f,
                    Speed = 2.6f + (i % 3) * 0.7f,
                    BaseScale = (i % 2 == 0) ? 1.0f : 0.75f
                });
            }
        }

        private void AddCloud(RectTransform parent, string name, float startX, float y, float speed, float minX, float maxX, float scale)
        {
            var img = CreateSpriteImage(parent, name, _cloudSprite, Mathf.Round(40f * scale), Mathf.Round(20f * scale),
                new Color(1f, 1f, 1f, 0.92f), sliced: false);
            img.rectTransform.anchoredPosition = new Vector2(startX, y);
            _clouds.Add(new DriftingCloud
            {
                Rect = img.rectTransform,
                Speed = speed,
                BaseY = y,
                Phase = startX * 0.03f,
                MinX = minX,
                MaxX = maxX
            });
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

            _vehicleDisplayName = ResolveVehicleName(vehicleId);
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

            ApplyMapTheme(map);
            ResetAnimationState();
            RefreshLabels();
            UiText.Prepare(_canvas, RefreshLabels);
        }

        public async UniTask FadeInAsync(float duration = 0.22f)
        {
            gameObject.SetActive(true);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Mathf.Min(0.05f, Time.unscaledDeltaTime);
                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
                float ease = 1f - (1f - t) * (1f - t);
                _rootGroup.alpha = ease;
                float s = Mathf.Lerp(0.92f, 1.0f, ease);
                _cardRect.localScale = new Vector3(s, s, 1f);
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            _rootGroup.alpha = 1f;
            _cardRect.localScale = Vector3.one;
        }

        public void SetProgress(float value)
        {
            _progress = Mathf.Clamp01(value);
            UpdateProgressVisuals();
        }

        public async UniTask CelebrateAndFadeOutAsync(float holdDuration = 0.24f, float fadeDuration = 0.24f)
        {
            _progress = 1f;
            _celebrating = true;
            _celebrateTime = 0f;
            UpdateProgressVisuals();
            RefreshLabels();
            SpawnCelebrationBurst();

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
            _puffTimer = 0f;
            _puffCounter = 0;
            _celebrating = false;
            _celebrateTime = 0f;

            for (int i = 0; i < _particles.Length; i++)
            {
                _particles[i].Active = false;
                _particles[i].Image.gameObject.SetActive(false);
            }

            if (_tankImage != null && _tankFrame0 != null)
                _tankImage.sprite = _tankFrame0;
            if (_tankRect != null)
            {
                _tankRect.anchoredPosition = new Vector2(_tankStartX, _tankBaseY);
                _tankRect.localRotation = Quaternion.identity;
                _tankRect.localScale = Vector3.one;
            }
            if (_flagRect != null)
                _flagRect.localRotation = Quaternion.identity;

            UpdateProgressVisuals();
        }

        private void ApplyMapTheme(BattleMap map)
        {
            MapTheme theme = map switch
            {
                BattleMap.City => new MapTheme
                {
                    BackdropSky = new Color(0.82f, 0.89f, 0.98f, 1f),
                    BackdropGround = new Color(0.75f, 0.80f, 0.90f, 1f),
                    StageSky = new Color(0.84f, 0.92f, 1.00f, 1f),
                    StageHorizon = new Color(0.96f, 0.88f, 0.92f, 1f),
                    StageGround = new Color(0.76f, 0.83f, 0.72f, 1f),
                    StageRoad = new Color(0.68f, 0.69f, 0.74f, 1f),
                    RoadDash = new Color(0.99f, 0.96f, 0.88f, 1f),
                    BadgeTint = new Color(0.92f, 0.95f, 1.00f, 1f),
                    FillTint = Color.white
                },
                BattleMap.Forest => new MapTheme
                {
                    BackdropSky = new Color(0.84f, 0.96f, 0.92f, 1f),
                    BackdropGround = new Color(0.71f, 0.89f, 0.72f, 1f),
                    StageSky = new Color(0.86f, 0.97f, 0.96f, 1f),
                    StageHorizon = new Color(0.78f, 0.94f, 0.80f, 1f),
                    StageGround = new Color(0.64f, 0.86f, 0.62f, 1f),
                    StageRoad = new Color(0.87f, 0.77f, 0.62f, 1f),
                    RoadDash = new Color(0.99f, 0.96f, 0.86f, 1f),
                    BadgeTint = new Color(0.91f, 0.99f, 0.92f, 1f),
                    FillTint = Color.white
                },
                _ => new MapTheme // Desert: warm peach cream & sunny sand
                {
                    BackdropSky = new Color(1.00f, 0.92f, 0.85f, 1f),
                    BackdropGround = new Color(0.96f, 0.84f, 0.66f, 1f),
                    StageSky = new Color(1.00f, 0.94f, 0.84f, 1f),
                    StageHorizon = new Color(0.99f, 0.86f, 0.70f, 1f),
                    StageGround = new Color(0.95f, 0.81f, 0.59f, 1f),
                    StageRoad = new Color(0.86f, 0.71f, 0.52f, 1f),
                    RoadDash = new Color(1.00f, 0.96f, 0.86f, 1f),
                    BadgeTint = new Color(1.00f, 0.96f, 0.88f, 1f),
                    FillTint = Color.white
                }
            };

            _backdropSkyImage.color = theme.BackdropSky;
            _backdropGroundImage.color = theme.BackdropGround;
            _stageSkyImage.color = theme.StageSky;
            _stageHorizonImage.color = theme.StageHorizon;
            _stageGroundImage.color = theme.StageGround;
            _stageRoadImage.color = theme.StageRoad;
            _badgePanelImage.color = theme.BadgeTint;
            _barFillImage.color = theme.FillTint;

            for (int i = 0; i < _roadDashImages.Count; i++)
                _roadDashImages[i].color = theme.RoadDash;

            Sprite decorSprite = map switch
            {
                BattleMap.City => _decorCity,
                BattleMap.Forest => _decorForest,
                _ => _decorDesert
            };
            for (int i = 0; i < _decorImages.Length; i++)
            {
                if (_decorImages[i] != null && decorSprite != null)
                    _decorImages[i].sprite = decorSprite;
            }
        }

        private void RefreshLabels()
        {
            if (_headingText != null)
            {
                if (_celebrating)
                {
                    _headingText.text = UiText.Get("SORTIE READY");
                    _headingText.color = ReadyCoral;
                }
                else
                {
                    int dots = (Mathf.FloorToInt(_animTime * 3.2f) % 3) + 1;
                    _headingText.text = UiText.Get("SORTIE LOADING") + new string('.', dots);
                    _headingText.color = TextBrown;
                }
            }

            if (_badgeText != null)
            {
                string mapText = UiText.Get(_mapKey);
                string modeText = UiText.Get(_modeKey);
                _badgeText.text = string.IsNullOrEmpty(_vehicleDisplayName)
                    ? $"{mapText}  /  {modeText}"
                    : $"{_vehicleDisplayName}  /  {mapText}  /  {modeText}";
            }

            if (_tipText != null)
                _tipText.text = UiText.Get(_tipKey);
        }

        private void UpdateProgressVisuals()
        {
            if (_barFillRect != null)
            {
                float fillWidth = _progress <= 0.005f ? 0f : Mathf.Max(8f, _barMaxFillWidth * _progress);
                _barFillRect.sizeDelta = new Vector2(fillWidth, _barFillRect.sizeDelta.y);
            }
            if (_percentText != null)
                _percentText.text = $"{Mathf.RoundToInt(_progress * 100f)}%";
        }

        private void Update()
        {
            float dt = Mathf.Min(0.05f, Time.unscaledDeltaTime);
            _animTime += dt;

            // Animate drifting clouds
            for (int i = 0; i < _clouds.Count; i++)
            {
                var c = _clouds[i];
                Vector2 pos = c.Rect.anchoredPosition;
                pos.x += c.Speed * dt;
                if (pos.x > c.MaxX) pos.x = c.MinX;
                pos.y = c.BaseY + Mathf.Sin(_animTime * 1.4f + c.Phase) * 2.0f;
                c.Rect.anchoredPosition = pos;
            }

            // Animate twinkling background stars
            for (int i = 0; i < _stars.Count; i++)
            {
                var s = _stars[i];
                float pulse = 0.72f + 0.32f * Mathf.Sin(_animTime * s.Speed + s.Phase);
                float sc = s.BaseScale * pulse;
                s.Rect.localScale = new Vector3(sc, sc, 1f);
                s.Rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(_animTime * 1.8f + s.Phase) * 14f);
            }

            // Animate header hearts bobbing cheerfully
            if (_titleHeartLeft != null && _titleHeartRight != null)
            {
                float hBob = Mathf.Sin(_animTime * 5.2f) * 2.5f;
                float hScale = 1f + Mathf.Sin(_animTime * 5.2f) * 0.09f;
                _titleHeartLeft.anchoredPosition = new Vector2(-132f, 95f + hBob);
                _titleHeartRight.anchoredPosition = new Vector2(132f, 95f - hBob);
                _titleHeartLeft.localScale = new Vector3(hScale, hScale, 1f);
                _titleHeartRight.localScale = new Vector3(hScale, hScale, 1f);
            }

            // Gently scroll road dashes while tank is marching
            if (!_celebrating && _roadDashImages.Count > 0)
            {
                float dashShift = (_animTime * 26f) % 22f;
                for (int i = 0; i < _roadDashImages.Count; i++)
                    _roadDashImages[i].rectTransform.anchoredPosition = new Vector2(-142f + i * 22f - dashShift, 0f);
            }

            // Animate hero chibi tank!
            UpdateTankAnimation(dt);

            // Animate floating exhaust puffs, hearts & stars
            UpdateParticles(dt);

            // Animated loading ellipsis dots
            if (!_celebrating && _headingText != null)
            {
                int dots = (Mathf.FloorToInt(_animTime * 3.2f) % 3) + 1;
                string expected = UiText.Get("SORTIE LOADING") + new string('.', dots);
                if (_headingText.text != expected)
                    _headingText.text = expected;
            }
        }

        private void UpdateTankAnimation(float dt)
        {
            if (_tankRect == null || _tankImage == null) return;

            float x = Mathf.Lerp(_tankStartX, _tankEndX, _progress);
            if (_celebrating)
            {
                _celebrateTime += dt;
                if (_tankHappy != null) _tankImage.sprite = _tankHappy;

                // Joyful little hop on reaching the goal flag!
                float hopPhase = Mathf.Clamp01(_celebrateTime / 0.22f);
                float hopY = Mathf.Sin(hopPhase * Mathf.PI) * 12f;
                float squashX = 1f + Mathf.Sin(hopPhase * Mathf.PI * 2f) * 0.06f;
                float squashY = 1f - Mathf.Sin(hopPhase * Mathf.PI * 2f) * 0.06f;
                _tankRect.anchoredPosition = new Vector2(x, _tankBaseY + hopY);
                _tankRect.localScale = new Vector3(squashX, squashY, 1f);
                _tankRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(hopPhase * Mathf.PI) * 5f);

                if (_flagRect != null)
                    _flagRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(_celebrateTime * 26f) * 8f);
                return;
            }

            // Chugging tread cycle + bouncy suspension
            bool altFrame = (Mathf.FloorToInt(_animTime * 7.5f) % 2) == 1;
            Sprite desired = altFrame ? (_tankFrame1 ?? _tankFrame0) : _tankFrame0;
            if (desired != null && _tankImage.sprite != desired)
                _tankImage.sprite = desired;

            float bounce = Mathf.Abs(Mathf.Sin(_animTime * 11.5f)) * 2.4f;
            float tilt = Mathf.Sin(_animTime * 11.5f) * 2.2f;
            _tankRect.anchoredPosition = new Vector2(x, _tankBaseY + bounce);
            _tankRect.localRotation = Quaternion.Euler(0f, 0f, tilt);
            _tankRect.localScale = Vector3.one;

            if (_flagRect != null)
                _flagRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(_animTime * 3.5f) * 3f);

            // Spawn cute exhaust puffs (and occasional hearts/stars!) behind the tank
            _puffTimer += dt;
            if (_puffTimer >= 0.15f)
            {
                _puffTimer = 0f;
                _puffCounter++;
                Vector2 exhaustPos = new(x - 32f, -24f + bounce);
                if (_puffCounter % 4 == 0)
                {
                    Sprite cute = (_puffCounter % 8 == 0) ? _starSprite : _heartSprite;
                    SpawnParticle(cute, exhaustPos + new Vector2(0f, 8f), new Vector2(-18f, 32f), 0.72f, 0.75f, 1.15f, 45f);
                }
                else
                {
                    SpawnParticle(_puffSprite, exhaustPos, new Vector2(-24f, 18f + UnityEngine.Random.Range(-4f, 6f)),
                        0.62f, 0.65f, 1.35f, UnityEngine.Random.Range(-30f, 30f));
                }
            }
        }

        private void SpawnCelebrationBurst()
        {
            float x = _tankEndX;
            SpawnParticle(_heartSprite, new Vector2(x + 4f, 16f), new Vector2(-16f, 52f), 0.55f, 0.9f, 1.35f, -35f);
            SpawnParticle(_starSprite, new Vector2(x + 12f, 18f), new Vector2(18f, 56f), 0.55f, 0.9f, 1.35f, 65f);
            SpawnParticle(_starSprite, new Vector2(x - 6f, 14f), new Vector2(-26f, 42f), 0.55f, 0.75f, 1.15f, -55f);
            SpawnParticle(_heartSprite, new Vector2(x + 22f, 12f), new Vector2(26f, 44f), 0.55f, 0.75f, 1.15f, 40f);
        }

        private void SpawnParticle(Sprite sprite, Vector2 pos, Vector2 vel, float lifetime, float startScale, float endScale, float spinSpeed)
        {
            if (sprite == null) return;
            for (int i = 0; i < _particles.Length; i++)
            {
                var p = _particles[i];
                if (p.Active) continue;
                p.Active = true;
                p.Age = 0f;
                p.Lifetime = lifetime;
                p.Position = pos;
                p.Velocity = vel;
                p.StartScale = startScale;
                p.EndScale = endScale;
                p.SpinSpeed = spinSpeed;
                p.Image.sprite = sprite;
                p.Image.color = Color.white;
                p.Rect.anchoredPosition = pos;
                p.Rect.localScale = new Vector3(startScale, startScale, 1f);
                p.Rect.localRotation = Quaternion.identity;
                p.Image.gameObject.SetActive(true);
                return;
            }
        }

        private void UpdateParticles(float dt)
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                var p = _particles[i];
                if (!p.Active) continue;
                p.Age += dt;
                if (p.Age >= p.Lifetime)
                {
                    p.Active = false;
                    p.Image.gameObject.SetActive(false);
                    continue;
                }

                float t = p.Age / p.Lifetime;
                p.Position += p.Velocity * dt;
                p.Velocity *= Mathf.Pow(0.45f, dt);
                p.Rect.anchoredPosition = p.Position;

                float scale = Mathf.Lerp(p.StartScale, p.EndScale, t);
                p.Rect.localScale = new Vector3(scale, scale, 1f);
                p.Rect.Rotate(0f, 0f, p.SpinSpeed * dt);

                Color c = p.Image.color;
                c.a = 1f - t * t;
                p.Image.color = c;
            }
        }

        private static string ResolveVehicleName(string vehicleId)
        {
            if (string.IsNullOrEmpty(vehicleId)) return "";
            if (_vehicleNameCache == null)
            {
                _vehicleNameCache = new Dictionary<string, string>(StringComparer.Ordinal);
                VehicleCatalog catalog = MetaLoader.LoadVehicleCatalog();
                if (catalog != null && catalog.vehicles != null)
                {
                    foreach (var v in catalog.vehicles)
                    {
                        if (v != null && !string.IsNullOrEmpty(v.id))
                            _vehicleNameCache[v.id] = string.IsNullOrEmpty(v.name) ? v.id.ToUpperInvariant() : v.name.ToUpperInvariant();
                    }
                }
            }
            return _vehicleNameCache.TryGetValue(vehicleId, out string name) ? name : vehicleId.ToUpperInvariant();
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

        private static void AddOutlineShadow(GameObject go)
        {
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(1f, 1f, 1f, 0.9f);
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
        }
    }
}
