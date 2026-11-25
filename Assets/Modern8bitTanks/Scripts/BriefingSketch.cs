using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BriefingSketch : MaskableGraphic, IPointerDownHandler, IPointerUpHandler,
        IInitializePotentialDragHandler, IDragHandler
    {
        // Two passes of up to eight vertices per point fit in uGUI's 16-bit mesh.
        private const int MaxPoints = 3000;
        private const float MapWidth = 320f;
        private const float MinPointSpacing = 1.5f;
        private static readonly Color32 Ink = new(255, 179, 124, 255);
        private static readonly Color32 InkOutline = new(26, 23, 24, 255);
        private readonly List<Vector2> _points = new();
        private readonly List<int> _strokeStarts = new();
        private int? _drawingPointer;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || _drawingPointer.HasValue ||
                _points.Count >= MaxPoints || !TryReadPointer(eventData, out Vector2 point, out bool inside) || !inside)
                return;

            _drawingPointer = eventData.pointerId;
            _strokeStarts.Add(_points.Count);
            _points.Add(point);
            SetVerticesDirty();
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

            if (_points.Count < MaxPoints)
            {
                Rect area = rectTransform.rect;
                Vector2 delta = Vector2.Scale(point - _points[^1], area.size) * (MapWidth / area.width);
                if (delta.sqrMagnitude >= MinPointSpacing * MinPointSpacing || (!inside && delta.sqrMagnitude > 0f))
                {
                    _points.Add(point);
                    SetVerticesDirty();
                }
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
            if (_strokeStarts.Count == 0) return;
            _drawingPointer = null;
            int start = _strokeStarts[^1];
            _strokeStarts.RemoveAt(_strokeStarts.Count - 1);
            _points.RemoveRange(start, _points.Count - start);
            SetVerticesDirty();
        }

        internal void Clear()
        {
            _drawingPointer = null;
            _strokeStarts.Clear();
            _points.Clear();
            SetVerticesDirty();
        }

        protected override void OnDisable()
        {
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
            DrawStrokes(mesh, area, 2.15f * scale, InkOutline);
            DrawStrokes(mesh, area, 1.15f * scale, Ink);
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

        private void DrawStrokes(VertexHelper mesh, Rect area, float radius, Color32 ink)
        {
            var corner = new Vector2(radius, radius);
            for (int stroke = 0; stroke < _strokeStarts.Count; stroke++)
            {
                int start = _strokeStarts[stroke];
                int end = stroke + 1 < _strokeStarts.Count ? _strokeStarts[stroke + 1] : _points.Count;
                Vector2 previous = Rect.NormalizedToPoint(area, _points[start]);
                for (int i = start; i < end; i++)
                {
                    Vector2 current = Rect.NormalizedToPoint(area, _points[i]);
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
