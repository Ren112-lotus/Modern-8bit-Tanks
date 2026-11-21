using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Modern8bitTanks
{
    /// <summary>
    /// Clean HD-2D URP Post-Processing (Bloom, Vignette, ACES Tonemapping, Split Toning):
    /// All light glow & atmosphere comes naturally from optical URP Post-Processing on the HD HDR buffer:
    /// - Silky HDR Bloom on muzzle flashes, shell tracers, sparks, fire, explosions, & oasis glints
    /// - Photographic diorama Vignette + crimson damage pulse
    /// - Cool sky shadows / warm sunlit highlights via Split Toning + ACES Filmic Tonemapping
    /// </summary>
    public class BattlePostProcess : MonoBehaviour
    {
        public bool postProcessEnabled = true;

        private Camera _cam;
        private UniversalAdditionalCameraData _camData;
        private Volume _volume;
        private VolumeProfile _profile;

        private Bloom _bloom;
        private Vignette _vignette;
        private ColorAdjustments _colorAdj;
        private WhiteBalance _whiteBalance;
        private SplitToning _splitToning;
        private Tonemapping _tonemap;

        private float _bloomPulse;
        private float _damagePulse;
        private float _exposurePulse;

        private static readonly Color WarmVignetteColor = new Color(0.08f, 0.06f, 0.05f, 1f);
        private static readonly Color DamageVignetteColor = new Color(0.76f, 0.08f, 0.05f, 1f);

        private const float BaseBloomIntensity = 1.65f;
        private const float BaseVignetteIntensity = 0.28f;

        public void Initialize(Camera gameCam)
        {
            _cam = gameCam;
            if (_cam != null)
            {
                _cam.allowHDR = true;
                _camData = _cam.GetComponent<UniversalAdditionalCameraData>();
                if (_camData == null)
                    _camData = _cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                _camData.renderPostProcessing = postProcessEnabled;
                _camData.antialiasing = AntialiasingMode.None;
            }

            var volGo = new GameObject("BattlePostProcessVolume");
            volGo.transform.SetParent(transform, false);
            _volume = volGo.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 100f;

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();

            // 1. Optical HD-2D Selective HDR Bloom (bicubic high-quality glow — shells & tracers blaze cleanly!)
            _bloom = _profile.Add<Bloom>(true);
            _bloom.threshold.overrideState = true;
            _bloom.threshold.value = 0.88f;
            _bloom.intensity.overrideState = true;
            _bloom.intensity.value = BaseBloomIntensity;
            _bloom.scatter.overrideState = true;
            _bloom.scatter.value = 0.78f;
            _bloom.highQualityFiltering.overrideState = true;
            _bloom.highQualityFiltering.value = true;
            _bloom.tint.overrideState = true;
            _bloom.tint.value = new Color(1.0f, 0.94f, 0.82f, 1f);

            // 2. Natural Warm Desert Sunlight White Balance
            _whiteBalance = _profile.Add<WhiteBalance>(true);
            _whiteBalance.temperature.overrideState = true;
            _whiteBalance.temperature.value = 13f;
            _whiteBalance.tint.overrideState = true;
            _whiteBalance.tint.value = 2f;

            // 3. HD-2D Split Toning (cool sky-lit shadows + warm sunlit highlights)
            _splitToning = _profile.Add<SplitToning>(true);
            _splitToning.shadows.overrideState = true;
            _splitToning.shadows.value = new Color(0.38f, 0.48f, 0.74f, 1f);
            _splitToning.highlights.overrideState = true;
            _splitToning.highlights.value = new Color(1.0f, 0.90f, 0.68f, 1f);
            _splitToning.balance.overrideState = true;
            _splitToning.balance.value = 10f;

            // 4. Photographic Diorama Vignette + crimson damage pulse
            _vignette = _profile.Add<Vignette>(true);
            _vignette.color.overrideState = true;
            _vignette.color.value = WarmVignetteColor;
            _vignette.intensity.overrideState = true;
            _vignette.intensity.value = BaseVignetteIntensity;
            _vignette.smoothness.overrideState = true;
            _vignette.smoothness.value = 0.52f;

            // 5. Crisp HD-2D contrast & vibrant saturation
            _colorAdj = _profile.Add<ColorAdjustments>(true);
            _colorAdj.postExposure.overrideState = true;
            _colorAdj.postExposure.value = 0.08f;
            _colorAdj.contrast.overrideState = true;
            _colorAdj.contrast.value = 12f;
            _colorAdj.saturation.overrideState = true;
            _colorAdj.saturation.value = 12f;

            // 6. ACES Filmic Tonemapping
            _tonemap = _profile.Add<Tonemapping>(true);
            _tonemap.mode.overrideState = true;
            _tonemap.mode.value = TonemappingMode.ACES;

            _volume.profile = _profile;
            SetEnabled(postProcessEnabled);
        }

        public void SetEnabled(bool enabled)
        {
            postProcessEnabled = enabled;
            if (_volume != null) _volume.enabled = enabled;
            if (_camData != null) _camData.renderPostProcessing = enabled;
        }

        public void ApplyMapTheme(BattleMap map)
        {
            if (_whiteBalance == null || _splitToning == null || _colorAdj == null) return;
            switch (map)
            {
                case BattleMap.City:
                    // Crisp overcast-golden European urban atmosphere
                    _whiteBalance.temperature.value = 4f;
                    _whiteBalance.tint.value = -1f;
                    _splitToning.shadows.value = new Color(0.36f, 0.44f, 0.62f, 1f);
                    _splitToning.highlights.value = new Color(0.98f, 0.92f, 0.80f, 1f);
                    _colorAdj.contrast.value = 14f;
                    _colorAdj.saturation.value = 8f;
                    break;
                case BattleMap.Forest:
                    // Lush emerald-golden dappled woodland daylight
                    _whiteBalance.temperature.value = 7f;
                    _whiteBalance.tint.value = -4f;
                    _splitToning.shadows.value = new Color(0.28f, 0.46f, 0.56f, 1f);
                    _splitToning.highlights.value = new Color(0.98f, 0.94f, 0.68f, 1f);
                    _colorAdj.contrast.value = 13f;
                    _colorAdj.saturation.value = 15f;
                    break;
                default: // Desert
                    _whiteBalance.temperature.value = 13f;
                    _whiteBalance.tint.value = 2f;
                    _splitToning.shadows.value = new Color(0.38f, 0.48f, 0.74f, 1f);
                    _splitToning.highlights.value = new Color(1.0f, 0.90f, 0.68f, 1f);
                    _colorAdj.contrast.value = 12f;
                    _colorAdj.saturation.value = 12f;
                    break;
            }
        }

        public void PulseCannonFire(bool isPlayer)
        {
            _bloomPulse = Mathf.Max(_bloomPulse, isPlayer ? 0.28f : 0.12f);
        }

        public void PulseExplosion(bool big)
        {
            _bloomPulse = Mathf.Max(_bloomPulse, big ? 1.35f : 0.55f);
            _exposurePulse = Mathf.Max(_exposurePulse, big ? 0.14f : 0.04f);
        }

        public void PulseRicochet()
        {
            _bloomPulse = Mathf.Max(_bloomPulse, 0.58f);
        }

        public void PulsePlayerDamage()
        {
            _damagePulse = 1.0f;
        }

        private void Update()
        {
            if (!postProcessEnabled || _bloom == null) return;
            float dt = Time.unscaledDeltaTime;

            _bloomPulse = Mathf.MoveTowards(_bloomPulse, 0f, dt * 3.0f);
            _damagePulse = Mathf.MoveTowards(_damagePulse, 0f, dt * 2.6f);
            _exposurePulse = Mathf.MoveTowards(_exposurePulse, 0f, dt * 2.4f);

            _bloom.intensity.value = BaseBloomIntensity + _bloomPulse;
            _colorAdj.postExposure.value = 0.08f + _exposurePulse;

            _vignette.color.value = Color.Lerp(WarmVignetteColor, DamageVignetteColor, _damagePulse);
            _vignette.intensity.value = BaseVignetteIntensity + _damagePulse * 0.24f;
        }

        private void OnDestroy()
        {
            if (_profile != null) Destroy(_profile);
        }
    }
}
