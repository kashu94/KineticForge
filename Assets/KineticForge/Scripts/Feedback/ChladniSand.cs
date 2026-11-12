using System;
using KineticForge.Core;
using KineticForge.Crafting;
using UnityEngine;

namespace KineticForge.Feedback
{
    /// <summary>
    /// Glowing filings on the relic's face that behave like sand on a bowed Chladni plate: wherever the face moves they
    /// are bounced away, and they drift down the vibration's gradient until they settle on the nodal lines, drawing the
    /// figure the relic rings. Each strike rearranges them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every grain lives in face UV. Its drift is <c>−F·∇F</c> of the ringing field <c>F</c>
    /// (<see cref="CymaticMath.FieldWithGradient"/>), normalized by the field's energy, plus a random hop proportional to
    /// how hard its spot shakes. Grains at rest on a node stay put; a silent relic lets them lie still. Once the sigil is
    /// set, grains on its lines glow gold.
    /// </para>
    /// <para>
    /// Grains render through a local-space particle system parented to the relic, updated with
    /// <see cref="ParticleSystem.SetParticles(ParticleSystem.Particle[], int)"/> from a cached array. The simulation uses
    /// a per-grain table of cosines and sines, about twenty trigonometric calls per grain per frame, and does not allocate.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(RelicResonance), typeof(RelicCarvingSurface))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Feedback/Chladni Sand")]
    public sealed class ChladniSand : MonoBehaviour
    {
        const int Harmonics = 6;

        [SerializeField, Tooltip("Additive grain material (Kinetic Forge/Forge Glow).")]
        Material _material = null;

        [SerializeField, Range(100, 3000), Tooltip("Number of grains.")]
        int _grainCount = 900;

        [SerializeField, Range(0.0005f, 0.006f), Tooltip("Grain size (m).")]
        float _grainSize = 0.0022f;

        [SerializeField, Range(0.01f, 2f), Tooltip("Drift speed toward the nodal lines (face widths per second at full energy).")]
        float _drift = 0.35f;

        [SerializeField, Range(0f, 0.5f), Tooltip("Random hop of grains on shaking spots (face widths per second).")]
        float _hop = 0.12f;

        [SerializeField, ColorUsage(false, true), Tooltip("Grain color.")]
        Color _color = new Color(0.55f, 0.45f, 0.3f);

        [SerializeField, ColorUsage(false, true), Tooltip("Color of grains on the set sigil's lines.")]
        Color _setColor = new Color(1.3f, 0.95f, 0.45f);

        RelicResonance _resonance;
        RelicCarvingSurface _surface;
        RelicSigil _sigil;
        RelicForgeRenderer _renderer;
        ParticleSystem _system;
        ParticleSystem.Particle[] _particles;
        Vector2[] _grains;
        readonly float[] _cu = new float[Harmonics];
        readonly float[] _su = new float[Harmonics];
        readonly float[] _cv = new float[Harmonics];
        readonly float[] _sv = new float[Harmonics];
        uint _random = 0x2545F491u;
        Action<GamePhaseChangedEvent> _onPhaseChanged;

        /// <summary>Number of grains.</summary>
        public int GrainCount => _grains != null ? _grains.Length : 0;

        /// <summary>Face UV of grain <paramref name="index"/>.</summary>
        /// <param name="index">Grain index.</param>
        /// <returns>The grain's face UV.</returns>
        public Vector2 GetGrain(int index)
        {
            return _grains[index];
        }

        /// <summary>Spreads the grains evenly over the face again.</summary>
        public void Scatter()
        {
            if (_grains == null)
            {
                return;
            }

            for (int i = 0; i < _grains.Length; i++)
            {
                _grains[i] = new Vector2(0.04f + 0.92f * NextUnit(), 0.04f + 0.92f * NextUnit());
            }
        }

        void Awake()
        {
            _resonance = GetComponent<RelicResonance>();
            _surface = GetComponent<RelicCarvingSurface>();
            _sigil = GetComponent<RelicSigil>();
            _renderer = GetComponent<RelicForgeRenderer>();
            _onPhaseChanged = HandlePhaseChanged;
            _grains = new Vector2[Mathf.Clamp(_grainCount, 100, 3000)];
            _particles = new ParticleSystem.Particle[_grains.Length];
            Scatter();
            CreateSystem();
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
            if (payload.Current == ForgePhase.FirstRelicForging)
            {
                Scatter();
            }
        }

        void LateUpdate()
        {
            // No sand on a relic that is still being poured.
            if (_renderer != null && _renderer.Fill < 0.999f)
            {
                _system.SetParticles(_particles, 0);
                return;
            }

            float deltaTime = Mathf.Min(Time.deltaTime, 0.05f);
            Vector4 amplitudes = _resonance.PlateAmplitudes;
            float energy = Mathf.Abs(amplitudes.x) + Mathf.Abs(amplitudes.y) + Mathf.Abs(amplitudes.z) + Mathf.Abs(amplitudes.w);
            float activity = Mathf.Clamp01(energy);
            bool set = _sigil != null && _sigil.Stage == SigilStage.Set;

            _surface.GetLocalFace(out Vector3 center, out Vector3 right, out Vector3 up, out Vector3 normal, out Vector2 size);
            Vector3 lift = normal * 0.0018f;
            Color32 baseColor = _color;
            Color32 setColor = _setColor;

            for (int i = 0; i < _grains.Length; i++)
            {
                Vector2 grain = _grains[i];
                float relative = 0f;
                if (activity > 1e-3f)
                {
                    Tabulate(grain);
                    float field = CymaticMath.FieldWithGradient(amplitudes, _cu, _su, _cv, _sv, out Vector2 gradient);
                    relative = field / energy;
                    Vector2 drift = -relative * (gradient / energy) * (_drift * 0.25f);
                    Vector2 hop = new Vector2(NextUnit() - 0.5f, NextUnit() - 0.5f) * (2f * _hop * Mathf.Abs(relative));
                    grain += (drift + hop) * (activity * deltaTime);
                    grain.x = Mathf.Clamp(grain.x, 0.02f, 0.98f);
                    grain.y = Mathf.Clamp(grain.y, 0.02f, 0.98f);
                    _grains[i] = grain;
                }

                ParticleSystem.Particle particle = _particles[i];
                particle.position = center + right * ((grain.x - 0.5f) * size.x) + up * ((grain.y - 0.5f) * size.y) + lift;
                particle.startSize = _grainSize;
                particle.startLifetime = 1000f;
                particle.remainingLifetime = 1000f;
                particle.startColor = set && Mathf.Abs(relative) < 0.06f ? setColor : baseColor;
                _particles[i] = particle;
            }

            _system.SetParticles(_particles, _particles.Length);
        }

        void Tabulate(Vector2 uv)
        {
            for (int k = 0; k < Harmonics; k++)
            {
                float au = k * Mathf.PI * uv.x;
                float av = k * Mathf.PI * uv.y;
                _cu[k] = Mathf.Cos(au);
                _su[k] = Mathf.Sin(au);
                _cv[k] = Mathf.Cos(av);
                _sv[k] = Mathf.Sin(av);
            }
        }

        float NextUnit()
        {
            uint x = _random;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _random = x;
            return (x & 0xFFFFFF) / 16777216f;
        }

        void CreateSystem()
        {
            GameObject host = new GameObject("Chladni Sand");
            host.transform.SetParent(transform, false);
            _system = host.AddComponent<ParticleSystem>();
            _system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = _system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = _grains.Length;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;

            ParticleSystem.EmissionModule emission = _system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = _system.shape;
            shape.enabled = false;

            ParticleSystemRenderer renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.minParticleSize = 0f;
            _system.Play();
        }
    }
}
