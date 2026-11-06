using System;
using UnityEngine;

namespace KineticForge.Core
{
    #region Shared enums

    /// <summary>
    /// Sensing tier that currently produces the stance stability signal.
    /// </summary>
    public enum StanceTrackingSource : byte
    {
        /// <summary>No pose source is available. Only the editor keyboard emulation can raise stability.</summary>
        None = 0,

        /// <summary>
        /// Headset pose only: Meta XR Simulator, Link without body tracking, or a plain editor camera.
        /// Stance is inferred from head drop and head steadiness.
        /// </summary>
        HeadPose = 1,

        /// <summary>Meta Inside-Out Body Tracking skeleton (upper body or full body).</summary>
        BodyTracking = 2,
    }

    /// <summary>
    /// Identifies the hand that produced an interaction.
    /// </summary>
    public enum ForgeHand : byte
    {
        /// <summary>Unknown hand, or an interaction not tied to a hand (for example a scripted strike).</summary>
        None = 0,

        /// <summary>The player's left hand.</summary>
        Left = 1,

        /// <summary>The player's right hand.</summary>
        Right = 2,
    }

    /// <summary>
    /// Top-level phases of the first-session game loop driven by <see cref="ForgeGameManager"/>.
    /// </summary>
    public enum ForgePhase : byte
    {
        /// <summary>The loop has not started yet.</summary>
        None = 0,

        /// <summary>Atmospheric introduction to the forge.</summary>
        Intro = 1,

        /// <summary>Standing reference capture followed by the first grounded brace.</summary>
        Calibration = 2,

        /// <summary>The player carves their first relic.</summary>
        FirstRelicForging = 3,

        /// <summary>Reward and completion sequence.</summary>
        Completion = 4,
    }

    /// <summary>
    /// Sub-steps of <see cref="ForgePhase.Calibration"/>, broadcast so tutorial UI can prompt the player.
    /// </summary>
    public enum CalibrationStep : byte
    {
        /// <summary>Calibration is not running.</summary>
        None = 0,

        /// <summary>The player is asked to stand tall and still before sampling starts.</summary>
        LeadIn = 1,

        /// <summary>The stance tracker is averaging the standing reference pose.</summary>
        SamplingNeutral = 2,

        /// <summary>The player must drop into a grounded stance and hold it.</summary>
        AwaitingBrace = 3,
    }

    #endregion

    #region Event payloads

    /// <summary>
    /// Payload of <see cref="ForgeEvents.StanceChanged"/>.
    /// </summary>
    public readonly struct StanceChangedEvent
    {
        /// <summary>Smoothed stance stability in [0, 1]. 0 is upright and unstable, 1 is fully grounded.</summary>
        public readonly float Stability;

        /// <summary>Stability carried by the previous broadcast, or the current value on the first broadcast.</summary>
        public readonly float PreviousStability;

        /// <summary>Sensing tier that produced the sensor part of the value.</summary>
        public readonly StanceTrackingSource Source;

        /// <summary>True while the editor keyboard emulation is above the sensor value and therefore drives it.</summary>
        public readonly bool IsEmulated;

        /// <summary>Creates a stance change payload.</summary>
        /// <param name="stability">Smoothed stability in [0, 1].</param>
        /// <param name="previousStability">Previously broadcast stability.</param>
        /// <param name="source">Active sensing tier.</param>
        /// <param name="isEmulated">Whether keyboard emulation dominates the value.</param>
        public StanceChangedEvent(float stability, float previousStability, StanceTrackingSource source, bool isEmulated)
        {
            Stability = stability;
            PreviousStability = previousStability;
            Source = source;
            IsEmulated = isEmulated;
        }

        /// <summary>Signed change since the previous broadcast.</summary>
        public float Delta => Stability - PreviousStability;
    }

    /// <summary>
    /// Payload of <see cref="ForgeEvents.StanceCalibrated"/>. Heights are metres along tracking-space up.
    /// </summary>
    public readonly struct StanceCalibratedEvent
    {
        /// <summary>Sensing tier active when calibration finished.</summary>
        public readonly StanceTrackingSource Source;

        /// <summary>True when at least one head sample was captured.</summary>
        public readonly bool HeadSampled;

        /// <summary>Averaged standing head height. Only meaningful when <see cref="HeadSampled"/> is true.</summary>
        public readonly float StandingHeadHeight;

        /// <summary>True when at least one body-tracked hip sample was captured.</summary>
        public readonly bool HipsSampled;

        /// <summary>Averaged standing hip height. Only meaningful when <see cref="HipsSampled"/> is true.</summary>
        public readonly float StandingHipHeight;

        /// <summary>Creates a calibration payload.</summary>
        /// <param name="source">Sensing tier active when calibration finished.</param>
        /// <param name="headSampled">Whether head samples were captured.</param>
        /// <param name="standingHeadHeight">Averaged standing head height in metres.</param>
        /// <param name="hipsSampled">Whether hip samples were captured.</param>
        /// <param name="standingHipHeight">Averaged standing hip height in metres.</param>
        public StanceCalibratedEvent(
            StanceTrackingSource source,
            bool headSampled,
            float standingHeadHeight,
            bool hipsSampled,
            float standingHipHeight)
        {
            Source = source;
            HeadSampled = headSampled;
            StandingHeadHeight = standingHeadHeight;
            HipsSampled = hipsSampled;
            StandingHipHeight = standingHipHeight;
        }

        /// <summary>True when no pose was available and the tracker kept its default or previous references.</summary>
        public bool UsedFallback => !HeadSampled && !HipsSampled;
    }

    /// <summary>
    /// Payload of <see cref="ForgeEvents.RelicGrabChanged"/>.
    /// </summary>
    public readonly struct RelicGrabEvent
    {
        /// <summary>Rigidbody of the relic that was grabbed or released.</summary>
        public readonly Rigidbody Relic;

        /// <summary>Hand performing the grab or release.</summary>
        public readonly ForgeHand Hand;

        /// <summary>True for a grab, false for a release.</summary>
        public readonly bool IsGrabbed;

        /// <summary>Creates a grab payload.</summary>
        /// <param name="relic">Rigidbody of the relic.</param>
        /// <param name="hand">Hand performing the interaction.</param>
        /// <param name="isGrabbed">True for a grab, false for a release.</param>
        public RelicGrabEvent(Rigidbody relic, ForgeHand hand, bool isGrabbed)
        {
            Relic = relic;
            Hand = hand;
            IsGrabbed = isGrabbed;
        }
    }

    /// <summary>
    /// Payload of <see cref="ForgeEvents.MicroGestureSlide"/>: one frame of thumb travel across the index finger.
    /// </summary>
    public readonly struct MicroGestureSlideEvent
    {
        /// <summary>Hand performing the slide.</summary>
        public readonly ForgeHand Hand;

        /// <summary>
        /// Thumb displacement this frame, normalized to index finger length.
        /// X runs across the finger (positive toward the thumb side), Y runs along it (positive toward the tip).
        /// </summary>
        public readonly Vector2 SlideDelta;

        /// <summary>Thumb-to-index contact strength in [0, 1], used as sliding friction.</summary>
        public readonly float ContactStrength;

        /// <summary>Seconds covered by <see cref="SlideDelta"/>.</summary>
        public readonly float DeltaTime;

        /// <summary>Creates a micro-gesture slide payload.</summary>
        /// <param name="hand">Hand performing the slide.</param>
        /// <param name="slideDelta">Normalized thumb displacement this frame.</param>
        /// <param name="contactStrength">Contact strength in [0, 1].</param>
        /// <param name="deltaTime">Seconds covered by the displacement.</param>
        public MicroGestureSlideEvent(ForgeHand hand, Vector2 slideDelta, float contactStrength, float deltaTime)
        {
            Hand = hand;
            SlideDelta = slideDelta;
            ContactStrength = contactStrength;
            DeltaTime = deltaTime;
        }

        /// <summary>Slide velocity in finger lengths per second, or zero when <see cref="DeltaTime"/> is not positive.</summary>
        public Vector2 Velocity => DeltaTime > 0f ? SlideDelta / DeltaTime : Vector2.zero;
    }

    /// <summary>
    /// Payload of <see cref="ForgeEvents.ChiselStrike"/>.
    /// </summary>
    public readonly struct ChiselStrikeEvent
    {
        /// <summary>Rigidbody of the relic that was struck. May be null for strikes on scenery.</summary>
        public readonly Rigidbody Relic;

        /// <summary>Hand that drove the strike.</summary>
        public readonly ForgeHand Hand;

        /// <summary>World-space contact point.</summary>
        public readonly Vector3 Point;

        /// <summary>World-space surface normal at the contact point.</summary>
        public readonly Vector3 Normal;

        /// <summary>Normalized strike energy in [0, 1].</summary>
        public readonly float Intensity;

        /// <summary>Stance-scaled carving precision in [0, 1].</summary>
        public readonly float Precision;

        /// <summary>Creates a chisel strike payload.</summary>
        /// <param name="relic">Rigidbody of the struck relic, or null.</param>
        /// <param name="hand">Hand that drove the strike.</param>
        /// <param name="point">World-space contact point.</param>
        /// <param name="normal">World-space surface normal.</param>
        /// <param name="intensity">Normalized strike energy in [0, 1].</param>
        /// <param name="precision">Stance-scaled precision in [0, 1].</param>
        public ChiselStrikeEvent(
            Rigidbody relic,
            ForgeHand hand,
            Vector3 point,
            Vector3 normal,
            float intensity,
            float precision)
        {
            Relic = relic;
            Hand = hand;
            Point = point;
            Normal = normal;
            Intensity = intensity;
            Precision = precision;
        }
    }

    /// <summary>
    /// Payload of <see cref="ForgeEvents.GamePhaseChanged"/>.
    /// </summary>
    public readonly struct GamePhaseChangedEvent
    {
        /// <summary>Phase that was exited.</summary>
        public readonly ForgePhase Previous;

        /// <summary>Phase that was entered.</summary>
        public readonly ForgePhase Current;

        /// <summary>Creates a phase change payload.</summary>
        /// <param name="previous">Phase that was exited.</param>
        /// <param name="current">Phase that was entered.</param>
        public GamePhaseChangedEvent(ForgePhase previous, ForgePhase current)
        {
            Previous = previous;
            Current = current;
        }
    }

    /// <summary>
    /// Payload of <see cref="ForgeEvents.ForgeProgressChanged"/>.
    /// </summary>
    public readonly struct ForgeProgressEvent
    {
        /// <summary>Chisel strikes that met the precision bar so far.</summary>
        public readonly int QualifiedStrikes;

        /// <summary>Qualified strikes required to finish the relic.</summary>
        public readonly int RequiredStrikes;

        /// <summary>Creates a progress payload.</summary>
        /// <param name="qualifiedStrikes">Qualified strikes so far.</param>
        /// <param name="requiredStrikes">Qualified strikes required.</param>
        public ForgeProgressEvent(int qualifiedStrikes, int requiredStrikes)
        {
            QualifiedStrikes = qualifiedStrikes;
            RequiredStrikes = requiredStrikes;
        }

        /// <summary>Normalized progress in [0, 1].</summary>
        public float Progress => RequiredStrikes > 0 ? Mathf.Clamp01((float)QualifiedStrikes / RequiredStrikes) : 1f;

        /// <summary>True once the required number of strikes has been reached.</summary>
        public bool IsComplete => QualifiedStrikes >= RequiredStrikes;
    }

    #endregion

    /// <summary>
    /// Centralized, strongly-typed event bus for cross-module gameplay signals.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Payloads are readonly structs delivered through <see cref="Action{T}"/>, so raising an event never allocates.
    /// Raise and handle events on the Unity main thread only.
    /// </para>
    /// <para>
    /// Subscribers should subscribe in <c>OnEnable</c> and unsubscribe in <c>OnDisable</c>, caching the delegate in a field
    /// so that enabling and disabling pooled objects does not allocate.
    /// </para>
    /// <para>
    /// Every subscription is cleared at <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/>, which keeps the bus
    /// clean when Enter Play Mode Options disable domain reload.
    /// </para>
    /// </remarks>
    public static class ForgeEvents
    {
        /// <summary>Raised when stance stability moves by more than the tracker's change threshold.</summary>
        public static event Action<StanceChangedEvent> StanceChanged;

        /// <summary>Raised by the game flow to ask the stance tracker to capture the standing reference pose.</summary>
        public static event Action CalibrationRequested;

        /// <summary>Raised by the stance tracker when it has captured the standing reference pose.</summary>
        public static event Action<StanceCalibratedEvent> StanceCalibrated;

        /// <summary>Raised when the calibration sub-step changes, including the reset to <see cref="CalibrationStep.None"/>.</summary>
        public static event Action<CalibrationStep> CalibrationStepChanged;

        /// <summary>Raised when a hand grabs or releases a relic.</summary>
        public static event Action<RelicGrabEvent> RelicGrabChanged;

        /// <summary>Raised each frame a thumb slides across the index finger while in contact.</summary>
        public static event Action<MicroGestureSlideEvent> MicroGestureSlide;

        /// <summary>Raised when a chisel strike lands.</summary>
        public static event Action<ChiselStrikeEvent> ChiselStrike;

        /// <summary>Raised after the game flow enters a new phase.</summary>
        public static event Action<GamePhaseChangedEvent> GamePhaseChanged;

        /// <summary>Raised when progress on the current relic changes.</summary>
        public static event Action<ForgeProgressEvent> ForgeProgressChanged;

        /// <summary>Raises <see cref="StanceChanged"/>.</summary>
        /// <param name="payload">Stance change data.</param>
        public static void RaiseStanceChanged(in StanceChangedEvent payload) => StanceChanged?.Invoke(payload);

        /// <summary>Raises <see cref="CalibrationRequested"/>.</summary>
        public static void RaiseCalibrationRequested() => CalibrationRequested?.Invoke();

        /// <summary>Raises <see cref="StanceCalibrated"/>.</summary>
        /// <param name="payload">Calibration result.</param>
        public static void RaiseStanceCalibrated(in StanceCalibratedEvent payload) => StanceCalibrated?.Invoke(payload);

        /// <summary>Raises <see cref="CalibrationStepChanged"/>.</summary>
        /// <param name="step">The calibration sub-step that was entered.</param>
        public static void RaiseCalibrationStepChanged(CalibrationStep step) => CalibrationStepChanged?.Invoke(step);

        /// <summary>Raises <see cref="RelicGrabChanged"/>.</summary>
        /// <param name="payload">Grab or release data.</param>
        public static void RaiseRelicGrabChanged(in RelicGrabEvent payload) => RelicGrabChanged?.Invoke(payload);

        /// <summary>Raises <see cref="MicroGestureSlide"/>.</summary>
        /// <param name="payload">Slide data.</param>
        public static void RaiseMicroGestureSlide(in MicroGestureSlideEvent payload) => MicroGestureSlide?.Invoke(payload);

        /// <summary>Raises <see cref="ChiselStrike"/>.</summary>
        /// <param name="payload">Strike data.</param>
        public static void RaiseChiselStrike(in ChiselStrikeEvent payload) => ChiselStrike?.Invoke(payload);

        /// <summary>Raises <see cref="GamePhaseChanged"/>.</summary>
        /// <param name="payload">Phase transition data.</param>
        public static void RaiseGamePhaseChanged(in GamePhaseChangedEvent payload) => GamePhaseChanged?.Invoke(payload);

        /// <summary>Raises <see cref="ForgeProgressChanged"/>.</summary>
        /// <param name="payload">Progress data.</param>
        public static void RaiseForgeProgressChanged(in ForgeProgressEvent payload) => ForgeProgressChanged?.Invoke(payload);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSubscriptions()
        {
            StanceChanged = null;
            CalibrationRequested = null;
            StanceCalibrated = null;
            CalibrationStepChanged = null;
            RelicGrabChanged = null;
            MicroGestureSlide = null;
            ChiselStrike = null;
            GamePhaseChanged = null;
            ForgeProgressChanged = null;
        }
    }
}
