using System;
using KineticForge.Core;
using UnityEngine;

namespace KineticForge.Platform
{
    /// <summary>
    /// Bridges Meta headset session events to the game: raises <see cref="ForgeEvents.PauseRequestChanged"/> when the
    /// Quest system menu takes input focus or the headset is taken off, and clears the request when the player returns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ForgeGameManager"/> combines these requests with application focus and manual pauses, then freezes the
    /// flow, time scale and audio as configured. Keeping the Meta dependency here leaves Core free of SDK types.
    /// </para>
    /// <para>
    /// Requires the Meta XR Core SDK (KF_META_XR_CORE). Without it the component stays idle and logs that once.
    /// Disabling the component clears every request it raised, so the game can never stay paused because of it.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Platform/XR Session Monitor")]
    public sealed class XRSessionMonitor : MonoBehaviour
    {
        const string LogSource = nameof(XRSessionMonitor);

        [SerializeField, Tooltip("Request a pause while the system menu or another overlay holds input focus.")]
        bool _pauseOnInputFocusLoss = true;

        [SerializeField, Tooltip("Request a pause while the headset is off the player's head.")]
        bool _pauseOnHeadsetRemoved = true;

        ForgePauseReason _activeRequests;

#if KF_META_XR_CORE
        Action _onInputFocusLost;
        Action _onInputFocusAcquired;
        Action _onHeadsetRemoved;
        Action _onHeadsetMounted;
        bool _subscribed;
#endif

        /// <summary>Whether input focus loss requests a pause.</summary>
        public bool PauseOnInputFocusLoss => _pauseOnInputFocusLoss;

        /// <summary>Whether removing the headset requests a pause.</summary>
        public bool PauseOnHeadsetRemoved => _pauseOnHeadsetRemoved;

        /// <summary>Pause reasons this monitor currently requests.</summary>
        public ForgePauseReason ActiveRequests => _activeRequests;

        void Awake()
        {
#if KF_META_XR_CORE
            _onInputFocusLost = () => SetRequest(ForgePauseReason.InputFocus, _pauseOnInputFocusLoss);
            _onInputFocusAcquired = () => SetRequest(ForgePauseReason.InputFocus, false);
            _onHeadsetRemoved = () => SetRequest(ForgePauseReason.HeadsetRemoved, _pauseOnHeadsetRemoved);
            _onHeadsetMounted = () => SetRequest(ForgePauseReason.HeadsetRemoved, false);
#endif
        }

        void OnEnable()
        {
#if KF_META_XR_CORE
            OVRManager.InputFocusLost += _onInputFocusLost;
            OVRManager.InputFocusAcquired += _onInputFocusAcquired;
            OVRManager.HMDUnmounted += _onHeadsetRemoved;
            OVRManager.HMDMounted += _onHeadsetMounted;
            _subscribed = true;
#endif
        }

        void Start()
        {
#if KF_META_XR_CORE
            // Events only report changes, so pick up a session that started without focus.
            try
            {
                if (OVRManager.isHmdPresent && !OVRManager.hasInputFocus)
                {
                    SetRequest(ForgePauseReason.InputFocus, _pauseOnInputFocusLoss);
                }
            }
            catch (Exception)
            {
                // Native plugin unavailable on this platform: there is no headset session to monitor.
            }
#else
            ForgeLog.Info(ForgeLogChannel.Platform, LogSource, "META_XR_CORE_MISSING",
                "Meta XR Core SDK not installed; headset focus and mount events are not monitored " +
                "(input focus: " + _pauseOnInputFocusLoss + ", headset removal: " + _pauseOnHeadsetRemoved + ").", this);
#endif
        }

        void OnDisable()
        {
#if KF_META_XR_CORE
            if (_subscribed)
            {
                OVRManager.InputFocusLost -= _onInputFocusLost;
                OVRManager.InputFocusAcquired -= _onInputFocusAcquired;
                OVRManager.HMDUnmounted -= _onHeadsetRemoved;
                OVRManager.HMDMounted -= _onHeadsetMounted;
                _subscribed = false;
            }
#endif

            SetRequest(ForgePauseReason.InputFocus, false);
            SetRequest(ForgePauseReason.HeadsetRemoved, false);
        }

        void SetRequest(ForgePauseReason reason, bool active)
        {
            bool isActive = (_activeRequests & reason) != 0;
            if (isActive == active)
            {
                return;
            }

            _activeRequests = active ? _activeRequests | reason : _activeRequests & ~reason;
            ForgeLog.Info(ForgeLogChannel.Platform, LogSource, active ? "PAUSE_REQUESTED" : "PAUSE_CLEARED",
                reason.ToString(), this);
            ForgeEvents.RaisePauseRequestChanged(new PauseRequestEvent(reason, active));
        }
    }
}
