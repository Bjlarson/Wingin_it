using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using WingingIt.Bots;
using WingingIt.Flight;
using WingingIt.World;

namespace WingingIt.Match
{
    /// <summary>Why the in-play class picker is open.</summary>
    public enum ClassPicker
    {
        None,
        /// <summary>The player is dead: the pick is the class they respawn in.</summary>
        Respawn,
        /// <summary>Parked and repaired at a home airfield: the pick swaps class on the spot.</summary>
        Airfield,
    }

    /// <summary>
    /// Runs one offline Teams match: lobby → class select → 3-2-1 → a 5-minute clock → scoreboard (see
    /// <see cref="MatchClock"/>). The world is frozen (time scale 0) everywhere but in play, the bots only join once a
    /// class is picked, and the player's controls and flight HUD are off outside play. Every death in play goes to the
    /// <see cref="MatchScore"/> (kills and assists), and objectives score through <see cref="AwardObjective"/>. <see cref="MatchScreens"/> draws it. <see cref="Dismiss"/> drops all of this for free
    /// flight (tests, sandbox).
    /// In play the class can change only on respawn (<see cref="ChooseRespawnClass"/> while dead) or parked and repaired
    /// at a home airfield (<see cref="ChangeClassAtAirfield"/>); see <see cref="ClassSwap"/>.
    /// </summary>
    public class MatchManager : MonoBehaviour
    {
        [Tooltip("The human player's plane.")]
        [SerializeField] PlaneController player;
        [Tooltip("Fills both teams with bots once the match starts. Held back until then.")]
        [SerializeField] BotRoster roster;
        [Tooltip("The classes on the class select screen, in order.")]
        [SerializeField] FlightStats[] classes;
        [Tooltip("Player controls (input, dev tools) that only run in play.")]
        [SerializeField] Behaviour[] playOnlyBehaviours;
        [Tooltip("Flight HUD and touch controls, hidden outside play.")]
        [SerializeField] GameObject[] playOnlyObjects;
        [Tooltip("Dev tools that break the match rules (free class switching). Only on after Dismiss.")]
        [SerializeField] Behaviour[] sandboxOnlyBehaviours;

        [Header("Rules")]
        [Tooltip("Seconds of play.")]
        [SerializeField, Min(1f)] float matchLength = 300f;
        [Tooltip("Real seconds of 3-2-1 before play.")]
        [SerializeField, Min(0f)] float countdownLength = 3f;
        [Tooltip("Team (and personal) points for bringing down an enemy plane.")]
        [SerializeField, Min(0)] int killPoints = 50;
        [Tooltip("Team (and personal) points for each other pilot who helped (see PlaneHealth's assist rule).")]
        [SerializeField, Min(0)] int assistPoints = 20;
        [SerializeField] string[] teamNames = { "West", "East" };
        [SerializeField] Color[] teamColors = { new(0.35f, 0.6f, 1f), new(1f, 0.38f, 0.32f) };

        // Kept across Play Again (a scene reload), so a rematch starts with the same picks.
        static BotDifficulty s_difficulty = BotDifficulty.Normal;
        static int s_classIndex;

        MatchClock _clock;
        MatchScore _score;
        bool _dismissed;
        PlaneRespawner _respawner;
        PlaneHealth _health;
        PlaneRepair _repair;
        bool _playOnlyOn = true; // as the scene is saved

        public event Action<MatchPhase> PhaseChanged;

        public MatchPhase Phase => _clock.Phase;
        /// <summary>Seconds left on the countdown (real time) or the match clock (game time).</summary>
        public float TimeLeft => _clock.TimeLeft;
        public MatchScore Score => _score;
        public PlaneController Player => player;
        public int PlayerTeam => player != null ? player.Team : 0;
        public int TeamCount => roster != null ? roster.TeamCount : 2;
        public BotRoster Roster => roster;
        public IReadOnlyList<FlightStats> Classes => classes;
        public int SelectedClass => s_classIndex;
        public bool IsDismissed => _dismissed;

        /// <summary>Whether (and why) the in-play class picker is showing. The world keeps running under it.</summary>
        public ClassPicker Picker { get; private set; }
        /// <summary>Seconds until the dead player reappears (0 when alive).</summary>
        public float RespawnTimeLeft => _respawner != null ? _respawner.RespawnTimeLeft : 0f;

        /// <summary>The home airfield the player is parked and repaired at, able to swap class; null if not.</summary>
        public Airfield SwapAirfield
        {
            get
            {
                if (player == null || _dismissed || _clock.Phase != MatchPhase.Playing)
                    return null;
                var field = HomeFieldAt(player.Position, player.Team);
                bool ok = ClassSwap.CanSwapAtAirfield(player.IsAlive, player.IsGrounded, field != null, player.Speed,
                    _repair != null ? _repair.Rules.stoppedSpeed : 0.5f, _repair != null && _repair.IsRepairing,
                    _health == null || _health.IsFullHealth);
                return ok ? field : null;
            }
        }

        public bool CanChangeClassAtAirfield => SwapAirfield != null;

        public float MatchLength
        {
            get => _clock.MatchLength;
            set => _clock.MatchLength = Mathf.Max(1f, value);
        }

        public float CountdownLength
        {
            get => _clock.CountdownLength;
            set => _clock.CountdownLength = Mathf.Max(0f, value);
        }

        public BotDifficulty Difficulty
        {
            get => s_difficulty;
            set
            {
                s_difficulty = value;
                if (roster != null)
                    roster.Difficulty = value;
            }
        }

        public string TeamName(int team) => team >= 0 && team < teamNames.Length ? teamNames[team] : $"Team {team + 1}";
        public Color TeamColor(int team) => team >= 0 && team < teamColors.Length ? teamColors[team] : Color.white;

        /// <summary>What the scoreboard calls a pilot: "You" for the player, the plane's name for bots.</summary>
        public string PilotName(PlaneController plane) => plane == player ? "You" : plane.name;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_difficulty = BotDifficulty.Normal;
            s_classIndex = 0;
        }

        void Awake()
        {
            _clock = new MatchClock(matchLength, countdownLength);
            _score = new MatchScore(TeamCount, killPoints, assistPoints);
            if (roster != null)
            {
                roster.enabled = false; // before its Start: no bots until the match starts
                roster.Difficulty = s_difficulty;
            }
            s_classIndex = Mathf.Clamp(s_classIndex, 0, Mathf.Max(0, classes.Length - 1));
            if (player != null)
            {
                _respawner = player.GetComponent<PlaneRespawner>();
                _health = player.GetComponent<PlaneHealth>();
                _repair = player.GetComponent<PlaneRepair>();
            }
            foreach (var b in sandboxOnlyBehaviours)
                if (b != null)
                    b.enabled = false;
            ApplyPhase();
        }

        void OnEnable()
        {
            PlaneHealth.AnyKilled += OnAnyKilled;
            if (_respawner != null)
                _respawner.Spawning += OnPlayerSpawning;
        }

        void OnDisable()
        {
            PlaneHealth.AnyKilled -= OnAnyKilled;
            if (_respawner != null)
                _respawner.Spawning -= OnPlayerSpawning;
            Time.timeScale = 1f; // never leave the game frozen (scene unload, tests)
        }

        void Update()
        {
            if (_dismissed)
                return;
            float dt = _clock.Phase == MatchPhase.Countdown ? Time.unscaledDeltaTime : Time.deltaTime;
            if (_clock.Tick(dt))
                ApplyPhase();
            if (_clock.Phase == MatchPhase.Playing)
                UpdatePicker();
        }

        /// <summary>Opens the respawn picker on death and closes it on respawn; drops the airfield picker once a swap isn't allowed.</summary>
        void UpdatePicker()
        {
            var want = Picker;
            if (player != null && !player.IsAlive)
                want = ClassPicker.Respawn;
            else if (Picker == ClassPicker.Respawn || (Picker == ClassPicker.Airfield && !CanChangeClassAtAirfield))
                want = ClassPicker.None;
            SetPicker(want);
        }

        void SetPicker(ClassPicker picker)
        {
            Picker = picker;
            // Controls off frees the mouse for the picker (the flight input locks it); the flight HUD would draw over it.
            SetPlayOnly(_clock.Phase == MatchPhase.Playing && picker == ClassPicker.None);
        }

        /// <summary>While dead: the class to respawn in. Takes effect when the respawn timer runs out.</summary>
        public void ChooseRespawnClass(int index)
        {
            if (Picker == ClassPicker.Respawn && IsClass(index))
                s_classIndex = index;
        }

        /// <summary>Opens the class picker while parked and repaired at a home airfield.</summary>
        public void OpenAirfieldPicker()
        {
            if (Picker == ClassPicker.None && CanChangeClassAtAirfield)
                SetPicker(ClassPicker.Airfield);
        }

        /// <summary>Closes the airfield picker without changing class.</summary>
        public void CloseAirfieldPicker()
        {
            if (Picker == ClassPicker.Airfield)
                SetPicker(ClassPicker.None);
        }

        /// <summary>
        /// Swaps the parked player into <see cref="Classes"/>[<paramref name="index"/>] on the spot (a fresh plane: full
        /// HP, ammo and ability) and closes the picker. Only while <see cref="CanChangeClassAtAirfield"/>. True if it swapped.
        /// </summary>
        public bool ChangeClassAtAirfield(int index)
        {
            if (!IsClass(index) || !CanChangeClassAtAirfield)
                return false;
            s_classIndex = index;
            if (player.Stats != classes[index])
                player.SetStats(classes[index]);
            if (Picker == ClassPicker.Airfield)
                SetPicker(ClassPicker.None);
            return true;
        }

        /// <summary>
        /// How many planes on the player's team, not counting the player, fly each of <see cref="Classes"/>: the bots
        /// planned so far before the match starts, then the planes in the air.
        /// </summary>
        public int[] TeamComposition()
        {
            var indexes = new List<int>();
            if (roster != null && !roster.enabled)
                foreach (var stats in roster.PlannedBots(PlayerTeam))
                    indexes.Add(Array.IndexOf(classes, stats));
            foreach (var plane in PlaneController.All)
                if (plane != player && plane.Team == PlayerTeam)
                    indexes.Add(Array.IndexOf(classes, plane.Stats));
            return ClassSwap.Composition(indexes, classes.Length);
        }

        bool IsClass(int index) => index >= 0 && index < classes.Length && classes[index] != null;

        void OnPlayerSpawning(PlaneRespawner _)
        {
            if (!_dismissed && IsClass(s_classIndex) && player.Stats != classes[s_classIndex])
                player.SetStats(classes[s_classIndex]);
        }

        static Airfield HomeFieldAt(Vector3 position, int team)
        {
            var zone = PlaneRepair.FriendlyZoneAt(position, team);
            if (zone == null)
                return null;
            foreach (var field in Airfield.All)
                if (field.IsHome && field.RepairZone == zone)
                    return field;
            return null;
        }

        /// <summary>Lobby → class select.</summary>
        public void OpenClassSelect()
        {
            if (_clock.OpenClassSelect())
                ApplyPhase();
        }

        /// <summary>Class select → lobby.</summary>
        public void BackToLobby()
        {
            if (_clock.BackToLobby())
                ApplyPhase();
        }

        /// <summary>
        /// Picks <see cref="Classes"/>[<paramref name="index"/>] on the class select screen: the player spawns fresh over
        /// home in it, the bots join, and the countdown starts.
        /// </summary>
        public void SelectClass(int index)
        {
            if (_clock.Phase != MatchPhase.ClassSelect || index < 0 || index >= classes.Length || classes[index] == null)
                return;
            s_classIndex = index;
            if (player != null)
            {
                player.SetStats(classes[index]);
                if (player.TryGetComponent<PlaneRespawner>(out var respawner))
                    respawner.SpawnNow();
            }
            if (roster != null)
            {
                roster.enabled = true;
                roster.Refresh(); // the bots appear now, frozen in place for the countdown
            }
            _clock.StartCountdown();
            ApplyPhase();
        }

        /// <summary>Ends the match now, as if time ran out.</summary>
        public void EndMatch()
        {
            if (_clock.End())
                ApplyPhase();
        }

        /// <summary>
        /// Objective points (convoy, supply drop, capture) for <paramref name="team"/>, and for <paramref name="pilot"/>
        /// if one earned them. Only counts in play. True if it scored.
        /// </summary>
        public bool AwardObjective(int team, int points, string label, PlaneController pilot = null)
        {
            if (_dismissed || _clock.Phase != MatchPhase.Playing)
                return false;
            return _score.AwardPoints(team, pilot, pilot != null ? PilotName(pilot) : null, points, label);
        }

        /// <summary>A rematch: reloads the level back to the lobby, keeping the difficulty and class picked.</summary>
        public void PlayAgain()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(gameObject.scene.name);
        }

        /// <summary>
        /// Drops the match flow for free flight: the world runs, the bots fill in, the player has control, no clock, no
        /// scoring. For tests and sandbox play.
        /// </summary>
        public void Dismiss()
        {
            _dismissed = true;
            Time.timeScale = 1f;
            Picker = ClassPicker.None;
            SetPlayOnly(true);
            foreach (var b in sandboxOnlyBehaviours)
                if (b != null)
                    b.enabled = true;
            if (roster != null)
                roster.enabled = true;
            enabled = false;
        }

        void ApplyPhase()
        {
            bool playing = _clock.Phase == MatchPhase.Playing;
            Time.timeScale = playing ? 1f : 0f;
            Picker = ClassPicker.None;
            SetPlayOnly(playing);
            if (playing)
                RegisterPilots();
            if (_clock.Phase == MatchPhase.Ended)
                RegisterPilots(); // anyone who joined late and never died still gets a line
            PhaseChanged?.Invoke(_clock.Phase);
        }

        void SetPlayOnly(bool on)
        {
            if (on == _playOnlyOn)
                return;
            _playOnlyOn = on;
            foreach (var b in playOnlyBehaviours)
                if (b != null)
                    b.enabled = on;
            foreach (var go in playOnlyObjects)
                if (go != null)
                    go.SetActive(on);
        }

        void RegisterPilots()
        {
            foreach (var plane in PlaneController.All)
                if (plane.Team >= 0 && plane.Team < TeamCount)
                    _score.Register(plane, PilotName(plane), plane.Team);
        }

        void OnAnyKilled(KillInfo kill)
        {
            if (_dismissed || _clock.Phase != MatchPhase.Playing || kill.Victim == null)
                return;
            var victim = kill.Victim.Plane;
            int? killerTeam = kill.Killer != null ? kill.Killer.Team
                : kill.Source is AAGun aa ? aa.Team
                : null;
            _score.RecordDeath(victim, PilotName(victim), victim.Team,
                kill.Killer, kill.Killer != null ? PilotName(kill.Killer) : null, killerTeam);
            foreach (var helper in kill.Assisters)
                if (helper != null)
                    _score.RecordAssist(helper, PilotName(helper), helper.Team, victim.Team);
        }
    }
}
