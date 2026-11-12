using UnityEngine;

namespace KineticForge.Feedback
{
    /// <summary>
    /// A gentle stream of glowing embers rising from a fire: the forge's ambient life. Builds its own particle system on
    /// Awake; place it at the mouth of a furnace or brazier.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Feedback/Forge Embers")]
    public sealed class ForgeEmbers : MonoBehaviour
    {
        [SerializeField, Tooltip("Additive material for embers (Kinetic Forge/Forge Glow).")]
        Material _material = null;

        [SerializeField, Range(0f, 100f), Tooltip("Embers per second.")]
        float _rate = 18f;

        [SerializeField, Range(0.01f, 2f), Tooltip("Radius (m) of the area embers rise from.")]
        float _radius = 0.25f;

        [SerializeField, Range(0.5f, 12f), Tooltip("Ember lifetime (s).")]
        float _lifetime = 5f;

        void Awake()
        {
            if (!TryGetComponent(out ParticleSystem system))
            {
                system = gameObject.AddComponent<ParticleSystem>();
            }

            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.CeilToInt(_rate * _lifetime * 1.5f) + 4;
            main.startLifetime = new ParticleSystem.MinMaxCurve(_lifetime * 0.6f, _lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.45f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.016f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.15f), new Color(1f, 0.8f, 0.4f));
            main.gravityModifier = -0.015f;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = _rate;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = _radius;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.6f;
            noise.scrollSpeed = 0.3f;

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.4f, 0.12f), 0.6f), new GradientColorKey(new Color(0.6f, 0.1f, 0.03f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;

            ParticleSystemRenderer renderer = GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            system.Play();
        }
    }
}
