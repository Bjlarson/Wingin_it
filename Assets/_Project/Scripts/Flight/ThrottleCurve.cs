using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>
    /// Throttle (0..1) to airspeed in the air. From the top of the idle band up it's the class's min to max speed, as
    /// always; inside the band it eases down to idle: the engine still turns, pulling the plane to just below its stall
    /// speed, so a plane at idle glides and settles instead of holding flying speed. Only touchdown cuts the engine.
    /// </summary>
    public static class ThrottleCurve
    {
        /// <summary>The bottom of the throttle range that eases from min speed down to idle.</summary>
        public const float IdleBand = 0.05f;

        /// <summary>The speed the engine pulls toward at <paramref name="throttle"/>, before thrust damage.</summary>
        public static float AirSpeed(float throttle, float idleSpeed, float minSpeed, float maxSpeed)
        {
            throttle = Mathf.Clamp01(throttle);
            if (throttle >= IdleBand)
                return Mathf.Lerp(minSpeed, maxSpeed, throttle);
            float bandTop = Mathf.Lerp(minSpeed, maxSpeed, IdleBand);
            return Mathf.Lerp(Mathf.Min(idleSpeed, minSpeed), bandTop, throttle / IdleBand);
        }

        /// <summary>How much of the engine's pull and drag is in use: 0 at idle, 1 from the top of the idle band up.</summary>
        public static float Power(float throttle) => Mathf.InverseLerp(0f, IdleBand, throttle);
    }
}
