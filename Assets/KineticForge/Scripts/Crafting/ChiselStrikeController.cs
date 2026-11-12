using System;
using System.Globalization;
using KineticForge.Core;
using KineticForge.Physics;
using UnityEngine;

namespace KineticForge.Crafting
{
    /// <summary>
    /// Turns a tap gesture into a chisel strike on the relic: scores its precision from cursor aim and stance, publishes
    /// <see cref="ForgeEvents.ChiselStrike"/>, and kicks the relic with a stance-scaled recoil.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Precision comes from <see cref="CarvingPrecision.Evaluate"/>: the distance from the cursor's strike point, tremor
    /// included, to the active mark, times the stance multiplier. The <see cref="RelicCarvingSurface"/> and
    /// <see cref="ForgeGameManager"/> both listen to the published strike, so the rune and the forging progress advance
    /// together when it qualifies.
    /// </para>
    /// <para>
    /// The recoil spins the relic around a random axis in the face plane through
    /// <see cref="RelicStabilizer.AddDisturbance"/>, so a braced stance absorbs it and an upright one sends the relic
    /// rocking, which in turn shakes the next aim.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(RelicCarvingSurface), typeof(CarvingCursor))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Crafting/Chisel Strike Controller")]
    public sealed class ChiselStrikeController : MonoBehaviour
    {
        const string LogSource = nameof(ChiselStrikeController);

        [SerializeField, Tooltip("Hand whose taps strike. None accepts either hand.")]
        ForgeHand _strikingHand = ForgeHand.None;

        [SerializeField, Range(0f, 1f), Tooltip("Precision multiplier at zero stance stability. 1 ignores stance.")]
        float _minimumStanceFactor = 0.4f;

        [SerializeField, Range(0f, 5f), Tooltip("Recoil angular speed change (rad/s at baseline inertia) of a full-strength strike.")]
        float _recoilAngularSpeed = 0.8f;

        [SerializeField, Range(0f, 1f), Tooltip("Minimum seconds between strikes.")]
        float _strikeCooldown = 0.12f;

        [SerializeField, Tooltip("Log every strike with its precision.")]
        bool _verboseLogging = false;

        readonly StanceReading _stance = new StanceReading();
        RelicCarvingSurface _surface;
        CarvingCursor _cursor;
        RelicStabilizer _stabilizer;
        RelicResonance _resonance;
        RelicSigil _sigil;
        RelicVoicing _voicing;
        Rigidbody _body;
        float _lastStrikeTime = float.NegativeInfinity;
        float _lastPrecision;
        bool _lastQualified;
        int _strikeCount;
        Action<MicroGestureTapEvent> _onTap;

        /// <summary>Precision of the latest strike in [0, 1].</summary>
        public float LastPrecision => _lastPrecision;

        /// <summary>True when the latest strike met the surface's precision bar.</summary>
        public bool LastStrikeQualified => _lastQualified;

        /// <summary>Strikes attempted since the component was enabled.</summary>
        public int StrikeCount => _strikeCount;

        /// <summary>Precision multiplier at zero stance stability.</summary>
        public float MinimumStanceFactor => _minimumStanceFactor;

        /// <summary>Strikes at the cursor now, as a tap of the given strength would. For tools and tests.</summary>
        /// <param name="hand">Hand credited with the strike.</param>
        /// <param name="intensity">Strike strength in [0, 1].</param>
        /// <returns>True when a strike was published.</returns>
        public bool Strike(ForgeHand hand, float intensity)
        {
            float now = Time.unscaledTime;
            if (_voicing != null && _voicing.IsActive)
            {
                return StrikeVoicing(hand, intensity, now);
            }

            if (!_surface.IsAcceptingStrikes || !_surface.TryGetActiveMark(out Vector3 mark, out _))
            {
                return RingWithoutCarving(hand, intensity, now);
            }

            if (now - _lastStrikeTime < _strikeCooldown)
            {
                return false;
            }

            _lastStrikeTime = now;
            _cursor.GetStrikePoint(out Vector3 point, out Vector3 normal);
            float distance = Vector3.Distance(point, mark);
            float stability = _stance.Stability;
            float precision = CarvingPrecision.Evaluate(distance, _surface.StrikeTolerance, stability, _minimumStanceFactor);
            float strength = ForgeMath.Sanitize01(intensity, 1f);

            _lastPrecision = precision;
            _lastQualified = precision >= _surface.MinStrikePrecision;
            _strikeCount++;

            if (_verboseLogging)
            {
                ForgeLog.Info(ForgeLogChannel.Crafting, LogSource, _lastQualified ? "STRIKE_QUALIFIED" : "STRIKE_MISSED",
                    "Precision " + precision.ToString("0.00", CultureInfo.InvariantCulture) + " (" +
                    (distance * 1000f).ToString("0", CultureInfo.InvariantCulture) + " mm off, stance " +
                    stability.ToString("0.00", CultureInfo.InvariantCulture) + ").", this);
            }

            int activeMark = _surface.ActiveIndex;
            int struckBefore = _surface.StruckCount;
            ForgeEvents.RaiseChiselStrike(new ChiselStrikeEvent(_body, hand, point, normal, strength, precision));
            ApplyRecoil(normal, strength);

            // The surface carves during the event, so a raised count tells which mark this strike carved.
            if (_resonance != null)
            {
                _resonance.ApplyStrike(hand, point, normal, strength, precision,
                    _surface.StruckCount > struckBefore ? activeMark : -1);
            }
            return true;
        }

        /// <summary>
        /// While a sigil is awakening, strikes ring the relic to shape its Chladni figure but carve nothing, so they are
        /// not published as chisel strikes and never count toward forging progress.
        /// </summary>
        /// <summary>
        /// A strike during voicing: the hammer only rings the relic; the chisel first cuts where it lands, lowering the
        /// voices by their sensitivity there, then rings it, so the player hears the result of the cut at once.
        /// </summary>
        bool StrikeVoicing(ForgeHand hand, float intensity, float now)
        {
            if (_resonance == null || now - _lastStrikeTime < _strikeCooldown)
            {
                return false;
            }

            _lastStrikeTime = now;
            _cursor.GetStrikePoint(out Vector3 point, out Vector3 normal);
            float strength = ForgeMath.Sanitize01(intensity, 1f);
            _strikeCount++;

            if (_voicing.Tool == VoicingTool.Chisel)
            {
                Vector2 uv = _surface.WorldToUV(point);
                _voicing.ApplyCut(uv, strength, _resonance.Softness, _resonance.Charge);
            }

            ApplyRecoil(normal, strength);
            _resonance.ApplyStrike(hand, point, normal, strength, 1f, -1);
            return true;
        }

        bool RingWithoutCarving(ForgeHand hand, float intensity, float now)
        {
            if (_sigil == null || !_sigil.IsAwakening || _resonance == null || now - _lastStrikeTime < _strikeCooldown)
            {
                return false;
            }

            _lastStrikeTime = now;
            _cursor.GetStrikePoint(out Vector3 point, out Vector3 normal);
            float strength = ForgeMath.Sanitize01(intensity, 1f);
            _strikeCount++;
            ApplyRecoil(normal, strength);
            _resonance.ApplyStrike(hand, point, normal, strength, 0f, -1);
            return true;
        }

        void Awake()
        {
            _surface = GetComponent<RelicCarvingSurface>();
            _cursor = GetComponent<CarvingCursor>();
            _stabilizer = GetComponent<RelicStabilizer>();
            _resonance = GetComponent<RelicResonance>();
            _sigil = GetComponent<RelicSigil>();
            _voicing = GetComponent<RelicVoicing>();
            _body = GetComponent<Rigidbody>();
            _onTap = HandleTap;
        }

        void OnEnable()
        {
            _stance.Enable(_stabilizer);
            ForgeEvents.MicroGestureTap += _onTap;
        }

        void Start()
        {
            // Components added after this one in the same frame are found here, once all have woken.
            if (_resonance == null)
            {
                _resonance = GetComponent<RelicResonance>();
            }

            if (_sigil == null)
            {
                _sigil = GetComponent<RelicSigil>();
            }

            if (_voicing == null)
            {
                _voicing = GetComponent<RelicVoicing>();
            }
        }

        void OnDisable()
        {
            _stance.Disable();
            ForgeEvents.MicroGestureTap -= _onTap;
        }

        void HandleTap(MicroGestureTapEvent payload)
        {
            if (_strikingHand == ForgeHand.None || payload.Hand == _strikingHand)
            {
                Strike(payload.Hand, payload.Strength);
            }
        }

        void ApplyRecoil(Vector3 normal, float strength)
        {
            if (_recoilAngularSpeed <= 0f || strength <= 0f)
            {
                return;
            }

            // A random axis in the face plane: the chisel tips the relic rather than spinning it about the strike normal.
            Vector3 axis = Vector3.Cross(normal, UnityEngine.Random.onUnitSphere);
            if (axis.sqrMagnitude < 1e-6f)
            {
                return;
            }

            Vector3 change = axis.normalized * (_recoilAngularSpeed * strength);
            if (_stabilizer != null)
            {
                _stabilizer.AddDisturbance(change);
            }
            else if (_body != null && !_body.isKinematic)
            {
                _body.AddTorque(change, ForceMode.VelocityChange);
            }
        }
    }
}
