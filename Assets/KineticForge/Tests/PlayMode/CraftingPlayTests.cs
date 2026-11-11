using System.Collections;
using System.Collections.Generic;
using KineticForge.Core;
using KineticForge.Crafting;
using KineticForge.Physics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KineticForge.Tests
{
    /// <summary>
    /// Runs the physics engine and the event bus end to end: the physical grip, and a full aim, tap, strike and carve loop.
    /// </summary>
    public sealed class CraftingPlayTests
    {
        GameObject _relic;
        Rigidbody _body;
        readonly List<GameObject> _extras = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            ForgeEvents.ResetSubscriptions();
            _relic = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _relic.name = "Relic (test)";
            _relic.transform.localScale = Vector3.one * 0.28f;
            _relic.transform.position = new Vector3(0f, 40f, 0f);
            _body = _relic.AddComponent<Rigidbody>();
            _body.mass = 20f;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_relic);
            foreach (GameObject extra in _extras)
            {
                Object.Destroy(extra);
            }

            _extras.Clear();
            ForgeEvents.ResetSubscriptions();
        }

        Transform CreateTarget(Vector3 position)
        {
            GameObject target = new GameObject("Hand Target (test)");
            target.transform.position = position;
            _extras.Add(target);
            return target.transform;
        }

        [UnityTest]
        public IEnumerator GrabDriver_CarriesTheRelicWithBothHands()
        {
            RelicGrabDriver driver = _relic.AddComponent<RelicGrabDriver>();
            List<RelicGrabEvent> grabs = new List<RelicGrabEvent>();
            ForgeEvents.RelicGrabChanged += payload => grabs.Add(payload);

            Vector3 start = _body.position;
            Transform left = CreateTarget(start + Vector3.left * 0.14f);
            Transform right = CreateTarget(start + Vector3.right * 0.14f);
            Assert.IsTrue(driver.BeginGrab(ForgeHand.Left, left, left.position));
            Assert.IsTrue(driver.BeginGrab(ForgeHand.Right, right, right.position));
            Assert.IsFalse(driver.BeginGrab(ForgeHand.Right, right, right.position), "one grip per hand");
            Assert.AreEqual(2, driver.GripCount);

            // Lift both hands 0.3 m; the hands carry the weight and the relic follows.
            Vector3 lift = Vector3.up * 0.3f;
            left.position += lift;
            right.position += lift;
            yield return new WaitForSeconds(1.5f);

            Assert.That(Vector3.Distance(start + lift, _body.position), Is.LessThan(0.05f));

            driver.EndGrab(ForgeHand.Left);
            driver.EndGrab(ForgeHand.Right);
            Assert.IsFalse(driver.IsHeld);
            Assert.AreEqual(4, grabs.Count);
            Assert.IsTrue(grabs[0].IsGrabbed && grabs[1].IsGrabbed && !grabs[2].IsGrabbed && !grabs[3].IsGrabbed);
        }

        [UnityTest]
        public IEnumerator GrabDriver_LetsGoWhenTheHandJumpsAway()
        {
            RelicGrabDriver driver = _relic.AddComponent<RelicGrabDriver>();
            Transform hand = CreateTarget(_body.position);
            Assert.IsTrue(driver.BeginGrab(ForgeHand.Right, hand, _body.position));
            yield return new WaitForFixedUpdate();

            hand.position += Vector3.forward * 2f;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.IsFalse(driver.IsHeld);
        }

        [UnityTest]
        public IEnumerator TapOnTheActiveMark_CarvesItAndAMissDoesNot()
        {
            RelicStabilizer stabilizer = _relic.AddComponent<RelicStabilizer>();
            RelicCarvingSurface surface = _relic.AddComponent<RelicCarvingSurface>();
            CarvingCursor cursor = _relic.AddComponent<CarvingCursor>();
            _relic.AddComponent<ChiselStrikeController>();
            _body.useGravity = false;

            List<ChiselStrikeEvent> strikes = new List<ChiselStrikeEvent>();
            ForgeEvents.ChiselStrike += payload => strikes.Add(payload);

            // Fully braced: no tremor, full stance multiplier.
            ForgeEvents.RaiseStanceChanged(new StanceChangedEvent(1f, 1f, StanceTrackingSource.HeadPose, false, 1f, 1f));
            yield return null;
            Assert.AreEqual(1f, stabilizer.TargetStability, 1e-5f);

            cursor.FaceCoordinates = surface.GetMarkCoordinates(0);
            ForgeEvents.RaiseMicroGestureTap(new MicroGestureTapEvent(ForgeHand.Right, 1f, MicroGestureSource.EditorMouse));

            Assert.AreEqual(1, strikes.Count);
            Assert.That(strikes[0].Precision, Is.GreaterThan(0.95f));
            Assert.AreSame(_body, strikes[0].Relic);
            Assert.AreEqual(1, surface.StruckCount);

            // Wait out the strike cooldown, then miss by aiming at the far corner.
            yield return new WaitForSecondsRealtime(0.2f);
            cursor.FaceCoordinates = new Vector2(0.5f, -0.5f);
            ForgeEvents.RaiseMicroGestureTap(new MicroGestureTapEvent(ForgeHand.Right, 1f, MicroGestureSource.EditorMouse));

            Assert.AreEqual(2, strikes.Count);
            Assert.AreEqual(0f, strikes[1].Precision, 1e-5f);
            Assert.AreEqual(1, surface.StruckCount);
        }

        [UnityTest]
        public IEnumerator SlideGestures_SteerTheCursor()
        {
            _relic.AddComponent<RelicCarvingSurface>();
            CarvingCursor cursor = _relic.AddComponent<CarvingCursor>();
            yield return null;

            Vector2 slide = new Vector2(0.1f, -0.05f);
            ForgeEvents.RaiseMicroGestureSlide(new MicroGestureSlideEvent(
                ForgeHand.Left, slide, 1f, 0.016f, MicroGestureSource.HandTracking));

            Vector2 expected = slide * cursor.SlideGain;
            Assert.That(Vector2.Distance(expected, cursor.FaceCoordinates), Is.LessThan(1e-5f));

            ForgeEvents.RaiseMicroGestureSlide(new MicroGestureSlideEvent(
                ForgeHand.Left, new Vector2(50f, 50f), 1f, 0.016f, MicroGestureSource.HandTracking));
            Assert.That(Vector2.Distance(new Vector2(0.5f, 0.5f), cursor.FaceCoordinates), Is.LessThan(1e-5f), "clamped");
        }
    }
}
