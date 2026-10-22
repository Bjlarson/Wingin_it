using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>
    /// Ram Royale's stand-in for Support's Repair Aura (no allies to heal): a short, hard charge, and rams landed while
    /// it runs hit harder (<see cref="ramDamageMultiplier"/>). Not a class asset: the mode hands it out through
    /// <see cref="PlaneAbility.Override"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Winging It/Abilities/Ram Boost", fileName = "RamBoost")]
    public class RamBoostAbility : AbilityStats
    {
        [Tooltip("Multiplies min and max speed while charging.")]
        [Min(1f)] public float speedMultiplier = 1.3f;
        [Tooltip("Multiplies acceleration, so the charge arrives at once.")]
        [Min(1f)] public float accelerationMultiplier = 4f;
        [Tooltip("Multiplies the damage this plane deals in a ram while charging.")]
        [Min(1f)] public float ramDamageMultiplier = 1.5f;

        public RamBoostAbility()
        {
            displayName = "Ram Boost";
            duration = 1.5f;
            cooldown = 10f;
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
