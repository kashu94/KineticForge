using UnityEngine;
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM && KF_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace KineticForge.Tracking
{
    /// <summary>Mouse buttons understood by <see cref="SimulatorPointer"/>.</summary>
    public enum SimulatorMouseButton : byte
    {
        /// <summary>Left button.</summary>
        Left = 0,

        /// <summary>Right button.</summary>
        Right = 1,

        /// <summary>Middle button.</summary>
        Middle = 2,
    }

    /// <summary>
    /// Editor-only mouse access for simulator fallbacks: movement in pixels and button state.
    /// </summary>
    /// <remarks>
    /// Reads the Input System package when it is installed and active, and the legacy Input Manager otherwise, exactly
    /// like <see cref="SimulatorKeyboard"/>. Outside the Editor every query reports no movement and no buttons, so fallback
    /// code can run unconditionally without affecting device builds. Queries do not allocate.
    /// </remarks>
    public static class SimulatorPointer
    {
#if UNITY_EDITOR && !(ENABLE_INPUT_SYSTEM && KF_INPUT_SYSTEM) && ENABLE_LEGACY_INPUT_MANAGER
        static int s_sampledFrame = -1;
        static Vector3 s_lastPosition;
        static Vector2 s_delta;
        static bool s_hasLastPosition;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_sampledFrame = -1;
            s_hasLastPosition = false;
            s_delta = Vector2.zero;
        }
#endif

        /// <summary>True when a mouse can be read in the current context.</summary>
        public static bool IsAvailable
        {
            get
            {
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM && KF_INPUT_SYSTEM
                return Mouse.current != null;
#elif UNITY_EDITOR && ENABLE_LEGACY_INPUT_MANAGER
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>Mouse movement this frame in screen pixels. Zero outside the Editor.</summary>
        public static Vector2 Delta
        {
            get
            {
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM && KF_INPUT_SYSTEM
                Mouse mouse = Mouse.current;
                return mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
#elif UNITY_EDITOR && ENABLE_LEGACY_INPUT_MANAGER
                int frame = Time.frameCount;
                if (frame != s_sampledFrame)
                {
                    s_sampledFrame = frame;
                    Vector3 position = Input.mousePosition;
                    s_delta = s_hasLastPosition ? (Vector2)(position - s_lastPosition) : Vector2.zero;
                    s_lastPosition = position;
                    s_hasLastPosition = true;
                }

                return s_delta;
#else
                return Vector2.zero;
#endif
            }
        }

        /// <summary>Mouse position in screen pixels, origin bottom left. Zero outside the Editor.</summary>
        public static Vector2 Position
        {
            get
            {
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM && KF_INPUT_SYSTEM
                Mouse mouse = Mouse.current;
                return mouse != null ? mouse.position.ReadValue() : Vector2.zero;
#elif UNITY_EDITOR && ENABLE_LEGACY_INPUT_MANAGER
                return Input.mousePosition;
#else
                return Vector2.zero;
#endif
            }
        }

        /// <summary>Returns true while <paramref name="button"/> is held down.</summary>
        /// <param name="button">Button to query.</param>
        /// <returns>True while held. Always false outside the Editor.</returns>
        public static bool IsHeld(SimulatorMouseButton button)
        {
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM && KF_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            return mouse != null && GetButton(mouse, button).isPressed;
#elif UNITY_EDITOR && ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton((int)button);
#else
            return false;
#endif
        }

        /// <summary>Returns true during the frame <paramref name="button"/> was pressed.</summary>
        /// <param name="button">Button to query.</param>
        /// <returns>True on the press frame. Always false outside the Editor.</returns>
        public static bool WasPressedThisFrame(SimulatorMouseButton button)
        {
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM && KF_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            return mouse != null && GetButton(mouse, button).wasPressedThisFrame;
#elif UNITY_EDITOR && ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown((int)button);
#else
            return false;
#endif
        }

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM && KF_INPUT_SYSTEM
        static UnityEngine.InputSystem.Controls.ButtonControl GetButton(Mouse mouse, SimulatorMouseButton button)
        {
            switch (button)
            {
                case SimulatorMouseButton.Right: return mouse.rightButton;
                case SimulatorMouseButton.Middle: return mouse.middleButton;
                default: return mouse.leftButton;
            }
        }
#endif
    }
}
