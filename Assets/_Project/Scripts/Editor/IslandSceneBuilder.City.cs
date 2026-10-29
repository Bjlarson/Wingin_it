using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using B = WingingIt.Editor.FlightTestSceneBuilder;

namespace WingingIt.Editor
{
    /// <summary>
    /// The Island's city: a downtown grid with its plazas and streets, the canal cut north to the sea with its quays
    /// and boats, and the two bridges over it with their ramps down to the streets.
    /// </summary>
    public static partial class IslandSceneBuilder
    {
        // ------------------------------------------------------------------ city

        const float CityPitch = 120f, CityBlockSize = 90f;

        /// <summary>
        /// A downtown grid: 120 m blocks with 30 m streets, towers tallest in the middle (~300 m) falling to ~40 m at
        /// the edge, some plazas left open, nothing on the canal banks or the central airfield's pad. With the city pack
        /// each lot gets one of its buildings (<see cref="CityModels.PlaceBuilding"/>), each block a sidewalk with
        /// lamps, each open block a plaza with trees, and the streets roads and traffic (<see cref="BuildStreets"/>);
        /// without it, boxes. No building stands in the way of a bridge or its ramps (<paramref name="bridges"/>).
        /// </summary>
        static void BuildCity(Transform parent, B.Materials m, CityModels city, NatureModels nature, List<Footprint> bridges)
        {
            var group = new GameObject("City").transform;
            group.SetParent(parent, false);
            var rng = new System.Random(4242);
            var pick = new System.Random(4343); // which building stands on a lot: its own stream, so the lots don't move
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            const float half = CityBlockSize * 0.5f;
            int n = 0, skipped = 0;
            for (float bz = CityCenter.y - CityRadius; bz <= CityCenter.y + CityRadius; bz += CityPitch)
            for (float bx = CityCenter.x - CityRadius; bx <= CityCenter.x + CityRadius; bx += CityPitch)
            {
                // Blocks on a grid offset so the center falls on an intersection.
                float cx = bx + CityPitch * 0.5f, cz = bz + CityPitch * 0.5f;
                float r = Vector2.Distance(new Vector2(cx, cz), CityCenter);
                if (r > CityRadius - 40f || CanalDistance(cx, cz) < 125f || PadMask(cx, cz) > 0.01f)
                    continue;
                var center = new Vector3(cx, LandHeight, cz);
                bool paved = city != null && IsFlat(cx, cz, half, half);
                if (rng.NextDouble() < 0.1) // left open: a square
                {
                    if (paved)
                        BuildPlaza(group, city, nature, center, pick);
                    continue;
                }
                if (paved)
                    city.Place("CityBlock", group, center, collider: false).name = "Sidewalk";
                if (Vector2.Distance(new Vector2(cx, cz), SpirePosition) < 100f) // the Spire's plaza
                    continue;

                float core = Mathf.Lerp(300f, 40f, Smooth(0f, CityRadius, r));
                var lots = new List<(Vector2 center, float size)>();
                if (rng.NextDouble() < 0.4)
                    lots.Add((new Vector2(cx, cz), Range(45f, 75f)));
                else
                    foreach (float ox in new[] { -22.5f, 22.5f })
                    foreach (float oz in new[] { -22.5f, 22.5f })
                        if (rng.NextDouble() < 0.75)
                            lots.Add((new Vector2(cx + ox, cz + oz), Range(26f, 40f)));

                foreach (var (lot, size) in lots)
                {
                    float h = Mathf.Max(20f, core * Range(0.45f, 1.05f) * (size < 42f ? 0.7f : 1f));
                    float w = size, d = Mathf.Min(CityBlockSize, size * Range(0.8f, 1.2f));
                    if (Footprint.Any(bridges, lot, new Vector2(w, d) * 0.5f, 1f))
                    {
                        skipped++;
                        continue;
                    }
                    if (city != null)
                    {
                        var building = city.PlaceBuilding(group, new Vector3(lot.x, LandHeight, lot.y), w, d, h, pick);
                        building.name = $"Building {n++:000} ({building.name})";
                        continue;
                    }
                    B.Box($"Building {n++:000}", group, new Vector3(lot.x, LandHeight + h * 0.5f, lot.y),
                        new Vector3(w, h, d), rng.NextDouble() < 0.5 ? m.Concrete : m.ConcreteDark, true).gameObject.isStatic = true;
                }
            }
            if (skipped > 0)
                Debug.Log($"[Winging It] City: {skipped} lots left empty for the bridges and their ramps");
            if (city != null)
                BuildStreets(group, city, bridges);
        }

        /// <summary>
        /// Whether the ground is dead level at <see cref="LandHeight"/> over a <paramref name="halfX"/> by
        /// <paramref name="halfZ"/> rectangle (and a heightmap cell round it), so a sidewalk or road laid there doesn't
        /// float or sink into a bump: not on the city's ragged edge, a canal bank's cut or a hill.
        /// </summary>
        static bool IsFlat(float x, float z, float halfX, float halfZ)
        {
            const float margin = 6f, tolerance = 0.2f;
            for (int i = -2; i <= 2; i++)
            for (int j = -2; j <= 2; j++)
                if (Mathf.Abs(Height(x + i * (halfX + margin) * 0.5f, z + j * (halfZ + margin) * 0.5f) - LandHeight) > tolerance)
                    return false;
            return true;
        }

        /// <summary>An open block: the pack's plaza (lawns, a fountain) with a pair of oaks on each lawn.</summary>
        static void BuildPlaza(Transform parent, CityModels city, NatureModels nature, Vector3 center, System.Random rng)
        {
            city.Place("Plaza", parent, center, collider: false);
            if (nature == null)
                return;
            foreach (float sx in new[] { -23f, 23f })
            foreach (float sz in new[] { -23f, 23f })
                for (int k = 0; k < 2; k++)
                {
                    var offset = new Vector3(sx + (k == 0 ? -6f : 6f), 0.35f, sz + (k == 0 ? 5f : -5f));
                    var tree = nature.Place(rng.NextDouble() < 0.5 ? "TreeOak" : "TreeOakTall", parent, center + offset, collider: false);
                    tree.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    tree.transform.localScale = Vector3.one * (1.6f + (float)rng.NextDouble() * 0.4f); // toy-sized, like the island's
                }
        }

        /// <summary>
        /// Roads on every street between blocks, and a crossing wherever two meet, where the ground is flat (so not on
        /// the city's edge, a pad or past the canal's promenade), with traffic stopped in their lanes (none under a
        /// bridge ramp).
        /// </summary>
        static void BuildStreets(Transform parent, CityModels city, List<Footprint> bridges)
        {
            var group = new GameObject("Streets").transform;
            group.SetParent(parent, false);
            var rng = new System.Random(4444);
            // Streets run down the gaps between blocks: x0 + i * pitch, z0 + j * pitch.
            float x0 = CityCenter.x - CityRadius, z0 = CityCenter.y - CityRadius;
            int lines = Mathf.FloorToInt(2f * CityRadius / CityPitch);
            const float roadHalfLength = (CityPitch - 30f) * 0.5f, roadHalfWidth = 15f;
            var crossings = new HashSet<Vector2Int>();
            for (int i = 1; i <= lines; i++)
            for (int j = 1; j <= lines; j++)
                foreach (bool alongX in new[] { true, false })
                {
                    // The road east of crossing (i, j), or north of it.
                    Vector2 mid = alongX
                        ? new Vector2(x0 + i * CityPitch + CityPitch * 0.5f, z0 + j * CityPitch)
                        : new Vector2(x0 + i * CityPitch, z0 + j * CityPitch + CityPitch * 0.5f);
                    if (!IsFlat(mid.x, mid.y, alongX ? roadHalfLength : roadHalfWidth, alongX ? roadHalfWidth : roadHalfLength)
                        || PadMask(mid.x, mid.y) > 0.01f)
                        continue;
                    float yaw = alongX ? 90f : 0f;
                    var rotation = Quaternion.Euler(0f, yaw, 0f);
                    var center = new Vector3(mid.x, LandHeight, mid.y);
                    city.Place("Road", group, center, rotation, Vector3.one, null, castShadows: false);
                    crossings.Add(new Vector2Int(i, j));
                    crossings.Add(alongX ? new Vector2Int(i + 1, j) : new Vector2Int(i, j + 1));

                    // Traffic: up to three vehicles in the four lanes (right-hand: the +x lanes head +z), never two in a slot.
                    var slots = new List<int>();
                    for (int c = rng.Next(4); c > 0; c--)
                    {
                        int slot = rng.Next(20);
                        if (slots.Contains(slot))
                            continue;
                        slots.Add(slot);
                        float lane = (slot / 5) switch { 0 => -10f, 1 => -4.5f, 2 => 4.5f, _ => 10f };
                        var local = new Vector3(lane, 0.1f, -30f + (slot % 5) * 15f);
                        string kind = rng.NextDouble() < 0.12 ? "Bus" : CityModels.Cars[rng.Next(3)];
                        Vector3 at = center + rotation * local;
                        if (!Footprint.Any(bridges, new Vector2(at.x, at.z), Vector2.zero, kind == "Bus" ? 8f : 4f))
                            city.PlaceVehicle(kind, group, at, yaw + (lane > 0f ? 0f : 180f));
                    }
                }
            foreach (var c in crossings)
            {
                var p = new Vector2(x0 + c.x * CityPitch, z0 + c.y * CityPitch);
                if (IsFlat(p.x, p.y, roadHalfWidth, roadHalfWidth))
                    city.Place("Crossing", group, new Vector3(p.x, LandHeight, p.y), Quaternion.identity, Vector3.one, null, castShadows: false);
            }
        }

        /// <summary>The city pack's Bridge (build_city.py): deck top over the water, half its length, half its width.</summary>
        const float BridgeDeckTop = 35.5f, BridgeHalfSpan = 115f, BridgeHalfWidth = 12f;
        /// <summary>
        /// The approach ramps: each runs on from a deck end and is back on the street's middle line within
        /// <see cref="RampSwing"/> m (the bridges are square to the canal, so their ends sit off the street), then comes
        /// down at an even grade, easing in and out over <see cref="RampEase"/> m, to touch down at least
        /// <see cref="RampMinRun"/> m out and <see cref="RampCrossingGap"/> m short of a crossing.
        /// </summary>
        const float RampMinRun = 210f, RampSwing = 90f, RampEase = 40f, RampCrossingGap = 12f;
        /// <summary>Ramp deck (the bridge's section): half width at the top, depth, and how far its end sits above the road.</summary>
        const float RampHalfWidth = 9f, RampDepth = 3f, RampEndLift = 0.25f;
        const float RampPierSpacing = 24f;

        /// <summary>Ground a bridge or ramp covers: its middle line and half width. Nothing is built or parked there.</summary>
        readonly struct Footprint
        {
            readonly Vector2[] _line;
            readonly float _halfWidth;

            public Footprint(Vector2[] line, float halfWidth)
            {
                _line = line;
                _halfWidth = halfWidth;
            }

            /// <summary>Whether it comes within <paramref name="margin"/> of a rectangle (its middle and half size).</summary>
            public bool Overlaps(Vector2 center, Vector2 half, float margin)
            {
                for (int i = 0; i < _line.Length - 1; i++)
                {
                    Vector2 a = _line[i], b = _line[i + 1];
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 2f));
                    for (int k = 0; k <= steps; k++)
                    {
                        Vector2 p = Vector2.Lerp(a, b, k / (float)steps) - center;
                        Vector2 outside = Vector2.Max(new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - half, Vector2.zero);
                        if (outside.magnitude < _halfWidth + margin)
                            return true;
                    }
                }
                return false;
            }

            public static bool Any(List<Footprint> footprints, Vector2 center, Vector2 half, float margin)
            {
                foreach (var f in footprints)
                    if (f.Overlaps(center, half, margin))
                        return true;
                return false;
            }
        }

        /// <summary>
        /// Two bridges over the canal, 34 m above the water, with 150 m between their towers to fly under: the city
        /// pack's cable-stayed bridge square across the canal with a ramp down to the street at each end
        /// (<see cref="BuildRamp"/>), or greybox boxes without it. Returns the ground they cover.
        /// </summary>
        static List<Footprint> BuildBridges(Transform parent, B.Materials m, CityModels city)
        {
            var group = new GameObject("Bridges").transform;
            group.SetParent(parent, false);
            var footprints = new List<Footprint>();
            var rampMeshes = new List<Mesh>();
            for (int i = 0; i < BridgeZ.Length; i++)
            {
                float z = BridgeZ[i], x = CanalX(z);
                if (city != null)
                {
                    // The deck runs along local x: turn it square to the canal.
                    float yaw = Mathf.Atan(CanalSlope(z)) * Mathf.Rad2Deg;
                    var rotation = Quaternion.Euler(0f, yaw, 0f);
                    var placed = city.Place("Bridge", group, new Vector3(x, 0f, z), rotation, Vector3.one, "BridgeCol");
                    placed.name = $"Bridge {i + 1}";
                    Vector3 axis = rotation * Vector3.right;
                    var origin = new Vector2(x, z);
                    var end = new Vector2(axis.x, axis.z) * BridgeHalfSpan;
                    footprints.Add(new Footprint(new[] { origin - end, origin + end }, BridgeHalfWidth));
                    foreach (float side in new[] { 1f, -1f })
                        footprints.Add(BuildRamp(placed.transform, $"Ramp {(side > 0f ? "East" : "West")}", origin + end * side,
                            new Vector2(axis.x, axis.z) * side, z, rampMeshes));
                    continue;
                }
                var bridge = new GameObject($"Bridge {i + 1}").transform;
                bridge.SetParent(group, false);
                bridge.localPosition = new Vector3(x, 0f, z);
                const float deck = 34f, span = 230f;
                B.Box("Deck", bridge, new Vector3(0f, deck, 0f), new Vector3(span, 3f, 18f), m.ConcreteDark, true);
                foreach (float side in new[] { -1f, 1f })
                {
                    B.Box("Tower", bridge, new Vector3(side * 75f, 45f, 0f), new Vector3(6f, 90f, 6f), m.Concrete, true);
                    B.Box("Ramp Pier", bridge, new Vector3(side * (span * 0.5f - 6f), deck * 0.5f, 0f), new Vector3(8f, deck, 14f), m.Concrete, true);
                    // Cables from the tower top to the deck ends and middle, for the silhouette (no collision).
                    foreach (float toward in new[] { 0f, side * span * 0.5f })
                    {
                        Vector3 top = new Vector3(side * 75f, 88f, 0f), end = new Vector3(toward, deck + 1.5f, 0f);
                        var cable = B.Box("Cable", bridge, (top + end) * 0.5f, new Vector3(0.6f, 0.6f, (end - top).magnitude), m.PlaneDark, false);
                        cable.transform.localRotation = Quaternion.LookRotation(end - top);
                    }
                }
                foreach (var t in bridge.GetComponentsInChildren<Transform>())
                    t.gameObject.isStatic = true;
            }
            SaveMeshes(RampMeshesPath, rampMeshes);
            return footprints;
        }

        const string RampMeshesPath = TerrainFolder + "/BridgeRamps.asset";

        /// <summary>
        /// One approach ramp, from a bridge deck's end (<paramref name="start"/>, heading <paramref name="heading"/>) down
        /// to the street along world z = <paramref name="streetZ"/>: the deck's girder section (asphalt, slate sides, a
        /// dashed middle line, white railings on posts) on concrete piers, which leave out the crossings so the streets
        /// across stay open underneath. A mesh in the city pack's style (<see cref="InkMesh"/>) with a collider of its
        /// girder and piers.
        /// </summary>
        static Footprint BuildRamp(Transform bridge, string name, Vector2 start, Vector2 heading, float streetZ, List<Mesh> meshes)
        {
            // Along the street u (out from the bridge), across it v (from its middle line): v eases from the deck end's
            // offset and heading to 0 over RampSwing (a cubic Hermite), then stays on the middle line.
            float dir = Mathf.Sign(heading.x);
            float v0 = start.y - streetZ, slope0 = heading.y / Mathf.Abs(heading.x);
            // Crossings are at x0 + k * pitch: touch down short of the first whose near edge lies far enough out.
            float x0 = CityCenter.x - CityRadius, run = float.MaxValue;
            for (int k = 0; k <= Mathf.CeilToInt(2f * CityRadius / CityPitch); k++)
            {
                float out_ = (x0 + k * CityPitch - dir * (15f + RampCrossingGap) - start.x) * dir;
                if (out_ >= RampMinRun)
                    run = Mathf.Min(run, out_);
            }
            float touchdown = start.x + run * dir;

            var line = new List<Vector3>(); // middle of the deck top, heights still to come
            for (float u = 0f; ; u = Mathf.Min(u + 3f, run))
            {
                float t = Mathf.Clamp01(u / RampSwing);
                float v = u < RampSwing
                    ? (2f * t * t * t - 3f * t * t + 1f) * v0 + (t * t * t - 2f * t * t + t) * RampSwing * slope0
                    : 0f;
                line.Add(new Vector3(start.x + u * dir, 0f, streetZ + v));
                if (u >= run)
                    break;
            }
            var arc = new float[line.Count];
            for (int i = 1; i < line.Count; i++)
                arc[i] = arc[i - 1] + Vector3.Distance(line[i], line[i - 1]);
            float length = arc[^1], fall = BridgeDeckTop - (LandHeight + RampEndLift);
            float grade = fall / (length - RampEase);
            float Top(float s)
            {
                if (s < RampEase)
                    return BridgeDeckTop - grade * s * s / (2f * RampEase);
                if (s > length - RampEase)
                    return LandHeight + RampEndLift + grade * Sq(length - s) / (2f * RampEase);
                return BridgeDeckTop - grade * (RampEase * 0.5f + s - RampEase);
            }
            for (int i = 0; i < line.Count; i++)
                line[i] = new Vector3(line[i].x, Top(arc[i]), line[i].z);

            var rights = new Vector3[line.Count];
            for (int i = 0; i < line.Count; i++)
            {
                Vector3 along = line[Mathf.Min(i + 1, line.Count - 1)] - line[Mathf.Max(i - 1, 0)];
                rights[i] = Vector3.Cross(Vector3.up, new Vector3(along.x, 0f, along.z)).normalized;
            }
            Vector3 At(int i, float across, float up) => line[i] + rights[i] * across + Vector3.up * up;

            var mesh = new InkMesh();
            // Girder: the bridge deck's section (top 18 m, chamfered down to 14 m), open where it meets the deck.
            var girder = new List<Vector3[]>();
            for (int i = 0; i < line.Count; i++)
                girder.Add(new[]
                {
                    At(i, RampHalfWidth, 0f), At(i, RampHalfWidth - 2f, -RampDepth), At(i, 2f - RampHalfWidth, -RampDepth),
                    At(i, -RampHalfWidth, 0f),
                });
            mesh.Solid(girder, (_, side) => side == 3 ? InkMesh.Asphalt : InkMesh.Slate, null, InkMesh.Slate, 0.25f, true);

            // Railings like the bridge's: a white rail on posts every 6 m along each edge.
            foreach (float across in new[] { -8.6f, 8.6f })
            {
                var rail = new List<Vector3[]>();
                for (int i = 0; i < line.Count; i++)
                    rail.Add(new[]
                    {
                        At(i, across + 0.175f, 0.85f), At(i, across + 0.175f, 1.35f), At(i, across - 0.175f, 1.35f),
                        At(i, across - 0.175f, 0.85f),
                    });
                mesh.Solid(rail, (_, _) => InkMesh.White, InkMesh.White, InkMesh.White, 0.06f, false);
            }
            for (int i = 0; i < line.Count; i++)
                if (i % 2 == 1)
                    foreach (float across in new[] { -8.6f, 8.6f })
                    {
                        Vector3 foot = At(i, across, 0f);
                        mesh.Box(foot, foot.y + 0.9f, Vector3.Cross(rights[i], Vector3.up), 0.24f, 0.24f, InkMesh.White, 0f, false);
                    }

            // Dashed middle line, 6 m on and 6 off (the bridge's are 5 and 5), laid along the curve.
            for (int i = 0; i + 2 < line.Count; i += 4)
            {
                const float lift = 0.1f, half = 0.25f;
                mesh.Decal(At(i, -half, lift), At(i, half, lift), At(i + 1, half, lift), At(i + 1, -half, lift), InkMesh.White);
                mesh.Decal(At(i + 1, -half, lift), At(i + 1, half, lift), At(i + 2, half, lift), At(i + 2, -half, lift), InkMesh.White);
            }

            // Piers wherever there's room under the girder, but never on a crossing.
            for (float s = RampPierSpacing * 0.66f; s < length; s += RampPierSpacing)
            {
                int i = System.Array.FindIndex(arc, a => a >= s);
                float underside = line[i].y - RampDepth;
                if (underside - LandHeight < 2f)
                    break;
                float nearest = x0 + Mathf.Round((line[i].x - x0) / CityPitch) * CityPitch;
                if (Mathf.Abs(line[i].x - nearest) < 15f + 3f && Mathf.Abs(line[i].z - streetZ) < 15f + 3f)
                    continue;
                Vector3 forward = Vector3.Cross(rights[i], Vector3.up);
                mesh.Box(new Vector3(line[i].x, LandHeight - 1f, line[i].z), underside + 0.3f, forward, 2.4f, 8f, InkMesh.Concrete, 0.15f, true);
            }

            var paint = mesh.ToMesh($"{bridge.name} {name}");
            var collider = mesh.ToCollider($"{bridge.name} {name} Col");
            meshes.Add(paint);
            meshes.Add(collider);
            var materials = new ClassModels.PlaneMaterials();
            var go = new GameObject(name);
            go.transform.SetParent(bridge, true);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // the mesh is in world space
            go.AddComponent<MeshFilter>().sharedMesh = paint;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { materials.Paint, materials.Ink };
            go.AddComponent<MeshCollider>().sharedMesh = collider;
            go.isStatic = true;

            var footprint = new Vector2[line.Count];
            for (int i = 0; i < line.Count; i++)
                footprint[i] = new Vector2(line[i].x, line[i].z);
            Debug.Log($"[Winging It] {bridge.name} {name}: {length:0} m down to the street at x {touchdown:0}, {grade * 100f:0.0}% grade");
            return new Footprint(footprint, RampHalfWidth + 0.5f);
        }

        /// <summary>Generated meshes into one asset (replaced every build), so the scene references them instead of embedding them.</summary>
        static void SaveMeshes(string path, List<Mesh> meshes)
        {
            AssetDatabase.DeleteAsset(path);
            if (meshes.Count == 0)
                return;
            AssetDatabase.CreateAsset(meshes[0], path);
            for (int i = 1; i < meshes.Count; i++)
                AssetDatabase.AddObjectToAsset(meshes[i], path);
        }

        /// <summary>Quay pieces' depth below street level and promenade width (ArtSource/City/build_city.py).</summary>
        const float QuayDepth = 16f, QuayPromenade = 12f;

        /// <summary>
        /// Quay walls down both banks of the canal and round its basin (the city pack's Quay pieces, their face on the
        /// heightmap's cut <see cref="CanalHalfWidth"/> from the middle), as far as there's land behind them, and boats on
        /// the water. Without the pack the bare cut stands in.
        /// </summary>
        static void BuildCanal(Transform parent, CityModels city)
        {
            if (city == null)
                return;
            var group = new GameObject("Canal").transform;
            group.SetParent(parent, false);

            void Quay(string kind, float length, Vector3 position, Vector3 awayFromWater)
            {
                // Local +x looks away from the water, local +z runs along the bank.
                var rotation = Quaternion.LookRotation(Vector3.Cross(awayFromWater, Vector3.up));
                var go = city.Place(kind, group, position, rotation, Vector3.one, null);
                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3(QuayPromenade * 0.5f, -QuayDepth * 0.5f, 0f);
                box.size = new Vector3(QuayPromenade, QuayDepth, length);
            }

            // Down each bank, end to end along the line CanalHalfWidth from the middle, until the land runs out.
            const float piece = 20f;
            foreach (float side in new[] { -1f, 1f })
                for (float z = CanalStartZ; ;)
                {
                    float dz = piece / Mathf.Sqrt(1f + Sq(CanalSlope(z)));
                    float zm = z + dz * 0.5f;
                    if (Height(CanalX(zm) + side * (CanalHalfWidth + 25f), zm) < 2f)
                        break; // the coast
                    var across = new Vector3(1f, 0f, -CanalSlope(zm)).normalized * side;
                    Quay("Quay", piece, new Vector3(CanalX(zm) + side * CanalHalfWidth, LandHeight, zm), across);
                    z += dz;
                }

            // Round the basin at the south end: short pieces on the half circle.
            const float shortPiece = 7f;
            var basin = new Vector3(CanalX(CanalStartZ), LandHeight, CanalStartZ);
            int count = Mathf.CeilToInt(Mathf.PI * CanalHalfWidth / shortPiece);
            for (int k = 0; k < count; k++)
            {
                float a = Mathf.PI + (k + 0.5f) * Mathf.PI / count;
                var out_ = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Quay("QuayShort", shortPiece, basin + out_ * CanalHalfWidth, out_);
            }

            // Boats: tugs and water buses along the middle, heading up or down the canal.
            var rng = new System.Random(4545);
            int n = 0;
            foreach (float z in new[] { 130f, 320f, 430f, 680f, 860f })
            {
                float x = CanalX(z) + (n % 2 == 0 ? -18f : 18f);
                if (Height(x, z) > -5f)
                    continue;
                float yaw = Mathf.Atan(CanalSlope(z)) * Mathf.Rad2Deg + (n % 2 == 0 ? 180f : 0f);
                city.PlaceVehicle(rng.NextDouble() < 0.5 ? "Tugboat" : "Ferry", group, new Vector3(x, 0f, z), yaw);
                n++;
            }
        }
    }
}
