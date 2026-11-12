using System;
using System.Globalization;
using System.Text;
using KineticForge.Core;
using KineticForge.Crafting;
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
    /// Text is a <see cref="TextMesh"/> rendered with the Forge Text shader, which works in URP and single-pass stereo.
    /// The panel sizes itself to <see cref="LineHeight"/> once its first text has a mesh, whatever the font settings.
    /// </para>
    /// <para>
    /// The guide reads state every frame but rebuilds its text only when what it shows changes, so it allocates a few
    /// small strings per second at most and none while nothing changes. Instructions match the platform: hands on Quest,
    /// keyboard and mouse in the Editor.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(TextMesh))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Feedback/Forge Guide")]
    public sealed class ForgeGuide : MonoBehaviour
    {
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");

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

        readonly StringBuilder _builder = new StringBuilder(256);
        TextMesh _text;
        Renderer _renderer;
        MaterialPropertyBlock _block;
        Texture _fontTexture;
        bool _scaled;
        int _shownKey = int.MinValue;
        int _strikes;
        float _lastCrackTime = -100f;
        bool _forged;
        int _carveStrikes;
        float _sigilSetTime = -100f;
        float _quenchedTime = -100f;
        RelicSigil _sigil;
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
            _text = GetComponent<TextMesh>();
            _renderer = GetComponent<Renderer>();
            _block = new MaterialPropertyBlock();
            _onResonated = HandleResonated;
            _onForged = HandleForged;
            _onPhaseChanged = HandlePhaseChanged;
            _onTap = HandleTap;
            _onSigilSet = () => _sigilSetTime = Time.time;
            _onQuenched = () => _quenchedTime = Time.time;
            _text.richText = true;
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
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
                _shownKey = key;
                _text.text = BuildText();
            }

            SyncFontTexture();
            if (!_scaled)
            {
                FitScale();
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
                    if (hands)
                    {
                        b.Append("\n\n<size=").Append(ScaledSize(0.75f)).Append("><color=#9fc8e0>Pinch with both hands to forge in your room</color></size>");
                    }
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

            if (_sigil != null && _sigil.Stage == SigilStage.Awakening)
            {
                BuildAwakening(b, hands);
                return;
            }

            if (_sigil != null && Time.time - _sigilSetTime < 3f)
            {
                Title(b, "SIGIL SET: " + _sigil.Sigil.Name.ToUpperInvariant());
                b.Append("\nThe relic remembers its figure.\nCarve the rune along the sand.");
                return;
            }

            if (_carveStrikes == 0)
            {
                Title(b, "CARVE THE RUNE");
                b.Append(hands
                    ? "\n<b>Point</b> at the glowing mark.\n<b>Pinch</b> to strike - or tap thumb to index finger."
                    : "\n<b>Right-drag</b> to aim at the glowing mark.\n<b>Left-click</b> to strike.");
                return;
            }

            if (_carveStrikes <= _lessonStrikes)
            {
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

        void BuildAwakening(StringBuilder b, bool hands)
        {
            if (_sigil.Suspended)
            {
                Title(b, "THE POUR");
                b.Append("\nMolten bronze fills the mold.");
                return;
            }

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
                Title(b, "QUENCHING");
                b.Append("\nThe water takes the heat.");
                return;
            }

            if (_quench != null && _quench.Stage == QuenchStage.Done && _forged)
            {
                Title(b, "THE RELIC SINGS");
                if (_sigil != null)
                {
                    b.Append("\n").Append(_sigil.Sigil.Name).Append(", in ").Append(_sigil.Sigil.NoteName).Append(". Grade ")
                        .Append(GradeName(_result.Grade)).Append('.');
                }

                b.Append("\nIt joins your Resonance Wall, to your left.\n")
                    .Append(hands ? "Point at a relic there and pinch to ring it." : "Click a relic there to ring it.");
                if (Time.time - _quenchedTime >= 1.5f)
                {
                    b.Append(hands ? "\n\n<color=#ffc860>Pinch to forge the next sigil</color>" : "\n\n<color=#ffc860>Click to forge the next sigil</color>");
                }

                return;
            }

            if (_forged)
            {
                bool fractured = _result.Grade == ForgeGrade.Fractured;
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

        int ScaledSize(float scale)
        {
            // Legacy rich text takes absolute sizes; scale from the mesh's font size (0 means the font's default).
            int baseSize = _text.fontSize > 0 ? _text.fontSize : (_text.font != null ? _text.font.fontSize : 32);
            return Mathf.Max(1, Mathf.RoundToInt(baseSize * scale));
        }

        static void Title(StringBuilder b, string title)
        {
            b.Append("<color=#ffc860><b>").Append(title).Append("</b></color>");
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

        void SyncFontTexture()
        {
            Font font = _text.font;
            Texture texture = font != null && font.material != null ? font.material.mainTexture : null;
            if (texture == null || texture == _fontTexture)
            {
                return;
            }

            // Dynamic fonts can rebuild their atlas; the shader always samples the current one.
            _fontTexture = texture;
            _renderer.GetPropertyBlock(_block);
            _block.SetTexture(MainTexId, texture);
            _renderer.SetPropertyBlock(_block);
        }

        void FitScale()
        {
            Bounds bounds = _renderer.bounds;
            string text = _text.text;
            if (bounds.size.y <= 1e-5f || string.IsNullOrEmpty(text))
            {
                return;
            }

            int lines = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    lines++;
                }
            }

            float lineHeight = bounds.size.y / lines;
            transform.localScale *= _lineHeight / lineHeight;
            _scaled = true;
        }

        #endregion
    }
}
