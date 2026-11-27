using Cysharp.Threading.Tasks;
using System;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
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
        [SerializeField] private InputField _onlineName;
        [SerializeField] private Text _onlineStatus;
        [SerializeField] private Text _onlineCodeLabel;
        private float _nextOnlineRefresh;
        [SerializeField] private Text _onlineFriendlyRoster;
        [SerializeField] private Text _onlineHostileRoster;
        [SerializeField] private Text _onlineVehicle;
        [SerializeField] private Text _onlineBack;
        [SerializeField] private Text _garageTitleButton;
        [SerializeField] private Text _garageDeployButton;
        [SerializeField] private Text _treeDeployButton;

        private OnlineModalUi _onlineModalUi;
        private string _onlineError;
        private bool _onlineOpen;
        private bool _onlineBusy;
        private bool _waitingForVehicleSelection;
        private bool _choosingOnlineVehicle;
        private int _onlineNameCancelFrame = -1;
        private int _onlineCodeCancelFrame = -1;
        private float _codeCopiedUntil = -1f;

        private void InitializeOnlineProfile()
        {
            _onlineName.characterLimit = MultiplayerSessionState.MaxDisplayNameLength;
            _onlineName.SetTextWithoutNotify(_multiplayer.DisplayName);
            _onlineName.onValueChanged.AddListener(_ => _onlineError = null);
            _onlineName.onEndEdit.AddListener(value =>
            {
                if (_onlineName.wasCanceled)
                {
                    _onlineNameCancelFrame = Time.frameCount;
                    return;
                }
                SaveOnlineDisplayName(value);
            });

            _onlineCode.characterLimit = 12;
            _onlineCode.onValueChanged.AddListener(raw =>
            {
                string clean = SanitizeJoinCode(raw);
                if (clean != raw) _onlineCode.SetTextWithoutNotify(clean);
                if (!string.IsNullOrEmpty(_onlineError)) _onlineError = null;
                _nextOnlineRefresh = 0f;
            });
            _onlineCode.onEndEdit.AddListener(value =>
            {
                if (_onlineCode.wasCanceled)
                {
                    _onlineCodeCancelFrame = Time.frameCount;
                    return;
                }
                var kb = Keyboard.current;
                bool submitted = kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
                if (submitted && !string.IsNullOrEmpty(SanitizeJoinCode(value)))
                    ConnectOnlineRoom(host: false);
            });

            _onlineFriendlyRoster.color = OnlineTeamColors.Friendly;
            _onlineHostileRoster.color = OnlineTeamColors.Hostile;

            _onlineModalUi = new OnlineModalUi(
                _font,
                _pixelCanvas,
                _onlineVisuals,
                _onlineHits,
                _onlineEntryVisuals,
                _onlineEntryHits,
                _onlineStartVisuals,
                _onlineStartHits,
                _onlineSelectVisuals,
                _onlineSelectHits,
                _onlineCode,
                _onlineName,
                _onlineStatus,
                _onlineCodeLabel,
                _onlineFriendlyRoster,
                _onlineHostileRoster,
                _onlineVehicle,
                _onlineBack,
                onlineJoinAction,
                onlineCreateAction,
                onlineStartAction,
                onlineVehicleAction,
                onlineBackAction,
                CopyOnlineJoinCode,
                PasteOnlineJoinCode,
                SwitchOnlineTeam);
        }

        private static string SanitizeJoinCode(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new StringBuilder(Mathf.Min(12, raw.Length));
            for (int i = 0; i < raw.Length && sb.Length < 12; i++)
            {
                char c = raw[i];
                if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') sb.Append(c);
                else if (c is >= 'a' and <= 'z') sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }

        private void CopyOnlineJoinCode()
        {
            var connection = _multiplayer.Connection;
            if (connection == null || string.IsNullOrEmpty(connection.JoinCode)) return;
            GUIUtility.systemCopyBuffer = connection.JoinCode;
            _codeCopiedUntil = Time.unscaledTime + 1.8f;
            PlayHangarPreviewSfx();
            _nextOnlineRefresh = 0f;
            RefreshOnlineRoom();
        }

        private void PasteOnlineJoinCode()
        {
            if (_onlineBusy || _multiplayer.Connection != null) return;
            string clean = SanitizeJoinCode(GUIUtility.systemCopyBuffer);
            if (string.IsNullOrEmpty(clean))
            {
                _onlineError = "CLIPBOARD EMPTY.";
                _nextOnlineRefresh = 0f;
                RefreshOnlineRoom();
                return;
            }
            _onlineError = null;
            _onlineCode.SetTextWithoutNotify(clean);
            PlayHangarPreviewSfx();
            _nextOnlineRefresh = 0f;
            RefreshOnlineRoom();
        }

        private void SwitchOnlineTeam()
        {
            var connection = _multiplayer.Connection;
            var room = connection != null ? connection.Room : null;
            if (room == null || connection.IsStarting || connection.IsLeaving || _onlineBusy || !room.CanSwitchTeam) return;
            _onlineError = null;
            room.SwitchTeam();
            PlayHangarPreviewSfx();
            _nextOnlineRefresh = 0f;
        }

        private void SaveOnlineDisplayName(string value)
        {
            string name = _multiplayer.SaveDisplayName(value);
            _onlineName.SetTextWithoutNotify(name);
            var room = _multiplayer.Connection != null ? _multiplayer.Connection.Room : null;
            if (room != null && room.IsSpawned) room.SetDisplayName(name);
        }

        private string OnlineTeamRoster(MultiplayerRoom room, int localSlot, bool friendly)
        {
            const int teamSize = MultiplayerSessionConnection.PlayersPerTeam;
            bool alpha = MultiplayerSessionConnection.IsAlphaSlot(localSlot) == friendly;
            int firstSlot = alpha ? 0 : teamSize;
            var roster = new StringBuilder(UiText.Format(friendly ? "ALLIES {0}/{1}" : "ENEMIES {0}/{1}", room.CountTeam(alpha), teamSize));
            for (int slot = firstSlot; slot < firstSlot + teamSize; slot++)
            {
                roster.Append('\n');
                if (!room.TryGetMemberAtSlot(slot, out var member))
                {
                    roster.Append("---");
                    continue;
                }

                string prefix = slot == localSlot ? ">" : (room.IsHostSlot(slot) ? "*" : " ");
                string rawName = member.DisplayName.IsEmpty
                    ? UiText.Format("PLAYER {0}", slot + 1)
                    : member.DisplayName.ToString();
                string shortName = TruncateLabel(rawName, 9);

                string tag;
                if (member.ChoosingVehicle)
                    tag = UiText.Get("SELECTING...");
                else
                    tag = FormatRosterVehicleTag(member.VehicleId.ToString());

                roster.Append(prefix).Append(shortName);
                if (!string.IsNullOrEmpty(tag))
                    roster.Append(" [").Append(tag).Append(']');
            }
            return roster.ToString();
        }

        private string FormatRosterVehicleTag(string vehicleId)
        {
            if (string.IsNullOrEmpty(vehicleId)) return "";
            var def = _vehicleCatalog.Find(vehicleId);
            if (def != null && !string.IsNullOrEmpty(def.name))
                return TruncateLabel(FormatTreeShortName(def.name, 7), 7);
            return TruncateLabel(vehicleId.ToUpperInvariant(), 7);
        }

        private static string TruncateLabel(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxChars) return value;
            return value.Substring(0, maxChars);
        }

        private void HandleOnlineKeyboard(Keyboard kb)
        {
            if (kb == null) return;
            bool typing = (_onlineName != null && _onlineName.isFocused) || (_onlineCode != null && _onlineCode.isFocused);
            bool canceledThisFrame = _onlineNameCancelFrame == Time.frameCount || _onlineCodeCancelFrame == Time.frameCount;

            if (kb.escapeKey.wasPressedThisFrame && !typing && !canceledThisFrame)
            {
                CloseOnlineRoom();
                return;
            }

            if (!typing && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
            {
                var connection = _multiplayer.Connection;
                var room = connection != null ? connection.Room : null;
                if (room != null && room.IsSpawned && connection.IsHost && room.CanDeploy && !connection.IsStarting && !_onlineBusy)
                    StartOnlineMatch();
            }
        }

        private void SetOnlineRoomOpen(bool open)
        {
            _onlineOpen = open;
            _onlineVisuals.SetActive(open);
            _onlineModalUi?.SetSelectingBannerVisible(_choosingOnlineVehicle && !open);
            SyncActiveHitGroups();
            _nextOnlineRefresh = 0f;
            if (open)
            {
                _onlineModalUi?.RefreshLocalizedLabels();
                RefreshOnlineRoom();
            }
        }

        private void RefreshOnlineRoom()
        {
            _onlineModalUi?.SyncResponsiveInputFonts(_windowbox);
            _onlineModalUi?.SyncFocusIndicators();

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

            bool busy = _onlineBusy || _waitingForVehicleSelection || (connection != null && connection.IsLeaving);
            bool isHost = inRoom && connection.IsHost;
            bool canStart = isHost && room.CanDeploy && !connection.IsStarting && !busy;
            bool showStart = isHost && !connection.IsStarting && !busy;
            bool canSelect = inRoom && !connection.IsStarting && !busy;
            bool canSwitch = canSelect && room.CanSwitchTeam;

            _onlineEntryVisuals.SetActive(!inRoom && !busy);
            _onlineEntryHits.SetActive(!inRoom && !busy);
            _onlineStartVisuals.SetActive(showStart);
            _onlineStartHits.SetActive(showStart);
            _onlineSelectVisuals.SetActive(canSelect);
            _onlineSelectHits.SetActive(canSelect);

            _onlineBack.text = UiText.Get(inRoom ? "LEAVE ROOM" : "BACK [ESC]");
            bool hasCode = inRoom && !string.IsNullOrEmpty(connection.JoinCode);
            _onlineCodeLabel.text = hasCode ? UiText.Format("CODE {0}", connection.JoinCode) : "";
            _onlineName.interactable = !busy && (connection == null || !connection.IsStarting);

            int localSlot = inRoom ? room.LocalSlot : -1;
            _onlineFriendlyRoster.text = localSlot >= 0 ? OnlineTeamRoster(room, localSlot, friendly: true) : "";
            _onlineHostileRoster.text = localSlot >= 0 ? OnlineTeamRoster(room, localSlot, friendly: false) : "";
            _onlineVehicle.text = inRoom && GetSelectedVehicle() is { } vehicle
                ? UiText.Format("YOUR TANK: {0}", vehicle.name.ToUpperInvariant()) : "";

            bool isError = false;
            bool isReady = false;
            if (connection != null && connection.IsLeaving)
            {
                _onlineStatus.text = UiText.Get("LEAVING ROOM...");
            }
            else if (busy)
            {
                _onlineStatus.text = UiText.Get(inRoom ? "UPDATING ROOM..." : "CONNECTING...");
            }
            else if (!string.IsNullOrEmpty(_onlineError))
            {
                _onlineStatus.text = UiText.Get(_onlineError);
                isError = true;
            }
            else if (!inRoom)
            {
                _onlineStatus.text = UiText.Get("CREATE A ROOM OR JOIN A FRIEND");
            }
            else if (connection.IsStarting)
            {
                _onlineStatus.text = UiText.Get("LOADING BATTLE...");
                isReady = true;
            }
            else if (isHost)
            {
                if (room.PlayerCount < 2)
                {
                    _onlineStatus.text = UiText.Get("WAITING FOR A PLAYER");
                }
                else if (room.CountTeam(true) == 0 || room.CountTeam(false) == 0)
                {
                    _onlineStatus.text = UiText.Get("NEED OPPONENT");
                }
                else if (room.CanDeploy)
                {
                    _onlineStatus.text = UiText.Get("READY TO START");
                    isReady = true;
                }
                else
                {
                    _onlineStatus.text = UiText.Get("WAITING FOR TANK SELECTION");
                }
            }
            else
            {
                _onlineStatus.text = UiText.Get("WAITING FOR HOST");
            }

            bool copiedFeedback = Time.unscaledTime < _codeCopiedUntil;
            bool hasTypedCode = !string.IsNullOrEmpty(SanitizeJoinCode(_onlineCode.text));
            _onlineModalUi?.UpdateState(
                inRoom: inRoom,
                isHost: isHost,
                busy: busy,
                canStart: canStart,
                canSelect: canSelect,
                canSwitchTeam: canSwitch,
                hasJoinCode: hasCode,
                hasTypedCode: hasTypedCode,
                copiedFeedback: copiedFeedback,
                isError: isError,
                isReadyStatus: isReady,
                playerCount: inRoom ? room.PlayerCount : 0,
                maxPlayers: MultiplayerSessionConnection.MaxPlayers,
                statusText: _onlineStatus);
        }

        private void BeginOnlineVehicleSelection()
        {
            var connection = _multiplayer.Connection;
            var room = connection != null ? connection.Room : null;
            if (room == null || connection.IsStarting || connection.IsLeaving || _onlineBusy) return;
            _waitingForVehicleSelection = true;
            _onlineError = null;
            room.SetVehicleSelection(_sessionState.SelectedPlayerVehicleId, choosing: true);
            _nextOnlineRefresh = 0f;
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
            _onlineModalUi?.SetSelectingBannerVisible(choosing);
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
            var code = host ? null : SanitizeJoinCode(_onlineCode.text);
            if (!host && string.IsNullOrEmpty(code))
            {
                _onlineError = "ENTER A JOIN CODE.";
                _nextOnlineRefresh = 0f;
                RefreshOnlineRoom();
                return;
            }

            SaveOnlineDisplayName(_onlineName.text);
            _onlineBusy = true;
            _onlineError = null;
            _codeCopiedUntil = -1f;
            _nextOnlineRefresh = 0f;
            RefreshOnlineRoom();
            try { await multiplayerSessionPrefab.ConnectAsync(_sessionState, _multiplayer, _sceneLoader, code); }
            catch (Exception exception)
            {
                _onlineError = host ? "COULD NOT CREATE ROOM. TRY AGAIN." : "COULD NOT JOIN. CHECK THE CODE AND TRY AGAIN.";
                Debug.LogWarning(exception.Message);
            }
            finally
            {
                _onlineBusy = false;
                _nextOnlineRefresh = 0f;
            }
        }

        private void StartOnlineMatch() => StartOnlineMatchAsync().Forget();

        private async UniTask StartOnlineMatchAsync()
        {
            var connection = _multiplayer.Connection;
            if (_onlineBusy || connection == null || connection.Room == null || !connection.Room.CanDeploy) return;
            _onlineBusy = true;
            _onlineError = null;
            _nextOnlineRefresh = 0f;
            RefreshOnlineRoom();
            try { await connection.StartMatchAsync(); }
            catch (Exception exception)
            {
                _onlineError = "COULD NOT START MATCH. TRY AGAIN.";
                Debug.LogWarning(exception.Message);
            }
            finally
            {
                _onlineBusy = false;
                _nextOnlineRefresh = 0f;
            }
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
                _nextOnlineRefresh = 0f;
                RefreshOnlineRoom();
                try { await connection.LeaveAsync(); }
                finally { _onlineBusy = false; }
            }

            if (this == null) return;
            _onlineError = null;
            _codeCopiedUntil = -1f;
            SetOnlineRoomOpen(false);
        }
    }
}
