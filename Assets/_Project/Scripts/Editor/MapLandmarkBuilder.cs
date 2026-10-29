using UnityEngine;
using WingingIt.World;
using B = WingingIt.Editor.FlightTestSceneBuilder;

namespace WingingIt.Editor
{
    /// <summary>
    /// Places the landmarks pack's landmarks on any map, each with a blinking beacon on top (<see cref="Landmark"/>)
    /// naming the region it marks: the Spire, the summit radar, the Pyramid and the lighthouse on its islet. Greybox
    /// stand-ins when the pack hasn't been generated. Make one, place what the map has, then <see cref="Finish"/>.
    /// </summary>
    internal sealed class MapLandmarkBuilder
    {
        /// <summary>The Pyramid's footprint and steps (the pack's model and the greybox are the same size).</summary>
        public const float PyramidBase = 280f, PyramidStepHeight = 26f;
        public const int PyramidSteps = 8;

        readonly Transform _group;
        readonly B.Materials _m;
        readonly NatureModels _nature;
        readonly LandmarkModels _models;
        readonly Material _beacon;

        /// <param name="models">The landmarks pack, or null for greybox.</param>
        /// <param name="nature">The nature pack (the lighthouse's islet), or null.</param>
        public MapLandmarkBuilder(Transform parent, B.Materials m, NatureModels nature, LandmarkModels models)
        {
            _group = new GameObject("Landmarks").transform;
            _group.SetParent(parent, false);
            _m = m;
            _nature = nature;
            _models = models;
            _beacon = B.Materials.UnlitMat("Greybox_Beacon", new Color(1f, 0.15f, 0.1f));
        }

        /// <summary>Marks them static, all but the parts that turn (the radar antenna and the lighthouse lens).</summary>
        public void Finish()
        {
            foreach (var t in _group.GetComponentsInChildren<Transform>())
                t.gameObject.isStatic = t.GetComponent<Spinner>() == null;
        }

        /// <summary>A landmark's root, on the ground at <paramref name="ground"/>.</summary>
        Transform NewLandmark(string name, Vector3 ground)
        {
            var root = new GameObject(name).transform;
            root.SetParent(_group, false);
            root.position = ground;
            return root;
        }

        /// <summary>Makes <paramref name="root"/> a <see cref="Landmark"/>: a top marker at <paramref name="top"/> and the beacon it blinks.</summary>
        static void Light(Transform root, string region, Vector3 top, Renderer beacon)
        {
            var marker = new GameObject("Top").transform;
            marker.SetParent(root, false);
            marker.localPosition = top;
            root.gameObject.AddComponent<Landmark>().Configure(root.name, region, marker, beacon);
        }

        /// <summary>A collider-less beacon <paramref name="size"/> across at <paramref name="localPos"/>: the pack's gem, or a greybox sphere.</summary>
        Renderer Beacon(Transform root, Vector3 localPos, float size) =>
            _models != null ? _models.Beacon(root, localPos, size) : B.Sphere("Beacon", root, localPos, size, _beacon, false);

        /// <summary>The highest ground on <paramref name="terrain"/> inside a rectangle, sampled every 10 m.</summary>
        public static Vector3 HighestPoint(Terrain terrain, Vector2 center, Vector2 halfSize)
        {
            Vector3 peak = Vector3.zero;
            float best = float.MinValue;
            for (float z = center.y - halfSize.y; z <= center.y + halfSize.y; z += 10f)
            for (float x = center.x - halfSize.x; x <= center.x + halfSize.x; x += 10f)
            {
                float h = terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
                if (h > best)
                {
                    best = h;
                    peak = new Vector3(x, h, z);
                }
            }
            return peak;
        }

        /// <summary>A city's landmark: a ~540 m stepped needle tower, standing on a plaza at <paramref name="ground"/>.</summary>
        public Transform Spire(Vector3 ground, string region)
        {
            var root = NewLandmark("The Spire", ground);
            var top = new Vector3(0f, 545f, 0f);
            if (_models != null)
            {
                _models.Body("Spire", root);
                top = _models.Top("Spire");
            }
            else
            {
                var glass = B.Materials.Mat("Greybox_SpireGlass", new Color(0.42f, 0.56f, 0.7f), smoothness: 0.8f);
                B.Box("Podium", root, new Vector3(0f, 6f, 0f), new Vector3(70f, 12f, 70f), _m.ConcreteDark, true);
                float y = 12f;
                foreach (var (width, height) in new[] { (44f, 220f), (34f, 120f), (24f, 80f), (14f, 40f) })
                {
                    B.Box("Shaft", root, new Vector3(0f, y + height * 0.5f, 0f), new Vector3(width, height, width), glass, true);
                    y += height;
                    B.Box("Band", root, new Vector3(0f, y - 2f, 0f), new Vector3(width + 4f, 4f, width + 4f), _m.Concrete, true);
                }
                // Observation deck above the first stage: the widest part, so the silhouette reads as a tower, not a box.
                B.Box("Observation Deck", root, new Vector3(0f, 236f, 0f), new Vector3(62f, 8f, 62f), _m.Concrete, true);
                B.Box("Needle", root, new Vector3(0f, y + 35f, 0f), new Vector3(2.5f, 70f, 2.5f), _m.Concrete, true);
            }
            Light(root, region, top, Beacon(root, top, 9f));
            return root;
        }

        /// <summary>A mountain landmark: a radar station (mast + dome) on <paramref name="peak"/>, on a plinth sunk into the rock.</summary>
        public Transform SummitRadar(Vector3 peak, string region)
        {
            var root = NewLandmark("Summit Radar", peak);
            var top = new Vector3(6f, 156f, -6f);
            if (_models != null)
            {
                _models.Body("SummitRadar", root);
                _models.RadarAntenna(root);
                top = _models.Top("SummitRadar");
            }
            else
            {
                var white = B.Materials.Mat("Greybox_RadarWhite", new Color(0.93f, 0.93f, 0.95f));
                B.Box("Plinth", root, new Vector3(0f, -12f, 0f), new Vector3(44f, 30f, 44f), _m.ConcreteDark, true);
                B.Sphere("Radome", root, new Vector3(-10f, 14f, 6f), 24f, white, true);
                B.Box("Mast", root, new Vector3(6f, 78f, -6f), new Vector3(5f, 150f, 5f), _m.PlaneDark, true);
                for (float h = 40f; h <= 140f; h += 50f)
                    B.Box("Mast Arm", root, new Vector3(6f, h, -6f), new Vector3(18f, 1.5f, 1.5f), _m.PlaneDark, true);
            }
            Light(root, region, top, Beacon(root, top, 7f));
            return root;
        }

        /// <summary>
        /// A desert landmark: a stepped pyramid, 280 m across and ~210 m tall, at <paramref name="xz"/>. It stands on the
        /// lowest ground under its footprint with the bottom step sunk in, so no edge floats.
        /// </summary>
        public Transform Pyramid(Terrain terrain, Vector2 xz, string region)
        {
            float ground = float.MaxValue;
            for (float dz = -PyramidBase * 0.5f; dz <= PyramidBase * 0.5f; dz += 20f)
            for (float dx = -PyramidBase * 0.5f; dx <= PyramidBase * 0.5f; dx += 20f)
                ground = Mathf.Min(ground, terrain.SampleHeight(new Vector3(xz.x + dx, 0f, xz.y + dz)) + terrain.transform.position.y);

            float height = PyramidSteps * PyramidStepHeight;
            var root = NewLandmark("The Pyramid", new Vector3(xz.x, ground, xz.y));
            var top = new Vector3(0f, height + 6f, 0f);
            if (_models != null)
            {
                _models.Body("Pyramid", root);
                top = _models.Top("Pyramid");
            }
            else
            {
                var stone = B.Materials.Mat("Greybox_Sandstone", new Color(0.8f, 0.66f, 0.45f), B.Materials.CheckerTexture(), new Vector2(4f, 2f));
                const float sink = 12f;
                for (int i = 0; i < PyramidSteps; i++)
                {
                    float width = PyramidBase * (1f - i / (float)PyramidSteps);
                    float bottom = i == 0 ? -sink : i * PyramidStepHeight;
                    float stepTop = (i + 1) * PyramidStepHeight;
                    B.Box($"Step {i + 1}", root, new Vector3(0f, (bottom + stepTop) * 0.5f, 0f), new Vector3(width, stepTop - bottom, width), stone, true);
                }
            }
            Light(root, region, top, Beacon(root, top, 8f));
            return root;
        }

        /// <summary>
        /// A sea landmark: a striped lighthouse on a rocky islet at <paramref name="xz"/>. Its lamp is the beacon (the
        /// pack's lens turns round it). The islet is the nature pack's cliff mesh (with a matching collider), its grassy
        /// top at <paramref name="isletHeight"/>, level with the lighthouse's foot: the map's ground under it should be a
        /// mound just inside the mesh.
        /// </summary>
        public Transform Lighthouse(Vector2 xz, float isletHeight, string region)
        {
            var root = NewLandmark("Lighthouse Rock", new Vector3(xz.x, isletHeight, xz.y));
            if (_models != null)
            {
                _models.Body("Lighthouse", root);
                _models.LighthouseLens(root);
                var lamp = _models.LighthouseLamp(root);
                Light(root, region, lamp.transform.localPosition, lamp);
            }
            else
            {
                var red = B.Materials.Mat("Greybox_LighthouseRed", new Color(0.78f, 0.16f, 0.14f));
                B.Box("Base", root, new Vector3(0f, -2f, 0f), new Vector3(20f, 8f, 20f), _m.ConcreteDark, true);
                float y = 2f;
                for (int i = 0; i < 4; i++)
                {
                    float width = 13f - i * 1.6f;
                    B.Box($"Tower {i + 1}", root, new Vector3(0f, y + 7.5f, 0f), new Vector3(width, 15f, width), i % 2 == 0 ? _m.Concrete : red, true);
                    y += 15f;
                }
                B.Box("Gallery", root, new Vector3(0f, y + 1f, 0f), new Vector3(12f, 2f, 12f), _m.PlaneDark, true);
                B.Box("Lamp Room", root, new Vector3(0f, y + 6f, 0f), new Vector3(6f, 8f, 6f), _m.Glass, true);
                B.Box("Roof", root, new Vector3(0f, y + 11f, 0f), new Vector3(7f, 2f, 7f), red, true);
                B.Box("Keeper's House", root, new Vector3(16f, 3f, 6f), new Vector3(12f, 8f, 9f), _m.Concrete, true);
                var lamp = B.Materials.UnlitMat("Greybox_LighthouseLamp", new Color(1f, 0.92f, 0.55f));
                var top = new Vector3(0f, y + 6f, 0f);
                Light(root, region, top, B.Sphere("Beacon", root, top, 6f, lamp, false));
            }
            _nature?.Place("Islet", root, new Vector3(xz.x, 0f, xz.y), collider: true);
            return root;
        }
    }
}
