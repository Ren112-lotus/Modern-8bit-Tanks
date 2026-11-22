using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Modern8bitTanks
{
    public partial class DesertBattleManager
    {
        private TankEntity[] _networkTanks;
        private Vector2[] _networkSpawns;
        private float _networkSnapshotTimer;
        private readonly List<SpriteRenderer> _networkProjectiles = new();
        private bool _networkResultShown;
        private bool _networkBattleStarted;

        private bool MultiplayerBattleReady()
        {
            if (!_networkBattleStarted && MultiplayerMatchTransport.Instance != null && MultiplayerMatchTransport.Instance.AllExpectedPresent)
            {
                _networkBattleStarted = true;
                _hud?.ShowWaveToast("BATTLE START", 2f);
            }
            return _networkBattleStarted;
        }

        private void UpdateMultiplayerWaiting(float dt)
        {
            var player = GetPlayerTank();
            if (_networkTanks != null)
                foreach (var tank in _networkTanks)
                    if (tank != null && tank.gameObject.activeSelf) tank.SyncVisuals(squash, spacingPx, onTurretBounce: OnTurretGroundBounce, onTurretTrail: OnTurretAirTrail);
            UpdateCameraAndAim(player, dt);
            PushHudUpdate(player, dt);
        }

        private void SpawnMultiplayerTanks()
        {
            if (_sceneAbrams != null) _sceneAbrams.gameObject.SetActive(false);
            if (_sceneT90 != null) _sceneT90.gameObject.SetActive(false);
            _networkTanks = new TankEntity[MultiplayerSessionConnection.MaxPlayers];
            _networkSpawns = new Vector2[_networkTanks.Length];
            for (var slot = 0; slot < _networkTanks.Length; slot++)
            {
                var alpha = slot < 4;
                var row = slot % 4;
                var spawn = new Vector2(WorldW * (alpha ? 0.08f : 0.92f), WorldH * (0.2f + row * 0.2f));
                _networkSpawns[slot] = spawn;
                var tank = SpawnExtraTank(alpha ? SelectedPlayerVehicle : SelectedEnemyVehicle,
                    alpha ? "ally" : "enemy", spawn, alpha ? 90f : -90f, slot == MultiplayerWebBootstrap.LocalSlot);
                _networkTanks[slot] = tank;
                if (tank == null) continue;
                tank.alive = false;
                tank.gameObject.SetActive(false);
            }
        }

        private void InitializeMultiplayerBattle()
        {
            var transport = MultiplayerMatchTransport.Instance;
            if (!MultiplayerWebBootstrap.Online || transport == null) return;
            transport.StateReceived += ApplyMultiplayerState;
            transport.PropImpactReceived += ApplyMultiplayerPropImpact;
            transport.SmokeReceived += ApplyMultiplayerSmoke;
            _hud?.ShowWaveToast("WAITING FOR PLAYERS", 9999f);
            transport.MarkSceneReady();
            if (transport.IsHost) UpdateMultiplayerRoster();
        }

        private void UpdateMultiplayerRoster()
        {
            var transport = MultiplayerMatchTransport.Instance;
            if (_networkTanks == null || transport == null || !transport.IsHost) return;
            for (var i = 0; i < _networkTanks.Length; i++)
            {
                var tank = _networkTanks[i];
                if (tank == null || tank.gameObject.activeSelf == transport.Occupied[i]) continue;
                if (transport.Occupied[i])
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
            var transport = MultiplayerMatchTransport.Instance;
            if (_networkTanks == null || transport == null || !transport.IsHost) return;
            for (var i = 0; i < _networkTanks.Length; i++)
                if (i != MultiplayerWebBootstrap.LocalSlot && _networkTanks[i] != null && transport.TryGetControl(i, out var input))
                    ApplyTankControl(_networkTanks[i], dt, UnpackControl(input));
        }

        private void BroadcastMultiplayerState()
        {
            var transport = MultiplayerMatchTransport.Instance;
            if (_networkTanks == null || transport == null || !transport.IsHost) return;
            _networkSnapshotTimer -= Time.unscaledDeltaTime;
            if (_networkSnapshotTimer > 0f) return;
            _networkSnapshotTimer = 0.1f;
            var states = new MultiplayerMatchTransport.TankState[_networkTanks.Length];
            for (var i = 0; i < states.Length; i++)
            {
                var t = _networkTanks[i];
                if (t == null) continue;
                var p = t.Position2D;
                states[i] = new MultiplayerMatchTransport.TankState { Present = transport.Occupied[i], Alive = t.alive,
                    X = p.x, Y = p.y, Hull = t.hullAngleDeg, Turret = t.turretAngleDeg,
                    MoveSpeed = t.moveSpeed, Reload = t.reloadTimer, FireTime = t.FireTime, RepairTime = t.RepairTime,
                    ActiveReload = t.ActiveReloadDuration, Crew = (int)t.ActiveCrew, Modules = (int)t.DisabledModules,
                    Ammo = (byte)t.selectedAmmoSlot, Loss = (byte)t.Loss, Extinguisher = t.ExtinguisherReady };
            }
            var count = Mathf.Min(_projectiles.Count, 32);
            var shots = new Vector2[count];
            for (var i = 0; i < count; i++) shots[i] = _projectiles[i].visualPos;
            byte result = _phase == GamePhase.RoundOver ? (_pendingWinner == "ally" ? (byte)1 : (byte)2) : _networkBattleStarted ? (byte)3 : (byte)0;
            transport.Broadcast(states, shots, result);
        }

        private void ApplyMultiplayerState(MultiplayerMatchTransport.TankState[] states, Vector2[] shots, byte result)
        {
            if (_networkTanks == null || states.Length != _networkTanks.Length) return;
            for (var i = 0; i < states.Length; i++)
            {
                var tank = _networkTanks[i];
                if (tank == null) continue;
                if (tank.gameObject.activeSelf != states[i].Present) tank.gameObject.SetActive(states[i].Present);
                if (!states[i].Present) { tank.alive = false; continue; }
                tank.Position2D = new Vector2(states[i].X, states[i].Y);
                tank.hullAngleDeg = states[i].Hull;
                tank.turretAngleDeg = states[i].Turret;
                tank.ApplyRemoteCombatState(states[i]);
            }
            while (_networkProjectiles.Count < shots.Length)
            {
                var go = new GameObject("Remote projectile");
                go.transform.SetParent(transform, false);
                var sprite = go.AddComponent<SpriteRenderer>();
                sprite.sprite = _whitePxSprite;
                sprite.color = new Color(1f, .78f, .34f);
                sprite.sortingOrder = 15000;
                go.transform.localScale = new Vector3(.18f, .18f, 1f);
                _networkProjectiles.Add(sprite);
            }
            for (var i = 0; i < _networkProjectiles.Count; i++)
            {
                _networkProjectiles[i].gameObject.SetActive(i < shots.Length);
                if (i < shots.Length) _networkProjectiles[i].transform.position = new Vector3(shots[i].x, shots[i].y, -2f);
            }
            if (result == 3 && !_networkBattleStarted)
            {
                _networkBattleStarted = true;
                _hud?.ShowWaveToast("BATTLE START", 2f);
            }
            if ((result == 1 || result == 2) && !_networkResultShown)
            {
                _networkResultShown = true;
                _phase = GamePhase.RoundOver;
                _hud?.ShowResult((result == 1) == (MultiplayerWebBootstrap.LocalSlot < 4), "4 VS 4");
            }
        }

        private void UpdateMultiplayerClient(float dt)
        {
            var transport = MultiplayerMatchTransport.Instance;
            var player = GetPlayerTank();
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) _hud?.HandleEscapeKey();
            var menuOpen = _hud != null && _hud.IsMenuOpen;
            _audio?.UpdateBgm(active: true, bgmEnabled, bgmVolume, duckForMenu: menuOpen);
            if (!menuOpen && _networkBattleStarted && player != null && player.alive && _phase == GamePhase.Playing && TryReadTankControl(out var input))
                transport.SendControl(PackControl(input));
            if (_networkTanks != null)
                foreach (var tank in _networkTanks)
                    if (tank != null && tank.gameObject.activeSelf) tank.SyncVisuals(squash, spacingPx, onTurretBounce: OnTurretGroundBounce, onTurretTrail: OnTurretAirTrail);
            foreach (var prop in _props) if (prop != null) prop.Tick(dt, squash, spacingPx);
            UpdateSmokeClouds(dt);
            UpdateParticles(dt);
            UpdateCameraRoll(player, dt);
            UpdateCameraAndAim(player, dt);
            UpdateMechanicalAudio(player, dt);
            PushHudUpdate(player, dt);
        }

        private void CleanupMultiplayerBattle()
        {
            if (MultiplayerMatchTransport.Instance != null)
            {
                MultiplayerMatchTransport.Instance.StateReceived -= ApplyMultiplayerState;
                MultiplayerMatchTransport.Instance.PropImpactReceived -= ApplyMultiplayerPropImpact;
                MultiplayerMatchTransport.Instance.SmokeReceived -= ApplyMultiplayerSmoke;
            }
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
