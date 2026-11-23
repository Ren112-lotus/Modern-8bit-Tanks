using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Modern8bitTanks
{
    /// <summary>
    /// Prefab & Runtime Sprite-Stacked tactical battlefield cover (desert mesas/compounds,
    /// multi-story city apartments/factories, forest pine clusters/boulders/cabins, & destructible props).
    /// Supports oriented-box (OBB) tank collision so tanks slide cleanly along large building walls,
    /// plus O(1) pixel solidMask so shells crumble masonry and fly through blasted breaches!
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(SortingGroup))]
    public class DestructibleProp : MonoBehaviour
    {
        [Header("Prop Configuration")]
        public string kind = "hesco_barrier";
        public PropMetaEntry meta;
        public SpriteStackRenderer stack;
        public float initialAngleDeg;
        public float pixelsPerUnit = 16f;

        [HideInInspector] public SortingGroup sortingGroup;
        [HideInInspector] public bool alive = true;
        [HideInInspector] public bool isDynamicSpawned;
        [HideInInspector] public float approxRadiusUnits = 1f;

        private bool[] _solidMask;
        private int _initialPixels;
        private int _remainingPixels;
        private Vector3 _savedStartPos;
        private bool _startPosCaptured;
        private float _renderedCameraRoll = float.NaN;

        private void OnEnable()
        {
            sortingGroup = GetComponent<SortingGroup>();
            if (stack == null) stack = GetComponentInChildren<SpriteStackRenderer>();
            if (!Application.isPlaying && stack != null)
            {
                stack.angleDeg = initialAngleDeg;
                UpdateSortOrder();
            }
        }

        private void Update()
        {
            if (!Application.isPlaying && stack != null)
            {
                stack.angleDeg = initialAngleDeg;
                UpdateSortOrder();
            }
        }

        public void ConfigureRuntime(string propKind, PropMetaEntry propMeta, Texture2D sheet, float angleDeg, float ppu, Material mat)
        {
            isDynamicSpawned = true;
            _startPosCaptured = false;
            kind = propKind;
            meta = propMeta;
            initialAngleDeg = angleDeg;
            pixelsPerUnit = ppu;
            stack.sourceSheet = sheet;
            stack.sliceCount = meta.slices;
            stack.pixelsPerUnit = ppu;
            stack.sliceMaterial = mat;
            stack.metallicWear = (kind == "fuel_tank" || kind == "tank_trap" || kind == "watch_tower" || kind == "city_factory")
                ? 0.55f
                : 0.08f;
            CaptureSpawnAndReset();
        }

        public void CaptureSpawnAndReset()
        {
            if (!_startPosCaptured)
            {
                _savedStartPos = transform.position;
                _startPosCaptured = true;
            }

            gameObject.SetActive(true);
            transform.position = _savedStartPos;
            alive = true;
            sortingGroup = GetComponent<SortingGroup>();
            if (stack == null) stack = GetComponentInChildren<SpriteStackRenderer>();
            if (stack == null) return;

            stack.angleDeg = initialAngleDeg;
            stack.hitFlash = 0f;
            stack.charred = false;
            stack.RebuildStack();

            _solidMask = new bool[stack.SliceWidth * stack.SliceHeight];
            stack.FillSolidMask(_solidMask);
            _initialPixels = Mathf.Max(1, stack.CountSolidPixels());
            _remainingPixels = _initialPixels;
            approxRadiusUnits = (Mathf.Max(meta.width, meta.length) * 0.52f) / pixelsPerUnit;
            UpdateSortOrder();
        }

        public void UpdateSortOrder()
        {
            if (sortingGroup == null) return;
            int order = Application.isPlaying
                ? SpriteStackCamera.DepthOrder(transform.position)
                : Mathf.Clamp(Mathf.RoundToInt(-transform.position.y * 100f), -10000, 10000);
            if (sortingGroup.sortingOrder != order) sortingGroup.sortingOrder = order;
        }

        public void Tick(float dt, float squash, float spacingPx)
        {
            if (!alive || stack == null) return;
            bool fadingHit = stack.hitFlash > 0f;
            if (fadingHit) stack.hitFlash = Mathf.Max(0f, stack.hitFlash - dt * 5f);
            float cameraRoll = SpriteStackCamera.RollDeg;
            if (fadingHit || cameraRoll != _renderedCameraRoll || stack.squash != squash || stack.spacingPx != spacingPx)
            {
                _renderedCameraRoll = cameraRoll;
                stack.squash = squash;
                stack.spacingPx = spacingPx;
                stack.ApplyVisuals();
            }
            UpdateSortOrder();
        }

        /// <summary>
        /// Exact Oriented Bounding Box (OBB) tank pushout in ground-plane space so tanks glide smoothly
        /// along wide rectangular buildings (apartments, factories, compounds, mesas) and narrow barricades!
        /// </summary>
        public void ResolveTankPush(ref Vector2 tankPos, float tankRadius, float squash)
        {
            if (!alive || meta == null) return;

            Vector2 center = transform.position;
            Vector2 delta = SpriteStackCamera.Unsquash(tankPos - center, squash);

            // Fast broad-phase circle reject
            float broad = approxRadiusUnits + tankRadius + 0.35f;
            if (delta.sqrMagnitude > broad * broad) return;

            float rad = -initialAngleDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad);
            float s = Mathf.Sin(rad);
            Vector2 local = new Vector2(delta.x * c - delta.y * s, delta.x * s + delta.y * c);

            float fillRatio = (kind == "palm_tree") ? 0.22f
                : (kind == "pine_cluster" || kind == "desert_mesa" || kind == "forest_boulder") ? 0.38f
                : 0.44f;
            float halfW = Mathf.Max(0.30f, (meta.width * fillRatio) / pixelsPerUnit);
            float halfL = Mathf.Max(0.30f, (meta.length * fillRatio) / pixelsPerUnit);

            Vector2 clamped = new Vector2(
                Mathf.Clamp(local.x, -halfW, halfW),
                Mathf.Clamp(local.y, -halfL, halfL)
            );
            Vector2 diff = local - clamped;
            float distSq = diff.sqrMagnitude;
            if (distSq >= tankRadius * tankRadius) return;

            Vector2 pushLocal;
            if (distSq > 0.00001f)
            {
                float d = Mathf.Sqrt(distSq);
                pushLocal = (diff / d) * (tankRadius - d);
            }
            else
            {
                // Center is inside box -> push out along shortest axis
                float px = (halfW + tankRadius) - Mathf.Abs(local.x);
                float py = (halfL + tankRadius) - Mathf.Abs(local.y);
                pushLocal = px < py
                    ? new Vector2(local.x >= 0f ? px : -px, 0f)
                    : new Vector2(0f, local.y >= 0f ? py : -py);
            }

            // Rotate push back to ground plane & re-apply screen-up squash
            Vector2 pushGround = new Vector2(pushLocal.x * c + pushLocal.y * s, -pushLocal.x * s + pushLocal.y * c);
            tankPos += SpriteStackCamera.Squash(pushGround, squash);
        }

        public bool IsSolidAt(Vector2 worldPos)
        {
            if (!alive || stack == null || _solidMask == null) return false;

            Vector2 d = SpriteStackCamera.Unsquash(worldPos - (Vector2)transform.position, stack.squash);
            if (d.magnitude > approxRadiusUnits + 0.35f) return false;

            Vector2 local = stack.WorldToLocalPixel(worldPos);
            int px = Mathf.FloorToInt(local.x);
            int py = Mathf.FloorToInt(local.y);
            if (px < 0 || px >= stack.SliceWidth || py < 0 || py >= stack.SliceHeight)
                return false;

            return _solidMask[py * stack.SliceWidth + px];
        }

        public bool ApplyHit(Vector2 worldPos, float radiusPx, bool isCannon, Action<Vector2, Color> onDebris)
        {
            if (!alive || stack == null) return false;

            bool isHeavy = meta != null && meta.heavyStructure;
            float effectiveR = isHeavy ? radiusPx * 0.72f : radiusPx;

            int removed = stack.CrumbleAtWorldPoint(worldPos, effectiveR, 0, -1, onDebris);
            if (removed > 0)
            {
                _remainingPixels = Mathf.Max(0, _remainingPixels - removed);
                stack.FillSolidMask(_solidMask);
            }

            float ratio = (float)_remainingPixels / Mathf.Max(1, _initialPixels);

            // Massive buildings, rock mesas, & granite cliffs stay standing as solid tactical cover
            // (taking awesome localized shell-scarred masonry damage without vanishing completely!)
            bool permanentMonument = isHeavy && kind != "pine_cluster" && kind != "forest_cabin";
            if (permanentMonument)
                return false;

            bool explosiveCollapse = (kind == "supply_crate" && isCannon && ratio < 0.72f)
                                     || (kind == "fuel_tank" && isCannon && ratio < 0.80f);
            bool treeOrTowerSnap = (kind == "palm_tree" || kind == "watch_tower") && isCannon && ratio < 0.62f;
            float collapseThresh = isHeavy ? 0.16f : 0.30f;

            if (ratio < collapseThresh || explosiveCollapse || treeOrTowerSnap)
            {
                alive = false;
                gameObject.SetActive(false);
                return true;
            }
            return false;
        }
    }
}
