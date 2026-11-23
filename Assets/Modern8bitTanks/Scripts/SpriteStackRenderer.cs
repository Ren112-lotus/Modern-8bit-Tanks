using System;
using System.Collections.Generic;
using UnityEngine;

namespace Modern8bitTanks
{
    /// <summary>
    /// Renders a horizontal sprite-stack sheet (ground slice on left -> top slice on right)
    /// both in Edit Mode (Scene View / Prefab Stage preview, DontSave) and Play Mode
    /// (mutable per-slice Texture2Ds supporting localized voxel/pixel crumbling and red hit flash).
    /// Supports tall vehicle profiles cleanly without horizontal slice gaps via sub-layer doubling.
    /// </summary>
    [ExecuteAlways]
    public class SpriteStackRenderer : MonoBehaviour
    {
        public Texture2D sourceSheet;
        public int sliceCount = 10;
        public float pixelsPerUnit = 16f;
        [Range(0.2f, 1.0f)] public float squash = 0.80f;
        [Range(0f, 3.0f)] public float spacingPx = 0.72f;
        public float baseZOffsetSlices = 0f;
        public int baseSortingOrder = 0;
        public Material sliceMaterial;
        public bool addGroundShadow = true;

        public float angleDeg;
        public float hitFlash;
        public bool charred;
        [Range(0f, 1f)] public float metallicWear = 0.20f;
        /// <summary>
        /// 1.0 = fully assembled; 0..1 animates individual slices slowly descending from overhead and stacking cleanly.
        /// </summary>
        [Range(0f, 1f)] public float dropProgress = 1f;
        public float dropHeightPx = 44f;

        private bool _wasDropping;

        private const string VisualContainerName = "__StackVisuals";
        private static SpriteStackVisuals visualPrefab;
        private Transform _visualRoot;
        private readonly List<Texture2D> _sliceTextures = new();
        private readonly List<Sprite> _sliceSprites = new();
        private readonly List<SpriteRenderer> _renderers = new();
        private readonly List<Transform> _holders = new();
        private readonly List<SpriteRenderer> _fillRenderers = new();
        private readonly List<Transform> _fillHolders = new();
        private SpriteRenderer _shadowSr;
        private Transform _shadowHolder;
        private SpriteRenderer _aoShadowSr;
        private Transform _aoShadowHolder;

        private int _sliceW;
        private int _sliceH;
        private MaterialPropertyBlock _mpb;
        private bool _dirtyRebuild;
        private bool _slicePixelsDamaged;

        private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");
        private static readonly int CharredId = Shader.PropertyToID("_Charred");
        private static readonly int Height01Id = Shader.PropertyToID("_Height01");
        private static readonly int MetallicWearId = Shader.PropertyToID("_MetallicWear");

        public int SliceWidth => _sliceW;
        public int SliceHeight => _sliceH;
        public int SliceCount => _sliceTextures.Count;

        private void OnEnable()
        {
            _mpb ??= new MaterialPropertyBlock();
            if (sourceSheet != null && sourceSheet.isReadable && _renderers.Count == 0)
            {
                RebuildStack();
            }
        }

        private void OnValidate()
        {
            _dirtyRebuild = true;
        }

        private void Update()
        {
            if (_dirtyRebuild)
            {
                _dirtyRebuild = false;
                if (sourceSheet != null && sourceSheet.isReadable)
                    RebuildStack();
            }
            else if (!Application.isPlaying && _renderers.Count > 0)
            {
                ApplyVisuals();
            }
        }

        internal void SetStackSource(
            Texture2D sheet,
            int count,
            float ppu,
            Material mat,
            int sortingOrderOffset,
            float zOffsetSlices,
            bool groundShadow)
        {
            _dirtyRebuild |= sourceSheet != sheet || sliceCount != count || pixelsPerUnit != ppu ||
                sliceMaterial != mat || baseSortingOrder != sortingOrderOffset || addGroundShadow != groundShadow;
            sourceSheet = sheet;
            sliceCount = count;
            pixelsPerUnit = ppu;
            sliceMaterial = mat;
            baseSortingOrder = sortingOrderOffset;
            baseZOffsetSlices = zOffsetSlices;
            addGroundShadow = groundShadow;
        }

        internal void ResetDamageVisuals()
        {
            hitFlash = 0f;
            charred = false;

            if (_dirtyRebuild || _visualRoot == null)
            {
                RebuildStack();
                return;
            }

            if (_slicePixelsDamaged)
            {
                Color32[] sourcePixels = sourceSheet.GetPixels32();
                var slicePixels = new Color32[_sliceW * _sliceH];
                for (int z = 0; z < _sliceTextures.Count; z++)
                    UploadSlicePixels(_sliceTextures[z], sourcePixels, slicePixels, z);

                _slicePixelsDamaged = false;
            }

            ApplyVisuals();
        }

        public void RebuildStack()
        {
            _dirtyRebuild = false;
            ClearStack();
            if (sourceSheet == null || !sourceSheet.isReadable || sliceCount <= 0)
                return;

            _mpb ??= new MaterialPropertyBlock();
            _sliceW = sourceSheet.width / sliceCount;
            _sliceH = sourceSheet.height;
            if (_sliceW <= 0 || _sliceH <= 0) return;

            if (visualPrefab == null)
                visualPrefab = Resources.Load<SpriteStackVisuals>("SpriteStack/SpriteStackVisuals");
            if (visualPrefab == null)
            {
                Debug.LogError("The sprite stack visuals prefab is missing.", this);
                return;
            }

            var visuals = Instantiate(visualPrefab, transform, false);
            visuals.name = VisualContainerName;
            visuals.gameObject.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            _visualRoot = visuals.transform;

            var contact = visuals.ContactShadow;
            var cast = visuals.CastShadow;
            contact.transform.parent.gameObject.SetActive(addGroundShadow);
            cast.transform.parent.gameObject.SetActive(addGroundShadow);
            if (addGroundShadow)
            {
                _aoShadowSr = contact;
                _aoShadowHolder = contact.transform.parent;
                _aoShadowSr.sortingOrder = baseSortingOrder - 2;
                _shadowSr = cast;
                _shadowHolder = cast.transform.parent;
                _shadowSr.sortingOrder = baseSortingOrder - 3;
            }

            Color32[] sourcePixels = sourceSheet.GetPixels32();
            var slicePixels = new Color32[_sliceW * _sliceH];

            for (int z = 0; z < sliceCount; z++)
            {
                var tex = new Texture2D(_sliceW, _sliceH, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
                };

                UploadSlicePixels(tex, sourcePixels, slicePixels, z);
                _sliceTextures.Add(tex);

                var spr = Sprite.Create(
                    tex,
                    new Rect(0, 0, _sliceW, _sliceH),
                    new Vector2(0.5f, 0.5f),
                    pixelsPerUnit,
                    0,
                    SpriteMeshType.FullRect
                );
                spr.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

                _sliceSprites.Add(spr);
                var layer = Instantiate(visuals.Layer, _visualRoot, false);
                layer.name = $"Layer_{z:00}";
                var sr = layer.Slice;
                sr.sprite = spr;
                sr.sortingOrder = baseSortingOrder + z * 2;
                if (sliceMaterial != null) sr.sharedMaterial = sliceMaterial;
                _renderers.Add(sr);
                _holders.Add(sr.transform.parent);

                var fill = layer.Fill;
                fill.sprite = spr;
                fill.sortingOrder = baseSortingOrder + z * 2 + 1;
                if (sliceMaterial != null) fill.sharedMaterial = sliceMaterial;
                _fillRenderers.Add(fill);
                _fillHolders.Add(fill.transform.parent);

                if (addGroundShadow && z == 1)
                {
                    if (_aoShadowSr != null) _aoShadowSr.sprite = spr;
                    if (_shadowSr != null) _shadowSr.sprite = spr;
                }
                else if (addGroundShadow && z == Mathf.Clamp(sliceCount / 2, 1, sliceCount - 1) && _shadowSr != null)
                {
                    // Use mid-height slice silhouette for cast shadow so tall towers/palms cast accurate shapes!
                    _shadowSr.sprite = spr;
                }
            }

            ApplyVisuals();
        }

        private void UploadSlicePixels(Texture2D texture, Color32[] sourcePixels, Color32[] slicePixels, int sliceIndex)
        {
            int sheetWidth = sourceSheet.width;
            for (int y = 0; y < _sliceH; y++)
            {
                int rowOffset = y * sheetWidth + sliceIndex * _sliceW;
                Array.Copy(sourcePixels, rowOffset, slicePixels, y * _sliceW, _sliceW);
            }

            texture.SetPixels32(slicePixels);
            texture.Apply(false, false);
        }

        public void ApplyVisuals()
        {
            if (_renderers.Count == 0) return;
            float unitPerPx = 1f / Mathf.Max(1f, pixelsPerUnit);
            float camRoll = Application.isPlaying ? SpriteStackCamera.RollDeg : 0f;
            Vector2 camUp = Application.isPlaying ? SpriteStackCamera.CamUp : Vector2.up;
            Quaternion holderRot = Quaternion.Euler(0f, 0f, camRoll);
            Quaternion sliceRot = Quaternion.Euler(0f, 0f, angleDeg - camRoll);
            bool useFill = spacingPx > 1.06f;

            bool dropping = dropProgress < 0.999f;
            float shadowVis = dropping ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(dropProgress * 2.4f)) : 1f;

            if (_aoShadowHolder != null && _aoShadowSr != null)
            {
                float sSpread = Mathf.Lerp(1.16f, 1.03f, shadowVis);
                _aoShadowHolder.localPosition = new Vector3(0.55f * unitPerPx, -0.55f * unitPerPx, 0.04f);
                _aoShadowHolder.localRotation = holderRot;
                _aoShadowHolder.localScale = new Vector3(sSpread, squash * sSpread, 1f);
                _aoShadowSr.transform.localRotation = sliceRot;
                if (dropping || _wasDropping)
                    _aoShadowSr.color = new Color(0.11f, 0.09f, 0.14f, 0.34f * shadowVis);
            }

            if (_shadowHolder != null && _shadowSr != null)
            {
                // Height-scaled directional afternoon shadow matching upper-left sun (-0.66, 0.75) -> lower-right cast
                float hScale = Mathf.Clamp((sliceCount * spacingPx) / 10f, 0.85f, 2.4f);
                _shadowHolder.localPosition = new Vector3(3.6f * hScale * unitPerPx, -2.7f * hScale * unitPerPx, 0.05f);
                _shadowHolder.localRotation = holderRot;
                _shadowHolder.localScale = new Vector3(1.04f, squash * 0.96f, 1f);
                _shadowSr.transform.localRotation = sliceRot;
                if (dropping || _wasDropping)
                    _shadowSr.color = new Color(0.15f, 0.13f, 0.20f, 0.28f * shadowVis);
            }

            for (int z = 0; z < _renderers.Count; z++)
            {
                if (_renderers[z] == null || _holders[z] == null) continue;
                float extraDropLift = 0f;
                float sliceAlpha = 1f;
                if (dropping)
                {
                    float zFrac = _renderers.Count > 1 ? (float)z / (_renderers.Count - 1) : 0f;
                    float sliceStart = zFrac * 0.56f;
                    const float sliceDur = 0.44f;
                    float t = Mathf.Clamp01((dropProgress - sliceStart) / sliceDur);
                    // Smooth cubic ease-out: glides gently down from the sky and settles softly into place
                    float ease = 1f - Mathf.Pow(1f - t, 3f);
                    extraDropLift = (1f - ease) * dropHeightPx * unitPerPx;
                    sliceAlpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 2.4f));
                }

                float liftY = (baseZOffsetSlices + z) * spacingPx * unitPerPx + extraDropLift;
                float sliceZ = -(baseZOffsetSlices + z) * 0.002f;
                _holders[z].localPosition = new Vector3(camUp.x * liftY, camUp.y * liftY, sliceZ);
                _holders[z].localRotation = holderRot;
                _holders[z].localScale = new Vector3(1f, squash, 1f);
                _renderers[z].transform.localRotation = sliceRot;
                if (dropping || _wasDropping)
                    _renderers[z].color = new Color(1f, 1f, 1f, sliceAlpha);

                float height01 = _renderers.Count > 1 ? (float)z / (_renderers.Count - 1) : 1f;
                if (_mpb != null)
                {
                    _renderers[z].GetPropertyBlock(_mpb);
                    _mpb.SetFloat(FlashAmountId, Mathf.Clamp01(hitFlash * 1.15f));
                    _mpb.SetFloat(CharredId, charred ? 1f : 0f);
                    _mpb.SetFloat(Height01Id, height01);
                    _mpb.SetFloat(MetallicWearId, metallicWear);
                    _renderers[z].SetPropertyBlock(_mpb);
                }

                if (z < _fillRenderers.Count && _fillRenderers[z] != null && _fillHolders[z] != null)
                {
                    bool fillOn = useFill && !dropping;
                    _fillRenderers[z].enabled = fillOn;
                    if (fillOn)
                    {
                        float fillLiftY = (baseZOffsetSlices + z + 0.5f) * spacingPx * unitPerPx;
                        float fillZ = -(baseZOffsetSlices + z + 0.5f) * 0.002f;
                        _fillHolders[z].localPosition = new Vector3(camUp.x * fillLiftY, camUp.y * fillLiftY, fillZ);
                        _fillHolders[z].localRotation = holderRot;
                        _fillHolders[z].localScale = new Vector3(1f, squash, 1f);
                        _fillRenderers[z].transform.localRotation = sliceRot;

                        if (_mpb != null)
                        {
                            float fillH01 = _renderers.Count > 1 ? Mathf.Clamp01((z + 0.5f) / (_renderers.Count - 1)) : 1f;
                            _fillRenderers[z].GetPropertyBlock(_mpb);
                            _mpb.SetFloat(FlashAmountId, Mathf.Clamp01(hitFlash * 1.15f));
                            _mpb.SetFloat(CharredId, charred ? 1f : 0f);
                            _mpb.SetFloat(Height01Id, fillH01);
                            _mpb.SetFloat(MetallicWearId, metallicWear);
                            _fillRenderers[z].SetPropertyBlock(_mpb);
                        }
                    }
                }
            }
            _wasDropping = dropping;
        }

        public int CountSolidPixels()
        {
            int count = 0;
            foreach (var tex in _sliceTextures)
            {
                if (tex == null) continue;
                Color32[] px = tex.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                    if (px[i].a > 110) count++;
            }
            return count;
        }

        public void FillSolidMask(bool[] mask)
        {
            Array.Clear(mask, 0, mask.Length);
            int[] checkZ = { 2, 5, Mathf.Min(_sliceTextures.Count - 1, 8) };
            foreach (int z in checkZ)
            {
                if (z < 0 || z >= _sliceTextures.Count || _sliceTextures[z] == null) continue;
                Color32[] px = _sliceTextures[z].GetPixels32();
                for (int i = 0; i < px.Length && i < mask.Length; i++)
                {
                    if (px[i].a > 100) mask[i] = true;
                }
            }
        }

        public Vector2 WorldToLocalPixel(Vector2 worldPos)
        {
            Vector2 delta = SpriteStackCamera.Unsquash(worldPos - (Vector2)transform.position, squash);
            float rad = -angleDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad);
            float s = Mathf.Sin(rad);
            Vector2 rot = new Vector2(delta.x * c - delta.y * s, delta.x * s + delta.y * c);
            return new Vector2(rot.x * pixelsPerUnit + _sliceW * 0.5f, rot.y * pixelsPerUnit + _sliceH * 0.5f);
        }

        public int CrumbleAtWorldPoint(
            Vector2 worldImpact,
            float radiusPx,
            int zStart,
            int zEnd,
            Action<Vector2, Color> onDebrisPixel)
        {
            if (_sliceTextures.Count == 0) return 0;
            hitFlash = 1.0f;

            Vector2 local = WorldToLocalPixel(worldImpact);
            int minZ = Mathf.Clamp(zStart, 0, _sliceTextures.Count);
            int maxZ = Mathf.Clamp(zEnd < 0 ? _sliceTextures.Count : zEnd, minZ, _sliceTextures.Count);

            int destroyed = 0;
            int debrisCount = 0;
            const int maxDebris = 20;
            float unitPerPx = 1f / pixelsPerUnit;

            for (int z = minZ; z < maxZ; z++)
            {
                var tex = _sliceTextures[z];
                if (tex == null) continue;
                Color32[] pixels = tex.GetPixels32();
                float rSlice = radiusPx * UnityEngine.Random.Range(0.78f, 1.20f);
                float rChar = rSlice + 1.35f;

                int x0 = Mathf.Clamp(Mathf.FloorToInt(local.x - rChar - 1), 0, _sliceW - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt(local.x + rChar + 1), 0, _sliceW - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(local.y - rChar - 1), 0, _sliceH - 1);
                int y1 = Mathf.Clamp(Mathf.CeilToInt(local.y + rChar + 1), 0, _sliceH - 1);

                bool changed = false;
                for (int py = y0; py <= y1; py++)
                {
                    for (int px = x0; px <= x1; px++)
                    {
                        int idx = py * _sliceW + px;
                        if (pixels[idx].a < 100) continue;

                        float dist = Vector2.Distance(new Vector2(px, py), local);
                        float jitter = ((px * 37 + py * 73 + z * 19) & 7) * 0.14f - 0.45f;
                        if (dist + jitter <= rSlice)
                        {
                            if (onDebrisPixel != null && debrisCount < maxDebris && UnityEngine.Random.value < 0.32f)
                            {
                                Vector2 spawnPos = worldImpact + SpriteStackCamera.CamUp * ((baseZOffsetSlices + z) * spacingPx * unitPerPx);
                                onDebrisPixel(spawnPos, pixels[idx]);
                                debrisCount++;
                            }
                            pixels[idx].a = 0;
                            destroyed++;
                            changed = true;
                        }
                        else if (dist <= rChar)
                        {
                            pixels[idx].r = (byte)(pixels[idx].r * 0.48f);
                            pixels[idx].g = (byte)(pixels[idx].g * 0.45f);
                            pixels[idx].b = (byte)(pixels[idx].b * 0.42f);
                            changed = true;
                        }
                    }
                }

                if (changed)
                {
                    tex.SetPixels32(pixels);
                    tex.Apply(false, false);
                    _slicePixelsDamaged = true;
                }
            }

            return destroyed;
        }

        private void ClearStack()
        {
            // Clean up any leftover preview/voxel container too
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name == VisualContainerName || child.name == "__VoxelMeshVisual")
                {
                    if (Application.isPlaying)
                    {
                        child.gameObject.SetActive(false);
                        Destroy(child.gameObject);
                    }
                    else
                    {
                        DestroyImmediate(child.gameObject);
                    }
                }
            }
            _visualRoot = null;
            _slicePixelsDamaged = false;
            _sliceW = 0;
            _sliceH = 0;
            _aoShadowHolder = null;
            _aoShadowSr = null;
            _shadowHolder = null;
            _shadowSr = null;

            foreach (var sprite in _sliceSprites)
            {
                if (sprite == null) continue;
                if (Application.isPlaying) Destroy(sprite);
                else DestroyImmediate(sprite);
            }
            _sliceSprites.Clear();

            foreach (var tex in _sliceTextures)
            {
                if (tex == null) continue;
                if (Application.isPlaying) Destroy(tex);
                else DestroyImmediate(tex);
            }
            _holders.Clear();
            _renderers.Clear();
            _fillHolders.Clear();
            _fillRenderers.Clear();
            _sliceTextures.Clear();
        }

        private void OnDisable()
        {
            if (!Application.isPlaying)
                ClearStack();
        }

        private void OnDestroy()
        {
            ClearStack();
        }
    }
}
