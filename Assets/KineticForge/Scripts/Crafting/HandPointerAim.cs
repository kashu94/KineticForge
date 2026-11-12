using KineticForge.Core;
using KineticForge.Tracking;
using UnityEngine;

namespace KineticForge.Crafting
{
    /// <summary>
    /// Hands-only aiming: point at the relic and the chisel follows. Casts this hand's system pointer ray, the stable
    /// shoulder-to-hand ray Meta computes for hand tracking, onto the carved face and steers the
    /// <see cref="CarvingCursor"/> to where it lands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Aim is smoothed with a 1€ filter, steady when held, quick when swept. It freezes while the index finger pinches,
    /// so the pinch that strikes cannot drag the aim, and steps aside while the thumb rests on the index finger, so
    /// micro-gesture slides can make fine corrections.
    /// </para>
    /// <para>
    /// Requires the Meta XR Core SDK (KF_META_XR_CORE) and a tracked hand; otherwise it stays idle and the other aim
    /// paths (thumb slides, thumbstick, mouse) work as before. The per-frame path does not allocate.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(-5)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Crafting/Hand Pointer Aim")]
    public sealed class HandPointerAim : MonoBehaviour
    {
        const float ResolveInterval = 2f;

        [SerializeField, Tooltip("Hand whose pointer aims.")]
        ForgeHand _hand = ForgeHand.Right;

        [SerializeField, Tooltip("Cursor to steer. Found in the scene when empty.")]
        CarvingCursor _cursor = null;

        [SerializeField, Range(0.3f, 5f), Tooltip("Longest pointer reach (m).")]
        float _maxDistance = 2.5f;

#if KF_META_XR_CORE
        [SerializeField, Range(0.1f, 0.9f), Tooltip("Index pinch strength above which the aim holds still.")]
        float _pinchHold = 0.35f;
#endif

        [SerializeField, Tooltip("1€ filter on the aim, in face units.")]
        OneEuroSettings _smoothing = new OneEuroSettings(1.2f, 6f, 1f);

        RelicCarvingSurface _surface;
        OneEuroFilter _filterX;
        OneEuroFilter _filterY;
        bool _aiming;

#if KF_META_XR_CORE
        OVRHand _ovrHand;
        MicroGestureTracker _microGestures;
        float _nextResolveTime;
#endif

        /// <summary>True while the pointer is on the relic's face and steering the cursor.</summary>
        public bool IsAiming => _aiming;

        /// <summary>Hand whose pointer aims.</summary>
        public ForgeHand Hand => _hand;

        void OnDisable()
        {
            _aiming = false;
            _filterX.Reset();
            _filterY.Reset();
        }

        void Update()
        {
            _aiming = false;
#if KF_META_XR_CORE
            if (!ForgePlatform.IsMetaRuntimeAvailable)
            {
                return;
            }

            if (Time.unscaledTime >= _nextResolveTime && (_ovrHand == null || _cursor == null))
            {
                Resolve();
            }

            if (_ovrHand == null || _cursor == null || _surface == null || !_surface.IsAimable)
            {
                return;
            }

            if (!_ovrHand.IsTracked || !_ovrHand.IsPointerPoseValid || _ovrHand.PointerPose == null)
            {
                _filterX.Reset();
                _filterY.Reset();
                return;
            }

            if (_ovrHand.GetFingerPinchStrength(OVRHand.HandFinger.Index) > _pinchHold ||
                (_microGestures != null && _microGestures.IsInContact))
            {
                return;
            }

            Transform pose = _ovrHand.PointerPose;
            if (!TryAim(pose.position, pose.forward, out Vector2 face))
            {
                return;
            }

            float deltaTime = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            OneEuroSettings settings = _smoothing.Sanitized();
            _cursor.FaceCoordinates = new Vector2(
                _filterX.Filter(face.x, deltaTime, settings),
                _filterY.Filter(face.y, deltaTime, settings));
            _aiming = true;
#endif
        }

        /// <summary>
        /// Intersects a ray with the relic's carved face. For tools and tests.
        /// </summary>
        /// <param name="origin">World-space ray origin.</param>
        /// <param name="direction">World-space ray direction.</param>
        /// <param name="faceCoordinates">Face coordinates hit, unclamped.</param>
        /// <returns>True when the ray hits the front of the face within reach.</returns>
        public bool TryAim(Vector3 origin, Vector3 direction, out Vector2 faceCoordinates)
        {
            faceCoordinates = default;
            if (_surface == null && _cursor != null)
            {
                _surface = _cursor.GetComponent<RelicCarvingSurface>();
            }

            if (_surface == null || direction.sqrMagnitude < 1e-8f)
            {
                return false;
            }

            _surface.GetFacePoint(Vector2.zero, out Vector3 center, out Vector3 normal);
            Vector3 forward = direction.normalized;
            float facing = Vector3.Dot(forward, normal);
            if (facing > -0.05f)
            {
                return false;
            }

            float distance = Vector3.Dot(center - origin, normal) / facing;
            if (distance < 0f || distance > _maxDistance)
            {
                return false;
            }

            Vector2 uv = _surface.WorldToUV(origin + forward * distance);
            float usable = Mathf.Max(0.01f, _surface.UsableArea);
            faceCoordinates = new Vector2((uv.x - 0.5f) / usable, (uv.y - 0.5f) / usable);
            return true;
        }

        /// <summary>Sets the cursor to steer. For tools and tests.</summary>
        /// <param name="cursor">The cursor.</param>
        public void SetCursor(CarvingCursor cursor)
        {
            _cursor = cursor;
            _surface = cursor != null ? cursor.GetComponent<RelicCarvingSurface>() : null;
        }

        void Start()
        {
            if (_cursor == null)
            {
                _cursor = FindAnyObjectByType<CarvingCursor>();
            }

            if (_cursor != null)
            {
                _surface = _cursor.GetComponent<RelicCarvingSurface>();
            }
        }

#if KF_META_XR_CORE
        void Resolve()
        {
            _nextResolveTime = Time.unscaledTime + ResolveInterval;
            if (_cursor == null)
            {
                SetCursor(FindAnyObjectByType<CarvingCursor>());
            }

            if (_ovrHand == null)
            {
                OVRSkeleton skeleton = HandSkeletonReader.FindHandSkeleton(_hand);
                if (skeleton != null)
                {
                    _ovrHand = skeleton.GetComponent<OVRHand>();
                }
            }

            if (_microGestures == null)
            {
                MicroGestureTracker[] trackers = FindObjectsByType<MicroGestureTracker>(FindObjectsSortMode.None);
                for (int i = 0; i < trackers.Length; i++)
                {
                    if (trackers[i].Hand == _hand)
                    {
                        _microGestures = trackers[i];
                        break;
                    }
                }
            }
        }
#endif
    }
}
