using KineticForge.Core;
using UnityEngine;

namespace KineticForge.Physics
{
    /// <summary>
    /// Physical grip for heavy relics: each holding hand pulls the relic toward itself through a spring-damper
    /// <see cref="ConfigurableJoint"/>, so the relic keeps its mass, collides, lags and swings instead of snapping to the
    /// hand. The grip's damping follows stance stability, which makes the Dual-Scale link tangible in the player's hands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each grip connects the relic to a kinematic anchor that follows the hand's transform every physics step. The joint
    /// keeps the grab-time offset between hand and relic, in position and rotation, so the relic stays where it was taken.
    /// Two hands make two joints and a stiffer, steadier hold, as in reality.
    /// </para>
    /// <para>
    /// Drive stiffness is set from natural frequency against the relic's mass and its current, stance-scaled inertia, so
    /// the relic follows the hand at the same pace whatever its size. The damping ratio is interpolated by the
    /// <see cref="RelicStabilizer"/> response: braced, the grip is close to critically damped and settles cleanly; unbraced,
    /// it is underdamped and overshoots. Because wobble and disturbances are sized against baseline inertia while the
    /// rotational spring scales with the current inertia, a braced relic also deflects less under them.
    /// </para>
    /// <para>
    /// The hands carry the relic's weight while it is held, and a grip that stretches past the break distance, for
    /// example when tracking jumps, lets go. Every grab and release is published through
    /// <see cref="ForgeEvents.RaiseRelicGrabChanged"/>, which also switches the stabilizer on.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Physics/Relic Grab Driver")]
    public sealed class RelicGrabDriver : MonoBehaviour
    {
        const string LogSource = nameof(RelicGrabDriver);
        const int SlotCount = 3;
        const float TwoPi = 2f * Mathf.PI;
        const float ResponseEpsilon = 0.01f;

        #region Serialized fields

        [Header("Positional Drive")]
        [SerializeField, Min(0.1f), Tooltip("Natural frequency (Hz) of each hand's positional spring. Lower feels heavier.")]
        float _linearFrequency = 3f;

        [SerializeField, Range(0f, 2f), Tooltip("Positional damping ratio at zero stability. Below 1 overshoots.")]
        float _unstableLinearDampingRatio = 0.3f;

        [SerializeField, Range(0f, 2f), Tooltip("Positional damping ratio at full stability. 1 is critically damped.")]
        float _stableLinearDampingRatio = 1f;

        [SerializeField, Min(0f), Tooltip("Largest force (N) one hand can apply. Limits how hard a heavy relic can be yanked.")]
        float _maxForce = 2500f;

        [Header("Rotational Drive")]
        [SerializeField, Min(0.1f), Tooltip("Natural frequency (Hz) of each hand's rotational spring.")]
        float _angularFrequency = 1.8f;

        [SerializeField, Range(0f, 2f), Tooltip("Rotational damping ratio at zero stability.")]
        float _unstableAngularDampingRatio = 0.2f;

        [SerializeField, Range(0f, 2f), Tooltip("Rotational damping ratio at full stability.")]
        float _stableAngularDampingRatio = 1f;

        [SerializeField, Min(0f), Tooltip("Largest torque (N·m) one hand can apply.")]
        float _maxTorque = 400f;

        [Header("Grip")]
        [SerializeField, Tooltip("Cancel gravity while held, as supporting hands do, so the springs only handle motion.")]
        bool _supportWeight = true;

        [SerializeField, Min(0.05f), Tooltip("Release a grip whose hand gets this far (m) from its grip point, for example after a tracking jump.")]
        float _breakDistance = 0.45f;

        #endregion

        #region Runtime state

        struct Grip
        {
            public bool Active;
            public Transform Target;
            public GameObject AnchorObject;
            public Rigidbody Anchor;
            public ConfigurableJoint Joint;
            public Vector3 LocalGripPoint;
        }

        readonly Grip[] _grips = new Grip[SlotCount];
        Rigidbody _body;
        RelicStabilizer _stabilizer;
        int _gripCount;
        float _drivenResponse = -1f;
        float _drivenInertia = -1f;

        #endregion

        #region Public API

        /// <summary>The held Rigidbody.</summary>
        public Rigidbody Body => _body;

        /// <summary>True while at least one hand grips the relic.</summary>
        public bool IsHeld => _gripCount > 0;

        /// <summary>Number of active grips.</summary>
        public int GripCount => _gripCount;

        /// <summary>True while <paramref name="hand"/> grips the relic.</summary>
        /// <param name="hand">Hand to check.</param>
        /// <returns>Whether that hand holds the relic.</returns>
        public bool IsHeldBy(ForgeHand hand)
        {
            int slot = (int)hand;
            return slot >= 0 && slot < SlotCount && _grips[slot].Active;
        }

        /// <summary>
        /// Starts a grip: <paramref name="hand"/> holds the relic at <paramref name="worldGripPoint"/> and pulls it along with
        /// <paramref name="target"/>, keeping the current offset between the two.
        /// </summary>
        /// <param name="hand">Hand that grips. Each hand can hold one grip.</param>
        /// <param name="target">Transform that follows the hand, for example an OVRCameraRig hand anchor.</param>
        /// <param name="worldGripPoint">World-space point on the relic where the hand closed.</param>
        /// <returns>False when that hand already grips, the target is missing or the component is inactive.</returns>
        public bool BeginGrab(ForgeHand hand, Transform target, Vector3 worldGripPoint)
        {
            int slot = (int)hand;
            if (!isActiveAndEnabled || target == null || slot < 0 || slot >= SlotCount || _grips[slot].Active ||
                !ForgeMath.IsFinite(worldGripPoint))
            {
                return false;
            }

            GameObject anchorObject = new GameObject(name + " Grip (" + hand + ")");
            anchorObject.transform.SetPositionAndRotation(target.position, target.rotation);
            Rigidbody anchor = anchorObject.AddComponent<Rigidbody>();
            anchor.isKinematic = true;
            anchor.interpolation = RigidbodyInterpolation.Interpolate;

            ConfigurableJoint joint = gameObject.AddComponent<ConfigurableJoint>();
            joint.autoConfigureConnectedAnchor = false;
            joint.connectedBody = anchor;
            joint.anchor = transform.InverseTransformPoint(worldGripPoint);
            joint.connectedAnchor = anchorObject.transform.InverseTransformPoint(worldGripPoint);
            joint.enableCollision = false;
            joint.xMotion = ConfigurableJointMotion.Free;
            joint.yMotion = ConfigurableJointMotion.Free;
            joint.zMotion = ConfigurableJointMotion.Free;
            joint.angularXMotion = ConfigurableJointMotion.Free;
            joint.angularYMotion = ConfigurableJointMotion.Free;
            joint.angularZMotion = ConfigurableJointMotion.Free;
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.targetPosition = Vector3.zero;
            joint.targetRotation = Quaternion.identity;

            _grips[slot] = new Grip
            {
                Active = true,
                Target = target,
                AnchorObject = anchorObject,
                Anchor = anchor,
                Joint = joint,
                LocalGripPoint = joint.anchor,
            };
            _gripCount++;
            ConfigureDrives(CurrentResponse(), force: true);

            ForgeEvents.RaiseRelicGrabChanged(new RelicGrabEvent(_body, hand, true));
            return true;
        }

        /// <summary>Ends the grip of <paramref name="hand"/>. Does nothing when that hand is not gripping.</summary>
        /// <param name="hand">Hand that lets go.</param>
        public void EndGrab(ForgeHand hand)
        {
            int slot = (int)hand;
            if (slot < 0 || slot >= SlotCount || !_grips[slot].Active)
            {
                return;
            }

            Grip grip = _grips[slot];
            _grips[slot] = default;
            _gripCount--;

            if (grip.Joint != null)
            {
                Destroy(grip.Joint);
            }

            if (grip.AnchorObject != null)
            {
                Destroy(grip.AnchorObject);
            }

            ForgeEvents.RaiseRelicGrabChanged(new RelicGrabEvent(_body, hand, false));
        }

        /// <summary>Releases every grip.</summary>
        public void ReleaseAll()
        {
            for (int slot = 0; slot < SlotCount; slot++)
            {
                EndGrab((ForgeHand)slot);
            }
        }

        #endregion

        #region Unity lifecycle

        void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _stabilizer = GetComponent<RelicStabilizer>();
        }

        void OnDisable()
        {
            ReleaseAll();
        }

        void FixedUpdate()
        {
            if (_gripCount == 0)
            {
                return;
            }

            for (int slot = 0; slot < SlotCount; slot++)
            {
                if (!_grips[slot].Active)
                {
                    continue;
                }

                Grip grip = _grips[slot];
                if (grip.Target == null || grip.Anchor == null || grip.Joint == null)
                {
                    EndGrab((ForgeHand)slot);
                    continue;
                }

                grip.Anchor.MovePosition(grip.Target.position);
                grip.Anchor.MoveRotation(grip.Target.rotation);

                Vector3 relicPoint = transform.TransformPoint(grip.LocalGripPoint);
                Vector3 handPoint = HandGripPoint(in grip);
                if ((relicPoint - handPoint).sqrMagnitude > _breakDistance * _breakDistance)
                {
                    ForgeLog.Info(ForgeLogChannel.Physics, LogSource, "GRIP_BROKEN",
                        (ForgeHand)slot + " hand moved too far from relic '" + name + "'; releasing.", this);
                    EndGrab((ForgeHand)slot);
                }
            }

            if (_gripCount == 0)
            {
                return;
            }

            ConfigureDrives(CurrentResponse(), force: false);

            if (_supportWeight && _body.useGravity && !_body.isKinematic)
            {
                _body.AddForce(-UnityEngine.Physics.gravity, ForceMode.Acceleration);
            }
        }

        void OnValidate()
        {
            _drivenResponse = -1f;
        }

        #endregion

        #region Drives

        float CurrentResponse()
        {
            return _stabilizer != null ? _stabilizer.AppliedResponse : 1f;
        }

        void ConfigureDrives(float response, bool force)
        {
            Vector3 inertia = _body.inertiaTensor;
            float meanInertia = Mathf.Max(1e-5f, (inertia.x + inertia.y + inertia.z) / 3f);
            if (!force && Mathf.Abs(response - _drivenResponse) < ResponseEpsilon &&
                Mathf.Abs(meanInertia - _drivenInertia) <= _drivenInertia * 0.01f)
            {
                return;
            }

            _drivenResponse = response;
            _drivenInertia = meanInertia;

            // Spring-damper from natural frequency and damping ratio: k = m w^2 and c = 2 z m w, with inertia for mass.
            float mass = Mathf.Max(0.01f, _body.mass);
            float linearOmega = TwoPi * _linearFrequency;
            float linearRatio = Mathf.Lerp(_unstableLinearDampingRatio, _stableLinearDampingRatio, response);
            JointDrive linear = MakeDrive(
                mass * linearOmega * linearOmega, 2f * linearRatio * mass * linearOmega, _maxForce);

            float angularOmega = TwoPi * _angularFrequency;
            float angularRatio = Mathf.Lerp(_unstableAngularDampingRatio, _stableAngularDampingRatio, response);
            JointDrive angular = MakeDrive(
                meanInertia * angularOmega * angularOmega, 2f * angularRatio * meanInertia * angularOmega, _maxTorque);

            for (int slot = 0; slot < SlotCount; slot++)
            {
                ConfigurableJoint joint = _grips[slot].Joint;
                if (!_grips[slot].Active || joint == null)
                {
                    continue;
                }

                joint.xDrive = linear;
                joint.yDrive = linear;
                joint.zDrive = linear;
                joint.slerpDrive = angular;
            }
        }

        static Vector3 HandGripPoint(in Grip grip)
        {
            // The anchor copies the target's position and rotation but not its scale, so ignore scale here too.
            return grip.Target.position + grip.Target.rotation * grip.Joint.connectedAnchor;
        }

        static JointDrive MakeDrive(float spring, float damper, float maximum)
        {
            JointDrive drive = default;
            drive.positionSpring = spring;
            drive.positionDamper = damper;
            drive.maximumForce = maximum > 0f ? maximum : float.MaxValue;
            return drive;
        }

        #endregion

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            for (int slot = 0; slot < SlotCount; slot++)
            {
                Grip grip = _grips[slot];
                if (!grip.Active || grip.Target == null || grip.Joint == null)
                {
                    continue;
                }

                Vector3 relicPoint = transform.TransformPoint(grip.LocalGripPoint);
                Vector3 handPoint = HandGripPoint(in grip);
                Gizmos.color = new Color(1f, 0.6f, 0.1f);
                Gizmos.DrawWireSphere(handPoint, 0.02f);
                Gizmos.DrawLine(handPoint, relicPoint);
            }
        }
#endif
    }
}
