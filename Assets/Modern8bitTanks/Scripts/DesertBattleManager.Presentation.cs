using UnityEngine;

namespace Modern8bitTanks
{
    public partial class DesertBattleManager
    {
        private readonly SpriteRenderer[] _scorchDecals = new SpriteRenderer[64];
        private int _nextScorchDecal;

        private void AddScorchDecal(Vector2 position, float radius, float alpha)
        {
            var decal = _scorchDecals[_nextScorchDecal];
            if (decal == null)
            {
                var mark = new GameObject("Scorch mark");
                mark.transform.SetParent(_fxRoot, false);
                decal = mark.AddComponent<SpriteRenderer>();
                decal.sprite = _smokePuffSprite;
                decal.sortingOrder = -29998;
                _scorchDecals[_nextScorchDecal] = decal;
            }
            _nextScorchDecal = (_nextScorchDecal + 1) % _scorchDecals.Length;
            decal.color = new Color(.16f, .12f, .08f, alpha);
            decal.transform.position = new Vector3(position.x, position.y, 4f);
            decal.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
        }

        private void EmitBattleEffect(BattleEffect effect, TankEntity target = null, TankEntity attacker = null)
        {
            if (!MultiplayerSessionConnection.Online)
            {
                PresentBattleEffect(effect, target, attacker);
                return;
            }

            effect.Attacker = FindBattleSlot(attacker);
            effect.Victim = FindBattleSlot(target);
            effect.Seed = Random.Range(0, int.MaxValue);
            MultiplayerMatchTransport.Instance?.PublishEffect(effect);
            PresentSeededBattleEffect(effect, target, attacker);
        }

        private byte FindBattleSlot(TankEntity tank)
        {
            if (tank == null || _networkTanks == null) return byte.MaxValue;
            int slot = System.Array.IndexOf(_networkTanks, tank);
            return slot >= 0 ? (byte)slot : byte.MaxValue;
        }

        private void ReceiveBattleEffect(BattleEffect effect)
        {
            if (_networkTanks == null) return;
            TankEntity target = effect.Victim < _networkTanks.Length ? _networkTanks[effect.Victim] : null;
            TankEntity attacker = effect.Attacker < _networkTanks.Length ? _networkTanks[effect.Attacker] : null;
            PresentSeededBattleEffect(effect, target, attacker);
        }

        private void PresentSeededBattleEffect(BattleEffect effect, TankEntity target, TankEntity attacker)
        {
            var savedRandom = Random.state;
            Random.InitState(effect.Seed);
            try { PresentBattleEffect(effect, target, attacker); }
            finally { Random.state = savedRandom; }
        }

        private void PresentBattleEffect(BattleEffect effect, TankEntity target, TankEntity attacker)
        {
            switch (effect.Kind)
            {
                case BattleEffectKind.ArmorImpact:
                    if (target != null) PresentArmorImpact(effect, target, attacker != null && attacker.isPlayer);
                    break;
                case BattleEffectKind.Splash:
                    if (target == null) break;
                    if (target.isPlayer) _postProcess?.PulsePlayerDamage();
                    if (effect.Loss == TankLoss.None) PresentHitLabel(target, effect.Hit);
                    break;
                case BattleEffectKind.TankLoss:
                    if (target != null) PresentTankLoss(target, effect.Loss, effect.Ground);
                    break;
                case BattleEffectKind.LossReplay:
                    if (target != null && (effect.EndsRound || target.isPlayer))
                        _hud?.ShowHitCamera(target, effect.Trace, effect.Hit, ProjectileKind.Shell, _cam,
                            isDecisiveKill: true, roundOverride: effect.ReplayReason switch { 1 => "FUEL", 2 => "BLAST", 3 => "HE", _ => "FIRE" },
                            confirmedLoss: effect.Loss);
                    break;
                case BattleEffectKind.Explosion:
                    PresentExplosion(effect.Visual, effect.Ground, effect.Big, effect.Bus, effect.Volume, effect.Shake);
                    break;
                case BattleEffectKind.PropBlast:
                    PresentPropBlast(effect.Ground, effect.Big);
                    break;
                case BattleEffectKind.PropSpark:
                    Color color = effect.Big ? new Color(1f, 0.48f, 0.14f) : new Color(1f, 0.82f, 0.36f);
                    SpawnParticle(effect.Visual, Vector2.up * 0.6f, -1.5f, 0.16f, effect.Big ? 0.28f : 0.18f, color, 4.2f);
                    break;
            }
        }

        private void PresentArmorImpact(BattleEffect effect, TankEntity target, bool firedLocally)
        {
            TankHit hit = effect.Hit;
            bool decisive = effect.Loss != TankLoss.None && (effect.EndsRound || target.isPlayer);
            if (firedLocally || decisive)
                _hud?.ShowHitCamera(target, effect.Trace, hit, effect.Projectile, _cam, decisive, confirmedLoss: effect.Loss);

            bool heavy = effect.Projectile == ProjectileKind.Shell || effect.Projectile == ProjectileKind.ATGM;
            if (effect.Bounced)
            {
                ShowArmorDeflection(target, effect.Ground, effect.Visual, effect.Normal);
                return;
            }

            if (hit.Armor == ArmorHit.Penetrated || hit.Ammo == AmmoType.HE)
            {
                if (hit.Armor == ArmorHit.Penetrated)
                {
                    Vector2 attachedPoint = target.Position2D + effect.Ground - effect.VictimPosition;
                    target.ShowPenetration(attachedPoint, heavy, SpawnDebrisParticle);
                }
                if (target.isPlayer) _postProcess?.PulsePlayerDamage();
                if (heavy)
                {
                    bool big = hit.Ammo == AmmoType.HE || hit.Ammo == AmmoType.APHE;
                    if (!MultiplayerSessionConnection.Online) _hitStop = 0.030f;
                    PresentExplosion(effect.Visual, effect.Ground, big, big ? SoundBus.HeavyReverb : SoundBus.Reverb,
                        big ? 0.80f : 0.60f, big ? 0.34f : 0.24f);
                }
            }
            else if (effect.Projectile != ProjectileKind.MG)
            {
                ShowArmorDeflection(target, effect.Ground, effect.Visual, effect.Normal);
            }

            if (!firedLocally && effect.Projectile != ProjectileKind.MG && effect.Loss == TankLoss.None)
                PresentHitLabel(target, hit);
        }

        private void EmitExplosion(Vector2 visual, Vector2 ground, bool big, SoundBus bus, float volume, float shake = 0f) =>
            EmitBattleEffect(new BattleEffect { Kind = BattleEffectKind.Explosion, Visual = visual, Ground = ground,
                Big = big, Bus = bus, Volume = volume, Shake = shake });

        private void PresentExplosion(Vector2 visual, Vector2 ground, bool big, SoundBus bus, float volume, float shake)
        {
            SpawnExplosion(visual, ground, big);
            if (volume > 0f) PlaySFXAt(_sfxExplosion, ground, bus, volume);
            _shake = Mathf.Max(_shake, shake);
        }

        private void PresentPropBlast(Vector2 position, bool fuel)
        {
            SpawnExplosion(position + Vector2.up * 0.35f, position, big: true);
            if (fuel)
            {
                SpawnExplosion(position + new Vector2(0.5f, 0.7f), position + new Vector2(0.5f, 0.2f), big: true);
                SpawnExplosion(position + new Vector2(-0.5f, 0.5f), position + new Vector2(-0.5f, 0.1f), big: true);
            }
            PlaySFXAt(_sfxExplosion, position, SoundBus.HeavyReverb, fuel ? 1.0f : 0.92f);
            _shake = Mathf.Max(_shake, fuel ? 0.56f : 0.40f);
            AddPopup(position + Vector2.up * 1.1f, fuel ? "FUEL BLAST!" : "BOOM!", new Color(1f, 0.50f, 0.18f));
        }
    }
}
