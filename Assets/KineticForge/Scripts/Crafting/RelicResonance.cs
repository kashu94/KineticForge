using System;
using System.Globalization;
using KineticForge.Core;
using KineticForge.Physics;
using UnityEngine;

namespace KineticForge.Crafting
{
    /// <summary>
    /// The relic as a resonating body: Resonant Forging's core. Every chisel strike rings the relic's plate modes, kicks
    /// its low pulse mode, heats it, and may crack it. The player's body is the damper.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pulse and rhythm.</b> The relic has a slow pulse mode, about one beat per second. A strike landing with the
    /// pulse's motion pumps energy in, like pushing a swing at the right moment; one landing against it drains energy.
    /// Charge, the pulse amplitude, deepens every carve. Hot metal pulses slower, so the tempo rises as the relic cools.
    /// See <see cref="ResonanceMath"/> for the exact model.
    /// </para>
    /// <para>
    /// <b>Body as damper.</b> The pulse physically sways the relic and shakes the chisel, scaled by how unbraced the stance
    /// is: a braced body grounds the energy, a tall one lets it through. When a strike stores more charge than the stance
    /// and the metal can take, the relic cracks and loses integrity. Cold metal cracks far more easily.
    /// </para>
    /// <para>
    /// <b>Plate modes.</b> Four plate modes ring on the carved face. A strike excites each in proportion to its shape at
    /// the contact point, so strikes on different spots ring different Chladni figures, which the Forge Metal shader
    /// draws and the relic synth sings.
    /// </para>
    /// <para>
    /// <b>Heat.</b> The relic starts each forging hot from the furnace and cools by Newton's law. Strikes and the
    /// damping of the pulse heat it, so steady in-rhythm work keeps it workable.
    /// </para>
    /// <para>
    /// <see cref="ChiselStrikeController"/> calls <see cref="ApplyStrike"/> right after publishing each strike, so this
    /// component knows which mark the strike carved. It then publishes <see cref="ForgeEvents.RelicResonated"/>, and
    /// <see cref="ForgeEvents.RelicForged"/> with the grade once the rune is complete. Nothing here allocates per frame.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(RelicCarvingSurface))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Crafting/Relic Resonance")]
    public sealed class RelicResonance : MonoBehaviour
    {
        const string LogSource = nameof(RelicResonance);

        #region Serialized fields

        [Header("Pulse")]
        [SerializeField, Range(0.4f, 3f), Tooltip("Pulse frequency (Hz) of cold metal: the fastest tempo.")]
        float _coldPulseFrequency = 1.25f;

        [SerializeField, Range(0.4f, 3f), Tooltip("Pulse frequency (Hz) fresh from the furnace: the slowest tempo.")]
        float _hotPulseFrequency = 0.8f;

        [SerializeField, Range(0.005f, 0.3f), Tooltip("Pulse damping ratio. Lower rings longer between strikes.")]
        float _pulseDamping = 0.035f;

        [SerializeField, Range(0.05f, 1f), Tooltip("Charge a full-strength, perfectly timed strike adds.")]
        float _kickPerStrike = 0.3f;

        [SerializeField, Range(0.3f, 1f), Tooltip("Timing at or above which a strike counts as in rhythm for streaks.")]
        float _rhythmThreshold = 0.6f;

        [Header("Body coupling")]
        [SerializeField, Range(0f, 0.3f), Tooltip("Sway of the relic (rad/s per unit of pulse velocity) with an unbraced stance.")]
        float _swayGain = 0.05f;

        [SerializeField, Range(0f, 0.03f), Tooltip("Extra chisel tremor (m) at full charge with an unbraced stance.")]
        float _maxResonanceTremor = 0.012f;

        [Header("Heat")]
        [SerializeField, Range(0f, 1f), Tooltip("Heat when forging starts: 1 is white-hot from the furnace; 0.85 is a working cherry-orange.")]
        float _startHeat = 0.85f;

        [SerializeField, Range(5f, 600f), Tooltip("Cooling time constant (s).")]
        float _coolingTime = 75f;

        [SerializeField, Range(0f, 0.1f), Tooltip("Heat gained per unit of pulse energy dissipated.")]
        float _heatFromResonance = 0.02f;

        [SerializeField, Range(0f, 0.1f), Tooltip("Heat gained per full-strength strike.")]
        float _heatPerStrike = 0.015f;

        [Header("Plate modes")]
        [SerializeField, Tooltip("Decay time (s) of each of the four plate modes. Equal times let repeated strikes at one spot converge on that spot's figure, which the sigil puzzle relies on.")]
        Vector4 _plateDecayTimes = new Vector4(2.2f, 2.2f, 2.2f, 2.2f);

        [SerializeField, Range(0.5f, 10f), Tooltip("How fast (1/s) a held figure pulls the plate modes back after a strike.")]
        float _plateHoldRate = 3f;

        [Header("Cracks")]
        [SerializeField, Range(0.3f, 3f), Tooltip("Strike stress above which the relic cracks.")]
        float _crackThreshold = 1.15f;

        [SerializeField, Range(0.05f, 1f), Tooltip("Integrity lost per crack.")]
        float _integrityPerCrack = 0.25f;

        [SerializeField, Range(0f, 1f), Tooltip("Share of the charge kept after a crack releases it.")]
        float _chargeKeptOnCrack = 0.4f;

        [SerializeField, Tooltip("Log every strike's timing, charge, heat and quality.")]
        bool _verboseLogging = false;

        #endregion

        #region Runtime state

        readonly StanceReading _stance = new StanceReading();
        readonly ForgeSignal<RelicResonanceEvent> _resonated = new ForgeSignal<RelicResonanceEvent>("RelicResonance.OnResonated");

        RelicCarvingSurface _surface;
        RelicStabilizer _stabilizer;
        Rigidbody _body;
        Vector2 _pulse;
        Vector4 _plate;
        float _heat;
        float _integrity = 1f;
        float _qualitySum;
        int _carvedCount;
        int _streak;
        int _bestStreak;
        float _firstStrikeTime = -1f;
        float _lastSwayVelocity;
        bool _plateHeld;
        Vector4 _plateHoldTarget;
        bool _forged;
        ForgeGrade _grade = ForgeGrade.C;
        Action<GamePhaseChangedEvent> _onPhaseChanged;

        #endregion

        #region Public API

        /// <summary>Raised after every strike this relic resonates with, before the global bus event.</summary>
        public event Action<RelicResonanceEvent> OnResonated
        {
            add => _resonated.Add(value);
            remove => _resonated.Remove(value);
        }

        /// <summary>Normalized heat: 1 fresh from the furnace, 0 cold.</summary>
        public float Heat => _heat;

        /// <summary>How workable the metal is now, in [0, 1].</summary>
        public float Softness => ResonanceMath.Softness(_heat);

        /// <summary>Resonance charge: the pulse amplitude.</summary>
        public float Charge => _pulse.magnitude;

        /// <summary>Pulse phasor (x, −v/ω).</summary>
        public Vector2 Pulse => _pulse;

        /// <summary>Current pulse frequency (Hz), which rises as the relic cools.</summary>
        public float PulseFrequency => ResonanceMath.PulseFrequency(_heat, _coldPulseFrequency, _hotPulseFrequency);

        /// <summary>True while the pulse rings strongly enough for strike timing to count.</summary>
        public bool IsPulsing => Charge >= ResonanceMath.RestAmplitude;

        /// <summary>Timing a strike would have right now, in [−1, 1].</summary>
        public float TimingNow => ResonanceMath.StrikeTiming(_pulse);

        /// <summary>Fraction of a pulse cycle until the ideal strike moment; 0 means now.</summary>
        public float CycleToIdealStrike => ResonanceMath.CycleToIdealStrike(_pulse);

        /// <summary>Signed amplitude of each plate mode.</summary>
        public Vector4 PlateAmplitudes => _plate;

        /// <summary>Remaining integrity in [0, 1]; 0 means fractured.</summary>
        public float Integrity => _integrity;

        /// <summary>Marks carved since forging started.</summary>
        public int CarvedCount => _carvedCount;

        /// <summary>Mean quality of the carved marks, or 0 before the first.</summary>
        public float MeanQuality => _carvedCount > 0 ? _qualitySum / _carvedCount : 0f;

        /// <summary>Current run of consecutive in-rhythm strikes.</summary>
        public int Streak => _streak;

        /// <summary>Longest run of consecutive in-rhythm strikes.</summary>
        public int BestStreak => _bestStreak;

        /// <summary>True once the rune is complete and graded.</summary>
        public bool IsForged => _forged;

        /// <summary>Grade of the finished relic. Valid once <see cref="IsForged"/> is true.</summary>
        public ForgeGrade Grade => _grade;

        /// <summary>Extra chisel tremor (m) the ringing relic adds through an unbraced stance.</summary>
        public float TremorBoost =>
            _maxResonanceTremor * Mathf.Clamp01(Charge / 1.5f) * (1f - Mathf.Clamp01(_stance.Stability));

        /// <summary>
        /// Rings the relic with a strike. Called by <see cref="ChiselStrikeController"/> right after it publishes
        /// <see cref="ForgeEvents.ChiselStrike"/>; public for tools and tests.
        /// </summary>
        /// <param name="hand">Hand that drove the strike.</param>
        /// <param name="point">World-space contact point.</param>
        /// <param name="normal">World-space face normal.</param>
        /// <param name="strength">Strike energy in [0, 1].</param>
        /// <param name="precision">Aim-and-stance precision in [0, 1].</param>
        /// <param name="carvedMark">Index of the mark the strike carved, or −1.</param>
        /// <returns>The published resonance payload.</returns>
        public RelicResonanceEvent ApplyStrike(ForgeHand hand, Vector3 point, Vector3 normal, float strength, float precision,
            int carvedMark)
        {
            strength = ForgeMath.Sanitize01(strength, 1f);
            precision = ForgeMath.Sanitize01(precision);
            float stability = Mathf.Clamp01(_stance.Stability);
            Vector2 uv = _surface.WorldToUV(point);
            Vector2 clampedUV = new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));

            float chargeBefore = Charge;
            float timing = ResonanceMath.StrikeTiming(_pulse);
            float softness = Softness;

            _pulse = ResonanceMath.Kick(_pulse, _kickPerStrike * strength);
            Vector4 excitation = ResonanceMath.PlateExcitation(clampedUV);
            for (int i = 0; i < ResonanceMath.PlateModeCount; i++)
            {
                float shape = ResonanceMath.PlateMode(i, clampedUV) * 0.5f;
                _plate[i] = Mathf.Clamp(_plate[i] + strength * shape, -1.5f, 1.5f);
            }

            _heat = Mathf.Clamp01(_heat + _heatPerStrike * strength);

            bool cracked = false;
            if (_integrity > 0f && ResonanceMath.CrackStress(Charge, stability, softness) > _crackThreshold)
            {
                cracked = true;
                _integrity = Mathf.Max(0f, _integrity - _integrityPerCrack);
                _pulse *= _chargeKeptOnCrack;
                ForgeLog.Info(ForgeLogChannel.Crafting, LogSource, _integrity > 0f ? "RELIC_CRACKED" : "RELIC_FRACTURED",
                    "The relic cracked: charge " + F2(chargeBefore) + " through a stance of " + F2(stability) +
                    " at heat " + F2(_heat) + ". Integrity " + F2(_integrity) + ". Brace before charging cold metal.", this);
            }

            if (Mathf.Clamp(timing, -1f, 1f) >= _rhythmThreshold)
            {
                _streak++;
                _bestStreak = Mathf.Max(_bestStreak, _streak);
            }
            else
            {
                _streak = 0;
            }

            if (_firstStrikeTime < 0f)
            {
                _firstStrikeTime = Time.time;
            }

            float quality = 0f;
            if (carvedMark >= 0)
            {
                quality = ResonanceMath.CarveQuality(precision, softness, timing, chargeBefore);
                _qualitySum += quality;
                _carvedCount++;
            }

            if (_verboseLogging)
            {
                ForgeLog.Info(ForgeLogChannel.Crafting, LogSource, "STRIKE_RESONATED",
                    "Timing " + F2(timing) + ", charge " + F2(chargeBefore) + " to " + F2(Charge) + ", heat " + F2(_heat) +
                    ", quality " + F2(quality) + (cracked ? ", cracked." : "."), this);
            }

            RelicResonanceEvent payload = new RelicResonanceEvent(_body, hand, point, normal, clampedUV, strength, precision,
                timing, Charge, _heat, excitation, quality, carvedMark, cracked, _integrity);
            _resonated.Raise(payload);
            ForgeEvents.RaiseRelicResonated(payload);

            if (carvedMark >= 0 && _surface.IsComplete && !_forged)
            {
                Finish();
            }

            return payload;
        }

        /// <summary>Starts a fresh forging: hot metal at rest, full integrity, no score.</summary>
        public void ResetForging()
        {
            _pulse = Vector2.zero;
            _plate = Vector4.zero;
            _heat = _startHeat;
            _integrity = 1f;
            _qualitySum = 0f;
            _carvedCount = 0;
            _streak = 0;
            _bestStreak = 0;
            _firstStrikeTime = -1f;
            _lastSwayVelocity = 0f;
            _plateHeld = false;
            _forged = false;
            _grade = ForgeGrade.C;
        }

        /// <summary>True while the plate modes are held at a figure.</summary>
        public bool IsPlateFigureHeld => _plateHeld;

        /// <summary>
        /// Holds the plate modes at a figure: strikes still ring them, but they settle back onto it instead of dying away.
        /// Used once a sigil is set, so its Chladni figure stays on the relic while it is carved.
        /// </summary>
        /// <param name="amplitudes">Plate mode amplitudes to hold.</param>
        public void HoldPlateFigure(Vector4 amplitudes)
        {
            _plateHeld = true;
            _plateHoldTarget = amplitudes;
        }

        /// <summary>Lets the plate modes ring down freely again.</summary>
        public void ReleasePlateFigure()
        {
            _plateHeld = false;
        }

        /// <summary>Sets the heat directly, for tools and tests.</summary>
        /// <param name="heat">Normalized heat in [0, 1].</param>
        public void SetHeat(float heat)
        {
            _heat = ForgeMath.Sanitize01(heat);
        }

        #endregion

        #region Unity lifecycle

        void Awake()
        {
            _surface = GetComponent<RelicCarvingSurface>();
            _stabilizer = GetComponent<RelicStabilizer>();
            _body = GetComponent<Rigidbody>();
            _onPhaseChanged = HandlePhaseChanged;
            ResetForging();
        }

        void OnEnable()
        {
            _stance.Enable(_stabilizer);
            ForgeEvents.GamePhaseChanged += _onPhaseChanged;
        }

        void OnDisable()
        {
            _stance.Disable();
            ForgeEvents.GamePhaseChanged -= _onPhaseChanged;
        }

        void Update()
        {
            float deltaTime = Time.deltaTime;
            if (!(deltaTime > 0f))
            {
                return;
            }

            float frequency = PulseFrequency;
            float amplitude = Charge;

            // Damping turns pulse energy into heat before the metal cools toward the room.
            _heat += _heatFromResonance * ResonanceMath.DissipatedPower(amplitude, frequency, _pulseDamping) * deltaTime;
            _heat = Mathf.Clamp01(ResonanceMath.Cool(_heat, _coolingTime, deltaTime));

            _pulse = ResonanceMath.StepOscillator(_pulse, frequency, _pulseDamping, deltaTime);
            if (_pulse.sqrMagnitude < 1e-8f)
            {
                _pulse = Vector2.zero;
            }

            if (_plateHeld)
            {
                _plate = Vector4.Lerp(_plate, _plateHoldTarget, 1f - Mathf.Exp(-_plateHoldRate * deltaTime));
            }
            else
            {
                for (int i = 0; i < ResonanceMath.PlateModeCount; i++)
                {
                    _plate[i] = ResonanceMath.Cool(_plate[i], Mathf.Max(0.05f, _plateDecayTimes[i]), deltaTime);
                }
            }
        }

        void FixedUpdate()
        {
            if (_stabilizer == null || _swayGain <= 0f)
            {
                return;
            }

            // Pulse velocity in amplitude units per second: v = −ω·y. The relic's spin follows it about its face's up
            // axis, as much as an unbraced stance lets through; the stabilizer's damping fights the rest.
            float velocity = -2f * Mathf.PI * PulseFrequency * _pulse.y;
            float change = velocity - _lastSwayVelocity;
            _lastSwayVelocity = velocity;
            float coupling = _swayGain * (1f - Mathf.Clamp01(_stance.Stability));
            if (coupling <= 0f || Mathf.Abs(change) < 1e-5f)
            {
                return;
            }

            _surface.GetLocalFace(out _, out _, out Vector3 up, out _, out _);
            _stabilizer.AddDisturbance(transform.TransformDirection(up) * (change * coupling));
        }

        void OnValidate()
        {
            _plateDecayTimes = Vector4.Max(_plateDecayTimes, new Vector4(0.05f, 0.05f, 0.05f, 0.05f));
        }

        #endregion

        void HandlePhaseChanged(GamePhaseChangedEvent payload)
        {
            if (payload.Current == ForgePhase.FirstRelicForging)
            {
                ResetForging();
            }
        }

        void Finish()
        {
            _forged = true;
            float mean = MeanQuality;
            _grade = ResonanceMath.Grade(mean, _integrity);
            float duration = _firstStrikeTime >= 0f ? Time.time - _firstStrikeTime : 0f;
            ForgeLog.Info(ForgeLogChannel.Crafting, LogSource, "RELIC_FORGED",
                "Relic '" + name + "' forged. Grade " + _grade + ", mean quality " + F2(mean) + ", integrity " +
                F2(_integrity) + ", best rhythm streak " + _bestStreak + ", " + duration.ToString("0", CultureInfo.InvariantCulture) +
                " s.", this);
            ForgeEvents.RaiseRelicForged(new RelicForgedEvent(_body, _grade, mean, _integrity, _bestStreak, duration));
        }

        static string F2(float value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
