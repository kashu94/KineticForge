using UnityEngine;

namespace KineticForge.Core
{
    /// <summary>
    /// Small, allocation-free math helpers shared by every module.
    /// </summary>
    /// <remarks>
    /// All smoothing here is the exact discretization of a first-order low-pass filter, so it produces the same curve at
    /// 72, 90 or 120 Hz and stays stable for any frame time, including long hitches.
    /// </remarks>
    public static class ForgeMath
    {
        /// <summary>
        /// Blend factor for frame-rate independent exponential smoothing: <c>1 - e^(-rate * deltaTime)</c>.
        /// </summary>
        /// <param name="rate">Convergence rate in 1/s. The time constant is <c>1 / rate</c>.</param>
        /// <param name="deltaTime">Elapsed seconds.</param>
        /// <returns>A factor in [0, 1]. 0 when either input is not positive.</returns>
        public static float ExpBlend(float rate, float deltaTime)
        {
            if (!(rate > 0f) || !(deltaTime > 0f))
            {
                return 0f;
            }

            return 1f - Mathf.Exp(-rate * deltaTime);
        }

        /// <summary>
        /// Blend factor for a first-order low-pass filter with the given cutoff frequency.
        /// </summary>
        /// <param name="cutoffHz">Cutoff frequency in Hz.</param>
        /// <param name="deltaTime">Elapsed seconds.</param>
        /// <returns>A factor in [0, 1].</returns>
        public static float LowPassBlend(float cutoffHz, float deltaTime)
        {
            return ExpBlend(2f * Mathf.PI * cutoffHz, deltaTime);
        }

        /// <summary>Moves <paramref name="current"/> toward <paramref name="target"/> with exponential smoothing.</summary>
        /// <param name="current">Current value.</param>
        /// <param name="target">Value to approach.</param>
        /// <param name="rate">Convergence rate in 1/s.</param>
        /// <param name="deltaTime">Elapsed seconds.</param>
        /// <returns>The smoothed value.</returns>
        public static float Damp(float current, float target, float rate, float deltaTime)
        {
            return current + (target - current) * ExpBlend(rate, deltaTime);
        }

        /// <summary>
        /// Like <see cref="Damp(float, float, float, float)"/>, but lands exactly on the target once within
        /// <paramref name="snapEpsilon"/>, so values such as 0 and 1 are reached exactly instead of only approached.
        /// </summary>
        /// <param name="current">Current value.</param>
        /// <param name="target">Value to approach.</param>
        /// <param name="rate">Convergence rate in 1/s.</param>
        /// <param name="deltaTime">Elapsed seconds.</param>
        /// <param name="snapEpsilon">Distance below which the result snaps to the target.</param>
        /// <returns>The smoothed value.</returns>
        public static float DampSnap(float current, float target, float rate, float deltaTime, float snapEpsilon)
        {
            float next = Damp(current, target, rate, deltaTime);
            return Mathf.Abs(next - target) < snapEpsilon ? target : next;
        }

        /// <summary>Vector form of <see cref="Damp(float, float, float, float)"/>.</summary>
        /// <param name="current">Current value.</param>
        /// <param name="target">Value to approach.</param>
        /// <param name="rate">Convergence rate in 1/s.</param>
        /// <param name="deltaTime">Elapsed seconds.</param>
        /// <returns>The smoothed value.</returns>
        public static Vector3 Damp(Vector3 current, Vector3 target, float rate, float deltaTime)
        {
            return current + (target - current) * ExpBlend(rate, deltaTime);
        }

        /// <summary>True when <paramref name="value"/> is neither NaN nor infinite.</summary>
        /// <param name="value">Value to test.</param>
        /// <returns>Whether the value is finite.</returns>
        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>True when every component of <paramref name="value"/> is finite.</summary>
        /// <param name="value">Vector to test.</param>
        /// <returns>Whether the vector is finite.</returns>
        public static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        /// <summary>Clamps to [0, 1] and maps NaN or infinity to <paramref name="fallback"/>.</summary>
        /// <param name="value">Value to sanitize.</param>
        /// <param name="fallback">Value returned for non-finite input.</param>
        /// <returns>A finite value in [0, 1].</returns>
        public static float Sanitize01(float value, float fallback = 0f)
        {
            return IsFinite(value) ? Mathf.Clamp01(value) : Mathf.Clamp01(fallback);
        }

        /// <summary>
        /// Returns the point on segment <paramref name="a"/>-<paramref name="b"/> closest to <paramref name="point"/>.
        /// A degenerate segment returns <paramref name="a"/>.
        /// </summary>
        /// <param name="point">Query point.</param>
        /// <param name="a">Segment start.</param>
        /// <param name="b">Segment end.</param>
        /// <returns>The closest point on the segment.</returns>
        public static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.x * ab.x + ab.y * ab.y;
            if (lengthSquared <= 1e-12f)
            {
                return a;
            }

            float t = Mathf.Clamp01(((point.x - a.x) * ab.x + (point.y - a.y) * ab.y) / lengthSquared);
            return new Vector2(a.x + ab.x * t, a.y + ab.y * t);
        }

        /// <summary>
        /// Hermite smoothstep: 0 at or below <paramref name="edge0"/>, 1 at or above <paramref name="edge1"/>, smooth in
        /// between. Same as HLSL <c>smoothstep</c>, unlike <see cref="Mathf.SmoothStep"/>, which interpolates.
        /// </summary>
        /// <param name="edge0">Lower edge.</param>
        /// <param name="edge1">Upper edge.</param>
        /// <param name="x">Input.</param>
        /// <returns>The smoothed value in [0, 1].</returns>
        public static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
