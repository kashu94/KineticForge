using System.Collections;
using KineticForge.Core;
using KineticForge.Crafting;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KineticForge.Tests
{
    /// <summary>
    /// Resonant Forging end to end: strikes ring the relic, charge and carve it, cracks follow a loose stance on cold
    /// metal, and the finished rune is graded.
    /// </summary>
    public sealed class ResonancePlayTests
    {
        GameObject _relic;
        RelicCarvingSurface _surface;
        CarvingCursor _cursor;
        ChiselStrikeController _strikes;
        RelicResonance _resonance;
        RelicForgeRenderer _renderer;

        [SetUp]
        public void SetUp()
        {
            ForgeEvents.ResetSubscriptions();
            _relic = new GameObject("Resonant Relic (test)");
            _relic.transform.position = new Vector3(0f, 60f, 0f);
            _relic.AddComponent<MeshFilter>();
            _relic.AddComponent<MeshRenderer>();
            _relic.AddComponent<BoxCollider>().size = new Vector3(0.3f, 0.3f, 0.08f);
            Rigidbody body = _relic.AddComponent<Rigidbody>();
            body.isKinematic = true;
            _surface = _relic.AddComponent<RelicCarvingSurface>();
            _cursor = _relic.AddComponent<CarvingCursor>();
            _strikes = _relic.AddComponent<ChiselStrikeController>();
            _resonance = _relic.AddComponent<RelicResonance>();
            _renderer = _relic.AddComponent<RelicForgeRenderer>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_relic);
            ForgeEvents.ResetSubscriptions();
        }

        IEnumerator StrikeActiveMark(bool braced = true)
        {
            // Set the stance, wait out the strike cooldown, aim exactly at the active mark, strike at full strength.
            float stability = braced ? 1f : 0f;
            ForgeEvents.RaiseStanceChanged(new StanceChangedEvent(stability, stability, StanceTrackingSource.HeadPose, false, stability, stability));
            yield return new WaitForSecondsRealtime(0.15f);
            _cursor.FaceCoordinates = _surface.GetMarkCoordinates(_surface.ActiveIndex);
            Assert.IsTrue(_strikes.Strike(ForgeHand.Right, 1f), "strike published");
        }

        [UnityTest]
        public IEnumerator Strike_RingsChargesAndCarvesTheRelic()
        {
            RelicResonanceEvent last = default;
            int resonated = 0;
            ForgeEvents.RelicResonated += payload =>
            {
                last = payload;
                resonated++;
            };

            yield return null;
            Assert.Greater(_resonance.Heat, 0.8f, "a fresh relic starts hot");
            Assert.IsFalse(_resonance.IsPulsing);

            yield return StrikeActiveMark();
            Assert.AreEqual(1, resonated);
            Assert.AreEqual(0, last.CarvedMark);
            Assert.Greater(last.Quality, 0f);
            Assert.IsTrue(_resonance.IsPulsing, "the strike started the pulse");
            Assert.Greater(_resonance.Charge, 0.2f);
            Assert.Greater(Mathf.Abs(_resonance.PlateAmplitudes.x) + Mathf.Abs(_resonance.PlateAmplitudes.y) +
                           Mathf.Abs(_resonance.PlateAmplitudes.z) + Mathf.Abs(_resonance.PlateAmplitudes.w), 0f);

            yield return null;
            Vector2 mark = _surface.FaceToUV(_surface.GetMarkCoordinates(0));
            Assert.Greater(_renderer.DepthAt(mark), 0.0005f, "the mark is dented into the mesh");
            Assert.AreEqual(0f, _renderer.DepthAt(new Vector2(0.95f, 0.95f)), "the rest of the face is untouched");
        }

        [UnityTest]
        public IEnumerator Pulse_DecaysAndCoolingHappensOverTime()
        {
            yield return StrikeActiveMark();
            float charge = _resonance.Charge;
            float heat = _resonance.Heat;
            yield return new WaitForSeconds(1f);
            Assert.Less(_resonance.Charge, charge, "the pulse rings down");
            Assert.Less(_resonance.Heat, heat + 0.02f);
        }

        [UnityTest]
        public IEnumerator ColdMetal_WithLooseStance_Cracks()
        {
            int cracks = 0;
            ForgeEvents.RelicResonated += payload => cracks += payload.Cracked ? 1 : 0;
            _resonance.SetHeat(0f);
            yield return StrikeActiveMark(braced: false);
            yield return StrikeActiveMark(braced: false);
            Assert.Greater(cracks, 0, "cold, unbraced, charged metal cracks");
            Assert.Less(_resonance.Integrity, 1f);
        }

        [UnityTest]
        public IEnumerator CarvingEveryMark_ForgesAndGradesTheRelic()
        {
            int forged = 0;
            RelicForgedEvent result = default;
            ForgeEvents.RelicForged += payload =>
            {
                forged++;
                result = payload;
            };

            int marks = _surface.MarkCount;
            for (int i = 0; i < marks; i++)
            {
                yield return StrikeActiveMark();
            }

            Assert.IsTrue(_surface.IsComplete);
            Assert.AreEqual(1, forged);
            Assert.IsTrue(_resonance.IsForged);
            Assert.AreEqual(marks, _resonance.CarvedCount);
            Assert.That(result.MeanQuality, Is.InRange(0.01f, 1f));
            Assert.AreEqual(_resonance.Grade, result.Grade);
        }

        [UnityTest]
        public IEnumerator WithGameFlow_TheLastStrikeCarvesAndGradesBeforeCompletion()
        {
            GameObject flow = new GameObject("Game Flow (test)");
            ForgeGameManager game = flow.AddComponent<ForgeGameManager>();
            int forged = 0;
            ForgeEvents.RelicForged += payload => forged++;
            yield return null;
            game.AdvancePhase();
            game.AdvancePhase();
            Assert.AreEqual(ForgePhase.FirstRelicForging, game.Phase);

            int marks = _surface.MarkCount;
            for (int i = 0; i < marks; i++)
            {
                yield return StrikeActiveMark();
            }

            Assert.AreEqual(marks, _surface.StruckCount, "the final strike carved the last mark");
            Assert.AreEqual(1, forged, "the relic was graded");
            Assert.AreEqual(ForgePhase.FirstRelicForging, game.Phase, "the final carve lands before the result screen");

            yield return new WaitForSeconds(2f);
            Assert.AreEqual(ForgePhase.Completion, game.Phase);
            Object.Destroy(flow);
        }
    }
}
