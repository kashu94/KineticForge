using KineticForge.Tracking;
using UnityEditor;
using UnityEngine;

namespace KineticForge.EditorTools
{
    /// <summary>
    /// Inspector for <see cref="StanceTracker"/>: the usual settings plus, in Play Mode, a live breakdown of every stance
    /// factor, the measured posture and the standing references, with calibration and seated-mode controls.
    /// </summary>
    [CustomEditor(typeof(StanceTracker))]
    internal sealed class StanceTrackerEditor : UnityEditor.Editor
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
                    "Enter Play Mode for live stance readouts. Hold Space or Down Arrow in the Game view to emulate a " +
                    "brace. Keep this object selected to see the stance gizmos in the Scene view.",
                    MessageType.Info);
                return;
            }

            StanceTracker tracker = (StanceTracker)target;
            StanceEvaluation evaluation = tracker.LastEvaluation;
            StanceReference reference = tracker.Reference;

            ForgeEditorGUI.Header("Live Stance");
            string source = tracker.Source.ToString();
            if (tracker.IsBridgingDropout)
            {
                source += " (bridging dropout)";
            }

            if (tracker.IsEmulationDominant)
            {
                source += " + keyboard";
            }

            ForgeEditorGUI.Value("Source", source);
            ForgeEditorGUI.Meter("Stance Stability", tracker.StanceStability);
            ForgeEditorGUI.Meter("Sensor", tracker.SensorStability);
            ForgeEditorGUI.Meter("Keyboard", tracker.EmulatedStability);

            ForgeEditorGUI.Header("Factors");
            ForgeEditorGUI.Meter("Grounding", evaluation.Grounding);
            ForgeEditorGUI.Meter("    Crouch", evaluation.Crouch);
            ForgeEditorGUI.Meter("    Lean", evaluation.Lean);
            ForgeEditorGUI.Meter("    Width", evaluation.Width);
            ForgeEditorGUI.Meter("Balance", evaluation.Balance);
            ForgeEditorGUI.Meter("    Center of Mass", evaluation.CenterOfMass);
            ForgeEditorGUI.Meter("    Steadiness", evaluation.Steadiness);

            ForgeEditorGUI.Header("Posture");
            ForgeEditorGUI.Value("Forward / Sideways Lean",
                ForgeEditorGUI.Format(evaluation.ForwardLeanDegrees, "0.0") + "° / " +
                ForgeEditorGUI.Format(evaluation.LateralLeanDegrees, "0.0") + "°");
            ForgeEditorGUI.Value("Stance Width", ForgeEditorGUI.Format(evaluation.StanceWidthMeters, "0.000") + " m");
            ForgeEditorGUI.Value("COM Offset", ForgeEditorGUI.Format(evaluation.CenterOfMassOffsetMeters, "0.000") + " m");
            ForgeEditorGUI.Value("Reference Speed", ForgeEditorGUI.Format(tracker.LastPose.ReferenceSpeed, "0.000") + " m/s");

            ForgeEditorGUI.Header("References");
            ForgeEditorGUI.Value("Standing Head",
                reference.HasHead ? ForgeEditorGUI.Format(reference.StandingHeadHeight, "0.000") + " m" : "not set");
            ForgeEditorGUI.Value("Standing Hips",
                reference.HasHips ? ForgeEditorGUI.Format(reference.StandingHipHeight, "0.000") + " m" : "not set");
            ForgeEditorGUI.Value("Neutral Lean",
                reference.HasNeutralLean ? ForgeEditorGUI.Format(reference.NeutralForwardLeanDegrees, "0.0") + "°" : "not set");
            ForgeEditorGUI.Value("Calibration",
                tracker.IsCalibrating ? "Sampling..." : tracker.IsCalibrated ? "Calibrated" : "Not calibrated");

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(tracker.IsCalibrating))
                {
                    if (GUILayout.Button("Calibrate Standing Pose"))
                    {
                        tracker.RequestCalibration();
                    }
                }

                tracker.SeatedMode = GUILayout.Toggle(tracker.SeatedMode, "Seated Mode", "Button");
            }
        }
    }
}
