using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    /// <summary>
    /// Title Screen (Sprite-Stacking Showcase Diorama + 3D Stacked Logo + Exploded-View Assembly)
    /// and Battle Mode / Map Selection Modal (opened after clicking BATTLE in Garage or Tech Tree).
    /// </summary>
    public partial class HangarController
    {
        private bool _titleOpen = false;
        private bool _titleTransitioning = false;
        private float _titleTime = 0f;
        private float _titleExitTimer = 0f;
        private bool _titleLandingDustFired = false;
        private bool _titleTurretLockDustFired = false;

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

        // 3D Sprite-Stacked Title Stage Objects
        [SerializeField] private Transform _titleStageRoot;
        [SerializeField] private SpriteStackRenderer _titleLogoStack;
        [SerializeField] private TankEntity[] _titleWingmen;
        [SerializeField] private SpriteStackRenderer[] _titleScenery;
        [SerializeField] private SpriteRenderer _titleDustPrefab;
        private Vector2[] _titleWingOffsets;
        private readonly List<TitlePropItem> _titleProps = new();
        private readonly List<TitleDustItem> _titleDusts = new();
        private float _titleDustTimer;

        private class TitlePropItem
        {
            public Transform root;
            public SortingGroup sg;
            public SpriteStackRenderer stack;
            public Vector2 pos;
        }

        private class TitleDustItem
        {
            public Transform tf;
            public SpriteRenderer sr;
            public Vector2 pos;
            public Vector2 vel;
            public float life;
            public float maxLife;
            public float baseSize;
            public Color baseColor;
        }

        // Convoy drives briskly toward East-Northeast (slice angle 72 deg) across the sunlit desert!
        private const float TitleConvoyAngleDeg = 72f;
        private const float TitleScrollSpeed = 3.2f;

        // ============================================================
        // 2. Build 3D Sprite-Stacked Title Stage (Logo + Wingmen + Props)
        // ============================================================
        private void InitializeTitleStage()
        {
            var center = new Vector2(WorldW * 0.5f, WorldH * 0.5f);
            _titleWingOffsets = new Vector2[_titleWingmen.Length];
            for (int i = 0; i < _titleWingmen.Length; i++)
            {
                var tank = _titleWingmen[i];
                string id = tank.tankType;
                _titleWingOffsets[i] = tank.Position2D - center;
                var vehicle = _vehicleMap.TryGetValue(id, out var found) ? found : GetSelectedVehicle();
                if (vehicle == null) continue;
                tank.TrySpawnAsVehicle(vehicle, LoadCachedHull(vehicle.id), LoadCachedTurret(vehicle.id),
                    tank.Position2D, TitleConvoyAngleDeg, _stackFlashMat);
            }

            foreach (var scenery in _titleScenery)
            {
                _titleProps.Add(new TitlePropItem
                {
                    root = scenery.transform,
                    sg = scenery.GetComponent<SortingGroup>(),
                    stack = scenery,
                    pos = scenery.transform.position
                });
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
            RefreshMapBackdropSprite(_sessionState.SelectedBattleMap);
            _titleTransitioning = false;
            _titleExitTimer = 0f;
            _titleTime = 0f;
            _titleLandingDustFired = false;
            _titleTurretLockDustFired = false;

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

            if (!open)
            {
                ClearTitleDust();
                if (_titleLogoStack != null) _titleLogoStack.dropProgress = 1f;
                for (int i = 0; i < _titleWingmen.Length; i++)
                    if (_titleWingmen[i] != null) _titleWingmen[i].SetAssemblyDrop(1f);
                if (showcaseTank != null)
                {
                    showcaseTank.SetAssemblyDrop(1f);
                    showcaseTank.gameObject.SetActive(true);
                    showcaseTank.Position2D = new Vector2(WorldW * 0.5f, WorldH * 0.5f + 0.15f);
                }
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

        // ============================================================
        // 4. Live Title Screen Sprite-Stacking Animation Update
        // ============================================================
        private void UpdateTitleStage(float dt)
        {
            if (!_titleOpen) return;

            _titleTime += dt;
            float exit01 = 0f;
            if (_titleTransitioning)
            {
                _titleExitTimer += dt;
                exit01 = Mathf.Clamp01(_titleExitTimer / 0.42f);
                if (exit01 >= 1f)
                {
                    SetTitleOpen(false);
                    return;
                }
            }

            Vector2 center = new Vector2(WorldW * 0.5f, WorldH * 0.5f);
            Vector2 convoyDir = SpriteStackCamera.Squash(TankEntity.SliceAngleToDir(TitleConvoyAngleDeg), squash).normalized;

            // 1. Slow, Graceful Layer-by-Layer Sky Descent (individual slices float down & stack one by one!)
            float leadDrop01 = _titleTransitioning ? 1f : Mathf.Clamp01((_titleTime - 0.18f) / 2.55f);
            float leadEase = 1f - Mathf.Pow(1f - leadDrop01, 2.2f);
            float tankSpacing = Mathf.Lerp(spacingPx * 1.38f, spacingPx, leadEase);

            // Soft landing dust puff when the lead tank's hull touches down, plus a crisp settle when turret locks in!
            if (!_titleLandingDustFired && leadDrop01 >= 0.56f)
            {
                _titleLandingDustFired = true;
                if (showcaseTank != null) showcaseTank.TriggerFireKick(0f);
                SpawnTitleDustBurst(center + new Vector2(0.15f, -0.60f), 8);
            }
            if (!_titleTurretLockDustFired && leadDrop01 >= 0.96f)
            {
                _titleTurretLockDustFired = true;
                if (showcaseTank != null) showcaseTank.TriggerFireKick(0f);
                SpawnTitleDustBurst(center + new Vector2(0.15f, -0.60f), 6);
            }

            // 2. Slow Layer-by-Layer Descending 3D Stacked Logo ("8-BIT TANKS" — 3.2s gentle float down!)
            if (_titleLogoStack != null)
            {
                float logoDrop01 = _titleTransitioning ? 1f : Mathf.Clamp01((_titleTime - 0.06f) / 3.20f);
                float logoEase = 1f - Mathf.Pow(1f - logoDrop01, 2.4f);
                float baseLogoSpacing = Mathf.Lerp(1.32f, 0.94f, logoEase) + Mathf.Sin(_titleTime * 1.6f) * 0.05f * logoEase;
                // On START click, logo expands vertically into floating slices & drifts cleanly away!
                float exitExplode = exit01 * exit01;
                _titleLogoStack.dropHeightPx = 58f;
                _titleLogoStack.dropProgress = logoDrop01;
                _titleLogoStack.spacingPx = baseLogoSpacing + exitExplode * 4.8f;
                _titleLogoStack.squash = 0.74f;
                _titleLogoStack.angleDeg = Mathf.Lerp(-10f, 0f, logoEase) + Mathf.Sin(_titleTime * 0.95f) * 9.5f * logoEase + exitExplode * 26f;
                _titleLogoStack.transform.position = new Vector3(
                    center.x,
                    center.y + 1.92f + Mathf.Sin(_titleTime * 1.35f) * 0.08f * logoEase + exitExplode * 3.8f,
                    0f
                );
                _titleLogoStack.ApplyVisuals();
            }

            // 3. Lead Showcase Tank + 2 Escort Wingmen descending staggered layer-by-layer into formation!
            float sway = Mathf.Sin(_titleTime * 1.25f) * 0.12f * leadEase;
            Vector2 leadPos = Vector2.Lerp(
                center + new Vector2(0.15f, -0.60f + sway * 0.5f),
                center + new Vector2(0f, 0.15f),
                exit01 * exit01
            );

            if (showcaseTank != null)
            {
                if (!showcaseTank.gameObject.activeSelf) showcaseTank.gameObject.SetActive(true);
                showcaseTank.SetAssemblyDrop(leadDrop01, dropHeightPx: 50f);
                showcaseTank.Position2D = leadPos;
                showcaseTank.hullAngleDeg = Mathf.LerpAngle(TitleConvoyAngleDeg + Mathf.Sin(_titleTime * 0.85f) * 3.2f, 35f + _manualAngleOffset, exit01);
                showcaseTank.turretAngleDeg = showcaseTank.hullAngleDeg + Mathf.Sin(_titleTime * 0.80f) * 24f * (1f - exit01) * leadEase;
                showcaseTank.SyncVisuals(squash, tankSpacing);
            }

            for (int i = 0; i < _titleWingmen.Length; i++)
            {
                TankEntity w = _titleWingmen[i];
                if (w == null) continue;
                float wingDelay = 0.55f + i * 0.35f;
                float wingDrop01 = _titleTransitioning ? 1f : Mathf.Clamp01((_titleTime - wingDelay) / 2.55f);
                float wingEase = 1f - Mathf.Pow(1f - wingDrop01, 2.2f);
                float wingSpacing = Mathf.Lerp(spacingPx * 1.38f, spacingPx, wingEase);

                w.SetAssemblyDrop(wingDrop01, dropHeightPx: 54f);
                Vector2 exitPush = (i == 0 ? new Vector2(-1f, 0.6f) : new Vector2(-1f, -0.6f)) * (exit01 * exit01 * 9.5f);
                Vector2 wPos = center + _titleWingOffsets[i] + new Vector2(0f, Mathf.Sin(_titleTime * 1.2f + i * 2.1f) * 0.10f * wingEase) + exitPush;
                w.Position2D = wPos;
                w.hullAngleDeg = TitleConvoyAngleDeg + Mathf.Sin(_titleTime * 0.75f + i * 1.7f) * 3f;
                w.turretAngleDeg = w.hullAngleDeg + Mathf.Cos(_titleTime * 0.68f + i * 1.9f) * 20f * wingEase;
                w.SyncVisuals(squash, wingSpacing);
            }

            // 4. Scroll Sprite-Stacked Battlefield Props past the convoy!
            float halfSpanX = 11.2f;
            float halfSpanY = 6.2f;
            for (int i = 0; i < _titleProps.Count; i++)
            {
                var item = _titleProps[i];
                if (item == null || item.root == null) continue;
                item.pos -= convoyDir * (TitleScrollSpeed * dt);

                // Seamless wrap-around back ahead of the convoy
                if (item.pos.x < center.x - halfSpanX)
                    item.pos.x += halfSpanX * 2f;
                if (item.pos.y < center.y - halfSpanY)
                    item.pos.y += halfSpanY * 2f;
                else if (item.pos.y > center.y + halfSpanY)
                    item.pos.y -= halfSpanY * 2f;

                Vector2 drawPos = item.pos - convoyDir * (exit01 * exit01 * 8.5f);
                item.root.position = new Vector3(drawPos.x, drawPos.y, 0f);
                if (item.sg != null)
                    item.sg.sortingOrder = Mathf.Clamp(Mathf.RoundToInt(-drawPos.y * 100f), -10000, 10000);
                if (item.stack != null)
                {
                    item.stack.squash = squash;
                    item.stack.spacingPx = spacingPx;
                    item.stack.ApplyVisuals();
                }
            }

            // 5. Continuous Track Dust behind convoy tanks once their hulls touch down!
            _titleDustTimer -= dt;
            if (_titleDustTimer <= 0f && !_titleTransitioning)
            {
                _titleDustTimer = 0.068f;
                if (showcaseTank != null && leadDrop01 >= 0.56f) EmitTitleTrackDust(showcaseTank, convoyDir);
                for (int i = 0; i < _titleWingmen.Length; i++)
                {
                    float wDrop = Mathf.Clamp01((_titleTime - (0.55f + i * 0.35f)) / 2.55f);
                    if (_titleWingmen[i] != null && wDrop >= 0.56f) EmitTitleTrackDust(_titleWingmen[i], convoyDir);
                }
            }
            UpdateTitleDust(dt, convoyDir);

            // 6. Camera smoothly frames the live Stacking convoy & zooms gently toward Garage on START!
            if (_cam != null)
            {
                float targetGarageZoom = GetAdaptiveShowcaseZoom(GetSelectedVehicle());
                float titleZoom = Mathf.Lerp(1.42f, targetGarageZoom, exit01 * exit01 * (3f - 2f * exit01));
                float orthoSize = ((PixelWindowbox.RenderHeight / PPU) * 0.5f) / titleZoom;
                _cam.orthographicSize = orthoSize;
                float camX = Mathf.Round(center.x * PPU) / PPU;
                float camY = Mathf.Round((center.y + Mathf.Lerp(0.15f, 0f, exit01)) * PPU) / PPU;
                _cam.transform.position = new Vector3(camX, camY, -20f);
                _cam.transform.rotation = Quaternion.identity;
            }
        }

        private void EmitTitleTrackDust(TankEntity t, Vector2 convoyDir)
        {
            if (_titleDusts.Count > 110) return;
            Vector2 fwd = TankEntity.SliceAngleToDir(t.hullAngleDeg);
            Vector2 right = new Vector2(-fwd.y, fwd.x);
            float trackW = ((t.meta != null && t.meta.hullWidth > 0 ? t.meta.hullWidth : 22) * 0.40f) / PPU;
            float trackL = ((t.meta != null && t.meta.hullLength > 0 ? t.meta.hullLength : 38) * 0.38f) / PPU;

            Color baseCol = _sessionState.SelectedBattleMap switch
            {
                BattleMap.City => new Color(0.62f, 0.61f, 0.58f, 0.34f),
                BattleMap.Forest => new Color(0.46f, 0.39f, 0.28f, 0.42f),
                _ => new Color(0.84f, 0.72f, 0.50f, 0.48f)
            };

            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 emit = t.Position2D + SpriteStackCamera.Squash(right * (side * trackW) - fwd * trackL, squash);
                Vector2 vel = -convoyDir * UnityEngine.Random.Range(2.2f, 3.4f) + new Vector2(UnityEngine.Random.Range(-0.25f, 0.25f), UnityEngine.Random.Range(0.10f, 0.32f));
                SpawnTitleDustPuff(emit, vel, UnityEngine.Random.Range(0.36f, 0.56f), UnityEngine.Random.Range(0.38f, 0.62f), baseCol);
            }
        }

        private void SpawnTitleDustBurst(Vector2 center, int count)
        {
            Color col = _sessionState.SelectedBattleMap switch
            {
                BattleMap.City => new Color(0.66f, 0.65f, 0.62f, 0.42f),
                BattleMap.Forest => new Color(0.50f, 0.42f, 0.30f, 0.50f),
                _ => new Color(0.88f, 0.76f, 0.54f, 0.56f)
            };
            for (int i = 0; i < count; i++)
            {
                float ang = (i / (float)count) * Mathf.PI * 2f + UnityEngine.Random.Range(-0.25f, 0.25f);
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * squash);
                SpawnTitleDustPuff(center + dir * 0.5f, dir * UnityEngine.Random.Range(1.2f, 2.4f), UnityEngine.Random.Range(0.45f, 0.72f), UnityEngine.Random.Range(0.45f, 0.75f), col);
            }
        }

        private void SpawnTitleDustPuff(Vector2 pos, Vector2 vel, float size, float life, Color color)
        {
            var sr = Instantiate(_titleDustPrefab, _titleStageRoot, false);
            var go = sr.gameObject;
            go.transform.position = new Vector3(pos.x, pos.y, -0.3f);
            go.transform.localScale = new Vector3(size * 0.75f, size * 0.75f, 1f);
            sr.color = color;
            sr.sortingOrder = Mathf.Clamp(Mathf.RoundToInt(-pos.y * 100f) - 18, -10500, 10500);
            _titleDusts.Add(new TitleDustItem
            {
                tf = go.transform,
                sr = sr,
                pos = pos,
                vel = vel,
                life = life,
                maxLife = life,
                baseSize = size,
                baseColor = color
            });
        }

        private void UpdateTitleDust(float dt, Vector2 convoyDir)
        {
            for (int i = _titleDusts.Count - 1; i >= 0; i--)
            {
                var d = _titleDusts[i];
                d.life -= dt;
                if (d.life <= 0f)
                {
                    if (d.tf != null) Destroy(d.tf.gameObject);
                    _titleDusts.RemoveAt(i);
                    continue;
                }
                d.pos += d.vel * dt;
                d.vel = Vector2.Lerp(d.vel, -convoyDir * (TitleScrollSpeed * 0.85f), dt * 3.5f);
                d.tf.position = new Vector3(d.pos.x, d.pos.y, -0.3f);
                float remain01 = Mathf.Clamp01(d.life / Mathf.Max(0.01f, d.maxLife));
                float t01 = 1f - remain01;
                float sc = d.baseSize * Mathf.Lerp(0.75f, 2.0f, Mathf.Sqrt(t01));
                d.tf.localScale = new Vector3(sc, sc, 1f);
                Color c = d.baseColor;
                c.a = d.baseColor.a * Mathf.Clamp01(t01 / 0.14f) * Mathf.Pow(remain01, 1.35f);
                d.sr.color = c;
            }
        }

        private void ClearTitleDust()
        {
            for (int i = 0; i < _titleDusts.Count; i++)
                if (_titleDusts[i].tf != null) Destroy(_titleDusts[i].tf.gameObject);
            _titleDusts.Clear();
        }
    }
}
