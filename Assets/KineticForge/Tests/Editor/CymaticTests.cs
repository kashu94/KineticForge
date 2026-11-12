using System.Collections.Generic;
using KineticForge.Core;
using KineticForge.Feedback;
using NUnit.Framework;
using UnityEngine;

namespace KineticForge.Tests
{
    /// <summary>Cymatics: sigil figures, matching, the field's gradient, generated runes and the forge archive.</summary>
    public sealed class CymaticTests
    {
        [Test]
        public void FigureFromStrike_IsUnitAndMatchesItself_UpToSign()
        {
            for (int i = 0; i < CymaticMath.SigilCount; i++)
            {
                Vector4 figure = CymaticMath.GetSigil(i).Figure;
                Assert.AreEqual(1f, figure.magnitude, 1e-4f);
                Assert.AreEqual(1f, CymaticMath.Match(figure * 0.3f, figure), 1e-4f);
                Assert.AreEqual(1f, CymaticMath.Match(-figure, figure), 1e-4f, "F and -F share their nodal lines");
            }

            Assert.AreEqual(0f, CymaticMath.Match(Vector4.zero, CymaticMath.GetSigil(0).Figure));
            Assert.Less(CymaticMath.Match(new Vector4(1f, 0f, 0f, 0f), new Vector4(0f, 1f, 0f, 0f)), 1e-4f);
        }

        [Test]
        public void Sigils_AreDistinctFigures()
        {
            for (int i = 0; i < CymaticMath.SigilCount; i++)
            {
                for (int j = i + 1; j < CymaticMath.SigilCount; j++)
                {
                    Assert.Less(CymaticMath.Match(CymaticMath.GetSigil(i).Figure, CymaticMath.GetSigil(j).Figure), 0.88f,
                        "sigils " + i + " and " + j + " must not set each other");
                }
            }

            Assert.AreEqual(CymaticMath.GetSigil(0).Name, CymaticMath.GetSigil(CymaticMath.SigilCount).Name, "the library wraps");
        }

        [Test]
        public void FieldWithGradient_MatchesFiniteDifferences()
        {
            float[] cu = new float[6], su = new float[6], cv = new float[6], sv = new float[6];
            Vector4 amplitudes = new Vector4(0.7f, -0.3f, 0.5f, 0.2f);
            Vector2 uv = new Vector2(0.37f, 0.61f);
            for (int k = 0; k < 6; k++)
            {
                cu[k] = Mathf.Cos(k * Mathf.PI * uv.x);
                su[k] = Mathf.Sin(k * Mathf.PI * uv.x);
                cv[k] = Mathf.Cos(k * Mathf.PI * uv.y);
                sv[k] = Mathf.Sin(k * Mathf.PI * uv.y);
            }

            float field = CymaticMath.FieldWithGradient(amplitudes, cu, su, cv, sv, out Vector2 gradient);
            const float H = 1e-3f;
            float du = (CymaticMath.Field(amplitudes, uv + new Vector2(H, 0f)) - CymaticMath.Field(amplitudes, uv - new Vector2(H, 0f))) / (2f * H);
            float dv = (CymaticMath.Field(amplitudes, uv + new Vector2(0f, H)) - CymaticMath.Field(amplitudes, uv - new Vector2(0f, H))) / (2f * H);
            Assert.AreEqual(CymaticMath.Field(amplitudes, uv), field, 1e-4f);
            Assert.AreEqual(du, gradient.x, 2e-2f);
            Assert.AreEqual(dv, gradient.y, 2e-2f);
        }

        [Test]
        public void GenerateRune_PlacesTwelveSpreadMarksOnTheNodalLines()
        {
            for (int i = 0; i < CymaticMath.SigilCount; i++)
            {
                Vector4 figure = CymaticMath.GetSigil(i).Figure;
                Assert.IsTrue(CymaticMath.GenerateRune(figure, 12, 0.8f, out Vector2[] marks, out Vector2Int[] strokes));
                Assert.AreEqual(12, marks.Length);
                Assert.GreaterOrEqual(strokes.Length, 3, "sigil " + i + " has grooves");

                float largest = 0f;
                for (int u = 0; u <= 20; u++)
                {
                    for (int v = 0; v <= 20; v++)
                    {
                        largest = Mathf.Max(largest, Mathf.Abs(CymaticMath.Field(figure, new Vector2(0.1f + 0.04f * u, 0.1f + 0.04f * v))));
                    }
                }

                for (int m = 0; m < marks.Length; m++)
                {
                    Assert.That(marks[m].x, Is.InRange(-0.5f, 0.5f));
                    Assert.That(marks[m].y, Is.InRange(-0.5f, 0.5f));
                    Vector2 uv = new Vector2(0.5f + marks[m].x * 0.8f, 0.5f + marks[m].y * 0.8f);
                    Assert.Less(Mathf.Abs(CymaticMath.Field(figure, uv)) / largest, 0.12f, "mark on a nodal line");
                    for (int n = m + 1; n < marks.Length; n++)
                    {
                        Assert.Greater(Vector2.Distance(marks[m], marks[n]), 0.06f, "marks are spread out");
                    }
                }

                for (int g = 0; g < strokes.Length; g++)
                {
                    Assert.That(strokes[g].x, Is.InRange(0, 11));
                    Assert.That(strokes[g].y, Is.InRange(0, 11));
                }
            }
        }

        [Test]
        public void ForgeArchive_RoundTripsAndKeepsTheLatest()
        {
            const string Key = "KineticForge.Archive";
            string saved = PlayerPrefs.GetString(Key, string.Empty);
            try
            {
                ForgeArchive.Clear();
                Assert.AreEqual(0, ForgeArchive.Load().Count);
                ForgeArchive.Append(new ForgedRelic(2, ForgeGrade.A, 0.81f));
                List<ForgedRelic> relics = ForgeArchive.Load();
                Assert.AreEqual(1, relics.Count);
                Assert.AreEqual(2, relics[0].Sigil);
                Assert.AreEqual(ForgeGrade.A, relics[0].Grade);
                Assert.AreEqual(0.81f, relics[0].Quality, 1e-3f);
                Assert.Less(new ForgedRelic(0, ForgeGrade.S, 1f).DetuneCents, new ForgedRelic(0, ForgeGrade.C, 0.3f).DetuneCents);

                for (int i = 0; i < ForgeArchive.Capacity + 3; i++)
                {
                    ForgeArchive.Append(new ForgedRelic(i, ForgeGrade.B, 0.5f));
                }

                relics = ForgeArchive.Load();
                Assert.AreEqual(ForgeArchive.Capacity, relics.Count);
                Assert.AreEqual(ForgeArchive.Capacity + 2, relics[relics.Count - 1].Sigil, "the newest is kept last");
            }
            finally
            {
                PlayerPrefs.SetString(Key, saved);
                PlayerPrefs.Save();
            }
        }
    }
}
