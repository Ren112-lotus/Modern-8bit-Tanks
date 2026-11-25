
using System;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    [RequireComponent(typeof(Button))]
    public sealed class PixelMenuButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private Image background;
        [SerializeField] private Color restingColor;
        [SerializeField] private Color hoverColor;
        [SerializeField] private bool preserveSelection;
        [SerializeField] private Color selectedColor;

        private Button _button;

        public void SetAppearance(Image image, Color resting, Color hovering, bool preserve = false, Color selected = default)
        {
            background = image;
            restingColor = resting;
            hoverColor = hovering;
            preserveSelection = preserve;
            selectedColor = selected;
        }

        public void Bind(Action action)
        {
            _button ??= GetComponent<Button>();
            _button.OnClickAsObservable().Subscribe(_ => action());
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _button ??= GetComponent<Button>();
            if (_button != null && !_button.interactable) return;
            SetHighlight(hoverColor);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _button ??= GetComponent<Button>();
            if (_button != null && !_button.interactable) return;
            SetHighlight(restingColor);
        }

        private void SetHighlight(Color color)
        {
            if (background != null && (!preserveSelection || background.color != selectedColor))
                background.color = color;
        }
    }
}
