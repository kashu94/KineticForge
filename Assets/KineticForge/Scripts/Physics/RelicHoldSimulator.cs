using KineticForge.Core;
using KineticForge.Tracking;
using UnityEngine;

namespace KineticForge.Physics
{
    /// <summary>
    /// Development fixture: a simulated hand that picks the relic up through its <see cref="RelicGrabDriver"/>, so stance
    /// stabilization and the physical grip can be seen and tuned without a headset.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On start, an invisible hand proxy grips the relic at its center of mass and lifts it by <see cref="HoldOffset"/>
    /// over a short ease, then optionally traces a slow figure-eight to mimic carrying. The grip goes through exactly the
    /// same driver, joints and <see cref="ForgeEvents.RelicGrabChanged"/> event as a real hand, so whatever is tuned here
    /// carries over to the headset.
    /// </para>
    /// <para>
    /// Pressing the nudge key (Editor only) kicks the relic through <see cref="RelicStabilizer.AddDisturbance"/>. Hold the
    /// stance tracker's brace key (Space) and watch the relic settle and shrug off nudges. Disabling the component releases
    /// the relic.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Rigidbody), typeof(RelicGrabDriver))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Physics/Relic Hold Simulator")]
    public sealed class RelicHoldSimulator : MonoBehaviour
    {
        const float TwoPi = 2f * Mathf.PI;

        [Header("Grip")]
        [SerializeField, Tooltip("Hand reported for the simulated grip.")]
        ForgeHand _hand = ForgeHand.Right;

        [SerializeField, Tooltip("World-space lift from the relic's start position to the hold point. Lifts it off its pedestal.")]
        Vector3 _holdOffset = new Vector3(0f, 0.25f, 0f);

        [SerializeField, Min(0.01f), Tooltip("Seconds the simulated hand takes to lift the relic to the hold point.")]
        float _liftDuration = 0.6f;

        [Header("Carry Motion")]
        [SerializeField, Min(0f), Tooltip("Size (m) of the figure-eight the hand traces. 0 keeps it still.")]
        float _carryAmplitude = 0.05f;

        [SerializeField, Min(0f), Tooltip("Speed (Hz) of the figure-eight.")]
        float _carryFrequency = 0.3f;

        [Header("Disturbance (Editor keyboard)")]
        [SerializeField, Tooltip("Press to kick the relic around a random axis.")]
        KeyCode _nudgeKey = KeyCode.N;

        [SerializeField, Min(0f), Tooltip("Angular speed change (rad/s) of a nudge at baseline inertia. A braced stance reduces it.")]
        float _nudgeAngularSpeed = 3f;

        Rigidbody _body;
        RelicGrabDriver _driver;
        RelicStabilizer _stabilizer;
        Transform _proxy;
        Vector3 _gripStart;
        float _holdTime;
        bool _started;
        bool _nudgeQueued;

        /// <summary>World-space lift from the relic's start position to the hold point.</summary>
        public Vector3 HoldOffset => _holdOffset;

        /// <summary>True while the simulated hand grips the relic.</summary>
        public bool IsHolding => _driver != null && _driver.IsHeldBy(_hand);

        /// <summary>Queues a nudge for the next physics step, as if the nudge key were pressed.</summary>
        public void Nudge() => _nudgeQueued = true;

        void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _driver = GetComponent<RelicGrabDriver>();
            _stabilizer = GetComponent<RelicStabilizer>();
        }

        void OnEnable()
        {
            // Grab only after every component has run OnEnable, so a RelicStabilizer is already listening.
            if (_started)
            {
                Grab();
            }
        }

        void Start()
        {
            _started = true;
            Grab();
        }

        void OnDisable()
        {
            if (_driver != null)
            {
                _driver.EndGrab(_hand);
            }

            if (_proxy != null)
            {
                Destroy(_proxy.gameObject);
                _proxy = null;
            }
        }

        void Update()
        {
            if (SimulatorKeyboard.WasPressedThisFrame(_nudgeKey))
            {
                _nudgeQueued = true;
            }
        }

        void FixedUpdate()
        {
            if (_proxy == null)
            {
                return;
            }

            _holdTime += Time.fixedDeltaTime;

            // Ease the lift in (smoothstep), then add the carry figure-eight on top.
            float lift = Mathf.SmoothStep(0f, 1f, _holdTime / _liftDuration);
            Vector3 target = _gripStart + _holdOffset * lift;
            if (_carryAmplitude > 0f && _carryFrequency > 0f)
            {
                float phase = TwoPi * _carryFrequency * _holdTime;
                target += new Vector3(Mathf.Sin(phase), 0.5f * Mathf.Sin(2f * phase), 0f) * (_carryAmplitude * lift);
            }

            _proxy.position = target;

            if (_nudgeQueued)
            {
                _nudgeQueued = false;
                Vector3 change = Random.onUnitSphere * _nudgeAngularSpeed;
                if (_stabilizer != null)
                {
                    _stabilizer.AddDisturbance(change);
                }
                else
                {
                    _body.AddTorque(change, ForceMode.VelocityChange);
                }
            }
        }

        void Grab()
        {
            if (_driver == null || _driver.IsHeldBy(_hand))
            {
                return;
            }

            if (_proxy == null)
            {
                _proxy = new GameObject(name + " Simulated Hand").transform;
            }

            _gripStart = _body.worldCenterOfMass;
            _holdTime = 0f;
            _proxy.SetPositionAndRotation(_gripStart, _body.rotation);
            _driver.BeginGrab(_hand, _proxy, _gripStart);
        }
    }
}
