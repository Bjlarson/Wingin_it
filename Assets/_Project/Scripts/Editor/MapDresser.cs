using System;
using System.Collections.Generic;
using UnityEngine;
using WingingIt.Maps;
using WingingIt.World;
using Random = System.Random;

namespace WingingIt.Editor
{
    /// <summary>
    /// Places a map pack's pieces on a heightmap map's built terrain (<see cref="MapDressing"/> says what goes where):
    /// samples the ground, keeps everything off the layout's airfield pads and approach lanes and away from the
    /// landmarks, keeps pieces from overlapping, and scatters small props as terrain trees. <see cref="Finish"/> adds
    /// those trees, clears the terrain's own trees from under every footprint and marks the still pieces static.
    /// Deterministic from the map's seed.
    /// </summary>
    internal sealed class MapDresser
    {
        /// <summary>
        /// How far (m) pieces keep off the approach lanes and pads: tall ones (over ~20 m: stacks, pylons, steeples) further;
        /// low ones may stand nearer the lanes, which fly 50-80 m up.
        /// </summary>
        const float TallMargin = 160f, LowMargin = 30f, PadMargin = 120f, TallPadMargin = 260f, LandmarkMargin = 120f;

        public readonly MapDefinition Map;
        public readonly Terrain Terrain;
        public readonly Transform Group;
        public readonly Random Rng;

        readonly TerrainData _data;
        readonly Vector3 _origin;
        readonly List<(Vector2 a, Vector2 b)> _lanes = new();
        readonly List<Vector2> _landmarks = new();
        readonly List<(Vector2 xz, float radius)> _footprints = new();
        readonly List<(GameObject prefab, Vector2 xz, float scale)> _trees = new();
        readonly Dictionary<string, int> _counts = new();

        public MapDresser(MapDefinition map, Transform world, Terrain terrain, string groupName)
        {
            Map = map;
            Terrain = terrain;
            _data = terrain.terrainData;
            _origin = terrain.transform.position;
            Rng = new Random(map.seed * 31 + 7);
            Group = new GameObject(groupName).transform;
            Group.SetParent(world, false);

            foreach (var lane in map.layout.lanes)
            {
                var field = map.layout.Airfield(lane.airfield);
                for (int i = 0; i < lane.points.Length; i++)
                {
                    Vector2 next = i + 1 < lane.points.Length ? lane.points[i + 1].xz : field?.position ?? lane.points[i].xz;
                    _lanes.Add((lane.points[i].xz, next));
                }
            }
            foreach (var landmark in world.GetComponentsInChildren<Landmark>())
                _landmarks.Add(new Vector2(landmark.transform.position.x, landmark.transform.position.z));
        }

        // ------------------------------------------------------------------------------------------ the ground

        public float Ground(Vector2 xz) => Terrain.SampleHeight(new Vector3(xz.x, 0f, xz.y)) + _origin.y;

        /// <summary>Meters above the sea (negative: the depth of water over the seabed).</summary>
        public float AboveSea(Vector2 xz) => Ground(xz) - Map.seaLevel;

        Vector2 Normalized(Vector2 xz) => new((xz.x - _origin.x) / _data.size.x, (xz.y - _origin.z) / _data.size.z);

        public float Slope(Vector2 xz)
        {
            var n = Normalized(xz);
            return _data.GetSteepness(Mathf.Clamp01(n.x), Mathf.Clamp01(n.y));
        }

        public Vector3 Normal(Vector2 xz)
        {
            var n = Normalized(xz);
            return _data.GetInterpolatedNormal(Mathf.Clamp01(n.x), Mathf.Clamp01(n.y));
        }

        /// <summary>The ground's spread (highest minus lowest, m) over a disc of <paramref name="radius"/>.</summary>
        public float Roughness(Vector2 xz, float radius)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var p in Ring(xz, radius, 8, includeCentre: true))
            {
                float h = Ground(p);
                lo = Mathf.Min(lo, h);
                hi = Mathf.Max(hi, h);
            }
            return hi - lo;
        }

        /// <summary>The lowest ground over a disc of <paramref name="radius"/>: a building set there sinks into the slope, never floats.</summary>
        public float Lowest(Vector2 xz, float radius)
        {
            float lo = Ground(xz);
            foreach (var p in Ring(xz, radius, 8))
                lo = Mathf.Min(lo, Ground(p));
            return lo;
        }

        public static IEnumerable<Vector2> Ring(Vector2 c, float r, int n, bool includeCentre = false)
        {
            if (includeCentre)
                yield return c;
            for (int k = 0; k < n; k++)
            {
                float a = k * Mathf.PI * 2f / n;
                yield return c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
        }

        /// <summary>The direction (unit xz) from <paramref name="xz"/> toward the lowest ground <paramref name="reach"/> m away.</summary>
        public Vector2 Downhill(Vector2 xz, float reach)
        {
            Vector2 best = Vector2.up;
            float low = float.MaxValue;
            foreach (var p in Ring(xz, reach, 16))
            {
                float h = Ground(p);
                if (h < low)
                {
                    low = h;
                    best = (p - xz).normalized;
                }
            }
            return best;
        }

        /// <summary>True when water at least <paramref name="depth"/> deep is within <paramref name="reach"/> m.</summary>
        public bool NearWater(Vector2 xz, float reach, float depth = 1f)
        {
            for (float r = reach * 0.5f; r <= reach + 0.1f; r += reach * 0.5f)
                foreach (var p in Ring(xz, r, 12))
                    if (AboveSea(p) < -depth)
                        return true;
            return false;
        }

        /// <summary>True when ground above the sea is within <paramref name="reach"/> m.</summary>
        public bool NearLand(Vector2 xz, float reach)
        {
            for (float r = reach * 0.25f; r <= reach + 0.1f; r += reach * 0.25f)
                foreach (var p in Ring(xz, r, 12))
                    if (AboveSea(p) > 0f)
                        return true;
            return false;
        }

        public static float YawToward(Vector2 direction) => Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;

        public static Vector2 Rotate(Vector2 v, float yawDegrees)
        {
            float a = -yawDegrees * Mathf.Deg2Rad;
            return new Vector2(v.x * Mathf.Cos(a) - v.y * Mathf.Sin(a), v.x * Mathf.Sin(a) + v.y * Mathf.Cos(a));
        }

        public float Range(float a, float b) => a + (float)Rng.NextDouble() * (b - a);

        public T Pick<T>(IReadOnlyList<T> items) => items[Rng.Next(items.Count)];

        // ------------------------------------------------------------------------------------------ keeping clear

        static float PadOutside(AirfieldAnchor field, Vector2 p)
        {
            Vector3 l = Quaternion.Euler(0f, -field.yaw, 0f) * new Vector3(p.x - field.position.x, 0f, p.y - field.position.y);
            return Vector2.Max(new Vector2(Mathf.Abs(l.x), Mathf.Abs(l.z)) - MapTerrainBuilder.PadHalfSize, Vector2.zero).magnitude;
        }

        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>
        /// True when a piece of <paramref name="radius"/> at <paramref name="xz"/> keeps off every pad and lane (further
        /// when <paramref name="tall"/>), away from the landmarks, inside the boundary and clear of what's placed already.
        /// </summary>
        public bool Clear(Vector2 xz, float radius, bool tall, bool ignoreFootprints = false)
        {
            if (xz.magnitude > Map.boundaryRadius - radius - 120f)
                return false;
            var n = Normalized(xz);
            if (n.x < 0.02f || n.x > 0.98f || n.y < 0.02f || n.y > 0.98f)
                return false;
            foreach (var field in Map.layout.airfields)
                if (PadOutside(field, xz) < radius + (tall ? TallPadMargin : PadMargin))
                    return false;
            foreach (var (a, b) in _lanes)
                if (SegmentDistance(xz, a, b) < radius + (tall ? TallMargin : LowMargin))
                    return false;
            foreach (var l in _landmarks)
                if (Vector2.Distance(l, xz) < radius + LandmarkMargin)
                    return false;
            if (!ignoreFootprints)
                foreach (var (c, r) in _footprints)
                    if (Vector2.Distance(c, xz) < radius + r + 2f)
                        return false;
            return true;
        }

        /// <summary>
        /// Up to <paramref name="count"/> spots, <paramref name="spacing"/> m apart, each passing <paramref name="ok"/>
        /// (tried in turn over random points inside the boundary).
        /// </summary>
        public List<Vector2> Sites(int count, float spacing, Func<Vector2, bool> ok, int tries = 6000)
        {
            var sites = new List<Vector2>();
            float reach = Map.boundaryRadius - 150f;
            for (int i = 0; i < tries && sites.Count < count; i++)
            {
                var p = new Vector2(Range(-reach, reach), Range(-reach, reach));
                if (p.magnitude > reach || !sites.TrueForAll(s => Vector2.Distance(s, p) >= spacing) || !ok(p))
                    continue;
                sites.Add(p);
            }
            return sites;
        }

        // ------------------------------------------------------------------------------------------ placing

        public void Count(string name) => _counts[name] = _counts.TryGetValue(name, out int n) ? n + 1 : 1;

        public void Claim(Vector2 xz, float radius) => _footprints.Add((xz, radius));

        /// <summary>
        /// A static piece at <paramref name="xz"/> on the ground (its lowest point under the footprint, less
        /// <paramref name="sink"/>), turned <paramref name="yaw"/>, scaled; solid (a mesh collider) when asked. Claims its footprint.
        /// </summary>
        public GameObject Put(MapPackModels pack, string name, Vector2 xz, float yaw, float scale = 1f, bool solid = false,
            float sink = 0f, float? y = null, Transform parent = null)
        {
            float radius = pack.Radius(name) * scale;
            float ground = y ?? Lowest(xz, radius * 0.6f) - sink;
            var go = pack.Place(name, parent ?? Group, new Vector3(xz.x, ground, xz.y), Quaternion.Euler(0f, yaw, 0f),
                Vector3.one * scale, solid ? name : null);
            Claim(xz, radius);
            Count(name);
            return go;
        }

        /// <summary>A flat piece (a field, a pond) laid along the ground's slope at <paramref name="xz"/>, sunk <paramref name="sink"/>.</summary>
        public GameObject Lay(MapPackModels pack, string name, Vector2 xz, float yaw, float sink = 0.1f, float scale = 1f)
        {
            var rotation = Quaternion.FromToRotation(Vector3.up, Normal(xz)) * Quaternion.Euler(0f, yaw, 0f);
            var go = pack.Place(name, Group, new Vector3(xz.x, Ground(xz) - sink, xz.y), rotation, Vector3.one * scale, null,
                castShadows: false);
            Claim(xz, pack.Radius(name) * scale * 0.9f);
            Count(name);
            return go;
        }

        /// <summary>A piece that floats on the sea and bobs (<see cref="Bobber"/>), not static.</summary>
        public GameObject Float(MapPackModels pack, string name, Vector2 xz, float yaw, float heave = 0.35f, float rock = 3f)
        {
            var go = pack.Place(name, Group, new Vector3(xz.x, Map.seaLevel, xz.y), Quaternion.Euler(0f, yaw, 0f), Vector3.one,
                null);
            go.isStatic = false;
            go.AddComponent<Bobber>().Configure(heave, rock, Range(4f, 7f));
            Claim(xz, pack.Radius(name));
            Count(name);
            return go;
        }

        /// <summary>
        /// A rotor (sails, a wind wheel) on <paramref name="body"/>: modelled lying in its xz plane round its hub, stood
        /// up to face the body's +z and turned about its own y by a <see cref="Spinner"/>.
        /// </summary>
        public GameObject Rotor(MapPackModels pack, string rotorName, string bodyName, GameObject body, float speed)
        {
            var go = pack.Mounted(rotorName, body.transform, bodyName);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.isStatic = false;
            go.AddComponent<Spinner>().Configure(speed);
            return go;
        }

        /// <summary>
        /// A prop drawn by the terrain as a tree (no collider) at <paramref name="xz"/>, unless something's built
        /// there. Added at <see cref="Finish"/>.
        /// </summary>
        public bool Tree(MapPackModels pack, string name, Vector2 xz, float minScale = 0.85f, float maxScale = 1.2f)
        {
            float scale = Range(minScale, maxScale);
            foreach (var (c, r) in _footprints)
                if (Vector2.Distance(c, xz) < r + 2f)
                    return false;
            var n = Normalized(xz);
            if (n.x <= 0f || n.x >= 1f || n.y <= 0f || n.y >= 1f)
                return false;
            _trees.Add((pack.TreePrefab(name), xz, scale));
            Count(name);
            return true;
        }

        /// <summary>
        /// <paramref name="count"/> trees of <paramref name="kinds"/> round <paramref name="centre"/> within
        /// <paramref name="radius"/>, where <paramref name="ok"/> allows (and clear of the pads and lanes).
        /// </summary>
        public int Grove(MapPackModels pack, string[] kinds, Vector2 centre, float radius, int count, Func<Vector2, bool> ok,
            float minScale = 0.85f, float maxScale = 1.2f)
        {
            int planted = 0;
            for (int i = 0; i < count * 3 && planted < count; i++)
            {
                float a = Range(0f, Mathf.PI * 2f), r = radius * Mathf.Sqrt(Range(0f, 1f));
                var p = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (ok(p) && Clear(p, 3f, false, ignoreFootprints: true) && Tree(pack, kinds[Rng.Next(kinds.Length)], p, minScale, maxScale))
                    planted++;
            }
            return planted;
        }

        /// <summary>
        /// Adds the pack trees to the terrain's own, takes the terrain's trees out from under every footprint, marks
        /// still pieces static and logs what went where.
        /// </summary>
        public void Finish()
        {
            var prototypes = new List<TreePrototype>(_data.treePrototypes);
            var instances = new List<TreeInstance>();
            foreach (var tree in _data.treeInstances)
            {
                var xz = new Vector2(_origin.x + tree.position.x * _data.size.x, _origin.z + tree.position.z * _data.size.z);
                if (!_footprints.Exists(f => Vector2.Distance(f.xz, xz) < f.radius + 3f))
                    instances.Add(tree);
            }
            int removed = _data.treeInstances.Length - instances.Count;
            foreach (var (prefab, xz, scale) in _trees)
            {
                int index = prototypes.FindIndex(p => p.prefab == prefab);
                if (index < 0)
                {
                    prototypes.Add(new TreePrototype { prefab = prefab });
                    index = prototypes.Count - 1;
                }
                var n = Normalized(xz);
                instances.Add(new TreeInstance
                {
                    position = new Vector3(n.x, 0f, n.y),
                    prototypeIndex = index,
                    widthScale = scale,
                    heightScale = scale * Range(0.92f, 1.08f),
                    rotation = Range(0f, Mathf.PI * 2f),
                    color = Color.white,
                    lightmapColor = Color.white,
                });
            }
            _data.treePrototypes = prototypes.ToArray();
            _data.SetTreeInstances(instances.ToArray(), snapToHeightmap: true);
            UnityEditor.EditorUtility.SetDirty(_data);

            foreach (var t in Group.GetComponentsInChildren<Transform>())
                t.gameObject.isStatic = t.GetComponentInParent<Bobber>() == null && t.GetComponent<Spinner>() == null
                    && t.GetComponentInParent<CableCar>() == null;

            var summary = new System.Text.StringBuilder();
            foreach (var kv in _counts)
                summary.Append($" {kv.Key} {kv.Value},");
            Debug.Log($"[Winging It] Dressed {Map.name}: {removed} terrain trees cleared;{summary.ToString().TrimEnd(',')}");
        }
    }
}
