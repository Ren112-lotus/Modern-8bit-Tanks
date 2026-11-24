using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Modern8bitTanks
{
    /// <summary>A distance field from the capture area through the available tank-sized passages.</summary>
    internal sealed class BattlefieldNavigation
    {
        private const int CellSize = 16;
        private const int Columns = BattlefieldLayout.Width / CellSize;
        private const int Rows = BattlefieldLayout.Height / CellSize;
        private const float Clearance = 17f;
        private static readonly Vector2Int[] Steps =
        {
            Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up
        };

        private readonly BattlefieldLayout _layout;
        private readonly int[] _distance = new int[Columns * Rows];
        private readonly bool[] _blocked = new bool[Columns * Rows];
        private readonly int[] _queue = new int[Columns * Rows];

        internal BattlefieldNavigation(BattlefieldLayout layout) => _layout = layout;

        internal void Rebuild(IReadOnlyList<DestructibleProp> props = null) =>
            RebuildAsync(null, props).GetAwaiter().GetResult();

        internal async UniTask RebuildAsync(BattleLoadBudget budget, IReadOnlyList<DestructibleProp> props = null)
        {
            Array.Fill(_distance, -1);
            int start = 0;
            int end = 0;
            for (int i = 0; i < _distance.Length; i++)
            {
                if (budget != null && (i & 63) == 0) await budget.YieldIfSpentAsync();
                Vector2 point = CellCenter(i);
                _blocked[i] = IsBlocked(point, props);
                if (_blocked[i] || (point - _layout.ControlPoint).sqrMagnitude > 24f * 24f) continue;
                _distance[i] = 0;
                _queue[end++] = i;
            }

            while (start < end)
            {
                if (budget != null && (start & 63) == 0) await budget.YieldIfSpentAsync();
                int cell = _queue[start++];
                foreach (var step in Steps)
                {
                    int next = CellOffset(cell, step);
                    if (next < 0 || _blocked[next] || _distance[next] >= 0) continue;
                    _distance[next] = _distance[cell] + 1;
                    _queue[end++] = next;
                }
            }
        }

        internal bool CanReachControl(Vector2 position) => _distance[PositionToCell(position)] >= 0;

        internal bool TryGetSteering(Vector2 position, Vector2 heading, out Vector2 direction)
        {
            direction = Vector2.zero;
            int cell = PositionToCell(position);
            if (_distance[cell] < 0)
            {
                // A tank already touching a wall may start just outside the clearance used for planning.
                int nearest = -1;
                float nearestDistance = float.PositiveInfinity;
                int orientation = cell % Columns < Columns / 2 ? 1 : -1;
                for (int y = -2; y <= 2; y++)
                for (int x = -2; x <= 2; x++)
                {
                    int candidate = CellOffset(cell, new Vector2Int(x * orientation, y * orientation));
                    if (candidate < 0 || _distance[candidate] < 0) continue;
                    float distance = (CellCenter(candidate) - position).sqrMagnitude;
                    if (distance >= nearestDistance) continue;
                    nearest = candidate;
                    nearestDistance = distance;
                }
                if (nearest < 0) return false;
                direction = (CellCenter(nearest) - position).normalized;
                return true;
            }

            if (_distance[cell] == 0)
            {
                direction = (_layout.ControlPoint - position).normalized;
                return true;
            }

            int next = FindNextCell(cell, heading);
            if (next < 0) return false;
            int lookAhead = FindNextCell(next, CellCenter(next) - position);
            if (lookAhead >= 0 && CanDriveStraight(position, CellCenter(lookAhead))) next = lookAhead;
            direction = (CellCenter(next) - position).normalized;
            return true;
        }

        private bool IsBlocked(Vector2 position, IReadOnlyList<DestructibleProp> props)
        {
            const float edge = BattleSceneController.BorderPadPx + Clearance;
            if (position.x < edge || position.x > BattlefieldLayout.Width - edge ||
                position.y < edge || position.y > BattlefieldLayout.Height - edge)
                return true;

            var cover = _layout.Cover;
            for (int i = 0; i < cover.Count; i++)
            {
                if (props != null && !props[i].alive) continue;
                if (cover[i].Contains(position, Clearance)) return true;
            }

            return false;
        }

        private int FindNextCell(int cell, Vector2 heading)
        {
            int result = -1;
            float alignment = float.NegativeInfinity;
            // Equal-cost turns must use the same preference after rotating the map.
            int orientation = cell % Columns < Columns / 2 ? 1 : -1;
            foreach (var offset in Steps)
            {
                Vector2Int step = offset * orientation;
                int next = CellOffset(cell, step);
                if (next < 0 || _distance[next] < 0 || _distance[next] >= _distance[cell]) continue;
                float nextAlignment = Vector2.Dot(heading, step);
                if (nextAlignment <= alignment) continue;
                result = next;
                alignment = nextAlignment;
            }
            return result;
        }

        private bool CanDriveStraight(Vector2 from, Vector2 to)
        {
            int samples = Mathf.CeilToInt(Vector2.Distance(from, to) / (CellSize * 0.25f));
            for (int i = 1; i < samples; i++)
                if (_blocked[PositionToCell(Vector2.Lerp(from, to, i / (float)samples))])
                    return false;
            return true;
        }

        private static int PositionToCell(Vector2 position)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt(position.x / CellSize), 0, Columns - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(position.y / CellSize), 0, Rows - 1);
            return y * Columns + x;
        }

        private static int CellOffset(int cell, Vector2Int offset)
        {
            int x = cell % Columns + offset.x;
            int y = cell / Columns + offset.y;
            return x >= 0 && x < Columns && y >= 0 && y < Rows ? y * Columns + x : -1;
        }

        private static Vector2 CellCenter(int index) =>
            new Vector2((index % Columns + 0.5f) * CellSize, (index / Columns + 0.5f) * CellSize);
    }
}
