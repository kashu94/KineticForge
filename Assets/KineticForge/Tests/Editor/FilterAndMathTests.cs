using KineticForge.Core;
using KineticForge.Platform;
using NUnit.Framework;
using UnityEngine;

namespace KineticForge.Tests
{
    /// <summary>
    /// Verifies frame-rate independence of the smoothing math, the 1€ filters and refresh-rate selection.
    /// </summary>
    public sealed class FilterAndMathTests
    {
        static float RunDamp(float rate, float seconds, int hz)
        {
            float value = 0f;
            float deltaTime = 1f / hz;
            int steps = Mathf.RoundToInt(seconds * hz);
            for (int i = 0; i < steps; i++)
            {
                value = ForgeMath.Damp(value, 1f, rate, deltaTime);
            }

            return value;
        }

        [Test]
        public void Damp_IsFrameRateIndependent()
        {
            float analytic = 1f - Mathf.Exp(-3.5f * 0.5f);
            Assert.AreEqual(analytic, RunDamp(3.5f, 0.5f, 72), 1e-4f);
            Assert.AreEqual(analytic, RunDamp(3.5f, 0.5f, 90), 1e-4f);
            Assert.AreEqual(analytic, RunDamp(3.5f, 0.5f, 120), 1e-4f);
        }

        [Test]
        public void ExpBlend_HandlesDegenerateInput()
        {
            Assert.AreEqual(0f, ForgeMath.ExpBlend(0f, 0.1f));
            Assert.AreEqual(0f, ForgeMath.ExpBlend(5f, 0f));
            Assert.AreEqual(0f, ForgeMath.ExpBlend(float.NaN, 0.1f));
            Assert.AreEqual(1f, ForgeMath.ExpBlend(5f, 1000f), 1e-6f);
        }

        [Test]
        public void DampSnap_LandsExactlyOnTarget()
        {
            float value = 0f;
            for (int i = 0; i < 1000; i++)
            {
                value = ForgeMath.DampSnap(value, 1f, 7f, 1f / 90f, 0.0005f);
            }

            Assert.AreEqual(1f, value);
        }

        [Test]
        public void Sanitize01_MapsNonFiniteToFallback()
        {
            Assert.AreEqual(0.25f, ForgeMath.Sanitize01(float.NaN, 0.25f));
            Assert.AreEqual(0f, ForgeMath.Sanitize01(float.PositiveInfinity));
            Assert.AreEqual(1f, ForgeMath.Sanitize01(3f));
        }

        [Test]
        public void ClosestPointOnSegment_ClampsToEnds()
        {
            Vector2 a = new Vector2(-1f, 0f);
            Vector2 b = new Vector2(1f, 0f);
            Assert.AreEqual(new Vector2(0.5f, 0f), ForgeMath.ClosestPointOnSegment(new Vector2(0.5f, 2f), a, b));
            Assert.AreEqual(b, ForgeMath.ClosestPointOnSegment(new Vector2(3f, 1f), a, b));
            Assert.AreEqual(a, ForgeMath.ClosestPointOnSegment(new Vector2(3f, 1f), a, a));
        }

        [Test]
        public void OneEuro_FirstSamplePassesThroughAndConstantStaysConstant()
        {
            OneEuroFilter3 filter = default;
            OneEuroSettings settings = OneEuroSettings.BodyJoints.Sanitized();
            Vector3 point = new Vector3(0.2f, 1.1f, -0.4f);

            Assert.AreEqual(point, filter.Filter(point, 1f / 90f, settings));
            for (int i = 0; i < 200; i++)
            {
                filter.Filter(point, 1f / 90f, settings);
            }

            Assert.That(Vector3.Distance(point, filter.Value), Is.LessThan(1e-6f));
            Assert.That(filter.Derivative.magnitude, Is.LessThan(1e-6f));
        }

        [Test]
        public void OneEuro_StepResponseMatchesAcrossFrameRates()
        {
            // With beta = 0 the filter is a plain low-pass, so its step response must equal 1 - e^(-2 pi fc t) at any rate.
            OneEuroSettings settings = new OneEuroSettings(1.5f, 0f, 1f).Sanitized();
            const float Duration = 1f / 3f;
            float expected = 1f - Mathf.Exp(-2f * Mathf.PI * 1.5f * Duration);

            foreach (int hz in new[] { 72, 90, 120 })
            {
                OneEuroFilter filter = default;
                filter.Filter(0f, 1f / hz, settings);
                int steps = hz / 3;
                for (int i = 0; i < steps; i++)
                {
                    filter.Filter(1f, 1f / hz, settings);
                }

                Assert.AreEqual(expected, filter.Value, 1e-3f, "at " + hz + " Hz");
            }
        }

        [Test]
        public void OneEuro_RemovesJitterFromAStillJoint()
        {
            OneEuroFilter3 filter = default;
            OneEuroSettings settings = OneEuroSettings.BodyJoints.Sanitized();
            Random.State previous = Random.state;
            Random.InitState(1234);

            Vector3 truth = new Vector3(0f, 1f, 0f);
            double rawError = 0d;
            double filteredError = 0d;
            const int Frames = 900;
            for (int i = 0; i < Frames; i++)
            {
                Vector3 sample = truth + Random.insideUnitSphere * 0.002f;
                Vector3 filtered = filter.Filter(sample, 1f / 90f, settings);
                if (i > 90)
                {
                    rawError += (sample - truth).sqrMagnitude;
                    filteredError += (filtered - truth).sqrMagnitude;
                }
            }

            Random.state = previous;
            Assert.That(filteredError, Is.LessThan(rawError / 9d), "RMS jitter should drop at least threefold");
        }

        [Test]
        public void OneEuro_IgnoresNonPositiveDeltaTime()
        {
            OneEuroFilter filter = default;
            OneEuroSettings settings = OneEuroSettings.BodyJoints.Sanitized();
            filter.Filter(1f, 0.01f, settings);
            Assert.AreEqual(1f, filter.Filter(5f, 0f, settings));
            Assert.AreEqual(1f, filter.Filter(5f, -1f, settings));
        }

        [Test]
        public void OneEuroSettings_SanitizeRepairsInvalidValues()
        {
            OneEuroSettings settings = new OneEuroSettings(float.NaN, -1f, 0f).Sanitized();
            Assert.AreEqual(OneEuroSettings.MinimumCutoff, settings.minCutoff);
            Assert.AreEqual(0f, settings.beta);
            Assert.AreEqual(OneEuroSettings.MinimumCutoff, settings.derivativeCutoff);
        }

        [Test]
        public void ChooseRefreshRate_PrefersHighestAtOrBelowPreference()
        {
            float[] quest3 = { 72f, 80f, 90f, 120f };
            Assert.AreEqual(90f, XRFramePacing.ChooseRefreshRate(quest3, 90f));
            Assert.AreEqual(90f, XRFramePacing.ChooseRefreshRate(quest3, 100f));
            Assert.AreEqual(120f, XRFramePacing.ChooseRefreshRate(quest3, 120f));
            Assert.AreEqual(72f, XRFramePacing.ChooseRefreshRate(quest3, 60f));
            Assert.AreEqual(0f, XRFramePacing.ChooseRefreshRate(new float[0], 90f));
            Assert.AreEqual(90f, XRFramePacing.ChooseRefreshRate(new[] { float.NaN, 90f, -1f }, 90f));
        }
    }
}
