using System;
using KineticForge.Core;
using UnityEngine;

namespace KineticForge.Crafting
{
    /// <summary>
    /// Renders the relic's forging state on the Forge Metal shader: carves real grooves into the tablet mesh, keeps the
    /// carve map (groove depth, rune glow, cracks), and feeds heat, pulse and the ringing plate modes to the material
    /// every frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On Awake the relic gets its own <see cref="RelicForgeMesh"/> tablet, sized to its <see cref="BoxCollider"/>, so
    /// carving never touches a shared asset. Each carved mark is dented into the front grid with a rounded chisel profile
    /// and written to the carve map; once both ends of a rune groove are carved, the groove between them is cut too.
    /// Groove depth and glow follow the strike's quality, so a well-forged rune is visibly deeper and brighter. Cracks
    /// branch from the strike point as dark seams that glow while the metal is hot.
    /// </para>
    /// <para>
    /// Geometry and texture change only on strikes. The per-frame path sets a few material properties through a
    /// <see cref="MaterialPropertyBlock"/> and does not allocate.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(RelicCarvingSurface))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Crafting/Relic Forge Renderer")]
    public sealed class RelicForgeRenderer : MonoBehaviour
    {
        static readonly int CarveMapId = Shader.PropertyToID("_CarveMap");
        static readonly int HeatId = Shader.PropertyToID("_Heat");
        static readonly int ModeAmpId = Shader.PropertyToID("_ModeAmp");
        static readonly int ModeMId = Shader.PropertyToID("_ModeM");
        static readonly int ModeNId = Shader.PropertyToID("_ModeN");
        static readonly int ModeSignId = Shader.PropertyToID("_ModeSign");
        static readonly int PulseId = Shader.PropertyToID("_Pulse");
        static readonly int TargetAmpId = Shader.PropertyToID("_TargetAmp");
        static readonly int TargetGlowId = Shader.PropertyToID("_TargetGlow");
        static readonly int FillId = Shader.PropertyToID("_Fill");
        static readonly int FillHeightId = Shader.PropertyToID("_FillHeight");

        [SerializeField, Range(16, 200), Tooltip("Front face grid resolution, in quads per side.")]
        int _gridResolution = RelicForgeMesh.DefaultResolution;

        [SerializeField, Range(32, 512), Tooltip("Carve map size, in texels per side.")]
        int _carveMapSize = 128;

        [SerializeField, Range(0.002f, 0.03f), Tooltip("Radius (m) of a carved mark.")]
        float _markRadius = 0.009f;

        [SerializeField, Range(0.001f, 0.02f), Tooltip("Half-width (m) of a rune groove.")]
        float _grooveRadius = 0.0045f;

        [SerializeField, Range(0.0005f, 0.01f), Tooltip("Depth (m) of a perfect carve.")]
        float _maxDepth = 0.0035f;

        [SerializeField, Range(0.01f, 0.2f), Tooltip("Length (m) of a crack.")]
        float _crackLength = 0.07f;

        [SerializeField, Range(0f, 1f), Tooltip("Visual strength of the plate modes on the face.")]
        float _chladniGain = 1f;

        RelicCarvingSurface _surface;
        RelicResonance _resonance;
        RelicSigil _sigil;
        RelicVoicing _voicing;
        Action<Vector2> _onCut;
        Rigidbody _body;
        MeshRenderer _renderer;
        float _fill = 1f;
        MaterialPropertyBlock _block;
        Mesh _mesh;
        Vector3[] _baseVertices;
        Vector3[] _vertices;
        float[] _vertexDepth;
        Texture2D _carveMap;
        Color32[] _pixels;
        Vector2 _faceSize = Vector2.one;
        Vector3 _inward = Vector3.forward;
        int _resolution;
        bool _dirty;
        Action<RelicResonanceEvent> _onResonated;
        Action<GamePhaseChangedEvent> _onPhaseChanged;

        /// <summary>
        /// How much of the relic exists, from 0 (an empty mold) to 1 (whole): the molten surface rises through it as it
        /// is poured.
        /// </summary>
        public float Fill
        {
            get => _fill;
            set => _fill = Mathf.Clamp01(value);
        }

        /// <summary>The carve map: R groove depth, G rune glow, B cracks.</summary>
        public Texture2D CarveMap => _carveMap;

        /// <summary>Carved depth (m) of the front face at a face UV, from the nearest grid vertex.</summary>
        /// <param name="uv">Face UV, both axes in [0, 1].</param>
        /// <returns>The depth, 0 where nothing is carved.</returns>
        public float DepthAt(Vector2 uv)
        {
            if (_vertexDepth == null)
            {
                return 0f;
            }

            int i = Mathf.Clamp(Mathf.RoundToInt(uv.x * _resolution), 0, _resolution);
            int j = Mathf.Clamp(Mathf.RoundToInt(uv.y * _resolution), 0, _resolution);
            return _vertexDepth[j * (_resolution + 1) + i];
        }

        /// <summary>Restores the uncarved tablet and clears the carve map.</summary>
        public void ResetCarving()
        {
            if (_vertices == null)
            {
                return;
            }

            Array.Copy(_baseVertices, _vertices, _vertices.Length);
            Array.Clear(_vertexDepth, 0, _vertexDepth.Length);
            Array.Clear(_pixels, 0, _pixels.Length);
            _dirty = true;
            Upload();
        }

        void Awake()
        {
            _surface = GetComponent<RelicCarvingSurface>();
            _resonance = GetComponent<RelicResonance>();
            _sigil = GetComponent<RelicSigil>();
            _body = GetComponent<Rigidbody>();
            _onCut = HandleCut;
            _renderer = GetComponent<MeshRenderer>();
            _block = new MaterialPropertyBlock();
            _onResonated = HandleResonated;
            _onPhaseChanged = HandlePhaseChanged;
            BuildMesh();
            BuildCarveMap();

            _renderer.GetPropertyBlock(_block);
            _block.SetTexture(CarveMapId, _carveMap);
            _block.SetVector(ModeMId, ResonanceMath.ModeM);
            _block.SetVector(ModeNId, ResonanceMath.ModeN);
            _block.SetVector(ModeSignId, ResonanceMath.ModeSign);
            _block.SetFloat(FillHeightId, _faceSize.y);
            _renderer.SetPropertyBlock(_block);
        }

        void OnEnable()
        {
            ForgeEvents.RelicResonated += _onResonated;
            ForgeEvents.GamePhaseChanged += _onPhaseChanged;
            if (_voicing != null)
            {
                _voicing.OnCut += _onCut;
            }
        }

        void OnDisable()
        {
            ForgeEvents.RelicResonated -= _onResonated;
            ForgeEvents.GamePhaseChanged -= _onPhaseChanged;
            if (_voicing != null)
            {
                _voicing.OnCut -= _onCut;
            }
        }

        void Start()
        {
            // Voicing may have been added after this component woke, so it is found here and followed from now on.
            if (_voicing == null)
            {
                _voicing = GetComponent<RelicVoicing>();
                if (_voicing != null)
                {
                    _voicing.OnCut += _onCut;
                }
            }
        }

        /// <summary>A chisel cut from voicing: a fresh, glowing notch where the metal was removed.</summary>
        void HandleCut(Vector2 uv)
        {
            if (_vertices == null)
            {
                return;
            }

            Stamp(uv, uv, _markRadius * 1.1f, _maxDepth * 0.85f, 1f);
        }

        void OnDestroy()
        {
            if (_mesh != null)
            {
                Destroy(_mesh);
            }

            if (_carveMap != null)
            {
                Destroy(_carveMap);
            }
        }

        void LateUpdate()
        {
            if (_dirty)
            {
                Upload();
            }

            if (_resonance == null)
            {
                return;
            }

            // Pulse brightness peaks exactly at the ideal strike moment, so the relic itself shows the beat.
            float pulse = _resonance.IsPulsing ? 0.5f + 0.5f * _resonance.TimingNow : 0.5f;
            _block.SetFloat(HeatId, _resonance.Heat);
            _block.SetVector(ModeAmpId, _resonance.PlateAmplitudes * _chladniGain);
            _block.SetFloat(PulseId, pulse);
            _block.SetFloat(FillId, _fill);
            bool awakening = _sigil != null && _sigil.Stage == SigilStage.Awakening;
            _block.SetVector(TargetAmpId, awakening ? _sigil.Target : Vector4.zero);
            _block.SetFloat(TargetGlowId, awakening ? 0.55f + 0.25f * Mathf.Sin(Time.time * 2.4f) : 0f);
            _renderer.SetPropertyBlock(_block);
        }

        #region Carving

        void HandleResonated(RelicResonanceEvent payload)
        {
            if (!ReferenceEquals(payload.Relic, _body) || _vertices == null)
            {
                return;
            }

            if (payload.CarvedMark >= 0 && payload.CarvedMark < _surface.MarkCount)
            {
                float quality = Mathf.Clamp01(payload.Quality);
                float depth = _maxDepth * Mathf.Lerp(0.35f, 1f, quality);
                Vector2 mark = _surface.FaceToUV(_surface.GetMarkCoordinates(payload.CarvedMark));
                Stamp(mark, mark, _markRadius, depth, quality);

                // Cut every groove that this mark completes: both ends carved, this one last.
                for (int i = 0; i < _surface.StrokeCount; i++)
                {
                    Vector2Int stroke = _surface.GetStroke(i);
                    if (Mathf.Max(stroke.x, stroke.y) != payload.CarvedMark || stroke.x < 0 || stroke.y < 0 ||
                        stroke.x >= _surface.MarkCount || stroke.y >= _surface.MarkCount)
                    {
                        continue;
                    }

                    Vector2 a = _surface.FaceToUV(_surface.GetMarkCoordinates(stroke.x));
                    Vector2 b = _surface.FaceToUV(_surface.GetMarkCoordinates(stroke.y));
                    Stamp(a, b, _grooveRadius, depth * 0.8f, quality);
                }
            }

            if (payload.Cracked)
            {
                Crack(payload.FaceUV);
            }
        }

        void HandlePhaseChanged(GamePhaseChangedEvent payload)
        {
            if (payload.Current == ForgePhase.FirstRelicForging)
            {
                ResetCarving();
            }
        }

        /// <summary>Cuts a rounded groove along segment a-b (a disc when a equals b) into mesh and carve map.</summary>
        void Stamp(Vector2 a, Vector2 b, float radius, float depth, float glow)
        {
            // Work in meters on the face so grooves stay round on non-square faces.
            Vector2 scale = _faceSize;
            Vector2 am = Vector2.Scale(a, scale);
            Vector2 bm = Vector2.Scale(b, scale);
            Vector2 min = Vector2.Min(am, bm) - new Vector2(radius, radius);
            Vector2 max = Vector2.Max(am, bm) + new Vector2(radius, radius);

            // Mesh grid.
            int n = _resolution;
            int i0 = Mathf.Clamp(Mathf.FloorToInt(min.x / scale.x * n), 0, n);
            int i1 = Mathf.Clamp(Mathf.CeilToInt(max.x / scale.x * n), 0, n);
            int j0 = Mathf.Clamp(Mathf.FloorToInt(min.y / scale.y * n), 0, n);
            int j1 = Mathf.Clamp(Mathf.CeilToInt(max.y / scale.y * n), 0, n);
            for (int j = j0; j <= j1; j++)
            {
                for (int i = i0; i <= i1; i++)
                {
                    Vector2 p = new Vector2((float)i / n * scale.x, (float)j / n * scale.y);
                    float profile = Profile(p, am, bm, radius);
                    if (profile <= 0f)
                    {
                        continue;
                    }

                    int index = j * (n + 1) + i;
                    float carved = depth * profile;
                    if (carved > _vertexDepth[index])
                    {
                        _vertexDepth[index] = carved;
                        _vertices[index] = _baseVertices[index] + _inward * carved;
                    }
                }
            }

            // Carve map texels.
            int size = _carveMapSize;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(min.x / scale.x * size), 0, size - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(max.x / scale.x * size), 0, size - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(min.y / scale.y * size), 0, size - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(max.y / scale.y * size), 0, size - 1);
            byte grooveValue = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(depth / Mathf.Max(1e-6f, _maxDepth)));
            byte glowValue = (byte)Mathf.RoundToInt(255f * Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(glow)));
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = new Vector2((x + 0.5f) / size * scale.x, (y + 0.5f) / size * scale.y);
                    float profile = Profile(p, am, bm, radius * 1.15f);
                    if (profile <= 0f)
                    {
                        continue;
                    }

                    int index = y * size + x;
                    Color32 texel = _pixels[index];
                    texel.r = (byte)Mathf.Max(texel.r, Mathf.RoundToInt(grooveValue * profile));
                    texel.g = (byte)Mathf.Max(texel.g, Mathf.RoundToInt(glowValue * Mathf.Sqrt(profile)));
                    texel.a = 255;
                    _pixels[index] = texel;
                }
            }

            _dirty = true;
        }

        /// <summary>Rounded chisel cross-section: 1 on the groove's center line, 0 at its edge.</summary>
        static float Profile(Vector2 point, Vector2 a, Vector2 b, float radius)
        {
            float distance = Vector2.Distance(point, ForgeMath.ClosestPointOnSegment(point, a, b));
            float x = 1f - (distance * distance) / (radius * radius);
            return x > 0f ? x * Mathf.Sqrt(x) : 0f;
        }

        void Crack(Vector2 start)
        {
            // A jagged random walk with one branch, drawn into the crack channel.
            Vector2 aspect = new Vector2(1f / _faceSize.x, 1f / _faceSize.y);
            float angle = UnityEngine.Random.Range(0f, 2f * Mathf.PI);
            DrawCrack(start, angle, _crackLength, aspect, 255);
            DrawCrack(start, angle + UnityEngine.Random.Range(1.6f, 2.6f), _crackLength * 0.55f, aspect, 200);
            _dirty = true;
        }

        void DrawCrack(Vector2 start, float angle, float length, Vector2 aspect, byte strength)
        {
            const int Segments = 9;
            Vector2 point = start;
            float step = length / Segments;
            for (int s = 0; s < Segments; s++)
            {
                angle += UnityEngine.Random.Range(-0.7f, 0.7f);
                Vector2 next = point + Vector2.Scale(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * step, aspect);
                DrawLine(point, next, (byte)(strength * (1f - 0.5f * s / Segments)));
                point = next;
            }
        }

        void DrawLine(Vector2 a, Vector2 b, byte strength)
        {
            int size = _carveMapSize;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) * size * 2f));
            for (int k = 0; k <= steps; k++)
            {
                Vector2 p = Vector2.Lerp(a, b, (float)k / steps);
                int x = Mathf.FloorToInt(p.x * size);
                int y = Mathf.FloorToInt(p.y * size);
                if (x < 0 || y < 0 || x >= size || y >= size)
                {
                    continue;
                }

                int index = y * size + x;
                Color32 texel = _pixels[index];
                texel.b = (byte)Mathf.Max(texel.b, strength);
                texel.a = 255;
                _pixels[index] = texel;
            }
        }

        void Upload()
        {
            _dirty = false;
            _carveMap.SetPixels32(_pixels);
            _carveMap.Apply(false, false);
            _mesh.SetVertices(_vertices);
            _mesh.RecalculateNormals();
        }

        #endregion

        #region Setup

        void BuildMesh()
        {
            Vector3 size = TryGetComponent(out BoxCollider box) ? box.size : new Vector3(0.3f, 0.3f, 0.09f);
            _resolution = Mathf.Clamp(_gridResolution, 16, 200);
            _mesh = RelicForgeMesh.Build(size, _resolution);
            _mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = _mesh;

            _baseVertices = _mesh.vertices;
            _vertices = (Vector3[])_baseVertices.Clone();
            _vertexDepth = new float[RelicForgeMesh.FrontVertexCount(_resolution)];
            _faceSize = new Vector2(size.x, size.y);
            _inward = Vector3.forward;
        }

        void BuildCarveMap()
        {
            int size = Mathf.Clamp(_carveMapSize, 32, 512);
            _carveMapSize = size;
            _carveMap = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "Relic Carve Map",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _pixels = new Color32[size * size];
            _carveMap.SetPixels32(_pixels);
            _carveMap.Apply(false, false);
        }

        #endregion
    }
}
