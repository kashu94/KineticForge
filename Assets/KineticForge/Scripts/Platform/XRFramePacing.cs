using System;
using System.Collections.Generic;
using System.Globalization;
using KineticForge.Core;
using UnityEngine;
using UnityEngine.XR;

namespace KineticForge.Platform
{
    /// <summary>Foveated rendering strength. Mirrors the Meta levels without exposing Meta types.</summary>
    public enum FoveationLevel : byte
    {
        /// <summary>Full resolution everywhere.</summary>
        Off = 0,

        /// <summary>Light peripheral reduction.</summary>
        Low = 1,

        /// <summary>Moderate peripheral reduction.</summary>
        Medium = 2,

        /// <summary>Strong peripheral reduction. A good default for Quest 3 at 90 Hz.</summary>
        High = 3,

        /// <summary>Strongest reduction, biased to the top of the view.</summary>
        HighTop = 4,
    }

    /// <summary>Suggested processor clock level. Mirrors the Meta levels without exposing Meta types.</summary>
    public enum ProcessorLevel : byte
    {
        /// <summary>Lowest clocks, longest battery life.</summary>
        PowerSavings = 0,

        /// <summary>Sustainable low clocks.</summary>
        SustainedLow = 1,

        /// <summary>Sustainable high clocks. The right choice for steady 90 Hz physics.</summary>
        SustainedHigh = 2,

        /// <summary>Short-term maximum clocks. The system may throttle back.</summary>
        Boost = 3,
    }

    /// <summary>
    /// Frame pacing for Quest: requests the display refresh rate, aligns the physics step to it, and sets foveated
    /// rendering and processor levels, so every frame the player sees carries a fresh, judder-free physics state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The physics step is matched to the refresh rate actually reported by the display (72, 80, 90 or 120 Hz on Quest 3),
    /// so a held relic advances exactly once per displayed frame instead of beating against Unity's default 50 Hz step.
    /// It follows rate changes, including ones the system makes, and the original step is restored on disable.
    /// </para>
    /// <para>
    /// Refresh rate, foveation and processor levels need the Meta XR Core SDK (KF_META_XR_CORE) and a connected headset.
    /// They are applied once the headset is present, retried once per second until then, and skipped without errors
    /// elsewhere. Physics alignment also works with any running Unity XR display. Nothing here allocates per frame.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Platform/XR Frame Pacing")]
    public sealed class XRFramePacing : MonoBehaviour
    {
        const string LogSource = nameof(XRFramePacing);
        const float PollInterval = 1f;
        const float RateTolerance = 0.5f;
        const float MinPhysicsRate = 30f;
        const float MaxPhysicsRate = 480f;

        [Header("Display")]
        [SerializeField, Range(60f, 120f), Tooltip("Preferred refresh rate (Hz). The highest supported rate at or below it is used. Quest 3 supports 72, 80, 90 and 120.")]
        float _preferredRefreshRate = 90f;

        [Header("Physics")]
        [SerializeField, Tooltip("Match the physics step to the display refresh rate so held relics move without judder.")]
        bool _alignPhysicsToDisplay = true;

        [SerializeField, Range(1, 4), Tooltip("Physics steps per displayed frame when aligned. 1 is enough with Rigidbody interpolation.")]
        int _physicsStepsPerFrame = 1;

        [Header("GPU and CPU (Meta Quest)")]
        [SerializeField, Tooltip("Fixed foveated rendering level, where supported.")]
        FoveationLevel _foveation = FoveationLevel.High;

        [SerializeField, Tooltip("Let the system lower foveation when GPU load allows it.")]
        bool _dynamicFoveation = true;

        [SerializeField, Tooltip("Suggested CPU level.")]
        ProcessorLevel _cpuLevel = ProcessorLevel.SustainedHigh;

        [SerializeField, Tooltip("Suggested GPU level.")]
        ProcessorLevel _gpuLevel = ProcessorLevel.SustainedHigh;

        [Header("Diagnostics")]
        [SerializeField, Tooltip("Log applied refresh, physics and foveation settings.")]
        bool _verboseLogging = true;

        readonly List<XRDisplaySubsystem> _displays = new List<XRDisplaySubsystem>(2);
        float _nextPollTime;
        bool _platformApplied;
        float _displayRate;
        float _originalFixedDeltaTime;
        bool _ownsFixedDeltaTime;

#if KF_META_XR_CORE
        Action<float, float> _onRefreshRateChanged;
        bool _subscribedToRefreshRate;
#endif

        /// <summary>Refresh rate currently reported by the display, in Hz. 0 until known.</summary>
        public float DisplayRefreshRate => _displayRate;

        /// <summary>Current physics step rate in Hz.</summary>
        public float PhysicsRate => Time.fixedDeltaTime > 0f ? 1f / Time.fixedDeltaTime : 0f;

        /// <summary>Preferred refresh rate in Hz.</summary>
        public float PreferredRefreshRate => _preferredRefreshRate;

        /// <summary>Requested fixed foveation level.</summary>
        public FoveationLevel Foveation => _foveation;

        /// <summary>Whether dynamic foveation is requested.</summary>
        public bool DynamicFoveation => _dynamicFoveation;

        /// <summary>Requested CPU level.</summary>
        public ProcessorLevel CpuLevel => _cpuLevel;

        /// <summary>Requested GPU level.</summary>
        public ProcessorLevel GpuLevel => _gpuLevel;

        /// <summary>True once the Meta platform settings have been applied, or skipped because they are unavailable.</summary>
        public bool PlatformSettingsApplied => _platformApplied;

        /// <summary>Re-applies every setting on the next frame, for example after changing values from code.</summary>
        public void Reapply()
        {
            _platformApplied = false;
            _displayRate = 0f;
            _nextPollTime = 0f;
        }

        void Awake()
        {
#if KF_META_XR_CORE
            _onRefreshRateChanged = HandleRefreshRateChanged;
#endif
        }

        void OnEnable()
        {
            _originalFixedDeltaTime = Time.fixedDeltaTime;
            Reapply();

#if KF_META_XR_CORE
            OVRManager.DisplayRefreshRateChanged += _onRefreshRateChanged;
            _subscribedToRefreshRate = true;
#endif
        }

        void OnDisable()
        {
#if KF_META_XR_CORE
            if (_subscribedToRefreshRate)
            {
                OVRManager.DisplayRefreshRateChanged -= _onRefreshRateChanged;
                _subscribedToRefreshRate = false;
            }
#endif

            if (_ownsFixedDeltaTime)
            {
                Time.fixedDeltaTime = _originalFixedDeltaTime;
                _ownsFixedDeltaTime = false;
            }
        }

        void Update()
        {
            if (Time.unscaledTime < _nextPollTime)
            {
                return;
            }

            _nextPollTime = Time.unscaledTime + PollInterval;

            if (!_platformApplied)
            {
                TryApplyPlatformSettings();
            }

            float rate = QueryDisplayRate();
            if (rate > 0f && Mathf.Abs(rate - _displayRate) > RateTolerance)
            {
                SetDisplayRate(rate);
            }
        }

        void OnValidate()
        {
            if (Application.isPlaying && isActiveAndEnabled)
            {
                Reapply();
            }
        }

        void SetDisplayRate(float rate)
        {
            _displayRate = rate;
            if (!_alignPhysicsToDisplay)
            {
                return;
            }

            float physicsRate = Mathf.Clamp(rate * _physicsStepsPerFrame, MinPhysicsRate, MaxPhysicsRate);
            Time.fixedDeltaTime = 1f / physicsRate;
            _ownsFixedDeltaTime = true;

            if (_verboseLogging)
            {
                ForgeLog.Info(ForgeLogChannel.Platform, LogSource, "PHYSICS_ALIGNED",
                    "Display " + rate.ToString("0.#", CultureInfo.InvariantCulture) + " Hz, physics " +
                    physicsRate.ToString("0.#", CultureInfo.InvariantCulture) + " Hz.", this);
            }
        }

        float QueryDisplayRate()
        {
#if KF_META_XR_CORE
            try
            {
                if (OVRManager.isHmdPresent && OVRManager.display != null)
                {
                    float frequency = OVRManager.display.displayFrequency;
                    if (frequency > 0f && ForgeMath.IsFinite(frequency))
                    {
                        return frequency;
                    }
                }
            }
            catch (Exception)
            {
                // Native plugin unavailable on this platform; fall through to the Unity XR display.
            }
#endif

            SubsystemManager.GetSubsystems(_displays);
            for (int i = 0; i < _displays.Count; i++)
            {
                XRDisplaySubsystem display = _displays[i];
                if (display != null && display.running && display.TryGetDisplayRefreshRate(out float rate) &&
                    rate > 0f && ForgeMath.IsFinite(rate))
                {
                    return rate;
                }
            }

            return 0f;
        }

        void TryApplyPlatformSettings()
        {
#if KF_META_XR_CORE
            try
            {
                if (!OVRManager.isHmdPresent || OVRManager.display == null)
                {
                    return;
                }

                ApplyRefreshRate(OVRManager.display);
                OVRManager.suggestedCpuPerfLevel = ToMeta(_cpuLevel);
                OVRManager.suggestedGpuPerfLevel = ToMeta(_gpuLevel);

                bool foveationSupported = OVRManager.fixedFoveatedRenderingSupported;
                if (foveationSupported)
                {
                    OVRManager.foveatedRenderingLevel = ToMeta(_foveation);
                    OVRManager.useDynamicFoveatedRendering = _dynamicFoveation;
                }

                _platformApplied = true;
                if (_verboseLogging)
                {
                    ForgeLog.Info(ForgeLogChannel.Platform, LogSource, "PLATFORM_APPLIED",
                        "CPU " + _cpuLevel + ", GPU " + _gpuLevel + ", foveation " +
                        (foveationSupported ? _foveation + (_dynamicFoveation ? " (dynamic)" : string.Empty) : "unsupported") +
                        ".", this);
                }
            }
            catch (Exception exception)
            {
                _platformApplied = true;
                ForgeLog.Warn(ForgeLogChannel.Platform, LogSource, "PLATFORM_UNAVAILABLE",
                    "Meta performance settings could not be applied (" + exception.GetType().Name + "). " +
                    "Physics alignment still runs.", this);
            }
#else
            _platformApplied = true;
            if (_verboseLogging)
            {
                ForgeLog.Info(ForgeLogChannel.Platform, LogSource, "META_XR_CORE_MISSING",
                    "Meta XR Core SDK not installed: refresh " +
                    _preferredRefreshRate.ToString("0", CultureInfo.InvariantCulture) + " Hz, foveation " + _foveation +
                    (_dynamicFoveation ? " (dynamic)" : string.Empty) + ", CPU " + _cpuLevel + " and GPU " + _gpuLevel +
                    " are not applied. Physics alignment still runs with any Unity XR display.", this);
            }
#endif
        }

#if KF_META_XR_CORE
        void ApplyRefreshRate(OVRDisplay display)
        {
            float[] available = display.displayFrequenciesAvailable;
            if (available == null || available.Length == 0)
            {
                return;
            }

            float chosen = ChooseRefreshRate(available, _preferredRefreshRate);
            if (chosen > 0f && Mathf.Abs(display.displayFrequency - chosen) > RateTolerance)
            {
                display.displayFrequency = chosen;
                if (_verboseLogging)
                {
                    ForgeLog.Info(ForgeLogChannel.Platform, LogSource, "REFRESH_REQUESTED",
                        "Requested " + chosen.ToString("0.#", CultureInfo.InvariantCulture) + " Hz.", this);
                }
            }
        }

        void HandleRefreshRateChanged(float fromRate, float toRate)
        {
            if (toRate > 0f && ForgeMath.IsFinite(toRate))
            {
                SetDisplayRate(toRate);
            }
        }

        static OVRManager.ProcessorPerformanceLevel ToMeta(ProcessorLevel level)
        {
            switch (level)
            {
                case ProcessorLevel.PowerSavings: return OVRManager.ProcessorPerformanceLevel.PowerSavings;
                case ProcessorLevel.SustainedLow: return OVRManager.ProcessorPerformanceLevel.SustainedLow;
                case ProcessorLevel.Boost: return OVRManager.ProcessorPerformanceLevel.Boost;
                default: return OVRManager.ProcessorPerformanceLevel.SustainedHigh;
            }
        }

        static OVRManager.FoveatedRenderingLevel ToMeta(FoveationLevel level)
        {
            switch (level)
            {
                case FoveationLevel.Off: return OVRManager.FoveatedRenderingLevel.Off;
                case FoveationLevel.Low: return OVRManager.FoveatedRenderingLevel.Low;
                case FoveationLevel.Medium: return OVRManager.FoveatedRenderingLevel.Medium;
                case FoveationLevel.HighTop: return OVRManager.FoveatedRenderingLevel.HighTop;
                default: return OVRManager.FoveatedRenderingLevel.High;
            }
        }
#endif

        /// <summary>
        /// Picks the highest rate at or below <paramref name="preferred"/>, or the lowest available rate when every rate is
        /// above it.
        /// </summary>
        /// <param name="available">Supported refresh rates in Hz, in any order.</param>
        /// <param name="preferred">Preferred refresh rate in Hz.</param>
        /// <returns>The chosen rate, or 0 when nothing valid is available.</returns>
        public static float ChooseRefreshRate(IReadOnlyList<float> available, float preferred)
        {
            float best = 0f;
            float lowest = float.MaxValue;
            if (available == null)
            {
                return 0f;
            }

            for (int i = 0; i < available.Count; i++)
            {
                float rate = available[i];
                if (!(rate > 0f) || !ForgeMath.IsFinite(rate))
                {
                    continue;
                }

                lowest = Mathf.Min(lowest, rate);
                if (rate <= preferred + RateTolerance && rate > best)
                {
                    best = rate;
                }
            }

            if (best > 0f)
            {
                return best;
            }

            return lowest < float.MaxValue ? lowest : 0f;
        }
    }
}
