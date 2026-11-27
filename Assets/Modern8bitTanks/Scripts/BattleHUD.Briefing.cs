using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    public partial class BattleHUD
    {
        [SerializeField] private GameObject briefingOverlay;
        [SerializeField] private GameObject briefingHits;
        [SerializeField] private RawImage briefingMap;
        [SerializeField] private BriefingSketch briefingSketch;
        [SerializeField] private Text briefingLocation;
        [SerializeField] private Text briefingObjective;
        [SerializeField] private Text briefingReadyLabel;
        [SerializeField] private Text briefingInkLegend;
        [SerializeField] private Image briefingFriendlyMarker;
        [SerializeField] private Text briefingFriendlyLabel;
        [SerializeField] private Image briefingHostileMarker;
        [SerializeField] private Text briefingHostileLabel;
        [SerializeField] private PixelMenuButton briefingReady;
        private Texture2D _briefingTexture;
        private string _briefingMapName;
        private bool _briefingVisible;
        private bool _briefingReady;

        internal void ShowBriefing(string mapName, Color32[] mapPixels)
        {
            _briefingMapName = mapName;
            _briefingVisible = true;
            _briefingReady = false;
            var transport = _multiplayer.IsInBattle ? _multiplayer.Transport : null;
            transport?.BeginBriefingDrawing();
            briefingSketch.Bind(transport, _multiplayer.LocalSlot);
            briefingReady.GetComponent<Button>().interactable = true;
            if (_multiplayer.IsInBattle)
            {
                briefingFriendlyMarker.color = briefingFriendlyLabel.color = OnlineTeamColors.Friendly;
                briefingHostileMarker.color = briefingHostileLabel.color = OnlineTeamColors.Hostile;
            }
            const int scale = BattleSceneController.BriefingMapScale;
            _briefingTexture ??= new Texture2D(BattlefieldLayout.Width / scale, BattlefieldLayout.Height / scale, TextureFormat.RGBA32, false)
            {
                name = "Battlefield Briefing Map",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _briefingTexture.SetPixels32(mapPixels);
            _briefingTexture.Apply(false, false);
            briefingMap.texture = _briefingTexture;
            _waveToastGo.SetActive(false);
            RefreshBriefingLabels();
            RefreshBriefingVisibility();
        }

        internal void SetBriefingReady()
        {
            _briefingReady = true;
            briefingReady.GetComponent<Button>().interactable = false;
            RefreshBriefingLabels();
        }

        internal void HideBriefing()
        {
            _briefingVisible = false;
            briefingSketch.Bind(null);
            RefreshBriefingVisibility();
        }

        internal void UndoBriefingStroke() => briefingSketch.Undo();

        internal void ClearBriefingSketch() => briefingSketch.Clear();

        private void RefreshBriefingLabels()
        {
            if (!_briefingVisible) return;
            string mode = _mgr.battleMode switch
            {
                BattleMode.Squad2v2 => "2v2",
                BattleMode.Battle5v5 => _multiplayer.IsInBattle ? UiText.Get("ONLINE BATTLE") : "5v5",
                _ => "1v1"
            };
            briefingLocation.text = $"{UiText.Get(_briefingMapName)}  /  {mode}";
            briefingObjective.text = UiText.Get("CAPTURE THE CENTER");
            briefingReadyLabel.text = UiText.Get(_briefingReady ? "WAITING FOR PLAYERS" : "READY [ENTER]");
            RefreshBriefingInkLegend();
        }

        private void RefreshBriefingInkLegend()
        {
            var room = _multiplayer.IsInBattle ? _multiplayer.Connection?.Room : null;
            briefingInkLegend.gameObject.SetActive(room != null);
            if (room == null) return;

            const int teamSize = MultiplayerSessionConnection.PlayersPerTeam;
            int firstSlot = _mgr.IsLocalAlpha ? 0 : teamSize;
            var legend = new StringBuilder();
            for (int slot = firstSlot; slot < firstSlot + teamSize; slot++)
            {
                if (!room.TryGetMemberAtSlot(slot, out var member)) continue;
                string name = member.DisplayName.IsEmpty ? $"#{slot + 1}" : member.DisplayName.ToString();
                var characters = new StringInfo(name);
                if (characters.LengthInTextElements > 6) name = characters.SubstringByTextElements(0, 5) + "…";
                name = name.Replace('<', '‹').Replace('>', '›');
                if (legend.Length > 0) legend.Append("   ");
                legend.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(BriefingSketch.ColorForSlot(slot)))
                    .Append('>').Append(name).Append("</color>");
            }
            briefingInkLegend.text = legend.ToString();
        }

        private void RefreshBriefingVisibility()
        {
            bool show = _briefingVisible && !IsMenuOpen;
            briefingOverlay.SetActive(show);
            briefingHits.SetActive(show);
        }
    }
}
