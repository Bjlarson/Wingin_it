using UnityEngine;

namespace WingingIt.Flight
{
    public enum PlanePartType
    {
        Body,
        WingLeft,
        WingRight,
        HorizontalStabilizer,
        VerticalStabilizer,
        Engine,
    }

    /// <summary>
    /// One damageable section of the modular plane. Its child colliders are its hitbox.
    /// HP is owned here but only changed through <see cref="PlaneHealth"/>.
    /// </summary>
    public class PlanePart : MonoBehaviour
    {
        public const int TypeCount = (int)PlanePartType.Engine + 1;

        public PlanePartType partType;
        [Tooltip("Stump left on the plane once this part breaks off. Must live outside this part's hierarchy.")]
        public GameObject brokenEdge;

        public float MaxHealth { get; private set; }
        public float Health { get; private set; }
        public float NormalizedHealth => MaxHealth > 0f ? Health / MaxHealth : 0f;
        /// <summary>At 0 HP. Destroyed parts can't take more damage.</summary>
        public bool IsDestroyed => Health <= 0f;
        /// <summary>Wings and stabilizers break off at 0 HP; the body explodes and the engine just dies.</summary>
        public bool CanDetach => IsDetachable(partType);
        /// <summary>Broken off: the part (and its hitbox) is gone and the broken edge shows instead.</summary>
        public bool IsDetached { get; private set; }

        public static bool IsDetachable(PlanePartType type) =>
            type != PlanePartType.Body && type != PlanePartType.Engine;

        /// <summary>Finds the part a hit collider belongs to (for raycasts and projectiles).</summary>
        public static bool TryGetFromCollider(Collider collider, out PlanePart part)
        {
            part = collider != null ? collider.GetComponentInParent<PlanePart>() : null;
            return part != null;
        }

        internal void ResetHealth(float maxHealth)
        {
            MaxHealth = Mathf.Max(1f, maxHealth);
            Health = MaxHealth;
        }

        /// <summary>Hides the part and its hitbox and shows the broken edge, or puts the part back.</summary>
        internal void SetDetached(bool detached)
        {
            IsDetached = detached;
            gameObject.SetActive(!detached);
            if (brokenEdge != null)
                brokenEdge.SetActive(detached);
        }

        /// <summary>Restores HP up to the max. Destroyed parts stay destroyed. Returns the HP actually restored.</summary>
        internal float Heal(float amount)
        {
            if (IsDestroyed)
                return 0f;
            float healed = Mathf.Min(MaxHealth - Health, Mathf.Max(0f, amount));
            Health += healed;
            return healed;
        }

        /// <summary>Returns the damage actually taken (clamped to remaining HP).</summary>
        internal float TakeDamage(float amount)
        {
            float taken = Mathf.Min(Health, Mathf.Max(0f, amount));
            Health -= taken;
            return taken;
        }
    }
}
