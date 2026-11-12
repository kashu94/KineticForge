using System;
using KineticForge.Core;
using UnityEngine;

namespace KineticForge.Tracking
{
    /// <summary>
    /// Micro half of Dual-Scale Input. Turns the thumb sliding along the side of the index finger into
    /// <see cref="ForgeEvents.MicroGestureSlide"/>, and a thumb tap into <see cref="ForgeEvents.MicroGestureTap"/>.
    /// Add one per hand.
    /// </summary>
    /// <remarks>
    /// <para>Input sources, chosen every frame in priority order:</para>
    /// <list type="number">
    /// <item><description>
    /// Hand tracking: the thumb tip is projected into a frame on the side of the index finger
    /// (<see cref="MicroGestureMath"/>), 1€-filtered, and streamed as slides while it touches the finger. Contact uses
    /// enter and release distances so it cannot flicker. Taps come from Meta's thumb-tap microgesture; on runtimes that
    /// never report microgestures, an index pinch taps instead. Meta's swipe microgestures add a fixed slide step.
    /// </description></item>
    /// <item><description>Touch controller: the thumbstick slides, the index trigger taps.</description></item>
    /// <item><description>Editor mouse: right-button drag slides, left click taps.</description></item>
    /// </list>
    /// <para>
    /// Slides are in finger lengths: X across the side of the finger toward the back of the hand, Y along it toward the
    /// tip. Hand data needs the Meta XR Core SDK (KF_META_XR_CORE) and an <c>OVRSkeleton</c> hand, found by handedness
    /// when not assigned. Runs in <c>LateUpdate</c> after the skeleton updates, in unscaled time, without allocating.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(-20)]
    [AddComponentMenu("Kinetic Forge/Tracking/Micro Gesture Tracker")]
    public sealed class MicroGestureTracker : MonoBehaviour
    {
        const string LogSource = nameof(MicroGestureTracker);
        const float ResolveInterval = 2f;

        #region Serialized fields

        [Header("Hand")]
        [SerializeField, Tooltip("Hand this tracker reads and reports.")]
        ForgeHand _hand = ForgeHand.Right;

#if KF_META_XR_CORE
        [SerializeField, Tooltip("OVRSkeleton of this hand. Found by handedness when empty.")]
        OVRSkeleton _handSkeleton = null;

        [Header("Hand Tracking")]
        [SerializeField, Range(0.005f, 0.05f), Tooltip("Thumb-to-index distance (m) at which the thumb starts touching the finger.")]
        float _contactDistance = 0.022f;

        [SerializeField, Range(0.01f, 0.08f), Tooltip("Thumb-to-index distance (m) at which contact ends. Larger than the contact distance so it cannot flicker.")]
        float _releaseDistance = 0.032f;

        [SerializeField, Tooltip("1€ filter on the thumb position, in finger lengths.")]
        OneEuroSettings _slideFilter = new OneEuroSettings(2f, 4f, 1f);

        [SerializeField, Tooltip("Strike with Meta's thumb-tap microgesture.")]
        bool _useThumbTapMicrogesture = true;

        [SerializeField, Tooltip("Strike with an index pinch on runtimes that never report microgestures.")]
        bool _pinchTapFallback = true;

        [SerializeField, Tooltip("Let an index pinch tap even where microgestures work, so a pinch always strikes. Off: pinch only on runtimes without microgestures.")]
        bool _pinchAlwaysTaps = true;

        [SerializeField, Range(0f, 0.5f), Tooltip("Slide (finger lengths) added by each Meta swipe microgesture. 0 ignores swipes.")]
        float _swipeStep = 0.12f;

        [SerializeField, Tooltip("Flip the across axis, if slides feel mirrored on device.")]
        bool _invertAcross = false;

        [Header("Controller Fallback")]
        [SerializeField, Tooltip("Use the Touch controller of this hand when the hand is not tracked.")]
        bool _enableControllerFallback = true;

        [SerializeField, Min(0f), Tooltip("Slide speed (finger lengths per second) at full thumbstick deflection.")]
        float _thumbstickSlideSpeed = 1.2f;

        [SerializeField, Range(0f, 0.9f), Tooltip("Thumbstick deflection ignored as rest noise.")]
        float _thumbstickDeadzone = 0.15f;
#endif

        [Header("Taps")]
        [SerializeField, Range(0f, 1f), Tooltip("Minimum seconds between taps, so one gesture never strikes twice.")]
        float _tapCooldown = 0.18f;

        [Header("Editor Mouse Fallback")]
        [SerializeField, Tooltip("In the Editor, right-drag slides and left click taps. Enable on one tracker only.")]
        bool _enableMouseFallback = true;

        [SerializeField, Min(0f), Tooltip("Slide (finger lengths) per mouse pixel.")]
        float _mouseSensitivity = 0.004f;

        [Header("Diagnostics")]
        [SerializeField, Tooltip("Log source switches and late skeleton binding.")]
        bool _verboseLogging = false;

        #endregion

        #region Runtime state

        readonly HandSkeletonReader _reader = new HandSkeletonReader();
        OneEuroFilter3 _filter;
        Vector2 _lastCoordinates;
        bool _hasLastCoordinates;
        bool _inContact;
        float _contactStrength;
        Vector2 _lastSlide;
        float _lastTapTime = float.NegativeInfinity;
        MicroGestureSource _source;
        bool _loggedMouse;

#if KF_META_XR_CORE
        OneEuroSettings _filterSettings;
        float _nextResolveTime;
        bool _microgesturesSeen;
        bool _wasPinching;
        OVRHand.MicrogestureType _lastGesture;
        bool _controllerUnavailable;
#endif

        #endregion

        #region Public API

        /// <summary>Hand this tracker reports.</summary>
        public ForgeHand Hand => _hand;

        /// <summary>Source that produced input on the latest frame.</summary>
        public MicroGestureSource Source => _source;

        /// <summary>True while the thumb touches the finger, the thumbstick is deflected or the right mouse button is held.</summary>
        public bool IsInContact => _inContact;

        /// <summary>Contact strength of the latest frame in [0, 1].</summary>
        public float ContactStrength => _contactStrength;

        /// <summary>Slide raised on the latest frame, in finger lengths.</summary>
        public Vector2 LastSlide => _lastSlide;

        /// <summary>True while this hand is tracked with valid data.</summary>
        public bool IsHandTracked => _reader.IsTracked;

        /// <summary>Enables or disables the Editor mouse fallback, for example on the second of two trackers.</summary>
        public bool MouseFallbackEnabled
        {
            get => _enableMouseFallback;
            set => _enableMouseFallback = value;
        }

        #endregion

        #region Unity lifecycle

        void Awake()
        {
#if KF_META_XR_CORE
            _filterSettings = _slideFilter.Sanitized();
#endif
        }

        void Start()
        {
#if KF_META_XR_CORE
            if (_handSkeleton == null)
            {
                _handSkeleton = HandSkeletonReader.FindHandSkeleton(_hand);
            }

            _reader.Bind(_handSkeleton, _hand);
            if (_handSkeleton == null)
            {
                ForgePlatform.ReportMissing(ForgeLogChannel.Tracking, LogSource, "HAND_SKELETON_MISSING",
                    "No " + _hand + " hand OVRSkeleton found. Hand micro-gestures are off; the controller and Editor " +
                    "mouse fallbacks still work. A hand spawned later is picked up automatically.", this);
            }

            _nextResolveTime = Time.unscaledTime + ResolveInterval;
#else
            ForgeLog.Info(ForgeLogChannel.Tracking, LogSource, "META_XR_CORE_MISSING",
                "Meta XR Core SDK not installed: hand micro-gestures are compiled out. Editor mouse fallback " +
                (_enableMouseFallback ? "on." : "off."), this);
#endif
        }

        void OnDisable()
        {
            EndContact();
        }

        void LateUpdate()
        {
            float deltaTime = Time.unscaledDeltaTime;
            if (!(deltaTime > 0f))
            {
                return;
            }

            MicroGestureSource source = MicroGestureSource.None;

            // In the Editor, a mouse button in use always wins, so desk testing never depends on XR device state.
            bool mouseActive = _enableMouseFallback && SimulatorPointer.IsAvailable &&
                               (SimulatorPointer.IsHeld(SimulatorMouseButton.Right) ||
                                SimulatorPointer.WasPressedThisFrame(SimulatorMouseButton.Left));
            if (mouseActive)
            {
                if (!_loggedMouse)
                {
                    _loggedMouse = true;
                    ForgeLog.Info(ForgeLogChannel.Tracking, LogSource, "MOUSE_INPUT_ACTIVE",
                        _hand + " tracker is reading the mouse: right-drag aims, left-click strikes.", this);
                }

                UpdateMouse(deltaTime);
                source = MicroGestureSource.EditorMouse;
            }

#if KF_META_XR_CORE
            if (source == MicroGestureSource.None)
            {
                if (!_reader.IsBound && Time.unscaledTime >= _nextResolveTime)
                {
                    RetryBindSkeleton();
                }

                if (_reader.IsTracked)
                {
                    source = MicroGestureSource.HandTracking;
                    UpdateHandTracking(deltaTime);
                }
                else if (_enableControllerFallback && ForgePlatform.IsMetaRuntimeAvailable && UpdateController(deltaTime))
                {
                    source = MicroGestureSource.Controller;
                }
            }
#endif

            if (source == MicroGestureSource.None)
            {
                EndContact();
            }

            if (source != _source)
            {
                if (_verboseLogging)
                {
                    ForgeLog.Info(ForgeLogChannel.Tracking, LogSource, "SOURCE_CHANGED",
                        _hand + ": " + _source + " -> " + source, this);
                }

                _source = source;
                EndContact();
            }
        }

        void OnValidate()
        {
#if KF_META_XR_CORE
            _releaseDistance = Mathf.Max(_releaseDistance, _contactDistance + 0.002f);
            _filterSettings = _slideFilter.Sanitized();
#endif
        }

        #endregion

        #region Sources

#if KF_META_XR_CORE
        void RetryBindSkeleton()
        {
            _nextResolveTime = Time.unscaledTime + ResolveInterval;
            OVRSkeleton skeleton = HandSkeletonReader.FindHandSkeleton(_hand);
            if (skeleton == null)
            {
                return;
            }

            _handSkeleton = skeleton;
            _reader.Bind(skeleton, _hand);
            if (_verboseLogging)
            {
                ForgeLog.Info(ForgeLogChannel.Tracking, LogSource, "HAND_SKELETON_BOUND",
                    "Bound " + _hand + " hand skeleton '" + skeleton.name + "'.", this);
            }
        }

        void UpdateHandTracking(float deltaTime)
        {
            if (TryReadFrame(out IndexFingerFrame frame, out Vector3 thumbTip))
            {
                Vector2 raw = MicroGestureMath.ProjectThumb(in frame, thumbTip, out float distance);
                if (_invertAcross)
                {
                    raw.x = -raw.x;
                }

                bool touching = _inContact ? distance <= _releaseDistance : distance <= _contactDistance;
                if (touching)
                {
                    float strength = MicroGestureMath.ContactStrength(distance, _contactDistance, _releaseDistance);
                    Vector3 filtered = _filter.Filter(new Vector3(raw.x, raw.y, 0f), deltaTime, in _filterSettings);
                    StreamContact(new Vector2(filtered.x, filtered.y), strength, deltaTime, MicroGestureSource.HandTracking);
                }
                else
                {
                    EndContact();
                }
            }
            else
            {
                EndContact();
            }

            UpdateHandTaps(deltaTime);
        }

        bool TryReadFrame(out IndexFingerFrame frame, out Vector3 thumbTip)
        {
            frame = default;
            if (!_reader.TryGetJoint(HandJoint.ThumbTip, out thumbTip) ||
                !_reader.TryGetJoint(HandJoint.IndexKnuckle, out Vector3 knuckle) ||
                !_reader.TryGetJoint(HandJoint.IndexMiddle, out Vector3 middle) ||
                !_reader.TryGetJoint(HandJoint.IndexEnd, out Vector3 end) ||
                !_reader.TryGetJoint(HandJoint.IndexTip, out Vector3 tip) ||
                !_reader.TryGetJoint(HandJoint.MiddleKnuckle, out Vector3 middleKnuckle))
            {
                return false;
            }

            return MicroGestureMath.TryBuildIndexFrame(
                knuckle, middle, end, tip, middleKnuckle, _hand == ForgeHand.Left, out frame);
        }

        void UpdateHandTaps(float deltaTime)
        {
            OVRHand hand = _reader.OvrHand;
            if (hand == null)
            {
                return;
            }

            if (_useThumbTapMicrogesture)
            {
                OVRHand.MicrogestureType gesture = hand.GetMicrogestureType();
                if (gesture != OVRHand.MicrogestureType.Invalid)
                {
                    _microgesturesSeen = true;
                }

                if (gesture != _lastGesture)
                {
                    if (gesture == OVRHand.MicrogestureType.ThumbTap)
                    {
                        TryTap(1f, MicroGestureSource.HandTracking);
                    }
                    else if (_swipeStep > 0f && TryGetSwipe(gesture, out Vector2 direction))
                    {
                        RaiseSlide(direction * _swipeStep, 1f, deltaTime, MicroGestureSource.HandTracking);
                    }
                }

                _lastGesture = gesture;
            }

            if (_pinchTapFallback && (_pinchAlwaysTaps || !_microgesturesSeen))
            {
                bool pinching = hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
                if (pinching && !_wasPinching)
                {
                    TryTap(Mathf.Clamp01(hand.GetFingerPinchStrength(OVRHand.HandFinger.Index)), MicroGestureSource.HandTracking);
                }

                _wasPinching = pinching;
            }
        }

        bool TryGetSwipe(OVRHand.MicrogestureType gesture, out Vector2 direction)
        {
            switch (gesture)
            {
                case OVRHand.MicrogestureType.SwipeForward: direction = Vector2.up; return true;
                case OVRHand.MicrogestureType.SwipeBackward: direction = Vector2.down; return true;
                case OVRHand.MicrogestureType.SwipeLeft: direction = _invertAcross ? Vector2.right : Vector2.left; return true;
                case OVRHand.MicrogestureType.SwipeRight: direction = _invertAcross ? Vector2.left : Vector2.right; return true;
                default: direction = Vector2.zero; return false;
            }
        }

        bool UpdateController(float deltaTime)
        {
            if (_controllerUnavailable)
            {
                return false;
            }

            OVRInput.Controller controller = _hand == ForgeHand.Left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            try
            {
                if (!OVRInput.IsControllerConnected(controller))
                {
                    return false;
                }

                Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, controller);
                float magnitude = stick.magnitude;
                if (magnitude > _thumbstickDeadzone && ForgeMath.IsFinite(magnitude))
                {
                    float strength = Mathf.InverseLerp(_thumbstickDeadzone, 1f, magnitude);
                    RaiseSlide(stick * (_thumbstickSlideSpeed * deltaTime), strength, deltaTime, MicroGestureSource.Controller);
                    _inContact = true;
                    _contactStrength = strength;
                }
                else
                {
                    EndContact();
                }

                if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, controller))
                {
                    TryTap(1f, MicroGestureSource.Controller);
                }

                return true;
            }
            catch (Exception exception)
            {
                // A missing native plugin on this platform must not throw every frame.
                _controllerUnavailable = true;
                ForgeLog.Warn(ForgeLogChannel.Tracking, LogSource, "CONTROLLER_INPUT_UNAVAILABLE",
                    "Touch controller input is unavailable (" + exception.GetType().Name + "); fallback disabled.", this);
                return false;
            }
        }
#endif

        void UpdateMouse(float deltaTime)
        {
            if (SimulatorPointer.IsHeld(SimulatorMouseButton.Right))
            {
                Vector2 coordinates = _hasLastCoordinates
                    ? _lastCoordinates + SimulatorPointer.Delta * _mouseSensitivity
                    : Vector2.zero;
                StreamContact(coordinates, 1f, deltaTime, MicroGestureSource.EditorMouse);
            }
            else
            {
                EndContact();
            }

            if (SimulatorPointer.WasPressedThisFrame(SimulatorMouseButton.Left))
            {
                TryTap(1f, MicroGestureSource.EditorMouse);
            }
        }

        #endregion

        #region Output

        void StreamContact(Vector2 coordinates, float strength, float deltaTime, MicroGestureSource source)
        {
            if (_inContact && _hasLastCoordinates)
            {
                RaiseSlide(coordinates - _lastCoordinates, strength, deltaTime, source);
            }

            _lastCoordinates = coordinates;
            _hasLastCoordinates = true;
            _inContact = true;
            _contactStrength = strength;
        }

        void EndContact()
        {
            _inContact = false;
            _hasLastCoordinates = false;
            _contactStrength = 0f;
            _lastSlide = Vector2.zero;
            _filter.Reset();
        }

        void RaiseSlide(Vector2 delta, float strength, float deltaTime, MicroGestureSource source)
        {
            if (!ForgeMath.IsFinite(delta.x) || !ForgeMath.IsFinite(delta.y))
            {
                return;
            }

            _lastSlide = delta;
            ForgeEvents.RaiseMicroGestureSlide(new MicroGestureSlideEvent(_hand, delta, strength, deltaTime, source));
        }

        void TryTap(float strength, MicroGestureSource source)
        {
            float now = Time.unscaledTime;
            if (now - _lastTapTime < _tapCooldown)
            {
                return;
            }

            _lastTapTime = now;
            ForgeEvents.RaiseMicroGestureTap(new MicroGestureTapEvent(_hand, ForgeMath.Sanitize01(strength, 1f), source));
        }

        #endregion
    }
}
