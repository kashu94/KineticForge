using UnityEngine;

namespace KineticForge.Feedback
{
    /// <summary>
    /// Renders relic notes ahead of time with the same modal model as <see cref="RelicSynth"/>: bell partials ringing
    /// down as damped sinusoids. Used where many relics ring at once, such as the Resonance Wall, so playback is a
    /// plain clip.
    /// </summary>
    public static class ModalNoteBank
    {
        /// <summary>Renders one struck note into a new mono clip. Allocates; call at load time.</summary>
        /// <param name="name">Clip name.</param>
        /// <param name="fundamental">Prime partial frequency, in hertz.</param>
        /// <param name="seconds">Clip length, in seconds.</param>
        /// <param name="detuneCents">Detune of odd partials, in cents.</param>
        /// <param name="sampleRate">Output sample rate.</param>
        /// <returns>The clip.</returns>
        public static AudioClip Render(string name, float fundamental, float seconds, float detuneCents, int sampleRate)
        {
            int length = Mathf.Max(1, Mathf.RoundToInt(seconds * sampleRate));
            float[] samples = new float[length];
            float detune = Mathf.Pow(2f, detuneCents / 1200f);
            float peak = 1e-6f;
            for (int k = 0; k < RelicSynth.s_ratios.Length; k++)
            {
                float ratio = (k & 1) == 1 ? RelicSynth.s_ratios[k] * detune : RelicSynth.s_ratios[k];
                float omega = 2f * Mathf.PI * fundamental * ratio / sampleRate;
                float decay = Mathf.Exp(-1f / (RelicSynth.s_decays[k] * sampleRate));
                float gain = RelicSynth.s_gains[k];
                float rotCos = Mathf.Cos(omega);
                float rotSin = Mathf.Sin(omega);
                float re = gain;
                float im = 0f;
                for (int i = 0; i < length; i++)
                {
                    float r = re;
                    re = (r * rotCos - im * rotSin) * decay;
                    im = (r * rotSin + im * rotCos) * decay;
                    samples[i] += im;
                }
            }

            for (int i = 0; i < length; i++)
            {
                peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            }

            // Normalize, and fade the last 50 ms so the clip never clicks.
            int fade = Mathf.Min(length, sampleRate / 20);
            for (int i = 0; i < length; i++)
            {
                float envelope = i >= length - fade ? (length - i) / (float)fade : 1f;
                samples[i] = samples[i] / peak * 0.8f * envelope;
            }

            AudioClip clip = AudioClip.Create(name, length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
