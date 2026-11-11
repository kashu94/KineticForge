using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using KineticForge.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace KineticForge.Tests
{
    /// <summary>
    /// Verifies the exception-isolated signal channel behind <see cref="ForgeEvents"/>.
    /// </summary>
    public sealed class ForgeSignalTests
    {
        [Test]
        public void Raise_InvokesHandlersInSubscriptionOrder()
        {
            List<int> calls = new List<int>();
            ForgeSignal<int> signal = new ForgeSignal<int>("Test");
            signal.Add(value => calls.Add(value));
            signal.Add(value => calls.Add(value * 10));

            signal.Raise(2);

            CollectionAssert.AreEqual(new[] { 2, 20 }, calls);
            Assert.AreEqual(2, signal.SubscriberCount);
        }

        [Test]
        public void ThrowingHandler_DoesNotStopTheOthers()
        {
            int hits = 0;
            ForgeSignal<int> signal = new ForgeSignal<int>("Test");
            signal.Add(_ => throw new InvalidOperationException("boom"));
            signal.Add(_ => hits++);

            LogAssert.Expect(LogType.Error, new Regex("HANDLER_EXCEPTION"));
            LogAssert.Expect(LogType.Exception, new Regex("boom"));
            signal.Raise(1);

            Assert.AreEqual(1, hits);
        }

        [Test]
        public void RemovingALaterHandlerDuringRaise_SkipsIt()
        {
            int laterHits = 0;
            ForgeSignal<int> signal = new ForgeSignal<int>("Test");
            Action<int> later = _ => laterHits++;
            signal.Add(_ => signal.Remove(later));
            signal.Add(later);

            signal.Raise(0);
            signal.Raise(0);

            Assert.AreEqual(0, laterHits);
            Assert.AreEqual(1, signal.SubscriberCount);
        }

        [Test]
        public void HandlerAddedDuringRaise_RunsFromTheNextRaise()
        {
            int addedHits = 0;
            bool added = false;
            ForgeSignal signal = new ForgeSignal("Test");
            signal.Add(() =>
            {
                if (!added)
                {
                    added = true;
                    signal.Add(() => addedHits++);
                }
            });

            signal.Raise();
            Assert.AreEqual(0, addedHits);

            signal.Raise();
            Assert.AreEqual(1, addedHits);
        }

        [Test]
        public void ClearDuringRaise_StopsRemainingHandlers()
        {
            int hits = 0;
            ForgeSignal signal = new ForgeSignal("Test");
            signal.Add(() => signal.Clear());
            signal.Add(() => hits++);

            signal.Raise();

            Assert.AreEqual(0, hits);
            Assert.AreEqual(0, signal.SubscriberCount);
        }

        [Test]
        public void DuplicateHandler_IsRemovedOneAtATime()
        {
            int hits = 0;
            ForgeSignal signal = new ForgeSignal("Test");
            Action handler = () => hits++;
            signal.Add(handler);
            signal.Add(handler);
            signal.Remove(handler);

            signal.Raise();

            Assert.AreEqual(1, hits);
        }

        [Test]
        public void NestedRaise_DeliversToEveryHandler()
        {
            List<int> calls = new List<int>();
            ForgeSignal<int> signal = new ForgeSignal<int>("Test");
            signal.Add(value =>
            {
                calls.Add(value);
                if (value == 0)
                {
                    signal.Raise(1);
                }
            });

            signal.Raise(0);

            CollectionAssert.AreEqual(new[] { 0, 1 }, calls);
        }

        [Test]
        public void ResetSubscriptions_ClearsTheBus()
        {
            int hits = 0;
            Action<ForgeProgressEvent> handler = _ => hits++;
            ForgeEvents.ForgeProgressChanged += handler;

            ForgeEvents.ResetSubscriptions();
            ForgeEvents.RaiseForgeProgressChanged(new ForgeProgressEvent(1, 2));

            Assert.AreEqual(0, hits);
        }
    }

    /// <summary>
    /// Verifies <see cref="ForgeGameManager"/> transitions, queuing and pausing. Edit Mode does not call
    /// <c>Awake</c> or <c>OnEnable</c>, so these tests drive the public API directly.
    /// </summary>
    public sealed class ForgeGameManagerTests
    {
        GameObject _gameObject;
        ForgeGameManager _manager;
        List<GamePhaseChangedEvent> _phases;
        Action<GamePhaseChangedEvent> _recordPhase;
        float _originalTimeScale;

        [SetUp]
        public void SetUp()
        {
            ForgeEvents.ResetSubscriptions();
            _originalTimeScale = Time.timeScale;
            _gameObject = new GameObject("Forge Game Manager (test)");
            _manager = _gameObject.AddComponent<ForgeGameManager>();
            _phases = new List<GamePhaseChangedEvent>();
            _recordPhase = payload => _phases.Add(payload);
            ForgeEvents.GamePhaseChanged += _recordPhase;
        }

        [TearDown]
        public void TearDown()
        {
            _manager.SetManualPause(false);
            Object.DestroyImmediate(_gameObject);
            ForgeEvents.ResetSubscriptions();
            Time.timeScale = _originalTimeScale;
            AudioListener.pause = false;
        }

        [Test]
        public void AdvancePhase_WalksTheLoopInOrder()
        {
            for (int i = 0; i < 5; i++)
            {
                _manager.AdvancePhase();
            }

            ForgePhase[] expected =
            {
                ForgePhase.Intro, ForgePhase.Calibration, ForgePhase.FirstRelicForging, ForgePhase.Completion,
                ForgePhase.Intro,
            };
            Assert.AreEqual(expected.Length, _phases.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], _phases[i].Current);
                Assert.AreEqual(i == 0 ? ForgePhase.None : expected[i - 1], _phases[i].Previous);
            }
        }

        [Test]
        public void Calibration_StartsWithLeadInAndClearsOnExit()
        {
            List<CalibrationStep> steps = new List<CalibrationStep>();
            ForgeEvents.CalibrationStepChanged += step => steps.Add(step);

            _manager.AdvancePhase();
            _manager.AdvancePhase();
            Assert.AreEqual(CalibrationStep.LeadIn, _manager.CurrentCalibrationStep);

            _manager.AdvancePhase();
            CollectionAssert.AreEqual(new[] { CalibrationStep.LeadIn, CalibrationStep.None }, steps);
        }

        [Test]
        public void TransitionRequestedDuringPhaseChange_IsQueuedNotNested()
        {
            ForgeEvents.GamePhaseChanged += payload =>
            {
                if (payload.Current == ForgePhase.Intro)
                {
                    _manager.AdvancePhase();
                }
            };

            _manager.StartExperience();

            Assert.AreEqual(ForgePhase.Calibration, _manager.Phase);
            Assert.AreEqual(2, _phases.Count);
            Assert.AreEqual(ForgePhase.Intro, _phases[0].Current);
            Assert.AreEqual(ForgePhase.Intro, _phases[1].Previous);
        }

        [Test]
        public void EnteringForging_PublishesZeroProgress()
        {
            ForgeProgressEvent? progress = null;
            ForgeEvents.ForgeProgressChanged += payload => progress = payload;

            _manager.AdvancePhase();
            _manager.AdvancePhase();
            _manager.AdvancePhase();

            Assert.IsTrue(progress.HasValue);
            Assert.AreEqual(0, progress.Value.QualifiedStrikes);
            Assert.AreEqual(_manager.RequiredStrikes, progress.Value.RequiredStrikes);
            Assert.IsFalse(progress.Value.IsComplete);
        }

        [Test]
        public void ManualPause_FreezesAndRestoresTimeAndAudio()
        {
            List<ExperiencePauseEvent> pauses = new List<ExperiencePauseEvent>();
            ForgeEvents.ExperiencePauseChanged += payload => pauses.Add(payload);
            Time.timeScale = 0.8f;

            _manager.SetManualPause(true);
            Assert.IsTrue(_manager.IsPaused);
            Assert.AreEqual(0f, Time.timeScale);
            Assert.IsTrue(AudioListener.pause);

            _manager.SetManualPause(false);
            Assert.IsFalse(_manager.IsPaused);
            Assert.AreEqual(0.8f, Time.timeScale, 1e-6f);
            Assert.IsFalse(AudioListener.pause);

            Assert.AreEqual(2, pauses.Count);
            Assert.IsTrue(pauses[0].IsPaused);
            Assert.AreEqual(ForgePauseReason.Manual, pauses[0].Reasons);
            Assert.IsFalse(pauses[1].IsPaused);
        }

        [Test]
        public void RepeatedManualPause_IsIdempotent()
        {
            int changes = 0;
            ForgeEvents.ExperiencePauseChanged += _ => changes++;

            _manager.SetManualPause(true);
            _manager.SetManualPause(true);
            _manager.SetManualPause(false);
            _manager.SetManualPause(false);

            Assert.AreEqual(2, changes);
        }
    }
}
