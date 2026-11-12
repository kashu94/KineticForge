using System.Collections;
using KineticForge.Core;
using KineticForge.Crafting;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KineticForge.Tests
{
    /// <summary>The cymatic sigil: strikes ring without carving until the figure is found, then its rune is carved.</summary>
    public sealed class SigilPlayTests
    {
        GameObject _relic;
        RelicCarvingSurface _surface;
        CarvingCursor _cursor;
        ChiselStrikeController _strikes;
        RelicSigil _sigil;

        [SetUp]
        public void SetUp()
        {
            ForgeEvents.ResetSubscriptions();
            _relic = new GameObject("Sigil Relic (test)");
            _relic.transform.position = new Vector3(0f, 80f, 0f);
            _relic.AddComponent<MeshFilter>();
            _relic.AddComponent<MeshRenderer>();
            _relic.AddComponent<BoxCollider>().size = new Vector3(0.3f, 0.3f, 0.08f);
            _relic.AddComponent<Rigidbody>().isKinematic = true;
            _surface = _relic.AddComponent<RelicCarvingSurface>();
            _cursor = _relic.AddComponent<CarvingCursor>();
            _strikes = _relic.AddComponent<ChiselStrikeController>();
            _relic.AddComponent<RelicResonance>();
            _sigil = _relic.AddComponent<RelicSigil>();
            _sigil.PersistProgress = false;
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

        [UnityTest]
        public IEnumerator RingingTheSeed_SetsTheSigil_ThenItsRuneCarves()
        {
            int chiselStrikes = 0;
            ForgeEvents.ChiselStrike += payload => chiselStrikes++;
            yield return null;
            Assert.AreEqual(SigilStage.Awakening, _sigil.Stage);
            Assert.IsFalse(_surface.IsAcceptingStrikes, "carving is gated while the sigil awakens");
            Assert.IsTrue(_surface.IsAimable);

            Vector2 seed = _sigil.Sigil.Seed;
            Vector2 face = new Vector2((seed.x - 0.5f) / _surface.UsableArea, (seed.y - 0.5f) / _surface.UsableArea);
            Vector2 defaultFirstMark = _surface.GetMarkCoordinates(0);
            for (int i = 0; i < 6 && _sigil.Stage == SigilStage.Awakening; i++)
            {
                yield return StrikeAt(face);
                for (float waited = 0f; waited < 1f && _sigil.Stage == SigilStage.Awakening; waited += Time.deltaTime)
                {
                    yield return null;
                }
            }

            Assert.AreEqual(SigilStage.Set, _sigil.Stage, "ringing the seed spot sets the sigil");
            Assert.AreEqual(0, chiselStrikes, "awakening strikes never count as chisel strikes");
            Assert.IsTrue(_surface.IsAcceptingStrikes);
            Assert.AreNotEqual(defaultFirstMark, _surface.GetMarkCoordinates(0), "the rune follows the figure");
            Assert.Greater(_sigil.BestMatch, 0.88f);

            yield return StrikeAt(_surface.GetMarkCoordinates(_surface.ActiveIndex));
            Assert.AreEqual(1, _surface.StruckCount, "the sigil's rune carves");
            Assert.AreEqual(1, chiselStrikes);
        }

        [UnityTest]
        public IEnumerator StrikingElsewhere_DoesNotSetTheSigil()
        {
            yield return null;
            Vector2 seed = _sigil.Sigil.Seed;

            // The face point farthest in figure from the seed.
            Vector2 worst = Vector2.zero;
            float worstMatch = 2f;
            for (int u = 0; u <= 10; u++)
            {
                for (int v = 0; v <= 10; v++)
                {
                    Vector2 uv = new Vector2(0.1f + 0.08f * u, 0.1f + 0.08f * v);
                    float match = CymaticMath.Match(CymaticMath.FigureFromStrike(uv), _sigil.Target);
                    if (match < worstMatch && CymaticMath.FigureFromStrike(uv).sqrMagnitude > 0.5f)
                    {
                        worstMatch = match;
                        worst = uv;
                    }
                }
            }

            Assert.Greater(Vector2.Distance(worst, seed), 0.05f);
            Vector2 face = new Vector2((worst.x - 0.5f) / _surface.UsableArea, (worst.y - 0.5f) / _surface.UsableArea);
            for (int i = 0; i < 4; i++)
            {
                yield return StrikeAt(face);
            }

            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(SigilStage.Awakening, _sigil.Stage);
            Assert.IsFalse(_surface.IsAcceptingStrikes);
        }
    }
}
