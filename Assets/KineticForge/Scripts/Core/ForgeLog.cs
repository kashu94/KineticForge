using UnityEngine;

namespace KineticForge.Core
{
    /// <summary>
    /// Module that emitted a log entry. Mirrors the script folder layout.
    /// </summary>
    public enum ForgeLogChannel : byte
    {
        /// <summary>Game flow, events and settings.</summary>
        Core = 0,

        /// <summary>Body, head and finger tracking.</summary>
        Tracking = 1,

        /// <summary>Relic physics and stabilization.</summary>
        Physics = 2,

        /// <summary>Carving, chiselling and mesh deformation.</summary>
        Crafting = 3,

        /// <summary>Audio, visual and pseudo-haptic feedback.</summary>
        Feedback = 4,
    }

    /// <summary>
    /// Structured logging front end. Every entry has the form
    /// <c>[KineticForge][Channel][Source] CODE: message</c>, so console output can be filtered by module, component or code.
    /// </summary>
    /// <remarks>
    /// <para>Never log from a per-frame path: building the message allocates. Log state changes and one-time diagnostics only.</para>
    /// <para>
    /// <see cref="Info"/> calls are compiled out of release players, including the evaluation of their arguments.
    /// Warnings and errors are always kept.
    /// </para>
    /// </remarks>
    public static class ForgeLog
    {
        /// <summary>Logs an informational entry. Stripped from non-development player builds.</summary>
        /// <param name="channel">Emitting module.</param>
        /// <param name="source">Emitting component, usually <c>nameof(Type)</c>.</param>
        /// <param name="code">Stable UPPER_SNAKE_CASE identifier for the condition.</param>
        /// <param name="message">Human-readable detail.</param>
        /// <param name="context">Optional object highlighted when the entry is clicked.</param>
        [HideInCallstack]
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void Info(ForgeLogChannel channel, string source, string code, string message, Object context = null)
        {
            Debug.Log(Format(channel, source, code, message), context);
        }

        /// <summary>Logs a warning: a recoverable condition where the game degrades gracefully.</summary>
        /// <param name="channel">Emitting module.</param>
        /// <param name="source">Emitting component, usually <c>nameof(Type)</c>.</param>
        /// <param name="code">Stable UPPER_SNAKE_CASE identifier for the condition.</param>
        /// <param name="message">Human-readable detail, including the fallback being used.</param>
        /// <param name="context">Optional object highlighted when the entry is clicked.</param>
        [HideInCallstack]
        public static void Warn(ForgeLogChannel channel, string source, string code, string message, Object context = null)
        {
            Debug.LogWarning(Format(channel, source, code, message), context);
        }

        /// <summary>Logs an error: a condition that breaks a feature and needs fixing.</summary>
        /// <param name="channel">Emitting module.</param>
        /// <param name="source">Emitting component, usually <c>nameof(Type)</c>.</param>
        /// <param name="code">Stable UPPER_SNAKE_CASE identifier for the condition.</param>
        /// <param name="message">Human-readable detail.</param>
        /// <param name="context">Optional object highlighted when the entry is clicked.</param>
        [HideInCallstack]
        public static void Error(ForgeLogChannel channel, string source, string code, string message, Object context = null)
        {
            Debug.LogError(Format(channel, source, code, message), context);
        }

        /// <summary>Builds the structured log line. Allocates; do not call per frame.</summary>
        /// <param name="channel">Emitting module.</param>
        /// <param name="source">Emitting component.</param>
        /// <param name="code">Condition identifier.</param>
        /// <param name="message">Human-readable detail.</param>
        /// <returns>The formatted log line.</returns>
        public static string Format(ForgeLogChannel channel, string source, string code, string message)
        {
            return "[KineticForge][" + ChannelName(channel) + "][" + source + "] " + code + ": " + message;
        }

        static string ChannelName(ForgeLogChannel channel)
        {
            switch (channel)
            {
                case ForgeLogChannel.Core: return "Core";
                case ForgeLogChannel.Tracking: return "Tracking";
                case ForgeLogChannel.Physics: return "Physics";
                case ForgeLogChannel.Crafting: return "Crafting";
                case ForgeLogChannel.Feedback: return "Feedback";
                default: return "Unknown";
            }
        }
    }
}
