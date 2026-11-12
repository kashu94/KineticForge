using UnityEngine;

namespace KineticForge.Feedback
{
    /// <summary>
    /// A static world-space label for the Forge Text shader: keeps the shader sampling the font's current atlas and
    /// sizes the label to <see cref="LineHeight"/> once its mesh exists.
    /// </summary>
    [RequireComponent(typeof(TextMesh))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Feedback/World Text")]
    public sealed class WorldText : MonoBehaviour
    {
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        [SerializeField, Range(0.005f, 0.2f), Tooltip("Height (m) of one line of text.")]
        float _lineHeight = 0.035f;

        TextMesh _text;
        Renderer _renderer;
        MaterialPropertyBlock _block;
        Texture _fontTexture;
        bool _scaled;

        /// <summary>Height (m) of one line of text.</summary>
        public float LineHeight => _lineHeight;

        void Awake()
        {
            _text = GetComponent<TextMesh>();
            _renderer = GetComponent<Renderer>();
            _block = new MaterialPropertyBlock();
        }

        void LateUpdate()
        {
            Font font = _text.font;
            Texture texture = font != null && font.material != null ? font.material.mainTexture : null;
            if (texture != null && texture != _fontTexture)
            {
                _fontTexture = texture;
                _renderer.GetPropertyBlock(_block);
                _block.SetTexture(MainTexId, texture);
                _renderer.SetPropertyBlock(_block);
            }

            if (_scaled)
            {
                return;
            }

            float height = _renderer.bounds.size.y;
            if (height > 1e-5f)
            {
                int lines = 1;
                string text = _text.text;
                for (int i = 0; i < text.Length; i++)
                {
                    if (text[i] == '\n')
                    {
                        lines++;
                    }
                }

                transform.localScale *= _lineHeight / (height / lines);
                _scaled = true;
            }
        }
    }
}
