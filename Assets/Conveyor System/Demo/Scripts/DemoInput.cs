using UnityEngine;
#if ENABLE_INPUT_SYSTEM && CONVEYOR_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ConveyorSystem.Demo
{
    /// <summary>
    /// Keys used by the demo scripts.
    /// </summary>
    public enum DemoKey
    {
        Alpha0, Alpha1, Alpha2, Alpha3, Alpha4, Alpha5, Alpha6, Alpha7, Alpha8, Alpha9,
        R, T, P, C, Escape, W, A, S, D, F, N, B, Enter, LeftArrow, RightArrow, Space, M
    }

    /// <summary>
    /// Minimal input wrapper for the demo scripts. Uses the Input System when it is active,
    /// and the legacy Input Manager otherwise.
    /// </summary>
    public static class DemoInput
    {
#if ENABLE_INPUT_SYSTEM && CONVEYOR_INPUT_SYSTEM
        private static readonly Key[] Keys =
        {
            Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9,
            Key.R, Key.T, Key.P, Key.C, Key.Escape, Key.W, Key.A, Key.S, Key.D, Key.F, Key.N, Key.B, Key.Enter, Key.LeftArrow, Key.RightArrow, Key.Space, Key.M
        };
#else
        private static readonly KeyCode[] Keys =
        {
            KeyCode.Alpha0, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9,
            KeyCode.R, KeyCode.T, KeyCode.P, KeyCode.C, KeyCode.Escape, KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.F, KeyCode.N, KeyCode.B, KeyCode.Return, KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.Space, KeyCode.M
        };
#endif

        /// <summary>
        /// True on the frame the key was pressed.
        /// </summary>
        public static bool GetKeyDown(DemoKey key)
        {
#if ENABLE_INPUT_SYSTEM && CONVEYOR_INPUT_SYSTEM
            var k = Keyboard.current;
            return k != null && k[Keys[(int)key]].wasPressedThisFrame;
#else
            return Input.GetKeyDown(Keys[(int)key]);
#endif
        }

        /// <summary>
        /// True while the key is held.
        /// </summary>
        public static bool GetKey(DemoKey key)
        {
#if ENABLE_INPUT_SYSTEM && CONVEYOR_INPUT_SYSTEM
            var k = Keyboard.current;
            return k != null && k[Keys[(int)key]].isPressed;
#else
            return Input.GetKey(Keys[(int)key]);
#endif
        }

        /// <summary>
        /// Mouse position in screen pixels.
        /// </summary>
        public static Vector2 PointerPosition
        {
            get
            {
#if ENABLE_INPUT_SYSTEM && CONVEYOR_INPUT_SYSTEM
                var m = Mouse.current;
                return m == null ? Vector2.zero : m.position.ReadValue();
#else
                return Input.mousePosition;
#endif
            }
        }

        /// <summary>
        /// True on the frame the left mouse button was pressed.
        /// </summary>
        public static bool LeftPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM && CONVEYOR_INPUT_SYSTEM
                var m = Mouse.current;
                return m != null && m.leftButton.wasPressedThisFrame;
#else
                return Input.GetMouseButtonDown(0);
#endif
            }
        }

        /// <summary>
        /// True while the left mouse button is held.
        /// </summary>
        public static bool LeftHeld
        {
            get
            {
#if ENABLE_INPUT_SYSTEM && CONVEYOR_INPUT_SYSTEM
                var m = Mouse.current;
                return m != null && m.leftButton.isPressed;
#else
                return Input.GetMouseButton(0);
#endif
            }
        }

        /// <summary>
        /// True while the right mouse button is held.
        /// </summary>
        public static bool RightHeld
        {
            get
            {
#if ENABLE_INPUT_SYSTEM && CONVEYOR_INPUT_SYSTEM
                var m = Mouse.current;
                return m != null && m.rightButton.isPressed;
#else
                return Input.GetMouseButton(1);
#endif
            }
        }

        /// <summary>
        /// WASD movement: x = right, y = forward, each -1..1.
        /// </summary>
        public static Vector2 Pan
        {
            get
            {
                float x = (GetKey(DemoKey.D) ? 1f : 0f) - (GetKey(DemoKey.A) ? 1f : 0f);
                float y = (GetKey(DemoKey.W) ? 1f : 0f) - (GetKey(DemoKey.S) ? 1f : 0f);
                return new Vector2(x, y);
            }
        }

        /// <summary>
        /// Horizontal orbit input: -1 (Q / Left Arrow) to +1 (E / Right Arrow).
        /// </summary>
        public static float Orbit
        {
            get
            {
#if ENABLE_INPUT_SYSTEM && CONVEYOR_INPUT_SYSTEM
                var k = Keyboard.current;
                if (k == null) return 0f;
                float v = 0f;
                if (k.qKey.isPressed || k.leftArrowKey.isPressed) v -= 1f;
                if (k.eKey.isPressed || k.rightArrowKey.isPressed) v += 1f;
                return v;
#else
                float v = 0f;
                if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftArrow)) v -= 1f;
                if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.RightArrow)) v += 1f;
                return v;
#endif
            }
        }

        /// <summary>
        /// Vertical tilt input: -1 (Down Arrow) to +1 (Up Arrow).
        /// </summary>
        public static float Tilt
        {
            get
            {
#if ENABLE_INPUT_SYSTEM && CONVEYOR_INPUT_SYSTEM
                var k = Keyboard.current;
                if (k == null) return 0f;
                float v = 0f;
                if (k.downArrowKey.isPressed) v -= 1f;
                if (k.upArrowKey.isPressed) v += 1f;
                return v;
#else
                float v = 0f;
                if (Input.GetKey(KeyCode.DownArrow)) v -= 1f;
                if (Input.GetKey(KeyCode.UpArrow)) v += 1f;
                return v;
#endif
            }
        }

        /// <summary>
        /// Mouse wheel movement this frame (positive = zoom in).
        /// </summary>
        public static float Scroll
        {
            get
            {
#if ENABLE_INPUT_SYSTEM && CONVEYOR_INPUT_SYSTEM
                var m = Mouse.current;
                return m == null ? 0f : Mathf.Clamp(m.scroll.ReadValue().y, -1f, 1f);
#else
                return Input.mouseScrollDelta.y;
#endif
            }
        }

        /// <summary>
        /// True on the frame the Space key was pressed.
        /// </summary>
        public static bool TogglePressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM && CONVEYOR_INPUT_SYSTEM
                var k = Keyboard.current;
                return k != null && k.spaceKey.wasPressedThisFrame;
#else
                return Input.GetKeyDown(KeyCode.Space);
#endif
            }
        }
    }
}
