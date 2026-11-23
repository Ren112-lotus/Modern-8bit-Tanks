using UnityEngine;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    public static class MilitaryUiSprites
    {
        public static Sprite ArmorPanel { get; } = Load(nameof(ArmorPanel));
        public static Sprite ArmorHeaderBar { get; } = Load(nameof(ArmorHeaderBar));
        public static Sprite SteelButton { get; } = Load(nameof(SteelButton));
        public static Sprite OrdnanceButton { get; } = Load(nameof(OrdnanceButton));
        public static Sprite HazardTape { get; } = Load(nameof(HazardTape));
        public static Sprite HazardTapeRed { get; } = Load(nameof(HazardTapeRed));
        public static Sprite Scanlines { get; } = Load(nameof(Scanlines));
        public static Sprite IconStar { get; } = Load(nameof(IconStar));
        public static Sprite IconCrosshair { get; } = Load(nameof(IconCrosshair));
        public static Sprite IconShell { get; } = Load(nameof(IconShell));
        public static Sprite IconArmor { get; } = Load(nameof(IconArmor));

        private static Sprite Load(string name) => Resources.Load<Sprite>($"MilitaryUI/{name}");

        public static RectTransform CreateTiledSprite(Transform parent, string name, Sprite sprite, int x, int y, int w, int h, Color tint)
        {
            var go = new GameObject(name) { layer = 5 };
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Tiled;
            img.pixelsPerUnitMultiplier = 1f;
            img.color = tint;
            img.raycastTarget = false;
            return rt;
        }

        public static RectTransform CreateSlicedSprite(Transform parent, string name, Sprite sprite, int x, int y, int w, int h, Color tint, out Image imgOut)
        {
            var go = new GameObject(name) { layer = 5 };
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;
            img.color = tint;
            img.raycastTarget = false;
            imgOut = img;
            return rt;
        }

        public static RectTransform CreateIcon(Transform parent, string name, Sprite icon, int x, int y, Color tint)
        {
            int w = icon != null ? (int)icon.rect.width : 9;
            int h = icon != null ? (int)icon.rect.height : 9;
            var go = new GameObject(name) { layer = 5 };
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            var img = go.AddComponent<Image>();
            img.sprite = icon;
            img.type = Image.Type.Simple;
            img.color = tint;
            img.raycastTarget = false;
            return rt;
        }
    }
}
