using System.Collections.Generic;
using UnityEngine;

namespace Modern8bitTanks
{
    /// <summary>
    /// Renders any of the 63 vehicles' ACTUAL 3D sprite-stack slices (hull_slices + turret_slices)
    /// into a crisp War-Thunder-style translucent ghost-steel X-Ray hologram Texture2D.
    /// Supports live per-frame hull & turret angle tracking and ballistic turret lift on ammo cookoff.
    /// </summary>
    public sealed class TankXRayRenderer
    {
        private static readonly Dictionary<Texture2D, Color32[]> SheetPixelCache = new();

        public Texture2D Texture { get; private set; }
        public int Width { get; }
        public int Height { get; }

        private readonly Color32[] _outPixels;
        private readonly byte[] _partBuf;   // 0 = empty, 1 = hull, 2 = turret
        private readonly float[] _zBuf;     // normalized height 0..1
        private readonly float[] _lumBuf;   // source luminance 0..1

        private Vector2 _centerPx;
        private float _hullDeg;
        private float _squash;
        private float _scale;
        private float _halfW;
        private float _halfL;
        private float _spacing;
        private int _hullBaseHeight;

        public TankXRayRenderer(int width, int height)
        {
            Width = Mathf.Max(16, width);
            Height = Mathf.Max(16, height);
            _outPixels = new Color32[Width * Height];
            _partBuf = new byte[Width * Height];
            _zBuf = new float[Width * Height];
            _lumBuf = new float[Width * Height];

            Texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
        }

        public void Dispose()
        {
            if (Texture != null)
            {
                Object.Destroy(Texture);
                Texture = null;
            }
        }

        private static Color32[] GetSheetPixels(Texture2D sheet)
        {
            if (sheet == null || !sheet.isReadable) return null;
            if (!SheetPixelCache.TryGetValue(sheet, out Color32[] px) || px == null)
            {
                px = sheet.GetPixels32();
                SheetPixelCache[sheet] = px;
            }
            return px;
        }

        public void Render(
            Texture2D hullSheet,
            Texture2D turretSheet,
            TankMetaEntry meta,
            float hullAngleDeg,
            float turretAngleDeg,
            float squash = 0.75f,
            float spacingPx = 1.0f,
            float scale = 1.0f,
            Vector2? centerOverride = null,
            bool drawGrid = false,
            bool transparentBg = true,
            float turretExtraLiftSlices = 0f,
            float cookoffHeat = 0f)
        {
            if (Texture == null || meta == null) return;

            int total = Width * Height;
            System.Array.Clear(_partBuf, 0, total);
            System.Array.Clear(_zBuf, 0, total);
            System.Array.Clear(_lumBuf, 0, total);

            _hullDeg = hullAngleDeg;
            _squash = Mathf.Clamp(squash, 0.35f, 1.0f);
            _spacing = spacingPx;
            _scale = Mathf.Max(0.4f, scale);
            _halfW = Mathf.Max(1f, meta.hullWidth * 0.5f);
            _halfL = Mathf.Max(1f, meta.hullLength * 0.5f);
            _hullBaseHeight = Mathf.Max(4, meta.hullBaseHeight);

            float totalSlices = Mathf.Max(1f, _hullBaseHeight + Mathf.Max(1, meta.turretSlices));
            _centerPx = centerOverride ?? new Vector2(Width * 0.5f, Height * 0.44f - (_hullBaseHeight * 0.32f) * _spacing * _scale);

            // 1. Rasterize REAL hull slices
            Color32[] hullPx = GetSheetPixels(hullSheet);
            if (hullPx != null && meta.hullSlices > 0)
            {
                int sw = hullSheet.width / meta.hullSlices;
                int sh = hullSheet.height;
                if (sw > 0 && sh > 0)
                {
                    RasterStack(hullPx, hullSheet.width, sw, sh, meta.hullSlices,
                        _centerPx, hullAngleDeg, 0f, totalSlices, partId: 1);
                }
            }

            // 2. Rasterize REAL turret slices (including live turret rotation & optional cookoff lift!)
            Color32[] turPx = GetSheetPixels(turretSheet);
            if (turPx != null && meta.turretSlices > 0)
            {
                int sw = turretSheet.width / meta.turretSlices;
                int sh = turretSheet.height;
                if (sw > 0 && sh > 0)
                {
                    Vector2 hd = TankEntity.SliceAngleToDir(hullAngleDeg);
                    Vector2 pivScreen = _centerPx + new Vector2(
                        hd.x * meta.hullPivotOffsetY * _scale,
                        hd.y * meta.hullPivotOffsetY * _squash * _scale
                    );
                    RasterStack(turPx, turretSheet.width, sw, sh, meta.turretSlices,
                        pivScreen, turretAngleDeg, _hullBaseHeight + turretExtraLiftSlices, totalSlices, partId: 2);
                }
            }

            // 3. War Thunder ghost-steel translucent shell (neutral cool steel so damaged modules & fire POP!)
            Color32 clearCol = transparentBg ? new Color32(0, 0, 0, 0) : new Color32(14, 17, 20, 220);
            Color32 gridCol = new Color32(34, 42, 48, 85);

            float heat = Mathf.Clamp01(cookoffHeat);
            Color32 outerEdgeHull = Color32.Lerp(new Color32(138, 162, 174, 215), new Color32(255, 138, 66, 235), heat);
            Color32 outerEdgeTurret = Color32.Lerp(new Color32(168, 194, 206, 225), new Color32(255, 178, 92, 240), heat);
            Color32 innerRidge = Color32.Lerp(new Color32(94, 114, 126, 170), new Color32(236, 108, 52, 205), heat);

            for (int y = 0; y < Height; y++)
            {
                int row = y * Width;
                for (int x = 0; x < Width; x++)
                {
                    int idx = row + x;
                    byte part = _partBuf[idx];
                    if (part == 0)
                    {
                        bool onGrid = drawGrid && ((x % 14 == 6) || (y % 14 == 6));
                        _outPixels[idx] = onGrid ? gridCol : clearCol;
                        continue;
                    }

                    byte pL = x > 0 ? _partBuf[idx - 1] : (byte)0;
                    byte pR = x < Width - 1 ? _partBuf[idx + 1] : (byte)0;
                    byte pD = y > 0 ? _partBuf[idx - Width] : (byte)0;
                    byte pU = y < Height - 1 ? _partBuf[idx + Width] : (byte)0;

                    if (pL == 0 || pR == 0 || pD == 0 || pU == 0)
                    {
                        _outPixels[idx] = part == 2 ? outerEdgeTurret : outerEdgeHull;
                        continue;
                    }

                    bool isPartBoundary = (pL != part || pR != part || pD != part || pU != part);
                    float zc = _zBuf[idx];
                    float zDiff = Mathf.Max(
                        Mathf.Max(Mathf.Abs(zc - _zBuf[idx - 1]), Mathf.Abs(zc - _zBuf[idx + 1])),
                        Mathf.Max(Mathf.Abs(zc - _zBuf[idx - Width]), Mathf.Abs(zc - _zBuf[idx + Width]))
                    );

                    if (isPartBoundary || zDiff > 0.16f)
                    {
                        _outPixels[idx] = innerRidge;
                    }
                    else
                    {
                        float lum = _lumBuf[idx];
                        float shade = 0.50f + 0.42f * zc + 0.30f * lum;
                        float baseR = Mathf.Lerp(34f, 168f, heat);
                        float baseG = Mathf.Lerp(46f, 68f, heat);
                        float baseB = Mathf.Lerp(54f, 34f, heat);
                        byte r = (byte)Mathf.Clamp(Mathf.RoundToInt(baseR * shade), 18, 220);
                        byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(baseG * shade), 24, 130);
                        byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(baseB * shade), 22, 108);
                        byte a = part == 2 ? (byte)108 : (byte)124;
                        _outPixels[idx] = new Color32(r, g, b, a);
                    }
                }
            }

            Texture.SetPixels32(_outPixels);
            Texture.Apply(false, false);
        }

        private void RasterStack(
            Color32[] sheetPx,
            int sheetW,
            int sliceW,
            int sliceH,
            int sliceCount,
            Vector2 centerPx,
            float angleDeg,
            float baseZOffset,
            float totalSlices,
            byte partId)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            float cosA = Mathf.Cos(rad);
            float sinA = Mathf.Sin(rad);
            float invScale = 1f / _scale;
            float invSquash = 1f / _squash;
            float halfSW = (sliceW - 1) * 0.5f;
            float halfSH = (sliceH - 1) * 0.5f;

            float maxR = Mathf.Sqrt(halfSW * halfSW + halfSH * halfSH) * _scale + 2f;
            int minX = Mathf.Max(0, Mathf.FloorToInt(centerPx.x - maxR));
            int maxX = Mathf.Min(Width - 1, Mathf.CeilToInt(centerPx.x + maxR));

            for (int z = 0; z < sliceCount; z++)
            {
                float liftY = (baseZOffset + z) * _spacing * _scale;
                float cy = centerPx.y + liftY;
                float zNorm = Mathf.Clamp01((baseZOffset + z) / totalSlices);
                int minY = Mathf.Max(0, Mathf.FloorToInt(cy - maxR * _squash));
                int maxY = Mathf.Min(Height - 1, Mathf.CeilToInt(cy + maxR * _squash));
                int xOff = z * sliceW;

                for (int py = minY; py <= maxY; py++)
                {
                    float dy = (py - cy) * invSquash * invScale;
                    int dstRow = py * Width;
                    for (int px = minX; px <= maxX; px++)
                    {
                        float dx = (px - centerPx.x) * invScale;
                        float sx = dx * cosA + dy * sinA + halfSW;
                        float sy = -dx * sinA + dy * cosA + halfSH;
                        int ix = Mathf.RoundToInt(sx);
                        int iy = Mathf.RoundToInt(sy);
                        if (ix < 0 || ix >= sliceW || iy < 0 || iy >= sliceH) continue;

                        Color32 c = sheetPx[iy * sheetW + xOff + ix];
                        if (c.a < 105) continue;

                        int idx = dstRow + px;
                        _partBuf[idx] = partId;
                        _zBuf[idx] = zNorm;
                        _lumBuf[idx] = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
                    }
                }
            }
        }

        public Vector2 ProjectLocalToPixel(Vector2 localNorm, float zSlices)
        {
            float sx = localNorm.x * _halfW;
            float sy = -localNorm.y * _halfL;
            float rad = _hullDeg * Mathf.Deg2Rad;
            float cosA = Mathf.Cos(rad);
            float sinA = Mathf.Sin(rad);
            float rx = sx * cosA - sy * sinA;
            float ry = sx * sinA + sy * cosA;
            return new Vector2(
                _centerPx.x + rx * _scale,
                _centerPx.y + (ry * _squash + zSlices * _spacing) * _scale
            );
        }
    }
}
