using NUnit.Framework;
using UnityEngine;
using WingingIt.World;

namespace WingingIt.Tests
{
    public class AirfieldTests
    {
        GameObject _root;
        Airfield _airfield;
        RepairZone _zone;

        [SetUp]
        public void SetUp()
        {
            // A runway heading east (+X in world), 40 x 600, with a 30 x 200 zone 140 m down it.
            _root = new GameObject("Airfield");
            _root.transform.SetPositionAndRotation(new Vector3(100f, 0f, 50f), Quaternion.Euler(0f, 90f, 0f));
            _zone = new GameObject("Repair Zone").AddComponent<RepairZone>();
            _zone.transform.SetParent(_root.transform, false);
            _zone.transform.localPosition = new Vector3(0f, 0.1f, 140f);
            _zone.Configure(new Vector2(30f, 200f), 8f);
            _airfield = _root.AddComponent<Airfield>();
            _airfield.Configure("Test Field", 1, true, new Vector2(40f, 600f), null, null, _zone);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        Vector3 World(float x, float y, float z) => _root.transform.TransformPoint(new Vector3(x, y, z));

        [Test]
        public void RepairZone_ContainsAParkedPlane_FollowingTheRunwayRotation()
        {
            Assert.IsTrue(_zone.Contains(World(0f, 3f, 140f)), "zone center");
            Assert.IsTrue(_zone.Contains(World(14f, 3f, 235f)), "near a corner");
            Assert.AreSame(_airfield, _zone.Airfield);
        }

        [Test]
        public void RepairZone_ExcludesPlanesOutsideOrFlyingOver()
        {
            Assert.IsFalse(_zone.Contains(World(0f, 3f, 0f)), "runway middle, before the zone");
            Assert.IsFalse(_zone.Contains(World(16f, 3f, 140f)), "beside the zone");
            Assert.IsFalse(_zone.Contains(World(0f, 30f, 140f)), "low pass over the zone");
            // Unrotated, this point would be inside: the zone must follow the airfield's heading.
            Assert.IsFalse(_zone.Contains(new Vector3(100f, 3f, 50f + 140f)));
        }

        [Test]
        public void IsOverRunway_ChecksFootprintAndHeight()
        {
            Assert.IsTrue(_airfield.IsOverRunway(World(0f, 2f, -290f)));
            Assert.IsTrue(_airfield.IsOverRunway(World(-19f, 5f, 0f)));
            Assert.IsFalse(_airfield.IsOverRunway(World(0f, 2f, 310f)), "past the end");
            Assert.IsFalse(_airfield.IsOverRunway(World(21f, 2f, 0f)), "off the side");
            Assert.IsFalse(_airfield.IsOverRunway(World(0f, 50f, 0f)), "too high");
            Assert.That(Vector3.Angle(Vector3.right, _airfield.RunwayDirection), Is.LessThan(0.01f));
        }

        [Test]
        public void RunwaySpawnSlots_StartAtTheRunwaySpawn_SideBySideThenFurtherDown()
        {
            var spawn = new GameObject("Runway Spawn").transform;
            spawn.SetParent(_root.transform, false);
            spawn.localPosition = new Vector3(0f, 3f, -270f);
            _airfield.Configure("Test Field", 1, true, new Vector2(40f, 600f), null, spawn, _zone);

            Assert.That(Vector3.Distance(_airfield.RunwaySpawnSlot(0).position, spawn.position), Is.LessThan(0.01f));
            for (int slot = 0; slot < Airfield.RunwaySpawnSlots; slot++)
            {
                Pose pose = _airfield.RunwaySpawnSlot(slot);
                Assert.IsTrue(_airfield.IsOverRunway(pose.position), $"slot {slot} is on the runway");
                Assert.That(Vector3.Angle(pose.forward, _airfield.RunwayDirection), Is.LessThan(0.01f), $"slot {slot} faces down it");
                for (int other = 0; other < slot; other++)
                    Assert.That(Vector3.Distance(pose.position, _airfield.RunwaySpawnSlot(other).position),
                        Is.GreaterThanOrEqualTo(Airfield.RunwayLaneSpacing - 0.01f), $"slots {other} and {slot} are apart");
            }
            Vector3 local1 = _root.transform.InverseTransformPoint(_airfield.RunwaySpawnSlot(1).position);
            Vector3 local3 = _root.transform.InverseTransformPoint(_airfield.RunwaySpawnSlot(3).position);
            Assert.That(local1.z, Is.EqualTo(-270f).Within(0.01f), "slot 1 is beside slot 0");
            Assert.That(local3.z, Is.EqualTo(-270f + Airfield.RunwayRowSpacing).Within(0.01f), "slot 3 starts the next row");
            Assert.That(Vector3.Distance(_airfield.RunwaySpawnSlot(Airfield.RunwaySpawnSlots).position, spawn.position),
                Is.LessThan(0.01f), "wraps around");
        }

        [Test]
        public void OnlyTheOwningTeamIsFriendly_AndNeutralServesNobody()
        {
            Assert.IsTrue(_airfield.IsFriendlyTo(1));
            Assert.IsFalse(_airfield.IsFriendlyTo(0));

            _airfield.Configure("Neutral Field", Airfield.Neutral, false, new Vector2(40f, 600f), null, null, _zone);
            Assert.IsTrue(_airfield.IsNeutral);
            Assert.IsFalse(_airfield.IsFriendlyTo(0));
            Assert.IsFalse(_airfield.IsFriendlyTo(1));
            Assert.IsFalse(_airfield.IsFriendlyTo(Airfield.Neutral), "neutral isn't a team");
        }
    }
}
