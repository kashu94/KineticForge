using KineticForge.Crafting;
using UnityEngine;

namespace KineticForge.Feedback
{
    /// <summary>
    /// Shows the relic's pulse at the mark to strike: a ring that closes onto the mark as the ideal strike moment
    /// approaches and glows gold inside the rhythm window. The player strikes as it closes.
    /// </summary>
    /// <remarks>
    /// The ring is a quad with an additive ring material, parented to the relic and flat on its face. While the pulse is at
    /// rest it breathes slowly at a middle size, since any first strike starts the rhythm. It hides whenever the surface
    /// is not accepting strikes. The per-frame path does not allocate.
    /// </remarks>
    [RequireComponent(typeof(RelicCarvingSurface), typeof(RelicResonance))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Feedback/Resonance Ring")]
    public sealed class ResonanceRing : MonoBehaviour
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField, Tooltip("Additive ring material (Kinetic Forge/Forge Glow with a ring thickness).")]
        Material _material = null;

        [SerializeField, Range(0.01f, 0.1f), Tooltip("Ring diameter (m) at the ideal strike moment.")]
        float _closedDiameter = 0.03f;

        [SerializeField, Range(0.04f, 0.4f), Tooltip("Ring diameter (m) one full pulse before the ideal moment.")]
        float _openDiameter = 0.16f;

        [SerializeField, Range(0.3f, 1f), Tooltip("Timing at or above which the ring shows the rhythm window.")]
        float _rhythmThreshold = 0.6f;

        [SerializeField, ColorUsage(false, true), Tooltip("Ring color outside the rhythm window.")]
        Color _waitColor = new Color(0.35f, 0.65f, 1f);

        [SerializeField, ColorUsage(false, true), Tooltip("Ring color inside the rhythm window.")]
        Color _strikeColor = new Color(1.4f, 0.95f, 0.35f);

        [SerializeField, ColorUsage(false, true), Tooltip("Ring color with the chisel in hand, outside the rhythm window.")]
        Color _chiselColor = new Color(1.3f, 0.55f, 0.18f);

        RelicCarvingSurface _surface;
        RelicResonance _resonance;
        RelicSigil _sigil;
        RelicVoicing _voicing;
        CarvingCursor _cursor;
        Transform _ring;
        Renderer _renderer;
        MaterialPropertyBlock _block;

        void Awake()
        {
            _surface = GetComponent<RelicCarvingSurface>();
            _resonance = GetComponent<RelicResonance>();
            _sigil = GetComponent<RelicSigil>();
            _voicing = GetComponent<RelicVoicing>();
            _cursor = GetComponent<CarvingCursor>();
            _block = new MaterialPropertyBlock();
        }

        void Start()
        {
            // Borrow the built-in quad mesh without adding a collider to the relic.
            GameObject template = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Mesh mesh = template.GetComponent<MeshFilter>().sharedMesh;
            Destroy(template);

            GameObject ring = new GameObject("Resonance Ring");
            ring.transform.SetParent(transform, false);
            ring.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = ring.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _ring = ring.transform;
            _renderer = renderer;
        }

        void LateUpdate()
        {
            if (_ring == null)
            {
                return;
            }

            if (_voicing == null)
            {
                _voicing = GetComponent<RelicVoicing>();
            }

            if (_cursor == null)
            {
                _cursor = GetComponent<CarvingCursor>();
            }

            if (_voicing != null && _voicing.IsActive)
            {
                UpdateVoicing();
                return;
            }

            // While a sigil awakens, the ring is the hint: it appears over the spot to strike once the search drags on.
            float hint = _sigil != null && _sigil.IsAwakening ? _sigil.Hint : 0f;
            bool visible = hint > 0.01f || (_surface.IsAcceptingStrikes && _surface.TryGetActiveMark(out _, out _));
            if (_renderer.enabled != visible)
            {
                _renderer.enabled = visible;
            }

            if (!visible)
            {
                return;
            }

            Vector3 mark;
            Vector3 normal;
            if (hint > 0.01f)
            {
                _surface.GetFacePoint(UvToFace(_sigil.Sigil.Seed), out mark, out normal);
                float breathe = 0.5f + 0.5f * Mathf.Sin(Time.time * 3f);
                PlaceRing(mark, normal, Mathf.Lerp(_openDiameter, _closedDiameter * 2f, breathe), _strikeColor * (0.25f + 0.75f * hint));
                return;
            }

            if (!_surface.TryGetActiveMark(out mark, out normal))
            {
                return;
            }

            float diameter;
            Color color;
            if (_resonance.IsPulsing)
            {
                float remaining = _resonance.CycleToIdealStrike;
                diameter = Mathf.Lerp(_closedDiameter, _openDiameter, remaining * remaining);
                float window = Mathf.InverseLerp(_rhythmThreshold, 1f, _resonance.TimingNow);
                color = Color.Lerp(_waitColor, _strikeColor, window > 0f ? 0.6f + 0.4f * window : 0f);
            }
            else
            {
                float breathe = 0.5f + 0.5f * Mathf.Sin(Time.time * 2f);
                diameter = Mathf.Lerp(_closedDiameter, _openDiameter, 0.35f + 0.15f * breathe);
                color = _waitColor * 0.8f;
            }

            PlaceRing(mark, normal, diameter, color);
        }

        /// <summary>
        /// While voicing, the ring is a reticle at the strike point: blue for the hammer, ember for the chisel, closing
        /// with the relic's pulse and flaring gold in the rhythm window. After a long search it moves to a good place to
        /// cut instead.
        /// </summary>
        void UpdateVoicing()
        {
            bool hint = _voicing.HintActive;
            bool visible = hint || _cursor != null;
            if (_renderer.enabled != visible)
            {
                _renderer.enabled = visible;
            }

            if (!visible)
            {
                return;
            }

            Vector3 point;
            Vector3 normal;
            if (hint)
            {
                _surface.GetFacePoint(UvToFace(_voicing.HintUV), out point, out normal);
                float breathe = 0.5f + 0.5f * Mathf.Sin(Time.time * 3f);
                PlaceRing(point, normal, Mathf.Lerp(_openDiameter, _closedDiameter * 2f, breathe), _strikeColor * 0.8f);
                return;
            }

            _cursor.GetStrikePoint(out point, out normal);
            Color baseColor = _voicing.Tool == VoicingTool.Chisel ? _chiselColor : _waitColor;
            float diameter;
            Color color;
            if (_resonance.IsPulsing)
            {
                float remaining = _resonance.CycleToIdealStrike;
                diameter = Mathf.Lerp(_closedDiameter, _openDiameter, remaining * remaining);
                float window = Mathf.InverseLerp(_rhythmThreshold, 1f, _resonance.TimingNow);
                color = Color.Lerp(baseColor, _strikeColor, window > 0f ? 0.6f + 0.4f * window : 0f);
            }
            else
            {
                diameter = Mathf.Lerp(_closedDiameter, _openDiameter, 0.3f);
                color = baseColor * 0.85f;
            }

            PlaceRing(point, normal, diameter, color);
        }

        Vector2 UvToFace(Vector2 uv)
        {
            float usable = Mathf.Max(0.01f, _surface.UsableArea);
            return new Vector2((uv.x - 0.5f) / usable, (uv.y - 0.5f) / usable);
        }

        void PlaceRing(Vector3 mark, Vector3 normal, float diameter, Color color)
        {
            _ring.SetPositionAndRotation(mark + normal * 0.003f, Quaternion.LookRotation(-normal, transform.up));
            Vector3 parentScale = transform.lossyScale;
            _ring.localScale = new Vector3(
                diameter / Mathf.Max(1e-4f, Mathf.Abs(parentScale.x)),
                diameter / Mathf.Max(1e-4f, Mathf.Abs(parentScale.y)),
                1f);

            _block.SetColor(ColorId, color);
            _renderer.SetPropertyBlock(_block);
        }
    }
}
