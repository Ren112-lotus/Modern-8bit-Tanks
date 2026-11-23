using System;
using UnityEngine;

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
    }
}
