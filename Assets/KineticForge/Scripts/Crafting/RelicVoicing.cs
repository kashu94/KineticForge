using System;
using System.Globalization;
using KineticForge.Core;
using KineticForge.Tracking;
using UnityEngine;

namespace KineticForge.Crafting
{
    /// <summary>Where a relic is in its voicing.</summary>
    public enum VoicingStage : byte
    {
        /// <summary>No commission is being voiced.</summary>
        Dormant = 0,

        /// <summary>The relic is being tuned: strikes ring it, and cuts lower its voices.</summary>
        Voicing = 1,

        /// <summary>Every voice is in tune, or the metal is spent; the relic rings a moment before it is finished.</summary>
        Settling = 2,

        /// <summary>The relic is finished.</summary>
        Done = 3,
    }

    /// <summary>The tool in the player's strike hand.</summary>
    public enum VoicingTool : byte
    {
        /// <summary>A strike rings the relic and cuts nothing: for listening.</summary>
        Hammer = 0,

        /// <summary>A strike cuts the relic where it lands and then rings it: for tuning.</summary>
        Chisel = 1,
    }

    /// <summary>
    /// The heart of the game: a relic is commissioned to sing a chord, but the pour leaves its four voices sharp. The
    /// player listens, reads the Chladni sand, and cuts metal where it lowers the right voices, until the relic rings the
    /// chord true. Every cut is final.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Play.</b> Each voice is one plate mode (<see cref="VoicingMath"/>). A strike rings the plate, and where it lands
    /// decides which voices ring loudest. With the hammer, a strike only rings; with the chisel it first cuts, which
    /// lowers each voice by its sensitivity at that spot, then rings, so the player hears the result at once. A tuning
    /// fork answers each strike with the voices' target pitches, so a voice that is off beats against its fork: a wobble
    /// that slows and dies as the voice comes true.
    /// </para>
    /// <para>
    /// <b>Finishing.</b> When every voice is within tolerance the relic settles for a moment. Another cut cancels that, so
    /// the player may keep refining. If the cut budget runs out first, the relic is finished as it stands.
    /// </para>
    /// <para>
    /// <b>Input.</b> A tap of the other hand's thumb, or the T key in the Editor, swaps hammer and chisel. Strikes and cuts
    /// come from <see cref="ChiselStrikeController"/>, which calls <see cref="ApplyCut"/>.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(RelicCarvingSurface), typeof(RelicResonance))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Crafting/Relic Voicing")]
    public sealed class RelicVoicing : MonoBehaviour
    {
        const string LogSource = nameof(RelicVoicing);
        const string NextCommissionKey = "KineticForge.NextCommission";

        [SerializeField, Range(0.5f, 6f), Tooltip("Seconds the relic rings in tune before it is finished. A further cut cancels it.")]
        float _settleSeconds = 2.4f;

        [SerializeField, Range(0.3f, 3f), Tooltip("Seconds the relic rings after the metal is spent before it is finished.")]
        float _spentSeconds = 1.6f;

        [SerializeField, Min(0f), Tooltip("Seconds without a cut before the ring marks a good place to cut.")]
        float _hintDelay = 20f;

        [SerializeField, Tooltip("Remember the next commission between sessions.")]
        bool _persistProgress = true;

        RelicCarvingSurface _surface;
        RelicResonance _resonance;
        VoicingCommission _commission;
        VoicingStage _stage;
        VoicingTool _tool;
        Vector4 _errors;
        int _commissionIndex;
        int _cutsUsed;
        float _settleTimer;
        bool _spent;
        float _idleTime;
        bool _suspended;
        bool _hintValid;
        Vector2 _hintUV;
        float _quality;
        readonly ForgeSignal<Vector2> _cutSignal = new ForgeSignal<Vector2>("RelicVoicing.OnCut");
        readonly ForgeSignal<VoicingTool> _toolSignal = new ForgeSignal<VoicingTool>("RelicVoicing.OnToolChanged");
        readonly ForgeSignal _tunedSignal = new ForgeSignal("RelicVoicing.OnInTune");
        Action<GamePhaseChangedEvent> _onPhaseChanged;
        Action<MicroGestureTapEvent> _onTap;

        /// <summary>Raised after every cut, with the cut's face position.</summary>
        public event Action<Vector2> OnCut
        {
            add => _cutSignal.Add(value);
            remove => _cutSignal.Remove(value);
        }

        /// <summary>Raised when the tool changes.</summary>
        public event Action<VoicingTool> OnToolChanged
        {
            add => _toolSignal.Add(value);
            remove => _toolSignal.Remove(value);
        }

        /// <summary>Raised when the last voice comes into tune and the relic starts to settle.</summary>
        public event Action OnInTune
        {
            add => _tunedSignal.Add(value);
            remove => _tunedSignal.Remove(value);
        }

        /// <summary>Where the relic is in its voicing.</summary>
        public VoicingStage Stage => _stage;

        /// <summary>True while the relic is being tuned or settling, and strikes belong to voicing.</summary>
        public bool IsActive => (_stage == VoicingStage.Voicing || _stage == VoicingStage.Settling) && !_suspended;

        /// <summary>The tool in hand.</summary>
        public VoicingTool Tool => _tool;

        /// <summary>The commission being voiced.</summary>
        public VoicingCommission Commission => _commission;

        /// <summary>Index of the commission being voiced.</summary>
        public int CommissionIndex => _commissionIndex;

        /// <summary>Error of each voice, in cents; positive is sharp.</summary>
        public Vector4 Errors => _errors;

        /// <summary>Cuts taken.</summary>
        public int CutsUsed => _cutsUsed;

        /// <summary>Cuts left before the metal is spent.</summary>
        public int CutsLeft => Mathf.Max(0, _commission.CutBudget - _cutsUsed);

        /// <summary>True once every voice is within tolerance.</summary>
        public bool IsInTune => VoicingMath.InTune(_errors, _commission.ToleranceCents);

        /// <summary>Seconds left before an in-tune relic is finished; zero when not settling.</summary>
        public float SettleRemaining => _stage == VoicingStage.Settling ? Mathf.Max(0f, _settleTimer) : 0f;

        /// <summary>True while the relic is being finished because the metal is spent.</summary>
        public bool IsSpent => _spent;

        /// <summary>Quality the finished relic earned, in [0, 1].</summary>
        public float Quality => _quality;

        /// <summary>True when the hint should show where to cut.</summary>
        public bool HintActive => _stage == VoicingStage.Voicing && _hintValid && _idleTime >= _hintDelay;

        /// <summary>Face position of the suggested cut. Meaningful while <see cref="HintActive"/>.</summary>
        public Vector2 HintUV => _hintUV;

        /// <summary>Holds voicing closed, for example during the pour.</summary>
        public bool Suspended
        {
            get => _suspended;
            set => _suspended = value;
        }

        /// <summary>Remember the next commission between sessions.</summary>
        public bool PersistProgress
        {
            get => _persistProgress;
            set => _persistProgress = value;
        }

        /// <summary>Pitch of voice <paramref name="voice"/> relative to its natural partial, in cents.</summary>
        /// <param name="voice">Voice index.</param>
        public float VoiceCents(int voice) => VoicingMath.TargetShiftCents(_commission, voice) + _errors[voice];

        /// <summary>Target pitch of voice <paramref name="voice"/> relative to its natural partial, in cents.</summary>
        /// <param name="voice">Voice index.</param>
        public float TargetCents(int voice) => VoicingMath.TargetShiftCents(_commission, voice);

        /// <summary>Starts voicing a commission, with the hammer in hand.</summary>
        /// <param name="index">Commission index; wraps.</param>
        public void Begin(int index)
        {
            _commissionIndex = ((index % VoicingMath.CommissionCount) + VoicingMath.CommissionCount) % VoicingMath.CommissionCount;
            _commission = VoicingMath.GetCommission(_commissionIndex);
            _errors = _commission.StartCents;
            _cutsUsed = 0;
            _settleTimer = 0f;
            _spent = false;
            _idleTime = 0f;
            _quality = 0f;
            _stage = VoicingStage.Voicing;
            RefreshHint();
            SetTool(VoicingTool.Hammer);
            _resonance.ReleasePlateFigure();

            // Strikes ring the relic and cut where voicing says; the rune marks play no part.
            _surface.SetGate(true);
            ForgeLog.Info(ForgeLogChannel.Crafting, LogSource, "VOICING_BEGAN",
                "Commission '" + _commission.Name + "': " + _commission.ChordName + " in " + _commission.RootName +
                ", tolerance " + _commission.ToleranceCents.ToString("0", CultureInfo.InvariantCulture) + " cents, " +
                _commission.CutBudget + " cuts.", this);
        }

        /// <summary>Swaps hammer and chisel.</summary>
        public void ToggleTool()
        {
            SetTool(_tool == VoicingTool.Hammer ? VoicingTool.Chisel : VoicingTool.Hammer);
        }

        /// <summary>Puts a tool in hand.</summary>
        /// <param name="tool">The tool.</param>
        public void SetTool(VoicingTool tool)
        {
            bool changed = _tool != tool;
            _tool = tool;
            if (changed)
            {
                _toolSignal.Raise(tool);
            }
        }

        /// <summary>How a cut at <paramref name="uv"/> would change each voice right now, in cents.</summary>
        /// <param name="uv">Face position, both axes in [0, 1].</param>
        /// <param name="strength">Strike strength in [0, 1].</param>
        /// <param name="softness">Metal softness in [0, 1].</param>
        /// <param name="charge">Pulse charge.</param>
        /// <returns>Cents per voice, lowering.</returns>
        public Vector4 Preview(Vector2 uv, float strength, float softness, float charge)
        {
            return VoicingMath.CutDelta(uv, VoicingMath.CutSize(strength, softness, charge));
        }

        /// <summary>
        /// Cuts the relic at <paramref name="uv"/>, lowering every voice by its sensitivity there. Does nothing unless the
        /// relic is being voiced with metal left.
        /// </summary>
        /// <param name="uv">Face position, both axes in [0, 1].</param>
        /// <param name="strength">Strike strength in [0, 1].</param>
        /// <param name="softness">Metal softness in [0, 1].</param>
        /// <param name="charge">Pulse charge before the strike.</param>
        /// <returns>The change made to each voice, in cents; zero when no cut was made.</returns>
        public Vector4 ApplyCut(Vector2 uv, float strength, float softness, float charge)
        {
            if (!IsActive || _cutsUsed >= _commission.CutBudget)
            {
                return Vector4.zero;
            }

            uv = new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));
            Vector4 delta = Preview(uv, strength, softness, charge);
            _errors += delta;
            _cutsUsed++;
            _idleTime = 0f;
            _settleTimer = 0f;
            _spent = false;
            _stage = VoicingStage.Voicing;
            RefreshHint();
            _cutSignal.Raise(uv);

            if (IsInTune)
            {
                BeginSettling(spent: false);
                _tunedSignal.Raise();
            }
            else if (_cutsUsed >= _commission.CutBudget)
            {
                BeginSettling(spent: true);
            }

            return delta;
        }

        void BeginSettling(bool spent)
        {
            _stage = VoicingStage.Settling;
            _spent = spent;
            _settleTimer = spent ? _spentSeconds : _settleSeconds;
        }

        void Finish()
        {
            _stage = VoicingStage.Done;
            _quality = VoicingMath.Quality(_errors, _commission.ToleranceCents, _cutsUsed, _commission.CutBudget);
            if (_persistProgress)
            {
                PlayerPrefs.SetInt(NextCommissionKey, _commissionIndex + 1);
                PlayerPrefs.Save();
            }

            ForgeLog.Info(ForgeLogChannel.Crafting, LogSource, "VOICING_FINISHED",
                "Commission '" + _commission.Name + "' finished with " + _cutsUsed + " cuts, errors " +
                _errors.x.ToString("0.0", CultureInfo.InvariantCulture) + "/" + _errors.y.ToString("0.0", CultureInfo.InvariantCulture) +
                "/" + _errors.z.ToString("0.0", CultureInfo.InvariantCulture) + "/" + _errors.w.ToString("0.0", CultureInfo.InvariantCulture) +
                " cents, quality " + _quality.ToString("0.00", CultureInfo.InvariantCulture) + (_spent ? " (metal spent)." : "."), this);
            _resonance.ForgeWithQuality(_quality);
        }

        void RefreshHint()
        {
            _hintValid = VoicingMath.SuggestCut(_errors, _commission.ToleranceCents, VoicingMath.CutSize(1f, 0.8f, 0f), out _hintUV);
        }

        int NextIndex()
        {
            return _persistProgress ? PlayerPrefs.GetInt(NextCommissionKey, 0) : _commissionIndex;
        }

        void Awake()
        {
            _surface = GetComponent<RelicCarvingSurface>();
            _resonance = GetComponent<RelicResonance>();
            _onPhaseChanged = HandlePhaseChanged;
            _onTap = HandleTap;
            _commission = VoicingMath.GetCommission(0);
            _errors = _commission.StartCents;
        }

        void OnEnable()
        {
            ForgeEvents.GamePhaseChanged += _onPhaseChanged;
            ForgeEvents.MicroGestureTap += _onTap;
        }

        void OnDisable()
        {
            ForgeEvents.GamePhaseChanged -= _onPhaseChanged;
            ForgeEvents.MicroGestureTap -= _onTap;
        }

        void Start()
        {
            if (FindAnyObjectByType<ForgeGameManager>() == null)
            {
                Begin(NextIndex());
            }
        }

        void Update()
        {
            if (!IsActive)
            {
                return;
            }

            if (SimulatorKeyboard.WasPressedThisFrame(KeyCode.T))
            {
                ToggleTool();
            }

            float deltaTime = Time.deltaTime;
            if (_stage == VoicingStage.Voicing)
            {
                _idleTime += deltaTime;
            }
            else if (_stage == VoicingStage.Settling)
            {
                _settleTimer -= deltaTime;
                if (_settleTimer <= 0f)
                {
                    Finish();
                }
            }
        }

        void HandlePhaseChanged(GamePhaseChangedEvent payload)
        {
            if (payload.Current == ForgePhase.FirstRelicForging)
            {
                Begin(NextIndex());
            }
            else if (payload.Current != ForgePhase.Completion)
            {
                _stage = VoicingStage.Dormant;
                _surface.SetGate(true);
            }
        }

        void HandleTap(MicroGestureTapEvent payload)
        {
            // The strike hand rings and cuts; the other hand's thumb swaps the tool.
            if (IsActive && payload.Hand == ForgeHand.Left)
            {
                ToggleTool();
            }
        }

        void OnValidate()
        {
            _settleSeconds = Mathf.Max(0.5f, _settleSeconds);
        }
    }
}
