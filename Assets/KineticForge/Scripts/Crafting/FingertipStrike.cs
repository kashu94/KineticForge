using KineticForge.Core;
using KineticForge.Tracking;
using UnityEngine;

namespace KineticForge.Crafting
{
    /// <summary>
    /// Lets the player touch the bronze: the strike hand's index fingertip hovering over the relic steers the aim point,
    /// so the tuner's ghost markers follow the finger, and tapping the face strikes it. The pointer and pinch still work;
    /// the finger simply takes over while it is near.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A tap is a fingertip approaching the face fast enough and reaching it from the front while over the face
    /// (<see cref="FingertipTouch"/>), so a hand that merely drifts past, rests nearby or hovers never strikes. Its speed
    /// sets the strike's strength. While the finger hovers, <see cref="HandPointerAim.Suppressed"/> keeps the pointer
    /// from fighting it for the aim point.
    /// </para>
    /// <para>
    /// Tracking comes from the hand skeleton and needs the Meta XR Core SDK (KF_META_XR_CORE). <see cref="ProcessTip"/>
    /// takes any fingertip position, so tests and other input can drive it.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(5)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Crafting/Fingertip Strike")]
    public sealed class FingertipStrike : MonoBehaviour
    {
        const float ResolveInterval = 2f;

        [SerializeField, Tooltip("Hand whose index fingertip strikes.")]
        ForgeHand _hand = ForgeHand.Right;

        [SerializeField, Range(0.03f, 0.25f), Tooltip("Distance (m) in front of the face within which the fingertip steers the aim point.")]
        float _hoverDistance = 0.1f;

        [SerializeField, Range(0.002f, 0.03f), Tooltip("Distance (m) from the face at which the fingertip counts as touching.")]
        float _contactDistance = 0.008f;

        [SerializeField, Range(0.03f, 0.2f), Tooltip("Distance (m) the fingertip must draw back before it can tap again.")]
        float _rearmDistance = 0.035f;

        [SerializeField, Range(0.02f, 0.5f), Tooltip("Slowest approach speed (m/s) that counts as a tap.")]
        float _minimumSpeed = 0.1f;

        readonly FingertipTouch _touch = new FingertipTouch();
        RelicCarvingSurface _surface;
        CarvingCursor _cursor;
        ChiselStrikeController _strikes;
        HandPointerAim _aim;
        bool _hovering;

#if KF_META_XR_CORE
        readonly HandSkeletonReader _reader = new HandSkeletonReader();
        float _nextResolveTime;
#endif

        /// <summary>True while the fingertip hovers over the face and steers the aim point.</summary>
        public bool IsHovering => _hovering;

        /// <summary>Taps that struck the relic.</summary>
        public int TouchCount { get; private set; }

        /// <summary>
        /// Takes one sample of the fingertip. Steers the aim point while it hovers over the face, and strikes when it taps.
        /// </summary>
        /// <param name="tipWorld">World-space fingertip position.</param>
        /// <param name="deltaTime">Seconds since the previous sample.</param>
        /// <returns>True when the sample was a tap that struck the relic.</returns>
        public bool ProcessTip(Vector3 tipWorld, float deltaTime)
        {
            if (_surface == null || _cursor == null)
            {
                return false;
            }

            _touch.ContactDistance = _contactDistance;
            _touch.RearmDistance = Mathf.Max(_rearmDistance, _contactDistance + 0.005f);
            _touch.MinimumSpeed = _minimumSpeed;

            _surface.GetFacePoint(Vector2.zero, out Vector3 center, out Vector3 normal);
            float distance = Vector3.Dot(tipWorld - center, normal);
            Vector2 uv = _surface.WorldToUV(tipWorld);
            bool over = _surface.IsAimable && uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
            SetHovering(over && distance <= _hoverDistance && distance >= -0.04f);
            if (_hovering)
            {
                _cursor.FaceCoordinates = ToFace(uv);
            }

            if (_touch.Step(distance, deltaTime, over, out float speed))
            {
                _cursor.FaceCoordinates = ToFace(uv);
                TouchCount++;
                _strikes?.Strike(_hand, _touch.StrengthFor(speed));
                return true;
            }

            return false;
        }

        /// <summary>The hand was lost: forget the last sample and hand the aim point back to the pointer.</summary>
        public void LoseTrack()
        {
            _touch.Reset();
            SetHovering(false);
        }

        void SetHovering(bool hovering)
        {
            _hovering = hovering;
            if (_aim != null)
            {
                _aim.Suppressed = hovering;
            }
        }

        Vector2 ToFace(Vector2 uv)
        {
            float usable = Mathf.Max(0.01f, _surface.UsableArea);
            return new Vector2(Mathf.Clamp((uv.x - 0.5f) / usable, -1f, 1f), Mathf.Clamp((uv.y - 0.5f) / usable, -1f, 1f));
        }

        void Awake()
        {
            _surface = GetComponent<RelicCarvingSurface>();
            _cursor = GetComponent<CarvingCursor>();
            _strikes = GetComponent<ChiselStrikeController>();
        }

        void Start()
        {
            // Components added after this one woke are found here.
            if (_surface == null)
            {
                _surface = GetComponent<RelicCarvingSurface>();
            }

            if (_cursor == null)
            {
                _cursor = GetComponent<CarvingCursor>();
            }

            if (_strikes == null)
            {
                _strikes = GetComponent<ChiselStrikeController>();
            }

            _aim = FindAnyObjectByType<HandPointerAim>();
        }

        void OnDisable()
        {
            LoseTrack();
        }

        void LateUpdate()
        {
#if KF_META_XR_CORE
            if (!ForgePlatform.IsMetaRuntimeAvailable)
            {
                return;
            }

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
                ProcessTip(tip, Mathf.Max(Time.unscaledDeltaTime, 1e-4f));
            }
            else
            {
                LoseTrack();
            }
#endif
        }

        void OnValidate()
        {
            _rearmDistance = Mathf.Max(_rearmDistance, _contactDistance + 0.005f);
        }
    }
}
