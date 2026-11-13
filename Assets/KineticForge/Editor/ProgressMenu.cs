using KineticForge.Crafting;
using KineticForge.Feedback;
using UnityEditor;
using UnityEngine;

namespace KineticForge.EditorTools
{
    /// <summary>
    /// Clears the progress the game saves between sessions, so a test run starts as a new player's does: with the lesson,
    /// the first commission and an empty Resonance Wall.
    /// </summary>
    internal static class ProgressMenu
    {
        const string LegacySigilKey = "KineticForge.NextSigil";

        [MenuItem("Kinetic Forge/Reset Saved Progress", false, 90)]
        static void ResetMenu()
        {
            if (!EditorUtility.DisplayDialog("Kinetic Forge",
                    "Forget the saved commission and every relic on the Resonance Wall? The next run starts with the lesson.",
                    "Reset", "Cancel"))
            {
                return;
            }

            Reset();
        }

        /// <summary>Forgets the saved commission, the legacy sigil progress and every archived relic.</summary>
        internal static void Reset()
        {
            ForgeArchive.Clear();
            PlayerPrefs.DeleteKey(RelicVoicing.NextCommissionKey);
            PlayerPrefs.DeleteKey(LegacySigilKey);
            PlayerPrefs.Save();
            Debug.Log("[KineticForge][Core][ProgressMenu] PROGRESS_RESET: Saved commission and Resonance Wall cleared.");
        }
    }
}
