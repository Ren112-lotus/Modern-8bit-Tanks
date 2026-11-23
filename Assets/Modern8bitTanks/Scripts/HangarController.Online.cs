using System;
using UnityEngine;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    public partial class HangarController
    {
        private GameObject _onlineVisuals;
        private GameObject _onlineHits;
        private GameObject _onlineEntryVisuals;
        private GameObject _onlineEntryHits;
        private GameObject _onlineStartVisuals;
        private GameObject _onlineStartHits;
        private GameObject _onlineSelectVisuals;
        private GameObject _onlineSelectHits;
        private InputField _onlineCode;
        private Text _onlineStatus;
        private Text _onlineCodeLabel;
        private float _nextOnlineRefresh;
        private Text _onlineRoster;
        private Text _onlineVehicle;
        private Text _onlineBack;
        private Text _garageTitleButton;
        private Text _garageDeployButton;
        private Text _treeDeployButton;
        private string _onlineError;
        private bool _onlineOpen;
        private bool _onlineBusy;
        private bool _waitingForVehicleSelection;
        private bool _choosingOnlineVehicle;

        private void BuildOnlineRoomUI()
        {
            _onlineVisuals = new GameObject("OnlineRoomVisuals") { layer = 5 };
            var vp = _onlineVisuals.AddComponent<RectTransform>();
            vp.SetParent(_hudRoot, false);
            Stretch(vp);

            _onlineHits = CreateHitGroup("OnlineRoomHits");
            Transform hp = _onlineHits.transform;
            const int w = 288;
            const int h = 184;
            int x = (renderWidth - w) / 2;
            int y = (renderHeight - h) / 2;

            CreatePixelSolid(vp, "OnlineRoomDim", 0, 0, renderWidth, renderHeight, new Color(.03f, .03f, .03f, .76f));
            var card = CreateTacticalCard(vp, "OnlineRoomCard", x, y, w, h, PanelGlassModal, BrassDim, LightCyan);
            MilitaryUiSprites.CreateIcon(card, "OnlineIcon", MilitaryUiSprites.IconCrosshair, 15, 154, LightCyan);
            CreatePixelText(card, "ONLINE BATTLE", 10, GoldColor, TextAnchor.MiddleCenter, 0, 150, w, 22, useTitleFont: true);
            _onlineCodeLabel = CreatePixelText(card, "", 10, GoldColor, TextAnchor.MiddleCenter, 12, 130, w - 24, 18);
            _onlineStatus = CreatePixelText(card, "", 8, TextLight, TextAnchor.MiddleCenter, 12, 112, w - 24, 17);
            _onlineRoster = CreatePixelText(card, "", 8, LightCyan, TextAnchor.MiddleCenter, 12, 83, w - 24, 26);
            _onlineVehicle = CreatePixelText(card, "", 8, TextLight, TextAnchor.MiddleCenter, 12, 60, w - 24, 18);

            _onlineEntryVisuals = new GameObject("RoomEntryVisuals") { layer = 5 };
            _onlineEntryVisuals.transform.SetParent(vp, false);
            var entry = _onlineEntryVisuals.AddComponent<RectTransform>();
            Stretch(entry);
            _onlineEntryHits = CreateHitGroup("RoomEntryHits");
            _onlineEntryHits.transform.SetParent(hp, false);
            Stretch((RectTransform)_onlineEntryHits.transform);

            MilitaryUiSprites.CreateSlicedSprite(entry, "CodeField", MilitaryUiSprites.SteelButton,
                x + 12, y + 62, 172, 22, BtnBg, out _);
            BuildOnlineCodeInput(_onlineEntryHits.transform, x + 16, y + 62, 164, 22);
            CreatePixelButton(entry, _onlineEntryHits.transform, "JOIN", x + 192, y + 62, 84, 22,
                BtnBg, LightCyan, LightCyan, 8, () => ConnectOnlineRoom(false));
            CreatePrimaryCtaButton(entry, _onlineEntryHits.transform, "CREATE ROOM", x + 12, y + 34, w - 24, 22,
                () => ConnectOnlineRoom(true));

            _onlineStartVisuals = new GameObject("RoomStartVisuals") { layer = 5 };
            _onlineStartVisuals.transform.SetParent(vp, false);
            Stretch(_onlineStartVisuals.AddComponent<RectTransform>());
            _onlineStartHits = CreateHitGroup("RoomStartHits");
            _onlineStartHits.transform.SetParent(hp, false);
            Stretch((RectTransform)_onlineStartHits.transform);
            CreatePrimaryCtaButton(_onlineStartVisuals.transform, _onlineStartHits.transform, "START MATCH",
                x + 148, y + 34, 128, 22, StartOnlineMatch);

            _onlineSelectVisuals = new GameObject("RoomVehicleVisuals") { layer = 5 };
            _onlineSelectVisuals.transform.SetParent(vp, false);
            Stretch(_onlineSelectVisuals.AddComponent<RectTransform>());
            _onlineSelectHits = CreateHitGroup("RoomVehicleHits");
            _onlineSelectHits.transform.SetParent(hp, false);
            Stretch((RectTransform)_onlineSelectHits.transform);
            CreatePixelButton(_onlineSelectVisuals.transform, _onlineSelectHits.transform, "SELECT TANK",
                x + 12, y + 34, 128, 22, BtnBg, LightCyan, LightCyan, 8, BeginOnlineVehicleSelection);

            _onlineBack = CreatePixelButton(vp, hp, "BACK", x + 12, y + 8, w - 24, 20,
                BtnBg, PanelBorder, TextLight, 8, CloseOnlineRoom);
            SetOnlineRoomOpen(false);
        }

        private void BuildOnlineCodeInput(Transform parent, int x, int y, int w, int h)
        {
            var root = new GameObject("JoinCodeInput", typeof(RectTransform), typeof(Image), typeof(InputField));
            root.transform.SetParent(parent, false);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = new Vector2((float)x / renderWidth, (float)y / renderHeight);
            rect.anchorMax = new Vector2((float)(x + w) / renderWidth, (float)(y + h) / renderHeight);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = root.GetComponent<Image>();
            image.color = Color.clear;

            var value = CreateOnlineInputText(root.transform, "Code", "", TextLight);
            var placeholder = CreateOnlineInputText(root.transform, "Placeholder", "ENTER JOIN CODE", TextMuted);
            _onlineCode = root.GetComponent<InputField>();
            _onlineCode.targetGraphic = image;
            _onlineCode.transition = Selectable.Transition.None;
            _onlineCode.textComponent = value;
            _onlineCode.placeholder = placeholder;
            _onlineCode.characterLimit = 12;
            _onlineCode.lineType = InputField.LineType.SingleLine;
            _onlineCode.onValidateInput = (_, _, c) =>
                c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' ? char.ToUpperInvariant(c) : '\0';
        }

        private Text CreateOnlineInputText(Transform parent, string name, string content, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Stretch((RectTransform)go.transform);
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = _font;
            text.fontSize = 22;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 9;
            text.resizeTextMaxSize = 22;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        private void SetOnlineRoomOpen(bool open)
        {
            _onlineOpen = open;
            _onlineVisuals.SetActive(open);
            SyncActiveHitGroups();
            _nextOnlineRefresh = 0f;
            if (open) RefreshOnlineRoom();
        }

        private void RefreshOnlineRoom()
        {
            if (Time.unscaledTime < _nextOnlineRefresh) return;
            _nextOnlineRefresh = Time.unscaledTime + 0.1f;
            var notice = MultiplayerSessionConnection.TakeNotice();
            if (notice != null) _onlineError = notice;
            var connection = MultiplayerSessionConnection.Active;
            var room = connection != null ? connection.Room : null;
            var inRoom = room != null && room.IsSpawned;
            if (_waitingForVehicleSelection && inRoom && room.IsLocalChoosingVehicle && !connection.IsStarting)
            {
                _waitingForVehicleSelection = false;
                SetOnlineVehicleSelection(true);
                return;
            }
            if (!inRoom || connection.IsStarting) _waitingForVehicleSelection = false;
            var busy = _onlineBusy || _waitingForVehicleSelection || (connection != null && connection.IsLeaving);
            var canStart = inRoom && connection.IsHost && room.CanDeploy && !connection.IsStarting && !busy;
            _onlineEntryVisuals.SetActive(!inRoom && !busy);
            _onlineEntryHits.SetActive(!inRoom && !busy);
            _onlineStartVisuals.SetActive(canStart);
            _onlineStartHits.SetActive(canStart);
            var canSelect = inRoom && !connection.IsStarting && !busy;
            _onlineSelectVisuals.SetActive(canSelect);
            _onlineSelectHits.SetActive(canSelect);
            _onlineBack.text = inRoom ? "LEAVE ROOM" : "BACK";
            _onlineCodeLabel.text = inRoom && !string.IsNullOrEmpty(connection.JoinCode) ? $"CODE {connection.JoinCode}" : "";
            _onlineRoster.text = inRoom
                ? $"ALPHA {room.CountTeam(true)}/4    BRAVO {room.CountTeam(false)}/4\nYOUR TEAM: {(room.LocalSlot < MultiplayerSessionConnection.MaxPlayers / 2 ? "ALPHA" : "BRAVO")}" 
                : "";
            _onlineVehicle.text = inRoom && GetSelectedVehicle() is { } vehicle ? $"YOUR TANK: {vehicle.name.ToUpperInvariant()}" : "";
            if (connection != null && connection.IsLeaving)
                _onlineStatus.text = "LEAVING ROOM...";
            else if (busy)
                _onlineStatus.text = inRoom ? "UPDATING ROOM..." : "CONNECTING...";
            else if (!string.IsNullOrEmpty(_onlineError))
                _onlineStatus.text = _onlineError;
            else if (!inRoom)
                _onlineStatus.text = "CREATE A ROOM OR JOIN A FRIEND";
            else if (connection.IsStarting)
                _onlineStatus.text = "LOADING BATTLE...";
            else
            {
                string nextAction = connection.IsHost
                    ? (room.PlayerCount < 2 ? "WAITING FOR A PLAYER" : room.CanDeploy ? "READY TO START" : "WAITING FOR TANK SELECTION")
                    : "WAITING FOR HOST";
                _onlineStatus.text = nextAction;
            }
        }

        private void BeginOnlineVehicleSelection()
        {
            var connection = MultiplayerSessionConnection.Active;
            var room = connection != null ? connection.Room : null;
            if (room == null || connection.IsStarting || connection.IsLeaving || _onlineBusy) return;
            _waitingForVehicleSelection = true;
            _onlineError = null;
            room.SetVehicleSelection(BattleSessionState.SelectedPlayerVehicleId, choosing: true);
        }

        private void SetOnlineVehicleSelection(bool choosing)
        {
            _choosingOnlineVehicle = choosing;
            _garageTitleButton.text = choosing ? "ROOM" : "TITLE";
            _garageDeployButton.text = choosing ? "CONFIRM TANK" : "BATTLE!";
            _treeDeployButton.text = _garageDeployButton.text;
            if (choosing) SetTitleOpen(false);
            else SetTechTreeOpen(false);
            SetOnlineRoomOpen(!choosing);
        }

        private void ConfirmOnlineVehicle()
        {
            var connection = MultiplayerSessionConnection.Active;
            if (connection != null && connection.Room != null && !connection.IsLeaving)
                connection.Room.SetVehicleSelection(BattleSessionState.SelectedPlayerVehicleId, choosing: false);
            SetOnlineVehicleSelection(false);
        }

        private async void ConnectOnlineRoom(bool host)
        {
            if (_onlineBusy || MultiplayerSessionConnection.Active != null) return;
            var code = host ? null : _onlineCode.text.Trim().ToUpperInvariant();
            if (!host && string.IsNullOrEmpty(code))
            {
                _onlineError = "ENTER A JOIN CODE.";
                return;
            }

            _onlineBusy = true;
            _onlineError = null;
            try { await MultiplayerSessionConnection.ConnectAsync(code); }
            catch (Exception exception)
            {
                _onlineError = host ? "COULD NOT CREATE ROOM. TRY AGAIN." : "COULD NOT JOIN. CHECK THE CODE AND TRY AGAIN.";
                Debug.LogWarning(exception.Message);
            }
            finally { _onlineBusy = false; }
        }

        private async void StartOnlineMatch()
        {
            var connection = MultiplayerSessionConnection.Active;
            if (_onlineBusy || connection == null) return;
            _onlineBusy = true;
            _onlineError = null;
            try { await connection.StartMatchAsync(); }
            catch (Exception exception)
            {
                _onlineError = "COULD NOT START MATCH. TRY AGAIN.";
                Debug.LogWarning(exception.Message);
            }
            finally { _onlineBusy = false; }
        }

        private async void CloseOnlineRoom()
        {
            if (_onlineBusy) return;
            _waitingForVehicleSelection = false;
            var connection = MultiplayerSessionConnection.Active;
            if (connection != null)
            {
                _onlineBusy = true;
                try { await connection.LeaveAsync(); }
                finally { _onlineBusy = false; }
            }

            if (this == null) return;
            _onlineError = null;
            SetOnlineRoomOpen(false);
        }
    }
}
