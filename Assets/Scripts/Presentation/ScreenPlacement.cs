using UnityEngine;

namespace StageReadyVR.Presentation
{
    /// <summary>Pure placement math for the projector screen, kept apart from Unity objects so it can be tested.</summary>
    public static class ScreenPlacement
    {
        /// <summary>
        /// A pose behind the head that faces the same way the head does, so the
        /// audience in front of the presenter sees the slide. Head pitch and roll
        /// are ignored, because a screen tilted by a glance at the floor looks wrong.
        /// A world space canvas shows its front to viewers looking along its forward axis,
        /// so the canvas forward points back toward the screen from the audience.
        /// </summary>
        public static Pose Behind(Vector3 headPosition, Vector3 headForward, float distance, float heightAboveHead)
        {
            Vector3 facing = Flatten(headForward);
            Vector3 position = headPosition - facing * distance + Vector3.up * heightAboveHead;
            return new Pose(position, Quaternion.LookRotation(-facing, Vector3.up));
        }

        /// <summary>The largest size with the given width over height that fits inside the bounds.</summary>
        public static Vector2 FitSize(float aspect, float maxWidth, float maxHeight)
        {
            if (aspect <= 0f || float.IsNaN(aspect) || float.IsInfinity(aspect)) aspect = 16f / 9f;
            float width = maxWidth;
            float height = width / aspect;
            if (height > maxHeight)
            {
                height = maxHeight;
                width = height * aspect;
            }
            return new Vector2(width, height);
        }

        static Vector3 Flatten(Vector3 forward)
        {
            forward.y = 0f;
            // looking straight up or down leaves no horizontal direction, so fall back to world forward
            return forward.sqrMagnitude < 1e-4f ? Vector3.forward : forward.normalized;
        }
    }
}
