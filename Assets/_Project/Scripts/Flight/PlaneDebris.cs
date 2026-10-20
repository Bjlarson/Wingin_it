using UnityEngine;
using WingingIt.Effects;

namespace WingingIt.Flight
{
    /// <summary>
    /// Loose wreckage (a broken-off part or a crashed plane's pieces). Cosmetic only: planes fly through it
    /// instead of crashing, and it despawns on its own, letting its smoke trail drift away rather than vanish with it.
    /// It kicks up dust (or spray) where it first slams into the ground.
    /// </summary>
    public class PlaneDebris : MonoBehaviour
    {
        const float ImpactSpeed = 8f;

        float _timeLeft = float.PositiveInfinity;
        bool _landed;

        public static bool IsDebris(Collider collider) =>
            collider.attachedRigidbody != null && collider.attachedRigidbody.TryGetComponent<PlaneDebris>(out _);

        /// <summary>Despawns after <paramref name="lifetime"/> seconds.</summary>
        public void Expire(float lifetime) => _timeLeft = lifetime;

        void Update()
        {
            _timeLeft -= Time.deltaTime;
            if (_timeLeft > 0f)
                return;
            foreach (var effect in GetComponentsInChildren<VfxEffect>())
                effect.Stop();
            Destroy(gameObject);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (_landed || collision.relativeVelocity.magnitude < ImpactSpeed)
                return;
            _landed = true;
            var contact = collision.GetContact(0);
            if (PlaneController.IsWater(collision.collider))
                Vfx.Play(VfxKind.WaterSplash, contact.point, 0.35f);
            else
                Vfx.Play(VfxKind.BulletHitGround, contact.point, Quaternion.FromToRotation(Vector3.up, contact.normal), 3f);
        }
    }
}
