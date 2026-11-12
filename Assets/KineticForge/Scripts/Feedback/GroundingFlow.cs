using System;
using KineticForge.Core;
using KineticForge.Crafting;
using KineticForge.Physics;
using UnityEngine;

namespace KineticForge.Feedback
{
    /// <summary>
    /// Makes "your body is the damper" visible. With a braced stance, the relic's resonance drains as glowing energy down
    /// into the floor at the player's feet, and every pulse sends a cymatic ripple across the floor. With a loose stance,
    /// the same energy has nowhere to go: the relic spits sparks from its edges.
    /// </summary>
    /// <remarks>
    /// Stance comes from the relic's <see cref="RelicStabilizer"/> when it has one, otherwise from
    /// <see cref="ForgeEvents.StanceChanged"/>. The player's feet are the main camera projected to the floor. Particle
    /// systems are built on Awake; the per-frame path emits with <see cref="ParticleSystem.Emit(ParticleSystem.EmitParams, int)"/>
    /// and does not allocate.
    /// </remarks>
    [RequireComponent(typeof(RelicResonance))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Feedback/Grounding Flow")]
    public sealed class GroundingFlow : MonoBehaviour
    {
        [SerializeField, Tooltip("Additive material for the energy stream and sparks (Kinetic Forge/Forge Glow).")]
        Material _streamMaterial = null;

        [SerializeField, Tooltip("Additive ring material for floor ripples (Forge Glow with a ring thickness).")]
        Material _rippleMaterial = null;

        [SerializeField, Tooltip("World height of the floor.")]
        float _floorHeight = 0f;

        [SerializeField, Range(0f, 200f), Tooltip("Energy motes per second at full charge and a fully braced stance.")]
        float _streamRate = 70f;

        [SerializeField, Range(0f, 200f), Tooltip("Sparks per second at full charge and a fully loose stance.")]
        float _sparkRate = 45f;

        [SerializeField, Range(0.5f, 6f), Tooltip("Final diameter (m) of a floor ripple.")]
        float _rippleSize = 2.6f;

        [SerializeField, ColorUsage(false, true), Tooltip("Color of grounded energy.")]
        Color _groundedColor = new Color(0.45f, 0.85f, 1.2f);

        [SerializeField, ColorUsage(false, true), Tooltip("Color of loose energy sparks.")]
        Color _looseColor = new Color(1.2f, 0.45f, 0.12f);

        RelicResonance _resonance;
        RelicStabilizer _stabilizer;
        Rigidbody _body;
        ParticleSystem _stream;
        ParticleSystem _ripples;
        float _busStability;
        float _streamDebt;
        float _sparkDebt;
        float _lastTiming;
        Action<StanceChangedEvent> _onStance;

        /// <summary>Stance stability in [0, 1] the flow is using.</summary>
        public float Stability => _stabilizer != null ? _stabilizer.TargetStability : _busStability;

        void Awake()
        {
            _resonance = GetComponent<RelicResonance>();
            _stabilizer = GetComponent<RelicStabilizer>();
            _body = GetComponent<Rigidbody>();
            _onStance = payload => _busStability = payload.Stability;
            _stream = CreateSystem("Grounding Stream", _streamMaterial, 400, ParticleSystemRenderMode.Stretch);
            _ripples = CreateSystem("Floor Ripples", _rippleMaterial, 12, ParticleSystemRenderMode.HorizontalBillboard);
            ParticleSystem.SizeOverLifetimeModule size = _ripples.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.12f, 1f, 1f));
        }

        void OnEnable()
        {
            ForgeEvents.StanceChanged += _onStance;
        }

        void OnDisable()
        {
            ForgeEvents.StanceChanged -= _onStance;
        }

        void LateUpdate()
        {
            float deltaTime = Time.deltaTime;
            if (!(deltaTime > 0f))
            {
                return;
            }

            float charge = Mathf.Clamp01(_resonance.Charge / 1.2f);
            float stability = Mathf.Clamp01(Stability);
            Vector3 relic = _body != null ? _body.worldCenterOfMass : transform.position;
            Vector3 feet = Feet(relic);

            // Grounded energy: motes fall from the relic's underside to the floor at the player's feet.
            _streamDebt += _streamRate * charge * stability * deltaTime;
            ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            while (_streamDebt >= 1f)
            {
                _streamDebt -= 1f;
                Vector3 start = relic + new Vector3((UnityEngine.Random.value - 0.5f) * 0.24f, -0.15f, (UnityEngine.Random.value - 0.5f) * 0.06f);
                Vector3 end = feet + new Vector3((UnityEngine.Random.value - 0.5f) * 0.35f, 0f, (UnityEngine.Random.value - 0.5f) * 0.35f);
                float lifetime = UnityEngine.Random.Range(0.45f, 0.7f);
                emit.position = start;
                emit.velocity = (end - start) / lifetime;
                emit.startLifetime = lifetime;
                emit.startSize = UnityEngine.Random.Range(0.004f, 0.008f);
                emit.startColor = _groundedColor * UnityEngine.Random.Range(0.6f, 1.1f);
                _stream.Emit(emit, 1);
            }

            // Loose energy: sparks thrown off the relic's edges.
            _sparkDebt += _sparkRate * charge * (1f - stability) * deltaTime;
            while (_sparkDebt >= 1f)
            {
                _sparkDebt -= 1f;
                Vector3 outward = UnityEngine.Random.onUnitSphere;
                outward.y = Mathf.Abs(outward.y) * 0.5f;
                emit.position = relic + Vector3.Scale(outward.normalized, new Vector3(0.16f, 0.16f, 0.05f));
                emit.velocity = outward.normalized * UnityEngine.Random.Range(0.8f, 2.2f);
                emit.startLifetime = UnityEngine.Random.Range(0.25f, 0.5f);
                emit.startSize = UnityEngine.Random.Range(0.003f, 0.006f);
                emit.startColor = _looseColor * UnityEngine.Random.Range(0.7f, 1.2f);
                _stream.Emit(emit, 1);
            }

            // A ripple each time the pulse passes its peak, as strong as the energy grounded.
            float timing = _resonance.IsPulsing ? _resonance.TimingNow : 0f;
            if (_lastTiming < 0.97f && timing >= 0.97f && charge * stability > 0.05f)
            {
                emit.position = new Vector3(feet.x, _floorHeight + 0.004f, feet.z);
                emit.velocity = Vector3.zero;
                emit.startLifetime = 1.6f;
                emit.startSize = _rippleSize;
                emit.startColor = _groundedColor * Mathf.Clamp01(0.3f + charge * stability);
                _ripples.Emit(emit, 1);
            }

            _lastTiming = timing;
        }

        Vector3 Feet(Vector3 relic)
        {
            Camera eye = Camera.main;
            Vector3 head = eye != null ? eye.transform.position : relic + Vector3.back * 0.55f;
            return new Vector3(head.x, _floorHeight + 0.01f, head.z) + (new Vector3(relic.x, 0f, relic.z) - new Vector3(head.x, 0f, head.z)) * 0.25f;
        }

        ParticleSystem CreateSystem(string systemName, Material material, int capacity, ParticleSystemRenderMode mode)
        {
            GameObject host = new GameObject(systemName);
            host.transform.SetParent(transform, false);
            ParticleSystem system = host.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = capacity;
            main.startSpeed = 0f;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.6f, 0.7f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;

            ParticleSystemRenderer renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = mode;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (mode == ParticleSystemRenderMode.Stretch)
            {
                renderer.velocityScale = 0.04f;
                renderer.lengthScale = 1.2f;
            }

            system.Play();
            return system;
        }
    }
}
