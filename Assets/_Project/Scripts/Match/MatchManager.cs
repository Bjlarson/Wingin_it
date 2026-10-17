using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using WingingIt.Bots;
using WingingIt.Flight;
using WingingIt.World;

namespace WingingIt.Match
{
    /// <summary>
    /// Runs one offline Teams match: lobby → class select → 3-2-1 → a 5-minute clock → scoreboard (see
    /// <see cref="MatchClock"/>). The world is frozen (time scale 0) everywhere but in play, the bots only join once a
    /// class is picked, and the player's controls and flight HUD are off outside play. Every death in play goes to the
    /// <see cref="MatchScore"/> (kills and assists), and objectives score through <see cref="AwardObjective"/>. <see cref="MatchScreens"/> draws it. <see cref="Dismiss"/> drops all of this for free
    /// flight (tests, sandbox).
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
            ApplyPhase();
        }

        void OnEnable() => PlaneHealth.AnyKilled += OnAnyKilled;

        void OnDisable()
        {
            PlaneHealth.AnyKilled -= OnAnyKilled;
            Time.timeScale = 1f; // never leave the game frozen (scene unload, tests)
        }

        void Update()
        {
            if (_dismissed)
                return;
            float dt = _clock.Phase == MatchPhase.Countdown ? Time.unscaledDeltaTime : Time.deltaTime;
            if (_clock.Tick(dt))
                ApplyPhase();
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
            SetPlayOnly(true);
            if (roster != null)
                roster.enabled = true;
            enabled = false;
        }

        void ApplyPhase()
        {
            bool playing = _clock.Phase == MatchPhase.Playing;
            Time.timeScale = playing ? 1f : 0f;
            SetPlayOnly(playing);
            if (playing)
                RegisterPilots();
            if (_clock.Phase == MatchPhase.Ended)
                RegisterPilots(); // anyone who joined late and never died still gets a line
            PhaseChanged?.Invoke(_clock.Phase);
        }

        void SetPlayOnly(bool on)
        {
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
