using UnityEngine;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    /// <summary>
    /// Procedural 1:1 Pixel-Art Military Armored Warfare UI Sprite Atlas (FilterMode.Point, 1 PPU):
    /// - 9-sliced Riveted Rolled Homogeneous Armor (RHA) steel panels with corner bolts & beveled weld seams
    /// - 9-sliced Stamped-Steel tactical buttons & heavy Brass Ordnance launch switches
    /// - Tiled 45-degree black/amber military Hazard Caution Tape strips
    /// - Tiled Brushed Gunmetal Rail & subtle CRT/Optics Scanline overlay
    /// - Crisp pixel-art military emblems (Star, Crosshair Sight, Artillery Shell, Rivet Bar)
    /// </summary>
    public static class MilitaryUiSprites
    {
        private static Sprite _armorPanel;
        private static Sprite _armorHeaderBar;
        private static Sprite _steelButton;
        private static Sprite _ordnanceButton;
        private static Sprite _hazardTape;
        private static Sprite _hazardTapeRed;
        private static Sprite _scanlines;
        private static Sprite _iconStar;
        private static Sprite _iconCrosshair;
        private static Sprite _iconShell;
        private static Sprite _iconArmor;

        public static Sprite ArmorPanel => _armorPanel != null ? _armorPanel : (_armorPanel = BuildRivetedArmorPanel());
        public static Sprite ArmorHeaderBar => _armorHeaderBar != null ? _armorHeaderBar : (_armorHeaderBar = BuildBrushedSteelBar());
        public static Sprite SteelButton => _steelButton != null ? _steelButton : (_steelButton = BuildSteelButton());
        public static Sprite OrdnanceButton => _ordnanceButton != null ? _ordnanceButton : (_ordnanceButton = BuildOrdnanceButton());
        public static Sprite HazardTape => _hazardTape != null ? _hazardTape : (_hazardTape = BuildHazardTape(new Color32(226, 168, 36, 255), new Color32(22, 24, 18, 255)));
        public static Sprite HazardTapeRed => _hazardTapeRed != null ? _hazardTapeRed : (_hazardTapeRed = BuildHazardTape(new Color32(214, 64, 42, 255), new Color32(24, 18, 16, 255)));
        public static Sprite Scanlines => _scanlines != null ? _scanlines : (_scanlines = BuildScanlines());
        public static Sprite IconStar => _iconStar != null ? _iconStar : (_iconStar = BuildGlyphSprite("IconStar", StarGlyph, new Color32(255, 255, 255, 255)));
        public static Sprite IconCrosshair => _iconCrosshair != null ? _iconCrosshair : (_iconCrosshair = BuildGlyphSprite("IconCrosshair", CrosshairGlyph, new Color32(255, 255, 255, 255)));
        public static Sprite IconShell => _iconShell != null ? _iconShell : (_iconShell = BuildGlyphSprite("IconShell", ShellGlyph, new Color32(255, 255, 255, 255)));
        public static Sprite IconArmor => _iconArmor != null ? _iconArmor : (_iconArmor = BuildGlyphSprite("IconArmor", ArmorGlyph, new Color32(255, 255, 255, 255)));

        // Matches Unity CanvasScaler default referencePixelsPerUnit = 100 so 1 sprite pixel == 1 exact 480x270 HUD pixel!
        private const float UiSpritePpu = 100f;

        // ============================================================
        // 1. 9-Sliced Riveted Rolled-Steel Armor Plate (20x20, border 5px)
        // ============================================================
        private static Sprite BuildRivetedArmorPanel()
        {
            const int w = 20, h = 20;
            var px = new Color32[w * h];

            Color32 outline = new Color32(14, 16, 13, 255);
            Color32 bevelHi = new Color32(114, 122, 98, 255);
            Color32 bevelMid = new Color32(68, 74, 58, 255);
            Color32 bevelLo = new Color32(26, 30, 24, 255);
            Color32 steelA = new Color32(38, 43, 35, 255);
            Color32 steelB = new Color32(33, 38, 31, 255);
            Color32 steelC = new Color32(43, 48, 39, 255);
            Color32 rivetHi = new Color32(154, 162, 136, 255);
            Color32 rivetMid = new Color32(84, 90, 74, 255);
            Color32 rivetSh = new Color32(18, 21, 16, 255);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Chamfered 1px armor corners for rugged military silhouette
                    bool cutCorner = (x == 0 || x == w - 1) && (y == 0 || y == h - 1);
                    if (cutCorner) { px[y * w + x] = new Color32(0, 0, 0, 0); continue; }

                    bool diagCorner = (x == 1 || x == w - 2) && (y == 0 || y == h - 1) ||
                                      (x == 0 || x == w - 1) && (y == 1 || y == h - 2);
                    if (x == 0 || x == w - 1 || y == 0 || y == h - 1 || diagCorner)
                    {
                        px[y * w + x] = outline;
                        continue;
                    }

                    // Crisp metallic top-left sunlit bevel & bottom-right deep shadow bevel
                    if (y == h - 2 || x == 1)
                    {
                        px[y * w + x] = (x == w - 2 || y == 1) ? bevelMid : bevelHi;
                        continue;
                    }
                    if (y == 1 || x == w - 2)
                    {
                        px[y * w + x] = bevelLo;
                        continue;
                    }

                    // Brushed rolled-steel interior with subtle horizontal armor grain
                    int n = (x * 17 + y * 31) & 7;
                    Color32 baseCol = (y % 3 == 0) ? steelA : (n == 0 ? steelC : steelB);
                    px[y * w + x] = baseCol;
                }
            }

            // Stamp 4 crisp 2x2 forged corner bolts inside the 5px 9-slice corners!
            StampRivet(px, w, h, 2, h - 4, rivetHi, rivetMid, rivetSh);
            StampRivet(px, w, h, w - 4, h - 4, rivetHi, rivetMid, rivetSh);
            StampRivet(px, w, h, 2, 2, rivetHi, rivetMid, rivetSh);
            StampRivet(px, w, h, w - 4, 2, rivetHi, rivetMid, rivetSh);

            var tex = MakeTex("UI_RivetedArmorPanel", w, h, px, TextureWrapMode.Clamp);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), UiSpritePpu, 0, SpriteMeshType.FullRect, new Vector4(5, 5, 5, 5));
        }

        private static void StampRivet(Color32[] px, int w, int h, int bx, int by, Color32 hi, Color32 mid, Color32 sh)
        {
            // 2x2 bolt head with 1px shadow lip underneath
            SetPx(px, w, h, bx, by + 1, hi);
            SetPx(px, w, h, bx + 1, by + 1, mid);
            SetPx(px, w, h, bx, by, mid);
            SetPx(px, w, h, bx + 1, by, sh);
        }

        // ============================================================
        // 2. 9-Sliced Stamped-Steel Military Switch Button (16x16, border 4px)
        // Bright silver-metallic base so Image.color tints cleanly to olive, brass, cyan, or crimson steel!
        // ============================================================
        private static Sprite BuildSteelButton()
        {
            const int w = 16, h = 16;
            var px = new Color32[w * h];

            Color32 outline = new Color32(36, 38, 34, 255);
            Color32 hi = new Color32(255, 255, 246, 255);
            Color32 sh = new Color32(72, 76, 68, 255);
            Color32 bodyTop = new Color32(216, 222, 206, 255);
            Color32 bodyMid = new Color32(188, 194, 178, 255);
            Color32 bodyBot = new Color32(156, 162, 148, 255);
            Color32 bolt = new Color32(246, 248, 236, 255);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if ((x == 0 || x == w - 1) && (y == 0 || y == h - 1))
                    {
                        px[y * w + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }
                    if (x == 0 || x == w - 1 || y == 0 || y == h - 1)
                    {
                        px[y * w + x] = outline;
                        continue;
                    }
                    if (y == h - 2 || x == 1)
                    {
                        px[y * w + x] = (x == w - 2 || y == 1) ? bodyMid : hi;
                        continue;
                    }
                    if (y == 1 || x == w - 2)
                    {
                        px[y * w + x] = sh;
                        continue;
                    }

                    int grain = ((x * 13 + y * 29) & 7) == 0 ? 12 : 0;
                    Color32 b = y > h * 2 / 3 ? bodyTop : (y < h / 3 ? bodyBot : bodyMid);
                    px[y * w + x] = new Color32(
                        (byte)Mathf.Min(255, b.r + grain),
                        (byte)Mathf.Min(255, b.g + grain),
                        (byte)Mathf.Min(255, b.b + grain),
                        255
                    );
                }
            }

            // Tiny stamped corner pin-rivets
            SetPx(px, w, h, 2, h - 3, bolt);
            SetPx(px, w, h, w - 3, h - 3, bolt);
            SetPx(px, w, h, 2, 2, bolt);
            SetPx(px, w, h, w - 3, 2, bolt);

            var tex = MakeTex("UI_SteelButton", w, h, px, TextureWrapMode.Clamp);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), UiSpritePpu, 0, SpriteMeshType.FullRect, new Vector4(4, 4, 4, 4));
        }

        // ============================================================
        // 3. 9-Sliced Heavy Brass Ordnance Launch Switch (20x16, border 6px)
        // ============================================================
        private static Sprite BuildOrdnanceButton()
        {
            const int w = 20, h = 16;
            var px = new Color32[w * h];

            Color32 outline = new Color32(20, 14, 6, 255);
            Color32 hi = new Color32(255, 236, 158, 255);
            Color32 midTop = new Color32(246, 198, 78, 255);
            Color32 mid = new Color32(228, 174, 52, 255);
            Color32 midBot = new Color32(196, 140, 34, 255);
            Color32 sh = new Color32(118, 78, 16, 255);
            Color32 hazardDk = new Color32(34, 28, 14, 255);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if ((x == 0 || x == w - 1) && (y == 0 || y == h - 1))
                    {
                        px[y * w + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }
                    if (x == 0 || x == w - 1 || y == 0 || y == h - 1)
                    {
                        px[y * w + x] = outline;
                        continue;
                    }
                    if (y == h - 2 || x == 1)
                    {
                        px[y * w + x] = (x == w - 2 || y == 1) ? mid : hi;
                        continue;
                    }
                    if (y == 1 || x == w - 2)
                    {
                        px[y * w + x] = sh;
                        continue;
                    }

                    // Diagonal hazard caution end-caps on left (x=2..4) and right (x=w-5..w-3)!
                    if (x <= 4 || x >= w - 5)
                    {
                        bool darkStripe = ((x + y) % 4) < 2;
                        px[y * w + x] = darkStripe ? hazardDk : midTop;
                        continue;
                    }
                    if (x == 5 || x == w - 6)
                    {
                        px[y * w + x] = outline;
                        continue;
                    }

                    px[y * w + x] = y >= h - 5 ? midTop : (y <= 4 ? midBot : mid);
                }
            }

            var tex = MakeTex("UI_OrdnanceButton", w, h, px, TextureWrapMode.Clamp);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), UiSpritePpu, 0, SpriteMeshType.FullRect, new Vector4(6, 4, 6, 4));
        }

        // ============================================================
        // 4. Tiled 45-Degree Military Hazard Caution Tape (8x4)
        // ============================================================
        private static Sprite BuildHazardTape(Color32 warnCol, Color32 darkCol)
        {
            const int w = 8, h = 4;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool yellow = ((x + y) & 7) < 4;
                    Color32 c = yellow ? warnCol : darkCol;
                    // Subtle top specular & bottom shade on the caution tape
                    if (y == h - 1 && yellow)
                        c = new Color32((byte)Mathf.Min(255, c.r + 22), (byte)Mathf.Min(255, c.g + 22), (byte)Mathf.Min(255, c.b + 12), 255);
                    else if (y == 0)
                        c = new Color32((byte)(c.r * 3 / 4), (byte)(c.g * 3 / 4), (byte)(c.b * 3 / 4), 255);
                    px[y * w + x] = c;
                }
            }
            var tex = MakeTex("UI_HazardTape", w, h, px, TextureWrapMode.Repeat);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), UiSpritePpu);
        }

        // ============================================================
        // 5. Seamlessly Tiled Brushed Gunmetal Armor Steel (32x16)
        // ============================================================
        private static Sprite BuildBrushedSteelBar()
        {
            const int w = 32, h = 16;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int hash = ((x * 29 + y * 47) ^ (x * 7)) & 15;
                    byte baseV = (byte)(26 + (y % 4 == 0 ? 3 : 0) + (hash == 0 ? 4 : 0));
                    // Subtle vertical armor plate seam every 32px
                    if (x == 0) baseV = (byte)Mathf.Max(12, baseV - 7);
                    else if (x == 1) baseV = (byte)Mathf.Min(255, baseV + 5);
                    px[y * w + x] = new Color32((byte)(baseV + 2), (byte)(baseV + 5), baseV, 244);
                }
            }
            var tex = MakeTex("UI_BrushedSteelBar", w, h, px, TextureWrapMode.Repeat);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), UiSpritePpu);
        }

        // ============================================================
        // 6. Tiled Subtle Optical / FCS Scanlines (4x4)
        // ============================================================
        private static Sprite BuildScanlines()
        {
            const int w = 4, h = 4;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                byte a = (byte)(y % 2 == 0 ? 28 : 0);
                for (int x = 0; x < w; x++)
                    px[y * w + x] = new Color32(6, 12, 8, a);
            }
            var tex = MakeTex("UI_Scanlines", w, h, px, TextureWrapMode.Repeat);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), UiSpritePpu);
        }

        // ============================================================
        // 7. Crisp 1:1 Pixel-Art Military Icons (Star, Crosshair, Shell, Shield)
        // ============================================================
        private static readonly string[] StarGlyph =
        {
            "....#....",
            "....#....",
            "...###...",
            "#########",
            ".#######.",
            "..#####..",
            "..##.##..",
            ".##...##.",
            ".#.....#."
        };

        private static readonly string[] CrosshairGlyph =
        {
            "....#....",
            "..#####..",
            ".##.#.##.",
            ".#.....#.",
            "###.#.###",
            ".#.....#.",
            ".##.#.##.",
            "..#####..",
            "....#...."
        };

        private static readonly string[] ShellGlyph =
        {
            "...#...",
            "..###..",
            "..###..",
            ".#####.",
            ".#####.",
            ".#####.",
            ".#####.",
            "#######",
            ".#####."
        };

        private static readonly string[] ArmorGlyph =
        {
            ".#######.",
            "#########",
            "##.###.##",
            "##.###.##",
            "#########",
            ".#######.",
            "..#####..",
            "...###...",
            "....#...."
        };

        private static Sprite BuildGlyphSprite(string name, string[] rows, Color32 tint)
        {
            int h = rows.Length;
            int w = rows[0].Length;
            var px = new Color32[w * h];
            for (int gy = 0; gy < h; gy++)
            {
                int py = h - 1 - gy;
                string row = rows[gy];
                for (int x = 0; x < w && x < row.Length; x++)
                    px[py * w + x] = row[x] == '#' ? tint : new Color32(0, 0, 0, 0);
            }
            var tex = MakeTex(name, w, h, px, TextureWrapMode.Clamp);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), UiSpritePpu);
        }

        private static Texture2D MakeTex(string name, int w, int h, Color32[] pixels, TextureWrapMode wrap)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = wrap,
                hideFlags = HideFlags.DontSave
            };
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static void SetPx(Color32[] px, int w, int h, int x, int y, Color32 col)
        {
            if (x >= 0 && x < w && y >= 0 && y < h)
                px[y * w + x] = col;
        }

        // ============================================================
        // Convenience uGUI Builders (1:1 Native Pixel-Art Widgets!)
        // ============================================================
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
