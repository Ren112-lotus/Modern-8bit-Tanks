using UnityEngine;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed partial class TankCutawayGraphic : MaskableGraphic
    {
        private enum Mode { Status, Replay, Hangar }
        // War Thunder Authentic X-Ray Color Palette
        private static readonly Color Grid = new Color32(42, 52, 58, 145);
        private static readonly Color WtIntactModule = new Color32(174, 188, 196, 235);
        private static readonly Color WtIntactCrew = new Color32(204, 216, 224, 240);
        private static readonly Color WtDamagedYellow = new Color32(255, 206, 68, 255);
        private static readonly Color WtDamagedOrange = new Color32(255, 128, 44, 255);
        private static readonly Color WtDestroyedRed = new Color32(238, 58, 48, 255);
        private static readonly Color WtBlackened = new Color32(48, 24, 24, 245);
        private static readonly Color WtPreviousDamage = new Color32(138, 52, 44, 235);
        private static readonly Color WtInactive = new Color32(82, 92, 98, 185);
        private static readonly Color Dark = new Color32(14, 18, 22, 230);

        private static readonly TankCrew[] CrewSeats = { TankCrew.Driver, TankCrew.Gunner, TankCrew.Commander, TankCrew.Loader };
        private const float StatusCrewColumnWidth = 12f;

        private Mode _mode = Mode.Status;
        private TankXRayRenderer _xray;
        private RawImage _bgImage;
        private RectMask2D _viewportMask;
        [SerializeField] private UnityEngine.UI.Text hangarAttackerLabel;
        [SerializeField] private UnityEngine.UI.Text hangarAmmoLabel;
        [SerializeField] private UnityEngine.UI.Text hangarArmorResultLabel;
        [SerializeField] private UnityEngine.UI.Text hangarDamageStatusLabel;
        [SerializeField] private UnityEngine.UI.Text hangarDisabledModulesLabel;
        private Rect StatusBodyRect
        {
            get
            {
                Vector4 padding = _viewportMask != null ? _viewportMask.padding : Vector4.zero;
                return Rect.MinMaxRect(padding.x, padding.y, _xray.Width - padding.z, _xray.Height - padding.w);
            }
        }

        // Cached vehicle state
        private string _vehicleId;
        private TankModule _disabled;
        private TankCrew _crew;
        private int _crewCapacity;
        private bool _active;
        private bool _onFire;
        private bool _frontEngine;
        private bool _wheeled;
        private int _wheelAxles;
        private bool _unmannedTurret;
        private bool _turretless;
        private TankMetaEntry _meta;
        private float _lastStatusTurretRel = -999f;

        private struct ModuleMarker
        {
            public TankModule Module;
            public Vector2 LocalPosition;
            public Vector2 Position;
            public Vector2 HalfSize;

            public ModuleMarker(TankModule module, Vector2 localPosition, Vector2 position, float scale)
            {
                Module = module;
                LocalPosition = localPosition;
                Position = position;
                HalfSize = ModuleHalfSize(module) * scale;
            }
        }

        public void AttachBackdrop(RawImage rawImg, int texW, int texH)
        {
            _bgImage = rawImg;
            _viewportMask = GetComponentInParent<RectMask2D>();
            if (_xray != null) _xray.Dispose();
            _xray = new TankXRayRenderer(texW, texH);
            if (_bgImage != null)
            {
                _bgImage.texture = _xray.Texture;
                _bgImage.color = Color.white;
                _bgImage.raycastTarget = false;
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_xray != null)
            {
                _xray.Dispose();
                _xray = null;
            }
        }

        public static Vector3[] ReadHullOutline(SpriteStackRenderer hull) => null;

        public void ShowStatus(TankEntity tank, Vector3[] unusedOutline = null)
        {
            if (tank == null || tank.meta == null) return;
            _mode = Mode.Status;
            float turretRel = Mathf.Round(Mathf.DeltaAngle(tank.hullAngleDeg, tank.turretAngleDeg) / 5f) * 5f;
            bool vehicleChanged = _vehicleId != tank.tankType || _meta != tank.meta || Mathf.Abs(turretRel - _lastStatusTurretRel) > 0.5f;
            bool stateChanged = _crewCapacity != tank.CrewCapacity || _crew != tank.ActiveCrew ||
                                _disabled != tank.DisabledModules || _active != tank.alive ||
                                _onFire != tank.OnFire || _frontEngine != tank.FrontEngine;
            if (!vehicleChanged && !stateChanged && !tank.OnFire) return;

            ReadVehicle(tank);
            _lastStatusTurretRel = turretRel;

            if (vehicleChanged && _xray != null)
            {
                Rect body = StatusBodyRect;
                Rect diagram = Rect.MinMaxRect(body.xMin + 1f, body.yMin + 2f,
                    body.xMax - StatusCrewColumnWidth - 2f, body.yMax - 1f);
                _xray.RenderStatus(tank.hullStack != null ? tank.hullStack.sourceSheet : null,
                    tank.turretStack != null ? tank.turretStack.sourceSheet : null, tank.meta, turretRel, diagram);
            }
            SetVerticesDirty();
        }

        private void ReadVehicle(TankEntity tank)
        {
            _vehicleId = tank.tankType;
            _meta = tank.meta;
            _disabled = tank.DisabledModules;
            _crew = tank.ActiveCrew;
            _crewCapacity = tank.CrewCapacity;
            _active = tank.alive;
            _onFire = tank.OnFire;
            _frontEngine = tank.FrontEngine;
            _wheeled = tank.IsWheeled;
            _wheelAxles = tank.WheelAxleCount;
            _unmannedTurret = tank.UnmannedTurret;
            _turretless = tank.Turretless;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (_crewCapacity == 0 || _xray == null || _meta == null) return;

            Rect rect = rectTransform.rect;
            float ox = rect.xMin;
            float oy = rect.yMin;
            float sx = rect.width / Mathf.Max(1, _xray.Width);
            float sy = rect.height / Mathf.Max(1, _xray.Height);
            float hullMidZ = _meta.hullBaseHeight * 0.55f;
            float turretZ = _meta.hullBaseHeight + Mathf.Max(1.5f, _meta.turretSlices * 0.35f);

            Vector2 Proj(Vector2 local, float z)
            {
                Vector2 px = _xray.ProjectLocalToPixel(local, z);
                return new Vector2(ox + px.x * sx, oy + px.y * sy);
            }

            float uiScale = _mode == Mode.Status ? 0.66f : (_mode == Mode.Hangar ? 1.02f : 0.80f);

            // Compute Ammo Cookoff expanding wave radius in local hull coordinates (0 -> 2.5 engulfs entire vehicle!)
            float cookoffAge = (_mode == Mode.Replay && _loss == TankLoss.Ammo && _cookoffStartTime >= 0f)
                ? (_elapsed - (_cookoffStartTime + 0.34f))
                : -1f;
            float cookoffLocalR = cookoffAge >= 0f
                ? Mathf.Lerp(0.05f, 2.5f, Mathf.Clamp01(cookoffAge / 0.52f))
                : 0f;
            Vector2 ammoLocal = TankDamageLayout.Ammo(_unmannedTurret, _frontEngine);

            bool Engulfed(Vector2 localPt) => cookoffLocalR > 0f && (localPt - ammoLocal).magnitude <= cookoffLocalR;

            // 1. Running Gear (Wheels for 8x8/4x4, Tracks for tracked vehicles)
            DrawRunningGear(mesh, Proj, TankModule.LeftRunningGear, -0.84f, uiScale, Engulfed(new Vector2(-0.84f, 0f)));
            DrawRunningGear(mesh, Proj, TankModule.RightRunningGear, 0.84f, uiScale, Engulfed(new Vector2(0.84f, 0f)));

            // 2. Internal Modules (War Thunder gray -> yellow/orange -> red -> blackened!)
            Vector2 engLocal = TankDamageLayout.Engine(_frontEngine);
            Vector2 trvLocal = TankDamageLayout.Traverse(_turretless);
            Vector2 wpnLocal = TankDamageLayout.Weapon(_turretless);

            System.Span<ModuleMarker> markers = stackalloc ModuleMarker[]
            {
                new(TankModule.Engine, engLocal, Proj(engLocal, hullMidZ), uiScale),
                new(TankModule.Traverse, trvLocal, Proj(trvLocal, _meta.hullBaseHeight * 0.85f), uiScale),
                new(TankModule.Weapon, wpnLocal, Proj(wpnLocal, _turretless ? hullMidZ + 1.5f : turretZ), uiScale),
                new(TankModule.Ammo, ammoLocal, Proj(ammoLocal, _unmannedTurret ? hullMidZ + 1f : hullMidZ), uiScale)
            };
            Rect statusBody = _mode == Mode.Status ? StatusBodyRect : default;
            if (_mode == Mode.Status)
                SpaceStatusMarkers(markers, oy + statusBody.yMin * sy + 1f, oy + statusBody.yMax * sy);
            foreach (var marker in markers)
                DrawModule(mesh, marker, uiScale, Engulfed(marker.LocalPosition));

            // 3. Crew Seats
            if (_mode == Mode.Status)
            {
                Rect body = statusBody;
                float dividerX = ox + (body.xMax - StatusCrewColumnWidth) * sx;
                float dividerY = oy + (body.yMin + 0.5f) * sy;
                Line(mesh, new Vector2(ox + body.xMin * sx, dividerY), new Vector2(ox + body.xMax * sx, dividerY), sy, Grid);
                Line(mesh, new Vector2(dividerX, dividerY), new Vector2(dividerX, oy + body.yMax * sy), sx, Grid);
                float colX = dividerX + StatusCrewColumnWidth * 0.5f * sx;
                float centerY = oy + body.center.y * sy;
                for (int i = 0; i < _crewCapacity; i++)
                {
                    Vector2 pos = new Vector2(colX, centerY + ((_crewCapacity - 1) * 0.5f - i) * 9.5f * sy);
                    DrawCrew(mesh, pos, CrewSeats[i], 0.76f, false);
                }
            }
            else
            {
                for (int i = 0; i < _crewCapacity; i++)
                {
                    Vector2 seatLocal = TankDamageLayout.CrewSeat(i, _unmannedTurret, _frontEngine);
                    float seatZ = (_unmannedTurret || i == 0) ? hullMidZ + 0.5f : turretZ - 0.5f;
                    DrawCrew(mesh, Proj(seatLocal, seatZ), CrewSeats[i], _mode == Mode.Hangar ? 0.94f : 0.76f, Engulfed(seatLocal));
                }
            }

            // 4. Engine Fire Flames (live animated flames in both Status & Replay!)
            bool showFire = _onFire || (_mode == Mode.Replay && _igniteStartTime >= 0f && _elapsed >= _igniteStartTime + 0.36f);
            if (showFire)
                DrawEngineFire(mesh, Proj(engLocal, hullMidZ + 1.8f), uiScale);

            // 5. War Thunder Ballistic Replay: Shell + 7-Ray Spall Cone + Ricochet + Full-Vehicle Ammo Cookoff!
            if (_mode == Mode.Replay)
                DrawReplayBallistics(mesh, Proj, hullMidZ + 1.2f, ammoLocal, ox, oy, rect.width, rect.height);
            else if (_mode == Mode.Hangar)
            {
                DrawHangarShot(mesh, Proj, rect);
                DrawHangarImpact(mesh, Proj);
            }
        }

        private void DrawRunningGear(VertexHelper mesh, System.Func<Vector2, float, Vector2> proj, TankModule module, float sideX, float scale, bool engulfed)
        {
            Color tint = ModuleColor(module, engulfed);
            if (_wheeled && _wheelAxles > 0)
            {
                int n = _wheelAxles;
                for (int i = 0; i < n; i++)
                {
                    float fy = n == 2 ? (i == 0 ? -0.58f : 0.58f) : Mathf.Lerp(-0.68f, 0.68f, i / (float)(n - 1));
                    Vector2 wp = proj(new Vector2(sideX, fy), 1.5f);
                    float r = 2.3f * scale;
                    Box(mesh, wp - new Vector2(r, r), r * 2f, r * 2f, Dark);
                    Box(mesh, wp - new Vector2(r - 0.7f, r - 0.7f), (r - 0.7f) * 2f, (r - 0.7f) * 2f, tint);
                }
            }
            else
            {
                Vector2 p0 = proj(new Vector2(sideX, -0.78f), 1.5f);
                Vector2 p1 = proj(new Vector2(sideX, 0.78f), 1.5f);
                Line(mesh, p0, p1, 2.8f * scale, Dark);
                Line(mesh, p0, p1, 1.4f * scale, tint);
            }
        }

        private bool TryGetLatestModuleHitAge(TankModule module, out float age)
        {
            age = -1f;
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                if ((_shots[i].Hit.StruckModules & module) != 0)
                {
                    age = _elapsed - _shots[i].StartTime;
                    return true;
                }
            }
            return false;
        }

        private bool TryGetLatestCrewHitAge(TankCrew crew, out float age)
        {
            age = -1f;
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                if ((_shots[i].Hit.KnockedOut & crew) != 0)
                {
                    age = _elapsed - _shots[i].StartTime;
                    return true;
                }
            }
            return false;
        }

        private Color ModuleColor(TankModule module, bool engulfed)
        {
            if (engulfed)
            {
                float cAge = _cookoffStartTime >= 0f ? (_elapsed - _cookoffStartTime) : _elapsed;
                return cAge < 0.65f ? WtDamagedOrange : WtBlackened;
            }

            if (_mode == Mode.Replay && TryGetLatestModuleHitAge(module, out float hitAge))
            {
                if (hitAge < 0.30f) return WtIntactModule;
                if (hitAge < 0.39f) return Flash;
                if (hitAge < 0.50f) return WtDamagedYellow;
                if (hitAge < 0.62f) return WtDamagedOrange;
                return WtDestroyedRed;
            }
            if ((_disabled & module) != 0) return _mode == Mode.Replay ? WtPreviousDamage : WtDestroyedRed;
            return _active || _mode != Mode.Status ? WtIntactModule : WtInactive;
        }

        private static Vector2 ModuleHalfSize(TankModule module) => module switch
        {
            TankModule.Weapon => new Vector2(4.4f, 1.9f),
            TankModule.Traverse => new Vector2(4f, 4f),
            TankModule.Ammo => new Vector2(3.8f, 3.8f),
            _ => new Vector2(4.6f, 2.9f)
        };

        private static void SpaceStatusMarkers(System.Span<ModuleMarker> markers, float bottom, float top)
        {
            const float gap = 1f;
            for (int i = 1; i < markers.Length; i++)
            {
                for (int j = i; j > 0 && markers[j].Position.y < markers[j - 1].Position.y; j--)
                    (markers[j - 1], markers[j]) = (markers[j], markers[j - 1]);
            }

            // Keep each marker over its part horizontally, and give neighboring markers room on the vertical axis.
            for (int i = 0; i < markers.Length; i++)
            {
                ref ModuleMarker marker = ref markers[i];
                float y = Mathf.Max(marker.Position.y, bottom + marker.HalfSize.y);
                foreach (ModuleMarker other in markers[..i])
                    if (SharesColumn(marker, other))
                        y = Mathf.Max(y, other.Position.y + other.HalfSize.y + marker.HalfSize.y + gap);
                marker.Position.y = y;
            }

            for (int i = markers.Length - 1; i >= 0; i--)
            {
                ref ModuleMarker marker = ref markers[i];
                float y = Mathf.Min(marker.Position.y, top - marker.HalfSize.y);
                foreach (ModuleMarker other in markers[(i + 1)..])
                    if (SharesColumn(marker, other))
                        y = Mathf.Min(y, other.Position.y - other.HalfSize.y - marker.HalfSize.y - gap);
                marker.Position.y = y;
            }

            static bool SharesColumn(ModuleMarker marker, ModuleMarker other) =>
                Mathf.Abs(marker.Position.x - other.Position.x) < marker.HalfSize.x + other.HalfSize.x + gap;
        }

        private void DrawModule(VertexHelper mesh, ModuleMarker marker, float s, bool engulfed)
        {
            Vector2 pos = marker.Position;
            TankModule module = marker.Module;
            Color ink = ModuleColor(module, engulfed);
            Color border = engulfed ? WtDestroyedRed : Dark;
            if (module == TankModule.Traverse)
                Diamond(mesh, pos, marker.HalfSize.x, border);
            else
                Box(mesh, pos - marker.HalfSize, marker.HalfSize.x * 2f, marker.HalfSize.y * 2f, border);

            if (module == TankModule.Weapon)
            {
                Box(mesh, pos + new Vector2(-3.4f, -0.9f) * s, 6.8f * s, 1.8f * s, ink);
                Box(mesh, pos + new Vector2(1.0f, -1.9f) * s, 1.8f * s, 3.8f * s, ink);
            }
            else if (module == TankModule.Traverse)
            {
                Diamond(mesh, pos, 3.0f * s, ink);
                Diamond(mesh, pos, 1.1f * s, Dark);
            }
            else if (module == TankModule.Ammo)
            {
                for (int i = -2; i <= 2; i += 2)
                    Box(mesh, pos + new Vector2(i - 0.6f, -2.4f) * s, 1.2f * s, 4.8f * s, ink);
            }
            else // Engine
            {
                Box(mesh, pos - new Vector2(3.6f, 1.9f) * s, 7.2f * s, 3.8f * s, ink);
                for (int i = -2; i <= 2; i += 2)
                    Box(mesh, pos + new Vector2(i - 0.4f, -1.1f) * s, 0.8f * s, 2.2f * s, Dark);
            }
        }

        private void DrawCrew(VertexHelper mesh, Vector2 pos, TankCrew crew, float s, bool engulfed)
        {
            float crewAge = 0f;
            bool newLoss = _mode == Mode.Replay && TryGetLatestCrewHitAge(crew, out crewAge);
            bool disabled = (_crew & crew) == 0 || engulfed;
            Color tint;
            if (engulfed)
            {
                float cAge = _cookoffStartTime >= 0f ? (_elapsed - _cookoffStartTime) : _elapsed;
                tint = cAge < 0.62f ? WtDamagedOrange : WtBlackened;
            }
            else if (newLoss)
                tint = crewAge < 0.32f ? WtIntactCrew : (crewAge < 0.44f ? Flash : (crewAge < 0.56f ? WtDamagedOrange : WtDestroyedRed));
            else if (disabled)
                tint = _mode == Mode.Replay ? WtPreviousDamage : WtDestroyedRed;
            else
                tint = (_active || _mode != Mode.Status) ? WtIntactCrew : WtInactive;

            Box(mesh, pos + new Vector2(-3.2f, -4.2f) * s, 6.4f * s, 8.4f * s, engulfed ? WtDestroyedRed : Dark);
            Box(mesh, pos + new Vector2(-1.0f, 1.7f) * s, 2.0f * s, 2.0f * s, tint);
            Box(mesh, pos + new Vector2(-2.5f, -0.9f) * s, 5.0f * s, 2.1f * s, tint);
            Box(mesh, pos + new Vector2(-1.7f, -3.5f) * s, 1.3f * s, 2.9f * s, tint);
            Box(mesh, pos + new Vector2(0.4f, -3.5f) * s, 1.3f * s, 2.9f * s, tint);
            if (disabled && (!newLoss || crewAge >= 0.50f))
                Line(mesh, pos + new Vector2(-2.6f, -3.2f) * s, pos + new Vector2(2.6f, 3.2f) * s, 1.1f, Dark);
        }

        private void DrawEngineFire(VertexHelper mesh, Vector2 pos, float s)
        {
            float t = Time.unscaledTime * 11f;
            for (int i = -1; i <= 1; i++)
            {
                float flick = 0.7f + 0.35f * Mathf.Sin(t + i * 2.1f);
                Vector2 basePt = pos + new Vector2(i * 3.0f * s, 0f);
                float h = (6.5f - Mathf.Abs(i) * 1.4f) * s * flick;
                Diamond(mesh, basePt + Vector2.up * (h * 0.45f), 2.8f * s * flick, CookoffMid);
                Diamond(mesh, basePt + Vector2.up * (h * 0.30f), 1.4f * s * flick, CookoffInner);
            }
        }

        private static Color FadeAlpha(Color c, float factor)
            => new Color(c.r, c.g, c.b, Mathf.Clamp01(c.a * factor));

        private static void Box(VertexHelper mesh, Vector2 p, float width, float height, Color ink)
            => Quad(mesh, p, p + new Vector2(0f, height), p + new Vector2(width, height), p + new Vector2(width, 0f), ink);

        private static void Diamond(VertexHelper mesh, Vector2 p, float radius, Color ink)
            => Quad(mesh, p + Vector2.left * radius, p + Vector2.up * radius, p + Vector2.right * radius, p + Vector2.down * radius, ink);

        private static void Octagon(VertexHelper mesh, Vector2 center, float radius, Color ink)
        {
            if (radius < 0.5f) return;
            Color32 col = ink;
            int centerIdx = mesh.currentVertCount;
            mesh.AddVert(center, col, Vector2.zero);
            const int segs = 8;
            for (int i = 0; i < segs; i++)
            {
                float a = (i / (float)segs) * Mathf.PI * 2f;
                Vector2 pt = center + new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius * 0.84f);
                mesh.AddVert(pt, col, Vector2.zero);
            }
            for (int i = 0; i < segs; i++)
            {
                int next = (i + 1) % segs;
                mesh.AddTriangle(centerIdx, centerIdx + 1 + i, centerIdx + 1 + next);
            }
        }

        private static void Line(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color ink)
        {
            Vector2 dir = b - a;
            if (dir.sqrMagnitude < 0.001f) return;
            Vector2 side = new Vector2(-dir.y, dir.x).normalized * (width * 0.5f);
            Quad(mesh, a - side, a + side, b + side, b - side, ink);
        }

        private static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 ink)
        {
            int i = mesh.currentVertCount;
            mesh.AddVert(a, ink, Vector2.zero);
            mesh.AddVert(b, ink, Vector2.zero);
            mesh.AddVert(c, ink, Vector2.zero);
            mesh.AddVert(d, ink, Vector2.zero);
            mesh.AddTriangle(i, i + 1, i + 2);
            mesh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
