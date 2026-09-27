using System;
using System.Collections.Generic;
using BattleSim.Core;
using UnityEngine;

namespace BattleSim
{
    /// <summary>
    /// Камера-полководец. Считает в координатах ядра (как веб-версия), Unity получает готовую позицию.
    /// Телефон: 1 палец — двигать карту, щипок — зум, поворот двумя пальцами — вращение,
    /// два пальца вверх/вниз — наклон, короткое касание — поставить отряд.
    /// ПК: WASD/стрелки — движение, Q/E — поворот, R/F — наклон, колесо — зум,
    /// ПКМ — вращение, ЛКМ с перетаскиванием — сдвиг, клик — поставить отряд.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public World World;
        public Func<Vector2, bool> IsOverUI;
        public Action<Vector2> OnTap, OnHover;
        public Func<V3?> Focus;
        public bool Cinematic;

        public float Yaw, Pitch = 34 * M.DEG, Dist = 85, GroundY;
        public V3 Target = new V3(0, 0, -48);
        float gYaw, gPitch = 34 * M.DEG, gDist = 85;
        V3 gTarget = new V3(0, 0, -48);
        Camera cam;

        class Finger { public Vector2 Start, Last; public float T; public bool Moved, Blocked; }
        readonly Dictionary<int, Finger> fingers = new Dictionary<int, Finger>();
        bool multi;
        Vector2 mouseStart, mouseLast;
        float mouseT;
        bool mouseMoved, mouseBlocked, mouseDown;

        const float MinPitch = 10 * M.DEG, MaxPitch = 86 * M.DEG;
        float Slop(bool touch) => touch ? Mathf.Max(14f, Screen.dpi > 0 ? Screen.dpi * 0.08f : 14f) : 6f;

        void Awake() { cam = GetComponent<Camera>(); }

        public void LookAt(float x, float z, float yaw, float pitch, float dist, bool instant)
        {
            gYaw = yaw; gPitch = pitch; gDist = dist; gTarget = new V3(x, 0, z);
            if (instant) { Yaw = yaw; Pitch = pitch; Dist = dist; Target = gTarget; }
        }

        V3 RayO(Vector2 screen, out V3 dir)
        {
            var r = cam.ScreenPointToRay(screen);
            dir = Conv.C(r.direction);
            return Conv.C(r.origin);
        }

        /// <summary>Точка на земле под экранной точкой (в координатах ядра).</summary>
        public V3? GroundPoint(Vector2 screen)
        {
            if (World == null) return null;
            var o = RayO(screen, out var d);
            return World.Raycast(o, d, out var hit) ? hit : (V3?)null;
        }

        /// <summary>«Схватить землю» и протащить её за пальцем или мышью.</summary>
        void DragPan(Vector2 from, Vector2 to)
        {
            if (from == to) return;
            var ao = RayO(from, out var ad);
            var bo = RayO(to, out var bd);
            if (Mathf.Abs(ad.y) < 1e-4f || Mathf.Abs(bd.y) < 1e-4f) return;
            float ta = (GroundY - ao.y) / ad.y, tb = (GroundY - bo.y) / bd.y;
            if (ta < 0 || tb < 0) return;
            var a = ao + ad * ta;
            var b = bo + bd * tb;
            float dx = a.x - b.x, dz = a.z - b.z, l = M.Hypot(dx, dz), max = Dist * 0.5f + 5;
            if (l > max) { dx *= max / l; dz *= max / l; }
            gTarget.x += dx; gTarget.z += dz;
            Target.x += dx; Target.z += dz;
            Cinematic = false;
        }

        void LateUpdate()
        {
            if (World == null || cam == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (InputBridge.UseTouch) HandleTouch(); else HandleMouse(dt);
            UpdateCamera(dt);
        }

        void HandleMouse(float dt)
        {
            Vector2 mp = InputBridge.MousePos;
            bool overUI = IsOverUI != null && IsOverUI(mp);
            float fx = Mathf.Sin(gYaw), fz = Mathf.Cos(gYaw);
            float mx = 0, mz = 0;
            if (InputBridge.Held(K.W) || InputBridge.Held(K.Up)) mz += 1;
            if (InputBridge.Held(K.S) || InputBridge.Held(K.Down)) mz -= 1;
            if (InputBridge.Held(K.D) || InputBridge.Held(K.Right)) mx += 1;
            if (InputBridge.Held(K.A) || InputBridge.Held(K.Left)) mx -= 1;
            if (mx != 0 || mz != 0)
            {
                float sp = (gDist * 0.9f + 10) * dt;
                // вправо = (-cos, 0, sin) для камеры, смотрящей вдоль (sin, 0, cos)
                gTarget.x += (fx * mz - fz * mx) * sp;
                gTarget.z += (fz * mz + fx * mx) * sp;
                Cinematic = false;
            }
            if (InputBridge.Held(K.Q)) gYaw += 1.6f * dt;
            if (InputBridge.Held(K.E)) gYaw -= 1.6f * dt;
            if (InputBridge.Held(K.R)) gPitch = M.Clamp(gPitch + 0.8f * dt, MinPitch, MaxPitch);
            if (InputBridge.Held(K.F)) gPitch = M.Clamp(gPitch - 0.8f * dt, MinPitch, MaxPitch);
            if (!overUI && Mathf.Abs(InputBridge.Scroll) > 0.01f) gDist = M.Clamp(gDist * Mathf.Pow(1.16f, -InputBridge.Scroll), 6, 170);

            // Вращение правой кнопкой
            if (InputBridge.MouseHeld(1) && !InputBridge.MouseDown(1))
            {
                var d = mp - mouseLast;
                gYaw -= d.x * 0.005f;
                gPitch = M.Clamp(gPitch - d.y * 0.004f, MinPitch, MaxPitch);
                if (d.sqrMagnitude > 0) Cinematic = false;
            }
            // Левая: клик = действие, перетаскивание = сдвиг карты. Средняя: сдвиг.
            if (InputBridge.MouseDown(0))
            {
                mouseDown = true; mouseBlocked = overUI; mouseMoved = false;
                mouseStart = mp; mouseT = Time.unscaledTime;
            }
            if (mouseDown && InputBridge.MouseHeld(0) && !mouseBlocked)
            {
                if (!mouseMoved && (mp - mouseStart).magnitude > Slop(false)) mouseMoved = true;
                if (mouseMoved) DragPan(mouseLast, mp);
            }
            if (InputBridge.MouseHeld(2) && !InputBridge.MouseDown(2) && !overUI) DragPan(mouseLast, mp);
            if (mouseDown && InputBridge.MouseUp(0))
            {
                mouseDown = false;
                if (!mouseBlocked && !mouseMoved && Time.unscaledTime - mouseT < 0.65f) OnTap?.Invoke(mp);
            }
            if (!overUI && !InputBridge.MouseHeld(0) && !InputBridge.MouseHeld(1)) OnHover?.Invoke(mp);
            mouseLast = mp;
        }

        void HandleTouch()
        {
            var touches = InputBridge.Touches;
            foreach (var t in touches)
                if (t.Began || !fingers.ContainsKey(t.Id))
                    fingers[t.Id] = new Finger { Start = t.Pos, Last = t.Pos, T = Time.unscaledTime, Blocked = IsOverUI != null && IsOverUI(t.Pos) };
            var live = new List<(Finger f, Vector2 p)>(2);
            foreach (var t in touches) { var f = fingers[t.Id]; if (!f.Blocked && !t.Ended) live.Add((f, t.Pos)); }

            if (live.Count >= 2)
            {
                multi = true;
                Cinematic = false;
                var (f0, p0) = live[0];
                var (f1, p1) = live[1];
                Vector2 a = f0.Last, b = f1.Last;
                float d0 = (b - a).magnitude, d1 = (p1 - p0).magnitude;
                if (d0 > 20 && d1 > 20) gDist = M.Clamp(gDist * d0 / d1, 6, 170);
                float a0 = Mathf.Atan2(b.y - a.y, b.x - a.x), a1 = Mathf.Atan2(p1.y - p0.y, p1.x - p0.x);
                gYaw -= M.WrapAngle(a1 - a0);
                float dyA = p0.y - a.y, dyB = p1.y - b.y;
                if (dyA * dyB > 0) gPitch = M.Clamp(gPitch - (dyA + dyB) * 0.5f * 0.004f, MinPitch, MaxPitch);
                f0.Moved = f1.Moved = true;
            }
            else if (live.Count == 1)
            {
                var (f, p) = live[0];
                if (!f.Moved && (p - f.Start).magnitude > Slop(true)) f.Moved = true;
                if (f.Moved && !multi) DragPan(f.Last, p);
            }
            foreach (var t in touches)
            {
                var f = fingers[t.Id];
                if (t.Ended)
                {
                    if (!f.Blocked && !f.Moved && !multi && touches.Count == 1 && Time.unscaledTime - f.T < 0.65f) OnTap?.Invoke(t.Pos);
                    fingers.Remove(t.Id);
                }
                else f.Last = t.Pos;
            }
            if (touches.Count == 0) { fingers.Clear(); multi = false; }
        }

        void UpdateCamera(float dt)
        {
            if (Cinematic)
            {
                gYaw += 0.12f * dt;
                var focus = Focus?.Invoke();
                if (focus.HasValue)
                {
                    float k = 1 - Mathf.Exp(-0.8f * dt);
                    gTarget.x += (focus.Value.x - gTarget.x) * k;
                    gTarget.z += (focus.Value.z - gTarget.z) * k;
                }
            }
            float lim = World.Field + 25;
            gTarget.x = M.Clamp(gTarget.x, -lim, lim); gTarget.z = M.Clamp(gTarget.z, -lim, lim);

            float s = 1 - Mathf.Exp(-12 * dt);
            Yaw += M.WrapAngle(gYaw - Yaw) * s;
            Pitch += (gPitch - Pitch) * s;
            Dist += (gDist - Dist) * s;
            Target.x += (gTarget.x - Target.x) * s;
            Target.z += (gTarget.z - Target.z) * s;
            GroundY += (World.GroundAt(Target.x, Target.z) - GroundY) * (1 - Mathf.Exp(-6 * dt));

            float cp = Mathf.Cos(Pitch);
            var look = new V3(Target.x, GroundY + 1, Target.z);
            var pos = new V3(look.x - Mathf.Sin(Yaw) * cp * Dist, look.y + Mathf.Sin(Pitch) * Dist, look.z - Mathf.Cos(Yaw) * cp * Dist);
            float minY = World.GroundAt(pos.x, pos.z) + 1.5f;
            if (pos.y < minY) pos.y = minY;
            var pu = Conv.U(pos);
            transform.SetPositionAndRotation(pu, Quaternion.LookRotation(Conv.U(look) - pu, Vector3.up));
        }
    }
}
