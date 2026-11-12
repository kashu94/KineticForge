using System;
using KineticForge.Core;
using KineticForge.Crafting;
using KineticForge.Physics;
using UnityEngine;

namespace KineticForge.Feedback
{
    /// <summary>Stage of the quench ceremony.</summary>
    public enum QuenchStage : byte
    {
        /// <summary>Waiting for a forged relic.</summary>
        Idle = 0,

        /// <summary>Carrying the relic to the water and plunging it.</summary>
        Plunging = 1,

        /// <summary>Hissing in the water while the heat drains away.</summary>
        Soaking = 2,

        /// <summary>Carrying the relic back to the cradle.</summary>
        Returning = 3,

        /// <summary>Quenched; the relic has sung its note.</summary>
        Done = 4,
    }

    /// <summary>
    /// The quench: a forged relic is carried from its cradle, plunged into the water trough in a burst of steam and a
    /// long hiss as its glow drains away, carried back, and struck once so it sings its tuned note.
    /// </summary>
    /// <remarks>
    /// Moves the relic by animating <see cref="RelicHoldSimulator.HoldOffset"/>, so the relic stays a physical body held by
    /// its spring grip the whole way. <see cref="Begin"/> is called by the guide when the player pinches on the result
    /// screen. The per-frame path does not allocate.
    /// </remarks>
    [RequireComponent(typeof(RelicHoldSimulator), typeof(RelicResonance))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Feedback/Relic Quench")]
    public sealed class RelicQuench : MonoBehaviour
    {
        [SerializeField, Tooltip("Point on the water surface the relic plunges into.")]
        Transform _water = null;

        [SerializeField, Tooltip("Additive material for steam (Kinetic Forge/Forge Glow).")]
        Material _steamMaterial = null;

        [SerializeField, Range(0f, 0.3f), Tooltip("How far (m) below the water surface the relic's center plunges.")]
        float _plungeDepth = 0.06f;

        [SerializeField, Range(0.05f, 0.5f), Tooltip("Clearance (m) the relic is lifted by before it moves sideways.")]
        float _clearance = 0.2f;

        [SerializeField, Range(0.2f, 4f), Tooltip("Seconds to carry the relic to the water.")]
        float _plungeSeconds = 1.8f;

        [SerializeField, Range(0.2f, 4f), Tooltip("Seconds in the water.")]
        float _soakSeconds = 1.6f;

        [SerializeField, Range(0.2f, 4f), Tooltip("Seconds to carry the relic back.")]
        float _returnSeconds = 1.6f;

        readonly ForgeSignal _quenched = new ForgeSignal("RelicQuench.OnQuenched");
        RelicHoldSimulator _hold;
        RelicResonance _resonance;
        RelicSynth _synth;
        Rigidbody _body;
        ParticleSystem _steam;
        QuenchStage _stage;
        float _stageTime;
        float _heatAtPlunge;
        Vector3 _restOffset;
        Vector3 _waterOffset;
        Vector3 _liftOffset;
        Vector3 _overOffset;
        Action<GamePhaseChangedEvent> _onPhaseChanged;

        /// <summary>Raised when the relic has been quenched and has sung.</summary>
        public event Action OnQuenched
        {
            add => _quenched.Add(value);
            remove => _quenched.Remove(value);
        }

        /// <summary>Current stage.</summary>
        public QuenchStage Stage => _stage;

        /// <summary>True when a forged relic is waiting to be quenched.</summary>
        public bool IsAvailable => _stage == QuenchStage.Idle && _resonance.IsForged && _water != null;

        /// <summary>True while the ceremony runs.</summary>
        public bool IsBusy => _stage == QuenchStage.Plunging || _stage == QuenchStage.Soaking || _stage == QuenchStage.Returning;

        /// <summary>Starts the quench.</summary>
        /// <returns>False when no forged relic is waiting.</returns>
        public bool Begin()
        {
            if (!IsAvailable)
            {
                return false;
            }

            _restOffset = _hold.HoldOffset;
            Vector3 relic = _body != null ? _body.worldCenterOfMass : transform.position;
            Vector3 grip = relic - _restOffset;
            Vector3 water = _water.position + Vector3.down * _plungeDepth;
            _liftOffset = _restOffset + Vector3.up * _clearance;
            _overOffset = new Vector3(water.x, relic.y + _clearance, water.z) - grip;
            _waterOffset = water - grip;
            Enter(QuenchStage.Plunging);
            return true;
        }

        void Awake()
        {
            _hold = GetComponent<RelicHoldSimulator>();
            _resonance = GetComponent<RelicResonance>();
            _synth = GetComponent<RelicSynth>();
            _body = GetComponent<Rigidbody>();
            _onPhaseChanged = HandlePhaseChanged;
            _steam = CreateSteam();
        }

        void OnEnable()
        {
            ForgeEvents.GamePhaseChanged += _onPhaseChanged;
        }

        void OnDisable()
        {
            ForgeEvents.GamePhaseChanged -= _onPhaseChanged;
        }

        void HandlePhaseChanged(GamePhaseChangedEvent payload)
        {
            if (payload.Current == ForgePhase.FirstRelicForging || payload.Current == ForgePhase.Intro)
            {
                if (IsBusy)
                {
                    _hold.HoldOffset = _restOffset;
                }

                _stage = QuenchStage.Idle;
            }
        }

        void Update()
        {
            if (!IsBusy)
            {
                return;
            }

            _stageTime += Time.deltaTime;
            switch (_stage)
            {
                case QuenchStage.Plunging:
                {
                    // Up, across, then down into the water: three eased legs.
                    float t = Mathf.Clamp01(_stageTime / _plungeSeconds) * 3f;
                    _hold.HoldOffset = t < 1f
                        ? Vector3.Lerp(_restOffset, _liftOffset, Ease(t))
                        : t < 2f
                            ? Vector3.Lerp(_liftOffset, _overOffset, Ease(t - 1f))
                            : Vector3.Lerp(_overOffset, _waterOffset, Ease(t - 2f));
                    if (_stageTime >= _plungeSeconds)
                    {
                        _heatAtPlunge = _resonance.Heat;
                        Steam(70);
                        if (_synth != null)
                        {
                            _synth.PlayHiss();
                        }

                        Enter(QuenchStage.Soaking);
                    }

                    break;
                }

                case QuenchStage.Soaking:
                {
                    float t = Mathf.Clamp01(_stageTime / _soakSeconds);
                    _resonance.SetHeat(Mathf.Lerp(_heatAtPlunge, 0f, Mathf.Sqrt(t)));
                    if (UnityEngine.Random.value < 0.4f)
                    {
                        Steam(1);
                    }

                    if (_stageTime >= _soakSeconds)
                    {
                        Enter(QuenchStage.Returning);
                    }

                    break;
                }

                case QuenchStage.Returning:
                {
                    float t = Mathf.Clamp01(_stageTime / _returnSeconds) * 3f;
                    _hold.HoldOffset = t < 1f
                        ? Vector3.Lerp(_waterOffset, _overOffset, Ease(t))
                        : t < 2f
                            ? Vector3.Lerp(_overOffset, _liftOffset, Ease(t - 1f))
                            : Vector3.Lerp(_liftOffset, _restOffset, Ease(t - 2f));
                    if (_stageTime >= _returnSeconds)
                    {
                        _hold.HoldOffset = _restOffset;
                        if (_synth != null)
                        {
                            _synth.Play(1f, new Vector4(1f, 1f, 1f, 1f));
                        }

                        Enter(QuenchStage.Done);
                        _quenched.Raise();
                    }

                    break;
                }
            }
        }

        void Enter(QuenchStage stage)
        {
            _stage = stage;
            _stageTime = 0f;
        }

        static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        void Steam(int count)
        {
            if (_steam == null || _water == null)
            {
                return;
            }

            ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                emit.position = _water.position + new Vector3((UnityEngine.Random.value - 0.5f) * 0.3f, 0.01f,
                    (UnityEngine.Random.value - 0.5f) * 0.2f);
                emit.velocity = new Vector3((UnityEngine.Random.value - 0.5f) * 0.2f, UnityEngine.Random.Range(0.35f, 0.9f),
                    (UnityEngine.Random.value - 0.5f) * 0.2f);
                emit.startSize = UnityEngine.Random.Range(0.06f, 0.14f);
                emit.startLifetime = UnityEngine.Random.Range(1.2f, 2.4f);
                emit.startColor = new Color(0.55f, 0.55f, 0.6f, 0.35f);
                _steam.Emit(emit, 1);
            }
        }

        ParticleSystem CreateSteam()
        {
            GameObject host = new GameObject("Quench Steam");
            host.transform.SetParent(transform, false);
            ParticleSystem system = host.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            main.startSpeed = 0f;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;
            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 2.5f));
            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            ParticleSystemRenderer renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _steamMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            system.Play();
            return system;
        }
    }
}
