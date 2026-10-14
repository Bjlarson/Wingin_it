namespace WingingIt.Flight
{
    /// <summary>
    /// One frame of pilot commands. Players, bots, and (later) the network all drive a
    /// <see cref="PlaneController"/> through this struct only.
    /// </summary>
    public struct FlightInput
    {
        /// <summary>-1..1, + = nose up.</summary>
        public float Pitch;
        /// <summary>-1..1, + = roll right (right wing down).</summary>
        public float Roll;
        /// <summary>-1..1, + = nose right.</summary>
        public float Yaw;
        /// <summary>-1..1 rate of throttle change. Ignored when <see cref="AutoThrottle"/> is set.</summary>
        public float Throttle;
        /// <summary>Hold throttle at the class cruise setting (mobile default).</summary>
        public bool AutoThrottle;
        public bool Boost;
    }
}
