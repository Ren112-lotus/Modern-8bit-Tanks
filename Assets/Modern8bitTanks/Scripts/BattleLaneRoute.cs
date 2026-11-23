using UnityEngine;

namespace Modern8bitTanks
{
    /// <summary>Tracks a single vehicle's progress along its assigned approach.</summary>
    internal sealed class BattleLaneRoute
    {
        private const float ArrivalRadius = 1.8f;
        private readonly Vector2[] _waypoints;
        private int _next;

        internal BattleLaneRoute(Vector2[] waypoints) => _waypoints = waypoints;

        internal bool TryGetSteering(Vector2 position, out Vector2 direction)
        {
            while (_next < _waypoints.Length)
            {
                Vector2 offset = _waypoints[_next] - position;
                if (offset.sqrMagnitude > ArrivalRadius * ArrivalRadius)
                {
                    direction = offset.normalized;
                    return true;
                }
                _next++;
            }

            direction = Vector2.zero;
            return false;
        }
    }
}
