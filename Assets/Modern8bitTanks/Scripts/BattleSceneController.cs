using Cysharp.Threading.Tasks;
using VContainer;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Modern8bitTanks
{
    public enum GamePhase
    {
        Title,
        Playing,
        RoundOver,
        Briefing
    }

    /// <summary>
    /// Coordinates battle rounds, vehicle simulation, and presentation for the selected map.
    /// </summary>
    public partial class BattleSceneController : MonoBehaviour
    {
        public const int WorldWidthPx = 2048;
        public const int WorldHeightPx = 1360;
        public const int BorderPadPx = 24;
        public const float PPU = 16f;
        internal const int SecondaryWeaponSlotIndex = 2;
        private int MapWidthPx => UsesControlPoints ? BattlefieldLayout.Width : WorldWidthPx;
        private int MapHeightPx => UsesControlPoints ? BattlefieldLayout.Height : WorldHeightPx;
        private float WorldW => MapWidthPx / PPU;
        private float WorldH => MapHeightPx / PPU;
        private float BorderPad => BorderPadPx / PPU;

        [Header("Scene & Prefab References (Pre-placed in Battle.unity)")]
        [UnityEngine.Serialization.FormerlySerializedAs("desertGroundRenderer")]
        public SpriteRenderer terrainRenderer;
        [SerializeField] private TankEntity _tankPrefab;
        [SerializeField] private Transform _tanksRoot;
        [SerializeField] private TankEntity _playerTank;
        [SerializeField] private TankEntity _primaryEnemyTank;

        [Header("Battle Settings")]
        public string selectedPlayerVehicleId = "us_m1a2_sep2";
        public string selectedEnemyVehicleId = "ru_t90a";
        public BattleMode battleMode = BattleMode.Duel1v1;
        public BattleMap battleMap = BattleMap.Desert;
        public BattleCameraMode cameraMode = BattleCameraMode.FixedTop;
        public bool arcadeWASD = false;
        public bool aiEnabled = true;
        public bool sfxEnabled = true;
        [Range(0f, 1f)] public float sfxVolume = 1.0f;
        public bool bgmEnabled = true;
        [Range(0f, 1f)] public float bgmVolume = 0.60f;

        [Header("Sprite Stacking Settings")]
        [Range(0.45f, 1.0f)] public float squash = 0.80f;
        [Range(0f, 2.5f)] public float spacingPx = 0.72f;

        [Header("Presentation")]
        public bool integerWindowbox = false;
        public bool postProcessEnabled = true;

        private PixelWindowbox _windowbox;
        private BattlePostProcess _postProcess;
        private MapAtmosphere _atmosphere;
        [SerializeField] private BattleHUD _hud;
        [SerializeField] private Material _emissiveFxMat;
        [SerializeField] private Material _groundHd2dMat;
        private MaterialPropertyBlock _fxMpb;
        private static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");
        [SerializeField] private Sprite _smokePuffSprite;

        [SerializeField] private BattleAudioController _audio;

        [SerializeField] private Sprite _whitePxSprite;

        // Runtime terrain texture
        private Texture2D _runtimeGroundTex;
        private Sprite _runtimeGroundSprite;
        private Color32[] _cleanGroundPixels;
        private Color32[] _groundPixels;
        private bool _groundDirty;

        // 63-Vehicle Tech Tree Catalog & Lazy-Cached Slice Sheets
        private VehicleDef[] _allVehicles = System.Array.Empty<VehicleDef>();
        private readonly Dictionary<string, VehicleDef> _vehicleMap = new();
        private readonly Dictionary<string, Texture2D> _hullTexCache = new();
        private readonly Dictionary<string, Texture2D> _turretTexCache = new();
        [SerializeField] private Material _stackFlashMat;
        private bool _enemyManuallySelected;

        // Scene & dynamic entities
        private readonly List<TankEntity> _tanks = new();
        private readonly List<DestructibleProp> _props = new();
        [SerializeField] private BattleSpawnAssets spawnAssets;
        [SerializeField] private Transform _fxRoot;
        private Camera _cam;
        private BattleSessionState _sessionState;
        private GameSceneLoader _sceneLoader;
        private MultiplayerSessionState _multiplayer;
        private BattleVisibility _visibility;
        private ControlPointSystem _controlPoints;
        private System.Func<Vector2, Vector2, bool> _hasLineOfSight;

        [Inject]
        private void Construct(Camera worldCamera, BattleSessionState sessionState, GameSceneLoader sceneLoader,
            MultiplayerSessionState multiplayer, BattleVisibility visibility, ControlPointSystem controlPoints,
            PixelWindowbox windowbox, BattlePostProcess postProcess, MapAtmosphere atmosphere)
        {
            _cam = worldCamera;
            _sessionState = sessionState;
            _sceneLoader = sceneLoader;
            _multiplayer = multiplayer;
            _visibility = visibility;
            _controlPoints = controlPoints;
            _hasLineOfSight = HasLineOfSight;
            _getNetworkViewport = GetNetworkViewport;
            _windowbox = windowbox;
            _postProcess = postProcess;
            _atmosphere = atmosphere;
        }
        private float _hitStop;
        private float _shake;

        // Phase / Wave / Aftermath state
        private GamePhase _phase = GamePhase.Title;
        private int _currentWave = 1;
        private float _nextWaveDelay;
        private bool _waitingNextWave;
        private float _aftermathTimer;
        private bool _resultBannerShown;
        private string _pendingWinner; // "ally" or "enemy"
        private string _pendingSubtitle;
        private Vector2 _killFocusPos;
        private float _killZoomBoost;
        private int _winsAlly;
        private int _winsEnemy;
        private int _rangeKills;
        private readonly List<RangeTargetSlot> _rangeTargets = new();

        private class RangeTargetSlot
        {
            public TankEntity tank;
            public Vector2 spawnPos;
            public float spawnAngle;
            public bool isMovingPatrol;
            public float patrolDir;
            public float respawnTimer;
        }

        // Aim overlay
        [SerializeField] private LineRenderer _aimLine;
        [SerializeField] private SpriteRenderer _reticleSr;

        public VehicleDef[] AllVehicles => _allVehicles;
        public VehicleDef SelectedPlayerVehicle => GetVehicleDef(selectedPlayerVehicleId);
        public VehicleDef SelectedEnemyVehicle => GetVehicleDef(selectedEnemyVehicleId);

        internal UniTask Initialization { get; private set; }
        private bool _initialized;

        private void Awake()
        {
            Initialization = InitializeAsync(destroyCancellationToken).Preserve();
            if (!_sceneLoader.IsLoading) Initialization.Forget();
        }

        private async UniTask InitializeAsync(CancellationToken cancellationToken)
        {
            await UniTask.NextFrame(cancellationToken: cancellationToken);
            var budget = new BattleLoadBudget(cancellationToken);
            _cam.orthographic = true;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.11f, 0.09f, 0.06f, 1f);

            // Crisp low-slung hull/turret height with clean vertical pixel preservation for small & large tanks alike!
            squash = 0.80f;
            spacingPx = 0.72f;

            // Pull player's Hangar selection & persistent settings from BattleSessionState!
            selectedPlayerVehicleId = _sessionState.SelectedPlayerVehicleId;
            battleMode = _sessionState.SelectedBattleMode;
            battleMap = _sessionState.SelectedBattleMap;
            cameraMode = BattleCameraMode.FixedTop;
            _sessionState.SelectedCameraMode = BattleCameraMode.FixedTop;
            arcadeWASD = _sessionState.ArcadeWASD;
            aiEnabled = _sessionState.AiEnabled;
            sfxEnabled = _sessionState.SfxEnabled;
            sfxVolume = _sessionState.SfxVolume;
            bgmEnabled = _sessionState.BgmEnabled;
            bgmVolume = _sessionState.BgmVolume;
            postProcessEnabled = _sessionState.PostProcessEnabled;
            integerWindowbox = _sessionState.IntegerWindowbox;
            if (_multiplayer.IsInBattle)
            {
                var room = _multiplayer.Connection.Room;
                int localSlot = _multiplayer.LocalSlot;
                selectedPlayerVehicleId = room.GetVehicleId(localSlot);
                const int teamSize = MultiplayerSessionConnection.PlayersPerTeam;
                int firstOpponentSlot = MultiplayerSessionConnection.IsAlphaSlot(localSlot) ? teamSize : 0;
                for (int slot = firstOpponentSlot; slot < firstOpponentSlot + teamSize; slot++)
                {
                    var id = room.GetVehicleId(slot);
                    if (id == null) continue;
                    selectedEnemyVehicleId = id;
                    break;
                }
                battleMode = BattleMode.Battle5v5;
                battleMap = BattleMap.Desert;
                aiEnabled = false;
            }
            _winsAlly = _sessionState.WinsAlly;
            _winsEnemy = _sessionState.WinsEnemy;

            _windowbox.Initialize(_cam, integerWindowbox, useHd2d: postProcessEnabled);
            _lastValidMouseWorld = new Vector2(WorldW * 0.5f, WorldH * 0.5f);

            _postProcess.postProcessEnabled = postProcessEnabled;
            // Apply post-processing at screen resolution after the low-resolution game render.
            _postProcess.Initialize(_windowbox.PresentCamera);
            _postProcess.ApplyMapTheme(battleMap);

            LoadVehicleCatalog();
            await budget.YieldIfSpentAsync();
            await LoadRuntimeResourcesAsync(cancellationToken);

            _audio.Initialize(_sfxTrackClatter, _sfxTurretServo, _sfxMissileMotor);
            _audio.StartBattleBgm(_sfxBattleBgm);

            _atmosphere.Initialize(WorldW, WorldH, _whitePxSprite, _emissiveFxMat);
            _atmosphere.ApplyMapTheme(battleMap);

            _props.AddRange(FindObjectsByType<DestructibleProp>(FindObjectsInactive.Include));
            _reticleTf = _reticleSr.transform;
            _reticleSr.sprite = _whitePxSprite;

            _hud.Initialize(this, _windowbox, _multiplayer);

            await budget.YieldIfSpentAsync();
            await ResetRoundAsync(budget);
            InitializeMultiplayerBattle();
            _initialized = true;
            _sceneLoader.ReportBattleInitialization(1f);
        }

        private void LoadVehicleCatalog()
        {
            VehicleCatalog cat = MetaLoader.LoadVehicleCatalog();
            _allVehicles = cat != null && cat.vehicles != null ? cat.vehicles : System.Array.Empty<VehicleDef>();
            _vehicleMap.Clear();
            foreach (var v in _allVehicles)
            {
                if (v != null && !string.IsNullOrEmpty(v.id))
                    _vehicleMap[v.id] = v;
            }

            if (!_vehicleMap.ContainsKey(selectedPlayerVehicleId) && _allVehicles.Length > 0)
                selectedPlayerVehicleId = _allVehicles[0].id;
            if (!_vehicleMap.ContainsKey(selectedEnemyVehicleId) && _allVehicles.Length > 0)
                selectedEnemyVehicleId = _allVehicles[_allVehicles.Length - 1].id;
        }

        public VehicleDef GetVehicleDef(string id)
        {
            if (!string.IsNullOrEmpty(id) && _vehicleMap.TryGetValue(id, out var def))
                return def;
            return _allVehicles.Length > 0 ? _allVehicles[0] : null;
        }

        public List<VehicleDef> GetVehiclesFor(string nation, string tab)
        {
            var list = new List<VehicleDef>();
            foreach (var v in _allVehicles)
            {
                if (v != null && v.nation == nation && v.tab == tab)
                    list.Add(v);
            }
            return list;
        }

        public void SelectPlayerVehicle(string id)
        {
            if (!_vehicleMap.ContainsKey(id)) return;
            selectedPlayerVehicleId = id;
            _sessionState.SelectedPlayerVehicleId = id;
        }

        public void SelectEnemyVehicle(string id)
        {
            if (!_vehicleMap.ContainsKey(id)) return;
            selectedEnemyVehicleId = id;
        }

        private VehicleDef PickOpposingRivalFor(VehicleDef playerDef)
        {
            if (playerDef == null || _allVehicles.Length == 0) return null;
            string rivalNation = playerDef.nation switch
            {
                "USA" => "USSR",
                "GER" => "USSR",
                _ => "USA"
            };

            VehicleDef best = null;
            int bestScore = int.MaxValue;
            foreach (var v in _allVehicles)
            {
                if (v == null || v.nation != rivalNation) continue;
                int tierDiff = Mathf.Abs(v.tier - playerDef.tier) * 10;
                int roleDiff = (v.role == playerDef.role) ? 0 : 3;
                int score = tierDiff + roleDiff;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = v;
                }
            }
            return best ?? _allVehicles[0];
        }

        private VehicleDef PickAllyWingmanFor(VehicleDef playerDef)
        {
            if (playerDef == null || _allVehicles.Length == 0) return playerDef;
            VehicleDef best = null;
            int bestScore = int.MaxValue;
            foreach (var v in _allVehicles)
            {
                if (v == null || v.nation != playerDef.nation || v.id == playerDef.id) continue;
                int score = Mathf.Abs(v.tier - playerDef.tier) * 10 + (v.role != playerDef.role ? 0 : 4);
                if (score < bestScore) { bestScore = score; best = v; }
            }
            return best ?? playerDef;
        }

        private bool TryDeployTank(TankEntity tank, VehicleDef def, string faction, Vector2 position, float angleDeg,
            bool isPlayerTank)
        {
            if (tank == null) return false;
            if (def == null)
            {
                tank.gameObject.SetActive(false);
                return false;
            }

            if (!_hullTexCache.TryGetValue(def.id, out Texture2D hullTex))
            {
                hullTex = Resources.Load<Texture2D>($"Vehicles/{def.id}_hull_slices");
                _hullTexCache[def.id] = hullTex;
            }
            if (!_turretTexCache.TryGetValue(def.id, out Texture2D turretTex))
            {
                turretTex = Resources.Load<Texture2D>($"Vehicles/{def.id}_turret_slices");
                _turretTexCache[def.id] = turretTex;
            }
            if (!tank.TrySpawnAsVehicle(def, hullTex, turretTex, position, angleDeg, _stackFlashMat))
            {
                tank.gameObject.SetActive(false);
                return false;
            }

            tank.isPlayer = isPlayerTank;
            tank.factionTeam = faction;
            tank.gameObject.SetActive(true);
            if (!_tanks.Contains(tank)) _tanks.Add(tank);
            return true;
        }

        private async UniTask LoadRuntimeResourcesAsync(CancellationToken cancellationToken)
        {
            _sfxCannonAbrams = await LoadBattleClipAsync("cannon", cancellationToken);
            _sfxCannonT90 = await LoadBattleClipAsync("cannon2", cancellationToken);
            _sfxRicochet = await LoadBattleClipAsync("ricochet", cancellationToken);
            _sfxExplosion = await LoadBattleClipAsync("explosion", cancellationToken);
            _sfxMg = await LoadBattleClipAsync("mg", cancellationToken);

            _sceneLoader.ReportBattleInitialization(0.08f);
            _sfxAbramsTurbine = await LoadBattleClipAsync("engine_abrams", cancellationToken);
            _sfxT90Diesel = await LoadBattleClipAsync("engine_t90", cancellationToken);
            _sfxTrackClatter = await LoadBattleClipAsync("tracks", cancellationToken);
            _sfxTurretServo = await LoadBattleClipAsync("turret_servo", cancellationToken);
            _sfxReloadClank = await LoadBattleClipAsync("reload_clank", cancellationToken);
            _sfxSmokePop = await LoadBattleClipAsync("smoke_pop", cancellationToken);
            _sfxMetalCrash = await LoadBattleClipAsync("metal_crash", cancellationToken);
            _sfxMissileLaunch = await LoadBattleClipAsync("missile_launch", cancellationToken) ?? _sfxSmokePop;
            _sfxMissileHeavy = await LoadBattleClipAsync("missile_heavy", cancellationToken) ?? _sfxMissileLaunch;
            _sfxMissileMotor = await LoadBattleClipAsync("missile_motor", cancellationToken);
            _sfxBattleBgm = await LoadBattleClipAsync("battle_bgm", cancellationToken);
            _sceneLoader.ReportBattleInitialization(0.18f);
            await LoadVoiceResourcesAsync(cancellationToken);

            _fxMpb = new MaterialPropertyBlock();
        }

        private static async UniTask<AudioClip> LoadBattleClipAsync(string path, CancellationToken cancellationToken)
        {
            var request = Resources.LoadAsync<AudioClip>($"SFX/{path}");
            var clip = (AudioClip)await request.ToUniTask(cancellationToken: cancellationToken);
            if (clip == null) return null;

            // Some importers decode synchronously. Give each unloaded clip its own frame.
            if (clip.loadState == AudioDataLoadState.Unloaded)
            {
                await UniTask.NextFrame(cancellationToken: cancellationToken);
                clip.LoadAudioData();
            }
            while (clip.loadState == AudioDataLoadState.Loading)
                await UniTask.NextFrame(cancellationToken: cancellationToken);

            return clip.loadState == AudioDataLoadState.Failed ? null : clip;
        }

        public void TogglePostProcess()
        {
            postProcessEnabled = !postProcessEnabled;
            if (_postProcess != null) _postProcess.SetEnabled(postProcessEnabled);
            if (_windowbox != null) _windowbox.SetHd2dMode(postProcessEnabled);
        }

        private void PrepareRuntimeGround()
        {
            if (_runtimeGroundTex != null &&
                _runtimeGroundTex.width == MapWidthPx && _runtimeGroundTex.height == MapHeightPx)
                return;

            ReleaseRuntimeGround();
            _runtimeGroundTex = new Texture2D(MapWidthPx, MapHeightPx, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            _groundPixels = new Color32[MapWidthPx * MapHeightPx];

            terrainRenderer.enabled = true;
            terrainRenderer.transform.position = new Vector3(WorldW * 0.5f, WorldH * 0.5f, 5f);
            terrainRenderer.sortingOrder = -30000;
            if (_groundHd2dMat != null) terrainRenderer.sharedMaterial = _groundHd2dMat;
            _runtimeGroundSprite = Sprite.Create(
                _runtimeGroundTex,
                new Rect(0, 0, MapWidthPx, MapHeightPx),
                new Vector2(0.5f, 0.5f),
                PPU
            );
            terrainRenderer.sprite = _runtimeGroundSprite;
        }

        private void ReleaseRuntimeGround()
        {
            if (_runtimeGroundSprite != null) Destroy(_runtimeGroundSprite);
            if (_runtimeGroundTex != null) Destroy(_runtimeGroundTex);
        }

        public void CycleCameraMode()
        {
            cameraMode = cameraMode switch
            {
                BattleCameraMode.TurretLock => BattleCameraMode.HullLock,
                BattleCameraMode.HullLock => BattleCameraMode.FixedTop,
                _ => BattleCameraMode.TurretLock
            };
            _sessionState.SelectedCameraMode = cameraMode;
            string msg = cameraMode switch
            {
                BattleCameraMode.TurretLock => "CAM: TURRET LOCK [C]",
                BattleCameraMode.HullLock => "CAM: HULL LOCK [C]",
                _ => "CAM: FIXED TOP [C]"
            };
            _hud?.ShowWaveToast(msg, 1.35f);
        }

        public void SyncSettingsToSession()
        {
            if (!_multiplayer.IsInBattle)
            {
                _sessionState.SelectedPlayerVehicleId = selectedPlayerVehicleId;
                _sessionState.SelectedBattleMode = battleMode;
                _sessionState.AiEnabled = aiEnabled;
            }
            _sessionState.SelectedCameraMode = cameraMode;
            _sessionState.ArcadeWASD = arcadeWASD;
            _sessionState.SfxEnabled = sfxEnabled;
            _sessionState.SfxVolume = sfxVolume;
            _sessionState.BgmEnabled = bgmEnabled;
            _sessionState.BgmVolume = bgmVolume;
            _sessionState.PostProcessEnabled = postProcessEnabled;
            _sessionState.IntegerWindowbox = integerWindowbox;
            _sessionState.WinsAlly = _winsAlly;
            _sessionState.WinsEnemy = _winsEnemy;
            _sessionState.SaveSettings();
        }

        public void ToggleSfxEnabled()
        {
            _sessionState.SfxEnabled = sfxEnabled;
            _sessionState.SfxVolume = sfxVolume;
            _sessionState.ToggleSfxEnabled();
            sfxEnabled = _sessionState.SfxEnabled;
            sfxVolume = _sessionState.SfxVolume;
            PlayUiPreviewSfx();
        }

        public void StepSfxVolume(float delta)
        {
            _sessionState.SfxEnabled = sfxEnabled;
            _sessionState.SfxVolume = sfxVolume;
            _sessionState.StepSfxVolume(delta);
            sfxEnabled = _sessionState.SfxEnabled;
            sfxVolume = _sessionState.SfxVolume;
            PlayUiPreviewSfx();
        }

        public void ToggleBgmEnabled()
        {
            _sessionState.BgmEnabled = bgmEnabled;
            _sessionState.BgmVolume = bgmVolume;
            _sessionState.ToggleBgmEnabled();
            bgmEnabled = _sessionState.BgmEnabled;
            bgmVolume = _sessionState.BgmVolume;
        }

        public void StepBgmVolume(float delta)
        {
            _sessionState.BgmEnabled = bgmEnabled;
            _sessionState.BgmVolume = bgmVolume;
            _sessionState.StepBgmVolume(delta);
            bgmEnabled = _sessionState.BgmEnabled;
            bgmVolume = _sessionState.BgmVolume;
        }

        public void PlayUiPreviewSfx()
        {
            if (sfxEnabled && sfxVolume > 0.001f && _sfxReloadClank != null)
                PlaySFXAt(_sfxReloadClank, GetAudioListenerPos(), SoundBus.Dry, 0.65f, centerPan: true, pitchMin: 0.98f, pitchMax: 1.02f);
        }

        public void EnterTitleScreen()
        {
            ReturnToHangar();
        }

        public void ReturnToOnlineRoom()
        {
            if (!_multiplayer.IsInBattle) return;
            SyncSettingsToSession();
            _multiplayer.Connection.ReturnToRoomAsync().Forget();
        }

        public void ReturnToHangar()
        {
            if (_multiplayer.IsInBattle)
            {
                SyncSettingsToSession();
                _multiplayer.Connection.LeaveAsync().Forget();
                return;
            }
            Time.timeScale = 1f;
            StopLoopingAudio();
            _audio?.StopBattleBgm();
            SyncSettingsToSession();
            _sceneLoader.LoadAsync(BattleSessionState.HangarSceneName).Forget();
        }

        public void ResetRound()
        {
            if (_initialized) ResetRoundAsync().GetAwaiter().GetResult();
        }

        private async UniTask ResetRoundAsync(BattleLoadBudget budget = null)
        {
            if (_multiplayer.IsInBattle && _networkTanks != null) return;
            Time.timeScale = 1f;
            _phase = UsesControlPoints ? GamePhase.Briefing : GamePhase.Playing;
            _briefingConfirmed = false;
            _currentWave = 1;
            _waitingNextWave = false;
            _aftermathTimer = 0f;
            _resultBannerShown = false;
            _pendingWinner = null;
            _wonByControl = false;
            _killZoomBoost = 0f;
            PlayerGroundAim = null;
            _hitStop = 0f;
            _shake = 0f;
            _camKickPos = Vector2.zero;
            _camKickVel = Vector2.zero;
            _windowbox?.SetTiltShiftParams(1.0f, 0.50f, 0.25f);

            // Pick a fresh RANDOM same-Tier performance-matched rival every battle!
            VehicleDef playerDef = SelectedPlayerVehicle;
            VehicleDef randRival = _multiplayer.IsInBattle ? null : BattleSessionState.PickRandomSameTierRival(_allVehicles, playerDef, excludeId: selectedEnemyVehicleId);
            if (randRival != null)
                selectedEnemyVehicleId = randRival.id;

            ClearDynamicFXAndExtraTanks();
            battleMap = _multiplayer.IsInBattle ? BattleMap.Desert : _sessionState.SelectedBattleMap;
            await PrepareBattlefieldLayoutAsync(budget);
            PrepareRuntimeGround();
            System.Action<float> terrainProgress = null;
            if (budget != null)
            {
                terrainProgress = progress => _sceneLoader.ReportBattleInitialization(Mathf.Lerp(0.38f, 0.74f, progress));
                terrainProgress(0f);
                await budget.YieldIfSpentAsync();
            }
            _cleanGroundPixels = UsesControlPoints
                ? await CurrentLayout.GroundPixelsAsync(budget, terrainProgress)
                : await GenerateMapPixelsAsync(battleMap, MapWidthPx, MapHeightPx, budget, terrainProgress);
            RestoreCleanGround();
            if (budget != null) await budget.YieldIfSpentAsync();
            await RefreshExteriorTerrainAsync(budget);
            if (budget != null) await budget.YieldIfSpentAsync();
            _postProcess?.ApplyMapTheme(battleMap);
            _atmosphere?.ApplyMapTheme(battleMap);
            if (budget != null) _sceneLoader.ReportBattleInitialization(0.80f);
            await ClearAndSpawnMapPropsAsync(battleMap, budget);
            ResetControlPoint();

            if (budget != null) _sceneLoader.ReportBattleInitialization(0.90f);
            await SpawnModeTanksAsync(budget);

            TankEntity player = GetPlayerTank();
            if (player != null)
                _camPos = player.Position2D;
            _camRollDeg = 0f;
            SpriteStackCamera.SetRoll(0f);
            if (_cam != null)
            {
                Vector2 halfSize = BattleViewport.HalfSizeFor(player, _cam.aspect);
                _cam.orthographicSize = halfSize.y;
                Vector2 center = ConstrainCameraCenter(_camPos, halfSize);
                _cam.transform.SetPositionAndRotation(new Vector3(center.x, center.y, -20f), Quaternion.identity);
            }
            if (_phase == GamePhase.Playing) StartPlayerEngineAudio(player);
            else StopLoopingAudio();

            if (_hud != null)
            {
                _hud.ClearPopups();
                _hud.ClearHitCamera();
                _hud.HideResult();
                VehicleDef eDef = SelectedEnemyVehicle;
                string eLabel = eDef != null ? $"{eDef.name.ToUpper()} ({eDef.TierRoman})" : "HOSTILE";
                string pTier = playerDef != null ? playerDef.TierRoman : "IX";
                string mapTag = UiText.Get(GetMapDisplayName(battleMap));
                string startMsg = battleMode switch
                {
                    BattleMode.FiringRange => UiText.Format("{0}  FIRING RANGE  [R] RESET", mapTag),
                    BattleMode.Battle5v5 => UiText.Format("{0}  5v5 (TIER {1})", mapTag, pTier),
                    BattleMode.Squad2v2 => UiText.Format("{0}  2v2 VS {1}", mapTag, eLabel),
                    BattleMode.SurvivalWave => UiText.Format("{0}  WAVE 1", mapTag),
                    _ => UiText.Format("{0}  VS  {1}", mapTag, eLabel)
                };
                if (_phase == GamePhase.Briefing) await ShowBattleBriefingAsync(budget);
                else
                {
                    _hud.HideBriefing();
                    _hud.ShowWaveToast(startMsg, 2.2f);
                }
            }

            ResetVoiceState();
            if (_phase == GamePhase.Playing && !_multiplayer.IsInBattle)
                NotifyVoiceBattleStart();
        }

        private void ClearDynamicFXAndExtraTanks()
        {
            foreach (Transform effect in _fxRoot)
            {
                effect.gameObject.SetActive(false);
                Destroy(effect.gameObject);
            }
            _projectiles.Clear();
            _particles.Clear();
            _smokeClouds.Clear();
            _radarSmokes.Clear();

            for (int i = _tanks.Count - 1; i >= 0; i--)
            {
                if (_tanks[i] != null && _tanks[i].isExtraSpawned)
                    Destroy(_tanks[i].gameObject);
            }
            _tanks.Clear();
        }

        private void RestoreCleanGround()
        {
            System.Array.Copy(_cleanGroundPixels, _groundPixels, _cleanGroundPixels.Length);
            _runtimeGroundTex.SetPixels32(_groundPixels);
            _runtimeGroundTex.Apply(false, false);
            _groundDirty = false;
        }

        private async UniTask SpawnModeTanksAsync(BattleLoadBudget budget)
        {
            _rangeTargets.Clear();
            _tanks.Clear();
            if (_multiplayer.IsInBattle)
            {
                await SpawnMultiplayerTanksAsync(budget);
                return;
            }
            VehicleDef playerDef = SelectedPlayerVehicle;
            VehicleDef enemyDef = SelectedEnemyVehicle;

            if (battleMode == BattleMode.FiringRange)
            {
                SpawnFiringRangeUnits(playerDef);
                return;
            }

            float westX = WorldW * 0.08f;
            float eastX = WorldW * 0.92f;

            TryDeployTank(_playerTank, playerDef, "ally",
                UsesControlPoints ? BattlefieldSpawn(true, 0) : new Vector2(westX, WorldH * 0.50f), 90f, isPlayerTank: true);
            if (budget != null) await budget.YieldIfSpentAsync();
            TryDeployTank(_primaryEnemyTank, enemyDef, "enemy",
                UsesControlPoints ? BattlefieldSpawn(false, 0) : new Vector2(eastX, WorldH * 0.50f), -90f, isPlayerTank: false);
            if (budget != null) await budget.YieldIfSpentAsync();

            if (battleMode == BattleMode.Squad2v2)
            {
                VehicleDef allyWing = BattleSessionState.PickRandomSameTierAlly(_allVehicles, playerDef);
                VehicleDef enemyWing = BattleSessionState.PickRandomSameTierRival(_allVehicles, playerDef, excludeId: enemyDef != null ? enemyDef.id : null);
                SpawnTankFromPrefab(allyWing, "ally", BattlefieldSpawn(true, 3), 90f, isPlayerTank: false);
                if (budget != null) await budget.YieldIfSpentAsync();
                SpawnTankFromPrefab(enemyWing, "enemy", BattlefieldSpawn(false, 3), -90f, isPlayerTank: false);
            }
            else if (battleMode == BattleMode.Battle5v5)
            {
                string prevEnemyId = enemyDef != null ? enemyDef.id : null;
                for (int k = 0; k < 4; k++)
                {
                    VehicleDef allyMate = BattleSessionState.PickRandomSameTierAlly(_allVehicles, playerDef);
                    int deploymentSlot = k + 1;
                    SpawnTankFromPrefab(allyMate, "ally", BattlefieldSpawn(true, deploymentSlot), 90f, isPlayerTank: false);
                    if (budget != null) await budget.YieldIfSpentAsync();

                    VehicleDef foe = BattleSessionState.PickRandomSameTierRival(_allVehicles, playerDef, excludeId: prevEnemyId);
                    if (foe != null) prevEnemyId = foe.id;
                    SpawnTankFromPrefab(foe, "enemy", BattlefieldSpawn(false, deploymentSlot), -90f, isPlayerTank: false);
                    if (budget != null) await budget.YieldIfSpentAsync();
                }
            }
        }

        private TankEntity SpawnTankFromPrefab(VehicleDef def, string faction, Vector2 position, float angleDeg, bool isPlayerTank)
        {
            if (_tankPrefab == null || def == null) return null;

            TankEntity tank = Instantiate(_tankPrefab, new Vector3(position.x, position.y, 0f), Quaternion.identity, _tanksRoot);
            tank.name = $"{faction}_{def.id}_{_tanks.Count}";
            tank.isExtraSpawned = true;
            if (TryDeployTank(tank, def, faction, position, angleDeg, isPlayerTank))
                return tank;

            Destroy(tank.gameObject);
            return null;
        }

        private VehicleDef PickSurvivalWaveEnemy(int wave, int slotIndex)
        {
            VehicleDef baseEnemy = SelectedEnemyVehicle;
            if (baseEnemy == null || _allVehicles.Length == 0) return baseEnemy;
            string nation = baseEnemy.nation;

            // Wave 1 = chosen rival; Wave 2+ escalates tiers across the rival nation, bringing Special Bosses on Wave 4+!
            int targetTier = Mathf.Clamp(baseEnemy.tier + (wave - 2) + (slotIndex == 0 && wave >= 4 ? 2 : 0),
                VehicleDef.LowestTier, VehicleDef.SpecialTier);
            var candidates = new List<VehicleDef>();
            foreach (var v in _allVehicles)
            {
                if (v != null && v.nation == nation && Mathf.Abs(v.tier - targetTier) <= 1)
                    candidates.Add(v);
            }
            if (candidates.Count == 0) return baseEnemy;
            return candidates[(wave * 3 + slotIndex) % candidates.Count];
        }

        private void SpawnNextSurvivalWave()
        {
            _currentWave++;
            _waitingNextWave = false;

            // Remove previous dead enemy wrecks except base scene tank
            for (int i = _tanks.Count - 1; i >= 0; i--)
            {
                if (_tanks[i] != null && _tanks[i].isExtraSpawned)
                {
                    Destroy(_tanks[i].gameObject);
                    _tanks.RemoveAt(i);
                }
            }

            // Resupply and recover repairable modules between waves.
            TankEntity player = GetPlayerTank();
            if (player != null && player.alive)
            {
                player.RestockAndRepair();
                AddPopup(player.Position2D + Vector2.up * 1.5f, "FIELD SERVICE", new Color(0.50f, 0.96f, 0.45f));
            }

            int enemyCount = Mathf.Min(4, _currentWave); // Wave 2 -> 2 enemies, Wave 3 -> 3, Wave 4+ -> 4!

            Vector2[] spawnPts =
            {
                new Vector2(WorldW - 5.5f, WorldH * 0.50f),
                new Vector2(WorldW - 5.5f, WorldH * 0.22f),
                new Vector2(WorldW - 5.5f, WorldH * 0.78f),
                new Vector2(WorldW * 0.50f, WorldH - 3.6f),
            };

            TryDeployTank(_primaryEnemyTank, PickSurvivalWaveEnemy(_currentWave, 0), "enemy",
                spawnPts[0], -90f, isPlayerTank: false);

            for (int k = 1; k < enemyCount; k++)
            {
                SpawnTankFromPrefab(PickSurvivalWaveEnemy(_currentWave, k), "enemy", spawnPts[k % spawnPts.Length], -90f, isPlayerTank: false);
            }

            _hud.ShowWaveToast(UiText.Format("WAVE {0} — {1} HOSTILES!", _currentWave, enemyCount), 2.2f);
        }

        private TankEntity GetPlayerTank()
        {
            for (int i = 0; i < _tanks.Count; i++)
                if (_tanks[i] != null && _tanks[i].isPlayer) return _tanks[i];
            return null;
        }

        private void Update()
        {
            if (!_initialized) return;
            if (_multiplayer.Connection != null && _multiplayer.Connection.IsReturningToLobby) return;
            if (!_multiplayer.IsInBattle && _sceneLoader != null && _sceneLoader.IsLoading) return;
            float dt = Mathf.Min(0.04f, Time.deltaTime);
            var kb = Keyboard.current;
            HandleBriefingInput(kb);

            if (_windowbox != null)
                _windowbox.integerScale = integerWindowbox;

            if (_multiplayer.IsInBattle && _multiplayer.Transport != null)
            {
                if (!_multiplayer.Transport.IsHost)
                {
                    UpdateMultiplayerClient(dt);
                    return;
                }
                UpdateMultiplayerRoster();
                if (!MultiplayerBattleReady())
                {
                    BroadcastMultiplayerState();
                    UpdateMultiplayerWaiting(dt);
                    return;
                }
            }

            if (kb != null && kb.escapeKey.wasPressedThisFrame && _hud != null)
            {
                _hud.HandleEscapeKey();
            }

            bool menuOpen = _hud != null && _hud.IsMenuOpen;
            _audio?.UpdateBgm(active: true, bgmEnabled, bgmVolume, duckForMenu: menuOpen);

            if (menuOpen && !_multiplayer.IsInBattle)
            {
                StopLoopingAudio();
                PushHudUpdate(GetPlayerTank(), 0f);
                return;
            }

            if (_phase == GamePhase.Briefing)
            {
                RenderBattleFrame(GetPlayerTank(), 0f);
                return;
            }

            if (!_multiplayer.IsInBattle && kb != null && kb.rKey.wasPressedThisFrame)
            {
                ResetRound();
                return;
            }

            if (kb != null && kb.hKey.wasPressedThisFrame)
                _hud?.CycleHudDensity();

            if (_hitStop > 0f)
            {
                _hitStop -= Time.unscaledDeltaTime;
                _hud?.TickHitCameraOnly();
                return;
            }

            // --- SURVIVAL NEXT WAVE TIMER ---
            if (_waitingNextWave && _phase == GamePhase.Playing)
            {
                _nextWaveDelay -= Time.unscaledDeltaTime;
                if (_nextWaveDelay <= 0f)
                    SpawnNextSurvivalWave();
            }

            UpdateRoundAftermath();

            TankEntity player = GetPlayerTank();
            if (player != null && !menuOpen) UpdatePlayer(player, dt);
            if (_multiplayer.IsInBattle) ApplyMultiplayerControls(dt);

            TickTankCombat(dt);
            UpdateOfflineOpponents(dt);
            ResolveTankMovementCollisions();

            UpdateProjectiles(dt);
            if (_phase == GamePhase.Playing && UsesControlPoints)
                UpdateControlPoint(dt);
            RenderBattleFrame(player, dt);
            if (_multiplayer.IsInBattle) BroadcastMultiplayerState();
        }

        private void TickTankCombat(float dt)
        {
            if (_phase != GamePhase.Playing) return;

            TankEntity burnedOut = null;
            foreach (var t in _tanks)
            {
                if (t == null || !t.alive) continue;
                t.TickCombat(dt, autoService: aiEnabled && !t.isPlayer);
                if (!t.alive)
                {
                    ShowTankLoss(t);
                    burnedOut = t;
                }
            }
            if (burnedOut != null)
            {
                if (_multiplayer.IsInBattle || burnedOut.isPlayer || WillRoundEndAfterKill(burnedOut))
                    TriggerSyntheticDecisiveXRay(burnedOut, "FIRE");
                CheckTeamVictoryOrWave(burnedOut);
            }
        }

        private void UpdateRoundAftermath()
        {
            // --- CINEMATIC KILL AFTERMATH SEQUENCE ---
            if (_phase == GamePhase.RoundOver)
            {
                float udt = Mathf.Min(0.05f, Time.unscaledDeltaTime);
                _aftermathTimer += udt;

                if (!_multiplayer.IsInBattle && _aftermathTimer < 0.78f)
                {
                    Time.timeScale = 0.24f;
                }
                else if (!_multiplayer.IsInBattle && _aftermathTimer < 1.55f)
                {
                    float s = (_aftermathTimer - 0.78f) / 0.77f;
                    Time.timeScale = Mathf.Lerp(0.24f, 1.0f, s * s * (3f - 2f * s));
                }
                else if (!_multiplayer.IsInBattle)
                {
                    Time.timeScale = 1.0f;
                }

                _killZoomBoost = Mathf.Lerp(_killZoomBoost, 0.24f, Mathf.Clamp01(udt * 2.2f));
                // Gently deepen HD-2D Tilt-Shift Depth of Field during dramatic Kill-Cam aftermath
                float killBokeh = Mathf.Lerp(1.0f, 1.45f, Mathf.Clamp01(_aftermathTimer / 0.85f));
                _windowbox?.SetTiltShiftParams(killBokeh, 0.50f, 0.22f);

                if (!_resultBannerShown && _aftermathTimer >= 2.25f)
                {
                    _resultBannerShown = true;
                    if (_hud != null && !string.IsNullOrEmpty(_pendingWinner))
                        _hud.ShowResult(_multiplayer.IsInBattle
                            ? (_pendingWinner == "ally") == IsLocalAlpha
                            : _pendingWinner == "ally", _pendingSubtitle);
                }
            }
        }

        private void RenderBattleFrame(TankEntity player, float dt)
        {
            if (_controlMarker != null) _controlMarker.color = ControlPointColor();
            _shake = Mathf.Max(0f, _shake - Time.unscaledDeltaTime * 15f);
            UpdateCameraRoll(player, dt);
            TankEntity observer = BattleVisibility.FindObserver(_tanks, player);
            UpdateCameraAndAim(player, observer, dt);
            bool limitVisibility = _phase == GamePhase.Playing && battleMode != BattleMode.FiringRange;
            _visibility.Refresh(_tanks, _networkTanks, player, observer, CameraViewport, _cam.aspect, limitVisibility, _hasLineOfSight);
            foreach (var tank in _tanks)
            {
                if (tank == null || !tank.gameObject.activeInHierarchy) continue;

                bool visible = _visibility.IsVisible(tank);
                bool outline = ShowOnlineTankMarkers && visible && tank.alive && IsTeammate(tank);
                tank.hullStack.SetTeamOutline(outline);
                tank.turretStack.SetTeamOutline(outline);
                tank.hullStack.SetBattleVisibility(visible);
                tank.turretStack.SetBattleVisibility(visible);
                tank.SyncVisuals(squash, spacingPx,
                    onTurretBounce: visible ? OnTurretGroundBounce : null,
                    onTurretTrail: visible ? OnTurretAirTrail : null);

                if (visible)
                {
                    UpdateTankMovementEffects(tank, dt);
                }
                else
                {
                    tank.treadAccum = 0f;
                }
            }
            Vector2? observerPosition = observer != null ? observer.Position2D : null;
            foreach (var prop in _props)
                if (prop != null) prop.Tick(dt, squash, spacingPx, observerPosition);

            UpdateSmokeClouds(dt);
            UpdateParticles(dt);
            UpdateMechanicalAudio(player, dt);
            PushHudUpdate(player, dt);

            if (_groundDirty)
            {
                _groundDirty = false;
                _runtimeGroundTex.SetPixels32(_groundPixels);
                _runtimeGroundTex.Apply(false, false);
            }
        }

        private void PushHudUpdate(TankEntity player, float dt)
        {
            if (_hud == null) return;
            _radarSmokes.Clear();
            foreach (var sm in _smokeClouds)
                _radarSmokes.Add(new RadarSmokeInfo { pos = sm.pos, radius = sm.radius });

            _hud.UpdateHUD(
                player,
                battleMode == BattleMode.FiringRange ? _rangeKills : _winsAlly,
                battleMode == BattleMode.FiringRange ? 0 : _winsEnemy,
                _currentWave, battleMode,
                _props, _tanks, _radarSmokes,
                _cam, WorldW, WorldH, dt
            );
        }

        // ============================================================
        // Multi-Tank AI (Smoke-aware LOS, Flanking, Squad-safe)
        // ============================================================
        private bool WillRoundEndAfterKill(TankEntity victim)
        {
            if (battleMode == BattleMode.FiringRange) return false;
            int allyAlive = 0, enemyAlive = 0;
            foreach (var t in _tanks)
            {
                if (t == null || !t.alive || t == victim) continue;
                if (t.factionTeam == "ally") allyAlive++;
                else enemyAlive++;
            }
            TankEntity player = GetPlayerTank();
            bool playerDead = player == null || !player.alive || player == victim;
            if (battleMode == BattleMode.SurvivalWave)
                return playerDead;
            return allyAlive == 0 || enemyAlive == 0;
        }

        private void TriggerSyntheticDecisiveXRay(TankEntity victim, string roundLabel)
        {
            if (victim == null || _hud == null) return;
            bool frontEng = victim.vehicleDef != null && victim.vehicleDef.IsFrontEngine;
            Vector2 entry = new Vector2(0f, frontEng ? 0.55f : -0.55f);
            Vector2 approach = new Vector2(0f, frontEng ? -1f : 1f);
            var trace = new TankShotTrace(entry, approach, Vector2.zero);
            TankModule mod = victim.Loss == TankLoss.Ammo ? TankModule.Ammo : TankModule.Engine;
            var hit = new TankHit(
                ArmorHit.Penetrated,
                modules: mod,
                crewLost: 0,
                ignited: victim.Loss == TankLoss.Fire || victim.OnFire,
                struckModules: mod
            );
            EmitBattleEffect(new BattleEffect { Kind = BattleEffectKind.LossReplay, Hit = hit, Trace = trace,
                Loss = victim.Loss, EndsRound = WillRoundEndAfterKill(victim),
                ReplayReason = roundLabel switch { "FUEL" => 1, "BLAST" => 2, "HE" => 3, _ => 0 } }, victim);
        }

        private void CheckTeamVictoryOrWave(TankEntity justKilled)
        {
            if (_phase != GamePhase.Playing) return;

            if (battleMode == BattleMode.FiringRange)
            {
                if (justKilled != null && justKilled.factionTeam == "enemy")
                {
                    _rangeKills++;
                    for (int i = 0; i < _rangeTargets.Count; i++)
                    {
                        if (_rangeTargets[i].tank == justKilled)
                        {
                            _rangeTargets[i].respawnTimer = 4.5f;
                            break;
                        }
                    }
                }
                else if (justKilled != null && justKilled.isPlayer)
                {
                    // Auto-recover player if they blew up a fuel drum right beside themselves on the range
                    justKilled.CaptureSpawnAndReset(new Vector2(WorldW * 0.14f, WorldH * 0.50f), 90f);
                }
                return;
            }

            int allyAlive = 0, enemyAlive = 0;
            foreach (var t in _tanks)
            {
                if (t == null || !t.alive) continue;
                if (t.factionTeam == "ally") allyAlive++;
                else enemyAlive++;
            }

            TankEntity player = GetPlayerTank();

            // Survival Wave Mode: if player cleared all enemies while alive -> start next wave!
            if (battleMode == BattleMode.SurvivalWave && player != null && player.alive && enemyAlive == 0)
            {
                _winsAlly++;
                _waitingNextWave = true;
                _nextWaveDelay = 2.4f;
                _hud?.ShowWaveToast(UiText.Format("WAVE {0} CLEAR  FIELD SERVICE", _currentWave), 2.2f);
                NotifyVoiceWaveClear();
                return;
            }

            // Round ends if either team is eliminated (or player died in Survival!)
            bool roundFinished = allyAlive == 0 || enemyAlive == 0 || (battleMode == BattleMode.SurvivalWave && (player == null || !player.alive));
            if (!roundFinished) return;

            string winner = (battleMode == BattleMode.SurvivalWave && (player == null || !player.alive))
                ? "enemy"
                : (allyAlive > 0 ? "ally" : "enemy");

            string pName = SelectedPlayerVehicle != null ? SelectedPlayerVehicle.name.ToUpper() : "PLAYER";
            string eName = SelectedEnemyVehicle != null ? SelectedEnemyVehicle.name.ToUpper() : "HOSTILE";
            string subtitle = _multiplayer.IsInBattle ? "ONLINE BATTLE" : battleMode switch
            {
                BattleMode.SurvivalWave => UiText.Format("{0} SURVIVED TO WAVE {1}", pName, _currentWave),
                BattleMode.Battle5v5 => winner == "ally" ? "5v5 ALLIED VICTORY" : "5v5 ENEMY VICTORY",
                BattleMode.Squad2v2 => winner == "ally" ? "2v2 SQUAD VICTORY" : "2v2 HOSTILE VICTORY",
                _ => UiText.Format("{0} VICTORY", winner == "ally" ? pName : eName)
            };
            FinishRound(winner, subtitle, justKilled.Position2D);
        }

        private void FinishRound(string winner, string subtitle, Vector2 focus, bool wonByControl = false)
        {
            if (_phase != GamePhase.Playing) return;
            _phase = GamePhase.RoundOver;
            _aftermathTimer = 0f;
            _resultBannerShown = false;
            if (!_multiplayer.IsInBattle && !wonByControl) _hitStop = 0.09f;
            _shake = wonByControl ? 0f : 0.50f;
            _killFocusPos = focus;
            _pendingWinner = winner;
            _pendingSubtitle = subtitle;
            _wonByControl = wonByControl;
            if (winner == "ally") _winsAlly++; else _winsEnemy++;
            NotifyVoiceRoundFinished(winner);
        }

        private static Vector2 Rotate2D(Vector2 v, float rad)
        {
            float c = Mathf.Cos(rad);
            float s = Mathf.Sin(rad);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        private void OnDestroy()
        {
            CleanupMultiplayerBattle();
            ReleaseExteriorTerrain();
            ReleaseRuntimeGround();
            Time.timeScale = 1f;
        }
    }
}
