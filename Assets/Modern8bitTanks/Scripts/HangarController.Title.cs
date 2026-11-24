using UnityEngine;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    /// <summary>
    /// Title stage and battle setup menu.
    /// </summary>
    public partial class HangarController
    {
        private bool _titleOpen = false;
        private bool _titleTransitioning = false;
        private float _titleExitTimer = 0f;
        private float _titleTime;

        private bool _deployOpen = false;
        private bool _deployFromTree = false;

        // UI Groups
        [SerializeField] private GameObject _titleVisualGroup;
        [SerializeField] private GameObject _titleHitsGroup;
        [SerializeField] private GameObject _deployVisualGroup;
        [SerializeField] private GameObject _deployHitsGroup;

        // Deploy Modal UI refs
        [SerializeField] private Text _deployVehicleLabel;
        [SerializeField] private Image _deployMode1v1Bg;
        [SerializeField] private Image _deployMode2v2Bg;
        [SerializeField] private Image _deployMode5v5Bg;
        [SerializeField] private Image _deployModeWaveBg;
        [SerializeField] private Image _deployModeRangeBg;
        [SerializeField] private Image _deployMapDesertBg;
        [SerializeField] private Image _deployMapCityBg;
        [SerializeField] private Image _deployMapForestBg;

        [SerializeField] private Transform _titleStageRoot;
        [SerializeField] private TankEntity[] _titleWingmen;
        private Vector2[] _titleWingPositions;
        private const float TitleTankAngle = 72f;

        private void InitializeTitleStage()
        {
            _titleWingPositions = new Vector2[_titleWingmen.Length];
            for (int i = 0; i < _titleWingmen.Length; i++)
            {
                var tank = _titleWingmen[i];
                if (tank == null) continue;
                _titleWingPositions[i] = tank.Position2D;
                string id = tank.tankType;
                var vehicle = _vehicleMap.TryGetValue(id, out var found) ? found : GetSelectedVehicle();
                if (vehicle == null) continue;
                if (tank.TrySpawnAsVehicle(vehicle, LoadCachedHull(vehicle.id), LoadCachedTurret(vehicle.id),
                    tank.Position2D, TitleTankAngle, _stackFlashMat))
                    tank.SyncVisuals(squash, spacingPx);
            }
        }

        private Texture2D LoadCachedHull(string id)
        {
            if (!_hullTexCache.TryGetValue(id, out Texture2D tex))
            {
                tex = Resources.Load<Texture2D>($"Vehicles/{id}_hull_slices");
                _hullTexCache[id] = tex;
            }
            return tex;
        }

        private Texture2D LoadCachedTurret(string id)
        {
            if (!_turretTexCache.TryGetValue(id, out Texture2D tex))
            {
                tex = Resources.Load<Texture2D>($"Vehicles/{id}_turret_slices");
                _turretTexCache[id] = tex;
            }
            return tex;
        }

        // ============================================================
        // 3. Title / Garage / Deploy State Transitions
        // ============================================================
        private void SetTitleOpen(bool open)
        {
            _titleOpen = open;
            _titleTransitioning = false;
            _titleExitTimer = 0f;
            _titleTime = 0f;
            _atmosphere?.SetVisible(!open);

            if (open)
            {
                _treeOpen = false;
                _deployOpen = false;
                _settingsOpen = false;
                _xrayMode = false;
                if (_hangarXrayWrap != null) _hangarXrayWrap.SetActive(false);
            }
            else
            {
                _sessionState.HasEnteredGarage = true;
            }

            if (_titleStageRoot != null) _titleStageRoot.gameObject.SetActive(open);
            if (_titleVisualGroup != null) _titleVisualGroup.SetActive(open);
            if (_garageVisualGroup != null) _garageVisualGroup.SetActive(!open && !_treeOpen);
            if (_treeVisualGroup != null) _treeVisualGroup.SetActive(!open && _treeOpen);
            if (_deployVisualGroup != null) _deployVisualGroup.SetActive(!open && _deployOpen);

            if (!open && showcaseTank != null)
            {
                showcaseTank.gameObject.SetActive(true);
                showcaseTank.Position2D = new Vector2(WorldW * 0.5f, WorldH * 0.5f + 0.15f);
            }

            SyncActiveHitGroups();
            RefreshAllUI();
        }

        private void BeginEnterGarageFromTitle()
        {
            if (!_titleOpen || _titleTransitioning) return;
            _titleTransitioning = true;
            _titleExitTimer = 0f;
            PlayHangarPreviewSfx();
        }

        private void SetDeployModalOpen(bool open, bool fromTree = false)
        {
            if (open && _choosingOnlineVehicle)
            {
                ConfirmOnlineVehicle();
                return;
            }
            if (open) _deployFromTree = fromTree;
            _deployOpen = open;
            if (_deployVisualGroup != null) _deployVisualGroup.SetActive(open);
            SyncActiveHitGroups();
            if (open) RefreshAllUI();
        }

        private void SyncActiveHitGroups()
        {
            bool settings = !_onlineOpen && _settingsOpen;
            bool deploy = !_onlineOpen && !settings && _deployOpen;
            bool title = !_onlineOpen && !settings && !deploy && _titleOpen;
            bool tree = !_onlineOpen && !settings && !deploy && !title && _treeOpen;
            bool garage = !_onlineOpen && !settings && !deploy && !title && !tree;

            if (_onlineHits != null) _onlineHits.SetActive(_onlineOpen);
            if (_settingsHitsGroup != null) _settingsHitsGroup.SetActive(settings);
            if (_deployHitsGroup != null) _deployHitsGroup.SetActive(deploy);
            if (_titleHitsGroup != null) _titleHitsGroup.SetActive(title);
            if (_treeHitsGroup != null) _treeHitsGroup.SetActive(tree);
            if (_garageHitsGroup != null) _garageHitsGroup.SetActive(garage);
        }

        private void RefreshDeployModalUI()
        {
            VehicleDef sel = GetSelectedVehicle();
            if (_deployVehicleLabel != null && sel != null)
                _deployVehicleLabel.text = $"{sel.name.ToUpper()}  •  TIER {sel.TierRoman} {sel.role}";

            if (_deployMode1v1Bg != null) _deployMode1v1Bg.color = _sessionState.SelectedBattleMode == BattleMode.Duel1v1 ? BtnSelected : BtnBg;
            if (_deployMode2v2Bg != null) _deployMode2v2Bg.color = _sessionState.SelectedBattleMode == BattleMode.Squad2v2 ? BtnSelected : BtnBg;
            if (_deployMode5v5Bg != null) _deployMode5v5Bg.color = _sessionState.SelectedBattleMode == BattleMode.Battle5v5 ? BtnSelected : BtnBg;
            if (_deployModeWaveBg != null) _deployModeWaveBg.color = _sessionState.SelectedBattleMode == BattleMode.SurvivalWave ? BtnSelected : BtnBg;
            if (_deployModeRangeBg != null) _deployModeRangeBg.color = _sessionState.SelectedBattleMode == BattleMode.FiringRange ? BtnSelected : BtnBg;

            if (_deployMapDesertBg != null) _deployMapDesertBg.color = _sessionState.SelectedBattleMap == BattleMap.Desert ? BtnSelected : BtnBg;
            if (_deployMapCityBg != null) _deployMapCityBg.color = _sessionState.SelectedBattleMap == BattleMap.City ? BtnSelected : BtnBg;
            if (_deployMapForestBg != null) _deployMapForestBg.color = _sessionState.SelectedBattleMap == BattleMap.Forest ? BtnSelected : BtnBg;
        }

        private void UpdateTitleStage(float dt)
        {
            if (!_titleOpen) return;

            _titleTime += dt;
            float blend = 0f;
            if (_titleTransitioning)
            {
                const float transitionDuration = 0.42f;
                _titleExitTimer += dt;
                if (_titleExitTimer >= transitionDuration)
                {
                    SetTitleOpen(false);
                    return;
                }
                blend = Mathf.SmoothStep(0f, 1f, _titleExitTimer / transitionDuration);
            }

            Vector2 center = new Vector2(WorldW * 0.5f, WorldH * 0.5f);
            if (showcaseTank != null)
            {
                if (!showcaseTank.gameObject.activeSelf) showcaseTank.gameObject.SetActive(true);
                Vector2 position = center + Vector2.Lerp(new Vector2(0.15f, -0.60f), new Vector2(0f, 0.15f), blend);
                UpdateTitleTankPose(showcaseTank, position, _titleTime, blend);
            }

            for (int i = 0; i < _titleWingmen.Length; i++)
            {
                var tank = _titleWingmen[i];
                if (tank != null)
                    UpdateTitleTankPose(tank, _titleWingPositions[i], _titleTime + (i + 1) * 1.7f, 0f);
            }

            if (_cam != null)
            {
                float targetGarageZoom = GetAdaptiveShowcaseZoom(GetSelectedVehicle());
                float titleZoom = Mathf.Lerp(1.42f, targetGarageZoom, blend);
                _cam.orthographicSize = ((PixelWindowbox.RenderHeight / PPU) * 0.5f) / titleZoom;
                float camX = Mathf.Round(center.x * PPU) / PPU;
                float camY = Mathf.Round((center.y + Mathf.Lerp(0.15f, 0f, blend)) * PPU) / PPU;
                _cam.transform.position = new Vector3(camX, camY, -20f);
                _cam.transform.rotation = Quaternion.identity;
            }
        }

        private void UpdateTitleTankPose(TankEntity tank, Vector2 position, float phase, float garageBlend)
        {
            float idleWeight = 1f - garageBlend;
            tank.Position2D = position + Vector2.up * (Mathf.Sin(phase * 1.2f) * 0.10f * idleWeight);
            float heading = TitleTankAngle + Mathf.Sin(phase * 0.85f) * 3f;
            tank.hullAngleDeg = Mathf.LerpAngle(heading, 35f + _manualAngleOffset, garageBlend);
            float traverse = tank.vehicleDef?.fixedGunTraverse is > 0 ? Mathf.Min(22f, tank.vehicleDef.fixedGunTraverse) : 22f;
            tank.turretAngleDeg = tank.hullAngleDeg + Mathf.Sin(phase * 0.78f) * traverse * idleWeight;
            tank.SyncVisuals(squash, spacingPx);
        }
    }
}
