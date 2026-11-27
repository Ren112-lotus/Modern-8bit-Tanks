using System.Collections.Generic;
using UnityEngine;

namespace Modern8bitTanks
{
    public partial class BattleSceneController
    {
        private readonly List<ParticleData> _particles = new();
        private readonly List<SmokeCloud> _smokeClouds = new();
        private readonly List<RadarSmokeInfo> _radarSmokes = new();

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

        private SpriteRenderer CreateEffect(string effectName)
        {
            var sprite = Instantiate(spawnAssets.SpriteEffect, _fxRoot, false);
            sprite.name = effectName;
            return sprite;
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

        private void DeploySmokeScreen(TankEntity t)
        {
            if (!t.alive || t.smokeCooldown > 0f || _phase != GamePhase.Playing) return;
            if (_multiplayer.IsInBattle)
                _multiplayer.Transport?.BroadcastSmoke(System.Array.IndexOf(_networkTanks, t));
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
            NotifyVoiceSmokeDeployed(t);
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
                if (_multiplayer.IsInBattle)
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
                if (py < BorderPadPx || py >= MapHeightPx - BorderPadPx) continue;
                for (int dx = 0; dx < w; dx++)
                {
                    int px = cx + dx;
                    if (px < BorderPadPx || px >= MapWidthPx - BorderPadPx) continue;
                    int idx = py * MapWidthPx + px;
                    _groundPixels[idx] = alpha >= 0.99f ? col : Color32.Lerp(_groundPixels[idx], col, alpha);
                }
            }
            _groundDirty = true;
        }

        private void StampScorchMark(Vector2 worldPos, int radiusPx, float alpha)
        {
            if (_multiplayer.IsInBattle)
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
                if (py < BorderPadPx || py >= MapHeightPx - BorderPadPx) continue;
                for (int dx = -radiusPx; dx <= radiusPx; dx++)
                {
                    int px = cx + dx;
                    if (px < BorderPadPx || px >= MapWidthPx - BorderPadPx) continue;
                    if (dx * dx + dy * dy <= radiusPx * radiusPx)
                    {
                        int idx = py * MapWidthPx + px;
                        _groundPixels[idx] = Color32.Lerp(_groundPixels[idx], dark, alpha);
                    }
                }
            }
            _groundDirty = true;
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
    }
}
