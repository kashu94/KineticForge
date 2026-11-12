using System;
using KineticForge.Core;
using UnityEngine;

namespace KineticForge.Platform
{
    /// <summary>
    /// Keeps a Meta XR scene playable where the Meta runtime cannot load, such as the Linux Editor. Runs before every other
    /// script; if Meta's native OVRPlugin is unavailable it switches the <c>OVRCameraRig</c> off for this session and adds a
    /// plain fallback camera, so Kinetic Forge runs on its keyboard, mouse and head-pose fallbacks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Meta ships OVRPlugin for Android, Windows and macOS only. Without it, <c>OVRManager.Awake</c> throws a
    /// <c>DllNotFoundException</c> and leaves the rig half initialized. The guard probes the plugin version, which Meta
    /// reads defensively, and acts before any Meta component wakes up. Changes are made to the playing scene only, so the
    /// saved scene keeps its rig for device builds.
    /// </para>
    /// <para>
    /// Wherever the plugin loads, on a Quest or in a Windows or macOS Editor, the guard does nothing. Without the Meta XR
    /// Core SDK (KF_META_XR_CORE) there is nothing to guard and the component stays idle.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(-32000)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Platform/Meta Runtime Guard")]
    public sealed class MetaRuntimeGuard : MonoBehaviour
    {
        const string LogSource = nameof(MetaRuntimeGuard);

        [SerializeField, Min(0.5f), Tooltip("Height (m) of the fallback camera above the rig origin.")]
        float _fallbackEyeHeight = 1.6f;

        [SerializeField, Range(-60f, 60f), Tooltip("Downward pitch (degrees) of the fallback camera, to look at the work.")]
        float _fallbackPitch = 25f;

        /// <summary>True when the guard disabled the Meta rig for this session.</summary>
        public bool RigDisabled { get; private set; }

        /// <summary>Fallback camera height above the rig origin, in meters.</summary>
        public float FallbackEyeHeight => _fallbackEyeHeight;

        /// <summary>Fallback camera downward pitch, in degrees.</summary>
        public float FallbackPitch => _fallbackPitch;

        void Awake()
        {
#if KF_META_XR_CORE
            if (IsMetaRuntimeLoadable())
            {
                return;
            }

            // A one-time scene query at startup, before any Meta component has run Awake.
            OVRCameraRig[] rigs = FindObjectsByType<OVRCameraRig>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            OVRManager[] managers = FindObjectsByType<OVRManager>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (rigs.Length == 0 && managers.Length == 0)
            {
                ForgePlatform.MarkMetaRuntimeUnavailable();
                return;
            }

            Transform origin = rigs.Length > 0 ? rigs[0].transform : managers[0].transform;
            for (int i = 0; i < rigs.Length; i++)
            {
                rigs[i].gameObject.SetActive(false);
            }

            for (int i = 0; i < managers.Length; i++)
            {
                managers[i].gameObject.SetActive(false);
            }

            CreateFallbackCamera(origin);
            RigDisabled = true;
            ForgePlatform.MarkMetaRuntimeUnavailable();
            ForgeLog.Info(ForgeLogChannel.Platform, LogSource, "META_RUNTIME_UNAVAILABLE",
                "Meta's OVRPlugin is not available on " + Application.platform + ". The Meta rig is off for this session " +
                "and a fallback camera stands in; keyboard, mouse and head-pose fallbacks drive the game.", this);
#endif
        }

#if KF_META_XR_CORE
        static bool IsMetaRuntimeLoadable()
        {
            try
            {
                // Meta reads the version defensively and reports 0.0.0 when the native library cannot load.
                Version version = OVRPlugin.version;
                return version != null && version.Major > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        void CreateFallbackCamera(Transform origin)
        {
            GameObject cameraObject = new GameObject("Fallback Camera (Meta runtime unavailable)");
            cameraObject.tag = "MainCamera";
            Vector3 eye = origin.position + Vector3.up * _fallbackEyeHeight;
            Quaternion facing = Quaternion.Euler(_fallbackPitch, origin.eulerAngles.y, 0f);
            cameraObject.transform.SetPositionAndRotation(eye, facing);

            Camera fallback = cameraObject.AddComponent<Camera>();
            fallback.nearClipPlane = 0.05f;
            if (RenderSettings.skybox == null)
            {
                // A sky-less scene clears to its fog color, so distant geometry fades into it.
                fallback.clearFlags = CameraClearFlags.SolidColor;
                fallback.backgroundColor = RenderSettings.fogColor;
            }
            cameraObject.AddComponent<AudioListener>();
        }
#endif
    }
}
