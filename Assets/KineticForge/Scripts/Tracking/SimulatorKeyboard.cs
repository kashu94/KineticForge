using UnityEngine;
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM && KF_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace KineticForge.Tracking
{
    /// <summary>
    /// Editor-only keyboard access for simulator fallbacks, addressed with legacy <see cref="KeyCode"/> values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reads the Input System package when it is installed and active (ENABLE_INPUT_SYSTEM with KF_INPUT_SYSTEM), and the
    /// legacy Input Manager otherwise. This avoids the exception that <c>UnityEngine.Input</c> throws when a project uses
    /// the Input System only.
    /// </para>
    /// <para>
    /// Outside the Editor every query returns false, so fallback code can run unconditionally without
    /// <c>#if</c> blocks and without affecting device builds. Queries do not allocate.
    /// </para>
    /// <para>
    /// Supported keys: letters, top-row digits, F1 to F12, arrows, Space, Return, Tab, Escape, Backspace, and left/right
    /// Shift, Control and Alt. Other keys report false under the Input System.
    /// </para>
    /// </remarks>
    public static class SimulatorKeyboard
    {
        /// <summary>True when a keyboard can be read in the current context.</summary>
        public static bool IsAvailable
        {
            get
            {
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM && KF_INPUT_SYSTEM
                return Keyboard.current != null;
#elif UNITY_EDITOR && ENABLE_LEGACY_INPUT_MANAGER
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>Returns true while <paramref name="key"/> is held down.</summary>
        /// <param name="key">Key to query.</param>
        /// <returns>True while the key is held. Always false outside the Editor.</returns>
        public static bool IsHeld(KeyCode key)
        {
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM && KF_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            Key mapped = ToInputSystemKey(key);
            return mapped != Key.None && keyboard[mapped].isPressed;
#elif UNITY_EDITOR && ENABLE_LEGACY_INPUT_MANAGER
            return key != KeyCode.None && Input.GetKey(key);
#else
            return false;
#endif
        }

        /// <summary>Returns true during the frame <paramref name="key"/> was pressed.</summary>
        /// <param name="key">Key to query.</param>
        /// <returns>True on the press frame. Always false outside the Editor.</returns>
        public static bool WasPressedThisFrame(KeyCode key)
        {
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM && KF_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            Key mapped = ToInputSystemKey(key);
            return mapped != Key.None && keyboard[mapped].wasPressedThisFrame;
#elif UNITY_EDITOR && ENABLE_LEGACY_INPUT_MANAGER
            return key != KeyCode.None && Input.GetKeyDown(key);
#else
            return false;
#endif
        }

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM && KF_INPUT_SYSTEM
        static Key ToInputSystemKey(KeyCode code)
        {
            // Letters, top-row digits 1-9 and F1-F12 are contiguous in both enums.
            if (code >= KeyCode.A && code <= KeyCode.Z)
            {
                return Key.A + (code - KeyCode.A);
            }

            if (code >= KeyCode.Alpha1 && code <= KeyCode.Alpha9)
            {
                return Key.Digit1 + (code - KeyCode.Alpha1);
            }

            if (code >= KeyCode.F1 && code <= KeyCode.F12)
            {
                return Key.F1 + (code - KeyCode.F1);
            }

            switch (code)
            {
                case KeyCode.Alpha0: return Key.Digit0;
                case KeyCode.Space: return Key.Space;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.Tab: return Key.Tab;
                case KeyCode.Escape: return Key.Escape;
                case KeyCode.Backspace: return Key.Backspace;
                case KeyCode.UpArrow: return Key.UpArrow;
                case KeyCode.DownArrow: return Key.DownArrow;
                case KeyCode.LeftArrow: return Key.LeftArrow;
                case KeyCode.RightArrow: return Key.RightArrow;
                case KeyCode.LeftShift: return Key.LeftShift;
                case KeyCode.RightShift: return Key.RightShift;
                case KeyCode.LeftControl: return Key.LeftCtrl;
                case KeyCode.RightControl: return Key.RightCtrl;
                case KeyCode.LeftAlt: return Key.LeftAlt;
                case KeyCode.RightAlt: return Key.RightAlt;
                default: return Key.None;
            }
        }
#endif
    }
}
