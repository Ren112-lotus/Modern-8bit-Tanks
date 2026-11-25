using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TankCutawayGraphic : MaskableGraphic
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

        private static readonly Color Tracer = new Color32(255, 214, 108, 255);
        private static readonly Color TracerDim = new Color32(168, 114, 54, 215);
        private static readonly Color SpallBright = new Color32(255, 174, 68, 240);
        private static readonly Color SpallHot = new Color32(255, 108, 42, 220);
        private static readonly Color RicochetCol = new Color32(142, 236, 255, 255);
        private static readonly Color Flash = new Color32(255, 250, 224, 255);

        private static readonly Color CookoffSmoke = new Color32(178, 44, 24, 185);
        private static readonly Color CookoffMid = new Color32(255, 106, 32, 220);
        private static readonly Color CookoffInner = new Color32(255, 196, 64, 242);
        private static readonly Color CookoffCore = new Color32(255, 248, 210, 255);

        private static readonly TankCrew[] CrewSeats = { TankCrew.Driver, TankCrew.Gunner, TankCrew.Commander, TankCrew.Loader };
        private const float StatusCrewColumnWidth = 12f;

        private Mode _mode = Mode.Status;
        private TankXRayRenderer _xray;
        private RawImage _bgImage;
        private RectMask2D _viewportMask;
        [SerializeField] private UnityEngine.UI.Text hangarAttackerLabel;
        [SerializeField] private UnityEngine.UI.Text hangarAmmoLabel;
        [SerializeField] private UnityEngine.UI.Text hangarArmorResultLabel;
        private Rect _hangarViewport;
        private (float X, TankHit? Hit, TankShotTrace Trace)? _hangarShot;

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

        // Live Replay state (tracks real enemy movement & supports concurrent multi-shot autocannon bursts!)
        private struct ReplayShot
        {
            public TankShotTrace Trace;
            public TankHit Hit;
            public float StartTime;
        }

        private TankEntity _replayTarget;
        private float _lastReplayHullDeg = -999f;
        private float _lastReplayTurretDeg = -999f;
        private float _lastCookoffLift = -1f;
        private readonly List<ReplayShot> _shots = new(24);
        private TankLoss _loss;
        private float _cookoffStartTime = -1f;
        private float _igniteStartTime = -1f;
        private float _elapsed;

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

        public void StartReplay(TankEntity tank, Vector3[] unusedOutline, TankShotTrace trace, TankHit hit, TankLoss? confirmedLoss = null)
        {
            if (tank == null || tank.meta == null) return;
            _mode = Mode.Replay;
            _replayTarget = tank;
            ReadVehicle(tank);
            _shots.Clear();
            _shots.Add(new ReplayShot { Trace = trace, Hit = hit, StartTime = 0f });
            _loss = confirmedLoss ?? tank.Loss;
            _cookoffStartTime = _loss == TankLoss.Ammo ? 0f : -1f;
            _igniteStartTime = hit.Ignited ? 0f : -1f;
            _elapsed = 0f;
            _lastReplayHullDeg = -999f;
            _lastReplayTurretDeg = -999f;
            _lastCookoffLift = -1f;

            RenderReplayHologram(force: true);
            SetVerticesDirty();
        }

        /// <summary>
        /// Appends a live follow-up shot (e.g. rapid-fire Autocannon burst!) onto the ongoing X-Ray replay
        /// without resetting earlier shots, so EVERY single autocannon round displays cleanly!
        /// </summary>
        public void AddReplayShot(TankShotTrace trace, TankHit hit, TankLoss currentLoss)
        {
            if (_mode != Mode.Replay) return;

            // Prune already-finished ballistic streaks if the burst is long
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                if (_elapsed - _shots[i].StartTime > 1.60f && _shots.Count > 12)
                    _shots.RemoveAt(i);
            }
            if (_shots.Count >= 24)
                _shots.RemoveAt(0);

            _shots.Add(new ReplayShot { Trace = trace, Hit = hit, StartTime = _elapsed });
            if (hit.Ignited && _igniteStartTime < 0f) _igniteStartTime = _elapsed;
            if (currentLoss != TankLoss.None) _loss = currentLoss;
            if (_loss == TankLoss.Ammo && _cookoffStartTime < 0f) _cookoffStartTime = _elapsed;
            SetVerticesDirty();
        }

        public void SetReplayTime(float elapsed)
        {
            if (_mode != Mode.Replay) return;
            bool timeChanged = !Mathf.Approximately(_elapsed, elapsed);
            _elapsed = elapsed;

            // Live-track real enemy movement, hull turning, turret rotation, fire, and cookoff turret lift!
            if (_replayTarget != null && _replayTarget.meta != null)
            {
                ReadVehicle(_replayTarget);
                if (_replayTarget.Loss != TankLoss.None) _loss = _replayTarget.Loss;
                RenderReplayHologram(force: false);
            }

            if (timeChanged || _onFire || _loss == TankLoss.Ammo)
                SetVerticesDirty();
        }

        private void RenderReplayHologram(bool force)
        {
            if (_xray == null || _replayTarget == null || _replayTarget.meta == null) return;

            // Use the enemy tank's ACTUAL live hull & turret angles so its motion mirrors the battlefield 1:1!
            float liveHullDeg = _replayTarget.hullAngleDeg;
            float liveTurretDeg = _replayTarget.turretAngleDeg;

            float cookoffAge = (_loss == TankLoss.Ammo && _cookoffStartTime >= 0f)
                ? (_elapsed - (_cookoffStartTime + 0.34f))
                : -1f;
            float cookoffT = cookoffAge > 0f ? Mathf.Clamp01(cookoffAge / 0.85f) : 0f;
            float turretLift = cookoffT > 0f ? Mathf.Pow(cookoffT, 0.72f) * 11.5f : 0f;
            float heat = cookoffT > 0f ? Mathf.Clamp01(Mathf.Sin(cookoffT * Mathf.PI * 0.88f) * 0.92f) : 0f;
            float cookoffSpin = cookoffT > 0f ? cookoffT * 72f : 0f;

            bool angleMoved = Mathf.Abs(Mathf.DeltaAngle(_lastReplayHullDeg, liveHullDeg)) > 1.2f ||
                              Mathf.Abs(Mathf.DeltaAngle(_lastReplayTurretDeg, liveTurretDeg + cookoffSpin)) > 1.2f ||
                              Mathf.Abs(turretLift - _lastCookoffLift) > 0.25f;

            if (!force && !angleMoved) return;

            _lastReplayHullDeg = liveHullDeg;
            _lastReplayTurretDeg = liveTurretDeg + cookoffSpin;
            _lastCookoffLift = turretLift;

            float span = Mathf.Max(_replayTarget.meta.hullLength * 1.18f, _replayTarget.meta.turretLength * 0.80f);
            float fitScale = Mathf.Clamp((_xray.Width * 0.60f) / Mathf.Max(28f, span), 0.90f, 1.32f);
            Vector2 c = new Vector2(_xray.Width * 0.50f, _xray.Height * 0.38f);

            _xray.Render(
                _replayTarget.hullStack != null ? _replayTarget.hullStack.sourceSheet : null,
                _replayTarget.turretStack != null ? _replayTarget.turretStack.sourceSheet : null,
                _replayTarget.meta,
                hullAngleDeg: liveHullDeg,
                turretAngleDeg: liveTurretDeg + cookoffSpin,
                squash: 0.75f,
                spacingPx: 0.92f,
                scale: fitScale,
                centerOverride: c,
                drawGrid: false,
                transparentBg: true,
                turretExtraLiftSlices: turretLift,
                cookoffHeat: heat
            );
        }

        internal void ShowHangarInspect(TankEntity tank, Camera worldCamera, Camera hudCamera)
        {
            if (tank.meta == null || _xray == null) return;
            _mode = Mode.Hangar;
            _replayTarget = null;
            ReadVehicle(tank);
            _active = true;
            _onFire = false;
            _disabled = TankModule.None;
            _crew = TankCrew.Driver | TankCrew.Gunner | TankCrew.Commander | (tank.CrewCapacity == 4 ? TankCrew.Loader : TankCrew.None);

            // The world and HUD render to separate textures but share the same viewport.
            Rect backdrop = _bgImage.GetPixelAdjustedRect();
            RectTransform backdropTransform = _bgImage.rectTransform;
            Vector2 viewportMin = hudCamera.WorldToViewportPoint(backdropTransform.TransformPoint(backdrop.min));
            Vector2 viewportMax = hudCamera.WorldToViewportPoint(backdropTransform.TransformPoint(backdrop.max));
            Vector2 viewportSize = viewportMax - viewportMin;
            _hangarViewport = new Rect(viewportMin, viewportSize);
            var textureScale = new Vector2(_xray.Width / viewportSize.x, _xray.Height / viewportSize.y);

            Vector2 Project(Vector3 world) =>
                Vector2.Scale((Vector2)worldCamera.WorldToViewportPoint(world) - viewportMin, textureScale);

            Vector3 hullOrigin = tank.hullStack.transform.position;
            Vector2 hullCenter = Project(hullOrigin);
            Vector2 turretCenter = Project(tank.turretStack.transform.position);
            float pixelsPerTexel = Project(hullOrigin + Vector3.right / tank.hullStack.pixelsPerUnit).x - hullCenter.x;
            _xray.RenderHangar(tank, hullCenter, turretCenter, pixelsPerTexel);
            SetVerticesDirty();
        }

        internal void SetArmorPreviewLoadout(string attacker, string ammo)
        {
            hangarAttackerLabel.text = attacker;
            hangarAmmoLabel.text = ammo;
        }

        internal void SetArmorPreviewResult(string text, ArmorHit? outcome)
        {
            hangarArmorResultLabel.text = text;
            hangarArmorResultLabel.color = ArmorPreviewColor(outcome);
        }

        internal bool PreviewHangarShot(TankEntity tank, Camera worldCamera, Vector2? aim,
            ProjectileKind kind, AmmoType ammo, float penetration, out TankHit? hit)
        {
            _hangarShot = null;
            hit = null;
            if (!aim.HasValue || !_hangarViewport.Contains(aim.Value)) return false;

            float x = aim.Value.x;
            float aimX = (x - _hangarViewport.xMin) / _hangarViewport.width;
            Vector2 from = worldCamera.ViewportToWorldPoint(new Vector3(x, _hangarViewport.yMin, 0f));
            Vector2 to = worldCamera.ViewportToWorldPoint(new Vector3(x, _hangarViewport.yMax, 0f));
            TankShotTrace trace = default;
            if (tank.TryGetArmorContact(from, to, tank.hullStack.squash, out ArmorContact contact))
                hit = tank.PreviewArmorHit(contact, kind, penetration, ammo, out trace);
            _hangarShot = (aimX, hit, trace);
            return true;
        }

        private static Color ArmorPreviewColor(ArmorHit? hit) => hit switch
        {
            ArmorHit.Penetrated => new Color32(157, 226, 138, 255),
            ArmorHit.Blocked => new Color32(255, 141, 125, 255),
            ArmorHit.Ricochet => RicochetCol,
            _ => WtIntactModule
        };

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
            else if (_mode == Mode.Hangar && _hangarShot.HasValue)
                DrawHangarShot(mesh, Proj, rect);
        }

        private void DrawHangarShot(VertexHelper mesh, System.Func<Vector2, float, Vector2> project, Rect rect)
        {
            var (x, hit, trace) = _hangarShot.Value;
            Vector2 bottomLeft = project(new Vector2(-1f, -1f), 0f);
            Vector2 bottomRight = project(new Vector2(1f, -1f), 0f);
            Vector2 topLeft = project(new Vector2(-1f, 1f), 0f);
            Vector2 topRight = project(new Vector2(1f, 1f), 0f);
            Color outline = FadeAlpha(RicochetCol, 0.42f);
            Line(mesh, bottomLeft, bottomRight, 1f, outline);
            Line(mesh, bottomRight, topRight, 1f, outline);
            Line(mesh, topRight, topLeft, 1f, outline);
            Line(mesh, topLeft, bottomLeft, 1f, outline);

            var origin = new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, x), rect.yMin + 3f);
            Color tint = ArmorPreviewColor(hit?.Armor);
            if (!hit.HasValue)
            {
                Line(mesh, origin, new Vector2(origin.x, rect.yMax - 3f), 1f, FadeAlpha(tint, 0.45f));
                return;
            }

            Vector2 impact = project(trace.Entry, 0f);
            Line(mesh, origin, impact, 1f, tint);
            Line(mesh, impact + new Vector2(-3f, -5f), impact, 1f, tint);
            Line(mesh, impact + new Vector2(3f, -5f), impact, 1f, tint);
            if (hit.Value.Armor == ArmorHit.Penetrated)
            {
                Vector2 delta = trace.End - trace.Entry;
                float reach = Mathf.Clamp01(Mathf.Min(ExitFraction(trace.Entry.x, delta.x), ExitFraction(trace.Entry.y, delta.y)));
                Line(mesh, impact, project(trace.Entry + delta * reach, 0f), 2f, tint);
            }
            Diamond(mesh, impact, 2.5f, tint);
        }

        private static float ExitFraction(float start, float delta) =>
            Mathf.Abs(delta) < 0.0001f ? 1f : ((delta > 0f ? 1f : -1f) - start) / delta;

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

        private void DrawReplayBallistics(VertexHelper mesh, System.Func<Vector2, float, Vector2> proj, float z,
                                          Vector2 ammoLocal, float ox, float oy, float rw, float rh)
        {
            for (int sIdx = 0; sIdx < _shots.Count; sIdx++)
            {
                ReplayShot shot = _shots[sIdx];
                float t = _elapsed - shot.StartTime;
                if (t < 0f || t > 1.50f) continue;

                TankShotTrace tr = shot.Trace;
                TankHit h = shot.Hit;
                Vector2 start = proj(tr.Entry - tr.Approach * 0.95f, z + 1.4f);
                Vector2 hit = proj(tr.Entry, z);
                Vector2 end = proj(tr.End, z - 0.5f);

                // 1. Incoming shell streak: head flies in (0..0.22s), tail chases into impact (0.14..0.44s) and vanishes!
                float inHead = Mathf.Clamp01(t / 0.22f);
                float inTail = Mathf.Clamp01((t - 0.14f) / 0.30f);
                if (inTail < 0.99f && inHead > inTail + 0.01f)
                {
                    float inAlpha = 1f - inTail * 0.65f;
                    Vector2 pTail = Vector2.Lerp(start, hit, inTail);
                    Vector2 pHead = Vector2.Lerp(start, hit, inHead);
                    Line(mesh, pTail, pHead, 1.45f, FadeAlpha(TracerDim, inAlpha));
                    if (inHead < 0.99f)
                        Diamond(mesh, pHead, 2.0f, Flash);
                }

                if (t < 0.21f) continue;

                // 2. Armor impact flash ring (0.21..0.58s) — pops and fades cleanly!
                float burstT = Mathf.Clamp01((t - 0.21f) / 0.37f);
                if (burstT < 0.99f)
                {
                    float burstFade = 1f - burstT;
                    float ring = Mathf.Lerp(5.4f, 1.6f, burstT);
                    Color burstCol = h.Armor == ArmorHit.Ricochet ? RicochetCol : Tracer;
                    Diamond(mesh, hit, ring, FadeAlpha(burstCol, burstFade));
                    Diamond(mesh, hit, 1.7f * burstFade, FadeAlpha(Flash, burstFade));
                }

                // 3. Post-impact rays: expand crisply, linger clearly so spall paths are readable,
                //    then tail sweeps smoothly forward into the tip and disappears (~1.35s)!
                if (h.Armor == ArmorHit.Penetrated)
                {
                    bool isChem = AmmoInfo.IsChemical(h.Ammo);
                    bool isAphe = h.Ammo == AmmoType.APHE;
                    bool isHe = AmmoInfo.IsExplosiveSplash(h.Ammo);

                    float outHead = Mathf.Clamp01((t - 0.22f) / 0.20f);
                    float outTailRaw = Mathf.Clamp01((t - 0.58f) / 0.70f);
                    float outTail = outTailRaw * outTailRaw;
                    if (outTail < 0.99f && outHead > outTail + 0.01f)
                    {
                        float penFade = Mathf.Clamp01((1f - outTailRaw) * 1.45f);
                        Vector2 p0 = Vector2.Lerp(hit, end, outTail);
                        Vector2 p1 = Vector2.Lerp(hit, end, outHead);
                        Color jetCol = isChem ? RicochetCol : (isHe ? CookoffMid : Tracer);
                        Line(mesh, p0, p1, Mathf.Lerp(isChem ? 2.2f : 1.9f, 0.95f, outTail), FadeAlpha(jetCol, penFade));
                        Diamond(mesh, p1, Mathf.Lerp(2.1f, 0.85f, outTail), FadeAlpha(Flash, penFade));
                    }

                    // APHE: 360-degree internal War Thunder explosive shrapnel burst inside the fighting compartment!
                    if (isAphe && t >= 0.34f)
                    {
                        float bHead = Mathf.Clamp01((t - 0.34f) / 0.20f);
                        float bTailRaw = Mathf.Clamp01((t - 0.68f) / 0.68f);
                        float bTail = bTailRaw * bTailRaw;
                        if (bTail < 0.99f && bHead > bTail + 0.01f)
                        {
                            float bf = Mathf.Clamp01((1f - bTailRaw) * 1.4f);
                            Octagon(mesh, end, Mathf.Lerp(2f, 8.5f, bHead) * (1f - bTailRaw * 0.55f), FadeAlpha(CookoffMid, 0.68f * bf));
                            Octagon(mesh, end, Mathf.Lerp(1.2f, 4.2f, bHead) * (1f - bTailRaw * 0.65f), FadeAlpha(CookoffCore, 0.85f * bf));
                            for (int k = 0; k < 10; k++)
                            {
                                float ang = (k / 10f) * Mathf.PI * 2f + 0.2f;
                                Vector2 rDir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (k % 2 == 0 ? 0.58f : 0.42f);
                                Vector2 rp0 = proj(tr.End + rDir * bTail, z - 0.4f);
                                Vector2 rp1 = proj(tr.End + rDir * bHead, z - 0.4f);
                                Line(mesh, rp0, rp1, Mathf.Lerp(1.2f, 0.55f, bTail), FadeAlpha(k % 2 == 0 ? SpallBright : SpallHot, bf));
                            }
                        }
                    }

                    // Directional Spall / Shaped-Charge Jet Cone
                    Vector2 dir = tr.End - tr.Entry;
                    if (dir.sqrMagnitude < 0.01f) dir = tr.Approach * 0.60f;
                    float[] fanAngles = isChem
                        ? new[] { -26f, -16f, -8f, 0f, 8f, 16f, 26f }
                        : new[] { -38f, -25f, -12f, 0f, 12f, 25f, 38f };
                    float[] fanLengths = isChem
                        ? new[] { 0.52f, 0.78f, 1.02f, 1.18f, 0.98f, 0.76f, 0.50f }
                        : new[] { 0.46f, 0.68f, 0.88f, 0.98f, 0.85f, 0.66f, 0.44f };
                    float[] fanStagger = { 0.045f, 0.022f, 0.008f, 0f, 0.012f, 0.028f, 0.050f };
                    for (int i = 0; i < fanAngles.Length; i++)
                    {
                        float st = fanStagger[i];
                        float spHead = Mathf.Clamp01((t - (0.23f + st)) / 0.20f);
                        float spTailRaw = Mathf.Clamp01((t - (0.62f + st)) / 0.74f);
                        float spTail = spTailRaw * spTailRaw;
                        if (spTail >= 0.99f || spHead <= spTail + 0.01f) continue;

                        float rad = fanAngles[i] * Mathf.Deg2Rad;
                        float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
                        Vector2 spDir = new Vector2(dir.x * c - dir.y * s, dir.x * s + dir.y * c) * fanLengths[i];
                        Vector2 p0 = proj(tr.Entry + spDir * spTail, z - 0.4f);
                        Vector2 p1 = proj(tr.Entry + spDir * spHead, z - 0.4f);

                        float spFade = Mathf.Clamp01((1f - spTailRaw) * 1.45f);
                        Color spCol = isChem ? (i == 3 ? Flash : SpallBright) : ((i % 2 == 0) ? SpallHot : SpallBright);
                        float w = (i == 3 ? 1.45f : 1.0f) * Mathf.Lerp(1f, 0.55f, spTail);
                        Line(mesh, p0, p1, w, FadeAlpha(spCol, spFade));
                        Diamond(mesh, p1, Mathf.Lerp(1.25f, 0.5f, spTail), FadeAlpha(Flash, spFade));
                    }
                }
                else if (h.Armor == ArmorHit.Ricochet)
                {
                    float bHead = Mathf.Clamp01((t - 0.22f) / 0.20f);
                    float bTailRaw = Mathf.Clamp01((t - 0.55f) / 0.67f);
                    float bTail = bTailRaw * bTailRaw;
                    if (bTail < 0.99f && bHead > bTail + 0.01f)
                    {
                        float bFade = Mathf.Clamp01((1f - bTailRaw) * 1.45f);
                        Vector2 p0 = Vector2.Lerp(hit, end, bTail);
                        Vector2 p1 = Vector2.Lerp(hit, end, bHead);
                        Line(mesh, p0, p1, Mathf.Lerp(1.7f, 0.9f, bTail), FadeAlpha(RicochetCol, bFade));
                        Diamond(mesh, p1, Mathf.Lerp(2.0f, 0.8f, bTail), FadeAlpha(Flash, bFade));
                    }

                    float skHead = Mathf.Clamp01((t - 0.22f) / 0.16f);
                    float skTail = Mathf.Clamp01((t - 0.42f) / 0.50f);
                    if (skTail < 0.99f && skHead > skTail + 0.01f)
                    {
                        float skFade = 1f - skTail;
                        Vector2 perp = (end - hit).sqrMagnitude > 0.1f
                            ? new Vector2(-(end - hit).y, (end - hit).x).normalized : Vector2.up;
                        Line(mesh, hit + perp * (5.5f * skTail), hit + perp * (5.5f * skHead), 1.0f, FadeAlpha(Flash, skFade));
                        Line(mesh, hit - perp * (4.5f * skTail), hit - perp * (4.5f * skHead), 0.9f, FadeAlpha(Tracer, skFade));
                    }
                }
                else
                {
                    bool isHeSplash = AmmoInfo.IsExplosiveSplash(h.Ammo);
                    if (isHeSplash)
                    {
                        float heT = Mathf.Clamp01((t - 0.22f) / 0.65f);
                        if (heT < 0.99f)
                        {
                            float heFade = 1f - heT;
                            Octagon(mesh, hit, Mathf.Lerp(3.5f, 13.5f, heT), FadeAlpha(CookoffMid, 0.62f * heFade));
                            Octagon(mesh, hit, Mathf.Lerp(2.0f, 7.5f, heT), FadeAlpha(CookoffInner, 0.80f * heFade));
                        }
                    }

                    float shHead = Mathf.Clamp01((t - 0.22f) / 0.16f);
                    float shTail = Mathf.Clamp01((t - 0.46f) / 0.52f);
                    if (shTail < 0.99f && shHead > shTail + 0.01f)
                    {
                        float shFade = 1f - shTail;
                        Vector2 backDir = (start - hit).normalized;
                        float len = isHeSplash ? 11.0f : 6.5f;
                        for (int i = -2; i <= 2; i++)
                        {
                            float rad = i * 26f * Mathf.Deg2Rad;
                            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
                            Vector2 sd = new Vector2(backDir.x * c - backDir.y * s, backDir.x * s + backDir.y * c);
                            Line(mesh, hit + sd * (len * shTail), hit + sd * (len * shHead), isHeSplash ? 1.3f : 1.0f, FadeAlpha(isHeSplash ? CookoffMid : Tracer, shFade));
                        }
                    }
                }
            }

            // =========================================================================
            // WAR THUNDER FULL-VEHICLE AMMO COOKOFF DETONATION (engulfs entire hull!)
            // =========================================================================
            if (_loss == TankLoss.Ammo && _cookoffStartTime >= 0f && _elapsed >= _cookoffStartTime + 0.34f)
            {
                float dtBoom = _elapsed - (_cookoffStartTime + 0.34f);
                float expand = Mathf.Clamp01(dtBoom / 0.58f);
                float easeOut = 1f - Mathf.Pow(1f - expand, 3f);
                float lingerFade = dtBoom > 0.95f ? Mathf.Clamp01(1f - (dtBoom - 0.95f) / 1.15f) : 1f;
                float flicker = 0.92f + 0.08f * Mathf.Sin(dtBoom * 32f);

                Vector2 ammoPos = proj(ammoLocal, z);
                Vector2 hullCenter = proj(Vector2.zero, z + 1f);
                Vector2 blastCenter = Vector2.Lerp(ammoPos, hullCenter, easeOut * 0.65f);

                // 1. Massive outer crimson-orange shockwave engulfing the ENTIRE hull & viewport!
                float outerR = Mathf.Lerp(5f, Mathf.Max(rw, rh) * 0.44f, easeOut) * flicker;
                Octagon(mesh, blastCenter, outerR, FadeAlpha(CookoffSmoke, 0.72f * lingerFade));

                // 2. Billowing secondary blast clouds spreading across front hull, engine bay, turret & flanks!
                Vector2[] spreadAnchors =
                {
                    new Vector2(0f, 0.55f),   // Front fighting compartment
                    new Vector2(0f, -0.58f),  // Rear engine bay
                    new Vector2(-0.52f, 0.10f),
                    new Vector2(0.52f, 0.10f),
                    new Vector2(-0.32f, -0.35f),
                    new Vector2(0.32f, 0.35f),
                };
                for (int i = 0; i < spreadAnchors.Length; i++)
                {
                    float delay = 0.06f + i * 0.045f;
                    float localT = Mathf.Clamp01((dtBoom - delay) / 0.45f);
                    if (localT <= 0.01f) continue;
                    float localEase = 1f - (1f - localT) * (1f - localT);
                    Vector2 puffPos = proj(spreadAnchors[i], z + (i % 2 == 0 ? 2.5f : 0.8f));
                    float pr = Mathf.Lerp(2.5f, 15.5f, localEase) * flicker;
                    Octagon(mesh, puffPos, pr, FadeAlpha(CookoffMid, 0.78f * lingerFade));
                    Octagon(mesh, puffPos, pr * 0.56f, FadeAlpha(CookoffInner, 0.88f * lingerFade));
                }

                // 3. Blazing central fireball + incandescent white-hot detonation core
                float midR = Mathf.Lerp(4f, 24f, easeOut) * flicker;
                Octagon(mesh, blastCenter, midR, FadeAlpha(CookoffMid, 0.88f * lingerFade));
                Octagon(mesh, blastCenter, midR * 0.62f, FadeAlpha(CookoffInner, 0.94f * lingerFade));

                float coreAlpha = Mathf.Clamp01(1f - dtBoom / 0.48f);
                if (coreAlpha > 0.02f)
                    Octagon(mesh, ammoPos, Mathf.Lerp(3f, 16f, Mathf.Clamp01(dtBoom / 0.24f)), FadeAlpha(CookoffCore, coreAlpha));

                // 4. 12 radial glowing ember/shrapnel streaks blasting outward through the armor!
                float sparkT = Mathf.Clamp01(dtBoom / 0.68f);
                float sparkAlpha = Mathf.Clamp01(1f - dtBoom / 0.88f);
                if (sparkAlpha > 0.03f)
                {
                    for (int i = 0; i < 12; i++)
                    {
                        float ang = (i * 30f + 11f) * Mathf.Deg2Rad;
                        Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.78f);
                        float dist0 = Mathf.Lerp(2f, 24f, sparkT);
                        float dist1 = dist0 + Mathf.Lerp(7f, 16f, 1f - sparkT);
                        Vector2 p0 = blastCenter + dir * dist0;
                        Vector2 p1 = blastCenter + dir * dist1;
                        Line(mesh, p0, p1, (i % 3 == 0) ? 1.4f : 1.0f, FadeAlpha((i % 2 == 0) ? CookoffCore : CookoffInner, sparkAlpha));
                    }
                }

                // 5. Brief full-frame warm flash at exact detonation frame (0.34s..0.46s)
                if (dtBoom < 0.14f)
                {
                    float flashA = (1f - dtBoom / 0.14f) * 0.34f;
                    Box(mesh, new Vector2(ox, oy), rw, rh, FadeAlpha(CookoffCore, flashA));
                }
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
