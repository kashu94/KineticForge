using System.Collections.Generic;
using System.Globalization;
using System.Text;
using KineticForge.Core;
using UnityEngine;

namespace KineticForge.Feedback
{
    /// <summary>A relic the player has forged and quenched.</summary>
    public readonly struct ForgedRelic
    {
        /// <summary>Index of its sigil in <see cref="CymaticMath"/>'s library.</summary>
        public readonly int Sigil;

        /// <summary>Its grade.</summary>
        public readonly ForgeGrade Grade;

        /// <summary>Mean carve quality in [0, 1]; also how in tune it sings.</summary>
        public readonly float Quality;

        /// <summary>Final error of each voice in cents when the relic was finished. Meaningful when <see cref="HasVoices"/>.</summary>
        public readonly Vector4 VoiceErrors;

        /// <summary>True when the relic was voiced: its plaque rings the chord exactly as it was tuned.</summary>
        public readonly bool HasVoices;

        /// <summary>Creates a record.</summary>
        /// <param name="sigil">Sigil index.</param>
        /// <param name="grade">Grade.</param>
        /// <param name="quality">Mean carve quality.</param>
        public ForgedRelic(int sigil, ForgeGrade grade, float quality)
        {
            Sigil = sigil;
            Grade = grade;
            Quality = quality;
            VoiceErrors = Vector4.zero;
            HasVoices = false;
        }

        /// <summary>Creates a voiced relic's record.</summary>
        /// <param name="commission">Commission index, which is also the index of its sigil figure.</param>
        /// <param name="grade">Grade.</param>
        /// <param name="quality">Quality in [0, 1].</param>
        /// <param name="voiceErrors">Final error of each voice, in cents.</param>
        public ForgedRelic(int commission, ForgeGrade grade, float quality, Vector4 voiceErrors)
        {
            Sigil = commission;
            Grade = grade;
            Quality = quality;
            VoiceErrors = new Vector4(Mathf.Round(voiceErrors.x), Mathf.Round(voiceErrors.y), Mathf.Round(voiceErrors.z),
                Mathf.Round(voiceErrors.w));
            HasVoices = true;
        }

        /// <summary>How far, in cents, its odd partials sit out of tune: a perfect relic rings pure.</summary>
        public float DetuneCents => Mathf.Clamp01(1f - Quality) * 40f;
    }

    /// <summary>
    /// The player's forged relics, kept between sessions in <see cref="PlayerPrefs"/>: the latest
    /// <see cref="Capacity"/>, oldest first.
    /// </summary>
    public static class ForgeArchive
    {
        /// <summary>Most relics kept.</summary>
        public const int Capacity = 12;

        const string Key = "KineticForge.Archive";

        /// <summary>Loads the archive. Allocates; call at load time or on change.</summary>
        /// <returns>The relics, oldest first.</returns>
        public static List<ForgedRelic> Load()
        {
            List<ForgedRelic> relics = new List<ForgedRelic>();
            string stored = PlayerPrefs.GetString(Key, string.Empty);
            if (string.IsNullOrEmpty(stored))
            {
                return relics;
            }

            string[] entries = stored.Split(';');
            for (int i = 0; i < entries.Length; i++)
            {
                string[] fields = entries[i].Split(',');
                if ((fields.Length == 3 || fields.Length == 7) &&
                    int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sigil) &&
                    int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int grade) &&
                    float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float quality) &&
                    grade >= 0 && grade <= (int)ForgeGrade.Fractured)
                {
                    if (fields.Length == 7)
                    {
                        if (TryParseErrors(fields, out Vector4 errors))
                        {
                            relics.Add(new ForgedRelic(sigil, (ForgeGrade)grade, Mathf.Clamp01(quality), errors));
                        }
                    }
                    else
                    {
                        relics.Add(new ForgedRelic(sigil, (ForgeGrade)grade, Mathf.Clamp01(quality)));
                    }
                }
            }

            return relics;
        }

        /// <summary>Adds a relic, dropping the oldest beyond <see cref="Capacity"/>, and saves.</summary>
        /// <param name="relic">The relic.</param>
        /// <returns>The updated archive.</returns>
        public static List<ForgedRelic> Append(ForgedRelic relic)
        {
            List<ForgedRelic> relics = Load();
            relics.Add(relic);
            while (relics.Count > Capacity)
            {
                relics.RemoveAt(0);
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < relics.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(';');
                }

                builder.Append(relics[i].Sigil.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(((int)relics[i].Grade).ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(relics[i].Quality.ToString("0.###", CultureInfo.InvariantCulture));
                if (relics[i].HasVoices)
                {
                    Vector4 e = relics[i].VoiceErrors;
                    builder.Append(',').Append(e.x.ToString("0", CultureInfo.InvariantCulture))
                        .Append(',').Append(e.y.ToString("0", CultureInfo.InvariantCulture))
                        .Append(',').Append(e.z.ToString("0", CultureInfo.InvariantCulture))
                        .Append(',').Append(e.w.ToString("0", CultureInfo.InvariantCulture));
                }
            }

            PlayerPrefs.SetString(Key, builder.ToString());
            PlayerPrefs.Save();
            return relics;
        }

        static bool TryParseErrors(string[] fields, out Vector4 errors)
        {
            errors = Vector4.zero;
            for (int i = 0; i < 4; i++)
            {
                if (!float.TryParse(fields[3 + i], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
                    float.IsNaN(value) || float.IsInfinity(value))
                {
                    return false;
                }

                errors[i] = Mathf.Clamp(value, -400f, 400f);
            }

            return true;
        }

        /// <summary>Forgets every relic.</summary>
        public static void Clear()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
