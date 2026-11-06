using System;
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
    /// only after its data has been valid and high-confidence for a short promotion delay, so flicker cannot thrash it.
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
    /// Body tracking code compiles only when <c>com.meta.xr.sdk.core</c> 60+ is installed, which defines KF_META_XR_CORE
    /// through the assembly definition. Missing rig or body components are reported once as structured warnings and never
    /// throw.
    /// </para>
    /// <para>
    /// Crouch is measured as a drop below standing reference heights. References are captured by
    /// <see cref="RequestCalibration"/> (raised by the game flow through <see cref="ForgeEvents.CalibrationRequested"/>),
    /// initialize lazily from the first pose, and creep upward when the player stands taller than them. Use a floor-level
    /// tracking origin, or recalibrate after a recenter that changes height.
    /// </para>
    /// <para>
    /// All work runs in <c>LateUpdate</c>, after <c>OVRSkeleton</c> and <c>OVRCameraRig</c> have written this frame's
    /// poses, in unscaled time so slow-motion effects never slow the player's body. The per-frame path does not allocate.
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
        const float MinDirectionSqrMagnitude = 0.0001f;
        const float HeadResolveInterval = 1f;

        #region Serialized fields

#if KF_META_XR_CORE
        [Header("Meta XR Sources")]
        [SerializeField, Tooltip("Camera rig providing tracking space and the center-eye pose. Found in the scene when empty.")]
        OVRCameraRig _cameraRig = null;

        [SerializeField, Tooltip("OVRSkeleton with Skeleton Type Body or FullBody, fed by OVRBody. Found in the scene when empty.")]
        OVRSkeleton _bodySkeleton = null;

        [SerializeField, Min(0f), Tooltip("Seconds body data must stay valid and high-confidence before it replaces the head-pose tier.")]
        float _bodyPromotionDelay = 0.3f;
#endif

        [Header("Fallback Sources")]
        [SerializeField, Tooltip("Head transform used when no OVRCameraRig is available. Camera.main is used when empty.")]
        Transform _headOverride = null;

        [Header("Solver")]
        [SerializeField]
        StanceSolverSettings _solverSettings = new StanceSolverSettings();

        [SerializeField, Tooltip("Seated play: the chair counts as a grounded base; stability comes from forward lean and steadiness.")]
        bool _seatedMode = false;

        [Header("Smoothing")]
        [SerializeField, Min(0.01f), Tooltip("Exponential rate (1/s) at which stability rises while settling into a stance.")]
        float _riseRate = 3.5f;

        [SerializeField, Min(0.01f), Tooltip("Exponential rate (1/s) at which stability falls when the stance is lost.")]
        float _fallRate = 7f;

        [SerializeField, Min(0.01f), Tooltip("Exponential rate (1/s) used to smooth the steadiness reference speed.")]
        float _speedSmoothingRate = 10f;

        [SerializeField, Range(0.0005f, 0.05f), Tooltip("Smallest stability change that is broadcast. Reaching 0 or 1 is always broadcast.")]
        float _changeEpsilon = 0.002f;

        [Header("Calibration")]
        [SerializeField, Min(0.1f), Tooltip("Seconds the standing pose is averaged when calibration is requested.")]
        float _calibrationDuration = 1f;

        [SerializeField, Min(0f), Tooltip("Max speed (m/s) at which a standing reference creeps up when the player stands taller than it.")]
        float _referenceRiseSpeed = 0.05f;

        [SerializeField, Tooltip("Calibrate when ForgeEvents.CalibrationRequested is raised.")]
        bool _respondToCalibrationRequests = true;

        [Header("Simulator Fallback (Editor only)")]
        [SerializeField, Tooltip("Hold a brace key to smoothly drive stability to 1. Inert in player builds.")]
        bool _enableKeyboardEmulation = true;

        [SerializeField, Tooltip("Primary brace key.")]
        KeyCode _braceKey = KeyCode.Space;

        [SerializeField, Tooltip("Secondary brace key.")]
        KeyCode _alternateBraceKey = KeyCode.DownArrow;

        [SerializeField, Min(0.01f), Tooltip("Exponential rate (1/s) toward 1 while a brace key is held.")]
        float _emulationRiseRate = 4f;

        [SerializeField, Min(0.01f), Tooltip("Exponential rate (1/s) back toward 0 after the brace key is released.")]
        float _emulationFallRate = 6f;

        [Header("Diagnostics")]
        [SerializeField, Tooltip("Log source switches, bone caching and calibration results.")]
        bool _verboseLogging = false;

        #endregion

        #region Runtime state

        Transform _trackingSpace;
        bool _hasTrackingSpace;
        Transform _head;
        float _nextHeadResolveTime;

#if KF_META_XR_CORE
        float _bodyValidTime;
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
#endif

        BodyPoseSample _pose;
        StanceReference _reference;
        StanceEvaluation _evaluation;
        StanceTrackingSource _source;
        Vector3 _previousReferencePoint;
        bool _hasPreviousReferencePoint;
        float _smoothedSpeed;

        float _sensorStability;
        float _emulatedStability;
        float _stability;
        float _lastBroadcastStability = -1f;

        bool _calibrating;
        bool _calibrated;
        float _calibrationElapsed;
        double _headHeightSum;
        double _hipHeightSum;
        int _headSampleCount;
        int _hipSampleCount;

        Action _onCalibrationRequested;
        bool _subscribedToCalibration;

        #endregion

        #region Public API

        /// <summary>
        /// Raised with the new <see cref="StanceStability"/> when it moves by more than the change threshold, when it
        /// reaches exactly 0 or 1, and once on the first evaluated frame.
        /// </summary>
        public event Action<float> OnStanceStabilityChanged;

        /// <summary>Raised when the active sensing tier changes.</summary>
        public event Action<StanceTrackingSource> OnTrackingSourceChanged;

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

        /// <summary>Factor breakdown of the latest evaluation, for debug overlays and tuning.</summary>
        public StanceEvaluation LastEvaluation => _evaluation;

        /// <summary>Standing references the solver currently measures crouch against.</summary>
        public StanceReference Reference => _reference;

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
            _headSampleCount = 0;
            _hipSampleCount = 0;

            if (_verboseLogging)
            {
                ForgeLog.Info(ForgeLogChannel.Tracking, LogSource, "CALIBRATION_STARTED",
                    "Sampling the standing pose for " + _calibrationDuration.ToString("0.##") + " s.", this);
            }
        }

#if KF_META_XR_CORE
        /// <summary>
        /// Binds a body skeleton at runtime, for example when the rig is spawned after this tracker started.
        /// Pass null to disable the body-tracking tier.
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
        }

        /// <summary>
        /// Binds a camera rig at runtime, for example when the rig is spawned after this tracker started.
        /// </summary>
        /// <param name="rig">The rig providing tracking space and the center-eye pose, or null.</param>
        public void BindCameraRig(OVRCameraRig rig)
        {
            _cameraRig = rig;
            _trackingSpace = rig != null ? rig.trackingSpace : null;
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
            _onCalibrationRequested = RequestCalibration;
        }

        void OnEnable()
        {
            if (_respondToCalibrationRequests)
            {
                ForgeEvents.CalibrationRequested += _onCalibrationRequested;
                _subscribedToCalibration = true;
            }

            _hasPreviousReferencePoint = false;
        }

        void OnDisable()
        {
            if (_subscribedToCalibration)
            {
                ForgeEvents.CalibrationRequested -= _onCalibrationRequested;
                _subscribedToCalibration = false;
            }

            _calibrating = false;
        }

        void Start()
        {
            ResolveSources();
        }

        void LateUpdate()
        {
            float rawDeltaTime = Time.unscaledDeltaTime;
            float deltaTime = Mathf.Min(rawDeltaTime, MaxDeltaTime);
            if (deltaTime <= 0f)
            {
                return;
            }

            _hasTrackingSpace = _trackingSpace != null;
            if (_head == null && Time.unscaledTime >= _nextHeadResolveTime)
            {
                ResolveHead();
            }

            StanceTrackingSource tier = SamplePose(deltaTime);
            if (tier != _source)
            {
                SetSource(tier);
            }

            UpdateReferenceSpeed(rawDeltaTime, deltaTime);

            if (_calibrating)
            {
                AccumulateCalibration(deltaTime);
            }
            else
            {
                UpdateReferences(deltaTime);
            }

            _evaluation = StanceSolver.Evaluate(in _pose, in _reference, _solverSettings, _seatedMode);

            float sensorTarget = _evaluation.Stability;
            float sensorRate = sensorTarget > _sensorStability ? _riseRate : _fallRate;
            _sensorStability = Smooth(_sensorStability, sensorTarget, sensorRate, deltaTime);

            UpdateEmulation(deltaTime);
            Broadcast(Mathf.Max(_sensorStability, _emulatedStability));
        }

        void OnValidate()
        {
            if (_solverSettings != null)
            {
                _solverSettings.Sanitize();
            }
        }

        #endregion

        #region Source resolution

        void ResolveSources()
        {
            _trackingSpace = null;

#if KF_META_XR_CORE
            if (_cameraRig == null)
            {
                _cameraRig = FindAnyObjectByType<OVRCameraRig>();
            }

            if (_cameraRig != null)
            {
                _trackingSpace = _cameraRig.trackingSpace;
            }
            else
            {
                ForgeLog.Warn(ForgeLogChannel.Tracking, LogSource, "CAMERA_RIG_MISSING",
                    "No OVRCameraRig found. Head pose falls back to the head override or Camera.main, in world space.", this);
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
                ForgeLog.Warn(ForgeLogChannel.Tracking, LogSource, "BODY_SKELETON_MISSING",
                    "No OVRSkeleton with Skeleton Type Body or FullBody found. Body tracking is off; " +
                    "using the head-pose tier and simulator fallback.", this);
            }
            else if (FindAnyObjectByType<OVRBody>() == null)
            {
                ForgeLog.Warn(ForgeLogChannel.Tracking, LogSource, "OVR_BODY_MISSING",
                    "OVRSkeleton '" + _bodySkeleton.name + "' has no OVRBody providing data. Add OVRBody next to it; " +
                    "until then the head-pose tier is used.", this);
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
                    "Stance relies on the editor keyboard fallback only.", this);
            }
        }

        void ResolveHead()
        {
            _nextHeadResolveTime = Time.unscaledTime + HeadResolveInterval;

            if (_headOverride != null)
            {
                _head = _headOverride;
                return;
            }

#if KF_META_XR_CORE
            if (_cameraRig != null && _cameraRig.centerEyeAnchor != null)
            {
                _head = _cameraRig.centerEyeAnchor;
                return;
            }
#endif

            Camera mainCamera = Camera.main;
            _head = mainCamera != null ? mainCamera.transform : null;
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

        static bool IsBodySkeleton(OVRSkeleton skeleton)
        {
            OVRSkeleton.SkeletonType type = skeleton.GetSkeletonType();
            return type == OVRSkeleton.SkeletonType.Body || type == OVRSkeleton.SkeletonType.FullBody;
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

        #endregion

        #region Sampling

        StanceTrackingSource SamplePose(float deltaTime)
        {
            _pose = default;
            bool hasHead = TrySampleHead(ref _pose);

#if KF_META_XR_CORE
            BodyPoseSample bodyPose = _pose;
            if (TrySampleBody(ref bodyPose))
            {
                _bodyValidTime += deltaTime;
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
                return false;
            }

            pose.Head = ToTrackingSpacePoint(_head.position);
            pose.HasHead = true;

            Vector3 forward = ToTrackingSpaceDirection(_head.forward);
            forward.y = 0f;
            float sqrMagnitude = forward.sqrMagnitude;
            if (sqrMagnitude > MinDirectionSqrMagnitude)
            {
                pose.Forward = forward / Mathf.Sqrt(sqrMagnitude);
                pose.HasFacing = true;
            }

            return true;
        }

#if KF_META_XR_CORE
        bool TrySampleBody(ref BodyPoseSample pose)
        {
            OVRSkeleton skeleton = _bodySkeleton;
            if (skeleton == null || !skeleton.IsInitialized || !skeleton.IsDataValid || !skeleton.IsDataHighConfidence)
            {
                return false;
            }

            if (!_bonesCached || _boneHips == null || _cachedSkeletonChangedCount != skeleton.SkeletonChangedCount)
            {
                if (!CacheBones(skeleton))
                {
                    return false;
                }
            }

            pose.Hips = ToTrackingSpacePoint(_boneHips.position);
            pose.HasHips = true;

            if (_hasBoneChest)
            {
                pose.Chest = ToTrackingSpacePoint(_boneChest.position);
                pose.HasChest = true;
            }

            // Keep the headset head when present: the head-pose tier and its calibration are measured from it.
            if (!pose.HasHead && _hasBoneHead)
            {
                pose.Head = ToTrackingSpacePoint(_boneHead.position);
                pose.HasHead = true;
            }

            if (_hasBoneFeet)
            {
                pose.LeftFoot = ToTrackingSpacePoint(_boneLeftFoot.position);
                pose.RightFoot = ToTrackingSpacePoint(_boneRightFoot.position);
                pose.HasFeet = true;
            }

            if (_hasBoneArms)
            {
                // The shoulder line is a steadier torso heading than the head, which turns independently.
                Vector3 across = ToTrackingSpacePoint(_boneRightArm.position) - ToTrackingSpacePoint(_boneLeftArm.position);
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
#endif

        void SetSource(StanceTrackingSource next)
        {
            StanceTrackingSource previous = _source;
            _source = next;
            _hasPreviousReferencePoint = false;
            _smoothedSpeed = 0f;

            if (_verboseLogging)
            {
                ForgeLog.Info(ForgeLogChannel.Tracking, LogSource, "SOURCE_CHANGED", previous + " -> " + next, this);
            }

            OnTrackingSourceChanged?.Invoke(next);
        }

        void UpdateReferenceSpeed(float rawDeltaTime, float smoothingDeltaTime)
        {
            bool bodyTier = _source == StanceTrackingSource.BodyTracking;
            bool hasPoint = bodyTier ? _pose.HasHips : _pose.HasHead;
            if (!hasPoint)
            {
                _hasPreviousReferencePoint = false;
                _smoothedSpeed = 0f;
                _pose.ReferenceSpeed = 0f;
                return;
            }

            Vector3 point = bodyTier ? _pose.Hips : _pose.Head;
            if (_hasPreviousReferencePoint)
            {
                // Divide by the real frame time so a hitch does not read as a lunge.
                float speed = Mathf.Min((point - _previousReferencePoint).magnitude / rawDeltaTime, MaxPlausibleSpeed);
                _smoothedSpeed = Smooth(_smoothedSpeed, speed, _speedSmoothingRate, smoothingDeltaTime);
            }

            _previousReferencePoint = point;
            _hasPreviousReferencePoint = true;
            _pose.ReferenceSpeed = _smoothedSpeed;
        }

        #endregion

        #region References and calibration

        void UpdateReferences(float deltaTime)
        {
            float maxRise = _referenceRiseSpeed * deltaTime;

            if (_pose.HasHead)
            {
                TrackReference(ref _reference.StandingHeadHeight, ref _reference.HasHead, _pose.Head.y, maxRise);
            }

            if (_source != StanceTrackingSource.BodyTracking || !_pose.HasHips)
            {
                return;
            }

            if (!_reference.HasHips && _reference.HasHead && _pose.HasHead)
            {
                // Body tracking came online after a head-only calibration. Assume the hips have dropped as far as the
                // head has, rather than treating the current, possibly crouched, pose as standing.
                _reference.StandingHipHeight =
                    _pose.Hips.y + Mathf.Max(0f, _reference.StandingHeadHeight - _pose.Head.y);
                _reference.HasHips = true;
                return;
            }

            TrackReference(ref _reference.StandingHipHeight, ref _reference.HasHips, _pose.Hips.y, maxRise);
        }

        static void TrackReference(ref float reference, ref bool hasReference, float observed, float maxRise)
        {
            if (!hasReference)
            {
                reference = observed;
                hasReference = true;
                return;
            }

            // Creep upward only, and slowly, so a jump or tiptoe cannot make normal standing read as a crouch.
            if (observed > reference)
            {
                reference = Mathf.MoveTowards(reference, observed, maxRise);
            }
        }

        void AccumulateCalibration(float deltaTime)
        {
            _calibrationElapsed += deltaTime;

            if (_pose.HasHead)
            {
                _headHeightSum += _pose.Head.y;
                _headSampleCount++;
            }

            if (_source == StanceTrackingSource.BodyTracking && _pose.HasHips)
            {
                _hipHeightSum += _pose.Hips.y;
                _hipSampleCount++;
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
            }

            if (hipsSampled)
            {
                _reference.StandingHipHeight = (float)(_hipHeightSum / _hipSampleCount);
                _reference.HasHips = true;
            }

            if (_verboseLogging)
            {
                ForgeLog.Info(ForgeLogChannel.Tracking, LogSource, "CALIBRATED",
                    "source=" + _source +
                    " head=" + (headSampled ? _reference.StandingHeadHeight.ToString("0.000") : "n/a") +
                    " hips=" + (hipsSampled ? _reference.StandingHipHeight.ToString("0.000") : "n/a"), this);
            }

            ForgeEvents.RaiseStanceCalibrated(new StanceCalibratedEvent(
                _source, headSampled, _reference.StandingHeadHeight, hipsSampled, _reference.StandingHipHeight));
        }

        #endregion

        #region Output

        void UpdateEmulation(float deltaTime)
        {
            // SimulatorKeyboard reports false outside the Editor, so this is inert on device.
            bool held = _enableKeyboardEmulation &&
                        (SimulatorKeyboard.IsHeld(_braceKey) || SimulatorKeyboard.IsHeld(_alternateBraceKey));
            float rate = held ? _emulationRiseRate : _emulationFallRate;
            _emulatedStability = Smooth(_emulatedStability, held ? 1f : 0f, rate, deltaTime);
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
            OnStanceStabilityChanged?.Invoke(stability);
            ForgeEvents.RaiseStanceChanged(new StanceChangedEvent(
                stability, first ? stability : previous, _source, IsEmulationDominant));
        }

        static float Smooth(float current, float target, float rate, float deltaTime)
        {
            // Frame-rate independent exponential approach that snaps once close enough to land exactly on the target.
            float next = Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * deltaTime));
            return Mathf.Abs(next - target) < SnapEpsilon ? target : next;
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
    }
}
