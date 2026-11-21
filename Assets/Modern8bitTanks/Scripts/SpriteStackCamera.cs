using UnityEngine;

namespace Modern8bitTanks
{
    public enum BattleCameraMode
    {
        TurretLock, // Camera roll aligns smoothly with the player's turret facing (12 o'clock = gun barrel!)
        HullLock,   // Camera roll aligns smoothly with the player's hull heading (12 o'clock = hull front!)
        FixedTop    // Classic north-up fixed top-down camera (0 deg)
    }

    /// <summary>
    /// Global 360-degree Sprite-Stacking camera basis helper:
    /// Keeps every stacked 3D slice (tanks, turrets, multi-story buildings, trees, mesas),
    /// vertical lift, squash foreshortening, shadows, particles, and painter's-algorithm depth sorting
    /// perfectly upright on screen at ANY camera roll angle!
    /// </summary>
    public static class SpriteStackCamera
    {
        public static float RollDeg { get; private set; }
        public static Vector2 CamRight { get; private set; } = Vector2.right;
        public static Vector2 CamUp { get; private set; } = Vector2.up;

        public static void SetRoll(float rollDeg)
        {
            RollDeg = rollDeg;
            float rad = rollDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad);
            float s = Mathf.Sin(rad);
            CamRight = new Vector2(c, s);
            CamUp = new Vector2(-s, c);
        }

        public static Vector2 Squash(Vector2 groundVec, float squash)
        {
            if (Mathf.Abs(RollDeg) < 0.001f)
                return new Vector2(groundVec.x, groundVec.y * squash);

            float r = groundVec.x * CamRight.x + groundVec.y * CamRight.y;
            float u = (groundVec.x * CamUp.x + groundVec.y * CamUp.y) * squash;
            return CamRight * r + CamUp * u;
        }

        public static Vector2 Unsquash(Vector2 worldVec, float squash)
        {
            float inv = 1f / Mathf.Max(0.1f, squash);
            if (Mathf.Abs(RollDeg) < 0.001f)
                return new Vector2(worldVec.x, worldVec.y * inv);

            float r = worldVec.x * CamRight.x + worldVec.y * CamRight.y;
            float u = (worldVec.x * CamUp.x + worldVec.y * CamUp.y) * inv;
            return CamRight * r + CamUp * u;
        }

        public static int DepthOrder(Vector2 worldPos)
        {
            float screenY = worldPos.x * CamUp.x + worldPos.y * CamUp.y;
            return Mathf.Clamp(Mathf.RoundToInt(-screenY * 100f), -10000, 10000);
        }
    }
}
