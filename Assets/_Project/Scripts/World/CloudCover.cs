using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.World
{
    /// <summary>One ellipsoid of a cloud bank: its center (relative to the bank) and its three radii.</summary>
    [System.Serializable]
    public struct CloudPuff
    {
        public Vector3 center;
        public Vector3 radii;

        public CloudPuff(Vector3 center, Vector3 radii)
        {
            this.center = center;
            this.radii = radii;
        }
    }

    /// <summary>
    /// Clouds as cover (island-map.md, Clouds). A sight line that passes through at least <see cref="BlockDepth"/> m of
    /// cloud is blocked: missile seekers, AA gunners and the HUD can't see through it. Clouds have no colliders, so
    /// planes, bullets, rockets and bombs fly straight through them. Pure math here; <see cref="CloudBank"/> holds the
    /// puffs in the scene.
    /// </summary>
    public static class CloudCover
    {
        /// <summary>Cloud a sight line must cross (m) to be blocked. Thinner wisps, and a plane just inside the edge, stay visible.</summary>
        public const float BlockDepth = 40f;

        /// <summary>How far a hidden-check looks out from a point (m): past the far side of any cloud bank.</summary>
        const float HiddenProbeRange = 1500f;

        static readonly Vector3[] HiddenProbeDirections = BuildProbeDirections();
        static readonly List<Vector2> s_intervals = new();

        /// <summary>True if <see cref="BlockDepth"/> m or more of cloud lies between the two points.</summary>
        public static bool Blocks(Vector3 from, Vector3 to) => ChordLength(from, to, BlockDepth) >= BlockDepth;

        /// <summary>Whether there's a clear (not cloud-blocked) view between two points. Terrain isn't checked here.</summary>
        public static bool CanSee(Vector3 from, Vector3 to) => !Blocks(from, to);

        /// <summary>
        /// Total length (m) of the segment that is inside any cloud. Stops adding banks once <paramref name="stopAt"/>
        /// is reached (pass <see cref="float.MaxValue"/> for the exact total).
        /// </summary>
        public static float ChordLength(Vector3 from, Vector3 to, float stopAt = float.MaxValue)
        {
            float total = 0f;
            var banks = CloudBank.All;
            for (int i = 0; i < banks.Count && total < stopAt; i++)
            {
                var bank = banks[i];
                if (!SegmentNearSphere(from, to, bank.BoundsCenter, bank.BoundsRadius))
                    continue;
                total += ChordLength(bank.Puffs, bank.transform.position, from, to);
            }
            return total;
        }

        /// <summary>True if the point is inside any cloud puff.</summary>
        public static bool IsInside(Vector3 point) => Immersion(point) > 0f;

        /// <summary>
        /// 0 outside every cloud, rising to 1 at the middle of a puff (how far in, relative to that puff's size). Drives
        /// the in-cloud whiteout.
        /// </summary>
        public static float Immersion(Vector3 point)
        {
            float best = 0f;
            foreach (var bank in CloudBank.All)
            {
                if ((point - bank.BoundsCenter).sqrMagnitude > bank.BoundsRadius * bank.BoundsRadius)
                    continue;
                best = Mathf.Max(best, Immersion(bank.Puffs, bank.transform.position, point));
            }
            return best;
        }

        /// <summary>
        /// True if the point is deep enough in cloud to be hidden from every direction: a sight line to it from
        /// anywhere outside crosses at least <see cref="BlockDepth"/> m of cloud (checked along 26 directions).
        /// </summary>
        public static bool IsHidden(Vector3 point)
        {
            if (!IsInside(point))
                return false;
            foreach (var dir in HiddenProbeDirections)
                if (!Blocks(point, point + dir * HiddenProbeRange))
                    return false;
            return true;
        }

        // ------------------------------------------------------------------ pure math

        /// <summary>
        /// Length (m) of the segment inside the union of <paramref name="puffs"/> (offset by <paramref name="origin"/>).
        /// Overlapping puffs aren't counted twice.
        /// </summary>
        public static float ChordLength(IReadOnlyList<CloudPuff> puffs, Vector3 origin, Vector3 from, Vector3 to)
        {
            float length = Vector3.Distance(from, to);
            if (length < 1e-4f || puffs == null)
                return 0f;

            s_intervals.Clear();
            for (int i = 0; i < puffs.Count; i++)
                if (SegmentInterval(puffs[i], origin, from, to, out float t0, out float t1))
                    s_intervals.Add(new Vector2(t0, t1));
            if (s_intervals.Count == 0)
                return 0f;

            s_intervals.Sort((a, b) => a.x.CompareTo(b.x));
            float covered = 0f, start = s_intervals[0].x, end = s_intervals[0].y;
            for (int i = 1; i < s_intervals.Count; i++)
            {
                var iv = s_intervals[i];
                if (iv.x > end)
                {
                    covered += end - start;
                    start = iv.x;
                }
                end = Mathf.Max(end, iv.y);
            }
            covered += end - start;
            return covered * length;
        }

        /// <summary>
        /// The part of the segment (as fractions 0..1 of it) inside one ellipsoid puff. False if it misses.
        /// </summary>
        public static bool SegmentInterval(CloudPuff puff, Vector3 origin, Vector3 from, Vector3 to, out float t0, out float t1)
        {
            t0 = t1 = 0f;
            Vector3 r = puff.radii;
            if (r.x <= 0f || r.y <= 0f || r.z <= 0f)
                return false;

            // Scale space so the puff is a unit sphere; a line stays a line, with the same parameter t.
            Vector3 c = origin + puff.center;
            Vector3 p = Div(from - c, r);
            Vector3 d = Div(to - from, r);
            float a = Vector3.Dot(d, d);
            float b = 2f * Vector3.Dot(p, d);
            float cc = Vector3.Dot(p, p) - 1f;
            if (a < 1e-12f)
                return false;
            float disc = b * b - 4f * a * cc;
            if (disc <= 0f)
                return false;
            float sq = Mathf.Sqrt(disc);
            t0 = Mathf.Max(0f, (-b - sq) / (2f * a));
            t1 = Mathf.Min(1f, (-b + sq) / (2f * a));
            return t1 > t0;
        }

        /// <summary>0 outside, up to 1 at the center of the most deeply entered puff.</summary>
        public static float Immersion(IReadOnlyList<CloudPuff> puffs, Vector3 origin, Vector3 point)
        {
            float best = 0f;
            for (int i = 0; i < puffs.Count; i++)
            {
                var puff = puffs[i];
                if (puff.radii.x <= 0f || puff.radii.y <= 0f || puff.radii.z <= 0f)
                    continue;
                float distance = Div(point - origin - puff.center, puff.radii).magnitude;
                best = Mathf.Max(best, 1f - distance);
            }
            return best;
        }

        static Vector3 Div(Vector3 a, Vector3 b) => new Vector3(a.x / b.x, a.y / b.y, a.z / b.z);

        static bool SegmentNearSphere(Vector3 from, Vector3 to, Vector3 center, float radius)
        {
            Vector3 d = to - from;
            float lengthSq = d.sqrMagnitude;
            float t = lengthSq > 1e-8f ? Mathf.Clamp01(Vector3.Dot(center - from, d) / lengthSq) : 0f;
            return (from + d * t - center).sqrMagnitude <= radius * radius;
        }

        /// <summary>The 26 directions to the faces, edges and corners of a cube.</summary>
        static Vector3[] BuildProbeDirections()
        {
            var dirs = new List<Vector3>();
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
                if (x != 0 || y != 0 || z != 0)
                    dirs.Add(new Vector3(x, y, z).normalized);
            return dirs.ToArray();
        }
    }
}
