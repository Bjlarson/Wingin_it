using System.Collections.Generic;
using UnityEngine;
using WingingIt.Flight;
using WingingIt.World;

namespace WingingIt.Maps
{
    /// <summary>
    /// Spawns a mode's pieces from the map's <see cref="MapLayout"/>: its airfields (capture fields with Domination's
    /// tower and light AA) and their approach lanes, its banner stands and its arena (<see cref="ModeArena"/>). The match
    /// asks for the mode's set when the mode is picked (<see cref="Build"/>) and for every piece on free flight
    /// (<see cref="BuildEverything"/>). A new set removes the pieces it doesn't use and keeps the ones it shares, so a
    /// home field (and the plane parked in its hangar) stays put from one mode to the next. A piece is shared only when
    /// it comes from the same anchors: another map's field or stand of the same name is spawned afresh.
    /// </summary>
    public class ModePieceBuilder : MonoBehaviour
    {
        [Tooltip("The map whose layout this spawns.")]
        [SerializeField] MapDefinition map;
        [SerializeField] ModePieceKit kit;

        // Domination's field defenses: the tower takes about 2 bomb hits, a light AA gun one bomb or a couple of rocket salvos.
        const float TowerHealth = 300f, TowerRebuild = 45f;
        const float LightAAHealth = 120f, LightAARebuild = 30f;

        /// <summary>Where every lane joins its runway's glide path: inside the home AA umbrella, ~31 m above the runway.</summary>
        const float FinalFixDistance = 600f;
        /// <summary>A lane waypoint flies its height above the highest ground or building within this far of it.</summary>
        const float LaneClearanceRadius = 60f;
        const float LaneHalfWidth = 150f;
        /// <summary>Gate pylons stand this far either side of a lane waypoint.</summary>
        const float LaneGateHalfWidth = 40f;
        /// <summary>The pylon top's sleeve reaches this far below the gate point; the stretched pole meets it there.</summary>
        const float PylonSleeveLength = 16f;
        /// <summary>How far the pole runs up inside the sleeve, so the joint never shows a gap.</summary>
        const float PylonPoleOverlap = 0.5f;

        /// <summary>A bot patrol point flies its height above the highest ground or building within this far of it.</summary>
        const float FightAreaClearanceRadius = 150f;

        const float BannerPoleHeight = 45f;
        /// <summary>Height of the line the banner hangs from, strung between the poles inside the grab zone (m).</summary>
        const float BannerLineHeight = 30f;
        const float BannerZoneRadius = 20f, BannerZoneHeight = 40f;

        readonly Dictionary<string, Airfield> _airfields = new();
        readonly Dictionary<string, GameObject> _lanes = new(); // each airfield's lanes, by airfield name
        readonly Dictionary<string, BannerStand> _stands = new(); // by airfield name
        readonly Dictionary<string, ModeArena> _arenas = new(); // by mode id
        readonly Dictionary<Color, Material> _laneMaterials = new();
        readonly List<Vector3> _fightAreas = new();
        readonly Dictionary<string, object[]> _from = new(); // the anchors each spawned piece was built from, by "kind:name"
        Transform _airfieldGroup, _laneGroup, _standGroup, _arenaGroup;

        public MapDefinition Map => map;
        public ModePieceKit Kit => kit;
        /// <summary>The airfields spawned right now, by name.</summary>
        public IReadOnlyDictionary<string, Airfield> Airfields => _airfields;
        public int StandCount => _stands.Count;
        public int ArenaCount => _arenas.Count;

        /// <summary>
        /// Where bots patrol among the pieces spawned now (<see cref="MapLayout.FightAreasFor"/>), each at its height above
        /// the highest thing near it; empty when the layout has none. A mode's arena brings its own.
        /// </summary>
        public IReadOnlyList<Vector3> FightAreas => _fightAreas;

        /// <summary>Raised after every build or clear: the pieces (and fight areas) changed. The minimap photographs them again.</summary>
        public event System.Action Changed;

        public void Configure(MapDefinition definition, ModePieceKit pieces)
        {
            map = definition;
            kit = pieces;
        }

        /// <summary>Spawns <paramref name="modeId"/>'s pieces (see <see cref="MapLayout.PiecesFor"/>) and removes the rest.</summary>
        public void Build(string modeId)
        {
            if (map != null)
                Build(map.layout.PiecesFor(modeId), map.layout.FightAreasFor(modeId));
        }

        /// <summary>Every piece the layout has: the map as one sandbox, for free flight.</summary>
        public void BuildEverything() => Build((string)null);

        /// <summary>Removes every spawned piece.</summary>
        public void Clear() => Build(new MapPieces());

        public void Build(MapPieces pieces, IReadOnlyList<FightAreaAnchor> fightAreas = null)
        {
            if (kit == null || kit.airfieldPrefab == null)
                Debug.LogWarning($"[Winging It] {name}: no mode piece kit, nothing spawned.");
            else
                Spawn(pieces);
            PlaceFightAreas(fightAreas);
            Changed?.Invoke();
        }

        void Spawn(MapPieces pieces)
        {
            EnsureGroups();

            // What to keep: the pieces asked for that are already up, built from the same anchors.
            var fieldNames = new HashSet<string>();
            foreach (var a in pieces.airfields)
                if (Keeps("field:" + a.name, FieldSource(a)))
                    fieldNames.Add(a.name);
            var standFields = new HashSet<string>();
            foreach (var s in pieces.bannerStands)
                if (fieldNames.Contains(s.airfield) && Keeps("stand:" + s.airfield, s))
                    standFields.Add(s.airfield);
            var arenaIds = new HashSet<string>();
            foreach (var a in pieces.arenas)
                if (Keeps("arena:" + a.modeId, a))
                    arenaIds.Add(a.modeId);

            RemoveExcept(_stands, standFields);
            RemoveExcept(_lanes, fieldNames);
            RemoveExcept(_airfields, fieldNames);
            RemoveExcept(_arenas, arenaIds);

            var added = new List<Airfield>();
            foreach (var a in pieces.airfields)
                if (!_airfields.ContainsKey(a.name))
                {
                    added.Add(_airfields[a.name] = SpawnAirfield(a));
                    _from["field:" + a.name] = FieldSource(a);
                }
            Physics.SyncTransforms(); // the new fields' buildings, so the lanes' clearance probe sees them
            foreach (var field in added)
                _lanes[field.DisplayName] = SpawnLanes(field);
            foreach (var s in pieces.bannerStands)
                if (!_stands.ContainsKey(s.airfield) && _airfields.TryGetValue(s.airfield, out var home))
                {
                    _stands[s.airfield] = SpawnBannerStand(home, s);
                    _from["stand:" + s.airfield] = new object[] { s };
                }
            foreach (var a in pieces.arenas)
                if (!_arenas.ContainsKey(a.modeId))
                {
                    _arenas[a.modeId] = SpawnArena(a);
                    _from["arena:" + a.modeId] = new object[] { a };
                }
        }

        /// <summary>What an airfield is built from: its anchor and its lanes' (a field comes with its lanes).</summary>
        object[] FieldSource(AirfieldAnchor a)
        {
            var source = new List<object> { a };
            foreach (var lane in map.layout.lanes)
                if (lane.airfield == a.name)
                    source.Add(lane);
            return source.ToArray();
        }

        /// <summary>False when a piece is up under <paramref name="key"/> but was built from other anchors than <paramref name="source"/>.</summary>
        bool Keeps(string key, params object[] source)
        {
            if (!_from.TryGetValue(key, out var was))
                return true;
            if (was.Length != source.Length)
                return false;
            for (int i = 0; i < was.Length; i++)
                if (!ReferenceEquals(was[i], source[i]))
                    return false;
            return true;
        }

        /// <summary>The fight areas in the world, each flown its height above the highest thing within <see cref="FightAreaClearanceRadius"/>.</summary>
        void PlaceFightAreas(IReadOnlyList<FightAreaAnchor> areas)
        {
            _fightAreas.Clear();
            if (areas == null)
                return;
            Physics.SyncTransforms(); // the pieces just spawned count as "highest thing" too
            foreach (var a in areas)
                _fightAreas.Add(new Vector3(a.point.xz.x, HighestPointNear(a.point.xz, FightAreaClearanceRadius) + a.point.agl, a.point.xz.y));
        }

        void EnsureGroups()
        {
            Transform Group(ref Transform group, string groupName)
            {
                if (group == null)
                {
                    group = new GameObject(groupName).transform;
                    group.SetParent(transform, false);
                }
                return group;
            }
            Group(ref _airfieldGroup, "Airfields");
            Group(ref _laneGroup, "Approach Lanes");
            Group(ref _standGroup, "Banner Stands");
            Group(ref _arenaGroup, "Mode Arenas");
        }

        static void RemoveExcept<T>(Dictionary<string, T> spawned, HashSet<string> keep) where T : Object
        {
            var drop = new List<string>();
            foreach (var (key, _) in spawned)
                if (!keep.Contains(key))
                    drop.Add(key);
            foreach (var key in drop)
            {
                var piece = spawned[key];
                spawned.Remove(key);
                if (piece == null)
                    continue;
                var go = piece as GameObject ?? ((Component)(Object)piece).gameObject;
                go.SetActive(false); // off the registries (Airfield.All, ...) now, not at the end of the frame
                Discard(go);
            }
        }

        static void Discard(Object o)
        {
            if (Application.isPlaying)
                Destroy(o);
            else
                DestroyImmediate(o);
        }

        // ------------------------------------------------------------------ airfields

        /// <summary>
        /// One airfield from the prefab, named and owned as the layout says. It's set up inside an inactive holder, so its
        /// parts wake (and register) only once it's the field it should be.
        /// </summary>
        Airfield SpawnAirfield(AirfieldAnchor a)
        {
            var holder = new GameObject("Spawning");
            holder.SetActive(false);
            var go = Instantiate(kit.airfieldPrefab, holder.transform);
            go.name = a.name;
            go.transform.SetPositionAndRotation(new Vector3(a.position.x, a.elevation, a.position.y), Quaternion.Euler(0f, a.yaw, 0f));
            var field = go.GetComponent<Airfield>();
            field.SetIdentity(a.name, a.team, a.home);
            field.SetTwoWay(a.twoWay);
            if (a.capturable)
                AddFieldDefenses(field);
            go.transform.SetParent(_airfieldGroup, true);
            Discard(holder);
            return field;
        }

        /// <summary>
        /// Turns a capture field's buildings into Airfield Domination's defenses (<see cref="FieldDefenses"/>): the tower
        /// and its cab become one bombable <see cref="GroundTarget"/>, and of the prefab's four AA guns the two on one
        /// diagonal stay, as destructible light AA; the other two go. Dormant until a Domination match.
        /// </summary>
        void AddFieldDefenses(Airfield field)
        {
            string shortName = field.DisplayName.Replace(" Airfield", "");
            var buildings = field.transform.Find("Buildings");
            var tower = buildings.Find("Control Tower"); // the airfield pack's stands on its foot
            var cab = buildings.Find("Tower Cab");
            if (cab != null) // greybox: a shaft box and a cab box, grouped on the ground
            {
                var shaft = tower;
                tower = new GameObject("Control Tower").transform;
                tower.SetParent(buildings, false);
                tower.localPosition = new Vector3(shaft.localPosition.x, 0f, shaft.localPosition.z); // squashes toward the ground
                shaft.name = "Tower Shaft";
                shaft.SetParent(tower, true);
                cab.SetParent(tower, true);
            }
            var towerTarget = tower.gameObject.AddComponent<GroundTarget>();
            towerTarget.Configure($"{shortName} Tower", TowerHealth, tower.GetComponentsInChildren<Renderer>(), TowerRebuild);

            var kept = new List<AAGun>();
            foreach (var gun in field.transform.Find("AA Guns").GetComponentsInChildren<AAGun>(true))
            {
                if (gun.name != "AA Gun 1" && gun.name != "AA Gun 4")
                {
                    DestroyImmediate(gun.gameObject); // never woken: no shells or flashes to tidy
                    continue;
                }
                gun.SetStats(kit.lightAA);
                gun.gameObject.AddComponent<GroundTarget>().Configure($"{shortName} AA", LightAAHealth,
                    gun.GetComponentsInChildren<Renderer>(), LightAARebuild);
                kept.Add(gun);
            }
            field.gameObject.AddComponent<FieldDefenses>().Configure(towerTarget, kept.ToArray());
        }

        // ------------------------------------------------------------------ approach lanes

        /// <summary>
        /// The field's lanes from the layout, each a marked route of gate pylons (coloured per lane, no collision) ending on
        /// the field's final approach fix. The pylons are the markers pack's, or greybox poles with a sphere.
        /// </summary>
        GameObject SpawnLanes(Airfield field)
        {
            var group = new GameObject(field.DisplayName).transform;
            group.SetParent(_laneGroup, false);
            foreach (var lane in map.layout.lanes)
                if (lane.airfield == field.DisplayName)
                    SpawnLane(group, field, lane);
            return group.gameObject;
        }

        void SpawnLane(Transform parent, Airfield field, LaneAnchor lane)
        {
            var points = lane.points;
            var root = new GameObject($"{field.DisplayName} - {lane.name}").transform;
            root.SetParent(parent, false);
            var path = new Vector3[points.Length + 1];
            for (int i = 0; i < points.Length; i++)
                path[i] = new Vector3(points[i].xz.x, HighestPointNear(points[i].xz, LaneClearanceRadius) + points[i].agl, points[i].xz.y);
            path[^1] = field.GlidePathPoint(FinalFixDistance, lane.reverseEnd);
            root.position = path[0];
            root.gameObject.AddComponent<ApproachLane>().Configure(lane.name, field, path, LaneHalfWidth);

            // A gate at every waypoint but the fix (the glide-path rings take over there): two pylons across the lane.
            var gateMat = LaneMaterial(lane.color);
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 inDir = i > 0 ? path[i] - path[i - 1] : path[i + 1] - path[i];
                Vector3 dir = inDir.normalized + (path[i + 1] - path[i]).normalized;
                dir.y = 0f;
                Vector3 across = Vector3.Cross(Vector3.up, dir.normalized);
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector3 top = path[i] + across * (side * LaneGateHalfWidth);
                    float ground = Mathf.Max(0f, HighestPointNear(new Vector2(top.x, top.z), 0f));
                    float height = Mathf.Max(1f, top.y - ground);
                    var gate = new GameObject(i == 0 ? "Entry Gate" : $"Gate {i}").transform;
                    gate.SetParent(root, false);
                    gate.position = new Vector3(top.x, ground, top.z);
                    if (kit.HasPylon)
                        Pylon(gate, height, gateMat);
                    else
                    {
                        Primitive(PrimitiveType.Cube, "Pole", gate, new Vector3(0f, height * 0.5f, 0f), new Vector3(1.5f, height, 1.5f), kit.greyboxPole);
                        Primitive(PrimitiveType.Sphere, "Marker", gate, new Vector3(0f, height, 0f), Vector3.one * 8f, gateMat);
                    }
                }
            }
        }

        /// <summary>One of the markers pack's pylon meshes as a child of <paramref name="gate"/>.</summary>
        static void PylonPart(string partName, Transform gate, Vector3 localPosition, KitMesh part, out MeshRenderer renderer)
        {
            var go = new GameObject(partName);
            go.transform.SetParent(gate, false);
            go.transform.localPosition = localPosition;
            go.AddComponent<MeshFilter>().sharedMesh = part.mesh;
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = part.materials;
        }

        /// <summary>
        /// A gate pylon standing on <paramref name="gate"/> with its marker <paramref name="height"/> up: footing, a white
        /// pole stretched to the sleeve, and the marker in the lane's glow. A pylon too short for the sleeve shrinks its top.
        /// </summary>
        void Pylon(Transform gate, float height, Material laneMaterial)
        {
            PylonPart("PylonBase", gate, Vector3.zero, kit.pylonBase, out _);
            PylonPart("PylonTop", gate, new Vector3(0f, height, 0f), kit.pylonTop, out var top);
            var mats = top.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && mats[i].name == "Weapon_Glow")
                    mats[i] = laneMaterial;
            top.sharedMaterials = mats;

            float pole = height - PylonSleeveLength + PylonPoleOverlap;
            if (pole > 1f)
            {
                PylonPart("PylonPole", gate, Vector3.zero, kit.pylonPole, out var poleRenderer);
                poleRenderer.transform.localScale = new Vector3(1f, pole, 1f);
            }
            else
                top.transform.localScale = Vector3.one * Mathf.Max(0.1f, height / (PylonSleeveLength + 1f));
        }

        /// <summary>One material per lane colour: the pylon glow (or greybox marker) tinted to it.</summary>
        Material LaneMaterial(Color color)
        {
            if (_laneMaterials.TryGetValue(color, out var mat))
                return mat;
            var source = kit.HasPylon ? kit.laneGlow : kit.greyboxLane;
            mat = source != null ? new Material(source) : null;
            if (mat != null)
                mat.SetColor("_BaseColor", kit.HasPylon ? PlaneVisuals.TeamTint(color) : color);
            return _laneMaterials[color] = mat;
        }

        // ------------------------------------------------------------------ banner stands

        /// <summary>
        /// Capture the Banner's stand (match-modes.md): a gate past the far end of a home runway, in line with it — two
        /// poles either side of the grab zone (<see cref="BannerStand"/>), across the runway's line, with the banner hanging
        /// from a line between them. Fly through the gap to grab it. Nothing on a stand has a collider, so a plane that
        /// clips a pole flies on through it.
        /// </summary>
        BannerStand SpawnBannerStand(Airfield home, BannerStandAnchor anchor)
        {
            var pole = kit.bannerPole;
            var cloth = home.Team >= 0 && home.Team < kit.bannerCloth.Length ? kit.bannerCloth[home.Team] : pole;

            Vector3 xz = home.transform.position + home.RunwayDirection * (home.RunwaySize.y * 0.5f + anchor.distance);
            float ground = GroundHeight(xz);

            var go = new GameObject($"{home.DisplayName.Replace(" Home Airfield", "").Replace(" Airfield", "")} Banner Stand");
            go.transform.SetParent(_standGroup, false);
            go.transform.SetPositionAndRotation(new Vector3(xz.x, ground, xz.z), home.transform.rotation);
            var mount = new GameObject("Flag Mount").transform;
            mount.SetParent(go.transform, false);
            mount.localPosition = new Vector3(0f, BannerLineHeight, 0f);
            foreach (float side in new[] { -1f, 1f })
            {
                float x = side * BannerZoneRadius;
                // Each pole stands on its own ground, so the gate sits right on a slope.
                float foot = GroundHeight(go.transform.TransformPoint(new Vector3(x, 0f, 0f))) - ground;
                float height = BannerPoleHeight - foot;
                string s = side < 0f ? "L" : "R";
                Cylinder($"Pole {s}", go.transform, new Vector3(x, foot + height * 0.5f, 0f), 1.2f, height, pole);
                Cylinder($"Base {s}", go.transform, new Vector3(x, foot + 0.5f, 0f), 4f, 1f, pole);
                // A team-coloured cap, so the gate reads from a distance.
                Cylinder($"Cap {s}", go.transform, new Vector3(x, BannerPoleHeight - 1.5f, 0f), 1.6f, 3f, cloth);
            }
            Primitive(PrimitiveType.Cube, "Line", go.transform, new Vector3(0f, BannerLineHeight, 0f), new Vector3(BannerZoneRadius * 2f, 0.15f, 0.15f), pole);
            // The cloth hangs from the middle of the line, its face across the gap; its long side (local z) runs along
            // the line, which is how the towed banner trails it.
            var flag = Primitive(PrimitiveType.Cube, "Banner", go.transform, new Vector3(0f, BannerLineHeight - 5f, 0f), new Vector3(0.3f, 10f, 14f), cloth);
            flag.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            var stand = go.AddComponent<BannerStand>();
            stand.Configure(home.Team, BannerZoneRadius, BannerZoneHeight, mount, flag.transform);
            return stand;
        }

        static Renderer Cylinder(string partName, Transform parent, Vector3 localPos, float diameter, float height, Material mat) =>
            Primitive(PrimitiveType.Cylinder, partName, parent, localPos, new Vector3(diameter, height * 0.5f, diameter), mat); // the primitive is 2 units tall

        /// <summary>A primitive with no collider.</summary>
        static Renderer Primitive(PrimitiveType type, string partName, Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = partName;
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            return r;
        }

        // ------------------------------------------------------------------ arenas

        /// <summary>A mode's play area (<see cref="ModeArena"/>) as the layout describes it, on the sea.</summary>
        ModeArena SpawnArena(ArenaAnchor a)
        {
            var arena = new GameObject($"{a.modeId} Arena").AddComponent<ModeArena>();
            arena.transform.SetParent(_arenaGroup, false);
            arena.transform.position = new Vector3(a.center.x, map.seaLevel, a.center.y);
            arena.Configure(a.modeId, a.radius, a.warning, a.ceiling, a.hardEdge);
            arena.ConfigureAirfields(a.closeAirfields);
            arena.ConfigureStart(a.airStart, a.startRadius, a.startAltitude, a.startSlots);
            var areas = new Vector3[a.fightAreas?.Length ?? 0];
            for (int i = 0; i < areas.Length; i++)
                areas[i] = new Vector3(a.fightAreas[i].xz.x, map.seaLevel + a.fightAreas[i].agl, a.fightAreas[i].xz.y);
            arena.ConfigureFightAreas(areas);
            return arena;
        }

        // ------------------------------------------------------------------ ground probes

        static readonly RaycastHit[] s_hits = new RaycastHit[16];

        /// <summary>
        /// The highest solid surface (ground, sea, building) at <paramref name="xz"/> or within <paramref name="radius"/> of
        /// it. Planes don't count: they may be flying through when the pieces spawn.
        /// </summary>
        public static float HighestPointNear(Vector2 xz, float radius)
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
                    int n = Physics.RaycastNonAlloc(from, Vector3.down, s_hits, 3000f, ~0, QueryTriggerInteraction.Ignore);
                    for (int h = 0; h < n; h++)
                        if (s_hits[h].rigidbody == null)
                            best = Mathf.Max(best, s_hits[h].point.y);
                }
            }
            return best;
        }

        /// <summary>The terrain's height under <paramref name="p"/> (the sea level off the terrain or with none).</summary>
        float GroundHeight(Vector3 p)
        {
            var terrain = Terrain.activeTerrain;
            return terrain != null ? terrain.SampleHeight(p) + terrain.transform.position.y : map.seaLevel;
        }
    }
}
