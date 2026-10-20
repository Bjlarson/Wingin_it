namespace WingingIt.Flight
{
    /// <summary>What the roll and pitch controls mean. Bots and mouse aim always fly <see cref="AutoLevel"/>.</summary>
    public enum ControlStyle
    {
        /// <summary>Stick is a turn rate; let go and the plane levels itself.</summary>
        AutoLevel,
        /// <summary>Stick is a turn rate; let go and the plane stops turning where it is (no self-leveling).</summary>
        Hold,
        /// <summary>Stick is a bank angle: left banks left and the plane turns left, centered levels the wings.</summary>
        Bank,
    }

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
        /// <summary>How <see cref="Pitch"/> and <see cref="Roll"/> are read. Default is the classic self-leveling stick.</summary>
        public ControlStyle Style;
    }
}
