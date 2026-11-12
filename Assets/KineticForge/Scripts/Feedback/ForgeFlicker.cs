using KineticForge.Core;
using KineticForge.Crafting;
using UnityEngine;

namespace KineticForge.Feedback
{
    /// <summary>
    /// Drives a light like fire: a living flicker from layered noise, or, with a relic as heat source, the relic's own
    /// incandescent glow that swells with its resonance pulse.
    /// </summary>
    [RequireComponent(typeof(Light))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Feedback/Forge Flicker")]
    public sealed class ForgeFlicker : MonoBehaviour
    {
        [SerializeField, Min(0f), Tooltip("Intensity at full flame or full heat.")]
        float _intensity = 2f;

        [SerializeField, Range(0f, 1f), Tooltip("Flicker depth as a share of the intensity.")]
        float _flicker = 0.25f;

        [SerializeField, Range(0.1f, 30f), Tooltip("Flicker speed.")]
        float _speed = 7f;

        [SerializeField, Tooltip("Relic whose heat and pulse drive the light instead of fire noise. Optional.")]
        RelicResonance _heatSource = null;

        Light _light;
        float _seed;

        void Awake()
        {
            _light = GetComponent<Light>();
            _seed = Random.Range(0f, 100f);
        }

        void Update()
        {
            float t = Time.time * _speed;
            float noise = 0.65f * Mathf.PerlinNoise(t, _seed) + 0.35f * Mathf.PerlinNoise(t * 2.7f, _seed + 31f);
            float flicker = 1f + _flicker * (noise - 0.5f) * 2f;

            if (_heatSource == null)
            {
                _light.intensity = _intensity * flicker;
                return;
            }

            Color glow = ResonanceMath.Incandescence(_heatSource.Heat);
            float brightness = Mathf.Max(glow.r, Mathf.Max(glow.g, glow.b));
            float pulse = _heatSource.IsPulsing ? 0.5f + 0.5f * _heatSource.TimingNow : 0f;
            float resonance = 0.35f * Mathf.Clamp01(_heatSource.Charge) * pulse;
            _light.intensity = _intensity * (brightness * Mathf.Lerp(1f, flicker, 0.4f) + resonance);
            if (brightness > 1e-3f)
            {
                _light.color = new Color(glow.r / brightness, glow.g / brightness, glow.b / brightness);
            }
        }
    }
}
