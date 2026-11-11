using System;
using System.Globalization;
#if KF_META_XR_CORE
using System.Collections.Generic;
#endif
using KineticForge.Core;
using UnityEngine;

namespace KineticForge.Tracking
{
    /// <summary>
    /// Macro half of Dual-Scale Input. Converts the player's posture into the normalized <see cref="StanceStability"/>
    /// scalar in [0, 1] that the physics and crafting layers consume.
    /// </summary>
    /// <remarks>
    /// <para>Sources are chosen every frame in priority order, degrading gracefully:</para>
    /// <list type="number">
    /// <item><description>
    /// Body tracking: an <c>OVRSkeleton</c> of type Body or FullBody fed by <c>OVRBody</c> (Meta Inside-Out Body Tracking).
    /// Uses hip drop, torso lean, ankle stance width, center of mass over the feet and hip steadiness. The tier activates
    /// only after its data has been valid and high-confidence for a short promotion delay, and brief dropouts are bridged
    /// by holding the last body evaluation, so confidence flicker cannot thrash the value.
    /// </description></item>
    /// <item><description>
    /// Head pose: the <c>OVRCameraRig</c> center eye, else the head override, else <c>Camera.main</c>. Uses head drop and
    /// head steadiness. This is the tier the Meta XR Simulator exercises.
    /// </description></item>
    /// <item><description>
    /// Keyboard emulation, Editor only: holding Space or Down Arrow smoothly drives stability to 1. It overlays both tiers,
    /// so it also works inside the Meta XR Simulator.
    /// </description></item>
    /// </list>
    /// <para>
    /// Every joint passes through a 1€ filter before evaluation, which removes millimeter tracking jitter without adding
    /// lag to real movement. Steadiness uses the velocity of the filtered joint, smoothed as a vector, so sensor noise does
    /// not read as sway at any refresh rate. Implausible jumps, for example after a recenter, restart the filters instead of
    /// registering as a lunge.
    /// </para>
    /// <para>
    /// Body tracking code compiles only when <c>com.meta.xr.sdk.core</c> 60+ is installed, which defines KF_META_XR_CORE
    /// through the assembly definition. Missing or misconfigured rig and body components are reported once as structured
    /// warnings and never throw. Components spawned later are picked up automatically.
    /// </para>
    /// <para>
    /// Crouch is measured as a drop below standing reference heights, and forward lean relative to the neutral lean. Both
    /// are captured by <see cref="RequestCalibration"/> (raised by the game flow through
    /// <see cref="ForgeEvents.CalibrationRequested"/>). Height references also initialize lazily from the first pose and
    /// creep upward when the player stands taller than them. Use a floor-level tracking origin.
    /// </para>
    /// <para>
    /// All work runs in <c>LateUpdate</c>, after <c>OVRSkeleton</c> and <c>OVRCameraRig</c> have written this frame's
    /// poses, in unscaled time so pauses and slow motion never distort the player's body. The per-frame path does not
    /// allocate.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Tracking/Stance Tracker")]
    public sealed class StanceTracker : MonoBehaviour
    {
        const string LogSource = nameof(StanceTracker);
        const float SnapEpsilon = 0.0005f;
        const float MaxDeltaTime = 0.1f;
        const float MaxPlausibleSpeed = 5f;
        const float MinTeleportDistance = 0.05f;
        const float MinDirectionSqrMagnitude = 0.0001f;
        const float MinFlatFacingSqrMagnitude = 0.04f;
        const float ResolveInterval = 1f;
        const float MaxRiseAboveCalibration = 0.05f;
        const float MinAnchorFacingSqrMagnitude = 0.01f;

        #region Serialized fields

#if KF_META_XR_CORE
        [Header("Meta XR Sources")]
        [SerializeField, Tooltip("Camera rig providing tracking space and the center-eye pose. Found in the scene when empty.")]
        OVRCameraRig _cameraRig = null;

        [SerializeField, Tooltip("OVRSkeleton with Skeleton Type Body or FullBody, fed by OVRBody. Found in the scene when empty.")]
        OVRSkeleton _bodySkeleton = null;

        [SerializeField, Min(0f), Tooltip("Seconds body data must stay valid and high-confidence before it replaces the head-pose tier.")]
        float _bodyPromotionDelay = 0.3f;

        [SerializeField, Min(0f), Tooltip("Seconds a body-tracking dropout is bridged by holding the last body evaluation before falling back to the head-pose tier.")]
        float _bodyDropoutGrace = 0.25f;

        [SerializeField, Tooltip("Ask the runtime for high-fidelity Inside-Out Body Tracking (Quest 3) the first time body data arrives.")]
        bool _requestHighFidelityBodyTracking = true;
#endif

        [Header("Head")]
        [SerializeField, Tooltip("Head transform used when no OVRCameraRig is available. Camera.main is used when empty.")]
        Transform _headOverride = null;

        [SerializeField, Tooltip("Offset from the neck pivot to the eyes in head space (Meta's default neck model). Stance is measured at the pivot, so looking down at the anvil never reads as a crouch.")]
        Vector3 _neckToEyeOffset = new Vector3(0f, 0.075f, 0.0805f);

        [Header("Solver")]
        [SerializeField]
        StanceSolverSettings _solverSettings = new StanceSolverSettings();

        [SerializeField, Tooltip("Seated play: the chair counts as a grounded base; stability comes from forward lean and steadiness.")]
        bool _seatedMode = false;

        [Header("Joint Filtering")]
        [SerializeField, Tooltip("1€ filter applied to every tracked joint before evaluation. Removes tracking jitter while keeping fast moves responsive.")]
        OneEuroSettings _jointFilter = OneEuroSettings.BodyJoints;

        [Header("Smoothing")]
        [SerializeField, Min(0.01f), Tooltip("Exponential rate (1/s) at which stability rises while settling into a stance.")]
        float _riseRate = 3.5f;

        [SerializeField, Min(0.01f), Tooltip("Exponential rate (1/s) at which stability falls when the stance is lost.")]
        float _fallRate = 7f;

        [SerializeField, Min(0.01f), Tooltip("Exponential rate (1/s) used to smooth the steadiness velocity estimate.")]
        float _speedSmoothingRate = 10f;

        [SerializeField, Range(0.0005f, 0.05f), Tooltip("Smallest stability change that is broadcast. Reaching 0 or 1 is always broadcast.")]
        float _changeEpsilon = 0.002f;

        [Header("Calibration")]
        [SerializeField, Min(0.1f), Tooltip("Seconds the standing pose is averaged when calibration is requested.")]
        float _calibrationDuration = 1f;

        [SerializeField, Min(0f), Tooltip("Max speed (m/s) at which an uncalibrated standing reference creeps up when the player stands taller than it.")]
        float _referenceRiseSpeed = 0.05f;

        [SerializeField, Min(0f), Tooltip("Max creep speed (m/s) after calibration. Kept low, and capped 5 cm above the calibrated height, so tiptoeing cannot make normal standing read as a crouch.")]
        float _calibratedReferenceRiseSpeed = 0.01f;

        [SerializeField, Tooltip("Calibrate when ForgeEvents.CalibrationRequested is raised.")]
        bool _respondToCalibrationRequests = true;

        [Header("Simulator Fallback (Editor only)")]
        [SerializeField, Tooltip("Hold a brace key to smoothly drive stability to 1. Inert in player builds.")]
        bool _enableKeyboardEmulation = true;

        [SerializeField, Tooltip("Primary brace key.")]
        KeyCode _braceKey = KeyCode.Space;

        [SerializeField, Tooltip("Secondary brace key.")]
        KeyCode _alternateBraceKey = KeyCode.DownArrow;

        [SerializeField, Tooltip("Press once to lock the brace on, again to release it. Handy on laptops that disable the touchpad while a key is held.")]
        KeyCode _braceToggleKey = KeyCode.B;

        [SerializeField, Min(0.01f), Tooltip("Exponential rate (1/s) toward 1 while a brace key is held.")]
        float _emulationRiseRate = 4f;

        [SerializeField, Min(0.01f), Tooltip("Exponential rate (1/s) back toward 0 after the brace key is released.")]
        float _emulationFallRate = 6f;

        [Header("Diagnostics")]
        [SerializeField, Tooltip("Log source switches, late-bound components, bone caching and calibration results.")]
        bool _verboseLogging = false;

        #endregion

        #region Runtime state

        Transform _trackingSpace;
        bool _hasTrackingSpace;
        Transform _head;
        float _nextResolveTime;
        OneEuroSettings _filterSettings;

#if KF_META_XR_CORE
        Transform _bodySpace;
        float _bodyValidTime;
        float _bodyLostTime;
        bool _fidelityRequested;
        bool _bonesCached;
        int _cachedSkeletonChangedCount;
        Transform _boneHips;
        Transform _boneChest;
        Transform _boneHead;
        Transform _boneLeftArm;
        Transform _boneRightArm;
        Transform _boneLeftFoot;
        Transform _boneRightFoot;
        bool _hasBoneChest;
        bool _hasBoneHead;
        bool _hasBoneArms;
        bool _hasBoneFeet;

        TrackedJoint _hipsJoint;
        TrackedJoint _chestJoint;
        TrackedJoint _bodyHeadJoint;
        TrackedJoint _leftArmJoint;
        TrackedJoint _rightArmJoint;
        TrackedJoint _leftFootJoint;
        TrackedJoint _rightFootJoint;
#endif

        TrackedJoint _headJoint;
        VelocityEstimator _headVelocity;
        VelocityEstimator _hipsVelocity;
        bool _bodyHeld;
        double _frameTime;
        float _headCeiling = float.PositiveInfinity;
        float _hipCeiling = float.PositiveInfinity;

        BodyPoseSample _pose;
        StanceReference _reference;
        StanceEvaluation _evaluation;
        StanceTrackingSource _source;

        float _sensorStability;
        float _emulatedStability;
        bool _braceLocked;
        float _stability;
        float _lastBroadcastStability = -1f;

        bool _calibrating;
        bool _calibrated;
        float _calibrationElapsed;
        double _headHeightSum;
        double _hipHeightSum;
        double _leanSum;
        Vector3 _headPositionSum;
        Vector3 _facingSum;
        int _headSampleCount;
        int _hipSampleCount;
        int _leanSampleCount;
        int _facingSampleCount;

        readonly ForgeSignal<float> _stabilityChanged = new ForgeSignal<float>("StanceTracker.OnStanceStabilityChanged");

        readonly ForgeSignal<StanceTrackingSource> _sourceChanged =
            new ForgeSignal<StanceTrackingSource>("StanceTracker.OnTrackingSourceChanged");

        Action _onCalibrationRequested;
        Action<ExperiencePauseEvent> _onExperiencePauseChanged;
        bool _subscribedToCalibration;

        #endregion

        #region Public API

        /// <summary>
        /// Raised with the new <see cref="StanceStability"/> when it moves by more than the change threshold, when it
        /// reaches exactly 0 or 1, and once on the first evaluated frame after enabling. A handler that throws is logged and
        /// does not stop the other handlers or the tracker.
        /// </summary>
        public event Action<float> OnStanceStabilityChanged
        {
            add => _stabilityChanged.Add(value);
            remove => _stabilityChanged.Remove(value);
        }

        /// <summary>Raised when the active sensing tier changes.</summary>
        public event Action<StanceTrackingSource> OnTrackingSourceChanged
        {
            add => _sourceChanged.Add(value);
            remove => _sourceChanged.Remove(value);
        }

        /// <summary>Final stance stability in [0, 1]: the larger of the smoothed sensor value and the keyboard emulation.</summary>
        public float StanceStability => _stability;

        /// <summary>Smoothed stability from the active sensing tier only.</summary>
        public float SensorStability => _sensorStability;

        /// <summary>Keyboard emulation value in [0, 1]. Always 0 in player builds.</summary>
        public float EmulatedStability => _emulatedStability;

        /// <summary>True while keyboard emulation is above the sensor value and therefore drives <see cref="StanceStability"/>.</summary>
        public bool IsEmulationDominant => _emulatedStability > _sensorStability;

        /// <summary>Sensing tier used on the latest frame.</summary>
        public StanceTrackingSource Source => _source;

        /// <summary>
        /// True while a brief body-tracking dropout is being bridged: <see cref="LastEvaluation"/> holds the last body
        /// evaluation instead of a fresh one.
        /// </summary>
        public bool IsBridgingDropout => _bodyHeld;

        /// <summary>Filtered pose of the latest frame in tracking space, for debug overlays and tuning.</summary>
        public BodyPoseSample LastPose => _pose;

        /// <summary>Factor breakdown of the latest evaluation, for debug overlays and tuning.</summary>
        public StanceEvaluation LastEvaluation => _evaluation;

        /// <summary>Standing references the solver currently measures crouch and lean against.</summary>
        public StanceReference Reference => _reference;

        /// <summary>Transform that poses are expressed in, or null when poses are in world space.</summary>
        public Transform TrackingSpace => _trackingSpace;

        /// <summary>True while the standing pose is being sampled.</summary>
        public bool IsCalibrating => _calibrating;

        /// <summary>True once at least one calibration has completed.</summary>
        public bool IsCalibrated => _calibrated;

        /// <summary>Seated play mode. The chair counts as a grounded base.</summary>
        public bool SeatedMode
        {
            get => _seatedMode;
            set => _seatedMode = value;
        }

        /// <summary>Solver tuning. Call <see cref="StanceSolverSettings.Sanitize"/> after changing values from code.</summary>
        public StanceSolverSettings SolverSettings => _solverSettings;

        /// <summary>Joint filter tuning. Assigned values are sanitized.</summary>
        public OneEuroSettings JointFilterSettings
        {
            get => _filterSettings;
            set
            {
                _jointFilter = value;
                _filterSettings = value.Sanitized();
            }
        }

        /// <summary>
        /// Starts averaging the standing pose for the configured duration, then raises
        /// <see cref="ForgeEvents.StanceCalibrated"/>. The player should stand tall and still. Restarts any calibration in
        /// progress.
        /// </summary>
        public void RequestCalibration()
        {
            _calibrating = true;
            _calibrationElapsed = 0f;
            _headHeightSum = 0d;
            _hipHeightSum = 0d;
            _leanSum = 0d;
            _headPositionSum = Vector3.zero;
            _facingSum = Vector3.zero;
            _headSampleCount = 0;
            _hipSampleCount = 0;
            _leanSampleCount = 0;
            _facingSampleCount = 0;

            if (_verboseLogging)
            {
                ForgeLog.Info(ForgeLogChannel.Tracking, LogSource, "CALIBRATION_STARTED",
                    "Sampling the standing pose for " + _calibrationDuration.ToString("0.##", CultureInfo.InvariantCulture) +
                    " s.", this);
            }
        }

        /// <summary>Converts a tracking-space point, such as one from <see cref="LastPose"/>, to world space.</summary>
        /// <param name="trackingSpacePoint">Point in tracking space.</param>
        /// <returns>The point in world space.</returns>
        public Vector3 TrackingToWorldPoint(Vector3 trackingSpacePoint)
        {
            return _trackingSpace != null ? _trackingSpace.TransformPoint(trackingSpacePoint) : trackingSpacePoint;
        }

#if KF_META_XR_CORE
        /// <summary>
        /// Binds a body skeleton at runtime, for example when the rig is spawned after this tracker started.
        /// Pass null to disable the body-tracking tier until another skeleton is found.
        /// </summary>
        /// <param name="skeleton">An <c>OVRSkeleton</c> of type Body or FullBody, or null.</param>
        public void BindBodySkeleton(OVRSkeleton skeleton)
        {
            if (skeleton != null && !IsBodySkeleton(skeleton))
            {
                ForgeLog.Warn(ForgeLogChannel.Tracking, LogSource, "BODY_SKELETON_INVALID",
                    "OVRSkeleton '" + skeleton.name + "' is of type " + skeleton.GetSkeletonType() +
                    ", expected Body or FullBody. Binding ignored.", this);
                return;
            }

            _bodySkeleton = skeleton;
            InvalidateBoneCache();
            ResetBodyJoints();
            if (skeleton != null)
            {
                ValidateBodyProvider(skeleton);
            }
        }

        /// <summary>
        /// Binds a camera rig at runtime, for example when the rig is spawned after this tracker started.
        /// </summary>
        /// <param name="rig">The rig providing tracking space and the center-eye pose, or null.</param>
        public void BindCameraRig(OVRCameraRig rig)
        {
            _cameraRig = rig;
            SetTrackingSpace(rig != null ? rig.trackingSpace : null);
            ResolveHead();
        }
#endif

        #endregion

        #region Unity lifecycle

        void Awake()
        {
            if (_solverSettings == null)
            {
                _solverSettings = new StanceSolverSettings();
            }

            _solverSettings.Sanitize();
            _filterSettings = _jointFilter.Sanitized();
            _onCalibrationRequested = RequestCalibration;
            _onExperiencePauseChanged = HandleExperiencePauseChanged;
        }

        void OnEnable()
        {
            if (_respondToCalibrationRequests)
            {
                ForgeEvents.CalibrationRequested += _onCalibrationRequested;
                _subscribedToCalibration = true;
            }

            ForgeEvents.ExperiencePauseChanged += _onExperiencePauseChanged;
            ResetAllJoints();
            _lastBroadcastStability = -1f;
        }

        void OnDisable()
        {
            if (_subscribedToCalibration)
            {
                ForgeEvents.CalibrationRequested -= _onCalibrationRequested;
                _subscribedToCalibration = false;
            }

            ForgeEvents.ExperiencePauseChanged -= _onExperiencePauseChanged;
            _calibrating = false;
            _bodyHeld = false;
        }

        void Start()
        {
            ResolveSources();
        }

        void LateUpdate()
        {
            float rawDeltaTime = Time.unscaledDeltaTime;
            if (!(rawDeltaTime > 0f))
            {
                return;
            }

            // Filters and velocities use true timestamps; perceptual smoothing is capped so a hitch cannot snap it.
            float deltaTime = Mathf.Min(rawDeltaTime, MaxDeltaTime);
            _frameTime = Time.unscaledTimeAsDouble;

            if (Time.unscaledTime >= _nextResolveTime)
            {
                RetryMissingSources();
            }

            _hasTrackingSpace = _trackingSpace != null;

            StanceTrackingSource tier = SamplePose(rawDeltaTime);
            if (tier != _source)
            {
                SetSource(tier);
            }

            _pose.ReferenceSpeed = tier == StanceTrackingSource.BodyTracking ? _hipsVelocity.Speed : _headVelocity.Speed;

            if (_calibrating)
            {
                AccumulateCalibration(deltaTime);
            }
            else
            {
                UpdateReferences(deltaTime);
            }

            if (!_bodyHeld)
            {
                _evaluation = StanceSolver.Evaluate(in _pose, in _reference, _solverSettings, _seatedMode);
            }

            float sensorTarget = ForgeMath.Sanitize01(_evaluation.Stability);
            float sensorRate = sensorTarget > _sensorStability ? _riseRate : _fallRate;
            _sensorStability = ForgeMath.DampSnap(_sensorStability, sensorTarget, sensorRate, deltaTime, SnapEpsilon);

            UpdateEmulation(deltaTime);
            Broadcast(Mathf.Max(_sensorStability, _emulatedStability));
        }

        void OnValidate()
        {
            if (_solverSettings != null)
            {
                _solverSettings.Sanitize();
            }

            _filterSettings = _jointFilter.Sanitized();
        }

        #endregion

        #region Source resolution

        void ResolveSources()
        {
            _nextResolveTime = Time.unscaledTime + ResolveInterval;

#if KF_META_XR_CORE
            if (_cameraRig == null)
            {
                _cameraRig = FindAnyObjectByType<OVRCameraRig>();
            }

            if (_cameraRig != null)
            {
                SetTrackingSpace(_cameraRig.trackingSpace);
            }
            else
            {
                ForgePlatform.ReportMissing(ForgeLogChannel.Tracking, LogSource, "CAMERA_RIG_MISSING",
                    "No OVRCameraRig found. Head pose falls back to the head override or Camera.main, in world space. " +
                    "A rig spawned later is picked up automatically.", this);
            }

            if (_bodySkeleton != null && !IsBodySkeleton(_bodySkeleton))
            {
                ForgeLog.Warn(ForgeLogChannel.Tracking, LogSource, "BODY_SKELETON_INVALID",
                    "Assigned OVRSkeleton '" + _bodySkeleton.name + "' is of type " + _bodySkeleton.GetSkeletonType() +
                    ", expected Body or FullBody. Ignoring it and searching the scene.", this);
                _bodySkeleton = null;
            }

            if (_bodySkeleton == null)
            {
                _bodySkeleton = FindBodySkeleton();
            }

            if (_bodySkeleton == null)
            {
                ForgePlatform.ReportMissing(ForgeLogChannel.Tracking, LogSource, "BODY_SKELETON_MISSING",
                    "No OVRSkeleton with Skeleton Type Body or FullBody found. Body tracking is off; " +
                    "using the head-pose tier and simulator fallback. A skeleton spawned later is picked up automatically.",
                    this);
            }
            else
            {
                ValidateBodyProvider(_bodySkeleton);
            }

            InvalidateBoneCache();
#else
            ForgeLog.Warn(ForgeLogChannel.Tracking, LogSource, "META_XR_CORE_MISSING",
                "Meta XR Core SDK (com.meta.xr.sdk.core 60+) is not installed, so body tracking is compiled out. " +
                "Using the head-pose tier and simulator fallback.", this);
#endif

            ResolveHead();
            if (_head == null)
            {
                ForgeLog.Warn(ForgeLogChannel.Tracking, LogSource, "HEAD_POSE_MISSING",
                    "No head transform found (OVRCameraRig, head override or Camera.main). " +
                    "Stance relies on the editor keyboard fallback until one appears.", this);
            }
        }

        void RetryMissingSources()
        {
            // Allocation-free re-resolution of components that were missing or destroyed, checked once per second.
            _nextResolveTime = Time.unscaledTime + ResolveInterval;

#if KF_META_XR_CORE
            if (_cameraRig == null)
            {
                OVRCameraRig rig = FindAnyObjectByType<OVRCameraRig>();
                if (rig != null)
                {
                    BindCameraRig(rig);
                    LogLateBinding("CAMERA_RIG_BOUND", "Bound OVRCameraRig '" + rig.name + "'.");
                }
            }
            else if (_trackingSpace == null && _cameraRig.trackingSpace != null)
            {
                SetTrackingSpace(_cameraRig.trackingSpace);
            }

            if (_bodySkeleton == null)
            {
                OVRSkeleton skeleton = FindBodySkeletonWithoutAllocating();
                if (skeleton != null)
                {
                    BindBodySkeleton(skeleton);
                    LogLateBinding("BODY_SKELETON_BOUND", "Bound OVRSkeleton '" + skeleton.name + "'.");
                }
            }
#endif

            if (_head == null)
            {
                ResolveHead();
            }
        }

        void LogLateBinding(string code, string message)
        {
            if (_verboseLogging)
            {
                ForgeLog.Info(ForgeLogChannel.Tracking, LogSource, code, message, this);
            }
        }

        void ResolveHead()
        {
            Transform previous = _head;

            if (_headOverride != null)
            {
                _head = _headOverride;
            }
#if KF_META_XR_CORE
            else if (_cameraRig != null && _cameraRig.centerEyeAnchor != null)
            {
                _head = _cameraRig.centerEyeAnchor;
            }
#endif
            else
            {
                Camera mainCamera = Camera.main;
                _head = mainCamera != null ? mainCamera.transform : null;
            }

            if (_head != previous)
            {
                _headJoint.Reset();
                _headVelocity.Reset();
            }
        }

#if KF_META_XR_CORE
        static OVRSkeleton FindBodySkeleton()
        {
            // One-time scene query at startup. The array allocation never happens per frame.
            OVRSkeleton[] skeletons = FindObjectsByType<OVRSkeleton>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < skeletons.Length; i++)
            {
                if (IsBodySkeleton(skeletons[i]))
                {
                    return skeletons[i];
                }
            }

            return null;
        }

        static OVRSkeleton FindBodySkeletonWithoutAllocating()
        {
            // OVRSkeleton looks for its data provider on itself and its parents, so a body skeleton sits on or below an
            // OVRBody. Both lookups return single components and do not allocate.
            OVRBody body = FindAnyObjectByType<OVRBody>();
            if (body == null)
            {
                return null;
            }

            OVRSkeleton skeleton = body.GetComponentInChildren<OVRSkeleton>();
            return skeleton != null && IsBodySkeleton(skeleton) ? skeleton : null;
        }

        static bool IsBodySkeleton(OVRSkeleton skeleton)
        {
            OVRSkeleton.SkeletonType type = skeleton.GetSkeletonType();
            return type == OVRSkeleton.SkeletonType.Body || type == OVRSkeleton.SkeletonType.FullBody;
        }

        void ValidateBodyProvider(OVRSkeleton skeleton)
        {
            OVRBody provider = skeleton.GetComponentInParent<OVRBody>(true);
            if (provider == null)
            {
                ForgeLog.Warn(ForgeLogChannel.Tracking, LogSource, "OVR_BODY_MISSING",
                    "OVRSkeleton '" + skeleton.name + "' has no OVRBody on itself or a parent, so it receives no data. " +
                    "Add OVRBody to the same GameObject; until then the head-pose tier is used.", this);
                return;
            }

            bool fullBodySkeleton = skeleton.GetSkeletonType() == OVRSkeleton.SkeletonType.FullBody;
            bool fullBodyProvider = provider.ProvidedSkeletonType == OVRPlugin.BodyJointSet.FullBody;
            if (fullBodySkeleton != fullBodyProvider)
            {
                ForgeLog.Warn(ForgeLogChannel.Tracking, LogSource, "BODY_PROVIDER_MISMATCH",
                    "OVRSkeleton '" + skeleton.name + "' expects " + skeleton.GetSkeletonType() + " but OVRBody '" +
                    provider.name + "' provides " + provider.ProvidedSkeletonType + ". Set both to Full Body (and Body " +
                    "Tracking Joint Set to Full Body in OVRManager); until then the skeleton receives no data.", this);
            }
        }

        void RequestHighFidelityOnce()
        {
            if (_fidelityRequested || !_requestHighFidelityBodyTracking)
            {
                return;
            }

            _fidelityRequested = true;
            bool accepted;
            try
            {
                accepted = OVRPlugin.RequestBodyTrackingFidelity(OVRPlugin.BodyTrackingFidelity2.High);
            }
            catch (Exception exception)
            {
                // A missing native plugin (for example an unsupported Editor platform) must never break tracking.
                ForgeLog.Warn(ForgeLogChannel.Tracking, LogSource, "BODY_FIDELITY_UNAVAILABLE",
                    "Could not request high-fidelity body tracking: " + exception.GetType().Name + ".", this);
                return;
            }

            if (_verboseLogging)
            {
                ForgeLog.Info(ForgeLogChannel.Tracking, LogSource, "BODY_FIDELITY_REQUESTED",
                    accepted ? "High-fidelity body tracking requested." : "Runtime declined high-fidelity body tracking.",
                    this);
            }
        }

        bool CacheBones(OVRSkeleton skeleton)
        {
            InvalidateBoneCache();

            IList<OVRBone> bones = skeleton.Bones;
            if (bones == null || bones.Count == 0)
            {
                return false;
            }

            // Body and FullBody bone ids share numeric values, so pick the id set by skeleton type and compare
            // against locals rather than switching over both sets.
            bool fullBody = skeleton.GetSkeletonType() == OVRSkeleton.SkeletonType.FullBody;
            OVRSkeleton.BoneId hipsId = fullBody ? OVRSkeleton.BoneId.FullBody_Hips : OVRSkeleton.BoneId.Body_Hips;
            OVRSkeleton.BoneId chestId = fullBody ? OVRSkeleton.BoneId.FullBody_Chest : OVRSkeleton.BoneId.Body_Chest;
            OVRSkeleton.BoneId headId = fullBody ? OVRSkeleton.BoneId.FullBody_Head : OVRSkeleton.BoneId.Body_Head;
            OVRSkeleton.BoneId leftArmId =
                fullBody ? OVRSkeleton.BoneId.FullBody_LeftArmUpper : OVRSkeleton.BoneId.Body_LeftArmUpper;
            OVRSkeleton.BoneId rightArmId =
                fullBody ? OVRSkeleton.BoneId.FullBody_RightArmUpper : OVRSkeleton.BoneId.Body_RightArmUpper;

            for (int i = 0; i < bones.Count; i++)
            {
                OVRBone bone = bones[i];
                if (bone == null || bone.Transform == null)
                {
                    continue;
                }

                OVRSkeleton.BoneId id = bone.Id;
                if (id == hipsId)
                {
                    _boneHips = bone.Transform;
                }
                else if (id == chestId)
                {
                    _boneChest = bone.Transform;
                }
                else if (id == headId)
                {
                    _boneHead = bone.Transform;
                }
                else if (id == leftArmId)
                {
                    _boneLeftArm = bone.Transform;
                }
                else if (id == rightArmId)
                {
                    _boneRightArm = bone.Transform;
                }
                else if (fullBody && id == OVRSkeleton.BoneId.FullBody_LeftFootAnkle)
                {
                    _boneLeftFoot = bone.Transform;
                }
                else if (fullBody && id == OVRSkeleton.BoneId.FullBody_RightFootAnkle)
                {
                    _boneRightFoot = bone.Transform;
                }
            }

            if (_boneHips == null)
            {
                return false;
            }

            _hasBoneChest = _boneChest != null;
            _hasBoneHead = _boneHead != null;
            _hasBoneArms = _boneLeftArm != null && _boneRightArm != null;
            _hasBoneFeet = _boneLeftFoot != null && _boneRightFoot != null;
            _cachedSkeletonChangedCount = skeleton.SkeletonChangedCount;
            _bodySpace = skeleton.transform;
            _bonesCached = true;

            if (_verboseLogging)
            {
                ForgeLog.Info(ForgeLogChannel.Tracking, LogSource, "BONES_CACHED",
                    (fullBody ? "FullBody" : "Body") + " skeleton: chest=" + _hasBoneChest + " head=" + _hasBoneHead +
                    " arms=" + _hasBoneArms + " feet=" + _hasBoneFeet + ".", this);
            }

            return true;
        }

        void InvalidateBoneCache()
        {
            _bonesCached = false;
            _bodySpace = null;
            _boneHips = null;
            _boneChest = null;
            _boneHead = null;
            _boneLeftArm = null;
            _boneRightArm = null;
            _boneLeftFoot = null;
            _boneRightFoot = null;
            _hasBoneChest = false;
            _hasBoneHead = false;
            _hasBoneArms = false;
            _hasBoneFeet = false;
        }
#endif

        void SetTrackingSpace(Transform space)
        {
            if (space == _trackingSpace)
            {
                return;
            }

            bool hadReferences = _reference.HasHead || _reference.HasHips || _reference.HasHeadAnchor;
            _trackingSpace = space;
            _hasTrackingSpace = space != null;
            ResetAllJoints();

            // Heights measured in the old space mean nothing in the new one; re-learn them instead of misreading them.
            if (hadReferences)
            {
                ResetHeightReferences();
                ForgeLog.Info(ForgeLogChannel.Tracking, LogSource, "REFERENCES_RESET",
                    "Tracking space changed; standing references will be re-learned. Recalibrate for best results.", this);
            }
        }

        void ResetHeightReferences()
        {
            _reference.HasHead = false;
            _reference.HasHips = false;
            _reference.HasHeadAnchor = false;
            _headCeiling = float.PositiveInfinity;
            _hipCeiling = float.PositiveInfinity;
            _calibrated = false;
            _calibrating = false;
        }

        void HandleExperiencePauseChanged(ExperiencePauseEvent payload)
        {
            // A pose sampled while the system menu is open is not a standing pose. The game flow asks again on resume.
            if (payload.IsPaused && _calibrating)
            {
                _calibrating = false;
                if (_verboseLogging)
                {
                    ForgeLog.Info(ForgeLogChannel.Tracking, LogSource, "CALIBRATION_ABORTED",
                        "Calibration cancelled because the experience paused (" + payload.Reasons + ").", this);
                }
            }
        }

        void ResetAllJoints()
        {
            _headJoint.Reset();
            _headVelocity.Reset();
            ResetBodyJoints();
        }

        void ResetBodyJoints()
        {
#if KF_META_XR_CORE
            _hipsJoint.Reset();
            _chestJoint.Reset();
            _bodyHeadJoint.Reset();
            _leftArmJoint.Reset();
            _rightArmJoint.Reset();
            _leftFootJoint.Reset();
            _rightFootJoint.Reset();
#endif
            _hipsVelocity.Reset();
        }

        #endregion

        #region Sampling

        StanceTrackingSource SamplePose(float deltaTime)
        {
            _pose = default;
            _bodyHeld = false;
            bool hasHead = TrySampleHead(ref _pose);

#if KF_META_XR_CORE
            BodyPoseSample bodyPose = _pose;
            if (TrySampleBody(ref bodyPose))
            {
                _bodyLostTime = 0f;
                _bodyValidTime += deltaTime;
                RequestHighFidelityOnce();

                if (_source == StanceTrackingSource.BodyTracking || _bodyValidTime >= _bodyPromotionDelay)
                {
                    _pose = bodyPose;
                    _pose.Source = StanceTrackingSource.BodyTracking;
                    return StanceTrackingSource.BodyTracking;
                }
            }
            else
            {
                _bodyValidTime = 0f;

                // Bridge brief dropouts: keep the body tier and its last evaluation instead of thrashing tiers.
                if (_source == StanceTrackingSource.BodyTracking && _bodyLostTime + deltaTime <= _bodyDropoutGrace)
                {
                    _bodyLostTime += deltaTime;
                    _bodyHeld = true;
                    _pose.Source = StanceTrackingSource.BodyTracking;
                    return StanceTrackingSource.BodyTracking;
                }

                _bodyLostTime = 0f;

                // OVRBody re-applies the project's fidelity whenever it restarts tracking, so ask again on reacquisition.
                _fidelityRequested = false;
                ResetBodyJoints();
            }
#endif

            if (hasHead)
            {
                _pose.Source = StanceTrackingSource.HeadPose;
                return StanceTrackingSource.HeadPose;
            }

            return StanceTrackingSource.None;
        }

        bool TrySampleHead(ref BodyPoseSample pose)
        {
            if (_head == null)
            {
                _headJoint.Reset();
                _headVelocity.Reset();
                return false;
            }

            // Measure at the neck pivot: the eyes swing down and forward when the head pitches, the pivot does not.
            Quaternion headRotation = _head.rotation;
            Vector3 pivot = _head.position - headRotation * _neckToEyeOffset;
            Vector3 rawHead = ToTrackingSpacePoint(pivot);
            Vector3 forward = ToTrackingSpaceDirection(headRotation * Vector3.forward);
            if (!ForgeMath.IsFinite(rawHead) || !ForgeMath.IsFinite(forward))
            {
                _headJoint.Reset();
                _headVelocity.Reset();
                return false;
            }

            pose.Head = _headJoint.Update(rawHead, _frameTime, in _filterSettings, out float elapsed, out bool jumped);
            pose.HasHead = true;
            if (jumped)
            {
                _headVelocity.Reset();
            }

            _headVelocity.Update(pose.Head, elapsed, _speedSmoothingRate);

            Vector3 flat = new Vector3(forward.x, 0f, forward.z);
            if (flat.sqrMagnitude < MinFlatFacingSqrMagnitude)
            {
                // Looking almost straight down the head's up vector points forward; looking straight up it points back.
                Vector3 up = ToTrackingSpaceDirection(headRotation * Vector3.up);
                Vector3 alternative = forward.y < 0f ? up : -up;
                flat = new Vector3(alternative.x, 0f, alternative.z);
            }

            float sqrMagnitude = flat.sqrMagnitude;
            if (sqrMagnitude > MinDirectionSqrMagnitude && ForgeMath.IsFinite(sqrMagnitude))
            {
                pose.Forward = flat / Mathf.Sqrt(sqrMagnitude);
                pose.HasFacing = true;
            }

            return true;
        }

#if KF_META_XR_CORE
        bool TrySampleBody(ref BodyPoseSample pose)
        {
            OVRSkeleton skeleton = _bodySkeleton;
            if (skeleton == null || !skeleton.isActiveAndEnabled || !skeleton.IsInitialized || !skeleton.IsDataValid ||
                !skeleton.IsDataHighConfidence)
            {
                return false;
            }

            if (!_bonesCached || _boneHips == null || _cachedSkeletonChangedCount != skeleton.SkeletonChangedCount)
            {
                if (!CacheBones(skeleton))
                {
                    return false;
                }

                ResetBodyJoints();
            }

            Vector3 rawHips = ToBodySpacePoint(_boneHips.position);
            if (!ForgeMath.IsFinite(rawHips))
            {
                return false;
            }

            pose.Hips = _hipsJoint.Update(rawHips, _frameTime, in _filterSettings, out float hipsElapsed, out bool hipsJumped);
            pose.HasHips = true;
            if (hipsJumped)
            {
                _hipsVelocity.Reset();
            }

            _hipsVelocity.Update(pose.Hips, hipsElapsed, _speedSmoothingRate);

            if (_hasBoneChest && TryFilterBone(_boneChest, ref _chestJoint, out Vector3 chest))
            {
                pose.Chest = chest;
                pose.HasChest = true;
            }

            // Keep the headset head when present: the head-pose tier and its calibration are measured from it.
            if (!pose.HasHead && _hasBoneHead && TryFilterBone(_boneHead, ref _bodyHeadJoint, out Vector3 head))
            {
                pose.Head = head;
                pose.HasHead = true;
            }

            if (_hasBoneFeet &&
                TryFilterBone(_boneLeftFoot, ref _leftFootJoint, out Vector3 leftFoot) &&
                TryFilterBone(_boneRightFoot, ref _rightFootJoint, out Vector3 rightFoot))
            {
                pose.LeftFoot = leftFoot;
                pose.RightFoot = rightFoot;
                pose.HasFeet = true;
            }

            if (_hasBoneArms &&
                TryFilterBone(_boneLeftArm, ref _leftArmJoint, out Vector3 leftArm) &&
                TryFilterBone(_boneRightArm, ref _rightArmJoint, out Vector3 rightArm))
            {
                // The shoulder line is a steadier torso heading than the head, which turns independently.
                Vector3 across = rightArm - leftArm;
                across.y = 0f;
                float sqrMagnitude = across.sqrMagnitude;
                if (sqrMagnitude > MinDirectionSqrMagnitude)
                {
                    across /= Mathf.Sqrt(sqrMagnitude);
                    pose.Forward = new Vector3(-across.z, 0f, across.x);
                    pose.HasFacing = true;
                }
            }

            return true;
        }

        bool TryFilterBone(Transform bone, ref TrackedJoint joint, out Vector3 filtered)
        {
            Vector3 raw = ToBodySpacePoint(bone.position);
            if (!ForgeMath.IsFinite(raw))
            {
                joint.Reset();
                filtered = default;
                return false;
            }

            filtered = joint.Update(raw, _frameTime, in _filterSettings, out _, out _);
            return true;
        }

        Vector3 ToBodySpacePoint(Vector3 worldPoint)
        {
            // OVRSkeleton writes body bones as tracking-space positions under its own transform, so its local space is
            // tracking space wherever the skeleton object sits in the hierarchy.
            return _bodySpace != null ? _bodySpace.InverseTransformPoint(worldPoint) : ToTrackingSpacePoint(worldPoint);
        }
#endif

        void SetSource(StanceTrackingSource next)
        {
            StanceTrackingSource previous = _source;
            _source = next;

            if (_verboseLogging)
            {
                ForgeLog.Info(ForgeLogChannel.Tracking, LogSource, "SOURCE_CHANGED", previous + " -> " + next, this);
            }

            _sourceChanged.Raise(next);
        }

        #endregion

        #region References and calibration

        void UpdateReferences(float deltaTime)
        {
            // Before calibration, creep quickly to recover from a crouched first frame; afterwards, slowly and capped.
            float maxRise = (_calibrated ? _calibratedReferenceRiseSpeed : _referenceRiseSpeed) * deltaTime;

            if (_pose.HasHead)
            {
                TrackReference(
                    ref _reference.StandingHeadHeight, ref _reference.HasHead, _pose.Head.y, maxRise, _headCeiling);

                if (!_reference.HasHeadAnchor && _pose.HasFacing)
                {
                    SetHeadAnchor(_pose.Head, _pose.Forward);
                }
            }

            if (_source != StanceTrackingSource.BodyTracking || _bodyHeld || !_pose.HasHips)
            {
                return;
            }

            if (!_reference.HasHips && _reference.HasHead && _pose.HasHead)
            {
                // Body tracking came online after a head-only calibration. Assume the hips have dropped as far as the
                // head pivot has, rather than treating the current, possibly crouched, pose as standing.
                _reference.StandingHipHeight =
                    _pose.Hips.y + Mathf.Max(0f, _reference.StandingHeadHeight - _pose.Head.y);
                _reference.HasHips = true;
                _hipCeiling = _reference.StandingHipHeight + MaxRiseAboveCalibration;
                return;
            }

            TrackReference(ref _reference.StandingHipHeight, ref _reference.HasHips, _pose.Hips.y, maxRise, _hipCeiling);
        }

        static void TrackReference(ref float reference, ref bool hasReference, float observed, float maxRise, float ceiling)
        {
            if (!hasReference)
            {
                reference = observed;
                hasReference = true;
                return;
            }

            // Creep upward only, slowly and below the ceiling, so a jump or tiptoe cannot make standing read as a crouch.
            if (observed > reference && reference < ceiling)
            {
                reference = Mathf.Min(Mathf.MoveTowards(reference, observed, maxRise), ceiling);
            }
        }

        void SetHeadAnchor(Vector3 head, Vector3 forward)
        {
            forward.y = 0f;
            float sqrMagnitude = forward.sqrMagnitude;
            if (sqrMagnitude < MinAnchorFacingSqrMagnitude)
            {
                return;
            }

            _reference.HeadAnchor = head;
            _reference.AnchorForward = forward / Mathf.Sqrt(sqrMagnitude);
            _reference.HasHeadAnchor = true;
        }

        void AccumulateCalibration(float deltaTime)
        {
            _calibrationElapsed += deltaTime;

            if (_pose.HasHead)
            {
                _headHeightSum += _pose.Head.y;
                _headSampleCount++;

                if (_pose.HasFacing)
                {
                    _headPositionSum += _pose.Head;
                    _facingSum += _pose.Forward;
                    _facingSampleCount++;
                }
            }

            if (_source == StanceTrackingSource.BodyTracking && !_bodyHeld && _pose.HasHips)
            {
                _hipHeightSum += _pose.Hips.y;
                _hipSampleCount++;

                if (StanceSolver.TryMeasureLean(in _pose, out float forwardDegrees, out _))
                {
                    _leanSum += forwardDegrees;
                    _leanSampleCount++;
                }
            }

            if (_calibrationElapsed >= _calibrationDuration)
            {
                CompleteCalibration();
            }
        }

        void CompleteCalibration()
        {
            _calibrating = false;
            _calibrated = true;

            bool headSampled = _headSampleCount > 0;
            bool hipsSampled = _hipSampleCount > 0;

            if (headSampled)
            {
                _reference.StandingHeadHeight = (float)(_headHeightSum / _headSampleCount);
                _reference.HasHead = true;
                _headCeiling = _reference.StandingHeadHeight + MaxRiseAboveCalibration;
            }

            if (hipsSampled)
            {
                _reference.StandingHipHeight = (float)(_hipHeightSum / _hipSampleCount);
                _reference.HasHips = true;
                _hipCeiling = _reference.StandingHipHeight + MaxRiseAboveCalibration;
            }

            if (_facingSampleCount > 0)
            {
                SetHeadAnchor(_headPositionSum / _facingSampleCount, _facingSum / _facingSampleCount);
            }

            if (_leanSampleCount > 0)
            {
                _reference.NeutralForwardLeanDegrees = Mathf.Clamp(
                    (float)(_leanSum / _leanSampleCount),
                    -StanceSolver.MaxNeutralLeanDegrees,
                    StanceSolver.MaxNeutralLeanDegrees);
                _reference.HasNeutralLean = true;
            }

            if (_verboseLogging)
            {
                ForgeLog.Info(ForgeLogChannel.Tracking, LogSource, "CALIBRATED",
                    "source=" + _source +
                    " head=" + FormatOptional(headSampled, _reference.StandingHeadHeight, "0.000") +
                    " hips=" + FormatOptional(hipsSampled, _reference.StandingHipHeight, "0.000") +
                    " neutralLean=" + FormatOptional(_leanSampleCount > 0, _reference.NeutralForwardLeanDegrees, "0.0"),
                    this);
            }

            ForgeEvents.RaiseStanceCalibrated(new StanceCalibratedEvent(
                _source, headSampled, _reference.StandingHeadHeight, hipsSampled, _reference.StandingHipHeight));
        }

        static string FormatOptional(bool hasValue, float value, string format)
        {
            return hasValue ? value.ToString(format, CultureInfo.InvariantCulture) : "n/a";
        }

        #endregion

        #region Output

        void UpdateEmulation(float deltaTime)
        {
            // SimulatorKeyboard reports false outside the Editor, so this is inert on device.
            if (_enableKeyboardEmulation && SimulatorKeyboard.WasPressedThisFrame(_braceToggleKey))
            {
                _braceLocked = !_braceLocked;
            }

            bool held = _enableKeyboardEmulation &&
                        (_braceLocked || SimulatorKeyboard.IsHeld(_braceKey) || SimulatorKeyboard.IsHeld(_alternateBraceKey));
            float rate = held ? _emulationRiseRate : _emulationFallRate;
            _emulatedStability = ForgeMath.DampSnap(_emulatedStability, held ? 1f : 0f, rate, deltaTime, SnapEpsilon);
        }

        void Broadcast(float stability)
        {
            _stability = stability;

            float previous = _lastBroadcastStability;
            bool first = previous < 0f;
            bool reachedEndpoint = (stability <= 0f || stability >= 1f) && stability != previous;
            if (!first && !reachedEndpoint && Mathf.Abs(stability - previous) < _changeEpsilon)
            {
                return;
            }

            _lastBroadcastStability = stability;
            _stabilityChanged.Raise(stability);
            ForgeEvents.RaiseStanceChanged(new StanceChangedEvent(
                stability,
                first ? stability : previous,
                _source,
                IsEmulationDominant,
                ForgeMath.Sanitize01(_evaluation.Grounding),
                ForgeMath.Sanitize01(_evaluation.Balance)));
        }

        Vector3 ToTrackingSpacePoint(Vector3 worldPoint)
        {
            return _hasTrackingSpace ? _trackingSpace.InverseTransformPoint(worldPoint) : worldPoint;
        }

        Vector3 ToTrackingSpaceDirection(Vector3 worldDirection)
        {
            return _hasTrackingSpace ? _trackingSpace.InverseTransformDirection(worldDirection) : worldDirection;
        }

        #endregion

        #region Debug drawing

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || _source == StanceTrackingSource.None)
            {
                return;
            }

            StanceEvaluation evaluation = _evaluation;
            BodyPoseSample pose = _pose;

            if (pose.HasHead)
            {
                Vector3 head = TrackingToWorldPoint(pose.Head);
                Gizmos.color = Color.Lerp(Color.red, Color.green, _stability);
                Gizmos.DrawWireSphere(head, 0.09f);
                Gizmos.DrawLine(head + Vector3.up * 0.12f, head + Vector3.up * (0.12f + 0.4f * _stability));
            }

            if (pose.HasHips)
            {
                Vector3 hips = TrackingToWorldPoint(pose.Hips);
                Gizmos.color = Color.Lerp(Color.gray, Color.yellow, evaluation.Crouch);
                Gizmos.DrawWireSphere(hips, 0.06f);

                if (pose.HasChest)
                {
                    Gizmos.color = Color.Lerp(Color.red, Color.green, evaluation.Lean);
                    Gizmos.DrawLine(hips, TrackingToWorldPoint(pose.Chest));
                }
            }

            if (pose.HasFeet && pose.HasHips && _solverSettings != null)
            {
                Vector3 leftFoot = TrackingToWorldPoint(pose.LeftFoot);
                Vector3 rightFoot = TrackingToWorldPoint(pose.RightFoot);
                Gizmos.color = Color.Lerp(Color.red, Color.green, evaluation.Width);
                Gizmos.DrawWireSphere(leftFoot, 0.04f);
                Gizmos.DrawWireSphere(rightFoot, 0.04f);
                Gizmos.DrawLine(leftFoot, rightFoot);

                Vector3 centerOfMass = pose.HasChest
                    ? Vector3.LerpUnclamped(pose.Hips, pose.Chest, _solverSettings.comChestBias)
                    : pose.Hips;
                Vector3 floorPoint = new Vector3(centerOfMass.x, (pose.LeftFoot.y + pose.RightFoot.y) * 0.5f, centerOfMass.z);
                Gizmos.color = Color.Lerp(Color.red, Color.green, evaluation.CenterOfMass);
                Gizmos.DrawLine(TrackingToWorldPoint(centerOfMass), TrackingToWorldPoint(floorPoint));
                Gizmos.DrawWireSphere(TrackingToWorldPoint(floorPoint), 0.03f);
            }
        }
#endif

        #endregion

        #region Nested types

        /// <summary>
        /// A tracked joint: 1€-filtered position with teleport detection. Mutable struct, used only through fields.
        /// </summary>
        struct TrackedJoint
        {
            OneEuroFilter3 _filter;
            Vector3 _lastRaw;
            double _lastTime;

            public void Reset()
            {
                _filter.Reset();
            }

            /// <summary>Filters one sample taken at <paramref name="time"/>.</summary>
            /// <param name="raw">Raw position.</param>
            /// <param name="time">Sample timestamp in seconds.</param>
            /// <param name="settings">Filter tuning.</param>
            /// <param name="elapsed">
            /// Seconds since this joint's previous sample, which spans any frames it was missing. 0 on the first sample.
            /// </param>
            /// <param name="discontinuity">True when the sample jumped implausibly far and the filter restarted.</param>
            /// <returns>The filtered position.</returns>
            public Vector3 Update(
                Vector3 raw,
                double time,
                in OneEuroSettings settings,
                out float elapsed,
                out bool discontinuity)
            {
                discontinuity = false;
                elapsed = 0f;
                if (_filter.IsInitialized)
                {
                    elapsed = (float)(time - _lastTime);

                    // Limits scale with the real gap, so movement during a bridged dropout is not mistaken for a jump.
                    // The floor keeps very short Editor frames from mistaking millimeter jitter for one.
                    float maxStep = Mathf.Max(MaxPlausibleSpeed * elapsed, MinTeleportDistance);
                    if ((raw - _lastRaw).sqrMagnitude > maxStep * maxStep)
                    {
                        // Faster than any human joint: a recenter or tracking glitch. Restart instead of smoothing it in.
                        _filter.Reset(raw);
                        discontinuity = true;
                    }
                }

                _lastRaw = raw;
                _lastTime = time;
                return _filter.Filter(raw, elapsed, in settings);
            }
        }

        /// <summary>
        /// Velocity of a filtered joint, smoothed as a vector so zero-mean jitter cancels out instead of adding up as speed.
        /// Mutable struct, used only through fields.
        /// </summary>
        struct VelocityEstimator
        {
            Vector3 _previous;
            Vector3 _velocity;
            bool _hasPrevious;

            public float Speed => _velocity.magnitude;

            public void Reset()
            {
                _hasPrevious = false;
                _velocity = Vector3.zero;
            }

            public void Update(Vector3 filteredPosition, float deltaTime, float smoothingRate)
            {
                if (_hasPrevious && deltaTime > 0f)
                {
                    Vector3 instantaneous = (filteredPosition - _previous) / deltaTime;
                    _velocity = ForgeMath.Damp(_velocity, instantaneous, smoothingRate, deltaTime);
                }

                _previous = filteredPosition;
                _hasPrevious = true;
            }
        }

        #endregion
    }
}
