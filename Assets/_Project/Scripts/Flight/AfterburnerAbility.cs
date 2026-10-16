using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>
    /// Interceptor: a big burst of speed on top of the normal flight envelope. Separate from Boost: it doesn't use
    /// the boost meter, and the two stack. Needs a working engine.
    /// </summary>
    [CreateAssetMenu(menuName = "Winging It/Abilities/Afterburner", fileName = "Afterburner")]
    public class AfterburnerAbility : AbilityStats
    {
        [Tooltip("Multiplies min and max speed, so every throttle setting gets faster.")]
        [Min(1f)] public float speedMultiplier = 1.4f;
        [Tooltip("Multiplies acceleration, so the extra speed arrives quickly.")]
        [Min(1f)] public float accelerationMultiplier = 2.5f;

        public AfterburnerAbility()
        {
            displayName = "Afterburner";
            duration = 4f;
            cooldown = 18f;
        }

        public override bool CanActivate(PlaneAbility ability) => ability.Plane.Modifiers.Get(FlightStat.Thrust) > 0f;

        public override void Begin(PlaneAbility ability)
        {
            var stack = ability.Plane.Modifiers;
            stack.Add(FlightStat.MinSpeed, ModifierOp.Multiply, speedMultiplier, ability);
            stack.Add(FlightStat.MaxSpeed, ModifierOp.Multiply, speedMultiplier, ability);
            stack.Add(FlightStat.Acceleration, ModifierOp.Multiply, accelerationMultiplier, ability);
            ability.SetFlameVisible(true);
        }

        public override void End(PlaneAbility ability)
        {
            ability.Plane.Modifiers.RemoveAllFromSource(ability);
            ability.SetFlameVisible(false);
        }
    }
}
