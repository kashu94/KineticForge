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
    /// Device that produced a micro-gesture, so feedback and tuning can tell hand tracking from fallbacks.
    /// </summary>
    public enum MicroGestureSource : byte
    {
        /// <summary>Unknown or scripted.</summary>
        None = 0,

        /// <summary>Tracked hand: thumb sliding on the index finger, or Meta's thumb-tap microgesture.</summary>
        HandTracking = 1,

        /// <summary>Touch controller: thumbstick for slides, face button for taps.</summary>
        Controller = 2,

        /// <summary>Editor mouse fallback: right button drag for slides, left click for taps.</summary>
        EditorMouse = 3,
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

    /// <summary>
    /// Reasons the experience can be paused. Several can be active at once; the experience resumes when none remain.
    /// </summary>
    [Flags]
    public enum ForgePauseReason : byte
    {
        /// <summary>No pause reason.</summary>
        None = 0,

        /// <summary>The application lost operating-system focus.</summary>
        ApplicationFocus = 1 << 0,

        /// <summary>The XR runtime took input focus, for example when the Quest system menu opens.</summary>
        InputFocus = 1 << 1,

        /// <summary>The player took the headset off.</summary>
        HeadsetRemoved = 1 << 2,

        /// <summary>Paused from code, a debug panel or an in-game menu.</summary>
        Manual = 1 << 3,

        /// <summary>The operating system suspended the application, for example when it moved to the background.</summary>
        ApplicationSuspended = 1 << 4,
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

        /// <summary>
        /// Unsmoothed grounding factor of the latest sensor evaluation in [0, 1]: crouch, lean and stance width.
        /// Lets feedback show why the stance is or is not stable.
        /// </summary>
        public readonly float Grounding;

        /// <summary>
        /// Unsmoothed balance factor of the latest sensor evaluation in [0, 1]: center of mass over the feet, times
        /// steadiness. Low values mean the player is swaying or off balance.
        /// </summary>
        public readonly float Balance;

        /// <summary>Creates a stance change payload.</summary>
        /// <param name="stability">Smoothed stability in [0, 1].</param>
        /// <param name="previousStability">Previously broadcast stability.</param>
        /// <param name="source">Active sensing tier.</param>
        /// <param name="isEmulated">Whether keyboard emulation dominates the value.</param>
        /// <param name="grounding">Latest sensor grounding factor in [0, 1].</param>
        /// <param name="balance">Latest sensor balance factor in [0, 1].</param>
        public StanceChangedEvent(
            float stability,
            float previousStability,
            StanceTrackingSource source,
            bool isEmulated,
            float grounding,
            float balance)
        {
            Stability = stability;
            PreviousStability = previousStability;
            Source = source;
            IsEmulated = isEmulated;
            Grounding = grounding;
            Balance = balance;
        }

        /// <summary>Signed change since the previous broadcast.</summary>
        public float Delta => Stability - PreviousStability;
    }

    /// <summary>
    /// Payload of <see cref="ForgeEvents.StanceCalibrated"/>. Heights are meters along tracking-space up.
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
        /// <param name="standingHeadHeight">Averaged standing head height in meters.</param>
        /// <param name="hipsSampled">Whether hip samples were captured.</param>
        /// <param name="standingHipHeight">Averaged standing hip height in meters.</param>
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
        /// Thumb displacement this frame, normalized to index finger length. X runs across the side of the finger
        /// (positive toward the back of the hand), Y runs along it (positive toward the tip). Controller and mouse
        /// fallbacks map their right and up to +X and +Y.
        /// </summary>
        public readonly Vector2 SlideDelta;

        /// <summary>Thumb-to-index contact strength in [0, 1], used as sliding friction.</summary>
        public readonly float ContactStrength;

        /// <summary>Seconds covered by <see cref="SlideDelta"/>.</summary>
        public readonly float DeltaTime;

        /// <summary>Device that produced the slide.</summary>
        public readonly MicroGestureSource Source;

        /// <summary>Creates a micro-gesture slide payload.</summary>
        /// <param name="hand">Hand performing the slide.</param>
        /// <param name="slideDelta">Normalized thumb displacement this frame.</param>
        /// <param name="contactStrength">Contact strength in [0, 1].</param>
        /// <param name="deltaTime">Seconds covered by the displacement.</param>
        /// <param name="source">Device that produced the slide.</param>
        public MicroGestureSlideEvent(
            ForgeHand hand,
            Vector2 slideDelta,
            float contactStrength,
            float deltaTime,
            MicroGestureSource source = MicroGestureSource.None)
        {
            Hand = hand;
            SlideDelta = slideDelta;
            ContactStrength = contactStrength;
            DeltaTime = deltaTime;
            Source = source;
        }

        /// <summary>Slide velocity in finger lengths per second, or zero when <see cref="DeltaTime"/> is not positive.</summary>
        public Vector2 Velocity => DeltaTime > 0f ? SlideDelta / DeltaTime : Vector2.zero;
    }

    /// <summary>
    /// Payload of <see cref="ForgeEvents.MicroGestureTap"/>: a discrete strike gesture, such as Meta's thumb tap on the side
    /// of the index finger.
    /// </summary>
    public readonly struct MicroGestureTapEvent
    {
        /// <summary>Hand that tapped.</summary>
        public readonly ForgeHand Hand;

        /// <summary>Tap strength in [0, 1]. Gestures without an analog strength report 1.</summary>
        public readonly float Strength;

        /// <summary>Device that produced the tap.</summary>
        public readonly MicroGestureSource Source;

        /// <summary>Creates a tap payload.</summary>
        /// <param name="hand">Hand that tapped.</param>
        /// <param name="strength">Tap strength in [0, 1].</param>
        /// <param name="source">Device that produced the tap.</param>
        public MicroGestureTapEvent(ForgeHand hand, float strength, MicroGestureSource source)
        {
            Hand = hand;
            Strength = strength;
            Source = source;
        }
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
    /// Payload of <see cref="ForgeEvents.RelicResonated"/>: what a chisel strike did to the relic as a resonating body.
    /// </summary>
    public readonly struct RelicResonanceEvent
    {
        /// <summary>Rigidbody of the struck relic.</summary>
        public readonly Rigidbody Relic;

        /// <summary>Hand that drove the strike.</summary>
        public readonly ForgeHand Hand;

        /// <summary>World-space contact point.</summary>
        public readonly Vector3 Point;

        /// <summary>World-space face normal at the contact point.</summary>
        public readonly Vector3 Normal;

        /// <summary>Contact point on the face, both axes in [0, 1].</summary>
        public readonly Vector2 FaceUV;

        /// <summary>Normalized strike energy in [0, 1].</summary>
        public readonly float Strength;

        /// <summary>Stance-scaled aim precision in [0, 1].</summary>
        public readonly float Precision;

        /// <summary>Timing against the relic's pulse in [−1, 1]: +1 perfectly in rhythm, −1 against it.</summary>
        public readonly float Timing;

        /// <summary>Resonance charge (pulse amplitude) after the strike.</summary>
        public readonly float Charge;

        /// <summary>Normalized heat after the strike: 1 fresh from the furnace, 0 cold.</summary>
        public readonly float Heat;

        /// <summary>How strongly the strike excited each of the four plate modes, in [0, 1].</summary>
        public readonly Vector4 ModeExcitation;

        /// <summary>Quality of the carved mark in [0, 1], or 0 when nothing was carved.</summary>
        public readonly float Quality;

        /// <summary>Index of the mark this strike carved, or −1.</summary>
        public readonly int CarvedMark;

        /// <summary>True when the strike cracked the relic.</summary>
        public readonly bool Cracked;

        /// <summary>Remaining integrity in [0, 1] after the strike.</summary>
        public readonly float Integrity;

        /// <summary>Creates a relic resonance payload.</summary>
        /// <param name="relic">Rigidbody of the struck relic.</param>
        /// <param name="hand">Hand that drove the strike.</param>
        /// <param name="point">World-space contact point.</param>
        /// <param name="normal">World-space face normal.</param>
        /// <param name="faceUV">Contact point on the face in [0, 1].</param>
        /// <param name="strength">Strike energy in [0, 1].</param>
        /// <param name="precision">Aim precision in [0, 1].</param>
        /// <param name="timing">Pulse timing in [−1, 1].</param>
        /// <param name="charge">Charge after the strike.</param>
        /// <param name="heat">Heat after the strike.</param>
        /// <param name="modeExcitation">Excitation per plate mode.</param>
        /// <param name="quality">Carve quality, or 0.</param>
        /// <param name="carvedMark">Carved mark index, or −1.</param>
        /// <param name="cracked">True when the strike cracked the relic.</param>
        /// <param name="integrity">Integrity after the strike.</param>
        public RelicResonanceEvent(
            Rigidbody relic,
            ForgeHand hand,
            Vector3 point,
            Vector3 normal,
            Vector2 faceUV,
            float strength,
            float precision,
            float timing,
            float charge,
            float heat,
            Vector4 modeExcitation,
            float quality,
            int carvedMark,
            bool cracked,
            float integrity)
        {
            Relic = relic;
            Hand = hand;
            Point = point;
            Normal = normal;
            FaceUV = faceUV;
            Strength = strength;
            Precision = precision;
            Timing = timing;
            Charge = charge;
            Heat = heat;
            ModeExcitation = modeExcitation;
            Quality = quality;
            CarvedMark = carvedMark;
            Cracked = cracked;
            Integrity = integrity;
        }
    }

    /// <summary>
    /// Payload of <see cref="ForgeEvents.RelicForged"/>: the result of a finished relic.
    /// </summary>
    public readonly struct RelicForgedEvent
    {
        /// <summary>Rigidbody of the forged relic.</summary>
        public readonly Rigidbody Relic;

        /// <summary>The grade.</summary>
        public readonly ForgeGrade Grade;

        /// <summary>Mean carve quality of all marks, in [0, 1].</summary>
        public readonly float MeanQuality;

        /// <summary>Remaining integrity in [0, 1].</summary>
        public readonly float Integrity;

        /// <summary>Longest run of consecutive strikes in rhythm.</summary>
        public readonly int BestStreak;

        /// <summary>Seconds from the first strike to the last mark.</summary>
        public readonly float Duration;

        /// <summary>Creates a relic forged payload.</summary>
        /// <param name="relic">Rigidbody of the forged relic.</param>
        /// <param name="grade">The grade.</param>
        /// <param name="meanQuality">Mean carve quality.</param>
        /// <param name="integrity">Remaining integrity.</param>
        /// <param name="bestStreak">Longest in-rhythm streak.</param>
        /// <param name="duration">Forging time, in seconds.</param>
        public RelicForgedEvent(Rigidbody relic, ForgeGrade grade, float meanQuality, float integrity, int bestStreak, float duration)
        {
            Relic = relic;
            Grade = grade;
            MeanQuality = meanQuality;
            Integrity = integrity;
            BestStreak = bestStreak;
            Duration = duration;
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

    /// <summary>
    /// Payload of <see cref="ForgeEvents.PauseRequestChanged"/>: one pause reason started or ended.
    /// </summary>
    public readonly struct PauseRequestEvent
    {
        /// <summary>The reason that changed. Usually a single flag.</summary>
        public readonly ForgePauseReason Reason;

        /// <summary>True when the reason became active, false when it cleared.</summary>
        public readonly bool IsActive;

        /// <summary>Creates a pause request payload.</summary>
        /// <param name="reason">The reason that changed.</param>
        /// <param name="isActive">Whether the reason became active.</param>
        public PauseRequestEvent(ForgePauseReason reason, bool isActive)
        {
            Reason = reason;
            IsActive = isActive;
        }
    }

    /// <summary>
    /// Payload of <see cref="ForgeEvents.ExperiencePauseChanged"/>: the experience paused or resumed.
    /// </summary>
    public readonly struct ExperiencePauseEvent
    {
        /// <summary>True while the experience is paused.</summary>
        public readonly bool IsPaused;

        /// <summary>Every pause reason active after the change. <see cref="ForgePauseReason.None"/> once resumed.</summary>
        public readonly ForgePauseReason Reasons;

        /// <summary>Creates a pause state payload.</summary>
        /// <param name="isPaused">Whether the experience is paused.</param>
        /// <param name="reasons">Active pause reasons.</param>
        public ExperiencePauseEvent(bool isPaused, ForgePauseReason reasons)
        {
            IsPaused = isPaused;
            Reasons = reasons;
        }
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
    /// Each event is backed by a <see cref="ForgeSignal{T}"/>. A handler that throws is logged and skipped, and the other
    /// handlers still run, so a faulty listener cannot break the game flow. Handlers may subscribe and unsubscribe while an
    /// event is being raised.
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
        static readonly ForgeSignal<StanceChangedEvent> s_stanceChanged =
            new ForgeSignal<StanceChangedEvent>("ForgeEvents.StanceChanged");

        static readonly ForgeSignal s_calibrationRequested = new ForgeSignal("ForgeEvents.CalibrationRequested");

        static readonly ForgeSignal<StanceCalibratedEvent> s_stanceCalibrated =
            new ForgeSignal<StanceCalibratedEvent>("ForgeEvents.StanceCalibrated");

        static readonly ForgeSignal<CalibrationStep> s_calibrationStepChanged =
            new ForgeSignal<CalibrationStep>("ForgeEvents.CalibrationStepChanged");

        static readonly ForgeSignal<RelicGrabEvent> s_relicGrabChanged =
            new ForgeSignal<RelicGrabEvent>("ForgeEvents.RelicGrabChanged");

        static readonly ForgeSignal<MicroGestureSlideEvent> s_microGestureSlide =
            new ForgeSignal<MicroGestureSlideEvent>("ForgeEvents.MicroGestureSlide");

        static readonly ForgeSignal<MicroGestureTapEvent> s_microGestureTap =
            new ForgeSignal<MicroGestureTapEvent>("ForgeEvents.MicroGestureTap");

        static readonly ForgeSignal<ChiselStrikeEvent> s_chiselStrike =
            new ForgeSignal<ChiselStrikeEvent>("ForgeEvents.ChiselStrike");

        static readonly ForgeSignal<RelicResonanceEvent> s_relicResonated =
            new ForgeSignal<RelicResonanceEvent>("ForgeEvents.RelicResonated");

        static readonly ForgeSignal<RelicForgedEvent> s_relicForged =
            new ForgeSignal<RelicForgedEvent>("ForgeEvents.RelicForged");

        static readonly ForgeSignal<GamePhaseChangedEvent> s_gamePhaseChanged =
            new ForgeSignal<GamePhaseChangedEvent>("ForgeEvents.GamePhaseChanged");

        static readonly ForgeSignal<ForgeProgressEvent> s_forgeProgressChanged =
            new ForgeSignal<ForgeProgressEvent>("ForgeEvents.ForgeProgressChanged");

        static readonly ForgeSignal<PauseRequestEvent> s_pauseRequestChanged =
            new ForgeSignal<PauseRequestEvent>("ForgeEvents.PauseRequestChanged");

        static readonly ForgeSignal<ExperiencePauseEvent> s_experiencePauseChanged =
            new ForgeSignal<ExperiencePauseEvent>("ForgeEvents.ExperiencePauseChanged");

        /// <summary>Raised when stance stability moves by more than the tracker's change threshold.</summary>
        public static event Action<StanceChangedEvent> StanceChanged
        {
            add => s_stanceChanged.Add(value);
            remove => s_stanceChanged.Remove(value);
        }

        /// <summary>Raised by the game flow to ask the stance tracker to capture the standing reference pose.</summary>
        public static event Action CalibrationRequested
        {
            add => s_calibrationRequested.Add(value);
            remove => s_calibrationRequested.Remove(value);
        }

        /// <summary>Raised by the stance tracker when it has captured the standing reference pose.</summary>
        public static event Action<StanceCalibratedEvent> StanceCalibrated
        {
            add => s_stanceCalibrated.Add(value);
            remove => s_stanceCalibrated.Remove(value);
        }

        /// <summary>Raised when the calibration sub-step changes, including the reset to <see cref="CalibrationStep.None"/>.</summary>
        public static event Action<CalibrationStep> CalibrationStepChanged
        {
            add => s_calibrationStepChanged.Add(value);
            remove => s_calibrationStepChanged.Remove(value);
        }

        /// <summary>Raised when a hand grabs or releases a relic.</summary>
        public static event Action<RelicGrabEvent> RelicGrabChanged
        {
            add => s_relicGrabChanged.Add(value);
            remove => s_relicGrabChanged.Remove(value);
        }

        /// <summary>Raised each frame a thumb slides across the index finger while in contact.</summary>
        public static event Action<MicroGestureSlideEvent> MicroGestureSlide
        {
            add => s_microGestureSlide.Add(value);
            remove => s_microGestureSlide.Remove(value);
        }

        /// <summary>Raised when a hand performs a discrete tap gesture, the trigger for a chisel strike.</summary>
        public static event Action<MicroGestureTapEvent> MicroGestureTap
        {
            add => s_microGestureTap.Add(value);
            remove => s_microGestureTap.Remove(value);
        }

        /// <summary>Raised when a chisel strike lands.</summary>
        public static event Action<ChiselStrikeEvent> ChiselStrike
        {
            add => s_chiselStrike.Add(value);
            remove => s_chiselStrike.Remove(value);
        }

        /// <summary>
        /// Raised after a chisel strike has rung the relic: its timing against the pulse, charge, heat, carve quality
        /// and any crack. Audio, haptics and effects listen here.
        /// </summary>
        public static event Action<RelicResonanceEvent> RelicResonated
        {
            add => s_relicResonated.Add(value);
            remove => s_relicResonated.Remove(value);
        }

        /// <summary>Raised when the last mark of a relic is carved, with its grade.</summary>
        public static event Action<RelicForgedEvent> RelicForged
        {
            add => s_relicForged.Add(value);
            remove => s_relicForged.Remove(value);
        }

        /// <summary>Raised after the game flow enters a new phase.</summary>
        public static event Action<GamePhaseChangedEvent> GamePhaseChanged
        {
            add => s_gamePhaseChanged.Add(value);
            remove => s_gamePhaseChanged.Remove(value);
        }

        /// <summary>Raised when progress on the current relic changes.</summary>
        public static event Action<ForgeProgressEvent> ForgeProgressChanged
        {
            add => s_forgeProgressChanged.Add(value);
            remove => s_forgeProgressChanged.Remove(value);
        }

        /// <summary>
        /// Raised by platform monitors when a pause reason starts or ends, for example when the Quest system menu opens.
        /// The game manager combines the reasons and publishes <see cref="ExperiencePauseChanged"/>.
        /// </summary>
        public static event Action<PauseRequestEvent> PauseRequestChanged
        {
            add => s_pauseRequestChanged.Add(value);
            remove => s_pauseRequestChanged.Remove(value);
        }

        /// <summary>Raised by the game manager when the experience pauses or resumes.</summary>
        public static event Action<ExperiencePauseEvent> ExperiencePauseChanged
        {
            add => s_experiencePauseChanged.Add(value);
            remove => s_experiencePauseChanged.Remove(value);
        }

        /// <summary>Raises <see cref="StanceChanged"/>.</summary>
        /// <param name="payload">Stance change data.</param>
        public static void RaiseStanceChanged(in StanceChangedEvent payload) => s_stanceChanged.Raise(payload);

        /// <summary>Raises <see cref="CalibrationRequested"/>.</summary>
        public static void RaiseCalibrationRequested() => s_calibrationRequested.Raise();

        /// <summary>Raises <see cref="StanceCalibrated"/>.</summary>
        /// <param name="payload">Calibration result.</param>
        public static void RaiseStanceCalibrated(in StanceCalibratedEvent payload) => s_stanceCalibrated.Raise(payload);

        /// <summary>Raises <see cref="CalibrationStepChanged"/>.</summary>
        /// <param name="step">The calibration sub-step that was entered.</param>
        public static void RaiseCalibrationStepChanged(CalibrationStep step) => s_calibrationStepChanged.Raise(step);

        /// <summary>Raises <see cref="RelicGrabChanged"/>.</summary>
        /// <param name="payload">Grab or release data.</param>
        public static void RaiseRelicGrabChanged(in RelicGrabEvent payload) => s_relicGrabChanged.Raise(payload);

        /// <summary>Raises <see cref="MicroGestureSlide"/>.</summary>
        /// <param name="payload">Slide data.</param>
        public static void RaiseMicroGestureSlide(in MicroGestureSlideEvent payload) => s_microGestureSlide.Raise(payload);

        /// <summary>Raises <see cref="MicroGestureTap"/>.</summary>
        /// <param name="payload">Tap data.</param>
        public static void RaiseMicroGestureTap(in MicroGestureTapEvent payload) => s_microGestureTap.Raise(payload);

        /// <summary>Raises <see cref="ChiselStrike"/>.</summary>
        /// <param name="payload">Strike data.</param>
        public static void RaiseChiselStrike(in ChiselStrikeEvent payload) => s_chiselStrike.Raise(payload);

        /// <summary>Raises <see cref="RelicResonated"/>.</summary>
        /// <param name="payload">Event data.</param>
        public static void RaiseRelicResonated(in RelicResonanceEvent payload) => s_relicResonated.Raise(payload);

        /// <summary>Raises <see cref="RelicForged"/>.</summary>
        /// <param name="payload">Event data.</param>
        public static void RaiseRelicForged(in RelicForgedEvent payload) => s_relicForged.Raise(payload);

        /// <summary>Raises <see cref="GamePhaseChanged"/>.</summary>
        /// <param name="payload">Phase transition data.</param>
        public static void RaiseGamePhaseChanged(in GamePhaseChangedEvent payload) => s_gamePhaseChanged.Raise(payload);

        /// <summary>Raises <see cref="ForgeProgressChanged"/>.</summary>
        /// <param name="payload">Progress data.</param>
        public static void RaiseForgeProgressChanged(in ForgeProgressEvent payload) => s_forgeProgressChanged.Raise(payload);

        /// <summary>Raises <see cref="PauseRequestChanged"/>.</summary>
        /// <param name="payload">Pause reason change.</param>
        public static void RaisePauseRequestChanged(in PauseRequestEvent payload) => s_pauseRequestChanged.Raise(payload);

        /// <summary>Raises <see cref="ExperiencePauseChanged"/>.</summary>
        /// <param name="payload">New pause state.</param>
        public static void RaiseExperiencePauseChanged(in ExperiencePauseEvent payload) =>
            s_experiencePauseChanged.Raise(payload);

        /// <summary>Removes every subscriber from every event. Called automatically before each Play Mode session.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetSubscriptions()
        {
            s_stanceChanged.Clear();
            s_calibrationRequested.Clear();
            s_stanceCalibrated.Clear();
            s_calibrationStepChanged.Clear();
            s_relicGrabChanged.Clear();
            s_microGestureSlide.Clear();
            s_microGestureTap.Clear();
            s_chiselStrike.Clear();
            s_relicResonated.Clear();
            s_relicForged.Clear();
            s_gamePhaseChanged.Clear();
            s_forgeProgressChanged.Clear();
            s_pauseRequestChanged.Clear();
            s_experiencePauseChanged.Clear();
        }
    }
}
