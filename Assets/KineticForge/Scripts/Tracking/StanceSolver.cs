using System;
using KineticForge.Core;
using UnityEngine;

namespace KineticForge.Tracking
{
    /// <summary>
    /// One frame of posture data in tracking space (metres, Y up), produced by <see cref="StanceTracker"/> and consumed by
    /// <see cref="StanceSolver"/>. The <c>Has*</c> flags mark which fields hold valid data for the active tier.
    /// </summary>
    public struct BodyPoseSample
    {
        /// <summary>Tier that produced this sample.</summary>
        public StanceTrackingSource Source;

        /// <summary>True when <see cref="Head"/> is valid.</summary>
        public bool HasHead;

        /// <summary>Head position. Comes from the headset when available, otherwise from the body skeleton.</summary>
        public Vector3 Head;

        /// <summary>True when <see cref="Hips"/> is valid. Body-tracking tier only.</summary>
        public bool HasHips;

        /// <summary>Hip joint position.</summary>
        public Vector3 Hips;

        /// <summary>True when <see cref="Chest"/> is valid. Body-tracking tier only.</summary>
        public bool HasChest;

        /// <summary>Chest joint position.</summary>
        public Vector3 Chest;

        /// <summary>True when both ankle positions are valid. Full-body tracking only.</summary>
        public bool HasFeet;

        /// <summary>Left ankle position.</summary>
        public Vector3 LeftFoot;

        /// <summary>Right ankle position.</summary>
        public Vector3 RightFoot;

        /// <summary>True when <see cref="Forward"/> is valid.</summary>
        public bool HasFacing;

        /// <summary>Horizontal unit vector the torso faces, from the shoulder line or else the head.</summary>
        public Vector3 Forward;

        /// <summary>Smoothed speed in m/s of the steadiness reference point: hips for body tracking, head otherwise.</summary>
        public float ReferenceSpeed;
    }

    /// <summary>
    /// Standing reference heights that crouch depth is measured against, in tracking space.
    /// </summary>
    public struct StanceReference
    {
        /// <summary>True when <see cref="StandingHeadHeight"/> is valid.</summary>
        public bool HasHead;

        /// <summary>Head height while standing tall.</summary>
        public float StandingHeadHeight;

        /// <summary>True when <see cref="StandingHipHeight"/> is valid.</summary>
        public bool HasHips;

        /// <summary>Hip height while standing tall.</summary>
        public float StandingHipHeight;
    }

    /// <summary>
    /// Factor breakdown of one stance evaluation. Factor values are in [0, 1].
    /// </summary>
    public struct StanceEvaluation
    {
        /// <summary>Crouch depth below the standing reference. Forced to 1 in seated mode.</summary>
        public float Crouch;

        /// <summary>Quality of the forward torso lean, penalised for overreach and sideways lean.</summary>
        public float Lean;

        /// <summary>Stance width between the ankles.</summary>
        public float Width;

        /// <summary>How well the center of mass sits over the base of support. 1 when feet are not tracked.</summary>
        public float CenterOfMass;

        /// <summary>Stillness of the steadiness reference point.</summary>
        public float Steadiness;

        /// <summary>Signed forward torso lean in degrees. Positive leans toward the facing direction.</summary>
        public float ForwardLeanDegrees;

        /// <summary>Unsigned sideways torso lean in degrees.</summary>
        public float LateralLeanDegrees;

        /// <summary>Horizontal distance between the ankles in metres.</summary>
        public float StanceWidthMeters;

        /// <summary>Weighted blend of crouch, lean and width over the factors available to the active tier.</summary>
        public float Grounding;

        /// <summary>Product of <see cref="CenterOfMass"/> and <see cref="Steadiness"/>.</summary>
        public float Balance;

        /// <summary>Final unsmoothed stance stability.</summary>
        public float Stability;
    }

    /// <summary>
    /// Tuning for <see cref="StanceSolver"/>. Distances are metres, angles are degrees, speeds are m/s.
    /// </summary>
    [Serializable]
    public sealed class StanceSolverSettings
    {
        const float MinDistanceSpan = 0.01f;
        const float MinAngleSpan = 1f;

        [Header("Factor Weights")]
        [Min(0f), Tooltip("Weight of crouch depth in the grounding blend.")]
        public float crouchWeight = 0.55f;

        [Min(0f), Tooltip("Weight of torso lean in the grounding blend. Body tracking only.")]
        public float leanWeight = 0.25f;

        [Min(0f), Tooltip("Weight of stance width in the grounding blend. Full-body tracking only.")]
        public float widthWeight = 0.2f;

        [Header("Crouch: drop below the standing reference")]
        [Min(0f), Tooltip("Hip drop where crouch starts to count.")]
        public float hipDropStart = 0.04f;

        [Min(0f), Tooltip("Hip drop that counts as a full grounded crouch.")]
        public float hipDropFull = 0.22f;

        [Min(0f), Tooltip("Head drop where crouch starts to count, for the head-pose tier.")]
        public float headDropStart = 0.05f;

        [Min(0f), Tooltip("Head drop that counts as a full grounded crouch, for the head-pose tier.")]
        public float headDropFull = 0.25f;

        [Header("Torso Lean")]
        [Tooltip("Forward lean where bracing starts to count.")]
        public float leanStart = 4f;

        [Tooltip("Forward lean that counts as a fully braced core.")]
        public float leanIdeal = 18f;

        [Tooltip("Forward lean beyond which the player starts to overreach.")]
        public float leanOverreach = 40f;

        [Tooltip("Forward lean at which overreach removes all lean credit.")]
        public float leanCollapse = 65f;

        [Tooltip("Sideways lean tolerated without penalty.")]
        public float lateralTolerance = 8f;

        [Tooltip("Sideways lean that removes all lean credit.")]
        public float lateralCollapse = 28f;

        [Header("Stance Width: ankle distance")]
        [Min(0f), Tooltip("Ankle distance with no width credit.")]
        public float widthNarrow = 0.18f;

        [Min(0f), Tooltip("Ankle distance with full width credit.")]
        public float widthBraced = 0.45f;

        [Header("Balance")]
        [Range(0f, 1f), Tooltip("Center of mass estimate as a blend from hips (0) to chest (1).")]
        public float comChestBias = 0.35f;

        [Min(0f), Tooltip("Horizontal center of mass offset from the feet midpoint tolerated without penalty.")]
        public float comTolerance = 0.07f;

        [Min(0f), Tooltip("Horizontal center of mass offset that removes all balance credit.")]
        public float comLimit = 0.28f;

        [Min(0f), Tooltip("Reference speed tolerated without penalty.")]
        public float speedCalm = 0.06f;

        [Min(0f), Tooltip("Reference speed that removes all steadiness credit.")]
        public float speedUnstable = 0.55f;

        [Min(0.1f), Tooltip("Multiplier on the speed thresholds for the head-pose tier, since the head moves more than the hips.")]
        public float headSpeedScale = 2f;

        [Range(0f, 1f), Tooltip("Share of grounding that poor balance can remove.")]
        public float balanceInfluence = 0.5f;

        /// <summary>
        /// Clamps values into valid ranges and keeps every start/full pair ordered. Called on load and on inspector edits.
        /// </summary>
        public void Sanitize()
        {
            crouchWeight = Mathf.Max(0f, crouchWeight);
            leanWeight = Mathf.Max(0f, leanWeight);
            widthWeight = Mathf.Max(0f, widthWeight);
            if (crouchWeight + leanWeight + widthWeight <= 0f)
            {
                crouchWeight = 1f;
            }

            hipDropStart = Mathf.Max(0f, hipDropStart);
            hipDropFull = Mathf.Max(hipDropFull, hipDropStart + MinDistanceSpan);
            headDropStart = Mathf.Max(0f, headDropStart);
            headDropFull = Mathf.Max(headDropFull, headDropStart + MinDistanceSpan);

            leanIdeal = Mathf.Max(leanIdeal, leanStart + MinAngleSpan);
            leanOverreach = Mathf.Max(leanOverreach, leanIdeal);
            leanCollapse = Mathf.Max(leanCollapse, leanOverreach + MinAngleSpan);
            lateralTolerance = Mathf.Max(0f, lateralTolerance);
            lateralCollapse = Mathf.Max(lateralCollapse, lateralTolerance + MinAngleSpan);

            widthNarrow = Mathf.Max(0f, widthNarrow);
            widthBraced = Mathf.Max(widthBraced, widthNarrow + MinDistanceSpan);

            comChestBias = Mathf.Clamp01(comChestBias);
            comTolerance = Mathf.Max(0f, comTolerance);
            comLimit = Mathf.Max(comLimit, comTolerance + MinDistanceSpan);
            speedCalm = Mathf.Max(0f, speedCalm);
            speedUnstable = Mathf.Max(speedUnstable, speedCalm + MinDistanceSpan);
            headSpeedScale = Mathf.Max(0.1f, headSpeedScale);
            balanceInfluence = Mathf.Clamp01(balanceInfluence);
        }
    }

    /// <summary>
    /// Pure, allocation-free stance model. Converts a <see cref="BodyPoseSample"/> into a stance stability in [0, 1].
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Grounding</b> is a weighted blend of crouch depth, forward torso lean and stance width. Only the factors available
    /// to the active tier take part, and the weights are renormalized over them. The head-pose tier therefore relies on
    /// crouch alone, while full-body tracking uses all three.
    /// </para>
    /// <para>
    /// <b>Balance</b> is the product of center-of-mass support and steadiness. It scales grounding down by up to
    /// <see cref="StanceSolverSettings.balanceInfluence"/>, so a deep crouch while swaying still reads as unstable.
    /// </para>
    /// <para>
    /// In seated mode the chair counts as a grounded base: crouch is forced to 1, center of mass is ignored,
    /// and stability comes from forward lean and steadiness.
    /// </para>
    /// </remarks>
    public static class StanceSolver
    {
        /// <summary>Evaluates one pose sample.</summary>
        /// <param name="pose">Pose for this frame, in tracking space.</param>
        /// <param name="reference">Standing references to measure crouch depth against.</param>
        /// <param name="settings">Tuning values. Must not be null.</param>
        /// <param name="seated">Whether the player is in seated mode.</param>
        /// <returns>The factor breakdown and final stability. All zero when the sample has no source.</returns>
        public static StanceEvaluation Evaluate(
            in BodyPoseSample pose,
            in StanceReference reference,
            StanceSolverSettings settings,
            bool seated)
        {
            StanceEvaluation result = default;
            if (pose.Source == StanceTrackingSource.None || settings == null)
            {
                return result;
            }

            bool bodyTier = pose.Source == StanceTrackingSource.BodyTracking;

            // Crouch: hips are the most reliable signal, the head is the headset-only fallback.
            if (seated)
            {
                result.Crouch = 1f;
            }
            else if (bodyTier && pose.HasHips && reference.HasHips)
            {
                result.Crouch = Mathf.InverseLerp(
                    settings.hipDropStart, settings.hipDropFull, reference.StandingHipHeight - pose.Hips.y);
            }
            else if (pose.HasHead && reference.HasHead)
            {
                result.Crouch = Mathf.InverseLerp(
                    settings.headDropStart, settings.headDropFull, reference.StandingHeadHeight - pose.Head.y);
            }

            float weightedSum = settings.crouchWeight * result.Crouch;
            float weightTotal = settings.crouchWeight;

            // Torso lean: a moderate forward lean braces the core, overreach and sideways lean do not.
            if (bodyTier && pose.HasHips && pose.HasFacing && (pose.HasChest || pose.HasHead))
            {
                Vector3 spine = (pose.HasChest ? pose.Chest : pose.Head) - pose.Hips;
                Vector3 forward = pose.Forward;
                Vector3 right = new Vector3(forward.z, 0f, -forward.x);

                float forwardDegrees = Mathf.Atan2(Vector3.Dot(spine, forward), spine.y) * Mathf.Rad2Deg;
                float lateralDegrees = Mathf.Abs(Mathf.Atan2(Vector3.Dot(spine, right), spine.y)) * Mathf.Rad2Deg;

                float rise = Mathf.InverseLerp(settings.leanStart, settings.leanIdeal, forwardDegrees);
                float overreach = Mathf.InverseLerp(settings.leanOverreach, settings.leanCollapse, forwardDegrees);
                float sideways = Mathf.InverseLerp(settings.lateralTolerance, settings.lateralCollapse, lateralDegrees);

                result.ForwardLeanDegrees = forwardDegrees;
                result.LateralLeanDegrees = lateralDegrees;
                result.Lean = rise * (1f - overreach) * (1f - sideways);

                weightedSum += settings.leanWeight * result.Lean;
                weightTotal += settings.leanWeight;
            }

            // Stance width: a wide or staggered base is braced. Full-body tracking only.
            bool hasFeet = bodyTier && pose.HasFeet;
            if (hasFeet)
            {
                float dx = pose.RightFoot.x - pose.LeftFoot.x;
                float dz = pose.RightFoot.z - pose.LeftFoot.z;
                result.StanceWidthMeters = Mathf.Sqrt(dx * dx + dz * dz);
                result.Width = Mathf.InverseLerp(settings.widthNarrow, settings.widthBraced, result.StanceWidthMeters);

                weightedSum += settings.widthWeight * result.Width;
                weightTotal += settings.widthWeight;
            }

            result.Grounding = weightTotal > 0f ? Mathf.Clamp01(weightedSum / weightTotal) : 0f;

            // Center of mass over the base of support.
            result.CenterOfMass = 1f;
            if (hasFeet && pose.HasHips && !seated)
            {
                Vector3 centerOfMass = pose.HasChest
                    ? Vector3.LerpUnclamped(pose.Hips, pose.Chest, settings.comChestBias)
                    : pose.Hips;
                float offsetX = centerOfMass.x - (pose.LeftFoot.x + pose.RightFoot.x) * 0.5f;
                float offsetZ = centerOfMass.z - (pose.LeftFoot.z + pose.RightFoot.z) * 0.5f;
                float offset = Mathf.Sqrt(offsetX * offsetX + offsetZ * offsetZ);
                result.CenterOfMass = 1f - Mathf.InverseLerp(settings.comTolerance, settings.comLimit, offset);
            }

            // Steadiness: a braced body is a still body.
            float speedScale = bodyTier ? 1f : settings.headSpeedScale;
            result.Steadiness = 1f - Mathf.InverseLerp(
                settings.speedCalm * speedScale, settings.speedUnstable * speedScale, pose.ReferenceSpeed);

            result.Balance = result.CenterOfMass * result.Steadiness;
            result.Stability = Mathf.Clamp01(
                result.Grounding * Mathf.Lerp(1f - settings.balanceInfluence, 1f, result.Balance));

            return result;
        }
    }
}
