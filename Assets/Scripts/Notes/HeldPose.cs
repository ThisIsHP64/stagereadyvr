using System;
using UnityEngine;

namespace StageReadyVR.Notes
{
    [Serializable]
    public struct HeldPoseSettings
    {
        [Tooltip("Metres in front of the eyes")]
        public float forward;
        [Tooltip("Metres below the eyes")]
        public float drop;
        [Tooltip("Degrees the body may turn before the notes start to follow")]
        public float yawDeadZone;
        [Tooltip("Seconds the notes take to settle after the body turns")]
        public float yawFollowSeconds;
        [Tooltip("Seconds the notes take to catch up with head position")]
        public float positionFollowSeconds;

        public static HeldPoseSettings Default => new HeldPoseSettings
        {
            forward = 0.4f,
            drop = 0.35f,
            yawDeadZone = 35f,
            yawFollowSeconds = 0.4f,
            positionFollowSeconds = 0.15f
        };
    }

    /// <summary>Where the notes are anchored between frames.</summary>
    public struct HeldPoseState
    {
        public bool Initialized;
        public Vector3 Head;
        public float Yaw;
        public bool Recentering;
    }

    /// <summary>
    /// Pure placement math for notes held at chest height. Only the heading of the
    /// head counts, so glancing up at the audience or down at the notes never moves
    /// them. Small turns are ignored and large ones are followed smoothly, which
    /// reads like notes held by the body rather than glued to the face.
    /// </summary>
    public static class HeldPose
    {
        // once recentering starts it runs until this close, so the notes do not stop at the dead zone edge
        const float SettledDegrees = 2f;

        /// <summary>
        /// Heading in degrees around the vertical axis. When looking almost straight
        /// down the forward vector has no reliable heading, so the head's up vector,
        /// which then points the way the body faces, is used instead.
        /// </summary>
        public static float YawOf(Vector3 forward, Vector3 up)
        {
            var flat = new Vector2(forward.x, forward.z);
            if (flat.sqrMagnitude < 0.04f)
            {
                // looking down tips up forward, looking up tips it backward
                float sign = forward.y < 0f ? 1f : -1f;
                flat = new Vector2(up.x, up.z) * sign;
            }
            if (flat.sqrMagnitude < 1e-6f) return 0f;
            return Mathf.Atan2(flat.x, flat.y) * Mathf.Rad2Deg;
        }

        public static HeldPoseState Snap(Vector3 head, Vector3 forward, Vector3 up)
        {
            return new HeldPoseState { Initialized = true, Head = head, Yaw = YawOf(forward, up) };
        }

        public static HeldPoseState Step(HeldPoseState state, Vector3 head, Vector3 forward, Vector3 up,
                                         HeldPoseSettings settings, float deltaTime)
        {
            if (!state.Initialized) return Snap(head, forward, up);

            float dt = Mathf.Max(0f, deltaTime);
            float delta = Mathf.DeltaAngle(state.Yaw, YawOf(forward, up));
            float absDelta = Mathf.Abs(delta);

            if (absDelta > settings.yawDeadZone) state.Recentering = true;
            else if (absDelta < SettledDegrees) state.Recentering = false;

            if (state.Recentering)
                state.Yaw = Mathf.Repeat(state.Yaw + delta * Ease(dt, settings.yawFollowSeconds) + 180f, 360f) - 180f;

            state.Head = Vector3.Lerp(state.Head, head, Ease(dt, settings.positionFollowSeconds));
            return state;
        }

        /// <summary>
        /// The world pose for the notes. A world space canvas shows its front to a viewer
        /// looking along its forward axis, so it looks from the eyes toward the notes,
        /// which also tilts the page up toward the face.
        /// </summary>
        public static Pose ToPose(HeldPoseState state, HeldPoseSettings settings)
        {
            float rad = state.Yaw * Mathf.Deg2Rad;
            var facing = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            Vector3 position = state.Head + facing * settings.forward + Vector3.down * settings.drop;

            Vector3 look = position - state.Head;
            Quaternion rotation = look.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(look, Vector3.up) : Quaternion.identity;
            return new Pose(position, rotation);
        }

        /// <summary>
        /// A level pose straight ahead along the head's heading, facing the viewer, for
        /// things placed once like the teleprompter. Head pitch is ignored so a glance
        /// at the floor when the session starts does not put it in the floor.
        /// </summary>
        public static Pose Ahead(Vector3 head, Vector3 forward, Vector3 up, float distance, float verticalOffset)
        {
            float rad = YawOf(forward, up) * Mathf.Deg2Rad;
            var facing = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            Vector3 position = head + facing * distance + Vector3.up * verticalOffset;
            return new Pose(position, Quaternion.LookRotation(facing, Vector3.up));
        }

        // frame rate independent exponential smoothing; zero time constant snaps
        static float Ease(float dt, float seconds)
        {
            return seconds <= 0f ? 1f : 1f - Mathf.Exp(-dt / seconds);
        }
    }
}
