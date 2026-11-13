using KineticForge.Core;
using NUnit.Framework;

namespace KineticForge.Tests
{
    /// <summary>Tap detection for a fingertip approaching a surface.</summary>
    public sealed class FingertipTouchTests
    {
        const float Frame = 1f / 72f;

        static bool Approach(FingertipTouch touch, float from, float to, int frames, bool inside, out float speed)
        {
            bool tapped = false;
            speed = 0f;
            for (int i = 0; i <= frames; i++)
            {
                float distance = from + (to - from) * i / frames;
                tapped |= touch.Step(distance, Frame, inside, out float s);
                if (tapped && speed == 0f)
                {
                    speed = s;
                }
            }

            return tapped;
        }

        [Test]
        public void AQuickApproach_Taps_AndItsSpeedIsMeasured()
        {
            FingertipTouch touch = new FingertipTouch();
            Assert.IsTrue(Approach(touch, 0.08f, 0.002f, 5, true, out float speed), "a quick approach taps");
            Assert.Greater(speed, 0.5f);
        }

        [Test]
        public void ASlowDrift_NeverTaps()
        {
            FingertipTouch touch = new FingertipTouch();
            Assert.IsFalse(Approach(touch, 0.08f, 0.0f, 400, true, out _), "drifting at a few cm per second is not a tap");
        }

        [Test]
        public void AFingerOutsideTheFace_NeverTaps()
        {
            FingertipTouch touch = new FingertipTouch();
            Assert.IsFalse(Approach(touch, 0.08f, 0.002f, 5, false, out _));
        }

        [Test]
        public void AFingerThatStartsBehindTheFace_NeverTaps()
        {
            FingertipTouch touch = new FingertipTouch();
            Assert.IsFalse(Approach(touch, -0.05f, -0.01f, 5, true, out _), "contact must come from the front");
        }

        [Test]
        public void AfterATap_TheFingerMustDrawBackBeforeTappingAgain()
        {
            FingertipTouch touch = new FingertipTouch();
            Assert.IsTrue(Approach(touch, 0.08f, 0.002f, 5, true, out _));

            // Hovering at the face, wobbling in and out of contact, never re-arms.
            bool again = false;
            for (int i = 0; i < 40; i++)
            {
                again |= touch.Step(i % 2 == 0 ? 0.002f : 0.02f, Frame, true, out _);
            }

            Assert.IsFalse(again, "a finger resting at the face does not strike twice");

            // Drawing back past the re-arm distance allows the next tap.
            touch.Step(0.06f, Frame, true, out _);
            Assert.IsTrue(Approach(touch, 0.06f, 0.002f, 5, true, out _), "a deliberate second tap strikes");
        }

        [Test]
        public void ATrackingGap_DoesNotCountAsATap()
        {
            FingertipTouch touch = new FingertipTouch();
            touch.Step(0.08f, Frame, true, out _);
            touch.Reset();
            Assert.IsFalse(touch.Step(0.002f, Frame, true, out _), "a finger that reappears touching has no approach to judge");
            Assert.IsFalse(touch.Step(0.002f, Frame, true, out _));
        }

        [Test]
        public void GarbageSamples_AreIgnored()
        {
            FingertipTouch touch = new FingertipTouch();
            Assert.IsFalse(touch.Step(float.NaN, Frame, true, out _));
            Assert.IsFalse(touch.Step(0.05f, 0f, true, out _));
            Assert.IsFalse(touch.Step(float.PositiveInfinity, Frame, true, out _));
            Assert.IsTrue(Approach(touch, 0.08f, 0.002f, 5, true, out _), "it recovers afterwards");
        }

        [Test]
        public void Strength_RisesWithSpeed_AndIsBounded()
        {
            FingertipTouch touch = new FingertipTouch();
            float gentle = touch.StrengthFor(touch.MinimumSpeed);
            float brisk = touch.StrengthFor(0.5f);
            float fast = touch.StrengthFor(5f);
            Assert.AreEqual(0.35f, gentle, 1e-4f);
            Assert.Greater(brisk, gentle);
            Assert.AreEqual(1f, fast, 1e-4f);
            Assert.AreEqual(0.35f, touch.StrengthFor(-3f), 1e-4f);
        }
    }
}
