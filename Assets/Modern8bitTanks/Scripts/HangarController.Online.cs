using Cysharp.Threading.Tasks;
using System;
using UnityEngine;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    public partial class HangarController
    {
        [SerializeField] private MultiplayerSessionConnection multiplayerSessionPrefab;
        [SerializeField] private GameObject _onlineVisuals;
        [SerializeField] private GameObject _onlineHits;
        [SerializeField] private GameObject _onlineEntryVisuals;
        [SerializeField] private GameObject _onlineEntryHits;
        [SerializeField] private GameObject _onlineStartVisuals;
        [SerializeField] private GameObject _onlineStartHits;
        [SerializeField] private GameObject _onlineSelectVisuals;
        [SerializeField] private GameObject _onlineSelectHits;
        [SerializeField] private InputField _onlineCode;
        [SerializeField] private Text _onlineStatus;
        [SerializeField] private Text _onlineCodeLabel;
        private float _nextOnlineRefresh;
        [SerializeField] private Text _onlineRoster;
        [SerializeField] private Text _onlineVehicle;
        [SerializeField] private Text _onlineBack;
        [SerializeField] private Text _garageTitleButton;
        [SerializeField] private Text _garageDeployButton;
        [SerializeField] private Text _treeDeployButton;
        private string _onlineError;
        private bool _onlineOpen;
        private bool _onlineBusy;
        private bool _waitingForVehicleSelection;
        private bool _choosingOnlineVehicle;

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
            var notice = _multiplayer.TakeNotice();
            if (notice != null) _onlineError = notice;
            var connection = _multiplayer.Connection;
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
            _onlineBack.text = UiText.Get(inRoom ? "LEAVE ROOM" : "BACK");
            _onlineCodeLabel.text = inRoom && !string.IsNullOrEmpty(connection.JoinCode) ? UiText.Format("CODE {0}", connection.JoinCode) : "";
            _onlineRoster.text = inRoom
                ? $"ALPHA {room.CountTeam(true)}/4    BRAVO {room.CountTeam(false)}/4\nYOUR TEAM: {(room.LocalSlot < MultiplayerSessionConnection.MaxPlayers / 2 ? "ALPHA" : "BRAVO")}" 
                : "";
            _onlineVehicle.text = inRoom && GetSelectedVehicle() is { } vehicle
                ? UiText.Format("YOUR TANK: {0}", vehicle.name.ToUpperInvariant()) : "";
            if (connection != null && connection.IsLeaving)
                _onlineStatus.text = UiText.Get("LEAVING ROOM...");
            else if (busy)
                _onlineStatus.text = UiText.Get(inRoom ? "UPDATING ROOM..." : "CONNECTING...");
            else if (!string.IsNullOrEmpty(_onlineError))
                _onlineStatus.text = UiText.Get(_onlineError);
            else if (!inRoom)
                _onlineStatus.text = UiText.Get("CREATE A ROOM OR JOIN A FRIEND");
            else if (connection.IsStarting)
                _onlineStatus.text = UiText.Get("LOADING BATTLE...");
            else
            {
                string nextAction = connection.IsHost
                    ? (room.PlayerCount < 2 ? "WAITING FOR A PLAYER" : room.CanDeploy ? "READY TO START" : "WAITING FOR TANK SELECTION")
                    : "WAITING FOR HOST";
                _onlineStatus.text = UiText.Get(nextAction);
            }
        }

        private void BeginOnlineVehicleSelection()
        {
            var connection = _multiplayer.Connection;
            var room = connection != null ? connection.Room : null;
            if (room == null || connection.IsStarting || connection.IsLeaving || _onlineBusy) return;
            _waitingForVehicleSelection = true;
            _onlineError = null;
            room.SetVehicleSelection(_sessionState.SelectedPlayerVehicleId, choosing: true);
        }

        private void SetOnlineVehicleSelection(bool choosing)
        {
            _choosingOnlineVehicle = choosing;
            _garageTitleButton.text = UiText.Get(choosing ? "ROOM" : "TITLE");
            _garageDeployButton.text = UiText.Get(choosing ? "CONFIRM TANK" : "BATTLE!");
            _treeDeployButton.text = _garageDeployButton.text;
            if (choosing) SetTitleOpen(false);
            else SetTechTreeOpen(false);
            SetOnlineRoomOpen(!choosing);
        }

        private void ConfirmOnlineVehicle()
        {
            var connection = _multiplayer.Connection;
            if (connection != null && connection.Room != null && !connection.IsLeaving)
                connection.Room.SetVehicleSelection(_sessionState.SelectedPlayerVehicleId, choosing: false);
            SetOnlineVehicleSelection(false);
        }

        private void ConnectOnlineRoom(bool host) => ConnectOnlineRoomAsync(host).Forget();

        private async UniTask ConnectOnlineRoomAsync(bool host)
        {
            if (_onlineBusy || _multiplayer.Connection != null) return;
            var code = host ? null : _onlineCode.text.Trim().ToUpperInvariant();
            if (!host && string.IsNullOrEmpty(code))
            {
                _onlineError = "ENTER A JOIN CODE.";
                return;
            }

            _onlineBusy = true;
            _onlineError = null;
            try { await multiplayerSessionPrefab.ConnectAsync(_sessionState, _multiplayer, _sceneLoader, code); }
            catch (Exception exception)
            {
                _onlineError = host ? "COULD NOT CREATE ROOM. TRY AGAIN." : "COULD NOT JOIN. CHECK THE CODE AND TRY AGAIN.";
                Debug.LogWarning(exception.Message);
            }
            finally { _onlineBusy = false; }
        }

        private void StartOnlineMatch() => StartOnlineMatchAsync().Forget();

        private async UniTask StartOnlineMatchAsync()
        {
            var connection = _multiplayer.Connection;
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

        private void CloseOnlineRoom() => CloseOnlineRoomAsync().Forget();

        private async UniTask CloseOnlineRoomAsync()
        {
            if (_onlineBusy) return;
            _waitingForVehicleSelection = false;
            var connection = _multiplayer.Connection;
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
