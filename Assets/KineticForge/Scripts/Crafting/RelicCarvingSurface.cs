using System;
using KineticForge.Core;
using UnityEngine;

namespace KineticForge.Crafting
{
    /// <summary>
    /// The carvable face of a relic and the rune carved into it: an ordered pattern of strike marks, one active at a time.
    /// A qualified <see cref="ForgeEvents.ChiselStrike"/> on this relic completes the active mark and lights the next.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The face is one side of the relic's local bounds (its <see cref="BoxCollider"/>, else its mesh), picked by
    /// <see cref="FaceNormal"/>. Pattern points are in face coordinates: [-0.5, 0.5] on both axes, scaled by the usable
    /// area so marks stay off the edges. Face coordinates map to world space through the relic's transform, so the rune
    /// moves, turns and scales with it.
    /// </para>
    /// <para>
    /// The default rune has 12 marks, matching the 12 qualified strikes <see cref="ForgeGameManager"/> requires, and the
    /// same 0.35 precision bar. Keep them in step when tuning either.
    /// </para>
    /// <para>
    /// While a game flow is running, strikes count only during <see cref="ForgePhase.FirstRelicForging"/>, and the rune
    /// resets when that phase starts. Without a game flow, strikes always count. Marker visuals carry no colliders, so
    /// they never change the relic's mass properties.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Crafting/Relic Carving Surface")]
    public sealed class RelicCarvingSurface : MonoBehaviour
    {
        const string LogSource = nameof(RelicCarvingSurface);
        const float MarkerLift = 0.0015f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        #region Serialized fields

        [Header("Face")]
        [SerializeField, Tooltip("Local direction of the carved face. Back (-Z) faces a player standing in front of an unrotated relic.")]
        Vector3 _faceNormal = Vector3.back;

        [SerializeField, Tooltip("Local direction that is 'up' on the face.")]
        Vector3 _faceUp = Vector3.up;

        [SerializeField, Range(0.3f, 1f), Tooltip("Share of the face the rune and cursor may use, keeping them off the edges.")]
        float _usableArea = 0.8f;

        [Header("Rune")]
        [SerializeField, Tooltip("Strike marks in carving order, in face coordinates from -0.5 to 0.5.")]
        Vector2[] _pattern = CreateDefaultPattern();

        [SerializeField, Tooltip("Grooves of the rune: pairs of mark indices carved as a line once both marks are carved.")]
        Vector2Int[] _strokes = CreateDefaultStrokes();

        [SerializeField, Range(0.005f, 0.06f), Tooltip("Distance (m) from a mark at which aim quality reaches zero.")]
        float _strikeTolerance = 0.02f;

        [SerializeField, Range(0f, 1f), Tooltip("Precision a strike needs to complete its mark. Keep equal to the game manager's bar.")]
        float _minStrikePrecision = 0.35f;

        [SerializeField, Tooltip("While a game flow runs, accept strikes only in the First Relic Forging phase.")]
        bool _onlyDuringForging = true;

        [Header("Markers")]
        [SerializeField, Tooltip("Show a small glowing marker for every mark.")]
        bool _showMarkers = true;

        [SerializeField, Tooltip("Material for markers. An unlit material makes them glow under bloom. Uses the default material when empty.")]
        Material _markerMaterial = null;

        [SerializeField, Range(0.004f, 0.05f), Tooltip("Marker diameter (m).")]
        float _markerSize = 0.014f;

        [SerializeField, ColorUsage(false, true), Tooltip("Color of marks still to carve.")]
        Color _pendingColor = new Color(0.22f, 0.18f, 0.16f);

        [SerializeField, ColorUsage(false, true), Tooltip("Color of the mark to strike next. Pulses.")]
        Color _activeColor = new Color(1.6f, 0.7f, 0.18f);

        [SerializeField, ColorUsage(false, true), Tooltip("Color of carved marks.")]
        Color _struckColor = new Color(2.4f, 1.5f, 0.55f);

        [SerializeField, Tooltip("Hide the marker of a carved mark, for relics whose carved grooves show the rune.")]
        bool _hideStruckMarkers = false;

        #endregion

        #region Runtime state

        Rigidbody _body;
        Vector3 _normal;
        Vector3 _up;
        Vector3 _right;
        Vector3 _faceCenter;
        float _width;
        float _height;
        int _struck;
        bool _accepting = true;
        bool _gated;
        Transform[] _markers;
        Renderer[] _markerRenderers;
        MaterialPropertyBlock _block;

        readonly ForgeSignal<int> _markStruck = new ForgeSignal<int>("RelicCarvingSurface.OnMarkStruck");
        readonly ForgeSignal _patternCompleted = new ForgeSignal("RelicCarvingSurface.OnPatternCompleted");

        Action<ChiselStrikeEvent> _onChiselStrike;
        Action<GamePhaseChangedEvent> _onPhaseChanged;

        #endregion

        #region Public API

        /// <summary>Raised with the index of each mark when it is carved.</summary>
        public event Action<int> OnMarkStruck
        {
            add => _markStruck.Add(value);
            remove => _markStruck.Remove(value);
        }

        /// <summary>Raised when the last mark is carved.</summary>
        public event Action OnPatternCompleted
        {
            add => _patternCompleted.Add(value);
            remove => _patternCompleted.Remove(value);
        }

        /// <summary>Rigidbody whose strikes this surface accepts. Null when the relic has none.</summary>
        public Rigidbody Body => _body;

        /// <summary>Local direction of the carved face.</summary>
        public Vector3 FaceNormal => _normal;

        /// <summary>Number of marks in the rune.</summary>
        public int MarkCount => _pattern != null ? _pattern.Length : 0;

        /// <summary>Number of marks carved so far.</summary>
        public int StruckCount => _struck;

        /// <summary>Index of the mark to strike next, or -1 when the rune is complete.</summary>
        public int ActiveIndex => _struck < MarkCount ? _struck : -1;

        /// <summary>True once every mark is carved.</summary>
        public bool IsComplete => MarkCount > 0 && _struck >= MarkCount;

        /// <summary>True while strikes can carve marks.</summary>
        public bool IsAcceptingStrikes => _accepting && !_gated && !IsComplete && MarkCount > 0;

        /// <summary>
        /// True while the face can be aimed at: carving, or gated while its rune is still being chosen (see
        /// <see cref="SetGate"/>), when strikes ring the relic without carving.
        /// </summary>
        public bool IsAimable => _accepting && !IsComplete && MarkCount > 0;

        /// <summary>True while carving is held closed by <see cref="SetGate"/>.</summary>
        public bool IsGated => _gated;

        /// <summary>
        /// Holds carving closed, or opens it again. While gated, marks are hidden and strikes carve nothing; the face can
        /// still be aimed at (<see cref="IsAimable"/>).
        /// </summary>
        /// <param name="gated">True to close carving.</param>
        public void SetGate(bool gated)
        {
            if (_gated != gated)
            {
                _gated = gated;
                RefreshMarkers();
            }
        }

        /// <summary>
        /// Replaces the rune with new marks and grooves and starts it from its first mark. Marker visuals are rebuilt
        /// when the mark count changes.
        /// </summary>
        /// <param name="marks">Marks in carving order, in face coordinates.</param>
        /// <param name="strokes">Grooves as pairs of mark indices.</param>
        public void SetPattern(Vector2[] marks, Vector2Int[] strokes)
        {
            int previousCount = MarkCount;
            _pattern = marks != null ? (Vector2[])marks.Clone() : Array.Empty<Vector2>();
            _strokes = strokes != null ? (Vector2Int[])strokes.Clone() : Array.Empty<Vector2Int>();
            _struck = 0;
            if (_markers != null && previousCount != MarkCount)
            {
                for (int i = 0; i < _markers.Length; i++)
                {
                    if (_markers[i] != null)
                    {
                        Destroy(_markers[i].gameObject);
                    }
                }

                _markers = null;
                if (_showMarkers)
                {
                    CreateMarkers();
                }
            }

            RefreshMarkers();
        }

        /// <summary>Number of grooves in the rune.</summary>
        public int StrokeCount => _strokes != null ? _strokes.Length : 0;

        /// <summary>Mark indices joined by groove <paramref name="index"/>.</summary>
        /// <param name="index">Groove index.</param>
        /// <returns>The two mark indices.</returns>
        public Vector2Int GetStroke(int index)
        {
            return _strokes[index];
        }

        /// <summary>Share of the face the rune and cursor may use.</summary>
        public float UsableArea => _usableArea;

        /// <summary>
        /// Maps face coordinates (-0.5 to 0.5 over the usable area) to face UV (0 to 1 over the whole face), the space
        /// of the relic's carve map and plate modes.
        /// </summary>
        /// <param name="faceCoordinates">Face coordinates.</param>
        /// <returns>Face UV.</returns>
        public Vector2 FaceToUV(Vector2 faceCoordinates)
        {
            return new Vector2(0.5f + faceCoordinates.x * _usableArea, 0.5f + faceCoordinates.y * _usableArea);
        }

        /// <summary>Projects a world point onto the face and returns its face UV, 0 to 1 over the whole face.</summary>
        /// <param name="worldPoint">World-space point.</param>
        /// <returns>Face UV, unclamped.</returns>
        public Vector2 WorldToUV(Vector3 worldPoint)
        {
            EnsureGeometry();
            Vector3 local = transform.InverseTransformPoint(worldPoint) - _faceCenter;
            return new Vector2(
                0.5f + Vector3.Dot(local, _right) / Mathf.Max(1e-6f, _width),
                0.5f + Vector3.Dot(local, _up) / Mathf.Max(1e-6f, _height));
        }

        /// <summary>The face in the relic's local space: its center, unit axes and full size.</summary>
        /// <param name="center">Local center of the face.</param>
        /// <param name="right">Local unit direction of increasing U.</param>
        /// <param name="up">Local unit direction of increasing V.</param>
        /// <param name="normal">Local unit outward normal.</param>
        /// <param name="size">Local width and height of the whole face.</param>
        public void GetLocalFace(out Vector3 center, out Vector3 right, out Vector3 up, out Vector3 normal, out Vector2 size)
        {
            EnsureGeometry();
            center = _faceCenter;
            right = _right;
            up = _up;
            normal = _normal;
            size = new Vector2(_width, _height);
        }

        /// <summary>Distance (m) from a mark at which aim quality reaches zero.</summary>
        public float StrikeTolerance => _strikeTolerance;

        /// <summary>Precision a strike needs to complete its mark.</summary>
        public float MinStrikePrecision => _minStrikePrecision;

        /// <summary>World-space size (m) of the usable face area: width and height.</summary>
        public Vector2 UsableFaceSize
        {
            get
            {
                EnsureGeometry();
                return new Vector2(
                    transform.TransformVector(_right * (_width * _usableArea)).magnitude,
                    transform.TransformVector(_up * (_height * _usableArea)).magnitude);
            }
        }

        /// <summary>Maps face coordinates (-0.5 to 0.5 on both axes) to a world point on the face and its normal.</summary>
        /// <param name="faceCoordinates">Face coordinates.</param>
        /// <param name="worldPoint">World-space point on the face.</param>
        /// <param name="worldNormal">World-space unit normal of the face.</param>
        public void GetFacePoint(Vector2 faceCoordinates, out Vector3 worldPoint, out Vector3 worldNormal)
        {
            EnsureGeometry();
            Vector3 local = _faceCenter +
                            _right * (faceCoordinates.x * _width * _usableArea) +
                            _up * (faceCoordinates.y * _height * _usableArea);
            worldPoint = transform.TransformPoint(local);
            worldNormal = transform.TransformDirection(_normal).normalized;
        }

        /// <summary>Face coordinates of the mark at <paramref name="index"/>.</summary>
        /// <param name="index">Mark index.</param>
        /// <returns>The mark's face coordinates.</returns>
        public Vector2 GetMarkCoordinates(int index)
        {
            return _pattern[index];
        }

        /// <summary>World position and normal of the mark to strike next.</summary>
        /// <param name="worldPoint">World-space mark position.</param>
        /// <param name="worldNormal">World-space face normal.</param>
        /// <returns>False when the rune is complete or empty.</returns>
        public bool TryGetActiveMark(out Vector3 worldPoint, out Vector3 worldNormal)
        {
            int index = ActiveIndex;
            if (index < 0)
            {
                worldPoint = default;
                worldNormal = default;
                return false;
            }

            GetFacePoint(_pattern[index], out worldPoint, out worldNormal);
            return true;
        }

        /// <summary>
        /// Applies a strike of the given precision to the active mark. Normally driven by
        /// <see cref="ForgeEvents.ChiselStrike"/>; public for tools and tests.
        /// </summary>
        /// <param name="precision">Strike precision in [0, 1].</param>
        /// <returns>True when the strike carved the active mark.</returns>
        public bool RegisterStrike(float precision)
        {
            if (!IsAcceptingStrikes || !(precision >= _minStrikePrecision))
            {
                return false;
            }

            int index = _struck;
            _struck++;
            RefreshMarkers();
            _markStruck.Raise(index);

            if (IsComplete)
            {
                ForgeLog.Info(ForgeLogChannel.Crafting, LogSource, "RUNE_COMPLETE",
                    "All " + MarkCount + " marks carved on '" + name + "'.", this);
                _patternCompleted.Raise();
            }

            return true;
        }

        /// <summary>Clears every carved mark and makes the first mark active again.</summary>
        public void ResetPattern()
        {
            _struck = 0;
            RefreshMarkers();
        }

        #endregion

        #region Unity lifecycle

        void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _block = new MaterialPropertyBlock();
            _onChiselStrike = HandleChiselStrike;
            _onPhaseChanged = HandlePhaseChanged;
            if (_pattern == null)
            {
                _pattern = Array.Empty<Vector2>();
            }

            if (_strokes == null)
            {
                _strokes = Array.Empty<Vector2Int>();
            }

            EnsureGeometry();
        }

        void OnEnable()
        {
            ForgeEvents.ChiselStrike += _onChiselStrike;
            ForgeEvents.GamePhaseChanged += _onPhaseChanged;
        }

        void OnDisable()
        {
            ForgeEvents.ChiselStrike -= _onChiselStrike;
            ForgeEvents.GamePhaseChanged -= _onPhaseChanged;
        }

        void Start()
        {
            if (_showMarkers)
            {
                CreateMarkers();
            }
        }

        void Update()
        {
            int active = ActiveIndex;
            if (_markers == null || _gated || active < 0 || _markers[active] == null)
            {
                return;
            }

            // A gentle pulse draws the eye to the next mark.
            float pulse = 1f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f);
            SetMarkerScale(active, _markerSize * (_accepting ? pulse : 1f));
        }

        void OnValidate()
        {
            _usableArea = Mathf.Clamp(_usableArea, 0.3f, 1f);
            _normal = Vector3.zero;
        }

        #endregion

        #region Events

        void HandleChiselStrike(ChiselStrikeEvent payload)
        {
            if (ReferenceEquals(payload.Relic, _body))
            {
                RegisterStrike(payload.Precision);
            }
        }

        void HandlePhaseChanged(GamePhaseChangedEvent payload)
        {
            if (payload.Current == ForgePhase.FirstRelicForging)
            {
                ResetPattern();
                _accepting = true;
            }
            else
            {
                _accepting = !_onlyDuringForging && payload.Current != ForgePhase.Completion;
            }

            RefreshMarkers();
        }

        #endregion

        #region Geometry and visuals

        void EnsureGeometry()
        {
            if (_normal != Vector3.zero)
            {
                return;
            }

            _normal = _faceNormal.sqrMagnitude > 1e-6f ? _faceNormal.normalized : Vector3.back;
            Vector3 up = _faceUp - _normal * Vector3.Dot(_faceUp, _normal);
            if (up.sqrMagnitude < 1e-6f)
            {
                up = Mathf.Abs(_normal.y) < 0.9f ? Vector3.up : Vector3.forward;
                up -= _normal * Vector3.Dot(up, _normal);
            }

            _up = up.normalized;
            _right = Vector3.Cross(_normal, _up);

            Bounds bounds = LocalBounds();
            Vector3 half = bounds.extents;
            _faceCenter = bounds.center + _normal * Support(half, _normal);
            _width = 2f * Support(half, _right);
            _height = 2f * Support(half, _up);
        }

        Bounds LocalBounds()
        {
            if (TryGetComponent(out BoxCollider box))
            {
                return new Bounds(box.center, box.size);
            }

            if (TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
            {
                return filter.sharedMesh.bounds;
            }

            return new Bounds(Vector3.zero, Vector3.one);
        }

        static float Support(Vector3 halfExtents, Vector3 direction)
        {
            // Half-width of an axis-aligned box along a unit direction.
            return Mathf.Abs(halfExtents.x * direction.x) + Mathf.Abs(halfExtents.y * direction.y) +
                   Mathf.Abs(halfExtents.z * direction.z);
        }

        void CreateMarkers()
        {
            int count = MarkCount;
            if (count == 0)
            {
                return;
            }

            // Borrow the built-in sphere mesh and default material without adding colliders to the relic.
            GameObject template = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Mesh mesh = template.GetComponent<MeshFilter>().sharedMesh;
            Material material = _markerMaterial != null ? _markerMaterial : template.GetComponent<Renderer>().sharedMaterial;
            Destroy(template);

            _markers = new Transform[count];
            _markerRenderers = new Renderer[count];
            for (int i = 0; i < count; i++)
            {
                GameObject marker = new GameObject("Rune Mark " + (i + 1));
                marker.transform.SetParent(transform, false);
                marker.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = marker.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                _markers[i] = marker.transform;
                _markerRenderers[i] = renderer;
            }

            RefreshMarkers();
        }

        void RefreshMarkers()
        {
            if (_markers == null)
            {
                return;
            }

            for (int i = 0; i < _markers.Length; i++)
            {
                if (_markers[i] == null)
                {
                    continue;
                }

                GetFacePoint(_pattern[i], out Vector3 point, out Vector3 normal);
                _markers[i].position = point + normal * MarkerLift;

                bool struck = i < _struck;
                bool active = i == ActiveIndex;
                Color color = struck ? _struckColor : active && _accepting ? _activeColor : _pendingColor;
                _block.Clear();
                _block.SetColor(BaseColorId, color);
                _block.SetColor(ColorId, color);
                _markerRenderers[i].SetPropertyBlock(_block);
                SetMarkerScale(i, _gated || (struck && _hideStruckMarkers) ? 0f : _markerSize * (struck ? 1.35f : 1f));
            }
        }

        void SetMarkerScale(int index, float worldDiameter)
        {
            Transform marker = _markers[index];
            Vector3 parentScale = transform.lossyScale;
            marker.localScale = new Vector3(
                worldDiameter / Mathf.Max(1e-4f, Mathf.Abs(parentScale.x)),
                worldDiameter / Mathf.Max(1e-4f, Mathf.Abs(parentScale.y)),
                worldDiameter / Mathf.Max(1e-4f, Mathf.Abs(parentScale.z)));
        }

        /// <summary>
        /// The default rune, a forge sigil: an upward triangle traced from its apex, then an inner anvil stroke with a
        /// crossbar. Twelve marks in carving order.
        /// </summary>
        /// <returns>A new array of face coordinates.</returns>
        public static Vector2[] CreateDefaultPattern()
        {
            return new[]
            {
                new Vector2(0f, 0.40f),
                new Vector2(-0.17f, 0.10f),
                new Vector2(-0.34f, -0.20f),
                new Vector2(-0.11f, -0.20f),
                new Vector2(0.11f, -0.20f),
                new Vector2(0.34f, -0.20f),
                new Vector2(0.17f, 0.10f),
                new Vector2(0f, 0.16f),
                new Vector2(0f, 0.02f),
                new Vector2(-0.09f, -0.06f),
                new Vector2(0.09f, -0.06f),
                new Vector2(0f, -0.10f),
            };
        }

        /// <summary>
        /// Grooves of the default rune: the triangle's three sides, then the anvil's post and crossbar.
        /// </summary>
        /// <returns>A new array of mark index pairs.</returns>
        public static Vector2Int[] CreateDefaultStrokes()
        {
            return new[]
            {
                new Vector2Int(0, 1), new Vector2Int(1, 2), new Vector2Int(2, 3), new Vector2Int(3, 4),
                new Vector2Int(4, 5), new Vector2Int(5, 6), new Vector2Int(6, 0),
                new Vector2Int(7, 8), new Vector2Int(8, 11), new Vector2Int(9, 10),
            };
        }

        #endregion
    }
}
