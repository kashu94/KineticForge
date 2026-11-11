using System.Collections;
using KineticForge.Core;
using KineticForge.Physics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KineticForge.Tests
{
    /// <summary>
    /// Runs the real physics engine to verify that stance stabilization changes how a relic moves, that a released relic
    /// gets its original physics back, and that the hold simulator grips and reports the relic.
    /// </summary>
    public sealed class RelicPhysicsTests
    {
        GameObject _relic;
        Rigidbody _body;
        RelicStabilizer _stabilizer;

        [SetUp]
        public void SetUp()
        {
            ForgeEvents.ResetSubscriptions();

            _relic = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _relic.name = "Relic (test)";
            _relic.transform.localScale = Vector3.one * 0.25f;
            _relic.transform.position = new Vector3(0f, 50f, 0f);
            _body = _relic.AddComponent<Rigidbody>();
            _body.mass = 20f;
            _body.useGravity = false;

            // No StanceTracker exists, so the stabilizer warns once and follows the event bus, which the tests drive.
            _stabilizer = _relic.AddComponent<RelicStabilizer>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_relic);
            ForgeEvents.ResetSubscriptions();
        }

        static void SetStability(float stability)
        {
            ForgeEvents.RaiseStanceChanged(new StanceChangedEvent(
                stability, stability, StanceTrackingSource.HeadPose, false, stability, 1f));
        }

        void Grab(ForgeHand hand, bool grabbed)
        {
            ForgeEvents.RaiseRelicGrabChanged(new RelicGrabEvent(_body, hand, grabbed));
        }

        [UnityTest]
        public IEnumerator Braced_WritesStableDampingAndInertia()
        {
            Vector3 baseline = _body.inertiaTensor;
            Grab(ForgeHand.Right, true);
            SetStability(1f);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.IsTrue(_stabilizer.IsStabilizing);
            Assert.AreEqual(3f, _body.linearDamping, 1e-3f);
            Assert.AreEqual(10f, _body.angularDamping, 1e-3f);
            Assert.AreEqual(3f, _stabilizer.CurrentInertiaScale, 1e-3f);
            Assert.That(Vector3.Distance(baseline * 3f, _body.inertiaTensor), Is.LessThan(1e-4f));
        }

        [UnityTest]
        public IEnumerator Release_RestoresOriginalPhysics()
        {
            float linear = _body.linearDamping;
            float angular = _body.angularDamping;
            Vector3 baseline = _body.inertiaTensor;

            Grab(ForgeHand.Left, true);
            SetStability(1f);
            yield return new WaitForFixedUpdate();
            Grab(ForgeHand.Left, false);
            yield return new WaitForFixedUpdate();

            Assert.IsFalse(_stabilizer.IsStabilizing);
            Assert.AreEqual(linear, _body.linearDamping, 1e-5f);
            Assert.AreEqual(angular, _body.angularDamping, 1e-5f);
            Assert.IsTrue(_body.automaticInertiaTensor);
            Assert.That(Vector3.Distance(baseline, _body.inertiaTensor), Is.LessThan(1e-4f));
        }

        [UnityTest]
        public IEnumerator BracedRelic_TurnsLessUnderTheSameDisturbance()
        {
            Grab(ForgeHand.Right, true);

            SetStability(0f);
            yield return new WaitForFixedUpdate();
            _stabilizer.AddDisturbance(Vector3.up * 2f);
            yield return new WaitForFixedUpdate();
            float unbracedSpeed = _body.angularVelocity.magnitude;

            _body.angularVelocity = Vector3.zero;
            SetStability(1f);
            yield return new WaitForFixedUpdate();
            _stabilizer.AddDisturbance(Vector3.up * 2f);
            yield return new WaitForFixedUpdate();
            float bracedSpeed = _body.angularVelocity.magnitude;

            // Inertia scale 0.6 vs 3 alone gives a 5x difference; stable angular damping adds to it.
            Assert.That(unbracedSpeed, Is.GreaterThan(2.5f));
            Assert.That(bracedSpeed, Is.LessThan(unbracedSpeed / 4f));
        }

        [UnityTest]
        public IEnumerator RepeatedGrabsFromOneHand_AreIdempotent()
        {
            Grab(ForgeHand.Right, true);
            Grab(ForgeHand.Right, true);
            Grab(ForgeHand.Right, false);
            Assert.IsFalse(_stabilizer.IsGrabbed);

            Grab(ForgeHand.None, true);
            Grab(ForgeHand.None, true);
            Grab(ForgeHand.None, false);
            Assert.IsTrue(_stabilizer.IsGrabbed, "Grabs not tied to a hand are counted, so one release keeps the other");
            yield return null;
        }

        [UnityTest]
        public IEnumerator HoldSimulator_LiftsHoldsAndReportsTheRelic()
        {
            _body.useGravity = true;
            Vector3 start = _body.position;
            RelicHoldSimulator simulator = _relic.AddComponent<RelicHoldSimulator>();

            // Start runs on the next frame; then give the spring time to settle.
            yield return null;
            Assert.IsTrue(simulator.IsHolding);
            Assert.IsTrue(_stabilizer.IsGrabbed);

            yield return new WaitForSeconds(1.5f);
            Vector3 holdPoint = start + simulator.HoldOffset;
            Assert.That(Vector3.Distance(holdPoint, _body.position), Is.LessThan(0.12f));

            Object.Destroy(simulator);
            yield return null;
            Assert.IsFalse(_stabilizer.IsGrabbed);
        }
    }
}
