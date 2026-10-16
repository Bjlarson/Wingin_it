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
        /// <summary>Hold to fire the machine guns.</summary>
        public bool FireGuns;
        /// <summary>Hold to fire rocket salvos (one per <see cref="RocketStats.salvoCooldown"/>).</summary>
        public bool FireRockets;
        /// <summary>Press to launch one homing missile at the locked target (needs a full lock).</summary>
        public bool FireMissile;
        /// <summary>Hold to drop bombs (one per <see cref="BombStats.dropInterval"/>).</summary>
        public bool DropBomb;
        /// <summary>Press to start the class ability (see <see cref="PlaneAbility"/>).</summary>
        public bool UseAbility;
    }
}
