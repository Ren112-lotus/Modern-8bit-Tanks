using UnityEngine;

namespace Modern8bitTanks
{
    public enum SoundBus
    {
        Dry,        // 響かない音（装填音・機銃・スモーク・車内メカ音）
        Reverb,     // 砂漠の岩壁に響く音（主砲・爆発・跳弾・砲塔落下）
        HeavyReverb // 大爆発・誘爆用の深い残響
    }

    /// <summary>
    /// Position-based 2D stereo spatial audio controller with strict separation between:
    /// - DRY voices (no reverb): cabin reload clank, coaxial MG, smoke pop, engine/tracks/turret servo
    /// - WET/REVERB voices (outdoor reverb with a distance-based low-pass filter):
    ///   main cannon blasts, explosions, ricochets, and heavy turret crash impacts.
    /// Uses dedicated voice pools so overlapping sounds at different positions keep independent stereo pan & pitch!
    /// </summary>
    public class BattleAudioController : MonoBehaviour
    {

        [Header("Audio Mix Balance (Editable Live in Inspector)")]
        [Range(0f, 1f)] public float engineMaster = 0.35f;
        [Range(0f, 1f)] public float tracksMaster = 0.28f;
        [Range(0f, 1f)] public float servoMaster = 0.35f;
        [Range(0f, 1.2f)] public float combatSfxMaster = 0.95f;
        [Range(0f, 1.2f)] public float bgmMaster = 0.65f;

        // Stereo & attenuation tuning (in Unity world units; expanded 1280x860 battlefield is 80 x 53.75 units)
        private const float FullPanDistance = 16.0f;
        private const float HalfVolumeDistance = 24.0f;
        private const float MaxAudibleDistance = 68.0f;

        [System.Serializable]
        private struct Voice
        {
            public AudioSource source;
            public AudioLowPassFilter lowPass;
            public AudioReverbFilter reverb;
            [System.NonSerialized] public float startTime;
            [System.NonSerialized] public float fadeStartTime;
            [System.NonSerialized] public float fadeEndTime;
            [System.NonSerialized] public float peakVolume;
        }

        [SerializeField] private Voice[] _dryPool;
        [SerializeField] private Voice[] _wetPool;
        private int _dryCursor;
        private int _wetCursor;

        // Continuous dry cabin/chassis loops for the player tank
        [SerializeField] private AudioSource _engineSource;
        [SerializeField] private AudioLowPassFilter _engineLp;
        [SerializeField] private AudioSource _trackSource;
        [SerializeField] private AudioLowPassFilter _trackLp;
        [SerializeField] private AudioSource _servoSource;
        [SerializeField] private AudioSource _missileMotorSource;
        [SerializeField] private AudioLowPassFilter _missileMotorLp;

        // Dedicated full-bandwidth stereo Battle BGM player (completely independent of SE/SFX!)
        [SerializeField] private AudioSource _bgmSource;

        public void Initialize(AudioClip trackClip, AudioClip servoClip, AudioClip missileMotorClip = null)
        {
            _trackSource.clip = trackClip;
            _servoSource.clip = servoClip;
            _missileMotorSource.clip = missileMotorClip;
        }

        private static void ConfigureOutdoorReverb(AudioReverbFilter rev, bool heavy)
        {
            rev.reverbPreset = AudioReverbPreset.User;
            rev.dryLevel = 0f;
            rev.room = heavy ? -180f : -420f;
            rev.roomHF = -800f;
            rev.roomLF = -100f;
            rev.decayTime = heavy ? 2.45f : 1.55f;
            rev.decayHFRatio = 0.42f;
            rev.reflectionsLevel = heavy ? -350f : -650f;
            rev.reflectionsDelay = 0.045f; // Short outdoor reflection
            rev.reverbLevel = heavy ? 120f : -180f;
            rev.reverbDelay = 0.080f;
            rev.hfReference = 3600f;
            rev.lfReference = 240f;
            rev.diffusion = 72f;
            rev.density = 68f;
        }

        /// <summary>
        /// Plays a one-shot sound spatially positioned in stereo relative to listenerPos,
        /// routed to either the DRY bus (no reverb) or the outdoor REVERB bus.
        /// Supports per-clip concurrency limiting (voice stealing) and maxDuration fade envelopes
        /// so long clips (e.g. 6s missile roars) never layer into harsh noise.
        /// </summary>
        public void PlayAt(
            AudioClip clip,
            Vector2 worldPos,
            Vector2 listenerPos,
            SoundBus bus,
            float baseVolume,
            bool sfxEnabled,
            float pitchMin = 0.94f,
            float pitchMax = 1.06f,
            bool centerPan = false,
            float sfxVolume = 1f,
            float maxDuration = 0f,
            int maxConcurrentForClip = 0,
            float minRetriggerSec = 0f)
        {
            float volScale = Mathf.Clamp01(sfxVolume);
            if (!sfxEnabled || volScale <= 0.001f || clip == null) return;

            Vector2 delta = worldPos - listenerPos;
            float dist = delta.magnitude;
            if (dist > MaxAudibleDistance && !centerPan) return;

            // 1. Stereo Panning: left of listener -> -1, right of listener -> +1
            float pan = centerPan ? 0f : Mathf.Clamp(delta.x / FullPanDistance, -0.92f, 0.92f);

            // 2. Natural outdoor distance attenuation
            float atten = centerPan ? 1f : 1f / (1f + Mathf.Pow(dist / HalfVolumeDistance, 1.35f));
            float finalVol = Mathf.Clamp01(baseVolume * combatSfxMaster * volScale * atten);
            if (finalVol < 0.015f) return;

            // 3. Distant air-absorption lowpass (faraway blasts lose crisp high end naturally)
            float dist01 = centerPan ? 0f : Mathf.Clamp01(dist / MaxAudibleDistance);
            float cutoffHz = Mathf.Lerp(22000f, 2400f, dist01 * dist01);

            Voice[] pool = bus == SoundBus.Dry ? _dryPool : _wetPool;
            if (pool == null || pool.Length == 0) return;

            float now = Time.unscaledTime;
            int chosenIdx = -1;

            // Enforce per-clip concurrency cap & minimum retrigger spacing to prevent multi-layered audio overload!
            if (maxConcurrentForClip > 0 || minRetriggerSec > 0f)
            {
                int activeCount = 0;
                int oldestIdx = -1;
                float oldestTime = float.MaxValue;
                float newestTime = -999f;

                for (int i = 0; i < pool.Length; i++)
                {
                    var s = pool[i].source;
                    if (s != null && s.isPlaying && s.clip == clip)
                    {
                        activeCount++;
                        if (pool[i].startTime < oldestTime)
                        {
                            oldestTime = pool[i].startTime;
                            oldestIdx = i;
                        }
                        if (pool[i].startTime > newestTime)
                            newestTime = pool[i].startTime;
                    }
                }

                if (minRetriggerSec > 0f && (now - newestTime) < minRetriggerSec)
                    return;

                if (maxConcurrentForClip > 0 && activeCount >= maxConcurrentForClip && oldestIdx >= 0)
                    chosenIdx = oldestIdx;
            }

            if (chosenIdx < 0)
            {
                if (bus == SoundBus.Dry)
                {
                    chosenIdx = _dryCursor;
                    _dryCursor = (_dryCursor + 1) % _dryPool.Length;
                }
                else
                {
                    chosenIdx = _wetCursor;
                    _wetCursor = (_wetCursor + 1) % _wetPool.Length;
                }
            }

            ref Voice voice = ref pool[chosenIdx];
            if (bus != SoundBus.Dry && voice.reverb != null)
            {
                ConfigureOutdoorReverb(voice.reverb, heavy: bus == SoundBus.HeavyReverb);
                // Distant canyon sounds pick up slightly stronger reverb tail relative to direct signal!
                if (dist01 > 0.2f)
                {
                    voice.reverb.reverbLevel += dist01 * 260f;
                    voice.reverb.decayTime += dist01 * 0.55f;
                }
            }

            voice.lowPass.cutoffFrequency = cutoffHz;
            voice.source.Stop();
            voice.source.clip = clip;
            voice.source.panStereo = pan;
            voice.source.volume = finalVol;
            voice.source.pitch = Random.Range(pitchMin, pitchMax);
            voice.source.Play();

            voice.startTime = now;
            voice.peakVolume = finalVol;
            if (maxDuration > 0.08f)
            {
                float fadeLen = Mathf.Min(0.32f, maxDuration * 0.35f);
                voice.fadeStartTime = now + Mathf.Max(0.05f, maxDuration - fadeLen);
                voice.fadeEndTime = now + maxDuration;
            }
            else
            {
                voice.fadeStartTime = 0f;
                voice.fadeEndTime = 0f;
            }
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            TickVoiceEnvelopes(_dryPool, now);
            TickVoiceEnvelopes(_wetPool, now);
        }

        private static void TickVoiceEnvelopes(Voice[] pool, float now)
        {
            if (pool == null) return;
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i].fadeEndTime <= 0f) continue;
                var src = pool[i].source;
                if (src == null || !src.isPlaying)
                {
                    pool[i].fadeEndTime = 0f;
                    continue;
                }
                if (now >= pool[i].fadeEndTime)
                {
                    src.Stop();
                    src.volume = 0f;
                    pool[i].fadeEndTime = 0f;
                }
                else if (now >= pool[i].fadeStartTime)
                {
                    float t = (now - pool[i].fadeStartTime) / Mathf.Max(0.01f, pool[i].fadeEndTime - pool[i].fadeStartTime);
                    src.volume = pool[i].peakVolume * (1f - t) * (1f - t);
                }
            }
        }

        public void StartPlayerLoops(AudioClip engineClip)
        {
            if (_engineSource == null) return;
            if (_engineSource.clip != engineClip)
            {
                _engineSource.Stop();
                _engineSource.clip = engineClip;
            }
            if (_engineSource.clip != null && !_engineSource.isPlaying) _engineSource.Play();
            if (_trackSource.clip != null && !_trackSource.isPlaying) _trackSource.Play();
            if (_servoSource.clip != null && !_servoSource.isPlaying) _servoSource.Play();
        }

        public void StartBattleBgm(AudioClip bgmClip)
        {
            if (_bgmSource == null || bgmClip == null) return;
            if (_bgmSource.clip != bgmClip)
            {
                _bgmSource.Stop();
                _bgmSource.clip = bgmClip;
            }
            if (!_bgmSource.isPlaying)
                _bgmSource.Play();
        }

        public void StopBattleBgm()
        {
            if (_bgmSource != null)
            {
                _bgmSource.Stop();
                _bgmSource.volume = 0f;
            }
        }

        public void UpdateBgm(bool active, bool bgmEnabled, float bgmVolume, bool duckForMenu)
        {
            if (_bgmSource == null || _bgmSource.clip == null) return;

            float vol01 = Mathf.Clamp01(bgmVolume);
            float targetVol = (!active || !bgmEnabled || vol01 <= 0.001f)
                ? 0f
                : Mathf.Clamp01(vol01 * bgmMaster * (duckForMenu ? 0.55f : 1f));

            if (targetVol > 0.001f && !_bgmSource.isPlaying)
                _bgmSource.Play();

            float rate = targetVol > _bgmSource.volume ? 2.4f : 4.2f;
            _bgmSource.volume = Mathf.MoveTowards(_bgmSource.volume, targetVol, Time.unscaledDeltaTime * rate);
        }

        public void StopLoopsImmediate()
        {
            if (_engineSource != null) _engineSource.volume = 0f;
            if (_trackSource != null) _trackSource.volume = 0f;
            if (_servoSource != null) _servoSource.volume = 0f;
            if (_missileMotorSource != null) _missileMotorSource.volume = 0f;
        }

        /// <summary>
        /// Spatialized in-flight rocket motor roar following the closest active TOW/ATGM missile!
        /// </summary>
        public void UpdateMissileMotorLoop(bool hasActiveMissile, Vector2 missilePos, Vector2 listenerPos, bool sfxEnabled, float dt, float sfxVolume = 1f)
        {
            if (_missileMotorSource == null || _missileMotorSource.clip == null) return;
            float volScale = Mathf.Clamp01(sfxVolume);
            if (!hasActiveMissile || !sfxEnabled || volScale <= 0.001f)
            {
                _missileMotorSource.volume = Mathf.MoveTowards(_missileMotorSource.volume, 0f, Time.unscaledDeltaTime * 7.5f);
                return;
            }
            if (!_missileMotorSource.isPlaying) _missileMotorSource.Play();

            Vector2 delta = missilePos - listenerPos;
            float dist = delta.magnitude;
            float pan = Mathf.Clamp(delta.x / FullPanDistance, -0.92f, 0.92f);
            float atten = 1f / (1f + Mathf.Pow(dist / HalfVolumeDistance, 1.25f));
            // Balanced in-flight rocket motor bed that never clashes with the launch ignition burst
            float targetVol = Mathf.Clamp01(0.24f * combatSfxMaster * volScale * atten);
            _missileMotorSource.panStereo = Mathf.Lerp(_missileMotorSource.panStereo, pan, dt * 12f);
            _missileMotorSource.volume = Mathf.Lerp(_missileMotorSource.volume, targetVol, dt * 8f);
            _missileMotorSource.pitch = 1.18f + Mathf.Clamp01(1f - dist / 30f) * 0.14f;
            if (_missileMotorLp != null)
                _missileMotorLp.cutoffFrequency = Mathf.Lerp(14000f, 2600f, Mathf.Clamp01(dist / MaxAudibleDistance));
        }

        public void UpdatePlayerLoops(bool active, float moving01, float turretTurn01, float dt, float sfxVolume = 1f)
        {
            float volScale = Mathf.Clamp01(sfxVolume);
            if (!active || volScale <= 0.001f)
            {
                _engineSource.volume = Mathf.MoveTowards(_engineSource.volume, 0f, Time.unscaledDeltaTime * 2.5f);
                _trackSource.volume = Mathf.MoveTowards(_trackSource.volume, 0f, Time.unscaledDeltaTime * 3.5f);
                _servoSource.volume = Mathf.MoveTowards(_servoSource.volume, 0f, Time.unscaledDeltaTime * 4.5f);
                return;
            }

            // Quiet, warm cabin bed so cannon blasts, ricochets, & reload clanks stay crisp and dominant!
            float targetEngVol = (0.11f + moving01 * 0.14f) * engineMaster * volScale; // ~0.038 idle -> ~0.088 full speed
            float targetEngPitch = 0.88f + moving01 * 0.30f;
            _engineSource.volume = Mathf.Lerp(_engineSource.volume, targetEngVol, dt * 6f);
            _engineSource.pitch = Mathf.Lerp(_engineSource.pitch, targetEngPitch, dt * 5f);
            if (_engineLp != null)
                _engineLp.cutoffFrequency = Mathf.Lerp(1350f, 2600f, moving01);

            // Subtle caterpillar clatter underneath the tank
            float targetTrackVol = moving01 > 0.08f ? (0.07f + moving01 * 0.16f) * tracksMaster * volScale : 0f; // max ~0.064
            _trackSource.volume = Mathf.Lerp(_trackSource.volume, targetTrackVol, dt * 8f);
            _trackSource.pitch = 0.88f + moving01 * 0.34f;
            if (_trackLp != null)
                _trackLp.cutoffFrequency = Mathf.Lerp(1800f, 3200f, moving01);

            // Very subtle turret traverse servo whirr
            float turn01 = Mathf.Clamp01(turretTurn01);
            float targetServoVol = turn01 > 0.14f ? turn01 * 0.12f * servoMaster * volScale : 0f; // max ~0.042
            _servoSource.volume = Mathf.Lerp(_servoSource.volume, targetServoVol, dt * 10f);
            _servoSource.pitch = 0.95f + turn01 * 0.22f;
        }
    }
}
