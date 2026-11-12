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

        /// <summary>Creates a record.</summary>
        /// <param name="sigil">Sigil index.</param>
        /// <param name="grade">Grade.</param>
        /// <param name="quality">Mean carve quality.</param>
        public ForgedRelic(int sigil, ForgeGrade grade, float quality)
        {
            Sigil = sigil;
            Grade = grade;
            Quality = quality;
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
                if (fields.Length == 3 &&
                    int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sigil) &&
                    int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int grade) &&
                    float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float quality) &&
                    grade >= 0 && grade <= (int)ForgeGrade.Fractured)
                {
                    relics.Add(new ForgedRelic(sigil, (ForgeGrade)grade, Mathf.Clamp01(quality)));
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
            }

            PlayerPrefs.SetString(Key, builder.ToString());
            PlayerPrefs.Save();
            return relics;
        }

        /// <summary>Forgets every relic.</summary>
        public static void Clear()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
