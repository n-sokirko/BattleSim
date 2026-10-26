using BattleSim.Core;
using UnityEngine;

namespace BattleSim
{
    /// <summary>
    /// Управление героем пальцами: слева — плавающий джойстик (ведёт героя), справа — четыре круглые кнопки способностей.
    /// Работает с несколькими касаниями сразу (идти и жать способность); на ПК то же мышью, плюс WASD и клавиши 1–4.
    /// Координаты экрана — в пикселях, начало внизу слева (как у касаний).
    /// </summary>
    public sealed class HeroPad
    {
        public static readonly string[] Short = { "Клич", "Удар", "Вызов", "Стена" };

        /// <summary>Ход героя в осях экрана: x — вправо, y — вверх, длина 0..1.</summary>
        public Vector2 Stick;
        /// <summary>Где стоит основание джойстика и где палец (экранные пиксели, начало внизу слева).</summary>
        public Vector2 Base, Knob;
        public bool Active;
        int stickId = int.MinValue;
        readonly bool[] pressed = new bool[4];
        readonly float[] flash = new float[4];

        /// <summary>Единица масштаба — как у Hud (под экран телефона).</summary>
        public static float Unit()
        {
            float dpi = Screen.dpi > 0 ? Screen.dpi / 200f : 0f;
            return Mathf.Clamp(Mathf.Max(Screen.height / 760f, dpi), 0.75f, 4f);
        }

        public float Radius => 40f * Unit();           // кнопка способности
        public float Reach => 62f * Unit();            // ход джойстика

        /// <summary>Центр кнопки способности i: сетка 2×2 в правом нижнем углу (клич и удар — в нижнем ряду).</summary>
        public Vector2 ButtonCenter(int i, Rect safeBL)
        {
            float u = Unit(), r = Radius, gap = 10f * u;
            int col = i % 2, row = i / 2;
            float x = safeBL.xMax - 14f * u - r - (1 - col) * (2 * r + gap);
            float y = safeBL.y + 16f * u + r + row * (2 * r + gap);
            return new Vector2(x, y);
        }

        /// <summary>Сколько места справа внизу занимают кнопки (остальной интерфейс держится левее).</summary>
        public float RightReserve => (4f * Radius + 10f * Unit() + 28f * Unit());

        /// <summary>Зона, где начинается джойстик: нижняя левая треть экрана.</summary>
        public Rect Zone(Rect safeBL) => new Rect(safeBL.x, safeBL.y, safeBL.width * 0.34f, safeBL.height * 0.62f);

        public Rect SafeBL()
        {
            var sa = Screen.safeArea;
            return new Rect(sa.x, sa.y, sa.width, sa.height);
        }

        /// <summary>Касание в зоне джойстика или на кнопке способности — камере и приказам отрядам не отдавать.</summary>
        public bool Blocks(Vector2 p)
        {
            var sb = SafeBL();
            if (InputBridge.UseTouch && (Active && (p - Base).magnitude < Reach * 1.6f || Zone(sb).Contains(p))) return true;
            for (int i = 0; i < 4; i++) if ((p - ButtonCenter(i, sb)).magnitude < Radius * 1.25f) return true;
            return false;
        }

        /// <summary>Нажата ли кнопка i прямо сейчас (для подсветки).</summary>
        public bool Held(int i) => flash[i] > 0;

        struct Ptr { public int Id; public Vector2 Pos; public bool Began, Ended; }
        readonly System.Collections.Generic.List<Ptr> ptrs = new System.Collections.Generic.List<Ptr>();

        void Gather()
        {
            ptrs.Clear();
            if (InputBridge.UseTouch)
            {
                foreach (var t in InputBridge.Touches) ptrs.Add(new Ptr { Id = t.Id, Pos = t.Pos, Began = t.Began, Ended = t.Ended });
            }
            else
            {
                bool down = InputBridge.MouseDown(0), held = InputBridge.MouseHeld(0), up = InputBridge.MouseUp(0);
                if (down || held || up) ptrs.Add(new Ptr { Id = -1, Pos = InputBridge.MousePos, Began = down, Ended = up });
            }
        }

        /// <summary>Обновляет джойстик и нажатия; возвращает номер нажатой способности или -1 (одну за кадр).</summary>
        public int Update(float dt, bool uiOpen)
        {
            for (int i = 0; i < 4; i++) flash[i] = Mathf.Max(0, flash[i] - dt);
            int cast = -1;
            var sb = SafeBL();
            Gather();
            var zone = Zone(sb);
            if (uiOpen) { Active = false; stickId = int.MinValue; Stick = Vector2.zero; return -1; }
            foreach (var p in ptrs)
            {
                if (p.Id == stickId)
                {
                    if (p.Ended) { stickId = int.MinValue; Active = false; Stick = Vector2.zero; continue; }
                    Knob = p.Pos;
                    var d = Knob - Base;
                    float len = d.magnitude;
                    if (len > Reach * 1.35f) { Base += d.normalized * (len - Reach * 1.35f); d = Knob - Base; len = d.magnitude; } // основание тянется за пальцем
                    Stick = len < Reach * 0.12f ? Vector2.zero : d / Reach;
                    if (Stick.magnitude > 1) Stick.Normalize();
                    continue;
                }
                if (!p.Began) continue;
                bool onButton = false;
                for (int i = 0; i < 4; i++)
                    if ((p.Pos - ButtonCenter(i, sb)).magnitude < Radius * 1.25f)
                    {
                        onButton = true;
                        if (cast < 0) { cast = i; flash[i] = 0.25f; }
                    }
                if (onButton) continue;
                if (stickId == int.MinValue && InputBridge.UseTouch && zone.Contains(p.Pos) && !p.Ended)
                {
                    stickId = p.Id; Active = true; Base = p.Pos; Knob = p.Pos; Stick = Vector2.zero;
                }
            }
            if (stickId != int.MinValue && !ptrs.Exists(q => q.Id == stickId)) { stickId = int.MinValue; Active = false; Stick = Vector2.zero; }
            // клавиатура: WASD ведёт героя, 1–4 — способности
            Vector2 k = Vector2.zero;
            if (InputBridge.Held(K.W)) k.y += 1;
            if (InputBridge.Held(K.S)) k.y -= 1;
            if (InputBridge.Held(K.D)) k.x += 1;
            if (InputBridge.Held(K.A)) k.x -= 1;
            if (k != Vector2.zero) Stick = k.normalized;
            else if (!Active) Stick = Vector2.zero;
            if (cast < 0)
            {
                if (InputBridge.Pressed(K.D1)) cast = 0;
                else if (InputBridge.Pressed(K.D2)) cast = 1;
                else if (InputBridge.Pressed(K.D3)) cast = 2;
                else if (InputBridge.Pressed(K.D4)) cast = 3;
                if (cast >= 0) flash[cast] = 0.25f;
            }
            if (Forced.HasValue) Stick = Forced.Value; // проверка без рук: -hero
            return cast;
        }

        /// <summary>Для автопроверки: ход героя задан кодом.</summary>
        public Vector2? Forced;

        public void Reset() { Stick = Vector2.zero; Active = false; stickId = int.MinValue; }
    }
}
