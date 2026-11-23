using UnityEngine;
using UnityEngine.InputSystem;

namespace Modern8bitTanks
{
    public partial class BattleSceneController
    {
        private TankEntity[] _networkTanks;
        private Vector2[] _networkSpawns;
        private const int SnapshotHistorySize = 12;
        private const float InterpolationDelay = MultiplayerMatchTransport.SnapshotInterval * 2f;
        private readonly MultiplayerMatchTransport.TankState[] _outgoingTanks = new MultiplayerMatchTransport.TankState[MultiplayerSessionConnection.MaxPlayers];
        private readonly MultiplayerMatchTransport.ShotState[] _outgoingProjectiles = new MultiplayerMatchTransport.ShotState[MultiplayerMatchTransport.MaxProjectiles];
        private readonly VehiclePose[,] _poseHistory = new VehiclePose[SnapshotHistorySize, MultiplayerSessionConnection.MaxPlayers];
        private readonly float[] _poseTimes = new float[SnapshotHistorySize];
        private readonly (float time, VehiclePose pose)[] _ownerHistory = new (float, VehiclePose)[128];
        private int _poseIndex, _poseCount, _ownerIndex, _ownerCount;
        private float _clockOffset, _lastRenderTime;
        private Vector2 _ownerPositionError;
        private float _ownerHullError, _ownerTurretError;
        private float _networkSnapshotTimer;

        private struct VehiclePose
        {
            public Vector2 Position;
            public float HullAngle;
            public float TurretAngle;

            public VehiclePose(Vector2 position, float hullAngle, float turretAngle)
            {
                Position = position;
                HullAngle = hullAngle;
                TurretAngle = turretAngle;
            }

            public VehiclePose(TankEntity tank) : this(tank.Position2D, tank.hullAngleDeg, tank.turretAngleDeg) { }

            public static VehiclePose Interpolate(VehiclePose from, VehiclePose to, float blend) =>
                new(Vector2.Lerp(from.Position, to.Position, blend), Mathf.LerpAngle(from.HullAngle, to.HullAngle, blend),
                    Mathf.LerpAngle(from.TurretAngle, to.TurretAngle, blend));

            public void Apply(TankEntity tank)
            {
                tank.Position2D = Position;
                tank.hullAngleDeg = HullAngle;
                tank.turretAngleDeg = TurretAngle;
            }
        }
        private sealed class RemoteShot
        {
            public SpriteRenderer Sprite;
            public bool Active, Seen;
            public MultiplayerMatchTransport.ShotState State;
            public Vector2 DisplayPosition;
            public float TrailTimer;
        }

        private readonly RemoteShot[] _networkProjectiles = new RemoteShot[MultiplayerMatchTransport.MaxProjectiles];
        private float _shotSnapshotTime;
        private ushort _nextProjectileId;
        private readonly SpriteRenderer[] _onlineTreads = new SpriteRenderer[192];
        private int _nextTread;
        private bool _networkResultShown;
        private bool _networkBattleStarted;
        private MultiplayerMatchTransport _subscribedTransport;

        private bool MultiplayerBattleReady()
        {
            if (!_networkBattleStarted && _multiplayer.Transport != null && _multiplayer.Transport.AllExpectedReady)
            {
                var connection = _multiplayer.Connection;
                if (connection.Room.CountTeam(false) == 0)
                {
                    _ = connection.ReturnToRoomAsync();
                    return false;
                }
                _networkBattleStarted = true;
                BeginBattle();
            }
            return _networkBattleStarted;
        }

        private void UpdateMultiplayerWaiting(float dt)
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                _hud?.HandleEscapeKey();
            RenderBattleFrame(GetPlayerTank(), dt);
        }

        private void SpawnMultiplayerTanks()
        {
            if (_playerTank != null) _playerTank.gameObject.SetActive(false);
            if (_primaryEnemyTank != null) _primaryEnemyTank.gameObject.SetActive(false);
            var room = _multiplayer.Connection.Room;
            _networkTanks = new TankEntity[MultiplayerSessionConnection.MaxPlayers];
            _networkSpawns = new Vector2[_networkTanks.Length];
            for (var slot = 0; slot < _networkTanks.Length; slot++)
            {
                var vehicleId = room.GetVehicleId(slot);
                if (vehicleId == null) continue;
                var alpha = slot < 4;
                var row = slot % 4;
                var spawn = BattlefieldSpawn(alpha, row);
                _networkSpawns[slot] = spawn;
                var tank = SpawnTankFromPrefab(GetVehicleDef(vehicleId),
                    alpha ? "ally" : "enemy", spawn, alpha ? 90f : -90f, slot == _multiplayer.LocalSlot);
                _networkTanks[slot] = tank;
                if (tank == null) continue;
                tank.alive = false;
                tank.gameObject.SetActive(false);
            }
        }

        private void InitializeMultiplayerBattle()
        {
            var transport = _multiplayer.Transport;
            if (!_multiplayer.IsInBattle || transport == null) return;
            _subscribedTransport = transport;
            transport.StateReceived += ApplyMultiplayerState;
            transport.PropImpactReceived += ApplyMultiplayerPropImpact;
            transport.SmokeReceived += ApplyMultiplayerSmoke;
            transport.WeaponFired += ApplyMultiplayerWeaponEffects;
            transport.EffectReceived += ReceiveBattleEffect;
            if (transport.IsHost) UpdateMultiplayerRoster();
        }

        private void UpdateMultiplayerRoster()
        {
            var transport = _multiplayer.Transport;
            if (_networkTanks == null || transport == null || !transport.IsHost) return;
            for (var i = 0; i < _networkTanks.Length; i++)
            {
                var tank = _networkTanks[i];
                if (tank == null || tank.gameObject.activeSelf == transport.IsOccupied(i)) continue;
                if (transport.IsOccupied(i))
                {
                    tank.gameObject.SetActive(true);
                    tank.CaptureSpawnAndReset(_networkSpawns[i], i < 4 ? 90f : -90f);
                }
                else
                {
                    tank.alive = false;
                    tank.gameObject.SetActive(false);
                    if (_networkBattleStarted && _phase == GamePhase.Playing) CheckTeamVictoryOrWave(tank);
                }
            }
        }

        private static MultiplayerMatchTransport.Control PackControl(TankControlInput input)
        {
            byte flags = 0;
            if (input.Up) flags |= 1; if (input.Down) flags |= 2;
            if (input.Left) flags |= 4; if (input.Right) flags |= 8;
            if (input.FirePrimary) flags |= 16; if (input.FireSecondary) flags |= 32;
            if (input.Smoke) flags |= 64; if (input.Repair) flags |= 128;
            return new MultiplayerMatchTransport.Control { Buttons = flags, Arcade = (byte)(input.Arcade ? 1 : 0),
                Ammo = (sbyte)input.AmmoSlot, AimX = input.AimWorld.x, AimY = input.AimWorld.y };
        }

        private static TankControlInput UnpackControl(MultiplayerMatchTransport.Control input)
        {
            return new TankControlInput { Up = (input.Buttons & 1) != 0, Down = (input.Buttons & 2) != 0,
                Left = (input.Buttons & 4) != 0, Right = (input.Buttons & 8) != 0,
                FirePrimary = (input.Buttons & 16) != 0, FireSecondary = (input.Buttons & 32) != 0,
                Smoke = (input.Buttons & 64) != 0, Repair = (input.Buttons & 128) != 0,
                Arcade = input.Arcade != 0, AmmoSlot = input.Ammo, AimWorld = new Vector2(input.AimX, input.AimY) };
        }

        private void ApplyMultiplayerControls(float dt)
        {
            var transport = _multiplayer.Transport;
            if (_networkTanks == null || transport == null || !transport.IsHost) return;
            for (var i = 0; i < _networkTanks.Length; i++)
                if (i != _multiplayer.LocalSlot && _networkTanks[i] != null && transport.TryConsumeControl(i, out var input))
                    ApplyTankControl(_networkTanks[i], dt, UnpackControl(input));
        }

        private void BroadcastMultiplayerState()
        {
            var transport = _multiplayer.Transport;
            if (_networkTanks == null || transport == null || !transport.IsHost) return;
            _networkSnapshotTimer -= Time.unscaledDeltaTime;
            if (_networkSnapshotTimer > 0f) return;
            _networkSnapshotTimer += MultiplayerMatchTransport.SnapshotInterval;
            if (_networkSnapshotTimer <= 0f) _networkSnapshotTimer = MultiplayerMatchTransport.SnapshotInterval;
            var states = _outgoingTanks;
            for (var i = 0; i < states.Length; i++)
            {
                var t = _networkTanks[i];
                if (t == null || !transport.IsOccupied(i))
                {
                    states[i].Present = false;
                    continue;
                }
                var p = t.Position2D;
                states[i] = new MultiplayerMatchTransport.TankState { Present = true, Alive = t.alive,
                    X = p.x, Y = p.y, Hull = t.hullAngleDeg, Turret = t.turretAngleDeg,
                    MoveSpeed = t.moveSpeed, Reload = t.reloadTimer, FireTime = t.FireTime, RepairTime = t.RepairTime,
                    ActiveReload = t.ActiveReloadDuration, Crew = (int)t.ActiveCrew, Modules = (int)t.DisabledModules,
                    Ammo = (byte)t.selectedAmmoSlot, Loss = (byte)t.Loss, Extinguisher = t.ExtinguisherReady };
            }
            var shots = _outgoingProjectiles;
            var count = Mathf.Min(_projectiles.Count, shots.Length);
            for (var i = 0; i < count; i++)
            {
                var shot = _projectiles[i];
                shots[i] = new MultiplayerMatchTransport.ShotState { Id = shot.networkId, Position = shot.visualPos,
                    Velocity = shot.vel, Kind = shot.kind, Ammo = shot.ammo };
            }
            byte result = _phase == GamePhase.RoundOver ? (_pendingWinner == "ally" ? (byte)1 : (byte)2) : _networkBattleStarted ? (byte)3 : (byte)0;
            UpdateRadarContacts();
            transport.Broadcast(states, shots, count, result, _visibility.NetworkContacts, _killFocusPos, ControlPoint, ControlScores, _wonByControl);
        }

        private void ApplyMultiplayerState(MultiplayerMatchTransport.TankState[] states, MultiplayerMatchTransport.ShotState[] shots,
            int shotCount, byte result, float hostTime, byte[] spots, Vector2 focus,
            ControlPointState controlPoint, Vector2 controlScores, bool wonByControl)
        {
            if (_networkTanks == null || states.Length != _networkTanks.Length) return;
            System.Array.Copy(spots, _visibility.NetworkContacts, _visibility.NetworkContacts.Length);
            _controlPoints.ApplySnapshot(controlPoint, controlScores);
            SyncControlPointVoice();
            float now = Time.unscaledTime;
            float sampleOffset = now - hostTime;
            _clockOffset = _poseCount == 0 ? sampleOffset : Mathf.Min(sampleOffset, _clockOffset + MultiplayerMatchTransport.SnapshotInterval * .02f);
            _poseTimes[_poseIndex] = hostTime;
            for (var i = 0; i < states.Length; i++)
            {
                var tank = _networkTanks[i];
                if (tank == null) continue;
                if (!states[i].Present)
                {
                    if (tank.gameObject.activeSelf) tank.gameObject.SetActive(false);
                    tank.alive = false;
                    continue;
                }

                var pose = new VehiclePose(new Vector2(states[i].X, states[i].Y), states[i].Hull, states[i].Turret);
                _poseHistory[_poseIndex, i] = pose;
                bool spawning = !tank.gameObject.activeSelf;
                bool killed = !spawning && tank.alive && !states[i].Alive;
                int oldAmmo = tank.selectedAmmoSlot;
                tank.ApplyRemoteCombatState(states[i], applySpeed: !tank.isPlayer || spawning || !states[i].Alive);
                if (killed) pose.Apply(tank);
                if (spawning)
                {
                    pose.Apply(tank);
                    tank.gameObject.SetActive(true);
                    if (tank.isPlayer) _camPos = pose.Position;
                }
                else if (tank.isPlayer && tank.alive)
                {
                    CorrectPredictedPose(tank, pose, now);
                    if (tank.selectedAmmoSlot != oldAmmo) ShowAmmoSelection(tank);
                }
            }
            _poseIndex = (_poseIndex + 1) % SnapshotHistorySize;
            _poseCount = Mathf.Min(_poseCount + 1, SnapshotHistorySize);
            ApplyMultiplayerProjectiles(shots, shotCount, hostTime);
            if (result != 0 && !_networkBattleStarted)
            {
                _networkBattleStarted = true;
                BeginBattle();
            }
            if ((result == 1 || result == 2) && !_networkResultShown)
            {
                _networkResultShown = true;
                FinishRound(result == 1 ? "ally" : "enemy", wonByControl ? ControlVictorySubtitle : "ONLINE BATTLE",
                    focus, wonByControl);
            }
        }

        private void ApplyMultiplayerProjectiles(MultiplayerMatchTransport.ShotState[] states, int count, float hostTime)
        {
            _shotSnapshotTime = hostTime;
            foreach (var shot in _networkProjectiles)
            {
                if (shot == null || !shot.Active) continue;
                shot.Seen = false;
                for (int i = 0; i < count; i++)
                {
                    if (states[i].Id != shot.State.Id) continue;
                    shot.State = states[i];
                    shot.Seen = true;
                    break;
                }
                if (shot.Seen) continue;
                shot.Active = false;
                shot.Sprite.enabled = false;
            }
            for (int i = 0; i < count; i++)
            {
                var state = states[i];
                int freeSlot = -1;
                bool exists = false;
                for (int j = 0; j < _networkProjectiles.Length; j++)
                {
                    var shot = _networkProjectiles[j];
                    if (shot == null || !shot.Active)
                    {
                        if (freeSlot < 0) freeSlot = j;
                        continue;
                    }
                    if (shot.State.Id != state.Id) continue;
                    exists = true;
                    break;
                }
                if (exists || freeSlot < 0) continue;
                var entry = _networkProjectiles[freeSlot];
                if (entry == null)
                {
                    var sprite = CreateEffect("Remote projectile");
                    sprite.sprite = _whitePxSprite;
                    _networkProjectiles[freeSlot] = entry = new RemoteShot { Sprite = sprite };
                }
                entry.State = state;
                entry.DisplayPosition = state.Position;
                entry.TrailTimer = 0f;
                entry.Active = true;
                SetProjectileAppearance(entry.Sprite, state.Ammo);
                entry.Sprite.enabled = true;
            }
        }

        private void RenderGuestProjectiles(float dt)
        {
            float elapsed = Mathf.Clamp(Time.unscaledTime - _clockOffset - _shotSnapshotTime, 0f, InterpolationDelay);
            Vector2 listener = GetAudioListenerPos();
            Vector2 nearestMissile = listener;
            float nearestDistance = float.PositiveInfinity;
            float correction = 1f - Mathf.Exp(-20f * dt);
            foreach (var shot in _networkProjectiles)
            {
                if (shot == null || !shot.Active) continue;
                var state = shot.State;
                var position = Vector2.Lerp(shot.DisplayPosition + state.Velocity * dt, state.Position + state.Velocity * elapsed, correction);
                shot.DisplayPosition = position;
                shot.Sprite.transform.SetPositionAndRotation(new Vector3(position.x, position.y, -2f),
                    Quaternion.Euler(0f, 0f, Mathf.Atan2(state.Velocity.y, state.Velocity.x) * Mathf.Rad2Deg));
                shot.TrailTimer -= dt;
                if (shot.TrailTimer <= 0f && state.Kind != ProjectileKind.MG && _particles.Count < 420)
                {
                    shot.TrailTimer = 1f / 40f;
                    PlayProjectileTrail(state.Kind, state.Ammo, position, state.Velocity.normalized);
                }
                if (state.Kind != ProjectileKind.ATGM) continue;
                float distance = (position - listener).sqrMagnitude;
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearestMissile = position;
            }
            _audio?.UpdateMissileMotorLoop(nearestDistance < float.PositiveInfinity && _phase == GamePhase.Playing,
                nearestMissile, listener, sfxEnabled, dt, sfxVolume);
        }

        private void ApplyMultiplayerWeaponEffects(int slot, ProjectileKind kind, AmmoType ammo, Vector2 muzzle, Vector2 ground, Vector2 dir)
        {
            var tank = _networkTanks?[slot];
            if (tank == null) return;
            if (tank.isPlayer)
            {
                tank.GetMuzzlePositions(squash, spacingPx, out Vector2 localMuzzle, out Vector2 localGround);
                muzzle = localMuzzle;
                ground = localGround;
            }
            float kick = kind switch
            {
                ProjectileKind.Shell => ammo == AmmoType.HE ? 4.4f : 3.8f,
                ProjectileKind.Autocannon => 1.6f,
                ProjectileKind.ATGM => ammo == AmmoType.GLATGM ? 3.2f : 2f,
                _ => 0f
            };
            if (kick > 0f) tank.TriggerFireKick(kick);
            PresentWeaponEffects(tank, kind, ammo, muzzle, ground, dir);
        }

        private void PresentWeaponEffects(TankEntity tank, ProjectileKind kind, AmmoType ammo, Vector2 muzzle, Vector2 ground, Vector2 dir)
        {
            switch (kind)
            {
                case ProjectileKind.Shell:
                    PlayMainCannonEffects(tank, muzzle, ground, dir);
                    break;
                case ProjectileKind.Autocannon:
                    PlayAutocannonEffects(tank, muzzle, ground, dir);
                    break;
                case ProjectileKind.ATGM:
                    PlayAtgmEffects(tank, ammo, muzzle, ground, dir);
                    break;
                case ProjectileKind.MG:
                    PlayMgEffects(tank, muzzle, ground, dir);
                    break;
            }
        }

        private void CorrectPredictedPose(TankEntity tank, VehiclePose authoritative, float now)
        {
            if (_ownerCount == 0) return;
            // A snapshot reflects input promoted on the host one round trip earlier.
            float sampleTime = now - Mathf.Clamp(_multiplayer.Transport.RoundTripSeconds + 0.025f, .04f, .45f);
            int newest = (_ownerIndex + _ownerHistory.Length - 1) % _ownerHistory.Length;
            var recent = _ownerHistory[newest];
            var past = recent;
            for (int age = 1; age < _ownerCount && past.time > sampleTime; age++)
            {
                recent = past;
                past = _ownerHistory[(newest + _ownerHistory.Length - age) % _ownerHistory.Length];
            }
            var sampled = VehiclePose.Interpolate(past.pose, recent.pose, Mathf.InverseLerp(past.time, recent.time, sampleTime));
            _ownerPositionError = authoritative.Position - sampled.Position;
            if (_ownerPositionError.sqrMagnitude > 4f)
            {
                authoritative.Apply(tank);
                _ownerCount = 0;
                _ownerPositionError = Vector2.zero;
            }
            _ownerHullError = Mathf.DeltaAngle(sampled.HullAngle, authoritative.HullAngle);
            _ownerTurretError = Mathf.DeltaAngle(sampled.TurretAngle, authoritative.TurretAngle);
        }

        private void PredictLocalTank(TankEntity tank, TankControlInput input, float dt)
        {
            if (tank == null || !tank.gameObject.activeSelf || !tank.alive || !_networkBattleStarted || _phase != GamePhase.Playing) return;
            ApplyTankMotion(tank, dt, input);
            float correction = 1f - Mathf.Exp(-6f * dt);
            Vector2 offset = _ownerPositionError * correction;
            tank.Position2D += offset;
            _ownerPositionError -= offset;
            tank.hullAngleDeg += _ownerHullError * correction;
            tank.turretAngleDeg = LimitGunTraverse(tank, tank.turretAngleDeg + _ownerTurretError * correction);
            _ownerHullError *= 1f - correction;
            _ownerTurretError *= 1f - correction;
            ResolveTankCollisions(tank);
            _ownerHistory[_ownerIndex] = (Time.unscaledTime, new VehiclePose(tank));
            _ownerIndex = (_ownerIndex + 1) % _ownerHistory.Length;
            _ownerCount = Mathf.Min(_ownerCount + 1, _ownerHistory.Length);
        }

        private void AddOnlineTread(Vector2 position, Color color)
        {
            var sprite = _onlineTreads[_nextTread];
            if (sprite == null)
            {
                sprite = CreateEffect("Track mark");
                sprite.transform.localScale = new Vector3(2f / PPU, 2f / PPU, 1f);
                sprite.sprite = _whitePxSprite;
                sprite.sortingOrder = -29999;
                _onlineTreads[_nextTread] = sprite;
            }
            _nextTread = (_nextTread + 1) % _onlineTreads.Length;
            color.a = .36f;
            sprite.color = color;
            sprite.transform.position = new Vector3(Mathf.Round(position.x * PPU) / PPU,
                Mathf.Round(position.y * PPU) / PPU, 4f);
        }

        private void InterpolateRemoteTanks()
        {
            if (_poseCount == 0) return;
            int newer = (_poseIndex + SnapshotHistorySize - 1) % SnapshotHistorySize;
            int older = newer;
            _lastRenderTime = Mathf.Max(_lastRenderTime, Time.unscaledTime - _clockOffset - InterpolationDelay);
            for (int age = 1; age < _poseCount && _poseTimes[older] > _lastRenderTime; age++)
            {
                newer = older;
                older = (_poseIndex + SnapshotHistorySize - 1 - age) % SnapshotHistorySize;
            }
            float blend = Mathf.InverseLerp(_poseTimes[older], _poseTimes[newer], _lastRenderTime);
            for (int i = 0; i < _networkTanks.Length; i++)
            {
                var tank = _networkTanks[i];
                if (tank == null || !tank.gameObject.activeSelf) continue;
                Vector2 previous = tank.Position2D;
                if (!tank.isPlayer && tank.alive)
                {
                    VehiclePose.Interpolate(_poseHistory[older, i], _poseHistory[newer, i], blend).Apply(tank);
                    tank.treadAccum += Vector2.Distance(previous, tank.Position2D);
                }
            }
        }

        private void UpdateMultiplayerClient(float dt)
        {
            var transport = _multiplayer.Transport;
            var player = GetPlayerTank();
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) _hud?.HandleEscapeKey();
            if (keyboard != null && keyboard.hKey.wasPressedThisFrame) _hud?.CycleHudDensity();
            var menuOpen = _hud != null && _hud.IsMenuOpen;
            _audio?.UpdateBgm(active: true, bgmEnabled, bgmVolume, duckForMenu: menuOpen);

            bool sendView = _networkBattleStarted && player != null && player.alive && _phase == GamePhase.Playing;
            if (!sendView || menuOpen || !TryReadTankControl(out var input))
            {
                input = new TankControlInput { Arcade = arcadeWASD, AmmoSlot = -1 };
                if (player != null)
                    input.AimWorld = player.GetTurretPivotWorld(squash) + SpriteStackCamera.Squash(TankEntity.SliceAngleToDir(player.turretAngleDeg), squash);
            }

            UpdateRoundAftermath();
            PredictLocalTank(player, input, dt);
            InterpolateRemoteTanks();
            RenderGuestProjectiles(dt);
            RenderBattleFrame(player, dt);
            if (sendView)
            {
                var control = PackControl(input);
                control.ViewCenter = _cam.transform.position;
                control.ViewRoll = _camRollDeg;
                transport.SendControl(control);
            }
            else
                transport.StopSendingControls();
        }

        private void CleanupMultiplayerBattle()
        {
            var transport = _subscribedTransport;
            _subscribedTransport = null;
            if (transport == null) return;

            transport.StateReceived -= ApplyMultiplayerState;
            transport.PropImpactReceived -= ApplyMultiplayerPropImpact;
            transport.SmokeReceived -= ApplyMultiplayerSmoke;
            transport.WeaponFired -= ApplyMultiplayerWeaponEffects;
            transport.EffectReceived -= ReceiveBattleEffect;
        }

        private void ApplyMultiplayerPropImpact(int index, Vector2 position, float radius, bool heavy, int seed)
        {
            if (index < 0 || index >= _props.Count || _props[index] == null) return;
            var previousState = Random.state;
            Random.InitState(seed);
            _props[index].ApplyHit(position, radius, heavy, SpawnDebrisParticle);
            Random.state = previousState;
        }

        private void ApplyMultiplayerSmoke(int slot)
        {
            if (_networkTanks == null || slot < 0 || slot >= _networkTanks.Length || _networkTanks[slot] == null) return;
            _networkTanks[slot].smokeCooldown = 0f;
            DeploySmokeScreen(_networkTanks[slot]);
        }
    }
}
