using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>
    /// A class's special ability: press to activate, it lasts <see cref="duration"/>, then recharges for
    /// <see cref="cooldown"/>. Each ability is a subclass with its own tuning. Assets are shared by every plane of a
    /// class, so they hold no state: everything per plane lives in <see cref="PlaneAbility"/>, and the effects are
    /// undone by source (modifiers, damage multiplier, visuals) when the ability ends.
    /// </summary>
    public abstract class AbilityStats : ScriptableObject
    {
        public string displayName = "Ability";
        [Tooltip("Seconds the effect lasts.")]
        [Min(0.05f)] public float duration = 4f;
        [Tooltip("Seconds to recharge, counted from when the effect ends.")]
        [Min(0f)] public float cooldown = 15f;

        /// <summary>Whether the ability can start right now (alive, off cooldown and not active are checked already).</summary>
        public virtual bool CanActivate(PlaneAbility ability) => true;
        public abstract void Begin(PlaneAbility ability);
        /// <summary>Called every physics step while active, after <see cref="Begin"/>.</summary>
        public virtual void Tick(PlaneAbility ability, float dt) { }
        /// <summary>Undoes everything <see cref="Begin"/> did. Also called when the plane dies, respawns or changes class.</summary>
        public abstract void End(PlaneAbility ability);
    }
}
