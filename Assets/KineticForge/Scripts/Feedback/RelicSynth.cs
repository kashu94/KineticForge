using System;
using System.Threading;
using KineticForge.Core;
using KineticForge.Crafting;
using UnityEngine;

namespace KineticForge.Feedback
{
    /// <summary>
    /// Modal sound synthesis: the relic rings like a struck bronze bell, computed sample by sample from its vibration
    /// modes rather than played from recordings. Every strike sounds different because it excites the modes differently.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Eight partials at bell ratios (hum, prime, minor third, fifth, nominal and upper partials) each ring as a damped
    /// sinusoid. A strike excites every partial in proportion to how strongly it excited the matching plate mode at the
    /// contact point (see <see cref="ResonanceMath.PlateExcitation"/>), so a strike on a nodal line drops that partial out
    /// of the sound, as it would on a real plate. Hot metal is softer: it rings lower and dies faster, a dull thud that
    /// brightens into a clear ring as the relic cools. A sustained hum follows the resonance charge and swells with the
    /// pulse, so the player can hear the rhythm. Strikes add a short noise transient; cracks add a dry crunch; a forged
    /// relic sings a full chord.
    /// </para>
    /// <para>
    /// Synthesis runs on the audio thread in <c>OnAudioFilterRead</c>. The AudioSource plays a constant carrier clip and the
    /// synth multiplies it, so Unity's 3D spatialization and attenuation still apply. Strikes reach the audio thread
    /// through a lock-free single-producer, single-consumer queue. Nothing allocates after Awake.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(AudioSource))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Feedback/Relic Synth")]
    public sealed class RelicSynth : MonoBehaviour
    {
        const string LogSource = nameof(RelicSynth);
        const int ModeCount = 8;
        const int QueueSize = 16;

        // Bell partial ratios, decay times (s) and gains, and the plate mode that shapes each partial.
        internal static readonly float[] s_ratios = { 0.5f, 1f, 1.19f, 1.5f, 2f, 2.51f, 2.66f, 3.01f };
        internal static readonly float[] s_decays = { 4.5f, 3.2f, 2.4f, 2f, 1.5f, 1f, 0.8f, 0.55f };
        internal static readonly float[] s_gains = { 0.35f, 0.5f, 0.3f, 0.28f, 0.32f, 0.18f, 0.14f, 0.1f };
        static readonly int[] s_plateMode = { 0, 0, 1, 1, 2, 2, 3, 3 };

        // The partial each voice (plate mode) owns. Voicing tunes these; the other partials keep the bell's color.
        static readonly int[] s_primary = { 1, 3, 4, 7 };

        [SerializeField, Tooltip("Relic whose strikes this synth plays. Uses this object's Rigidbody when empty.")]
        Rigidbody _relic = null;

        [SerializeField, Range(80f, 1200f), Tooltip("Prime partial frequency (Hz) of cold metal.")]
        float _coldFundamental = 330f;

        [SerializeField, Range(0f, 0.4f), Tooltip("How much lower (share of frequency) fully hot metal rings.")]
        float _hotDetune = 0.12f;

        [SerializeField, Range(0f, 6f), Tooltip("How much faster fully hot metal dies away.")]
        float _hotDamping = 2.5f;

        [SerializeField, Range(0f, 1f), Tooltip("Overall volume.")]
        float _volume = 0.55f;

        [SerializeField, Range(0f, 0.5f), Tooltip("Volume of the resonance hum at full charge.")]
        float _humVolume = 0.14f;

        [SerializeField, Range(0f, 1f), Tooltip("Volume of the strike transient.")]
        float _clickVolume = 0.35f;

        [SerializeField, Range(0f, 1.5f), Tooltip("Volume of a crack.")]
        float _crackVolume = 0.9f;

        // Audio thread state.
        readonly float[] _re = new float[ModeCount];
        readonly float[] _im = new float[ModeCount];
        readonly float[] _rotCos = new float[ModeCount];
        readonly float[] _rotSin = new float[ModeCount];
        readonly float[] _decay = new float[ModeCount];

        // Tuning fork: one reference tone per voice at its target pitch, ringing beside the relic's own voice.
        readonly float[] _forkRe = new float[VoicingMath.VoiceCount];
        readonly float[] _forkIm = new float[VoicingMath.VoiceCount];
        readonly float[] _forkCos = new float[VoicingMath.VoiceCount];
        readonly float[] _forkSin = new float[VoicingMath.VoiceCount];
        readonly float[] _forkDecay = new float[VoicingMath.VoiceCount];
        float _humRe = 1f;
        float _humIm;
        float _hum;
        float _clickEnvelope;
        float _crackEnvelope;
        float _crackFiltered;
        uint _noise = 0x9E3779B9u;
        int _cracksPlayed;
        int _queueRead;

        // Shared between threads.
        readonly float[] _queueStrength = new float[QueueSize];
        readonly Vector4[] _queueExcitation = new Vector4[QueueSize];
        int _queueWrite;
        readonly float[] _voiceCents = new float[VoicingMath.VoiceCount];
        readonly float[] _forkCents = new float[VoicingMath.VoiceCount];
        volatile bool _voiced;
        volatile bool _forkOn;
        volatile float _heat;
        volatile float _humTarget;
        volatile float _fundamental;
        volatile float _detune;
        volatile int _crackCount;
        volatile int _hissCount;
        int _hissesPlayed;
        float _hissEnvelope;
        float _hissFiltered;
        volatile bool _running;
        float _sampleRate = 48000f;

        // Main thread state.
        RelicResonance _resonance;
        RelicSigil _sigil;
        RelicVoicing _voicing;
        AudioSource _source;
        AudioClip _carrier;
        Action<RelicResonanceEvent> _onResonated;
        Action<RelicForgedEvent> _onForged;

        /// <summary>Prime partial frequency (Hz) the relic rings at when cold.</summary>
        public float Fundamental => _fundamental;

        /// <summary>
        /// Tunes the relic: sets the prime partial (Hz) and how far out of tune, in cents, its odd partials sit. An in-tune
        /// relic rings pure; a detuned one beats.
        /// </summary>
        /// <param name="fundamental">Prime partial frequency, in hertz.</param>
        /// <param name="detuneCents">Detune of odd partials, in cents.</param>
        public void Tune(float fundamental, float detuneCents)
        {
            _fundamental = Mathf.Clamp(fundamental, 40f, 2000f);
            _detune = Mathf.Clamp(detuneCents, 0f, 100f);
        }

        /// <summary>Plays the hiss of hot metal quenched in water.</summary>
        public void PlayHiss()
        {
            _hissCount++;
        }

        /// <summary>Plays a strike of the given strength and plate mode excitation. For tools and tests.</summary>
        /// <param name="strength">Strike strength in [0, 1].</param>
        /// <param name="plateExcitation">Excitation of the four plate modes, in [0, 1].</param>
        /// <returns>False when the queue was full and the strike was dropped.</returns>
        public bool Play(float strength, Vector4 plateExcitation)
        {
            int write = _queueWrite;
            if (write - Volatile.Read(ref _queueRead) >= QueueSize)
            {
                return false;
            }

            int slot = write % QueueSize;
            _queueStrength[slot] = Mathf.Clamp01(strength);
            _queueExcitation[slot] = plateExcitation;
            Volatile.Write(ref _queueWrite, write + 1);
            return true;
        }

        void Awake()
        {
            _source = GetComponent<AudioSource>();
            _resonance = GetComponent<RelicResonance>();
            _sigil = GetComponent<RelicSigil>();
            _voicing = GetComponent<RelicVoicing>();
            if (_relic == null)
            {
                _relic = GetComponent<Rigidbody>();
            }

            _onResonated = HandleResonated;
            _onForged = HandleForged;
            _fundamental = _coldFundamental;
            _sampleRate = Mathf.Max(8000, AudioSettings.outputSampleRate);

            // A constant carrier: Unity spatializes it, and the synth multiplies it by the generated signal.
            int length = Mathf.RoundToInt(_sampleRate);
            _carrier = AudioClip.Create("Relic Synth Carrier", length, 1, Mathf.RoundToInt(_sampleRate), false);
            float[] ones = new float[length];
            for (int i = 0; i < length; i++)
            {
                ones[i] = 1f;
            }

            _carrier.SetData(ones, 0);
            _source.clip = _carrier;
            _source.loop = true;
            _source.playOnAwake = false;
            _source.dopplerLevel = 0f;
            UpdateRotators();
        }

        void OnEnable()
        {
            ForgeEvents.RelicResonated += _onResonated;
            ForgeEvents.RelicForged += _onForged;
            _running = true;
            if (_source != null && !_source.isPlaying)
            {
                _source.Play();
            }
        }

        void OnDisable()
        {
            ForgeEvents.RelicResonated -= _onResonated;
            ForgeEvents.RelicForged -= _onForged;
            _running = false;
        }

        void OnDestroy()
        {
            if (_carrier != null)
            {
                Destroy(_carrier);
            }
        }

        void Update()
        {
            if (_resonance == null)
            {
                _humTarget = 0f;
                return;
            }

            _heat = _resonance.Heat;

            SyncVoices();

            // A voiced relic rings its commission's root; its errors are in the voices themselves.
            if (_voicing != null && _voicing.Stage != VoicingStage.Dormant)
            {
                float root = _voicing.Commission.RootHz;
                if (!Mathf.Approximately(root, _fundamental) || _detune != 0f)
                {
                    Tune(root, 0f);
                }
            }
            else if (_sigil != null && _sigil.Stage != SigilStage.Dormant)
            {
                float note = _sigil.Sigil.Note;
                float detune = _resonance.IsForged ? Mathf.Clamp01(1f - _resonance.MeanQuality) * 40f : 0f;
                if (!Mathf.Approximately(note, _fundamental) || !Mathf.Approximately(detune, _detune))
                {
                    Tune(note, detune);
                }
            }

            float swell = _resonance.IsPulsing ? 0.65f + 0.35f * _resonance.TimingNow : 0.65f;
            _humTarget = _humVolume * Mathf.Clamp01(_resonance.Charge / 1.2f) * swell;
        }

        /// <summary>
        /// Copies the voicing's current pitches to the audio thread. Also called when a strike arrives, so a cut and the
        /// ring it causes always sound together.
        /// </summary>
        void SyncVoices()
        {
            if (_voicing == null || _voicing.Stage == VoicingStage.Dormant)
            {
                _voiced = false;
                _forkOn = false;
                return;
            }

            for (int m = 0; m < VoicingMath.VoiceCount; m++)
            {
                _voiceCents[m] = _voicing.VoiceCents(m);
                _forkCents[m] = _voicing.TargetCents(m);
            }

            _voiced = true;
            _forkOn = _voicing.Stage == VoicingStage.Voicing || _voicing.Stage == VoicingStage.Settling;
        }

        void Start()
        {
            // A voicing component added after this one woke is found here.
            if (_voicing == null)
            {
                _voicing = GetComponent<RelicVoicing>();
            }
        }

        void HandleResonated(RelicResonanceEvent payload)
        {
            if (!ReferenceEquals(payload.Relic, _relic))
            {
                return;
            }

            SyncVoices();
            Play(payload.Strength, payload.ModeExcitation);
            if (payload.Cracked)
            {
                _crackCount++;
            }
        }

        void HandleForged(RelicForgedEvent payload)
        {
            if (ReferenceEquals(payload.Relic, _relic) && payload.Grade != ForgeGrade.Fractured)
            {
                // The finished relic sings: every partial at once.
                Play(1f, new Vector4(1f, 1f, 1f, 1f));
            }
        }

        #region Audio thread

        void UpdateRotators()
        {
            float heat = Mathf.Clamp01(_heat);
            float pitch = 1f - _hotDetune * heat;
            float dampingScale = 1f + _hotDamping * heat * heat;
            float fundamental = _fundamental;
            float detune = Mathf.Pow(2f, _detune / 1200f);
            bool voiced = _voiced;
            for (int k = 0; k < ModeCount; k++)
            {
                float ratio = (k & 1) == 1 ? s_ratios[k] * detune : s_ratios[k];
                float omega = 2f * Mathf.PI * fundamental * ratio * pitch / _sampleRate;
                _rotCos[k] = Mathf.Cos(omega);
                _rotSin[k] = Mathf.Sin(omega);
                _decay[k] = Mathf.Exp(-dampingScale / (s_decays[k] * _sampleRate));
            }

            for (int m = 0; m < VoicingMath.VoiceCount; m++)
            {
                int k = s_primary[m];
                float baseRatio = s_ratios[k];

                // The relic's voice rings at its natural partial shifted by its tuning; the fork rings at the target.
                if (voiced)
                {
                    float omegaVoice = 2f * Mathf.PI * fundamental * baseRatio * Mathf.Pow(2f, _voiceCents[m] / 1200f) * pitch / _sampleRate;
                    _rotCos[k] = Mathf.Cos(omegaVoice);
                    _rotSin[k] = Mathf.Sin(omegaVoice);
                }

                float omegaFork = 2f * Mathf.PI * fundamental * baseRatio * Mathf.Pow(2f, _forkCents[m] / 1200f) * pitch / _sampleRate;
                _forkCos[m] = Mathf.Cos(omegaFork);
                _forkSin[m] = Mathf.Sin(omegaFork);
                _forkDecay[m] = _decay[k];
            }
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_running || channels <= 0)
            {
                return;
            }

            UpdateRotators();
            ConsumeQueue();

            // Hum rotator at half the prime, renormalized per block so rounding never grows it.
            float humOmega = 2f * Mathf.PI * _fundamental * 0.5f * (1f - _hotDetune * Mathf.Clamp01(_heat)) / _sampleRate;
            float humCos = Mathf.Cos(humOmega);
            float humSin = Mathf.Sin(humOmega);
            float humLength = Mathf.Sqrt(_humRe * _humRe + _humIm * _humIm);
            if (humLength > 1e-6f)
            {
                _humRe /= humLength;
                _humIm /= humLength;
            }
            else
            {
                _humRe = 1f;
                _humIm = 0f;
            }

            int crackRequests = _crackCount;
            if (crackRequests != _cracksPlayed)
            {
                _cracksPlayed = crackRequests;
                _crackEnvelope = 1f;
            }

            int hissRequests = _hissCount;
            if (hissRequests != _hissesPlayed)
            {
                _hissesPlayed = hissRequests;
                _hissEnvelope = 1f;
            }

            float humTarget = _humTarget;
            float volume = _volume;
            float clickVolume = _clickVolume;
            float crackVolume = _crackVolume;
            int frames = data.Length / channels;
            for (int frame = 0; frame < frames; frame++)
            {
                float sample = 0f;
                for (int k = 0; k < ModeCount; k++)
                {
                    float re = _re[k];
                    float im = _im[k];
                    float decay = _decay[k];
                    _re[k] = (re * _rotCos[k] - im * _rotSin[k]) * decay;
                    _im[k] = (re * _rotSin[k] + im * _rotCos[k]) * decay;
                    sample += _im[k];
                }

                for (int m = 0; m < VoicingMath.VoiceCount; m++)
                {
                    float fr = _forkRe[m];
                    float fi = _forkIm[m];
                    float fd = _forkDecay[m];
                    _forkRe[m] = (fr * _forkCos[m] - fi * _forkSin[m]) * fd;
                    _forkIm[m] = (fr * _forkSin[m] + fi * _forkCos[m]) * fd;
                    sample += _forkIm[m];
                }

                _hum += (humTarget - _hum) * 0.0005f;
                float hr = _humRe;
                _humRe = hr * humCos - _humIm * humSin;
                _humIm = hr * humSin + _humIm * humCos;
                sample += _hum * _humIm;

                float noise = NextNoise();
                sample += noise * _clickEnvelope * clickVolume;
                _clickEnvelope *= 0.995f;

                _crackFiltered += (noise - _crackFiltered) * 0.3f;
                sample += _crackFiltered * _crackEnvelope * crackVolume * (0.6f + 0.4f * NextNoise());
                _crackEnvelope *= 0.9997f;

                // Quench hiss: high-passed noise, a long sizzle.
                float previous = _hissFiltered;
                _hissFiltered = noise;
                sample += (noise - previous) * 0.5f * _hissEnvelope * _hissEnvelope;
                _hissEnvelope *= 0.99995f;

                sample *= volume;
                sample /= 1f + Mathf.Abs(sample);

                int offset = frame * channels;
                for (int channel = 0; channel < channels; channel++)
                {
                    data[offset + channel] *= sample;
                }
            }
        }

        void ConsumeQueue()
        {
            int write = Volatile.Read(ref _queueWrite);
            int read = _queueRead;
            while (read < write)
            {
                int slot = read % QueueSize;
                float strength = _queueStrength[slot];
                Vector4 excitation = _queueExcitation[slot];
                for (int k = 0; k < ModeCount; k++)
                {
                    // An impulse starts each partial at zero phase: added to the real part, it rings out as a sine.
                    float amount = s_gains[k] * strength * (0.3f + 0.7f * Mathf.Clamp01(excitation[s_plateMode[k]]));
                    _re[k] = Mathf.Clamp(_re[k] + amount, -1.5f, 1.5f);
                }

                if (_forkOn)
                {
                    // The fork answers with each voice's target pitch, as loud as the voice it checks.
                    for (int m = 0; m < VoicingMath.VoiceCount; m++)
                    {
                        float amount = s_gains[s_primary[m]] * strength * (0.3f + 0.7f * Mathf.Clamp01(excitation[m]));
                        _forkRe[m] = Mathf.Clamp(_forkRe[m] + amount, -1.5f, 1.5f);
                    }
                }

                _clickEnvelope = Mathf.Max(_clickEnvelope, strength);
                read++;
            }

            Volatile.Write(ref _queueRead, read);
        }

        float NextNoise()
        {
            // xorshift32: fast, allocation-free and safe on the audio thread.
            uint x = _noise;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _noise = x;
            return (x & 0xFFFFFF) / 8388608f - 1f;
        }

        #endregion
    }
}
