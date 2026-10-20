using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using WingingIt.Bots;
using WingingIt.Flight;
using WingingIt.Settings;
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
    /// Runs one offline match: menu (main menu + game mode select, the player's plane parked in its home hangar)
    /// → class select → 3-2-1 → the mode's clock → scoreboard (see <see cref="MatchClock"/>). The world is frozen (time
    /// scale 0) everywhere but in play, the bots only join once a class is picked, and the player's controls and flight
    /// HUD are off outside play. In play the in-match menu (<see cref="OpenMenu"/>) never pauses: it only lets go of the
    /// controls. The rules come from the <see cref="Mode"/> picked on the game mode screen (<see cref="SelectMode"/>):
    /// every death in play goes to it to score, it can end the match early and decides the winner, and it sets the
    /// respawn rules (<see cref="PlaneRespawner.Rules"/>) while the match is on. Objectives score through
    /// <see cref="AwardObjective"/>. <see cref="MatchScreens"/> draws it. <see cref="Dismiss"/> drops all of this for
    /// free flight (tests, sandbox).
    /// In play the class can change only on respawn (<see cref="ChooseRespawnClass"/> while dead) or parked and repaired
    /// at a home airfield (<see cref="ChangeClassAtAirfield"/>); see <see cref="ClassSwap"/>.
    /// </summary>
    public class MatchManager : MonoBehaviour, ISpawnRules
    {
        [Tooltip("The human player's plane.")]
        [SerializeField] PlaneController player;
        [Tooltip("Fills both teams with bots once the match starts. Held back until then.")]
        [SerializeField] BotRoster roster;
        [Tooltip("The classes on the class select screen, in order.")]
        [SerializeField] FlightStats[] classes;
        [Tooltip("Player controls (input, dev tools) that only run in play.")]
        [SerializeField] Behaviour[] playOnlyBehaviours;
        [Tooltip("Flight HUD and debug overlay, hidden outside play.")]
        [SerializeField] GameObject[] playOnlyObjects;
        [Tooltip("On-screen controls: like the HUD, but also hidden while the in-match menu is open (taps go to the menu).")]
        [SerializeField] GameObject[] controlObjects;
        [Tooltip("Dev tools that break the match rules (free class switching). Only on after Dismiss.")]
        [SerializeField] Behaviour[] sandboxOnlyBehaviours;

        [Header("Rules")]
        [Tooltip("Real seconds of 3-2-1 before play. The rest of the rules come from the game mode (MatchMode).")]
        [SerializeField, Min(0f)] float countdownLength = 3f;
        [SerializeField] string[] teamNames = { "West", "East" };
        [SerializeField] Color[] teamColors = { new(0.35f, 0.6f, 1f), new(1f, 0.38f, 0.32f) };

        // Kept across Play Again (a scene reload), so a rematch starts with the same picks.
        static readonly Dictionary<string, BotDifficulty> s_difficulty = new(); // per mode id, read from PlayerPrefs once
        static int s_classIndex;
        static int s_modeIndex;
        static bool s_skipMenu; // Play Again: the reload opens on class select

        MatchClock _clock;
        MatchScore _score;
        MatchMode _mode;
        ModeArena _arena;
        List<Vector3> _levelFightAreas;
        BannerWorld _banners;
        bool _dismissed;
        PlaneRespawner _respawner;
        PlaneHealth _health;
        PlaneRepair _repair;
        bool _playOnlyOn = true, _controlsOn = true; // as the scene is saved
        bool _parked;

        public event Action<MatchPhase> PhaseChanged;

        public MatchPhase Phase => _clock.Phase;
        /// <summary>Seconds left on the countdown (real time) or the match clock (game time).</summary>
        public float TimeLeft => _clock.TimeLeft;
        public MatchScore Score => _score;
        /// <summary>The rules of the mode being played.</summary>
        public MatchMode Mode => _mode;
        /// <summary>The level's arena for the mode, in force from the class pick on (null: the level as built).</summary>
        public ModeArena Arena => _arena;
        /// <summary>Which of <see cref="GameModeInfo.All"/> is being played (-1 for a mode set with <see cref="SetMode"/>).</summary>
        public int ModeIndex { get; private set; }
        /// <summary>The winning team by the mode's rules, or <see cref="MatchScore.Draw"/>.</summary>
        public int Winner => _mode.Winner(_score);
        public PlaneController Player => player;
        public int PlayerTeam => player != null ? player.Team : 0;
        /// <summary>Capture the Banner's banners in the level; null in other modes and before the first frame of a Banner match.</summary>
        public BannerWorld Banners => _banners;
        public int TeamCount => roster != null ? roster.TeamCount : 2;
        public BotRoster Roster => roster;
        public IReadOnlyList<FlightStats> Classes => classes;
        public int SelectedClass => s_classIndex;
        public bool IsDismissed => _dismissed;
        /// <summary>The in-match menu is open (the match runs on; the player's controls are let go).</summary>
        public bool MenuOpen { get; private set; }
        /// <summary>Where the player's plane waits during the menu: its home hangar. Null if the level has none.</summary>
        public Transform HangarSpot => player != null ? Airfield.HomeOf(player.Team)?.HangarSpot : null;

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

        /// <summary>The bots' difficulty for the mode being played (Normal for a mode not on a card).</summary>
        public BotDifficulty Difficulty
        {
            get => _mode?.Info != null ? DifficultyFor(_mode.Info) : BotDifficulty.Normal;
            set
            {
                if (_mode?.Info != null)
                    SetDifficulty(_mode.Info, value);
            }
        }

        /// <summary>A mode's bot difficulty: each card keeps its own, remembered between sessions.</summary>
        public static BotDifficulty DifficultyFor(GameModeInfo mode)
        {
            if (!s_difficulty.TryGetValue(mode.Id, out var d))
            {
                d = (BotDifficulty)Mathf.Clamp(PlayerPrefs.GetInt(mode.DifficultyPref, (int)mode.DefaultDifficulty), 0, 2);
                s_difficulty[mode.Id] = d;
            }
            return d;
        }

        /// <summary>Sets <paramref name="mode"/>'s bot difficulty (saved); the bots change at once if it's the mode being played.</summary>
        public void SetDifficulty(GameModeInfo mode, BotDifficulty value)
        {
            s_difficulty[mode.Id] = value;
            PlayerPrefs.SetInt(mode.DifficultyPref, (int)value);
            if (roster != null && _mode?.Info == mode)
                roster.Difficulty = value;
        }

        public string TeamName(int team) => team >= 0 && team < teamNames.Length ? teamNames[team] : $"Team {team + 1}";
        /// <summary>The team's colour, or its high-contrast one if the player picked that (see <see cref="GameSettings.TeamColor"/>).</summary>
        public Color TeamColor(int team) => GameSettings.TeamColor(team, team >= 0 && team < teamColors.Length ? teamColors[team] : Color.white);

        /// <summary>What the scoreboard calls a pilot: "You" for the player, the plane's name for bots.</summary>
        public string PilotName(PlaneController plane) => plane == player ? "You" : plane.name;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_difficulty.Clear();
            s_classIndex = 0;
            s_modeIndex = 0;
            s_skipMenu = false;
        }

        void Awake()
        {
            _clock = new MatchClock(300f, countdownLength);
            if (s_modeIndex < 0 || s_modeIndex >= GameModeInfo.All.Count || !GameModeInfo.All[s_modeIndex].IsPlayable)
                s_modeIndex = 0;
            ModeIndex = s_modeIndex;
            UseMode(GameModeInfo.All[s_modeIndex].CreateMode());
            if (s_skipMenu)
                _clock.OpenClassSelect();
            s_skipMenu = false;
            if (roster != null)
                roster.enabled = false; // before its Start: no bots until the match starts (UseMode set its difficulty)
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

        void Start()
        {
            if (!_dismissed && _clock.Phase is MatchPhase.Menu or MatchPhase.ClassSelect)
                ParkInHangar();
        }

        /// <summary>The menu backdrop: the player's plane, in the class picked last, parked in its home hangar.</summary>
        void ParkInHangar()
        {
            var spot = HangarSpot;
            if (player == null || spot == null)
                return;
            if (IsClass(s_classIndex) && player.Stats != classes[s_classIndex])
                player.SetStats(classes[s_classIndex]);
            player.SpawnOnGround(spot.position, spot.rotation);
            _parked = true;
        }

        /// <summary>
        /// Game mode screen: plays <see cref="GameModeInfo.All"/>[<paramref name="index"/>] (a fresh score, its length
        /// on the clock). Only from the menu. Remembered for Play Again.
        /// </summary>
        public void SelectMode(int index)
        {
            if (_dismissed || _clock.Phase != MatchPhase.Menu || index < 0 || index >= GameModeInfo.All.Count
                || !GameModeInfo.All[index].IsPlayable)
                return;
            s_modeIndex = index;
            ModeIndex = index;
            UseMode(GameModeInfo.All[index].CreateMode());
        }

        /// <summary>Plays <paramref name="mode"/>, which isn't on a card (tests, modes in the works). Only from the menu.</summary>
        public void SetMode(MatchMode mode)
        {
            if (_dismissed || _clock.Phase != MatchPhase.Menu || mode == null)
                return;
            ModeIndex = -1;
            UseMode(mode);
        }

        void UseMode(MatchMode mode)
        {
            DropBanners();
            _mode = mode;
            _score = mode.CreateScore(TeamCount);
            if (mode.Info != null)
                _clock.MatchLength = mode.Info.Length;
            if (roster != null)
                roster.Difficulty = Difficulty;
        }

        // ISpawnRules: the mode's, while a match is on (PlaneRespawner.Rules is this from OnEnable to Dismiss / OnDisable).
        bool ISpawnRules.CanRespawn(PlaneController plane) =>
            _dismissed || _clock.Phase != MatchPhase.Playing || _mode.CanRespawn(_score.Get(plane));

        bool ISpawnRules.TryGetSpawnPose(PlaneController plane, Vector3 deathPosition, out Pose pose, out bool onRunway)
        {
            pose = default;
            onRunway = false;
            if (_dismissed)
                return false;
            return _mode.TryGetSpawnPose(plane, deathPosition, out pose, out onRunway)
                || (_arena != null && _arena.TryGetSpawnPose(plane, out pose));
        }

        void ISpawnRules.OnSpawned(PlaneController plane)
        {
            if (!_dismissed)
                _mode.OnSpawned(plane);
        }

        void OnEnable()
        {
            PlaneRespawner.Rules = this;
            PlaneVisuals.TeamColors = TeamColor;
            PlaneHealth.AnyKilled += OnAnyKilled;
            InputSystem.onDeviceChange += OnDeviceChange;
            if (_respawner != null)
                _respawner.Spawning += OnPlayerSpawning;
        }

        void OnDisable()
        {
            if (ReferenceEquals(PlaneRespawner.Rules, this))
                PlaneRespawner.Rules = null;
            if (PlaneVisuals.TeamColors?.Target == (object)this)
                PlaneVisuals.TeamColors = null;
            PlaneHealth.AnyKilled -= OnAnyKilled;
            InputSystem.onDeviceChange -= OnDeviceChange;
            if (_respawner != null)
                _respawner.Spawning -= OnPlayerSpawning;
            DropArena();
            DropBanners();
            Time.timeScale = 1f; // never leave the game frozen (scene unload, tests)
        }

        void Update()
        {
            if (_dismissed)
                return;
            float dt = _clock.Phase == MatchPhase.Countdown ? Time.unscaledDeltaTime : Time.deltaTime;
            if (_clock.Tick(dt))
                ApplyPhase();
            if (_clock.Phase != MatchPhase.Playing)
                return;
            _mode.Tick(_score, dt);
            TickBanners(dt);
            if (_mode.EarlyWinner(_score) != null)
            {
                EndMatch();
                return;
            }
            UpdatePicker();
        }

        /// <summary>Capture the Banner: the stands' banners, towed banners and carrier drag run in the level (made on first use).</summary>
        void TickBanners(float dt)
        {
            if (_mode is not BannerMode banner)
                return;
            _banners ??= BannerWorld.Create(banner, TeamCount);
            _banners?.Tick(_score, dt);
        }

        void DropBanners()
        {
            _banners?.Dispose();
            _banners = null;
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
            if (picker == ClassPicker.Respawn)
                MenuOpen = false; // crashing shows only the plane select, never the pause menu
            RefreshPlayOnly();
        }

        /// <summary>
        /// Opens the in-match menu (countdown or play). The match keeps running: the player's controls let go of the
        /// stick and the mouse is freed; the HUD stays up behind the menu.
        /// </summary>
        public void OpenMenu()
        {
            if (_dismissed || MenuOpen || Picker == ClassPicker.Respawn || _clock.Phase is not (MatchPhase.Countdown or MatchPhase.Playing))
                return;
            MenuOpen = true;
            RefreshPlayOnly();
        }

        public void CloseMenu()
        {
            if (!MenuOpen)
                return;
            MenuOpen = false;
            RefreshPlayOnly();
        }

        // Platform guidelines: a lost controller or a backgrounded app opens the menu (the match still runs on).
        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is Gamepad && change is InputDeviceChange.Removed or InputDeviceChange.Disconnected)
                OpenMenu();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
                OpenMenu();
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

        /// <summary>Menu → class select: the game mode was confirmed.</summary>
        public void OpenClassSelect()
        {
            if (_clock.OpenClassSelect())
                ApplyPhase();
        }

        /// <summary>Class select → menu (the game mode screen). The hangar shows the class picked last again.</summary>
        public void BackToMenu()
        {
            if (!_clock.BackToMenu())
                return;
            if (_parked)
                ParkInHangar();
            ApplyPhase();
        }

        /// <summary>
        /// Class select: parks <see cref="Classes"/>[<paramref name="index"/>] in the hangar in place of the plane there,
        /// so the highlighted card is seen in the world. Nothing is picked until <see cref="SelectClass"/>.
        /// </summary>
        public void PreviewClass(int index)
        {
            if (_dismissed || !_parked || _clock.Phase != MatchPhase.ClassSelect || !IsClass(index) || player.Stats == classes[index])
                return;
            var spot = HangarSpot;
            if (spot == null)
                return;
            player.SetStats(classes[index]);
            player.SpawnOnGround(spot.position, spot.rotation);
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
            UseArena(); // before anyone spawns: an arena can move the spawns
            if (player != null)
            {
                player.SetStats(classes[index]);
                if (player.TryGetComponent<PlaneRespawner>(out var respawner))
                    respawner.SpawnNow();
                _parked = false;
            }
            if (roster != null)
            {
                roster.enabled = true;
                roster.Refresh(); // the bots appear now, frozen in place for the countdown
            }
            _clock.StartCountdown();
            ApplyPhase();
        }

        /// <summary>
        /// Puts the mode's arena in force for the match (<see cref="MatchMode.ArenaId"/>): its play area, closed
        /// airfields and bot patrols. Its air start comes through <see cref="ISpawnRules.TryGetSpawnPose"/>.
        /// </summary>
        void UseArena()
        {
            DropArena();
            _arena = ModeArena.For(_mode.ArenaId);
            if (_arena == null)
                return;
            _arena.Apply();
            if (roster != null && _arena.FightAreas.Count > 0)
            {
                _levelFightAreas = new List<Vector3>(roster.FightAreas);
                roster.SetFightAreas(_arena.FightAreas);
            }
        }

        /// <summary>The level as built again: its own play area, airfields and patrols.</summary>
        void DropArena()
        {
            if (_arena != null)
                _arena.Restore();
            _arena = null;
            if (_levelFightAreas != null && roster != null)
                roster.SetFightAreas(_levelFightAreas);
            _levelFightAreas = null;
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

        /// <summary>A rematch: reloads the level straight to class select, keeping the mode, difficulty and class picked.</summary>
        public void PlayAgain()
        {
            s_skipMenu = true;
            Reload();
        }

        /// <summary>Back to the main menu: from the scoreboard, or leaving a match (offline the team just plays on without you).</summary>
        public void ReturnToMainMenu()
        {
            s_skipMenu = false;
            Reload();
        }

        void Reload()
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
            if (ReferenceEquals(PlaneRespawner.Rules, this))
                PlaneRespawner.Rules = null;
            Time.timeScale = 1f;
            Picker = ClassPicker.None;
            MenuOpen = false;
            DropArena(); // free flight is the level as built
            DropBanners();
            RefreshPlayOnly();
            if (_parked && player != null && player.TryGetComponent<PlaneRespawner>(out var respawner))
                respawner.SpawnNow(); // out of the hangar, as free flight always started
            _parked = false;
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
            if (_clock.Phase is not (MatchPhase.Countdown or MatchPhase.Playing))
                MenuOpen = false; // time ran out with it open: the scoreboard takes over
            RefreshPlayOnly();
            if (playing)
                RegisterPilots();
            if (_clock.Phase == MatchPhase.Ended)
                RegisterPilots(); // anyone who joined late and never died still gets a line
            PhaseChanged?.Invoke(_clock.Phase);
        }

        /// <summary>
        /// The flight HUD shows in play with no class picker up (the picker frees the mouse, and the HUD would draw over
        /// it); the controls and on-screen controls also need the in-match menu closed. Dismissed, all of it is on.
        /// </summary>
        void RefreshPlayOnly()
        {
            bool hud = _dismissed || (_clock.Phase == MatchPhase.Playing && Picker == ClassPicker.None);
            bool controls = hud && !MenuOpen;
            if (hud != _playOnlyOn)
            {
                _playOnlyOn = hud;
                foreach (var go in playOnlyObjects)
                    if (go != null)
                        go.SetActive(hud);
            }
            if (controls != _controlsOn)
            {
                _controlsOn = controls;
                foreach (var b in playOnlyBehaviours)
                    if (b != null)
                        b.enabled = controls;
                foreach (var go in controlObjects)
                    if (go != null)
                        go.SetActive(controls);
            }
        }

        PilotRef Ref(PlaneController plane) => new(plane, PilotName(plane), plane.Team);

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
            _banners?.CarrierDown(victim);
            int? killerTeam = kill.Killer != null ? kill.Killer.Team
                : kill.Source is AAGun aa ? aa.Team
                : null;
            var helpers = new List<PilotRef>();
            foreach (var helper in kill.Assisters)
                if (helper != null)
                    helpers.Add(Ref(helper));
            _mode.RecordKill(_score, new KillRecord(Ref(victim), kill.Killer != null ? Ref(kill.Killer) : default, killerTeam, helpers));
        }
    }
}
