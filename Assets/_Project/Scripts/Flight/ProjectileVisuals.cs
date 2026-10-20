using System;
using UnityEngine;
using UnityEngine.Rendering;
using WingingIt.Effects;
using Object = UnityEngine.Object;

namespace WingingIt.Flight
{
    /// <summary>
    /// Rocket, missile and bomb visuals: the weapon's model (ArtSource/Weapons) with a short exhaust flame and a smoke
    /// trail (a puff trail from the VFX pack, where asked), or a placeholder box when the prefab has no model. Also
    /// builds tracer and muzzle-flash objects.
    /// </summary>
    public static class ProjectileVisuals
    {
        /// <summary>A mesh from the weapons model and the materials for its submeshes. Unset: use the placeholder.</summary>
        [Serializable]
        public struct WeaponModel
        {
            public Mesh mesh;
            public Material[] materials;

            public bool IsSet => mesh != null && materials != null && materials.Length > 0;

            /// <summary>Uniform scale that puts the nose tip (the mesh's furthest +Z, origin mid-length) at half of <paramref name="length"/>.</summary>
            public float ScaleForLength(float length) => mesh.bounds.max.z > 0f ? length * 0.5f / mesh.bounds.max.z : 1f;
        }

        /// <summary>
        /// The visual for one projectile. <paramref name="puffTrail"/> &gt; 0 trails chunky puffs (<see cref="VfxKind.MissileTrail"/>
        /// at that scale) instead of the smoke ribbon, when the VFX pack is there.
        /// </summary>
        public static Transform Create(string name, Transform parent, Vector3 position, Vector3 dir, float length, float width,
            Material bodyMaterial, Material flameMaterial, Material smokeMaterial, float smokeTime, float smokeEndWidth,
            WeaponModel model = default, float puffTrail = 0f)
        {
            if (bodyMaterial == null && !model.IsSet)
                return null;
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(position, Quaternion.LookRotation(dir));

            if (model.IsSet)
            {
                var body = MeshObject("Body", root, model).transform;
                body.localScale = Vector3.one * model.ScaleForLength(length);
            }
            else
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.name = "Body";
                Object.DestroyImmediate(body.GetComponent<Collider>()); // never let a sweep hit it
                var r = body.GetComponent<Renderer>();
                r.sharedMaterial = bodyMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                body.transform.SetParent(root, false);
                body.transform.localScale = new Vector3(width, width, length);
            }

            if (flameMaterial != null)
                Trail("Exhaust", root, length, flameMaterial, 0.08f, 0.3f, width * 2f, 0f);
            bool puffs = puffTrail > 0f && Vfx.Attach(VfxKind.MissileTrail, root, new Vector3(0f, 0f, -length * 0.5f), puffTrail) != null;
            if (!puffs && smokeMaterial != null)
                Trail("Smoke", root, length, smokeMaterial, smokeTime, 1f, width * 1.5f, smokeEndWidth);
            return root;
        }

        /// <summary>A collider-less, shadowless renderer of <paramref name="model"/> under <paramref name="parent"/>.</summary>
        public static GameObject MeshObject(string name, Transform parent, WeaponModel model)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = model.mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = model.materials;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        /// <summary>
        /// A muzzle flash under <paramref name="muzzle"/>, hidden until <see cref="MuzzleFlash.Show"/>. Null without a model.
        /// </summary>
        public static MuzzleFlash CreateFlash(Transform muzzle, WeaponModel model, Vector3 localPosition, float size)
        {
            if (!model.IsSet || muzzle == null)
                return null;
            var go = MeshObject("Muzzle Flash", muzzle, model);
            go.transform.localPosition = localPosition;
            go.SetActive(false);
            return new MuzzleFlash(go.transform, size);
        }

        /// <summary>A muzzle flash that shows for a moment per shot, at a random roll and size so rapid fire flickers.</summary>
        public class MuzzleFlash
        {
            const float Duration = 0.045f;
            readonly Transform _transform;
            readonly float _size;
            float _timeLeft;

            public MuzzleFlash(Transform transform, float size)
            {
                _transform = transform;
                _size = size;
            }

            public void Show()
            {
                if (_transform == null)
                    return;
                _timeLeft = Duration;
                _transform.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));
                _transform.localScale = Vector3.one * (_size * UnityEngine.Random.Range(0.75f, 1.15f));
                _transform.gameObject.SetActive(true);
            }

            public void Tick(float dt)
            {
                if (_transform == null || _timeLeft <= 0f)
                    return;
                _timeLeft -= dt;
                if (_timeLeft <= 0f)
                    _transform.gameObject.SetActive(false);
            }

            public void Hide()
            {
                _timeLeft = 0f;
                if (_transform != null)
                    _transform.gameObject.SetActive(false);
            }
        }

        static void Trail(string name, Transform root, float length, Material material, float time, float minVertexDistance,
            float startWidth, float endWidth)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, 0f, -length * 0.5f);
            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = material;
            trail.time = time;
            trail.minVertexDistance = minVertexDistance;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, startWidth), new Keyframe(1f, endWidth));
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.autodestruct = false;
        }

        /// <summary>Removes the model but lets its trails hang for a moment instead of vanishing with it.</summary>
        public static void Release(Transform visual)
        {
            if (visual == null)
                return;
            foreach (var puffs in visual.GetComponentsInChildren<VfxEffect>())
                puffs.Stop();
            foreach (var trail in visual.GetComponentsInChildren<TrailRenderer>())
            {
                trail.transform.SetParent(null, true);
                trail.emitting = false;
                Object.Destroy(trail.gameObject, trail.time);
            }
            Object.Destroy(visual.gameObject);
        }
    }
}
