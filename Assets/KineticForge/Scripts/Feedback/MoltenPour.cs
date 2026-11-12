using System;
using KineticForge.Core;
using KineticForge.Crafting;
using UnityEngine;

namespace KineticForge.Feedback
{
    /// <summary>
    /// The opening: a crucible tips over the cradle, a stream of molten bronze pours, and the relic fills its mold from
    /// the bottom up, white-hot, then settles to a working glow. Plays at the intro and again whenever a fresh relic is
    /// forged.
    /// </summary>
    /// <remarks>
    /// Drives <see cref="RelicForgeRenderer.Fill"/>, holds the relic at full heat while it pours, and suspends the
    /// <see cref="RelicSigil"/> until the relic exists. The stream is a thin molten cylinder stretched each frame from the
    /// crucible's lip to the rising surface. The per-frame path does not allocate.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Feedback/Molten Pour")]
    public sealed class MoltenPour : MonoBehaviour
    {
        [SerializeField, Tooltip("Relic to pour. Found in the scene when empty.")]
        RelicForgeRenderer _relic = null;

        [SerializeField, Tooltip("Pivot the crucible tips around.")]
        Transform _crucible = null;

        [SerializeField, Tooltip("The crucible's hanging root: lowered on its chain for a pour, hoisted out of view otherwise.")]
        Transform _hoist = null;

        [SerializeField, Range(0f, 2f), Tooltip("How far (m) the crucible is hoisted when not pouring.")]
        float _hoistHeight = 0.9f;

        [SerializeField, Tooltip("Point the molten stream leaves the crucible from.")]
        Transform _lip = null;

        [SerializeField, Tooltip("Thin cylinder drawn as the molten stream.")]
        Transform _stream = null;

        [SerializeField, Range(10f, 120f), Tooltip("Tip angle (degrees) while pouring.")]
        float _tipAngle = 75f;

        [SerializeField, Range(1f, 10f), Tooltip("Seconds the first pour takes, at the intro.")]
        float _introSeconds = 6f;

        [SerializeField, Range(1f, 10f), Tooltip("Seconds a later pour takes.")]
        float _repourSeconds = 3.5f;

        [SerializeField, Range(0.002f, 0.05f), Tooltip("Stream thickness (m).")]
        float _streamWidth = 0.016f;

        RelicResonance _resonance;
        RelicSigil _sigil;
        Rigidbody _body;
        Quaternion _restRotation;
        Vector3 _loweredPosition;
        float _time = -1f;
        float _duration;
        Action<GamePhaseChangedEvent> _onPhaseChanged;

        /// <summary>True while a pour plays.</summary>
        public bool IsPouring => _time >= 0f;

        /// <summary>Starts a pour lasting <paramref name="seconds"/>.</summary>
        /// <param name="seconds">Duration, in seconds.</param>
        public void Pour(float seconds)
        {
            if (_relic == null)
            {
                return;
            }

            _duration = Mathf.Max(0.5f, seconds);
            _time = 0f;
            _relic.Fill = 0f;
            if (_sigil != null)
            {
                _sigil.Suspended = true;
            }
        }

        void Awake()
        {
            _onPhaseChanged = HandlePhaseChanged;
            if (_crucible != null)
            {
                _restRotation = _crucible.localRotation;
            }

            if (_hoist != null)
            {
                _loweredPosition = _hoist.position;
                _hoist.position = _loweredPosition + Vector3.up * _hoistHeight;
            }

            if (_stream != null)
            {
                _stream.gameObject.SetActive(false);
            }

            // Resolved here, not in Start: the intro's phase event can arrive before this component starts.
            if (_relic == null)
            {
                _relic = FindAnyObjectByType<RelicForgeRenderer>();
            }

            if (_relic != null)
            {
                _resonance = _relic.GetComponent<RelicResonance>();
                _sigil = _relic.GetComponent<RelicSigil>();
                _body = _relic.GetComponent<Rigidbody>();
            }
        }

        void OnEnable()
        {
            ForgeEvents.GamePhaseChanged += _onPhaseChanged;
        }

        void OnDisable()
        {
            ForgeEvents.GamePhaseChanged -= _onPhaseChanged;
        }

        void HandlePhaseChanged(GamePhaseChangedEvent payload)
        {
            if (payload.Current == ForgePhase.Intro)
            {
                Pour(_introSeconds);
            }
            else if (payload.Current == ForgePhase.FirstRelicForging && payload.Previous == ForgePhase.Completion)
            {
                Pour(_repourSeconds);
            }
        }

        void Update()
        {
            if (_time < 0f || _relic == null)
            {
                return;
            }

            _time += Time.deltaTime;
            float t = _time / _duration;

            // Lower on the chain, tip, pour, tip back, hoist away.
            if (_hoist != null)
            {
                float lowered = t < 0.12f ? Smooth(t / 0.12f) : t < 0.86f ? 1f : 1f - Smooth((t - 0.86f) / 0.14f);
                _hoist.position = _loweredPosition + Vector3.up * (_hoistHeight * (1f - lowered));
            }

            float tip = t < 0.12f ? 0f : t < 0.24f ? Smooth((t - 0.12f) / 0.12f) : t < 0.78f ? 1f : 1f - Smooth((t - 0.78f) / 0.08f);
            if (_crucible != null)
            {
                _crucible.localRotation = _restRotation * Quaternion.Euler(0f, 0f, -_tipAngle * tip);
            }

            float fill = Mathf.Clamp01((t - 0.22f) / 0.56f);
            _relic.Fill = fill;
            if (_resonance != null)
            {
                _resonance.SetHeat(Mathf.Lerp(1f, 0.85f, Mathf.Clamp01((t - 0.75f) / 0.25f)));
            }

            UpdateStream(tip > 0.6f && fill < 1f, fill);

            if (t >= 1f)
            {
                _time = -1f;
                _relic.Fill = 1f;
                UpdateStream(false, 1f);
                if (_crucible != null)
                {
                    _crucible.localRotation = _restRotation;
                }

                if (_hoist != null)
                {
                    _hoist.position = _loweredPosition + Vector3.up * _hoistHeight;
                }

                if (_sigil != null)
                {
                    _sigil.Suspended = false;
                }
            }
        }

        void UpdateStream(bool visible, float fill)
        {
            if (_stream == null || _lip == null)
            {
                return;
            }

            if (_stream.gameObject.activeSelf != visible)
            {
                _stream.gameObject.SetActive(visible);
            }

            if (!visible)
            {
                return;
            }

            // From the lip straight down to the molten surface rising through the mold.
            Transform relic = _relic.transform;
            Vector3 center = _body != null ? _body.worldCenterOfMass : relic.position;
            Vector3 top = _lip.position;
            float surface = center.y + (fill - 0.5f) * 0.3f;
            Vector3 bottom = new Vector3(top.x, Mathf.Min(surface, top.y - 0.01f), top.z);
            Vector3 span = bottom - top;
            _stream.SetPositionAndRotation((top + bottom) * 0.5f, Quaternion.FromToRotation(Vector3.up, span.normalized));
            Vector3 parentScale = _stream.parent != null ? _stream.parent.lossyScale : Vector3.one;
            _stream.localScale = new Vector3(_streamWidth / parentScale.x, span.magnitude * 0.5f / parentScale.y, _streamWidth / parentScale.z);
        }

        static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
