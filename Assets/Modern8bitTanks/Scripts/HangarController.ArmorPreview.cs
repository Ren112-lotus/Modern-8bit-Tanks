using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Modern8bitTanks
{
    public partial class HangarController
    {
        private const double ArmorPreviewIdleResetSeconds = 5d;

        private VehicleDef _pinnedArmorPreviewAttacker;
        private VehicleDef _armorPreviewAttacker;
        private AmmoType[] _armorPreviewLoadout;
        private int _armorPreviewAmmoIndex;
        private bool _armorPreviewLabelsDirty = true;
        private (bool aiming, bool inFlight, ArmorHit? outcome, int penetration, int armor, int angle)? _armorPreviewReadout;
        private Vector2? _armorPreviewPress;
        private bool _armorPreviewHasFired;
        private TankHit? _armorPreviewLastHit;
        private Vector2? _armorPreviewLastPointer;
        private double _armorPreviewLastInteraction;
        private (int crew, TankModule modules, TankLoss loss, int fireSeconds)? _armorPreviewDamage;

        private static readonly (TankModule module, string label)[] ArmorPreviewModules =
        {
            (TankModule.LeftRunningGear, "XRAY LEFT DRIVE"),
            (TankModule.RightRunningGear, "XRAY RIGHT DRIVE"),
            (TankModule.Engine, "XRAY ENGINE"),
            (TankModule.Traverse, "XRAY TRAVERSE"),
            (TankModule.Weapon, "XRAY WEAPON"),
            (TankModule.Ammo, "XRAY AMMO")
        };

        private bool ReadArmorPreviewClick(Mouse mouse, bool overUI)
        {
            Vector2 pointer = mouse.position.ReadValue();
            if (mouse.leftButton.wasPressedThisFrame)
                _armorPreviewPress = !overUI && !mouse.rightButton.isPressed ? pointer : null;
            if (mouse.rightButton.isPressed)
                _armorPreviewPress = null;

            if (_armorPreviewPress is Vector2 origin && (pointer - origin).sqrMagnitude >= 16f)
            {
                _lastDragX = origin.x;
                _draggingTank = float.IsFinite(origin.x);
                _armorPreviewPress = null;
            }

            if (!mouse.leftButton.wasReleasedThisFrame) return false;
            bool fire = _armorPreviewPress is Vector2 press && !overUI &&
                _windowbox.TryScreenToViewport(press, out Vector2 viewport) && _hangarXrayGraphic.ContainsHangarAim(viewport);
            _armorPreviewPress = null;
            return fire;
        }

        private void ResetArmorPreviewDamage()
        {
            if (_armorPreviewHasFired && showcaseTank != null)
                showcaseTank.CaptureSpawnAndReset(showcaseTank.Position2D, showcaseTank.hullAngleDeg);
            ClearArmorPreviewHistory();
        }

        private void ClearArmorPreviewHistory()
        {
            _armorPreviewPress = null;
            _armorPreviewHasFired = false;
            _armorPreviewLastHit = null;
            _armorPreviewLastPointer = null;
            _armorPreviewLastInteraction = 0d;
            _armorPreviewReadout = null;
            _armorPreviewDamage = null;
            _hangarXrayGraphic?.ClearHangarShot();
        }

        private void UpdateArmorPreview(Keyboard keyboard, Mouse mouse, bool fire, float dt)
        {
            if (showcaseTank.vehicleDef == null) return;
            bool interactive = !_settingsOpen && !_deployOpen && !_onlineOpen;
            bool canAim = interactive && !_hangarXrayGraphic.HangarShotBusy;
            bool pickAttacker = canAim && keyboard != null && keyboard.pKey.wasPressedThisFrame;
            int requestedSlot = canAim && keyboard != null ? ReadArmorPreviewAmmoKey(keyboard) : -1;
            if (interactive)
            {
                UpdateArmorPreviewIdleReset(mouse, fire || pickAttacker || requestedSlot >= 0);
                if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
                {
                    ResetArmorPreviewDamage();
                    fire = false;
                }
                showcaseTank.TickCombat(dt, autoService: false);
            }
            if (pickAttacker)
            {
                _pinnedArmorPreviewAttacker = _pinnedArmorPreviewAttacker == null ? showcaseTank.vehicleDef : null;
                _armorPreviewLabelsDirty = true;
            }

            VehicleDef attacker = _pinnedArmorPreviewAttacker ?? showcaseTank.vehicleDef;
            if (attacker != _armorPreviewAttacker)
            {
                _armorPreviewAttacker = attacker;
                _armorPreviewLoadout = attacker.GetAmmoLoadout();
                _armorPreviewAmmoIndex = 0;
                _armorPreviewLabelsDirty = true;
            }

            int count = _armorPreviewLoadout.Length + (attacker.HasSecondaryAtgm ? 1 : 0);
            if (requestedSlot >= 0 && requestedSlot < count && requestedSlot != _armorPreviewAmmoIndex)
            {
                _armorPreviewAmmoIndex = requestedSlot;
                _armorPreviewLabelsDirty = true;
            }

            if (_armorPreviewLabelsDirty)
            {
                var labels = new string[count];
                for (int i = 0; i < count; i++)
                {
                    AmmoType round = i < _armorPreviewLoadout.Length ? _armorPreviewLoadout[i] : AmmoType.TOW;
                    string option = $"[{i + 1}] {AmmoInfo.FullLabel(round)}";
                    labels[i] = i == _armorPreviewAmmoIndex ? $"<color=#FFDB78>{option}</color>" : option;
                }
                string attackerKey = _pinnedArmorPreviewAttacker == null ? "XRAY PIN ATTACKER" : "XRAY UNPIN ATTACKER";
                _hangarXrayGraphic.SetArmorPreviewLoadout(UiText.Format(attackerKey, attacker.name), string.Join("   ", labels));
                _armorPreviewReadout = null;
                _armorPreviewDamage = null;
                _armorPreviewLabelsDirty = false;
            }

            Vector2? aim = null;
            if (canAim && !_draggingTank && mouse != null &&
                _windowbox.TryScreenToViewport(mouse.position.ReadValue(), out Vector2 viewport))
            {
                if (mouse.rightButton.isPressed)
                    _hangarXrayGraphic.MoveHangarShotOrigin(viewport);
                aim = viewport;
            }

            bool secondaryAtgm = _armorPreviewAmmoIndex == _armorPreviewLoadout.Length;
            AmmoType ammo = secondaryAtgm ? AmmoType.TOW : _armorPreviewLoadout[_armorPreviewAmmoIndex];
            ProjectileKind kind = ammo switch
            {
                AmmoType.APBelt or AmmoType.HEIBelt => ProjectileKind.Autocannon,
                AmmoType.TOW or AmmoType.GLATGM => ProjectileKind.ATGM,
                _ => attacker.weapon == "ATGM" ? ProjectileKind.ATGM : ProjectileKind.Shell
            };
            float penetration = AmmoInfo.Penetration(kind, ammo, attacker, secondaryAtgm);
            TankHit? hit = null;
            bool aiming;
            if (interactive && _hangarXrayGraphic.AdvanceHangarShot(showcaseTank, Time.unscaledDeltaTime, out hit))
            {
                aiming = true;
                _armorPreviewHasFired = true;
                if (hit.HasValue || showcaseTank.alive) _armorPreviewLastHit = hit;
                _armorPreviewLastInteraction = Time.unscaledTimeAsDouble;
            }
            else if (_hangarXrayGraphic.HangarShotBusy)
            {
                aiming = true;
                hit = _armorPreviewLastHit;
            }
            else
            {
                aiming = _hangarXrayGraphic.PreviewHangarShot(showcaseTank, _cam, aim, kind, ammo, penetration, fire, out hit);
            }
            bool inFlight = _hangarXrayGraphic.HangarShotInFlight;
            if (!showcaseTank.alive)
                hit = _armorPreviewLastHit;
            UpdateArmorPreviewDamageReadout();

            var readout = (aiming, inFlight, outcome: hit?.Armor, penetration: Mathf.RoundToInt(hit?.PenetrationValue ?? penetration),
                armor: Mathf.RoundToInt(hit?.EffectiveArmor ?? 0f), angle: Mathf.RoundToInt(hit?.ImpactAngleDeg ?? 0f));
            if (_armorPreviewReadout == readout) return;
            _armorPreviewReadout = readout;

            if (inFlight)
            {
                _hangarXrayGraphic.SetArmorPreviewResult(UiText.Get("XRAY SHOT IN FLIGHT"), null);
                return;
            }

            string result = hit?.Armor switch
            {
                ArmorHit.Penetrated => UiText.Get("XRAY PENETRATED"),
                ArmorHit.Blocked => UiText.Get("XRAY BLOCKED"),
                ArmorHit.Ricochet => UiText.Get("RICOCHET"),
                _ => UiText.Get(aiming ? "XRAY MISS" : "XRAY AIM")
            };
            if (hit.HasValue)
                result = UiText.Format("XRAY ARMOR RESULT", result, readout.penetration, readout.armor, readout.angle);
            _hangarXrayGraphic.SetArmorPreviewResult(result, hit?.Armor);
        }

        private void UpdateArmorPreviewIdleReset(Mouse mouse, bool interacted)
        {
            if (mouse != null)
            {
                Vector2 pointer = mouse.position.ReadValue();
                bool inPreview = _windowbox.TryScreenToViewport(pointer, out Vector2 viewport) &&
                    _hangarXrayGraphic.ContainsHangarAim(viewport);
                if (_draggingTank || inPreview)
                    interacted |= pointer != _armorPreviewLastPointer || mouse.leftButton.isPressed || mouse.rightButton.isPressed;
                _armorPreviewLastPointer = pointer;
            }

            double now = Time.unscaledTimeAsDouble;
            if (interacted)
                _armorPreviewLastInteraction = now;
            if (_armorPreviewHasFired && !_hangarXrayGraphic.HangarShotInFlight &&
                now - _armorPreviewLastInteraction >= ArmorPreviewIdleResetSeconds)
                ResetArmorPreviewDamage();
        }

        private void UpdateArmorPreviewDamageReadout()
        {
            var state = (crew: showcaseTank.CrewRemaining, modules: showcaseTank.DisabledModules,
                loss: showcaseTank.Loss, fireSeconds: Mathf.CeilToInt(showcaseTank.FireTime));
            if (_armorPreviewDamage == state) return;
            _armorPreviewDamage = state;

            string condition = state.loss switch
            {
                TankLoss.Ammo => UiText.Get("AMMO BLOWN!"),
                TankLoss.Crew => UiText.Get("KNOCKED OUT"),
                TankLoss.Fire => UiText.Get("ENGINE FIRE BURNOUT"),
                _ => state.fireSeconds > 0 ? UiText.Format("FIRE {0}s", state.fireSeconds) : null
            };
            string status = UiText.Format("XRAY CREW COUNT", state.crew, showcaseTank.CrewCapacity);
            if (condition != null)
                status += "  /  " + condition;

            var disabled = new List<string>(ArmorPreviewModules.Length);
            foreach (var (module, label) in ArmorPreviewModules)
                if ((state.modules & module) != 0)
                    disabled.Add(UiText.Get(label));
            _hangarXrayGraphic.SetArmorPreviewDamage(status, disabled.Count == 0
                ? UiText.Get("XRAY NO DAMAGE")
                : string.Join("/", disabled), condition != null, disabled.Count != 0);
        }

        private static int ReadArmorPreviewAmmoKey(Keyboard keyboard)
        {
            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) return 0;
            if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) return 1;
            if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) return 2;
            if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) return 3;
            return -1;
        }
    }
}
