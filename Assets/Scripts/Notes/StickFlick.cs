namespace StageReadyVR.Notes
{
    /// <summary>
    /// Turns a thumbstick axis into single page turns. A flick fires once when it
    /// passes the fire threshold, then the stick must come back near centre before
    /// it can fire again, so holding the stick never skips through pages.
    /// </summary>
    public sealed class StickFlick
    {
        public const float DefaultFire = 0.7f;
        public const float DefaultRearm = 0.3f;

        readonly float fire;
        readonly float rearm;
        bool armed = true;

        public StickFlick(float fire = DefaultFire, float rearm = DefaultRearm)
        {
            // a rearm point above the fire point would let one push fire repeatedly
            this.fire = fire;
            this.rearm = rearm < fire ? rearm : fire * 0.5f;
        }

        /// <summary>Feed the axis once per frame. Returns 1 for a right flick, -1 for left, 0 otherwise.</summary>
        public int Step(float x)
        {
            float magnitude = x < 0f ? -x : x;

            if (!armed)
            {
                if (magnitude < rearm) armed = true;
                return 0;
            }

            if (magnitude < fire) return 0;
            armed = false;
            return x > 0f ? 1 : -1;
        }

        /// <summary>Forgets a held stick, for example after the notes were hidden.</summary>
        public void Reset()
        {
            armed = true;
        }
    }
}
