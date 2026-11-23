using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    public partial class BattleHUD
    {
        private const float ReplayDuration = 2.85f;
        private const float DecisiveReplayDuration = 4.10f;
        private const int HitCamW = 138;
        private const int HitCamH = 94;

        private static readonly Color TextLight = new Color(0.96f, 0.95f, 0.89f, 1f);

        // War Thunder Dark Obsidian-Glass X-Ray Palette
        private static readonly Color WtAccent = new Color32(236, 188, 92, 255);

        private static readonly (TankModule Module, string Label)[] HitParts =
        {
            (TankModule.LeftRunningGear, "L.TRK"),
            (TankModule.RightRunningGear, "R.TRK"),
            (TankModule.Engine, "ENG"),
            (TankModule.Traverse, "TRV"),
            (TankModule.Weapon, "GUN"),
            (TankModule.Ammo, "AMMO")
        };

        [SerializeField] private RectTransform _vehicleStatusCard;
        [SerializeField] private CanvasGroup _vehicleStatusCg;
        [SerializeField] private TankCutawayGraphic _vehicleStatusGraphic;
        [SerializeField] private RawImage _vehicleStatusBackdrop;
        [SerializeField] private GameObject _fireIcon;
        [SerializeField] private GameObject _repairIcon;
        [SerializeField] private Text _serviceKeyText;

        [SerializeField] private RectTransform _hitCameraCard;
        [SerializeField] private CanvasGroup _hitCameraCanvasGroup;
        [SerializeField] private TankCutawayGraphic _hitCameraGraphic;
        [SerializeField] private RawImage _hitCameraBackdrop;
        [SerializeField] private Text _hitCameraTarget;
        [SerializeField] private Text _hitCameraRound;
        [SerializeField] private Text _hitCameraResult;
        [SerializeField] private Text _hitCameraBallistics;
        [SerializeField] private Text _hitCameraParts;
        private float _hitCameraAge;
        private float _hitCameraDuration = ReplayDuration;
        private int _hitCameraPriority;
        private bool _hitCameraDecisive;
        private TankEntity _hitCameraTargetTank;
        private TankModule _burstStruckModules;
        private TankCrew _burstKnockedOut;
        private int _burstHitCount;

        private void UpdateVehicleStatus(TankEntity player)
        {
            _vehicleStatusCard.gameObject.SetActive(player != null);
            if (player == null) return;
            _vehicleStatusGraphic.ShowStatus(player);

            bool fire = player.alive && player.OnFire;
            bool service = player.alive && !fire && (player.Repairing || player.DisabledModules != TankModule.None);
            _fireIcon.SetActive(fire);
            _repairIcon.SetActive(service);

            if (fire)
            {
                _serviceKeyText.color = DangerRed;
                int seconds = Mathf.CeilToInt(player.FireTime);
                _serviceKeyText.text = player.ExtinguisherReady ? $"[F] {seconds}s" : UiText.Format("FIRE {0}s", seconds);
            }
            else if (player.Repairing)
            {
                _serviceKeyText.color = AbramsGold;
                _serviceKeyText.text = UiText.Format("FIX {0:F1}s", player.RepairTime);
            }
            else if (service)
            {
                _serviceKeyText.color = DangerRed;
                string penalty = UiText.Get(player.HasDisabled(TankModule.Weapon) ? "GUN"
                    : player.HasDisabled(TankModule.Engine) ? "ENG"
                    : player.HasDisabled(TankModule.Traverse) ? "TRV"
                    : "TRK");
                _serviceKeyText.text = Mathf.Abs(player.moveSpeed) <= 0.08f ? $"[F] {penalty}" : penalty;
            }
            else if (player.alive && player.CrewRemaining < player.CrewCapacity)
            {
                _serviceKeyText.color = AbramsGold;
                _serviceKeyText.text = UiText.Format("CRW {0}/{1}", player.CrewRemaining, player.CrewCapacity);
            }
            else
            {
                _serviceKeyText.text = "";
            }
        }

        public void ShowHitCamera(
            TankEntity target,
            TankShotTrace trace,
            TankHit hit,
            ProjectileKind kind,
            Camera cam = null,
            bool isDecisiveKill = false,
            string roundOverride = null,
            TankLoss? confirmedLoss = null)
        {
            if (_hitCameraCard == null || target == null) return;
            TankLoss loss = confirmedLoss ?? target.Loss;
            int priority = isDecisiveKill ? 4
                : loss != TankLoss.None ? 3
                : hit.Modules != TankModule.None || hit.KnockedOut != TankCrew.None || hit.Ignited ? 2
                : kind == ProjectileKind.MG ? 0 : 1;

            // Never let MG bullets overwrite an active cannon/autocannon X-Ray replay
            if (!isDecisiveKill && kind == ProjectileKind.MG && _hitCameraCard.gameObject.activeSelf && _hitCameraPriority >= 1)
                return;

            // MULTI-SHOT LIVE BURST ACCUMULATION:
            // When firing an Autocannon (or rapid follow-up shells) at the SAME target already open in X-Ray,
            // append EVERY SINGLE ROUND simultaneously without dropping shots or resetting the animation!
            bool sameActiveTarget = _hitCameraCard.gameObject.activeSelf &&
                                    _hitCameraTargetTank == target &&
                                    string.IsNullOrEmpty(roundOverride);
            if (sameActiveTarget)
            {
                _hitCameraGraphic.AddReplayShot(trace, hit, loss);
                _burstHitCount++;
                _burstStruckModules |= hit.StruckModules;
                _burstKnockedOut |= hit.KnockedOut;

                if (isDecisiveKill)
                {
                    _hitCameraDecisive = true;
                    SetPixelRect(_hitCameraCard, 4, _H - 4 - HitCamH, HitCamW, HitCamH);
                }

                float extraLife = isDecisiveKill ? 3.85f : 2.35f;
                _hitCameraDuration = Mathf.Max(_hitCameraDuration, _hitCameraAge + extraLife);

                string baseRound = AmmoInfo.FullLabel(hit.Ammo);
                _hitCameraRound.text = _burstHitCount > 1 ? $"{baseRound} x{_burstHitCount}" : baseRound;

                if (priority >= _hitCameraPriority)
                {
                    _hitCameraPriority = priority;
                    _hitCameraResult.text = UiText.Get(loss switch
                    {
                        TankLoss.Ammo => "AMMO BLOWN!",
                        TankLoss.Fire => "BURNED OUT",
                        TankLoss.Crew => target.isPlayer ? "KNOCKED OUT" : "DESTROYED",
                        _ when hit.Ignited => "FIRE!",
                        _ when hit.Armor == ArmorHit.Penetrated => "HIT",
                        _ when hit.Armor == ArmorHit.Ricochet => "RICOCHET",
                        _ => "NO PEN"
                    });
                    _hitCameraResult.color = priority >= 2 ? DangerRed : (hit.Armor == ArmorHit.Ricochet ? SmokeCyan : WtAccent);
                }

                int penS = Mathf.RoundToInt(hit.PenetrationValue);
                int effS = Mathf.RoundToInt(hit.EffectiveArmor);
                int angS = Mathf.RoundToInt(hit.ImpactAngleDeg);
                _hitCameraBallistics.text = hit.Armor == ArmorHit.Ricochet
                    ? $"{angS} DEG"
                    : $"{penS}/{effS} {angS}D";

                _hitCameraParts.text = loss == TankLoss.Ammo
                    ? UiText.Get("AMMUNITION COOKOFF")
                    : loss == TankLoss.Fire
                        ? UiText.Get("ENGINE FIRE BURNOUT")
                        : DescribeHitParts(_burstStruckModules, _burstKnockedOut, target.IsWheeled);
                _hitCameraParts.color = loss == TankLoss.Ammo ? WtAccent : TextLight;
                _hitCameraParts.enabled = true;
                return;
            }

            // Different target: only throttle lower-priority non-autocannon glances
            if (!isDecisiveKill && kind != ProjectileKind.Autocannon && _hitCameraCard.gameObject.activeSelf &&
                priority < _hitCameraPriority && _hitCameraAge < 1.15f)
                return;

            // On decisive Match-End Kill Cam, dock cleanly at Top-Left (Y: 172..266) so ResultCard (Y: 62..148) NEVER overlaps!
            bool dockLeft = true;
            if (!isDecisiveKill && cam != null)
            {
                Vector3 vp = cam.WorldToViewportPoint(new Vector3(target.Position2D.x, target.Position2D.y, 0f));
                dockLeft = vp.x >= 0.48f;
            }
            int hx = dockLeft ? 4 : (_W - 4 - HitCamW);
            int radarHeight = Mathf.RoundToInt(_radarRect.rect.height);
            int hy = isDecisiveKill ? (_H - 4 - HitCamH) : (dockLeft ? (_H - 6 - HitCamH) : (_H - radarHeight - 8 - HitCamH));
            SetPixelRect(_hitCameraCard, hx, hy, HitCamW, HitCamH);

            _hitCameraTargetTank = target;
            _burstHitCount = 1;
            _burstStruckModules = hit.StruckModules;
            _burstKnockedOut = hit.KnockedOut;
            _hitCameraAge = 0f;
            _hitCameraDuration = isDecisiveKill ? DecisiveReplayDuration : ReplayDuration;
            _hitCameraDecisive = isDecisiveKill;
            _hitCameraPriority = priority;

            string rawName = target.vehicleDef != null ? target.vehicleDef.name : target.tankType;
            _hitCameraTarget.text = target.isPlayer ? UiText.Get("YOU") + " " + TrimHudName(rawName, 7) : TrimHudName(rawName, 11);
            _hitCameraTarget.color = target.isPlayer ? DangerRed : TextLight;

            _hitCameraRound.text = !string.IsNullOrEmpty(roundOverride)
                ? roundOverride
                : AmmoInfo.FullLabel(hit.Ammo);

            _hitCameraResult.text = UiText.Get(loss switch
            {
                TankLoss.Ammo => "AMMO BLOWN!",
                TankLoss.Fire => "BURNED OUT",
                TankLoss.Crew => target.isPlayer ? "KNOCKED OUT" : "DESTROYED",
                _ when hit.Ignited => "FIRE!",
                _ when hit.Armor == ArmorHit.Penetrated => "HIT",
                _ when hit.Armor == ArmorHit.Ricochet => "RICOCHET",
                _ => "NO PEN"
            });
            _hitCameraResult.color = priority >= 2 ? DangerRed : (hit.Armor == ArmorHit.Ricochet ? SmokeCyan : WtAccent);

            int pen = Mathf.RoundToInt(hit.PenetrationValue);
            int eff = Mathf.RoundToInt(hit.EffectiveArmor);
            int ang = Mathf.RoundToInt(hit.ImpactAngleDeg);
            if (!string.IsNullOrEmpty(roundOverride))
                _hitCameraBallistics.text = UiText.Get(loss == TankLoss.Ammo ? "COOKOFF" : "FATAL");
            else
                _hitCameraBallistics.text = hit.Armor == ArmorHit.Ricochet
                    ? $"{ang} DEG"
                    : $"{pen}/{eff} {ang}D";

            _hitCameraParts.text = loss == TankLoss.Ammo
                ? UiText.Get("AMMUNITION COOKOFF")
                : loss == TankLoss.Fire
                    ? UiText.Get("ENGINE FIRE BURNOUT")
                    : DescribeHitParts(_burstStruckModules, _burstKnockedOut, target.IsWheeled);
            _hitCameraParts.color = loss == TankLoss.Ammo ? WtAccent : TextLight;
            _hitCameraParts.enabled = false;
            _hitCameraCanvasGroup.alpha = 1f;
            _hitCameraCard.gameObject.SetActive(true);
            _hitCameraGraphic.StartReplay(target, null, trace, hit, loss);
        }

        /// <summary>
        /// Keeps the X-Ray Hit Camera animating smoothly even during impact hit-stop freeze frames!
        /// </summary>
        public void TickHitCameraOnly()
        {
            if (_hitCameraCard == null || !_hitCameraCard.gameObject.activeSelf || IsMenuOpen) return;
            UpdateHitCamera(1f);
        }

        private static string DescribeHitParts(TankModule struckModules, TankCrew knockedOut, bool wheeled)
        {
            if (struckModules == TankModule.None && knockedOut == TankCrew.None) return "";
            var parts = new List<string>(5);
            foreach (var (module, label) in HitParts)
            {
                if ((struckModules & module) == 0) continue;
                if (wheeled && module == TankModule.LeftRunningGear) parts.Add("L.WHL");
                else if (wheeled && module == TankModule.RightRunningGear) parts.Add("R.WHL");
                else parts.Add(label);
            }

            string seat = knockedOut switch
            {
                TankCrew.None => null,
                TankCrew.Driver => "DRIVER",
                TankCrew.Gunner => "GUNNER",
                TankCrew.Commander => "CMDR",
                TankCrew.Loader => "LOADER",
                _ => "CREW"
            };
            if (seat != null) parts.Add(seat);
            return string.Join(" ", parts);
        }

        private void UpdateHitCamera(float proximityAlpha)
        {
            if (_hitCameraCard == null || !_hitCameraCard.gameObject.activeSelf || IsMenuOpen) return;
            _hitCameraAge += Time.unscaledDeltaTime;
            if (_hitCameraAge >= _hitCameraDuration)
            {
                ClearHitCamera();
                return;
            }

            // Continuous live 30fps tracking for the FULL duration so enemy motion & expanding cookoff play live!
            float frame = Mathf.Floor(_hitCameraAge * 30f) / 30f;
            _hitCameraGraphic.SetReplayTime(frame);
            _hitCameraParts.enabled = frame >= 0.42f;
            float fadeOut = Mathf.Clamp01((_hitCameraDuration - _hitCameraAge) / 0.35f);
            _hitCameraCanvasGroup.alpha = fadeOut * (_hitCameraDecisive ? 1f : proximityAlpha);
        }

        public void ClearHitCamera()
        {
            _hitCameraDecisive = false;
            _hitCameraPriority = 0;
            _hitCameraTargetTank = null;
            _burstHitCount = 0;
            _burstStruckModules = TankModule.None;
            _burstKnockedOut = TankCrew.None;
            if (_hitCameraCard != null) _hitCameraCard.gameObject.SetActive(false);
        }
    }
}
