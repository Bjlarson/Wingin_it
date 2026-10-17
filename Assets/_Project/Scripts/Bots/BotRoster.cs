using System.Collections.Generic;
using UnityEngine;
using WingingIt.Flight;
using WingingIt.World;

namespace WingingIt.Bots
{
    /// <summary>The pure rules behind <see cref="BotRoster"/>: which class the next bot flies and which fight areas it patrols.</summary>
    public static class TeamFill
    {
        /// <summary>
        /// The class the next plane on a team should fly: the one furthest below its share of the team, given how many
        /// of each it has (<paramref name="counts"/>) and the target mix (<paramref name="weights"/>, e.g. 3/2/2/1 for 8).
        /// Ties go to the bigger share, then the lower index. A weight of 0 is never picked. -1 if every weight is 0.
        /// </summary>
        public static int PickClass(IReadOnlyList<int> counts, IReadOnlyList<int> weights)
        {
            int best = -1;
            float bestRatio = float.MaxValue;
            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i] <= 0)
                    continue;
                float ratio = (i < counts.Count ? counts[i] : 0) / (float)weights[i];
                if (best < 0 || ratio < bestRatio - 1e-4f || (Mathf.Abs(ratio - bestRatio) <= 1e-4f && weights[i] > weights[best]))
                {
                    best = i;
                    bestRatio = ratio;
                }
            }
            return best;
        }

        /// <summary>
        /// The two fight areas the <paramref name="index"/>th bot on a team patrols between, as indexes into the team's
        /// areas sorted nearest-home first. The first <paramref name="areaCount"/> bots take neighboring pairs around the
        /// list (the last one wraps back home), the next ones skip one, and so on, so a team spreads over every area and
        /// both teams' bots keep crossing in the middle. Both are 0 with one area.
        /// </summary>
        public static (int from, int to) PatrolPair(int index, int areaCount)
        {
            if (areaCount <= 1)
                return (0, 0);
            int step = 1 + (index / areaCount) % (areaCount - 1);
            int from = index % areaCount;
            return (from, (from + step) % areaCount);
        }

        /// <summary>How many bots a team needs so that it has <paramref name="teamSize"/> planes counting its <paramref name="humans"/>.</summary>
        public static int BotsWanted(int teamSize, int humans) => Mathf.Max(0, teamSize - humans);
    }

    /// <summary>
    /// Keeps every team at <see cref="TeamSize"/> planes by adding bots for the empty slots (8v8 by default). Every
    /// plane without a <see cref="BotBrain"/> counts as a human. A new bot flies the class its team is most short of
    /// (the <c>classWeights</c> mix), appears over its home airfield like a respawn, and patrols between two of the
    /// level's fight areas. When a human takes a slot, a bot leaves the next time it's dead (never mid-fight).
    /// All bots share the roster's <see cref="Difficulty"/>.
    /// </summary>
    public class BotRoster : MonoBehaviour
    {
        [Tooltip("The plane every bot flies (the same prefab as the player's).")]
        [SerializeField] GameObject planePrefab;
        [Tooltip("The classes bots can fly.")]
        [SerializeField] FlightStats[] classes;
        [Tooltip("The target team mix, one weight per class: 3/2/2/1 makes 3 Fighters, 2 Interceptors, 2 Heavies and 1 Support in 8.")]
        [SerializeField] int[] classWeights = { 3, 2, 2, 1 };

        [Header("Teams")]
        [Tooltip("Planes per team, humans and bots together.")]
        [SerializeField, Min(0)] int teamSize = 8;
        [Tooltip("Teams 0 .. this - 1 get filled.")]
        [SerializeField, Min(1)] int teamCount = 2;
        [Tooltip("Every bot's skill. Changing it in play mode applies at once.")]
        [SerializeField] BotDifficulty difficulty = BotDifficulty.Normal;

        [Header("Patrol")]
        [Tooltip("Where the fighting happens: each bot loops between two of these (at the height given) when nothing's in sight.")]
        [SerializeField] List<Vector3> fightAreas = new();
        [Tooltip("Seconds between checks for slots to fill or bots to remove.")]
        [SerializeField] float refreshInterval = 2f;

        readonly List<BotBrain> _bots = new();
        readonly Dictionary<int, int> _spawned = new(); // per team: bots ever spawned, so replacements get new routes
        float _nextRefresh;

        public IReadOnlyList<BotBrain> Bots => _bots;
        public IReadOnlyList<Vector3> FightAreas => fightAreas;
        public int TeamCount => teamCount;

        public int TeamSize
        {
            get => teamSize;
            set
            {
                teamSize = Mathf.Max(0, value);
                if (isActiveAndEnabled)
                    Refresh();
            }
        }

        public BotDifficulty Difficulty
        {
            get => difficulty;
            set
            {
                difficulty = value;
                foreach (var bot in _bots)
                    if (bot != null)
                        bot.Difficulty = value;
            }
        }

        public void Configure(GameObject prefab, FlightStats[] botClasses, IEnumerable<Vector3> areas)
        {
            planePrefab = prefab;
            classes = botClasses;
            fightAreas = new List<Vector3>(areas);
        }

        void Start() => Refresh();

        void Update()
        {
            if (Time.time < _nextRefresh)
                return;
            _nextRefresh = Time.time + refreshInterval;
            Refresh();
        }

        void OnValidate()
        {
            if (Application.isPlaying)
                Difficulty = difficulty;
        }

        /// <summary>Removes every bot and stops filling slots (for tests and levels that want an empty sky).</summary>
        public void Clear()
        {
            enabled = false;
            foreach (var bot in _bots)
                if (bot != null)
                    Destroy(bot.gameObject);
            _bots.Clear();
        }

        /// <summary>Adds bots to any team short of <see cref="TeamSize"/>, and removes dead ones from teams over it.</summary>
        public void Refresh()
        {
            _bots.RemoveAll(b => b == null);
            for (int team = 0; team < teamCount; team++)
            {
                int humans = 0;
                foreach (var plane in PlaneController.All)
                    if (plane.Team == team && !plane.TryGetComponent<BotBrain>(out _))
                        humans++;

                int want = TeamFill.BotsWanted(teamSize, humans);
                int have = CountBots(team);
                for (; have < want; have++)
                    if (Spawn(team) == null)
                        break;
                for (int i = _bots.Count - 1; i >= 0 && have > want; i--)
                {
                    var plane = _bots[i].GetComponent<PlaneController>();
                    if (plane.Team != team || plane.IsAlive)
                        continue;
                    Destroy(_bots[i].gameObject);
                    _bots.RemoveAt(i);
                    have--;
                }
            }
        }

        int CountBots(int team)
        {
            int n = 0;
            foreach (var bot in _bots)
                if (bot.GetComponent<PlaneController>().Team == team)
                    n++;
            return n;
        }

        /// <summary>A new bot on <paramref name="team"/>, over (or on) its home airfield. Null if the team has no airfield or there's nothing to fly.</summary>
        BotBrain Spawn(int team)
        {
            Airfield home = HomeField(team);
            int cls = TeamFill.PickClass(ClassCounts(team), classWeights);
            if (home == null || planePrefab == null || cls < 0 || cls >= classes.Length || classes[cls] == null)
            {
                Debug.LogWarning($"BotRoster: can't add a bot to team {team} (no home airfield, plane prefab or class).", this);
                return null;
            }

            _spawned.TryGetValue(team, out int index);
            _spawned[team] = index + 1;

            Transform spawn = home.AirSpawn != null ? home.AirSpawn : home.transform;
            var go = Instantiate(planePrefab, spawn.position, spawn.rotation, transform);
            go.name = $"Bot {team}-{index + 1} ({classes[cls].displayName})";
            var plane = go.GetComponent<PlaneController>();
            plane.Team = team;
            plane.SetStats(classes[cls]);

            go.AddComponent<BotPilot>();
            var brain = go.AddComponent<BotBrain>();
            brain.Difficulty = difficulty;
            if (go.TryGetComponent<PlaneRespawner>(out var respawner))
                respawner.SpawnNow();
            brain.SetPatrol(PatrolRoute(home.transform.position, index));
            _bots.Add(brain);
            return brain;
        }

        /// <summary>The <paramref name="index"/>th bot's patrol: two fight areas (see <see cref="TeamFill.PatrolPair"/>), nearest-home areas first.</summary>
        List<Vector3> PatrolRoute(Vector3 home, int index)
        {
            var sorted = new List<Vector3>(fightAreas);
            sorted.Sort((a, b) => FlatDistance(a, home).CompareTo(FlatDistance(b, home)));
            var route = new List<Vector3>();
            if (sorted.Count == 0)
                return route;
            var (from, to) = TeamFill.PatrolPair(index, sorted.Count);
            route.Add(sorted[from]);
            if (to != from)
                route.Add(sorted[to]);
            return route;
        }

        static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        int[] ClassCounts(int team)
        {
            var counts = new int[classes.Length];
            foreach (var plane in PlaneController.All)
            {
                if (plane.Team != team)
                    continue;
                int i = System.Array.IndexOf(classes, plane.Stats);
                if (i >= 0)
                    counts[i]++;
            }
            return counts;
        }

        static Airfield HomeField(int team)
        {
            foreach (var field in Airfield.All)
                if (field.IsHome && field.Team == team)
                    return field;
            return Airfield.NearestFriendly(Vector3.zero, team);
        }
    }
}
