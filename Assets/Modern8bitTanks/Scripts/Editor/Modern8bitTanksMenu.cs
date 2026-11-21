#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Modern8bitTanks.Editor
{
    /// <summary>
    /// Builds real Unity Prefabs (M1A2_Abrams.prefab, T90_Tank.prefab, and 4 Desert Cover Prefabs),
    /// saves DesertGround.png & URPSpriteStackFlash.mat, and populates Assets/Scenes/DesertBattle.unity
    /// with every tank and cover prop pre-placed and visible directly in Scene View!
    /// Auto-runs once on load if Prefabs have not been built yet.
    /// </summary>
    [InitializeOnLoad]
    public static class Modern8bitTanksMenu
    {
        private const string BaseDir = "Assets/Modern8bitTanks";
        private const string PrefabDir = BaseDir + "/Prefabs";
        private const string MaterialDir = BaseDir + "/Materials";
        private const string TextureDir = BaseDir + "/Textures";
        private const string HangarScenePath = "Assets/Scenes/Hangar.unity";
        private const string BattleScenePath = "Assets/Scenes/DesertBattle.unity";

        static Modern8bitTanksMenu()
        {
            // Automatically build Prefabs (including expanded 10-prop battlefield) + Hangar & DesertBattle scenes once!
            bool needsFullRebuild = !File.Exists(PrefabDir + "/M1A2_Abrams.prefab")
                                    || !File.Exists(PrefabDir + "/Prop_DesertBunker.prefab");
            bool missingHangar = !File.Exists(HangarScenePath);
            if (needsFullRebuild || missingHangar)
            {
                EditorApplication.delayCall += () =>
                {
                    if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                    if (!File.Exists(PrefabDir + "/M1A2_Abrams.prefab") || !File.Exists(PrefabDir + "/Prop_DesertBunker.prefab"))
                        BuildPrefabsAndScene();
                    else if (!File.Exists(HangarScenePath))
                        BuildHangarSceneOnly();
                };
            }
        }

        [MenuItem("Tools/Modern 8bit Tanks/Build Hangar Scene & Register Build Settings")]
        public static void BuildHangarSceneOnly()
        {
            Directory.CreateDirectory("Assets/Scenes");

            // Ensure crisp HintedRaster pixel fonts are re-imported
            string[] fontGuids = AssetDatabase.FindAssets("t:Font", new[] { BaseDir + "/Resources/Fonts" });
            foreach (string g in fontGuids)
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(g), ImportAssetOptions.ForceUpdate);

            Sprite groundSprite = AssetDatabase.LoadAssetAtPath<Sprite>(TextureDir + "/DesertGround.png");
            GameObject abramsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/M1A2_Abrams.prefab");
            CreateAndSaveHangarScene(groundSprite, abramsPrefab);
            RegisterBuildScenes();
            AssetDatabase.Refresh();
            Debug.Log("[Modern 8bit Tanks] Created Assets/Scenes/Hangar.unity & registered Hangar + DesertBattle in Build Settings!");
        }

        [MenuItem("Tools/Modern 8bit Tanks/Build Prefabs & Setup Hangar + Battle Scenes")]
        public static void BuildPrefabsAndScene()
        {
            Directory.CreateDirectory(PrefabDir);
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(TextureDir);
            Directory.CreateDirectory("Assets/Scenes");

            GraphicsSettings.transparencySortMode = TransparencySortMode.CustomAxis;
            GraphicsSettings.transparencySortAxis = new Vector3(0f, 1f, 0f);

            // 1. Force crisp Point/Uncompressed textures & HintedRaster pixel fonts!
            string[] texGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { BaseDir + "/Resources" });
            foreach (string g in texGuids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (p.EndsWith("_slices.png"))
                    AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
            }
            string[] fontGuids = AssetDatabase.FindAssets("t:Font", new[] { BaseDir + "/Resources/Fonts" });
            foreach (string g in fontGuids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
            }

            // 2. Create/Load shared URP SpriteStack material asset
            string matPath = MaterialDir + "/URPSpriteStackFlash.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                Shader sh = AssetDatabase.LoadAssetAtPath<Shader>(BaseDir + "/Resources/Shaders/URPSpriteStackFlash.shader")
                            ?? Shader.Find("Modern8bitTanks/URPSpriteStackFlash")
                            ?? Shader.Find("Sprites/Default");
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, matPath);
            }

            // 3. Save DesertGround.png so the full sunlit desert map is visible right in Scene View!
            string groundPath = TextureDir + "/DesertGround.png";
            var groundTex = new Texture2D(DesertBattleManager.WorldWidthPx, DesertBattleManager.WorldHeightPx, TextureFormat.RGBA32, false);
            groundTex.SetPixels32(DesertBattleManager.GenerateDesertPixels());
            groundTex.Apply();
            File.WriteAllBytes(groundPath, groundTex.EncodeToPNG());
            Object.DestroyImmediate(groundTex);
            AssetDatabase.ImportAsset(groundPath, ImportAssetOptions.ForceUpdate);
            Sprite groundSprite = AssetDatabase.LoadAssetAtPath<Sprite>(groundPath);

            // 4. Build Tank Prefabs (M1A2_Abrams.prefab & T90_Tank.prefab)
            var tanksMeta = MetaLoader.LoadTanksMeta();
            GameObject abramsPrefab = BuildTankPrefab(
                "M1A2_Abrams",
                "abrams",
                tanksMeta.abrams,
                AssetDatabase.LoadAssetAtPath<Texture2D>(BaseDir + "/Resources/Tanks/abrams_hull_slices.png"),
                AssetDatabase.LoadAssetAtPath<Texture2D>(BaseDir + "/Resources/Tanks/abrams_turret_slices.png"),
                mat,
                startAngleDeg: 90f
            );

            GameObject t90Prefab = BuildTankPrefab(
                "T90_Tank",
                "t90",
                tanksMeta.t90,
                AssetDatabase.LoadAssetAtPath<Texture2D>(BaseDir + "/Resources/Tanks/t90_hull_slices.png"),
                AssetDatabase.LoadAssetAtPath<Texture2D>(BaseDir + "/Resources/Tanks/t90_turret_slices.png"),
                mat,
                startAngleDeg: -90f
            );

            // 5. Build ALL 10 Desert Cover & Landmark Prop Prefabs
            var propsMeta = MetaLoader.LoadPropsMeta();
            var propPrefabs = new Dictionary<string, GameObject>
            {
                ["sandbag_wall"] = BuildPropPrefab("Prop_SandbagWall", "sandbag_wall", propsMeta.sandbag_wall, mat),
                ["hesco_barrier"] = BuildPropPrefab("Prop_HescoBarrier", "hesco_barrier", propsMeta.hesco_barrier, mat),
                ["desert_ruin"] = BuildPropPrefab("Prop_DesertRuin", "desert_ruin", propsMeta.desert_ruin, mat),
                ["supply_crate"] = BuildPropPrefab("Prop_SupplyCrate", "supply_crate", propsMeta.supply_crate, mat),
                ["desert_bunker"] = BuildPropPrefab("Prop_DesertBunker", "desert_bunker", propsMeta.desert_bunker, mat),
                ["watch_tower"] = BuildPropPrefab("Prop_WatchTower", "watch_tower", propsMeta.watch_tower, mat),
                ["fuel_tank"] = BuildPropPrefab("Prop_FuelTank", "fuel_tank", propsMeta.fuel_tank, mat),
                ["palm_tree"] = BuildPropPrefab("Prop_PalmTree", "palm_tree", propsMeta.palm_tree, mat),
                ["desert_rock"] = BuildPropPrefab("Prop_DesertRock", "desert_rock", propsMeta.desert_rock, mat),
                ["tank_trap"] = BuildPropPrefab("Prop_TankTrap", "tank_trap", propsMeta.tank_trap, mat),
            };

            AssetDatabase.SaveAssets();

            // 6. Build Expanded 1280x860 Assets/Scenes/DesertBattle.unity with rich 3-lane battlefield!
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            float worldW = DesertBattleManager.WorldWidthPx / DesertBattleManager.PPU;
            float worldH = DesertBattleManager.WorldHeightPx / DesertBattleManager.PPU;

            // Main Camera (fixed-angle orthographic 2D)
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = (270f / DesertBattleManager.PPU) * 0.5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.11f, 0.09f, 0.06f, 1f);
            cam.allowMSAA = false;
            cam.transform.position = new Vector3(worldW * 0.5f, worldH * 0.5f, -20f);
            camGo.AddComponent<AudioListener>();

            // Pre-placed DesertGround (1280x860)
            var groundGo = new GameObject("DesertGround");
            groundGo.transform.position = new Vector3(worldW * 0.5f, worldH * 0.5f, 5f);
            var groundSr = groundGo.AddComponent<SpriteRenderer>();
            groundSr.sprite = groundSprite;
            groundSr.sortingOrder = -30000;

            // Pre-placed Base Tanks group
            var tanksRoot = new GameObject("Tanks").transform;
            var abInst = (GameObject)PrefabUtility.InstantiatePrefab(abramsPrefab, tanksRoot);
            abInst.transform.position = new Vector3(112f / DesertBattleManager.PPU, worldH * 0.5f, 0f);
            abInst.GetComponent<TankEntity>().initialAngleDeg = 90f;

            var t9Inst = (GameObject)PrefabUtility.InstantiatePrefab(t90Prefab, tanksRoot);
            t9Inst.transform.position = new Vector3((DesertBattleManager.WorldWidthPx - 112f) / DesertBattleManager.PPU, worldH * 0.5f, 0f);
            t9Inst.GetComponent<TankEntity>().initialAngleDeg = -90f;

            // Pre-placed 3-Lane 5v5 Desert Battlefield Landmarks (46 destructible Sprite-Stack objects!)
            var propsRoot = new GameObject("CoverProps").transform;
            var layout = new (string kind, float fx, float fy, float deg)[]
            {
                // --- CENTER OUTPOST (Contested concrete pad: bunkers, watchtowers, fuel depot, HESCO, crates!) ---
                ("desert_bunker",  0.50f, 0.50f,   0f),
                ("watch_tower",    0.45f, 0.56f,  12f),
                ("watch_tower",    0.55f, 0.44f, -12f),
                ("fuel_tank",      0.47f, 0.43f,   0f),
                ("fuel_tank",      0.53f, 0.57f,   0f),
                ("hesco_barrier",  0.42f, 0.50f,  90f),
                ("hesco_barrier",  0.58f, 0.50f,  90f),
                ("supply_crate",   0.44f, 0.46f,  14f),
                ("supply_crate",   0.56f, 0.54f, -14f),
                ("tank_trap",      0.41f, 0.43f,  25f),
                ("tank_trap",      0.59f, 0.57f, -25f),

                // --- NORTH CANYON LANE (Sandstone boulders, bunkers, ruins, flanking sniper positions!) ---
                ("desert_rock",    0.50f, 0.85f,  18f),
                ("desert_rock",    0.34f, 0.80f, -15f),
                ("desert_rock",    0.66f, 0.80f,  22f),
                ("desert_bunker",  0.38f, 0.72f, -12f),
                ("desert_bunker",  0.62f, 0.72f,  12f),
                ("desert_ruin",    0.25f, 0.82f,  10f),
                ("desert_ruin",    0.75f, 0.82f, -10f),
                ("sandbag_wall",   0.46f, 0.76f,   0f),
                ("sandbag_wall",   0.54f, 0.76f,   0f),
                ("fuel_tank",      0.32f, 0.70f,  90f),
                ("fuel_tank",      0.68f, 0.70f,  90f),
                ("supply_crate",   0.50f, 0.74f,   8f),

                // --- WEST & EAST FORWARD APPROACHES (Checkpoints, tank traps, sandbag nests!) ---
                ("sandbag_wall",   0.22f, 0.52f,  90f),
                ("sandbag_wall",   0.78f, 0.48f,  90f),
                ("hesco_barrier",  0.29f, 0.58f,  15f),
                ("hesco_barrier",  0.71f, 0.42f, -15f),
                ("hesco_barrier",  0.29f, 0.40f, -18f),
                ("hesco_barrier",  0.71f, 0.60f,  18f),
                ("tank_trap",      0.26f, 0.48f,  10f),
                ("tank_trap",      0.74f, 0.52f, -10f),
                ("watch_tower",    0.20f, 0.68f,   0f),
                ("watch_tower",    0.80f, 0.32f,   0f),
                ("desert_rock",    0.19f, 0.34f,  30f),
                ("desert_rock",    0.81f, 0.66f, -30f),

                // --- SOUTH OASIS & RUINED VILLAGE (Date palms around turquoise pool, ruins, ammo dumps!) ---
                ("palm_tree",      0.43f, 0.21f,  15f),
                ("palm_tree",      0.57f, 0.21f, -20f),
                ("palm_tree",      0.46f, 0.12f,  40f),
                ("palm_tree",      0.54f, 0.12f, -35f),
                ("palm_tree",      0.38f, 0.16f,  25f),
                ("palm_tree",      0.62f, 0.16f, -25f),
                ("desert_ruin",    0.34f, 0.26f, -15f),
                ("desert_ruin",    0.66f, 0.26f,  15f),
                ("desert_ruin",    0.24f, 0.18f,  12f),
                ("desert_ruin",    0.76f, 0.18f, -12f),
                ("supply_crate",   0.37f, 0.23f,   0f),
                ("supply_crate",   0.63f, 0.23f,   0f),
                ("tank_trap",      0.49f, 0.27f,  45f),
                ("tank_trap",      0.51f, 0.27f, -45f),
            };

            for (int i = 0; i < layout.Length; i++)
            {
                var item = layout[i];
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(propPrefabs[item.kind], propsRoot);
                inst.name = $"{propPrefabs[item.kind].name}_{i + 1:00}";
                inst.transform.position = new Vector3(worldW * item.fx, worldH * item.fy, 0f);
                var dp = inst.GetComponent<DestructibleProp>();
                dp.initialAngleDeg = item.deg;
                if (dp.stack != null) dp.stack.angleDeg = item.deg;
                dp.UpdateSortOrder();
            }

            // DesertBattleManager with Prefab & Ground references wired!
            var mgrGo = new GameObject("DesertBattleManager");
            var mgr = mgrGo.AddComponent<DesertBattleManager>();
            mgr.desertGroundRenderer = groundSr;
            mgr.abramsPrefab = abramsPrefab.GetComponent<TankEntity>();
            mgr.t90Prefab = t90Prefab.GetComponent<TankEntity>();

            EditorSceneManager.SaveScene(scene, BattleScenePath);

            // 7. Build Dedicated Hangar / Garage + WoT Tech Tree Scene (Assets/Scenes/Hangar.unity)
            CreateAndSaveHangarScene(groundSprite, abramsPrefab);

            // 8. Register BOTH scenes in Build Settings (0: Hangar, 1: DesertBattle)
            RegisterBuildScenes();

            AssetDatabase.Refresh();
            Debug.Log("[Modern 8bit Tanks] Built Hangar.unity (Garage + WoT Tech Tree) & DesertBattle.unity + registered both in Build Settings!");
        }

        private static void CreateAndSaveHangarScene(Sprite groundSprite, GameObject abramsPrefab)
        {
            float worldW = DesertBattleManager.WorldWidthPx / DesertBattleManager.PPU;
            float worldH = DesertBattleManager.WorldHeightPx / DesertBattleManager.PPU;

            var hangarScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var hCamGo = new GameObject("Main Camera");
            hCamGo.tag = "MainCamera";
            var hCam = hCamGo.AddComponent<Camera>();
            hCam.orthographic = true;
            hCam.orthographicSize = ((270f / HangarController.PPU) * 0.5f) / 1.65f;
            hCam.clearFlags = CameraClearFlags.SolidColor;
            hCam.backgroundColor = new Color(0.11f, 0.09f, 0.06f, 1f);
            hCam.allowMSAA = false;
            hCam.transform.position = new Vector3(worldW * 0.5f, worldH * 0.5f, -20f);
            hCamGo.AddComponent<AudioListener>();

            var hGroundGo = new GameObject("DesertGround");
            hGroundGo.transform.position = new Vector3(worldW * 0.5f, worldH * 0.5f, 5f);
            var hGroundSr = hGroundGo.AddComponent<SpriteRenderer>();
            hGroundSr.sprite = groundSprite;
            hGroundSr.sortingOrder = -30000;

            TankEntity hTank = null;
            if (abramsPrefab != null)
            {
                var hTankInst = (GameObject)PrefabUtility.InstantiatePrefab(abramsPrefab);
                hTankInst.name = "ShowcaseTank";
                hTankInst.transform.position = new Vector3(worldW * 0.5f, worldH * 0.5f + 0.35f, 0f);
                hTank = hTankInst.GetComponent<TankEntity>();
                hTank.initialAngleDeg = 35f;
                Selection.activeGameObject = hTankInst;
            }

            var hCtrlGo = new GameObject("HangarController");
            var hCtrl = hCtrlGo.AddComponent<HangarController>();
            hCtrl.desertGroundRenderer = hGroundSr;
            hCtrl.showcaseTank = hTank;

            EditorSceneManager.SaveScene(hangarScene, HangarScenePath);
        }

        private static void RegisterBuildScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(HangarScenePath, true),
                new EditorBuildSettingsScene(BattleScenePath, true),
            };
        }

        private static GameObject BuildTankPrefab(
            string prefabName,
            string tankType,
            TankMetaEntry meta,
            Texture2D hullSheet,
            Texture2D turretSheet,
            Material mat,
            float startAngleDeg)
        {
            var temp = new GameObject(prefabName);
            var tank = temp.AddComponent<TankEntity>();
            tank.InitializeInEditor(tankType, meta, hullSheet, turretSheet, DesertBattleManager.PPU, mat, startAngleDeg);

            string path = $"{PrefabDir}/{prefabName}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
            Object.DestroyImmediate(temp);
            return prefab;
        }

        private static GameObject BuildPropPrefab(
            string prefabName,
            string kind,
            PropMetaEntry meta,
            Material mat)
        {
            Texture2D sheet = AssetDatabase.LoadAssetAtPath<Texture2D>($"{BaseDir}/Resources/Props/prop_{kind}_slices.png");
            var temp = new GameObject(prefabName);
            var prop = temp.AddComponent<DestructibleProp>();
            prop.InitializeInEditor(kind, meta, sheet, 0f, DesertBattleManager.PPU, mat);

            string path = $"{PrefabDir}/{prefabName}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
            Object.DestroyImmediate(temp);
            return prefab;
        }
    }
}
#endif
