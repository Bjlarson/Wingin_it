using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>
    /// Support: heals every part (not lost parts or a dead engine) of allies within <see cref="radius"/>, and itself
    /// more slowly. The only healing in the air. Support also has a passive radar that reveals enemies near it to
    /// its team (<see cref="PlaneAbility.IsRevealedTo"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "Winging It/Abilities/Repair Aura", fileName = "RepairAura")]
    public class RepairAuraAbility : AbilityStats
    {
        [Tooltip("Allies within this distance (m) are healed.")]
        [Min(1f)] public float radius = 80f;
        [Tooltip("Fraction of each part's max HP healed per second.")]
        [Range(0f, 1f)] public float healPerSecond = 0.06f;
        [Tooltip("The Support heals itself at this fraction of the ally rate.")]
        [Range(0f, 1f)] public float selfHealMultiplier = 0.5f;

        [Header("Radar (passive)")]
        [Tooltip("Enemies within this distance (m) of the Support are revealed to its team. 0 = no radar.")]
        [Min(0f)] public float radarRange = 700f;

        public RepairAuraAbility()
        {
            displayName = "Repair Aura";
            duration = 6f;
            cooldown = 25f;
        }

        public override void Begin(PlaneAbility ability) => ability.SetAuraVisible(true, radius);

        public override void Tick(PlaneAbility ability, float dt)
        {
            var self = ability.Plane;
            float radiusSq = radius * radius;
            foreach (var other in PlaneController.All)
            {
                if (!other.IsAlive || (other != self && !other.IsAllyOf(self)))
                    continue;
                if ((other.Position - self.Position).sqrMagnitude > radiusSq)
                    continue;
                var health = other == self ? ability.Health : other.GetComponent<PlaneHealth>();
                if (health == null)
                    continue;
                float rate = healPerSecond * (other == self ? selfHealMultiplier : 1f);
                health.HealFraction(rate * dt, self);
            }
        }

        public override void End(PlaneAbility ability) => ability.SetAuraVisible(false, radius);
    }
}
