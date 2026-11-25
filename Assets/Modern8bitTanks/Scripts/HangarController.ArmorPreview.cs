using UnityEngine;
using UnityEngine.InputSystem;

namespace Modern8bitTanks
{
    public partial class HangarController
    {
        private VehicleDef _pinnedArmorPreviewAttacker;
        private VehicleDef _armorPreviewAttacker;
        private AmmoType[] _armorPreviewLoadout;
        private int _armorPreviewAmmoIndex;
        private bool _armorPreviewLabelsDirty = true;
        private (bool aiming, ArmorHit? outcome, int penetration, int armor, int angle)? _armorPreviewReadout;

        private void UpdateArmorPreview(Keyboard keyboard, Mouse mouse)
        {
            if (showcaseTank.vehicleDef == null) return;
            bool interactive = !_settingsOpen && !_deployOpen && !_onlineOpen;
            if (interactive && keyboard != null && keyboard.pKey.wasPressedThisFrame)
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
            if (interactive && keyboard != null)
            {
                int slot = ReadArmorPreviewAmmoKey(keyboard);
                if (slot >= 0 && slot < count && slot != _armorPreviewAmmoIndex)
                {
                    _armorPreviewAmmoIndex = slot;
                    _armorPreviewLabelsDirty = true;
                }
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
                _armorPreviewLabelsDirty = false;
            }

            Vector2? aim = null;
            if (interactive && !_draggingTank && mouse != null &&
                _windowbox.TryScreenToViewport(mouse.position.ReadValue(), out Vector2 viewport))
                aim = viewport;

            bool secondaryAtgm = _armorPreviewAmmoIndex == _armorPreviewLoadout.Length;
            AmmoType ammo = secondaryAtgm ? AmmoType.TOW : _armorPreviewLoadout[_armorPreviewAmmoIndex];
            ProjectileKind kind = ammo switch
            {
                AmmoType.APBelt or AmmoType.HEIBelt => ProjectileKind.Autocannon,
                AmmoType.TOW or AmmoType.GLATGM => ProjectileKind.ATGM,
                _ => attacker.weapon == "ATGM" ? ProjectileKind.ATGM : ProjectileKind.Shell
            };
            float penetration = AmmoInfo.Penetration(kind, ammo, attacker, secondaryAtgm);
            bool aiming = _hangarXrayGraphic.PreviewHangarShot(showcaseTank, _cam, aim, kind, ammo, penetration, out TankHit? hit);
            var readout = (aiming, outcome: hit?.Armor, penetration: Mathf.RoundToInt(penetration),
                armor: Mathf.RoundToInt(hit?.EffectiveArmor ?? 0f), angle: Mathf.RoundToInt(hit?.ImpactAngleDeg ?? 0f));
            if (_armorPreviewReadout == readout) return;
            _armorPreviewReadout = readout;

            string result = hit?.Armor switch
            {
                ArmorHit.Penetrated => UiText.Get("XRAY PENETRATED"),
                ArmorHit.Blocked => UiText.Get("XRAY BLOCKED"),
                ArmorHit.Ricochet => UiText.Get("RICOCHET"),
                _ => UiText.Get(aiming ? "XRAY MISS" : "XRAY AIM FROM BELOW")
            };
            if (hit.HasValue)
                result = UiText.Format("XRAY ARMOR RESULT", result, readout.penetration, readout.armor, readout.angle);
            _hangarXrayGraphic.SetArmorPreviewResult(result, hit?.Armor);
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
