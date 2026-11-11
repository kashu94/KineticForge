using KineticForge.Core;
using KineticForge.Tracking;
using NUnit.Framework;
using UnityEngine;

namespace KineticForge.Tests
{
    /// <summary>
    /// Verifies the stance model: crouch, lean, width, support zone, steadiness, seated mode and NaN safety.
    /// </summary>
    public sealed class StanceSolverTests
    {
        const float StandingHipHeight = 1.0f;
        const float StandingHeadHeight = 1.65f;
        const float SpineLength = 0.45f;

        static StanceSolverSettings Settings()
        {
            StanceSolverSettings settings = new StanceSolverSettings();
            settings.Sanitize();
            return settings;
        }

        static BodyPoseSample HeadPose(float headHeight, float speed = 0f)
        {
            return new BodyPoseSample
            {
                Source = StanceTrackingSource.HeadPose,
                HasHead = true,
                Head = new Vector3(0f, headHeight, 0f),
                HasFacing = true,
                Forward = Vector3.forward,
                ReferenceSpeed = speed,
            };
        }

        static StanceReference References(bool withAnchor = false)
        {
            return new StanceReference
            {
                HasHead = true,
                StandingHeadHeight = StandingHeadHeight,
                HasHips = true,
                StandingHipHeight = StandingHipHeight,
                HasHeadAnchor = withAnchor,
                HeadAnchor = new Vector3(0f, StandingHeadHeight, 0f),
                AnchorForward = Vector3.forward,
            };
        }

        /// <summary>Full-body pose facing +Z, with the chest tilted forward by <paramref name="leanDegrees"/>.</summary>
        static BodyPoseSample BodyPose(
            float hipDrop,
            float leanDegrees,
            float feetWidth,
            float speed = 0f,
            float sidewaysLeanDegrees = 0f,
            Vector3 feetOffset = default)
        {
            Vector3 hips = new Vector3(0f, StandingHipHeight - hipDrop, 0f);
            Quaternion tilt = Quaternion.Euler(leanDegrees, 0f, -sidewaysLeanDegrees);
            Vector3 chest = hips + tilt * (Vector3.up * SpineLength);

            return new BodyPoseSample
            {
                Source = StanceTrackingSource.BodyTracking,
                HasHead = true,
                Head = chest + Vector3.up * 0.25f,
                HasHips = true,
                Hips = hips,
                HasChest = true,
                Chest = chest,
                HasFeet = true,
                LeftFoot = new Vector3(-feetWidth * 0.5f, 0.08f, 0f) + feetOffset,
                RightFoot = new Vector3(feetWidth * 0.5f, 0.08f, 0f) + feetOffset,
                HasFacing = true,
                Forward = Vector3.forward,
                ReferenceSpeed = speed,
            };
        }

        [Test]
        public void NoSource_YieldsZeroEvaluation()
        {
            StanceEvaluation evaluation = StanceSolver.Evaluate(default, References(), Settings(), false);
            Assert.AreEqual(0f, evaluation.Stability);
            Assert.AreEqual(0f, evaluation.Grounding);
        }

        [Test]
        public void HeadTier_StandingTall_IsUnstable()
        {
            StanceEvaluation evaluation =
                StanceSolver.Evaluate(HeadPose(StandingHeadHeight), References(), Settings(), false);
            Assert.AreEqual(0f, evaluation.Crouch, 1e-5f);
            Assert.AreEqual(0f, evaluation.Stability, 1e-5f);
        }

        [Test]
        public void HeadTier_DeepStillCrouch_IsFullyStable()
        {
            StanceEvaluation evaluation =
                StanceSolver.Evaluate(HeadPose(StandingHeadHeight - 0.3f), References(), Settings(), false);
            Assert.AreEqual(1f, evaluation.Crouch, 1e-5f);
            Assert.AreEqual(1f, evaluation.Stability, 1e-5f);
        }

        [Test]
        public void HeadTier_CrouchIsLinearBetweenThresholds()
        {
            StanceSolverSettings settings = Settings();
            float midDrop = (settings.headDropStart + settings.headDropFull) * 0.5f;
            StanceEvaluation evaluation =
                StanceSolver.Evaluate(HeadPose(StandingHeadHeight - midDrop), References(), settings, false);
            Assert.AreEqual(0.5f, evaluation.Crouch, 1e-4f);
        }

        [Test]
        public void Swaying_RemovesUpToBalanceInfluence()
        {
            StanceSolverSettings settings = Settings();
            StanceEvaluation still =
                StanceSolver.Evaluate(HeadPose(StandingHeadHeight - 0.3f, 0f), References(), settings, false);
            StanceEvaluation swaying = StanceSolver.Evaluate(
                HeadPose(StandingHeadHeight - 0.3f, settings.speedUnstable * settings.headSpeedScale * 2f),
                References(), settings, false);

            Assert.AreEqual(0f, swaying.Steadiness, 1e-5f);
            Assert.AreEqual(still.Stability * (1f - settings.balanceInfluence), swaying.Stability, 1e-4f);
        }

        [Test]
        public void BodyTier_AthleticBrace_IsStable()
        {
            StanceEvaluation evaluation =
                StanceSolver.Evaluate(BodyPose(0.2f, 18f, 0.45f), References(), Settings(), false);

            Assert.AreEqual(1f, evaluation.Lean, 1e-3f);
            Assert.AreEqual(1f, evaluation.Width, 1e-3f);
            Assert.AreEqual(1f, evaluation.CenterOfMass, 1e-3f);
            Assert.That(evaluation.Stability, Is.GreaterThan(0.9f));
        }

        [Test]
        public void BodyTier_StandingUpright_IsUnstable()
        {
            StanceEvaluation evaluation =
                StanceSolver.Evaluate(BodyPose(0f, 0f, 0.15f), References(), Settings(), false);
            Assert.That(evaluation.Stability, Is.LessThan(0.05f));
        }

        [Test]
        public void Lean_MeasuresForwardAndSidewaysWithUnityAxes()
        {
            Assert.IsTrue(StanceSolver.TryMeasureLean(BodyPose(0f, 20f, 0.3f), out float forward, out float lateral));
            Assert.AreEqual(20f, forward, 0.01f);
            Assert.AreEqual(0f, lateral, 0.01f);

            Assert.IsTrue(StanceSolver.TryMeasureLean(
                BodyPose(0f, 0f, 0.3f, sidewaysLeanDegrees: 12f), out forward, out lateral));
            Assert.AreEqual(0f, forward, 0.01f);
            Assert.AreEqual(12f, lateral, 0.01f);
        }

        [Test]
        public void Lean_OverreachRemovesCredit()
        {
            StanceEvaluation evaluation =
                StanceSolver.Evaluate(BodyPose(0.2f, 70f, 0.45f), References(), Settings(), false);
            Assert.AreEqual(0f, evaluation.Lean, 1e-5f);
        }

        [Test]
        public void Lean_IsMeasuredFromCalibratedNeutral()
        {
            StanceSolverSettings settings = Settings();
            StanceReference reference = References();
            float withoutNeutral = StanceSolver.Evaluate(BodyPose(0.2f, 12f, 0.45f), reference, settings, false).Lean;

            reference.HasNeutralLean = true;
            reference.NeutralForwardLeanDegrees = 6f;
            StanceEvaluation withNeutral = StanceSolver.Evaluate(BodyPose(0.2f, 12f, 0.45f), reference, settings, false);

            Assert.AreEqual(6f, withNeutral.ForwardLeanDegrees, 0.01f);
            Assert.That(withNeutral.Lean, Is.LessThan(withoutNeutral));
        }

        [Test]
        public void NeutralLean_IsClampedToLimit()
        {
            StanceReference reference = References();
            reference.HasNeutralLean = true;
            reference.NeutralForwardLeanDegrees = 40f;
            StanceEvaluation evaluation = StanceSolver.Evaluate(BodyPose(0.2f, 30f, 0.45f), reference, Settings(), false);
            Assert.AreEqual(30f - StanceSolver.MaxNeutralLeanDegrees, evaluation.ForwardLeanDegrees, 0.01f);
        }

        [Test]
        public void SupportZone_ShiftingWeightBetweenFeetIsFree()
        {
            StanceSolverSettings settings = Settings();

            // Feet 0.5 m apart, shifted 0.1 m left: the center of mass sits inside the centered zone.
            BodyPoseSample inside = BodyPose(0.2f, 18f, 0.5f, feetOffset: new Vector3(-0.1f, 0f, 0f));
            Assert.AreEqual(0f, StanceSolver.MeasureSupportOffset(inside, settings), 0.01f);

            // Shifted 0.35 m: past the right foot's side of the zone.
            BodyPoseSample outside = BodyPose(0.2f, 18f, 0.5f, feetOffset: new Vector3(-0.35f, 0f, 0f));
            float offset = StanceSolver.MeasureSupportOffset(outside, settings);
            Assert.AreEqual(0.35f - 0.5f * 0.5f * settings.comSupportSpan, offset, 0.01f);
            Assert.That(StanceSolver.Evaluate(outside, References(), settings, false).CenterOfMass, Is.LessThan(1f));
        }

        [Test]
        public void Seated_HeadTierUpright_DoesNotReachBrace()
        {
            StanceSolverSettings settings = Settings();
            StanceEvaluation evaluation =
                StanceSolver.Evaluate(HeadPose(StandingHeadHeight), References(true), settings, true);

            Assert.AreEqual(settings.seatedBaseGrounding, evaluation.Grounding, 1e-4f);
            Assert.That(evaluation.Stability, Is.LessThan(0.75f));
        }

        [Test]
        public void Seated_HeadTierLeaningIntoWork_Braces()
        {
            StanceSolverSettings settings = Settings();
            float travel = settings.seatedTorsoLength * Mathf.Sin(settings.leanIdeal * Mathf.Deg2Rad);
            BodyPoseSample pose = HeadPose(StandingHeadHeight - 0.03f);
            pose.Head += Vector3.forward * travel;

            StanceEvaluation evaluation = StanceSolver.Evaluate(pose, References(true), settings, true);
            Assert.AreEqual(settings.leanIdeal, evaluation.ForwardLeanDegrees, 0.05f);
            Assert.That(evaluation.Stability, Is.GreaterThan(0.95f));
        }

        [Test]
        public void NonFiniteInput_YieldsZeroEvaluation()
        {
            BodyPoseSample pose = HeadPose(float.NaN);
            StanceEvaluation evaluation = StanceSolver.Evaluate(pose, References(), Settings(), false);
            Assert.AreEqual(0f, evaluation.Stability);
            Assert.IsTrue(ForgeMath.IsFinite(evaluation.Grounding));
        }

        [Test]
        public void Sanitize_RepairsNaNAndKeepsCrouchWeighted()
        {
            StanceSolverSettings settings = new StanceSolverSettings
            {
                crouchWeight = 0f,
                leanWeight = 0.5f,
                hipDropFull = float.NaN,
            };
            settings.Sanitize();

            Assert.That(settings.crouchWeight, Is.GreaterThan(0f));
            Assert.AreEqual(new StanceSolverSettings().hipDropFull, settings.hipDropFull);

            // With crouch the only factor present, its absolute weight does not matter.
            StanceEvaluation evaluation =
                StanceSolver.Evaluate(HeadPose(StandingHeadHeight - 0.3f), References(), settings, false);
            Assert.AreEqual(1f, evaluation.Grounding, 1e-4f);
        }

        [Test]
        public void Sanitize_KeepsThresholdPairsOrdered()
        {
            StanceSolverSettings settings = new StanceSolverSettings
            {
                hipDropStart = 0.3f,
                hipDropFull = 0.1f,
                leanStart = 30f,
                leanIdeal = 10f,
                comTolerance = 0.5f,
                comLimit = 0.1f,
            };
            settings.Sanitize();

            Assert.That(settings.hipDropFull, Is.GreaterThan(settings.hipDropStart));
            Assert.That(settings.leanIdeal, Is.GreaterThan(settings.leanStart));
            Assert.That(settings.leanOverreach, Is.GreaterThanOrEqualTo(settings.leanIdeal));
            Assert.That(settings.leanCollapse, Is.GreaterThan(settings.leanOverreach));
            Assert.That(settings.comLimit, Is.GreaterThan(settings.comTolerance));
        }
    }
}
