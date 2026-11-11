using System;
using UnityEngine;

namespace KineticForge.Core
{
    /// <summary>
    /// Tuning for <see cref="OneEuroFilter"/> and <see cref="OneEuroFilter3"/>.
    /// </summary>
    /// <remarks>
    /// Tune <see cref="minCutoff"/> first with the signal held still until jitter is gone. Then raise <see cref="beta"/>
    /// while moving quickly until lag is acceptable. See Casiez, Roussel and Vogel, "1€ Filter", CHI 2012.
    /// </remarks>
    [Serializable]
    public struct OneEuroSettings
    {
        /// <summary>Smallest allowed cutoff, used by the other properties.</summary>
        public const float MinimumCutoff = 0.01f;

        /// <summary>Cutoff in Hz while the signal is still. Lower removes more jitter and adds more lag.</summary>
        [Min(MinimumCutoff), Tooltip("Cutoff (Hz) while the signal is still. Lower removes more jitter but adds lag.")]
        public float minCutoff;

        /// <summary>Cutoff increase in Hz per unit/s of speed. Higher reduces lag during fast motion.</summary>
        [Min(0f), Tooltip("Cutoff increase (Hz) per unit/s of speed. Higher reduces lag during fast motion.")]
        public float beta;

        /// <summary>Cutoff in Hz for the speed estimate that drives the adaptive cutoff.</summary>
        [Min(MinimumCutoff), Tooltip("Cutoff (Hz) of the speed estimate that drives the adaptive cutoff.")]
        public float derivativeCutoff;

        /// <summary>Creates filter settings.</summary>
        /// <param name="minCutoff">Cutoff in Hz while still.</param>
        /// <param name="beta">Cutoff increase in Hz per unit/s of speed.</param>
        /// <param name="derivativeCutoff">Cutoff in Hz of the speed estimate.</param>
        public OneEuroSettings(float minCutoff, float beta, float derivativeCutoff)
        {
            this.minCutoff = minCutoff;
            this.beta = beta;
            this.derivativeCutoff = derivativeCutoff;
        }

        /// <summary>Defaults for body joints in metres: still joints lose millimetre jitter, moving joints keep up.</summary>
        public static OneEuroSettings BodyJoints => new OneEuroSettings(1.5f, 2.5f, 1f);

        /// <summary>Returns a copy with every value clamped into its valid range. NaN becomes the minimum.</summary>
        /// <returns>Sanitized settings.</returns>
        public OneEuroSettings Sanitized()
        {
            return new OneEuroSettings(
                ForgeMath.IsFinite(minCutoff) ? Mathf.Max(MinimumCutoff, minCutoff) : MinimumCutoff,
                ForgeMath.IsFinite(beta) ? Mathf.Max(0f, beta) : 0f,
                ForgeMath.IsFinite(derivativeCutoff) ? Mathf.Max(MinimumCutoff, derivativeCutoff) : MinimumCutoff);
        }
    }

    /// <summary>
    /// Allocation-free 1€ filter for a scalar signal: an adaptive low-pass filter that removes jitter when the signal is
    /// slow and reduces lag when it is fast.
    /// </summary>
    /// <remarks>
    /// The smoothing factors use the exact exponential form, so results do not depend on frame rate. The speed estimate
    /// differentiates the raw signal, as in the authors' reference implementation. This is a mutable struct: store it in a
    /// field and call methods on that field, never on a copy.
    /// </remarks>
    public struct OneEuroFilter
    {
        float _value;
        float _rawPrevious;
        float _derivative;
        bool _initialized;

        /// <summary>True once the filter has received a sample since the last reset.</summary>
        public bool IsInitialized => _initialized;

        /// <summary>Latest filtered value.</summary>
        public float Value => _value;

        /// <summary>Low-passed rate of change of the raw signal, in units per second.</summary>
        public float Derivative => _derivative;

        /// <summary>Forgets all history. The next sample passes through unfiltered.</summary>
        public void Reset()
        {
            _initialized = false;
            _derivative = 0f;
        }

        /// <summary>Restarts the filter at <paramref name="value"/> with zero speed.</summary>
        /// <param name="value">Value to restart from.</param>
        public void Reset(float value)
        {
            _value = value;
            _rawPrevious = value;
            _derivative = 0f;
            _initialized = true;
        }

        /// <summary>Filters one sample.</summary>
        /// <param name="raw">Raw sample.</param>
        /// <param name="deltaTime">Seconds since the previous sample. Non-positive values return the previous output.</param>
        /// <param name="settings">Filter tuning. Pass sanitized settings.</param>
        /// <returns>The filtered value.</returns>
        public float Filter(float raw, float deltaTime, in OneEuroSettings settings)
        {
            if (!_initialized)
            {
                Reset(raw);
                return _value;
            }

            if (!(deltaTime > 0f))
            {
                return _value;
            }

            float rawDerivative = (raw - _rawPrevious) / deltaTime;
            _derivative += (rawDerivative - _derivative) * ForgeMath.LowPassBlend(settings.derivativeCutoff, deltaTime);

            float cutoff = settings.minCutoff + settings.beta * Mathf.Abs(_derivative);
            _value += (raw - _value) * ForgeMath.LowPassBlend(cutoff, deltaTime);
            _rawPrevious = raw;
            return _value;
        }
    }

    /// <summary>
    /// Allocation-free 1€ filter for a <see cref="Vector3"/> signal such as a tracked joint position. The adaptive cutoff
    /// follows the magnitude of the filtered velocity, so all three axes share one cutoff and the path keeps its shape.
    /// </summary>
    /// <remarks>
    /// This is a mutable struct: store it in a field and call methods on that field, never on a copy.
    /// </remarks>
    public struct OneEuroFilter3
    {
        Vector3 _value;
        Vector3 _rawPrevious;
        Vector3 _derivative;
        bool _initialized;

        /// <summary>True once the filter has received a sample since the last reset.</summary>
        public bool IsInitialized => _initialized;

        /// <summary>Latest filtered value.</summary>
        public Vector3 Value => _value;

        /// <summary>Low-passed velocity of the raw signal, in units per second.</summary>
        public Vector3 Derivative => _derivative;

        /// <summary>Forgets all history. The next sample passes through unfiltered.</summary>
        public void Reset()
        {
            _initialized = false;
            _derivative = Vector3.zero;
        }

        /// <summary>Restarts the filter at <paramref name="value"/> with zero velocity.</summary>
        /// <param name="value">Value to restart from.</param>
        public void Reset(Vector3 value)
        {
            _value = value;
            _rawPrevious = value;
            _derivative = Vector3.zero;
            _initialized = true;
        }

        /// <summary>Filters one sample.</summary>
        /// <param name="raw">Raw sample.</param>
        /// <param name="deltaTime">Seconds since the previous sample. Non-positive values return the previous output.</param>
        /// <param name="settings">Filter tuning. Pass sanitized settings.</param>
        /// <returns>The filtered value.</returns>
        public Vector3 Filter(Vector3 raw, float deltaTime, in OneEuroSettings settings)
        {
            if (!_initialized)
            {
                Reset(raw);
                return _value;
            }

            if (!(deltaTime > 0f))
            {
                return _value;
            }

            Vector3 rawDerivative = (raw - _rawPrevious) / deltaTime;
            _derivative += (rawDerivative - _derivative) * ForgeMath.LowPassBlend(settings.derivativeCutoff, deltaTime);

            float cutoff = settings.minCutoff + settings.beta * _derivative.magnitude;
            _value += (raw - _value) * ForgeMath.LowPassBlend(cutoff, deltaTime);
            _rawPrevious = raw;
            return _value;
        }
    }
}
