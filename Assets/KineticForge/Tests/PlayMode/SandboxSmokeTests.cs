using System.Collections;
using KineticForge.Core;
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

            SceneManager.SetActiveScene(previous);
            yield return SceneManager.UnloadSceneAsync(sandbox);
            ForgeEvents.ResetSubscriptions();
        }
    }
}
