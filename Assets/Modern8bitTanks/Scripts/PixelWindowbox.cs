using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    /// <summary>
    /// HD-2D Hybrid Dual-Layer Pixel-Perfect Letterbox / Windowbox Renderer:
    ///   1. Game World -> _gameRt (HD-2D 1440x810 DefaultHDR with Point-filtered pixel sprites,
    ///      silky URP HDR Bloom, dynamic combat lighting, & Tilt-Shift Miniature Bokeh compositor!)
    ///   2. Pixel-Perfect uGUI HUD (Layer 5 UI) -> _hudRt (exact 480x270 ARGB32 Point, zero blur!)
    /// Toggleable cleanly between HD-2D mode and pure 480x270 Retro mode at runtime!
    /// </summary>
    public class PixelWindowbox : MonoBehaviour
    {
        public int renderWidth = 480;
        public int renderHeight = 270;
        public int hd2dScale = 3; // 480x270 * 3 = 1440x810 HD-2D HDR world buffer
        public bool hd2dEnabled = true;
        public bool integerScale = false; // Default: 16:9 Letterbox
        public Color matteColor = new Color(0.05f, 0.05f, 0.04f, 1f);
        public Color borderColor = new Color(0.28f, 0.22f, 0.14f, 1f);

        private Camera _gameCam;
        private Camera _hudCam;
        private Camera _displayClearCam;
        private RenderTexture _gameRt;
        private RenderTexture _hudRt;

        private Canvas _displayCanvas;
        private RectTransform _frameRect;
        private RectTransform _gameImageRect;
        private RawImage _gameRawImage;
        private RectTransform _hudImageRect;
        private RectTransform _hitOverlayRect;
        private Material _hd2dCompositeMat;

        private static readonly int TiltShiftStrengthId = Shader.PropertyToID("_TiltShiftStrength");
        private static readonly int FocusCenterId = Shader.PropertyToID("_FocusCenter");
        private static readonly int FocusWidthId = Shader.PropertyToID("_FocusWidth");
        private static readonly int TexelSizeId = Shader.PropertyToID("_TexelSize");
        private static readonly int Hd2dEnabledId = Shader.PropertyToID("_Hd2dEnabled");
        private static readonly int Hd2dTimeId = Shader.PropertyToID("_Hd2dTime");

        public Rect ScreenDisplayRect { get; private set; }
        public Camera HudCamera => _hudCam;
        public RectTransform HitOverlayRect => _hitOverlayRect;
        public RenderTexture TargetTexture => _gameRt;
        public int GameRenderScale => hd2dEnabled ? Mathf.Clamp(hd2dScale, 1, 4) : 1;

        public void Initialize(Camera gameCamera, int width = 480, int height = 270, bool useIntegerScale = false, bool useHd2d = true)
        {
            _gameCam = gameCamera;
            renderWidth = Mathf.Max(160, width);
            renderHeight = Mathf.Max(90, height);
            integerScale = useIntegerScale;
            hd2dEnabled = useHd2d;

            BuildHd2dCompositeMaterial();
            CreateRenderTextures();
            CreateHudCamera();
            CreateDisplayClearCamera();
            CreateLetterboxDisplayCanvas();
            ApplyHd2dPresentation();
            UpdateLayout();
        }

        private void BuildHd2dCompositeMaterial()
        {
            if (_hd2dCompositeMat != null) return;
            Shader sh = Resources.Load<Shader>("Shaders/URPHd2dScreenComposite")
                        ?? Shader.Find("Modern8bitTanks/URPHd2dScreenComposite");
            if (sh != null)
            {
                _hd2dCompositeMat = new Material(sh);
                _hd2dCompositeMat.SetFloat(TiltShiftStrengthId, 1.0f);
                _hd2dCompositeMat.SetFloat(FocusCenterId, 0.50f);
                _hd2dCompositeMat.SetFloat(FocusWidthId, 0.25f);
            }
        }

        public void SetHd2dMode(bool enabled)
        {
            if (hd2dEnabled == enabled && _gameRt != null)
            {
                ApplyHd2dPresentation();
                return;
            }
            hd2dEnabled = enabled;
            RecreateGameRenderTexture();
            ApplyHd2dPresentation();
        }

        public void SetTiltShiftParams(float strength, float focusCenter = 0.50f, float focusWidth = 0.25f)
        {
            if (_hd2dCompositeMat == null) return;
            _hd2dCompositeMat.SetFloat(TiltShiftStrengthId, strength);
            _hd2dCompositeMat.SetFloat(FocusCenterId, focusCenter);
            _hd2dCompositeMat.SetFloat(FocusWidthId, focusWidth);
        }

        private void ApplyHd2dPresentation()
        {
            Shader.SetGlobalFloat(Hd2dEnabledId, hd2dEnabled ? 1f : 0f);
            if (_gameRawImage == null) return;
            _gameRawImage.texture = _gameRt;
            _gameRawImage.material = (hd2dEnabled && _hd2dCompositeMat != null) ? _hd2dCompositeMat : null;
        }

        private void CreateRenderTextures()
        {
            ReleaseTextures();
            RecreateGameRenderTexture();

            // Crisp ARGB32 RenderTexture for Pixel-Perfect HUD (480x270 exact pixel grid — NEVER blurs!)
            _hudRt = new RenderTexture(renderWidth, renderHeight, 16, RenderTextureFormat.ARGB32)
            {
                name = "Modern8bitTanks_PixelHUD_RT",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            _hudRt.Create();
        }

        private void RecreateGameRenderTexture()
        {
            if (_gameRt != null)
            {
                if (_gameCam != null && _gameCam.targetTexture == _gameRt) _gameCam.targetTexture = null;
                _gameRt.Release();
                Destroy(_gameRt);
                _gameRt = null;
            }

            int scale = GameRenderScale;
            int gw = renderWidth * scale;
            int gh = renderHeight * scale;

            // HD-2D HDR RenderTexture for Game World + URP Bloom / Lighting / Tilt-Shift
            _gameRt = new RenderTexture(gw, gh, 24, RenderTextureFormat.DefaultHDR)
            {
                name = hd2dEnabled ? "Modern8bitTanks_HD2D_GameHDR_RT" : "Modern8bitTanks_Retro_GameHDR_RT",
                filterMode = hd2dEnabled ? FilterMode.Bilinear : FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            _gameRt.Create();

            if (_hd2dCompositeMat != null)
                _hd2dCompositeMat.SetVector(TexelSizeId, new Vector4(1f / gw, 1f / gh, gw, gh));

            if (_gameCam != null)
            {
                _gameCam.targetTexture = _gameRt;
                _gameCam.allowMSAA = false;
                _gameCam.allowHDR = true;
                // Exclude Layer 5 (UI) from game camera so HUD renders strictly via _hudCam into _hudRt
                _gameCam.cullingMask &= ~(1 << 5);
            }
            if (_gameRawImage != null)
                _gameRawImage.texture = _gameRt;
        }

        private void CreateHudCamera()
        {
            var hudCamGo = new GameObject("PixelHudCamera");
            hudCamGo.transform.SetParent(transform, false);
            hudCamGo.transform.position = new Vector3(0f, 0f, -100f);

            _hudCam = hudCamGo.AddComponent<Camera>();
            _hudCam.orthographic = true;
            _hudCam.orthographicSize = renderHeight * 0.5f;
            _hudCam.nearClipPlane = 0.1f;
            _hudCam.farClipPlane = 200f;
            _hudCam.clearFlags = CameraClearFlags.SolidColor;
            _hudCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _hudCam.cullingMask = 1 << 5; // Layer 5 (UI) only
            _hudCam.depth = 10;
            _hudCam.allowMSAA = false;
            _hudCam.allowHDR = false;
            _hudCam.targetTexture = _hudRt;

            var urpData = hudCamGo.AddComponent<UniversalAdditionalCameraData>();
            urpData.renderPostProcessing = false;
            urpData.antialiasing = AntialiasingMode.None;
            urpData.renderShadows = false;
        }

        private void CreateDisplayClearCamera()
        {
            var clearGo = new GameObject("DisplayMatteCamera");
            clearGo.transform.SetParent(transform, false);
            _displayClearCam = clearGo.AddComponent<Camera>();
            _displayClearCam.clearFlags = CameraClearFlags.SolidColor;
            _displayClearCam.backgroundColor = matteColor;
            _displayClearCam.cullingMask = 0;
            _displayClearCam.depth = -100;
            _displayClearCam.orthographic = true;
            _displayClearCam.allowMSAA = false;
            _displayClearCam.allowHDR = false;
        }

        private void CreateLetterboxDisplayCanvas()
        {
            var canvasGo = new GameObject("PixelLetterboxCanvas");
            canvasGo.transform.SetParent(transform, false);

            _displayCanvas = canvasGo.AddComponent<Canvas>();
            _displayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _displayCanvas.sortingOrder = 0;
            _displayCanvas.pixelPerfect = false;
            canvasGo.AddComponent<GraphicRaycaster>();

            // 1. Fullscreen dark letterbox matte
            var bgGo = new GameObject("MatteBackdrop");
            bgGo.transform.SetParent(canvasGo.transform, false);
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.color = matteColor;
            bgImg.raycastTarget = false;
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            // 2. Subtle 2px military bezel
            var frameGo = new GameObject("LetterboxFrame");
            frameGo.transform.SetParent(canvasGo.transform, false);
            var frameImg = frameGo.AddComponent<Image>();
            frameImg.color = borderColor;
            frameImg.raycastTarget = false;
            _frameRect = frameGo.GetComponent<RectTransform>();
            CenterRect(_frameRect);

            // 3. Game HDR RenderTexture (HD-2D Tilt-Shift Composite or Retro Point)
            var gameRawGo = new GameObject("PixelGameRawImage");
            gameRawGo.transform.SetParent(canvasGo.transform, false);
            _gameRawImage = gameRawGo.AddComponent<RawImage>();
            _gameRawImage.texture = _gameRt;
            _gameRawImage.color = Color.white;
            _gameRawImage.raycastTarget = false;
            _gameImageRect = gameRawGo.GetComponent<RectTransform>();
            CenterRect(_gameImageRect);

            // 4. Pixel-Perfect HUD RenderTexture stacked cleanly over game (Point-filtered!)
            var hudRawGo = new GameObject("PixelHudRawImage");
            hudRawGo.transform.SetParent(canvasGo.transform, false);
            var hudRaw = hudRawGo.AddComponent<RawImage>();
            hudRaw.texture = _hudRt;
            hudRaw.color = Color.white;
            hudRaw.raycastTarget = false;
            _hudImageRect = hudRawGo.GetComponent<RectTransform>();
            CenterRect(_hudImageRect);

            // 5. Matched transparent HitOverlay for exact click hitboxes on Pixel-Perfect UI buttons!
            var hitGo = new GameObject("HudHitOverlay");
            hitGo.transform.SetParent(canvasGo.transform, false);
            _hitOverlayRect = hitGo.AddComponent<RectTransform>();
            CenterRect(_hitOverlayRect);
        }

        private static void CenterRect(RectTransform rt)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }

        private void LateUpdate()
        {
            Shader.SetGlobalFloat(Hd2dTimeId, Time.time);
            Shader.SetGlobalFloat(Hd2dEnabledId, hd2dEnabled ? 1f : 0f);
            UpdateLayout();
        }

        public void UpdateLayout()
        {
            if (_gameImageRect == null || Screen.width <= 0 || Screen.height <= 0) return;

            float sw = Screen.width;
            float sh = Screen.height;
            float fitScale = Mathf.Min(sw / renderWidth, sh / renderHeight);
            float finalScale = (integerScale && fitScale >= 1f)
                ? Mathf.Floor(fitScale)
                : Mathf.Max(0.1f, fitScale);

            float dispW = Mathf.Round(renderWidth * finalScale);
            float dispH = Mathf.Round(renderHeight * finalScale);
            Vector2 sz = new Vector2(dispW, dispH);

            _gameImageRect.sizeDelta = sz;
            _hudImageRect.sizeDelta = sz;
            _hitOverlayRect.sizeDelta = sz;
            _frameRect.sizeDelta = new Vector2(dispW + 4f, dispH + 4f);

            float xMin = (sw - dispW) * 0.5f;
            float yMin = (sh - dispH) * 0.5f;
            ScreenDisplayRect = new Rect(xMin, yMin, dispW, dispH);
        }

        public bool TryScreenToViewport(Vector2 screenPos, out Vector2 viewportUv)
        {
            viewportUv = new Vector2(0.5f, 0.5f);
            if (!float.IsFinite(screenPos.x) || !float.IsFinite(screenPos.y))
                return false;

            Rect r = ScreenDisplayRect;
            if (r.width <= 1f || r.height <= 1f)
                return false;

            float u = Mathf.Clamp01((screenPos.x - r.xMin) / r.width);
            float v = Mathf.Clamp01((screenPos.y - r.yMin) / r.height);
            viewportUv = new Vector2(u, v);
            return true;
        }

        private void ReleaseTextures()
        {
            if (_gameRt != null)
            {
                if (_gameCam != null && _gameCam.targetTexture == _gameRt) _gameCam.targetTexture = null;
                _gameRt.Release();
                Destroy(_gameRt);
                _gameRt = null;
            }
            if (_hudRt != null)
            {
                if (_hudCam != null && _hudCam.targetTexture == _hudRt) _hudCam.targetTexture = null;
                _hudRt.Release();
                Destroy(_hudRt);
                _hudRt = null;
            }
        }

        private void OnDestroy()
        {
            ReleaseTextures();
            if (_hd2dCompositeMat != null) Destroy(_hd2dCompositeMat);
        }
    }
}
