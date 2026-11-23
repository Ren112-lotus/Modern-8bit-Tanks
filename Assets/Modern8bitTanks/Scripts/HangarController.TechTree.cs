using System;

using UnityEngine;
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

            foreach (var vehicle in _allVehicles)
            {
                int x = PadX + TreeColumn(vehicle) * ColStep;
                Color border = TreeLineColor(vehicle);
                var slot = CreateTreeNodeSlot(_treeContentRt, _treeHitsGroup.transform, x, TreeRow(vehicle), border);
                slot.vehicleId = vehicle.id;
                _treeNodes.Add(vehicle.id, slot);
            }

            foreach (var vehicle in _allVehicles)
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
                return _vehicleMap.TryGetValue(vehicle.treeFrom, out var linked) && linked.nation == vehicle.nation
                    ? linked : null;

            if (vehicle.historyColumn > 0) return null;
            VehicleDef previous = null;
            foreach (var other in _allVehicles)
            {
                if (other.nation != vehicle.nation || other.historyColumn > 0 || other.tier >= vehicle.tier)
                    continue;
                if (other.tab == vehicle.tab && (previous == null || previous.tier < other.tier))
                    previous = other;
            }

            if (previous != null || vehicle.tab == "T1_5") return previous;
            foreach (var other in _allVehicles)
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
                if (_treeDragAccumDist <= 7f && !string.IsNullOrEmpty(slot.vehicleId))
                    SelectVehicle(slot.vehicleId, closeTreeIfSame: true);
            });
            return slot;
        }
    }
}
