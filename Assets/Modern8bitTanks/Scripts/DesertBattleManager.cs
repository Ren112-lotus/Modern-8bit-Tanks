using Cysharp.Threading.Tasks;
using VContainer;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Modern8bitTanks
{
    public enum GamePhase
    {
        Title,
        Playing,
        RoundOver
    }

    /// <summary>
    /// Complete Desert Sprite Stacking Tank Battle Manager for Unity 6 (URP 17 + New Input System):
    /// - Live 360° Hangar Title Screen (Tank & Mode Select: 1v1 Duel / 2v2 Squad / Survival Wave)
    /// - Directional Armor (Front / Side / Rear Crit) + Smoke Grenade Launcher [Q/Shift] breaking AI LOS
    /// - Ballistic Turret Toss on kill / ammo cook-off + cinematic slow-motion kill cam
    /// - Procedural Engine (Abrams Gas Turbine / T-90 Diesel), Track Clatter, Turret Servo, & Reload Clank SFX
    /// - Real CC0 Cannon, Ricochet, Explosion, & MG SFX + Natural Desert Daylight & URP HDR Bloom
    /// </summary>
    public partial class DesertBattleManager : MonoBehaviour
    {
        public const int WorldWidthPx = 2048;
        public const int WorldHeightPx = 1360;
        public const int BorderPadPx = 24;
        public const float PPU = 16f;

        private float WorldW => WorldWidthPx / PPU;
        private float WorldH => WorldHeightPx / PPU;
        private float BorderPad => BorderPadPx / PPU;

        [Header("Scene & Prefab References (Pre-placed in DesertBattle.unity)")]
        public SpriteRenderer desertGroundRenderer;
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
        [Range(0.7f, 1.5f)] public float cameraZoom = 1.10f;

        [Header("Fixed RenderTexture Letterbox")]
        public int renderWidth = 480;
        public int renderHeight = 270;
        public bool integerWindowbox = false; // Default: 16:9 Letterbox
        public bool postProcessEnabled = true;

        private PixelWindowbox _windowbox;
        private BattlePostProcess _postProcess;
        private DesertAtmosphere _atmosphere;
        [SerializeField] private BattleHUD _hud;
        private Vector2 _lastValidMouseWorld;
        [SerializeField] private Material _emissiveFxMat;
        [SerializeField] private Material _groundHd2dMat;
        private MaterialPropertyBlock _fxMpb;
        private static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");
        [SerializeField] private Sprite _smokePuffSprite;

        // Audio clips (CC0 recordings + procedural mechanical SFX)
        private AudioClip _sfxCannonAbrams;
        private AudioClip _sfxCannonT90;
        private AudioClip _sfxRicochet;
        private AudioClip _sfxExplosion;
        private AudioClip _sfxMg;
        private AudioClip _sfxAbramsTurbine;
        private AudioClip _sfxT90Diesel;
        private AudioClip _sfxTrackClatter;
        private AudioClip _sfxTurretServo;
        private AudioClip _sfxReloadClank;
        private AudioClip _sfxSmokePop;
        private AudioClip _sfxMetalCrash;
        private AudioClip _sfxMissileLaunch;
        private AudioClip _sfxMissileHeavy;
        private AudioClip _sfxMissileMotor;
        private AudioClip _sfxBattleBgm;

        [SerializeField] private BattleAudioController _audio;

        [SerializeField] private Sprite _whitePxSprite;

        // Runtime mutable DesertGround
        private Texture2D _runtimeGroundTex;
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
        private readonly List<ProjectileData> _projectiles = new();
        private readonly List<ParticleData> _particles = new();
        private readonly List<SmokeCloud> _smokeClouds = new();
        private readonly List<RadarSmokeInfo> _radarSmokes = new();

        [SerializeField] private BattleSpawnAssets spawnAssets;
        [SerializeField] private Transform _fxRoot;
        private Camera _cam;
        private BattleSessionState _sessionState;
        private GameSceneLoader _sceneLoader;

        [Inject]
        private void Construct(Camera worldCamera, BattleSessionState sessionState, GameSceneLoader sceneLoader,
            PixelWindowbox windowbox, BattlePostProcess postProcess, DesertAtmosphere atmosphere)
        {
            _cam = worldCamera;
            _sessionState = sessionState;
            _sceneLoader = sceneLoader;
            _windowbox = windowbox;
            _postProcess = postProcess;
            _atmosphere = atmosphere;
        }
        private Vector2 _camPos;
        private float _camRollDeg;
        private Vector2 _camKickPos;
        private Vector2 _camKickVel;
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
        private Transform _reticleTf;
        [SerializeField] private SpriteRenderer _reticleSr;

        private class ProjectileData
        {
            public ushort networkId;
            public ProjectileKind kind;
            public AmmoType ammo;
            public string ownerFaction; // "ally" or "enemy"
            public TankEntity owner;
            public bool playerOwned;
            public Vector2 visualPos;
            public Vector2 groundPos;
            public Vector2 vel;
            public float speed;
            public float penetration;
            public int armorBouncesLeft;
            public float life;
            public float age;
            public GameObject go;
            public SpriteRenderer sr;

            public bool IsHeavy => kind == ProjectileKind.Shell || kind == ProjectileKind.ATGM;
            public bool IsPrimary => kind != ProjectileKind.MG;
            public bool IsGuided => AmmoInfo.IsGuided(ammo);
        }

        public VehicleDef[] AllVehicles => _allVehicles;
        public VehicleDef SelectedPlayerVehicle => GetVehicleDef(selectedPlayerVehicleId);
        public VehicleDef SelectedEnemyVehicle => GetVehicleDef(selectedEnemyVehicleId);

        private class ParticleData
        {
            public Vector2 pos;
            public Vector2 vel;
            public float gravity;
            public float life;
            public float maxLife;
            public float baseSize;
            public Color baseColor;
            public bool expandAndFade;
            public float endScaleMul;
            public float drag;
            public float zPos;
            public GameObject go;
            public SpriteRenderer sr;
        }

        private class SmokeCloud
        {
            public Vector2 pos;
            public Vector2 vel;
            public float radius;
            public float life;
            public float maxLife;
            public float puffTimer;
        }

        private void Awake()
        {
            _cam.orthographic = true;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.11f, 0.09f, 0.06f, 1f);

            // Crisp low-slung hull/turret height with clean vertical pixel preservation for small & large tanks alike!
            squash = 0.80f;
            spacingPx = 0.72f;
            cameraZoom = Mathf.Clamp(cameraZoom < 1.05f ? 1.10f : cameraZoom, 0.80f, 1.45f);

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
            if (MultiplayerSessionConnection.Online)
            {
                var room = MultiplayerSessionConnection.Active.Room;
                int localSlot = MultiplayerSessionConnection.LocalSlot;
                selectedPlayerVehicleId = room.GetVehicleId(localSlot);
                int firstOpponentSlot = localSlot < MultiplayerSessionConnection.MaxPlayers / 2
                    ? MultiplayerSessionConnection.MaxPlayers / 2 : 0;
                for (int slot = firstOpponentSlot; slot < firstOpponentSlot + MultiplayerSessionConnection.MaxPlayers / 2; slot++)
                {
                    var id = room.GetVehicleId(slot);
                    if (id == null) continue;
                    selectedEnemyVehicleId = id;
                    break;
                }
                battleMode = BattleMode.Duel1v1;
                battleMap = BattleMap.Desert;
                aiEnabled = false;
            }
            _winsAlly = _sessionState.WinsAlly;
            _winsEnemy = _sessionState.WinsEnemy;

            _windowbox.Initialize(_cam, renderWidth, renderHeight, integerWindowbox, useHd2d: postProcessEnabled);
            _lastValidMouseWorld = new Vector2(WorldW * 0.5f, WorldH * 0.5f);

            _postProcess.postProcessEnabled = postProcessEnabled;
            // Attach URP Post-Processing to PresentCamera so Bloom/Vignette/ACES run AFTER the 480x270 RT at full screen res!
            _postProcess.Initialize(_windowbox.PresentCamera);
            _postProcess.ApplyMapTheme(battleMap);

            LoadVehicleCatalog();
            LoadRuntimeResources();

            _audio.Initialize(_sfxTrackClatter, _sfxTurretServo, _sfxMissileMotor);
            _audio.StartBattleBgm(_sfxBattleBgm);

            _atmosphere.Initialize(WorldW, WorldH, _whitePxSprite, _emissiveFxMat);
            _atmosphere.ApplyMapTheme(battleMap);

            PrepareRuntimeGround();
            _props.AddRange(FindObjectsByType<DestructibleProp>(FindObjectsInactive.Include));
            _reticleTf = _reticleSr.transform;
            _reticleSr.sprite = _whitePxSprite;

            _hud.Initialize(this, _windowbox);

            // Dedicated Battle Scene: launch straight into combat against a random same-Tier rival!
            ResetRound();
            InitializeMultiplayerBattle();
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

        private void LoadRuntimeResources()
        {
            _sfxCannonAbrams = Resources.Load<AudioClip>("SFX/cannon");
            _sfxCannonT90 = Resources.Load<AudioClip>("SFX/cannon2");
            _sfxRicochet = Resources.Load<AudioClip>("SFX/ricochet");
            _sfxExplosion = Resources.Load<AudioClip>("SFX/explosion");
            _sfxMg = Resources.Load<AudioClip>("SFX/mg");

            _sfxAbramsTurbine = Resources.Load<AudioClip>("SFX/engine_abrams");
            _sfxT90Diesel = Resources.Load<AudioClip>("SFX/engine_t90");
            _sfxTrackClatter = Resources.Load<AudioClip>("SFX/tracks");
            _sfxTurretServo = Resources.Load<AudioClip>("SFX/turret_servo");
            _sfxReloadClank = Resources.Load<AudioClip>("SFX/reload_clank");
            _sfxSmokePop = Resources.Load<AudioClip>("SFX/smoke_pop");
            _sfxMetalCrash = Resources.Load<AudioClip>("SFX/metal_crash");
            _sfxMissileLaunch = Resources.Load<AudioClip>("SFX/missile_launch") ?? _sfxSmokePop;
            _sfxMissileHeavy = Resources.Load<AudioClip>("SFX/missile_heavy") ?? _sfxMissileLaunch;
            _sfxMissileMotor = Resources.Load<AudioClip>("SFX/missile_motor");
            _sfxBattleBgm = Resources.Load<AudioClip>("SFX/battle_bgm");

            _fxMpb = new MaterialPropertyBlock();
        }

        public void TogglePostProcess()
        {
            postProcessEnabled = !postProcessEnabled;
            if (_postProcess != null) _postProcess.SetEnabled(postProcessEnabled);
            if (_windowbox != null) _windowbox.SetHd2dMode(postProcessEnabled);
        }

        private void PrepareRuntimeGround()
        {

            _runtimeGroundTex = new Texture2D(WorldWidthPx, WorldHeightPx, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            _cleanGroundPixels = GenerateMapPixels(battleMap, WorldWidthPx, WorldHeightPx);
            _groundPixels = new Color32[_cleanGroundPixels.Length];

            desertGroundRenderer.enabled = true;
            desertGroundRenderer.transform.position = new Vector3(WorldW * 0.5f, WorldH * 0.5f, 5f);
            desertGroundRenderer.sortingOrder = -30000;
            if (_groundHd2dMat != null) desertGroundRenderer.sharedMaterial = _groundHd2dMat;
            desertGroundRenderer.sprite = Sprite.Create(
                _runtimeGroundTex,
                new Rect(0, 0, WorldWidthPx, WorldHeightPx),
                new Vector2(0.5f, 0.5f),
                PPU
            );
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
            if (!MultiplayerSessionConnection.Online)
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
            if (!MultiplayerSessionConnection.Online) return;
            SyncSettingsToSession();
            MultiplayerSessionConnection.Active.ReturnToRoomAsync().Forget();
        }

        public void ReturnToHangar()
        {
            if (MultiplayerSessionConnection.Online)
            {
                SyncSettingsToSession();
                MultiplayerSessionConnection.Active.LeaveAsync().Forget();
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
            if (MultiplayerSessionConnection.Online && _networkTanks != null) return;
            Time.timeScale = 1f;
            _phase = GamePhase.Playing;
            _currentWave = 1;
            _waitingNextWave = false;
            _aftermathTimer = 0f;
            _resultBannerShown = false;
            _pendingWinner = null;
            _killZoomBoost = 0f;
            _hitStop = 0f;
            _shake = 0f;
            _camKickPos = Vector2.zero;
            _camKickVel = Vector2.zero;
            _windowbox?.SetTiltShiftParams(1.0f, 0.50f, 0.25f);

            // Pick a fresh RANDOM same-Tier performance-matched rival every battle!
            VehicleDef playerDef = SelectedPlayerVehicle;
            VehicleDef randRival = MultiplayerSessionConnection.Online ? null : BattleSessionState.PickRandomSameTierRival(_allVehicles, playerDef, excludeId: selectedEnemyVehicleId);
            if (randRival != null)
                selectedEnemyVehicleId = randRival.id;

            ClearDynamicFXAndExtraTanks();
            battleMap = MultiplayerSessionConnection.Online ? BattleMap.Desert : _sessionState.SelectedBattleMap;
            _cleanGroundPixels = GenerateMapPixels(battleMap, WorldWidthPx, WorldHeightPx);
            RestoreCleanGround();
            _postProcess?.ApplyMapTheme(battleMap);
            _atmosphere?.ApplyMapTheme(battleMap);
            ClearAndSpawnMapProps(battleMap);

            SpawnModeTanks();

            TankEntity player = GetPlayerTank();
            if (player != null)
                _camPos = player.Position2D;
            _camRollDeg = 0f;
            SpriteStackCamera.SetRoll(0f);
            if (_cam != null) _cam.transform.rotation = Quaternion.identity;
            StartPlayerEngineAudio(player);

            if (_hud != null)
            {
                _hud.ClearPopups();
                _hud.ClearHitCamera();
                _hud.HideResult();
                VehicleDef eDef = SelectedEnemyVehicle;
                string eLabel = eDef != null ? $"{eDef.name.ToUpper()} ({eDef.TierRoman})" : "HOSTILE";
                string pTier = playerDef != null ? playerDef.TierRoman : "IX";
                string mapTag = GetMapDisplayName(battleMap);
                string startMsg = battleMode switch
                {
                    BattleMode.FiringRange => $"{mapTag}  FIRING RANGE  [R] RESET",
                    BattleMode.Battle5v5 => $"{mapTag}  5v5 (TIER {pTier})",
                    BattleMode.Squad2v2 => $"{mapTag}  2v2 VS {eLabel}",
                    BattleMode.SurvivalWave => $"{mapTag}  WAVE 1",
                    _ => $"{mapTag}  VS {eLabel}"
                };
                _hud.ShowWaveToast(startMsg, 2.2f);
            }
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

        private void SpawnModeTanks()
        {
            _rangeTargets.Clear();
            _tanks.Clear();
            if (MultiplayerSessionConnection.Online)
            {
                SpawnMultiplayerTanks();
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
                new Vector2(westX, WorldH * 0.50f), 90f, isPlayerTank: true);
            TryDeployTank(_primaryEnemyTank, enemyDef, "enemy",
                new Vector2(eastX, WorldH * 0.50f), -90f, isPlayerTank: false);

            if (battleMode == BattleMode.Squad2v2)
            {
                VehicleDef allyWing = BattleSessionState.PickRandomSameTierAlly(_allVehicles, playerDef);
                VehicleDef enemyWing = BattleSessionState.PickRandomSameTierRival(_allVehicles, playerDef, excludeId: enemyDef != null ? enemyDef.id : null);
                SpawnTankFromPrefab(allyWing, "ally", new Vector2(westX + 1.2f, WorldH * 0.28f), 90f, isPlayerTank: false);
                SpawnTankFromPrefab(enemyWing, "enemy", new Vector2(eastX - 1.2f, WorldH * 0.72f), -90f, isPlayerTank: false);
            }
            else if (battleMode == BattleMode.Battle5v5)
            {
                // 5v5 Armored Company Clash across North / Mid / Center / South-Mid / Oasis lanes!
                Vector2[] allySpawns =
                {
                    new Vector2(westX + 1.5f, WorldH * 0.80f),
                    new Vector2(westX + 3.2f, WorldH * 0.65f),
                    new Vector2(westX + 3.2f, WorldH * 0.35f),
                    new Vector2(westX + 1.5f, WorldH * 0.18f),
                };
                Vector2[] enemySpawns =
                {
                    new Vector2(eastX - 1.5f, WorldH * 0.80f),
                    new Vector2(eastX - 3.2f, WorldH * 0.65f),
                    new Vector2(eastX - 3.2f, WorldH * 0.35f),
                    new Vector2(eastX - 1.5f, WorldH * 0.18f),
                };

                string prevEnemyId = enemyDef != null ? enemyDef.id : null;
                for (int k = 0; k < 4; k++)
                {
                    VehicleDef allyMate = BattleSessionState.PickRandomSameTierAlly(_allVehicles, playerDef);
                    SpawnTankFromPrefab(allyMate, "ally", allySpawns[k], 90f, isPlayerTank: false);

                    VehicleDef foe = BattleSessionState.PickRandomSameTierRival(_allVehicles, playerDef, excludeId: prevEnemyId);
                    if (foe != null) prevEnemyId = foe.id;
                    SpawnTankFromPrefab(foe, "enemy", enemySpawns[k], -90f, isPlayerTank: false);
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

            _hud.ShowWaveToast($"WAVE {_currentWave} — {enemyCount} HOSTILES!", 2.2f);
        }

        private TankEntity GetPlayerTank()
        {
            for (int i = 0; i < _tanks.Count; i++)
                if (_tanks[i] != null && _tanks[i].isPlayer) return _tanks[i];
            return null;
        }

        // ============================================================
        // Continuous Mechanical Tank Audio (DRY Cabin Loops + Spatialized One-Shots)
        // ============================================================
        private Vector2 GetAudioListenerPos()
        {
            TankEntity player = GetPlayerTank();
            if (player != null && player.alive && _phase == GamePhase.Playing)
                return player.Position2D;
            return _camPos;
        }

        private void StartPlayerEngineAudio(TankEntity player)
        {
            if (player == null || _audio == null) return;
            bool isTurbine = player.vehicleDef != null
                ? player.vehicleDef.engine == "Turbine"
                : player.tankType == "abrams";
            _audio.StartPlayerLoops(isTurbine ? _sfxAbramsTurbine : _sfxT90Diesel);
        }

        private void StopLoopingAudio()
        {
            if (_audio != null) _audio.StopLoopsImmediate();
        }

        private void UpdateMechanicalAudio(TankEntity player, float dt)
        {
            if (_audio == null) return;
            bool active = sfxEnabled && sfxVolume > 0.001f && _phase == GamePhase.Playing && player != null && player.alive && (_hud == null || !_hud.IsMenuOpen);
            if (!active)
            {
                _audio.UpdatePlayerLoops(false, 0f, 0f, dt, sfxVolume);
                return;
            }

            float speed01 = Mathf.Clamp01(Mathf.Abs(player.moveSpeed) / (64f / PPU));
            float moving01 = Mathf.Max(speed01, Mathf.Abs(player.HullTurnInput) * 0.7f);
            float turn01 = Mathf.Clamp01(player.TurretTurnRateDeg / 220f);
            _audio.UpdatePlayerLoops(true, moving01, turn01, dt, sfxVolume);

            // Crisp metallic breech-lock CLANK inside the turret for main cannons / ATGMs (DRY, centered!)
            if (player.JustFinishedReloading && player.reloadDuration >= 0.65f)
            {
                PlaySFXAt(_sfxReloadClank, player.Position2D, SoundBus.Dry, 0.65f, centerPan: true);
            }
        }

        // ============================================================
        // Main Update Loop
        // ============================================================
        private void Update()
        {
            if (MultiplayerSessionConnection.Active != null && MultiplayerSessionConnection.Active.IsReturningToLobby) return;
            float dt = Mathf.Min(0.04f, Time.deltaTime);
            var kb = Keyboard.current;

            if (_windowbox != null)
                _windowbox.integerScale = integerWindowbox;

            if (MultiplayerSessionConnection.Online && MultiplayerMatchTransport.Instance != null)
            {
                if (!MultiplayerMatchTransport.Instance.IsHost)
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

            if (menuOpen && !MultiplayerSessionConnection.Online)
            {
                StopLoopingAudio();
                PushHudUpdate(GetPlayerTank(), 0f);
                return;
            }

            if (!MultiplayerSessionConnection.Online && kb != null && kb.rKey.wasPressedThisFrame)
            {
                ResetRound();
                return;
            }

            if (kb != null && kb.hKey.wasPressedThisFrame)
                _hud?.CycleHudDensity();

            // Mouse wheel camera zoom for wide top-down battlefield sightlines
            var mouse = Mouse.current;
            if (mouse != null && _phase == GamePhase.Playing)
            {
                float scrollY = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scrollY) > 0.01f)
                    cameraZoom = Mathf.Clamp(cameraZoom + Mathf.Sign(scrollY) * 0.08f, 0.76f, 1.48f);
            }

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
            if (MultiplayerSessionConnection.Online) ApplyMultiplayerControls(dt);

            if (_phase == GamePhase.Playing)
            {
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
                    if (MultiplayerSessionConnection.Online || burnedOut.isPlayer || WillRoundEndAfterKill(burnedOut))
                        TriggerSyntheticDecisiveXRay(burnedOut, "FIRE");
                    CheckTeamVictoryOrWave(burnedOut);
                }
            }

            // Multi-tank AI (or non-hostile Firing Range targets & auto-respawn!)
            if (MultiplayerSessionConnection.Online) { }
            else if (battleMode == BattleMode.FiringRange)
            {
                UpdateFiringRangeTargets(dt);
            }
            else
            {
                foreach (var t in _tanks)
                {
                    if (t == null || !t.alive || t.isPlayer) continue;
                    TankEntity nearestHostile = FindNearestOpposingTank(t);
                    if (nearestHostile != null) UpdateAI(t, nearestHostile, dt);
                }
            }

            // Tank physics
            foreach (var t in _tanks)
            {
                if (t == null || !t.gameObject.activeSelf) continue;
                ResolveTankCollisions(t);
            }

            // Tank-vs-tank separation
            for (int i = 0; i < _tanks.Count; i++)
            {
                for (int j = i + 1; j < _tanks.Count; j++)
                {
                    if (_tanks[i] == null || _tanks[j] == null || !_tanks[i].gameObject.activeSelf || !_tanks[j].gameObject.activeSelf) continue;
                    Vector2 diff = _tanks[j].Position2D - _tanks[i].Position2D;
                    float dist = diff.magnitude;
                    float minSep = 24f / PPU;
                    if (dist > 0.001f && dist < minSep)
                    {
                        Vector2 push = (diff / dist) * ((minSep - dist) * 0.5f);
                        _tanks[i].Position2D -= push;
                        _tanks[j].Position2D += push;
                    }
                }
            }

            UpdateProjectiles(dt);
            RenderBattleFrame(player, dt);
            if (MultiplayerSessionConnection.Online) BroadcastMultiplayerState();
        }

        private void UpdateRoundAftermath()
        {
            // --- CINEMATIC KILL AFTERMATH SEQUENCE ---
            if (_phase == GamePhase.RoundOver)
            {
                float udt = Mathf.Min(0.05f, Time.unscaledDeltaTime);
                _aftermathTimer += udt;

                if (!MultiplayerSessionConnection.Online && _aftermathTimer < 0.78f)
                {
                    Time.timeScale = 0.24f;
                }
                else if (!MultiplayerSessionConnection.Online && _aftermathTimer < 1.55f)
                {
                    float s = (_aftermathTimer - 0.78f) / 0.77f;
                    Time.timeScale = Mathf.Lerp(0.24f, 1.0f, s * s * (3f - 2f * s));
                }
                else if (!MultiplayerSessionConnection.Online)
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
                        _hud.ShowResult(MultiplayerSessionConnection.Online
                            ? (_pendingWinner == "ally") == (MultiplayerSessionConnection.LocalSlot < 4)
                            : _pendingWinner == "ally", _pendingSubtitle);
                }
            }
        }

        private void RenderBattleFrame(TankEntity player, float dt)
        {
            _shake = Mathf.Max(0f, _shake - Time.unscaledDeltaTime * 15f);
            UpdateCameraRoll(player, dt);
            foreach (var tank in _tanks)
            {
                if (tank == null || !tank.gameObject.activeSelf) continue;
                tank.SyncVisuals(squash, spacingPx, onTurretBounce: OnTurretGroundBounce, onTurretTrail: OnTurretAirTrail);
                UpdateTankMovementEffects(tank, dt);
            }
            foreach (var prop in _props)
                if (prop != null) prop.Tick(dt, squash, spacingPx);

            UpdateSmokeClouds(dt);
            UpdateParticles(dt);
            UpdateCameraAndAim(player, dt);
            UpdateMechanicalAudio(player, dt);
            PushHudUpdate(player, dt);

            if (_groundDirty)
            {
                _groundDirty = false;
                _runtimeGroundTex.SetPixels32(_groundPixels);
                _runtimeGroundTex.Apply(false, false);
            }
        }

        private void OnTurretGroundBounce(Vector2 worldPos)
        {
            PlaySFXAt(_sfxMetalCrash, worldPos, SoundBus.Reverb, 0.78f);
            _shake = Mathf.Max(_shake, 0.16f);
            StampScorchMark(worldPos, 4, 0.25f);
            for (int i = 0; i < 8; i++)
            {
                float ang = Random.Range(0f, Mathf.PI * 2f);
                SpawnParticle(
                    worldPos,
                    new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Random.Range(0.8f, 2.0f),
                    -1.2f,
                    Random.Range(0.25f, 0.5f),
                    0.18f,
                    new Color(0.84f, 0.72f, 0.48f, 0.85f),
                    1.0f
                );
            }
        }

        private void OnTurretAirTrail(Vector2 worldPos)
        {
            bool ember = Random.value < 0.45f;
            SpawnParticle(
                worldPos,
                new Vector2(Random.Range(-0.4f, 0.4f), Random.Range(0.2f, 0.7f)),
                -0.6f,
                Random.Range(0.25f, 0.50f),
                ember ? 0.12f : 0.20f,
                ember ? new Color(1f, 0.62f, 0.22f) : new Color(0.24f, 0.22f, 0.20f, 0.85f),
                ember ? 2.6f : 1.0f
            );
        }

        private TankEntity FindNearestOpposingTank(TankEntity self)
        {
            TankEntity best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < _tanks.Count; i++)
            {
                var other = _tanks[i];
                if (other == null || !other.alive || other.factionTeam == self.factionTeam) continue;
                float d = Vector2.Distance(self.Position2D, other.Position2D);
                if (d < bestDist) { bestDist = d; best = other; }
            }
            return best;
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
        // Player Controls + Smoke Launcher [Q / Shift]
        // ============================================================
        private Vector2 GetMouseWorld2D()
        {
            var mouse = Mouse.current;
            if (mouse == null || _cam == null || _windowbox == null) return _lastValidMouseWorld;

            Vector2 screen = mouse.position.ReadValue();
            if (!_windowbox.TryScreenToViewport(screen, out Vector2 uv))
                return _lastValidMouseWorld;

            float depth = Mathf.Abs(_cam.transform.position.z);
            Vector3 w = _cam.ViewportToWorldPoint(new Vector3(uv.x, uv.y, depth));
            if (float.IsFinite(w.x) && float.IsFinite(w.y))
                _lastValidMouseWorld = new Vector2(w.x, w.y);
            return _lastValidMouseWorld;
        }

        private void UpdatePlayer(TankEntity t, float dt)
        {
            if (!t.alive || _phase != GamePhase.Playing) return;
            if (!TryReadTankControl(out var input)) return;
            ApplyTankControl(t, dt, input);
        }

        private bool TryReadTankControl(out TankControlInput input)
        {
            input = default;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null) return false;

            bool up = kb.wKey.isPressed || kb.upArrowKey.isPressed;
            bool down = kb.sKey.isPressed || kb.downArrowKey.isPressed;
            bool left = kb.aKey.isPressed || kb.leftArrowKey.isPressed;
            bool right = kb.dKey.isPressed || kb.rightArrowKey.isPressed;
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            input = new TankControlInput
            {
                Up = up, Down = down, Left = left, Right = right,
                Repair = kb.fKey.wasPressedThisFrame,
                AimWorld = GetMouseWorld2D(),
                FirePrimary = (!overUI && mouse != null && mouse.leftButton.isPressed) || kb.spaceKey.isPressed,
                FireSecondary = !overUI && mouse != null && mouse.rightButton.isPressed,
                Smoke = kb.qKey.wasPressedThisFrame || kb.leftShiftKey.wasPressedThisFrame,
                AmmoSlot = kb.digit1Key.wasPressedThisFrame ? 0 : kb.digit2Key.wasPressedThisFrame ? 1 : kb.digit3Key.wasPressedThisFrame ? 2 : -1,
                Arcade = arcadeWASD
            };
            return true;
        }

        private struct TankControlInput
        {
            public bool Up, Down, Left, Right, Repair, FirePrimary, FireSecondary, Smoke, Arcade;
            public int AmmoSlot;
            public Vector2 AimWorld;
        }

        private void ApplyTankControl(TankEntity t, float dt, TankControlInput input)
        {
            if (!t.alive || _phase != GamePhase.Playing) return;
            bool driving = input.Up || input.Down || input.Left || input.Right;
            if (input.Repair || (t.Repairing && driving)) t.RequestService();

            ApplyTankMotion(t, dt, input);

            if (input.FirePrimary) FirePrimaryWeapon(t);
            if (input.FireSecondary) FireSecondaryWeapon(t);
            if (input.Smoke) DeploySmokeScreen(t);

            // War Thunder [1] / [2] / [3] Ammunition Switching!
            int wantAmmo = input.AmmoSlot;
            if (wantAmmo >= 0 && wantAmmo < t.AmmoLoadout.Length && wantAmmo != t.selectedAmmoSlot)
            {
                t.selectedAmmoSlot = wantAmmo;
                if (t.isPlayer) ShowAmmoSelection(t);
            }
        }

        private void ShowAmmoSelection(TankEntity t)
        {
            AmmoType chosen = t.CurrentAmmo;
            PlaySFXAt(_sfxReloadClank, t.Position2D, SoundBus.Dry, 0.55f, centerPan: true);
            string hint = AmmoInfo.IsGuided(chosen) ? " (MOUSE GUIDED)"
                : chosen == AmmoType.HE ? " (OVERPRESSURE)"
                : chosen == AmmoType.HEATFS ? " (SHAPED CHARGE)"
                : chosen == AmmoType.APHE ? " (INTERNAL BLAST)" : "";
            _hud?.ShowWaveToast($"[{t.selectedAmmoSlot + 1}] {AmmoInfo.FullLabel(chosen)}{hint}", 1.45f);
        }

        private void ApplyTankMotion(TankEntity t, float dt, TankControlInput input)
        {
            float rawSpd = t.vehicleDef != null ? t.vehicleDef.speed : 64f;
            float maxSpeed = rawSpd / PPU * t.DriveFactor;
            float turnDegPerSec = t.hullTurnSpeed * t.TurnFactor;
            float turretDegPerSec = t.turretTurnSpeed * t.TraverseFactor;
            float initialHullAngle = t.hullAngleDeg;
            t.HullTurnInput = 0f;

            if (input.Arcade)
            {
                Vector2 inp = Vector2.zero;
                if (input.Left) inp.x -= 1f;
                if (input.Right) inp.x += 1f;
                if (input.Up) inp.y += 1f;
                if (input.Down) inp.y -= 1f;

                if (inp.sqrMagnitude > 0.01f)
                {
                    inp.Normalize();
                    // Align WASD screen input with the rotated camera basis (W always moves UP on screen!)
                    Vector2 worldInp = (SpriteStackCamera.CamRight * inp.x + SpriteStackCamera.CamUp * inp.y).normalized;
                    float targetHull = TankEntity.TargetDeltaToSliceAngle(worldInp, squash);
                    t.hullAngleDeg = Mathf.MoveTowardsAngle(t.hullAngleDeg, targetHull, turnDegPerSec * 1.45f * dt);
                    t.Position2D += worldInp * (maxSpeed * dt);
                    t.moveSpeed = maxSpeed;
                    t.treadAccum += maxSpeed * dt;
                }
                else
                {
                    t.moveSpeed = 0f;
                }
            }
            else
            {
                float throttle = 0f;
                float steer = 0f;
                if (input.Up) throttle += 1f;
                if (input.Down) throttle -= 0.72f;
                if (input.Left) steer += 1f;
                if (input.Right) steer -= 1f;

                t.HullTurnInput = steer * t.TurnFactor;
                if (Mathf.Abs(steer) > 0.01f)
                {
                    t.hullAngleDeg = Mathf.Repeat(t.hullAngleDeg + steer * turnDegPerSec * dt, 360f);
                    t.treadAccum += Mathf.Abs(steer * turnDegPerSec * dt) * 0.0085f;
                }

                float targetSpd = throttle * maxSpeed;
                t.moveSpeed = Mathf.Lerp(t.moveSpeed, targetSpd, Mathf.Clamp01(dt * 8f));
                if (Mathf.Abs(t.moveSpeed) > 0.08f)
                {
                    Vector2 dir = TankEntity.SliceAngleToDir(t.hullAngleDeg);
                    t.Position2D += SpriteStackCamera.Squash(dir * (t.moveSpeed * dt), squash);
                    t.treadAccum += Mathf.Abs(t.moveSpeed) * dt;
                }
            }

            Vector2 mouseWorld = input.AimWorld;
            Vector2 piv = t.GetTurretPivotWorld(squash);
            float desiredTurret = TankEntity.TargetDeltaToSliceAngle(mouseWorld - piv, squash);
            if (t.HasDisabled(TankModule.Traverse))
                t.turretAngleDeg += Mathf.DeltaAngle(initialHullAngle, t.hullAngleDeg);
            else
                t.turretAngleDeg = Mathf.MoveTowardsAngle(t.turretAngleDeg,
                    LimitGunTraverse(t, desiredTurret), turretDegPerSec * dt);
            t.turretAngleDeg = LimitGunTraverse(t, t.turretAngleDeg);

        }

        private void DeploySmokeScreen(TankEntity t)
        {
            if (!t.alive || t.smokeCooldown > 0f || _phase != GamePhase.Playing) return;
            if (MultiplayerSessionConnection.Online)
                MultiplayerMatchTransport.Instance?.BroadcastSmoke(System.Array.IndexOf(_networkTanks, t));
            t.smokeCooldown = t.smokeDuration;

            Vector2 td = TankEntity.SliceAngleToDir(t.turretAngleDeg);
            Vector2 fwd = SpriteStackCamera.Squash(td, squash).normalized;
            Vector2 piv = t.GetTurretPivotWorld(squash);

            // Launch 3 fan-spread smoke canisters ahead of the turret!
            float[] angles = { -0.52f, 0f, 0.52f };
            foreach (float a in angles)
            {
                Vector2 dir = Rotate2D(fwd, a);
                Vector2 landPos = piv + dir * 2.4f;
                _smokeClouds.Add(new SmokeCloud
                {
                    pos = landPos,
                    vel = dir * 0.45f,
                    radius = 2.0f,
                    life = 6.5f,
                    maxLife = 6.5f,
                    puffTimer = 0f
                });
            }

            PlaySFXAt(_sfxSmokePop, t.Position2D, SoundBus.Dry, 0.68f);
            if (_hud != null) _hud.SpawnPopup(t.Position2D + Vector2.up * 1.5f, "SMOKE DEPLOYED!", new Color(0.72f, 0.90f, 1f));
        }

        private void UpdateSmokeClouds(float dt)
        {
            for (int i = _smokeClouds.Count - 1; i >= 0; i--)
            {
                var sm = _smokeClouds[i];
                sm.life -= dt;
                if (sm.life <= 0f)
                {
                    _smokeClouds.RemoveAt(i);
                    continue;
                }
                sm.pos += sm.vel * dt;
                sm.vel *= 0.94f;
                sm.puffTimer -= dt;
                if (sm.puffTimer <= 0f)
                {
                    sm.puffTimer = 0.14f;
                    float fade = Mathf.Clamp01(sm.life / 1.5f);
                    for (int k = 0; k < 2; k++)
                    {
                        Vector2 off = Random.insideUnitCircle * (sm.radius * 0.65f);
                        float shade = Random.Range(0.76f, 0.92f);
                        SpawnParticle(
                            sm.pos + off + Vector2.up * 0.45f,
                            new Vector2(Random.Range(-0.25f, 0.45f), Random.Range(0.15f, 0.45f)),
                            0.1f,
                            Random.Range(0.65f, 1.15f),
                            Random.Range(0.95f, 1.55f),
                            new Color(shade, shade * 0.98f, shade * 0.94f, 0.62f * fade),
                            1.0f,
                            _smokePuffSprite
                        );
                    }
                }
            }
        }

        // ============================================================
        // Multi-Tank AI (Smoke-aware LOS, Flanking, Squad-safe)
        // ============================================================
        private static float LimitGunTraverse(TankEntity tank, float angle)
        {
            int limit = tank.vehicleDef?.fixedGunTraverse ?? 0;
            return limit > 0 ? tank.hullAngleDeg + Mathf.Clamp(Mathf.DeltaAngle(tank.hullAngleDeg, angle), -limit, limit)
                : angle;
        }

        private void UpdateAI(TankEntity t, TankEntity target, float dt)
        {
            if (!t.alive || !target.alive || !aiEnabled || _phase != GamePhase.Playing) return;

            Vector2 piv = t.GetTurretPivotWorld(squash);
            Vector2 delta = target.Position2D - piv;
            float dist = delta.magnitude;

            bool losClear = HasLineOfSight(t.Position2D, target.Position2D);

            // Only lock turret tightly if enemy isn't hidden behind smoke/cover
            float aiTurretDegPerSec = t.turretTurnSpeed * t.TraverseFactor;
            if (losClear)
            {
                float desiredTurret = TankEntity.TargetDeltaToSliceAngle(delta, squash);
                t.turretAngleDeg = Mathf.MoveTowardsAngle(t.turretAngleDeg, desiredTurret, aiTurretDegPerSec * 0.72f * dt);
            }
            else
            {
                // Slowly sweep turret while flanking around cover/smoke
                t.turretAngleDeg = Mathf.MoveTowardsAngle(t.turretAngleDeg, t.hullAngleDeg, aiTurretDegPerSec * 0.40f * dt);
            }

            t.aiStrafeTimer -= dt;
            if (t.aiStrafeTimer <= 0f)
            {
                t.aiStrafeTimer = Random.Range(1.8f, 3.8f);
                t.aiStrafeDir = Random.value < 0.5f ? 1f : -1f;
            }

            Vector2 u = delta / Mathf.Max(0.01f, dist);
            Vector2 move = Vector2.zero;
            if (dist > 19.5f) move += u;
            else if (dist < 8.0f) move -= u;
            move += new Vector2(-u.y, u.x) * (t.aiStrafeDir * 0.95f);

            foreach (var prop in _props)
            {
                if (prop == null || !prop.alive) continue;
                Vector2 bDiff = t.Position2D - (Vector2)prop.transform.position;
                float bd = bDiff.magnitude;
                if (bd < prop.approxRadiusUnits + 2.0f && bd > 0.01f)
                    move += (bDiff / bd) * 2.4f;
            }

            // Avoid crowding allied tanks in 2v2 / Wave mode
            foreach (var other in _tanks)
            {
                if (other == null || other == t || !other.alive) continue;
                Vector2 oDiff = t.Position2D - other.Position2D;
                float od = oDiff.magnitude;
                if (od < 3.6f && od > 0.01f)
                    move += (oDiff / od) * 1.6f;
            }

            if (t.Position2D.x < BorderPad + 2.8f) move.x += 1.5f;
            if (t.Position2D.x > WorldW - BorderPad - 2.8f) move.x -= 1.5f;
            if (t.Position2D.y < BorderPad + 2.8f) move.y += 1.5f;
            if (t.Position2D.y > WorldH - BorderPad - 2.8f) move.y -= 1.5f;

            t.moveSpeed = 0f;
            if (move.sqrMagnitude > 0.02f)
            {
                bool aimWithHull = t.vehicleDef?.fixedGunTraverse is > 0 && losClear && dist < 38f;
                float targetHull = TankEntity.TargetDeltaToSliceAngle(aimWithHull ? delta : move.normalized, squash);
                float initialHullAngle = t.hullAngleDeg;
                float aiHullDegPerSec = t.hullTurnSpeed * 0.85f * t.TurnFactor;
                t.hullAngleDeg = Mathf.MoveTowardsAngle(t.hullAngleDeg, targetHull, aiHullDegPerSec * dt);
                float turnedDeg = Mathf.Abs(Mathf.DeltaAngle(initialHullAngle, t.hullAngleDeg));
                if (t.HasDisabled(TankModule.Traverse))
                    t.turretAngleDeg += Mathf.DeltaAngle(initialHullAngle, t.hullAngleDeg);
                t.moveSpeed = ((t.vehicleDef != null ? t.vehicleDef.speed : 60f) * 0.68f) / PPU * t.DriveFactor;
                if (aimWithHull && dist >= 8f && dist <= 19.5f)
                    t.moveSpeed = 0f;
                Vector2 dir = TankEntity.SliceAngleToDir(t.hullAngleDeg) * (aimWithHull && dist < 8f ? -1f : 1f);
                t.Position2D += SpriteStackCamera.Squash(dir * (t.moveSpeed * dt), squash);
                t.treadAccum += (Mathf.Abs(t.moveSpeed) * dt) + (turnedDeg * 0.0085f);
            }

            t.turretAngleDeg = LimitGunTraverse(t, t.turretAngleDeg);
            if (losClear && dist < 38f)
            {
                float desiredTurret = TankEntity.TargetDeltaToSliceAngle(delta, squash);
                float aimErr = Mathf.Abs(Mathf.DeltaAngle(t.turretAngleDeg, desiredTurret));
                if (aimErr < 8f)
                {
                    bool isAuto = t.vehicleDef != null && (t.vehicleDef.weapon == "AutocannonATGM" || t.vehicleDef.weapon == "TwinAutocannonATGM");
                    t.aiFireDelay -= dt;
                    if (t.aiFireDelay <= 0f)
                    {
                        FirePrimaryWeapon(t);
                        t.aiFireDelay = isAuto ? Random.Range(0.14f, 0.26f) : Random.Range(0.32f, 0.68f);
                    }
                    else if (t.vehicleDef != null && t.vehicleDef.HasSecondaryAtgm && t.atgmCooldown <= 0f && dist > 6.5f && Random.value < 0.08f)
                    {
                        FireSecondaryWeapon(t);
                    }
                    else if (!isAuto && dist < 10.5f && Random.value < 0.10f)
                    {
                        FireSecondaryWeapon(t);
                    }
                }
            }
        }

        // ============================================================
        // Weapons (Main Cannon / Autocannon / Twin Autocannon / ATGM)
        // ============================================================
        private void FirePrimaryWeapon(TankEntity t)
        {
            if (!t.CanFire || _phase != GamePhase.Playing) return;
            string w = t.vehicleDef != null ? t.vehicleDef.weapon : "MainCannon";
            switch (w)
            {
                case "AutocannonATGM":
                    FireAutocannon(t, twinBarrel: false);
                    break;
                case "TwinAutocannonATGM":
                    FireAutocannon(t, twinBarrel: true);
                    break;
                case "ATGM":
                    FireAtgmMissile(t, asSecondary: false);
                    break;
                default:
                    FireMainCannon(t);
                    break;
            }
        }

        private void FireSecondaryWeapon(TankEntity t)
        {
            if (!t.CanFire || _phase != GamePhase.Playing) return;
            if (t.vehicleDef != null && t.vehicleDef.HasSecondaryAtgm)
                FireAtgmMissile(t, asSecondary: true);
            else
                FireMG(t);
        }

        private void FireMainCannon(TankEntity t)
        {
            if (t.reloadTimer > 0f) return;

            AmmoType ammo = t.CurrentAmmo;
            if (ammo == AmmoType.GLATGM)
            {
                FireAtgmMissile(t, asSecondary: false, ammoOverride: AmmoType.GLATGM);
                return;
            }

            t.BeginReload();
            t.TriggerFireKick(ammo == AmmoType.HE ? 4.4f : 3.8f);

            t.GetMuzzlePositions(squash, spacingPx, out Vector2 visMuz, out Vector2 gndMuz);
            Vector2 td = TankEntity.SliceAngleToDir(t.turretAngleDeg);
            Vector2 dir = SpriteStackCamera.Squash(td, squash).normalized;
            t.Position2D -= dir * (0.8f / PPU);

            float basePen = t.vehicleDef != null ? t.vehicleDef.penetration : 80f;
            float speedPx = ammo switch
            {
                AmmoType.APFSDS => 305f,
                AmmoType.APHE => 255f,
                AmmoType.HEATFS => 222f,
                AmmoType.HE => 195f,
                _ => 265f
            };
            float pen = ammo switch
            {
                AmmoType.APFSDS => basePen * 1.08f,
                AmmoType.APHE => basePen * 0.96f,
                AmmoType.HEATFS => basePen * 1.02f,
                AmmoType.HE => basePen * 0.36f,
                _ => basePen
            };
            int bounces = (ammo == AmmoType.APFSDS || ammo == AmmoType.APHE) ? 1 : 0;
            SpawnProjectile(ProjectileKind.Shell, ammo, t, visMuz, gndMuz, dir * (speedPx / PPU), pen, armorBounces: bounces, life: 4.0f);

        }

        private void PlayMainCannonEffects(TankEntity t, Vector2 visMuz, Vector2 gndMuz, Vector2 dir)
        {
            Vector2 perp = new Vector2(-dir.y, dir.x);

            // 1. Crisp HDR muzzle flare (blooms naturally via URP Post-Process!)
            SpawnParticle(visMuz + dir * 0.14f, dir * 1.2f, 0f, 0.055f, 0.34f, new Color(1f, 0.98f, 0.78f), 3.6f);
            SpawnParticle(visMuz + dir * 0.34f, dir * 2.4f, 0f, 0.045f, 0.22f, new Color(1f, 0.72f, 0.25f), 2.8f);

            // 2. Side-venting muzzle blast jets
            for (int side = -1; side <= 1; side += 2)
            {
                for (int k = 0; k < 2; k++)
                {
                    Vector2 ventDir = (perp * side * 0.92f + dir * (0.25f - k * 0.15f)).normalized;
                    SpawnParticle(
                        visMuz,
                        ventDir * Random.Range(2.2f, 3.8f),
                        0f,
                        Random.Range(0.08f, 0.16f),
                        k == 0 ? 0.18f : 0.13f,
                        k == 0 ? new Color(1f, 0.85f, 0.45f) : new Color(0.84f, 0.78f, 0.68f),
                        k == 0 ? 2.2f : 1.0f
                    );
                }
            }

            // 3. Desert sand shockwave under barrel
            for (int i = 0; i < 6; i++)
            {
                float ang = Random.Range(-1.35f, 1.35f);
                Vector2 dustDir = Rotate2D(dir, ang);
                Color sandCol = i % 2 == 0 ? new Color(0.86f, 0.74f, 0.50f, 0.85f) : new Color(0.75f, 0.62f, 0.40f, 0.75f);
                SpawnParticle(gndMuz + Random.insideUnitCircle * 0.22f, dustDir * Random.Range(1.0f, 2.6f), 0.15f, Random.Range(0.28f, 0.55f), Random.Range(0.16f, 0.26f), sandCol, 1.0f);
            }

            // 4. Lingering muzzle gunsmoke
            for (int i = 0; i < 3; i++)
            {
                SpawnParticle(
                    visMuz + dir * (0.08f * i),
                    dir * Random.Range(0.35f, 0.95f) + new Vector2(Random.Range(-0.25f, 0.25f), Random.Range(0.25f, 0.55f)),
                    0.2f,
                    Random.Range(0.45f, 0.80f),
                    Random.Range(0.14f, 0.22f),
                    new Color(0.56f, 0.54f, 0.50f, 0.7f),
                    1.0f
                );
            }

            if (t.isPlayer) _camKickVel -= dir * 4.8f;
            if (_postProcess != null) _postProcess.PulseCannonFire(t.isPlayer);
            AudioClip cannonClip = (t.vehicleDef != null && t.vehicleDef.nation == "USSR") ? _sfxCannonT90 : _sfxCannonAbrams;
            float maxCannonDur = cannonClip == _sfxCannonT90 ? 1.55f : 0f;
            PlaySFXAt(cannonClip, gndMuz, SoundBus.Reverb, t.isPlayer ? 0.80f : 0.72f, maxDuration: maxCannonDur, maxConcurrentForClip: 3);
        }

        private void FireAutocannon(TankEntity t, bool twinBarrel)
        {
            if (t.reloadTimer > 0f) return;
            t.BeginReload();
            t.TriggerFireKick(1.6f);

            float lateralPx = 0f;
            if (twinBarrel)
            {
                t.twinBarrelToggle = 1 - t.twinBarrelToggle;
                lateralPx = t.twinBarrelToggle == 0 ? -2.2f : 2.2f;
            }

            t.GetMuzzlePositions(squash, spacingPx, out Vector2 visMuz, out Vector2 gndMuz, lateralPx);
            Vector2 td = TankEntity.SliceAngleToDir(t.turretAngleDeg);
            Vector2 baseDir = SpriteStackCamera.Squash(td, squash).normalized;
            Vector2 dir = Rotate2D(baseDir, Random.Range(-0.025f, 0.025f));

            AmmoType ammo = t.CurrentAmmo == AmmoType.HEIBelt ? AmmoType.HEIBelt : AmmoType.APBelt;
            float basePen = t.vehicleDef != null ? t.vehicleDef.penetration : 36f;
            float speedPx = ammo == AmmoType.HEIBelt ? 268f : 308f;
            float penetration = ammo == AmmoType.HEIBelt ? basePen * 0.56f : basePen * 1.06f;
            SpawnProjectile(ProjectileKind.Autocannon, ammo, t, visMuz, gndMuz, dir * (speedPx / PPU), penetration, armorBounces: 0, life: 2.5f);

        }

        private void PlayAutocannonEffects(TankEntity t, Vector2 visMuz, Vector2 gndMuz, Vector2 dir)
        {
            SpawnParticle(visMuz + dir * 0.12f, dir * 1.4f, 0f, 0.045f, 0.22f, new Color(1f, 0.94f, 0.56f), 3.1f);
            SpawnParticle(gndMuz, dir * 0.9f + Random.insideUnitCircle * 0.4f, 0.12f, 0.22f, 0.28f, new Color(0.76f, 0.68f, 0.52f, 0.55f), 1.0f, _smokePuffSprite);

            if (t.isPlayer) _camKickVel -= dir * 1.45f;
            // Crisp high-tempo autocannon bark (pitched-up cannon + dry punch)
            PlaySFXAt(_sfxCannonAbrams, gndMuz, SoundBus.Dry, t.isPlayer ? 0.48f : 0.40f, centerPan: false, pitchMin: 1.52f, pitchMax: 1.72f);
        }

        private void FireAtgmMissile(TankEntity t, bool asSecondary, AmmoType? ammoOverride = null)
        {
            if (asSecondary)
            {
                if (t.atgmCooldown > 0f) return;
                t.atgmCooldown = t.atgmDuration;
            }
            else
            {
                if (t.reloadTimer > 0f) return;
                t.BeginReload();
            }

            AmmoType ammo = ammoOverride ?? (asSecondary ? AmmoType.TOW : t.CurrentAmmo);
            if (!asSecondary && ammo != AmmoType.TOW && ammo != AmmoType.GLATGM && ammo != AmmoType.HE)
                ammo = AmmoType.TOW;

            t.TriggerFireKick(ammo == AmmoType.GLATGM ? 3.2f : 2.0f);
            float sideOff = asSecondary ? 5.5f : 0f;
            t.GetMuzzlePositions(squash, spacingPx, out Vector2 visMuz, out Vector2 gndMuz, sideOff);
            Vector2 td = TankEntity.SliceAngleToDir(t.turretAngleDeg);
            Vector2 dir = SpriteStackCamera.Squash(td, squash).normalized;

            float basePen = asSecondary
                ? 82f + Mathf.Min(t.vehicleDef != null ? t.vehicleDef.tier : 8, 10) * 1.0f
                : (t.vehicleDef != null ? t.vehicleDef.penetration : 88f);
            float penetration = ammo == AmmoType.HE ? basePen * 0.42f : basePen * 1.08f;
            float speedPx = ammo == AmmoType.GLATGM ? 232f : (ammo == AmmoType.HE ? 195f : 206f);
            SpawnProjectile(ProjectileKind.ATGM, ammo, t, visMuz, gndMuz, dir * (speedPx / PPU), penetration, armorBounces: 0, life: 5.5f);

        }

        private void PlayAtgmEffects(TankEntity t, AmmoType ammo, Vector2 visMuz, Vector2 gndMuz, Vector2 dir)
        {
            // Rocket backblast smoke & fiery ignition plume
            for (int i = 0; i < 8; i++)
            {
                Vector2 back = Rotate2D(-dir, Random.Range(-0.55f, 0.55f));
                SpawnParticle(
                    visMuz - dir * 0.25f,
                    back * Random.Range(1.4f, 3.6f),
                    0.15f,
                    Random.Range(0.28f, 0.58f),
                    i < 3 ? Random.Range(0.22f, 0.34f) : Random.Range(0.42f, 0.68f),
                    i < 3 ? new Color(1f, 0.72f, 0.24f) : new Color(0.88f, 0.86f, 0.82f, 0.72f),
                    i < 3 ? 4.8f : 1.0f,
                    i < 3 ? _whitePxSprite : _smokePuffSprite
                );
            }

            if (t.isPlayer) _camKickVel -= dir * 2.2f;
            if (_postProcess != null) _postProcess.PulseCannonFire(t.isPlayer);
            // Crisp missile launch ignition (trimmed & concurrency-capped so 6s/2.7s clips never stack into harsh noise!)
            bool heavyLaunch = ammo == AmmoType.GLATGM || ammo == AmmoType.HE;
            AudioClip launchClip = heavyLaunch ? _sfxMissileHeavy : _sfxMissileLaunch;
            float launchVol = heavyLaunch ? (t.isPlayer ? 0.72f : 0.56f) : (t.isPlayer ? 0.78f : 0.62f);
            float launchMaxDur = heavyLaunch ? 1.10f : 0.95f;
            int maxVoices = heavyLaunch ? 1 : 2;
            PlaySFXAt(
                launchClip,
                gndMuz,
                SoundBus.Reverb,
                launchVol,
                centerPan: false,
                pitchMin: 0.95f,
                pitchMax: 1.05f,
                maxDuration: launchMaxDur,
                maxConcurrentForClip: maxVoices,
                minRetriggerSec: 0.10f
            );
        }

        private void FireMG(TankEntity t)
        {
            if (t.mgCooldown > 0f) return;
            t.mgCooldown = 0.11f;

            Vector2 piv = t.GetTurretPivotWorld(squash);
            Vector2 td = TankEntity.SliceAngleToDir(t.turretAngleDeg);
            Vector2 baseDir = SpriteStackCamera.Squash(td, squash).normalized;
            Vector2 dir = Rotate2D(baseDir, Random.Range(-0.06f, 0.06f));

            float dist = 12f / PPU;
            Vector2 gndPos = piv + SpriteStackCamera.Squash(td * dist, squash);
            Vector2 visPos = gndPos + SpriteStackCamera.CamUp * (((t.meta.hullBaseHeight + 2f) * spacingPx) / PPU);

            SpawnProjectile(ProjectileKind.MG, AmmoType.MG, t, visPos, gndPos, dir * (310f / PPU), 0f, armorBounces: 0, life: 0.95f);
        }

        private void PlayMgEffects(TankEntity t, Vector2 visPos, Vector2 gndPos, Vector2 dir)
        {
            SpawnParticle(visPos, dir * 1.5f, 0f, 0.06f, 0.13f, new Color(1f, 0.95f, 0.62f), 2.8f);
            // Coaxial MG stays crisp & dry (no reverb muddiness on rapid bursts!)
            PlaySFXAt(_sfxMg, gndPos, SoundBus.Dry, t.isPlayer ? 0.28f : 0.24f);
        }

        private SpriteRenderer CreateEffect(string effectName)
        {
            var sprite = Instantiate(spawnAssets.SpriteEffect, _fxRoot, false);
            sprite.name = effectName;
            return sprite;
        }

        private void SpawnProjectile(ProjectileKind kind, AmmoType ammo, TankEntity owner, Vector2 visPos, Vector2 gndPos, Vector2 vel, float penetration, int armorBounces, float life)
        {
            var sr = CreateEffect($"{kind}_{ammo}");
            var go = sr.gameObject;
            go.transform.position = new Vector3(visPos.x, visPos.y, -2f);
            sr.sprite = _whitePxSprite;
            SetProjectileAppearance(sr, ammo);
            go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg);
            if (MultiplayerSessionConnection.Online)
                MultiplayerMatchTransport.Instance?.BroadcastWeaponFired(System.Array.IndexOf(_networkTanks, owner),
                    kind, ammo, visPos, gndPos, vel.normalized);

            _projectiles.Add(new ProjectileData
            {
                networkId = ++_nextProjectileId,
                kind = kind,
                ammo = ammo,
                ownerFaction = owner.factionTeam,
                owner = owner,
                playerOwned = owner.isPlayer,
                visualPos = visPos,
                groundPos = gndPos,
                vel = vel,
                speed = vel.magnitude,
                penetration = penetration,
                armorBouncesLeft = armorBounces,
                life = life,
                age = 0f,
                go = go,
                sr = sr
            });
            PresentWeaponEffects(owner, kind, ammo, visPos, gndPos, vel.normalized);
        }

        private void SetProjectileAppearance(SpriteRenderer sr, AmmoType ammo)
        {
            sr.color = ammo switch
            {
                AmmoType.TOW => new Color(1f, 0.38f, 0.14f),
                AmmoType.GLATGM => new Color(0.55f, 0.95f, 1f),
                AmmoType.HEATFS => new Color(1f, 0.56f, 0.18f),
                AmmoType.HE => new Color(1f, 0.68f, 0.16f),
                AmmoType.APHE => new Color(1f, 0.84f, 0.36f),
                AmmoType.APFSDS => new Color(1f, 0.96f, 0.72f),
                AmmoType.HEIBelt => new Color(1f, 0.52f, 0.16f),
                AmmoType.APBelt => new Color(1f, 0.88f, 0.44f),
                _ => new Color(1f, 0.85f, 0.42f)
            };
            sr.sortingOrder = 15000;
            if (_emissiveFxMat != null)
            {
                sr.sharedMaterial = _emissiveFxMat;
                float hdr = ammo switch
                {
                    AmmoType.TOW => 12.5f,
                    AmmoType.GLATGM => 12.0f,
                    AmmoType.HEATFS => 11.0f,
                    AmmoType.HE => 10.5f,
                    AmmoType.APFSDS => 10.0f,
                    AmmoType.APHE => 9.5f,
                    AmmoType.HEIBelt => 8.8f,
                    AmmoType.APBelt => 8.0f,
                    _ => 6.2f
                };
                _fxMpb.SetFloat(EmissionIntensityId, hdr);
                sr.SetPropertyBlock(_fxMpb);
            }
            sr.transform.localScale = ammo switch
            {
                AmmoType.TOW or AmmoType.GLATGM => new Vector3(0.46f, 0.17f, 1f),
                AmmoType.HE => new Vector3(0.36f, 0.18f, 1f),
                AmmoType.HEATFS or AmmoType.APFSDS or AmmoType.APHE => new Vector3(0.34f, 0.15f, 1f),
                AmmoType.APBelt or AmmoType.HEIBelt => new Vector3(0.24f, 0.11f, 1f),
                _ => new Vector3(0.16f, 0.09f, 1f)
            };
        }

        private void UpdateProjectiles(float dt)
        {
            Vector2 listenerPos = GetAudioListenerPos();
            bool hasGuidedMissile = false;
            Vector2 closestMissilePos = listenerPos;
            float closestMissileDistSq = float.MaxValue;

            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                var p = _projectiles[i];
                p.life -= dt;
                p.age += dt;
                bool remove = p.life <= 0f;

                // SACLOS Mouse-Guided TOW / GL-ATGM Missile Steering!
                if (!remove && p.IsGuided && p.age >= 0.07f && p.speed > 0.1f)
                {
                    Vector2 aimTarget = Vector2.zero;
                    bool hasTarget = false;
                    float turnRateDeg = 220f;

                    if (p.playerOwned && _phase == GamePhase.Playing)
                    {
                        aimTarget = GetMouseWorld2D();
                        hasTarget = true;
                        turnRateDeg = p.ammo == AmmoType.TOW ? 225f : 185f;
                    }
                    else if (!p.playerOwned && MultiplayerSessionConnection.Online && _phase == GamePhase.Playing)
                    {
                        int slot = _networkTanks == null ? -1 : System.Array.IndexOf(_networkTanks, p.owner);
                        if (MultiplayerMatchTransport.Instance != null && MultiplayerMatchTransport.Instance.TryGetControl(slot, out var control))
                        {
                            aimTarget = new Vector2(control.AimX, control.AimY);
                            hasTarget = true;
                            turnRateDeg = p.ammo == AmmoType.TOW ? 225f : 185f;
                        }
                    }
                    else if (!p.playerOwned && _phase == GamePhase.Playing)
                    {
                        // AI missile gently tracks nearest opposing tank (dodgeable behind buildings/smoke!)
                        float bestD = float.MaxValue;
                        for (int ti = 0; ti < _tanks.Count; ti++)
                        {
                            var cand = _tanks[ti];
                            if (cand == null || !cand.alive || cand.factionTeam == p.ownerFaction) continue;
                            float d = (cand.Position2D - p.groundPos).sqrMagnitude;
                            if (d < bestD) { bestD = d; aimTarget = cand.Position2D; hasTarget = true; }
                        }
                        turnRateDeg = 72f;
                    }

                    if (hasTarget)
                    {
                        Vector2 toGoal = aimTarget - p.groundPos;
                        if (toGoal.sqrMagnitude > 0.64f)
                        {
                            float curAng = Mathf.Atan2(p.vel.y, p.vel.x) * Mathf.Rad2Deg;
                            float wantAng = Mathf.Atan2(toGoal.y, toGoal.x) * Mathf.Rad2Deg;
                            float deltaAng = Mathf.DeltaAngle(curAng, wantAng);
                            // True SACLOS forward-cone guidance: once the missile passes the cursor
                            // (cursor is >68 deg off the nose / behind it), NEVER U-turn back — fly straight ahead!
                            if (Mathf.Abs(deltaAng) <= 68f)
                            {
                                float wobble = p.ammo == AmmoType.TOW ? Mathf.Sin(p.age * 21f) * 3.8f : Mathf.Sin(p.age * 16f) * 2.0f;
                                float newAng = Mathf.MoveTowardsAngle(curAng, wantAng + wobble, turnRateDeg * dt);
                                float rad = newAng * Mathf.Deg2Rad;
                                p.vel = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * p.speed;
                                if (p.go != null) p.go.transform.rotation = Quaternion.Euler(0f, 0f, newAng);
                            }
                        }
                    }

                    // Hand off smoothly to the single spatialized in-flight motor loop after the initial ignition burst
                    if (p.age >= 0.25f)
                    {
                        float dSq = (p.groundPos - listenerPos).sqrMagnitude;
                        if (dSq < closestMissileDistSq)
                        {
                            closestMissileDistSq = dSq;
                            closestMissilePos = p.groundPos;
                            hasGuidedMissile = true;
                        }
                    }
                }

                int subSteps = p.IsHeavy ? 4 : 3;
                float subDt = dt / subSteps;

                for (int s = 0; s < subSteps && !remove; s++)
                {
                    Vector2 nextGnd = p.groundPos + p.vel * subDt;

                    if (nextGnd.x <= BorderPad || nextGnd.x >= WorldW - BorderPad ||
                        nextGnd.y <= BorderPad || nextGnd.y >= WorldH - BorderPad)
                    {
                        if (p.IsPrimary)
                            EmitExplosion(p.visualPos, p.groundPos, p.ammo == AmmoType.HE, SoundBus.Reverb,
                                p.IsHeavy ? (p.ammo == AmmoType.HE ? 0.78f : 0.56f) : 0f);
                        remove = true;
                        break;
                    }

                    DestructibleProp hitProp = null;
                    for (int pi = 0; pi < _props.Count; pi++)
                    {
                        if (_props[pi] != null && _props[pi].IsSolidAt(nextGnd))
                        {
                            hitProp = _props[pi];
                            break;
                        }
                    }

                    if (hitProp != null)
                    {
                        float heMul = p.ammo == AmmoType.HE ? 1.85f : (p.ammo == AmmoType.HEIBelt ? 1.55f : 1.0f);
                        float craterR = (p.IsHeavy
                            ? (hitProp.meta.ricochetHeavy ? 4.2f : 5.6f)
                            : (p.kind == ProjectileKind.Autocannon ? 2.4f : 1.5f)) * heMul;
                        var randomState = Random.state;
                        if (MultiplayerSessionConnection.Online)
                        {
                            int seed = Random.Range(0, int.MaxValue);
                            randomState = Random.state;
                            MultiplayerMatchTransport.Instance?.BroadcastPropImpact(_props.IndexOf(hitProp), nextGnd, craterR, p.IsHeavy || p.ammo == AmmoType.HEIBelt, seed);
                            Random.InitState(seed);
                        }
                        bool collapsed = hitProp.ApplyHit(nextGnd, craterR, p.IsHeavy || p.ammo == AmmoType.HEIBelt, SpawnDebrisParticle);
                        if (MultiplayerSessionConnection.Online) Random.state = randomState;

                        if (collapsed && (hitProp.kind == "supply_crate" || hitProp.kind == "fuel_tank"))
                        {
                            bool isFuel = hitProp.kind == "fuel_tank";
                            Vector2 cPos = hitProp.transform.position;
                            EmitBattleEffect(new BattleEffect { Kind = BattleEffectKind.PropBlast, Ground = cPos, Big = isFuel });
                            float blastR = isFuel ? 4.6f : 2.9f;
                            foreach (var t in _tanks)
                            {
                                if (_phase != GamePhase.Playing || t == null || !t.alive) continue;
                                float distance = Vector2.Distance(t.Position2D, cPos);
                                if (distance >= blastR) continue;
                                TankHit damage = t.ApplyBlast(cPos, squash, isFuel, distance < blastR * 0.55f);
                                if (damage.Ignited || damage.Modules != TankModule.None)
                                    ShowTankHit(t, damage);
                                if (!t.alive)
                                {
                                    ShowTankLoss(t);
                                    if (MultiplayerSessionConnection.Online || t.isPlayer || WillRoundEndAfterKill(t))
                                        TriggerSyntheticDecisiveXRay(t, isFuel ? "FUEL" : "BLAST");
                                    CheckTeamVictoryOrWave(t);
                                }
                            }
                        }
                        else if (p.ammo == AmmoType.HE)
                        {
                            EmitExplosion(p.visualPos, nextGnd, true, SoundBus.HeavyReverb, 0.84f, 0.34f);
                            ApplyHeOverpressureSplash(nextGnd, p.ownerFaction, 2.75f);
                        }
                        else if (p.IsHeavy)
                        {
                            EmitExplosion(p.visualPos, nextGnd, false, SoundBus.Reverb, 0.56f, 0.14f);
                        }
                        else if (p.kind == ProjectileKind.Autocannon)
                        {
                            EmitBattleEffect(new BattleEffect { Kind = BattleEffectKind.PropSpark, Visual = p.visualPos,
                                Big = p.ammo == AmmoType.HEIBelt });
                        }

                        remove = true;
                        break;
                    }

                    TankEntity hitTank = null;
                    ArmorContact contact = default;
                    if (_phase == GamePhase.Playing)
                    {
                        foreach (var target in _tanks)
                        {
                            if (target == null || !target.alive || target.factionTeam == p.ownerFaction) continue;
                            if (!target.TryGetArmorContact(p.groundPos, nextGnd, squash, out ArmorContact candidate)) continue;
                            if (hitTank != null && candidate.Fraction >= contact.Fraction) continue;
                            hitTank = target;
                            contact = candidate;
                        }
                    }

                    if (hitTank != null)
                    {
                        Vector2 impact = Vector2.Lerp(p.groundPos, nextGnd, contact.Fraction);
                        Vector2 visualImpact = impact + (p.visualPos - p.groundPos);
                        TankHit hit = hitTank.ApplyArmorHit(contact, p.kind, p.penetration, out TankShotTrace trace, p.ammo);
                        bool bounced = hit.Armor == ArmorHit.Ricochet && p.armorBouncesLeft > 0;
                        if (bounced)
                        {
                            p.vel = Vector2.Reflect(p.vel, contact.GroundNormal);
                            trace = new TankShotTrace(trace.Entry, trace.Approach,
                                trace.Entry + hitTank.ToCombatDirection(p.vel, squash) * 0.65f);
                        }
                        EmitBattleEffect(new BattleEffect { Kind = BattleEffectKind.ArmorImpact, Projectile = p.kind,
                            Ground = impact, Visual = visualImpact, Normal = contact.GroundNormal, VictimPosition = hitTank.Position2D,
                            Hit = hit, Trace = trace, Loss = hitTank.Loss, Bounced = bounced,
                            EndsRound = !hitTank.alive && WillRoundEndAfterKill(hitTank) }, hitTank, p.owner);

                        if (bounced)
                        {
                            p.armorBouncesLeft--;
                            p.penetration *= 0.76f;
                            Vector2 escape = contact.GroundNormal * 0.04f;
                            p.groundPos = impact + escape;
                            p.visualPos = visualImpact + escape;
                            p.sr.color = new Color(1f, 0.42f, 0.22f);
                            p.go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(p.vel.y, p.vel.x) * Mathf.Rad2Deg);
                            continue;
                        }

                        if (p.ammo == AmmoType.HE)
                            ApplyHeOverpressureSplash(impact, p.ownerFaction, 2.4f, excludeTank: hitTank);

                        if (!hitTank.alive)
                        {
                            ShowTankLoss(hitTank);
                            CheckTeamVictoryOrWave(hitTank);
                        }
                        remove = true;
                        break;
                    }

                    p.groundPos = nextGnd;
                    p.visualPos += p.vel * subDt;
                }

                if (remove)
                {
                    if (p.go != null) Destroy(p.go);
                    _projectiles.RemoveAt(i);
                }
                else
                {
                    p.go.transform.position = new Vector3(p.visualPos.x, p.visualPos.y, -2f);
                    PlayProjectileTrail(p.kind, p.ammo, p.visualPos, p.vel.normalized);
                }
            }

            _audio?.UpdateMissileMotorLoop(hasGuidedMissile && _phase == GamePhase.Playing, closestMissilePos, listenerPos, sfxEnabled, dt, sfxVolume);
        }

        private void PlayProjectileTrail(ProjectileKind kind, AmmoType ammo, Vector2 position, Vector2 direction)
        {
            if (kind == ProjectileKind.ATGM || AmmoInfo.IsGuided(ammo))
            {
                // Signature blazing HDR TOW/ATGM rocket motor + thick white guidance exhaust trail!
                bool ember = Random.value < 0.58f;
                Color flameCol = ammo == AmmoType.GLATGM
                    ? new Color(0.58f, 0.94f, 1f)
                    : new Color(1f, 0.52f, 0.14f);
                SpawnParticle(
                    position - direction * 0.22f,
                    -direction * 0.6f + new Vector2(Random.Range(-0.2f, 0.2f), Random.Range(0.1f, 0.3f)),
                    0.1f,
                    ember ? 0.16f : 0.42f,
                    ember ? 0.16f : 0.22f,
                    ember ? flameCol : new Color(0.92f, 0.91f, 0.88f, 0.68f),
                    ember ? 7.5f : 1.0f,
                    ember ? _whitePxSprite : _smokePuffSprite
                );
            }
            else if (kind == ProjectileKind.Shell)
            {
                Color wakeCol = ammo == AmmoType.HEATFS || ammo == AmmoType.HE
                    ? new Color(1f, 0.58f, 0.18f, 0.88f)
                    : new Color(1f, 0.86f, 0.44f, 0.85f);
                SpawnParticle(
                    position - direction * 0.14f,
                    Vector2.zero,
                    0f,
                    0.065f,
                    0.10f,
                    wakeCol,
                    5.2f
                );
                if (Random.value < 0.45f)
                {
                    SpawnParticle(
                        position - direction * 0.18f,
                        new Vector2(Random.Range(-0.15f, 0.15f), Random.Range(0.05f, 0.25f)),
                        0f,
                        0.18f,
                        0.09f,
                        new Color(0.92f, 0.88f, 0.78f, 0.50f),
                        1.0f
                    );
                }
            }
            else if (kind == ProjectileKind.Autocannon && Random.value < 0.65f)
            {
                Color acCol = ammo == AmmoType.HEIBelt
                    ? new Color(1f, 0.48f, 0.16f, 0.85f)
                    : new Color(1f, 0.76f, 0.28f, 0.80f);
                SpawnParticle(
                    position - direction * 0.10f,
                    Vector2.zero,
                    0f,
                    0.05f,
                    0.08f,
                    acCol,
                    4.5f
                );
            }
        }

        private void ApplyHeOverpressureSplash(Vector2 origin, string ownerFaction, float blastRadius, TankEntity excludeTank = null)
        {
            foreach (var t in _tanks)
            {
                if (_phase != GamePhase.Playing || t == null || !t.alive || t == excludeTank) continue;
                if (t.factionTeam == ownerFaction) continue;
                float dist = Vector2.Distance(t.Position2D, origin);
                if (dist >= blastRadius) continue;
                TankHit splash = t.ApplyBlast(origin, squash, fuel: false, close: dist < blastRadius * 0.55f);
                if (splash.Modules != TankModule.None || splash.Ignited)
                    ShowTankHit(t, splash);
                if (!t.alive)
                {
                    ShowTankLoss(t);
                    if (MultiplayerSessionConnection.Online || t.isPlayer || WillRoundEndAfterKill(t))
                        TriggerSyntheticDecisiveXRay(t, "HE");
                    CheckTeamVictoryOrWave(t);
                }
            }
        }

        private void ShowArmorDeflection(TankEntity target, Vector2 ground, Vector2 visual, Vector2 normal)
        {
            target.TriggerArmorRicochetFlash();
            SpawnRicochetSparks(visual, normal);
            if (_postProcess != null) _postProcess.PulseRicochet();
            PlaySFXAt(_sfxRicochet, ground, SoundBus.Reverb, 0.65f);
        }

        private void ShowTankHit(TankEntity target, TankHit hit) =>
            EmitBattleEffect(new BattleEffect { Kind = BattleEffectKind.Splash, Hit = hit, Loss = target.Loss }, target);

        private void PresentHitLabel(TankEntity target, TankHit hit)
        {
            string label;
            if (hit.Ignited) label = "ENGINE FIRE";
            else if ((hit.Modules & TankModule.Ammo) != 0) label = "AMMO HIT";
            else if ((hit.Modules & TankModule.Weapon) != 0) label = "WEAPON DISABLED";
            else if ((hit.Modules & TankModule.Traverse) != 0) label = "TRAVERSE DISABLED";
            else if ((hit.Modules & TankModule.Engine) != 0) label = "ENGINE DISABLED";
            else if (hit.CrewLost > 0) label = $"CREW -{hit.CrewLost}";
            else if (hit.Modules != TankModule.None) label = "RUNNING GEAR HIT";
            else label = hit.Armor switch
            {
                ArmorHit.Penetrated => "PENETRATION",
                ArmorHit.Ricochet => "RICOCHET",
                _ => "NO PENETRATION"
            };

            Color color = hit.Armor == ArmorHit.Penetrated || hit.Modules != TankModule.None
                ? new Color(1f, 0.55f, 0.24f) : new Color(1f, 0.88f, 0.47f);
            AddPopup(target.Position2D + Vector2.up * 1.45f, label, color);
        }

        private void ShowTankLoss(TankEntity target) =>
            EmitBattleEffect(new BattleEffect { Kind = BattleEffectKind.TankLoss, Loss = target.Loss, Ground = target.Position2D }, target);

        private void PresentTankLoss(TankEntity target, TankLoss loss, Vector2 position)
        {
            string label = loss switch
            {
                TankLoss.Ammo => "AMMO COOKOFF",
                TankLoss.Fire => "BURNED OUT",
                _ => "CREW KNOCKED OUT"
            };
            AddPopup(position + Vector2.up * 1.55f, label, new Color(1f, 0.64f, 0.26f));
            if (loss == TankLoss.Ammo)
            {
                PresentExplosion(position + Vector2.up * 0.5f, position, true, SoundBus.HeavyReverb, 0.94f, 0.50f);
                target.TriggerTurretToss();
            }
        }

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
                _hud?.ShowWaveToast($"WAVE {_currentWave} CLEAR  FIELD SERVICE", 2.2f);
                return;
            }

            // Round ends if either team is eliminated (or player died in Survival!)
            bool roundFinished = allyAlive == 0 || enemyAlive == 0 || (battleMode == BattleMode.SurvivalWave && (player == null || !player.alive));
            if (!roundFinished) return;

            // Keep the round-ending camera on the final casualty.
            _phase = GamePhase.RoundOver;
            _aftermathTimer = 0f;
            _resultBannerShown = false;
            if (!MultiplayerSessionConnection.Online) _hitStop = 0.09f;
            _shake = 0.50f;
            _killFocusPos = justKilled.Position2D;

            string winner = (battleMode == BattleMode.SurvivalWave && (player == null || !player.alive))
                ? "enemy"
                : (allyAlive > 0 ? "ally" : "enemy");

            _pendingWinner = winner;
            if (winner == "ally") _winsAlly++; else _winsEnemy++;

            string pName = SelectedPlayerVehicle != null ? SelectedPlayerVehicle.name.ToUpper() : "PLAYER";
            string eName = SelectedEnemyVehicle != null ? SelectedEnemyVehicle.name.ToUpper() : "HOSTILE";
            _pendingSubtitle = MultiplayerSessionConnection.Online ? "ONLINE BATTLE" : battleMode switch
            {
                BattleMode.SurvivalWave => $"{pName} SURVIVED TO WAVE {_currentWave}",
                BattleMode.Battle5v5 => winner == "ally" ? "5v5 ALLIED VICTORY" : "5v5 ENEMY VICTORY",
                BattleMode.Squad2v2 => winner == "ally" ? "2v2 SQUAD VICTORY" : "2v2 HOSTILE VICTORY",
                _ => winner == "ally" ? $"{pName} VICTORY" : $"{eName} VICTORY"
            };
        }

        // ============================================================
        // Physics, Collisions, LOS (Props + Smoke Clouds), & FX
        // ============================================================
        private void ResolveTankCollisions(TankEntity t)
        {
            float rad = 12f / PPU;
            Vector2 p = t.Position2D;
            p.x = Mathf.Clamp(p.x, BorderPad + rad, WorldW - BorderPad - rad);
            p.y = Mathf.Clamp(p.y, BorderPad + rad, WorldH - BorderPad - rad);

            foreach (var prop in _props)
            {
                if (prop == null || !prop.alive) continue;
                prop.ResolveTankPush(ref p, rad, squash);
            }
            p.x = Mathf.Clamp(p.x, BorderPad + rad, WorldW - BorderPad - rad);
            p.y = Mathf.Clamp(p.y, BorderPad + rad, WorldH - BorderPad - rad);
            t.Position2D = p;
        }

        private void UpdateTankMovementEffects(TankEntity t, float dt)
        {
            if (t.treadAccum >= 4.2f / PPU)
            {
                t.treadAccum = 0f;
                StampTankTreads(t);
            }

            UpdateTrackDust(t, dt);

            if (t.OnFire || t.HasDisabled(TankModule.Engine) || t.Loss == TankLoss.Fire || t.Loss == TankLoss.Ammo)
            {
                t.smokeTimer -= dt;
                if (t.smokeTimer <= 0f)
                {
                    t.smokeTimer = t.alive ? 0.15f : 0.08f;
                    Color sc = ((t.OnFire || !t.alive) && Random.value < 0.38f)
                        ? new Color(1f, 0.46f, 0.13f)
                        : new Color(0.24f, 0.22f, 0.20f);
                    float hdr = sc.r > 0.8f ? 2.4f : 1.0f;
                    SpawnParticle(
                        t.Position2D + new Vector2(Random.Range(-0.35f, 0.35f), 0.7f),
                        new Vector2(Random.Range(-0.6f, 0.6f), Random.Range(0.8f, 1.4f)),
                        -0.3f,
                        Random.Range(0.45f, 0.8f),
                        t.alive ? 0.16f : 0.24f,
                        sc,
                        hdr
                    );
                }
            }
        }

        private void StampTankTreads(TankEntity t)
        {
            Vector2 d = TankEntity.SliceAngleToDir(t.hullAngleDeg);
            Vector2 perp = new Vector2(-d.y, d.x);
            float tankPpu = Mathf.Max(8f, t.pixelsPerUnit);
            float trackOff = ((t.meta != null && t.meta.hullWidth > 0 ? t.meta.hullWidth * 0.38f : 8.5f)) / tankPpu;
            Color32 treadCol = battleMap switch
            {
                BattleMap.City => new Color32(44, 45, 48, 255),
                BattleMap.Forest => new Color32(68, 56, 36, 255),
                _ => new Color32(122, 94, 56, 255)
            };
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 tp = t.Position2D + SpriteStackCamera.Squash(perp * (trackOff * side), squash);
                if (MultiplayerSessionConnection.Online)
                    AddOnlineTread(tp, treadCol);
                else
                    StampGroundRect(Mathf.RoundToInt(tp.x * PPU), Mathf.RoundToInt(tp.y * PPU), 2, 2, treadCol, 0.36f);
            }
        }

        /// <summary>
        /// Kicks up biome-aware dust clouds, dirt clods, and grit particles directly from left & right tracks
        /// (or tires on wheeled vehicles) proportional to forward speed and pivot-turn track slip!
        /// </summary>
        private void UpdateTrackDust(TankEntity t, float dt)
        {
            if (t == null || !t.alive || _phase != GamePhase.Playing) return;

            float speedAbs = Mathf.Abs(t.moveSpeed);
            float turnRate = t.HullTurnRateDeg;
            float turnAbs = Mathf.Abs(turnRate);

            float move01 = Mathf.Clamp01(speedAbs / 3.8f);
            float turn01 = Mathf.Clamp01(turnAbs / 115f);
            float intensity = Mathf.Clamp01(Mathf.Max(move01, turn01 * 0.92f) + move01 * turn01 * 0.35f);
            if (intensity < 0.07f)
            {
                t.dustTimer = Mathf.Min(t.dustTimer, 0.03f);
                return;
            }

            t.dustTimer -= dt;
            if (t.dustTimer > 0f) return;

            float biomeCadence = battleMap switch
            {
                BattleMap.Desert => 0.88f,
                BattleMap.City => 1.25f,
                _ => 1.02f
            };
            t.dustTimer = Mathf.Lerp(0.115f, 0.042f, intensity) * biomeCadence * Random.Range(0.85f, 1.15f);

            // Particle budget guard so 5v5 multi-tank skirmishes remain butter-smooth
            if (_particles.Count > 420) return;

            Vector2 fwd = TankEntity.SliceAngleToDir(t.hullAngleDeg);
            Vector2 right = new Vector2(-fwd.y, fwd.x);
            bool isWheeled = t.vehicleDef != null && t.vehicleDef.IsWheeled;

            float tankPpu = Mathf.Max(8f, t.pixelsPerUnit);
            float hullW = (t.meta != null && t.meta.hullWidth > 0 ? t.meta.hullWidth : 22) / tankPpu;
            float hullL = (t.meta != null && t.meta.hullLength > 0 ? t.meta.hullLength : 38) / tankPpu;
            float trackHalfW = hullW * (isWheeled ? 0.39f : 0.43f);
            float trackHalfL = hullL * 0.37f;

            bool pivotOnly = speedAbs < 0.35f && turnAbs > 18f;
            float moveSign = t.moveSpeed >= 0f ? 1f : -1f;

            for (int side = -1; side <= 1; side += 2)
            {
                // During in-place pivot turns, left & right tracks counter-rotate and throw dust in opposite directions!
                float localTrackDir = pivotOnly
                    ? (turnRate > 0f ? -side : side)
                    : moveSign;

                float alongSign = -localTrackDir; // Dust exits behind the driving end of the track
                float alongJitter = Random.Range(-0.18f, 0.14f) * hullL;
                Vector2 localOff = right * (side * trackHalfW) + fwd * (alongSign * trackHalfL + alongJitter);
                Vector2 emitPos = t.Position2D + SpriteStackCamera.Squash(localOff, squash);

                Vector2 kickDir = SpriteStackCamera.Squash(
                    (fwd * alongSign * Random.Range(0.55f, 1.05f) + right * (side * Random.Range(0.22f, 0.65f))).normalized,
                    squash
                );
                float kickSpd = Mathf.Lerp(0.45f, 1.55f, intensity) * Random.Range(0.8f, 1.25f);
                Vector2 puffVel = kickDir * kickSpd + SpriteStackCamera.CamUp * Random.Range(0.12f, 0.34f);

                Color puffCol;
                Color gritCol;
                float puffScale;
                float puffLife;
                float gritChance;

                if (battleMap == BattleMap.Forest)
                {
                    float shade = Random.Range(0.86f, 1.08f);
                    puffCol = new Color(0.46f * shade, 0.39f * shade, 0.28f * shade, Mathf.Lerp(0.24f, 0.44f, intensity));
                    gritCol = Random.value < 0.38f
                        ? new Color(0.24f, 0.44f, 0.18f, 0.92f)  // Torn grass flecks
                        : new Color(0.32f, 0.23f, 0.14f, 0.95f); // Wet dark mud clods
                    puffScale = Mathf.Lerp(0.30f, 0.56f, intensity) * (isWheeled ? 0.85f : 1.0f);
                    puffLife = Random.Range(0.36f, 0.62f);
                    gritChance = 0.58f;
                }
                else if (battleMap == BattleMap.City)
                {
                    float shade = Random.Range(0.90f, 1.08f);
                    puffCol = new Color(0.62f * shade, 0.61f * shade, 0.58f * shade, Mathf.Lerp(0.18f, 0.34f, intensity));
                    gritCol = new Color(0.46f * shade, 0.45f * shade, 0.43f * shade, 0.85f); // Concrete / asphalt grit
                    puffScale = Mathf.Lerp(0.26f, 0.48f, intensity) * (isWheeled ? 0.82f : 1.0f);
                    puffLife = Random.Range(0.32f, 0.54f);
                    gritChance = 0.32f;
                }
                else // Desert — thick sunlit sand plumes & flying sand grains!
                {
                    float shade = Random.Range(0.90f, 1.08f);
                    puffCol = new Color(0.84f * shade, 0.72f * shade, 0.50f * shade, Mathf.Lerp(0.30f, 0.54f, intensity));
                    gritCol = new Color(0.90f * shade, 0.77f * shade, 0.50f * shade, 0.94f);
                    puffScale = Mathf.Lerp(0.34f, 0.68f, intensity) * (isWheeled ? 0.86f : 1.05f);
                    puffLife = Random.Range(0.42f, 0.74f);
                    gritChance = 0.54f;
                }

                // Sort gently under the tank's SortingGroup (-pos.y * 100) so dust billows out from beneath the treads!
                int dustOrder = Mathf.Clamp(Mathf.RoundToInt(-emitPos.y * 100f) - 18, -10500, 10500);

                SpawnParticle(
                    emitPos + Random.insideUnitCircle * 0.08f,
                    puffVel,
                    gravity: 0.14f,
                    life: puffLife,
                    size: puffScale * Random.Range(0.85f, 1.18f),
                    color: puffCol,
                    emissionIntensity: 1.0f,
                    customSprite: _smokePuffSprite,
                    sortingOrder: dustOrder,
                    expandAndFade: true,
                    endScaleMul: 2.15f,
                    drag: 0.92f,
                    zPos: -0.4f
                );

                // Crisp pixel sand grains / mud clods / gravel chips kicked up by the sprockets
                if (Random.value < gritChance * (0.45f + intensity * 0.75f))
                {
                    Vector2 gritVel = kickDir * (kickSpd * Random.Range(1.1f, 1.9f))
                                      + SpriteStackCamera.CamUp * Random.Range(0.65f, 1.45f);
                    SpawnParticle(
                        emitPos + Random.insideUnitCircle * 0.06f,
                        gritVel,
                        gravity: -4.8f,
                        life: Random.Range(0.18f, 0.34f),
                        size: Random.value < 0.35f ? 0.10f : 0.065f,
                        color: gritCol,
                        emissionIntensity: 1.0f,
                        customSprite: _whitePxSprite,
                        sortingOrder: dustOrder + 4,
                        expandAndFade: false,
                        endScaleMul: 0.35f,
                        drag: 0.96f,
                        zPos: -0.6f
                    );
                }
            }
        }

        private void StampGroundRect(int cx, int cy, int w, int h, Color32 col, float alpha)
        {
            for (int dy = 0; dy < h; dy++)
            {
                int py = cy + dy;
                if (py < BorderPadPx || py >= WorldHeightPx - BorderPadPx) continue;
                for (int dx = 0; dx < w; dx++)
                {
                    int px = cx + dx;
                    if (px < BorderPadPx || px >= WorldWidthPx - BorderPadPx) continue;
                    int idx = py * WorldWidthPx + px;
                    _groundPixels[idx] = alpha >= 0.99f ? col : Color32.Lerp(_groundPixels[idx], col, alpha);
                }
            }
            _groundDirty = true;
        }

        private void StampScorchMark(Vector2 worldPos, int radiusPx, float alpha)
        {
            if (MultiplayerSessionConnection.Online)
            {
                AddScorchDecal(worldPos, radiusPx / PPU, alpha);
                return;
            }
            int cx = Mathf.RoundToInt(worldPos.x * PPU);
            int cy = Mathf.RoundToInt(worldPos.y * PPU);
            Color32 dark = new Color32(42, 32, 22, 255);
            for (int dy = -radiusPx; dy <= radiusPx; dy++)
            {
                int py = cy + dy;
                if (py < BorderPadPx || py >= WorldHeightPx - BorderPadPx) continue;
                for (int dx = -radiusPx; dx <= radiusPx; dx++)
                {
                    int px = cx + dx;
                    if (px < BorderPadPx || px >= WorldWidthPx - BorderPadPx) continue;
                    if (dx * dx + dy * dy <= radiusPx * radiusPx)
                    {
                        int idx = py * WorldWidthPx + px;
                        _groundPixels[idx] = Color32.Lerp(_groundPixels[idx], dark, alpha);
                    }
                }
            }
            _groundDirty = true;
        }

        private bool HasLineOfSight(Vector2 a, Vector2 b)
        {
            const int steps = 26;
            for (int i = 1; i < steps; i++)
            {
                Vector2 pt = Vector2.Lerp(a, b, (float)i / steps);
                for (int pi = 0; pi < _props.Count; pi++)
                    if (_props[pi] != null && _props[pi].IsSolidAt(pt)) return false;
                for (int si = 0; si < _smokeClouds.Count; si++)
                    if (Vector2.Distance(pt, _smokeClouds[si].pos) < _smokeClouds[si].radius) return false;
            }
            return true;
        }

        private void SpawnDebrisParticle(Vector2 pos, Color col)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float spd = Random.Range(1.0f, 3.6f);
            SpawnParticle(
                pos,
                new Vector2(Mathf.Cos(ang) * spd, Mathf.Sin(ang) * spd + Random.Range(1.0f, 2.2f)),
                gravity: -6.0f,
                life: Random.Range(0.35f, 0.78f),
                size: Random.value < 0.6f ? 0.14f : 0.08f,
                color: col
            );
        }

        private void SpawnRicochetSparks(Vector2 pos, Vector2 normal)
        {
            float baseAng = Mathf.Atan2(normal.y, normal.x);
            for (int i = 0; i < 12; i++)
            {
                float ang = baseAng + Random.Range(-0.7f, 0.7f);
                float spd = Random.Range(2.2f, 5.2f);
                Color c = i < 5 ? new Color(1f, 0.98f, 0.76f) : new Color(1f, 0.68f, 0.22f);
                SpawnParticle(pos, new Vector2(Mathf.Cos(ang) * spd, Mathf.Sin(ang) * spd + 0.5f), -2.5f, Random.Range(0.14f, 0.30f), i < 5 ? 0.14f : 0.08f, c, i < 5 ? 3.8f : 2.6f);
            }
        }

        private void SpawnExplosion(Vector2 visualPos, Vector2 groundPos, bool big)
        {
            int count = big ? 50 : 18;
            for (int i = 0; i < count; i++)
            {
                float ang = Random.Range(0f, Mathf.PI * 2f);
                float spd = Random.Range(big ? 1.4f : 1.0f, big ? 6.0f : 3.2f);
                bool isSmoke = i >= count * 0.76f;
                Color c =
                    i < count * 0.25f ? new Color(1f, 0.97f, 0.72f) :
                    i < count * 0.55f ? new Color(1f, 0.49f, 0.15f) :
                    !isSmoke ? new Color(0.85f, 0.24f, 0.13f) :
                    new Color(0.24f, 0.22f, 0.20f, 0.72f);
                float hdr =
                    i < count * 0.25f ? 3.8f :
                    i < count * 0.55f ? 2.6f :
                    !isSmoke ? 1.6f : 1.0f;
                SpawnParticle(
                    visualPos + Random.insideUnitCircle * (big ? 0.5f : 0.18f),
                    new Vector2(Mathf.Cos(ang) * spd, Mathf.Sin(ang) * spd + (big ? 1.2f : 0.5f)),
                    isSmoke ? 0.2f : -2.2f,
                    Random.Range(big ? 0.35f : 0.18f, big ? 0.92f : 0.44f),
                    isSmoke ? (big ? Random.Range(0.75f, 1.25f) : Random.Range(0.45f, 0.70f)) : (big ? Random.Range(0.18f, 0.28f) : 0.14f),
                    c,
                    hdr,
                    isSmoke ? _smokePuffSprite : _whitePxSprite
                );
            }
            if (_postProcess != null) _postProcess.PulseExplosion(big);
            StampScorchMark(groundPos, big ? 13 : 5, big ? 0.52f : 0.30f);
        }

        private void SpawnParticle(
            Vector2 pos,
            Vector2 vel,
            float gravity,
            float life,
            float size,
            Color color,
            float emissionIntensity = 1.0f,
            Sprite customSprite = null,
            int sortingOrder = 16000,
            bool expandAndFade = false,
            float endScaleMul = 0.3f,
            float drag = 0.965f,
            float zPos = -2.5f)
        {
            var sr = CreateEffect("Particle");
            var go = sr.gameObject;
            go.transform.position = new Vector3(pos.x, pos.y, zPos);
            float initScale = expandAndFade ? size * 0.72f : size;
            go.transform.localScale = new Vector3(initScale, initScale, 1f);
            sr.sprite = customSprite != null ? customSprite : _whitePxSprite;
            sr.color = color;
            sr.sortingOrder = sortingOrder;
            if (_emissiveFxMat != null)
            {
                sr.sharedMaterial = _emissiveFxMat;
                _fxMpb.SetFloat(EmissionIntensityId, emissionIntensity);
                sr.SetPropertyBlock(_fxMpb);
            }

            _particles.Add(new ParticleData
            {
                pos = pos, vel = vel, gravity = gravity,
                life = life, maxLife = life, baseSize = size,
                baseColor = color, expandAndFade = expandAndFade,
                endScaleMul = endScaleMul, drag = drag, zPos = zPos,
                go = go, sr = sr
            });
        }

        private void UpdateParticles(float dt)
        {
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var pt = _particles[i];
                pt.life -= dt;
                if (pt.life <= 0f)
                {
                    if (pt.go != null) Destroy(pt.go);
                    _particles.RemoveAt(i);
                    continue;
                }
                pt.vel += SpriteStackCamera.CamUp * (pt.gravity * dt);
                pt.vel *= Mathf.Pow(pt.drag > 0f ? pt.drag : 0.965f, dt * 60f);
                pt.pos += pt.vel * dt;
                pt.go.transform.position = new Vector3(pt.pos.x, pt.pos.y, pt.zPos);

                float remain01 = Mathf.Clamp01(pt.life / Mathf.Max(0.001f, pt.maxLife));
                if (pt.expandAndFade)
                {
                    float t01 = 1f - remain01;
                    float sc = pt.baseSize * Mathf.Lerp(0.72f, pt.endScaleMul, Mathf.Sqrt(t01));
                    pt.go.transform.localScale = new Vector3(sc, sc, 1f);
                    if (pt.sr != null)
                    {
                        float fadeIn = Mathf.Clamp01(t01 / 0.14f);
                        float fadeOut = Mathf.Pow(remain01, 1.35f);
                        Color c = pt.baseColor;
                        c.a = pt.baseColor.a * fadeIn * fadeOut;
                        pt.sr.color = c;
                    }
                }
                else
                {
                    float sc = pt.baseSize * (remain01 + pt.endScaleMul);
                    pt.go.transform.localScale = new Vector3(sc, sc, 1f);
                }
            }
        }

        private void AddPopup(Vector2 worldPos, string text, Color color)
        {
            if (_hud != null) _hud.SpawnPopup(worldPos, text, color);
        }

        private void UpdateCameraRoll(TankEntity player, float dt)
        {
            if (cameraMode == BattleCameraMode.FixedTop)
            {
                _camRollDeg = Mathf.LerpAngle(_camRollDeg, 0f, Mathf.Clamp01(Time.unscaledDeltaTime * 8.5f));
            }
            else if (player != null && player.alive && _phase == GamePhase.Playing)
            {
                float targetRoll = cameraMode == BattleCameraMode.TurretLock
                    ? (player.turretAngleDeg - 180f)
                    : (player.hullAngleDeg - 180f);
                float err = Mathf.DeltaAngle(_camRollDeg, targetRoll);
                if (cameraMode == BattleCameraMode.TurretLock)
                {
                    // Tiny 3-deg stability deadzone around 12 o'clock so fine weakspot aiming stays rock-solid,
                    // while swinging the turret rotates the 3D sprite-stacked world smoothly behind the gun!
                    if (Mathf.Abs(err) > 3.0f)
                    {
                        float excess = Mathf.Sign(err) * (Mathf.Abs(err) - 3.0f);
                        float maxStep = Mathf.Clamp(Mathf.Abs(excess) * 4.2f, 20f, 175f) * dt;
                        _camRollDeg = Mathf.Repeat(_camRollDeg + Mathf.Clamp(excess, -maxStep, maxStep), 360f);
                    }
                }
                else
                {
                    _camRollDeg = Mathf.LerpAngle(_camRollDeg, targetRoll, Mathf.Clamp01(dt * 7.5f));
                }
            }

            SpriteStackCamera.SetRoll(_camRollDeg);
            if (_cam != null)
                _cam.transform.rotation = Quaternion.Euler(0f, 0f, _camRollDeg);
        }

        private void UpdateCameraAndAim(TankEntity player, float dt)
        {
            if (_cam == null) return;

            float effectiveZoom = Mathf.Clamp(cameraZoom + _killZoomBoost, 0.7f, 1.85f);
            float orthoSize = ((renderHeight / PPU) * 0.5f) / effectiveZoom;
            _cam.orthographicSize = orthoSize;

            Vector2 mouseW = GetMouseWorld2D();
            Vector2 targetCam = _camPos;
            float camSmooth;
            if (_phase == GamePhase.RoundOver)
            {
                targetCam = _killFocusPos;
                camSmooth = Time.unscaledDeltaTime * 3.4f;
            }
            else if (player != null && player.alive)
            {
                Vector2 lookAhead = Vector2.ClampMagnitude((mouseW - player.Position2D) * 0.26f, 3.4f);
                targetCam = player.Position2D + lookAhead;
                camSmooth = dt * 6.5f;
            }
            else
            {
                // Spectate a surviving allied tank in 5v5 / 2v2 if player was knocked out!
                TankEntity spec = null;
                foreach (var t in _tanks)
                    if (t != null && t.alive && t.factionTeam == "ally") { spec = t; break; }
                if (spec != null) targetCam = spec.Position2D;
                camSmooth = dt * 3.8f;
            }
            _camPos = Vector2.Lerp(_camPos, targetCam, Mathf.Clamp01(camSmooth));

            float aspect = (float)renderWidth / Mathf.Max(1, renderHeight);
            float halfH = orthoSize;
            float halfW = orthoSize * aspect;
            float clampX = halfW;
            float clampY = halfH;
            _camPos.x = Mathf.Clamp(_camPos.x, clampX, Mathf.Max(clampX, WorldW - clampX));
            _camPos.y = Mathf.Clamp(_camPos.y, clampY, Mathf.Max(clampY, WorldH - clampY));

            Vector2 springAcc = -_camKickPos * 260f - _camKickVel * 28f;
            _camKickVel += springAcc * dt;
            _camKickPos += _camKickVel * dt;
            _camKickPos = Vector2.ClampMagnitude(_camKickPos, 0.55f);

            Vector2 shakeOff = _shake > 0f ? Random.insideUnitCircle * _shake : Vector2.zero;
            Vector2 totalOff = _camKickPos + shakeOff;
            float snapPpu = PPU * (_windowbox != null ? _windowbox.GameRenderScale : 1);
            float camX = Mathf.Round((_camPos.x + totalOff.x) * snapPpu) / snapPpu;
            float camY = Mathf.Round((_camPos.y + totalOff.y) * snapPpu) / snapPpu;
            _cam.transform.position = new Vector3(camX, camY, -20f);
            _cam.transform.rotation = Quaternion.Euler(0f, 0f, _camRollDeg);

            if (player != null && player.alive && _phase == GamePhase.Playing)
            {
                _aimLine.enabled = true;
                _reticleSr.enabled = true;
                player.GetMuzzlePositions(squash, spacingPx, out Vector2 visMuz, out Vector2 gndMuz);
                Vector2 td = TankEntity.SliceAngleToDir(player.turretAngleDeg);
                Vector2 dir = SpriteStackCamera.Squash(td, squash).normalized;
                Vector2 zLiftVec = visMuz - gndMuz;

                Vector2 endGnd = gndMuz;
                for (int i = 0; i < 90; i++)
                {
                    Vector2 next = endGnd + dir * 0.22f;
                    if (next.x <= BorderPad || next.x >= WorldW - BorderPad || next.y <= BorderPad || next.y >= WorldH - BorderPad)
                        break;
                    bool blocked = false;
                    for (int pi = 0; pi < _props.Count; pi++)
                        if (_props[pi] != null && _props[pi].IsSolidAt(next)) { blocked = true; break; }
                    if (blocked) break;
                    endGnd = next;
                }

                _aimLine.SetPosition(0, new Vector3(visMuz.x, visMuz.y, -4f));
                _aimLine.SetPosition(1, new Vector3(endGnd.x + zLiftVec.x, endGnd.y + zLiftVec.y, -4f));

                _reticleTf.position = new Vector3(mouseW.x, mouseW.y, -4f);
                _reticleTf.rotation = Quaternion.identity;
                _reticleSr.color = !player.CanFire ? new Color(1f, 0.36f, 0.30f) :
                    player.reloadTimer > 0f ? new Color(0.95f, 0.66f, 0.31f) : new Color(0.61f, 1f, 0.43f);
            }
            else
            {
                _aimLine.enabled = false;
                _reticleSr.enabled = false;
            }
        }

        private void PlaySFXAt(
            AudioClip clip,
            Vector2 worldPos,
            SoundBus bus,
            float volume,
            bool centerPan = false,
            float pitchMin = 0.94f,
            float pitchMax = 1.06f,
            float maxDuration = 0f,
            int maxConcurrentForClip = 0,
            float minRetriggerSec = 0f)
        {
            if (_audio == null) return;
            _audio.PlayAt(
                clip,
                worldPos,
                GetAudioListenerPos(),
                bus,
                volume,
                sfxEnabled,
                pitchMin,
                pitchMax,
                centerPan,
                sfxVolume,
                maxDuration,
                maxConcurrentForClip,
                minRetriggerSec
            );
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
            Time.timeScale = 1f;
        }
    }
}
