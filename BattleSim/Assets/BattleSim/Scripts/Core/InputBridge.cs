using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using ETouchPhase = UnityEngine.InputSystem.TouchPhase;
#endif

namespace BattleSim
{
    public struct Pointer
    {
        public int Id;
        public Vector2 Pos;
        public bool Began, Ended;
    }

    public enum K { W, A, S, D, Q, E, R, F, Up, Down, Left, Right, Space, Escape, Tab }

    /// <summary>
    /// Единый ввод для нового Input System и старого Input Manager —
    /// работает при любой настройке "Active Input Handling" в проекте.
    /// </summary>
    public static class InputBridge
    {
        public static readonly List<Pointer> Touches = new List<Pointer>();
        public static Vector2 MousePos;
        public static float Scroll;
        public static bool UseTouch => Application.isMobilePlatform;

        public static void Init()
        {
#if ENABLE_INPUT_SYSTEM
            if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
#endif
        }

        public static void Poll()
        {
            Touches.Clear();
#if ENABLE_INPUT_SYSTEM
            foreach (var t in ETouch.activeTouches)
            {
                var ph = t.phase;
                Touches.Add(new Pointer
                {
                    Id = t.touchId,
                    Pos = t.screenPosition,
                    Began = ph == ETouchPhase.Began,
                    Ended = ph == ETouchPhase.Ended || ph == ETouchPhase.Canceled
                });
            }
            var mouse = Mouse.current;
            if (mouse != null)
            {
                MousePos = mouse.position.ReadValue();
                float s = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(s) > 10f) s /= 120f; // на Windows одно деление колеса = 120
                Scroll = s;
            }
            else Scroll = 0f;
#elif ENABLE_LEGACY_INPUT_MANAGER
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                Touches.Add(new Pointer
                {
                    Id = t.fingerId,
                    Pos = t.position,
                    Began = t.phase == TouchPhase.Began,
                    Ended = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled
                });
            }
            MousePos = Input.mousePosition;
            Scroll = Input.mouseScrollDelta.y;
#endif
        }

        public static bool MouseHeld(int button)
        {
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            if (m == null) return false;
            return button == 0 ? m.leftButton.isPressed : button == 1 ? m.rightButton.isPressed : m.middleButton.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(button);
#else
            return false;
#endif
        }

        public static bool MouseDown(int button)
        {
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            if (m == null) return false;
            return button == 0 ? m.leftButton.wasPressedThisFrame : button == 1 ? m.rightButton.wasPressedThisFrame : m.middleButton.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(button);
#else
            return false;
#endif
        }

        public static bool MouseUp(int button)
        {
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            if (m == null) return false;
            return button == 0 ? m.leftButton.wasReleasedThisFrame : button == 1 ? m.rightButton.wasReleasedThisFrame : m.middleButton.wasReleasedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonUp(button);
#else
            return false;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        static Key Map(K k)
        {
            switch (k)
            {
                case K.W: return Key.W;
                case K.A: return Key.A;
                case K.S: return Key.S;
                case K.D: return Key.D;
                case K.Q: return Key.Q;
                case K.E: return Key.E;
                case K.R: return Key.R;
                case K.F: return Key.F;
                case K.Up: return Key.UpArrow;
                case K.Down: return Key.DownArrow;
                case K.Left: return Key.LeftArrow;
                case K.Right: return Key.RightArrow;
                case K.Space: return Key.Space;
                case K.Escape: return Key.Escape;
                default: return Key.Tab;
            }
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        static KeyCode Map(K k)
        {
            switch (k)
            {
                case K.W: return KeyCode.W;
                case K.A: return KeyCode.A;
                case K.S: return KeyCode.S;
                case K.D: return KeyCode.D;
                case K.Q: return KeyCode.Q;
                case K.E: return KeyCode.E;
                case K.R: return KeyCode.R;
                case K.F: return KeyCode.F;
                case K.Up: return KeyCode.UpArrow;
                case K.Down: return KeyCode.DownArrow;
                case K.Left: return KeyCode.LeftArrow;
                case K.Right: return KeyCode.RightArrow;
                case K.Space: return KeyCode.Space;
                case K.Escape: return KeyCode.Escape;
                default: return KeyCode.Tab;
            }
        }
#endif

        public static bool Held(K k)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb[Map(k)].isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(Map(k));
#else
            return false;
#endif
        }

        public static bool Pressed(K k)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb[Map(k)].wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(Map(k));
#else
            return false;
#endif
        }
    }
}
