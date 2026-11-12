using UnityEngine;

namespace KineticForge.Core
{
    /// <summary>Letter grade of a forged relic, from best to worst.</summary>
    public enum ForgeGrade
    {
        /// <summary>Masterwork: near-perfect strikes, kept hot and in rhythm, no cracks.</summary>
        S = 0,

        /// <summary>Fine work.</summary>
        A = 1,

        /// <summary>Sound work.</summary>
        B = 2,

        /// <summary>Rough work.</summary>
        C = 3,

        /// <summary>The relic fractured.</summary>
        Fractured = 4,
    }

    /// <summary>
    /// The physics of Resonant Forging: vibration modes of a struck plate, the exact motion of a damped oscillator, the
    /// relic's heat, and the rules that turn them into carving quality, cracks and a grade. Pure functions; nothing
    /// allocates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Plate modes.</b> The carved face vibrates in shapes <c>φ(u, v) = cos(mπu)cos(nπv) + s·cos(nπu)cos(mπv)</c> on
    /// the unit square, the classic approximation for a free square plate. Their zero lines are Chladni figures, where
    /// sand gathers on a bowed plate. A strike at face point p excites mode k in proportion to φ_k(p), so a strike on a
    /// node leaves that mode silent, exactly as on a real plate.
    /// </para>
    /// <para>
    /// <b>Pulse.</b> The relic's low "pulse" mode is a lightly damped oscillator stored as the phasor
    /// <c>z = x − i·v/ω = A·e^{iθ}</c>. Advancing it is an exact rotation and decay, so it never drifts at any frame rate.
    /// A strike is an impulse that adds to velocity: it pumps energy in when it lands with the motion, at θ = −π/2, and
    /// drains it against the motion. This is pushing a swing, and it is what makes strike rhythm matter.
    /// </para>
    /// <para>
    /// <b>Heat.</b> Heat is normalized: 1 fresh from the furnace, 0 at room temperature. It cools by Newton's law, solved
    /// exactly. Vibration energy dissipated by damping turns back into heat, so steady in-rhythm strikes keep the metal
    /// workable. Hot metal is soft: it carves deep and forgives. Cold metal is brittle.
    /// </para>
    /// </remarks>
    public static class ResonanceMath
    {
        /// <summary>Number of plate modes shown on the face and used to shape the sound.</summary>
        public const int PlateModeCount = 4;

        /// <summary>Pulse amplitude below which the pulse counts as at rest and strike timing as neutral.</summary>
        public const float RestAmplitude = 0.06f;

        // Mode indices m, n and symmetry sign s. Shared with the Forge Metal shader through material properties.
        static readonly Vector4 s_modeM = new Vector4(1f, 1f, 2f, 1f);
        static readonly Vector4 s_modeN = new Vector4(2f, 3f, 3f, 4f);
        static readonly Vector4 s_modeSign = new Vector4(-1f, 1f, -1f, 1f);

        /// <summary>First index m of each plate mode.</summary>
        public static Vector4 ModeM => s_modeM;

        /// <summary>Second index n of each plate mode.</summary>
        public static Vector4 ModeN => s_modeN;

        /// <summary>Symmetry sign s of each plate mode: +1 or −1.</summary>
        public static Vector4 ModeSign => s_modeSign;

        #region Plate modes

        /// <summary>Shape of one plate mode at a point.</summary>
        /// <param name="m">First mode index.</param>
        /// <param name="n">Second mode index.</param>
        /// <param name="sign">Symmetry sign, +1 or −1.</param>
        /// <param name="u">Horizontal face position in [0, 1].</param>
        /// <param name="v">Vertical face position in [0, 1].</param>
        /// <returns>The mode shape, in [−2, 2].</returns>
        public static float PlateMode(float m, float n, float sign, float u, float v)
        {
            return Mathf.Cos(m * Mathf.PI * u) * Mathf.Cos(n * Mathf.PI * v) +
                   sign * Mathf.Cos(n * Mathf.PI * u) * Mathf.Cos(m * Mathf.PI * v);
        }

        /// <summary>Shape of the relic's plate mode <paramref name="index"/> at a face point.</summary>
        /// <param name="index">Mode index in [0, <see cref="PlateModeCount"/>).</param>
        /// <param name="uv">Face position, both axes in [0, 1].</param>
        /// <returns>The mode shape, in [−2, 2].</returns>
        public static float PlateMode(int index, Vector2 uv)
        {
            return PlateMode(s_modeM[index], s_modeN[index], s_modeSign[index], uv.x, uv.y);
        }

        /// <summary>
        /// How strongly a strike at <paramref name="uv"/> excites each plate mode, in [0, 1] per mode. A strike on a
        /// node line excites that mode not at all.
        /// </summary>
        /// <param name="uv">Face position, both axes in [0, 1].</param>
        /// <returns>Excitation per mode.</returns>
        public static Vector4 PlateExcitation(Vector2 uv)
        {
            return new Vector4(
                Mathf.Abs(PlateMode(0, uv)) * 0.5f,
                Mathf.Abs(PlateMode(1, uv)) * 0.5f,
                Mathf.Abs(PlateMode(2, uv)) * 0.5f,
                Mathf.Abs(PlateMode(3, uv)) * 0.5f);
        }

        #endregion

        #region Pulse oscillator

        /// <summary>
        /// Advances a damped oscillator phasor exactly by <paramref name="deltaTime"/>: it turns by the damped angular
        /// frequency and shrinks by <c>e^{−ζω·dt}</c>.
        /// </summary>
        /// <param name="phasor">Phasor (x, −v/ω) as a 2D vector.</param>
        /// <param name="frequency">Natural frequency, in hertz.</param>
        /// <param name="dampingRatio">Damping ratio ζ in [0, 1).</param>
        /// <param name="deltaTime">Time step, in seconds.</param>
        /// <returns>The advanced phasor.</returns>
        public static Vector2 StepOscillator(Vector2 phasor, float frequency, float dampingRatio, float deltaTime)
        {
            if (!(deltaTime > 0f) || !(frequency > 0f))
            {
                return phasor;
            }

            float zeta = Mathf.Clamp(dampingRatio, 0f, 0.99f);
            float omega = 2f * Mathf.PI * frequency;
            float omegaDamped = omega * Mathf.Sqrt(1f - zeta * zeta);
            float decay = Mathf.Exp(-zeta * omega * deltaTime);
            float angle = omegaDamped * deltaTime;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);
            return new Vector2(
                (phasor.x * cos - phasor.y * sin) * decay,
                (phasor.x * sin + phasor.y * cos) * decay);
        }

        /// <summary>
        /// Strike timing against the pulse, in [−1, 1]: +1 lands exactly with the motion and pumps the most energy in,
        /// −1 lands against it and drains the most. 0 while the pulse is at rest.
        /// </summary>
        /// <param name="phasor">Pulse phasor.</param>
        /// <returns>The timing.</returns>
        public static float StrikeTiming(Vector2 phasor)
        {
            float amplitude = phasor.magnitude;
            if (amplitude < RestAmplitude)
            {
                return 0f;
            }

            // The kick adds along −y, so it grows the phasor most where the phasor points along −y (θ = −π/2).
            return -phasor.y / amplitude;
        }

        /// <summary>Applies a strike impulse of size <paramref name="kick"/> to the pulse phasor.</summary>
        /// <param name="phasor">Pulse phasor.</param>
        /// <param name="kick">Impulse in amplitude units; a full in-phase strike adds this much amplitude.</param>
        /// <returns>The phasor after the strike.</returns>
        public static Vector2 Kick(Vector2 phasor, float kick)
        {
            if (phasor.magnitude < RestAmplitude)
            {
                // From rest, any strike starts the pulse, and the motion it starts is by definition in phase.
                return new Vector2(0f, -Mathf.Max(kick, phasor.magnitude));
            }

            return new Vector2(phasor.x, phasor.y - kick);
        }

        /// <summary>
        /// Fraction of a pulse cycle, in [0, 1), until the next ideal strike moment. 0 means strike now.
        /// </summary>
        /// <param name="phasor">Pulse phasor.</param>
        /// <returns>The remaining fraction of the cycle.</returns>
        public static float CycleToIdealStrike(Vector2 phasor)
        {
            // Phase advances counterclockwise; the ideal phase is −π/2 (equivalently 3π/2).
            float theta = Mathf.Atan2(phasor.y, phasor.x);
            float remaining = (1.5f * Mathf.PI - theta) / (2f * Mathf.PI);
            remaining -= Mathf.Floor(remaining);
            return remaining;
        }

        /// <summary>Energy dissipated per second by a pulse of amplitude A, in amplitude² per second: 2ζω·A².</summary>
        /// <param name="amplitude">Pulse amplitude.</param>
        /// <param name="frequency">Natural frequency, in hertz.</param>
        /// <param name="dampingRatio">Damping ratio ζ.</param>
        /// <returns>The dissipated power.</returns>
        public static float DissipatedPower(float amplitude, float frequency, float dampingRatio)
        {
            return 2f * Mathf.Max(0f, dampingRatio) * 2f * Mathf.PI * Mathf.Max(0f, frequency) * amplitude * amplitude;
        }

        #endregion

        #region Heat

        /// <summary>Cools heat toward 0 by Newton's law, solved exactly over <paramref name="deltaTime"/>.</summary>
        /// <param name="heat">Normalized heat.</param>
        /// <param name="timeConstant">Cooling time constant, in seconds.</param>
        /// <param name="deltaTime">Time step, in seconds.</param>
        /// <returns>The cooled heat.</returns>
        public static float Cool(float heat, float timeConstant, float deltaTime)
        {
            if (!(timeConstant > 0f) || !(deltaTime > 0f))
            {
                return heat;
            }

            return heat * Mathf.Exp(-deltaTime / timeConstant);
        }

        /// <summary>How soft, and so workable, the metal is at a heat, in [0, 1].</summary>
        /// <param name="heat">Normalized heat.</param>
        /// <returns>The softness.</returns>
        public static float Softness(float heat)
        {
            return ForgeMath.SmoothStep(0.15f, 0.7f, heat);
        }

        /// <summary>
        /// Pulse frequency at a heat: hot metal is softer and rings slower. The tempo the player must follow rises as
        /// the relic cools.
        /// </summary>
        /// <param name="heat">Normalized heat.</param>
        /// <param name="coldFrequency">Frequency at room temperature, in hertz.</param>
        /// <param name="hotFrequency">Frequency fresh from the furnace, in hertz.</param>
        /// <returns>The frequency, in hertz.</returns>
        public static float PulseFrequency(float heat, float coldFrequency, float hotFrequency)
        {
            return Mathf.Lerp(coldFrequency, hotFrequency, Mathf.Clamp01(heat));
        }

        #endregion

        #region Quality, cracks and grade

        /// <summary>
        /// Quality of a carved mark in [0, 1]: aim-and-stance precision, times how workable the metal is, times how well
        /// the strike rode the pulse, with a bonus for resonance charge.
        /// </summary>
        /// <param name="precision">Strike precision in [0, 1].</param>
        /// <param name="softness">Metal softness in [0, 1].</param>
        /// <param name="timing">Strike timing in [−1, 1].</param>
        /// <param name="charge">Pulse amplitude before the strike.</param>
        /// <returns>The quality.</returns>
        public static float CarveQuality(float precision, float softness, float timing, float charge)
        {
            float quality = Mathf.Clamp01(precision) *
                            Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(softness)) *
                            Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(timing));
            return Mathf.Clamp01(quality * (1f + 0.35f * Mathf.Clamp(charge, 0f, 1.5f)));
        }

        /// <summary>
        /// Stress a strike puts on the relic. Above 1 it cracks. Charge stored in the relic has to go somewhere: a braced
        /// body drains it into the ground, an unbraced one lets it tear the metal, and cold metal tears easily.
        /// </summary>
        /// <param name="charge">Pulse amplitude after the strike.</param>
        /// <param name="stability">Stance stability in [0, 1].</param>
        /// <param name="softness">Metal softness in [0, 1].</param>
        /// <returns>The stress.</returns>
        public static float CrackStress(float charge, float stability, float softness)
        {
            return (0.5f + Mathf.Max(0f, charge)) * (1f - Mathf.Clamp01(stability)) * (1.5f - Mathf.Clamp01(softness));
        }

        /// <summary>Grade of a forged relic.</summary>
        /// <param name="meanQuality">Mean carve quality in [0, 1].</param>
        /// <param name="integrity">Remaining integrity in [0, 1]; 0 means fractured.</param>
        /// <returns>The grade.</returns>
        public static ForgeGrade Grade(float meanQuality, float integrity)
        {
            if (!(integrity > 0f))
            {
                return ForgeGrade.Fractured;
            }

            float score = Mathf.Clamp01(meanQuality) * Mathf.Lerp(0.75f, 1f, Mathf.Clamp01(integrity));
            return score >= 0.85f ? ForgeGrade.S : score >= 0.7f ? ForgeGrade.A : score >= 0.5f ? ForgeGrade.B : ForgeGrade.C;
        }

        #endregion

        #region Color

        /// <summary>
        /// Incandescent glow color of metal at a heat, approximating black-body color: dull red, cherry, orange, yellow-orange.
        /// Black below a dull red threshold. Not premultiplied by intensity.
        /// </summary>
        /// <param name="heat">Normalized heat.</param>
        /// <returns>The glow color, linear.</returns>
        public static Color Incandescence(float heat)
        {
            float t = Mathf.Clamp01(heat);
            float r = Mathf.Clamp01(t * 1.7f);
            float g = Mathf.Clamp01(t * 1.7f - 0.6f) * 0.8f;
            float b = Mathf.Clamp01(t * 1.7f - 1.25f) * 0.5f;
            float visible = ForgeMath.SmoothStep(0.2f, 0.55f, t);
            return new Color(r * r * visible, g * g * visible, b * b * visible, 1f);
        }

        #endregion
    }
}
