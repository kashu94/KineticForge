using KineticForge.Core;
using KineticForge.Tracking;
using UnityEngine;

namespace KineticForge.Feedback
{
    /// <summary>
    /// "Forge in your room": a mixed reality mode that dissolves the stone hall into Quest passthrough, so the anvil,
    /// braziers, trough, wall and relic stand in the player's real room and the grounding ripples run across their real
    /// floor. Toggle by pinching with both hands for a moment (M in the Editor).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Needs Insight Passthrough enabled on the <c>OVRManager</c> and an <c>OVRPassthroughLayer</c> under the rig, both
    /// set up by the sandbox builder. In mixed reality, cameras clear to transparent so passthrough shows behind the
    /// scene, fog is switched off, and the hall's objects are hidden. Where passthrough is unavailable, such as the Linux
    /// Editor, the toggle previews the mode by hiding the hall only.
    /// </para>
    /// <para>The per-frame path does not allocate.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Feedback/Mixed Reality Mode")]
    public sealed class MixedRealityMode : MonoBehaviour
    {
        const string LogSource = nameof(MixedRealityMode);

        [SerializeField, Tooltip("Objects hidden in mixed reality: the hall around the forge.")]
        GameObject[] _hideInMixedReality = new GameObject[0];

        [SerializeField, Tooltip("Editor key that toggles the mode.")]
        KeyCode _editorToggleKey = KeyCode.M;

#if KF_META_XR_CORE
        [SerializeField, Range(0.3f, 3f), Tooltip("Seconds both hands must pinch to toggle.")]
        float _pinchHoldSeconds = 1f;

        float _bothPinchTime;
        bool _toggledThisHold;
#endif

        bool _active;
        bool _fogBefore;
        Camera[] _cameras = new Camera[0];
        Color[] _clearColors = new Color[0];
        CameraClearFlags[] _clearFlags = new CameraClearFlags[0];

#if KF_META_XR_CORE
        OVRPassthroughLayer _layer;
        OVRHand _left;
        OVRHand _right;
        float _nextResolveTime;
        bool _warned;
#endif

        /// <summary>True while mixed reality is on.</summary>
        public bool IsActive => _active;

        /// <summary>Turns mixed reality on or off.</summary>
        /// <param name="active">True for mixed reality.</param>
        public void SetActive(bool active)
        {
            if (_active == active)
            {
                return;
            }

            _active = active;
            for (int i = 0; i < _hideInMixedReality.Length; i++)
            {
                if (_hideInMixedReality[i] != null)
                {
                    _hideInMixedReality[i].SetActive(!active);
                }
            }

            if (active)
            {
                _fogBefore = RenderSettings.fog;
                RenderSettings.fog = false;
                CaptureCameras();
            }
            else
            {
                RenderSettings.fog = _fogBefore;
            }

            bool passthrough = false;
#if KF_META_XR_CORE
            if (_layer == null)
            {
                _layer = FindAnyObjectByType<OVRPassthroughLayer>(FindObjectsInactive.Include);
            }

            if (_layer != null && ForgePlatform.IsMetaRuntimeAvailable)
            {
                _layer.hidden = !active;
                passthrough = true;
            }
            else if (active && !_warned)
            {
                _warned = true;
                ForgePlatform.ReportMissing(ForgeLogChannel.Feedback, LogSource, "PASSTHROUGH_UNAVAILABLE",
                    "No passthrough layer or Meta runtime: mixed reality hides the hall only.", this);
            }
#endif
            for (int i = 0; i < _cameras.Length; i++)
            {
                if (_cameras[i] == null)
                {
                    continue;
                }

                if (active && passthrough)
                {
                    _cameras[i].clearFlags = CameraClearFlags.SolidColor;
                    _cameras[i].backgroundColor = new Color(0f, 0f, 0f, 0f);
                }
                else if (!active)
                {
                    _cameras[i].clearFlags = _clearFlags[i];
                    _cameras[i].backgroundColor = _clearColors[i];
                }
            }

            ForgeLog.Info(ForgeLogChannel.Feedback, LogSource, active ? "MIXED_REALITY_ON" : "MIXED_REALITY_OFF",
                active ? "Forging in your room." : "Back in the forge hall.", this);
        }

        void Update()
        {
            if (SimulatorKeyboard.WasPressedThisFrame(_editorToggleKey))
            {
                SetActive(!_active);
            }

#if KF_META_XR_CORE
            if (!ForgePlatform.IsMetaRuntimeAvailable)
            {
                return;
            }

            if ((_left == null || _right == null) && Time.unscaledTime >= _nextResolveTime)
            {
                _nextResolveTime = Time.unscaledTime + 2f;
                OVRSkeleton left = HandSkeletonReader.FindHandSkeleton(ForgeHand.Left);
                OVRSkeleton right = HandSkeletonReader.FindHandSkeleton(ForgeHand.Right);
                _left = left != null ? left.GetComponent<OVRHand>() : null;
                _right = right != null ? right.GetComponent<OVRHand>() : null;
            }

            bool both = _left != null && _right != null && _left.IsTracked && _right.IsTracked &&
                        _left.GetFingerIsPinching(OVRHand.HandFinger.Index) &&
                        _right.GetFingerIsPinching(OVRHand.HandFinger.Index);
            if (!both)
            {
                _bothPinchTime = 0f;
                _toggledThisHold = false;
                return;
            }

            _bothPinchTime += Time.unscaledDeltaTime;
            if (!_toggledThisHold && _bothPinchTime >= _pinchHoldSeconds)
            {
                _toggledThisHold = true;
                SetActive(!_active);
            }
#endif
        }

        void CaptureCameras()
        {
            _cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            _clearColors = new Color[_cameras.Length];
            _clearFlags = new CameraClearFlags[_cameras.Length];
            for (int i = 0; i < _cameras.Length; i++)
            {
                _clearColors[i] = _cameras[i].backgroundColor;
                _clearFlags[i] = _cameras[i].clearFlags;
            }
        }
    }
}
