using UnityEngine;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    public partial class BattleHUD
    {
        [SerializeField] private GameObject briefingOverlay;
        [SerializeField] private GameObject briefingHits;
        [SerializeField] private RawImage briefingMap;
        [SerializeField] private Text briefingLocation;
        [SerializeField] private Text briefingObjective;
        [SerializeField] private Text briefingReadyLabel;
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
            briefingReady.GetComponent<Button>().interactable = true;
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
            RefreshBriefingVisibility();
        }

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
        }

        private void RefreshBriefingVisibility()
        {
            bool show = _briefingVisible && !IsMenuOpen;
            briefingOverlay.SetActive(show);
            briefingHits.SetActive(show);
        }
    }
}
