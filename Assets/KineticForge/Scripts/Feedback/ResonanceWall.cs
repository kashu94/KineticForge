using System;
using System.Collections.Generic;
using KineticForge.Core;
using KineticForge.Crafting;
using KineticForge.Tracking;
using UnityEngine;

namespace KineticForge.Feedback
{
    /// <summary>
    /// The Resonance Wall: every relic the player forges and quenches hangs here, showing its Chladni sigil, and keeps the
    /// note it was tuned to. Point at a relic and pinch, or click it in the Editor, and it rings. Over sessions the wall
    /// becomes the player's own instrument.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Plaques come from <see cref="ForgeArchive"/> on Start and are added when a <see cref="RelicQuench"/> finishes. Each
    /// plaque's note is rendered ahead of time by <see cref="ModalNoteBank"/> with the detune its carving earned, so a
    /// fine relic rings pure and a rough one beats. Grades tint the sigil: gold for S, blue for A, white for B, dim for C,
    /// red for fractured.
    /// </para>
    /// <para>
    /// Strike rays come from the right hand's system pointer on Quest and from the mouse in the Editor. Plaques are laid
    /// out in a grid on this transform's XY plane, facing its −Z. The per-frame path does not allocate.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(AudioSource))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Feedback/Resonance Wall")]
    public sealed class ResonanceWall : MonoBehaviour
    {
        static readonly int ModeAmpId = Shader.PropertyToID("_ModeAmp");
        static readonly int ModeMId = Shader.PropertyToID("_ModeM");
        static readonly int ModeNId = Shader.PropertyToID("_ModeN");
        static readonly int ModeSignId = Shader.PropertyToID("_ModeSign");
        static readonly int ChladniColorId = Shader.PropertyToID("_ChladniColor");
        static readonly int EmissiveId = Shader.PropertyToID("_Emissive");

        [SerializeField, Tooltip("Material for plaques (Kinetic Forge/Forge Metal).")]
        Material _plaqueMaterial = null;

        [SerializeField, Range(2, 6), Tooltip("Plaques per row.")]
        int _columns = 4;

        [SerializeField, Range(0.08f, 0.4f), Tooltip("Plaque size (m).")]
        float _plaqueSize = 0.17f;

        [SerializeField, Range(0.3f, 2f), Tooltip("Seconds between the relics of a progression.")]
        float _progressionSpacing = 0.85f;

        [SerializeField, Range(0.01f, 0.2f), Tooltip("Gap (m) between plaques.")]
        float _gap = 0.06f;

        [SerializeField, Range(1f, 8f), Tooltip("Longest strike reach (m).")]
        float _reach = 6f;

        [SerializeField, Tooltip("Relic whose quench adds plaques. Found in the scene when empty.")]
        RelicQuench _quench = null;

        sealed class Plaque
        {
            public Transform Transform;
            public Renderer Renderer;
            public Collider Collider;
            public AudioClip Note;
            public Vector4 Figure;
            public Color Tint;
            public float Glow;
        }

        readonly List<Plaque> _plaques = new List<Plaque>();
        MaterialPropertyBlock _block;
        AudioSource _audio;
        Mesh _mesh;
        Action<MicroGestureTapEvent> _onTap;
        Action _onQuenched;
        RelicSigil _sigil;
        RelicVoicing _voicing;
        RelicResonance _resonance;
        const int ProgressionCapacity = 12;
        const int ProgressionLength = 5;
        readonly int[] _progression = new int[ProgressionCapacity];
        int _progressionCount;
        int _progressionNext;
        float _progressionTime;

#if KF_META_XR_CORE
        OVRHand _pointerHand;
        float _nextResolveTime;
#endif

        /// <summary>Number of relics on the wall.</summary>
        public int Count => _plaques.Count;

        /// <summary>True while a progression is still playing.</summary>
        public bool IsPlayingProgression => _progressionNext < _progressionCount;

        /// <summary>
        /// Plays the latest relics' chords in the order they were forged, one after another, so the player's tuned relics
        /// become a short piece of music. A new call replaces one that is still playing.
        /// </summary>
        /// <param name="count">How many of the latest relics to play, newest last.</param>
        public void PlayProgression(int count)
        {
            int play = Mathf.Clamp(count, 0, Mathf.Min(_plaques.Count, ProgressionCapacity));
            _progressionCount = play;
            _progressionNext = 0;
            for (int i = 0; i < play; i++)
            {
                _progression[i] = _plaques.Count - play + i;
            }

            _progressionTime = Time.time;
        }

        /// <summary>Rings plaque <paramref name="index"/>. For tools and tests.</summary>
        /// <param name="index">Plaque index, oldest first.</param>
        public void Ring(int index)
        {
            if (index < 0 || index >= _plaques.Count)
            {
                return;
            }

            Plaque plaque = _plaques[index];
            plaque.Glow = 1f;
            if (plaque.Note != null)
            {
                _audio.PlayOneShot(plaque.Note, 0.9f);
            }
        }

        void Awake()
        {
            _audio = GetComponent<AudioSource>();
            _block = new MaterialPropertyBlock();
            _onTap = HandleTap;
            _onQuenched = HandleQuenched;
            _mesh = RelicForgeMesh.Build(new Vector3(_plaqueSize, _plaqueSize, 0.025f), 24);
        }

        void Start()
        {
            if (_quench == null)
            {
                _quench = FindAnyObjectByType<RelicQuench>();
            }

            if (_quench != null)
            {
                _quench.OnQuenched += _onQuenched;
                _sigil = _quench.GetComponent<RelicSigil>();
                _voicing = _quench.GetComponent<RelicVoicing>();
                _resonance = _quench.GetComponent<RelicResonance>();
            }

            List<ForgedRelic> relics = ForgeArchive.Load();
            for (int i = 0; i < relics.Count; i++)
            {
                AddPlaque(relics[i]);
            }
        }

        void OnEnable()
        {
            ForgeEvents.MicroGestureTap += _onTap;
        }

        void OnDisable()
        {
            ForgeEvents.MicroGestureTap -= _onTap;
        }

        void OnDestroy()
        {
            if (_quench != null)
            {
                _quench.OnQuenched -= _onQuenched;
            }

            for (int i = 0; i < _plaques.Count; i++)
            {
                if (_plaques[i].Note != null)
                {
                    Destroy(_plaques[i].Note);
                }
            }

            if (_mesh != null)
            {
                Destroy(_mesh);
            }
        }

        void LateUpdate()
        {
            if (_progressionNext < _progressionCount && Time.time >= _progressionTime)
            {
                Ring(_progression[_progressionNext]);
                _progressionNext++;
                _progressionTime = Time.time + _progressionSpacing;
            }

            float deltaTime = Time.deltaTime;
            for (int i = 0; i < _plaques.Count; i++)
            {
                Plaque plaque = _plaques[i];
                plaque.Glow = Mathf.Max(0f, plaque.Glow - deltaTime * 0.8f);
                _block.SetVector(ModeAmpId, plaque.Figure * (0.55f + 0.9f * plaque.Glow));
                _block.SetColor(ChladniColorId, plaque.Tint);
                _block.SetFloat(EmissiveId, 0.15f * plaque.Glow);
                plaque.Renderer.SetPropertyBlock(_block);
            }
        }

        void HandleQuenched()
        {
            if ((_sigil == null && _voicing == null) || _resonance == null)
            {
                return;
            }

            ForgedRelic relic = _voicing != null
                ? new ForgedRelic(_voicing.CommissionIndex, _resonance.Grade, _resonance.MeanQuality, _voicing.Errors)
                : new ForgedRelic(_sigil.SigilIndex, _resonance.Grade, _resonance.MeanQuality);
            ForgeArchive.Append(relic);
            if (_plaques.Count >= ForgeArchive.Capacity)
            {
                Plaque oldest = _plaques[0];
                _plaques.RemoveAt(0);
                Destroy(oldest.Transform.gameObject);
                if (oldest.Note != null)
                {
                    Destroy(oldest.Note);
                }

                Layout();
            }

            AddPlaque(relic);

            // The new relic joins the others: the wall plays the latest few, ending on it.
            PlayProgression(ProgressionLength);
        }

        void HandleTap(MicroGestureTapEvent payload)
        {
            if (_plaques.Count == 0 || !TryGetStrikeRay(out Ray ray))
            {
                return;
            }

            if (!UnityEngine.Physics.Raycast(ray, out RaycastHit hit, _reach, ~0, QueryTriggerInteraction.Collide))
            {
                return;
            }

            for (int i = 0; i < _plaques.Count; i++)
            {
                if (_plaques[i].Collider == hit.collider)
                {
                    Ring(i);
                    return;
                }
            }
        }

        bool TryGetStrikeRay(out Ray ray)
        {
#if KF_META_XR_CORE
            if (ForgePlatform.IsMetaRuntimeAvailable)
            {
                if (_pointerHand == null && Time.unscaledTime >= _nextResolveTime)
                {
                    _nextResolveTime = Time.unscaledTime + 2f;
                    OVRSkeleton skeleton = HandSkeletonReader.FindHandSkeleton(ForgeHand.Right);
                    _pointerHand = skeleton != null ? skeleton.GetComponent<OVRHand>() : null;
                }

                if (_pointerHand != null && _pointerHand.IsTracked && _pointerHand.IsPointerPoseValid &&
                    _pointerHand.PointerPose != null)
                {
                    ray = new Ray(_pointerHand.PointerPose.position, _pointerHand.PointerPose.forward);
                    return true;
                }
            }
#endif
            Camera eye = Camera.main;
            if (eye != null && SimulatorPointer.IsAvailable)
            {
                ray = eye.ScreenPointToRay(SimulatorPointer.Position);
                return true;
            }

            ray = default;
            return false;
        }

        void AddPlaque(ForgedRelic relic)
        {
            CymaticSigil sigil = CymaticMath.GetSigil(relic.Sigil);
            GameObject host = new GameObject("Relic Plaque (" + sigil.Name + ")");
            host.transform.SetParent(transform, false);
            host.AddComponent<MeshFilter>().sharedMesh = _mesh;
            MeshRenderer renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _plaqueMaterial;
            BoxCollider collider = host.AddComponent<BoxCollider>();
            collider.size = new Vector3(_plaqueSize, _plaqueSize, 0.03f);
            collider.isTrigger = true;

            _block.Clear();
            _block.SetVector(ModeMId, ResonanceMath.ModeM);
            _block.SetVector(ModeNId, ResonanceMath.ModeN);
            _block.SetVector(ModeSignId, ResonanceMath.ModeSign);
            renderer.SetPropertyBlock(_block);

            int sampleRate = Mathf.Max(8000, AudioSettings.outputSampleRate);
            Plaque plaque = new Plaque
            {
                Transform = host.transform,
                Renderer = renderer,
                Collider = collider,
                Figure = sigil.Figure,
                Tint = GradeTint(relic.Grade),
                Note = relic.HasVoices
                    ? ModalNoteBank.RenderChord("Relic Chord " + sigil.NoteName, VoicingMath.GetCommission(relic.Sigil),
                        relic.VoiceErrors, 3.5f, sampleRate)
                    : ModalNoteBank.Render("Relic Note " + sigil.NoteName, sigil.Note, 3f, relic.DetuneCents, sampleRate),
            };
            _plaques.Add(plaque);
            Layout();
        }

        void Layout()
        {
            float pitch = _plaqueSize + _gap;
            int rows = Mathf.CeilToInt(ForgeArchive.Capacity / (float)_columns);
            for (int i = 0; i < _plaques.Count; i++)
            {
                int column = i % _columns;
                int row = i / _columns;
                Vector3 local = new Vector3((column - (_columns - 1) * 0.5f) * pitch, ((rows - 1) * 0.5f - row) * pitch, 0f);
                _plaques[i].Transform.SetLocalPositionAndRotation(local, Quaternion.identity);
            }
        }

        static Color GradeTint(ForgeGrade grade)
        {
            switch (grade)
            {
                case ForgeGrade.S: return new Color(1.4f, 1f, 0.4f);
                case ForgeGrade.A: return new Color(0.4f, 0.85f, 1.3f);
                case ForgeGrade.B: return new Color(0.9f, 0.9f, 0.95f);
                case ForgeGrade.C: return new Color(0.45f, 0.42f, 0.4f);
                default: return new Color(1.2f, 0.3f, 0.1f);
            }
        }
    }
}
