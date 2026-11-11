using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace KineticForge.EditorTools
{
    /// <summary>
    /// Shared IMGUI helpers for the Kinetic Forge inspectors: labelled meters, values and section headers.
    /// </summary>
    internal static class ForgeEditorGUI
    {
        /// <summary>Draws a bold section header with a little space above it.</summary>
        /// <param name="text">Header text.</param>
        public static void Header(string text)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
        }

        /// <summary>Draws a labelled meter for a value in [0, 1].</summary>
        /// <param name="label">Row label.</param>
        /// <param name="value">Value in [0, 1]; out-of-range values are clamped for display.</param>
        /// <param name="suffix">Optional text appended to the numeric readout.</param>
        public static void Meter(string label, float value, string suffix = null)
        {
            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect bar = EditorGUI.PrefixLabel(row, new GUIContent(label));
            EditorGUI.ProgressBar(bar, Mathf.Clamp01(value), Format(value, "0.00") + suffix);
        }

        /// <summary>Draws a read-only labelled value.</summary>
        /// <param name="label">Row label.</param>
        /// <param name="value">Text to show.</param>
        public static void Value(string label, string value)
        {
            EditorGUILayout.LabelField(label, value);
        }

        /// <summary>Formats a number with the invariant culture.</summary>
        /// <param name="value">Number to format.</param>
        /// <param name="format">.NET numeric format string.</param>
        /// <returns>The formatted number.</returns>
        public static string Format(float value, string format)
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
