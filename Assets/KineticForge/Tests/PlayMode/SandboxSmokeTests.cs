using System.Collections;
using KineticForge.Core;
using KineticForge.Crafting;
using KineticForge.Feedback;
using KineticForge.Physics;
using KineticForge.Tracking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace KineticForge.Tests
{
    /// <summary>
    /// Plays the generated sandbox scene, with its real Meta rig and every Kinetic Forge system, and fails on any error
    /// or exception logged while it runs.
    /// </summary>
    public sealed class SandboxSmokeTests
    {
        const string SandboxScene = "Sandbox";

        [UnityTest]
        public IEnumerator Sandbox_PlaysCleanly()
        {
            ForgeEvents.ResetSubscriptions();
            Scene previous = SceneManager.GetActiveScene();
            yield return SceneManager.LoadSceneAsync(SandboxScene, LoadSceneMode.Additive);
            Scene sandbox = SceneManager.GetSceneByName(SandboxScene);
            Assert.IsTrue(sandbox.IsValid(), "Sandbox.unity must be in the build settings; run Kinetic Forge > Create Sandbox Scene.");
            SceneManager.SetActiveScene(sandbox);

            yield return new WaitForSeconds(3f);

            ForgeGameManager manager = Object.FindAnyObjectByType<ForgeGameManager>();
            StanceTracker tracker = Object.FindAnyObjectByType<StanceTracker>();
            RelicStabilizer stabilizer = Object.FindAnyObjectByType<RelicStabilizer>();
            RelicHoldSimulator simulator = Object.FindAnyObjectByType<RelicHoldSimulator>();

            Assert.IsNotNull(manager);
            Assert.IsNotNull(tracker);
            Assert.AreEqual(ForgePhase.Intro, manager.Phase);
            Assert.IsTrue(simulator != null && simulator.IsHolding, "the simulated hand holds the relic");
            Assert.IsTrue(stabilizer != null && stabilizer.IsGrabbed);
            Assert.IsTrue(ForgeMath.IsFinite(stabilizer.Body.position), "relic physics stayed finite");
            Assert.IsNotNull(Object.FindAnyObjectByType<RelicResonance>(), "the relic resonates");
            Assert.IsNotNull(Object.FindAnyObjectByType<RelicForgeRenderer>(), "the relic renders its forging state");
            ForgeGuide guide = Object.FindAnyObjectByType<ForgeGuide>();
            Assert.IsTrue(guide != null && guide.Text.Contains("KINETIC FORGE"), "the guide shows the intro");
            Assert.IsNotNull(Object.FindAnyObjectByType<RelicSigil>(), "the relic has a sigil to awaken");
            Assert.IsNotNull(Object.FindAnyObjectByType<ChladniSand>(), "the relic carries Chladni sand");
            Assert.IsNotNull(Object.FindAnyObjectByType<ResonanceWall>(), "the forge has a Resonance Wall");
            MoltenPour pour = Object.FindAnyObjectByType<MoltenPour>();
            Assert.IsTrue(pour != null && pour.IsPouring, "the intro pours the relic");
            ForgeDiagnostics diagnostics = Object.FindAnyObjectByType<ForgeDiagnostics>();
            Assert.IsTrue(diagnostics != null && ForgeDiagnostics.IsCompiledIn, "the Editor carries the diagnostics panel");
            Assert.IsFalse(diagnostics.GetComponent<MeshRenderer>().enabled, "diagnostics start hidden in the Editor");
            Assert.AreEqual(0, diagnostics.ErrorCount, "nothing logged an error while the sandbox started");

            SceneManager.SetActiveScene(previous);
            yield return SceneManager.UnloadSceneAsync(sandbox);
            ForgeEvents.ResetSubscriptions();
        }
    }
}
