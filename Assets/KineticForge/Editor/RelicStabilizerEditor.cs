using KineticForge.Physics;
using UnityEditor;
using UnityEngine;

namespace KineticForge.EditorTools
{
    /// <summary>
    /// Inspector for <see cref="RelicStabilizer"/>: setup warnings, and in Play Mode the live stance response, the physical
    /// values written to the Rigidbody, and test controls.
    /// </summary>
    [CustomEditor(typeof(RelicStabilizer))]
    internal sealed class RelicStabilizerEditor : UnityEditor.Editor
    {
        /// <inheritdoc />
        public override bool RequiresConstantRepaint() => Application.isPlaying;

        /// <inheritdoc />
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            RelicStabilizer stabilizer = (RelicStabilizer)target;
            Rigidbody body = stabilizer.GetComponent<Rigidbody>();
            RelicHoldSimulator simulator = stabilizer.GetComponent<RelicHoldSimulator>();

            if (body != null && body.isKinematic)
            {
                EditorGUILayout.HelpBox(
                    "This Rigidbody is kinematic, so damping, inertia and wobble have no effect. Drive held relics " +
                    "through a joint or velocity tracking instead.",
                    MessageType.Warning);
            }

            if (!Application.isPlaying)
            {
                if (simulator == null)
                {
                    EditorGUILayout.HelpBox(
                        "No grab system yet? Add a Relic Hold Simulator to hang this relic on a simulated hand. Then hold " +
                        "Space to brace and press N to nudge it.",
                        MessageType.Info);
                }

                return;
            }

            ForgeEditorGUI.Header("Live Response");
            ForgeEditorGUI.Value("Following",
                stabilizer.StanceSource != null ? stabilizer.StanceSource.name : "ForgeEvents bus (fallback)");
            ForgeEditorGUI.Value("Held / Stabilizing", stabilizer.IsGrabbed + " / " + stabilizer.IsStabilizing);
            ForgeEditorGUI.Meter("Target Stability", stabilizer.TargetStability);
            ForgeEditorGUI.Meter("Response", stabilizer.AppliedResponse);

            if (body != null)
            {
                ForgeEditorGUI.Header("Rigidbody");
                ForgeEditorGUI.Value("Linear / Angular Damping",
                    ForgeEditorGUI.Format(body.linearDamping, "0.00") + " / " +
                    ForgeEditorGUI.Format(body.angularDamping, "0.00"));
                ForgeEditorGUI.Value("Inertia Scale", "x" + ForgeEditorGUI.Format(stabilizer.CurrentInertiaScale, "0.00"));
                ForgeEditorGUI.Value("Angular Speed",
                    ForgeEditorGUI.Format(body.angularVelocity.magnitude, "0.00") + " rad/s");
                ForgeEditorGUI.Value("Linear Speed", ForgeEditorGUI.Format(body.linearVelocity.magnitude, "0.00") + " m/s");
            }

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Recapture Baseline"))
                {
                    stabilizer.RecaptureBaseline();
                }

                using (new EditorGUI.DisabledScope(simulator == null || !simulator.IsHolding))
                {
                    if (GUILayout.Button("Nudge (N)") && simulator != null)
                    {
                        simulator.Nudge();
                    }
                }
            }
        }
    }
}
