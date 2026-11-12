using System;
using System.Globalization;
using KineticForge.Core;
using UnityEngine;

namespace KineticForge.Crafting
{
    /// <summary>Stage of a relic's sigil.</summary>
    public enum SigilStage : byte
    {
        /// <summary>Not forging.</summary>
        Dormant = 0,

        /// <summary>The player rings the relic to find the spot whose Chladni figure matches the sigil.</summary>
        Awakening = 1,

        /// <summary>The figure is set and held; its rune is being carved.</summary>
        Set = 2,
    }

    /// <summary>
    /// The cymatic sigil puzzle: each relic must first be rung into its sigil's Chladni figure, and only then can the
    /// rune that figure draws be carved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// While awakening, carving is gated (<see cref="RelicCarvingSurface.SetGate"/>) and strikes only ring the relic. The
    /// sand and the shader show the figure the relic rings; a faint blue ghost shows the target. When the ringing
    /// figure matches the target closely enough (<see cref="CymaticMath.Match"/>) for long enough, the sigil sets: the
    /// relic holds the figure, a rune of marks along its nodal lines replaces the old one
    /// (<see cref="CymaticMath.GenerateRune"/>), and carving opens. If the player has not found the spot after a while,
    /// <see cref="Hint"/> rises, which the resonance ring uses to show where to strike.
    /// </para>
    /// <para>
    /// The next sigil in the library is chosen each time forging starts, and the choice persists between sessions, so
    /// every relic carries a new figure and a new note. Without a game flow in the scene, awakening starts on Start.
    /// The per-frame path does not allocate; rune generation allocates once per relic.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(RelicCarvingSurface), typeof(RelicResonance))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Crafting/Relic Sigil")]
    public sealed class RelicSigil : MonoBehaviour
    {
        const string LogSource = nameof(RelicSigil);
        const string NextSigilKey = "KineticForge.NextSigil";

        [SerializeField, Range(0.6f, 0.99f), Tooltip("Figure match (absolute cosine) the relic must ring to set the sigil.")]
        float _matchThreshold = 0.88f;

        [SerializeField, Range(0.05f, 3f), Tooltip("Seconds the match must hold.")]
        float _holdSeconds = 0.5f;

        [SerializeField, Range(0.05f, 2f), Tooltip("Plate mode energy (amplitude length) below which the relic counts as silent.")]
        float _minimumEnergy = 0.3f;

        [SerializeField, Range(0.2f, 1.5f), Tooltip("Amplitude at which a set figure is held while carving.")]
        float _heldAmplitude = 0.8f;

        [SerializeField, Min(0f), Tooltip("Seconds of searching before the hint starts to show where to strike.")]
        float _hintDelay = 6f;

        [SerializeField, Min(0.1f), Tooltip("Seconds the hint takes to reach full strength.")]
        float _hintRamp = 5f;

        [SerializeField, Tooltip("Remember the next sigil between sessions.")]
        bool _persistProgress = true;

        RelicCarvingSurface _surface;
        RelicResonance _resonance;
        CymaticSigil _sigil;
        Vector4 _target;
        int _sigilIndex;
        SigilStage _stage;
        float _match;
        float _bestMatch;
        float _heldFor;
        float _searchTime;
        bool _suspended;
        readonly ForgeSignal _setSignal = new ForgeSignal("RelicSigil.OnSigilSet");
        Action<GamePhaseChangedEvent> _onPhaseChanged;

        /// <summary>Raised when the relic rings into its sigil and carving opens.</summary>
        public event Action OnSigilSet
        {
            add => _setSignal.Add(value);
            remove => _setSignal.Remove(value);
        }

        /// <summary>Current stage.</summary>
        public SigilStage Stage => _stage;

        /// <summary>True while strikes ring the relic toward its sigil, and are not suspended.</summary>
        public bool IsAwakening => _stage == SigilStage.Awakening && !_suspended;

        /// <summary>The current sigil.</summary>
        public CymaticSigil Sigil => _sigil;

        /// <summary>Index of the current sigil in the library.</summary>
        public int SigilIndex => _sigilIndex;

        /// <summary>Unit plate mode amplitudes of the target figure.</summary>
        public Vector4 Target => _target;

        /// <summary>How closely the ringing figure matches the target now, in [0, 1].</summary>
        public float Match => _match;

        /// <summary>Best match reached while awakening, in [0, 1].</summary>
        public float BestMatch => _bestMatch;

        /// <summary>Strength in [0, 1] of the hint that shows where to strike.</summary>
        public float Hint => _stage == SigilStage.Awakening
            ? Mathf.Clamp01((_searchTime - _hintDelay) / _hintRamp)
            : 0f;

        /// <summary>Remember the next sigil between sessions. Turn off for tests and demos.</summary>
        public bool PersistProgress
        {
            get => _persistProgress;
            set => _persistProgress = value;
        }

        /// <summary>
        /// Pauses awakening, for example while a fresh relic is still being poured. Strikes then neither ring nor carve.
        /// </summary>
        public bool Suspended
        {
            get => _suspended;
            set => _suspended = value;
        }

        /// <summary>Starts awakening the sigil at <paramref name="index"/> in the library.</summary>
        /// <param name="index">Sigil index; wraps around the library.</param>
        public void BeginAwakening(int index)
        {
            _sigilIndex = ((index % CymaticMath.SigilCount) + CymaticMath.SigilCount) % CymaticMath.SigilCount;
            _sigil = CymaticMath.GetSigil(_sigilIndex);
            _target = _sigil.Figure;
            _stage = SigilStage.Awakening;
            _match = 0f;
            _bestMatch = 0f;
            _heldFor = 0f;
            _searchTime = 0f;
            _resonance.ReleasePlateFigure();
            _surface.SetGate(true);
        }

        /// <summary>Sets the sigil at once, as a perfect ringing would. For tools, tests and demos.</summary>
        public void SetNow()
        {
            if (_stage == SigilStage.Awakening)
            {
                SetSigil();
            }
        }

        void Awake()
        {
            _surface = GetComponent<RelicCarvingSurface>();
            _resonance = GetComponent<RelicResonance>();
            _onPhaseChanged = HandlePhaseChanged;
            _sigil = CymaticMath.GetSigil(0);
            _target = _sigil.Figure;
        }

        void OnEnable()
        {
            ForgeEvents.GamePhaseChanged += _onPhaseChanged;
        }

        void OnDisable()
        {
            ForgeEvents.GamePhaseChanged -= _onPhaseChanged;
        }

        void Start()
        {
            if (FindAnyObjectByType<ForgeGameManager>() == null)
            {
                BeginAwakening(NextIndex());
            }
        }

        void Update()
        {
            if (_stage != SigilStage.Awakening || _suspended)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            _searchTime += deltaTime;
            Vector4 amplitudes = _resonance.PlateAmplitudes;
            _match = amplitudes.magnitude >= _minimumEnergy ? CymaticMath.Match(amplitudes, _target) : 0f;
            _bestMatch = Mathf.Max(_bestMatch, _match);
            _heldFor = _match >= _matchThreshold ? _heldFor + deltaTime : 0f;
            if (_heldFor >= _holdSeconds)
            {
                SetSigil();
            }
        }

        void HandlePhaseChanged(GamePhaseChangedEvent payload)
        {
            if (payload.Current == ForgePhase.FirstRelicForging)
            {
                BeginAwakening(NextIndex());
            }
            else if (payload.Current != ForgePhase.Completion)
            {
                // Between relics there is no rune to show until the next sigil is rung.
                _stage = SigilStage.Dormant;
                _surface.SetGate(true);
            }
        }

        void SetSigil()
        {
            if (!CymaticMath.GenerateRune(_target, _surface.MarkCount > 0 ? _surface.MarkCount : 12, _surface.UsableArea,
                    out Vector2[] marks, out Vector2Int[] strokes))
            {
                ForgeLog.Warn(ForgeLogChannel.Crafting, LogSource, "RUNE_GENERATION_FAILED",
                    "Sigil '" + _sigil.Name + "' has too few nodal points for a rune; keeping the current rune.", this);
            }
            else
            {
                _surface.SetPattern(marks, strokes);
            }

            _stage = SigilStage.Set;
            _resonance.HoldPlateFigure(_target * _heldAmplitude * Mathf.Sign(Vector4.Dot(_resonance.PlateAmplitudes, _target) + 1e-6f));
            _surface.SetGate(false);
            if (_persistProgress)
            {
                PlayerPrefs.SetInt(NextSigilKey, _sigilIndex + 1);
                PlayerPrefs.Save();
            }

            ForgeLog.Info(ForgeLogChannel.Crafting, LogSource, "SIGIL_SET",
                "Sigil '" + _sigil.Name + "' set after " + _searchTime.ToString("0", CultureInfo.InvariantCulture) +
                " s: " + _surface.MarkCount + " marks, " + _surface.StrokeCount + " grooves, note " + _sigil.NoteName + ".", this);
            _setSignal.Raise();
        }

        int NextIndex()
        {
            return _persistProgress ? PlayerPrefs.GetInt(NextSigilKey, 0) : _sigilIndex;
        }
    }
}
