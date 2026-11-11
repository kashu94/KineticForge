using System;
using KineticForge.Core;
using KineticForge.Physics;
using UnityEngine;

namespace KineticForge.Crafting
{
    /// <summary>
    /// The chisel's aim point on a relic's carving face, steered by thumb micro-gestures and shaken by an unsteady stance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each <see cref="ForgeEvents.MicroGestureSlide"/> moves the cursor in face coordinates: one finger length of thumb
    /// travel moves it <see cref="SlideGain"/> face widths. X steers across the face and Y up it, clamped to the usable area.
    /// </para>
    /// <para>
    /// The strike point adds a smooth Perlin tremor whose amplitude grows as stance stability falls:
    /// <c>maxTremor * (1 - stability)^exponent</c>. A braced player aims cleanly; an upright one fights the shake. A light
    /// aim assist pulls the point toward the active mark once it is within two tolerances.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(RelicCarvingSurface))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Crafting/Carving Cursor")]
    public sealed class CarvingCursor : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        #region Serialized fields

        [Header("Steering")]
        [SerializeField, Tooltip("Hand whose slides steer the cursor. None accepts either hand.")]
        ForgeHand _hand = ForgeHand.None;

        [SerializeField, Range(0.05f, 3f), Tooltip("Face widths moved per finger length of thumb travel.")]
        float _slideGain = 0.8f;

        [SerializeField, Tooltip("Mirror horizontal steering.")]
        bool _invertX = false;

        [SerializeField, Tooltip("Mirror vertical steering.")]
        bool _invertY = false;

        [Header("Stance Tremor")]
        [SerializeField, Range(0f, 0.05f), Tooltip("Tremor amplitude (m) at zero stability.")]
        float _maxTremor = 0.012f;

        [SerializeField, Range(0.1f, 20f), Tooltip("How fast the tremor wanders (noise units per second).")]
        float _tremorFrequency = 5f;

        [SerializeField, Range(0.5f, 4f), Tooltip("Shape of the tremor curve. Above 1, a half-braced stance already steadies the aim a lot.")]
        float _tremorExponent = 1.5f;

        [Header("Aim Assist")]
        [SerializeField, Range(0f, 0.6f), Tooltip("Pull toward the active mark once within two tolerances. 0 disables.")]
        float _aimAssist = 0.3f;

        [Header("Visual")]
        [SerializeField, Tooltip("Show a small marker at the strike point.")]
        bool _showCursor = true;

        [SerializeField, Tooltip("Material for the marker. Uses the default material when empty.")]
        Material _cursorMaterial = null;

        [SerializeField, Range(0.004f, 0.04f), Tooltip("Marker diameter (m).")]
        float _cursorSize = 0.012f;

        [SerializeField, ColorUsage(false, true), Tooltip("Marker color far from the mark.")]
        Color _farColor = new Color(0.35f, 0.65f, 1f);

        [SerializeField, ColorUsage(false, true), Tooltip("Marker color on target.")]
        Color _onTargetColor = new Color(0.4f, 2.2f, 0.7f);

        #endregion

        #region Runtime state

        readonly StanceReading _stance = new StanceReading();
        RelicCarvingSurface _surface;
        Vector2 _face;
        Vector2 _noiseSeed;
        Transform _visual;
        Renderer _visualRenderer;
        MaterialPropertyBlock _block;
        Action<MicroGestureSlideEvent> _onSlide;

        #endregion

        #region Public API

        /// <summary>Current cursor position in face coordinates, before tremor and assist.</summary>
        public Vector2 FaceCoordinates
        {
            get => _face;
            set => _face = Clamp(value);
        }

        /// <summary>Face widths moved per finger length of thumb travel.</summary>
        public float SlideGain => _slideGain;

        /// <summary>Current tremor amplitude in meters.</summary>
        public float TremorAmplitude => _maxTremor * Mathf.Pow(1f - Mathf.Clamp01(_stance.Stability), _tremorExponent);

        /// <summary>
        /// Computes the point a strike would hit right now: cursor position plus stance tremor plus aim assist.
        /// </summary>
        /// <param name="worldPoint">World-space strike point on the face.</param>
        /// <param name="worldNormal">World-space face normal.</param>
        public void GetStrikePoint(out Vector3 worldPoint, out Vector3 worldNormal)
        {
            Vector2 size = _surface.UsableFaceSize;
            float amplitude = TremorAmplitude;
            Vector2 tremor = Vector2.zero;
            if (amplitude > 0f && size.x > 0f && size.y > 0f)
            {
                float t = Time.time * _tremorFrequency;
                tremor = new Vector2(
                    (Mathf.PerlinNoise(t, _noiseSeed.x) - 0.5f) * 2f * amplitude / size.x,
                    (Mathf.PerlinNoise(_noiseSeed.y, t) - 0.5f) * 2f * amplitude / size.y);
            }

            _surface.GetFacePoint(_face + tremor, out worldPoint, out worldNormal);

            if (_aimAssist > 0f && _surface.TryGetActiveMark(out Vector3 mark, out _))
            {
                float tolerance = _surface.StrikeTolerance;
                float closeness = 1f - Mathf.InverseLerp(tolerance, 2f * tolerance, Vector3.Distance(worldPoint, mark));
                worldPoint = Vector3.Lerp(worldPoint, mark, _aimAssist * closeness);
            }
        }

        /// <summary>Aim quality of a strike right now, in [0, 1], ignoring stance. For feedback and tutorials.</summary>
        /// <returns>The aim quality, or 0 when there is no active mark.</returns>
        public float PreviewAim()
        {
            if (!_surface.TryGetActiveMark(out Vector3 mark, out _))
            {
                return 0f;
            }

            GetStrikePoint(out Vector3 point, out _);
            return CarvingPrecision.Aim(Vector3.Distance(point, mark), _surface.StrikeTolerance);
        }

        /// <summary>Moves the cursor back to the center of the face.</summary>
        public void Recenter()
        {
            _face = Vector2.zero;
        }

        #endregion

        #region Unity lifecycle

        void Awake()
        {
            _surface = GetComponent<RelicCarvingSurface>();
            _block = new MaterialPropertyBlock();
            _noiseSeed = new Vector2(UnityEngine.Random.Range(0f, 500f), UnityEngine.Random.Range(0f, 500f));
            _onSlide = HandleSlide;
        }

        void OnEnable()
        {
            _stance.Enable(GetComponent<RelicStabilizer>());
            ForgeEvents.MicroGestureSlide += _onSlide;
        }

        void OnDisable()
        {
            _stance.Disable();
            ForgeEvents.MicroGestureSlide -= _onSlide;
        }

        void Start()
        {
            if (_showCursor)
            {
                CreateVisual();
            }
        }

        void LateUpdate()
        {
            if (_visual == null)
            {
                return;
            }

            bool visible = _surface.IsAcceptingStrikes;
            if (_visualRenderer.enabled != visible)
            {
                _visualRenderer.enabled = visible;
            }

            if (!visible)
            {
                return;
            }

            GetStrikePoint(out Vector3 point, out Vector3 normal);
            _visual.position = point + normal * 0.002f;

            float aim = 0f;
            if (_surface.TryGetActiveMark(out Vector3 mark, out _))
            {
                aim = CarvingPrecision.Aim(Vector3.Distance(point, mark), _surface.StrikeTolerance);
            }

            Color color = Color.Lerp(_farColor, _onTargetColor, aim);
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            _visualRenderer.SetPropertyBlock(_block);
        }

        #endregion

        #region Steering

        void HandleSlide(MicroGestureSlideEvent payload)
        {
            if (_hand != ForgeHand.None && payload.Hand != _hand)
            {
                return;
            }

            if (!_surface.IsAcceptingStrikes)
            {
                return;
            }

            Vector2 delta = payload.SlideDelta * _slideGain;
            if (_invertX)
            {
                delta.x = -delta.x;
            }

            if (_invertY)
            {
                delta.y = -delta.y;
            }

            _face = Clamp(_face + delta);
        }

        static Vector2 Clamp(Vector2 face)
        {
            if (!ForgeMath.IsFinite(face.x) || !ForgeMath.IsFinite(face.y))
            {
                return Vector2.zero;
            }

            return new Vector2(Mathf.Clamp(face.x, -0.5f, 0.5f), Mathf.Clamp(face.y, -0.5f, 0.5f));
        }

        void CreateVisual()
        {
            GameObject template = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Mesh mesh = template.GetComponent<MeshFilter>().sharedMesh;
            Material material = _cursorMaterial != null ? _cursorMaterial : template.GetComponent<Renderer>().sharedMaterial;
            Destroy(template);

            GameObject visual = new GameObject("Carving Cursor");
            visual.transform.SetParent(transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Vector3 parentScale = transform.lossyScale;
            visual.transform.localScale = new Vector3(
                _cursorSize / Mathf.Max(1e-4f, Mathf.Abs(parentScale.x)),
                _cursorSize / Mathf.Max(1e-4f, Mathf.Abs(parentScale.y)),
                _cursorSize / Mathf.Max(1e-4f, Mathf.Abs(parentScale.z)));

            _visual = visual.transform;
            _visualRenderer = renderer;
        }

        #endregion
    }
}
