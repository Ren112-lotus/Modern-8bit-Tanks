using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Modern8bitTanks
{
    public partial class HangarController
    {
        private float _currentShowcaseZoom = 2.0f;

        private bool _xrayMode = false;
        private bool _draggingTank;
        private float _lastDragX;
        private float _manualAngleOffset;

        private float GetAdaptiveShowcaseZoom(VehicleDef def)
        {
            if (def == null) return showcaseZoom;
            float len = def.hullLength > 0 ? def.hullLength : 38f;
            if (len <= 26f) return 2.50f; // Micro tankettes (Wiesel, MS-1, M50, ASU-57): crystal-clear close framing!
            if (len <= 34f) return 2.25f; // Light tanks (Stuart, Ha-Go, Pz II, Sheridan)
            if (len <= 44f) return 2.00f; // Standard MBTs: exact 2x pixel scale!
            return 1.80f;                 // Massive heavies (Tiger II, A7V, M103)
        }

        private void SetupMapBackdrop()
        {
            mapBackdropRenderer.enabled = true;
            mapBackdropRenderer.transform.position = new Vector3(WorldW * 0.5f, WorldH * 0.5f, 5f);
            mapBackdropRenderer.sortingOrder = -30000;
            if (_groundHd2dMat != null) mapBackdropRenderer.sharedMaterial = _groundHd2dMat;
        }

        private void RefreshMapBackdropTheme(BattleMap map)
        {
            if (_titleOpen)
            {
                mapBackdropRenderer.sprite = Resources.Load<Sprite>($"Hangar/{map}Backdrop");
                mapBackdropRenderer.transform.localScale = Vector3.one;
            }
            else
            {
                mapBackdropRenderer.sprite = _garageBackdrop;
                FitGarageBackdrop();
            }

            _postProcess?.ApplyMapTheme(_titleOpen ? map : BattleMap.City);
        }

        private void FitGarageBackdrop()
        {
            float height = _cam.orthographicSize * 2f;
            Vector3 size = _garageBackdrop.bounds.size;
            float scale = Mathf.Max(height * _cam.aspect / size.x, height / size.y);
            mapBackdropRenderer.transform.localScale = new Vector3(scale, scale, 1f);
        }

        private void SetupCenterShowcaseTank()
        {

            if (showcaseTank.hullStack != null && showcaseTank.hullStack.sliceMaterial != null)
                _stackFlashMat = showcaseTank.hullStack.sliceMaterial;

            Vector2 center = new Vector2(WorldW * 0.5f, WorldH * 0.5f + 0.15f);
            showcaseTank.transform.position = new Vector3(center.x, center.y, 0f);
            showcaseTank.isPlayer = false;
            showcaseTank.factionTeam = "ally";
        }

        private void ApplySelectedTankToTurntable()
        {
            VehicleDef def = GetSelectedVehicle();
            if (def == null || showcaseTank == null) return;

            Vector2 center = new Vector2(WorldW * 0.5f, WorldH * 0.5f + 0.15f);
            var (hullSheet, turretSheet) = _vehicleSheets.Get(def.id);
            if (!showcaseTank.TrySpawnAsVehicle(def, hullSheet, turretSheet,
                center, 35f + _manualAngleOffset, _stackFlashMat))
                return;

            showcaseTank.SetAssemblyDrop(1f);
            ClearArmorPreviewHistory();
            // A larger vehicle must fit as soon as it replaces the previous model.
            _currentShowcaseZoom = Mathf.Min(_currentShowcaseZoom, GetAdaptiveShowcaseZoom(def));
            RefreshAllUI();
        }

        private void SetXRayMode(bool on)
        {
            if (!on) ResetArmorPreviewDamage();
            _xrayMode = on;
            if (_hangarXrayWrap != null) _hangarXrayWrap.SetActive(on && !_treeOpen);
            if (_btnXrayBg != null) _btnXrayBg.color = on ? new Color(0.22f, 0.54f, 0.62f, 1f) : BtnBg;
            if (showcaseTank != null) showcaseTank.gameObject.SetActive(!on || _treeOpen);
            RefreshAllUI();
        }

        private bool ReadShowcasePointer(Mouse mouse)
        {
            bool fireArmorPreview = false;
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (_xrayMode)
            {
                if (_hangarXrayGraphic.HangarShotBusy)
                {
                    _armorPreviewPress = null;
                    _draggingTank = false;
                }
                else
                {
                    fireArmorPreview = ReadArmorPreviewClick(mouse, overUI);
                }
            }
            else if (!overUI && mouse.leftButton.wasPressedThisFrame)
            {
                _lastDragX = mouse.position.ReadValue().x;
                _draggingTank = float.IsFinite(_lastDragX);
            }
            if (mouse.leftButton.wasReleasedThisFrame)
                _draggingTank = false;
            if (_draggingTank)
            {
                float curX = mouse.position.ReadValue().x;
                if (float.IsFinite(curX))
                {
                    float dx = curX - _lastDragX;
                    _lastDragX = curX;
                    _manualAngleOffset = Mathf.Repeat(_manualAngleOffset + dx * 0.45f, 360f);
                }
            }
            return fireArmorPreview;
        }

        private void UpdateShowcase(Keyboard keyboard, Mouse mouse, bool fireArmorPreview, float dt)
        {
            // Adaptive crisp showcase zoom (automatically frames small tankettes close-up & MBTs at crisp 2x pixels!)
            float targetZoom = GetAdaptiveShowcaseZoom(GetSelectedVehicle());
            _currentShowcaseZoom = Mathf.Lerp(_currentShowcaseZoom, targetZoom, 1f - Mathf.Exp(-10f * dt));
            if (Mathf.Abs(_currentShowcaseZoom - targetZoom) < 0.006f) _currentShowcaseZoom = targetZoom;

            // Center camera squarely on the showcase turntable
            if (_cam != null)
            {
                float orthoSize = ((PixelWindowbox.RenderHeight / PPU) * 0.5f) / _currentShowcaseZoom;
                _cam.orthographicSize = orthoSize;
                FitGarageBackdrop();
                float camX = Mathf.Round((WorldW * 0.5f) * PPU) / PPU;
                float camY = Mathf.Round((WorldH * 0.5f) * PPU) / PPU;
                _cam.transform.position = new Vector3(camX, camY, -20f);
                _cam.transform.rotation = Quaternion.identity;
            }

            if (showcaseTank != null)
            {
                bool inspect = _xrayMode && !_treeOpen;
                if (inspect && fireArmorPreview)
                    ResetArmorPreviewDamage();

                if (!_draggingTank && !inspect)
                    _manualAngleOffset = Mathf.Repeat(_manualAngleOffset + 24f * dt, 360f);
                showcaseTank.hullAngleDeg = Mathf.Repeat(35f + _manualAngleOffset, 360f);
                if (!inspect || !_hangarXrayGraphic.HangarShotBusy)
                    showcaseTank.turretAngleDeg = showcaseTank.hullAngleDeg + Mathf.Sin(Time.time * 0.95f) *
                        Mathf.Min(28f, showcaseTank.vehicleDef?.fixedGunTraverse is > 0 ? showcaseTank.vehicleDef.fixedGunTraverse : 28f);

                if (showcaseTank.gameObject.activeSelf == inspect)
                    showcaseTank.gameObject.SetActive(!inspect);
                showcaseTank.SyncVisuals(squash, spacingPx);
                if (inspect)
                {
                    _hangarXrayGraphic.ShowHangarInspect(showcaseTank, _cam, _windowbox.HudCamera);
                    UpdateArmorPreview(keyboard, mouse, fireArmorPreview, dt);
                }
            }
        }
    }
}
