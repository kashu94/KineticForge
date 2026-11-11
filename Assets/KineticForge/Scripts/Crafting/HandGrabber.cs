using System;
using KineticForge.Core;
using KineticForge.Physics;
using KineticForge.Tracking;
using UnityEngine;

namespace KineticForge.Crafting
{
    /// <summary>
    /// Grabs relics with one hand: closing a tracked hand, or squeezing a Touch controller's grip, near a relic with a
    /// <see cref="RelicGrabDriver"/> starts a physical grip; opening it lets go. Add one per hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A tracked hand counts as closed when its middle and ring fingertips curl in toward the wrist, with separate close
    /// and open distances so the grip never flickers. A controller uses its grip trigger with the same hysteresis. Thumb
    /// and index stay free, so the holding hand can still perform micro-gestures.
    /// </para>
    /// <para>
    /// On close, the nearest relic collider within reach of the palm is gripped at its closest surface point. The grip
    /// follows this hand's <c>OVRCameraRig</c> anchor, which tracks both hands and controllers.
    /// </para>
    /// <para>
    /// Requires the Meta XR Core SDK (KF_META_XR_CORE); without it the component stays idle. Rig, anchor and hand skeleton
    /// are found automatically when left empty. The per-frame path does not allocate.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(-10)]
    [AddComponentMenu("Kinetic Forge/Crafting/Hand Grabber")]
    public sealed class HandGrabber : MonoBehaviour
    {
        const string LogSource = nameof(HandGrabber);
        const int OverlapCapacity = 16;
        const float ResolveInterval = 2f;

        [SerializeField, Tooltip("Hand this grabber reads.")]
        ForgeHand _hand = ForgeHand.Left;

        [SerializeField, Range(0.02f, 0.2f), Tooltip("Reach (m) around the palm within which a relic can be grabbed.")]
        float _reach = 0.08f;

        [SerializeField, Tooltip("Layers searched for relic colliders.")]
        LayerMask _relicLayers = ~0;

#if KF_META_XR_CORE
        [SerializeField, Tooltip("Transform the grip follows. This hand's OVRCameraRig anchor when empty.")]
        Transform _handAnchor = null;

        [SerializeField, Tooltip("OVRSkeleton of this hand. Found by handedness when empty.")]
        OVRSkeleton _handSkeleton = null;

        [SerializeField, Range(0.05f, 0.2f), Tooltip("Average middle and ring fingertip distance to the wrist (m) below which the hand counts as closed.")]
        float _closeDistance = 0.105f;

        [SerializeField, Range(0.06f, 0.25f), Tooltip("Distance (m) above which a closed hand counts as open again.")]
        float _openDistance = 0.13f;

        [SerializeField, Range(0.1f, 1f), Tooltip("Controller grip trigger value that grabs.")]
        float _gripPress = 0.55f;

        [SerializeField, Range(0f, 0.9f), Tooltip("Controller grip trigger value that releases.")]
        float _gripRelease = 0.35f;
#endif

        readonly Collider[] _overlaps = new Collider[OverlapCapacity];
        RelicGrabDriver _held;
        bool _closed;

#if KF_META_XR_CORE
        readonly HandSkeletonReader _reader = new HandSkeletonReader();
        float _nextResolveTime;
        bool _controllerUnavailable;
#endif

        /// <summary>Hand this grabber reads.</summary>
        public ForgeHand Hand => _hand;

        /// <summary>Relic currently gripped by this hand, or null.</summary>
        public RelicGrabDriver Held => _held;

        /// <summary>True while the hand or controller counts as closed.</summary>
        public bool IsClosed => _closed;

        /// <summary>Reach (m) around the palm within which a relic can be grabbed.</summary>
        public float Reach => _reach;

        /// <summary>
        /// Grabs the nearest relic within reach of <paramref name="palm"/>, gripped by <paramref name="target"/>.
        /// Used by the input paths; public for tools and tests.
        /// </summary>
        /// <param name="palm">World-space palm position.</param>
        /// <param name="target">Transform the grip follows.</param>
        /// <returns>True when a relic was gripped.</returns>
        public bool TryGrabNearest(Vector3 palm, Transform target)
        {
            if (_held != null || target == null)
            {
                return false;
            }

            int count = UnityEngine.Physics.OverlapSphereNonAlloc(
                palm, _reach, _overlaps, _relicLayers, QueryTriggerInteraction.Ignore);

            RelicGrabDriver best = null;
            Vector3 bestPoint = default;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider candidate = _overlaps[i];
                _overlaps[i] = null;
                Rigidbody body = candidate != null ? candidate.attachedRigidbody : null;
                if (body == null || !body.TryGetComponent(out RelicGrabDriver driver) || !driver.isActiveAndEnabled)
                {
                    continue;
                }

                Vector3 point = candidate.ClosestPoint(palm);
                float distance = (point - palm).sqrMagnitude;
                if (distance < bestDistance)
                {
                    best = driver;
                    bestPoint = point;
                    bestDistance = distance;
                }
            }

            if (best == null || !best.BeginGrab(_hand, target, bestPoint))
            {
                return false;
            }

            _held = best;
            return true;
        }

        /// <summary>Releases the relic held by this hand, if any.</summary>
        public void Release()
        {
            if (_held != null)
            {
                _held.EndGrab(_hand);
                _held = null;
            }
        }

        void Start()
        {
#if KF_META_XR_CORE
            ResolveSources(logMissing: true);
#else
            ForgeLog.Info(ForgeLogChannel.Crafting, LogSource, "META_XR_CORE_MISSING",
                "Meta XR Core SDK not installed: " + _hand + " hand grabbing is off (reach " + _reach + " m, layers " +
                _relicLayers.value + "). Use a Relic Hold Simulator in the Editor.", this);
#endif
        }

        void OnDisable()
        {
            Release();
            _closed = false;
        }

        void LateUpdate()
        {
            // The driver may have let go on its own, for example when tracking jumped past its break distance.
            if (_held != null && !_held.IsHeldBy(_hand))
            {
                _held = null;
            }

#if KF_META_XR_CORE
            if (Time.unscaledTime >= _nextResolveTime && (_handAnchor == null || !_reader.IsBound))
            {
                ResolveSources(logMissing: false);
            }

            if (_handAnchor == null)
            {
                return;
            }

            bool closed;
            Vector3 palm;
            if (_reader.IsTracked && TryReadHand(out closed, out palm))
            {
                UpdateGrip(closed, palm);
            }
            else if (TryReadController(out closed))
            {
                UpdateGrip(closed, _handAnchor.position);
            }
            else if (_closed)
            {
                // Hand and controller both lost: let go rather than freezing the relic in the air.
                _closed = false;
                Release();
            }
#endif
        }

#if KF_META_XR_CORE
        void UpdateGrip(bool closed, Vector3 palm)
        {
            if (closed && !_closed)
            {
                TryGrabNearest(palm, _handAnchor);
            }
            else if (!closed && _closed)
            {
                Release();
            }

            _closed = closed;
        }

        bool TryReadHand(out bool closed, out Vector3 palm)
        {
            closed = _closed;
            palm = default;
            if (!_reader.TryGetJoint(HandJoint.Wrist, out Vector3 wrist) ||
                !_reader.TryGetJoint(HandJoint.MiddleKnuckle, out Vector3 knuckle) ||
                !_reader.TryGetJoint(HandJoint.MiddleTip, out Vector3 middleTip) ||
                !_reader.TryGetJoint(HandJoint.RingTip, out Vector3 ringTip))
            {
                return false;
            }

            // The palm center sits roughly between the wrist and the middle knuckle.
            palm = (wrist + knuckle) * 0.5f;
            float curl = 0.5f * (Vector3.Distance(middleTip, wrist) + Vector3.Distance(ringTip, wrist));
            closed = _closed ? curl < _openDistance : curl < _closeDistance;
            return true;
        }

        bool TryReadController(out bool closed)
        {
            closed = false;
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

                float grip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, controller);
                closed = _closed ? grip > _gripRelease : grip >= _gripPress;
                return true;
            }
            catch (Exception exception)
            {
                _controllerUnavailable = true;
                ForgeLog.Warn(ForgeLogChannel.Crafting, LogSource, "CONTROLLER_INPUT_UNAVAILABLE",
                    "Touch controller input is unavailable (" + exception.GetType().Name + "); controller grabbing is off.", this);
                return false;
            }
        }

        void ResolveSources(bool logMissing)
        {
            _nextResolveTime = Time.unscaledTime + ResolveInterval;

            if (_handAnchor == null)
            {
                OVRCameraRig rig = FindAnyObjectByType<OVRCameraRig>();
                if (rig != null)
                {
                    _handAnchor = _hand == ForgeHand.Left ? rig.leftHandAnchor : rig.rightHandAnchor;
                }
            }

            if (!_reader.IsBound)
            {
                if (_handSkeleton == null)
                {
                    _handSkeleton = HandSkeletonReader.FindHandSkeleton(_hand);
                }

                if (_handSkeleton != null)
                {
                    _reader.Bind(_handSkeleton, _hand);
                }
            }

            if (logMissing && _handAnchor == null)
            {
                ForgePlatform.ReportMissing(ForgeLogChannel.Crafting, LogSource, "HAND_ANCHOR_MISSING",
                    "No OVRCameraRig hand anchor for the " + _hand + " hand. Grabbing starts once a rig appears.", this);
            }
        }

        void OnValidate()
        {
            _openDistance = Mathf.Max(_openDistance, _closeDistance + 0.005f);
            _gripRelease = Mathf.Min(_gripRelease, _gripPress - 0.05f);
        }
#endif

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = _held != null ? new Color(1f, 0.6f, 0.1f) : _closed ? Color.yellow : new Color(0.4f, 0.7f, 1f);
#if KF_META_XR_CORE
            if (_handAnchor != null)
            {
                Gizmos.DrawWireSphere(_handAnchor.position, _reach);
            }
#endif
        }
#endif
    }
}
