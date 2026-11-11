using System.Collections.Generic;
using KineticForge.Core;
using KineticForge.Crafting;
using KineticForge.Tracking;
using NUnit.Framework;
using UnityEngine;

namespace KineticForge.Tests
{
    /// <summary>
    /// Verifies the carving precision model, the thumb-on-index geometry and the rune surface.
    /// </summary>
    public sealed class CraftingTests
    {
        const float Tolerance = 0.02f;

        [Test]
        public void Aim_IsFullInsideTheCenterAndZeroAtTheEdge()
        {
            Assert.AreEqual(1f, CarvingPrecision.Aim(0f, Tolerance), 1e-6f);
            Assert.AreEqual(1f, CarvingPrecision.Aim(Tolerance * CarvingPrecision.CenterShare, Tolerance), 1e-6f);
            Assert.AreEqual(0f, CarvingPrecision.Aim(Tolerance, Tolerance), 1e-6f);
            Assert.AreEqual(0f, CarvingPrecision.Aim(1f, Tolerance), 1e-6f);
            Assert.AreEqual(0f, CarvingPrecision.Aim(float.NaN, Tolerance));

            float previous = 1f;
            for (int i = 1; i <= 20; i++)
            {
                float aim = CarvingPrecision.Aim(Tolerance * i / 20f, Tolerance);
                Assert.That(aim, Is.LessThanOrEqualTo(previous + 1e-6f), "aim must never improve with distance");
                previous = aim;
            }
        }

        [Test]
        public void Precision_RewardsBracing()
        {
            const float Floor = 0.4f;
            const float QualifyingBar = 0.35f;

            Assert.AreEqual(1f, CarvingPrecision.Evaluate(0f, Tolerance, 1f, Floor), 1e-6f);
            Assert.AreEqual(Floor, CarvingPrecision.Evaluate(0f, Tolerance, 0f, Floor), 1e-6f);

            // Perfect aim from an unbraced stance only just qualifies; a half-tolerance miss does not.
            Assert.That(CarvingPrecision.Evaluate(0f, Tolerance, 0f, Floor), Is.GreaterThanOrEqualTo(QualifyingBar));
            Assert.That(CarvingPrecision.Evaluate(Tolerance * 0.6f, Tolerance, 0f, Floor), Is.LessThan(QualifyingBar));
            Assert.That(CarvingPrecision.Evaluate(Tolerance * 0.6f, Tolerance, 1f, Floor), Is.GreaterThan(QualifyingBar));
        }

        static IndexFingerFrame Frame(bool leftHand)
        {
            // Palm down, fingers along +Z; the middle finger lies toward +X on a right hand and -X on a left hand.
            float side = leftHand ? -0.02f : 0.02f;
            Assert.IsTrue(MicroGestureMath.TryBuildIndexFrame(
                Vector3.zero,
                new Vector3(0f, 0f, 0.045f),
                new Vector3(0f, 0f, 0.07f),
                new Vector3(0f, 0f, 0.09f),
                new Vector3(side, 0f, 0f),
                leftHand,
                out IndexFingerFrame frame));
            return frame;
        }

        [Test]
        public void IndexFrame_PointsAcrossTowardTheBackOfEitherHand()
        {
            IndexFingerFrame right = Frame(false);
            IndexFingerFrame left = Frame(true);

            Assert.That(Vector3.Distance(Vector3.forward, right.Along), Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(Vector3.up, right.Across), Is.LessThan(1e-5f), "back of a palm-down right hand");
            Assert.That(Vector3.Distance(Vector3.up, left.Across), Is.LessThan(1e-5f), "back of a palm-down left hand");
            Assert.AreEqual(0.09f, right.Length, 1e-5f);
        }

        [Test]
        public void ThumbProjection_MeasuresSlideInFingerLengths()
        {
            IndexFingerFrame frame = Frame(false);
            Vector3 thumb = new Vector3(-0.015f, 0f, 0.03f);

            Vector2 start = MicroGestureMath.ProjectThumb(frame, thumb, out float distance);
            Assert.AreEqual(0.015f, distance, 1e-5f);
            Assert.AreEqual(0f, start.x, 1e-5f);
            Assert.AreEqual(0.03f / 0.09f, start.y, 1e-5f);

            Vector2 moved = MicroGestureMath.ProjectThumb(frame, thumb + new Vector3(0f, 0.009f, 0.018f), out _);
            Vector2 slide = moved - start;
            Assert.AreEqual(0.1f, slide.x, 1e-4f);
            Assert.AreEqual(0.2f, slide.y, 1e-4f);
        }

        [Test]
        public void IndexFrame_RejectsDegenerateJoints()
        {
            Assert.IsFalse(MicroGestureMath.TryBuildIndexFrame(
                Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero, Vector3.right, false, out _));
            Assert.IsFalse(MicroGestureMath.TryBuildIndexFrame(
                Vector3.zero, Vector3.forward * 0.04f, Vector3.forward * 0.07f, Vector3.forward * 0.09f,
                Vector3.forward * 0.02f, false, out _), "a middle knuckle on the finger axis gives no side");
        }

        [Test]
        public void ContactStrength_FallsFromPressedToReleased()
        {
            Assert.AreEqual(1f, MicroGestureMath.ContactStrength(0.005f, 0.022f, 0.032f), 1e-6f);
            Assert.AreEqual(0f, MicroGestureMath.ContactStrength(0.032f, 0.022f, 0.032f), 1e-6f);
            float middle = MicroGestureMath.ContactStrength(0.022f, 0.022f, 0.032f);
            Assert.That(middle, Is.GreaterThan(0f).And.LessThan(1f));
        }

        [Test]
        public void DefaultRune_MatchesTheForgingRequirement()
        {
            GameObject flow = new GameObject("Flow (test)");
            try
            {
                ForgeGameManager manager = flow.AddComponent<ForgeGameManager>();
                Assert.AreEqual(manager.RequiredStrikes, RelicCarvingSurface.CreateDefaultPattern().Length);
            }
            finally
            {
                Object.DestroyImmediate(flow);
            }
        }

        [Test]
        public void Surface_AdvancesOnQualifiedStrikesAndCompletes()
        {
            GameObject relic = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                RelicCarvingSurface surface = relic.AddComponent<RelicCarvingSurface>();
                List<int> struck = new List<int>();
                int completions = 0;
                surface.OnMarkStruck += index => struck.Add(index);
                surface.OnPatternCompleted += () => completions++;

                Assert.AreEqual(0, surface.ActiveIndex);
                Assert.IsFalse(surface.RegisterStrike(surface.MinStrikePrecision - 0.01f), "below the bar");
                Assert.AreEqual(0, surface.StruckCount);

                for (int i = 0; i < surface.MarkCount; i++)
                {
                    Assert.IsTrue(surface.RegisterStrike(surface.MinStrikePrecision));
                }

                Assert.IsTrue(surface.IsComplete);
                Assert.AreEqual(-1, surface.ActiveIndex);
                Assert.IsFalse(surface.RegisterStrike(1f), "a complete rune takes no more strikes");
                Assert.AreEqual(surface.MarkCount, struck.Count);
                Assert.AreEqual(surface.MarkCount - 1, struck[struck.Count - 1]);
                Assert.AreEqual(1, completions);

                surface.ResetPattern();
                Assert.AreEqual(0, surface.ActiveIndex);
                Assert.IsTrue(surface.IsAcceptingStrikes);
            }
            finally
            {
                Object.DestroyImmediate(relic);
            }
        }

        [Test]
        public void Surface_MapsFaceCoordinatesOntoTheScaledFace()
        {
            GameObject relic = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                relic.transform.localScale = Vector3.one * 0.28f;
                relic.transform.position = new Vector3(1f, 2f, 3f);
                RelicCarvingSurface surface = relic.AddComponent<RelicCarvingSurface>();

                surface.GetFacePoint(Vector2.zero, out Vector3 center, out Vector3 normal);
                Assert.That(Vector3.Distance(new Vector3(1f, 2f, 3f - 0.14f), center), Is.LessThan(1e-5f));
                Assert.That(Vector3.Distance(Vector3.back, normal), Is.LessThan(1e-5f));

                // Viewed from the front (-Z), face +X is the viewer's right; the usable area is 80 % of the face.
                surface.GetFacePoint(new Vector2(0.5f, 0.5f), out Vector3 corner, out _);
                Vector3 offset = corner - center;
                Assert.AreEqual(0.112f, offset.x, 1e-5f);
                Assert.AreEqual(0.112f, offset.y, 1e-5f);
                Assert.AreEqual(0f, offset.z, 1e-5f);

                Vector2 size = surface.UsableFaceSize;
                Assert.AreEqual(0.224f, size.x, 1e-5f);
                Assert.AreEqual(0.224f, size.y, 1e-5f);
            }
            finally
            {
                Object.DestroyImmediate(relic);
            }
        }
    }
}
