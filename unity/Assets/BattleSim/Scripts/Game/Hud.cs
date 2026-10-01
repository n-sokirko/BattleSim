using System.Collections.Generic;
using BattleSim.Core;
using UnityEngine;

namespace BattleSim
{
    /// <summary>Интерфейс на IMGUI: работает без Canvas/EventSystem и масштабируется под экран телефона.</summary>
    public partial class Hud : MonoBehaviour
    {
        public GameMain Game;

        readonly List<Rect> blockers = new List<Rect>();
        bool collect;
        float s = 1f;
        Rect safe;
        Texture2D round, white;
        GUIStyle btn, btnOn, btnGold, big, label, small, title, panel, labelTag, center, caption;
        int styleBase = -1;
        string openDrop;

        static readonly Color Panel = new Color(0.063f, 0.078f, 0.1f, 0.82f);
        static readonly Color PanelHi = new Color(0.13f, 0.16f, 0.2f, 0.94f);
        static readonly Color Brass = new Color(0.84f, 0.64f, 0.25f, 1f);
        static readonly Color Ink = new Color(0.93f, 0.89f, 0.8f, 1f);
        static readonly Color Muted = new Color(0.65f, 0.62f, 0.55f, 1f);
        static readonly Color Blue = new Color(0.29f, 0.53f, 0.92f, 1f);
        static readonly Color Red = new Color(0.87f, 0.31f, 0.25f, 1f);

        public bool IsOverUI(Vector2 screenPos)
        {
            var p = new Vector2(screenPos.x, Screen.height - screenPos.y);
            foreach (var r in blockers) if (r.Contains(p)) return true;
            return Game != null && (Game.HelpOpen || Game.Phase == Phase.Loading || Game.Menu != GameMain.Ui.None);
        }

        void Awake()
        {
            round = MakeRound(32, 10);
            white = Texture2D.whiteTexture;
        }

        static Texture2D MakeRound(int size, float radius)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(radius - d + 0.5f)));
                }
            t.Apply();
            return t;
        }

        void Styles()
        {
            int fb = Mathf.RoundToInt(14f * s);
            if (fb == styleBase && btn != null) return;
            styleBase = fb;
            GUIStyle Base(int size, FontStyle fs, TextAnchor a, Color c)
            {
                var st = new GUIStyle { fontSize = size, fontStyle = fs, alignment = a, wordWrap = false, richText = true, clipping = TextClipping.Clip };
                st.normal.textColor = c;
                return st;
            }
            btn = Base(fb, FontStyle.Bold, TextAnchor.MiddleCenter, Ink);
            btn.normal.background = round; btn.hover.background = round; btn.active.background = round;
            btn.hover.textColor = Color.white; btn.active.textColor = Brass;
            btn.border = new RectOffset(10, 10, 10, 10);
            btn.padding = new RectOffset(8, 8, 4, 4);
            btnGold = new GUIStyle(btn);
            btnGold.normal.textColor = new Color(0.15f, 0.1f, 0.02f); btnGold.hover.textColor = Color.black; btnGold.active.textColor = Color.black;
            btnOn = new GUIStyle(btn);
            btnOn.normal.textColor = new Color(0.96f, 0.84f, 0.57f);
            big = new GUIStyle(btn) { fontSize = Mathf.RoundToInt(24f * s) };
            big.normal.textColor = new Color(0.15f, 0.1f, 0.02f); big.hover.textColor = Color.black;
            label = Base(fb, FontStyle.Bold, TextAnchor.MiddleLeft, Ink);
            small = Base(Mathf.RoundToInt(12f * s), FontStyle.Normal, TextAnchor.MiddleLeft, Muted);
            small.wordWrap = true;
            center = Base(Mathf.RoundToInt(14f * s), FontStyle.Normal, TextAnchor.MiddleCenter, Ink);
            center.wordWrap = true;
            title = Base(Mathf.RoundToInt(38f * s), FontStyle.Bold, TextAnchor.MiddleCenter, Brass);
            caption = Base(Mathf.RoundToInt(17f * s), FontStyle.Bold, TextAnchor.MiddleCenter, Ink);
            caption.wordWrap = true;
            panel = new GUIStyle { normal = { background = round }, border = new RectOffset(10, 10, 10, 10) };
            labelTag = Base(Mathf.RoundToInt(12f * s), FontStyle.Bold, TextAnchor.MiddleCenter, Ink);
            labelTag.normal.background = round;
            labelTag.border = new RectOffset(10, 10, 10, 10);
            labelTag.padding = new RectOffset(6, 6, 2, 2);
        }

        // ---------------------------------------------------------------- помощники

        void Block(Rect r) { if (collect) blockers.Add(r); }

        void Box(Rect r, Color c, bool block = true)
        {
            if (block) Block(r);
            var old = GUI.backgroundColor;
            GUI.backgroundColor = c;
            GUI.Box(r, GUIContent.none, panel);
            GUI.backgroundColor = old;
        }

        bool Button(Rect r, string text, bool on = false, GUIStyle st = null, Color? bg = null)
        {
            Block(r);
            var old = GUI.backgroundColor;
            GUI.backgroundColor = bg ?? (on ? new Color(0.84f, 0.64f, 0.25f, 0.45f) : Panel);
            bool clicked = GUI.Button(r, text, st ?? (on ? btnOn : btn));
            GUI.backgroundColor = old;
            return clicked;
        }

        void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        /// <summary>Кнопка-список: показывает текущее значение, по нажатию раскрывает варианты.</summary>
        int Drop(Rect r, string id, string[] options, int sel)
        {
            bool open = openDrop == id;
            if (Button(r, options[sel] + (open ? "  ▴" : "  ▾"), open)) openDrop = open ? null : id;
            if (openDrop != id) return sel;
            for (int i = 0; i < options.Length; i++)
            {
                var o = new Rect(r.x, r.yMax + 4 * s + i * (r.height + 2 * s), r.width, r.height);
                if (Button(o, options[i], i == sel, null, PanelHi)) { openDrop = null; return i; }
            }
            return sel;
        }

        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
        static string Time(float t) => $"{(int)t / 60}:{(int)t % 60:00}";

        float Pad => 12f * s;
        float BtnH => 40f * s;

        // ---------------------------------------------------------------- отрисовка

        void OnGUI()
        {
            if (Game == null) return;
            collect = Event.current.type == EventType.Layout;
            if (collect) blockers.Clear();
            float dpi = Screen.dpi > 0 ? Screen.dpi / 200f : 0f;
            s = Mathf.Clamp(Mathf.Max(Screen.height / 760f, dpi), 0.75f, 4f);
            Styles();
            var sa = Screen.safeArea;
            safe = new Rect(sa.x, Screen.height - sa.yMax, sa.width, sa.height);

            if (Game.Phase == Phase.Loading) { DrawLoading(); return; }
            if (Game.Menu == GameMain.Ui.Title || Game.Menu == GameMain.Ui.Chapters)
            {
                if (Game.Menu == GameMain.Ui.Title) DrawTitle(); else DrawChapters();
                DrawToast();
                if (Game.HelpOpen) DrawHelp();
                return;
            }
            bool story = Game.Story != null;
            DrawSquadBars();
            DrawLabels();
            DrawTally();
            if (Game.Menu == GameMain.Ui.Intro) { DrawIntro(); DrawToast(); if (Game.HelpOpen) DrawHelp(); return; }
            if (Game.Phase == Phase.Setup) { if (story) DrawStorySetup(); else DrawSetup(); }
            else DrawFight();
            if (Game.Phase != Phase.Setup) DrawChronicle();
            if (Game.Phase == Phase.Fight) { DrawCaption(); if (story) DrawOrders(); }
            DrawSpeech();
            if (Game.Phase == Phase.Result) { if (story) DrawStoryResult(); else DrawResult(); }
            DrawToast();
            if (Game.HelpOpen) DrawHelp();
        }

        void DrawLoading()
        {
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.063f, 0.078f, 0.1f, 1f));
            float w = Mathf.Min(460 * s, Screen.width - 2 * Pad), cx = Screen.width / 2f, cy = Screen.height / 2f;
            var t = new GUIStyle(title) { fontSize = Mathf.RoundToInt(72 * s) };
            GUI.Label(new Rect(cx - w / 2, cy - 120 * s, w, 90 * s), "Сеча", t);
            GUI.Label(new Rect(cx - w / 2, cy - 20 * s, w, 30 * s), Game.LoadText, center);
            var bar = new Rect(cx - w / 2, cy + 20 * s, w, 6 * s);
            Fill(bar, new Color(1, 1, 1, 0.08f));
            Fill(new Rect(bar.x, bar.y, bar.width * Game.LoadProgress, bar.height), Brass);
            var cr = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(cx - w / 2, cy + 40 * s, w, 30 * s), "Модели: KayKit и Quaternius (CC0)", cr);
        }

        void DrawTally()
        {
            var b = Game.Battle;
            float w = Mathf.Min(330 * s, safe.width - 2 * Pad);
            bool setup = Game.Phase == Phase.Setup;
            var r = new Rect(safe.x + Pad, safe.y + Pad, w, (setup ? 108 : 88) * s);
            Box(r, Panel);
            bool fight = !setup;
            int a0 = fight ? b.Alive[0] : b.PlanCount[0], a1 = fight ? b.Alive[1] : b.PlanCount[1];
            var inner = new Rect(r.x + 12 * s, r.y + 6 * s, r.width - 24 * s, 22 * s);
            string n0 = Game.Story != null ? b.Races[0].Name : "Синие", n1 = Game.Story != null ? b.Races[1].Name : "Красные";
            GUI.Label(inner, $"<color=#8db4f5>{n0} {a0}{(fight ? " / " + b.PlanCount[0] : "")}</color>", label);
            GUI.Label(inner, $"<color=#f08a7e>{a1}{(fight ? " / " + b.PlanCount[1] : "")} {n1}</color>", new GUIStyle(label) { alignment = TextAnchor.MiddleRight });
            var bar = new Rect(r.x + 12 * s, r.y + 32 * s, r.width - 24 * s, 8 * s);
            Fill(bar, new Color(0, 0, 0, 0.45f));
            int total = Mathf.Max(1, a0 + a1);
            Fill(new Rect(bar.x, bar.y, bar.width * a0 / total, bar.height), Blue);
            Fill(new Rect(bar.x + bar.width * a0 / total, bar.y, bar.width * a1 / total, bar.height), Red);
            GUI.Label(new Rect(r.x + 12 * s, r.y + 44 * s, r.width - 24 * s, 18 * s), Game.MapName, small);
            var wind = Game.World.Wind;
            float ws = M.Hypot(wind.x, wind.z);
            string windText = ws < 0.5f ? "Безветрие" : $"{WindArrow()} Ветер {ws:0} м/с";
            if (Game.Style != null && Game.Style.Elev < 26) windText += " · низкое солнце слепит стрелков";
            GUI.Label(new Rect(r.x + 12 * s, r.y + 62 * s, r.width - 24 * s, 18 * s), windText, small);
            if (setup && Game.Story != null)
            {
                var c0 = b.CommanderSpec(0); var c1 = b.CommanderSpec(1);
                GUI.Label(new Rect(r.x + 12 * s, r.y + 80 * s, r.width - 24 * s, 26 * s),
                    $"<color=#cdbb8f>Ведёт {c0.Name} ({Defs.Traits[c0.Trait].Name}) · враг — {c1.Name}</color>", small);
            }
            else if (setup)
            {
                string info = b.UseCommanders && b.PlanCount[0] > 0 && b.PlanCount[1] > 0
                    ? $"Синих ведёт {b.CommanderSpec(0).Name} ({Defs.Traits[b.CommanderSpec(0).Trait].Name}), красных — {b.CommanderSpec(1).Name} ({Defs.Traits[b.CommanderSpec(1).Trait].Name})"
                    : "Без полководцев отряды бьются кто во что горазд";
                GUI.Label(new Rect(r.x + 12 * s, r.y + 80 * s, r.width - 24 * s, 26 * s), "<color=#cdbb8f>" + info + "</color>", small);
            }
        }

        /// <summary>Стрелка ветра относительно камеры: вверх — от зрителя вглубь сцены.</summary>
        string WindArrow()
        {
            var w = Game.World.Wind;
            float yaw = Game.Rig.Yaw;
            float sx = -w.x * Mathf.Cos(yaw) + w.z * Mathf.Sin(yaw), sy = w.x * Mathf.Sin(yaw) + w.z * Mathf.Cos(yaw);
            float a = Mathf.Atan2(sx, sy) * Mathf.Rad2Deg;
            string[] arrows = { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" };
            int i = ((Mathf.RoundToInt(a / 45f) % 8) + 8) % 8;
            return arrows[i];
        }

        void DrawSetup()
        {
            var b = Game.Battle;
            // Верхний ряд
            float y = safe.y + Pad, x = safe.xMax - Pad;
            float bw = Mathf.Clamp((safe.width - 360 * s) / 7.7f, 90 * s, 150 * s);
            var items = new List<(string id, float w)> { ("menu", bw * 0.7f), ("help", bw), ("cmd", bw), ("clear", bw * 0.8f), ("size", bw), ("random", bw), ("type", bw), ("new", bw) };
            var rects = new Dictionary<string, Rect>();
            foreach (var (id, w) in items) { x -= w; rects[id] = new Rect(x, y, w, BtnH); x -= 6 * s; }
            if (Button(rects["new"], "Новая карта")) { openDrop = null; Game.NewMapButton(); }
            string[] mapOpts = { "Любая местность", "Поле", "Лес", "Горы", "Болото", "Город" };
            int ms = Drop(rects["type"], "map", mapOpts, Game.MapSel + 1);
            if (ms != Game.MapSel + 1) Game.ChangeMapType(ms - 1);
            if (Button(rects["random"], "Случайные армии")) { openDrop = null; Game.MakeArmies(); }
            string[] sizeOpts = { "Стычка", "Сражение", "Великая сеча" };
            int ss = Drop(rects["size"], "size", sizeOpts, Game.ArmySize - 1);
            if (ss != Game.ArmySize - 1) { Game.ArmySize = ss + 1; Game.MakeArmies(); }
            if (Button(rects["clear"], "Очистить")) { openDrop = null; b.ClearAll(); }
            if (Button(rects["cmd"], b.UseCommanders ? "Полководцы: есть" : "Полководцы: нет", b.UseCommanders)) b.UseCommanders = !b.UseCommanders;
            if (Button(rects["help"], "Как устроен бой")) Game.OpenHelp(true);
            if (Button(rects["menu"], "Меню")) { openDrop = null; Game.OpenTitle(); }

            // Нижняя панель: армии, карточки войск, ластик, «В бой!»
            float fightW = 190 * s, fightH = 64 * s, bottom = safe.yMax - Pad;
            var fr = new Rect(safe.xMax - Pad - fightW, bottom - fightH, fightW, fightH);
            bool can = b.PlanCount[0] > 0 && b.PlanCount[1] > 0;
            if (Button(fr, "В бой!", false, big, can ? Brass : new Color(0.45f, 0.45f, 0.45f, 0.9f))) Game.StartBattle();

            float cardH = 64 * s, bx = safe.x + Pad, by = bottom - cardH;
            float avail = fr.x - bx - Pad;
            float teamW = 110 * s, cardW = Mathf.Min(150 * s, (avail - teamW - 90 * s - 20 * s) / 4f);
            Box(new Rect(bx - 4 * s, by - 4 * s, avail + 8 * s, cardH + 8 * s), Panel);
            for (int t = 0; t < 2; t++)
            {
                var r = new Rect(bx, by + t * (cardH / 2), teamW, cardH / 2 - 2 * s);
                bool on = Game.Team == t;
                if (Button(r, $"<color=#{Hex(t == 0 ? Blue : Red)}>■</color> {Game.Battle.Races[t].Name}", on)) { if (Game.Team == t) Game.CycleRace(t); Game.Team = t; Game.Eraser = false; }
            }
            float cx = bx + teamW + 8 * s;
            for (int i = 0; i < 4; i++)
            {
                var t = Game.Battle.Races[Game.Team].Units[i];
                var r = new Rect(cx, by, cardW, cardH);
                bool on = Game.Type == i && !Game.Eraser;
                string stats = $"{t.Hp:0} ОЗ · урон {t.Dmg:0}{(t.Ranged ? " · " + t.Range + " м" : "")}";
                if (Button(r, "", on)) { Game.Type = i; Game.Eraser = false; }
                GUI.Label(new Rect(r.x + 8 * s, r.y + 4 * s, r.width - 16 * s, 20 * s), $"<b>{t.Name}</b>  <size={Mathf.RoundToInt(10 * s)}><color=#a59d8b>{i + 1}</color></size>", label);
                GUI.Label(new Rect(r.x + 8 * s, r.y + 24 * s, r.width - 16 * s, 18 * s), stats, small);
                GUI.Label(new Rect(r.x + 8 * s, r.y + 42 * s, r.width - 16 * s, 18 * s), "<color=#cdbb8f>" + t.Note + "</color>", small);
                cx += cardW + 6 * s;
            }
            if (Button(new Rect(cx, by, 84 * s, cardH), "Ластик", Game.Eraser)) Game.Eraser = !Game.Eraser;

            string hint = Game.Eraser ? "Нажмите на солдат, чтобы убрать отряд"
                : InputBridge.UseTouch ? "Нажмите на землю — поставить отряд. Один палец двигает карту, два — зум и поворот"
                : "Клик — поставить отряд · WASD — двигать · правая кнопка — вращать · колесо — зум · T — сменить армию";
            var hs = new GUIStyle(center) { fontSize = Mathf.RoundToInt(12 * s) };
            GUI.Label(new Rect(safe.x + Pad + 1, by - 30 * s + 1, safe.width - 2 * Pad, 24 * s), "<color=#000000cc>" + hint + "</color>", hs);
            GUI.Label(new Rect(safe.x + Pad, by - 30 * s, safe.width - 2 * Pad, 24 * s), hint, hs);
        }

        void DrawFight()
        {
            float y = safe.y + Pad, bw = 88 * s, x = safe.xMax - Pad;
            var r = new Rect[8];
            for (int i = 7; i >= 0; i--) { float w = i == 7 ? 64 * s : i == 6 ? 44 * s : i == 5 ? 106 * s : i == 4 ? 96 * s : bw; x -= w; r[i] = new Rect(x, y, w, BtnH); x -= 6 * s; }
            if (Button(r[0], Game.Paused ? "Дальше" : "Пауза", Game.Paused)) Game.Paused = !Game.Paused;
            if (Button(r[1], "×0,25", Game.Speed == 0.25f && !Game.Paused)) { Game.Speed = 0.25f; Game.Paused = false; }
            if (Button(r[2], "×1", Game.Speed == 1f && !Game.Paused)) { Game.Speed = 1f; Game.Paused = false; }
            if (Button(r[3], "×2", Game.Speed == 2f && !Game.Paused)) { Game.Speed = 2f; Game.Paused = false; }
            bool orbit = Game.Rig.Cinematic && !Game.DirectorOn, direct = Game.Rig.Cinematic && Game.DirectorOn;
            if (Button(r[4], "Облёт", orbit)) Game.SetOrbit(!orbit);
            if (Button(r[5], "Режиссёр", direct)) Game.SetDirector(!direct);
            if (Button(r[6], "?")) Game.OpenHelp(true);
            bool muted = Game.Sound != null && Game.Sound.Muted;
            if (Button(r[7], muted ? "<color=#8a8a8a>Звук</color>" : "Звук", !muted)) Game.ToggleSound();
            if (Game.Phase == Phase.Fight && Button(new Rect(safe.x + Pad, safe.yMax - Pad - BtnH, 180 * s, BtnH), Game.Story != null ? "■ Заново" : "■ К расстановке")) Game.StopBattle();
        }

        void DrawChronicle()
        {
            int max = Screen.height < 500 ? 3 : 6;
            float w = Mathf.Min(440 * s, safe.width - 2 * Pad), lineH = 34 * s;
            float y = safe.yMax - Pad - BtnH - 10 * s;
            int n = Mathf.Min(max, Game.Chronicle.Count);
            for (int i = 0; i < n; i++)
            {
                var e = Game.Chronicle[i];
                y -= lineH;
                var r = new Rect(safe.x + Pad, y, w, lineH - 3 * s);
                Box(r, new Color(0.063f, 0.078f, 0.1f, i == 0 ? 0.8f : 0.6f), false);
                Fill(new Rect(r.x, r.y + 3 * s, 3 * s, r.height - 6 * s), e.Team == 0 ? Blue : e.Team == 1 ? Red : Muted);
                var st = new GUIStyle(small) { normal = { textColor = i == 0 ? Ink : new Color(Ink.r, Ink.g, Ink.b, 0.75f) } };
                GUI.Label(new Rect(r.x + 10 * s, r.y, r.width - 14 * s, r.height), $"<color=#a59d8b>{Time(e.T)}</color>  {e.Text}", st);
            }
        }

        /// <summary>Подпись режиссёра в нижней трети кадра: что сейчас показывают (цветная черта — чья армия).</summary>
        void DrawCaption()
        {
            var d = Game.Director;
            if (d == null || d.CaptionT <= 0 || string.IsNullOrEmpty(d.Caption)) return;
            float a = Mathf.Clamp01(d.CaptionT / 0.4f) * Mathf.Clamp01((2.5f - d.CaptionT) / 0.2f);
            var content = new GUIContent(d.Caption);
            float w = Mathf.Min(Mathf.Max(360 * s, caption.CalcSize(content).x + 40 * s), safe.width - 2 * Pad);
            float h = Mathf.Max(46 * s, caption.CalcHeight(content, w - 30 * s) + 14 * s);
            // правее летописи, на уровне её нижней строки; на узком экране — над летописью
            float chronR = safe.x + Pad + Mathf.Min(440 * s, safe.width - 2 * Pad) + Pad;
            float x = Mathf.Max(safe.center.x - w / 2, chronR), y = safe.yMax - Pad - BtnH - 10 * s - h;
            if (x + w > safe.xMax - Pad) { x = safe.center.x - w / 2; y -= (Screen.height < 500 ? 3 : 6) * 34 * s + 6 * s; }
            if (Game.Story != null) y -= BtnH + 70 * s; // над панелью приказов
            var r = new Rect(x, y, w, h);
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, a);
            Box(r, new Color(0.047f, 0.06f, 0.08f, 0.86f), false);
            Fill(new Rect(r.x, r.y + 6 * s, 4 * s, r.height - 12 * s), d.CaptionTeam == 0 ? Blue : d.CaptionTeam == 1 ? Red : Brass);
            GUI.Label(new Rect(r.x + 16 * s, r.y, r.width - 26 * s, r.height), content, caption);
            GUI.color = old;
        }

        /// <summary>
        /// Над отрядами на общем плане — две тонкие полоски: сколько осталось бойцов (цвет армии) и боевой дух
        /// (золото → красный). Видно, кто тает и кто вот-вот побежит. Вблизи камеры не рисуются.
        /// </summary>
        void DrawSquadBars()
        {
            if (Game.Phase != Phase.Fight || Game.Cam == null) return;
            var cp = Game.Cam.transform.position;
            float w = 30 * s, h = 3.5f * s;
            foreach (var sq in Game.Battle.Squads)
            {
                if (sq.Special || sq.T.Hero || sq.Alive == 0 || sq.Hidden || sq.Size < 2) continue;
                var wp = Conv.U(sq.Center.x, sq.Center.y + (sq.T.Mount ? 5.2f : 4.4f), sq.Center.z);
                float d = Vector3.Distance(cp, wp);
                float a = Mathf.Clamp01((d - 25) / 15f);
                if (a <= 0.02f) continue;
                var sp = Game.Cam.WorldToScreenPoint(wp);
                if (sp.z < 0 || sp.x < -40 || sp.x > Screen.width + 40 || sp.y < -20 || sp.y > Screen.height + 20) continue;
                float x = sp.x - w / 2, y = Screen.height - sp.y;
                float str = Mathf.Clamp01(sq.Alive / (float)sq.Size), mor = sq.T.Fearless ? 1 : Mathf.Clamp01(sq.Morale / 100f);
                bool rout = sq.Order.Mode == Mode.Rout;
                Fill(new Rect(x - 1, y - 1, w + 2, h * 2 + 3), new Color(0, 0, 0, 0.55f * a));
                var tc = sq.Team == 0 ? Blue : Red;
                Fill(new Rect(x, y, w * str, h), new Color(tc.r, tc.g, tc.b, a));
                var mc = rout ? new Color(0.55f, 0.55f, 0.55f) : Color.Lerp(new Color(0.9f, 0.25f, 0.18f), new Color(0.96f, 0.82f, 0.4f), mor);
                Fill(new Rect(x, y + h + 1, w * (rout ? 1 : mor), h), new Color(mc.r, mc.g, mc.b, a * 0.95f));
            }
        }

        /// <summary>Над отрядами — свежие приказы, над полководцами — имя.</summary>
        void DrawLabels()
        {
            if (Game.Phase == Phase.Setup || Game.Cam == null) return;
            foreach (var sq in Game.Battle.Squads)
            {
                if (sq.Alive == 0) continue;
                string text = null;
                float op = 1, h;
                V3 p;
                bool leader = false, general = false;
                if (sq.Special)
                {
                    var lead = sq.Units.Count > 0 ? sq.Units[0] : null;
                    var c = lead?.Cmd;
                    if (c == null || !lead.Alive) continue;
                    general = c.Role != Role.Captain;
                    leader = true;
                    text = general ? "★ " + c.Name : "Воевода " + c.Name + (c.Mission != null ? " · " + Defs.MissionText(c.Mission.Kind) : "");
                    p = lead.Pos; h = general ? 4.9f : 4.4f;
                }
                else if (sq.T.Hero && sq.Title != null)
                { // богатырь — всегда подписан: крупный боец не должен казаться «выросшим» солдатом
                    var hu = sq.Units.Count > 0 ? sq.Units[0] : null;
                    if (hu == null || !hu.Alive) continue;
                    text = sq.T.Name + " " + sq.Title + (hu.Duel != null ? " · поединок" : "");
                    leader = true; general = true;
                    p = hu.Pos; h = 2.2f * sq.T.Scale + 0.6f;
                }
                else
                {
                    if (sq.LabelT > 0) { text = sq.Player && sq.Order.Mode == Mode.Move ? "На место" : Defs.OrderText(sq.Order.Kind); op = Mathf.Min(1, sq.LabelT); }
                    else if (sq.Hidden) { text = "затаились"; op = 0.7f; }
                    if (text == null) continue;
                    p = sq.Center; h = sq.T.Mount ? 4 : 3.2f;
                }
                var sp = Game.Cam.WorldToScreenPoint(Conv.U(p.x, p.y + h, p.z));
                if (sp.z < 0 || sp.x < -50 || sp.x > Screen.width + 50 || sp.y < -20 || sp.y > Screen.height + 20) continue;
                var st = labelTag;
                var size = st.CalcSize(new GUIContent(text));
                var r = new Rect(sp.x - size.x / 2, Screen.height - sp.y - size.y, size.x, size.y);
                var old = GUI.backgroundColor;
                var oc = GUI.color;
                GUI.color = new Color(1, 1, 1, op);
                GUI.backgroundColor = leader ? (general ? new Color(0.45f, 0.33f, 0.1f, 0.95f) : new Color(0.25f, 0.2f, 0.12f, 0.9f)) : sq.Team == 0 ? new Color(0.12f, 0.2f, 0.36f, 0.9f) : new Color(0.36f, 0.12f, 0.1f, 0.9f);
                var ts = new GUIStyle(st);
                ts.normal.textColor = general ? new Color(0.96f, 0.84f, 0.57f) : leader ? new Color(0.9f, 0.83f, 0.64f) : Ink;
                GUI.Box(r, text, ts);
                GUI.backgroundColor = old;
                GUI.color = oc;
            }
        }

        string resultText;
        float resultAt = -1;

        /// <summary>Итоги боя (считаются один раз): выжившие по видам войск, богатыри, лучший отряд, главные моменты.</summary>
        string ResultSummary()
        {
            var b = Game.Battle;
            if (resultText != null && Mathf.Approximately(resultAt, b.Time)) return resultText;
            resultAt = b.Time;
            var sb = new System.Text.StringBuilder();
            for (int t = 0; t < 2; t++)
            {
                string col = t == 0 ? "#8cb4f5" : "#f08a7d";
                sb.Append($"<color={col}><b>{(t == 0 ? "Синие" : "Красные")} · {b.Races[t].Name}</b></color>\n");
                var planned = new Dictionary<int, int>();
                foreach (var p in b.Plan) if (p.Team == t) planned[p.Type] = planned.TryGetValue(p.Type, out int n) ? n + 1 : 1;
                var parts = new List<string>();
                foreach (var kv in planned)
                {
                    int alive = 0;
                    foreach (var u in b.Units) if (u.Alive && u.Team == t && u.Type == kv.Key) alive++;
                    parts.Add($"{Defs.All[kv.Key].Name.ToLowerInvariant()} {alive}/{kv.Value}");
                }
                sb.Append(string.Join(" · ", parts)).Append("\n");
                var h = b.Heroes[t];
                if (h != null) sb.Append($"{h.T.Name} {h.Squad.Title}: {(h.Alive ? "жив" : "пал")}, сразил {h.Kills}\n");
                Squad best = null; int bk = 0;
                foreach (var q in b.Squads)
                {
                    if (q.Team != t || q.Special || q.T.Hero) continue;
                    int k = 0;
                    foreach (var u in q.Units) k += u.Kills;
                    if (k > bk) { bk = k; best = q; }
                }
                if (best != null) sb.Append($"Лучший отряд — {best.Name}: сразили {bk}\n");
                sb.Append("\n");
            }
            // главные моменты из летописи
            string[] keys = { "Поединок", "победил в поединке", " пал!", "ревёт", "Хитрость", "поднимают", "рассыпаются", "смыкают", "Засада", "дрогнули" };
            var moments = new List<string>();
            var seen = new HashSet<string>();
            foreach (var key in keys)
                foreach (var e in b.Log)
                    if (e.Text.Contains(key) && seen.Add(key))
                    {
                        moments.Add($"<color=#cdbb8f>{(int)e.T / 60}:{(int)e.T % 60:00}</color> {e.Text}");
                        break;
                    }
            moments.Sort((x, y) => string.CompareOrdinal(x, y));
            if (moments.Count > 0) sb.Append("<b>Как это было</b>\n").Append(string.Join("\n", moments.GetRange(0, Mathf.Min(6, moments.Count))));
            return resultText = sb.ToString();
        }

        void DrawResult()
        {
            float w = Mathf.Min(640 * s, safe.width - 2 * Pad), h = Mathf.Min(560 * s, safe.height - 2 * Pad);
            var r = new Rect(safe.center.x - w / 2, safe.center.y - h / 2, w, h);
            Box(r, new Color(0.047f, 0.06f, 0.08f, 0.92f));
            int win = Game.Winner;
            var b = Game.Battle;
            string t = win == 0 ? "Победа синих" : win == 1 ? "Победа красных" : "Ничья";
            var ts = new GUIStyle(title);
            ts.normal.textColor = win == 0 ? new Color(0.55f, 0.71f, 0.96f) : win == 1 ? new Color(0.94f, 0.54f, 0.49f) : Ink;
            GUI.Label(new Rect(r.x, r.y + 12 * s, r.width, 50 * s), t, ts);
            string sub = win >= 0 ? $"Выжило {b.Alive[win]} из {b.PlanCount[win]} · бой длился {Mathf.RoundToInt(Game.BattleTime)} с" : "Никто не выжил";
            GUI.Label(new Rect(r.x + 20 * s, r.y + 60 * s, r.width - 40 * s, 26 * s), sub, center);
            var st = new GUIStyle(small) { wordWrap = true, richText = true, fontSize = Mathf.RoundToInt(13 * s), alignment = TextAnchor.UpperLeft };
            GUI.Label(new Rect(r.x + 24 * s, r.y + 94 * s, r.width - 48 * s, h - 94 * s - BtnH - 34 * s), ResultSummary(), st);
            float bw = (r.width - 40 * s - 16 * s) / 3, by = r.yMax - BtnH - 18 * s, bx = r.x + 20 * s;
            if (Button(new Rect(bx, by, bw, BtnH), "Реванш", false, btnGold, Brass)) Game.StartBattle();
            if (Button(new Rect(bx + bw + 8 * s, by, bw, BtnH), "Изменить армии")) Game.StopBattle();
            if (Button(new Rect(bx + 2 * (bw + 8 * s), by, bw, BtnH), "Новая карта")) { Game.StopBattle(); Game.NewMap(); }
        }

        void DrawToast()
        {
            if (Game.ToastT <= 0 || string.IsNullOrEmpty(Game.Toast)) return;
            float a = Mathf.Clamp01(Game.ToastT / 0.4f);
            float w = Mathf.Min(620 * s, safe.width - 2 * Pad), h = 42 * s;
            float dy = Game.Speech != null && Game.SpeechT > 0 ? 82 * s : 0;
            var r = new Rect(safe.center.x - w / 2, safe.y + Pad + 100 * s + dy, w, h);
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, a);
            Box(r, PanelHi, false);
            GUI.Label(r, Game.Toast, center);
            GUI.color = old;
        }

        Vector2 helpScroll;

        void DrawHelp()
        {
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.03f, 0.04f, 0.055f, 0.55f));
            float w = Mathf.Min(980 * s, safe.width - 2 * Pad), h = Mathf.Min(620 * s, safe.height - 2 * Pad);
            var r = new Rect(safe.center.x - w / 2, safe.center.y - h / 2, w, h);
            Box(r, new Color(0.055f, 0.067f, 0.086f, 0.96f));
            var ts = new GUIStyle(title) { fontSize = Mathf.RoundToInt(28 * s), alignment = TextAnchor.MiddleLeft };
            GUI.Label(new Rect(r.x + 22 * s, r.y + 12 * s, r.width, 44 * s), "Как устроен бой", ts);
            var body = new Rect(r.x + 22 * s, r.y + 62 * s, r.width - 44 * s, r.height - 130 * s);
            var st = new GUIStyle(small) { fontSize = Mathf.RoundToInt(13 * s), normal = { textColor = Ink } };
            float cw = body.width - 20 * s, ch = st.CalcHeight(new GUIContent(HelpText), cw);
            helpScroll = GUI.BeginScrollView(body, helpScroll, new Rect(0, 0, cw, ch));
            GUI.Label(new Rect(0, 0, cw, ch), HelpText, st);
            GUI.EndScrollView();
            if (Button(new Rect(r.xMax - 150 * s, r.yMax - BtnH - 16 * s, 130 * s, BtnH), "Понятно", false, null, Brass)) Game.OpenHelp(false);
        }

        const string HelpText =
            "<color=#cdbb8f><b>РЕЛЬЕФ РЕШАЕТ</b></color>\n" +
            "• <color=#f6d792><b>Высота.</b></color> В гору идут медленнее, натиск конницы на склоне гаснет. Удар сверху сильнее до 35%, снизу слабее до 30%. Арбалетчики на холме бьют дальше.\n" +
            "• <color=#f6d792><b>Укрытия.</b></color> Болт — настоящий снаряд: он врезается в склон, ограду или крепостную стену. Без прямой видимости стрелок не стреляет. На стене за зубцами болты ранят вдвое слабее.\n" +
            "• <color=#f6d792><b>Ограды и проёмы.</b></color> Через ограду лезут втрое медленнее, через проём проходят свободно.\n" +
            "• <color=#f6d792><b>Низины.</b></color> Отряд в овраге издали не виден, пока не выстрелит или враг не подойдёт ближе 20 м. Кольцо под ним темнеет.\n\n" +
            "<color=#cdbb8f><b>МЕСТНОСТИ</b></color>\n" +
            "• <color=#f6d792><b>Поле.</b></color> Холмы, овраг между армиями, каменные ограды с проёмами.\n" +
            "• <color=#f6d792><b>Лес.</b></color> Чаща прячет отряды, глушит болты и вдвое замедляет конницу. Хитрый полководец устроит там засаду.\n" +
            "• <color=#f6d792><b>Горы.</b></color> Цепь скал поперёк поля, между ними — узкие ущелья; впереди и позади — холмы и одиночные горы. На скалы не забраться: войска идут ущельями, стрелки занимают холмы.\n" +
            "• <color=#f6d792><b>Болото.</b></color> Глубокую воду не перейти, по топи и броду идут медленно, кони вязнут. В камышах легко затаиться.\n" +
            "• <color=#f6d792><b>Город.</b></color> Крепостные стены с башнями, воротами и проломами, лестницы на боевой ход, детинец на холме, тесные кварталы и баррикады.\n\n" +
            "<color=#cdbb8f><b>ПОЛКОВОДЦЫ ДУМАЮТ</b></color>\n" +
            "• У каждой армии свой полководец на белом коне со знаменем: осторожный, яростный или хитрый.\n" +
            "• Раз в 3–4 секунды он оценивает силы и рельеф и отдаёт приказы: занять высоту, устроить засаду, обойти с фланга, ударить конницей по стрелкам, прикрыть стрелков, отойти и разогнаться снова. Арбалетчики сами ищут рубеж стрельбы — повыше, за своей пехотой, на стене.\n" +
            "• <color=#f6d792><b>Цепочка командования.</b></color> У большой армии главнокомандующий делит войско на крылья с воеводами и держит резерв. Никто не отсиживается: отряд без дела идёт в бой.\n\n" +
            "<color=#cdbb8f><b>ЧЕГО НЕТ В ДРУГИХ ИГРАХ</b></color>\n" +
            "• <color=#f6d792><b>Гонцы.</b></color> Приказ дальнему отряду или воеводе везёт всадник. Перехватите его, и приказ не дойдёт.\n" +
            "• <color=#f6d792><b>Солнце и ветер.</b></color> Низкое солнце слепит стрелков. Ветер сносит болты и меняет дальность.\n" +
            "• <color=#f6d792><b>Засады.</b></color> Первый удар из укрытия сильнее в 1,6 раза. Удар в спину сильнее в 1,35 раза, щит от него не спасает.\n" +
            "• <color=#f6d792><b>Боевой дух.</b></color> Потери, болты и удары в спину его подтачивают. Сломленный отряд бежит, а рядом со знаменем полководца приходит в себя.\n\n" +
            "<color=#cdbb8f><b>ПОХОД: ВАШИ ПРИКАЗЫ</b></color>\n" +
            "• В главах похода свои полки ставятся из запаса ниже золотой черты — или кнопкой «Расставить за меня».\n" +
            "• В бою коснитесь своего отряда (под ним загорятся золотые кольца), потом земли — он пойдёт туда и встанет; вражеского отряда — ударит по нему. Кнопки: «В атаку», «Стоять», «Отступить», «Сам» — вернуть отряд воеводе; у богатыря — «На поединок».\n" +
            "• Отряды без ваших приказов ведёт ваш воевода. Побежавший отряд приказов не слышит, а опомнившись — снова под рукой воеводы. Пауза — чтобы подумать.\n\n" +
            "<color=#cdbb8f><b>КАМЕРА</b></color>\n" +
            "• <color=#f6d792><b>Режиссёр</b></color> (Tab или кнопка «Режиссёр»). Камера сама показывает главное: натиск конницы — ещё до удара и с замедлением, первые сшибки, бегство, гибель полководцев, самую гущу сечи; время от времени — общий план. N — следующий план. Тронули камеру — она ваша, а через 10 с покоя режиссёр вернётся.\n" +
            "• <color=#f6d792><b>Облёт.</b></color> Медленный облёт над центром боя.";
    }
}
