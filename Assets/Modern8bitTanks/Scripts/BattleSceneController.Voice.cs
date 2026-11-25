using System.Threading;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Modern8bitTanks
{
    public enum BattleVoiceEvent
    {
        BattleStart,
        EnemyKill,
        Victory,
        Defeat,
        CriticalHit,
        ShotRicochet,
        PlayerFire,
        PlayerDamage,
        PlayerDamageTrack,
        PlayerDamageGun,
        CpCaptured,
        CpLost,
        ArmorBounce,
        SmokeDeploy,
        WaveClear
    }

    public partial class BattleSceneController
    {
        private readonly struct VoiceRule
        {
            public readonly string Prefix;
            public readonly int Variants;
            public readonly int Priority;
            public readonly float CooldownSec;
            public readonly int CooldownGroup;
            public readonly bool BypassGlobalGap;

            public VoiceRule(string prefix, int variants, int priority, float cooldownSec, int cooldownGroup = -1, bool bypassGlobalGap = false)
            {
                Prefix = prefix;
                Variants = variants;
                Priority = priority;
                CooldownSec = cooldownSec;
                CooldownGroup = cooldownGroup;
                BypassGlobalGap = bypassGlobalGap;
            }
        }

        private const int VoiceEventCount = 15;
        private const int DamageCooldownGroup = 100;
        private const float VoiceGlobalGapSec = 0.55f;

        private static readonly VoiceRule[] VoiceRules =
        {
            new("voice_battle_start_", variants: 3, priority: 3, cooldownSec: 5.0f, bypassGlobalGap: true), // BattleStart
            new("voice_enemy_kill_", variants: 6, priority: 3, cooldownSec: 1.8f),                        // EnemyKill
            new("voice_victory_", variants: 3, priority: 4, cooldownSec: 5.0f, bypassGlobalGap: true), // Victory
            new("voice_defeat_", variants: 3, priority: 4, cooldownSec: 4.0f, bypassGlobalGap: true), // Defeat
            new("voice_critical_hit_", variants: 3, priority: 2, cooldownSec: 3.8f),                        // CriticalHit
            new("voice_shot_ricochet_", variants: 3, priority: 1, cooldownSec: 4.5f),                        // ShotRicochet
            new("voice_player_fire_", variants: 2, priority: 3, cooldownSec: 6.0f),                        // PlayerFire
            new("voice_player_damage_", variants: 2, priority: 2, cooldownSec: 5.5f, cooldownGroup: DamageCooldownGroup), // PlayerDamage
            new("voice_player_damage_track_", variants: 1, priority: 2, cooldownSec: 5.5f, cooldownGroup: DamageCooldownGroup), // PlayerDamageTrack
            new("voice_player_damage_gun_", variants: 1, priority: 2, cooldownSec: 5.5f, cooldownGroup: DamageCooldownGroup), // PlayerDamageGun
            new("voice_cp_captured_", variants: 2, priority: 3, cooldownSec: 4.5f),                        // CpCaptured
            new("voice_cp_lost_", variants: 2, priority: 3, cooldownSec: 4.5f),                        // CpLost
            new("voice_armor_bounce_", variants: 2, priority: 1, cooldownSec: 5.0f),                        // ArmorBounce
            new("voice_smoke_deploy_", variants: 2, priority: 1, cooldownSec: 4.5f),                        // SmokeDeploy
            new("voice_wave_clear_", variants: 2, priority: 4, cooldownSec: 4.0f, bypassGlobalGap: true)  // WaveClear
        };

        private AudioClip[][] _voiceClips;
        private int[] _lastVoiceClipIndex;
        private float[] _voiceEventCooldownUntil;
        private readonly Dictionary<int, float> _voiceGroupCooldownUntil = new();
        private int _activeVoicePriority;
        private float _activeVoiceEndTime;
        private sbyte _lastVoiceCpOwner;

        private string LocalVoiceFaction =>
            _multiplayer.IsInBattle && !MultiplayerSessionConnection.IsAlphaSlot(_multiplayer.LocalSlot) ? "enemy" : "ally";

        private int LocalVoiceTeamSign =>
            _multiplayer.IsInBattle && !MultiplayerSessionConnection.IsAlphaSlot(_multiplayer.LocalSlot) ? -1 : 1;

        private async UniTask LoadVoiceResourcesAsync(CancellationToken cancellationToken)
        {
            _voiceClips = new AudioClip[VoiceEventCount][];
            _lastVoiceClipIndex = new int[VoiceEventCount];
            _voiceEventCooldownUntil = new float[VoiceEventCount];
            Array.Fill(_lastVoiceClipIndex, -1);

            for (int i = 0; i < VoiceEventCount; i++)
            {
                VoiceRule rule = VoiceRules[i];
                var clips = new List<AudioClip>(rule.Variants);
                for (int variant = 1; variant <= rule.Variants; variant++)
                {
                    var clip = await LoadBattleClipAsync($"Voice/{rule.Prefix}{variant:00}", cancellationToken);
                    if (clip != null) clips.Add(clip);
                }
                _voiceClips[i] = clips.ToArray();
                _sceneLoader.ReportBattleInitialization(Mathf.Lerp(0.18f, 0.34f, (i + 1f) / VoiceEventCount));
            }
        }

        private void ResetVoiceState()
        {
            _audio?.StopVoice();
            _activeVoicePriority = 0;
            _activeVoiceEndTime = 0f;
            _voiceGroupCooldownUntil.Clear();
            if (_voiceEventCooldownUntil != null)
                Array.Clear(_voiceEventCooldownUntil, 0, _voiceEventCooldownUntil.Length);
            ResetControlPointVoiceTracking();
        }

        private void ResetControlPointVoiceTracking()
        {
            _lastVoiceCpOwner = ControlPoint.Owner;
        }

        private void SyncControlPointVoice()
        {
            if (!UsesControlPoints || _phase != GamePhase.Playing) return;
            sbyte owner = ControlPoint.Owner;
            if (owner == _lastVoiceCpOwner) return;
            _lastVoiceCpOwner = owner;

            if (owner == 0) return;
            int localSign = LocalVoiceTeamSign;
            PlayBattleVoice(owner == localSign ? BattleVoiceEvent.CpCaptured : BattleVoiceEvent.CpLost);
        }

        private bool PlayBattleVoice(BattleVoiceEvent voiceEvent)
        {
            if (_audio == null || !sfxEnabled || sfxVolume <= 0.001f || _voiceClips == null)
                return false;

            int idx = (int)voiceEvent;
            if (idx < 0 || idx >= VoiceEventCount)
                return false;

            AudioClip[] pool = _voiceClips[idx];
            if ((pool == null || pool.Length == 0) &&
                (voiceEvent == BattleVoiceEvent.PlayerDamageTrack || voiceEvent == BattleVoiceEvent.PlayerDamageGun))
            {
                idx = (int)BattleVoiceEvent.PlayerDamage;
                pool = _voiceClips[idx];
            }
            if (pool == null || pool.Length == 0)
                return false;

            VoiceRule rule = VoiceRules[idx];
            float now = Time.unscaledTime;

            if (now < _voiceEventCooldownUntil[idx])
                return false;

            if (rule.CooldownGroup >= 0 &&
                _voiceGroupCooldownUntil.TryGetValue(rule.CooldownGroup, out float groupUntil) &&
                now < groupUntil)
                return false;

            bool isPlaying = _audio.IsVoicePlaying && now < _activeVoiceEndTime;
            if (isPlaying)
            {
                if (rule.Priority <= _activeVoicePriority)
                    return false;
            }
            else if (!rule.BypassGlobalGap && now < _activeVoiceEndTime + VoiceGlobalGapSec)
            {
                return false;
            }

            int pick = PickNonRepeatingVoiceIndex(idx, pool.Length);
            AudioClip clip = pool[pick];
            if (clip == null) return false;

            _audio.PlayVoice(clip, sfxEnabled, sfxVolume);
            _activeVoicePriority = rule.Priority;
            _activeVoiceEndTime = now + clip.length;
            _voiceEventCooldownUntil[idx] = now + rule.CooldownSec;
            if (rule.CooldownGroup >= 0)
                _voiceGroupCooldownUntil[rule.CooldownGroup] = now + rule.CooldownSec;

            return true;
        }

        private int PickNonRepeatingVoiceIndex(int eventIdx, int length)
        {
            if (length <= 1)
            {
                _lastVoiceClipIndex[eventIdx] = 0;
                return 0;
            }

            int prev = _lastVoiceClipIndex[eventIdx];
            int pick = UnityEngine.Random.Range(0, length);
            if (pick == prev)
                pick = (pick + 1 + UnityEngine.Random.Range(0, length - 1)) % length;

            _lastVoiceClipIndex[eventIdx] = pick;
            return pick;
        }

        private void NotifyVoiceBattleStart()
        {
            if (battleMode == BattleMode.FiringRange) return;
            PlayBattleVoice(BattleVoiceEvent.BattleStart);
        }

        private void NotifyVoiceWaveClear()
        {
            PlayBattleVoice(BattleVoiceEvent.WaveClear);
        }

        private void NotifyVoiceRoundFinished(string winner)
        {
            bool playerWon = string.Equals(winner, LocalVoiceFaction, StringComparison.OrdinalIgnoreCase);
            PlayBattleVoice(playerWon ? BattleVoiceEvent.Victory : BattleVoiceEvent.Defeat);
        }

        private void NotifyVoiceSmokeDeployed(TankEntity tank)
        {
            if (tank == null || !tank.isPlayer || _phase != GamePhase.Playing) return;
            PlayBattleVoice(BattleVoiceEvent.SmokeDeploy);
        }

        private void NotifyVoiceArmorImpact(BattleEffect effect, TankEntity target, bool firedLocally)
        {
            if (target == null || _phase != GamePhase.Playing) return;
            if (effect.Projectile == ProjectileKind.MG) return;

            TankHit hit = effect.Hit;

            if (firedLocally && !target.isPlayer && target.factionTeam != LocalVoiceFaction)
            {
                if (effect.Loss != TankLoss.None)
                {
                    if (!effect.EndsRound)
                        PlayBattleVoice(BattleVoiceEvent.EnemyKill);
                    return;
                }

                if (effect.Bounced || (hit.Armor != ArmorHit.Penetrated && hit.Ammo != AmmoType.HE))
                {
                    PlayBattleVoice(BattleVoiceEvent.ShotRicochet);
                    return;
                }

                if (hit.Ignited || hit.Modules != TankModule.None || hit.CrewLost > 0)
                    PlayBattleVoice(BattleVoiceEvent.CriticalHit);
                return;
            }

            if (target.isPlayer)
            {
                if (effect.Loss != TankLoss.None)
                {
                    if (!effect.EndsRound)
                        PlayBattleVoice(BattleVoiceEvent.Defeat);
                    return;
                }

                if (effect.Bounced || (hit.Armor != ArmorHit.Penetrated && hit.Ammo != AmmoType.HE))
                {
                    PlayBattleVoice(BattleVoiceEvent.ArmorBounce);
                    return;
                }

                if (hit.Ignited)
                {
                    PlayBattleVoice(BattleVoiceEvent.PlayerFire);
                    return;
                }

                if (hit.Modules != TankModule.None)
                    PlayPlayerModuleDamageVoice(hit.Modules);
            }
        }

        private void NotifyVoiceSplashHit(TankEntity target, TankHit hit, TankLoss loss)
        {
            if (target == null || !target.isPlayer || loss != TankLoss.None || _phase != GamePhase.Playing)
                return;

            if (hit.Ignited)
                PlayBattleVoice(BattleVoiceEvent.PlayerFire);
            else if (hit.Modules != TankModule.None)
                PlayPlayerModuleDamageVoice(hit.Modules);
        }

        private void NotifyVoiceTankLoss(TankEntity target)
        {
            if (target == null || _phase != GamePhase.Playing) return;
            if (WillRoundEndAfterKill(target)) return;

            if (target.isPlayer)
            {
                PlayBattleVoice(BattleVoiceEvent.Defeat);
                return;
            }

            if (target.factionTeam != LocalVoiceFaction)
                PlayBattleVoice(BattleVoiceEvent.EnemyKill);
        }

        private void PlayPlayerModuleDamageVoice(TankModule modules)
        {
            const TankModule tracks = TankModule.LeftRunningGear | TankModule.RightRunningGear;
            if ((modules & tracks) != 0)
                PlayBattleVoice(BattleVoiceEvent.PlayerDamageTrack);
            else if ((modules & TankModule.Weapon) != 0)
                PlayBattleVoice(BattleVoiceEvent.PlayerDamageGun);
            else
                PlayBattleVoice(BattleVoiceEvent.PlayerDamage);
        }
    }
}
