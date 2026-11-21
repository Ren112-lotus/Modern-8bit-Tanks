using System;
using System.Collections.Generic;
using UnityEngine;

namespace Modern8bitTanks
{
    /// <summary>
    /// Renders a horizontal sprite-stack sheet (ground slice on left -> top slice on right)
    /// both in Edit Mode (Scene View / Prefab Stage preview, DontSave) and Play Mode
    /// (mutable per-slice Texture2Ds supporting localized voxel/pixel crumbling and red hit flash).
    /// </summary>
    [ExecuteAlways]
    public class SpriteStackRenderer : MonoBehaviour
    {
        public Texture2D sourceSheet;
        public int sliceCount = 10;
        public float pixelsPerUnit = 16f;
        [Range(0.2f, 1.0f)] public float squash = 0.75f;
        [Range(0f, 3.0f)] public float spacingPx = 1.0f;
        public float baseZOffsetSlices = 0f;
        public int baseSortingOrder = 0;
        public Material sliceMaterial;
        public bool addGroundShadow = true;

        public float angleDeg;
        public float hitFlash;
        public bool charred;

        private const string VisualContainerName = "__StackVisuals";
        private Transform _visualRoot;
        private readonly List<Texture2D> _sliceTextures = new();
        private readonly List<SpriteRenderer> _renderers = new();
        private readonly List<Transform> _holders = new();
        private SpriteRenderer _shadowSr;
        private Transform _shadowHolder;
        private SpriteRenderer _aoShadowSr;
        private Transform _aoShadowHolder;

        private int _sliceW;
        private int _sliceH;
        private MaterialPropertyBlock _mpb;
        private bool _dirtyRebuild;

        private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");
        private static readonly int CharredId = Shader.PropertyToID("_Charred");
        private static readonly int Height01Id = Shader.PropertyToID("_Height01");

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

        public void Configure(
            Texture2D sheet,
            int count,
            float ppu,
            Material mat,
            int sortingOrderOffset,
            float zOffsetSlices,
            bool groundShadow)
        {
            sourceSheet = sheet;
            sliceCount = count;
            pixelsPerUnit = ppu;
            sliceMaterial = mat;
            baseSortingOrder = sortingOrderOffset;
            baseZOffsetSlices = zOffsetSlices;
            addGroundShadow = groundShadow;
            _mpb ??= new MaterialPropertyBlock();
            RebuildStack();
        }

        public void RebuildStack()
        {
            ClearStack();
            if (sourceSheet == null || !sourceSheet.isReadable || sliceCount <= 0)
                return;

            _mpb ??= new MaterialPropertyBlock();
            _sliceW = sourceSheet.width / sliceCount;
            _sliceH = sourceSheet.height;
            if (_sliceW <= 0 || _sliceH <= 0) return;

            var rootGo = new GameObject(VisualContainerName);
            rootGo.transform.SetParent(transform, false);
            // Never serialize temporary preview/runtime slice children into .prefab or .unity files!
            rootGo.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            _visualRoot = rootGo.transform;

            Color32[] srcPixels = sourceSheet.GetPixels32();
            int sheetW = sourceSheet.width;

            if (addGroundShadow)
            {
                // Layer 1: Tight HD-2D Contact Ambient Occlusion shadow grounding treads/base onto sand
                _aoShadowHolder = new GameObject("AoShadowHolder").transform;
                _aoShadowHolder.SetParent(_visualRoot, false);
                var aoGo = new GameObject("AoShadow");
                aoGo.transform.SetParent(_aoShadowHolder, false);
                _aoShadowSr = aoGo.AddComponent<SpriteRenderer>();
                _aoShadowSr.sortingOrder = baseSortingOrder - 2;
                _aoShadowSr.color = new Color(0.11f, 0.09f, 0.14f, 0.34f);

                // Layer 2: Directional afternoon sun-cast shadow (cool desert indigo-umber!)
                _shadowHolder = new GameObject("ShadowHolder").transform;
                _shadowHolder.SetParent(_visualRoot, false);
                var shGo = new GameObject("Shadow");
                shGo.transform.SetParent(_shadowHolder, false);
                _shadowSr = shGo.AddComponent<SpriteRenderer>();
                _shadowSr.sortingOrder = baseSortingOrder - 3;
                _shadowSr.color = new Color(0.15f, 0.13f, 0.20f, 0.28f);
            }

            for (int z = 0; z < sliceCount; z++)
            {
                var tex = new Texture2D(_sliceW, _sliceH, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
                };

                var pixels = new Color32[_sliceW * _sliceH];
                for (int y = 0; y < _sliceH; y++)
                {
                    int srcRow = y * sheetW + z * _sliceW;
                    int dstRow = y * _sliceW;
                    for (int x = 0; x < _sliceW; x++)
                    {
                        pixels[dstRow + x] = srcPixels[srcRow + x];
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply(false, false);
                _sliceTextures.Add(tex);

                var holder = new GameObject($"Holder_{z:00}").transform;
                holder.SetParent(_visualRoot, false);
                _holders.Add(holder);

                var sGo = new GameObject($"Slice_{z:00}");
                sGo.transform.SetParent(holder, false);
                var sr = sGo.AddComponent<SpriteRenderer>();
                var spr = Sprite.Create(
                    tex,
                    new Rect(0, 0, _sliceW, _sliceH),
                    new Vector2(0.5f, 0.5f),
                    pixelsPerUnit,
                    0,
                    SpriteMeshType.FullRect
                );
                spr.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                sr.sprite = spr;
                sr.sortingOrder = baseSortingOrder + z;
                if (sliceMaterial != null) sr.sharedMaterial = sliceMaterial;
                _renderers.Add(sr);

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

        public void ApplyVisuals()
        {
            if (_renderers.Count == 0) return;
            float unitPerPx = 1f / Mathf.Max(1f, pixelsPerUnit);
            float camRoll = Application.isPlaying ? SpriteStackCamera.RollDeg : 0f;
            Vector2 camUp = Application.isPlaying ? SpriteStackCamera.CamUp : Vector2.up;
            Quaternion holderRot = Quaternion.Euler(0f, 0f, camRoll);
            Quaternion sliceRot = Quaternion.Euler(0f, 0f, angleDeg - camRoll);

            if (_aoShadowHolder != null && _aoShadowSr != null)
            {
                _aoShadowHolder.localPosition = new Vector3(0.55f * unitPerPx, -0.55f * unitPerPx, 0.04f);
                _aoShadowHolder.localRotation = holderRot;
                _aoShadowHolder.localScale = new Vector3(1.03f, squash * 1.03f, 1f);
                _aoShadowSr.transform.localRotation = sliceRot;
            }

            if (_shadowHolder != null && _shadowSr != null)
            {
                // Height-scaled directional afternoon shadow matching upper-left sun (-0.66, 0.75) -> lower-right cast
                float hScale = Mathf.Clamp(sliceCount / 10f, 0.75f, 2.2f);
                _shadowHolder.localPosition = new Vector3(3.6f * hScale * unitPerPx, -2.7f * hScale * unitPerPx, 0.05f);
                _shadowHolder.localRotation = holderRot;
                _shadowHolder.localScale = new Vector3(1.04f, squash * 0.96f, 1f);
                _shadowSr.transform.localRotation = sliceRot;
            }

            for (int z = 0; z < _renderers.Count; z++)
            {
                if (_renderers[z] == null || _holders[z] == null) continue;
                float liftY = Mathf.Round((baseZOffsetSlices + z) * spacingPx) * unitPerPx;
                float sliceZ = -(baseZOffsetSlices + z) * 0.002f;
                _holders[z].localPosition = new Vector3(camUp.x * liftY, camUp.y * liftY, sliceZ);
                _holders[z].localRotation = holderRot;
                _holders[z].localScale = new Vector3(1f, squash, 1f);
                _renderers[z].transform.localRotation = sliceRot;

                if (_mpb != null)
                {
                    float height01 = _renderers.Count > 1 ? (float)z / (_renderers.Count - 1) : 1f;
                    _renderers[z].GetPropertyBlock(_mpb);
                    _mpb.SetFloat(FlashAmountId, Mathf.Clamp01(hitFlash * 1.15f));
                    _mpb.SetFloat(CharredId, charred ? 1f : 0f);
                    _mpb.SetFloat(Height01Id, height01);
                    _renderers[z].SetPropertyBlock(_mpb);
                }
            }
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
                }
            }

            return destroyed;
        }

        private void ClearStack()
        {
            // Clean up any leftover preview container too
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name == VisualContainerName)
                {
                    if (Application.isPlaying) Destroy(child.gameObject);
                    else DestroyImmediate(child.gameObject);
                }
            }
            _visualRoot = null;
            _aoShadowHolder = null;
            _aoShadowSr = null;
            _shadowHolder = null;
            _shadowSr = null;

            foreach (var tex in _sliceTextures)
            {
                if (tex == null) continue;
                if (Application.isPlaying) Destroy(tex);
                else DestroyImmediate(tex);
            }
            _holders.Clear();
            _renderers.Clear();
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
