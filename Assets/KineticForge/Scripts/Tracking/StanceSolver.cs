using System;
using KineticForge.Core;
using UnityEngine;

namespace KineticForge.Tracking
{
    /// <summary>
    /// One frame of posture data in tracking space (meters, Y up), produced by <see cref="StanceTracker"/> and consumed by
    /// <see cref="StanceSolver"/>. The <c>Has*</c> flags mark which fields hold valid data for the active tier.
    /// </summary>
    public struct BodyPoseSample
    {
        /// <summary>Tier that produced this sample.</summary>
        public StanceTrackingSource Source;

        /// <summary>True when <see cref="Head"/> is valid.</summary>
        public bool HasHead;

        /// <summary>
        /// Head pivot position: the headset pose moved back along the neck model to the neck joint, so pitching the head
        /// to look down does not lower it. Falls back to the body skeleton's head joint without a headset.
        /// </summary>
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
    /// Standing references that crouch depth and torso lean are measured against, in tracking space.
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

        /// <summary>True when <see cref="NeutralForwardLeanDegrees"/> was captured by a body-tracked calibration.</summary>
        public bool HasNeutralLean;

        /// <summary>
        /// Forward torso lean measured while standing tall, in degrees. Subtracted from live lean so natural posture and
        /// skeleton rest-pose bias do not count as bracing. Clamped to ±<see cref="StanceSolver.MaxNeutralLeanDegrees"/>.
        /// </summary>
        public float NeutralForwardLeanDegrees;

        /// <summary>True when <see cref="HeadAnchor"/> and <see cref="AnchorForward"/> are valid.</summary>
        public bool HasHeadAnchor;

        /// <summary>
        /// Upright head pivot position from calibration. In seated mode, head travel away from it along
        /// <see cref="AnchorForward"/> estimates torso lean when no body tracking is available.
        /// </summary>
        public Vector3 HeadAnchor;

        /// <summary>Horizontal unit facing captured with <see cref="HeadAnchor"/>.</summary>
        public Vector3 AnchorForward;
    }

    /// <summary>
    /// Factor breakdown of one stance evaluation. Factor values are in [0, 1].
    /// </summary>
    public struct StanceEvaluation
    {
        /// <summary>Crouch depth below the standing reference. Not used in seated mode.</summary>
        public float Crouch;

        /// <summary>Quality of the forward torso lean, penalized for overreach and sideways lean.</summary>
        public float Lean;

        /// <summary>Stance width between the ankles.</summary>
        public float Width;

        /// <summary>How well the center of mass sits over the base of support. 1 when feet are not tracked.</summary>
        public float CenterOfMass;

        /// <summary>Stillness of the steadiness reference point.</summary>
        public float Steadiness;

        /// <summary>
        /// Signed forward torso lean in degrees, relative to the calibrated neutral lean when one exists.
        /// Positive leans toward the facing direction.
        /// </summary>
        public float ForwardLeanDegrees;

        /// <summary>Unsigned sideways torso lean in degrees.</summary>
        public float LateralLeanDegrees;

        /// <summary>Horizontal distance between the ankles in meters.</summary>
        public float StanceWidthMeters;

        /// <summary>Horizontal distance in meters from the center of mass to the centered support zone between the feet.</summary>
        public float CenterOfMassOffsetMeters;

        /// <summary>Weighted blend of crouch, lean and width over the factors available to the active tier.</summary>
        public float Grounding;

        /// <summary>Product of <see cref="CenterOfMass"/> and <see cref="Steadiness"/>.</summary>
        public float Balance;

        /// <summary>Final unsmoothed stance stability.</summary>
        public float Stability;
    }

    /// <summary>
    /// Tuning for <see cref="StanceSolver"/>. Distances are meters, angles are degrees, speeds are m/s.
    /// </summary>
    [Serializable]
    public sealed class StanceSolverSettings
    {
        const float MinDistanceSpan = 0.01f;
        const float MinAngleSpan = 1f;
        const float MinCrouchWeight = 0.01f;

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

        [Range(0f, 1f), Tooltip("Share of the ankle-to-ankle line, centered between the feet, over which the center of mass counts as fully supported. 0 measures from the midpoint only.")]
        public float comSupportSpan = 0.6f;

        [Range(0f, 0.15f), Tooltip("Shift of the support line along the facing direction, from the ankle joints to mid-foot.")]
        public float comFootForwardOffset = 0.05f;

        [Min(0f), Tooltip("Reference speed tolerated without penalty.")]
        public float speedCalm = 0.06f;

        [Min(0f), Tooltip("Reference speed that removes all steadiness credit.")]
        public float speedUnstable = 0.55f;

        [Min(0.1f), Tooltip("Multiplier on the speed thresholds for the head-pose tier, since the head moves more than the hips.")]
        public float headSpeedScale = 2f;

        [Range(0f, 1f), Tooltip("Share of grounding that poor balance can remove.")]
        public float balanceInfluence = 0.5f;

        [Header("Seated Mode")]
        [Range(0f, 1f), Tooltip("Grounding credited to the chair alone. The rest must come from leaning into the work.")]
        public float seatedBaseGrounding = 0.55f;

        [Min(0.2f), Tooltip("Hip-to-neck length (m) used to turn seated head travel into a lean angle when only the headset is tracked.")]
        public float seatedTorsoLength = 0.55f;

        /// <summary>
        /// Clamps values into valid ranges and keeps every start/full pair ordered. Called on load and on inspector edits.
        /// </summary>
        public void Sanitize()
        {
            StanceSolverSettings defaults = null;
            ReplaceNonFinite(ref crouchWeight, ref defaults, d => d.crouchWeight);
            ReplaceNonFinite(ref leanWeight, ref defaults, d => d.leanWeight);
            ReplaceNonFinite(ref widthWeight, ref defaults, d => d.widthWeight);
            ReplaceNonFinite(ref hipDropStart, ref defaults, d => d.hipDropStart);
            ReplaceNonFinite(ref hipDropFull, ref defaults, d => d.hipDropFull);
            ReplaceNonFinite(ref headDropStart, ref defaults, d => d.headDropStart);
            ReplaceNonFinite(ref headDropFull, ref defaults, d => d.headDropFull);
            ReplaceNonFinite(ref leanStart, ref defaults, d => d.leanStart);
            ReplaceNonFinite(ref leanIdeal, ref defaults, d => d.leanIdeal);
            ReplaceNonFinite(ref leanOverreach, ref defaults, d => d.leanOverreach);
            ReplaceNonFinite(ref leanCollapse, ref defaults, d => d.leanCollapse);
            ReplaceNonFinite(ref lateralTolerance, ref defaults, d => d.lateralTolerance);
            ReplaceNonFinite(ref lateralCollapse, ref defaults, d => d.lateralCollapse);
            ReplaceNonFinite(ref widthNarrow, ref defaults, d => d.widthNarrow);
            ReplaceNonFinite(ref widthBraced, ref defaults, d => d.widthBraced);
            ReplaceNonFinite(ref comChestBias, ref defaults, d => d.comChestBias);
            ReplaceNonFinite(ref comTolerance, ref defaults, d => d.comTolerance);
            ReplaceNonFinite(ref comLimit, ref defaults, d => d.comLimit);
            ReplaceNonFinite(ref comSupportSpan, ref defaults, d => d.comSupportSpan);
            ReplaceNonFinite(ref comFootForwardOffset, ref defaults, d => d.comFootForwardOffset);
            ReplaceNonFinite(ref speedCalm, ref defaults, d => d.speedCalm);
            ReplaceNonFinite(ref speedUnstable, ref defaults, d => d.speedUnstable);
            ReplaceNonFinite(ref headSpeedScale, ref defaults, d => d.headSpeedScale);
            ReplaceNonFinite(ref balanceInfluence, ref defaults, d => d.balanceInfluence);
            ReplaceNonFinite(ref seatedBaseGrounding, ref defaults, d => d.seatedBaseGrounding);
            ReplaceNonFinite(ref seatedTorsoLength, ref defaults, d => d.seatedTorsoLength);

            // Crouch is the only factor the head-pose tier has, so it can never be weighted out entirely. Renormalization
            // makes its absolute weight irrelevant whenever it is the only factor present.
            crouchWeight = Mathf.Max(MinCrouchWeight, crouchWeight);
            leanWeight = Mathf.Max(0f, leanWeight);
            widthWeight = Mathf.Max(0f, widthWeight);

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
            comSupportSpan = Mathf.Clamp01(comSupportSpan);
            comFootForwardOffset = Mathf.Clamp(comFootForwardOffset, 0f, 0.15f);
            speedCalm = Mathf.Max(0f, speedCalm);
            speedUnstable = Mathf.Max(speedUnstable, speedCalm + MinDistanceSpan);
            headSpeedScale = Mathf.Max(0.1f, headSpeedScale);
            balanceInfluence = Mathf.Clamp01(balanceInfluence);
            seatedBaseGrounding = Mathf.Clamp01(seatedBaseGrounding);
            seatedTorsoLength = Mathf.Max(0.2f, seatedTorsoLength);
        }

        static void ReplaceNonFinite(
            ref float value,
            ref StanceSolverSettings defaults,
            Func<StanceSolverSettings, float> select)
        {
            if (ForgeMath.IsFinite(value))
            {
                return;
            }

            // Only reached for corrupted data, so the allocation never happens in normal play.
            defaults ??= new StanceSolverSettings();
            value = select(defaults);
        }
    }

    /// <summary>
    /// Pure, allocation-free stance model. Converts a <see cref="BodyPoseSample"/> into a stance stability in [0, 1].
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Grounding</b> is a weighted blend of crouch depth, forward torso lean and stance width. Only the factors available
    /// to the active tier take part, and the weights are renormalized over them. The head-pose tier therefore relies on
    /// crouch alone, while full-body tracking uses all three. Forward lean is measured relative to the neutral lean captured
    /// at calibration, so a player's natural posture never counts as bracing.
    /// </para>
    /// <para>
    /// <b>Balance</b> is the product of center-of-mass support and steadiness. The center of mass, a blend of hips and
    /// chest, is projected onto the floor and measured against a support zone: the central part of the line between the
    /// feet, shifted forward from the ankle joints to mid-foot. Shifting weight within that zone costs nothing; drifting
    /// past a foot or off the line toward the toes or heels does. Balance scales grounding down by up to
    /// <see cref="StanceSolverSettings.balanceInfluence"/>, so a deep crouch while swaying still reads as unstable.
    /// </para>
    /// <para>
    /// In seated mode the chair supplies <see cref="StanceSolverSettings.seatedBaseGrounding"/>, and leaning into the work
    /// supplies the rest: grounding = lerp(seatedBase, 1, lean). Crouch, stance width and center of mass are ignored.
    /// Lean comes from the torso with body tracking, and otherwise from how far the head pivot has travelled from its
    /// calibrated upright position, converted to an angle over <see cref="StanceSolverSettings.seatedTorsoLength"/>.
    /// Sitting upright and still therefore never reads as a full brace.
    /// </para>
    /// <para>
    /// On Quest 3, full-body leg joints come from Meta's Generative Legs, which infer the legs from upper-body motion.
    /// Treat stance width and center of mass as supporting factors: they are plausible, not measured.
    /// </para>
    /// <para>Non-finite input never propagates: a NaN anywhere yields an all-zero evaluation.</para>
    /// </remarks>
    public static class StanceSolver
    {
        /// <summary>Largest neutral forward lean accepted from calibration, in degrees.</summary>
        public const float MaxNeutralLeanDegrees = 15f;

        /// <summary>
        /// Measures raw torso lean from hips to chest (or head) relative to the facing direction, without neutral
        /// correction. Unity tracking space is left-handed with Y up, so the facing's right is <c>(f.z, 0, -f.x)</c>.
        /// </summary>
        /// <param name="pose">Pose to measure. Needs hips, facing, and chest or head.</param>
        /// <param name="forwardDegrees">Signed lean toward the facing direction, in degrees.</param>
        /// <param name="lateralDegrees">Unsigned sideways lean, in degrees.</param>
        /// <returns>True when the pose holds the joints the measurement needs.</returns>
        public static bool TryMeasureLean(in BodyPoseSample pose, out float forwardDegrees, out float lateralDegrees)
        {
            forwardDegrees = 0f;
            lateralDegrees = 0f;
            if (!pose.HasHips || !pose.HasFacing || !(pose.HasChest || pose.HasHead))
            {
                return false;
            }

            Vector3 spine = (pose.HasChest ? pose.Chest : pose.Head) - pose.Hips;
            if (spine.sqrMagnitude < 1e-6f)
            {
                return false;
            }

            Vector3 forward = pose.Forward;
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);

            // Project the spine into the sagittal and frontal planes and measure each tilt from vertical.
            forwardDegrees = Mathf.Atan2(Vector3.Dot(spine, forward), spine.y) * Mathf.Rad2Deg;
            lateralDegrees = Mathf.Abs(Mathf.Atan2(Vector3.Dot(spine, right), spine.y)) * Mathf.Rad2Deg;
            return ForgeMath.IsFinite(forwardDegrees) && ForgeMath.IsFinite(lateralDegrees);
        }

        /// <summary>Evaluates one pose sample.</summary>
        /// <param name="pose">Pose for this frame, in tracking space.</param>
        /// <param name="reference">Standing references to measure crouch depth and lean against.</param>
        /// <param name="settings">Tuning values, already sanitized. Null yields an all-zero evaluation.</param>
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
            if (seated)
            {
                EvaluateSeatedGrounding(in pose, in reference, settings, bodyTier, ref result);
            }
            else
            {
                EvaluateStandingGrounding(in pose, in reference, settings, bodyTier, ref result);
            }

            // Steadiness: a braced body is a still body.
            float speedScale = bodyTier ? 1f : settings.headSpeedScale;
            result.Steadiness = 1f - Mathf.InverseLerp(
                settings.speedCalm * speedScale, settings.speedUnstable * speedScale, pose.ReferenceSpeed);

            result.Balance = result.CenterOfMass * result.Steadiness;
            result.Stability = Mathf.Clamp01(
                result.Grounding * Mathf.Lerp(1f - settings.balanceInfluence, 1f, result.Balance));

            if (!ForgeMath.IsFinite(result.Stability) || !ForgeMath.IsFinite(result.Grounding) ||
                !ForgeMath.IsFinite(result.Balance))
            {
                return default;
            }

            return result;
        }

        /// <summary>
        /// Estimates seated torso lean from how far the head pivot has travelled from its calibrated upright position:
        /// <c>asin(travel / torsoLength)</c> along and across the calibrated facing. Used when only the headset is tracked.
        /// </summary>
        /// <param name="pose">Pose with a head pivot.</param>
        /// <param name="reference">References holding the upright head anchor.</param>
        /// <param name="settings">Tuning values.</param>
        /// <param name="forwardDegrees">Signed forward lean in degrees.</param>
        /// <param name="lateralDegrees">Unsigned sideways lean in degrees.</param>
        /// <returns>True when both the pose and the references hold the data the estimate needs.</returns>
        public static bool TryMeasureSeatedHeadLean(
            in BodyPoseSample pose,
            in StanceReference reference,
            StanceSolverSettings settings,
            out float forwardDegrees,
            out float lateralDegrees)
        {
            forwardDegrees = 0f;
            lateralDegrees = 0f;
            if (!pose.HasHead || !reference.HasHeadAnchor || settings == null)
            {
                return false;
            }

            Vector3 forward = reference.AnchorForward;
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            Vector3 travel = pose.Head - reference.HeadAnchor;
            float inverseLength = 1f / Mathf.Max(0.2f, settings.seatedTorsoLength);

            forwardDegrees = Mathf.Asin(Mathf.Clamp(Vector3.Dot(travel, forward) * inverseLength, -1f, 1f)) * Mathf.Rad2Deg;
            lateralDegrees =
                Mathf.Abs(Mathf.Asin(Mathf.Clamp(Vector3.Dot(travel, right) * inverseLength, -1f, 1f))) * Mathf.Rad2Deg;
            return ForgeMath.IsFinite(forwardDegrees) && ForgeMath.IsFinite(lateralDegrees);
        }

        static void EvaluateStandingGrounding(
            in BodyPoseSample pose,
            in StanceReference reference,
            StanceSolverSettings settings,
            bool bodyTier,
            ref StanceEvaluation result)
        {
            // Crouch: hips are the most reliable signal, the head pivot is the headset-only fallback.
            if (bodyTier && pose.HasHips && reference.HasHips)
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
            if (bodyTier && TryMeasureLean(in pose, out float rawForwardDegrees, out float lateralDegrees))
            {
                result.ForwardLeanDegrees = rawForwardDegrees - NeutralLean(in reference);
                result.LateralLeanDegrees = lateralDegrees;
                result.Lean = LeanCredit(result.ForwardLeanDegrees, lateralDegrees, settings);

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

            // Center of mass over the support zone between the feet.
            result.CenterOfMass = 1f;
            if (hasFeet && pose.HasHips)
            {
                result.CenterOfMassOffsetMeters = MeasureSupportOffset(in pose, settings);
                result.CenterOfMass =
                    1f - Mathf.InverseLerp(settings.comTolerance, settings.comLimit, result.CenterOfMassOffsetMeters);
            }
        }

        static void EvaluateSeatedGrounding(
            in BodyPoseSample pose,
            in StanceReference reference,
            StanceSolverSettings settings,
            bool bodyTier,
            ref StanceEvaluation result)
        {
            // The chair is the base of support, so crouch, width and center of mass do not apply.
            result.CenterOfMass = 1f;

            bool hasLean;
            float forwardDegrees;
            float lateralDegrees;
            if (bodyTier && TryMeasureLean(in pose, out float rawForwardDegrees, out lateralDegrees))
            {
                forwardDegrees = rawForwardDegrees - NeutralLean(in reference);
                hasLean = true;
            }
            else
            {
                hasLean = TryMeasureSeatedHeadLean(in pose, in reference, settings, out forwardDegrees, out lateralDegrees);
            }

            if (hasLean)
            {
                result.ForwardLeanDegrees = forwardDegrees;
                result.LateralLeanDegrees = lateralDegrees;
                result.Lean = LeanCredit(forwardDegrees, lateralDegrees, settings);
            }

            result.Grounding = Mathf.Lerp(settings.seatedBaseGrounding, 1f, result.Lean);
        }

        static float LeanCredit(float forwardDegrees, float lateralDegrees, StanceSolverSettings settings)
        {
            float rise = Mathf.InverseLerp(settings.leanStart, settings.leanIdeal, forwardDegrees);
            float overreach = Mathf.InverseLerp(settings.leanOverreach, settings.leanCollapse, forwardDegrees);
            float sideways = Mathf.InverseLerp(settings.lateralTolerance, settings.lateralCollapse, lateralDegrees);
            return rise * (1f - overreach) * (1f - sideways);
        }

        static float NeutralLean(in StanceReference reference)
        {
            return reference.HasNeutralLean
                ? Mathf.Clamp(reference.NeutralForwardLeanDegrees, -MaxNeutralLeanDegrees, MaxNeutralLeanDegrees)
                : 0f;
        }

        /// <summary>
        /// Horizontal distance from the center of mass to the support zone: the central
        /// <see cref="StanceSolverSettings.comSupportSpan"/> share of the ankle-to-ankle line, shifted toward mid-foot.
        /// </summary>
        /// <param name="pose">Pose with hips and both feet.</param>
        /// <param name="settings">Tuning values.</param>
        /// <returns>The offset in meters.</returns>
        public static float MeasureSupportOffset(in BodyPoseSample pose, StanceSolverSettings settings)
        {
            Vector3 centerOfMass = pose.HasChest
                ? Vector3.LerpUnclamped(pose.Hips, pose.Chest, settings.comChestBias)
                : pose.Hips;

            Vector2 footShift = pose.HasFacing
                ? new Vector2(pose.Forward.x, pose.Forward.z) * settings.comFootForwardOffset
                : Vector2.zero;
            Vector2 left = new Vector2(pose.LeftFoot.x, pose.LeftFoot.z) + footShift;
            Vector2 right = new Vector2(pose.RightFoot.x, pose.RightFoot.z) + footShift;
            Vector2 middle = (left + right) * 0.5f;
            Vector2 halfZone = (right - left) * (0.5f * settings.comSupportSpan);

            Vector2 point = new Vector2(centerOfMass.x, centerOfMass.z);
            Vector2 closest = ForgeMath.ClosestPointOnSegment(point, middle - halfZone, middle + halfZone);
            return Vector2.Distance(point, closest);
        }
    }
}
