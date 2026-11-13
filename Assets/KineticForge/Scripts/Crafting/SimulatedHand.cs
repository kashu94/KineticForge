using KineticForge.Core;
using KineticForge.Tracking;
using UnityEngine;

namespace KineticForge.Crafting
{
    /// <summary>
    /// Lets the Editor mimic a tracked hand where no headset exists: a fingertip that hovers over the relic under the
    /// mouse, pokes it on a click, and can be lost on demand, all through the same <see cref="FingertipStrike"/> the real
    /// hand drives. Also previews the headset's field of view.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Keys (Editor, no Meta runtime).</b> F toggles finger mode, in which the mouse moves a fingertip over the face
    /// and a left click pokes it, at the speed of a real tap. H toggles losing the hand, to see tracking loss. D, held,
    /// mutes at the aim point (see <see cref="FingertipDamper"/>). F2 toggles a headset-like field of view.
    /// </para>
    /// <para>
    /// A poke approaches the face fast enough to count as a tap, touches, and draws back, so everything downstream (the
    /// hover ghosts, the strike strength, the re-arm rule) behaves as it would for a hand. While finger mode is on the
    /// mouse click no longer strikes by itself. In a player build the component switches itself off.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(-1)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Crafting/Simulated Hand")]
    public sealed class SimulatedHand : MonoBehaviour
    {
        [SerializeField, Range(0.02f, 0.2f), Tooltip("Distance (m) in front of the face the fingertip hovers at.")]
        float _hoverDistance = 0.06f;

        [SerializeField, Range(0.02f, 0.2f), Tooltip("Seconds a poke takes to reach the face.")]
        float _approachSeconds = 0.07f;

        [SerializeField, Range(0f, 0.3f), Tooltip("Seconds the fingertip stays at the face.")]
        float _contactSeconds = 0.05f;

        [SerializeField, Range(0.05f, 0.5f), Tooltip("Seconds a poke takes to draw back.")]
        float _retreatSeconds = 0.2f;

        [SerializeField, Range(60f, 110f), Tooltip("Vertical field of view (degrees) of the headset preview.")]
        float _headsetFieldOfView = 96f;

        [SerializeField, Range(0.6f, 1.4f), Tooltip("Aspect ratio of the headset preview: a Quest eye is nearly square.")]
        float _headsetAspect = 0.935f;

        const float ContactDepth = 0.002f;

        RelicCarvingSurface _surface;
        FingertipStrike _finger;
        FingertipDamper _damper;
        MicroGestureTracker _strikeTracker;
        GameObject _marker;
        Renderer _markerRenderer;
        Camera _camera;
        float _storedFieldOfView;
        bool _headsetView;
        float _pokeTime = -1f;
        bool _fingerMode;
        bool _handLost;
        Vector3 _tip;

        /// <summary>True while the mouse drives a simulated fingertip.</summary>
        public bool FingerMode
        {
            get => _fingerMode;
            set => SetFingerMode(value);
        }

        /// <summary>True while the simulated hand is lost.</summary>
        public bool HandLost => _handLost;

        /// <summary>True while a poke is under way.</summary>
        public bool IsPoking => _pokeTime >= 0f;

        /// <summary>World position of the simulated fingertip.</summary>
        public Vector3 TipPosition => _tip;

        /// <summary>Starts a poke, unless one is under way.</summary>
        public void BeginPoke()
        {
            if (_pokeTime < 0f)
            {
                _pokeTime = 0f;
            }
        }

        /// <summary>Loses or regains the simulated hand.</summary>
        /// <param name="lost">True to lose it.</param>
        public void SetHandLost(bool lost)
        {
            _handLost = lost;
            if (lost)
            {
                _pokeTime = -1f;
                _finger?.LoseTrack();
                _damper?.LoseTrack();
            }
        }

        /// <summary>
        /// Advances the simulated fingertip by one frame: moves it over <paramref name="face"/> at the hover distance or
        /// along a poke, and hands its position to the strike. Does nothing unless finger mode is on and the hand is there.
        /// </summary>
        /// <param name="deltaTime">Seconds since the previous frame.</param>
        /// <param name="face">Face coordinates under the pointer, in [-1, 1].</param>
        public void Step(float deltaTime, Vector2 face)
        {
            if (!_fingerMode || _handLost || _surface == null || _finger == null)
            {
                SetMarker(false, false);
                return;
            }

            float depth = _hoverDistance;
            if (_pokeTime >= 0f)
            {
                _pokeTime += deltaTime;
                float total = _approachSeconds + _contactSeconds + _retreatSeconds;
                if (_pokeTime < _approachSeconds)
                {
                    depth = Mathf.Lerp(_hoverDistance, ContactDepth, _pokeTime / _approachSeconds);
                }
                else if (_pokeTime < _approachSeconds + _contactSeconds)
                {
                    depth = ContactDepth;
                }
                else if (_pokeTime < total)
                {
                    depth = Mathf.Lerp(ContactDepth, _hoverDistance, (_pokeTime - _approachSeconds - _contactSeconds) / _retreatSeconds);
                }
                else
                {
                    _pokeTime = -1f;
                }
            }

            _surface.GetFacePoint(face, out Vector3 onFace, out Vector3 normal);
            _tip = onFace + normal * depth;
            _finger.ProcessTip(_tip, Mathf.Max(deltaTime, 1e-4f));
            SetMarker(true, _pokeTime >= 0f);
        }

        void SetFingerMode(bool on)
        {
            if (_fingerMode == on)
            {
                return;
            }

            _fingerMode = on;
            if (!on)
            {
                _pokeTime = -1f;
                _finger?.LoseTrack();
                SetMarker(false, false);
            }

            // A mouse click would otherwise strike by itself as well as poke.
            if (_strikeTracker == null)
            {
                MicroGestureTracker[] trackers = FindObjectsByType<MicroGestureTracker>(FindObjectsSortMode.None);
                for (int i = 0; i < trackers.Length; i++)
                {
                    if (trackers[i].Hand == ForgeHand.Right)
                    {
                        _strikeTracker = trackers[i];
                        break;
                    }
                }
            }

            if (_strikeTracker != null)
            {
                _strikeTracker.MouseFallbackEnabled = !on;
            }
        }

        void SetMarker(bool visible, bool poking)
        {
            if (_marker == null)
            {
                if (!visible)
                {
                    return;
                }

                _marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _marker.name = "Simulated Fingertip";
                _marker.transform.localScale = Vector3.one * 0.016f;
                Destroy(_marker.GetComponent<Collider>());
                _markerRenderer = _marker.GetComponent<Renderer>();
                _markerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            _marker.SetActive(visible);
            if (!visible)
            {
                return;
            }

            _marker.transform.position = _tip;
            Color color = poking ? new Color(1f, 0.95f, 0.8f) : new Color(0.4f, 0.85f, 1f);
            if (_markerRenderer.material.HasProperty("_BaseColor"))
            {
                _markerRenderer.material.SetColor("_BaseColor", color);
            }
        }

        void Awake()
        {
            if (!Application.isEditor)
            {
                // Mimicking a hand is for the Editor; a headset has real ones.
                enabled = false;
                return;
            }

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

            if (_finger == null)
            {
                _finger = GetComponent<FingertipStrike>();
            }

            if (_damper == null)
            {
                _damper = GetComponent<FingertipDamper>();
            }
        }

        void OnDisable()
        {
            if (_fingerMode)
            {
                SetFingerMode(false);
            }

            SetHeadsetView(false);
        }

        void OnDestroy()
        {
            if (_marker != null)
            {
                Destroy(_marker);
            }
        }

        void Update()
        {
            if (!SimulatorKeyboard.IsAvailable || ForgePlatform.IsMetaRuntimeAvailable)
            {
                return;
            }

            if (SimulatorKeyboard.WasPressedThisFrame(KeyCode.F))
            {
                SetFingerMode(!_fingerMode);
            }

            if (SimulatorKeyboard.WasPressedThisFrame(KeyCode.H))
            {
                SetHandLost(!_handLost);
            }

            if (SimulatorKeyboard.WasPressedThisFrame(KeyCode.F2))
            {
                SetHeadsetView(!_headsetView);
            }

            if (!_fingerMode)
            {
                return;
            }

            if (SimulatorPointer.WasPressedThisFrame(SimulatorMouseButton.Left))
            {
                BeginPoke();
            }

            if (TryPointerFace(out Vector2 face))
            {
                _lastFace = face;
            }

            Step(Time.unscaledDeltaTime, _lastFace);
        }

        Vector2 _lastFace;

        /// <summary>The face coordinates under the mouse, found by casting the camera ray onto the face's plane.</summary>
        bool TryPointerFace(out Vector2 face)
        {
            face = default;
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            if (_camera == null || _surface == null)
            {
                return false;
            }

            Ray ray = _camera.ScreenPointToRay(SimulatorPointer.Position);
            _surface.GetFacePoint(Vector2.zero, out Vector3 center, out Vector3 normal);
            float facing = Vector3.Dot(ray.direction, normal);
            if (facing > -1e-4f)
            {
                return false;
            }

            float distance = Vector3.Dot(center - ray.origin, normal) / facing;
            if (distance < 0f)
            {
                return false;
            }

            Vector2 uv = _surface.WorldToUV(ray.origin + ray.direction * distance);
            float usable = Mathf.Max(0.01f, _surface.UsableArea);
            face = new Vector2(Mathf.Clamp((uv.x - 0.5f) / usable, -1.4f, 1.4f), Mathf.Clamp((uv.y - 0.5f) / usable, -1.4f, 1.4f));
            return true;
        }

        void SetHeadsetView(bool on)
        {
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            if (_camera == null || on == _headsetView)
            {
                return;
            }

            _headsetView = on;
            if (on)
            {
                _storedFieldOfView = _camera.fieldOfView;
                _camera.fieldOfView = _headsetFieldOfView;
                _camera.aspect = _headsetAspect;
            }
            else
            {
                _camera.fieldOfView = _storedFieldOfView;
                _camera.ResetAspect();
            }
        }
    }
}
