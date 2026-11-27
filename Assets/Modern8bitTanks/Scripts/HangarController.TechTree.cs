using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    public partial class HangarController
    {
        [SerializeField] private TechTreeNodeView treeNodePrefab;
        [SerializeField] private PixelMenuButton treeNodeHitPrefab;
        [SerializeField] private Image treeLinkPrefab;
        [SerializeField] private PixelMenuButton[] treeNationActions;
        [SerializeField] private Image[] treeNationBackdrops;
        [SerializeField] private GameObject[] treeNationLinkRoots;
        [SerializeField] private PixelMenuButton treeGarageAction;
        [SerializeField] private PixelMenuButton treeScrollLeftAction;
        [SerializeField] private PixelMenuButton treeScrollRightAction;
        [SerializeField] private PixelMenuButton treeHistoryAction;
        [SerializeField] private PixelMenuButton treeEarlyAction;
        [SerializeField] private PixelMenuButton treeMiddleAction;
        [SerializeField] private PixelMenuButton treeModernAction;
        [SerializeField] private PixelMenuButton treeViewTankAction;
        [SerializeField] private PixelMenuButton treeDeployAction;

        private void InitializeTechTree()
        {
            for (int i = 0; i < TreeNations.Length; i++)
            {
                var (nation, label) = TreeNations[i];
                var background = treeNationBackdrops[i];
                background.GetComponentInChildren<Text>().text = GetNationTabLabel(nation, label);
                _nationButtons.Add(nation, background);
                _nationLinks.Add(nation, treeNationLinkRoots[i]);
                treeNationActions[i].Bind(() =>
                {
                    _treeNation = nation;
                    RefreshAllUI();
                    ScrollTreeToSelected();
                });
            }

            treeGarageAction.Bind(() => SetTechTreeOpen(false));
            treeScrollLeftAction.Bind(() => ScrollTechTree(-2));
            treeScrollRightAction.Bind(() => ScrollTechTree(2));
            treeHistoryAction.Bind(() => JumpTreeTo(0));
            treeEarlyAction.Bind(() => JumpTreeTo(HistoryColumns));
            treeMiddleAction.Bind(() => JumpTreeTo(HistoryColumns + 5));
            treeModernAction.Bind(() => JumpTreeTo(HistoryColumns + VehicleDef.ModernTier - 1));
            treeViewTankAction.Bind(() => SetTechTreeOpen(false));
            treeDeployAction.Bind(() => SetDeployModalOpen(true, fromTree: true));

            foreach (var vehicle in _vehicleCatalog.Vehicles)
            {
                int x = PadX + TreeColumn(vehicle) * ColStep;
                Color border = TreeLineColor(vehicle);
                var slot = CreateTreeNodeSlot(_treeContentRt, _treeHitsGroup.transform, x, TreeRow(vehicle), border);
                slot.vehicle = vehicle;
                _treeNodes.Add(vehicle.id, slot);
            }

            foreach (var vehicle in _vehicleCatalog.Vehicles)
            {
                VehicleDef parent = TreeParent(vehicle);
                if (parent != null)
                    ConnectTreeNodes(_nationLinks[vehicle.nation].transform, _treeNodes[parent.id],
                        _treeNodes[vehicle.id], TreeLineColor(vehicle));
            }
        }

        private void ScrollTechTree(int columns)
        {
            _targetScrollX = Mathf.Clamp(_targetScrollX + columns * ColStep, 0f, MaxScrollX);
        }

        private static int TreeColumn(VehicleDef vehicle) =>
            vehicle.historyColumn > 0 ? vehicle.historyColumn - 1 : HistoryColumns + vehicle.tier - 1;

        private void JumpTreeTo(int column) => _targetScrollX = Mathf.Clamp(column * ColStep, 0f, MaxScrollX);

        private static int TreeRow(VehicleDef vehicle)
        {
            int bottom = 28;
            int middle = (ViewH - NodeH) / 2 - 10;
            int top = ViewH - NodeH - 48;
            if (vehicle.historyColumn > 0)
                return vehicle.historyRow == 1 ? top : vehicle.historyRow == 2 ? bottom : middle;
            return vehicle.tab switch
            {
                "MBT" => top,
                "LIGHT" => vehicle.tier < VehicleDef.ModernTier ? top : middle,
                "SUPPORT" => bottom,
                _ => middle
            };
        }

        private static Color TreeLineColor(VehicleDef vehicle)
        {
            if (vehicle.IsSpecial) return SpecialBorder;
            return vehicle.tab switch
            {
                "LIGHT" => LightCyan,
                "SUPPORT" => SupportAmber,
                "MBT" => MbtGreen,
                _ => vehicle.role == "TD" ? SupportAmber : vehicle.role == "LIGHT" ? LightCyan : PanelBorder
            };
        }

        private VehicleDef TreeParent(VehicleDef vehicle)
        {
            if (!string.IsNullOrEmpty(vehicle.treeFrom))
            {
                var linked = _vehicleCatalog.Find(vehicle.treeFrom);
                return linked?.nation == vehicle.nation ? linked : null;
            }

            if (vehicle.historyColumn > 0) return null;
            VehicleDef previous = null;
            foreach (var other in _vehicleCatalog.Vehicles)
            {
                if (other.nation != vehicle.nation || other.historyColumn > 0 || other.tier >= vehicle.tier)
                    continue;
                if (other.tab == vehicle.tab && (previous == null || previous.tier < other.tier))
                    previous = other;
            }

            if (previous != null || vehicle.tab == "T1_5") return previous;
            foreach (var other in _vehicleCatalog.Vehicles)
                if (other.nation == vehicle.nation && other.tab == "T1_5" && other.tier < vehicle.tier &&
                    (previous == null || previous.tier < other.tier))
                    previous = other;
            return previous;
        }

        private void ConnectTreeNodes(Transform parent, TreeNodeSlot source, TreeNodeSlot target, Color color)
        {
            int start = source.contentX + NodeW;
            int end = target.contentX;
            if (start >= end) return;
            int y1 = source.contentY + NodeH / 2;
            int y2 = target.contentY + NodeH / 2;
            if (y1 == y2)
            {
                CreateTreeLink(parent, start, y1, end - start, 2, color);
                return;
            }

            int elbow = start + ColGap / 2;
            CreateTreeLink(parent, start, y1, elbow - start + 2, 2, color);
            CreateTreeLink(parent, elbow, Math.Min(y1, y2), 2, Math.Abs(y2 - y1) + 2, color);
            CreateTreeLink(parent, elbow, y2, end - elbow, 2, color);
        }

        private void CreateTreeLink(Transform parent, int x, int y, int width, int height, Color color)
        {
            var link = Instantiate(treeLinkPrefab, parent, false);
            SetPixelRect(link.rectTransform, x, y, width, height);
            link.color = color;
        }

        private TreeNodeSlot CreateTreeNodeSlot(Transform contentParent, Transform hitParent, int x, int y, Color border)
        {
            var view = Instantiate(treeNodePrefab, contentParent, false);
            view.name = $"Node_{x}_{y}";
            var rect = (RectTransform)view.transform;
            rect.anchoredPosition = new Vector2(x, y);
            view.Border.color = border;

            var hit = Instantiate(treeNodeHitPrefab, hitParent, false);
            hit.name = $"HitNode_{x}_{y}";
            hit.SetAppearance(view.Background, BtnBg, BtnHover, preserve: true, selected: BtnSelected);
            var slot = new TreeNodeSlot
            {
                visualGo = view.gameObject,
                hitGo = hit.gameObject,
                hitRt = (RectTransform)hit.transform,
                borderImg = view.Border,
                fillImg = view.Background,
                topText = view.Classification,
                nameText = view.VehicleName,
                idleBorder = border,
                contentX = x,
                contentY = y
            };

            hit.Bind(() =>
            {
                if (_treeDragAccumDist <= 7f && slot.vehicle != null && !string.IsNullOrEmpty(slot.vehicle.id))
                    SelectVehicle(slot.vehicle.id, closeTreeIfSame: true);
            });
            return slot;
        }

        // Horizontal WoT Tech Tree Layout Constants (all EVEN integer pixels!)
        private const int ViewX = 8;
        private const int ViewY = 70;
        private const int ViewW = 464;
        private const int ViewH = 260;
        private const int NodeW = 104;
        private const int NodeH = 30;
        private const int ColGap = 20;
        private const int ColStep = NodeW + ColGap; // 124px per Tier column!
        private const int PadX = 16;
        private const int HistoryColumns = 8;
        private const int ContentW = PadX * 2 + (HistoryColumns + VehicleDef.SpecialTier) * ColStep - ColGap;
        private const float MaxScrollX = ContentW - ViewW;

        private string _treeNation = "USA";
        private bool _treeOpen = false;

        // Horizontal Tree Scroll state
        private float _treeScrollX = 0f;
        private float _targetScrollX = 0f;
        private bool _draggingTree;
        private float _treeDragStartScreenX;
        private float _treeDragStartScroll;
        private float _treeDragAccumDist;

        private const int ScrollTrackX = 32;
        private const int ScrollTrackW = 416;

        // Tech Tree UI
        private static readonly (string Code, string Label)[] TreeNations =
        {
            ("USA", "USA"), ("GER", "GERMANY"), ("USSR", "USSR / RU"), ("JPN", "JAPAN")
        };
        private readonly Dictionary<string, Image> _nationButtons = new();
        private readonly Dictionary<string, GameObject> _nationLinks = new();

        private class TreeNodeSlot
        {
            public GameObject visualGo;
            public GameObject hitGo;
            public RectTransform hitRt;
            public Image borderImg;
            public Image fillImg;
            public Text topText;
            public Text nameText;
            public VehicleDef vehicle;
            public Color idleBorder;
            public int contentX;
            public int contentY; // local inside viewport [0..ViewH]
        }

        private readonly Dictionary<string, TreeNodeSlot> _treeNodes = new();

        private void ScrollTreeToSelected(bool instant = false)
        {
            VehicleDef sel = GetSelectedVehicle();
            if (sel == null || sel.nation != _treeNation)
            {
                _targetScrollX = 0f;
            }
            else
            {
                int col = TreeColumn(sel);
                float nodeCenterX = PadX + col * ColStep + NodeW * 0.5f;
                _targetScrollX = Mathf.Clamp(nodeCenterX - ViewW * 0.5f, 0f, MaxScrollX);
            }
            if (instant)
            {
                _treeScrollX = _targetScrollX;
                ApplyTreeScrollVisuals();
            }
        }

        private void ApplyTreeScrollVisuals()
        {
            int scrollInt = Mathf.RoundToInt(_treeScrollX);
            if (_treeContentRt != null)
                _treeContentRt.anchoredPosition = new Vector2(-scrollInt, 0f);

            // Sync pixel scrollbar thumb
            if (_scrollThumbRt != null)
            {
                int thumbW = Mathf.RoundToInt(ScrollTrackW * ((float)ViewW / ContentW));
                int travel = ScrollTrackW - thumbW;
                int thumbX = ScrollTrackX + Mathf.RoundToInt((scrollInt / Mathf.Max(1f, MaxScrollX)) * travel);
                _scrollThumbRt.anchoredPosition = new Vector2(thumbX, 58);
                _scrollThumbRt.sizeDelta = new Vector2(thumbW, 6);
            }

            // Dynamically sync every visible node's click hitbox clipped cleanly to the scroll viewport!
            foreach (var slot in _treeNodes.Values)
            {
                if (slot == null || slot.hitGo == null) continue;
                bool activeItem = slot.vehicle.nation == _treeNation;
                if (!activeItem)
                {
                    slot.hitGo.SetActive(false);
                    continue;
                }

                float sx0 = ViewX + slot.contentX - scrollInt;
                float sx1 = sx0 + NodeW;
                float clipX0 = Mathf.Max(ViewX, sx0);
                float clipX1 = Mathf.Min(ViewX + ViewW, sx1);

                if (clipX1 - clipX0 < 4f)
                {
                    slot.hitGo.SetActive(false);
                }
                else
                {
                    slot.hitGo.SetActive(true);
                    float sy0 = ViewY + slot.contentY;
                    float sy1 = sy0 + NodeH;
                    slot.hitRt.anchorMin = new Vector2(clipX0 / PixelWindowbox.RenderWidth, sy0 / PixelWindowbox.RenderHeight);
                    slot.hitRt.anchorMax = new Vector2(clipX1 / PixelWindowbox.RenderWidth, sy1 / PixelWindowbox.RenderHeight);
                    slot.hitRt.offsetMin = Vector2.zero;
                    slot.hitRt.offsetMax = Vector2.zero;
                }
            }
        }

        private void SetTechTreeOpen(bool open)
        {
            if (open && _settingsOpen) SetSettingsOpen(false);
            if (open && _deployOpen) SetDeployModalOpen(false);
            if (open) ResetArmorPreviewDamage();
            _treeOpen = open;
            if (_treeVisualGroup != null) _treeVisualGroup.SetActive(open && !_titleOpen);
            if (_garageVisualGroup != null) _garageVisualGroup.SetActive(!open && !_titleOpen);
            if (_hangarXrayWrap != null) _hangarXrayWrap.SetActive(_xrayMode && !open && !_titleOpen);
            if (showcaseTank != null) showcaseTank.gameObject.SetActive(!_xrayMode || open || _titleOpen);
            SyncActiveHitGroups();
            if (open)
            {
                VehicleDef cur = GetSelectedVehicle();
                if (cur != null) _treeNation = cur.nation;
                RefreshAllUI();
                ScrollTreeToSelected(instant: true);
            }
            else
            {
                RefreshAllUI();
            }
        }

        private string GetNationTabLabel(string nation, string title)
        {
            int count = Array.FindAll(_vehicleCatalog.Vehicles, vehicle => vehicle != null && vehicle.nation == nation).Length;
            return $"{title} ({count})";
        }

        private static string FormatTreeShortName(string raw, int maxChars)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string s = raw
                .Replace("Kanonenjagdpanzer", "KanJPz")
                .Replace("M41 Walker Bulldog", "M41 Bulldog")
                .Replace("M41 (Bundeswehr)", "M41 Bundeswehr")
                .Replace("Ru 251 Prototype", "Ru 251")
                .Replace("Spahpanzer ", "")
                .Replace("Raketenjagdpanzer ", "RAKJPZ ")
                .Replace("M1128 Stryker MGS", "STRYKER MGS")
                .Replace("BMPT Terminator", "BMPT TERM.")
                .Replace("2S25M Sprut-SDM1", "SPRUT-SDM1")
                .Replace("2S25 Sprut-SD", "SPRUT-SD")
                .ToUpper();
            return s.Length <= maxChars ? s : s.Substring(0, maxChars);
        }

        private void UpdateTreeScroll(Keyboard keyboard, Mouse mouse, float dt)
        {
            if (keyboard != null)
            {
                float keyDir = 0f;
                if (keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed) keyDir -= 1f;
                if (keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed) keyDir += 1f;
                if (Mathf.Abs(keyDir) > 0.01f)
                    _targetScrollX = Mathf.Clamp(_targetScrollX + keyDir * 360f * dt, 0f, MaxScrollX);
            }

            if (mouse != null)
            {
                Vector2 wheel = mouse.scroll.ReadValue();
                float wheelDelta = Mathf.Abs(wheel.x) > 0.01f ? wheel.x : -wheel.y;
                if (Mathf.Abs(wheelDelta) > 0.01f)
                {
                    float step = Mathf.Sign(wheelDelta) * 86f;
                    _targetScrollX = Mathf.Clamp(_targetScrollX + step, 0f, MaxScrollX);
                }

                // Click-drag horizontal panning inside tree viewport
                Vector2 screenPos = mouse.position.ReadValue();
                bool inTreeBand = _windowbox.TryScreenToViewport(screenPos, out Vector2 uv)
                                  && uv.y * PixelWindowbox.RenderHeight >= ViewY - 12
                                  && uv.y * PixelWindowbox.RenderHeight <= ViewY + ViewH;

                if (mouse.leftButton.wasPressedThisFrame && inTreeBand)
                {
                    _draggingTree = true;
                    _treeDragStartScreenX = screenPos.x;
                    _treeDragStartScroll = _targetScrollX;
                    _treeDragAccumDist = 0f;
                }
                if (mouse.leftButton.wasReleasedThisFrame)
                    _draggingTree = false;

                if (_draggingTree)
                {
                    float dxScreen = screenPos.x - _treeDragStartScreenX;
                    _treeDragAccumDist = Mathf.Max(_treeDragAccumDist, Mathf.Abs(dxScreen));
                    float scaleToVirtual = PixelWindowbox.RenderWidth / Mathf.Max(1f, _windowbox.HitOverlayRect.rect.width);
                    _targetScrollX = Mathf.Clamp(_treeDragStartScroll - dxScreen * scaleToVirtual, 0f, MaxScrollX);
                }
            }

            _treeScrollX = Mathf.Lerp(_treeScrollX, _targetScrollX, 1f - Mathf.Exp(-14f * dt));
            if (Mathf.Abs(_treeScrollX - _targetScrollX) < 0.3f) _treeScrollX = _targetScrollX;
            ApplyTreeScrollVisuals();
        }

        private void RefreshTechTreeUI(VehicleDef sel)
        {
            if (sel != null)
            {
                if (_treeDetailTitle != null)
                    _treeDetailTitle.text = $"{sel.TierRoman}  {sel.name.ToUpper()}  ({sel.role} / {sel.WeaponLabel})";
                if (_treeDetailStats != null)
                    _treeDetailStats.text = $"SPD {(int)sel.speed}  TRN {(int)sel.EffectiveHullTurnSpeed}  TUR {(int)sel.EffectiveTurretTurnSpeed}  PEN {(int)sel.penetration}  RLD {sel.reload:F2}s  {sel.engine.ToUpper()}";
            }

            foreach (var (nation, button) in _nationButtons)
                button.color = _treeNation == nation ? BtnSelected : BtnBg;
            foreach (var (nation, links) in _nationLinks)
                links.SetActive(_treeNation == nation);

            foreach (var slot in _treeNodes.Values)
            {
                VehicleDef vehicle = slot.vehicle;
                bool visible = vehicle.nation == _treeNation;
                slot.visualGo.SetActive(visible);
                if (!visible) continue;

                bool selected = vehicle.id == _sessionState.SelectedPlayerVehicleId;
                string tag = vehicle.year > 0 ? vehicle.year.ToString() : vehicle.TierRoman;
                slot.topText.text = $"{tag}  {vehicle.role}";
                slot.topText.color = selected || vehicle.IsSpecial ? GoldColor : TextMuted;
                slot.nameText.text = FormatTreeShortName(vehicle.name, 15);
                slot.nameText.color = selected ? GoldColor : TextLight;
                slot.fillImg.color = selected ? BtnSelected : BtnBg;
                slot.borderImg.color = selected ? GoldColor : slot.idleBorder;
            }
            if (_treeOpen) ApplyTreeScrollVisuals();
        }

    }
}
