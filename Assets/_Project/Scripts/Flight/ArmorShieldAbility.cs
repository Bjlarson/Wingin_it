using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>Heavy Fighter: takes less damage on every part for a few seconds.</summary>
    [CreateAssetMenu(menuName = "Winging It/Abilities/Armor Shield", fileName = "ArmorShield")]
    public class ArmorShieldAbility : AbilityStats
    {
        [Tooltip("Fraction of incoming damage still taken (0.5 = half damage).")]
        [Range(0f, 1f)] public float damageTaken = 0.5f;

        public ArmorShieldAbility()
        {
            displayName = "Armor Shield";
            duration = 4f;
            cooldown = 20f;
        }

        public override void Begin(PlaneAbility ability)
        {
            if (ability.Health != null)
                ability.Health.DamageTakenMultiplier = damageTaken;
            ability.SetShieldVisible(true);
        }

        public override void End(PlaneAbility ability)
        {
            if (ability.Health != null)
                ability.Health.DamageTakenMultiplier = 1f;
            ability.SetShieldVisible(false);
        }
    }
}
