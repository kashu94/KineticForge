using System;
using System.Globalization;
using UnityEngine;

namespace KineticForge.Core
{
    /// <summary>
    /// State machine for the first-session loop: Intro, Calibration, First Relic Forging and Completion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The manager lives in Core and never references tracking, physics or crafting types. It reacts to
    /// <see cref="ForgeEvents"/> signals and publishes phase changes, calibration steps and forge progress on the same bus,
    /// so UI, audio and gameplay systems stay decoupled from the flow logic.
    /// </para>
    /// <para>Flow:</para>
    /// <list type="number">
    /// <item><description>Intro plays for a fixed duration.</description></item>
    /// <item><description>
    /// Calibration shows a lead-in prompt, asks the stance tracker to sample the standing pose, then waits for the player
    /// to hold a grounded brace. A timeout keeps the flow moving if no tracker answers, and an optional skip timeout
    /// prevents a soft-lock for players who cannot brace.
    /// </description></item>
    /// <item><description>First Relic Forging counts chisel strikes that meet the precision bar.</description></item>
    /// <item><description>Completion plays the reward sequence and can loop back to the intro for kiosk demos.</description></item>
    /// </list>
    /// <para>
    /// A transition requested while another is being applied, for example by a <see cref="ForgeEvents.GamePhaseChanged"/>
    /// subscriber, is queued and applied after the current one finishes instead of nesting. The latest queued request wins.
    /// Timers use scaled time, so setting <see cref="Time.timeScale"/> to zero pauses the flow.
    /// </para>
    /// <para>
    /// Pausing: the manager combines <see cref="ForgePauseReason"/> flags from application focus, OS suspension,
    /// <see cref="ForgeEvents.PauseRequestChanged"/> (raised by platform monitors when the Quest system menu opens or the
    /// headset is removed) and <see cref="SetManualPause"/>. While any reason is active, flow timers stop, and optionally
    /// time scale and audio are frozen. <see cref="ForgeEvents.ExperiencePauseChanged"/> is raised on every change.
    /// Application focus and suspension are ignored in the Editor, so clicking other windows never stalls a play test.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Core/Forge Game Manager")]
    public sealed class ForgeGameManager : MonoBehaviour
    {
        const string LogSource = nameof(ForgeGameManager);
        const int MaxChainedTransitions = 8;

        [Header("Flow")]
        [SerializeField, Tooltip("Start the intro automatically when the scene starts.")]
        bool _autoStart = true;

        [Header("Intro")]
        [SerializeField, Min(0f), Tooltip("Seconds the intro plays before calibration begins.")]
        float _introDuration = 8f;

        [Header("Calibration")]
        [SerializeField, Min(0f), Tooltip("Seconds the 'stand tall and still' prompt shows before the standing pose is sampled.")]
        float _calibrationLeadIn = 2f;

        [SerializeField, Min(0.5f), Tooltip("Seconds to wait for StanceCalibrated before continuing with the tracker's current references.")]
        float _calibrationTimeout = 6f;

        [SerializeField, Range(0.05f, 1f), Tooltip("Stance stability the player must reach to count as braced.")]
        float _braceThreshold = 0.75f;

        [SerializeField, Range(0f, 0.5f), Tooltip("How far stability may dip below the threshold before an active brace is lost.")]
        float _braceHysteresis = 0.1f;

        [SerializeField, Min(0f), Tooltip("Seconds the brace must be held without being lost.")]
        float _braceHoldDuration = 1.5f;

        [SerializeField, Min(0f), Tooltip("Seconds after which the brace step is skipped so the player is never soft-locked. 0 disables the skip.")]
        float _braceSkipTimeout = 30f;

        [Header("First Relic")]
        [SerializeField, Min(1), Tooltip("Qualifying chisel strikes needed to forge the first relic.")]
        int _requiredStrikes = 12;

        [SerializeField, Range(0f, 1f), Tooltip("Minimum strike precision that counts toward forging progress.")]
        float _minStrikePrecision = 0.35f;

        [Header("Completion")]
        [SerializeField, Tooltip("Loop back to the intro after the completion sequence, for kiosk demos.")]
        bool _restartAfterCompletion = false;

        [SerializeField, Min(0f), Tooltip("Seconds the completion sequence plays before looping, when looping is enabled.")]
        float _completionDuration = 10f;

        [Header("Pause")]
        [SerializeField, Tooltip("Pause when the app loses focus, is suspended, the Quest system menu opens or the headset is removed. Manual pauses always work.")]
        bool _pauseOnFocusLoss = true;

        [SerializeField, Tooltip("Set Time.timeScale to 0 while paused, freezing physics and scaled-time effects. The previous value is restored on resume.")]
        bool _freezeTimeWhilePaused = true;

        [SerializeField, Tooltip("Pause all audio through AudioListener.pause while paused.")]
        bool _pauseAudioWhilePaused = true;

        ForgePhase _phase;
        CalibrationStep _calibrationStep;
        float _phaseElapsed;
        float _stepElapsed;
        float _latestStability;
        bool _isBraced;
        float _braceHeldTime;
        int _qualifiedStrikes;

        bool _isTransitioning;
        bool _hasPendingTransition;
        bool _pendingAllowsReentry;
        ForgePhase _pendingPhase;

        ForgePauseReason _pauseReasons;
        bool _isPaused;
        bool _ownsTimeScale;
        bool _ownsAudioPause;
        float _timeScaleBeforePause = 1f;

        Action<StanceChangedEvent> _onStanceChanged;
        Action<StanceCalibratedEvent> _onStanceCalibrated;
        Action<ChiselStrikeEvent> _onChiselStrike;
        Action<PauseRequestEvent> _onPauseRequest;

        #region Public API

        /// <summary>Current top-level phase.</summary>
        public ForgePhase Phase => _phase;

        /// <summary>Current calibration sub-step, or <see cref="CalibrationStep.None"/> outside calibration.</summary>
        public CalibrationStep CurrentCalibrationStep => _calibrationStep;

        /// <summary>Seconds spent in the current phase, in scaled time.</summary>
        public float PhaseElapsed => _phaseElapsed;

        /// <summary>Most recent stance stability received from <see cref="ForgeEvents.StanceChanged"/>.</summary>
        public float LatestStability => _latestStability;

        /// <summary>True while the player holds a brace during <see cref="CalibrationStep.AwaitingBrace"/>.</summary>
        public bool IsBraced => _isBraced;

        /// <summary>Progress of the current brace hold in [0, 1], for tutorial UI.</summary>
        public float BraceProgress =>
            _braceHoldDuration > 0f ? Mathf.Clamp01(_braceHeldTime / _braceHoldDuration) : (_isBraced ? 1f : 0f);

        /// <summary>Chisel strikes on the current relic that met the precision bar.</summary>
        public int QualifiedStrikes => _qualifiedStrikes;

        /// <summary>Qualified strikes needed to forge the first relic.</summary>
        public int RequiredStrikes => _requiredStrikes;

        /// <summary>Forging progress on the current relic in [0, 1].</summary>
        public float ForgeProgress => Mathf.Clamp01((float)_qualifiedStrikes / Mathf.Max(1, _requiredStrikes));

        /// <summary>True while any pause reason is active. Flow timers do not advance while paused.</summary>
        public bool IsPaused => _isPaused;

        /// <summary>Every pause reason currently active.</summary>
        public ForgePauseReason PauseReasons => _pauseReasons;

        /// <summary>Pauses or resumes from code, a debug panel or an in-game menu.</summary>
        /// <param name="paused">True to add the manual pause reason, false to clear it.</param>
        public void SetManualPause(bool paused) => SetPauseReason(ForgePauseReason.Manual, paused);

        /// <summary>Starts the loop at the intro. Does nothing if the loop is already running.</summary>
        public void StartExperience()
        {
            if (_phase == ForgePhase.None)
            {
                RequestTransition(ForgePhase.Intro, false);
            }
        }

        /// <summary>Restarts the loop at the intro, re-entering it if it is already active.</summary>
        public void RestartExperience() => RequestTransition(ForgePhase.Intro, true);

        /// <summary>Skips to the next phase; Completion wraps to the intro. Intended for debugging and demo control.</summary>
        public void AdvancePhase() => RequestTransition(NextPhase(_phase), true);

        #endregion

        #region Unity lifecycle

        void Awake()
        {
            _onStanceChanged = HandleStanceChanged;
            _onStanceCalibrated = HandleStanceCalibrated;
            _onChiselStrike = HandleChiselStrike;
            _onPauseRequest = HandlePauseRequest;
        }

        void OnEnable()
        {
            ForgeEvents.StanceChanged += _onStanceChanged;
            ForgeEvents.StanceCalibrated += _onStanceCalibrated;
            ForgeEvents.ChiselStrike += _onChiselStrike;
            ForgeEvents.PauseRequestChanged += _onPauseRequest;
        }

        void OnDisable()
        {
            ForgeEvents.StanceChanged -= _onStanceChanged;
            ForgeEvents.StanceCalibrated -= _onStanceCalibrated;
            ForgeEvents.ChiselStrike -= _onChiselStrike;
            ForgeEvents.PauseRequestChanged -= _onPauseRequest;

            // Never leave the game frozen or muted after this manager goes away, for example on scene unload.
            ClearAllPauseReasons();
        }

        void Start()
        {
            if (_autoStart)
            {
                StartExperience();
            }
        }

        void Update()
        {
            if (_phase == ForgePhase.None || _isPaused)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            _phaseElapsed += deltaTime;

            switch (_phase)
            {
                case ForgePhase.Intro:
                    if (_phaseElapsed >= _introDuration)
                    {
                        RequestTransition(ForgePhase.Calibration, false);
                    }
                    break;

                case ForgePhase.Calibration:
                    TickCalibration(deltaTime);
                    break;

                case ForgePhase.Completion:
                    if (_restartAfterCompletion && _phaseElapsed >= _completionDuration)
                    {
                        RequestTransition(ForgePhase.Intro, true);
                    }
                    break;
            }
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (!Application.isEditor)
            {
                SetPlatformPauseReason(ForgePauseReason.ApplicationFocus, !hasFocus);
            }
        }

        void OnApplicationPause(bool pauseStatus)
        {
            if (!Application.isEditor)
            {
                SetPlatformPauseReason(ForgePauseReason.ApplicationSuspended, pauseStatus);
            }
        }

        void OnValidate()
        {
            _requiredStrikes = Mathf.Max(1, _requiredStrikes);
            _braceHysteresis = Mathf.Min(_braceHysteresis, _braceThreshold);
        }

        [ContextMenu("Kinetic Forge/Advance Phase")]
        void AdvancePhaseFromContextMenu()
        {
            if (Application.isPlaying)
            {
                AdvancePhase();
            }
        }

        [ContextMenu("Kinetic Forge/Restart Experience")]
        void RestartExperienceFromContextMenu()
        {
            if (Application.isPlaying)
            {
                RestartExperience();
            }
        }

        #endregion

        #region Calibration

        void TickCalibration(float deltaTime)
        {
            _stepElapsed += deltaTime;

            switch (_calibrationStep)
            {
                case CalibrationStep.LeadIn:
                    if (_stepElapsed >= _calibrationLeadIn)
                    {
                        // Enter the step before raising, so a tracker that answers synchronously is still accepted.
                        SetCalibrationStep(CalibrationStep.SamplingNeutral);
                        ForgeEvents.RaiseCalibrationRequested();
                    }
                    break;

                case CalibrationStep.SamplingNeutral:
                    if (_stepElapsed >= _calibrationTimeout)
                    {
                        ForgeLog.Warn(ForgeLogChannel.Core, LogSource, "CALIBRATION_TIMEOUT",
                            "No StanceCalibrated event arrived within " +
                            _calibrationTimeout.ToString("0.#", CultureInfo.InvariantCulture) + " s. " +
                            "Check that a StanceTracker is active. Continuing with its current stance references.", this);
                        SetCalibrationStep(CalibrationStep.AwaitingBrace);
                    }
                    break;

                case CalibrationStep.AwaitingBrace:
                    TickBrace(deltaTime);
                    break;
            }
        }

        void TickBrace(float deltaTime)
        {
            float threshold = _isBraced ? _braceThreshold - _braceHysteresis : _braceThreshold;
            _isBraced = _latestStability >= threshold;
            _braceHeldTime = _isBraced ? _braceHeldTime + deltaTime : 0f;

            if (_isBraced && _braceHeldTime >= _braceHoldDuration)
            {
                RequestTransition(ForgePhase.FirstRelicForging, false);
                return;
            }

            if (_braceSkipTimeout > 0f && _stepElapsed >= _braceSkipTimeout)
            {
                ForgeLog.Info(ForgeLogChannel.Core, LogSource, "BRACE_SKIPPED",
                    "Brace was not held within the skip timeout; continuing to the first relic.", this);
                RequestTransition(ForgePhase.FirstRelicForging, false);
            }
        }

        void SetCalibrationStep(CalibrationStep step)
        {
            _stepElapsed = 0f;
            _isBraced = false;
            _braceHeldTime = 0f;

            if (step == _calibrationStep)
            {
                return;
            }

            _calibrationStep = step;
            ForgeEvents.RaiseCalibrationStepChanged(step);
        }

        #endregion

        #region Event handlers

        void HandleStanceChanged(StanceChangedEvent payload)
        {
            _latestStability = payload.Stability;
        }

        void HandleStanceCalibrated(StanceCalibratedEvent payload)
        {
            if (_phase != ForgePhase.Calibration || _calibrationStep != CalibrationStep.SamplingNeutral || _isPaused)
            {
                return;
            }

            if (payload.UsedFallback)
            {
                ForgeLog.Info(ForgeLogChannel.Core, LogSource, "CALIBRATION_FALLBACK",
                    "Stance tracker had no pose to sample; it keeps its default references.", this);
            }

            SetCalibrationStep(CalibrationStep.AwaitingBrace);
        }

        void HandleChiselStrike(ChiselStrikeEvent payload)
        {
            if (_phase != ForgePhase.FirstRelicForging || _isPaused || !(payload.Precision >= _minStrikePrecision))
            {
                return;
            }

            _qualifiedStrikes++;
            RaiseProgress();

            if (_qualifiedStrikes >= _requiredStrikes)
            {
                RequestTransition(ForgePhase.Completion, false);
            }
        }

        void HandlePauseRequest(PauseRequestEvent payload)
        {
            // Manual pauses always apply; platform reasons follow the focus-loss setting. A combined request does both.
            ForgePauseReason manual = payload.Reason & ForgePauseReason.Manual;
            if (manual != ForgePauseReason.None)
            {
                SetPauseReason(manual, payload.IsActive);
            }

            ForgePauseReason platform = payload.Reason & ~ForgePauseReason.Manual;
            if (platform != ForgePauseReason.None)
            {
                SetPlatformPauseReason(platform, payload.IsActive);
            }
        }

        #endregion

        #region Pause

        void SetPlatformPauseReason(ForgePauseReason reason, bool active)
        {
            // Platform reasons are honored only when enabled, but may always be cleared.
            if (active && !_pauseOnFocusLoss)
            {
                return;
            }

            SetPauseReason(reason, active);
        }

        void SetPauseReason(ForgePauseReason reason, bool active)
        {
            ForgePauseReason next = active ? _pauseReasons | reason : _pauseReasons & ~reason;
            if (next == _pauseReasons)
            {
                return;
            }

            _pauseReasons = next;
            bool paused = next != ForgePauseReason.None;
            if (paused != _isPaused)
            {
                _isPaused = paused;
                ApplyPauseSideEffects(paused);
                ForgeLog.Info(ForgeLogChannel.Core, LogSource, paused ? "PAUSED" : "RESUMED", "Reasons: " + next, this);

                // Poses sampled while the system menu is open are not standing poses. Rewind to the lead-in so the
                // player is prompted again and a fresh calibration is requested on resume.
                if (paused && _phase == ForgePhase.Calibration && _calibrationStep == CalibrationStep.SamplingNeutral)
                {
                    SetCalibrationStep(CalibrationStep.LeadIn);
                }
            }

            ForgeEvents.RaiseExperiencePauseChanged(new ExperiencePauseEvent(paused, next));
        }

        void ClearAllPauseReasons()
        {
            if (_pauseReasons == ForgePauseReason.None)
            {
                return;
            }

            _pauseReasons = ForgePauseReason.None;
            _isPaused = false;
            ApplyPauseSideEffects(false);
            ForgeEvents.RaiseExperiencePauseChanged(new ExperiencePauseEvent(false, ForgePauseReason.None));
        }

        void ApplyPauseSideEffects(bool paused)
        {
            if (paused)
            {
                if (_freezeTimeWhilePaused && !_ownsTimeScale)
                {
                    _timeScaleBeforePause = Time.timeScale;
                    Time.timeScale = 0f;
                    _ownsTimeScale = true;
                }

                if (_pauseAudioWhilePaused && !_ownsAudioPause)
                {
                    AudioListener.pause = true;
                    _ownsAudioPause = true;
                }

                return;
            }

            if (_ownsTimeScale)
            {
                // Restore only if nobody else changed the scale while paused.
                if (Time.timeScale == 0f)
                {
                    Time.timeScale = _timeScaleBeforePause;
                }

                _ownsTimeScale = false;
            }

            if (_ownsAudioPause)
            {
                AudioListener.pause = false;
                _ownsAudioPause = false;
            }
        }

        #endregion

        #region Transitions

        void RequestTransition(ForgePhase target, bool allowReentry)
        {
            _pendingPhase = target;
            _pendingAllowsReentry = allowReentry;
            _hasPendingTransition = true;

            if (_isTransitioning)
            {
                // The outer call drains the request once the current transition has finished.
                return;
            }

            _isTransitioning = true;
            try
            {
                int chained = 0;
                while (_hasPendingTransition)
                {
                    _hasPendingTransition = false;
                    ForgePhase next = _pendingPhase;

                    if (next == _phase && !_pendingAllowsReentry)
                    {
                        continue;
                    }

                    if (++chained > MaxChainedTransitions)
                    {
                        ForgeLog.Error(ForgeLogChannel.Core, LogSource, "TRANSITION_LOOP",
                            "Too many chained phase transitions in one request; dropping the rest. " +
                            "A GamePhaseChanged subscriber is probably requesting transitions in a cycle.", this);
                        break;
                    }

                    ApplyTransition(next);
                }
            }
            finally
            {
                _hasPendingTransition = false;
                _isTransitioning = false;
            }
        }

        void ApplyTransition(ForgePhase next)
        {
            ForgePhase previous = _phase;
            ExitPhase(previous);

            _phase = next;
            _phaseElapsed = 0f;

            ForgeLog.Info(ForgeLogChannel.Core, LogSource, "PHASE_CHANGED", previous + " -> " + next, this);
            ForgeEvents.RaiseGamePhaseChanged(new GamePhaseChangedEvent(previous, next));

            EnterPhase(next);
        }

        void ExitPhase(ForgePhase phase)
        {
            if (phase == ForgePhase.Calibration)
            {
                SetCalibrationStep(CalibrationStep.None);
            }
        }

        void EnterPhase(ForgePhase phase)
        {
            switch (phase)
            {
                case ForgePhase.Intro:
                    _qualifiedStrikes = 0;
                    break;

                case ForgePhase.Calibration:
                    SetCalibrationStep(CalibrationStep.LeadIn);
                    break;

                case ForgePhase.FirstRelicForging:
                    _qualifiedStrikes = 0;
                    RaiseProgress();
                    break;
            }
        }

        void RaiseProgress()
        {
            ForgeEvents.RaiseForgeProgressChanged(new ForgeProgressEvent(_qualifiedStrikes, _requiredStrikes));
        }

        static ForgePhase NextPhase(ForgePhase phase)
        {
            switch (phase)
            {
                case ForgePhase.None: return ForgePhase.Intro;
                case ForgePhase.Intro: return ForgePhase.Calibration;
                case ForgePhase.Calibration: return ForgePhase.FirstRelicForging;
                case ForgePhase.FirstRelicForging: return ForgePhase.Completion;
                default: return ForgePhase.Intro;
            }
        }

        #endregion
    }
}
