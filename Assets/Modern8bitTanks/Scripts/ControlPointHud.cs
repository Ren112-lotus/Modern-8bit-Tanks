using UnityEngine;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    public sealed class ControlPointHud : MonoBehaviour
    {
        [SerializeField] private Text _allyScore;
        [SerializeField] private Text _enemyScore;
        [SerializeField] private Text _objective;
        [SerializeField] private Text _status;
        [SerializeField] private RectTransform _waveToast;

        internal void SetVisible(bool visible)
        {
            if (gameObject.activeSelf == visible) return;

            _waveToast.anchoredPosition += Vector2.down * (visible ? 26f : -26f);
            if (visible) RefreshObjective();
            gameObject.SetActive(visible);
        }

        internal void RefreshObjective() =>
            _objective.text = UiText.Format("CAPTURE / {0} TO WIN", ControlPointSystem.ScoreToWin);

        internal void Render(ControlPointState state, Vector2 scores, Color ally, Color enemy, Color status)
        {
            _allyScore.text = Mathf.FloorToInt(scores.x).ToString();
            _enemyScore.text = Mathf.FloorToInt(scores.y).ToString();
            _allyScore.color = ally;
            _enemyScore.color = enemy;
            _status.text = state.Contested ? UiText.Get("CONTESTED")
                : state.Owner == 0 ? UiText.Get("POINT NEUTRAL") : UiText.Get("POINT HELD");
            _status.color = status;
        }
    }
}
