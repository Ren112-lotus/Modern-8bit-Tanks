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
        [SerializeField, Min(1f)] private float sliceSpacingScale = 1f;
        [SerializeField] private bool revealOverlappingTank;

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
        private Vector2 _occlusionCenter;
        private float _occlusionAmount;

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
            stack.metallicWear = (kind == "fuel_tank" || kind == "tank_trap" || kind == "watch_tower" ||
                                  kind == "city_factory" || kind == "cargo_container" || kind == "radar_station" ||
                                  kind == "water_tower" || kind == "wrecked_truck" || kind == "steel_silo" || kind == "guard_booth" ||
                                  kind == "radio_mast" || kind == "oil_drum_stack" || kind == "checkpoint_gate" ||
                                  kind == "generator_unit" || kind == "wrecked_tank" || kind == "bus_stop" ||
                                  kind == "abandoned_car" || kind == "solar_array" ||
                                  kind == "rail_boxcar" || kind == "forklift_wreck" || kind == "sam_launcher" ||
                                  kind == "transformer_substation" || kind == "radar_dome" || kind == "field_artillery" ||
                                  kind == "fuel_pump_island" || kind == "crashed_heli")
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
            _occlusionAmount = 0f;
            stack.SetOcclusionFocus(Vector4.zero);
            stack.RebuildStack();

            _solidMask = new bool[stack.SliceWidth * stack.SliceHeight];
            stack.FillSolidMask(_solidMask, useGroundFootprint: meta.roundedFootprint);
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

        public void Tick(float dt, float squash, float spacingPx, Vector2? observerPosition = null)
        {
            if (!alive || stack == null) return;
            bool fadingHit = stack.hitFlash > 0f;
            if (fadingHit) stack.hitFlash = Mathf.Max(0f, stack.hitFlash - dt * 5f);
            spacingPx *= sliceSpacingScale;
            bool changedFocus = revealOverlappingTank && UpdateOcclusionFocus(observerPosition, spacingPx);
            float cameraRoll = SpriteStackCamera.RollDeg;
            if (fadingHit || changedFocus || cameraRoll != _renderedCameraRoll || stack.squash != squash || stack.spacingPx != spacingPx)
            {
                _renderedCameraRoll = cameraRoll;
                stack.squash = squash;
                stack.spacingPx = spacingPx;
                stack.ApplyVisuals();
            }
            UpdateSortOrder();
        }

        private bool UpdateOcclusionFocus(Vector2? observerPosition, float spacingPx)
        {
            const float revealRadius = 3f;
            bool overlaps = false;
            if (observerPosition is Vector2 observer)
            {
                Vector2 offset = observer - (Vector2)transform.position;
                float forward = Vector2.Dot(offset, SpriteStackCamera.CamUp);
                float side = Vector2.Dot(offset, SpriteStackCamera.CamRight);
                float lift = stack.SliceCount * spacingPx / pixelsPerUnit;
                overlaps = forward > 0f && forward < SpriteRadius + lift + revealRadius
                    && Mathf.Abs(side) < SpriteRadius + revealRadius &&
                    (stack.CoversWorldPoint(observer, spacingPx) ||
                     stack.CoversWorldPoint(observer - SpriteStackCamera.CamUp * 0.85f, spacingPx) ||
                     stack.CoversWorldPoint(observer + SpriteStackCamera.CamRight * 0.8f, spacingPx) ||
                     stack.CoversWorldPoint(observer - SpriteStackCamera.CamRight * 0.8f, spacingPx));
                _occlusionCenter = observer;
            }

            _occlusionAmount = Mathf.MoveTowards(_occlusionAmount, overlaps ? 1f : 0f, Time.unscaledDeltaTime * 8f);
            return stack.SetOcclusionFocus(_occlusionAmount > 0f
                ? new Vector4(_occlusionCenter.x, _occlusionCenter.y, revealRadius, _occlusionAmount)
                : Vector4.zero);
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
            float broad = (meta.fixedCover && stack != null ? SpriteRadius : approxRadiusUnits) + tankRadius + 0.35f;
            if (delta.sqrMagnitude > broad * broad) return;

            float rad = -initialAngleDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad);
            float s = Mathf.Sin(rad);
            Vector2 local = new Vector2(delta.x * c - delta.y * s, delta.x * s + delta.y * c);

            float fillRatio = meta.fixedCover ? 0.50f
                : (kind == "palm_tree" || kind == "desert_scrub" || kind == "dead_tree" || kind == "autumn_tree" || kind == "bush_cluster" || kind == "tree_stump" ||
                   kind == "cactus_cluster" || kind == "cypress_tree" || kind == "birch_clump" || kind == "reed_marsh" || kind == "bamboo_grove") ? 0.22f
                : (kind == "pine_cluster" || kind == "broadleaf_grove" || kind == "desert_mesa" || kind == "forest_boulder" || kind == "moss_rocks" || kind == "rock_arch" || kind == "steel_silo" ||
                   kind == "rock_pillar" || kind == "basalt_rocks" || kind == "hay_bale" || kind == "windmill_tower" || kind == "radar_dome" || kind == "stone_well") ? 0.38f
                : 0.44f;
            float halfW = Mathf.Max(0.30f, (meta.width * fillRatio) / pixelsPerUnit);
            float halfL = Mathf.Max(0.30f, (meta.length * fillRatio) / pixelsPerUnit);

            if (meta.roundedFootprint)
            {
                Vector2 radii = new Vector2(halfW + tankRadius, halfL + tankRadius);
                Vector2 normalized = new Vector2(local.x / radii.x, local.y / radii.y);
                if (normalized.sqrMagnitude >= 1f) return;
                Vector2 edge = Vector2.Scale(normalized.sqrMagnitude > 0.0001f ? normalized.normalized : Vector2.right, radii);
                Vector2 push = edge - local;
                tankPos += SpriteStackCamera.Squash(new Vector2(push.x * c + push.y * s, -push.x * s + push.y * c), squash);
                return;
            }

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
            float radius = SpriteRadius;
            if (d.sqrMagnitude > radius * radius) return false;
            return IsSolidPixel(stack.WorldToLocalPixel(worldPos));
        }

        internal float SpriteRadius => 0.5f * new Vector2(stack.SliceWidth, stack.SliceHeight).magnitude / pixelsPerUnit;

        private bool IsSolidPixel(Vector2 local)
        {
            int px = Mathf.FloorToInt(local.x);
            int py = Mathf.FloorToInt(local.y);
            if (px < 0 || px >= stack.SliceWidth || py < 0 || py >= stack.SliceHeight)
                return false;

            return _solidMask[py * stack.SliceWidth + px];
        }

        internal bool TryHitSegment(Vector2 from, Vector2 to, out float fraction)
        {
            fraction = 0f;
            if (!alive || stack == null || _solidMask == null) return false;

            Vector2 a = SpriteStackCamera.Unsquash(from - (Vector2)transform.position, stack.squash);
            Vector2 delta = SpriteStackCamera.Unsquash(to - from, stack.squash);
            float closest = Mathf.Clamp01(-Vector2.Dot(a, delta) / Mathf.Max(delta.sqrMagnitude, 0.000001f));
            float radius = SpriteRadius;
            if ((a + delta * closest).sqrMagnitude > radius * radius) return false;

            Vector2 start = stack.WorldToLocalPixel(from);
            Vector2 travel = stack.WorldToLocalPixel(to) - start;
            float enter = 0f, exit = 1f;
            if (!ClipAxis(start.x, travel.x, stack.SliceWidth, ref enter, ref exit)
                || !ClipAxis(start.y, travel.y, stack.SliceHeight, ref enter, ref exit)) return false;

            // Structural walls fill their footprint. Other props retain their per-pixel holes.
            if (meta != null && meta.fixedCover && !meta.roundedFootprint)
            {
                fraction = enter;
                return true;
            }

            int samples = Mathf.Max(1, Mathf.CeilToInt(travel.magnitude * (exit - enter) * 2f));
            for (int i = 0; i <= samples; i++)
            {
                float t = Mathf.Lerp(enter, exit, i / (float)samples);
                if (!IsSolidPixel(start + travel * t)) continue;
                fraction = t;
                return true;
            }
            return false;
        }

        private static bool ClipAxis(float start, float travel, int size, ref float enter, ref float exit)
        {
            if (Mathf.Abs(travel) < 0.000001f) return start >= 0 && start < size;
            float a = -start / travel, b = (size - start) / travel;
            enter = Mathf.Max(enter, Mathf.Min(a, b));
            exit = Mathf.Min(exit, Mathf.Max(a, b));
            return enter <= exit;
        }

        public bool ApplyHit(Vector2 worldPos, float radiusPx, bool isCannon, Action<Vector2, Color> onDebris)
        {
            if (!alive || stack == null || (meta != null && meta.fixedCover)) return false;

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
            bool permanentMonument = isHeavy && kind != "pine_cluster" && kind != "broadleaf_grove" && kind != "forest_cabin";
            if (permanentMonument)
                return false;

            bool explosiveCollapse = (kind == "supply_crate" && isCannon && ratio < 0.72f)
                                     || (kind == "fuel_tank" && isCannon && ratio < 0.80f)
                                     || (kind == "ammo_depot" && isCannon && ratio < 0.76f)
                                     || (kind == "oil_drum_stack" && isCannon && ratio < 0.78f)
                                     || (kind == "sam_launcher" && isCannon && ratio < 0.74f)
                                     || (kind == "transformer_substation" && isCannon && ratio < 0.74f)
                                     || (kind == "fuel_pump_island" && isCannon && ratio < 0.78f);
            bool treeOrTowerSnap = (kind == "palm_tree" || kind == "desert_scrub" || kind == "dead_tree" || kind == "autumn_tree" ||
                                    kind == "bush_cluster" || kind == "watch_tower" || kind == "radar_station" ||
                                    kind == "water_tower" || kind == "guard_booth" || kind == "radio_mast" || kind == "bus_stop" ||
                                    kind == "cactus_cluster" || kind == "cypress_tree" || kind == "birch_clump" || kind == "reed_marsh" || kind == "bamboo_grove") && isCannon && ratio < 0.62f;
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
