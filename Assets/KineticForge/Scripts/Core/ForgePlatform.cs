using UnityEngine;

namespace KineticForge.Core
{
    /// <summary>
    /// Session-wide platform capabilities shared by every module.
    /// </summary>
    /// <remarks>
    /// When the Meta XR runtime cannot run in this session, for example in the Linux Editor where Meta ships no native
    /// OVRPlugin, the scene is not misconfigured: the hardware path simply does not exist. Components then report missing
    /// rig, hand or body objects through <see cref="ReportMissing"/> as information instead of warnings, and rely on their
    /// keyboard, mouse and head-pose fallbacks. The flag resets before every Play Mode session.
    /// </remarks>
    public static class ForgePlatform
    {
        /// <summary>False once a component has found that the Meta XR runtime cannot run in this session.</summary>
        public static bool IsMetaRuntimeAvailable { get; private set; } = true;

        /// <summary>Records that the Meta XR runtime cannot run in this session.</summary>
        public static void MarkMetaRuntimeUnavailable()
        {
            IsMetaRuntimeAvailable = false;
        }

        /// <summary>
        /// Logs a missing Meta component: a warning when the Meta runtime is available, because the scene is then
        /// misconfigured, and information otherwise, because the platform cannot provide it anyway.
        /// </summary>
        /// <param name="channel">Emitting module.</param>
        /// <param name="source">Emitting component.</param>
        /// <param name="code">Stable UPPER_SNAKE_CASE identifier for the condition.</param>
        /// <param name="message">Human-readable detail, including the fallback being used.</param>
        /// <param name="context">Optional object highlighted when the entry is clicked.</param>
        [HideInCallstack]
        public static void ReportMissing(ForgeLogChannel channel, string source, string code, string message, Object context)
        {
            if (IsMetaRuntimeAvailable)
            {
                ForgeLog.Warn(channel, source, code, message, context);
            }
            else
            {
                ForgeLog.Info(channel, source, code, message, context);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            IsMetaRuntimeAvailable = true;
        }
    }
}
