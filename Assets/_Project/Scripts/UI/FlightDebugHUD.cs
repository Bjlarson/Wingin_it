using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using WingingIt.Dev;
using WingingIt.Flight;
using WingingIt.Input;
using WingingIt.Settings;
using WingingIt.World;

namespace WingingIt.UI
{
    /// <summary>
    /// Dev overlay drawn with IMGUI beside the real <see cref="FlightHUD"/>: a few dev readouts (bottom left) and the F1
    /// help, both only with dev tools on.
    /// </summary>
    public class FlightDebugHUD : MonoBehaviour
    {
        [SerializeField] PlaneController plane;
        [SerializeField] PlayerFlightInput player;
        [SerializeField] bool showHelp = true;

        GUIStyle _tiny, _small;
        const float ReferenceHeight = 1080f;

        void Awake() => useGUILayout = false; // no GUILayout here: skips IMGUI's layout pass

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame && GameSettings.DevToolsOn)
                showHelp = !showHelp;
        }

        void OnGUI()
        {
            if (plane == null || !GameSettings.DevToolsOn || Event.current.type != EventType.Repaint)
                return;
            EnsureStyles();

            float scale = Screen.height / ReferenceHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            DrawDevPanel();
            if (showHelp)
                DrawHelp();
        }

        /// <summary>
        /// Bottom left, dev info the real HUD doesn't show: the control mode and held buttons, ring progress, the nearest
        /// landmark, the nearest friendly field (with the auto-brake stop distance) and who its AA is shooting at.
        /// </summary>
        void DrawDevPanel()
        {
            var lines = new List<string>();
            string mode = player == null ? "AI"
                : player.TouchControlsActive ? "Touch"
                : player.UsingGamepad ? "Gamepad"
                : player.Mode == ControlMode.MouseAim ? "Mouse aim" : player.Mode == ControlMode.Follow ? "Follow" : "Direct";
            string weapons = player == null ? "" :
                $"{(player.FireGunsHeld ? " [GUNS]" : "")}{(player.FireMissileHeld ? " [MSL]" : "")}" +
                $"{(player.FireSecondaryHeld ? " [SEC]" : "")}{(player.DropBombHeld ? " [BOMB]" : "")}" +
                $"{(player.AbilityHeld ? " [ABILITY]" : "")}";
            lines.Add($"Controls: {mode}{weapons}{(showHelp ? "" : "    F1  help")}");
            if (RingGate.TotalCount > 0)
                lines.Add($"Rings {RingGate.PassedCount}/{RingGate.TotalCount}");
            var landmark = Landmark.Nearest(plane.transform.position);
            if (landmark != null)
            {
                Vector3 d = landmark.transform.position - plane.transform.position;
                lines.Add($"Near {landmark.DisplayName} ({landmark.Region}) {new Vector2(d.x, d.z).magnitude / 1000f:0.0} km");
            }
            var home = Airfield.NearestFriendly(plane.transform.position, plane.Team);
            if (home != null)
            {
                float km = Vector3.Distance(plane.transform.position, home.transform.position) / 1000f;
                bool inZone = home.RepairZone != null && home.RepairZone.Contains(plane.transform.position);
                string stop = plane.IsAutoBraking && plane.AutoBrakeStopDistance is float stopAt && plane.Speed >= 0.1f
                    ? $"  zone stop {Mathf.Max(0f, stopAt):0} m" : "";
                lines.Add($"{home.DisplayName} {km:0.0} km{(inZone ? "  [REPAIR ZONE]" : "")}{stop}");
                string engaging = HomeAAEngaging(home);
                if (engaging != null)
                    lines.Add($"Home AA engaging {engaging}");
            }

            const float lineHeight = 22f;
            float bottom = player != null && player.TouchControlsActive ? ReferenceHeight - 440f : ReferenceHeight - 24f; // above the touch stick
            float x = 30f, y = bottom - lines.Count * lineHeight;
            Box(new Rect(x - 10f, y - 6f, 400f, lines.Count * lineHeight + 12f), new Color(0f, 0f, 0f, 0.3f));
            foreach (var line in lines)
            {
                GUI.Label(new Rect(x, y - 2f, 390f, 24f), line, _tiny);
                y += lineHeight;
            }
        }

        /// <summary>The planes our home airfield's AA guns are shooting at, or null if none.</summary>
        static string HomeAAEngaging(Airfield home)
        {
            string names = null;
            foreach (var gun in AAGun.All)
            {
                if (gun.Airfield != home || gun.Target == null || !gun.IsFiring)
                    continue;
                string n = gun.Target.name;
                if (names == null)
                    names = n;
                else if (!names.Contains(n))
                    names += $", {n}";
            }
            return names;
        }

        void DrawHelp()
        {
            string help =
                "F1  hide help\n" +
                "Mouse  aim (mouse-aim) / pitch (direct)\n" +
                "W/S  throttle    A/D  roll    Q/E  yaw    Arrows  pitch/roll\n" +
                "Shift  boost    C  free look    M  switch mouse-aim / direct\n" +
                "1-4 or Tab  switch class    F2  touch controls\n" +
                "5-0  damage body / L wing / R wing / H stab / V stab / engine\n" +
                "Backspace  restore parts\n" +
                "Space / LMB  guns (put the + on the red pipper)\n" +
                "R / MMB  rockets, Heavy/Support (orange diamond = lead)\n" +
                "RMB  missile, Fighter/Interceptor (hold on the nose to lock)\n" +
                "K  nearest plane fires a missile at you (turn hard to dodge)\n" +
                "T  a target drone flies straight ahead of you for 20 s\n" +
                "L  final approach to your runway (gear down, idle)\n" +
                "    approach: fly through the rings, box dot = glide path\n" +
                $"N  spawn: {(LevelSettings.CurrentSpawnMode == SpawnMode.Air ? "AIR" : "RUNWAY")} (switch + respawn; W/Shift to take off)\n" +
                $"J  your home AA shoots at you: {(AAGun.DevHostilePlane == plane ? "ON" : "OFF")}\n" +
                $"Y  drones break when hit: {(DamageTester.DronesEvade ? "ON" : "OFF")}\n" +
                "B  bomb, Heavy (ground circle = where it lands)\n" +
                "F  ability: Fighter barrel roll (breaks locks + missiles),\n" +
                "    Interceptor afterburner, Heavy armor shield, Support repair aura\n" +
                "Green diamonds = enemies on your Support's radar (sees through clouds)\n" +
                "Clouds: fly deep inside to break locks, missiles and AA";
            var rect = new Rect(20f, 300f, 620f, 555f); // under the minimap
            Box(rect, new Color(0f, 0f, 0f, 0.35f));
            GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, rect.height - 16f), help, _small);
        }

        static void Box(Rect r, Color color)
        {
            var c = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = c;
        }

        void EnsureStyles()
        {
            if (_small != null)
                return;
            _small = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Normal };
            _small.normal.textColor = Color.white;
            _tiny = new GUIStyle(_small) { fontSize = 15 };
        }
    }
}
