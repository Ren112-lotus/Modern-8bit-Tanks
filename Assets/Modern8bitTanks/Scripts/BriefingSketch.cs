using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    internal enum BriefingInkAction : byte { Start, Extend, Undo, Clear }

    internal sealed class BriefingInk
    {
        // At most 3,000 visible points across five players fit the uGUI mesh.
        internal const int MaxPoints = 3000;
        internal readonly List<Vector2> Points = new();
        internal readonly List<int> StrokeStarts = new();

        internal bool Apply(BriefingInkAction action, Vector2 point, int limit)
        {
            switch (action)
            {
                case BriefingInkAction.Start when Points.Count < limit:
                    StrokeStarts.Add(Points.Count);
                    Points.Add(point);
                    return true;
                case BriefingInkAction.Extend when Points.Count is > 0 && Points.Count < limit:
                    Points.Add(point);
                    return true;
                case BriefingInkAction.Undo when StrokeStarts.Count > 0:
                    int start = StrokeStarts[^1];
                    StrokeStarts.RemoveAt(StrokeStarts.Count - 1);
                    Points.RemoveRange(start, Points.Count - start);
                    return true;
                case BriefingInkAction.Clear when Points.Count > 0:
                    Points.Clear();
                    StrokeStarts.Clear();
                    return true;
                default:
                    return false;
            }
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BriefingSketch : MaskableGraphic, IPointerDownHandler, IPointerUpHandler,
        IInitializePotentialDragHandler, IDragHandler
    {
        private const float MapWidth = 320f;
        private const float MinPointSpacing = 1.5f;
        private static readonly Color32 Ink = new(255, 179, 124, 255);
        private static readonly Color32 InkOutline = new(26, 23, 24, 255);
        private static readonly Color32[] PlayerInk =
        {
            new(90, 207, 255, 255), new(255, 125, 191, 255), new(174, 232, 103, 255),
            new(196, 160, 255, 255), new(255, 187, 74, 255)
        };
        private readonly BriefingInk _offlineInk = new();
        private MultiplayerMatchTransport _transport;
        private int _firstTeamSlot;
        private int _localSlot;
        private int? _drawingPointer;

        internal static Color32 ColorForSlot(int slot) => PlayerInk[slot % PlayerInk.Length];

        internal void Bind(MultiplayerMatchTransport transport, int localSlot = -1)
        {
            if (isActiveAndEnabled && _transport != null) _transport.BriefingDrawingChanged -= SetVerticesDirty;
            _transport = transport;
            _localSlot = localSlot;
            _firstTeamSlot = MultiplayerSessionConnection.IsAlphaSlot(localSlot) ? 0 : MultiplayerSessionConnection.PlayersPerTeam;
            _drawingPointer = null;
            _offlineInk.Apply(BriefingInkAction.Clear, default, BriefingInk.MaxPoints);
            if (isActiveAndEnabled && _transport != null) _transport.BriefingDrawingChanged += SetVerticesDirty;
            SetVerticesDirty();
        }

        private bool Apply(BriefingInkAction action, Vector2 point = default)
        {
            if (_transport != null) return _transport.SendBriefingDrawing(action, point);
            if (!_offlineInk.Apply(action, point, BriefingInk.MaxPoints)) return false;
            SetVerticesDirty();
            return true;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || _drawingPointer.HasValue ||
                !TryReadPointer(eventData, out Vector2 point, out bool inside) || !inside)
                return;

            if (Apply(BriefingInkAction.Start, point)) _drawingPointer = eventData.pointerId;
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            if (_drawingPointer == eventData.pointerId)
                eventData.useDragThreshold = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_drawingPointer != eventData.pointerId ||
                !TryReadPointer(eventData, out Vector2 point, out bool inside))
                return;

            var ink = _transport != null ? _transport.GetBriefingInk(_localSlot) : _offlineInk;
            if (ink != null && ink.Points.Count > 0)
            {
                Rect area = rectTransform.rect;
                Vector2 delta = Vector2.Scale(point - ink.Points[^1], area.size) * (MapWidth / area.width);
                if (delta.sqrMagnitude >= MinPointSpacing * MinPointSpacing || (!inside && delta.sqrMagnitude > 0f))
                    Apply(BriefingInkAction.Extend, point);
            }

            if (!inside) _drawingPointer = null;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_drawingPointer != eventData.pointerId || eventData.button != PointerEventData.InputButton.Left)
                return;
            OnDrag(eventData);
            _drawingPointer = null;
        }

        internal void Undo()
        {
            _drawingPointer = null;
            Apply(BriefingInkAction.Undo);
        }

        internal void Clear()
        {
            _drawingPointer = null;
            Apply(BriefingInkAction.Clear);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (_transport != null) _transport.BriefingDrawingChanged += SetVerticesDirty;
        }

        protected override void OnDisable()
        {
            if (_transport != null) _transport.BriefingDrawingChanged -= SetVerticesDirty;
            _drawingPointer = null;
            base.OnDisable();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect area = rectTransform.rect;
            // Keep the blank map raycastable before the first stroke.
            AddQuad(mesh, area, area.min, new Vector2(area.xMin, area.yMax), area.max,
                new Vector2(area.xMax, area.yMin), new Color32(0, 0, 0, 0));
            float scale = area.width / MapWidth;
            if (_transport == null)
            {
                DrawStrokes(mesh, area, _offlineInk, 2.15f * scale, InkOutline);
                DrawStrokes(mesh, area, _offlineInk, 1.15f * scale, Ink);
                return;
            }

            for (int slot = _firstTeamSlot; slot < _firstTeamSlot + MultiplayerSessionConnection.PlayersPerTeam; slot++)
            {
                var ink = _transport.GetBriefingInk(slot);
                if (ink == null) continue;
                DrawStrokes(mesh, area, ink, 2.15f * scale, InkOutline);
                DrawStrokes(mesh, area, ink, 1.15f * scale, ColorForSlot(slot));
            }
        }

        private bool TryReadPointer(PointerEventData eventData, out Vector2 point, out bool inside)
        {
            point = Vector2.zero;
            inside = false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position,
                    eventData.pressEventCamera, out Vector2 local))
                return false;

            Rect area = rectTransform.rect;
            if (area.width <= 0f || area.height <= 0f || !float.IsFinite(local.x) || !float.IsFinite(local.y))
                return false;
            point = Rect.PointToNormalized(area, local);
            inside = area.Contains(local);
            return true;
        }

        private static void DrawStrokes(VertexHelper mesh, Rect area, BriefingInk drawing, float radius, Color32 ink)
        {
            var corner = new Vector2(radius, radius);
            var points = drawing.Points;
            var starts = drawing.StrokeStarts;
            for (int stroke = 0; stroke < starts.Count; stroke++)
            {
                int start = starts[stroke];
                int end = stroke + 1 < starts.Count ? starts[stroke + 1] : points.Count;
                Vector2 previous = Rect.NormalizedToPoint(area, points[start]);
                for (int i = start; i < end; i++)
                {
                    Vector2 current = Rect.NormalizedToPoint(area, points[i]);
                    if (i > start)
                    {
                        Vector2 normal = Vector2.Perpendicular(current - previous).normalized * radius;
                        AddQuad(mesh, area, previous - normal, previous + normal, current + normal, current - normal, ink);
                    }
                    AddQuad(mesh, area, current - corner, current + new Vector2(-radius, radius),
                        current + corner, current + new Vector2(radius, -radius), ink);
                    previous = current;
                }
            }
        }

        private static void AddQuad(VertexHelper mesh, Rect area, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 color)
        {
            int first = mesh.currentVertCount;
            AddClippedVertex(a);
            AddClippedVertex(b);
            AddClippedVertex(c);
            AddClippedVertex(d);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first, first + 2, first + 3);

            void AddClippedVertex(Vector2 point)
            {
                point.x = Mathf.Clamp(point.x, area.xMin, area.xMax);
                point.y = Mathf.Clamp(point.y, area.yMin, area.yMax);
                mesh.AddVert(point, color, Vector2.zero);
            }
        }
    }
}
