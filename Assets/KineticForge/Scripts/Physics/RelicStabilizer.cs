using System;
using System.Globalization;
using KineticForge.Core;
using KineticForge.Tracking;
using UnityEngine;

namespace KineticForge.Physics
{
    /// <summary>
    /// Pseudo-haptic weight for heavy relics. Maps stance stability onto the relic's <see cref="Rigidbody"/>, so a grounded
    /// stance makes it feel planted and controllable, while an upright or unbalanced stance makes it twitchy and hard to steady.
    /// </summary>
    /// <remarks>
    /// <para>Each physics step, using the response curve evaluated at the latest stance stability:</para>
    /// <list type="bullet">
    /// <item><description>Linear and angular damping are interpolated between their unstable and stable values.</description></item>
    /// <item><description>
    /// The inertia tensor is scaled from its collider-derived baseline, so a braced relic resists sudden rotation and an
    /// unbraced one spins up easily.
    /// </description></item>
    /// <item><description>
    /// Optionally, a smooth Perlin wobble torque scaled by instability is applied while a hand holds the relic.
    /// </description></item>
    /// </list>
    /// <para>
    /// Disturbances are physical torques, not raw accelerations. The wobble and <see cref="AddDisturbance"/> are sized
    /// against the baseline inertia, so with the scaled tensor a braced relic really turns less: its angular response is
    /// divided by the current inertia scale. Torques act in the relic's principal axes, so the wobble rotates with it like a
    /// hand tremor.
    /// </para>
    /// <para>
    /// By default stabilization applies only while a hand holds the relic (<see cref="ForgeEvents.RelicGrabChanged"/>). A
    /// released relic gets its original damping and inertia back, so it falls and tumbles like any other object.
    /// </para>
    /// <para>
    /// Stability comes from <see cref="StanceTracker.OnStanceStabilityChanged"/> on an assigned tracker, or one found once
    /// when the component is enabled. If no tracker exists, a structured warning is logged once, a fallback stability is
    /// held, and <see cref="ForgeEvents.StanceChanged"/> is followed instead, so a tracker spawned later still drives the relic.
    /// </para>
    /// <para>
    /// Damping and inertia only shape motion the physics engine integrates. Drive a held relic through a joint or velocity
    /// tracking; a kinematic grab bypasses these settings. Disabling the component restores the Rigidbody's original values.
    /// Call <see cref="RecaptureBaseline"/> after changing the relic's colliders or mass, for example after a slice.
    /// </para>
    /// <para>
    /// Inside the <c>KineticForge.Physics</c> namespace, the name <c>Physics</c> resolves to this namespace, not to
    /// <c>UnityEngine.Physics</c>. Write <c>UnityEngine.Physics.Raycast</c> in full in this assembly and in any assembly
    /// that references it.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Physics/Relic Stabilizer")]
    public sealed class RelicStabilizer : MonoBehaviour
    {
        const string LogSource = nameof(RelicStabilizer);
        const float ApplyEpsilon = 0.0005f;
        const float WobbleCutoff = 0.001f;
        const float NoiseSeedRange = 1000f;
        const int HandMask = 0x7;
        const int AnonymousHandBit = 1 << (int)ForgeHand.None;

        static bool s_warnedMissingTracker;

        #region Serialized fields

        [Header("Stance Source")]
        [SerializeField, Tooltip("Stance tracker to follow. Found in the scene when the component is enabled if left empty.")]
        StanceTracker _stanceTracker = null;

        [SerializeField, Range(0f, 1f), Tooltip("Stability held while no stance tracker is available.")]
        float _fallbackStability = 0.25f;

        [Header("Response")]
        [SerializeField, Tooltip("Maps stance stability (x) to physical stabilization (y). Both axes in [0, 1].")]
        AnimationCurve _responseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [SerializeField, Tooltip("Stabilize only while a hand holds the relic (ForgeEvents.RelicGrabChanged). A released relic keeps its original damping and inertia. Untick to test without a grab system.")]
        bool _stabilizeOnlyWhileGrabbed = true;

        [SerializeField, Tooltip("Switch the Rigidbody to interpolation on Awake if it has none, so a held relic moves smoothly between physics steps.")]
        bool _enforceInterpolation = true;

        [Header("Linear Damping")]
        [SerializeField, Min(0f), Tooltip("Linear damping at zero stability.")]
        float _unstableLinearDamping = 0.05f;

        [SerializeField, Min(0f), Tooltip("Linear damping at full stability.")]
        float _stableLinearDamping = 3f;

        [Header("Angular Damping")]
        [SerializeField, Min(0f), Tooltip("Angular damping at zero stability.")]
        float _unstableAngularDamping = 0.05f;

        [SerializeField, Min(0f), Tooltip("Angular damping at full stability.")]
        float _stableAngularDamping = 10f;

        [Header("Inertia Tensor")]
        [SerializeField, Min(0.05f), Tooltip("Multiplier on the baseline inertia tensor at zero stability. Below 1 feels twitchy.")]
        float _unstableInertiaScale = 0.6f;

        [SerializeField, Min(0.05f), Tooltip("Multiplier on the baseline inertia tensor at full stability. Above 1 feels planted.")]
        float _stableInertiaScale = 3f;

        [Header("Instability Wobble")]
        [SerializeField, Tooltip("Apply a smooth noise torque that grows as stability drops.")]
        bool _enableWobble = true;

        [SerializeField, Tooltip("Wobble only while a hand holds this relic (ForgeEvents.RelicGrabChanged). Untick to test without a grab system.")]
        bool _wobbleOnlyWhileGrabbed = true;

        [SerializeField, Min(0f), Tooltip("Peak wobble angular acceleration (rad/s^2) at zero stability and baseline inertia. Higher inertia scales reduce it further.")]
        float _maxWobbleAcceleration = 8f;

        [SerializeField, Min(0.01f), Tooltip("How fast the wobble noise evolves. Higher values feel more jittery.")]
        float _wobbleFrequency = 2.5f;

        #endregion

        #region Runtime state

        Rigidbody _body;
        float _baselineLinearDamping;
        float _baselineAngularDamping;
        bool _baselineAutomaticInertia;
        Vector3 _baselineInertiaTensor;
        Quaternion _baselineInertiaTensorRotation;
        bool _canScaleInertia;

        float _targetStability;
        float _appliedResponse;
        float _writtenResponse;
        float _appliedInertiaScale = 1f;
        bool _appliedActive;
        bool _hasApplied;
        bool _dirty;
        int _grabMask;
        int _anonymousGrabs;
        Vector3 _noiseSeed;

        StanceTracker _subscribedTracker;
        bool _subscribedToBus;

        Action<float> _onStabilityChanged;
        Action<StanceChangedEvent> _onBusStanceChanged;
        Action<RelicGrabEvent> _onRelicGrabChanged;

        #endregion

        #region Public API

        /// <summary>The stabilized Rigidbody.</summary>
        public Rigidbody Body => _body;

        /// <summary>Latest stance stability received, in [0, 1].</summary>
        public float TargetStability => _targetStability;

        /// <summary>Response-curve output for the latest stability, in [0, 1]. Also drives the wobble.</summary>
        public float AppliedResponse => _appliedResponse;

        /// <summary>Inertia multiplier currently applied. 1 while stabilization is inactive or inertia cannot be scaled.</summary>
        public float CurrentInertiaScale => _appliedActive && _canScaleInertia ? _appliedInertiaScale : 1f;

        /// <summary>True while at least one hand, or a grab not tied to a hand, holds this relic.</summary>
        public bool IsGrabbed => _grabMask != 0 || _anonymousGrabs > 0;

        /// <summary>True while stance stabilization is written to the Rigidbody.</summary>
        public bool IsStabilizing => !_stabilizeOnlyWhileGrabbed || IsGrabbed;

        /// <summary>Collider-derived inertia tensor captured as the baseline, in the principal frame.</summary>
        public Vector3 BaselineInertiaTensor => _baselineInertiaTensor;

        /// <summary>Stance tracker currently followed, or null while following the event bus.</summary>
        public StanceTracker StanceSource => _subscribedTracker;

        /// <summary>Switches the stance tracker this relic follows. Pass null to search the scene again.</summary>
        /// <param name="tracker">Tracker to follow, or null.</param>
        public void SetStanceTracker(StanceTracker tracker)
        {
            bool bound = isActiveAndEnabled;
            if (bound)
            {
                UnbindStanceSource();
            }

            _stanceTracker = tracker;

            if (bound)
            {
                BindStanceSource();
            }
        }

        /// <summary>
        /// Applies an angular impulse sized to change the relic's angular velocity by
        /// <paramref name="worldAngularVelocityChange"/> (rad/s) at baseline inertia. A braced stance, with its higher
        /// inertia scale, reduces the actual change. Use for chisel recoil, bumps and scripted disturbances.
        /// </summary>
        /// <param name="worldAngularVelocityChange">Desired angular velocity change in world space at baseline inertia.</param>
        public void AddDisturbance(Vector3 worldAngularVelocityChange)
        {
            if (_body == null || _body.isKinematic || !ForgeMath.IsFinite(worldAngularVelocityChange))
            {
                return;
            }

            Quaternion principalToWorld = _body.rotation * _body.inertiaTensorRotation;
            Vector3 principal = Quaternion.Inverse(principalToWorld) * worldAngularVelocityChange;
            AddBaselineScaledTorque(principal, ForceMode.Impulse);
        }

        /// <summary>
        /// Recomputes the inertia baseline from the current colliders and mass, then reapplies stabilization.
        /// Call after slicing or otherwise reshaping the relic. Switches the Rigidbody to automatic inertia.
        /// </summary>
        public void RecaptureBaseline()
        {
            if (_body == null)
            {
                return;
            }

            _body.automaticInertiaTensor = true;
            _body.ResetInertiaTensor();
            _baselineAutomaticInertia = true;
            CaptureInertiaBaseline();
            _hasApplied = false;
            _dirty = true;
        }

        #endregion

        #region Unity lifecycle

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_warnedMissingTracker = false;
        }

        void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _onStabilityChanged = SetTargetStability;
            _onBusStanceChanged = HandleBusStanceChanged;
            _onRelicGrabChanged = HandleRelicGrabChanged;
            _noiseSeed = new Vector3(
                UnityEngine.Random.Range(0f, NoiseSeedRange),
                UnityEngine.Random.Range(0f, NoiseSeedRange),
                UnityEngine.Random.Range(0f, NoiseSeedRange));

            if (_enforceInterpolation && _body.interpolation == RigidbodyInterpolation.None)
            {
                _body.interpolation = RigidbodyInterpolation.Interpolate;
                ForgeLog.Info(ForgeLogChannel.Physics, LogSource, "INTERPOLATION_ENABLED",
                    "Relic '" + name + "' had no Rigidbody interpolation; enabled it for smooth held motion.", this);
            }

            _baselineLinearDamping = _body.linearDamping;
            _baselineAngularDamping = _body.angularDamping;
            _baselineAutomaticInertia = _body.automaticInertiaTensor;
            CaptureInertiaBaseline();
        }

        void OnEnable()
        {
            ForgeEvents.RelicGrabChanged += _onRelicGrabChanged;
            BindStanceSource();
        }

        void OnDisable()
        {
            ForgeEvents.RelicGrabChanged -= _onRelicGrabChanged;
            UnbindStanceSource();
            _grabMask = 0;
            _anonymousGrabs = 0;
            RestoreBaseline();
        }

        void FixedUpdate()
        {
            // A tracker destroyed under us (for example on a rig respawn) would freeze stability; rebind instead.
            if (!ReferenceEquals(_subscribedTracker, null) && _subscribedTracker == null)
            {
                UnbindStanceSource();
                _stanceTracker = null;
                BindStanceSource();
            }

            if (_dirty)
            {
                ApplyStability();
            }

            if (_enableWobble)
            {
                ApplyWobble();
            }
        }

        void OnValidate()
        {
            if (Application.isPlaying)
            {
                // Live tuning: reapply on the next physics step.
                _hasApplied = false;
                _dirty = true;
            }
        }

        #endregion

        #region Stance source

        void BindStanceSource()
        {
            StanceTracker tracker = _stanceTracker != null ? _stanceTracker : FindAnyObjectByType<StanceTracker>();
            if (tracker != null)
            {
                _stanceTracker = tracker;
                _subscribedTracker = tracker;
                tracker.OnStanceStabilityChanged += _onStabilityChanged;
                SetTargetStability(tracker.StanceStability);
                return;
            }

            if (!s_warnedMissingTracker)
            {
                s_warnedMissingTracker = true;
                ForgeLog.Warn(ForgeLogChannel.Physics, LogSource, "STANCE_TRACKER_MISSING",
                    "No StanceTracker found for relic '" + name + "'. Holding fallback stability " +
                    _fallbackStability.ToString("0.##", CultureInfo.InvariantCulture) +
                    " and following ForgeEvents.StanceChanged until a tracker appears. Logged once for all relics.", this);
            }

            ForgeEvents.StanceChanged += _onBusStanceChanged;
            _subscribedToBus = true;
            SetTargetStability(_fallbackStability);
        }

        void UnbindStanceSource()
        {
            // ReferenceEquals, not ==: a tracker destroyed during scene teardown still holds the managed event to detach from.
            if (!ReferenceEquals(_subscribedTracker, null))
            {
                _subscribedTracker.OnStanceStabilityChanged -= _onStabilityChanged;
                _subscribedTracker = null;
            }

            if (_subscribedToBus)
            {
                ForgeEvents.StanceChanged -= _onBusStanceChanged;
                _subscribedToBus = false;
            }
        }

        void SetTargetStability(float stability)
        {
            _targetStability = ForgeMath.Sanitize01(stability, _targetStability);
            _dirty = true;
        }

        void HandleBusStanceChanged(StanceChangedEvent payload)
        {
            SetTargetStability(payload.Stability);
        }

        void HandleRelicGrabChanged(RelicGrabEvent payload)
        {
            if (!ReferenceEquals(payload.Relic, _body))
            {
                return;
            }

            bool wasGrabbed = IsGrabbed;
            int bit = 1 << ((int)payload.Hand & HandMask);
            if (bit == AnonymousHandBit)
            {
                // Grabs not tied to a hand (scripted holds, fixtures) can overlap, so they are counted.
                _anonymousGrabs = payload.IsGrabbed ? _anonymousGrabs + 1 : Mathf.Max(0, _anonymousGrabs - 1);
            }
            else
            {
                // One bit per hand makes repeated grab or release events from the same hand idempotent.
                _grabMask = payload.IsGrabbed ? _grabMask | bit : _grabMask & ~bit;
            }

            if (IsGrabbed != wasGrabbed)
            {
                _dirty = true;
            }
        }

        #endregion

        #region Physics

        void ApplyStability()
        {
            _dirty = false;

            float response = EvaluateResponse(_targetStability);
            _appliedResponse = response;

            // Compare against the value last written to the body, so many sub-epsilon steps still add up to a write.
            bool active = IsStabilizing;
            bool stateChanged = !_hasApplied || active != _appliedActive;
            if (!stateChanged && (!active || Mathf.Abs(response - _writtenResponse) < ApplyEpsilon))
            {
                return;
            }

            _writtenResponse = response;
            _appliedActive = active;
            _hasApplied = true;

            if (!active)
            {
                RestorePhysicalBaseline();
                return;
            }

            _body.linearDamping = Mathf.Lerp(_unstableLinearDamping, _stableLinearDamping, response);
            _body.angularDamping = Mathf.Lerp(_unstableAngularDamping, _stableAngularDamping, response);

            if (_canScaleInertia)
            {
                _appliedInertiaScale = Mathf.Lerp(_unstableInertiaScale, _stableInertiaScale, response);
                _body.inertiaTensor = _baselineInertiaTensor * _appliedInertiaScale;
                _body.inertiaTensorRotation = _baselineInertiaTensorRotation;
            }
        }

        void ApplyWobble()
        {
            if (_maxWobbleAcceleration <= 0f || _body.isKinematic)
            {
                return;
            }

            if (_wobbleOnlyWhileGrabbed && !IsGrabbed)
            {
                return;
            }

            float instability = 1f - _appliedResponse;
            if (instability <= WobbleCutoff)
            {
                return;
            }

            // Time.time returns the fixed timestep clock inside FixedUpdate, keeping the noise deterministic per step.
            float t = Time.time * _wobbleFrequency;
            Vector3 noise = new Vector3(
                Mathf.PerlinNoise(t, _noiseSeed.x) - 0.5f,
                Mathf.PerlinNoise(_noiseSeed.y, t) - 0.5f,
                Mathf.PerlinNoise(t + _noiseSeed.z, _noiseSeed.z) - 0.5f);

            // Centered Perlin noise spans about [-0.5, 0.5]; doubling maps it to about [-1, 1].
            AddBaselineScaledTorque(noise * (2f * instability * _maxWobbleAcceleration), ForceMode.Force);
        }

        /// <summary>
        /// Applies a principal-frame angular quantity scaled by the baseline inertia: an angular acceleration with
        /// <see cref="ForceMode.Force"/>, or an angular velocity change with <see cref="ForceMode.Impulse"/>. The engine then
        /// divides by the current, stance-scaled inertia.
        /// </summary>
        void AddBaselineScaledTorque(Vector3 principalAngularQuantity, ForceMode mode)
        {
            if (!_canScaleInertia)
            {
                // No valid tensor to size a torque against: apply the motion directly in world space.
                Vector3 world = _body.rotation * _body.inertiaTensorRotation * principalAngularQuantity;
                _body.AddTorque(world, mode == ForceMode.Impulse ? ForceMode.VelocityChange : ForceMode.Acceleration);
                return;
            }

            Vector3 principalTorque = Vector3.Scale(_baselineInertiaTensor, principalAngularQuantity);
            Quaternion principalToWorld = _body.rotation * _body.inertiaTensorRotation;
            _body.AddTorque(principalToWorld * principalTorque, mode);
        }

        float EvaluateResponse(float stability)
        {
            if (_responseCurve == null || _responseCurve.length == 0)
            {
                return stability;
            }

            return ForgeMath.Sanitize01(_responseCurve.Evaluate(stability), stability);
        }

        void CaptureInertiaBaseline()
        {
            _baselineInertiaTensor = _body.inertiaTensor;
            _baselineInertiaTensorRotation = _body.inertiaTensorRotation;
            _canScaleInertia = _baselineInertiaTensor.x > 0f && _baselineInertiaTensor.y > 0f &&
                               _baselineInertiaTensor.z > 0f && ForgeMath.IsFinite(_baselineInertiaTensor);
            _appliedInertiaScale = 1f;

            if (!_canScaleInertia)
            {
                ForgeLog.Warn(ForgeLogChannel.Physics, LogSource, "INERTIA_INVALID",
                    "Relic '" + name + "' has a non-positive inertia tensor " + _baselineInertiaTensor +
                    ". Inertia scaling is off; damping still applies. Add colliders or set a valid tensor.", this);
            }
        }

        void RestoreBaseline()
        {
            if (_body == null)
            {
                return;
            }

            RestorePhysicalBaseline();
            _hasApplied = false;
            _appliedActive = false;
        }

        void RestorePhysicalBaseline()
        {
            _body.linearDamping = _baselineLinearDamping;
            _body.angularDamping = _baselineAngularDamping;
            _appliedInertiaScale = 1f;

            if (_baselineAutomaticInertia)
            {
                _body.automaticInertiaTensor = true;
            }
            else if (_canScaleInertia)
            {
                _body.inertiaTensor = _baselineInertiaTensor;
                _body.inertiaTensorRotation = _baselineInertiaTensorRotation;
            }
        }

        #endregion
    }
}
