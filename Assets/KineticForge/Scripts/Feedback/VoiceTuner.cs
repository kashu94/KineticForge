using System.Globalization;
using System.Text;
using KineticForge.Core;
using KineticForge.Crafting;
using TMPro;
using UnityEngine;

namespace KineticForge.Feedback
{
    /// <summary>
    /// The tuner: a plate beside the anvil with one gauge per voice of the relic being voiced. Each gauge shows how far
    /// its voice is from its target in cents, the in-tune band, and, as a ghost marker, where the next cut at the
    /// strike point would take it. A row brightens when a strike rings its voice, so the player learns which spots ring
    /// which voices.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The plate builds its own children on Awake: a glass plate (Forge Panel), a title and subtitle, then per voice a
    /// label, a gauge quad (Forge Gauge) and a readout, and a footer with the tool, the cuts left and a hint. It fades in
    /// when voicing starts and out when it is dormant.
    /// </para>
    /// <para>
    /// The per-frame path does not allocate: gauge values go to the shader through material property blocks, readouts are
    /// picked from a table of strings built once, and the footer is rebuilt only when what it shows changes.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Feedback/Voice Tuner")]
    public sealed class VoiceTuner : MonoBehaviour
    {
        static readonly int SizeId = Shader.PropertyToID("_Size");
        static readonly int FadeId = Shader.PropertyToID("_Fade");
        static readonly int BorderColorId = Shader.PropertyToID("_BorderColor");
        static readonly int GlowWidthId = Shader.PropertyToID("_GlowWidth");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int RangeId = Shader.PropertyToID("_Range");
        static readonly int ToleranceId = Shader.PropertyToID("_Tolerance");
        static readonly int ErrorId = Shader.PropertyToID("_Error");
        static readonly int DeltaId = Shader.PropertyToID("_Delta");
        static readonly int PreviewAlphaId = Shader.PropertyToID("_PreviewAlpha");
        static readonly int ActiveId = Shader.PropertyToID("_Active");

        const int CentsTableLimit = 199;
        const float GaugeRangeCents = 150f;

        static readonly Color s_goldRule = new Color(1f, 0.76f, 0.42f, 0.9f);
        static readonly Color s_tunedRule = new Color(0.5f, 1f, 0.62f, 0.95f);
        static readonly Color s_spentRule = new Color(1f, 0.45f, 0.35f, 0.9f);
        static readonly Color s_flatColor = new Color(0.5f, 0.62f, 1f, 1f);
        static readonly Color s_tunedColor = new Color(0.55f, 1f, 0.65f, 1f);

        static string[] s_centsTable;

        [SerializeField, Tooltip("Relic to read. The first relic with voicing when empty.")]
        RelicVoicing _voicing = null;

        [SerializeField, Tooltip("Forge Panel material for the glass plate.")]
        Material _panelMaterial = null;

        [SerializeField, Tooltip("Forge Gauge material, one instance per voice is not needed: values go through property blocks.")]
        Material _gaugeMaterial = null;

        [SerializeField, Tooltip("Display face for the title (Cinzel).")]
        TMP_FontAsset _titleFont = null;

        [SerializeField, Tooltip("Text face (Inter).")]
        TMP_FontAsset _textFont = null;

        [SerializeField, Tooltip("Plate size in meters.")]
        Vector2 _size = new Vector2(0.5f, 0.44f);

        [SerializeField, Tooltip("One color per voice.")]
        Color[] _voiceColors =
        {
            new Color(1f, 0.72f, 0.3f),
            new Color(0.35f, 0.85f, 1f),
            new Color(0.85f, 0.55f, 1f),
            new Color(0.62f, 1f, 0.5f),
        };

        [SerializeField, Range(1f, 20f), Tooltip("How quickly the plate fades in and out (1/s).")]
        float _fadeRate = 6f;

        readonly StringBuilder _builder = new StringBuilder(96);
        readonly Renderer[] _gaugeRenderers = new Renderer[VoicingMath.VoiceCount];
        readonly TMP_Text[] _labels = new TMP_Text[VoicingMath.VoiceCount];
        readonly TMP_Text[] _readouts = new TMP_Text[VoicingMath.VoiceCount];
        readonly int[] _shownCents = new int[VoicingMath.VoiceCount];
        readonly bool[] _shownTuned = new bool[VoicingMath.VoiceCount];
        readonly Vector2 _gaugeSize = new Vector2(0.232f, 0.046f);

        MaterialPropertyBlock _block;
        Renderer _plate;
        TMP_Text _title;
        TMP_Text _subtitle;
        TMP_Text _footerLeft;
        TMP_Text _footerRight;
        TMP_Text _hint;
        RelicResonance _resonance;
        RelicCarvingSurface _surface;
        CarvingCursor _cursor;
        float _fade;
        float _glowWidth;
        int _shownCommission = -1;
        int _footerKey = int.MinValue;
        bool _built;
        bool _shown;

        /// <summary>True while the tuner is visible enough to read.</summary>
        public bool IsShown => _fade > 0.5f;

        /// <summary>Number of voice gauges the tuner built.</summary>
        public int GaugeCount => _built ? VoicingMath.VoiceCount : 0;

        void Awake()
        {
            _block = new MaterialPropertyBlock();
            _glowWidth = _panelMaterial != null && _panelMaterial.HasProperty(GlowWidthId) ? _panelMaterial.GetFloat(GlowWidthId) : 0f;
            BuildCentsTable();
            Build();
            SetRenderersVisible(false);
        }

        void Start()
        {
            ResolveRelic();
        }

        void ResolveRelic()
        {
            if (_voicing == null)
            {
                _voicing = FindAnyObjectByType<RelicVoicing>();
            }

            if (_voicing != null)
            {
                _resonance = _voicing.GetComponent<RelicResonance>();
                _surface = _voicing.GetComponent<RelicCarvingSurface>();
                _cursor = _voicing.GetComponent<CarvingCursor>();
            }
        }

        void LateUpdate()
        {
            if (!_built)
            {
                return;
            }

            if (_voicing == null || _resonance == null)
            {
                ResolveRelic();
            }

            bool want = _voicing != null && _voicing.Stage != VoicingStage.Dormant;
            float target = want ? 1f : 0f;
            _fade = Mathf.MoveTowards(_fade, target, _fadeRate * Time.unscaledDeltaTime);
            bool visible = _fade > 0.005f;
            if (visible != _shown)
            {
                SetRenderersVisible(visible);
            }

            if (!visible || _voicing == null)
            {
                return;
            }

            Refresh();
        }

        void Refresh()
        {
            VoicingCommission commission = _voicing.Commission;
            if (commission.Index != _shownCommission)
            {
                ApplyCommission(commission);
            }

            Vector4 errors = _voicing.Errors;
            float tolerance = commission.ToleranceCents;
            bool tuned = _voicing.IsInTune;
            VoicingStage stage = _voicing.Stage;

            // Where the next cut would take each voice, at the point a strike would land.
            Vector4 delta = Vector4.zero;
            float previewAlpha = 0f;
            if (stage == VoicingStage.Voicing && _cursor != null && _surface != null && _resonance != null)
            {
                _cursor.GetStrikePoint(out Vector3 point, out _);
                Vector2 uv = _surface.WorldToUV(point);
                delta = _voicing.Preview(uv, 1f, _resonance.Softness, _resonance.Charge);
                previewAlpha = _voicing.Tool == VoicingTool.Chisel ? 1f : 0.35f;
            }

            Vector4 plate = _resonance != null ? _resonance.PlateAmplitudes : Vector4.zero;
            for (int m = 0; m < VoicingMath.VoiceCount; m++)
            {
                float error = errors[m];
                bool inTune = Mathf.Abs(error) <= tolerance;
                Color needle = inTune ? s_tunedColor : error < 0f ? s_flatColor : VoiceColor(m);

                _gaugeRenderers[m].GetPropertyBlock(_block);
                _block.SetColor(ColorId, needle);
                _block.SetVector(SizeId, new Vector4(_gaugeSize.x, _gaugeSize.y, 0f, 0f));
                _block.SetFloat(RangeId, GaugeRangeCents);
                _block.SetFloat(ToleranceId, tolerance);
                _block.SetFloat(ErrorId, error);
                _block.SetFloat(DeltaId, delta[m]);
                _block.SetFloat(PreviewAlphaId, previewAlpha);
                _block.SetFloat(ActiveId, Mathf.Clamp01(0.3f + Mathf.Abs(plate[m]) * 1.1f));
                _block.SetFloat(FadeId, _fade);
                _gaugeRenderers[m].SetPropertyBlock(_block);

                int rounded = Mathf.Clamp(Mathf.RoundToInt(error), -CentsTableLimit, CentsTableLimit);
                if (rounded != _shownCents[m] || inTune != _shownTuned[m])
                {
                    _shownCents[m] = rounded;
                    _shownTuned[m] = inTune;
                    _readouts[m].text = inTune ? "OK" : s_centsTable[rounded + CentsTableLimit];
                    _readouts[m].color = inTune ? s_tunedColor : error < 0f ? s_flatColor : new Color(1f, 0.86f, 0.62f);
                }

                _labels[m].alpha = _fade;
                _readouts[m].alpha = _fade;
            }

            Color rule = stage == VoicingStage.Settling && _voicing.IsSpent ? s_spentRule : tuned ? s_tunedRule : s_goldRule;
            _plate.GetPropertyBlock(_block);
            _block.SetVector(SizeId, new Vector4(_size.x, _size.y, 0f, 0f));
            _block.SetFloat(FadeId, _fade);
            _block.SetColor(BorderColorId, rule);
            _plate.SetPropertyBlock(_block);

            _title.alpha = _fade;
            _subtitle.alpha = _fade * 0.85f;
            _footerLeft.alpha = _fade;
            _footerRight.alpha = _fade;
            _hint.alpha = _fade * 0.7f;
            RefreshFooter(stage, tuned);
        }

        void RefreshFooter(VoicingStage stage, bool tuned)
        {
            int seconds = Mathf.CeilToInt(_voicing.SettleRemaining);
            int key = ((int)stage * 31 + (int)_voicing.Tool) * 31 + _voicing.CutsLeft;
            key = key * 31 + (tuned ? 1 : 0);
            key = key * 31 + (_voicing.IsSpent ? 1 : 0);
            key = key * 31 + seconds;
            if (key == _footerKey)
            {
                return;
            }

            _footerKey = key;
            StringBuilder b = _builder;
            b.Length = 0;
            if (stage == VoicingStage.Done)
            {
                b.Append("<color=#ffc860>FINISHED</color>");
            }
            else if (stage == VoicingStage.Settling && _voicing.IsSpent)
            {
                b.Append("<color=#ff7a6a>METAL SPENT</color>");
            }
            else if (tuned)
            {
                b.Append("<color=#8dffa6>IN TUNE</color>  <size=80%>hold still</size>");
            }
            else if (_voicing.Tool == VoicingTool.Chisel)
            {
                b.Append("<color=#ffb060>CHISEL</color>  <size=80%>every cut is final</size>");
            }
            else
            {
                b.Append("<color=#8ec8ff>HAMMER</color>  <size=80%>rings, never cuts</size>");
            }

            _footerLeft.text = b.ToString();

            b.Length = 0;
            if (stage == VoicingStage.Settling && !_voicing.IsSpent && seconds > 0)
            {
                b.Append(seconds.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                b.Append("CUTS <color=#ffc860>").Append(_voicing.CutsLeft.ToString(CultureInfo.InvariantCulture)).Append("</color>");
            }

            _footerRight.text = b.ToString();
        }

        void ApplyCommission(VoicingCommission commission)
        {
            _shownCommission = commission.Index;
            _title.text = commission.Name.ToUpperInvariant();
            _builder.Length = 0;
            _builder.Append(commission.ChordName).Append(" in ").Append(commission.RootName).Append("   ")
                .Append("tune each voice within +/-").Append(commission.ToleranceCents.ToString("0", CultureInfo.InvariantCulture))
                .Append(" cents");
            _subtitle.text = _builder.ToString();
            for (int m = 0; m < VoicingMath.VoiceCount; m++)
            {
                _labels[m].text = commission.Roles[m];
                _shownCents[m] = int.MinValue;
                _shownTuned[m] = false;
            }

            _footerKey = int.MinValue;
        }

        Color VoiceColor(int voice)
        {
            return _voiceColors != null && voice < _voiceColors.Length ? _voiceColors[voice] : Color.white;
        }

        #region Construction

        void Build()
        {
            Mesh quad = BorrowQuadMesh();
            float halfWidth = _size.x * 0.5f;
            float halfHeight = _size.y * 0.5f;

            _plate = CreateQuad("Plate", quad, _panelMaterial, Vector3.zero,
                new Vector2(_size.x + 2f * _glowWidth, _size.y + 2f * _glowWidth), 0.012f);

            _title = CreateText("Title", _titleFont != null ? _titleFont : _textFont, 0.027f, TextAlignmentOptions.Center,
                new Vector2(0f, halfHeight - 0.042f), new Vector2(_size.x - 0.06f, 0.04f), new Color(1f, 0.86f, 0.6f), 8f);
            _subtitle = CreateText("Subtitle", _textFont, 0.0145f, TextAlignmentOptions.Center,
                new Vector2(0f, halfHeight - 0.077f), new Vector2(_size.x - 0.04f, 0.03f), new Color(0.78f, 0.74f, 0.7f), 0f);

            float rowTop = halfHeight - 0.13f;
            const float RowPitch = 0.058f;
            float labelLeft = -halfWidth + 0.03f;
            float readoutRight = halfWidth - 0.03f;
            float gaugeCenter = 0.015f;
            for (int m = 0; m < VoicingMath.VoiceCount; m++)
            {
                float y = rowTop - m * RowPitch;
                _labels[m] = CreateText("Label " + m, _textFont, 0.0185f, TextAlignmentOptions.Left,
                    new Vector2(labelLeft, y), new Vector2(0.12f, 0.03f), VoiceColor(m), 0f, pivotX: 0f);
                _gaugeRenderers[m] = CreateQuad("Gauge " + m, quad, _gaugeMaterial, new Vector3(gaugeCenter, y, 0f), _gaugeSize, 0.008f);
                _readouts[m] = CreateText("Readout " + m, _textFont, 0.0185f, TextAlignmentOptions.Right,
                    new Vector2(readoutRight, y), new Vector2(0.08f, 0.03f), Color.white, 0f, pivotX: 1f);
                _shownCents[m] = int.MinValue;
            }

            float footerY = -halfHeight + 0.062f;
            _footerLeft = CreateText("Footer Left", _textFont, 0.0165f, TextAlignmentOptions.Left,
                new Vector2(labelLeft, footerY), new Vector2(0.3f, 0.03f), new Color(0.9f, 0.9f, 0.9f), 0f, pivotX: 0f);
            _footerRight = CreateText("Footer Right", _textFont, 0.0165f, TextAlignmentOptions.Right,
                new Vector2(readoutRight, footerY), new Vector2(0.14f, 0.03f), new Color(0.9f, 0.9f, 0.9f), 0f, pivotX: 1f);
            _hint = CreateText("Hint", _textFont, 0.0125f, TextAlignmentOptions.Center,
                new Vector2(0f, -halfHeight + 0.026f), new Vector2(_size.x - 0.04f, 0.025f), new Color(0.72f, 0.7f, 0.68f), 0f);
            _hint.text = ForgePlatform.IsMetaRuntimeAvailable
                ? "Tap your left thumb to swap hammer and chisel"
                : "T swaps hammer and chisel";
            _built = true;
        }

        static Mesh BorrowQuadMesh()
        {
            // The built-in quad mesh, borrowed from a primitive that is destroyed at once, so no collider is added.
            GameObject template = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Mesh mesh = template.GetComponent<MeshFilter>().sharedMesh;
            Destroy(template);
            return mesh;
        }

        Renderer CreateQuad(string objectName, Mesh mesh, Material material, Vector3 localPosition, Vector2 size, float depth)
        {
            GameObject quad = new GameObject(objectName);
            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = new Vector3(localPosition.x, localPosition.y, depth);
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);
            quad.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = quad.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return renderer;
        }

        TMP_Text CreateText(string objectName, TMP_FontAsset font, float lineHeight, TextAlignmentOptions alignment,
            Vector2 localPosition, Vector2 box, Color color, float characterSpacing, float pivotX = 0.5f)
        {
            GameObject host = new GameObject(objectName);
            host.transform.SetParent(transform, false);
            host.transform.localPosition = new Vector3(localPosition.x, localPosition.y, 0f);
            TextMeshPro text = host.AddComponent<TextMeshPro>();
            if (font != null)
            {
                text.font = font;
            }

            text.fontSize = ForgeGuide.FontSizeFor(text.font, lineHeight);
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = true;
            text.characterSpacing = characterSpacing;
            text.color = color;
            text.rectTransform.sizeDelta = box;
            text.rectTransform.pivot = new Vector2(pivotX, 0.5f);
            MeshRenderer renderer = host.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return text;
        }

        void SetRenderersVisible(bool visible)
        {
            _shown = visible;
            for (int i = 0; i < transform.childCount; i++)
            {
                transform.GetChild(i).gameObject.SetActive(visible);
            }
        }

        static void BuildCentsTable()
        {
            if (s_centsTable != null)
            {
                return;
            }

            s_centsTable = new string[2 * CentsTableLimit + 1];
            for (int cents = -CentsTableLimit; cents <= CentsTableLimit; cents++)
            {
                s_centsTable[cents + CentsTableLimit] = (cents > 0 ? "+" : string.Empty) + cents.ToString(CultureInfo.InvariantCulture);
            }
        }

        #endregion
    }
}
