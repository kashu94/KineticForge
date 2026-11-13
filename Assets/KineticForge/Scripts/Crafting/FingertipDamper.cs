using KineticForge.Core;
using KineticForge.Tracking;
using UnityEngine;

namespace KineticForge.Crafting
{
    /// <summary>
    /// Gives the other hand a job: a fingertip resting on the relic mutes the voices that move most under it, exactly as
    /// touching a bell silences its vibration. With the strike hand ringing the relic and this hand muting, the player can
    /// isolate one voice and hear whether it is off. In the Editor, hold D to press a finger at the aim point.
    /// </summary>
    /// <remarks>
    /// <para>
    /// While the fingertip is touching the face and voicing is active, <see cref="RelicResonance.SetDamping"/> mutes
    /// every voice in proportion to its mode shape under the finger (the synth silences it and the sand figure fades
    /// with it). Lifting the finger, losing the hand or leaving voicing lifts the touch. A finger on a nodal line mutes
    /// almost nothing, so where you press is a choice.
    /// </para>
    /// <para>
    /// Tracking comes from the hand skeleton and needs the Meta XR Core SDK (KF_META_XR_CORE). <see cref="ProcessTip"/>
    /// takes any fingertip position, so tests and other input can drive it.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(5)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Crafting/Fingertip Damper")]
    public sealed class FingertipDamper : MonoBehaviour
    {
        const float ResolveInterval = 2f;

        [SerializeField, Tooltip("Hand whose index fingertip mutes. The strike hand is the other one.")]
        ForgeHand _hand = ForgeHand.Left;

        [SerializeField, Range(0.002f, 0.04f), Tooltip("Distance (m) from the face within which the fingertip counts as pressing.")]
        float _touchDistance = 0.014f;

        RelicCarvingSurface _surface;
        RelicResonance _resonance;
        RelicVoicing _voicing;
        CarvingCursor _cursor;
        bool _damping;

#if KF_META_XR_CORE
        readonly HandSkeletonReader _reader = new HandSkeletonReader();
        float _nextResolveTime;
#endif

        /// <summary>Hand whose fingertip mutes.</summary>
        public ForgeHand Hand => _hand;

        /// <summary>True while a fingertip is muting the relic.</summary>
        public bool IsDamping => _damping;

        /// <summary>
        /// Takes one sample of the fingertip: mutes at the point while it presses the face, and lifts the touch otherwise.
        /// </summary>
        /// <param name="tipWorld">World-space fingertip position.</param>
        /// <returns>True while the fingertip is pressing the face.</returns>
        public bool ProcessTip(Vector3 tipWorld)
        {
            if (_surface == null || _resonance == null)
            {
                return false;
            }

            _surface.GetFacePoint(Vector2.zero, out Vector3 center, out Vector3 normal);
            float distance = Vector3.Dot(tipWorld - center, normal);
            Vector2 uv = _surface.WorldToUV(tipWorld);
            bool over = uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
            bool pressing = over && IsVoicing() && distance <= _touchDistance && distance >= -0.04f;
            Press(pressing, uv);
            return pressing;
        }

        /// <summary>Lifts the touch, for example when the hand is lost.</summary>
        public void LoseTrack()
        {
            Press(false, default);
        }

        bool IsVoicing()
        {
            return _voicing == null || _voicing.IsActive;
        }

        void Press(bool pressing, Vector2 uv)
        {
            if (pressing)
            {
                _resonance.SetDamping(uv, 1f);
                _damping = true;
            }
            else if (_damping)
            {
                _resonance.ClearDamping();
                _damping = false;
            }
        }

        void Awake()
        {
            Resolve();
        }

        void Start()
        {
            Resolve();
        }

        void Resolve()
        {
            if (_surface == null)
            {
                _surface = GetComponent<RelicCarvingSurface>();
            }

            if (_resonance == null)
            {
                _resonance = GetComponent<RelicResonance>();
            }

            if (_voicing == null)
            {
                _voicing = GetComponent<RelicVoicing>();
            }

            if (_cursor == null)
            {
                _cursor = GetComponent<CarvingCursor>();
            }
        }

        void OnDisable()
        {
            LoseTrack();
        }

        void LateUpdate()
        {
            // In the Editor, holding D presses a finger on the relic at the aim point.
            if (SimulatorKeyboard.IsAvailable && Application.isEditor && SimulatorKeyboard.IsHeld(KeyCode.D) &&
                _cursor != null && _surface != null && _resonance != null && IsVoicing())
            {
                _cursor.GetStrikePoint(out Vector3 point, out _);
                Press(true, _surface.WorldToUV(point));
                return;
            }

#if KF_META_XR_CORE
            if (ForgePlatform.IsMetaRuntimeAvailable)
            {
                if (!_reader.IsBound && Time.unscaledTime >= _nextResolveTime)
                {
                    _nextResolveTime = Time.unscaledTime + ResolveInterval;
                    OVRSkeleton skeleton = HandSkeletonReader.FindHandSkeleton(_hand);
                    if (skeleton != null)
                    {
                        _reader.Bind(skeleton, _hand);
                    }
                }

                if (_reader.IsTracked && _reader.TryGetJoint(HandJoint.IndexTip, out Vector3 tip))
                {
                    ProcessTip(tip);
                    return;
                }
            }
#endif
            if (_damping && !IsHeldByTest)
            {
                LoseTrack();
            }
        }

        /// <summary>Set by tests that drive <see cref="ProcessTip"/> by hand, so the frame loop does not lift their touch.</summary>
        public bool IsHeldByTest { get; set; }
    }
}
