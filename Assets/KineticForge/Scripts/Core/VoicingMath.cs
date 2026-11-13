using UnityEngine;

namespace KineticForge.Core
{
    /// <summary>A relic commission: a chord the finished relic must sing, and the state the pour left it in.</summary>
    public readonly struct VoicingCommission
    {
        /// <summary>Position in the commission library; also the index of the sigil figure shown on its plaque.</summary>
        public readonly int Index;

        /// <summary>Name of the commission, shared with its sigil.</summary>
        public readonly string Name;

        /// <summary>Name of the chord the relic must sing, such as "Open Fifths".</summary>
        public readonly string ChordName;

        /// <summary>Root note in hertz.</summary>
        public readonly float RootHz;

        /// <summary>Name of the root note, such as "A".</summary>
        public readonly string RootName;

        /// <summary>Target pitch of each voice as a ratio of the root, in plate mode order.</summary>
        public readonly float[] Ratios;

        /// <summary>Musical role of each voice in the chord, such as "Root" or "Fifth".</summary>
        public readonly string[] Roles;

        /// <summary>How sharp each voice rings straight from the pour, in cents.</summary>
        public readonly Vector4 StartCents;

        /// <summary>Largest error, in cents, at which a voice counts as in tune.</summary>
        public readonly float ToleranceCents;

        /// <summary>Cuts the relic can take before the metal is spent.</summary>
        public readonly int CutBudget;

        /// <summary>Times the torch can re-melt the last cut, undoing it. Early commissions allow more.</summary>
        public readonly int Remelts;

        /// <summary>Creates a commission.</summary>
        public VoicingCommission(int index, string name, string chordName, float rootHz, string rootName, float[] ratios,
            string[] roles, Vector4 startCents, float toleranceCents, int cutBudget, int remelts)
        {
            Remelts = remelts;
            Index = index;
            Name = name;
            ChordName = chordName;
            RootHz = rootHz;
            RootName = rootName;
            Ratios = ratios;
            Roles = roles;
            StartCents = startCents;
            ToleranceCents = toleranceCents;
            CutBudget = cutBudget;
        }
    }

    /// <summary>How a cut went, judged from the change it made.</summary>
    public enum CutVerdict : byte
    {
        /// <summary>No cut has been judged.</summary>
        None = 0,

        /// <summary>The cut brought voices closer to tune without spoiling any.</summary>
        Good = 1,

        /// <summary>The cut pushed a voice flat of its band, where cuts can never bring it back.</summary>
        TooFar = 2,

        /// <summary>The cut hardly changed any voice: it landed on the nodes.</summary>
        Wasted = 3,

        /// <summary>The last cut was undone with the torch.</summary>
        Undone = 4,
    }

    /// <summary>
    /// The mathematics of voicing: tuning a relic's four plate modes, its voices, by cutting metal. Pure and
    /// deterministic, so it is fully unit-tested and the same on every device.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The model.</b> Each voice is one plate mode. Where the mode moves most, cutting thins the plate most for that
    /// voice and lowers its pitch most. At the nodal lines, the lines the Chladni sand collects on, a cut changes that
    /// voice not at all. The sensitivity of voice <c>m</c> at a point is <c>φ_m(u, v)² / 2</c>, in [0, 2], and a cut lowers
    /// the voice by <c>sensitivity × cut size</c> cents. This follows how bell founders tune: metal is removed where the
    /// partial they want to lower moves most. It is a simplified model, tuned for play, not a finite element simulation.
    /// </para>
    /// <para>
    /// <b>The state.</b> A voice's error is its pitch relative to its target, in cents, positive when sharp. Cuts only
    /// lower pitch, so a relic starts sharp and every cut is final: overshooting leaves a voice flat for good.
    /// </para>
    /// </remarks>
    public static class VoicingMath
    {
        /// <summary>Number of voices, one per plate mode.</summary>
        public const int VoiceCount = ResonanceMath.PlateModeCount;

        /// <summary>Cents a full-strength cut lowers a voice at sensitivity one, before heat and charge.</summary>
        public const float BaseCutCents = 18f;

        /// <summary>Share of a voice's tolerance below which its score is perfect.</summary>
        public const float PerfectShare = 0.4f;

        /// <summary>The primary partial each voice owns, as ratios of the relic's fundamental before any tuning.</summary>
        public static readonly float[] NaturalRatios = { 1f, 1.5f, 2f, 3.01f };

        static readonly VoicingCommission[] s_library = BuildLibrary();

        /// <summary>Number of commissions.</summary>
        public static int CommissionCount => s_library.Length;

        /// <summary>Gets a commission, wrapping the index.</summary>
        /// <param name="index">Any integer; negative and large values wrap.</param>
        /// <returns>The commission.</returns>
        public static VoicingCommission GetCommission(int index)
        {
            int count = s_library.Length;
            return s_library[((index % count) + count) % count];
        }

        /// <summary>
        /// How far a voice's target sits from its natural partial, in cents. The relic's partial pair rings at its natural
        /// pitch shifted by this plus the voice's current error.
        /// </summary>
        /// <param name="commission">The commission.</param>
        /// <param name="voice">Voice index in [0, <see cref="VoiceCount"/>).</param>
        /// <returns>The shift in cents.</returns>
        public static float TargetShiftCents(in VoicingCommission commission, int voice)
        {
            return 1200f * Mathf.Log(commission.Ratios[voice] / NaturalRatios[voice], 2f);
        }

        /// <summary>Sensitivity of one voice to a cut at <paramref name="uv"/>, in [0, 2].</summary>
        /// <param name="voice">Voice index.</param>
        /// <param name="uv">Face position, both axes in [0, 1].</param>
        /// <returns>The sensitivity.</returns>
        public static float Sensitivity(int voice, Vector2 uv)
        {
            float shape = ResonanceMath.PlateMode(voice, uv);
            return shape * shape * 0.5f;
        }

        /// <summary>Sensitivity of every voice to a cut at <paramref name="uv"/>.</summary>
        /// <param name="uv">Face position, both axes in [0, 1].</param>
        /// <returns>One component per voice.</returns>
        public static Vector4 Sensitivities(Vector2 uv)
        {
            return new Vector4(Sensitivity(0, uv), Sensitivity(1, uv), Sensitivity(2, uv), Sensitivity(3, uv));
        }

        /// <summary>
        /// Size of a cut in cents at sensitivity one. Hot metal cuts deeper, and a charged relic strikes harder, so rhythm
        /// buys coarse, fast cuts and cold, calm strikes give fine control.
        /// </summary>
        /// <param name="strength">Strike strength in [0, 1].</param>
        /// <param name="softness">Metal softness in [0, 1].</param>
        /// <param name="charge">Pulse charge before the strike.</param>
        /// <returns>The cut size in cents.</returns>
        public static float CutSize(float strength, float softness, float charge)
        {
            return BaseCutCents * ForgeMath.Sanitize01(strength, 1f) * (0.6f + 0.4f * ForgeMath.Sanitize01(softness)) *
                   (1f + 0.5f * Mathf.Clamp01(charge));
        }

        /// <summary>The pitch change a cut at <paramref name="uv"/> would make to each voice, in cents (all negative or zero).</summary>
        /// <param name="uv">Face position, both axes in [0, 1].</param>
        /// <param name="cutSize">Cut size from <see cref="CutSize"/>.</param>
        /// <returns>Cents per voice, lowering.</returns>
        public static Vector4 CutDelta(Vector2 uv, float cutSize)
        {
            return -cutSize * Sensitivities(uv);
        }

        /// <summary>True when every voice is within <paramref name="toleranceCents"/> of its target.</summary>
        /// <param name="errors">Error per voice, in cents.</param>
        /// <param name="toleranceCents">Tolerance in cents.</param>
        public static bool InTune(Vector4 errors, float toleranceCents)
        {
            return Mathf.Abs(errors.x) <= toleranceCents && Mathf.Abs(errors.y) <= toleranceCents &&
                   Mathf.Abs(errors.z) <= toleranceCents && Mathf.Abs(errors.w) <= toleranceCents;
        }

        /// <summary>Score of one voice in [0, 1]: perfect within a share of tolerance, then falling to zero.</summary>
        /// <param name="errorCents">Error in cents.</param>
        /// <param name="toleranceCents">Tolerance in cents.</param>
        public static float VoiceScore(float errorCents, float toleranceCents)
        {
            float tolerance = Mathf.Max(0.01f, toleranceCents);
            float excess = Mathf.Max(0f, Mathf.Abs(errorCents) - PerfectShare * tolerance);
            return Mathf.Clamp01(1f - excess / (2.5f * tolerance));
        }

        /// <summary>
        /// Quality of a voiced relic in [0, 1]: the mean voice score, with a small bonus for metal left uncut.
        /// </summary>
        /// <param name="errors">Final error per voice, in cents.</param>
        /// <param name="toleranceCents">Tolerance in cents.</param>
        /// <param name="cutsUsed">Cuts taken.</param>
        /// <param name="cutBudget">Cuts available.</param>
        public static float Quality(Vector4 errors, float toleranceCents, int cutsUsed, int cutBudget)
        {
            float mean = 0.25f * (VoiceScore(errors.x, toleranceCents) + VoiceScore(errors.y, toleranceCents) +
                                  VoiceScore(errors.z, toleranceCents) + VoiceScore(errors.w, toleranceCents));
            float left = cutBudget > 0 ? 1f - Mathf.Clamp01((float)cutsUsed / cutBudget) : 0f;
            return Mathf.Clamp01(mean * (0.9f + 0.1f * left));
        }

        /// <summary>Total pitch change, in cents, below which a cut counts as wasted.</summary>
        public const float WastedCents = 2f;

        /// <summary>Judges a cut from the errors before and after it.</summary>
        /// <param name="before">Error per voice before the cut, in cents.</param>
        /// <param name="after">Error per voice after the cut, in cents.</param>
        /// <param name="toleranceCents">Tolerance in cents.</param>
        /// <returns>Wasted, too far, or good.</returns>
        public static CutVerdict JudgeCut(Vector4 before, Vector4 after, float toleranceCents)
        {
            float moved = Mathf.Abs(after.x - before.x) + Mathf.Abs(after.y - before.y) + Mathf.Abs(after.z - before.z) +
                          Mathf.Abs(after.w - before.w);
            if (moved < WastedCents)
            {
                return CutVerdict.Wasted;
            }

            for (int m = 0; m < VoiceCount; m++)
            {
                if (after[m] < -toleranceCents && before[m] >= -toleranceCents)
                {
                    return CutVerdict.TooFar;
                }
            }

            return CutVerdict.Good;
        }

        /// <summary>
        /// Finds the single cut that most reduces the total squared error without pushing any voice flatter than
        /// <paramref name="toleranceCents"/>. Used for the hint, and by tests to prove each commission is solvable.
        /// </summary>
        /// <param name="errors">Error per voice, in cents.</param>
        /// <param name="toleranceCents">Tolerance in cents.</param>
        /// <param name="cutSize">Cut size from <see cref="CutSize"/>.</param>
        /// <param name="uv">Receives the best cut position.</param>
        /// <returns>False when no cut improves the relic.</returns>
        public static bool SuggestCut(Vector4 errors, float toleranceCents, float cutSize, out Vector2 uv)
        {
            const int Steps = 23;
            const float Margin = 0.06f;
            float best = 0f;
            uv = new Vector2(0.5f, 0.5f);
            float current = errors.sqrMagnitude;
            for (int j = 0; j < Steps; j++)
            {
                float v = Mathf.Lerp(Margin, 1f - Margin, j / (Steps - 1f));
                for (int i = 0; i < Steps; i++)
                {
                    Vector2 point = new Vector2(Mathf.Lerp(Margin, 1f - Margin, i / (Steps - 1f)), v);
                    Vector4 next = errors + CutDelta(point, cutSize);
                    if (next.x < -toleranceCents || next.y < -toleranceCents || next.z < -toleranceCents ||
                        next.w < -toleranceCents)
                    {
                        continue;
                    }

                    float gain = current - next.sqrMagnitude;
                    if (gain > best)
                    {
                        best = gain;
                        uv = point;
                    }
                }
            }

            return best > 0f;
        }

        static VoicingCommission[] BuildLibrary()
        {
            // Chords are built from just ratios. Start offsets, tolerances and budgets rise in difficulty; the prototype
            // that fixed them solved every commission with a greedy player in well under budget, and a sloppier one most
            // of the time on the hardest.
            return new[]
            {
                // The first is the lesson: wide tolerance, generous metal and three re-melts.
                Make(0, "Open Fifths", new[] { 1f, 1.5f, 2f, 3f }, new[] { "Root", "Fifth", "Octave", "Twelfth" },
                    new Vector4(50f, 65f, 35f, 75f), 26f, 20, 3),
                Make(1, "Major", new[] { 1f, 1.25f, 1.5f, 2f }, new[] { "Root", "Third", "Fifth", "Octave" },
                    new Vector4(65f, 40f, 85f, 55f), 18f, 15, 2),
                Make(2, "Minor", new[] { 1f, 1.2f, 1.5f, 2f }, new[] { "Root", "Minor Third", "Fifth", "Octave" },
                    new Vector4(75f, 90f, 50f, 65f), 14f, 15, 2),
                Make(3, "Suspended", new[] { 1f, 4f / 3f, 1.5f, 2f }, new[] { "Root", "Fourth", "Fifth", "Octave" },
                    new Vector4(55f, 95f, 70f, 85f), 11f, 16, 1),
                Make(4, "Major Seventh", new[] { 1f, 1.25f, 1.5f, 1.875f }, new[] { "Root", "Third", "Fifth", "Seventh" },
                    new Vector4(80f, 60f, 100f, 90f), 8f, 16, 1),
                Make(5, "Minor Seventh", new[] { 1f, 1.2f, 1.5f, 1.8f }, new[] { "Root", "Minor Third", "Fifth", "Seventh" },
                    new Vector4(95f, 105f, 75f, 110f), 6f, 18, 0),
            };
        }

        static VoicingCommission Make(int index, string chord, float[] ratios, string[] roles, Vector4 start, float tolerance,
            int budget, int remelts)
        {
            CymaticSigil sigil = CymaticMath.GetSigil(index);
            return new VoicingCommission(index, sigil.Name, chord, sigil.Note, sigil.NoteName, ratios, roles, start, tolerance,
                budget, remelts);
        }
    }
}
