using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    /// <summary>
    /// True Post-RT HD-2D Hybrid Pixel-Perfect Renderer:
    ///   1. Game World -> _gameRt (crisp 480x270 DefaultHDR Point, NO post-processing inside RT!)
    ///   2. _presentCam renders _gameRt onto the native screen viewport (Letterbox / Integer Windowbox)
    ///      with Tilt-Shift DoF, THEN applies URP Post-Processing (HDR Bloom, Vignette, SplitToning, ACES)
    ///      at FULL NATIVE SCREEN RESOLUTION — razor-sharp pixel art + silky high-res optical Bloom!
    ///   3. Pixel-Perfect uGUI HUD (Layer 5 UI) -> _hudRt (480x270 Point) composited cleanly on top
    ///      so HUD text & X-Ray are never affected by Bloom or Vignette!
    /// </summary>
    public class PixelWindowbox : MonoBehaviour
    {
        private const int PresentLayer = 30; // Dedicated isolated layer for the fullscreen RT presentation quad

        public int renderWidth = 480;
        public int renderHeight = 270;
        public int hd2dScale = 1; // Crisp 480x270 native pixel-art HDR buffer
        public bool hd2dEnabled = true;
        public bool integerScale = false; // Default: 16:9 Letterbox
        public Color matteColor = new Color(0.05f, 0.05f, 0.04f, 1f);
        public Color borderColor = new Color(0.28f, 0.22f, 0.14f, 1f);

        private Camera _gameCam;
        private Camera _presentCam;
        private Camera _hudCam;
        private Camera _displayClearCam;
        private RenderTexture _gameRt;
        private RenderTexture _hudRt;

        private Mesh _presentQuadMesh;
        private Transform _presentQuadTf;
        private MeshRenderer _presentQuadMr;

        private Canvas _displayCanvas;
        private RectTransform _bezelTop;
        private RectTransform _bezelBot;
        private RectTransform _bezelLeft;
        private RectTransform _bezelRight;
        private RectTransform _hudImageRect;
        private RectTransform _hitOverlayRect;
        private Material _hd2dCompositeMat;

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int TiltShiftStrengthId = Shader.PropertyToID("_TiltShiftStrength");
        private static readonly int FocusCenterId = Shader.PropertyToID("_FocusCenter");
        private static readonly int FocusWidthId = Shader.PropertyToID("_FocusWidth");
        private static readonly int TexelSizeId = Shader.PropertyToID("_TexelSize");
        private static readonly int Hd2dEnabledId = Shader.PropertyToID("_Hd2dEnabled");
        private static readonly int Hd2dTimeId = Shader.PropertyToID("_Hd2dTime");

        public Rect ScreenDisplayRect { get; private set; }
        public Camera PresentCamera => _presentCam;
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
            CreatePresentCamera();
            CreateHudOverlayCanvas();
            ApplyHd2dPresentation();
            UpdateLayout();
        }

        private void BuildHd2dCompositeMaterial()
        {
            if (_hd2dCompositeMat != null) return;
            Shader sh = Resources.Load<Shader>("Shaders/URPHd2dScreenComposite")
                        ?? Shader.Find("Modern8bitTanks/URPHd2dScreenComposite")
                        ?? Shader.Find("Universal Render Pipeline/Unlit");
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
            if (_hd2dCompositeMat != null && _gameRt != null)
            {
                _hd2dCompositeMat.SetTexture(MainTexId, _gameRt);
                _hd2dCompositeMat.SetVector(TexelSizeId, new Vector4(1f / _gameRt.width, 1f / _gameRt.height, _gameRt.width, _gameRt.height));
            }
            if (_presentQuadMr != null && _hd2dCompositeMat != null)
                _presentQuadMr.sharedMaterial = _hd2dCompositeMat;
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

            // Raw 480x270 Point-filtered HDR RenderTexture (post-processing runs AFTER this RT on _presentCam!)
            _gameRt = new RenderTexture(gw, gh, 24, RenderTextureFormat.DefaultHDR)
            {
                name = hd2dEnabled ? "Modern8bitTanks_PixelHDR_RT" : "Modern8bitTanks_Retro_RT",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            _gameRt.Create();

            if (_hd2dCompositeMat != null)
            {
                _hd2dCompositeMat.SetTexture(MainTexId, _gameRt);
                _hd2dCompositeMat.SetVector(TexelSizeId, new Vector4(1f / gw, 1f / gh, gw, gh));
            }

            if (_gameCam != null)
            {
                _gameCam.targetTexture = _gameRt;
                _gameCam.depth = -200;
                _gameCam.allowMSAA = false;
                _gameCam.allowHDR = true;
                // Exclude Layer 5 (UI) and PresentLayer so _gameCam renders strictly the game world
                _gameCam.cullingMask &= ~((1 << 5) | (1 << PresentLayer));

                var gameUrp = _gameCam.GetComponent<UniversalAdditionalCameraData>();
                if (gameUrp == null) gameUrp = _gameCam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                // CRITICAL: Keep URP Post-Processing OFF on _gameCam so raw HDR values survive into _gameRt
                // and Bloom / Vignette / ACES run at full native screen resolution on _presentCam!
                gameUrp.renderPostProcessing = false;
                gameUrp.antialiasing = AntialiasingMode.None;
            }
        }

        private void CreatePresentCamera()
        {
            var pCamGo = new GameObject("PostRtPresentCamera");
            pCamGo.transform.SetParent(transform, false);
            pCamGo.transform.position = new Vector3(0f, 0f, -500f);

            _presentCam = pCamGo.AddComponent<Camera>();
            _presentCam.orthographic = true;
            _presentCam.orthographicSize = 0.5f;
            _presentCam.nearClipPlane = 0.1f;
            _presentCam.farClipPlane = 10f;
            _presentCam.clearFlags = CameraClearFlags.SolidColor;
            _presentCam.backgroundColor = matteColor;
            _presentCam.cullingMask = 1 << PresentLayer;
            _presentCam.depth = 0;
            _presentCam.allowMSAA = false;
            _presentCam.allowHDR = true;
            _presentCam.targetTexture = null; // Renders directly to native display with full-res URP Post-Processing!

            var pUrp = pCamGo.AddComponent<UniversalAdditionalCameraData>();
            pUrp.renderPostProcessing = hd2dEnabled;
            pUrp.antialiasing = AntialiasingMode.None;
            pUrp.renderShadows = false;

            // Dedicated fullscreen quad holding _gameRt
            _presentQuadMesh = new Mesh { name = "PostRtPresentQuadMesh" };
            _presentQuadMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f)
            };
            _presentQuadMesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            _presentQuadMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _presentQuadMesh.RecalculateBounds();

            var quadGo = new GameObject("PostRtPresentQuad") { layer = PresentLayer };
            quadGo.transform.SetParent(pCamGo.transform, false);
            quadGo.transform.localPosition = new Vector3(0f, 0f, 1f);
            _presentQuadTf = quadGo.transform;

            var mf = quadGo.AddComponent<MeshFilter>();
            mf.sharedMesh = _presentQuadMesh;

            _presentQuadMr = quadGo.AddComponent<MeshRenderer>();
            _presentQuadMr.sharedMaterial = _hd2dCompositeMat;
            _presentQuadMr.shadowCastingMode = ShadowCastingMode.Off;
            _presentQuadMr.receiveShadows = false;
            _presentQuadMr.lightProbeUsage = LightProbeUsage.Off;
            _presentQuadMr.reflectionProbeUsage = ReflectionProbeUsage.Off;
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
            _hudCam.depth = -150;
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
            var urp = clearGo.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = false;
            urp.renderShadows = false;
        }

        private void CreateHudOverlayCanvas()
        {
            var canvasGo = new GameObject("PixelLetterboxCanvas");
            canvasGo.transform.SetParent(transform, false);

            _displayCanvas = canvasGo.AddComponent<Canvas>();
            _displayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _displayCanvas.sortingOrder = 0;
            _displayCanvas.pixelPerfect = false;
            canvasGo.AddComponent<GraphicRaycaster>();

            // 1. Subtle 2px outer military bezel (4 thin edge bars so center stays transparent for _presentCam!)
            _bezelTop = CreateBezelBar(canvasGo.transform, "BezelTop");
            _bezelBot = CreateBezelBar(canvasGo.transform, "BezelBot");
            _bezelLeft = CreateBezelBar(canvasGo.transform, "BezelLeft");
            _bezelRight = CreateBezelBar(canvasGo.transform, "BezelRight");

            // 2. Pixel-Perfect HUD RenderTexture stacked cleanly over post-processed game (Point-filtered!)
            var hudRawGo = new GameObject("PixelHudRawImage");
            hudRawGo.transform.SetParent(canvasGo.transform, false);
            var hudRaw = hudRawGo.AddComponent<RawImage>();
            hudRaw.texture = _hudRt;
            hudRaw.color = Color.white;
            hudRaw.raycastTarget = false;
            _hudImageRect = hudRawGo.GetComponent<RectTransform>();
            CenterRect(_hudImageRect);

            // 3. Matched transparent HitOverlay for exact click hitboxes on Pixel-Perfect UI buttons!
            var hitGo = new GameObject("HudHitOverlay");
            hitGo.transform.SetParent(canvasGo.transform, false);
            _hitOverlayRect = hitGo.AddComponent<RectTransform>();
            CenterRect(_hitOverlayRect);
        }

        private RectTransform CreateBezelBar(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = borderColor;
            img.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            CenterRect(rt);
            return rt;
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
            if (_hudImageRect == null || Screen.width <= 0 || Screen.height <= 0) return;

            float sw = Screen.width;
            float sh = Screen.height;
            float fitScale = Mathf.Min(sw / renderWidth, sh / renderHeight);
            float finalScale = (integerScale && fitScale >= 1f)
                ? Mathf.Floor(fitScale)
                : Mathf.Max(0.1f, fitScale);

            float dispW = Mathf.Round(renderWidth * finalScale);
            float dispH = Mathf.Round(renderHeight * finalScale);
            Vector2 sz = new Vector2(dispW, dispH);

            _hudImageRect.sizeDelta = sz;
            _hitOverlayRect.sizeDelta = sz;

            float xMin = (sw - dispW) * 0.5f;
            float yMin = (sh - dispH) * 0.5f;
            ScreenDisplayRect = new Rect(xMin, yMin, dispW, dispH);

            // Sync PresentCamera's native viewport & quad aspect exactly to ScreenDisplayRect!
            if (_presentCam != null)
            {
                _presentCam.rect = new Rect(xMin / sw, yMin / sh, dispW / sw, dispH / sh);
            }
            if (_presentQuadTf != null && dispH > 0f)
            {
                _presentQuadTf.localScale = new Vector3(dispW / dispH, 1f, 1f);
            }

            // Position 2px military bezel bars neatly around the exact viewport border
            const float t = 2f;
            if (_bezelTop != null)
            {
                _bezelTop.sizeDelta = new Vector2(dispW + t * 2f, t);
                _bezelTop.anchoredPosition = new Vector2(0f, (dispH + t) * 0.5f);
            }
            if (_bezelBot != null)
            {
                _bezelBot.sizeDelta = new Vector2(dispW + t * 2f, t);
                _bezelBot.anchoredPosition = new Vector2(0f, -(dispH + t) * 0.5f);
            }
            if (_bezelLeft != null)
            {
                _bezelLeft.sizeDelta = new Vector2(t, dispH);
                _bezelLeft.anchoredPosition = new Vector2(-(dispW + t) * 0.5f, 0f);
            }
            if (_bezelRight != null)
            {
                _bezelRight.sizeDelta = new Vector2(t, dispH);
                _bezelRight.anchoredPosition = new Vector2((dispW + t) * 0.5f, 0f);
            }
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
            if (_presentQuadMesh != null) Destroy(_presentQuadMesh);
        }
    }
}
