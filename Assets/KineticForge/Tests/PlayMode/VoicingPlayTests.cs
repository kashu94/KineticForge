using System.Collections;
using KineticForge.Core;
using KineticForge.Crafting;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KineticForge.Tests
{
    /// <summary>Voicing end to end: the hammer rings, the chisel cuts, an in-tune relic settles and is forged, spent metal ends it.</summary>
    public sealed class VoicingPlayTests
    {
        GameObject _relic;
        RelicCarvingSurface _surface;
        CarvingCursor _cursor;
        ChiselStrikeController _strikes;
        RelicResonance _resonance;
        RelicVoicing _voicing;

        [SetUp]
        public void SetUp()
        {
            ForgeEvents.ResetSubscriptions();
            _relic = new GameObject("Voicing Relic (test)");
            _relic.transform.position = new Vector3(0f, 80f, 0f);
            _relic.AddComponent<MeshFilter>();
            _relic.AddComponent<MeshRenderer>();
            _relic.AddComponent<BoxCollider>().size = new Vector3(0.3f, 0.3f, 0.08f);
            _relic.AddComponent<Rigidbody>().isKinematic = true;
            _surface = _relic.AddComponent<RelicCarvingSurface>();
            _cursor = _relic.AddComponent<CarvingCursor>();
            _strikes = _relic.AddComponent<ChiselStrikeController>();
            _resonance = _relic.AddComponent<RelicResonance>();
            _voicing = _relic.AddComponent<RelicVoicing>();
            _voicing.PersistProgress = false;
            _relic.AddComponent<RelicForgeRenderer>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_relic);
            ForgeEvents.ResetSubscriptions();
        }

        IEnumerator StrikeAt(Vector2 face)
        {
            ForgeEvents.RaiseStanceChanged(new StanceChangedEvent(1f, 1f, StanceTrackingSource.HeadPose, false, 1f, 1f));
            yield return new WaitForSecondsRealtime(0.15f);
            _cursor.FaceCoordinates = face;
            Assert.IsTrue(_strikes.Strike(ForgeHand.Right, 1f));
        }

        Vector2 UvToFace(Vector2 uv)
        {
            float usable = _surface.UsableArea;
            return new Vector2((uv.x - 0.5f) / usable, (uv.y - 0.5f) / usable);
        }

        [UnityTest]
        public IEnumerator TheHammerRings_AndCutsNothing()
        {
            yield return null;
            Assert.AreEqual(VoicingStage.Voicing, _voicing.Stage);
            Assert.AreEqual(VoicingTool.Hammer, _voicing.Tool, "voicing starts with the hammer");
            Vector4 before = _voicing.Errors;
            Assert.AreEqual(VoicingMath.GetCommission(0).StartCents, before, "a new relic is as sharp as the pour left it");

            yield return StrikeAt(new Vector2(-0.4f, 0.3f));
            Assert.AreEqual(before, _voicing.Errors, "the hammer never cuts");
            Assert.AreEqual(0, _voicing.CutsUsed);
            Assert.Greater(_resonance.PlateAmplitudes.magnitude, 0.05f, "but the plate rings");
            Assert.IsTrue(_surface.IsAimable, "the face is aimable without any rune marks showing");
            Assert.IsFalse(_surface.IsAcceptingStrikes, "no rune mark is being carved");
        }

        [UnityTest]
        public IEnumerator TheChisel_CutsWhereItLands_AndOnlyEverLowersPitch()
        {
            yield return null;
            int cuts = 0;
            _voicing.OnCut += uv => cuts++;
            _voicing.SetTool(VoicingTool.Chisel);

            Vector4 before = _voicing.Errors;
            yield return StrikeAt(new Vector2(-0.6f, 0.5f));
            Vector4 after = _voicing.Errors;
            Assert.AreEqual(1, _voicing.CutsUsed);
            Assert.AreEqual(1, cuts, "a cut raises OnCut once");
            Assert.LessOrEqual(after.x, before.x);
            Assert.LessOrEqual(after.y, before.y);
            Assert.LessOrEqual(after.z, before.z);
            Assert.LessOrEqual(after.w, before.w);
            Assert.Less(after.x + after.y + after.z + after.w, before.x + before.y + before.z + before.w - 0.5f,
                "a cut away from the nodes lowers at least one voice");
            Assert.AreEqual(_voicing.TargetCents(1) + after.y, _voicing.VoiceCents(1), 1e-4f);
        }

        [UnityTest]
        public IEnumerator TappingTheLeftThumb_CyclesTheTool()
        {
            yield return null;

            // Keep the strike controller out of it: a tap would otherwise also strike.
            _strikes.enabled = false;
            Assert.AreEqual(VoicingTool.Hammer, _voicing.Tool);
            VoicingTool announced = VoicingTool.Hammer;
            _voicing.OnToolChanged += tool => announced = tool;
            ForgeEvents.RaiseMicroGestureTap(new MicroGestureTapEvent(ForgeHand.Left, 1f, MicroGestureSource.HandTracking));
            Assert.AreEqual(VoicingTool.Chisel, _voicing.Tool);
            Assert.AreEqual(VoicingTool.Chisel, announced);
            _voicing.ToggleTool();
            Assert.AreEqual(VoicingTool.Hammer, _voicing.Tool, "with nothing to undo, the torch is skipped");
        }

        [UnityTest]
        public IEnumerator TheTorch_UndoesTheLastCut_AndGivesTheCutBack()
        {
            yield return null;
            _voicing.SetTool(VoicingTool.Chisel);
            Vector4 start = _voicing.Errors;
            yield return StrikeAt(new Vector2(-0.6f, 0.5f));
            float size = VoicingMath.CutSize(1f, _resonance.Softness, _resonance.Charge);
            Assert.IsTrue(VoicingMath.SuggestCut(_voicing.Errors, _voicing.Commission.ToleranceCents, size, out Vector2 uv));
            yield return StrikeAt(UvToFace(uv));
            Assert.AreEqual(2, _voicing.CutsUsed);
            Vector4 afterTwo = _voicing.Errors;
            Assert.IsTrue(_voicing.CanRemelt);

            bool rebuilt = false;
            _voicing.OnRemelt += () => rebuilt = true;
            _voicing.ToggleTool();
            Assert.AreEqual(VoicingTool.Torch, _voicing.Tool, "the torch follows the chisel once a cut can be undone");
            yield return StrikeAt(new Vector2(0f, 0f));

            Assert.IsTrue(rebuilt, "listeners are told to rebuild the carving");
            Assert.AreEqual(1, _voicing.CutsUsed, "the cut is given back");
            Assert.AreEqual(VoicingMath.GetCommission(0).Remelts - 1, _voicing.RemeltsLeft);
            Assert.AreEqual(VoicingTool.Chisel, _voicing.Tool, "the torch gives way to the chisel");
            Assert.AreEqual(CutVerdict.Undone, _voicing.LastVerdict);
            Assert.Greater((afterTwo - _voicing.Errors).magnitude, 0.5f, "the voices went back up");
            Assert.Less(_voicing.Errors.x, start.x + 0.01f);
            Assert.GreaterOrEqual(_resonance.Heat, 0.95f, "the torch reheats the metal");
        }

        [UnityTest]
        public IEnumerator TheTorch_IsSkipped_WhenThereIsNothingToUndo_OrNoReMeltsRemain()
        {
            yield return null;
            _voicing.SetTool(VoicingTool.Chisel);
            _voicing.ToggleTool();
            Assert.AreEqual(VoicingTool.Hammer, _voicing.Tool, "no cut yet, so no torch");

            _voicing.Begin(5);
            _voicing.SetTool(VoicingTool.Chisel);
            yield return StrikeAt(new Vector2(-0.6f, 0.5f));
            Assert.AreEqual(1, _voicing.CutsUsed);
            Assert.IsFalse(_voicing.CanRemelt, "the hardest commission has no re-melts");
            _voicing.ToggleTool();
            Assert.AreEqual(VoicingTool.Hammer, _voicing.Tool);
            Assert.IsFalse(_voicing.Remelt());
        }

        [UnityTest]
        public IEnumerator ATouchingFingertip_HoversThenTaps_AndStrikesWhereItLands()
        {
            yield return null;
            FingertipStrike finger = _relic.AddComponent<FingertipStrike>();
            yield return null;
            ForgeEvents.RaiseStanceChanged(new StanceChangedEvent(1f, 1f, StanceTrackingSource.HeadPose, false, 1f, 1f));
            _voicing.SetTool(VoicingTool.Chisel);

            Vector2 face = new Vector2(-0.5f, 0.4f);
            _surface.GetFacePoint(face, out Vector3 onFace, out Vector3 normal);

            // Hovering: the finger steers the aim point and the strike has not happened.
            Assert.IsFalse(finger.ProcessTip(onFace + normal * 0.06f, 0.014f));
            Assert.IsTrue(finger.IsHovering);
            Assert.AreEqual(face.x, _cursor.FaceCoordinates.x, 0.03f, "the aim point follows the finger");
            Assert.AreEqual(face.y, _cursor.FaceCoordinates.y, 0.03f);
            Assert.AreEqual(0, _voicing.CutsUsed);

            // A quick tap onto the face cuts there.
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.IsFalse(finger.ProcessTip(onFace + normal * 0.03f, 0.014f));
            Assert.IsTrue(finger.ProcessTip(onFace + normal * 0.003f, 0.014f), "a quick approach taps");
            Assert.AreEqual(1, finger.TouchCount);
            Assert.AreEqual(1, _voicing.CutsUsed, "with the chisel up, a tap cuts");
            Assert.Greater(_resonance.PlateAmplitudes.magnitude, 0.05f, "and rings the plate");

            // Resting at the face never strikes again; drawing back and tapping does.
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.IsFalse(finger.ProcessTip(onFace + normal * 0.02f, 0.014f));
            Assert.IsFalse(finger.ProcessTip(onFace + normal * 0.003f, 0.014f));
            Assert.AreEqual(1, _voicing.CutsUsed);
            finger.ProcessTip(onFace + normal * 0.07f, 0.014f);
            yield return new WaitForSecondsRealtime(0.15f);
            finger.ProcessTip(onFace + normal * 0.03f, 0.014f);
            Assert.IsTrue(finger.ProcessTip(onFace + normal * 0.003f, 0.014f));
            Assert.AreEqual(2, _voicing.CutsUsed);

            finger.LoseTrack();
            Assert.IsFalse(finger.IsHovering, "losing the hand releases the aim point");
        }

        [UnityTest]
        public IEnumerator AFingertipOffTheFace_OrFarAway_DoesNothing()
        {
            yield return null;
            FingertipStrike finger = _relic.AddComponent<FingertipStrike>();
            yield return null;
            _surface.GetFacePoint(new Vector2(0.2f, 0.2f), out Vector3 onFace, out Vector3 normal);
            Vector3 sideways = _relic.transform.right * 0.6f;

            Assert.IsFalse(finger.ProcessTip(onFace + normal * 0.3f, 0.014f), "far in front");
            Assert.IsFalse(finger.IsHovering);
            Assert.IsFalse(finger.ProcessTip(onFace + sideways + normal * 0.06f, 0.014f), "off to the side");
            Assert.IsFalse(finger.IsHovering);
            finger.ProcessTip(onFace + sideways + normal * 0.03f, 0.014f);
            Assert.IsFalse(finger.ProcessTip(onFace + sideways + normal * 0.003f, 0.014f), "a tap beside the relic is not a strike");
            Assert.AreEqual(0, finger.TouchCount);
            Assert.AreEqual(0, _voicing.CutsUsed);
        }

        [UnityTest]
        public IEnumerator EveryCut_IsJudged()
        {
            yield return null;
            _voicing.SetTool(VoicingTool.Chisel);
            Assert.AreEqual(CutVerdict.None, _voicing.LastVerdict);

            yield return StrikeAt(Vector2.zero);
            Assert.AreEqual(CutVerdict.Wasted, _voicing.LastVerdict, "a cut on the nodes moves nothing");

            float size = VoicingMath.CutSize(1f, _resonance.Softness, _resonance.Charge);
            Assert.IsTrue(VoicingMath.SuggestCut(_voicing.Errors, _voicing.Commission.ToleranceCents, size, out Vector2 uv));
            yield return StrikeAt(UvToFace(uv));
            Assert.AreEqual(CutVerdict.Good, _voicing.LastVerdict);
            Assert.AreEqual(Time.time, _voicing.LastVerdictTime, 0.5f);
        }

        [UnityTest]
        public IEnumerator TheHint_NeedsTheChisel_AndComesSoonOnTheLesson()
        {
            yield return null;
            Assert.AreEqual(0, _voicing.CommissionIndex);
            yield return new WaitForSeconds(RelicVoicing.LessonHintDelay + 0.4f);
            Assert.IsFalse(_voicing.HintActive, "no hint while the hammer is in hand");

            _voicing.SetTool(VoicingTool.Chisel);
            Assert.IsTrue(_voicing.HintActive, "the lesson points to a good cut once the chisel is up and nothing is cut");
            Assert.IsTrue(VoicingMath.SuggestCut(_voicing.Errors, _voicing.Commission.ToleranceCents,
                VoicingMath.CutSize(1f, 0.8f, 0f), out Vector2 expected));
            Assert.AreEqual(expected.x, _voicing.HintUV.x, 1e-4f);
            Assert.AreEqual(expected.y, _voicing.HintUV.y, 1e-4f);
        }

        [UnityTest]
        public IEnumerator TuningEveryVoice_SettlesTheRelic_ThenForgesIt()
        {
            yield return null;
            int forged = 0;
            RelicForgedEvent result = default;
            ForgeEvents.RelicForged += payload =>
            {
                forged++;
                result = payload;
            };

            bool tuned = false;
            _voicing.OnInTune += () => tuned = true;
            _voicing.SetTool(VoicingTool.Chisel);

            // Cut exactly where the hint says, as a careful player would.
            for (int i = 0; i < _voicing.Commission.CutBudget && _voicing.Stage == VoicingStage.Voicing; i++)
            {
                float size = VoicingMath.CutSize(1f, _resonance.Softness, _resonance.Charge);
                if (!VoicingMath.SuggestCut(_voicing.Errors, _voicing.Commission.ToleranceCents, size, out Vector2 uv))
                {
                    break;
                }

                yield return StrikeAt(UvToFace(uv));
            }

            Assert.IsTrue(tuned, "the careful player reaches tune: errors " + _voicing.Errors + " after " + _voicing.CutsUsed + " cuts");
            Assert.AreEqual(VoicingStage.Settling, _voicing.Stage);
            Assert.AreEqual(0, forged, "the relic rings a moment before it is finished");
            Assert.Greater(_voicing.SettleRemaining, 0f);

            for (float waited = 0f; waited < 4f && _voicing.Stage != VoicingStage.Done; waited += Time.unscaledDeltaTime)
            {
                yield return null;
            }

            Assert.AreEqual(VoicingStage.Done, _voicing.Stage);
            Assert.AreEqual(1, forged, "the finished relic is forged exactly once");
            Assert.IsTrue(_resonance.IsForged);
            Assert.Greater(_voicing.Quality, 0.6f);
            Assert.AreEqual(_voicing.Quality, _resonance.MeanQuality, 1e-4f);
            Assert.AreNotEqual(ForgeGrade.Fractured, result.Grade);
        }

        [UnityTest]
        public IEnumerator AnotherCut_WhileSettling_CancelsTheSettle()
        {
            yield return null;
            _voicing.SetTool(VoicingTool.Chisel);
            for (int i = 0; i < _voicing.Commission.CutBudget && _voicing.Stage == VoicingStage.Voicing; i++)
            {
                float size = VoicingMath.CutSize(1f, _resonance.Softness, _resonance.Charge);
                if (!VoicingMath.SuggestCut(_voicing.Errors, _voicing.Commission.ToleranceCents, size, out Vector2 uv))
                {
                    break;
                }

                yield return StrikeAt(UvToFace(uv));
            }

            Assert.AreEqual(VoicingStage.Settling, _voicing.Stage);
            int before = _voicing.CutsUsed;
            yield return StrikeAt(new Vector2(0f, 0f));
            Assert.AreEqual(before + 1, _voicing.CutsUsed);
            Assert.AreNotEqual(VoicingStage.Done, _voicing.Stage, "a further cut keeps the relic in the player's hands");
        }

        [UnityTest]
        public IEnumerator WhenTheMetalIsSpent_TheRelicIsFinishedAsItStands()
        {
            _voicing.Begin(5);
            yield return null;
            int forged = 0;
            ForgeEvents.RelicForged += payload => forged++;
            _voicing.SetTool(VoicingTool.Chisel);

            // Cuts at the centre of the plate are on every node: they change nothing but use the metal up.
            int budget = _voicing.Commission.CutBudget;
            for (int i = 0; i < budget; i++)
            {
                yield return StrikeAt(Vector2.zero);
            }

            Assert.AreEqual(budget, _voicing.CutsUsed);
            Assert.AreEqual(0, _voicing.CutsLeft);
            Assert.AreEqual(VoicingStage.Settling, _voicing.Stage);
            Assert.IsTrue(_voicing.IsSpent);
            Assert.Less((_voicing.Commission.StartCents - _voicing.Errors).magnitude, 0.01f, "nothing was lowered");

            for (float waited = 0f; waited < 4f && _voicing.Stage != VoicingStage.Done; waited += Time.unscaledDeltaTime)
            {
                yield return null;
            }

            Assert.AreEqual(VoicingStage.Done, _voicing.Stage);
            Assert.AreEqual(1, forged);
            Assert.Less(_voicing.Quality, 0.3f, "an untuned relic earns little");
            Assert.AreEqual(Vector4.zero, _voicing.ApplyCut(new Vector2(0.2f, 0.2f), 1f, 1f, 0f), "no cut once the relic is done");
        }
    }
}
