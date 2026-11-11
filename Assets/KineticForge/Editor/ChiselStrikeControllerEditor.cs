using KineticForge.Core;
using KineticForge.Crafting;
using UnityEditor;
using UnityEngine;

namespace KineticForge.EditorTools
{
    /// <summary>
    /// Inspector for <see cref="ChiselStrikeController"/>: in Play Mode, the rune progress, live aim and stance tremor,
    /// the latest strike's precision, and buttons to strike, recenter the cursor and reset the rune.
    /// </summary>
    [CustomEditor(typeof(ChiselStrikeController))]
    internal sealed class ChiselStrikeControllerEditor : UnityEditor.Editor
    {
        /// <inheritdoc />
        public override bool RequiresConstantRepaint() => Application.isPlaying;

        /// <inheritdoc />
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "In Play Mode: right-drag in the Game view to aim the carving cursor, left-click to strike, hold Space " +
                    "to brace. On Quest: slide the thumb along the index finger to aim, tap it to strike.",
                    MessageType.Info);
                return;
            }

            ChiselStrikeController controller = (ChiselStrikeController)target;
            RelicCarvingSurface surface = controller.GetComponent<RelicCarvingSurface>();
            CarvingCursor cursor = controller.GetComponent<CarvingCursor>();
            if (surface == null || cursor == null)
            {
                return;
            }

            ForgeEditorGUI.Header("Rune");
            ForgeEditorGUI.Meter("Progress", surface.MarkCount > 0 ? (float)surface.StruckCount / surface.MarkCount : 0f,
                "  (" + surface.StruckCount + "/" + surface.MarkCount + ")");
            ForgeEditorGUI.Value("Accepting Strikes", surface.IsAcceptingStrikes ? "Yes" : "No (waiting for the forging phase)");

            ForgeEditorGUI.Header("Aim");
            ForgeEditorGUI.Meter("Aim Quality", cursor.PreviewAim());
            ForgeEditorGUI.Value("Stance Tremor", ForgeEditorGUI.Format(cursor.TremorAmplitude * 1000f, "0.0") + " mm");
            ForgeEditorGUI.Value("Cursor (face)",
                ForgeEditorGUI.Format(cursor.FaceCoordinates.x, "0.00") + ", " +
                ForgeEditorGUI.Format(cursor.FaceCoordinates.y, "0.00"));

            ForgeEditorGUI.Header("Last Strike");
            ForgeEditorGUI.Meter("Precision", controller.LastPrecision,
                controller.StrikeCount == 0 ? "  (none yet)" : controller.LastStrikeQualified ? "  (carved)" : "  (missed)");
            ForgeEditorGUI.Value("Strikes", controller.StrikeCount.ToString());

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!surface.IsAcceptingStrikes))
                {
                    if (GUILayout.Button("Strike Now"))
                    {
                        controller.Strike(ForgeHand.Right, 1f);
                    }
                }

                if (GUILayout.Button("Recenter Cursor"))
                {
                    cursor.Recenter();
                }

                if (GUILayout.Button("Reset Rune"))
                {
                    surface.ResetPattern();
                }
            }
        }
    }
}
