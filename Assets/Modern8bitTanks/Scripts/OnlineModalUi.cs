using System;
using UnityEngine;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    /// <summary>
    /// Builds and maintains the upgraded pixel-aligned Online Battle modal layout,
    /// keeping 480x360 pixel visuals and ScreenDisplayRect hit targets mathematically synced.
    /// </summary>
    internal sealed class OnlineModalUi
    {
        private const float RefW = PixelWindowbox.RenderWidth;
        private const float RefH = PixelWindowbox.RenderHeight;

        private const int CardX = 66;
        private const int CardY = 38;
        private const int CardW = 348;
        private const int CardH = 284;

        private static readonly Color InsetBg = new(0.07f, 0.09f, 0.07f, 0.90f);
        private static readonly Color FriendlyBg = new(0.06f, 0.11f, 0.14f, 0.88f);
        private static readonly Color HostileBg = new(0.14f, 0.09f, 0.06f, 0.88f);
        private static readonly Color BorderDim = new(0.26f, 0.31f, 0.25f, 0.92f);
        private static readonly Color BorderFocus = new(0.52f, 0.88f, 0.94f, 1f);
        private static readonly Color StatusBgNormal = new(0.07f, 0.09f, 0.07f, 0.92f);
        private static readonly Color StatusBgError = new(0.24f, 0.08f, 0.07f, 0.94f);
        private static readonly Color StatusBgReady = new(0.08f, 0.16f, 0.09f, 0.92f);
        private static readonly Color StatusTextNormal = new(0.96f, 0.78f, 0.38f, 1f);
        private static readonly Color StatusTextError = new(1.00f, 0.44f, 0.36f, 1f);
        private static readonly Color StatusTextReady = new(0.56f, 0.94f, 0.42f, 1f);
        private static readonly Color BtnRest = new(0.26f, 0.29f, 0.23f, 0.98f);
        private static readonly Color BtnHover = new(0.37f, 0.42f, 0.33f, 1f);
        private static readonly Color BtnDisabled = new(0.15f, 0.17f, 0.14f, 0.85f);
        private static readonly Color CtaRest = new(0.92f, 0.90f, 0.85f, 1f);
        private static readonly Color CtaHover = new(1.00f, 0.95f, 0.76f, 1f);
        private static readonly Color CtaDisabled = new(0.22f, 0.24f, 0.20f, 0.88f);
        private static readonly Color CtaTextActive = new(0.09f, 0.10f, 0.08f, 1f);
        private static readonly Color CtaTextDisabled = new(0.52f, 0.54f, 0.48f, 1f);
        private static readonly Color TextLight = new(0.93f, 0.92f, 0.86f, 1f);
        private static readonly Color TextMuted = new(0.68f, 0.70f, 0.62f, 1f);
        private static readonly Color CyanAccent = new(0.52f, 0.88f, 0.94f, 1f);

        private readonly Font _font;
        private readonly InputField _codeInput;
        private readonly InputField _nameInput;

        // Visual references created or repositioned
        private Image _statusBg;
        private Image _statusBar;
        private Text _headerBadge;
        private Text _joinSectionLabel;
        private Text _namePlaceholder;
        private Text _codePlaceholder;
        private Image _nameFocusBorder;
        private Image _codeFocusBorder;

        private GameObject _lobbyDecorGroup;
        private GameObject _copyVisuals;
        private GameObject _copyHits;
        private Image _copyBg;
        private Image _copyAccent;
        private Text _copyLabel;
        private Button _copyButton;

        private Image _pasteBg;
        private Text _pasteLabel;
        private Button _pasteButton;

        private Image _joinBg;
        private Image _joinAccent;
        private Text _joinLabel;
        private Button _joinButton;

        private GameObject _switchVisuals;
        private GameObject _switchHits;
        private Image _switchBg;
        private Image _switchAccent;
        private Text _switchLabel;
        private Button _switchButton;

        private RectTransform _selectTankVisualRt;
        private RectTransform _selectTankHitRt;
        private RectTransform _selectTankLabelRt;
        private Image _startBg;
        private Text _startLabel;
        private Button _startButton;
        private PixelMenuButton _startMenuButton;

        private GameObject _selectingBanner;
        private Text _selectingBannerText;

        private int _lastScaledFontSize = -1;

        public OnlineModalUi(
            Font font,
            Canvas pixelCanvas,
            GameObject onlineVisuals,
            GameObject onlineHits,
            GameObject entryVisuals,
            GameObject entryHits,
            GameObject startVisuals,
            GameObject startHits,
            GameObject selectVisuals,
            GameObject selectHits,
            InputField codeInput,
            InputField nameInput,
            Text statusText,
            Text codeLabel,
            Text friendlyRoster,
            Text hostileRoster,
            Text vehicleText,
            Text backText,
            PixelMenuButton joinAction,
            PixelMenuButton createAction,
            PixelMenuButton startAction,
            PixelMenuButton vehicleAction,
            PixelMenuButton backAction,
            Action onCopyCode,
            Action onPasteCode,
            Action onSwitchTeam)
        {
            _font = font;
            _codeInput = codeInput;
            _nameInput = nameInput;
            _startMenuButton = startAction;

            Transform card = onlineVisuals.transform.Find("OnlineRoomCard");
            if (card == null) return;

            var cardRt = card.GetComponent<RectTransform>();
            SetPixelRect(cardRt, CardX, CardY, CardW, CardH);

            // Restyle header accents on expanded card
            TrySetRect(card.Find("TopAccent"), 4, CardH - 4, CardW - 8, 2);
            TrySetRect(card.Find("HazardStrip"), 6, CardH - 8, CardW - 12, 3);
            TrySetRect(card.Find("OnlineIcon"), 14, CardH - 28, 9, 9);

            Transform titleTf = card.Find("Txt_ONLINE BATTLE");
            if (titleTf != null)
            {
                SetPixelRect(titleTf.GetComponent<RectTransform>(), 28, CardH - 34, 180, 22);
                var titleText = titleTf.GetComponent<Text>();
                if (titleText != null) titleText.alignment = TextAnchor.MiddleLeft;
            }

            _headerBadge = CreatePixelText(card, "HeaderBadge", "", 8, CyanAccent, 214, CardH - 34, 120, 22, TextAnchor.MiddleRight);

            // Dedicated status & error banner below header
            _statusBg = CreateSolidBox(card, "StatusBannerBg", 12, 228, 324, 18, StatusBgNormal);
            _statusBar = CreateSolidBox(card, "StatusBannerBar", 12, 228, 2, 18, StatusTextNormal);
            statusText.transform.SetParent(card, false);
            statusText.transform.SetAsLastSibling();
            SetPixelRect(statusText.rectTransform, 18, 228, 312, 18);
            statusText.alignment = TextAnchor.MiddleCenter;

            // Profile row: Display Name (world y = 238..260 -> card y = 200..222)
            Transform nameLabelTf = card.Find("DisplayNameLabel");
            if (nameLabelTf != null)
                SetPixelRect(nameLabelTf.GetComponent<RectTransform>(), 14, 200, 88, 22);

            Transform nameFieldTf = onlineVisuals.transform.Find("DisplayNameField");
            if (nameFieldTf != null)
            {
                SetPixelRect(nameFieldTf.GetComponent<RectTransform>(), CardX + 106, CardY + 200, 228, 22);
                _nameFocusBorder = CreateBorderFrame(nameFieldTf, "FocusBorder", 228, 22, BorderDim);
            }
            SetHitAnchor(nameInput.GetComponent<RectTransform>(), CardX + 106, CardY + 200, 228, 22);
            _namePlaceholder = EnsurePlaceholder(nameInput, "ENTER DISPLAY NAME");

            // =================================================================
            // Entry View (not in room): Join-by-Code box + Create Room CTA
            // =================================================================
            var joinBox = CreateSolidBox(entryVisuals.transform, "JoinSectionBg", CardX + 12, CardY + 128, 324, 62, InsetBg);
            joinBox.transform.SetAsFirstSibling();
            CreateBorderFrame(joinBox.transform, "JoinSectionBorder", 324, 62, BorderDim);
            _joinSectionLabel = CreatePixelText(joinBox.transform, "JoinSectionLabel", "", 8, CyanAccent, 8, 44, 308, 14, TextAnchor.MiddleLeft);

            // Divider between Join and Create
            CreateSolidBox(entryVisuals.transform, "EntryDivider", CardX + 16, CardY + 114, 316, 1, BorderDim);

            // Code input field + Paste button + Join button (world y = 176..200)
            Transform codeFieldTf = entryVisuals.transform.Find("CodeField");
            if (codeFieldTf != null)
            {
                SetPixelRect(codeFieldTf.GetComponent<RectTransform>(), CardX + 20, CardY + 138, 172, 24);
                _codeFocusBorder = CreateBorderFrame(codeFieldTf, "FocusBorder", 172, 24, BorderDim);
            }
            SetHitAnchor(codeInput.GetComponent<RectTransform>(), CardX + 20, CardY + 138, 172, 24);
            _codePlaceholder = EnsurePlaceholder(codeInput, "ENTER JOIN CODE");

            // New Paste button (world x = 264, y = 176, w = 52, h = 24)
            CreateDynamicButton(
                entryVisuals.transform, entryHits.transform, "PASTE",
                CardX + 198, CardY + 138, 52, 24,
                BtnRest, BtnHover, TextLight, CyanAccent,
                onPasteCode,
                out _, out _, out _pasteBg, out _, out _pasteLabel, out _pasteButton);

            // Reposition Join button (world x = 322, y = 176, w = 68, h = 24)
            Transform joinBtnTf = entryVisuals.transform.Find("Btn_JOIN");
            if (joinBtnTf != null)
            {
                SetPixelRect(joinBtnTf.GetComponent<RectTransform>(), CardX + 256, CardY + 138, 68, 24);
                _joinBg = joinBtnTf.GetComponent<Image>();
                Transform joinBarTf = joinBtnTf.Find("StatusBar");
                if (joinBarTf != null)
                {
                    SetPixelRect(joinBarTf.GetComponent<RectTransform>(), 2, 3, 2, 18);
                    _joinAccent = joinBarTf.GetComponent<Image>();
                }
                Transform joinTxtTf = joinBtnTf.Find("Txt_JOIN");
                if (joinTxtTf != null)
                {
                    SetPixelRect(joinTxtTf.GetComponent<RectTransform>(), 4, 0, 62, 24);
                    _joinLabel = joinTxtTf.GetComponent<Text>();
                }
            }
            if (joinAction != null)
            {
                SetHitAnchor(joinAction.GetComponent<RectTransform>(), CardX + 256, CardY + 138, 68, 24);
                _joinButton = joinAction.GetComponent<Button>();
            }

            // Reposition Create Room CTA (world x = 78, y = 110, w = 324, h = 26)
            Transform createBtnTf = entryVisuals.transform.Find("Cta_CREATE ROOM");
            if (createBtnTf != null)
            {
                SetPixelRect(createBtnTf.GetComponent<RectTransform>(), CardX + 12, CardY + 72, 324, 26);
                TrySetRect(createBtnTf.Find("Txt_CREATE ROOM"), 0, 0, 324, 26);
            }
            if (createAction != null)
                SetHitAnchor(createAction.GetComponent<RectTransform>(), CardX + 12, CardY + 72, 324, 26);

            // =================================================================
            // Lobby View (inside room): Code+Copy bar, 2-Column Rosters, Vehicle, Actions
            // =================================================================
            _lobbyDecorGroup = new GameObject("RoomLobbyDecor", typeof(RectTransform)) { layer = 5 };
            _lobbyDecorGroup.transform.SetParent(card, false);
            var lobbyRt = _lobbyDecorGroup.GetComponent<RectTransform>();
            SetPixelRect(lobbyRt, 0, 0, CardW, CardH);
            // Keep lobby boxes behind roster/code/vehicle text labels
            _lobbyDecorGroup.transform.SetSiblingIndex(Mathf.Min(4, card.childCount - 1));

            // Room code bar box (card y = 170..194)
            var codeBox = CreateSolidBox(lobbyRt, "CodeBarBg", 12, 170, 324, 24, InsetBg);
            CreateBorderFrame(codeBox.transform, "CodeBarBorder", 324, 24, BorderDim);
            CreateSolidBox(lobbyRt, "CodeBarAccent", 12, 170, 2, 24, StatusTextNormal);

            codeLabel.transform.SetParent(card, false);
            codeLabel.transform.SetAsLastSibling();
            SetPixelRect(codeLabel.rectTransform, 20, 170, 232, 24);
            codeLabel.alignment = TextAnchor.MiddleLeft;
            codeLabel.color = CyanAccent;

            // One-click Copy button inside code bar
            CreateDynamicButton(
                onlineVisuals.transform, onlineHits.transform, "COPY",
                CardX + 258, CardY + 172, 72, 20,
                BtnRest, BtnHover, TextLight, StatusTextNormal,
                onCopyCode,
                out _copyVisuals, out _copyHits, out _copyBg, out _copyAccent, out _copyLabel, out _copyButton);

            // 2-column team roster boxes (card y = 92..166, h = 74)
            var friendlyBox = CreateSolidBox(lobbyRt, "FriendlyRosterBg", 12, 92, 158, 74, FriendlyBg);
            CreateBorderFrame(friendlyBox.transform, "FriendlyBorder", 158, 74, new Color(0.28f, 0.50f, 0.68f, 0.85f));
            CreateSolidBox(lobbyRt, "FriendlyTopBar", 12, 164, 158, 2, OnlineTeamColors.Friendly);

            var hostileBox = CreateSolidBox(lobbyRt, "HostileRosterBg", 178, 92, 158, 74, HostileBg);
            CreateBorderFrame(hostileBox.transform, "HostileBorder", 158, 74, new Color(0.68f, 0.40f, 0.24f, 0.85f));
            CreateSolidBox(lobbyRt, "HostileTopBar", 178, 164, 158, 2, OnlineTeamColors.Hostile);

            friendlyRoster.transform.SetParent(card, false);
            friendlyRoster.transform.SetAsLastSibling();
            SetPixelRect(friendlyRoster.rectTransform, 18, 94, 146, 68);
            friendlyRoster.alignment = TextAnchor.UpperLeft;
            friendlyRoster.lineSpacing = 1.15f;

            hostileRoster.transform.SetParent(card, false);
            hostileRoster.transform.SetAsLastSibling();
            SetPixelRect(hostileRoster.rectTransform, 184, 94, 146, 68);
            hostileRoster.alignment = TextAnchor.UpperLeft;
            hostileRoster.lineSpacing = 1.15f;

            // Selected vehicle strip (card y = 72..88)
            var vehBox = CreateSolidBox(lobbyRt, "VehicleStripBg", 12, 72, 324, 16, InsetBg);
            CreateBorderFrame(vehBox.transform, "VehicleStripBorder", 324, 16, BorderDim);
            vehicleText.transform.SetParent(card, false);
            vehicleText.transform.SetAsLastSibling();
            SetPixelRect(vehicleText.rectTransform, 18, 72, 312, 16);
            vehicleText.alignment = TextAnchor.MiddleCenter;
            vehicleText.color = TextLight;

            // Lobby action row (card y = 44..66 -> world y = 82..104)
            Transform selectBtnTf = selectVisuals.transform.Find("Btn_SELECT TANK");
            if (selectBtnTf != null)
            {
                _selectTankVisualRt = selectBtnTf.GetComponent<RectTransform>();
                Transform selectTxtTf = selectBtnTf.Find("Txt_SELECT TANK");
                if (selectTxtTf != null) _selectTankLabelRt = selectTxtTf.GetComponent<RectTransform>();
            }
            if (vehicleAction != null)
                _selectTankHitRt = vehicleAction.GetComponent<RectTransform>();

            // New Switch Team button
            CreateDynamicButton(
                onlineVisuals.transform, onlineHits.transform, "SWITCH TEAM",
                CardX + 122, CardY + 44, 104, 22,
                BtnRest, BtnHover, TextLight, CyanAccent,
                onSwitchTeam,
                out _switchVisuals, out _switchHits, out _switchBg, out _switchAccent, out _switchLabel, out _switchButton);

            // Start Match button (always visible for host; disabled style until CanDeploy)
            Transform startBtnTf = startVisuals.transform.Find("Cta_START MATCH");
            if (startBtnTf != null)
            {
                SetPixelRect(startBtnTf.GetComponent<RectTransform>(), CardX + 232, CardY + 44, 104, 22);
                _startBg = startBtnTf.GetComponent<Image>();
                Transform startTxtTf = startBtnTf.Find("Txt_START MATCH");
                if (startTxtTf != null)
                {
                    SetPixelRect(startTxtTf.GetComponent<RectTransform>(), 0, 0, 104, 22);
                    _startLabel = startTxtTf.GetComponent<Text>();
                }
            }
            if (startAction != null)
            {
                SetHitAnchor(startAction.GetComponent<RectTransform>(), CardX + 232, CardY + 44, 104, 22);
                _startButton = startAction.GetComponent<Button>();
            }

            // Common Footer Back / Leave Room button (card y = 12..34 -> world y = 50..72)
            Transform backBtnTf = onlineVisuals.transform.Find("Btn_BACK");
            if (backBtnTf != null)
            {
                SetPixelRect(backBtnTf.GetComponent<RectTransform>(), CardX + 12, CardY + 12, 324, 22);
                TrySetRect(backBtnTf.Find("Txt_BACK"), 0, 0, 324, 22);
            }
            if (backAction != null)
                SetHitAnchor(backAction.GetComponent<RectTransform>(), CardX + 12, CardY + 12, 324, 22);

            // Garage banner shown when choosing a tank for an active online room
            BuildSelectingBanner(pixelCanvas.transform);
            RefreshLocalizedLabels();
        }

        private void BuildSelectingBanner(Transform pixelCanvasRoot)
        {
            _selectingBanner = new GameObject("OnlineVehicleSelectingBanner", typeof(RectTransform)) { layer = 5 };
            _selectingBanner.transform.SetParent(pixelCanvasRoot, false);
            var rt = _selectingBanner.GetComponent<RectTransform>();
            SetPixelRect(rt, 96, 336, 288, 18);

            var bg = CreateSolidBox(rt, "Bg", 0, 0, 288, 18, new Color(0.06f, 0.11f, 0.14f, 0.94f));
            CreateBorderFrame(bg.transform, "Border", 288, 18, CyanAccent);
            CreateSolidBox(rt, "Accent", 0, 0, 3, 18, CyanAccent);
            _selectingBannerText = CreatePixelText(rt, "Text", "", 8, CyanAccent, 8, 0, 274, 18, TextAnchor.MiddleCenter);
            _selectingBanner.SetActive(false);
        }

        public void SetSelectingBannerVisible(bool visible)
        {
            if (_selectingBanner != null) _selectingBanner.SetActive(visible);
        }

        public void RefreshLocalizedLabels()
        {
            if (_joinSectionLabel != null) _joinSectionLabel.text = UiText.Get("JOIN BY CODE");
            if (_namePlaceholder != null) _namePlaceholder.text = UiText.Get("ENTER DISPLAY NAME");
            if (_codePlaceholder != null) _codePlaceholder.text = UiText.Get("ENTER JOIN CODE");
            if (_pasteLabel != null) _pasteLabel.text = UiText.Get("PASTE");
            if (_switchLabel != null) _switchLabel.text = UiText.Get("SWITCH TEAM");
            if (_selectingBannerText != null) _selectingBannerText.text = UiText.Get("ONLINE TANK SELECT BANNER");
        }

        public void SyncResponsiveInputFonts(PixelWindowbox windowbox)
        {
            if (windowbox == null) return;
            float dispH = windowbox.ScreenDisplayRect.height;
            if (dispH <= 1f) return;
            int fontSize = Mathf.Clamp(Mathf.RoundToInt(dispH / RefH * 9.5f), 10, 28);
            if (fontSize == _lastScaledFontSize) return;
            _lastScaledFontSize = fontSize;

            ApplyInputFontSize(_codeInput, fontSize);
            ApplyInputFontSize(_nameInput, fontSize);
        }

        public void SyncFocusIndicators()
        {
            if (_nameFocusBorder != null)
                SetBorderColor(_nameFocusBorder.transform, _nameInput != null && _nameInput.isFocused ? BorderFocus : BorderDim);
            if (_codeFocusBorder != null)
                SetBorderColor(_codeFocusBorder.transform, _codeInput != null && _codeInput.isFocused ? BorderFocus : BorderDim);
        }

        public void UpdateState(
            bool inRoom,
            bool isHost,
            bool busy,
            bool canStart,
            bool canSelect,
            bool canSwitchTeam,
            bool hasJoinCode,
            bool hasTypedCode,
            bool copiedFeedback,
            bool isError,
            bool isReadyStatus,
            int playerCount,
            int maxPlayers,
            Text statusText)
        {
            if (_headerBadge != null)
            {
                _headerBadge.text = inRoom
                    ? UiText.Format("PLAYERS {0}/{1}", playerCount, maxPlayers)
                    : UiText.Format("UP TO {0} PLAYERS", maxPlayers);
                _headerBadge.color = inRoom ? CyanAccent : TextMuted;
            }

            // Status banner styling
            if (_statusBg != null && _statusBar != null && statusText != null)
            {
                if (isError)
                {
                    _statusBg.color = StatusBgError;
                    _statusBar.color = StatusTextError;
                    statusText.color = StatusTextError;
                }
                else if (isReadyStatus)
                {
                    _statusBg.color = StatusBgReady;
                    _statusBar.color = StatusTextReady;
                    statusText.color = StatusTextReady;
                }
                else
                {
                    _statusBg.color = StatusBgNormal;
                    _statusBar.color = StatusTextNormal;
                    statusText.color = StatusTextNormal;
                }
            }

            if (_lobbyDecorGroup != null) _lobbyDecorGroup.SetActive(inRoom);

            // Copy button visibility & feedback
            bool showCopy = inRoom && hasJoinCode && !busy;
            if (_copyVisuals != null) _copyVisuals.SetActive(showCopy);
            if (_copyHits != null) _copyHits.SetActive(showCopy);
            if (_copyLabel != null)
            {
                _copyLabel.text = UiText.Get(copiedFeedback ? "COPIED!" : "COPY");
                _copyLabel.color = copiedFeedback ? StatusTextReady : TextLight;
            }
            if (_copyAccent != null)
                _copyAccent.color = copiedFeedback ? StatusTextReady : StatusTextNormal;

            // Join button enabled styling in entry view
            bool joinActive = !busy && hasTypedCode;
            if (_joinButton != null) _joinButton.interactable = joinActive;
            if (_joinBg != null && !joinActive)
                _joinBg.color = BtnDisabled;
            else if (_joinBg != null && _joinBg.color == BtnDisabled)
                _joinBg.color = BtnRest;
            if (_joinAccent != null)
                _joinAccent.color = joinActive ? CyanAccent : BorderDim;
            if (_joinLabel != null)
                _joinLabel.color = joinActive ? TextLight : TextMuted;

            if (_pasteButton != null) _pasteButton.interactable = !busy;
            if (_pasteBg != null && busy) _pasteBg.color = BtnDisabled;

            // Lobby action bar dynamic sizing (3 buttons for host, 2 wider buttons for guest)
            bool showLobbyActions = canSelect;
            if (showLobbyActions)
            {
                if (isHost)
                {
                    SetSelectTankRect(CardX + 12, CardY + 44, 104, 22);
                    SetSwitchTeamRect(CardX + 122, CardY + 44, 104, 22);
                }
                else
                {
                    SetSelectTankRect(CardX + 12, CardY + 44, 158, 22);
                    SetSwitchTeamRect(CardX + 178, CardY + 44, 158, 22);
                }
            }

            if (_switchVisuals != null) _switchVisuals.SetActive(showLobbyActions);
            if (_switchHits != null) _switchHits.SetActive(showLobbyActions);
            if (_switchButton != null) _switchButton.interactable = showLobbyActions && canSwitchTeam;
            if (_switchBg != null)
            {
                if (!canSwitchTeam) _switchBg.color = BtnDisabled;
                else if (_switchBg.color == BtnDisabled) _switchBg.color = BtnRest;
            }
            if (_switchAccent != null)
                _switchAccent.color = canSwitchTeam ? CyanAccent : BorderDim;
            if (_switchLabel != null)
                _switchLabel.color = canSwitchTeam ? TextLight : TextMuted;

            // Host Start Match button: always visible to host in lobby; disabled visually until CanDeploy
            if (_startButton != null) _startButton.interactable = canStart;
            if (_startBg != null)
            {
                if (!canStart)
                {
                    _startBg.color = CtaDisabled;
                }
                else if (_startBg.color == CtaDisabled)
                {
                    _startBg.color = CtaRest;
                    _startMenuButton?.SetAppearance(_startBg, CtaRest, CtaHover);
                }
            }
            if (_startLabel != null)
                _startLabel.color = canStart ? CtaTextActive : CtaTextDisabled;
        }

        private void SetSelectTankRect(int x, int y, int w, int h)
        {
            if (_selectTankVisualRt != null) SetPixelRect(_selectTankVisualRt, x, y, w, h);
            if (_selectTankLabelRt != null) SetPixelRect(_selectTankLabelRt, 4, 0, w - 6, h);
            if (_selectTankHitRt != null) SetHitAnchor(_selectTankHitRt, x, y, w, h);
        }

        private void SetSwitchTeamRect(int x, int y, int w, int h)
        {
            if (_switchBg != null) SetPixelRect(_switchBg.rectTransform, x, y, w, h);
            if (_switchLabel != null) SetPixelRect(_switchLabel.rectTransform, 4, 0, w - 6, h);
            if (_switchButton != null) SetHitAnchor(_switchButton.GetComponent<RectTransform>(), x, y, w, h);
        }

        private void CreateDynamicButton(
            Transform visualsParent,
            Transform hitsParent,
            string key,
            int x, int y, int w, int h,
            Color restColor, Color hoverColor, Color textColor, Color accentColor,
            Action onClick,
            out GameObject visualGo,
            out GameObject hitGo,
            out Image bgOut,
            out Image accentOut,
            out Text labelOut,
            out Button buttonOut)
        {
            visualGo = new GameObject($"Btn_{key}", typeof(RectTransform), typeof(Image)) { layer = 5 };
            visualGo.transform.SetParent(visualsParent, false);
            var vRt = visualGo.GetComponent<RectTransform>();
            SetPixelRect(vRt, x, y, w, h);
            bgOut = visualGo.GetComponent<Image>();
            bgOut.sprite = MilitaryUiSprites.SteelButton;
            bgOut.type = MilitaryUiSprites.SteelButton != null ? Image.Type.Sliced : Image.Type.Simple;
            bgOut.pixelsPerUnitMultiplier = 1f;
            bgOut.color = restColor;
            bgOut.raycastTarget = false;

            accentOut = CreateSolidBox(vRt, "StatusBar", 2, 3, 2, h - 6, accentColor);
            labelOut = CreatePixelText(vRt, $"Txt_{key}", UiText.Get(key), 8, textColor, 4, 0, w - 6, h, TextAnchor.MiddleCenter);

            hitGo = new GameObject($"Hit_{key}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(PixelMenuButton)) { layer = 5 };
            hitGo.transform.SetParent(hitsParent, false);
            SetHitAnchor(hitGo.GetComponent<RectTransform>(), x, y, w, h);
            var hitImg = hitGo.GetComponent<Image>();
            hitImg.color = Color.clear;
            hitImg.raycastTarget = true;

            buttonOut = hitGo.GetComponent<Button>();
            buttonOut.transition = Selectable.Transition.None;

            var menuBtn = hitGo.GetComponent<PixelMenuButton>();
            menuBtn.SetAppearance(bgOut, restColor, hoverColor);
            menuBtn.Bind(onClick);
        }

        private Text EnsurePlaceholder(InputField field, string key)
        {
            if (field == null) return null;
            if (field.placeholder is Text existing)
            {
                existing.text = UiText.Get(key);
                existing.color = new Color(0.64f, 0.67f, 0.60f, 0.72f);
                return existing;
            }

            var go = new GameObject("Placeholder", typeof(RectTransform), typeof(Text)) { layer = 5 };
            go.transform.SetParent(field.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var txt = go.GetComponent<Text>();
            txt.font = field.textComponent != null && field.textComponent.font != null ? field.textComponent.font : _font;
            txt.fontSize = field.textComponent != null ? field.textComponent.fontSize : 14;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = new Color(0.64f, 0.67f, 0.60f, 0.72f);
            txt.text = UiText.Get(key);
            txt.raycastTarget = false;
            field.placeholder = txt;
            return txt;
        }

        private static void ApplyInputFontSize(InputField field, int size)
        {
            if (field == null) return;
            if (field.textComponent != null) field.textComponent.fontSize = size;
            if (field.placeholder is Text ph) ph.fontSize = size;
        }

        private static Image CreateBorderFrame(Transform parent, string name, int w, int h, Color color)
        {
            var holder = new GameObject(name, typeof(RectTransform)) { layer = 5 };
            holder.transform.SetParent(parent, false);
            var rt = holder.GetComponent<RectTransform>();
            SetPixelRect(rt, 0, 0, w, h);

            var top = CreateSolidBox(rt, "Top", 0, h - 1, w, 1, color);
            CreateSolidBox(rt, "Bottom", 0, 0, w, 1, color);
            CreateSolidBox(rt, "Left", 0, 0, 1, h, color);
            CreateSolidBox(rt, "Right", w - 1, 0, 1, h, color);
            return top;
        }

        private static void SetBorderColor(Transform topEdge, Color color)
        {
            Transform holder = topEdge != null ? topEdge.parent : null;
            if (holder == null) return;
            for (int i = 0; i < holder.childCount; i++)
            {
                var img = holder.GetChild(i).GetComponent<Image>();
                if (img != null) img.color = color;
            }
        }

        private static Image CreateSolidBox(Transform parent, string name, int x, int y, int w, int h, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)) { layer = 5 };
            go.transform.SetParent(parent, false);
            SetPixelRect(go.GetComponent<RectTransform>(), x, y, w, h);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private Text CreatePixelText(Transform parent, string name, string content, int fontSize, Color color, int x, int y, int w, int h, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text)) { layer = 5 };
            go.transform.SetParent(parent, false);
            SetPixelRect(go.GetComponent<RectTransform>(), x, y, w, h);
            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.text = content;
            text.color = color;
            text.alignment = align;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static void TrySetRect(Transform tf, int x, int y, int w, int h)
        {
            if (tf == null) return;
            var rt = tf.GetComponent<RectTransform>();
            if (rt != null) SetPixelRect(rt, x, y, w, h);
        }

        private static void SetPixelRect(RectTransform rt, int x, int y, int w, int h)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void SetHitAnchor(RectTransform rt, int x, int y, int w, int h)
        {
            rt.anchorMin = new Vector2(x / RefW, y / RefH);
            rt.anchorMax = new Vector2((x + w) / RefW, (y + h) / RefH);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
        }
    }
}
