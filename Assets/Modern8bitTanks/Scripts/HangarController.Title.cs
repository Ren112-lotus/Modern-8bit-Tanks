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

        private bool _deployOpen = false;
        private bool _deployFromTree = false;

        // UI Groups
        private GameObject _titleVisualGroup;
        private GameObject _titleHitsGroup;
        private GameObject _deployVisualGroup;
        private GameObject _deployHitsGroup;

        // Deploy Modal UI refs
        private Text _deployVehicleLabel;
        private Image _deployMode1v1Bg;
        private Image _deployMode2v2Bg;
        private Image _deployMode5v5Bg;
        private Image _deployModeWaveBg;
        private Image _deployModeRangeBg;
        private Image _deployMapDesertBg;
        private Image _deployMapCityBg;
        private Image _deployMapForestBg;

        // 3D Sprite-Stacked Title Stage Objects
        private Transform _titleStageRoot;
        private SpriteStackRenderer _titleLogoStack;
        private Texture2D _titleLogoSheet;
        private readonly List<TankEntity> _titleWingmen = new();
        private readonly List<TitlePropItem> _titleProps = new();
        private readonly List<TitleDustItem> _titleDusts = new();
        private Sprite _titleDustSprite;
        private Texture2D _titleDustTex;
        private float _titleDustTimer;
        private PropsMetaFile _titlePropsMeta;

        private class TitlePropItem
        {
            public Transform root;
            public SortingGroup sg;
            public SpriteStackRenderer stack;
            public Vector2 pos;
            public float speedMul;
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
        // 1. Build Title Screen UI & Battle Mode Selection Modal UI
        // ============================================================
        private void BuildTitleAndDeployGroups()
        {
            _titleVisualGroup = new GameObject("TitleVisuals") { layer = 5 };
            _titleVisualGroup.transform.SetParent(_hudRoot, false);
            Stretch(_titleVisualGroup.AddComponent<RectTransform>());

            _deployVisualGroup = new GameObject("DeployVisuals") { layer = 5 };
            _deployVisualGroup.transform.SetParent(_hudRoot, false);
            Stretch(_deployVisualGroup.AddComponent<RectTransform>());

            // Always keep Settings overlay on top of Title, Garage, TechTree, and Deploy modal!
            if (_settingsVisualGroup != null)
                _settingsVisualGroup.transform.SetAsLastSibling();

            _titleHitsGroup = CreateHitGroup("TitleHits");
            _deployHitsGroup = CreateHitGroup("DeployHits");
            if (_settingsHitsGroup != null)
                _settingsHitsGroup.transform.SetAsLastSibling();
        }

        private void BuildTitleUI()
        {
            Transform vp = _titleVisualGroup.transform;
            Transform hp = _titleHitsGroup.transform;
            int W = renderWidth;

            // Subtle dark cinematic bottom band — upper 78% stays wide open for the live 3D Stacking Diorama!
            CreatePixelSolid(vp, "TitleBotShade", 0, 0, W, 56, new Color(0.04f, 0.04f, 0.03f, 0.78f));
            CreatePixelSolid(vp, "TitleBotRule", 0, 56, W, 1, PanelBorder);

            CreatePixelText(vp, "SPRITE-STACKED ARMORED COMBAT  •  121 VEHICLES", 8, TextMuted, TextAnchor.MiddleCenter, 0, 38, W, 14);

            // Centered primary START GAME + SETTINGS buttons (EVEN pixel rects!)
            CreatePixelButton(vp, hp, "START GAME (ENTER)", 122, 10, 156, 22, GoldColor, GoldColor, DarkInk, 8, BeginEnterGarageFromTitle, useTitleFont: true);
            CreatePixelButton(vp, hp, "SETTINGS", 286, 10, 72, 22, BtnBg, LightCyan, LightCyan, 8, () => SetSettingsOpen(true));
        }

        private void BuildDeployModalUI()
        {
            Transform vp = _deployVisualGroup.transform;
            Transform hp = _deployHitsGroup.transform;
            int W = renderWidth;
            int H = renderHeight;

            CreatePixelSolid(vp, "DeployDim", 0, 0, W, H, new Color(0.03f, 0.03f, 0.03f, 0.74f));

            int cw = 280;
            int ch = 206;
            int cx = (W - cw) / 2;
            int cy = (H - ch) / 2;
            CreatePixelBox(vp, "DeployCard", cx, cy, cw, ch, new Color(0.08f, 0.09f, 0.08f, 0.98f), GoldColor);
            CreatePixelText(vp, "SELECT BATTLE MODE", 8, GoldColor, TextAnchor.MiddleCenter, cx, cy + 186, cw, 14, useTitleFont: true);
            _deployVehicleLabel = CreatePixelText(vp, "", 8, TextMuted, TextAnchor.MiddleCenter, cx + 8, cy + 170, cw - 16, 12);

            // 5 Battle Mode rows (all EVEN coordinates & dimensions!)
            CreatePixelButton(vp, hp, "1v1 DUEL   - SAME-TIER RIVAL", cx + 12, cy + 148, 256, 18, BtnSelected, PanelBorder, TextLight, 8, () =>
            {
                BattleSessionState.SelectedBattleMode = BattleMode.Duel1v1;
                RefreshAllUI();
            }, out _deployMode1v1Bg);

            CreatePixelButton(vp, hp, "2v2 SQUAD  - WINGMAN CLASH", cx + 12, cy + 128, 256, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                BattleSessionState.SelectedBattleMode = BattleMode.Squad2v2;
                RefreshAllUI();
            }, out _deployMode2v2Bg);

            CreatePixelButton(vp, hp, "5v5 BATTLE - COMPANY WAR", cx + 12, cy + 108, 256, 18, BtnBg, GoldColor, GoldColor, 8, () =>
            {
                BattleSessionState.SelectedBattleMode = BattleMode.Battle5v5;
                RefreshAllUI();
            }, out _deployMode5v5Bg);

            CreatePixelButton(vp, hp, "SURVIVAL   - ENDLESS WAVES", cx + 12, cy + 88, 256, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                BattleSessionState.SelectedBattleMode = BattleMode.SurvivalWave;
                RefreshAllUI();
            }, out _deployModeWaveBg);

            CreatePixelButton(vp, hp, "RANGE      - FIRING PRACTICE", cx + 12, cy + 68, 256, 18, BtnBg, LightCyan, LightCyan, 8, () =>
            {
                BattleSessionState.SelectedBattleMode = BattleMode.FiringRange;
                RefreshAllUI();
            }, out _deployModeRangeBg);

            // Map selector row inside Deploy Modal (live updates backdrop behind modal!)
            CreatePixelText(vp, "BATTLEFIELD MAP", 8, TextMuted, TextAnchor.MiddleCenter, cx, cy + 56, cw, 10);
            CreatePixelButton(vp, hp, "DESERT", cx + 12, cy + 36, 82, 18, BtnSelected, GoldColor, TextLight, 8, () => SelectBattleMap(BattleMap.Desert), out _deployMapDesertBg);
            CreatePixelButton(vp, hp, "CITY", cx + 98, cy + 36, 84, 18, BtnBg, PanelBorder, TextLight, 8, () => SelectBattleMap(BattleMap.City), out _deployMapCityBg);
            CreatePixelButton(vp, hp, "FOREST", cx + 186, cy + 36, 82, 18, BtnBg, MbtGreen, TextLight, 8, () => SelectBattleMap(BattleMap.Forest), out _deployMapForestBg);

            // Bottom Action Buttons: BACK + DEPLOY!
            CreatePixelButton(vp, hp, "BACK [ESC]", cx + 12, cy + 10, 96, 20, BtnBg, PanelBorder, TextLight, 8, () => SetDeployModalOpen(false));
            CreatePixelButton(vp, hp, "DEPLOY! (ENTER)", cx + 114, cy + 10, 154, 20, GoldColor, GoldColor, DarkInk, 8, LaunchBattleScene, useTitleFont: true);
        }

        // ============================================================
        // 2. Build 3D Sprite-Stacked Title Stage (Logo + Wingmen + Props)
        // ============================================================
        private void BuildTitleStage()
        {
            if (_titleStageRoot != null) return;

            var rootGo = new GameObject("TitleStackingStage");
            rootGo.transform.SetParent(transform, false);
            _titleStageRoot = rootGo.transform;

            // Soft circular pixel-dust sprite for title convoy tracks
            _titleDustTex = CreateTitleDustTexture(24);
            _titleDustSprite = Sprite.Create(_titleDustTex, new Rect(0, 0, 24, 24), new Vector2(0.5f, 0.5f), 24f);

            // 1. Procedural 10-layer Sprite-Stacked 3D Logo ("8-BIT TANKS")!
            _titleLogoSheet = GenerateStackedTitleLogoSheet(out int logoSlices);
            var logoGo = new GameObject("TitleStackedLogo");
            logoGo.transform.SetParent(_titleStageRoot, false);
            var logoSg = logoGo.AddComponent<SortingGroup>();
            logoSg.sortingOrder = 18000; // Always floats proudly above the battlefield convoy!
            _titleLogoStack = logoGo.AddComponent<SpriteStackRenderer>();
            _titleLogoStack.metallicWear = 0.92f;
            _titleLogoStack.Configure(_titleLogoSheet, logoSlices, PPU, _stackFlashMat, 0, 0f, groundShadow: true);
            logoGo.transform.localScale = new Vector3(1.62f, 1.62f, 1f);

            // 2. Escort Wingmen (2 extra Sprite-Stacked tanks running in echelon formation!)
            string[] wingIds = { "us_m2a4", "de_leo2a7v" };
            for (int i = 0; i < wingIds.Length; i++)
            {
                VehicleDef wDef = _vehicleMap.TryGetValue(wingIds[i], out var found) ? found : GetSelectedVehicle();
                if (wDef == null) continue;

                var wGo = new GameObject($"TitleWingman_{i}");
                wGo.transform.SetParent(_titleStageRoot, false);
                var tank = wGo.AddComponent<TankEntity>();
                tank.isPlayer = false;
                tank.factionTeam = "ally";

                Texture2D hullTex = LoadCachedHull(wDef.id);
                Texture2D turretTex = LoadCachedTurret(wDef.id);
                tank.ApplyVehicleDef(wDef, hullTex, turretTex, _stackFlashMat);
                tank.CaptureSpawnAndReset(new Vector2(WorldW * 0.5f, WorldH * 0.5f), TitleConvoyAngleDeg);
                _titleWingmen.Add(tank);
            }

            // 3. Scrolling Sprite-Stacked Desert Props (parallax立体風景!)
            _titlePropsMeta = MetaLoader.LoadPropsMeta();
            string[] propKinds =
            {
                "palm_tree", "desert_rock", "sandbag_wall", "palm_tree",
                "desert_bunker", "desert_rock", "watch_tower", "hesco_barrier"
            };
            Vector2 center = new Vector2(WorldW * 0.5f, WorldH * 0.5f);
            Vector2[] offsets =
            {
                new Vector2(-6.5f,  2.8f),
                new Vector2( 2.8f,  3.2f),
                new Vector2( 7.6f,  1.2f),
                new Vector2(-2.2f, -3.4f),
                new Vector2( 5.4f, -2.9f),
                new Vector2(-8.2f, -1.6f),
                new Vector2( 9.5f,  3.6f),
                new Vector2(-4.4f,  3.7f)
            };

            for (int i = 0; i < propKinds.Length; i++)
            {
                string kind = propKinds[i];
                Texture2D sheet = Resources.Load<Texture2D>($"Props/{kind}_slices");
                PropMetaEntry pMeta = _titlePropsMeta != null ? _titlePropsMeta.GetByKind(kind) : null;
                if (sheet == null || pMeta == null) continue;

                var pGo = new GameObject($"TitleProp_{i}_{kind}");
                pGo.transform.SetParent(_titleStageRoot, false);
                var sg = pGo.AddComponent<SortingGroup>();
                var st = pGo.AddComponent<SpriteStackRenderer>();
                st.angleDeg = (i * 37f) % 360f;
                st.Configure(sheet, pMeta.slices, PPU, _stackFlashMat, 0, 0f, groundShadow: true);
                st.squash = squash;
                st.spacingPx = spacingPx;

                Vector2 p = center + offsets[i];
                pGo.transform.position = new Vector3(p.x, p.y, 0f);
                sg.sortingOrder = Mathf.Clamp(Mathf.RoundToInt(-p.y * 100f), -10000, 10000);

                _titleProps.Add(new TitlePropItem
                {
                    root = pGo.transform,
                    sg = sg,
                    stack = st,
                    pos = p,
                    speedMul = 1.0f
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
            _titleTransitioning = false;
            _titleExitTimer = 0f;
            _titleTime = 0f;
            _titleLandingDustFired = false;

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
                BattleSessionState.HasEnteredGarage = true;
            }

            if (_titleStageRoot != null) _titleStageRoot.gameObject.SetActive(open);
            if (_titleVisualGroup != null) _titleVisualGroup.SetActive(open);
            if (_garageVisualGroup != null) _garageVisualGroup.SetActive(!open && !_treeOpen);
            if (_treeVisualGroup != null) _treeVisualGroup.SetActive(!open && _treeOpen);
            if (_deployVisualGroup != null) _deployVisualGroup.SetActive(!open && _deployOpen);

            if (!open)
            {
                ClearTitleDust();
                if (showcaseTank != null)
                {
                    showcaseTank.gameObject.SetActive(true);
                    showcaseTank.Position2D = new Vector2(WorldW * 0.5f, WorldH * 0.5f + 0.35f);
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
            if (open) _deployFromTree = fromTree;
            _deployOpen = open;
            if (_deployVisualGroup != null) _deployVisualGroup.SetActive(open);
            SyncActiveHitGroups();
            if (open) RefreshAllUI();
        }

        private void SyncActiveHitGroups()
        {
            bool settings = _settingsOpen;
            bool deploy = !settings && _deployOpen;
            bool title = !settings && !deploy && _titleOpen;
            bool tree = !settings && !deploy && !title && _treeOpen;
            bool garage = !settings && !deploy && !title && !tree;

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

            if (_deployMode1v1Bg != null) _deployMode1v1Bg.color = BattleSessionState.SelectedBattleMode == BattleMode.Duel1v1 ? BtnSelected : BtnBg;
            if (_deployMode2v2Bg != null) _deployMode2v2Bg.color = BattleSessionState.SelectedBattleMode == BattleMode.Squad2v2 ? BtnSelected : BtnBg;
            if (_deployMode5v5Bg != null) _deployMode5v5Bg.color = BattleSessionState.SelectedBattleMode == BattleMode.Battle5v5 ? BtnSelected : BtnBg;
            if (_deployModeWaveBg != null) _deployModeWaveBg.color = BattleSessionState.SelectedBattleMode == BattleMode.SurvivalWave ? BtnSelected : BtnBg;
            if (_deployModeRangeBg != null) _deployModeRangeBg.color = BattleSessionState.SelectedBattleMode == BattleMode.FiringRange ? BtnSelected : BtnBg;

            if (_deployMapDesertBg != null) _deployMapDesertBg.color = BattleSessionState.SelectedBattleMap == BattleMap.Desert ? BtnSelected : BtnBg;
            if (_deployMapCityBg != null) _deployMapCityBg.color = BattleSessionState.SelectedBattleMap == BattleMap.City ? BtnSelected : BtnBg;
            if (_deployMapForestBg != null) _deployMapForestBg.color = BattleSessionState.SelectedBattleMap == BattleMap.Forest ? BtnSelected : BtnBg;
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

            // 1. Exploded-View Sprite-Stacking Assembly Curve (layers drop & snap together on launch!)
            float assemble01 = Mathf.Clamp01((_titleTime - 0.08f) / 0.78f);
            float easeOut = 1f - Mathf.Pow(1f - assemble01, 3f);
            float tankSpacing = Mathf.Lerp(2.35f, spacingPx, easeOut);

            // Trigger landing dust burst right when the stacked slices snap together!
            if (!_titleLandingDustFired && assemble01 >= 0.96f)
            {
                _titleLandingDustFired = true;
                if (showcaseTank != null) showcaseTank.TriggerFireKick(0f); // Suspension dip!
                SpawnTitleDustBurst(center + new Vector2(0.15f, -0.55f), 10);
            }

            // 2. Animate 3D Sprite-Stacked Logo ("8-BIT TANKS")
            if (_titleLogoStack != null)
            {
                float logoAsm = Mathf.Clamp01(_titleTime / 0.85f);
                float logoEase = 1f - Mathf.Pow(1f - logoAsm, 3f);
                float baseLogoSpacing = Mathf.Lerp(3.8f, 0.92f, logoEase) + Mathf.Sin(_titleTime * 2.1f) * 0.06f * logoEase;
                // On PLAY click, logo explodes vertically into floating stack slices & flies off!
                float exitExplode = exit01 * exit01;
                _titleLogoStack.spacingPx = baseLogoSpacing + exitExplode * 4.8f;
                _titleLogoStack.squash = 0.72f;
                _titleLogoStack.angleDeg = Mathf.Lerp(-24f, 0f, logoEase) + Mathf.Sin(_titleTime * 1.15f) * 11f * logoEase + exitExplode * 28f;
                _titleLogoStack.transform.position = new Vector3(
                    center.x,
                    center.y + 1.95f + Mathf.Sin(_titleTime * 1.6f) * 0.08f + exitExplode * 3.8f,
                    0f
                );
                _titleLogoStack.ApplyVisuals();
            }

            // 3. Lead Showcase Tank + 2 Escort Wingmen rolling in formation
            float sway = Mathf.Sin(_titleTime * 1.45f) * 0.14f;
            Vector2 leadPos = Vector2.Lerp(
                center + new Vector2(0.15f, -0.60f + sway * 0.5f),
                center + new Vector2(0f, 0.35f),
                exit01 * exit01
            );

            if (showcaseTank != null)
            {
                if (!showcaseTank.gameObject.activeSelf) showcaseTank.gameObject.SetActive(true);
                showcaseTank.Position2D = leadPos;
                showcaseTank.hullAngleDeg = Mathf.LerpAngle(TitleConvoyAngleDeg + Mathf.Sin(_titleTime * 0.9f) * 3.5f, 35f + _manualAngleOffset, exit01);
                showcaseTank.turretAngleDeg = showcaseTank.hullAngleDeg + Mathf.Sin(_titleTime * 0.85f) * 26f * (1f - exit01);
                showcaseTank.SyncVisuals(squash, tankSpacing);
            }

            Vector2[] wingOffsets =
            {
                new Vector2(-3.35f,  0.65f),
                new Vector2(-2.90f, -2.05f)
            };
            for (int i = 0; i < _titleWingmen.Count; i++)
            {
                TankEntity w = _titleWingmen[i];
                if (w == null) continue;
                Vector2 exitPush = (i == 0 ? new Vector2(-1f, 0.6f) : new Vector2(-1f, -0.6f)) * (exit01 * exit01 * 9.5f);
                Vector2 wPos = center + wingOffsets[i] + new Vector2(0f, Mathf.Sin(_titleTime * 1.3f + i * 2.1f) * 0.12f) + exitPush;
                w.Position2D = wPos;
                w.hullAngleDeg = TitleConvoyAngleDeg + Mathf.Sin(_titleTime * 0.8f + i * 1.7f) * 3f;
                w.turretAngleDeg = w.hullAngleDeg + Mathf.Cos(_titleTime * 0.72f + i * 1.9f) * 22f;
                w.SyncVisuals(squash, tankSpacing);
            }

            // 4. Scroll Sprite-Stacked Battlefield Props past the convoy!
            float halfSpanX = 11.2f;
            float halfSpanY = 6.2f;
            for (int i = 0; i < _titleProps.Count; i++)
            {
                var item = _titleProps[i];
                if (item == null || item.root == null) continue;
                item.pos -= convoyDir * (TitleScrollSpeed * item.speedMul * dt);

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

            // 5. Continuous Track Dust behind all 3 moving convoy tanks!
            _titleDustTimer -= dt;
            if (_titleDustTimer <= 0f && !_titleTransitioning)
            {
                _titleDustTimer = 0.065f;
                if (showcaseTank != null) EmitTitleTrackDust(showcaseTank, convoyDir);
                for (int i = 0; i < _titleWingmen.Count; i++)
                    if (_titleWingmen[i] != null) EmitTitleTrackDust(_titleWingmen[i], convoyDir);
            }
            UpdateTitleDust(dt, convoyDir);

            // 6. Camera smoothly frames the live Stacking convoy & zooms gently toward Garage on PLAY!
            if (_cam != null)
            {
                float titleZoom = Mathf.Lerp(1.42f, showcaseZoom, exit01 * exit01 * (3f - 2f * exit01));
                float orthoSize = ((renderHeight / PPU) * 0.5f) / titleZoom;
                _cam.orthographicSize = orthoSize;
                float camX = Mathf.Round(center.x * PPU) / PPU;
                float camY = Mathf.Round((center.y + Mathf.Lerp(0.15f, 0f, exit01)) * PPU) / PPU;
                _cam.transform.position = new Vector3(camX, camY, -20f);
                _cam.transform.rotation = Quaternion.identity;
            }
        }

        private void EmitTitleTrackDust(TankEntity t, Vector2 convoyDir)
        {
            if (_titleDusts.Count > 110 || _titleDustSprite == null) return;
            Vector2 fwd = TankEntity.SliceAngleToDir(t.hullAngleDeg);
            Vector2 right = new Vector2(-fwd.y, fwd.x);
            float trackW = ((t.meta != null && t.meta.hullWidth > 0 ? t.meta.hullWidth : 22) * 0.40f) / PPU;
            float trackL = ((t.meta != null && t.meta.hullLength > 0 ? t.meta.hullLength : 38) * 0.38f) / PPU;

            Color baseCol = BattleSessionState.SelectedBattleMap switch
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
            Color col = BattleSessionState.SelectedBattleMap switch
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
            if (_titleStageRoot == null || _titleDustSprite == null) return;
            var go = new GameObject("TitleDust");
            go.transform.SetParent(_titleStageRoot, false);
            go.transform.position = new Vector3(pos.x, pos.y, -0.3f);
            go.transform.localScale = new Vector3(size * 0.75f, size * 0.75f, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _titleDustSprite;
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

        private void CleanupTitleStage()
        {
            ClearTitleDust();
            if (_titleLogoSheet != null) Destroy(_titleLogoSheet);
            if (_titleDustTex != null) Destroy(_titleDustTex);
        }

        // ============================================================
        // 5. Procedural 10-Layer Sprite-Stacked 3D Logo Generator!
        // ============================================================
        private static Texture2D CreateTitleDustTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c) / c;
                    float dy = (y - c) / c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Pow(Mathf.Clamp01(1f - r), 1.45f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        private static Texture2D GenerateStackedTitleLogoSheet(out int sliceCount)
        {
            const string title = "8-BIT TANKS";
            const int glyphW = 5;
            const int glyphH = 7;
            const int padX = 5;
            const int padY = 4;
            int textW = title.Length * (glyphW + 1) - 1; // 65px
            int sliceW = textW + padX * 2;               // 75px
            int sliceH = glyphH + padY * 2;              // 15px
            sliceCount = 10;

            bool[,] letterMask = new bool[sliceW, sliceH];
            for (int i = 0; i < title.Length; i++)
            {
                string[] rows = GetPixelGlyph5x7(title[i]);
                int ox = padX + i * (glyphW + 1);
                for (int gy = 0; gy < glyphH; gy++)
                {
                    // Row 0 is top of glyph -> higher texture Y!
                    int py = padY + (glyphH - 1 - gy);
                    string r = rows[gy];
                    for (int gx = 0; gx < glyphW && gx < r.Length; gx++)
                    {
                        if (r[gx] == '#')
                            letterMask[ox + gx, py] = true;
                    }
                }
            }

            // Dilated 1px outline around letters for bold armor-plate bevel
            bool[,] letterOutline = new bool[sliceW, sliceH];
            for (int y = 0; y < sliceH; y++)
            {
                for (int x = 0; x < sliceW; x++)
                {
                    if (letterMask[x, y]) { letterOutline[x, y] = true; continue; }
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && nx < sliceW && ny >= 0 && ny < sliceH && letterMask[nx, ny])
                                letterOutline[x, y] = true;
                        }
                    }
                }
            }

            int sheetW = sliceW * sliceCount;
            var px = new Color32[sheetW * sliceH];

            Color32 plateEdge   = new Color32(24, 26, 22, 255);
            Color32 plateDark   = new Color32(46, 52, 42, 255);
            Color32 plateMid    = new Color32(68, 76, 60, 255);
            Color32 plateRivet  = new Color32(158, 152, 126, 255);
            Color32 textShadow  = new Color32(96, 64, 24, 255);
            Color32 textBronze  = new Color32(158, 110, 38, 255);
            Color32 textAmber   = new Color32(216, 162, 58, 255);
            Color32 textGold    = new Color32(248, 206, 94, 255);
            Color32 textTopSpec = new Color32(255, 242, 178, 255);
            Color32 trimInk     = new Color32(28, 22, 14, 255);

            for (int z = 0; z < sliceCount; z++)
            {
                int xOff = z * sliceW;
                for (int y = 0; y < sliceH; y++)
                {
                    int row = y * sheetW + xOff;
                    for (int x = 0; x < sliceW; x++)
                    {
                        // Chamfered military armor backing plate on bottom slices (z = 0..3)
                        int cornerDist = Mathf.Min(x, sliceW - 1 - x) + Mathf.Min(y, sliceH - 1 - y);
                        bool inPlate = cornerDist >= 2;
                        bool onPlateBorder = inPlate && (x == 0 || x == sliceW - 1 || y == 0 || y == sliceH - 1 || cornerDist == 2);
                        bool isRivet = (x == 2 || x == sliceW - 3) && (y == 2 || y == sliceH - 3);

                        Color32 col = default;
                        if (z <= 2)
                        {
                            if (inPlate)
                                col = onPlateBorder ? plateEdge : (isRivet && z == 2 ? plateRivet : (z == 0 ? plateDark : plateMid));
                        }
                        else if (z == 3)
                        {
                            if (letterOutline[x, y])
                                col = letterMask[x, y] ? textShadow : trimInk;
                            else if (inPlate && !onPlateBorder && (y == 1 || y == sliceH - 2))
                                col = plateDark;
                        }
                        else if (z <= 8)
                        {
                            if (letterMask[x, y])
                                col = z <= 5 ? textBronze : (z <= 7 ? textAmber : textGold);
                            else if (z <= 5 && letterOutline[x, y])
                                col = trimInk;
                        }
                        else // Topmost z == 9: gleaming top gold bevel!
                        {
                            if (letterMask[x, y])
                            {
                                bool topRim = (y + 1 >= sliceH) || !letterMask[x, y + 1];
                                col = topRim ? textTopSpec : textGold;
                            }
                        }

                        px[row + x] = col;
                    }
                }
            }

            var sheet = new Texture2D(sheetW, sliceH, TextureFormat.RGBA32, false)
            {
                name = "Procedural_TitleLogo_Slices",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            sheet.SetPixels32(px);
            sheet.Apply(false, false);
            return sheet;
        }

        private static string[] GetPixelGlyph5x7(char c) => c switch
        {
            '8' => new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." },
            '-' => new[] { ".....", ".....", ".....", "#####", ".....", ".....", "....." },
            'B' => new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." },
            'I' => new[] { ".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###." },
            'T' => new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
            'A' => new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            'N' => new[] { "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#" },
            'K' => new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" },
            'S' => new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
            _   => new[] { ".....", ".....", ".....", ".....", ".....", ".....", "....." }
        };
    }
}
