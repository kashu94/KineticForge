using UnityEngine;

namespace KineticForge.Core
{
    /// <summary>
    /// Detects a fingertip tapping a surface: a fingertip approaching the face fast enough, crossing into contact from the
    /// front while inside the face. It re-arms only once the finger has drawn back, so a resting or hovering finger never
    /// strikes twice. Pure and allocation free.
    /// </summary>
    /// <remarks>
    /// Distances are signed meters from the face plane, positive in front. Feed one sample per frame. A tracking gap
    /// should call <see cref="Reset"/>, so a finger that reappears already touching does not count as a tap.
    /// </remarks>
    public sealed class FingertipTouch
    {
        /// <summary>Distance (m) at or below which the fingertip counts as touching.</summary>
        public float ContactDistance = 0.008f;

        /// <summary>Distance (m) the finger must draw back before it can tap again.</summary>
        public float RearmDistance = 0.035f;

        /// <summary>Slowest approach speed (m/s) that counts as a tap rather than a drift.</summary>
        public float MinimumSpeed = 0.1f;

        /// <summary>Approach speed (m/s) that gives a full-strength strike.</summary>
        public float FullStrengthSpeed = 0.9f;

        bool _armed = true;
        bool _hasLast;
        float _lastDistance;

        /// <summary>Forgets the last sample, for example after tracking was lost.</summary>
        public void Reset()
        {
            _hasLast = false;
            _armed = true;
        }

        /// <summary>
        /// Takes one sample of the fingertip.
        /// </summary>
        /// <param name="distance">Signed distance from the face plane in meters; positive in front.</param>
        /// <param name="deltaTime">Seconds since the previous sample.</param>
        /// <param name="insideFace">True when the fingertip is over the face.</param>
        /// <param name="speed">Receives the approach speed in meters per second; positive when moving toward the face.</param>
        /// <returns>True on the frame the fingertip taps the face.</returns>
        public bool Step(float distance, float deltaTime, bool insideFace, out float speed)
        {
            speed = 0f;
            if (!float.IsFinite(distance) || !(deltaTime > 0f))
            {
                _hasLast = false;
                return false;
            }

            bool tapped = false;
            if (_hasLast)
            {
                speed = (_lastDistance - distance) / deltaTime;
                if (_armed && insideFace && _lastDistance > ContactDistance && distance <= ContactDistance &&
                    speed >= MinimumSpeed)
                {
                    tapped = true;
                    _armed = false;
                }
            }

            if (!_armed && (distance >= RearmDistance || !insideFace))
            {
                _armed = true;
            }

            _lastDistance = distance;
            _hasLast = true;
            return tapped;
        }

        /// <summary>Strike strength in [0.35, 1] for an approach speed: a gentle tap rings softly, a quick one fully.</summary>
        /// <param name="speed">Approach speed in meters per second.</param>
        /// <returns>The strength.</returns>
        public float StrengthFor(float speed)
        {
            float span = Mathf.Max(1e-3f, FullStrengthSpeed - MinimumSpeed);
            return Mathf.Lerp(0.35f, 1f, Mathf.Clamp01((speed - MinimumSpeed) / span));
        }
    }
}
