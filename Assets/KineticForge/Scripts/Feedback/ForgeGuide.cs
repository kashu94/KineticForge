using System;
using System.Globalization;
using System.Text;
using KineticForge.Core;
using KineticForge.Crafting;
using TMPro;
using UnityEngine;

namespace KineticForge.Feedback
{
    /// <summary>
    /// The in-world guide: a floating text panel above the forge that teaches the game as it is played. It walks the
    /// player through settling in, bracing, pointing and striking, then teaches the pulse, warns about cooling metal,
    /// excess charge and cracks, keeps a compact status line during forging, and presents the grade at the end, where a
    /// pinch forges another relic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Text is TextMesh Pro, set in Inter with Cinzel titles, sized so one line is <see cref="LineHeight"/> meters tall
    /// whatever the font. A <see cref="ForgePanel"/> behind it fits a glass plate to the text. Each new step fades in.
    /// </para>
    /// <para>
    /// The guide reads state every frame but rebuilds its text only when what it shows changes, so it allocates a few
    /// small strings per second at most and none while nothing changes. Instructions match the platform: hands on Quest,
    /// keyboard and mouse in the Editor.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(TextMeshPro))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Feedback/Forge Guide")]
    public sealed class ForgeGuide : MonoBehaviour
    {
        /// <summary>Name of the display font asset used for titles, as TMP's &lt;font&gt; tag expects it.</summary>
        public const string TitleFont = "Cinzel SDF";

        const float TextScaleFactor = 0.1f;
        const int StageCount = 4;
        const float VerdictSeconds = 3f;

        /// <summary>Strikes the relic takes before the first lesson gives way to the tuning lesson.</summary>
        const int VoicingRingStrikes = 3;

        [SerializeField, Tooltip("Game flow to follow. Found in the scene when empty.")]
        ForgeGameManager _game = null;

        [SerializeField, Tooltip("Relic to report on. Found in the scene when empty.")]
        RelicResonance _relic = null;

        [SerializeField, Range(0.01f, 0.1f), Tooltip("Height (m) of one line of text.")]
        float _lineHeight = 0.032f;

        [SerializeField, Min(0f), Tooltip("Seconds on the completion screen before a pinch forges again.")]
        float _forgeAgainDelay = 3f;

        [SerializeField, Range(0, 12), Tooltip("Strikes after which the pulse lesson gives way to the status line.")]
        int _lessonStrikes = 4;

        [SerializeField, Range(0f, 1f), Tooltip("Seconds a new step takes to fade in.")]
        float _fadeIn = 0.22f;

        readonly StringBuilder _builder = new StringBuilder(256);
        TMP_Text _text;
        float _shownTime = -100f;
        int _shownKey = int.MinValue;
        int _shownStep = int.MinValue;
        int _strikes;
        float _lastCrackTime = -100f;
        bool _forged;
        int _carveStrikes;
        float _sigilSetTime = -100f;
        float _quenchedTime = -100f;
        RelicSigil _sigil;
        RelicVoicing _voicing;
        RelicQuench _quench;
        Action _onSigilSet;
        Action _onQuenched;
        RelicForgedEvent _result;
        Action<RelicResonanceEvent> _onResonated;
        Action<RelicForgedEvent> _onForged;
        Action<GamePhaseChangedEvent> _onPhaseChanged;
        Action<MicroGestureTapEvent> _onTap;

        /// <summary>Height (m) of one line of text.</summary>
        public float LineHeight => _lineHeight;

        /// <summary>The text currently shown.</summary>
        public string Text => _text != null ? _text.text : string.Empty;

        void Awake()
        {
            _text = GetComponent<TMP_Text>();
            _onResonated = HandleResonated;
            _onForged = HandleForged;
            _onPhaseChanged = HandlePhaseChanged;
            _onTap = HandleTap;
            _onSigilSet = () => _sigilSetTime = Time.time;
            _onQuenched = () => _quenchedTime = Time.time;
            _text.richText = true;
            _text.alignment = TextAlignmentOptions.Center;
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            ApplyLineHeight();
        }

        void OnEnable()
        {
            ForgeEvents.RelicResonated += _onResonated;
            ForgeEvents.RelicForged += _onForged;
            ForgeEvents.GamePhaseChanged += _onPhaseChanged;
            ForgeEvents.MicroGestureTap += _onTap;
        }

        void OnDisable()
        {
            ForgeEvents.RelicResonated -= _onResonated;
            ForgeEvents.RelicForged -= _onForged;
            ForgeEvents.GamePhaseChanged -= _onPhaseChanged;
            ForgeEvents.MicroGestureTap -= _onTap;
        }

        void Start()
        {
            if (_game == null)
            {
                _game = FindAnyObjectByType<ForgeGameManager>();
            }

            if (_relic == null)
            {
                _relic = FindAnyObjectByType<RelicResonance>();
            }

            if (_relic != null)
            {
                _voicing = _relic.GetComponent<RelicVoicing>();
                _sigil = _relic.GetComponent<RelicSigil>();
                _quench = _relic.GetComponent<RelicQuench>();
                if (_sigil != null)
                {
                    _sigil.OnSigilSet += _onSigilSet;
                }

                if (_quench != null)
                {
                    _quench.OnQuenched += _onQuenched;
                }
            }
        }

        void OnDestroy()
        {
            if (_sigil != null)
            {
                _sigil.OnSigilSet -= _onSigilSet;
            }

            if (_quench != null)
            {
                _quench.OnQuenched -= _onQuenched;
            }
        }

        void LateUpdate()
        {
            int key = ComputeKey();
            if (key != _shownKey)
            {
                // A new step fades in; live readouts within a step (counts, meters) update in place.
                int step = ComputeStepKey();
                if (step != _shownStep)
                {
                    _shownStep = step;
                    _shownTime = Time.unscaledTime;
                }

                _shownKey = key;
                _text.text = BuildText();
            }

            float alpha = _fadeIn > 0f ? Mathf.Clamp01((Time.unscaledTime - _shownTime) / _fadeIn) : 1f;
            alpha = alpha * alpha * (3f - 2f * alpha);
            if (!Mathf.Approximately(_text.alpha, alpha))
            {
                _text.alpha = alpha;
            }
        }

        /// <summary>
        /// Identifies the step being taught: the discrete stages only, without the live readouts that
        /// <see cref="ComputeKey"/> also tracks, so a readout changing never restarts the fade.
        /// </summary>
        int ComputeStepKey()
        {
            unchecked
            {
                int key = 23;
                if (_game != null)
                {
                    key = key * 31 + (int)_game.Phase;
                    key = key * 31 + (int)_game.CurrentCalibrationStep;
                    key = key * 31 + (_game.Phase == ForgePhase.Completion && _game.PhaseElapsed >= _forgeAgainDelay ? 1 : 0);
                }

                key = key * 31 + Mathf.Min(_carveStrikes, _lessonStrikes + 1);
                key = key * 31 + Mathf.Min(_strikes, 1);
                if (_voicing != null)
                {
                    // The voicing steps: pour, ring, tune, settle, spent.
                    key = key * 31 + (int)_voicing.Stage;
                    key = key * 31 + (_voicing.Suspended ? 1 : 0);
                    key = key * 31 + (_strikes >= VoicingRingStrikes ? 1 : 0);
                    key = key * 31 + (_voicing.CutsUsed > 0 ? 1 : 0);
                    key = key * 31 + (_voicing.IsSpent ? 1 : 0);
                }

                if (_sigil != null)
                {
                    key = key * 31 + (int)_sigil.Stage;
                    key = key * 31 + (_sigil.Hint > 0f ? 1 : 0);
                    key = key * 31 + (_sigil.Suspended ? 1 : 0);
                    key = key * 31 + (Time.time - _sigilSetTime < 3f ? 1 : 0);
                }

                if (_quench != null)
                {
                    key = key * 31 + (int)_quench.Stage;
                    key = key * 31 + (Time.time - _quenchedTime >= 1.5f ? 1 : 0);
                }

                key = key * 31 + (_forged ? 1 : 0);
                key = key * 31 + (Time.time - _lastCrackTime < 3f ? 1 : 0);
                return key;
            }
        }

        #region Events

        void HandleResonated(RelicResonanceEvent payload)
        {
            _strikes++;
            if (_sigil == null || _sigil.Stage == SigilStage.Set)
            {
                _carveStrikes++;
            }

            if (payload.Cracked)
            {
                _lastCrackTime = Time.time;
            }
        }

        void HandleForged(RelicForgedEvent payload)
        {
            _forged = true;
            _result = payload;
        }

        void HandlePhaseChanged(GamePhaseChangedEvent payload)
        {
            if (payload.Current == ForgePhase.FirstRelicForging)
            {
                _strikes = 0;
                _carveStrikes = 0;
                _sigilSetTime = -100f;
                _quenchedTime = -100f;
                _forged = false;
                _lastCrackTime = -100f;
            }
        }

        void HandleTap(MicroGestureTapEvent payload)
        {
            if (_game == null || _game.Phase != ForgePhase.Completion || _game.PhaseElapsed < _forgeAgainDelay)
            {
                return;
            }

            // On the result screen a pinch first quenches the relic, then, once it has sung, forges the next one.
            if (_quench != null && _quench.IsAvailable)
            {
                _quench.Begin();
            }
            else if (_quench == null || (_quench.Stage == QuenchStage.Done && Time.time - _quenchedTime >= 1.5f))
            {
                _game.ForgeAgain();
            }
        }

        #endregion

        #region Text

        /// <summary>A fingerprint of everything the text shows, so it is rebuilt only on change.</summary>
        int ComputeKey()
        {
            unchecked
            {
                int key = 17;
                ForgePhase phase = _game != null ? _game.Phase : ForgePhase.FirstRelicForging;
                key = key * 31 + (int)phase;
                if (_game != null)
                {
                    key = key * 31 + (int)_game.CurrentCalibrationStep;
                    key = key * 31 + Mathf.RoundToInt(_game.BraceProgress * 10f);
                    key = key * 31 + (_game.Phase == ForgePhase.Completion && _game.PhaseElapsed >= _forgeAgainDelay ? 1 : 0);
                }

                key = key * 31 + Mathf.Min(_carveStrikes, _lessonStrikes + 1);
                key = key * 31 + Mathf.Min(_strikes, 1);
                if (_voicing != null)
                {
                    key = key * 31 + (int)_voicing.Stage;
                    key = key * 31 + (int)_voicing.Tool;
                    key = key * 31 + (_voicing.Suspended ? 1 : 0);
                    key = key * 31 + Mathf.Min(_strikes, VoicingRingStrikes);
                    key = key * 31 + Mathf.Min(_voicing.CutsUsed, 2);
                    key = key * 31 + (_voicing.HintActive ? 1 : 0);
                    key = key * 31 + (_voicing.IsSpent ? 1 : 0);
                    key = key * 31 + VoiceStateMask();
                    key = key * 31 + (Time.time - _voicing.LastVerdictTime < VerdictSeconds ? (int)_voicing.LastVerdict : 0);
                    key = key * 31 + _voicing.RemeltsLeft;
                    key = key * 31 + (_voicing.CanRemelt ? 1 : 0);
                }

                if (_sigil != null)
                {
                    key = key * 31 + (int)_sigil.Stage;
                    key = key * 31 + Mathf.RoundToInt(_sigil.Match * 20f);
                    key = key * 31 + (_sigil.Hint > 0f ? 1 : 0);
                    key = key * 31 + (_sigil.Suspended ? 1 : 0);
                    key = key * 31 + (Time.time - _sigilSetTime < 3f ? 1 : 0);
                }

                if (_quench != null)
                {
                    key = key * 31 + (int)_quench.Stage;
                    key = key * 31 + (Time.time - _quenchedTime >= 1.5f ? 1 : 0);
                }
                key = key * 31 + (_forged ? 1 : 0);
                key = key * 31 + (Time.time - _lastCrackTime < 3f ? 1 : 0);
                if (_relic != null)
                {
                    key = key * 31 + Mathf.RoundToInt(_relic.Heat * 20f);
                    key = key * 31 + Mathf.RoundToInt(_relic.Charge * 10f);
                    key = key * 31 + _relic.Streak;
                    key = key * 31 + _relic.CarvedCount;
                    key = key * 31 + (_relic.Integrity > 0f ? Mathf.RoundToInt(_relic.Integrity * 4f) : -1);
                    key = key * 31 + (StanceWarning() ? 1 : 0);
                }

                return key;
            }
        }

        string BuildText()
        {
            StringBuilder b = _builder;
            b.Length = 0;
            bool hands = ForgePlatform.IsMetaRuntimeAvailable;
            ForgePhase phase = _game != null ? _game.Phase : ForgePhase.FirstRelicForging;
            switch (phase)
            {
                case ForgePhase.None:
                case ForgePhase.Intro:
                    Title(b, "KINETIC FORGE");
                    b.Append("\nThe relic rings with every strike.\nYour body is its anchor.");
                    break;

                case ForgePhase.Calibration:
                    BuildCalibration(b, hands);
                    break;

                case ForgePhase.FirstRelicForging:
                    BuildForging(b, hands);
                    break;

                case ForgePhase.Completion:
                    BuildCompletion(b, hands);
                    break;
            }

            return b.ToString();
        }

        void BuildCalibration(StringBuilder b, bool hands)
        {
            CalibrationStep step = _game != null ? _game.CurrentCalibrationStep : CalibrationStep.None;
            if (step == CalibrationStep.AwaitingBrace)
            {
                Title(b, "BRACE YOURSELF");
                b.Append(hands
                    ? "\nLean toward the relic and hold still.\nSeated or standing, plant yourself.\n\n"
                    : "\nHold Space (or press B) to brace.\n\n");
                Bar(b, _game != null ? _game.BraceProgress : 0f);
                return;
            }

            Title(b, "SETTLE IN");
            b.Append(step == CalibrationStep.SamplingNeutral
                ? "\nHold still...\nReading your posture."
                : "\nSit or stand comfortably,\nrelaxed, facing the anvil.");
        }

        void BuildForging(StringBuilder b, bool hands)
        {
            if (_relic != null && _relic.Integrity <= 0f)
            {
                Title(b, "THE RELIC FRACTURED");
                b.Append("\nFinish the rune anyway: every mark still counts.");
                AppendStatus(b);
                return;
            }

            if (Time.time - _lastCrackTime < 3f)
            {
                b.Append("<color=#ff6a30><b>CRACK!</b></color>\nToo much charge through a loose stance.\nBrace before charging cold metal.");
                AppendStatus(b);
                return;
            }

            if (_voicing != null && _voicing.Stage != VoicingStage.Dormant && _voicing.Stage != VoicingStage.Done)
            {
                BuildVoicing(b, hands);
                return;
            }

            if (_sigil != null && _sigil.Stage == SigilStage.Awakening)
            {
                BuildAwakening(b, hands);
                return;
            }

            if (_sigil != null && Time.time - _sigilSetTime < 3f)
            {
                Step(b, 1);
                Title(b, "SIGIL SET: " + _sigil.Sigil.Name.ToUpperInvariant());
                b.Append("\nThe relic remembers its figure.\nCarve the rune along the sand.");
                return;
            }

            if (_carveStrikes == 0)
            {
                Step(b, 2);
                Title(b, "CARVE THE RUNE");
                b.Append(hands
                    ? "\n<b>Point</b> at the glowing mark.\n<b>Pinch</b> to strike - or tap thumb to index finger."
                    : "\n<b>Right-drag</b> to aim at the glowing mark.\n<b>Left-click</b> to strike.");
                return;
            }

            if (_carveStrikes <= _lessonStrikes)
            {
                Step(b, 2);
                Title(b, "RIDE THE PULSE");
                b.Append("\nThe relic has a heartbeat.\nStrike as the ring <color=#ffc860>closes and turns gold</color>:\nin rhythm, it charges and carves deeper.");
                AppendStatus(b);
                return;
            }

            if (StanceWarning())
            {
                b.Append("<color=#7fc8ff><b>CHARGE IS HIGH</b></color>\nBrace, or the relic will crack.");
            }
            else if (_relic != null && _relic.Heat < 0.3f)
            {
                b.Append("<color=#ff9a50><b>THE METAL IS COOLING</b></color>\nKeep the rhythm to keep it hot.\nCold metal is brittle.");
            }
            else
            {
                b.Append(_relic != null && _relic.Streak >= 3 ? "<color=#ffc860><b>IN RHYTHM</b></color>" : "<b>CARVE THE RUNE</b>");
            }

            AppendStatus(b);
        }

        /// <summary>The steps of voicing: the pour, ringing the relic, tuning it, settling, and a spent relic.</summary>
        void BuildVoicing(StringBuilder b, bool hands)
        {
            if (_voicing.Suspended)
            {
                Title(b, "THE POUR");
                b.Append("\nMolten bronze fills the mold.");
                return;
            }

            VoicingCommission commission = _voicing.Commission;
            if (_voicing.Stage == VoicingStage.Settling)
            {
                if (_voicing.IsSpent)
                {
                    b.Append("<color=#ff7a6a><b>THE METAL IS SPENT</b></color>\nThe relic is finished as it stands.");
                }
                else
                {
                    b.Append("<color=#8dffa6><b>IN TUNE</b></color>\nHold still: it rings true.\nCut again to refine it.");
                }

                return;
            }

            if (_voicing.CutsUsed == 0 && _strikes < VoicingRingStrikes && commission.Index != 0)
            {
                Step(b, 1);
                Title(b, "RING THE RELIC");
                b.Append("\n").Append(commission.ChordName).Append(" in ").Append(commission.RootName)
                    .Append(": four voices, all ringing sharp.\nStrike it. Every spot rings different voices.")
                    .Append(hands ? "\n<b>Touch</b> the relic, or <b>point</b> and <b>pinch</b>: watch the tuner and the sand." : "\n<b>Right-drag</b> to aim, <b>left-click</b> to strike.");
                return;
            }

            Step(b, commission.Index == 0 && _strikes < VoicingRingStrikes ? 1 : 2);
            Title(b, "TUNE THE VOICES");
            b.Append("\n<size=88%>Goal: every needle inside the <color=#8dffa6>green band</color></size>");
            if (commission.Index == 0)
            {
                AppendLesson(b, hands);
            }
            else if (_voicing.CutsUsed == 0)
            {
                b.Append("\nCutting lowers pitch where the metal moves most:\nthe <b>bare patches</b>, never the sand.");
            }
            else
            {
                AppendVoiceStates(b, commission);
            }

            AppendVerdict(b);
            if (commission.Index != 0)
            {
                if (_voicing.Tool == VoicingTool.Hammer)
                {
                    b.Append(hands ? "\n<color=#ffb060>Tap your left thumb for the chisel.</color>" : "\n<color=#ffb060>Press T for the chisel.</color>");
                }
                else if (_voicing.Tool == VoicingTool.Torch)
                {
                    b.Append("\n<color=#ff7a50>Torch: strike to undo your last cut.</color>");
                }
                else
                {
                    b.Append(_voicing.HintActive
                        ? "\n<color=#ffc860>The ring marks a good place to cut.</color>"
                        : "\nThe ghost on each gauge shows where a cut would take it.\nEvery cut is final. Listen for the wobble to fade.");
                }

                if (_voicing.CutsUsed >= 2 && _voicing.Tool != VoicingTool.Torch)
                {
                    b.Append(hands
                        ? "\n<size=88%><color=#9fc8e0>Tip: press a fingertip of your other hand on the relic to mute the voices there.</color></size>"
                        : "\n<size=88%><color=#9fc8e0>Tip: hold D to mute the voices at the aim point.</color></size>");
                }
            }
        }

        /// <summary>The first relic's lesson: a checklist with the current step marked, then what to do next.</summary>
        void AppendLesson(StringBuilder b, bool hands)
        {
            bool rang = _strikes >= VoicingRingStrikes;
            bool chisel = _voicing.Tool != VoicingTool.Hammer || _voicing.CutsUsed > 0;
            bool cut = _voicing.CutsUsed > 0;
            int current = !rang ? 1 : !chisel ? 2 : !cut ? 3 : 4;
            b.Append('\n');
            LessonLine(b, 1, current, rang, "Ring the relic: " + Mathf.Min(_strikes, VoicingRingStrikes) + " of " + VoicingRingStrikes);
            LessonLine(b, 2, current, chisel, hands ? "Pick the chisel: tap your left thumb" : "Pick the chisel: press T");
            LessonLine(b, 3, current, cut, "Cut where the ring points");
            if (current == 4)
            {
                AppendVoiceStates(b, _voicing.Commission);
                b.Append(_voicing.Tool == VoicingTool.Torch
                    ? "\n<color=#ff7a50>Torch: strike to undo your last cut.</color>"
                    : _voicing.CanRemelt && _voicing.Tool == VoicingTool.Chisel
                        ? (hands ? "\n<size=88%>Cut too far? Tap your left thumb for the torch.</size>" : "\n<size=88%>Cut too far? Press T for the torch.</size>")
                        : string.Empty);
            }
            else if (current == 3)
            {
                b.Append(hands
                    ? "\n<size=88%>Hover a finger over the relic: each gauge's ghost follows it.</size>"
                    : "\n<size=88%>Aim: each gauge's ghost shows where a cut would take it.</size>");
            }
        }

        static void LessonLine(StringBuilder b, int number, int current, bool done, string text)
        {
            b.Append('\n');
            if (done)
            {
                b.Append("<color=#8dffa6>[x] ").Append(text).Append("</color>");
            }
            else if (number == current)
            {
                b.Append("<color=#ffc860><b>[ ] ").Append(text).Append("</b></color>");
            }
            else
            {
                b.Append("<color=#8a7f75>[ ] ").Append(text).Append("</color>");
            }
        }

        /// <summary>How the last cut went, for three seconds after it.</summary>
        void AppendVerdict(StringBuilder b)
        {
            if (Time.time - _voicing.LastVerdictTime >= VerdictSeconds)
            {
                return;
            }

            switch (_voicing.LastVerdict)
            {
                case CutVerdict.Good:
                    b.Append("\n<color=#8dffa6><b>GOOD CUT</b></color>");
                    break;
                case CutVerdict.TooFar:
                    b.Append(_voicing.CanRemelt
                        ? "\n<color=#ff7a6a><b>TOO FAR:</b> a voice went flat. Use the torch to undo it.</color>"
                        : "\n<color=#ff7a6a><b>TOO FAR:</b> a voice went flat, and cuts never raise it.</color>");
                    break;
                case CutVerdict.Wasted:
                    b.Append("\n<color=#ffc860><b>WASTED:</b> that spot barely moves any voice.</color>");
                    break;
                case CutVerdict.Undone:
                    b.Append("\n<color=#ffb060><b>UNDONE:</b> the last cut is re-melted.</color>");
                    break;
            }
        }

        /// <summary>"Lower: Root, Fifth" for the voices still sharp, and a warning for any gone flat.</summary>
        void AppendVoiceStates(StringBuilder b, VoicingCommission commission)
        {
            Vector4 errors = _voicing.Errors;
            float tolerance = commission.ToleranceCents;
            bool any = false;
            for (int m = 0; m < VoicingMath.VoiceCount; m++)
            {
                if (errors[m] > tolerance)
                {
                    b.Append(any ? ", " : "\nStill sharp: <color=#ffc860>").Append(commission.Roles[m]);
                    any = true;
                }
            }

            if (any)
            {
                b.Append("</color>");
            }

            bool flat = false;
            for (int m = 0; m < VoicingMath.VoiceCount; m++)
            {
                if (errors[m] < -tolerance)
                {
                    b.Append(flat ? ", " : "\n<color=#7fa8ff>Too flat, cuts only lower: ").Append(commission.Roles[m]);
                    flat = true;
                }
            }

            if (flat)
            {
                b.Append("</color>");
            }

            if (!any && !flat)
            {
                b.Append("\nEvery voice is within tolerance.");
            }
        }

        /// <summary>Which voices are sharp, flat or in tune, as a base-3 number, for the display key.</summary>
        int VoiceStateMask()
        {
            Vector4 errors = _voicing.Errors;
            float tolerance = _voicing.Commission.ToleranceCents;
            int mask = 0;
            for (int m = 0; m < VoicingMath.VoiceCount; m++)
            {
                mask = mask * 3 + (errors[m] > tolerance ? 1 : errors[m] < -tolerance ? 2 : 0);
            }

            return mask;
        }

        void BuildAwakening(StringBuilder b, bool hands)
        {
            if (_sigil.Suspended)
            {
                Title(b, "THE POUR");
                b.Append("\nMolten bronze fills the mold.");
                return;
            }

            Step(b, 1);
            Title(b, "AWAKEN THE SIGIL");
            b.Append(_strikes == 0
                ? (hands ? "\nPoint at the relic and pinch to strike it.\nWatch the sand." : "\nAim and left-click to strike the relic.\nWatch the sand.")
                : "\nEvery spot rings a different figure.\nFind the one that draws the <color=#7fd8ff>glowing blue sigil</color>.");
            b.Append("\n\n<size=").Append(ScaledSize(0.85f)).Append(">Figure match  ");
            Bar(b, _sigil.Match);
            b.Append("  ").Append(Percent(_sigil.Match)).Append("</size>");
            if (_sigil.Hint > 0f)
            {
                b.Append("\n<color=#ffc860>The ring marks where it wants to be struck.</color>");
            }
        }

        void BuildCompletion(StringBuilder b, bool hands)
        {
            if (_quench != null && _quench.IsBusy)
            {
                Step(b, 3);
                Title(b, "QUENCHING");
                b.Append("\nThe water takes the heat.");
                return;
            }

            if (_quench != null && _quench.Stage == QuenchStage.Done && _forged)
            {
                Step(b, 4);
                Title(b, "THE RELIC SINGS");
                if (_voicing != null)
                {
                    b.Append("\n").Append(_voicing.Commission.Name).Append(", ").Append(_voicing.Commission.ChordName).Append(" in ")
                        .Append(_voicing.Commission.RootName).Append(". Grade ").Append(GradeName(_result.Grade)).Append('.');
                }
                else if (_sigil != null)
                {
                    b.Append("\n").Append(_sigil.Sigil.Name).Append(", in ").Append(_sigil.Sigil.NoteName).Append(". Grade ")
                        .Append(GradeName(_result.Grade)).Append('.');
                }

                b.Append("\nIt joins your Resonance Wall, to your left.\n")
                    .Append(hands ? "Point at a relic there and pinch to ring it." : "Click a relic there to ring it.");
                if (Time.time - _quenchedTime >= 1.5f)
                {
                    b.Append(hands ? "\n\n<color=#ffc860>Pinch to forge the next sigil</color>" : "\n\n<color=#ffc860>Click to forge the next sigil</color>");
                    if (hands)
                    {
                        // Mixed reality is offered once the core loop is learned, never as a first-minute verb.
                        b.Append("\n<size=").Append(ScaledSize(0.75f)).Append("><color=#9fc8e0>Or pinch with both hands to forge in your own room</color></size>");
                    }
                }

                return;
            }

            if (_forged)
            {
                bool fractured = _result.Grade == ForgeGrade.Fractured;
                Step(b, 3);
                Title(b, fractured ? "THE RELIC SHATTERED" : "RELIC FORGED");
                b.Append("\n<size=").Append(ScaledSize(fractured ? 1.2f : 1.8f)).Append("><b>Grade ").Append(GradeName(_result.Grade))
                    .Append("</b></size>\n");
                b.Append("Quality ").Append(Percent(_result.MeanQuality)).Append("   Integrity ")
                    .Append(Percent(_result.Integrity)).Append('\n');
                b.Append("Best rhythm ").Append(_result.BestStreak).Append("   Time ")
                    .Append(_result.Duration.ToString("0", CultureInfo.InvariantCulture)).Append(" s");
            }
            else
            {
                Step(b, 3);
                Title(b, "RELIC FORGED");
            }

            if (_game != null && _game.PhaseElapsed >= _forgeAgainDelay)
            {
                bool quench = _quench != null && _quench.IsAvailable;
                b.Append(hands
                    ? (quench ? "\n\n<color=#ffc860>Pinch to quench the relic</color>" : "\n\n<color=#ffc860>Pinch to forge again</color>")
                    : (quench ? "\n\n<color=#ffc860>Click to quench the relic</color>" : "\n\n<color=#ffc860>Click to forge again</color>"));
            }
        }

        void AppendStatus(StringBuilder b)
        {
            if (_relic == null)
            {
                return;
            }

            int total = 12;
            RelicCarvingSurface surface = _relic.GetComponent<RelicCarvingSurface>();
            if (surface != null)
            {
                total = surface.MarkCount;
            }

            b.Append("\n\n<size=").Append(ScaledSize(0.8f)).Append(">Mark ").Append(Mathf.Min(_relic.CarvedCount + 1, total)).Append('/').Append(total);
            b.Append("    Heat ").Append(Percent(_relic.Heat));
            b.Append("    Charge ").Append(_relic.Charge.ToString("0.0", CultureInfo.InvariantCulture));
            if (_relic.Streak >= 2)
            {
                b.Append("    <color=#ffc860>Rhythm x").Append(_relic.Streak).Append("</color>");
            }

            if (_relic.Integrity < 1f)
            {
                b.Append("    <color=#ff6a30>Integrity ").Append(Percent(_relic.Integrity)).Append("</color>");
            }

            b.Append("</size>");
        }

        bool StanceWarning()
        {
            return _relic != null && _game != null && _relic.Charge > 0.9f && _game.LatestStability < 0.4f;
        }

        static string ScaledSize(float scale)
        {
            // TMP rich text takes relative sizes.
            return Mathf.RoundToInt(scale * 100f).ToString(CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>A small "STEP n OF 4" line above a stage title, so the player always knows where they are.</summary>
        static void Step(StringBuilder b, int step)
        {
            b.Append("<size=58%><cspace=0.14em><color=#b89a78>STEP ").Append(step).Append(" OF ").Append(StageCount)
                .Append("</color></cspace></size>\n");
        }

        static void Title(StringBuilder b, string title)
        {
            b.Append("<font=\"").Append(TitleFont).Append("\"><cspace=0.06em><color=#ffc860>").Append(title)
                .Append("</color></cspace></font>");
        }

        static void Bar(StringBuilder b, float progress)
        {
            const int Segments = 16;
            int filled = Mathf.RoundToInt(Mathf.Clamp01(progress) * Segments);
            b.Append("<color=#ffc860>");
            for (int i = 0; i < filled; i++)
            {
                b.Append('|');
            }

            b.Append("</color><color=#5a4a40>");
            for (int i = filled; i < Segments; i++)
            {
                b.Append('|');
            }

            b.Append("</color>");
        }

        static string Percent(float value)
        {
            return Mathf.RoundToInt(Mathf.Clamp01(value) * 100f).ToString(CultureInfo.InvariantCulture) + "%";
        }

        static string GradeName(ForgeGrade grade)
        {
            return grade == ForgeGrade.Fractured ? "-" : grade.ToString();
        }

        #endregion

        #region Rendering

        void ApplyLineHeight()
        {
            _text.fontSize = FontSizeFor(_text.font, _lineHeight);
        }

        /// <summary>
        /// The TextMesh Pro font size at which one line of 3D text is <paramref name="lineHeight"/> meters tall: TMP scales
        /// points by 0.1 for 3D text, and a line spans the face's line height.
        /// </summary>
        /// <param name="font">Font asset, or null for a typical face.</param>
        /// <param name="lineHeight">Line height in meters.</param>
        /// <returns>The font size to assign.</returns>
        public static float FontSizeFor(TMP_FontAsset font, float lineHeight)
        {
            float ratio = font != null && font.faceInfo.pointSize > 0
                ? font.faceInfo.lineHeight / font.faceInfo.pointSize * Mathf.Max(font.faceInfo.scale, 1e-3f)
                : 1.2f;
            return lineHeight / (TextScaleFactor * ratio);
        }

        #endregion
    }
}
