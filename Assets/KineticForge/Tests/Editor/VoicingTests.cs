using System.Collections.Generic;
using KineticForge.Core;
using KineticForge.Feedback;
using NUnit.Framework;
using UnityEngine;

namespace KineticForge.Tests
{
    /// <summary>The mathematics of voicing: commissions, sensitivity, cuts, scoring, and that every commission is solvable.</summary>
    public sealed class VoicingTests
    {
        [Test]
        public void Library_HasSixCommissions_ThatGetHarder_AndWrap()
        {
            Assert.AreEqual(6, VoicingMath.CommissionCount);
            float previousTolerance = float.MaxValue;
            for (int i = 0; i < VoicingMath.CommissionCount; i++)
            {
                VoicingCommission c = VoicingMath.GetCommission(i);
                Assert.AreEqual(i, c.Index);
                Assert.AreEqual(CymaticMath.GetSigil(i).Name, c.Name, "a commission shares its sigil's name");
                Assert.AreEqual(CymaticMath.GetSigil(i).Note, c.RootHz, 1e-3f);
                Assert.AreEqual(VoicingMath.VoiceCount, c.Ratios.Length);
                Assert.AreEqual(VoicingMath.VoiceCount, c.Roles.Length);
                Assert.AreEqual(1f, c.Ratios[0], 1e-6f, "voice zero is always the root");
                Assert.Less(c.ToleranceCents, previousTolerance, "tolerance tightens with each commission");
                previousTolerance = c.ToleranceCents;
                Assert.Greater(c.CutBudget, 8);
                for (int m = 0; m < VoicingMath.VoiceCount; m++)
                {
                    Assert.Greater(c.StartCents[m], c.ToleranceCents, "every voice starts out of tune");
                    Assert.IsFalse(string.IsNullOrEmpty(c.Roles[m]));
                }
            }

            Assert.AreEqual(0, VoicingMath.GetCommission(6).Index);
            Assert.AreEqual(5, VoicingMath.GetCommission(-1).Index);
        }

        [Test]
        public void TargetShift_IsZeroForNaturalPartials_AndMatchesTheRatio()
        {
            VoicingCommission fifths = VoicingMath.GetCommission(0);
            Assert.AreEqual(0f, VoicingMath.TargetShiftCents(fifths, 0), 1e-3f);
            Assert.AreEqual(0f, VoicingMath.TargetShiftCents(fifths, 1), 1e-3f);
            Assert.AreEqual(0f, VoicingMath.TargetShiftCents(fifths, 2), 1e-3f);
            Assert.AreEqual(1200f * Mathf.Log(3f / 3.01f, 2f), VoicingMath.TargetShiftCents(fifths, 3), 1e-3f);

            VoicingCommission major = VoicingMath.GetCommission(1);
            Assert.AreEqual(1200f * Mathf.Log(1.25f / 1.5f, 2f), VoicingMath.TargetShiftCents(major, 1), 1e-3f);
            Assert.Less(VoicingMath.TargetShiftCents(major, 1), -300f, "a major third sits a minor third below the natural fifth");
        }

        [Test]
        public void Sensitivity_IsNonNegative_BoundedAndZeroAtTheNodes()
        {
            for (int voice = 0; voice < VoicingMath.VoiceCount; voice++)
            {
                for (float u = 0f; u <= 1f; u += 0.05f)
                {
                    for (float v = 0f; v <= 1f; v += 0.05f)
                    {
                        float s = VoicingMath.Sensitivity(voice, new Vector2(u, v));
                        Assert.GreaterOrEqual(s, 0f);
                        Assert.LessOrEqual(s, 2f + 1e-4f);
                    }
                }

                Assert.AreEqual(0f, VoicingMath.Sensitivity(voice, new Vector2(0.5f, 0.5f)), 1e-5f,
                    "the centre of the plate is a node of every voice");
            }

            Vector4 all = VoicingMath.Sensitivities(new Vector2(0.2f, 0.7f));
            for (int voice = 0; voice < VoicingMath.VoiceCount; voice++)
            {
                Assert.AreEqual(VoicingMath.Sensitivity(voice, new Vector2(0.2f, 0.7f)), all[voice], 1e-6f);
            }
        }

        [Test]
        public void CutSize_GrowsWithStrengthHeatAndCharge_AndSurvivesGarbage()
        {
            float cold = VoicingMath.CutSize(1f, 0f, 0f);
            float hot = VoicingMath.CutSize(1f, 1f, 0f);
            float charged = VoicingMath.CutSize(1f, 1f, 1f);
            Assert.Less(cold, hot);
            Assert.Less(hot, charged);
            Assert.AreEqual(0f, VoicingMath.CutSize(0f, 1f, 1f), 1e-6f);
            Assert.AreEqual(VoicingMath.BaseCutCents, hot, 1e-4f);
            Assert.IsTrue(float.IsFinite(VoicingMath.CutSize(float.NaN, float.PositiveInfinity, float.NegativeInfinity)));
        }

        [Test]
        public void CutDelta_OnlyEverLowersPitch()
        {
            for (float u = 0.05f; u < 1f; u += 0.1f)
            {
                for (float v = 0.05f; v < 1f; v += 0.1f)
                {
                    Vector4 delta = VoicingMath.CutDelta(new Vector2(u, v), 18f);
                    Assert.LessOrEqual(delta.x, 0f);
                    Assert.LessOrEqual(delta.y, 0f);
                    Assert.LessOrEqual(delta.z, 0f);
                    Assert.LessOrEqual(delta.w, 0f);
                }
            }

            Assert.Less(VoicingMath.CutDelta(new Vector2(0.5f, 0.5f), 18f).magnitude, 1e-3f, "a cut at the centre changes nothing");
        }

        [Test]
        public void InTune_AndScoring_FollowTolerance()
        {
            Assert.IsTrue(VoicingMath.InTune(new Vector4(10f, -10f, 0f, 14f), 14f));
            Assert.IsFalse(VoicingMath.InTune(new Vector4(10f, -10f, 0f, 14.1f), 14f));
            Assert.AreEqual(1f, VoicingMath.VoiceScore(0f, 14f), 1e-6f);
            Assert.AreEqual(1f, VoicingMath.VoiceScore(0.4f * 14f, 14f), 1e-6f, "a share of the tolerance is perfect");
            Assert.AreEqual(VoicingMath.VoiceScore(-9f, 14f), VoicingMath.VoiceScore(9f, 14f), 1e-6f, "flat and sharp score alike");
            Assert.Less(VoicingMath.VoiceScore(14f, 14f), 1f);
            Assert.Greater(VoicingMath.VoiceScore(14f, 14f), 0.5f, "just inside tolerance is still a good voice");
            Assert.AreEqual(0f, VoicingMath.VoiceScore(500f, 14f), 1e-6f);

            float tuned = VoicingMath.Quality(Vector4.zero, 14f, 6, 14);
            float sloppy = VoicingMath.Quality(new Vector4(14f, 14f, 14f, 14f), 14f, 6, 14);
            float spentMetal = VoicingMath.Quality(Vector4.zero, 14f, 14, 14);
            Assert.Greater(tuned, 0.9f);
            Assert.Less(sloppy, tuned);
            Assert.Less(spentMetal, tuned, "leaving metal uncut earns a little");
            Assert.AreEqual(ForgeGrade.S, ResonanceMath.Grade(tuned, 1f));
            Assert.AreEqual(ForgeGrade.A, ResonanceMath.Grade(sloppy, 1f));
        }

        [Test]
        public void SuggestCut_Improves_NeverOvershoots_AndStopsWhenTuned()
        {
            Vector4 errors = new Vector4(60f, 80f, 40f, 70f);
            float tolerance = 14f;
            float size = VoicingMath.CutSize(1f, 0.8f, 0f);
            Assert.IsTrue(VoicingMath.SuggestCut(errors, tolerance, size, out Vector2 uv));
            Vector4 after = errors + VoicingMath.CutDelta(uv, size);
            Assert.Less(after.sqrMagnitude, errors.sqrMagnitude);
            Assert.GreaterOrEqual(Mathf.Min(Mathf.Min(after.x, after.y), Mathf.Min(after.z, after.w)), -tolerance);
            Assert.IsFalse(VoicingMath.SuggestCut(Vector4.zero, tolerance, size, out _), "a tuned relic needs no cut");
        }

        [Test]
        public void EveryCommission_IsSolvable_WithinItsBudget_EvenWithColdMetal()
        {
            // The worst case: cold metal, no charge. Hot and charged strikes cut more, which only makes it easier.
            float[] sizes = { VoicingMath.CutSize(1f, 0.3f, 0f), VoicingMath.CutSize(1f, 0.8f, 0f), VoicingMath.CutSize(1f, 1f, 1f) };
            for (int i = 0; i < VoicingMath.CommissionCount; i++)
            {
                VoicingCommission c = VoicingMath.GetCommission(i);
                for (int s = 0; s < sizes.Length; s++)
                {
                    Vector4 errors = c.StartCents;
                    int cuts = 0;
                    while (cuts < c.CutBudget && !VoicingMath.InTune(errors, c.ToleranceCents))
                    {
                        if (!VoicingMath.SuggestCut(errors, c.ToleranceCents, sizes[s], out Vector2 uv))
                        {
                            break;
                        }

                        errors += VoicingMath.CutDelta(uv, sizes[s]);
                        cuts++;
                    }

                    Assert.IsTrue(VoicingMath.InTune(errors, c.ToleranceCents),
                        c.Name + " was not solved with cut size " + sizes[s] + ": " + errors + " after " + cuts + " cuts.");
                }
            }
        }

        [Test]
        public void ForgeArchive_KeepsVoiceErrors_AndStillReadsOldRecords()
        {
            const string Key = "KineticForge.Archive";
            string saved = PlayerPrefs.GetString(Key, string.Empty);
            try
            {
                ForgeArchive.Clear();
                PlayerPrefs.SetString(Key, "2,1,0.8;bad,record;3,2,0.6,5,-4,12,0;1,0,0.9,1,2,3");
                List<ForgedRelic> relics = ForgeArchive.Load();
                Assert.AreEqual(2, relics.Count, "malformed and truncated records are dropped");
                Assert.IsFalse(relics[0].HasVoices, "an old record has no voices");
                Assert.IsTrue(relics[1].HasVoices);
                Assert.AreEqual(new Vector4(5f, -4f, 12f, 0f), relics[1].VoiceErrors);

                ForgeArchive.Clear();
                ForgeArchive.Append(new ForgedRelic(4, ForgeGrade.S, 0.93f, new Vector4(3.4f, -2.6f, 0f, 7.5f)));
                relics = ForgeArchive.Load();
                Assert.AreEqual(1, relics.Count);
                Assert.IsTrue(relics[0].HasVoices);
                Assert.AreEqual(4, relics[0].Sigil);
                Assert.AreEqual(new Vector4(3f, -3f, 0f, 8f), relics[0].VoiceErrors, "errors are kept to the nearest cent");

                PlayerPrefs.SetString(Key, "1,0,0.5,NaN,0,0,0");
                Assert.AreEqual(0, ForgeArchive.Load().Count, "a non-finite error invalidates the record");
            }
            finally
            {
                PlayerPrefs.SetString(Key, saved);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void ModalNoteBank_RendersAFiniteChord_ForEveryCommission()
        {
            for (int i = 0; i < VoicingMath.CommissionCount; i++)
            {
                AudioClip clip = ModalNoteBank.RenderChord("test chord", VoicingMath.GetCommission(i), new Vector4(4f, -3f, 6f, 0f), 0.5f, 24000);
                try
                {
                    float[] samples = new float[clip.samples];
                    Assert.IsTrue(clip.GetData(samples, 0));
                    float peak = 0f;
                    for (int s = 0; s < samples.Length; s++)
                    {
                        Assert.IsTrue(float.IsFinite(samples[s]));
                        peak = Mathf.Max(peak, Mathf.Abs(samples[s]));
                    }

                    Assert.Greater(peak, 0.5f);
                    Assert.LessOrEqual(peak, 0.81f);
                }
                finally
                {
                    Object.DestroyImmediate(clip);
                }
            }
        }
    }
}
