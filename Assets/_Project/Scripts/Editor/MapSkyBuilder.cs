using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WingingIt.Maps;
using WingingIt.World;
using B = WingingIt.Editor.FlightTestSceneBuilder;

namespace WingingIt.Editor
{
    /// <summary>
    /// The sky for any map, from its <see cref="MapSky"/>: sun, fog and skybox on the open scene, and the puffy cloud
    /// banks, raised clear of whatever stands under them (<see cref="HighestPointNear"/>).
    /// </summary>
    internal static class MapSkyBuilder
    {
        const string SkyPath = B.MaterialsFolder + "/Greybox_Sky.mat";

        /// <summary>Sun, fog and skybox of the open scene.</summary>
        public static void ApplyAtmosphere(MapSky sky)
        {
            B.SetUpAtmosphere(sky.fogStart, sky.fogEnd);
            var sun = RenderSettings.sun;
            sun.transform.rotation = Quaternion.Euler(sky.sunAngles.x, sky.sunAngles.y, 0f);
            sun.intensity = sky.sunIntensity;
            RenderSettings.fogColor = sky.fogColor;
            RenderSettings.skybox = sky.skybox != null ? sky.skybox : GreyboxSky();
        }

        /// <summary>The procedural sky with its lower half the fog color, so the sea fades into it with no seam at the horizon.</summary>
        internal static Material GreyboxSky()
        {
            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
            if (sky != null)
                return sky;
            sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetColor("_GroundColor", RenderSettings.fogColor);
            sky.SetFloat("_AtmosphereThickness", 0.9f);
            AssetDatabase.CreateAsset(sky, SkyPath);
            return sky;
        }

        /// <summary>
        /// Puffy cloud banks (<see cref="CloudBank"/>): an ellipsoid core with puffs around and on top of it, rendered as
        /// collider-less white spheres that match the cover volume. With <see cref="MapSky.mirrorClouds"/>, each bank and
        /// its mirror share one altitude (the higher of the two needed to clear the ground), so both teams' clouds are the
        /// same. Needs the ground's colliders in the scene: the clearance is found by raycasting down.
        /// </summary>
        public static Transform BuildClouds(Transform parent, MapSky sky, NatureModels nature)
        {
            var group = new GameObject("Clouds").transform;
            group.SetParent(parent, false);
            var mat = B.Materials.Mat("Greybox_Cloud", new Color(0.97f, 0.98f, 1f), smoothness: 0f);
            int n = 0;
            foreach (var bank in sky.clouds)
            {
                var puffs = CloudPuffs(bank.radius, bank.seed, sky.cloudCoreHalfHeight);
                float underside = 0f;
                foreach (var p in puffs)
                    underside = Mathf.Max(underside, p.radii.y - p.center.y);
                float y = bank.altitude;
                foreach (var at in sky.mirrorClouds ? new[] { bank.xz, -bank.xz } : new[] { bank.xz })
                    y = Mathf.Max(y, HighestPointNear(at, bank.radius) + sky.cloudClearance + underside);

                BuildCloudBank(group, $"Cloud {++n}", new Vector3(bank.xz.x, y, bank.xz.y), puffs, mat, nature);
                if (!sky.mirrorClouds)
                    continue;
                var mirrored = new CloudPuff[puffs.Length];
                for (int i = 0; i < puffs.Length; i++)
                    mirrored[i] = new CloudPuff(new Vector3(-puffs[i].center.x, puffs[i].center.y, -puffs[i].center.z), puffs[i].radii);
                BuildCloudBank(group, $"Cloud {++n}", new Vector3(-bank.xz.x, y, -bank.xz.y), mirrored, mat, nature);
            }
            return group;
        }

        /// <summary>One bank's puffs, relative to its center: a big flat core, a ring of side puffs, and two on top.</summary>
        static CloudPuff[] CloudPuffs(float radius, int seed, float h)
        {
            var rng = new System.Random(seed);
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var puffs = new List<CloudPuff> { new(Vector3.zero, new Vector3(radius * 0.6f, h, radius * 0.6f)) };
            int ring = 6 + rng.Next(3);
            float start = Range(0f, Mathf.PI * 2f);
            for (int i = 0; i < ring; i++)
            {
                float a = start + i * Mathf.PI * 2f / ring + Range(-0.25f, 0.25f);
                float d = radius * Range(0.45f, 0.65f);
                float r = radius * Range(0.3f, 0.42f);
                puffs.Add(new CloudPuff(new Vector3(Mathf.Cos(a) * d, h * Range(-0.25f, 0.15f), Mathf.Sin(a) * d),
                    new Vector3(r, h * Range(0.6f, 0.85f), r * Range(0.85f, 1.15f))));
            }
            for (int i = 0; i < 2; i++)
            {
                float a = Range(0f, Mathf.PI * 2f);
                float d = radius * Range(0.1f, 0.3f);
                float r = radius * Range(0.3f, 0.4f);
                puffs.Add(new CloudPuff(new Vector3(Mathf.Cos(a) * d, h * Range(0.45f, 0.65f), Mathf.Sin(a) * d),
                    new Vector3(r, h * Range(0.55f, 0.7f), r)));
            }
            return puffs.ToArray();
        }

        /// <summary>
        /// One bank: a faceted puff mesh per cover puff (the nature pack's unit-radius CloudPuffA-C, scaled to its radii),
        /// or a greybox sphere when the pack hasn't been generated.
        /// </summary>
        static void BuildCloudBank(Transform parent, string name, Vector3 position, CloudPuff[] puffs, Material mat, NatureModels nature)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.position = position;
            for (int i = 0; i < puffs.Length; i++)
            {
                Renderer r;
                if (nature != null)
                {
                    var go = nature.Place($"CloudPuff{"ABC"[i % 3]}", root, position + puffs[i].center, collider: false, castShadows: false);
                    go.name = $"Puff {i + 1}";
                    go.transform.localScale = puffs[i].radii;
                    r = go.GetComponent<Renderer>();
                    r.sharedMaterial = nature.CloudMaterial; // unlit: the baked tones are the shading, so no grey undersides
                }
                else
                {
                    r = B.Sphere($"Puff {i + 1}", root, puffs[i].center, 1f, mat, false);
                    r.transform.localScale = puffs[i].radii * 2f;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                r.receiveShadows = false;
            }
            root.gameObject.AddComponent<CloudBank>().Configure(puffs);
            foreach (var t in root.GetComponentsInChildren<Transform>())
                t.gameObject.isStatic = true;
        }

        /// <summary>The highest solid surface (ground, sea, building) at <paramref name="xz"/> or within <paramref name="radius"/> of it.</summary>
        internal static float HighestPointNear(Vector2 xz, float radius)
        {
            float best = 0f;
            for (int ring = 0; ring <= 2; ring++)
            {
                float r = radius * ring / 2f;
                int samples = ring == 0 ? 1 : 12;
                for (int k = 0; k < samples; k++)
                {
                    float a = k * Mathf.PI * 2f / samples;
                    var from = new Vector3(xz.x + Mathf.Cos(a) * r, 2000f, xz.y + Mathf.Sin(a) * r);
                    if (Physics.Raycast(from, Vector3.down, out var hit, 3000f, ~0, QueryTriggerInteraction.Ignore))
                        best = Mathf.Max(best, hit.point.y);
                }
            }
            return best;
        }
    }
}
