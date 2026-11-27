using UnityEngine;

namespace Modern8bitTanks
{
    public partial class BattleSceneController
    {
        // Audio clips (CC0 recordings + procedural mechanical SFX)
        private AudioClip _sfxCannonAbrams;
        private AudioClip _sfxCannonT90;
        private AudioClip _sfxRicochet;
        private AudioClip _sfxExplosion;
        private AudioClip _sfxMg;
        private AudioClip _sfxAbramsTurbine;
        private AudioClip _sfxT90Diesel;
        private AudioClip _sfxTrackClatter;
        private AudioClip _sfxTurretServo;
        private AudioClip _sfxReloadClank;
        private AudioClip _sfxSmokePop;
        private AudioClip _sfxMetalCrash;
        private AudioClip _sfxMissileLaunch;
        private AudioClip _sfxMissileHeavy;
        private AudioClip _sfxMissileMotor;
        private AudioClip _sfxBattleBgm;

        private Vector2 GetAudioListenerPos()
        {
            TankEntity player = GetPlayerTank();
            if (player != null && player.alive && _phase == GamePhase.Playing)
                return player.Position2D;
            return _camPos;
        }

        private void StartPlayerEngineAudio(TankEntity player)
        {
            if (player == null || _audio == null) return;
            bool isTurbine = player.vehicleDef != null
                ? player.vehicleDef.engine == "Turbine"
                : player.tankType == "abrams";
            _audio.StartPlayerLoops(isTurbine ? _sfxAbramsTurbine : _sfxT90Diesel);
        }

        private void StopLoopingAudio()
        {
            if (_audio != null) _audio.StopLoopsImmediate();
        }

        private void UpdateMechanicalAudio(TankEntity player, float dt)
        {
            if (_audio == null) return;
            bool active = sfxEnabled && sfxVolume > 0.001f && _phase == GamePhase.Playing && player != null && player.alive && (_hud == null || !_hud.IsMenuOpen);
            if (!active)
            {
                _audio.UpdatePlayerLoops(false, 0f, 0f, dt, sfxVolume);
                return;
            }

            float speed01 = Mathf.Clamp01(Mathf.Abs(player.moveSpeed) / (64f / PPU));
            float moving01 = Mathf.Max(speed01, Mathf.Abs(player.HullTurnInput) * 0.7f);
            float turn01 = Mathf.Clamp01(player.TurretTurnRateDeg / 220f);
            _audio.UpdatePlayerLoops(true, moving01, turn01, dt, sfxVolume);

            // Crisp metallic breech-lock CLANK inside the turret for main cannons / ATGMs (DRY, centered!)
            if (player.JustFinishedReloading && player.reloadDuration >= 0.65f)
            {
                PlaySFXAt(_sfxReloadClank, player.Position2D, SoundBus.Dry, 0.65f, centerPan: true);
            }
        }

        private void PlaySFXAt(
            AudioClip clip,
            Vector2 worldPos,
            SoundBus bus,
            float volume,
            bool centerPan = false,
            float pitchMin = 0.94f,
            float pitchMax = 1.06f,
            float maxDuration = 0f,
            int maxConcurrentForClip = 0,
            float minRetriggerSec = 0f)
        {
            if (_audio == null) return;
            _audio.PlayAt(
                clip,
                worldPos,
                GetAudioListenerPos(),
                bus,
                volume,
                sfxEnabled,
                pitchMin,
                pitchMax,
                centerPan,
                sfxVolume,
                maxDuration,
                maxConcurrentForClip,
                minRetriggerSec
            );
        }
    }
}
