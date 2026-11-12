#if UNITY_EDITOR || DEVELOPMENT_BUILD || KF_DIAGNOSTICS
#define KF_DIAGNOSTICS_ACTIVE
#endif

using System.Text;
using KineticForge.Core;
using KineticForge.Tracking;
using UnityEngine;

namespace KineticForge.Feedback
{
    /// <summary>
    /// World-space diagnostics panel for testing on the headset without a cable: frame rate, game phase, stance source,
    /// body tracking permission, hand tracking, passthrough, and a count of warnings and errors with the latest error.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Compiled into the Editor, development builds and test builds made with the <c>KF_DIAGNOSTICS</c> define
    /// (<c>Kinetic Forge &gt; Quest Build &gt; Build Test APK</c>). In any other build the object switches itself off at
    /// startup, so the competition build never shows it. In the Editor it starts hidden; F1 toggles it.
    /// </para>
    /// <para>
    /// The panel stays fixed in the world, beside the forge, so it never covers the work. Its text is rebuilt a few
    /// times per second, which allocates a little; that is accepted because the panel only exists in test builds.
    /// </para>
    /// </remarks>
    [AddComponentMenu("Kinetic Forge/Feedback/Forge Diagnostics")]
    public sealed class ForgeDiagnostics : MonoBehaviour
    {
        const float WorstFrameWindow = 2f;
        const int ErrorPreviewLength = 72;

        // Every serialized field exists in every build, so the scene's serialized layout matches the player's. Builds
        // without diagnostics never read them.
#if !KF_DIAGNOSTICS_ACTIVE
#pragma warning disable CS0414
#endif
        [SerializeField, Tooltip("Text the panel writes to. Found on this GameObject when empty.")]
        TextMesh _text = null;

        [SerializeField, Range(0.1f, 2f), Tooltip("Seconds between text refreshes.")]
        float _refreshInterval = 0.25f;

        [SerializeField, Tooltip("Visible when Play starts in the Editor. F1 toggles it.")]
        bool _visibleInEditor = false;
#if !KF_DIAGNOSTICS_ACTIVE
#pragma warning restore CS0414
#endif

#if KF_DIAGNOSTICS_ACTIVE
        readonly StringBuilder _builder = new StringBuilder(512);
        MeshRenderer _renderer;
        ForgeGameManager _game;
        StanceTracker _stance;
        MicroGestureTracker[] _hands = System.Array.Empty<MicroGestureTracker>();

        float _windowTime;
        int _windowFrames;
        float _averageFps;
        float _worstFrame;
        float _worstFrameWindowStart;
        float _worstFrameShown;
        float _nextRefresh;
        int _warnings;
        int _errors;
        string _lastError = string.Empty;
        bool _visible = true;
#endif

        /// <summary>True when this build includes the diagnostics panel.</summary>
        public static bool IsCompiledIn
        {
            get
            {
#if KF_DIAGNOSTICS_ACTIVE
                return true;
#else
                return false;
#endif
            }
        }

#if KF_DIAGNOSTICS_ACTIVE
        /// <summary>Errors and exceptions logged since startup.</summary>
        public int ErrorCount => _errors;

        /// <summary>Warnings logged since startup.</summary>
        public int WarningCount => _warnings;

        /// <summary>Average frame rate over the last refresh interval.</summary>
        public float AverageFps => _averageFps;

        void Awake()
        {
            if (_text == null)
            {
                _text = GetComponent<TextMesh>();
            }

            _renderer = GetComponent<MeshRenderer>();
            SetVisible(!Application.isEditor || _visibleInEditor);
        }

        void OnEnable()
        {
            Application.logMessageReceived += HandleLog;
        }

        void OnDisable()
        {
            Application.logMessageReceived -= HandleLog;
        }

        void Start()
        {
            _game = FindAnyObjectByType<ForgeGameManager>();
            _stance = FindAnyObjectByType<StanceTracker>();
            _hands = FindObjectsByType<MicroGestureTracker>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }

        void Update()
        {
            if (Application.isEditor && SimulatorKeyboard.WasPressedThisFrame(KeyCode.F1))
            {
                SetVisible(!_visible);
            }

            float deltaTime = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;
            _windowTime += deltaTime;
            _windowFrames++;
            if (now - _worstFrameWindowStart > WorstFrameWindow)
            {
                _worstFrameShown = _worstFrame;
                _worstFrame = 0f;
                _worstFrameWindowStart = now;
            }

            _worstFrame = Mathf.Max(_worstFrame, deltaTime);

            if (now < _nextRefresh)
            {
                return;
            }

            _nextRefresh = now + _refreshInterval;
            _averageFps = _windowTime > 0f ? _windowFrames / _windowTime : 0f;
            _windowTime = 0f;
            _windowFrames = 0;
            if (_visible && _text != null)
            {
                _text.text = Compose();
            }
        }

        void SetVisible(bool visible)
        {
            _visible = visible;
            if (_renderer != null)
            {
                _renderer.enabled = visible;
            }
        }

        void HandleLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning)
            {
                _warnings++;
                return;
            }

            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                _errors++;
                _lastError = condition.Length > ErrorPreviewLength
                    ? condition.Substring(0, ErrorPreviewLength) + "..."
                    : condition;
            }
        }

        string Compose()
        {
            StringBuilder b = _builder;
            b.Length = 0;
            b.Append("<b>DIAGNOSTICS</b>  v").Append(Application.version).Append('\n');

            float minFps = _worstFrameShown > 0f ? 1f / _worstFrameShown : 0f;
            b.Append("FPS ").Append(Colored(Mathf.RoundToInt(_averageFps).ToString(), _averageFps >= 70f, _averageFps >= 58f))
                .Append("  min ").Append(Mathf.RoundToInt(minFps));
            AppendDisplayRate(b);
            b.Append('\n');

            b.Append("Phase  ").Append(_game != null ? _game.Phase.ToString() : "no game manager").Append('\n');

            if (_stance != null)
            {
                b.Append("Stance  ").Append(Colored(_stance.Source.ToString(),
                        _stance.Source == StanceTrackingSource.BodyTracking, _stance.Source != StanceTrackingSource.None))
                    .Append(_stance.SeatedMode ? "  seated" : "  standing")
                    .Append("  ").Append(_stance.StanceStability.ToString("0.00"))
                    .Append(_stance.IsCalibrated ? string.Empty : "  (not calibrated)").Append('\n');
            }
            else
            {
                b.Append("Stance  no tracker\n");
            }

            AppendMetaState(b);

            b.Append("Hands ");
            for (int i = 0; i < _hands.Length; i++)
            {
                MicroGestureTracker hand = _hands[i];
                if (hand == null)
                {
                    continue;
                }

                b.Append("  ").Append(hand.Hand == ForgeHand.Left ? "L " : "R ")
                    .Append(Colored(hand.IsHandTracked ? "tracked" : "lost", hand.IsHandTracked, false))
                    .Append(" (").Append(hand.Source.ToString()).Append(')');
            }

            b.Append('\n');
            b.Append("Log  ").Append(Colored(_errors + " errors", _errors == 0, false))
                .Append("  ").Append(_warnings).Append(" warnings");
            if (_lastError.Length > 0)
            {
                b.Append("\n<size=34>").Append(_lastError).Append("</size>");
            }

            return b.ToString();
        }

        static void AppendDisplayRate(StringBuilder b)
        {
#if KF_META_XR_CORE
            if (ForgePlatform.IsMetaRuntimeAvailable)
            {
                b.Append("  display ").Append(Mathf.RoundToInt(OVRPlugin.systemDisplayFrequency)).Append(" Hz");
            }
#endif
        }

        static void AppendMetaState(StringBuilder b)
        {
#if KF_META_XR_CORE
            if (!ForgePlatform.IsMetaRuntimeAvailable)
            {
                b.Append("Meta runtime  unavailable (Editor fallback)\n");
                return;
            }

            bool body = OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.BodyTracking);
            b.Append("Body permission  ").Append(Colored(body ? "granted" : "denied", body, false)).Append('\n');
            bool handTracking = OVRPlugin.GetHandTrackingEnabled();
            bool passthrough = OVRManager.IsInsightPassthroughInitialized();
            b.Append("Hand tracking  ").Append(Colored(handTracking ? "on" : "off", handTracking, false))
                .Append("  Passthrough  ").Append(Colored(passthrough ? "ready" : "not ready", passthrough, true)).Append('\n');
#else
            b.Append("Meta XR SDK  not installed\n");
#endif
        }

        static string Colored(string text, bool good, bool acceptable)
        {
            string color = good ? "#7CFF9B" : acceptable ? "#FFD27A" : "#FF7A6E";
            return "<color=" + color + ">" + text + "</color>";
        }
#else
        void Awake()
        {
            // Not compiled into this build: keep the competition build free of test UI.
            gameObject.SetActive(false);
        }
#endif

        void OnValidate()
        {
            _refreshInterval = Mathf.Max(0.1f, _refreshInterval);
        }
    }
}
