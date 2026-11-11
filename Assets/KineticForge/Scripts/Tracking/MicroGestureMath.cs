using KineticForge.Core;
using UnityEngine;

namespace KineticForge.Tracking
{
    /// <summary>
    /// Coordinate frame on the side of the index finger, the surface the thumb slides across. Built by
    /// <see cref="MicroGestureMath.TryBuildIndexFrame"/>.
    /// </summary>
    public struct IndexFingerFrame
    {
        /// <summary>Index knuckle, the frame origin.</summary>
        public Vector3 Knuckle;

        /// <summary>Index middle joint.</summary>
        public Vector3 Middle;

        /// <summary>Index end joint.</summary>
        public Vector3 End;

        /// <summary>Index tip.</summary>
        public Vector3 Tip;

        /// <summary>Unit axis along the finger, from the knuckle toward the tip.</summary>
        public Vector3 Along;

        /// <summary>Unit axis across the side of the finger, toward the back of the hand.</summary>
        public Vector3 Across;

        /// <summary>Index finger length in meters: the sum of its three segments.</summary>
        public float Length;
    }

    /// <summary>
    /// Pure, allocation-free geometry for thumb-on-index micro-gestures.
    /// </summary>
    /// <remarks>
    /// The thumb tip is expressed in a frame on the side of the index finger: Y along the finger toward the tip, X across
    /// it toward the back of the hand, both in finger lengths so the same slide reads the same on any hand size. Contact is
    /// the thumb tip's distance to the finger's joint chain.
    /// </remarks>
    public static class MicroGestureMath
    {
        const float MinSegmentLength = 0.002f;

        /// <summary>Builds the index finger frame from tracked joints.</summary>
        /// <param name="knuckle">Index knuckle.</param>
        /// <param name="middle">Index middle joint.</param>
        /// <param name="end">Index end joint.</param>
        /// <param name="tip">Index tip.</param>
        /// <param name="middleKnuckle">Middle finger knuckle, which fixes the side of the finger.</param>
        /// <param name="isLeftHand">True for a left hand, so both hands use the same "toward the back" direction.</param>
        /// <param name="frame">The frame.</param>
        /// <returns>False when the joints are degenerate or non-finite.</returns>
        public static bool TryBuildIndexFrame(
            Vector3 knuckle,
            Vector3 middle,
            Vector3 end,
            Vector3 tip,
            Vector3 middleKnuckle,
            bool isLeftHand,
            out IndexFingerFrame frame)
        {
            frame = default;
            float length = Vector3.Distance(knuckle, middle) + Vector3.Distance(middle, end) + Vector3.Distance(end, tip);
            Vector3 along = end - knuckle;
            if (!(length > MinSegmentLength * 3f) || along.sqrMagnitude < MinSegmentLength * MinSegmentLength)
            {
                return false;
            }

            along.Normalize();

            // The side axis points from the index toward the middle finger, perpendicular to the index.
            Vector3 side = middleKnuckle - knuckle;
            side -= along * Vector3.Dot(side, along);
            if (side.sqrMagnitude < MinSegmentLength * MinSegmentLength)
            {
                return false;
            }

            side.Normalize();

            // In Unity's left-handed space Cross(along, side) points to the back of a right hand and the palm of a left one.
            Vector3 across = Vector3.Cross(along, side);
            if (isLeftHand)
            {
                across = -across;
            }

            frame = new IndexFingerFrame
            {
                Knuckle = knuckle,
                Middle = middle,
                End = end,
                Tip = tip,
                Along = along,
                Across = across,
                Length = length,
            };
            return ForgeMath.IsFinite(across) && ForgeMath.IsFinite(length);
        }

        /// <summary>Expresses the thumb tip in the finger frame.</summary>
        /// <param name="frame">Index finger frame.</param>
        /// <param name="thumbTip">Thumb tip position.</param>
        /// <param name="distanceToFinger">Distance in meters from the thumb tip to the index joint chain.</param>
        /// <returns>X across the finger and Y along it, in finger lengths.</returns>
        public static Vector2 ProjectThumb(in IndexFingerFrame frame, Vector3 thumbTip, out float distanceToFinger)
        {
            Vector3 offset = thumbTip - frame.Knuckle;
            float inverseLength = 1f / frame.Length;
            Vector2 coordinates = new Vector2(
                Vector3.Dot(offset, frame.Across) * inverseLength,
                Vector3.Dot(offset, frame.Along) * inverseLength);

            float distance = DistanceToSegment(thumbTip, frame.Knuckle, frame.Middle);
            distance = Mathf.Min(distance, DistanceToSegment(thumbTip, frame.Middle, frame.End));
            distance = Mathf.Min(distance, DistanceToSegment(thumbTip, frame.End, frame.Tip));
            distanceToFinger = distance;
            return coordinates;
        }

        /// <summary>Contact strength in [0, 1]: 1 when pressed against the finger, 0 at the release distance.</summary>
        /// <param name="distanceToFinger">Thumb-to-finger distance in meters.</param>
        /// <param name="contactDistance">Distance at which contact begins.</param>
        /// <param name="releaseDistance">Distance at which contact ends.</param>
        /// <returns>The contact strength.</returns>
        public static float ContactStrength(float distanceToFinger, float contactDistance, float releaseDistance)
        {
            return 1f - Mathf.InverseLerp(contactDistance * 0.5f, releaseDistance, distanceToFinger);
        }

        /// <summary>Distance from a point to a segment. A degenerate segment measures to its start.</summary>
        /// <param name="point">Query point.</param>
        /// <param name="a">Segment start.</param>
        /// <param name="b">Segment end.</param>
        /// <returns>The distance.</returns>
        public static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lengthSquared = ab.sqrMagnitude;
            float t = lengthSquared > 1e-12f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSquared) : 0f;
            return Vector3.Distance(point, a + ab * t);
        }
    }
}
