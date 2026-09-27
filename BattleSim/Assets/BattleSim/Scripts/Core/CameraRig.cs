using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattleSim
{
    /// <summary>
    /// Камера-полководец.
    /// Телефон: 1 палец — двигать карту, щипок — зум, поворот двумя пальцами — вращение,
    /// два пальца вверх/вниз — наклон, короткое касание — поставить отряд.
    /// ПК: WASD/стрелки — движение, Q/E — поворот, R/F — наклон, колесо — зум,
    /// ПКМ — вращение, СКМ или ЛКМ с перетаскиванием — сдвиг, клик ЛКМ — поставить отряд.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public WorldGen World;
        public Func<Vector2, bool> IsOverUI;
        public Action<Vector3> OnTap;
        public bool Cinematic;
        public Func<Vector3?> CinematicFocus;

        public float Yaw = 0f, Pitch = 34f, Dist = 85f;
        public Vector3 Pivot = new Vector3(0f, 0f, -50f);

        float yawT, pitchT, distT;
        Vector3 pivotT;
        float pivotY;
        Camera cam;

        const float MinDist = 6f, MaxDist = 170f, MinPitch = 10f, MaxPitch = 86f;
        const float PivotLimit = WorldGen.FieldHalf + 25f;

        // Жесты
        class Finger { public Vector2 Start, Last; public float StartTime; public bool Moved, Blocked; }
        readonly Dictionary<int, Finger> fingers = new Dictionary<int, Finger>();
        bool multiTouchUsed;
        Vector2 mouseStart, mouseLast;
        float mouseStartTime;
        bool mouseMoved, mouseBlocked, mouseDownActive;

        float TapSlop => Mathf.Max(12f, Screen.dpi > 0 ? Screen.dpi * 0.08f : 12f);

        void Awake()
        {
            cam = GetComponent<Camera>();
            SnapToTargets();
        }

        public void SnapToTargets()
        {
            yawT = Yaw; pitchT = Pitch; distT = Dist; pivotT = Pivot;
        }

        public void LookAt(Vector3 pivot, float yaw, float pitch, float dist, bool instant)
        {
            pivotT = pivot; yawT = yaw; pitchT = pitch; distT = dist;
            if (instant) { Pivot = pivot; Yaw = yaw; Pitch = pitch; Dist = dist; }
        }

        void LateUpdate()
        {
            if (World == null) return;
            float dt = Time.unscaledDeltaTime;
            if (InputBridge.UseTouch) HandleTouch();
            else HandleMouseAndKeys(dt);

            if (Cinematic)
            {
                yawT += 7f * dt;
                var focus = CinematicFocus != null ? CinematicFocus() : null;
                if (focus.HasValue) pivotT = Vector3.Lerp(pivotT, focus.Value, 1f - Mathf.Exp(-0.8f * dt));
            }

            pitchT = Mathf.Clamp(pitchT, MinPitch, MaxPitch);
            distT = Mathf.Clamp(distT, MinDist, MaxDist);
            pivotT.x = Mathf.Clamp(pivotT.x, -PivotLimit, PivotLimit);
            pivotT.z = Mathf.Clamp(pivotT.z, -PivotLimit, PivotLimit);

            float k = 1f - Mathf.Exp(-12f * dt);
            Yaw = Mathf.LerpAngle(Yaw, yawT, k);
            Pitch = Mathf.Lerp(Pitch, pitchT, k);
            Dist = Mathf.Lerp(Dist, distT, k);
            Pivot = Vector3.Lerp(Pivot, pivotT, k);
            float groundY = World.HeightAt(Pivot.x, Pivot.z);
            pivotY = Mathf.Lerp(pivotY, groundY, 1f - Mathf.Exp(-6f * dt));

            Vector3 target = new Vector3(Pivot.x, pivotY + 1f, Pivot.z);
            Quaternion rot = Quaternion.Euler(Pitch, Yaw, 0f);
            Vector3 pos = target - rot * Vector3.forward * Dist;
            float minY = World.HeightAt(pos.x, pos.z) + 1.5f;
            if (pos.y < minY) pos.y = minY;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos));
        }

        // ---------------------------------------------------------------- ПК

        void HandleMouseAndKeys(float dt)
        {
            Vector2 mp = InputBridge.MousePos;
            bool overUI = IsOverUI != null && IsOverUI(mp);

            Vector3 fwd = Quaternion.Euler(0f, yawT, 0f) * Vector3.forward;
            Vector3 right = Quaternion.Euler(0f, yawT, 0f) * Vector3.right;
            Vector2 move = Vector2.zero;
            if (InputBridge.Held(K.W) || InputBridge.Held(K.Up)) move.y += 1f;
            if (InputBridge.Held(K.S) || InputBridge.Held(K.Down)) move.y -= 1f;
            if (InputBridge.Held(K.D) || InputBridge.Held(K.Right)) move.x += 1f;
            if (InputBridge.Held(K.A) || InputBridge.Held(K.Left)) move.x -= 1f;
            if (move != Vector2.zero) { pivotT += (fwd * move.y + right * move.x) * (distT * 0.9f + 10f) * dt; Cinematic = false; }
            if (InputBridge.Held(K.Q)) yawT += 90f * dt;
            if (InputBridge.Held(K.E)) yawT -= 90f * dt;
            if (InputBridge.Held(K.R)) pitchT += 45f * dt;
            if (InputBridge.Held(K.F)) pitchT -= 45f * dt;

            if (!overUI && Mathf.Abs(InputBridge.Scroll) > 0.01f)
                distT *= Mathf.Pow(0.88f, InputBridge.Scroll);

            // Вращение правой кнопкой
            if (InputBridge.MouseHeld(1) && !InputBridge.MouseDown(1))
            {
                Vector2 d = mp - mouseLast;
                yawT += d.x * 0.25f;
                pitchT -= d.y * 0.2f;
            }

            // Левая: клик = действие, перетаскивание = сдвиг карты. Средняя: сдвиг.
            if (InputBridge.MouseDown(0))
            {
                mouseDownActive = true;
                mouseBlocked = overUI;
                mouseMoved = false;
                mouseStart = mp;
                mouseStartTime = Time.unscaledTime;
            }
            if (mouseDownActive && InputBridge.MouseHeld(0) && !mouseBlocked)
            {
                if (!mouseMoved && (mp - mouseStart).magnitude > TapSlop) mouseMoved = true;
                if (mouseMoved) DragPan(mouseLast, mp);
            }
            if (InputBridge.MouseHeld(2) && !InputBridge.MouseDown(2) && !overUI) DragPan(mouseLast, mp);
            if (mouseDownActive && InputBridge.MouseUp(0))
            {
                mouseDownActive = false;
                if (!mouseBlocked && !mouseMoved) Tap(mp);
            }
            mouseLast = mp;
        }

        // ---------------------------------------------------------------- телефон

        void HandleTouch()
        {
            var touches = InputBridge.Touches;
            foreach (var t in touches)
            {
                if (t.Began || !fingers.ContainsKey(t.Id))
                {
                    fingers[t.Id] = new Finger
                    {
                        Start = t.Pos, Last = t.Pos, StartTime = Time.unscaledTime,
                        Blocked = IsOverUI != null && IsOverUI(t.Pos)
                    };
                }
            }

            // Активные пальцы, которые не начались на кнопках интерфейса
            var live = new List<(Finger f, Vector2 pos)>(2);
            foreach (var t in touches)
            {
                var f = fingers[t.Id];
                if (!f.Blocked && !t.Ended) live.Add((f, t.Pos));
            }

            if (live.Count >= 2)
            {
                multiTouchUsed = true;
                Cinematic = false;
                var (f0, p0) = live[0];
                var (f1, p1) = live[1];
                Vector2 prevA = f0.Last, prevB = f1.Last;
                float prevSpan = (prevB - prevA).magnitude, span = (p1 - p0).magnitude;
                if (prevSpan > 20f && span > 20f) distT *= prevSpan / span;
                float prevAng = Mathf.Atan2(prevB.y - prevA.y, prevB.x - prevA.x) * Mathf.Rad2Deg;
                float ang = Mathf.Atan2(p1.y - p0.y, p1.x - p0.x) * Mathf.Rad2Deg;
                yawT -= Mathf.DeltaAngle(prevAng, ang);
                Vector2 d0 = p0 - prevA, d1 = p1 - prevB;
                if (d0.y * d1.y > 0f) pitchT -= (d0.y + d1.y) * 0.5f * 0.15f;
                f0.Moved = f1.Moved = true;
            }
            else if (live.Count == 1)
            {
                var (f, p) = live[0];
                if (!f.Moved && (p - f.Start).magnitude > TapSlop) f.Moved = true;
                if (f.Moved && !multiTouchUsed) { DragPan(f.Last, p); Cinematic = false; }
            }

            foreach (var t in touches)
            {
                var f = fingers[t.Id];
                if (t.Ended)
                {
                    if (!f.Blocked && !f.Moved && !multiTouchUsed && touches.Count == 1 && Time.unscaledTime - f.StartTime < 0.6f)
                        Tap(t.Pos);
                    fingers.Remove(t.Id);
                }
                else f.Last = t.Pos;
            }
            if (touches.Count == 0)
            {
                fingers.Clear();
                multiTouchUsed = false;
            }
        }

        // ---------------------------------------------------------------- общее

        /// <summary>"Схватить землю" и протащить её за пальцем/мышью.</summary>
        void DragPan(Vector2 from, Vector2 to)
        {
            if (from == to) return;
            var plane = new Plane(Vector3.up, new Vector3(0f, pivotY, 0f));
            Ray ra = cam.ScreenPointToRay(from), rb = cam.ScreenPointToRay(to);
            if (!plane.Raycast(ra, out float ta) || !plane.Raycast(rb, out float tb)) return;
            Vector3 delta = ra.GetPoint(ta) - rb.GetPoint(tb);
            delta.y = 0f;
            delta = Vector3.ClampMagnitude(delta, distT * 0.5f + 5f);
            pivotT += delta;
            Pivot += delta; // без запаздывания, чтобы земля "прилипала" к пальцу
        }

        void Tap(Vector2 screen)
        {
            if (OnTap == null) return;
            if (World.Raycast(cam.ScreenPointToRay(screen), out Vector3 hit)) OnTap(hit);
        }
    }
}
