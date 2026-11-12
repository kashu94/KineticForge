using TMPro;
using UnityEngine;

namespace KineticForge.Feedback
{
    /// <summary>
    /// The plate behind a world-space TextMesh Pro label: smoked glass with a gilt rule and a soft glow (the Forge Panel
    /// shader), fitted to the text with padding. When the text changes size the plate glides to its new size, and it
    /// fades with the text.
    /// </summary>
    /// <remarks>
    /// Put it on a child of the label, on a quad (the built-in Quad mesh). The plate sits a few millimeters behind the text
    /// and takes the label's alpha, so a label that fades in brings its plate with it. An empty label hides the plate. The
    /// per-frame path does not allocate: sizes go to the shader through a material property block.
    /// </remarks>
    [RequireComponent(typeof(MeshRenderer))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Feedback/Forge Panel")]
    public sealed class ForgePanel : MonoBehaviour
    {
        static readonly int SizeId = Shader.PropertyToID("_Size");
        static readonly int FadeId = Shader.PropertyToID("_Fade");
        static readonly int GlowWidthId = Shader.PropertyToID("_GlowWidth");

        [SerializeField, Tooltip("Label this plate backs. Found on the parent when empty.")]
        TMP_Text _text = null;

        [SerializeField, Tooltip("Margin (m) around the text, horizontal and vertical.")]
        Vector2 _padding = new Vector2(0.05f, 0.032f);

        [SerializeField, Min(0f), Tooltip("Narrowest plate (m), so short titles still sit on a proper plate.")]
        float _minWidth = 0.34f;

        [SerializeField, Range(0f, 0.02f), Tooltip("Distance (m) behind the text.")]
        float _depth = 0.006f;

        [SerializeField, Range(1f, 40f), Tooltip("How quickly the plate glides to a new size (1/s).")]
        float _resizeRate = 14f;

        MeshRenderer _renderer;
        MaterialPropertyBlock _block;
        Vector2 _size;
        bool _sized;
        float _glowWidth;

        /// <summary>Current plate size in meters, without the glow margin.</summary>
        public Vector2 Size => _size;

        void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _block = new MaterialPropertyBlock();
            if (_text == null && transform.parent != null)
            {
                _text = transform.parent.GetComponent<TMP_Text>();
            }

            Material material = _renderer.sharedMaterial;
            _glowWidth = material != null && material.HasProperty(GlowWidthId) ? material.GetFloat(GlowWidthId) : 0f;
        }

        void LateUpdate()
        {
            if (_text == null)
            {
                return;
            }

            Bounds bounds = _text.textBounds;
            bool empty = _text.textInfo == null || _text.textInfo.characterCount == 0 || bounds.size.x <= 1e-5f;
            if (empty)
            {
                if (_renderer.enabled)
                {
                    _renderer.enabled = false;
                }

                _sized = false;
                return;
            }

            if (!_renderer.enabled)
            {
                _renderer.enabled = true;
            }

            // Text bounds are in the label's local space; the plate is its child, so they map directly.
            Vector2 target = new Vector2(Mathf.Max(bounds.size.x + 2f * _padding.x, _minWidth), bounds.size.y + 2f * _padding.y);
            if (!_sized)
            {
                _size = target;
                _sized = true;
            }
            else
            {
                float blend = 1f - Mathf.Exp(-_resizeRate * Time.unscaledDeltaTime);
                _size = Vector2.Lerp(_size, target, blend);
            }

            Transform plate = transform;
            plate.localPosition = new Vector3(bounds.center.x, bounds.center.y, _depth);
            plate.localRotation = Quaternion.identity;
            plate.localScale = new Vector3(_size.x + 2f * _glowWidth, _size.y + 2f * _glowWidth, 1f);

            _renderer.GetPropertyBlock(_block);
            _block.SetVector(SizeId, new Vector4(_size.x, _size.y, 0f, 0f));
            _block.SetFloat(FadeId, _text.alpha);
            _renderer.SetPropertyBlock(_block);
        }

        void OnValidate()
        {
            _padding = Vector2.Max(_padding, Vector2.zero);
        }
    }
}
