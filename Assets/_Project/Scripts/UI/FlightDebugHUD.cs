using UnityEngine;
using UnityEngine.InputSystem;
using WingingIt.CameraSystem;
using WingingIt.Flight;
using WingingIt.Input;
using WingingIt.World;

namespace WingingIt.UI
{
    /// <summary>
    /// Prototype HUD drawn with IMGUI: speed, throttle, boost, warnings, and mouse-aim reticles.
    /// The real HUD (Phase 6) replaces this.
    /// </summary>
    public class FlightDebugHUD : MonoBehaviour
    {
        [SerializeField] PlaneController plane;
        [SerializeField] PlayerFlightInput player;
        [SerializeField] FlightCameraRig cameraRig;
        [SerializeField] PlaneRespawner respawner;
        [SerializeField] Camera viewCamera;
        [SerializeField] bool showHelp = true;

        GUIStyle _label, _title, _big, _small;
        const float ReferenceHeight = 1080f;

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame)
                showHelp = !showHelp;
        }

        void OnGUI()
        {
            if (plane == null)
                return;
            EnsureStyles();

            float scale = Screen.height / ReferenceHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;

            DrawReticles(scale);
            DrawFlightPanel();
            DrawWarnings(width);
            if (showHelp)
                DrawHelp(width);
        }

        void DrawFlightPanel()
        {
            float x = 30f, y = ReferenceHeight - 230f;
            Box(new Rect(x - 12f, y - 12f, 380f, 215f), new Color(0f, 0f, 0f, 0.35f));

            string cls = plane.Stats != null ? plane.Stats.displayName : "?";
            GUI.Label(new Rect(x, y, 360f, 40f), cls.ToUpperInvariant(), _title);
            y += 44f;
            GUI.Label(new Rect(x, y, 360f, 28f), $"SPD {plane.Speed * 3.6f:0} km/h    ALT {plane.transform.position.y:0} m", _label);
            y += 34f;
            Bar(new Rect(x, y, 260f, 16f), plane.Throttle, new Color(0.9f, 0.9f, 0.9f), "THR");
            y += 26f;
            Bar(new Rect(x, y, 260f, 16f), plane.BoostMeter,
                plane.IsBoosting ? new Color(1f, 0.55f, 0.1f) : new Color(0.3f, 0.75f, 1f), "BST");
            y += 30f;

            string mode = player == null ? "AI"
                : player.TouchControlsActive ? "Touch"
                : player.UsingGamepad ? "Gamepad"
                : player.Mode == ControlMode.MouseAim ? "Mouse aim" : "Direct";
            string weapons = player == null ? "" :
                $"{(player.FireGunsHeld ? " [GUNS]" : "")}{(player.FireMissileHeld ? " [MSL]" : "")}" +
                $"{(player.FireSecondaryHeld ? " [SEC]" : "")}{(player.AbilityHeld ? " [ABILITY]" : "")}";
            GUI.Label(new Rect(x, y, 360f, 24f), $"Controls: {mode}{weapons}", _small);
            y += 24f;
            if (RingGate.TotalCount > 0)
                GUI.Label(new Rect(x, y, 360f, 24f), $"Rings {RingGate.PassedCount}/{RingGate.TotalCount}", _small);
        }

        void DrawWarnings(float width)
        {
            string warning = null;
            if (!plane.IsAlive)
                warning = respawner != null ? $"CRASHED  —  respawn in {respawner.RespawnTimeLeft:0.0}" : "CRASHED";
            else if (plane.IsOutOfBounds)
                warning = "LEAVING COMBAT AREA — TURNING BACK";
            else if (plane.IsNearBoundary)
                warning = "COMBAT AREA EDGE";
            else if (plane.IsNearCeiling)
                warning = "ALTITUDE LIMIT";
            else if (plane.LiftFactor < 0.95f)
                warning = "LOW SPEED";

            if (warning != null)
            {
                var c = GUI.color;
                GUI.color = Mathf.Repeat(Time.time, 0.8f) < 0.55f ? new Color(1f, 0.35f, 0.25f) : Color.white;
                GUI.Label(new Rect(0f, 140f, width, 50f), warning, _big);
                GUI.color = c;
            }

            if (player != null && !player.TouchControlsActive && !player.UsingGamepad &&
                Cursor.lockState != CursorLockMode.Locked)
                GUI.Label(new Rect(0f, ReferenceHeight * 0.5f + 60f, width, 30f), "Click to capture the mouse  (Esc releases it)", _label);
        }

        void DrawHelp(float width)
        {
            const string help =
                "F1  hide help\n" +
                "Mouse  aim (mouse-aim) / pitch (direct)\n" +
                "W/S  throttle    A/D  roll    Q/E  yaw    Arrows  pitch/roll\n" +
                "Shift  boost    C  free look    M  switch mouse-aim / direct\n" +
                "1-4 or Tab  switch class    F2  touch controls\n" +
                "Space/LMB guns, RMB missile, R secondary, F ability (Phase 2)";
            var rect = new Rect(width - 640f, 20f, 620f, 160f);
            Box(rect, new Color(0f, 0f, 0f, 0.35f));
            GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, rect.height - 16f), help, _small);
        }

        void DrawReticles(float scale)
        {
            if (viewCamera == null || cameraRig == null || !plane.IsAlive)
                return;

            // Where the nose is pointing (always) and where the mouse is aiming (mouse-aim only).
            DrawMarker(cameraRig.BoresightPoint, scale, 10f, new Color(1f, 1f, 1f, 0.9f), cross: true);
            if (player != null && player.MouseAimActive)
                DrawMarker(cameraRig.AimPoint, scale, 18f, new Color(0.4f, 1f, 0.5f, 0.9f), cross: false);
        }

        void DrawMarker(Vector3 world, float scale, float size, Color color, bool cross)
        {
            Vector3 sp = viewCamera.WorldToScreenPoint(world);
            if (sp.z <= 0f)
                return;
            Vector2 p = new Vector2(sp.x, Screen.height - sp.y) / scale;
            if (cross)
            {
                Box(new Rect(p.x - size, p.y - 1f, size * 2f, 2f), color);
                Box(new Rect(p.x - 1f, p.y - size, 2f, size * 2f), color);
            }
            else
            {
                float t = 2f;
                Box(new Rect(p.x - size, p.y - size, size * 2f, t), color);
                Box(new Rect(p.x - size, p.y + size - t, size * 2f, t), color);
                Box(new Rect(p.x - size, p.y - size, t, size * 2f), color);
                Box(new Rect(p.x + size - t, p.y - size, t, size * 2f), color);
            }
        }

        void Bar(Rect r, float value, Color color, string label)
        {
            GUI.Label(new Rect(r.x, r.y - 5f, 50f, 24f), label, _small);
            var bar = new Rect(r.x + 50f, r.y, r.width, r.height);
            Box(bar, new Color(1f, 1f, 1f, 0.15f));
            Box(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(value), bar.height), color);
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
            if (_label != null)
                return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            _label.normal.textColor = Color.white;
            _title = new GUIStyle(_label) { fontSize = 34 };
            _big = new GUIStyle(_title) { alignment = TextAnchor.UpperCenter };
            _small = new GUIStyle(_label) { fontSize = 18, fontStyle = FontStyle.Normal };
        }
    }
}
