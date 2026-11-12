using UnityEngine;

namespace KineticForge.Feedback
{
    /// <summary>
    /// Living flame for a hearth or brazier: soft additive tongues that rise, curl and cool from yellow-white through
    /// orange to deep red. Builds its own particle system on Awake.
    /// </summary>
    /// <remarks>
    /// Tuned for Quest: a few dozen particles at most, no shadows, no lighting. Pair it with a flickering point light
    /// (<see cref="ForgeFlicker"/>) and <see cref="ForgeEmbers"/> for the full fire.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Feedback/Forge Flame")]
    public sealed class ForgeFlame : MonoBehaviour
    {
        [SerializeField, Tooltip("Additive soft-disc material (Kinetic Forge/Forge Glow).")]
        Material _material = null;

        [SerializeField, Range(0.02f, 1f), Tooltip("Radius (m) of the fuel bed the flames rise from.")]
        float _radius = 0.14f;

        [SerializeField, Range(0.1f, 3f), Tooltip("Overall flame size; scales height, width and particle size.")]
        float _scale = 1f;

        [SerializeField, Range(1f, 80f), Tooltip("Flame tongues per second.")]
        float _rate = 26f;

        /// <summary>Overall flame size.</summary>
        public float Scale => _scale;

        void Awake()
        {
            if (!TryGetComponent(out ParticleSystem system))
            {
                system = gameObject.AddComponent<ParticleSystem>();
            }

            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            const float MaxLifetime = 0.85f;
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.CeilToInt(_rate * MaxLifetime * 1.4f) + 4;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, MaxLifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f * _scale, 0.8f * _scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f * _scale, 0.3f * _scale);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            main.gravityModifier = -0.05f;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = _rate;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 6f;
            shape.radius = _radius;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            // Tongues narrow and fade as they rise.
            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.55f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.15f));
            size.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            ParticleSystem.RotationOverLifetimeModule rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.32f * _scale;
            noise.frequency = 1.4f;
            noise.scrollSpeed = 0.9f;
            noise.damping = true;

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.93f, 0.72f), 0f),
                    new GradientColorKey(new Color(1f, 0.68f, 0.26f), 0.25f),
                    new GradientColorKey(new Color(1f, 0.38f, 0.1f), 0.6f),
                    new GradientColorKey(new Color(0.55f, 0.1f, 0.03f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.85f, 0.12f),
                    new GradientAlphaKey(0.55f, 0.6f),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = gradient;

            ParticleSystemRenderer renderer = GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingFudge = -2f;

            system.Play();
        }

        void OnValidate()
        {
            _radius = Mathf.Max(0.02f, _radius);
            _scale = Mathf.Max(0.1f, _scale);
            _rate = Mathf.Max(1f, _rate);
        }
    }
}
