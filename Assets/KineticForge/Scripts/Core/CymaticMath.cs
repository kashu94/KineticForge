using System.Collections.Generic;
using UnityEngine;

namespace KineticForge.Core
{
    /// <summary>A target Chladni figure: the figure a strike at <see cref="Seed"/> rings, and the note its relic sings.</summary>
    public readonly struct CymaticSigil
    {
        /// <summary>Display name.</summary>
        public readonly string Name;

        /// <summary>Face UV whose strike rings this figure.</summary>
        public readonly Vector2 Seed;

        /// <summary>Frequency (Hz) of the note the finished relic sings.</summary>
        public readonly float Note;

        /// <summary>Name of the note.</summary>
        public readonly string NoteName;

        /// <summary>Creates a sigil.</summary>
        /// <param name="name">Display name.</param>
        /// <param name="seed">Face UV whose strike rings the figure.</param>
        /// <param name="note">Note frequency (Hz).</param>
        /// <param name="noteName">Note name.</param>
        public CymaticSigil(string name, Vector2 seed, float note, string noteName)
        {
            Name = name;
            Seed = seed;
            Note = note;
            NoteName = noteName;
        }

        /// <summary>Unit plate mode amplitudes of the figure.</summary>
        public Vector4 Figure => CymaticMath.FigureFromStrike(Seed);
    }

    /// <summary>
    /// Cymatics: the Chladni figures of the relic's plate modes, how close a ringing relic is to a target figure, and
    /// the rune that follows a figure's nodal lines. Pure functions; nothing here allocates except
    /// <see cref="GenerateRune"/>, which runs once per relic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The face rings as <c>F(u, v) = Σ a_k φ_k(u, v)</c> with the plate modes of <see cref="ResonanceMath"/>. Its zero set,
    /// where the plate stands still, is the Chladni figure; sand bounced off the moving parts gathers there. Since
    /// <c>F</c> and <c>−F</c> share their nodal lines, figures compare by the absolute cosine of their amplitude vectors.
    /// </para>
    /// <para>
    /// A strike at p adds <c>φ(p)</c> to the amplitudes, and every mode rings down at the same rate while a figure is being
    /// sought, so repeated strikes at one spot converge on that spot's figure. Each sigil is the figure of one seed spot:
    /// the player finds it by ear, by eye and by the sand.
    /// </para>
    /// </remarks>
    public static class CymaticMath
    {
        // Seeds chosen so no two figures match above 0.75, ordered from the widest matching zone (about 11 % of the
        // face) to the narrowest (about 2 %), each with a rune of at least four grooves.
        static readonly CymaticSigil[] s_library =
        {
            new CymaticSigil("The Kindling", new Vector2(0.31f, 0.405f), 220f, "A"),
            new CymaticSigil("The Anvil's Eye", new Vector2(0.5f, 0.405f), 261.63f, "C"),
            new CymaticSigil("The Ember Crown", new Vector2(0.12f, 0.12f), 293.66f, "D"),
            new CymaticSigil("The Tidebreaker", new Vector2(0.785f, 0.12f), 329.63f, "E"),
            new CymaticSigil("The Hollow Bell", new Vector2(0.69f, 0.69f), 392f, "G"),
            new CymaticSigil("The Last Forge", new Vector2(0.12f, 0.595f), 440f, "A'"),
        };

        /// <summary>Number of sigils, in order of difficulty.</summary>
        public static int SigilCount => s_library.Length;

        /// <summary>The sigil at <paramref name="index"/>, wrapping around the library.</summary>
        /// <param name="index">Sigil index; any integer.</param>
        /// <returns>The sigil.</returns>
        public static CymaticSigil GetSigil(int index)
        {
            int count = s_library.Length;
            return s_library[((index % count) + count) % count];
        }

        /// <summary>The unit amplitude vector a strike at <paramref name="uv"/> rings.</summary>
        /// <param name="uv">Face UV.</param>
        /// <returns>Unit plate mode amplitudes, or zero where every mode is silent.</returns>
        public static Vector4 FigureFromStrike(Vector2 uv)
        {
            Vector4 shape = new Vector4(
                ResonanceMath.PlateMode(0, uv), ResonanceMath.PlateMode(1, uv),
                ResonanceMath.PlateMode(2, uv), ResonanceMath.PlateMode(3, uv));
            float length = shape.magnitude;
            return length > 1e-5f ? shape / length : Vector4.zero;
        }

        /// <summary>Displacement of the ringing face at <paramref name="uv"/>.</summary>
        /// <param name="amplitudes">Plate mode amplitudes.</param>
        /// <param name="uv">Face UV.</param>
        /// <returns>The displacement F.</returns>
        public static float Field(Vector4 amplitudes, Vector2 uv)
        {
            return amplitudes.x * ResonanceMath.PlateMode(0, uv) + amplitudes.y * ResonanceMath.PlateMode(1, uv) +
                   amplitudes.z * ResonanceMath.PlateMode(2, uv) + amplitudes.w * ResonanceMath.PlateMode(3, uv);
        }

        /// <summary>Displacement and its gradient at a point, from precomputed cosines and sines.</summary>
        /// <param name="amplitudes">Plate mode amplitudes.</param>
        /// <param name="cu">cos(kπu) for k = 0..5.</param>
        /// <param name="su">sin(kπu) for k = 0..5.</param>
        /// <param name="cv">cos(kπv) for k = 0..5.</param>
        /// <param name="sv">sin(kπv) for k = 0..5.</param>
        /// <param name="gradient">∂F/∂u and ∂F/∂v.</param>
        /// <returns>The displacement F.</returns>
        public static float FieldWithGradient(Vector4 amplitudes, float[] cu, float[] su, float[] cv, float[] sv, out Vector2 gradient)
        {
            Vector4 m = ResonanceMath.ModeM;
            Vector4 n = ResonanceMath.ModeN;
            Vector4 s = ResonanceMath.ModeSign;
            float field = 0f;
            float du = 0f;
            float dv = 0f;
            for (int k = 0; k < ResonanceMath.PlateModeCount; k++)
            {
                int mi = (int)m[k];
                int ni = (int)n[k];
                float a = amplitudes[k];
                float sign = s[k];
                field += a * (cu[mi] * cv[ni] + sign * cu[ni] * cv[mi]);
                du += a * (-mi * Mathf.PI * su[mi] * cv[ni] - sign * ni * Mathf.PI * su[ni] * cv[mi]);
                dv += a * (-ni * Mathf.PI * cu[mi] * sv[ni] - sign * mi * Mathf.PI * cu[ni] * sv[mi]);
            }

            gradient = new Vector2(du, dv);
            return field;
        }

        /// <summary>
        /// How closely the ringing figure matches a target, in [0, 1]: the absolute cosine between amplitude vectors.
        /// 0 while the relic is silent.
        /// </summary>
        /// <param name="amplitudes">Current plate mode amplitudes.</param>
        /// <param name="target">Target amplitudes.</param>
        /// <returns>The match.</returns>
        public static float Match(Vector4 amplitudes, Vector4 target)
        {
            float a = amplitudes.magnitude;
            float t = target.magnitude;
            if (a < 1e-4f || t < 1e-4f)
            {
                return 0f;
            }

            return Mathf.Clamp01(Mathf.Abs(Vector4.Dot(amplitudes, target)) / (a * t));
        }

        /// <summary>
        /// Generates a rune along a figure's nodal lines: <paramref name="count"/> evenly spread marks on the lines, in a
        /// path order, and grooves joining neighbors that lie on the same line.
        /// </summary>
        /// <param name="figure">Plate mode amplitudes of the figure.</param>
        /// <param name="count">Number of marks.</param>
        /// <param name="usableArea">Share of the face the rune may use, as in <c>RelicCarvingSurface</c>.</param>
        /// <param name="marks">Receives mark positions in face coordinates (−0.5 to 0.5 over the usable area).</param>
        /// <param name="strokes">Receives grooves as pairs of mark indices.</param>
        /// <returns>False when the figure has too few nodal points in the usable area.</returns>
        public static bool GenerateRune(Vector4 figure, int count, float usableArea, out Vector2[] marks, out Vector2Int[] strokes)
        {
            const int Grid = 64;
            float usable = Mathf.Clamp(usableArea, 0.3f, 1f);
            float low = 0.5f - usable * 0.5f;
            float step = usable / (Grid - 1);
            float[,] field = new float[Grid, Grid];
            float largest = 1e-6f;
            for (int i = 0; i < Grid; i++)
            {
                for (int j = 0; j < Grid; j++)
                {
                    float f = Field(figure, new Vector2(low + i * step, low + j * step));
                    field[i, j] = f;
                    largest = Mathf.Max(largest, Mathf.Abs(f));
                }
            }

            // Nodal points: grid cells where the field changes sign.
            List<Vector2> candidates = new List<Vector2>();
            for (int i = 0; i < Grid - 1; i++)
            {
                for (int j = 0; j < Grid - 1; j++)
                {
                    float f = field[i, j];
                    if (f * field[i + 1, j] <= 0f || f * field[i, j + 1] <= 0f)
                    {
                        candidates.Add(new Vector2(low + (i + 0.5f) * step, low + (j + 0.5f) * step));
                    }
                }
            }

            if (candidates.Count < count || count < 2)
            {
                marks = System.Array.Empty<Vector2>();
                strokes = System.Array.Empty<Vector2Int>();
                return false;
            }

            // Spread the marks with farthest point sampling, starting near the top center.
            List<Vector2> chosen = new List<Vector2>(count);
            Vector2 start = candidates[0];
            for (int i = 1; i < candidates.Count; i++)
            {
                Vector2 c = candidates[i];
                if (c.y - Mathf.Abs(c.x - 0.5f) > start.y - Mathf.Abs(start.x - 0.5f))
                {
                    start = c;
                }
            }

            chosen.Add(start);
            float[] nearest = new float[candidates.Count];
            for (int i = 0; i < candidates.Count; i++)
            {
                nearest[i] = (candidates[i] - start).sqrMagnitude;
            }

            while (chosen.Count < count)
            {
                int best = 0;
                for (int i = 1; i < candidates.Count; i++)
                {
                    if (nearest[i] > nearest[best])
                    {
                        best = i;
                    }
                }

                Vector2 pick = candidates[best];
                chosen.Add(pick);
                for (int i = 0; i < candidates.Count; i++)
                {
                    nearest[i] = Mathf.Min(nearest[i], (candidates[i] - pick).sqrMagnitude);
                }
            }

            // Order as a path: always carve the nearest remaining mark next.
            List<Vector2> path = new List<Vector2>(count) { chosen[0] };
            chosen.RemoveAt(0);
            while (chosen.Count > 0)
            {
                Vector2 last = path[path.Count - 1];
                int next = 0;
                for (int i = 1; i < chosen.Count; i++)
                {
                    if ((chosen[i] - last).sqrMagnitude < (chosen[next] - last).sqrMagnitude)
                    {
                        next = i;
                    }
                }

                path.Add(chosen[next]);
                chosen.RemoveAt(next);
            }

            // Grooves: shortest pairs first, along the nodal line, at most two per mark.
            List<Vector3> pairs = new List<Vector3>();
            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    float distance = Vector2.Distance(path[i], path[j]);
                    if (distance < 0.34f * usable / 0.8f && OnNodalLine(figure, path[i], path[j], largest))
                    {
                        pairs.Add(new Vector3(distance, i, j));
                    }
                }
            }

            pairs.Sort((x, y) => x.x.CompareTo(y.x));
            int[] degree = new int[count];
            List<Vector2Int> grooves = new List<Vector2Int>();
            for (int p = 0; p < pairs.Count; p++)
            {
                int a = (int)pairs[p].y;
                int b = (int)pairs[p].z;
                if (degree[a] < 2 && degree[b] < 2)
                {
                    grooves.Add(new Vector2Int(a, b));
                    degree[a]++;
                    degree[b]++;
                }
            }

            marks = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                marks[i] = new Vector2((path[i].x - 0.5f) / usable, (path[i].y - 0.5f) / usable);
            }

            strokes = grooves.ToArray();
            return true;
        }

        static bool OnNodalLine(Vector4 figure, Vector2 a, Vector2 b, float largest)
        {
            const int Samples = 12;
            for (int t = 0; t <= Samples; t++)
            {
                if (Mathf.Abs(Field(figure, Vector2.Lerp(a, b, (float)t / Samples))) / largest > 0.2f)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
