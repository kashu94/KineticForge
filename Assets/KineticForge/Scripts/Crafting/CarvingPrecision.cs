using UnityEngine;

namespace KineticForge.Crafting
{
    /// <summary>
    /// The carving precision model, where both scales of Dual-Scale Input meet: micro aim times macro stance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Aim</b> is 1 inside the inner fifth of the strike tolerance and falls smoothly to 0 at the tolerance edge.
    /// <b>Stance</b> scales it from <c>minimumStanceFactor</c> when unbraced to 1 when fully braced.
    /// </para>
    /// <para>
    /// With the default 0.4 stance floor and the game's 0.35 qualifying bar, a perfect strike from an unbraced stance
    /// barely qualifies, and the cursor tremor of an unbraced stance makes perfect aim unlikely. Bracing is what makes
    /// carving reliable.
    /// </para>
    /// </remarks>
    public static class CarvingPrecision
    {
        /// <summary>Share of the tolerance radius that counts as dead center.</summary>
        public const float CenterShare = 0.2f;

        /// <summary>Aim quality in [0, 1] for a strike <paramref name="distance"/> meters from its mark.</summary>
        /// <param name="distance">Distance from the strike point to the mark, in meters.</param>
        /// <param name="tolerance">Distance at which aim quality reaches 0.</param>
        /// <returns>The aim quality.</returns>
        public static float Aim(float distance, float tolerance)
        {
            if (!(tolerance > 0f) || float.IsNaN(distance))
            {
                return 0f;
            }

            float t = Mathf.InverseLerp(tolerance * CenterShare, tolerance, distance);
            return 1f - t * t * (3f - 2f * t);
        }

        /// <summary>Stance multiplier in [minimumStanceFactor, 1].</summary>
        /// <param name="stability">Stance stability in [0, 1].</param>
        /// <param name="minimumStanceFactor">Multiplier at zero stability.</param>
        /// <returns>The stance multiplier.</returns>
        public static float Stance(float stability, float minimumStanceFactor)
        {
            float floor = Mathf.Clamp01(minimumStanceFactor);
            return Mathf.Lerp(floor, 1f, float.IsNaN(stability) ? 0f : Mathf.Clamp01(stability));
        }

        /// <summary>Strike precision in [0, 1]: aim quality times stance multiplier.</summary>
        /// <param name="distance">Distance from the strike point to the mark, in meters.</param>
        /// <param name="tolerance">Distance at which aim quality reaches 0.</param>
        /// <param name="stability">Stance stability in [0, 1].</param>
        /// <param name="minimumStanceFactor">Stance multiplier at zero stability.</param>
        /// <returns>The precision.</returns>
        public static float Evaluate(float distance, float tolerance, float stability, float minimumStanceFactor)
        {
            return Aim(distance, tolerance) * Stance(stability, minimumStanceFactor);
        }
    }
}
