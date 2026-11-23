
using System;
using UnityEngine;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    public partial class BattleHUD
    {
        [Serializable]
        private sealed class MenuLayout
        {
            public GameObject resultOverlay;
            public CanvasGroup resultCanvasGroup;
            public GameObject resultHits;
            public Text resultTitle;
            public Text resultSubtitle;
            public PixelMenuButton resultPrimary;
            public PixelMenuButton resultSecondary;
            public GameObject pauseOverlay;
            public GameObject pauseHits;
            public Text nextRivalLabel;
            public PixelMenuButton resume;
            public PixelMenuButton settings;
            public PixelMenuButton nextAction;
            public PixelMenuButton exit;
        }

        [SerializeField] private MenuLayout offlineMenu;
        [SerializeField] private MenuLayout hostMenu;
        [SerializeField] private MenuLayout guestMenu;

        [SerializeField] private PixelMenuButton sfxDown;
        [SerializeField] private PixelMenuButton sfxToggle;
        [SerializeField] private PixelMenuButton sfxUp;
        [SerializeField] private PixelMenuButton bgmDown;
        [SerializeField] private PixelMenuButton bgmToggle;
        [SerializeField] private PixelMenuButton bgmUp;
        [SerializeField] private PixelMenuButton driveMode;
        [SerializeField] private PixelMenuButton enemyAi;
        [SerializeField] private PixelMenuButton graphicsMode;
        [SerializeField] private PixelMenuButton scalingMode;
        [SerializeField] private PixelMenuButton backToPause;
        [SerializeField] private Text popupPrefab;


        private void InitializeLayout()
        {
            _pixelCanvas.worldCamera = _windowbox.HudCamera;
            _pixelCanvas.enabled = true;
            _menuHitRoot.SetParent(_windowbox.HitOverlayRect, false);

            Vector2 radarSize = _radarImage.rectTransform.rect.size;
            _radarWidth = Mathf.RoundToInt(radarSize.x);
            _radarHeight = Mathf.RoundToInt(radarSize.y);
            _radarTex = new Texture2D(_radarWidth, _radarHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _radarPixels = new Color32[_radarWidth * _radarHeight];
            _radarImage.texture = _radarTex;
            AttachCutaway(_vehicleStatusGraphic, _vehicleStatusBackdrop);
            AttachCutaway(_hitCameraGraphic, _hitCameraBackdrop);

            BindBattleMenu();
            BindSettings();
            RefreshMenuLabels();
        }

        private static void AttachCutaway(TankCutawayGraphic graphic, RawImage backdrop)
        {
            var size = backdrop.rectTransform.rect.size;
            graphic.AttachBackdrop(backdrop, Mathf.RoundToInt(size.x), Mathf.RoundToInt(size.y));
        }

        private void BindBattleMenu()
        {
            bool online = _multiplayer.IsInBattle;
            bool isHost = online && _multiplayer.Connection.IsHost;
            MenuLayout layout = online ? (isHost ? hostMenu : guestMenu) : offlineMenu;
            _resultOverlay = layout.resultOverlay;
            _resultCanvasGroup = layout.resultCanvasGroup;
            _resultHitsGroup = layout.resultHits;
            _resultTitle = layout.resultTitle;
            _resultSubtitle = layout.resultSubtitle;
            _menuOverlay = layout.pauseOverlay;
            _menuHitsGroup = layout.pauseHits;
            _btnNextRivalLabel = layout.nextRivalLabel;

            layout.resume.Bind(CloseAllMenus);
            layout.settings.Bind(() => SetSettingsOpen(true));
            layout.exit.Bind(() =>
            {
                CloseAllMenus();
                _mgr.ReturnToHangar();
            });
            if (online)
            {
                layout.resultPrimary.Bind(isHost ? _mgr.ReturnToOnlineRoom : _mgr.ReturnToHangar);
                if (isHost) layout.nextAction.Bind(_mgr.ReturnToOnlineRoom);
            }
            else
            {
                layout.resultPrimary.Bind(_mgr.ResetRound);
                layout.resultSecondary.Bind(_mgr.ReturnToHangar);
                layout.nextAction.Bind(() =>
                {
                    CloseAllMenus();
                    _mgr.ResetRound();
                });
            }
        }

        private void BindSettings()
        {
            BindSetting(sfxDown, () => _mgr.StepSfxVolume(-0.1f));
            BindSetting(sfxToggle, _mgr.ToggleSfxEnabled);
            BindSetting(sfxUp, () => _mgr.StepSfxVolume(0.1f));
            BindSetting(bgmDown, () => _mgr.StepBgmVolume(-0.1f));
            BindSetting(bgmToggle, _mgr.ToggleBgmEnabled);
            BindSetting(bgmUp, () => _mgr.StepBgmVolume(0.1f));
            BindSetting(driveMode, () =>
            {
                _mgr.arcadeWASD = !_mgr.arcadeWASD;
                _mgr.SyncSettingsToSession();
            });
            BindSetting(enemyAi, () =>
            {
                _mgr.aiEnabled = !_mgr.aiEnabled;
                _mgr.SyncSettingsToSession();
            });
            BindSetting(graphicsMode, () =>
            {
                _mgr.TogglePostProcess();
                _mgr.SyncSettingsToSession();
            });
            BindSetting(scalingMode, () =>
            {
                _mgr.integerWindowbox = !_mgr.integerWindowbox;
                _mgr.SyncSettingsToSession();
            });
            backToPause.Bind(() => SetSettingsOpen(false, reopenPauseMenu: true));
        }

        private void BindSetting(PixelMenuButton button, Action apply)
        {
            button.Bind(() =>
            {
                apply();
                RefreshMenuLabels();
            });
        }
    }
}
