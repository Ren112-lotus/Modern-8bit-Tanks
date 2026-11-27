using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Modern8bitTanks
{
    public partial class HangarController
    {
        [SerializeField] private PixelMenuButton garageTitleAction;
        [SerializeField] private PixelMenuButton garageSettingsAction;
        [SerializeField] private PixelMenuButton garageXrayAction;
        [SerializeField] private PixelMenuButton garageTreeAction;
        [SerializeField] private PixelMenuButton garagePreviousAction;
        [SerializeField] private PixelMenuButton garageNationAction;
        [SerializeField] private PixelMenuButton garageNextAction;
        [SerializeField] private PixelMenuButton garageOnlineAction;
        [SerializeField] private PixelMenuButton garageDeployAction;

        [SerializeField] private PixelMenuButton titleStartAction;
        [SerializeField] private PixelMenuButton titleOnlineAction;
        [SerializeField] private PixelMenuButton titleSettingsAction;

        [SerializeField] private PixelMenuButton deployDuelAction;
        [SerializeField] private PixelMenuButton deploySquadAction;
        [SerializeField] private PixelMenuButton deployCompanyAction;
        [SerializeField] private PixelMenuButton deploySurvivalAction;
        [SerializeField] private PixelMenuButton deployRangeAction;
        [SerializeField] private PixelMenuButton deployDesertAction;
        [SerializeField] private PixelMenuButton deployCityAction;
        [SerializeField] private PixelMenuButton deployForestAction;
        [SerializeField] private PixelMenuButton deployBackAction;
        [SerializeField] private PixelMenuButton deployConfirmAction;

        [SerializeField] private PixelMenuButton settingsSfxDownAction;
        [SerializeField] private PixelMenuButton settingsSfxToggleAction;
        [SerializeField] private PixelMenuButton settingsSfxUpAction;
        [SerializeField] private PixelMenuButton settingsBgmDownAction;
        [SerializeField] private PixelMenuButton settingsBgmToggleAction;
        [SerializeField] private PixelMenuButton settingsBgmUpAction;
        [SerializeField] private PixelMenuButton settingsDriveAction;
        [SerializeField] private PixelMenuButton settingsAiAction;
        [SerializeField] private PixelMenuButton settingsGraphicsAction;
        [SerializeField] private PixelMenuButton settingsDisplayAction;
        [SerializeField] private PixelMenuButton settingsCloseAction;

        [SerializeField] private PixelMenuButton onlineJoinAction;
        [SerializeField] private PixelMenuButton onlineCreateAction;
        [SerializeField] private PixelMenuButton onlineStartAction;
        [SerializeField] private PixelMenuButton onlineVehicleAction;
        [SerializeField] private PixelMenuButton onlineBackAction;


        private void InitializeMenus()
        {
            _pixelCanvas.worldCamera = _windowbox.HudCamera;
            _pixelCanvas.enabled = true;
            _menuHitRoot.SetParent(_windowbox.HitOverlayRect, false);

            var backdropSize = _hangarXrayBackdrop.rectTransform.sizeDelta;
            _hangarXrayGraphic.AttachBackdrop(_hangarXrayBackdrop, (int)backdropSize.x, (int)backdropSize.y);
            _onlineCode.onValidateInput = (_, _, c) =>
                c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' ? char.ToUpperInvariant(c) : '\0';

            garageTitleAction.Bind(() =>
            {
                if (_choosingOnlineVehicle) ConfirmOnlineVehicle();
                else SetTitleOpen(true);
            });
            garageSettingsAction.Bind(() => SetSettingsOpen(true));
            garageXrayAction.Bind(() => SetXRayMode(!_xrayMode));
            garageTreeAction.Bind(() => SetTechTreeOpen(true));
            garagePreviousAction.Bind(() => CycleSelectedInCurrentNation(-1));
            garageNationAction.Bind(CycleNationInGarage);
            garageNextAction.Bind(() => CycleSelectedInCurrentNation(1));
            garageOnlineAction.Bind(() =>
            {
                if (_choosingOnlineVehicle) ConfirmOnlineVehicle();
                else SetOnlineRoomOpen(true);
            });
            garageDeployAction.Bind(() => SetDeployModalOpen(true));

            titleStartAction.Bind(BeginEnterGarageFromTitle);
            titleOnlineAction.Bind(() => SetOnlineRoomOpen(true));
            titleSettingsAction.Bind(() => SetSettingsOpen(true));

            deployDuelAction.Bind(() => SelectBattleMode(BattleMode.Duel1v1));
            deploySquadAction.Bind(() => SelectBattleMode(BattleMode.Squad2v2));
            deployCompanyAction.Bind(() => SelectBattleMode(BattleMode.Battle5v5));
            deploySurvivalAction.Bind(() => SelectBattleMode(BattleMode.SurvivalWave));
            deployRangeAction.Bind(() => SelectBattleMode(BattleMode.FiringRange));
            deployDesertAction.Bind(() => SelectBattleMap(BattleMap.Desert));
            deployCityAction.Bind(() => SelectBattleMap(BattleMap.City));
            deployForestAction.Bind(() => SelectBattleMap(BattleMap.Forest));
            deployBackAction.Bind(() => SetDeployModalOpen(false));
            deployConfirmAction.Bind(LaunchBattleScene);

            settingsSfxDownAction.Bind(() => ChangeSfxVolume(() => _sessionState.StepSfxVolume(-0.1f)));
            settingsSfxToggleAction.Bind(() => ChangeSfxVolume(_sessionState.ToggleSfxEnabled));
            settingsSfxUpAction.Bind(() => ChangeSfxVolume(() => _sessionState.StepSfxVolume(0.1f)));
            settingsBgmDownAction.Bind(() => ChangeBgmVolume(() => _sessionState.StepBgmVolume(-0.1f)));
            settingsBgmToggleAction.Bind(() => ChangeBgmVolume(_sessionState.ToggleBgmEnabled));
            settingsBgmUpAction.Bind(() => ChangeBgmVolume(() => _sessionState.StepBgmVolume(0.1f)));
            settingsDriveAction.Bind(() =>
            {
                _sessionState.ArcadeWASD = !_sessionState.ArcadeWASD;
                SaveMenuSettings();
            });
            settingsAiAction.Bind(() =>
            {
                _sessionState.AiEnabled = !_sessionState.AiEnabled;
                SaveMenuSettings();
            });
            settingsGraphicsAction.Bind(() =>
            {
                _sessionState.PostProcessEnabled = !_sessionState.PostProcessEnabled;
                _postProcess.SetEnabled(_sessionState.PostProcessEnabled);
                _windowbox.SetHd2dMode(_sessionState.PostProcessEnabled);
                SaveMenuSettings();
            });
            settingsDisplayAction.Bind(() =>
            {
                _sessionState.IntegerWindowbox = !_sessionState.IntegerWindowbox;
                _windowbox.integerScale = _sessionState.IntegerWindowbox;
                SaveMenuSettings();
            });
            settingsCloseAction.Bind(() => SetSettingsOpen(false));

            onlineJoinAction.Bind(() => ConnectOnlineRoom(false));
            onlineCreateAction.Bind(() => ConnectOnlineRoom(true));
            onlineStartAction.Bind(StartOnlineMatch);
            onlineVehicleAction.Bind(BeginOnlineVehicleSelection);
            onlineBackAction.Bind(CloseOnlineRoom);

            SetOnlineRoomOpen(false);
        }

        private void SelectBattleMode(BattleMode mode)
        {
            _sessionState.SelectedBattleMode = mode;
            RefreshAllUI();
        }

        private void ChangeSfxVolume(Action change)
        {
            change();
            PlayHangarPreviewSfx();
            RefreshSettingsLabels();
        }

        private void ChangeBgmVolume(Action change)
        {
            change();
            RefreshSettingsLabels();
        }

        private void SaveMenuSettings()
        {
            _sessionState.SaveSettings();
            RefreshSettingsLabels();
        }

        private void HandleMenuKeyboard(Keyboard keyboard)
        {
            if (keyboard == null) return;

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                if (_settingsOpen) SetSettingsOpen(false);
                else if (_deployOpen) SetDeployModalOpen(false);
                else if (_treeOpen) SetTechTreeOpen(false);
                else if (_choosingOnlineVehicle) ConfirmOnlineVehicle();
                else SetSettingsOpen(true);
                return;
            }

            if (keyboard.oKey.wasPressedThisFrame && !_treeOpen && !_deployOpen)
            {
                SetSettingsOpen(!_settingsOpen);
                return;
            }

            if (_settingsOpen) return;

            bool confirm = keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
            if (_titleOpen)
            {
                if (confirm || keyboard.spaceKey.wasPressedThisFrame) BeginEnterGarageFromTitle();
                return;
            }

            if (_deployOpen)
            {
                if (confirm) LaunchBattleScene();
                return;
            }

            if (keyboard.tKey.wasPressedThisFrame)
                SetTechTreeOpen(!_treeOpen);
            else if (keyboard.xKey.wasPressedThisFrame && !_treeOpen)
                SetXRayMode(!_xrayMode);
            else if (confirm)
                SetDeployModalOpen(true, fromTree: _treeOpen);
            else if (!_treeOpen && keyboard.leftArrowKey.wasPressedThisFrame)
                CycleSelectedInCurrentNation(-1);
            else if (!_treeOpen && keyboard.rightArrowKey.wasPressedThisFrame)
                CycleSelectedInCurrentNation(1);
        }

        private void PlayHangarPreviewSfx()
        {
            if (_previewAudio == null || _previewSfxClip == null) return;
            if (!_sessionState.SfxEnabled || _sessionState.SfxVolume <= 0.001f) return;
            _previewAudio.Stop();
            _previewAudio.clip = _previewSfxClip;
            _previewAudio.volume = Mathf.Clamp01(0.62f * _sessionState.SfxVolume);
            _previewAudio.pitch = 1.0f;
            _previewAudio.Play();
        }

        private void RefreshSettingsLabels()
        {
            if (_setSfxLabel != null)
            {
                _setSfxLabel.text = BattleSessionState.FormatVolumeLabel("SE", _sessionState.SfxEnabled, _sessionState.SfxVolume);
                _setSfxLabel.color = (_sessionState.SfxEnabled && _sessionState.SfxVolume > 0.001f) ? MbtGreen : TextMuted;
            }
            if (_setBgmLabel != null)
            {
                _setBgmLabel.text = BattleSessionState.FormatVolumeLabel("BGM", _sessionState.BgmEnabled, _sessionState.BgmVolume);
                _setBgmLabel.color = (_sessionState.BgmEnabled && _sessionState.BgmVolume > 0.001f) ? LightCyan : TextMuted;
            }
            if (_setDriveLabel != null)
                _setDriveLabel.text = UiText.Get(_sessionState.ArcadeWASD ? "DRIVE: ARCADE WASD" : "DRIVE: TANK STEER");
            if (_setAiLabel != null)
                _setAiLabel.text = UiText.Get(_sessionState.AiEnabled ? "ENEMY AI: ON" : "ENEMY AI: OFF");
            if (_setGfxLabel != null)
                _setGfxLabel.text = UiText.Get(_sessionState.PostProcessEnabled ? "GFX: HD-2D" : "GFX: RETRO");
            if (_setViewLabel != null)
                _setViewLabel.text = UiText.Get(_sessionState.IntegerWindowbox ? "VIEW: INT WINDOWBOX" : "VIEW: 4:3");
        }

        private void SetSettingsOpen(bool open)
        {
            _settingsOpen = open;
            if (_settingsVisualGroup != null) _settingsVisualGroup.SetActive(open);
            SyncActiveHitGroups();
            if (open) RefreshSettingsLabels();
        }

    }
}
