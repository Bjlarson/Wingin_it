using UnityEngine;
using UnityEngine.Rendering;

namespace WingingIt.Flight
{
    /// <summary>Placeholder rocket and missile models: a box body with a short exhaust flame and a smoke trail.</summary>
    public static class ProjectileVisuals
    {
        public static Transform Create(string name, Transform parent, Vector3 position, Vector3 dir, float length, float width,
            Material bodyMaterial, Material flameMaterial, Material smokeMaterial, float smokeTime, float smokeEndWidth)
        {
            if (bodyMaterial == null)
                return null;
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(position, Quaternion.LookRotation(dir));

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>()); // never let a sweep hit it
            var r = body.GetComponent<Renderer>();
            r.sharedMaterial = bodyMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            body.transform.SetParent(root, false);
            body.transform.localScale = new Vector3(width, width, length);

            if (flameMaterial != null)
                Trail("Exhaust", root, length, flameMaterial, 0.08f, 0.3f, width * 2f, 0f);
            if (smokeMaterial != null)
                Trail("Smoke", root, length, smokeMaterial, smokeTime, 1f, width * 1.5f, smokeEndWidth);
            return root;
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
