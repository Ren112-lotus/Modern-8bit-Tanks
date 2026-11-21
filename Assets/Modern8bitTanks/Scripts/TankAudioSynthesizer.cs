using UnityEngine;

namespace Modern8bitTanks
{
    /// <summary>
    /// Generates authentic retro-military procedural AudioClips at startup so every mechanical detail
    /// has tactile feedback with zero missing assets:
    /// - M1A2 Abrams AGT1500 Gas Turbine loop (deep hum + iconic turbine whine)
    /// - T-90 V-92S2 Diesel loop (throaty rhythmic diesel rumble)
    /// - Caterpillar track clatter loop
    /// - Turret traverse servo loop
    /// - Breech-lock reload "CLANK!" one-shot
    /// - Smoke grenade launcher "POP-HISSS!" one-shot
    /// - Heavy metallic turret crash one-shot
    /// </summary>
    public static class TankAudioSynthesizer
    {
        private const int SampleRate = 22050;

        public static AudioClip CreateAbramsTurbineLoop()
        {
            int samples = SampleRate * 2;
            float[] data = new float[samples];
            float lp = 0f;
            var rng = new System.Random(101);

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                // Deep gas-turbine rumble + smooth high-pitched jet whine (~860Hz & 1720Hz)
                float hum = Mathf.Sin(t * Mathf.PI * 2f * 64f) * 0.28f
                          + Mathf.Sin(t * Mathf.PI * 2f * 128f) * 0.14f;
                float whine = Mathf.Sin(t * Mathf.PI * 2f * 860f) * 0.11f
                            + Mathf.Sin(t * Mathf.PI * 2f * 1290f) * 0.05f;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp = lp * 0.88f + white * 0.12f;
                data[i] = Mathf.Clamp((hum + whine + lp * 0.30f) * 0.55f, -1f, 1f);
            }
            return MakeClip("SFX_AbramsTurbine", data);
        }

        public static AudioClip CreateT90DieselLoop()
        {
            int samples = SampleRate * 2;
            float[] data = new float[samples];
            float lp = 0f;
            var rng = new System.Random(202);

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                // Throaty V12 diesel firing rhythm (~24Hz chug modulating 48Hz & 96Hz)
                float chug = 0.65f + 0.35f * Mathf.Sin(t * Mathf.PI * 2f * 24f);
                float body = (Mathf.Sin(t * Mathf.PI * 2f * 48f) * 0.42f
                           + Mathf.Sin(t * Mathf.PI * 2f * 96f) * 0.22f
                           + Mathf.Sin(t * Mathf.PI * 2f * 144f) * 0.10f) * chug;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp = lp * 0.82f + white * 0.18f;
                data[i] = Mathf.Clamp((body + lp * 0.35f) * 0.60f, -1f, 1f);
            }
            return MakeClip("SFX_T90Diesel", data);
        }

        public static AudioClip CreateTrackClatterLoop()
        {
            int samples = SampleRate * 2;
            float[] data = new float[samples];
            var rng = new System.Random(303);
            float bp = 0f;

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                // Metallic track-link rhythm (~18 links/sec)
                float pulse = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI * 2f * 18f)), 6f);
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                bp = bp * 0.70f + white * 0.30f;
                float clink = Mathf.Sin(t * Mathf.PI * 2f * 410f) * pulse * 0.22f;
                data[i] = Mathf.Clamp((bp * (0.18f + pulse * 0.55f) + clink) * 0.48f, -1f, 1f);
            }
            return MakeClip("SFX_TrackClatter", data);
        }

        public static AudioClip CreateTurretServoLoop()
        {
            int samples = SampleRate;
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                float s = Mathf.Sin(t * Mathf.PI * 2f * 290f) * 0.25f
                        + Mathf.Sin(t * Mathf.PI * 2f * 580f) * 0.12f
                        + Mathf.Sin(t * Mathf.PI * 2f * 870f) * 0.05f;
                data[i] = s * 0.35f;
            }
            return MakeClip("SFX_TurretServo", data);
        }

        public static AudioClip CreateReloadClank()
        {
            int samples = (int)(SampleRate * 0.26f);
            float[] data = new float[samples];
            var rng = new System.Random(404);

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                // Two-stage heavy steel breech lock: "KA-CHUNK!" (0.00s + 0.09s)
                float e1 = Mathf.Exp(-t * 36f);
                float s1 = (Mathf.Sin(t * Mathf.PI * 2f * 520f) * 0.5f + Mathf.Sin(t * Mathf.PI * 2f * 1380f) * 0.35f + (float)(rng.NextDouble() * 2.0 - 1.0) * 0.3f) * e1;

                float t2 = t - 0.085f;
                float s2 = 0f;
                if (t2 > 0f)
                {
                    float e2 = Mathf.Exp(-t2 * 26f);
                    s2 = (Mathf.Sin(t2 * Mathf.PI * 2f * 310f) * 0.65f + Mathf.Sin(t2 * Mathf.PI * 2f * 980f) * 0.45f + (float)(rng.NextDouble() * 2.0 - 1.0) * 0.25f) * e2;
                }
                data[i] = Mathf.Clamp((s1 * 0.65f + s2 * 0.9f) * 0.72f, -1f, 1f);
            }
            return MakeClip("SFX_ReloadClank", data);
        }

        public static AudioClip CreateSmokePop()
        {
            int samples = (int)(SampleRate * 0.55f);
            float[] data = new float[samples];
            var rng = new System.Random(505);
            float lp = 0f;

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                // Initial mortar tube "THUMP-POP" + pressurized smoke "HISSSS"
                float pop = Mathf.Sin(t * Mathf.PI * 2f * Mathf.Lerp(210f, 65f, Mathf.Clamp01(t * 14f))) * Mathf.Exp(-t * 28f) * 0.85f;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp = lp * 0.55f + white * 0.45f;
                float hissEnv = Mathf.Clamp01(t * 22f) * Mathf.Exp(-t * 4.5f);
                data[i] = Mathf.Clamp((pop + lp * hissEnv * 0.55f) * 0.75f, -1f, 1f);
            }
            return MakeClip("SFX_SmokePop", data);
        }

        public static AudioClip CreateMetalCrash()
        {
            int samples = (int)(SampleRate * 0.42f);
            float[] data = new float[samples];
            var rng = new System.Random(606);

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                float env = Mathf.Exp(-t * 11f);
                float ring = Mathf.Sin(t * Mathf.PI * 2f * 195f) * 0.5f
                           + Mathf.Sin(t * Mathf.PI * 2f * 412f) * 0.35f
                           + Mathf.Sin(t * Mathf.PI * 2f * 685f) * 0.2f;
                float crunch = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t * 22f) * 0.6f;
                data[i] = Mathf.Clamp((ring * env + crunch) * 0.75f, -1f, 1f);
            }
            return MakeClip("SFX_MetalCrash", data);
        }

        private static AudioClip MakeClip(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
