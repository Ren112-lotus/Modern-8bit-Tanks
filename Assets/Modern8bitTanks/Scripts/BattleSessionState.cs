using System.Collections.Generic;
using UnityEngine;

namespace Modern8bitTanks
{
    public enum BattleMap
    {
        Desert,
        City,
        Forest
    }

    /// <summary>
    /// Cross-scene session state bridging Hangar.unity (Garage & WoT Tech Tree) and DesertBattle.unity (Combat).
    /// Also provides fair random same-Tier matchmaking based on vehicle Tier & performance class.
    /// </summary>
    public static class BattleSessionState
    {
        public const string HangarSceneName = "Hangar";
        public const string BattleSceneName = "DesertBattle";

        public static string SelectedPlayerVehicleId = "us_m1a2_sep2";
        public static BattleMode SelectedBattleMode = BattleMode.Duel1v1;
        public static BattleMap SelectedBattleMap = BattleMap.Desert;
        public static BattleCameraMode SelectedCameraMode = BattleCameraMode.FixedTop;

        public static bool ArcadeWASD = false;
        public static bool AiEnabled = true;
        public static bool SfxEnabled = true;
        public static float SfxVolume = 1.0f;
        public static bool BgmEnabled = true;
        public static float BgmVolume = 0.60f;
        public static bool PostProcessEnabled = true;
        public static bool IntegerWindowbox = false;

        public static int WinsAlly = 0;
        public static int WinsEnemy = 0;
        public static bool HasEnteredGarage = false;

        private const string PrefPrefix = "M8T_Setting_";

        static BattleSessionState()
        {
            LoadSettings();
        }

        public static void LoadSettings()
        {
            ArcadeWASD = PlayerPrefs.GetInt(PrefPrefix + "ArcadeWASD", 0) != 0;
            AiEnabled = PlayerPrefs.GetInt(PrefPrefix + "AiEnabled", 1) != 0;
            SfxEnabled = PlayerPrefs.GetInt(PrefPrefix + "SfxEnabled", 1) != 0;
            SfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefPrefix + "SfxVolume", 1.0f));
            BgmEnabled = PlayerPrefs.GetInt(PrefPrefix + "BgmEnabled", 1) != 0;
            BgmVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefPrefix + "BgmVolume", 0.60f));
            PostProcessEnabled = PlayerPrefs.GetInt(PrefPrefix + "PostProcess", 1) != 0;
            IntegerWindowbox = PlayerPrefs.GetInt(PrefPrefix + "IntWindowbox", 0) != 0;
        }

        public static void SaveSettings()
        {
            PlayerPrefs.SetInt(PrefPrefix + "ArcadeWASD", ArcadeWASD ? 1 : 0);
            PlayerPrefs.SetInt(PrefPrefix + "AiEnabled", AiEnabled ? 1 : 0);
            PlayerPrefs.SetInt(PrefPrefix + "SfxEnabled", SfxEnabled ? 1 : 0);
            PlayerPrefs.SetFloat(PrefPrefix + "SfxVolume", Mathf.Clamp01(SfxVolume));
            PlayerPrefs.SetInt(PrefPrefix + "BgmEnabled", BgmEnabled ? 1 : 0);
            PlayerPrefs.SetFloat(PrefPrefix + "BgmVolume", Mathf.Clamp01(BgmVolume));
            PlayerPrefs.SetInt(PrefPrefix + "PostProcess", PostProcessEnabled ? 1 : 0);
            PlayerPrefs.SetInt(PrefPrefix + "IntWindowbox", IntegerWindowbox ? 1 : 0);
            PlayerPrefs.Save();
        }

        public static void StepSfxVolume(float delta)
        {
            float baseVol = SfxEnabled ? SfxVolume : 0f;
            float next = Mathf.Round(Mathf.Clamp01(baseVol + delta) * 10f) * 0.1f;
            SfxVolume = next;
            SfxEnabled = next > 0.001f;
            SaveSettings();
        }

        public static void ToggleSfxEnabled()
        {
            SfxEnabled = !SfxEnabled;
            if (SfxEnabled && SfxVolume <= 0.001f) SfxVolume = 0.6f;
            SaveSettings();
        }

        public static void StepBgmVolume(float delta)
        {
            float baseVol = BgmEnabled ? BgmVolume : 0f;
            float next = Mathf.Round(Mathf.Clamp01(baseVol + delta) * 10f) * 0.1f;
            BgmVolume = next;
            BgmEnabled = next > 0.001f;
            SaveSettings();
        }

        public static void ToggleBgmEnabled()
        {
            BgmEnabled = !BgmEnabled;
            if (BgmEnabled && BgmVolume <= 0.001f) BgmVolume = 0.6f;
            SaveSettings();
        }

        public static string FormatVolumeLabel(string tag, bool enabled, float volume)
        {
            if (!enabled || volume <= 0.001f) return $"{tag}: OFF";
            int pct = Mathf.RoundToInt(Mathf.Clamp01(volume) * 100f);
            return $"{tag}: {pct}%";
        }

        /// <summary>
        /// Randomly selects an opposing-nation opponent of the SAME Tier and comparable performance bracket.
        /// </summary>
        public static VehicleDef PickRandomSameTierRival(VehicleDef[] all, VehicleDef playerDef, string excludeId = null)
        {
            if (all == null || all.Length == 0) return null;
            if (playerDef == null) return all[Random.Range(0, all.Length)];

            var strictPool = new List<VehicleDef>();
            var sameTierPool = new List<VehicleDef>();
            var nearTierPool = new List<VehicleDef>();

            foreach (var v in all)
            {
                if (v == null || v.id == playerDef.id) continue;
                if (v.nation == playerDef.nation) continue;

                if (v.tier == playerDef.tier)
                {
                    sameTierPool.Add(v);
                    bool roleMatch = playerDef.role == v.role || playerDef.weapon == v.weapon;
                    if (roleMatch)
                        strictPool.Add(v);
                }
                else if (Mathf.Abs(v.tier - playerDef.tier) == 1)
                {
                    nearTierPool.Add(v);
                }
            }

            List<VehicleDef> chosen = strictPool.Count > 0
                ? strictPool
                : (sameTierPool.Count > 0 ? sameTierPool : nearTierPool);

            if (chosen.Count == 0)
                return all[Random.Range(0, all.Length)];

            // Avoid repeating the exact same rival back-to-back when multiple candidates exist
            if (!string.IsNullOrEmpty(excludeId) && chosen.Count > 1)
            {
                var filtered = chosen.FindAll(v => v.id != excludeId);
                if (filtered.Count > 0) chosen = filtered;
            }

            return chosen[Random.Range(0, chosen.Count)];
        }

        /// <summary>
        /// Randomly selects a same-nation allied wingman of the same or nearest Tier for 2v2 Squad battles.
        /// </summary>
        public static VehicleDef PickRandomSameTierAlly(VehicleDef[] all, VehicleDef playerDef)
        {
            if (all == null || all.Length == 0 || playerDef == null) return playerDef;

            var sameTier = new List<VehicleDef>();
            var nearTier = new List<VehicleDef>();
            foreach (var v in all)
            {
                if (v == null || v.id == playerDef.id || v.nation != playerDef.nation) continue;
                if (v.tier == playerDef.tier) sameTier.Add(v);
                else if (Mathf.Abs(v.tier - playerDef.tier) <= 1) nearTier.Add(v);
            }
            var pool = sameTier.Count > 0 ? sameTier : nearTier;
            return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : playerDef;
        }
    }
}
