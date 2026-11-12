using KineticForge.Core;
using NUnit.Framework;
using UnityEngine;

namespace KineticForge.Tests
{
    /// <summary>
    /// The physics of Resonant Forging: plate modes, the exact pulse oscillator, strike timing, cooling, quality,
    /// cracks and grades.
    /// </summary>
    public sealed class ResonanceMathTests
    {
        const float Tolerance = 1e-4f;

        [Test]
        public void PlateMode_AntisymmetricMode_HasNodalLineOnDiagonal()
        {
            for (float u = 0f; u <= 1f; u += 0.1f)
            {
                Assert.AreEqual(0f, ResonanceMath.PlateMode(0, new Vector2(u, u)), Tolerance, "mode (1,2,-) is silent on u = v");
            }

            Assert.Greater(Mathf.Abs(ResonanceMath.PlateMode(0, new Vector2(0.1f, 0.8f))), 0.1f);
        }

        [Test]
        public void PlateExcitation_OnNode_DoesNotExciteThatMode()
        {
            Vector4 onNode = ResonanceMath.PlateExcitation(new Vector2(0.3f, 0.3f));
            Assert.AreEqual(0f, onNode.x, Tolerance);
            for (int i = 0; i < 4; i++)
            {
                Assert.That(onNode[i], Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void StepOscillator_Undamped_KeepsAmplitudeAtAnyFrameRate()
        {
            Vector2 phasor = new Vector2(0.7f, -0.2f);
            float amplitude = phasor.magnitude;
            for (int i = 0; i < 1000; i++)
            {
                phasor = ResonanceMath.StepOscillator(phasor, 1.1f, 0f, 1f / 72f);
            }

            Assert.AreEqual(amplitude, phasor.magnitude, 1e-3f);
        }

        [Test]
        public void StepOscillator_Damped_DecaysExactlyAndIndependentOfStepSize()
        {
            const float Frequency = 1f;
            const float Zeta = 0.05f;
            Vector2 oneStep = ResonanceMath.StepOscillator(new Vector2(1f, 0f), Frequency, Zeta, 2f);
            Vector2 manySteps = new Vector2(1f, 0f);
            for (int i = 0; i < 144; i++)
            {
                manySteps = ResonanceMath.StepOscillator(manySteps, Frequency, Zeta, 2f / 144f);
            }

            float expected = Mathf.Exp(-Zeta * 2f * Mathf.PI * Frequency * 2f);
            Assert.AreEqual(expected, oneStep.magnitude, 1e-4f);
            Assert.AreEqual(oneStep.x, manySteps.x, 1e-3f);
            Assert.AreEqual(oneStep.y, manySteps.y, 1e-3f);
        }

        [Test]
        public void Kick_WithTheMotion_PumpsEnergyIn_AgainstItDrainsIt()
        {
            Vector2 withMotion = new Vector2(0f, -1f);
            Vector2 againstMotion = new Vector2(0f, 1f);

            Assert.AreEqual(1f, ResonanceMath.StrikeTiming(withMotion), Tolerance);
            Assert.AreEqual(-1f, ResonanceMath.StrikeTiming(againstMotion), Tolerance);
            Assert.AreEqual(1.3f, ResonanceMath.Kick(withMotion, 0.3f).magnitude, Tolerance);
            Assert.AreEqual(0.7f, ResonanceMath.Kick(againstMotion, 0.3f).magnitude, Tolerance);
        }

        [Test]
        public void Kick_FromRest_StartsThePulseWithNeutralTiming()
        {
            Assert.AreEqual(0f, ResonanceMath.StrikeTiming(Vector2.zero));
            Vector2 started = ResonanceMath.Kick(Vector2.zero, 0.3f);
            Assert.AreEqual(0.3f, started.magnitude, Tolerance);
            Assert.AreEqual(1f, ResonanceMath.StrikeTiming(started), Tolerance, "the motion a strike starts is in phase with it");
        }

        [Test]
        public void CycleToIdealStrike_CountsDownToThePumpingPhase()
        {
            Assert.AreEqual(0f, Mathf.Repeat(ResonanceMath.CycleToIdealStrike(new Vector2(0f, -1f)) + 1e-4f, 1f), 1e-3f);
            Assert.AreEqual(0.75f, ResonanceMath.CycleToIdealStrike(new Vector2(1f, 0f)), Tolerance);
            Assert.AreEqual(0.25f, ResonanceMath.CycleToIdealStrike(new Vector2(-1f, 0f)), Tolerance);

            // A quarter cycle later, a quarter less remains.
            Vector2 later = ResonanceMath.StepOscillator(new Vector2(1f, 0f), 1f, 0f, 0.25f);
            Assert.AreEqual(0.5f, ResonanceMath.CycleToIdealStrike(later), 1e-3f);
        }

        [Test]
        public void Cool_FollowsNewtonsLawExactly()
        {
            Assert.AreEqual(Mathf.Exp(-1f), ResonanceMath.Cool(1f, 10f, 10f), Tolerance);
            float stepped = 1f;
            for (int i = 0; i < 100; i++)
            {
                stepped = ResonanceMath.Cool(stepped, 10f, 0.1f);
            }

            Assert.AreEqual(Mathf.Exp(-1f), stepped, 1e-3f);
        }

        [Test]
        public void PulseFrequency_RisesAsTheRelicCools()
        {
            Assert.Less(ResonanceMath.PulseFrequency(1f, 1.25f, 0.8f), ResonanceMath.PulseFrequency(0f, 1.25f, 0.8f));
        }

        [Test]
        public void CarveQuality_RewardsHeatRhythmAndCharge()
        {
            float cold = ResonanceMath.CarveQuality(0.8f, 0f, 0f, 0f);
            float hot = ResonanceMath.CarveQuality(0.8f, 1f, 0f, 0f);
            float inRhythm = ResonanceMath.CarveQuality(0.8f, 1f, 1f, 0f);
            float charged = ResonanceMath.CarveQuality(0.8f, 1f, 1f, 1f);
            Assert.Less(cold, hot);
            Assert.Less(hot, inRhythm);
            Assert.Less(inRhythm, charged);
            Assert.LessOrEqual(ResonanceMath.CarveQuality(1f, 1f, 1f, 5f), 1f);
            Assert.AreEqual(0f, ResonanceMath.CarveQuality(0f, 1f, 1f, 1f), Tolerance);
        }

        [Test]
        public void CrackStress_ColdLooseStanceCracks_HotOrBracedHolds()
        {
            Assert.Greater(ResonanceMath.CrackStress(1f, 0f, 0f), 1.15f, "cold metal, loose stance, charged");
            Assert.Less(ResonanceMath.CrackStress(1.5f, 0f, 1f), 1.15f, "hot metal forgives");
            Assert.Less(ResonanceMath.CrackStress(1.5f, 0.8f, 0f), 1.15f, "a braced body grounds the charge");
        }

        [Test]
        public void Grade_FollowsQualityAndIntegrity()
        {
            Assert.AreEqual(ForgeGrade.S, ResonanceMath.Grade(0.9f, 1f));
            Assert.AreEqual(ForgeGrade.A, ResonanceMath.Grade(0.75f, 1f));
            Assert.AreEqual(ForgeGrade.B, ResonanceMath.Grade(0.6f, 1f));
            Assert.AreEqual(ForgeGrade.C, ResonanceMath.Grade(0.3f, 1f));
            Assert.AreEqual(ForgeGrade.Fractured, ResonanceMath.Grade(1f, 0f));
            Assert.Greater((int)ResonanceMath.Grade(0.9f, 0.25f), (int)ResonanceMath.Grade(0.9f, 1f), "cracks cost grade");
        }

        [Test]
        public void Incandescence_IsDarkWhenColdAndBrightWhenHot()
        {
            Color cold = ResonanceMath.Incandescence(0f);
            Color hot = ResonanceMath.Incandescence(1f);
            Assert.AreEqual(0f, cold.r + cold.g + cold.b, Tolerance);
            Assert.Greater(hot.r, 0.9f);
            Assert.Greater(hot.r, hot.b, "hot metal glows warm");
        }

        [Test]
        public void SmoothStep_MatchesHlsl()
        {
            Assert.AreEqual(0f, ForgeMath.SmoothStep(0.2f, 0.6f, 0.1f));
            Assert.AreEqual(1f, ForgeMath.SmoothStep(0.2f, 0.6f, 0.9f));
            Assert.AreEqual(0.5f, ForgeMath.SmoothStep(0.2f, 0.6f, 0.4f), Tolerance);
        }
    }
}
