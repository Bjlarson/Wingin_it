using UnityEngine;
using WingingIt.Flight;
using WingingIt.Settings;
using WingingIt.World;
using static WingingIt.UI.GlassGUI;

namespace WingingIt.UI
{
    // Center messages (death, boundary, AA fire), the status toast above the cluster (touchdown, takeoff, repair) and
    // the cloud wash.
    public partial class FlightHUD
    {
        const float TouchdownMessageTime = 2.5f;
        const float RepairMessageTime = 2.5f;
        static readonly Color RepairBlue = new(0.45f, 0.9f, 1f);

        float _touchdownTime = float.NegativeInfinity;
        float _repairEndTime = float.NegativeInfinity;
        bool _repairCompleted;
        DamageFeedback _feedback;

        void OnTouchedDown(PlaneController _, TouchdownInfo info) => _touchdownTime = Time.time;
        void OnRepairCompleted(PlaneRepair _) => (_repairEndTime, _repairCompleted) = (Time.time, true);
        void OnRepairCancelled(PlaneRepair _) => (_repairEndTime, _repairCompleted) = (Time.time, false);

        /// <summary>The big flashing line under the score bar: death, boundary, ceiling or AA fire. Null when none.</summary>
        public string CenterWarning
        {
            get
            {
                if (plane == null)
                    return null;
                if (!plane.IsAlive)
                {
                    var kill = health != null ? health.LastKill : default;
                    string killer = kill.Killer != null ? PilotName(kill.Killer) : kill.Source is AAGun aa ? $"{TeamName(aa.Team)} AA" : null;
                    TouchdownFault? crashFault = plane.LastDeathCause == DeathCause.Collision && plane.HasTouchedDown
                        && plane.LastTouchdown.Result == TouchdownResult.Crashed ? plane.LastTouchdown.Fault : null;
                    string death = HudText.Death(plane.LastDeathCause == DeathCause.ShotDown, crashFault, killer);
                    return respawner != null ? $"{death}  —  RESPAWN IN {respawner.RespawnTimeLeft:0.0}" : death;
                }
                if (plane.IsOutOfBounds)
                    return "LEAVING COMBAT AREA — TURNING BACK";
                if (plane.IsNearBoundary)
                    return "COMBAT AREA EDGE";
                if (plane.IsNearCeiling)
                    return "ALTITUDE LIMIT";
                if (IsUnderAAFire())
                    return "AA FIRE — GET OUT FAST OR LOW";
                return null;
            }
        }

        bool IsUnderAAFire()
        {
            foreach (var gun in AAGun.All)
                if (gun.Target == plane && gun.IsFiring)
                    return true;
            return false;
        }

        void DrawCenterWarning(float width)
        {
            string warning = CenterWarning;
            if (warning == null)
                return;
            Color color = GameSettings.Blink(Time.time, 0.8f, 0.6875f) ? Red : Color.white;
            _g.Shadowed(new Rect(0f, 136f, width, 48f), warning, _g.Title, 4f, TextAnchor.MiddleCenter, color, 0.85f);
        }

        /// <summary>
        /// A glass toast above the instrument cluster: the touchdown result (green landed, yellow bounced), READY FOR
        /// TAKEOFF on a runway spawn, the repair timer and shield while parked in our zone, then REPAIRED / CANCELLED.
        /// Moves up out of the way while <see cref="DamageFeedback"/> shows RETURN TO BASE there.
        /// </summary>
        void DrawStatusToast(float width)
        {
            if (!plane.IsAlive)
                return;
            string title = null, caption = null;
            Color color = Green;
            float progress = -1f;

            if (repair != null && repair.IsRepairing)
            {
                title = $"REPAIRING  {repair.Tier.ToString().ToUpperInvariant()}  {repair.TimeLeft:0.0} S";
                color = RepairBlue;
                progress = repair.Progress;
                if (health != null && health.IsInvulnerable)
                    caption = "SHIELDED: INVULNERABLE UNTIL REPAIRED";
            }
            else if (repair != null && Time.time - _repairEndTime < RepairMessageTime)
            {
                title = _repairCompleted ? "REPAIRED" : "REPAIR CANCELLED";
                caption = _repairCompleted ? null : "LEFT THE ZONE";
                color = _repairCompleted ? Green : Amber;
            }
            else if (plane.HasTouchedDown && Time.time - _touchdownTime < TouchdownMessageTime)
            {
                var td = plane.LastTouchdown;
                bool landed = td.Result == TouchdownResult.Landed;
                title = landed ? "LANDED" : $"BOUNCED: {HudText.Fault(td.Fault)}";
                caption = landed ? $"{td.SinkRate:0.0} M/S SINK" : $"-{plane.LandingRules.bounceDamage * 100f:0}% BODY";
                color = landed ? Green : Amber;
            }
            else if (plane.IsGrounded && !plane.HasTouchedDown && plane.Speed < 0.1f)
            {
                title = "READY FOR TAKEOFF";
                caption = "THROTTLE UP OR BOOST, PULL UP AT FLYING SPEED";
            }
            if (title == null)
                return;

            float w = Mathf.Max(_g.TrackedWidth(title, _g.Item, 3f, 1.2f), caption != null ? _g.TrackedWidth(caption, _g.Hint, 2f, 0.85f) : 0f) + 64f;
            float h = 50f + (caption != null ? 20f : 0f) + (progress >= 0f ? 16f : 0f);
            float bottom = _clusterRect.y - 14f - (_feedback != null && _feedback.ShowingReturnToBase ? 96f : 0f);
            var panel = new Rect((width - w) / 2f, bottom - h, w, h);
            _g.Panel(panel, GlowOpen, false);
            float y = panel.y + 12f;
            _g.Tracked(new Rect(panel.x, y, panel.width, 28f), title, _g.Item, 3f, TextAnchor.MiddleCenter, color, 1.2f);
            y += 30f;
            if (progress >= 0f)
            {
                var bar = new Rect(panel.x + 32f, y + 2f, panel.width - 64f, 8f);
                _g.RoundFill(bar, 3f, Alpha(Color.white, 0.12f));
                _g.RoundFill(new Rect(bar.x, bar.y, Mathf.Max(6f, bar.width * Mathf.Clamp01(progress)), bar.height), 3f, color);
                y += 16f;
            }
            if (caption != null)
                _g.Tracked(new Rect(panel.x, y, panel.width, 20f), caption, _g.Hint, 2f, TextAnchor.MiddleCenter,
                    progress >= 0f && health != null && health.IsInvulnerable ? Shield : Steel, 0.85f);
        }

        /// <summary>
        /// Flying through cloud: the screen washes out white the deeper the camera is in, and a line under the crosshair
        /// says whether the plane is hidden (locks, missiles and AA can't see it) or only in the edge of the cloud.
        /// </summary>
        void DrawCloudWash(float width)
        {
            Vector3 eye = viewCamera != null ? viewCamera.transform.position : plane.transform.position;
            float immersion = CloudCover.Immersion(eye);
            if (immersion > 0f)
                _g.Fill(new Rect(0f, 0f, width, _height), new Color(0.95f, 0.96f, 0.98f, Mathf.Lerp(0.35f, 0.85f, Mathf.Clamp01(immersion * 2f))));
        }

        void DrawCloudStatus(float width)
        {
            if (!plane.IsAlive || !CloudCover.IsInside(plane.transform.position))
                return;
            bool hidden = CloudCover.IsHidden(plane.transform.position);
            // Dark text: it sits on the white wash.
            _g.Tracked(new Rect(0f, _height * 0.5f + 56f, width, 22f), hidden ? "IN CLOUD: HIDDEN FROM LOCKS AND AA" : "CLOUD EDGE: STILL VISIBLE",
                _g.Hint, 2.4f, TextAnchor.MiddleCenter, hidden ? new Color(0.15f, 0.3f, 0.55f) : new Color(0.3f, 0.35f, 0.45f), 0.95f);
        }

        /// <summary>Mouse aim needs the cursor captured: say so above the crosshair until it is.</summary>
        void DrawMouseCaptureHint(float width)
        {
            if (player == null || !player.enabled || player.TouchControlsActive || player.UsingGamepad || Cursor.lockState == CursorLockMode.Locked)
                return;
            _g.Shadowed(new Rect(0f, _height * 0.5f - 150f, width, 22f), "CLICK TO CAPTURE THE MOUSE  ·  ESC RELEASES IT", _g.Hint, 2f,
                TextAnchor.MiddleCenter, Alpha(White, 0.9f), 0.95f);
        }
    }
}
