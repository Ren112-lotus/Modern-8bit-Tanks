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

            // Riveted brushed-steel armored title dock with diagonal military hazard caution tape!
            MilitaryUiSprites.CreateTiledSprite(vp, "TitleBotSteel", MilitaryUiSprites.ArmorHeaderBar, 0, 0, W, 50, PanelGlassDeep);
            MilitaryUiSprites.CreateTiledSprite(vp, "TitleBotHazard", MilitaryUiSprites.HazardTape, 0, 50, W, 4, new Color(1f, 1f, 1f, 0.92f));
            CreatePixelSolid(vp, "TitleBotRuleTop", 0, 54, W, 1, new Color(0.06f, 0.07f, 0.05f, 1f));
            CreatePixelSolid(vp, "TitleBotRuleSub", 0, 49, W, 1, new Color(0.06f, 0.07f, 0.05f, 1f));

            CreatePixelText(vp, "ARMORED WARFARE  •  121 HISTORICAL & MODERN VEHICLES", 8, TextMuted, TextAnchor.MiddleCenter, 0, 34, W, 14);

            CreatePrimaryCtaButton(vp, hp, "START GAME (ENTER)", 68, 7, 164, 24, BeginEnterGarageFromTitle);
            CreatePixelButton(vp, hp, "ONLINE", 240, 7, 88, 24, BtnBg, LightCyan, LightCyan, 8, () => SetOnlineRoomOpen(true));
            CreatePixelButton(vp, hp, "SETTINGS", 336, 7, 76, 24, BtnBg, PanelBorder, TextLight, 8, () => SetSettingsOpen(true));
        }

        private void BuildDeployModalUI()
        {
            Transform vp = _deployVisualGroup.transform;
            Transform hp = _deployHitsGroup.transform;
            int W = renderWidth;
            int H = renderHeight;

            CreatePixelSolid(vp, "DeployDim", 0, 0, W, H, new Color(0.02f, 0.03f, 0.02f, 0.80f));

            int cw = 296;
            int ch = 218;
            int cx = (W - cw) / 2;
            int cy = (H - ch) / 2;
            var card = CreateTacticalCard(vp, "DeployCard", cx, cy, cw, ch, PanelGlassModal, BrassDim, GoldColor, withHazardTape: false);
            // Red/black war-room alert hazard strip across top of Deployment Briefing!
            MilitaryUiSprites.CreateTiledSprite(card, "DeployRedHazard", MilitaryUiSprites.HazardTapeRed, 6, ch - 8, cw - 12, 3, new Color(1f, 1f, 1f, 0.92f));
            MilitaryUiSprites.CreateIcon(card, "DeployCrosshair", MilitaryUiSprites.IconCrosshair, 14, ch - 23, GoldColor);
            CreatePixelText(vp, "DEPLOYMENT BRIEFING", 8, GoldColor, TextAnchor.MiddleCenter, cx, cy + 193, cw, 14, useTitleFont: true);
            _deployVehicleLabel = CreatePixelText(vp, "", 8, LightCyan, TextAnchor.MiddleCenter, cx + 8, cy + 177, cw - 16, 12);
            CreateWeldSeam(card, 12, 174, cw - 24);

            // 5 Battle Mode rows (all EVEN coordinates & dimensions!)
            CreatePixelButton(vp, hp, "1v1 DUEL   - SAME-TIER RIVAL", cx + 14, cy + 150, cw - 28, 18, BtnSelected, PanelBorder, TextLight, 8, () =>
            {
                BattleSessionState.SelectedBattleMode = BattleMode.Duel1v1;
                RefreshAllUI();
            }, out _deployMode1v1Bg);

            CreatePixelButton(vp, hp, "2v2 SQUAD  - WINGMAN CLASH", cx + 14, cy + 130, cw - 28, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                BattleSessionState.SelectedBattleMode = BattleMode.Squad2v2;
                RefreshAllUI();
            }, out _deployMode2v2Bg);

            CreatePixelButton(vp, hp, "5v5 BATTLE - COMPANY WAR", cx + 14, cy + 110, cw - 28, 18, BtnBg, GoldColor, GoldColor, 8, () =>
            {
                BattleSessionState.SelectedBattleMode = BattleMode.Battle5v5;
                RefreshAllUI();
            }, out _deployMode5v5Bg);

            CreatePixelButton(vp, hp, "SURVIVAL   - ENDLESS WAVES", cx + 14, cy + 90, cw - 28, 18, BtnBg, PanelBorder, TextLight, 8, () =>
            {
                BattleSessionState.SelectedBattleMode = BattleMode.SurvivalWave;
                RefreshAllUI();
            }, out _deployModeWaveBg);

            CreatePixelButton(vp, hp, "RANGE      - FIRING PRACTICE", cx + 14, cy + 70, cw - 28, 18, BtnBg, LightCyan, LightCyan, 8, () =>
            {
                BattleSessionState.SelectedBattleMode = BattleMode.FiringRange;
                RefreshAllUI();
            }, out _deployModeRangeBg);

            // Map selector row inside Deploy Modal (live updates backdrop behind modal!)
            CreatePixelText(vp, "THEATER OF OPERATIONS", 8, TextMuted, TextAnchor.MiddleCenter, cx, cy + 57, cw, 10);
            CreatePixelButton(vp, hp, "DESERT", cx + 14, cy + 37, 86, 18, BtnSelected, GoldColor, TextLight, 8, () => SelectBattleMap(BattleMap.Desert), out _deployMapDesertBg);
            CreatePixelButton(vp, hp, "CITY", cx + 105, cy + 37, 86, 18, BtnBg, PanelBorder, TextLight, 8, () => SelectBattleMap(BattleMap.City), out _deployMapCityBg);
            CreatePixelButton(vp, hp, "FOREST", cx + 196, cy + 37, 86, 18, BtnBg, MbtGreen, TextLight, 8, () => SelectBattleMap(BattleMap.Forest), out _deployMapForestBg);

            // Bottom Action Buttons: BACK + DEPLOY!
            CreatePixelButton(vp, hp, "BACK [ESC]", cx + 14, cy + 10, 96, 22, BtnBg, PanelBorder, TextLight, 8, () => SetDeployModalOpen(false));
            CreatePrimaryCtaButton(vp, hp, "DEPLOY! (ENTER)", cx + 116, cy + 10, cw - 130, 22, LaunchBattleScene);
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
                if (_titleLogoStack != null) _titleLogoStack.dropProgress = 1f;
                for (int i = 0; i < _titleWingmen.Count; i++)
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

            Vector2[] wingOffsets =
            {
                new Vector2(-3.35f,  0.65f),
                new Vector2(-2.90f, -2.05f)
            };
            for (int i = 0; i < _titleWingmen.Count; i++)
            {
                TankEntity w = _titleWingmen[i];
                if (w == null) continue;
                float wingDelay = 0.55f + i * 0.35f;
                float wingDrop01 = _titleTransitioning ? 1f : Mathf.Clamp01((_titleTime - wingDelay) / 2.55f);
                float wingEase = 1f - Mathf.Pow(1f - wingDrop01, 2.2f);
                float wingSpacing = Mathf.Lerp(spacingPx * 1.38f, spacingPx, wingEase);

                w.SetAssemblyDrop(wingDrop01, dropHeightPx: 54f);
                Vector2 exitPush = (i == 0 ? new Vector2(-1f, 0.6f) : new Vector2(-1f, -0.6f)) * (exit01 * exit01 * 9.5f);
                Vector2 wPos = center + wingOffsets[i] + new Vector2(0f, Mathf.Sin(_titleTime * 1.2f + i * 2.1f) * 0.10f * wingEase) + exitPush;
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

            // 5. Continuous Track Dust behind convoy tanks once their hulls touch down!
            _titleDustTimer -= dt;
            if (_titleDustTimer <= 0f && !_titleTransitioning)
            {
                _titleDustTimer = 0.068f;
                if (showcaseTank != null && leadDrop01 >= 0.56f) EmitTitleTrackDust(showcaseTank, convoyDir);
                for (int i = 0; i < _titleWingmen.Count; i++)
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
