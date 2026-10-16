using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>
    /// Fighter: a quick full roll that shakes off every missile chasing the plane and resets every lock on it.
    /// Rolls toward the stick (right with no roll input).
    /// </summary>
    [CreateAssetMenu(menuName = "Winging It/Abilities/Barrel Roll", fileName = "BarrelRoll")]
    public class BarrelRollAbility : AbilityStats
    {
        public BarrelRollAbility()
        {
            displayName = "Barrel Roll";
            duration = 0.8f;
            cooldown = 10f;
        }

        public override void Begin(PlaneAbility ability)
        {
            var plane = ability.Plane;
            float direction = plane.CurrentInput.Roll < -0.1f ? -1f : 1f;
            plane.StartBarrelRoll(direction * 360f / duration, duration);
            PlaneMissiles.BreakLocksOn(plane);
        }

        public override void End(PlaneAbility ability) => ability.Plane.StopBarrelRoll();
    }
}
