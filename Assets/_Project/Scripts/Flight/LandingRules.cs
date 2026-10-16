using System;
using UnityEngine;

namespace WingingIt.Flight
{
    public enum TouchdownResult
    {
        /// <summary>Wheels down, rolling on the ground.</summary>
        Landed,
        /// <summary>A hard landing: some body damage and a hop back into the air.</summary>
        Bounced,
        /// <summary>Destroyed on touchdown.</summary>
        Crashed,
    }

    /// <summary>What was wrong with a touchdown (the worst thing, if there were several).</summary>
    public enum TouchdownFault
    {
        None,
        /// <summary>Hit the ground before the gear was locked.</summary>
        GearNotLocked,
        /// <summary>Pitch or roll beyond the limit.</summary>
        BadAngle,
        /// <summary>Sinking fast enough to crash.</summary>
        SinkTooFast,
        /// <summary>Sinking fast enough to bounce.</summary>
        HardSink,
        /// <summary>Forward speed over the touchdown limit.</summary>
        TooFast,
        /// <summary>Touched down on the sea, which is always a crash.</summary>
        Ditched,
    }

    /// <summary>One touchdown: what happened, why, and the numbers it was judged on.</summary>
    public readonly struct TouchdownInfo
    {
        public readonly TouchdownResult Result;
        public readonly TouchdownFault Fault;
        /// <summary>Forward speed along the ground (m/s).</summary>
        public readonly float Speed;
        /// <summary>Downward speed (m/s, + = sinking).</summary>
        public readonly float SinkRate;
        /// <summary>Degrees, + = nose up.</summary>
        public readonly float Pitch;
        /// <summary>Degrees, + = right wing up.</summary>
        public readonly float Roll;

        public TouchdownInfo(TouchdownResult result, TouchdownFault fault, float speed, float sinkRate, float pitch, float roll)
        {
            Result = result;
            Fault = fault;
            Speed = speed;
            SinkRate = sinkRate;
            Pitch = pitch;
            Roll = roll;
        }
    }

    /// <summary>
    /// Judges a touchdown: gear locked, gentle sink, wings and nose near level, and not too fast. Generous on purpose:
    /// landing should never feel like a chore, but a sloppy one costs HP and time. Pure data + math, so it's testable
    /// and can run on the server unchanged.
    /// </summary>
    [Serializable]
    public class LandingRules
    {
        [Tooltip("Pitch or roll beyond this (deg) on touchdown is a crash.")]
        public float maxAngle = 15f;
        [Tooltip("Sinking faster than this (m/s) bounces.")]
        public float bounceSinkRate = 6f;
        [Tooltip("Sinking faster than this (m/s) crashes.")]
        public float crashSinkRate = 15f;
        [Tooltip("Touching down faster than this × the class min speed bounces.")]
        public float maxSpeedFactor = 1.6f;

        [Header("Bounce")]
        [Tooltip("Body damage from a bounce, as a fraction of the body's max HP. A bounce that would kill is a crash.")]
        [Range(0f, 1f)] public float bounceDamage = 0.1f;
        [Tooltip("The nose kicks up this much (deg) on a bounce, so the plane hops.")]
        public float bouncePitch = 5f;

        /// <summary>
        /// Close enough to level (within twice the angle limit) that hitting flat ground with the airframe counts as a
        /// failed landing (a belly landing, a wingtip strike) rather than just flying into the ground.
        /// </summary>
        public bool IsLandingAttempt(float pitch, float roll) =>
            Mathf.Abs(pitch) <= maxAngle * 2f && Mathf.Abs(roll) <= maxAngle * 2f;

        /// <summary>Highest forward speed that still lands cleanly for a class with this min speed.</summary>
        public float MaxTouchdownSpeed(float classMinSpeed) => classMinSpeed * maxSpeedFactor;

        /// <summary>
        /// Judges a touchdown. <paramref name="sinkRate"/> is + when descending; <paramref name="pitch"/> and
        /// <paramref name="roll"/> are in degrees from level.
        /// </summary>
        public TouchdownInfo Evaluate(bool gearLocked, float speed, float sinkRate, float pitch, float roll, float classMinSpeed)
        {
            TouchdownInfo Result(TouchdownResult result, TouchdownFault fault) =>
                new(result, fault, speed, sinkRate, pitch, roll);

            if (!gearLocked)
                return Result(TouchdownResult.Crashed, TouchdownFault.GearNotLocked);
            if (Mathf.Abs(pitch) > maxAngle || Mathf.Abs(roll) > maxAngle)
                return Result(TouchdownResult.Crashed, TouchdownFault.BadAngle);
            if (sinkRate > crashSinkRate)
                return Result(TouchdownResult.Crashed, TouchdownFault.SinkTooFast);
            if (sinkRate > bounceSinkRate)
                return Result(TouchdownResult.Bounced, TouchdownFault.HardSink);
            if (speed > MaxTouchdownSpeed(classMinSpeed))
                return Result(TouchdownResult.Bounced, TouchdownFault.TooFast);
            return Result(TouchdownResult.Landed, TouchdownFault.None);
        }
    }
}
