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
        private const int StatusCardW = 68;
        private const int StatusCardH = 58;

        // War Thunder Dark Obsidian-Glass X-Ray Palette
        private static readonly Color WtBg = new Color32(12, 15, 18, 198);
        private static readonly Color WtViewportBg = new Color32(9, 12, 15, 214);
        private static readonly Color WtBorder = new Color32(72, 84, 92, 210);
        private static readonly Color WtAccent = new Color32(236, 188, 92, 255);
        private static readonly Color WtInfo = new Color32(174, 194, 206, 255);

        private static readonly (TankModule Module, string Label)[] HitParts =
        {
            (TankModule.LeftRunningGear, "L.TRK"),
            (TankModule.RightRunningGear, "R.TRK"),
            (TankModule.Engine, "ENG"),
            (TankModule.Traverse, "TRV"),
            (TankModule.Weapon, "GUN"),
            (TankModule.Ammo, "AMMO")
        };

        private RectTransform _vehicleStatusCard;
        private CanvasGroup _vehicleStatusCg;
        private TankCutawayGraphic _vehicleStatusGraphic;
        private GameObject _fireIcon;
        private GameObject _repairIcon;
        private Text _serviceKeyText;

        private RectTransform _hitCameraCard;
        private CanvasGroup _hitCameraCanvasGroup;
        private TankCutawayGraphic _hitCameraGraphic;
        private Text _hitCameraTarget;
        private Text _hitCameraRound;
        private Text _hitCameraResult;
        private Text _hitCameraBallistics;
        private Text _hitCameraParts;
        private float _hitCameraAge;
        private float _hitCameraDuration = ReplayDuration;
        private int _hitCameraPriority;
        private bool _hitCameraDecisive;
        private TankEntity _hitCameraTargetTank;
        private TankModule _burstStruckModules;
        private TankCrew _burstKnockedOut;
        private int _burstHitCount;
        private int _radarPanelHeight;

        private void BuildDamageHud(Transform parent, int radarHeight)
        {
            _radarPanelHeight = radarHeight;

            // Bottom-Left Riveted Armored Damage Doll Monitor (68 x 58 EVEN)
            _vehicleStatusCard = MilitaryUiSprites.CreateSlicedSprite(parent, "VehicleStatus", MilitaryUiSprites.ArmorPanel, 4, 4, StatusCardW, StatusCardH, ArmorTint, out _);
            _vehicleStatusCg = _vehicleStatusCard.gameObject.AddComponent<CanvasGroup>();
            _vehicleStatusCg.blocksRaycasts = false;
            _vehicleStatusCg.interactable = false;

            CreatePixelSolid(_vehicleStatusCard, "StatusCrtBg", 3, 13, StatusCardW - 6, StatusCardH - 16, WtViewportBg);
            _vehicleStatusGraphic = AddCutaway(_vehicleStatusCard, "VehicleDiagram", 3, 13, StatusCardW - 6, StatusCardH - 16, StatusCardW - 6, StatusCardH - 16);
            MilitaryUiSprites.CreateTiledSprite(_vehicleStatusCard, "StatusScan", MilitaryUiSprites.Scanlines, 3, 13, StatusCardW - 6, StatusCardH - 16, Color.white);
            CreatePixelSolid(_vehicleStatusCard, "ServiceDivider", 3, 12, StatusCardW - 6, 1, new Color32(18, 22, 16, 220));

            _fireIcon = BuildServiceIcon(_vehicleStatusCard, "Fire", DangerRed, new[]
            {
                "...#....", "...##...", "..###.#.", ".######.", ".##.###.", ".#...##.", "..####..", "...##..."
            });
            _repairIcon = BuildServiceIcon(_vehicleStatusCard, "Repair", AbramsGold, new[]
            {
                ".....#.#", ".....###", "....###.", "...###..", "..##....", ".##.....", "##......", ".#......"
            });
            _serviceKeyText = CreatePixelText(_vehicleStatusCard, "", 8, AbramsGold, TextAnchor.MiddleLeft, 13, 1, StatusCardW - 15, 10);
        }

        /// <summary>
        /// Dedicated top HUD layer built AFTER ResultOverlay (and before PauseMenu)
        /// so the War Thunder X-Ray Hit Camera stays 100% crisp, bright, & animating even on Match End!
        /// </summary>
        private void BuildHitCameraLayer()
        {
            var layerGo = new GameObject("HitCameraLayer") { layer = 5 };
            layerGo.transform.SetParent(_hudRoot, false);
            _hitCamLayer = layerGo.AddComponent<RectTransform>();
            Stretch(_hitCamLayer);

            // Riveted Armored Live Hit Camera Monitor (138 x 94 EVEN, auto-docks opposite from hit enemy!)
            _hitCameraCard = MilitaryUiSprites.CreateSlicedSprite(_hitCamLayer, "HitCamera", MilitaryUiSprites.ArmorPanel, 4, _H - 4 - HitCamH,
                HitCamW, HitCamH, ArmorModalTint, out _);
            _hitCameraCanvasGroup = _hitCameraCard.gameObject.AddComponent<CanvasGroup>();
            _hitCameraCanvasGroup.blocksRaycasts = false;
            _hitCameraCanvasGroup.interactable = false;

            // Clean top telemetry header
            CreatePixelSolid(_hitCameraCard, "LiveDot", 5, HitCamH - 9, 3, 3, DangerRed);
            _hitCameraTarget = CreatePixelText(_hitCameraCard, "", 8, TextLight, TextAnchor.MiddleLeft, 11, HitCamH - 12, 84, 10);
            _hitCameraRound = CreatePixelText(_hitCameraCard, "", 8, WtInfo, TextAnchor.MiddleRight, 94, HitCamH - 12, HitCamW - 99, 10);

            int vpW = HitCamW - 8;
            int vpH = 52;
            var viewport = CreatePixelSolid(_hitCameraCard, "Viewport", 4, 28, vpW, vpH, WtViewportBg);
            viewport.gameObject.AddComponent<RectMask2D>();
            _hitCameraGraphic = AddCutaway(viewport, "ImpactDiagram", 0, 0, vpW, vpH, vpW, vpH);
            MilitaryUiSprites.CreateTiledSprite(viewport, "HitCamScan", MilitaryUiSprites.Scanlines, 0, 0, vpW, vpH, Color.white);

            // Clean bottom War Thunder result + ballistic + struck modules readout
            CreatePixelSolid(_hitCameraCard, "ResultDivider", 4, 27, HitCamW - 8, 1, new Color32(18, 22, 16, 220));
            _hitCameraResult = CreatePixelText(_hitCameraCard, "", 8, WtAccent, TextAnchor.MiddleLeft, 5, 16, 74, 10);
            _hitCameraBallistics = CreatePixelText(_hitCameraCard, "", 8, WtInfo, TextAnchor.MiddleRight, 76, 16, HitCamW - 81, 10);
            _hitCameraParts = CreatePixelText(_hitCameraCard, "", 8, TextLight, TextAnchor.MiddleLeft, 5, 4, HitCamW - 10, 10);
            ClearHitCamera();
        }

        private static TankCutawayGraphic AddCutaway(Transform parent, string name, int x, int y, int width, int height, int texW, int texH)
        {
            var container = new GameObject(name + "_Wrap") { layer = 5 };
            container.transform.SetParent(parent, false);
            var cRt = container.AddComponent<RectTransform>();
            SetPixelRect(cRt, x, y, width, height);

            var bgGo = new GameObject(name + "_Hologram") { layer = 5 };
            bgGo.transform.SetParent(cRt, false);
            var bgImg = bgGo.AddComponent<RawImage>();
            Stretch(bgImg.rectTransform);
            bgImg.raycastTarget = false;

            var fgGo = new GameObject(name) { layer = 5 };
            fgGo.transform.SetParent(cRt, false);
            var graphic = fgGo.AddComponent<TankCutawayGraphic>();
            Stretch(graphic.rectTransform);
            graphic.raycastTarget = false;
            graphic.AttachBackdrop(bgImg, texW, texH);
            return graphic;
        }

        private static GameObject BuildServiceIcon(Transform parent, string name, Color tint, string[] pixels)
        {
            var icon = new GameObject(name) { layer = 5 };
            icon.transform.SetParent(parent, false);
            var rect = icon.AddComponent<RectTransform>();
            SetPixelRect(rect, 3, 2, 8, 8);
            for (int row = 0; row < pixels.Length; row++)
            {
                for (int x = 0; x < pixels[row].Length; x++)
                    if (pixels[row][x] == '#') CreatePixelSolid(rect, "Pixel", x, 7 - row, 1, 1, tint);
            }
            icon.SetActive(false);
            return icon;
        }

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
                _serviceKeyText.text = player.ExtinguisherReady ? $"[F] {Mathf.CeilToInt(player.FireTime)}s" : $"FIRE {Mathf.CeilToInt(player.FireTime)}s";
            }
            else if (player.Repairing)
            {
                _serviceKeyText.color = AbramsGold;
                _serviceKeyText.text = $"FIX {player.RepairTime:F1}s";
            }
            else if (service)
            {
                _serviceKeyText.color = DangerRed;
                string penalty = player.HasDisabled(TankModule.Weapon) ? "GUN"
                    : player.HasDisabled(TankModule.Engine) ? "ENG"
                    : player.HasDisabled(TankModule.Traverse) ? "TRV"
                    : "TRK";
                _serviceKeyText.text = Mathf.Abs(player.moveSpeed) <= 0.08f ? $"[F] {penalty}" : penalty;
            }
            else if (player.alive && player.CrewRemaining < player.CrewCapacity)
            {
                _serviceKeyText.color = AbramsGold;
                _serviceKeyText.text = $"CRW {player.CrewRemaining}/{player.CrewCapacity}";
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
                    _hitCameraResult.text = loss switch
                    {
                        TankLoss.Ammo => "AMMO BLOWN!",
                        TankLoss.Fire => "BURNED OUT",
                        TankLoss.Crew => target.isPlayer ? "KNOCKED OUT" : "DESTROYED",
                        _ when hit.Ignited => "FIRE!",
                        _ when hit.Armor == ArmorHit.Penetrated => "HIT",
                        _ when hit.Armor == ArmorHit.Ricochet => "RICOCHET",
                        _ => "NO PEN"
                    };
                    _hitCameraResult.color = priority >= 2 ? DangerRed : (hit.Armor == ArmorHit.Ricochet ? SmokeCyan : WtAccent);
                }

                int penS = Mathf.RoundToInt(hit.PenetrationValue);
                int effS = Mathf.RoundToInt(hit.EffectiveArmor);
                int angS = Mathf.RoundToInt(hit.ImpactAngleDeg);
                _hitCameraBallistics.text = hit.Armor == ArmorHit.Ricochet
                    ? $"{angS} DEG"
                    : $"{penS}/{effS} {angS}D";

                _hitCameraParts.text = loss == TankLoss.Ammo
                    ? "AMMUNITION COOKOFF"
                    : loss == TankLoss.Fire
                        ? "ENGINE FIRE BURNOUT"
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
            int hy = isDecisiveKill ? (_H - 4 - HitCamH) : (dockLeft ? (_H - 6 - HitCamH) : (_H - _radarPanelHeight - 8 - HitCamH));
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
            _hitCameraTarget.text = target.isPlayer ? ("YOU " + TrimHudName(rawName, 7)) : TrimHudName(rawName, 11);
            _hitCameraTarget.color = target.isPlayer ? DangerRed : TextLight;

            _hitCameraRound.text = !string.IsNullOrEmpty(roundOverride)
                ? roundOverride
                : AmmoInfo.FullLabel(hit.Ammo);

            _hitCameraResult.text = loss switch
            {
                TankLoss.Ammo => "AMMO BLOWN!",
                TankLoss.Fire => "BURNED OUT",
                TankLoss.Crew => target.isPlayer ? "KNOCKED OUT" : "DESTROYED",
                _ when hit.Ignited => "FIRE!",
                _ when hit.Armor == ArmorHit.Penetrated => "HIT",
                _ when hit.Armor == ArmorHit.Ricochet => "RICOCHET",
                _ => "NO PEN"
            };
            _hitCameraResult.color = priority >= 2 ? DangerRed : (hit.Armor == ArmorHit.Ricochet ? SmokeCyan : WtAccent);

            int pen = Mathf.RoundToInt(hit.PenetrationValue);
            int eff = Mathf.RoundToInt(hit.EffectiveArmor);
            int ang = Mathf.RoundToInt(hit.ImpactAngleDeg);
            if (!string.IsNullOrEmpty(roundOverride))
                _hitCameraBallistics.text = loss == TankLoss.Ammo ? "COOKOFF" : "FATAL";
            else
                _hitCameraBallistics.text = hit.Armor == ArmorHit.Ricochet
                    ? $"{ang} DEG"
                    : $"{pen}/{eff} {ang}D";

            _hitCameraParts.text = loss == TankLoss.Ammo
                ? "AMMUNITION COOKOFF"
                : loss == TankLoss.Fire
                    ? "ENGINE FIRE BURNOUT"
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
