using UnityEngine;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    public sealed partial class TankCutawayGraphic
    {
        private const float HangarFlightDuration = 0.70f;
        private const float HangarImpactHoldDuration = 0.35f;

        private struct HangarFlight
        {
            public Vector2 Origin;
            public Vector2 Destination;
            public ArmorContact? Contact;
            public ProjectileKind Kind;
            public AmmoType Ammo;
            public float Penetration;
            public float Elapsed;
        }

        private Rect _hangarViewport;
        private Vector2 _hangarShotOrigin = new(0.5f, 0f);
        private (Vector2 End, TankHit? Hit, TankShotTrace Trace)? _hangarShot;
        private HangarFlight? _hangarFlight;
        private ReplayShot? _hangarImpact;
        private float _hangarResultUntil;
        internal bool HangarShotInFlight => _hangarFlight.HasValue;
        internal bool HangarShotBusy => HangarShotInFlight || Time.unscaledTime < _hangarResultUntil;

        internal void ShowHangarInspect(TankEntity tank, Camera worldCamera, Camera hudCamera)
        {
            if (tank.meta == null || _xray == null) return;
            _mode = Mode.Hangar;
            _replayTarget = null;
            ReadVehicle(tank);

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

        internal void SetArmorPreviewDamage(string status, string disabled, bool hasCondition, bool hasFailedModules)
        {
            hangarDamageStatusLabel.text = status;
            hangarDamageStatusLabel.color = hasCondition ? ArmorPreviewColor(ArmorHit.Blocked) : WtIntactCrew;
            hangarDisabledModulesLabel.text = disabled;
            hangarDisabledModulesLabel.color = hasFailedModules ? ArmorPreviewColor(ArmorHit.Blocked) : WtIntactModule;
        }

        internal bool ContainsHangarAim(Vector2 viewport) => _hangarViewport.Contains(viewport);

        internal bool TryGetHangarAimDirection(Vector2 viewport, out Vector2 direction)
        {
            direction = Vector2.zero;
            if (!ContainsHangarAim(viewport)) return false;
            direction = Rect.PointToNormalized(_hangarViewport, viewport) - _hangarShotOrigin;
            return direction.sqrMagnitude >= 0.000001f;
        }

        internal void ClearHangarShot()
        {
            _hangarShot = null;
            _hangarFlight = null;
            _hangarImpact = null;
            _hangarResultUntil = 0f;
        }

        internal void MoveHangarShotOrigin(Vector2 viewport)
        {
            if (!ContainsHangarAim(viewport)) return;
            var center = new Vector2(0.5f, 0.5f);
            Vector2 delta = Rect.PointToNormalized(_hangarViewport, viewport) - center;
            float extent = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
            if (extent > 0.0001f)
                _hangarShotOrigin = center + delta * (0.5f / extent);
        }

        internal bool PreviewHangarShot(TankEntity tank, Camera worldCamera, Vector2? aim,
            ProjectileKind kind, AmmoType ammo, float penetration, bool fire, out TankHit? hit)
        {
            _hangarShot = null;
            hit = null;
            if (!tank.alive || !aim.HasValue || !TryGetHangarAimDirection(aim.Value, out Vector2 direction))
                return false;
            float edge = Mathf.Min(
                ExitFraction(_hangarShotOrigin.x, direction.x, 0f, 1f),
                ExitFraction(_hangarShotOrigin.y, direction.y, 0f, 1f));
            Vector2 end = _hangarShotOrigin + direction * edge;
            Vector2 from = worldCamera.ViewportToWorldPoint(Rect.NormalizedToPoint(_hangarViewport, _hangarShotOrigin));
            Vector2 to = worldCamera.ViewportToWorldPoint(Rect.NormalizedToPoint(_hangarViewport, end));
            bool intersects = tank.TryGetArmorContact(from, to, tank.hullStack.squash, out ArmorContact contact);
            if (fire)
            {
                _hangarFlight = new HangarFlight
                {
                    Origin = _hangarShotOrigin,
                    Destination = intersects ? Vector2.Lerp(_hangarShotOrigin, end, contact.Fraction) : end,
                    Contact = intersects ? contact : null,
                    Kind = kind,
                    Ammo = ammo,
                    Penetration = penetration
                };
                return true;
            }

            TankShotTrace trace = default;
            if (intersects)
                hit = tank.PreviewArmorHit(contact, kind, penetration, ammo, out trace);
            _hangarShot = (end, hit, trace);
            return true;
        }

        internal bool AdvanceHangarShot(TankEntity tank, float unscaledDeltaTime, out TankHit? hit)
        {
            hit = null;
            if (!_hangarFlight.HasValue) return false;
            var flight = _hangarFlight.Value;
            flight.Elapsed += Mathf.Min(unscaledDeltaTime, 0.05f);
            if (flight.Elapsed < HangarFlightDuration)
            {
                _hangarFlight = flight;
                return false;
            }

            _hangarFlight = null;
            _hangarResultUntil = Time.unscaledTime + HangarImpactHoldDuration;
            TankShotTrace trace = default;
            if (tank.alive && flight.Contact is ArmorContact contact)
            {
                TankHit actual = tank.ApplyArmorHit(contact, flight.Kind, flight.Penetration, out trace, flight.Ammo);
                hit = actual;
                if (actual.Armor == ArmorHit.Penetrated)
                    trace = new TankShotTrace(trace.Entry, trace.Approach, FindHangarTraceEnd(trace));
                _hangarImpact = new ReplayShot { Trace = trace, Hit = actual, StartTime = Time.unscaledTime };
                ReadVehicle(tank);
            }
            _hangarShot = (flight.Destination, hit, trace);
            return true;
        }

        private static Color ArmorPreviewColor(ArmorHit? hit) => hit switch
        {
            ArmorHit.Penetrated => new Color32(157, 226, 138, 255),
            ArmorHit.Blocked => new Color32(255, 141, 125, 255),
            ArmorHit.Ricochet => RicochetCol,
            _ => WtIntactModule
        };

        private void DrawHangarImpact(VertexHelper mesh, System.Func<Vector2, float, Vector2> project)
        {
            if (!_hangarImpact.HasValue) return;
            ReplayShot impact = _hangarImpact.Value;
            float t = Time.unscaledTime - impact.StartTime + ReplayImpactTime;
            if (t > ReplayShotDuration) return;

            TankShotTrace trace = impact.Trace;
            Vector2 start = project(trace.Entry - trace.Approach * 0.95f, 0f);
            Vector2 hit = project(trace.Entry, 0f);
            DrawImpactBallistics(mesh, project, 0f, trace, impact.Hit, t, start, hit);
        }

        private void DrawHangarShot(VertexHelper mesh, System.Func<Vector2, float, Vector2> project, Rect rect)
        {
            Vector2 origin = Rect.NormalizedToPoint(rect, _hangarShotOrigin);
            Vector2 marker = new(Mathf.Clamp(origin.x, rect.xMin + 3f, rect.xMax - 3f),
                Mathf.Clamp(origin.y, rect.yMin + 3f, rect.yMax - 3f));
            Diamond(mesh, marker, 2.5f, Tracer);
            if (!_hangarShot.HasValue && !_hangarFlight.HasValue) return;

            if (_hangarFlight is HangarFlight flight)
            {
                Vector2 start = Rect.NormalizedToPoint(rect, flight.Origin);
                Vector2 destination = Rect.NormalizedToPoint(rect, flight.Destination);
                Line(mesh, start, destination, 1f, FadeAlpha(TracerDim, 0.52f));
                DrawHangarProjectile(mesh, start, destination, flight.Elapsed / HangarFlightDuration);
                return;
            }

            var (end, hit, trace) = _hangarShot.Value;
            Color tint = ArmorPreviewColor(hit?.Armor);
            if (!hit.HasValue)
            {
                Line(mesh, origin, Rect.NormalizedToPoint(rect, end), 1f, FadeAlpha(tint, 0.45f));
                return;
            }

            Vector2 impact = project(trace.Entry, 0f);
            Line(mesh, origin, impact, 1f, tint);
            Vector2 direction = (impact - origin).normalized;
            Vector2 arrow = impact - direction * 5f;
            Vector2 wing = Vector2.Perpendicular(direction) * 3f;
            Line(mesh, arrow - wing, impact, 1f, tint);
            Line(mesh, arrow + wing, impact, 1f, tint);
            if (hit.Value.Armor == ArmorHit.Penetrated)
                Line(mesh, impact, project(FindHangarTraceEnd(trace), 0f), 2f, tint);
            Diamond(mesh, impact, 2.5f, tint);
        }

        private static void DrawHangarProjectile(VertexHelper mesh, Vector2 start, Vector2 end, float progress)
        {
            Vector2 span = end - start;
            Vector2 head = Vector2.Lerp(start, end, progress);
            Vector2 tail = head - span.normalized * Mathf.Min(10f, span.magnitude * progress);
            Line(mesh, tail, head, 2f, Tracer);
            Diamond(mesh, head, 2.2f, Flash);
        }

        private static Vector2 FindHangarTraceEnd(TankShotTrace trace)
        {
            Vector2 delta = trace.End - trace.Entry;
            float reach = Mathf.Clamp01(Mathf.Min(ExitFraction(trace.Entry.x, delta.x, -1f, 1f),
                ExitFraction(trace.Entry.y, delta.y, -1f, 1f)));
            return trace.Entry + delta * reach;
        }

        private static float ExitFraction(float start, float delta, float min, float max) =>
            Mathf.Abs(delta) < 0.000001f ? float.PositiveInfinity : ((delta > 0f ? max : min) - start) / delta;
    }
}
