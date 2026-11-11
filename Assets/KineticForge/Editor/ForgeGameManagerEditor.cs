using KineticForge.Core;
using KineticForge.Crafting;
using UnityEditor;
using UnityEngine;

namespace KineticForge.EditorTools
{
    /// <summary>
    /// Inspector for <see cref="ForgeGameManager"/>: in Play Mode, the live flow state plus controls to step phases,
    /// pause, trigger calibration and simulate a precise chisel strike on the scene's relic.
    /// </summary>
    [CustomEditor(typeof(ForgeGameManager))]
    internal sealed class ForgeGameManagerEditor : UnityEditor.Editor
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
                    "Play Mode adds the live flow state and test controls: advance phases, pause, recalibrate and " +
                    "simulate chisel strikes.",
                    MessageType.Info);
                return;
            }

            ForgeGameManager manager = (ForgeGameManager)target;

            ForgeEditorGUI.Header("Live Flow");
            string phase = manager.Phase.ToString();
            if (manager.IsPaused)
            {
                phase += " (paused: " + manager.PauseReasons + ")";
            }

            ForgeEditorGUI.Value("Phase", phase);
            ForgeEditorGUI.Value("Phase Time", ForgeEditorGUI.Format(manager.PhaseElapsed, "0.0") + " s");
            ForgeEditorGUI.Meter("Latest Stability", manager.LatestStability);

            if (manager.Phase == ForgePhase.Calibration)
            {
                ForgeEditorGUI.Value("Calibration Step", manager.CurrentCalibrationStep.ToString());
                ForgeEditorGUI.Meter("Brace Hold", manager.BraceProgress, manager.IsBraced ? " (braced)" : string.Empty);
            }

            ForgeEditorGUI.Meter("Forge Progress", manager.ForgeProgress,
                "  (" + manager.QualifiedStrikes + "/" + manager.RequiredStrikes + ")");

            ForgeEditorGUI.Header("Test Controls");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Advance Phase"))
                {
                    manager.AdvancePhase();
                }

                if (GUILayout.Button("Restart"))
                {
                    manager.RestartExperience();
                }

                bool manualPause = (manager.PauseReasons & ForgePauseReason.Manual) != 0;
                if (GUILayout.Button(manualPause ? "Resume" : "Pause"))
                {
                    manager.SetManualPause(!manualPause);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Request Calibration"))
                {
                    ForgeEvents.RaiseCalibrationRequested();
                }

                using (new EditorGUI.DisabledScope(manager.Phase != ForgePhase.FirstRelicForging))
                {
                    if (GUILayout.Button("Simulate Precise Strike"))
                    {
                        SimulatePreciseStrike();
                    }
                }
            }
        }

        static void SimulatePreciseStrike()
        {
            // Strike the scene's relic on its active mark, so the rune and the forging progress advance together.
            RelicCarvingSurface surface = Object.FindAnyObjectByType<RelicCarvingSurface>();
            Rigidbody relic = null;
            Vector3 point = Vector3.zero;
            Vector3 normal = Vector3.up;
            if (surface != null && surface.TryGetActiveMark(out point, out normal))
            {
                relic = surface.Body;
            }

            ForgeEvents.RaiseChiselStrike(new ChiselStrikeEvent(relic, ForgeHand.Right, point, normal, 1f, 1f));
        }
    }
}
