using System.Collections.Generic;
using UnityEngine;

namespace BattleSim
{
    /// <summary>Интерфейс на IMGUI: работает без настройки Canvas/EventSystem и масштабируется под экран.</summary>
    public class BattleHud : MonoBehaviour
    {
        public BattleGame Game;

        readonly List<Rect> blockers = new List<Rect>();
        bool collect;
        float s = 1f;
        Rect safe;

        Texture2D round, white;
        GUIStyle btn, btnBig, label, labelCenter, title, panel;
        int styleFontBase = -1;

        static readonly Color Dark = new Color(0.08f, 0.1f, 0.13f, 0.82f);
        static readonly Color Accent = new Color(0.98f, 0.72f, 0.22f, 0.95f);
        static readonly Color Selected = new Color(0.32f, 0.36f, 0.42f, 0.95f);

        public bool IsOverUI(Vector2 screenPos)
        {
            var p = new Vector2(screenPos.x, Screen.height - screenPos.y);
            foreach (var r in blockers) if (r.Contains(p)) return true;
            return false;
        }

        void Awake()
        {
            round = MakeRound(32, 11);
            white = Texture2D.whiteTexture;
        }

        static Texture2D MakeRound(int size, float radius)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                    float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(radius - d + 0.5f)));
                }
            }
            t.Apply();
            return t;
        }

        void BuildStyles()
        {
            int fb = Mathf.RoundToInt(16f * s);
            if (fb == styleFontBase && btn != null) return;
            styleFontBase = fb;

            GUIStyle Base(int size, FontStyle fs, TextAnchor anchor)
            {
                var st = new GUIStyle { fontSize = size, fontStyle = fs, alignment = anchor, wordWrap = false, richText = true };
                st.normal.textColor = Color.white;
                return st;
            }

            btn = Base(fb, FontStyle.Bold, TextAnchor.MiddleCenter);
            btn.normal.background = round;
            btn.hover.background = round; btn.hover.textColor = new Color(1f, 0.95f, 0.8f);
            btn.active.background = round; btn.active.textColor = Accent;
            btn.border = new RectOffset(11, 11, 11, 11);
            btn.padding = new RectOffset(6, 6, 4, 4);
            btn.clipping = TextClipping.Clip;

            btnBig = new GUIStyle(btn) { fontSize = Mathf.RoundToInt(24f * s) };
            btnBig.normal.textColor = new Color(0.12f, 0.08f, 0.02f);
            btnBig.hover.textColor = Color.black;

            label = Base(Mathf.RoundToInt(15f * s), FontStyle.Bold, TextAnchor.MiddleLeft);
            labelCenter = Base(Mathf.RoundToInt(15f * s), FontStyle.Normal, TextAnchor.MiddleCenter);
            labelCenter.wordWrap = true;
            title = Base(Mathf.RoundToInt(38f * s), FontStyle.Bold, TextAnchor.MiddleCenter);

            panel = new GUIStyle { normal = { background = round }, border = new RectOffset(11, 11, 11, 11) };
        }

        // ---------------------------------------------------------------- помощники

        void Block(Rect r)
        {
            if (collect) blockers.Add(r);
        }

        void Panel(Rect r, Color c)
        {
            Block(r);
            var old = GUI.backgroundColor;
            GUI.backgroundColor = c;
            GUI.Box(r, GUIContent.none, panel);
            GUI.backgroundColor = old;
        }

        bool Button(Rect r, string text, Color bg, GUIStyle style = null)
        {
            Block(r);
            var old = GUI.backgroundColor;
            GUI.backgroundColor = bg;
            bool clicked = GUI.Button(r, text, style ?? btn);
            GUI.backgroundColor = old;
            return clicked;
        }

        void Bar(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        // ---------------------------------------------------------------- отрисовка

        void OnGUI()
        {
            if (Game == null) return;
            collect = Event.current.type == EventType.Layout;
            if (collect) blockers.Clear();

            float dpiScale = Screen.dpi > 0 ? Screen.dpi / 210f : 0f;
            s = Mathf.Clamp(Mathf.Max(Screen.height / 720f, dpiScale), 0.8f, 4f);
            BuildStyles();
            Rect sa = Screen.safeArea;
            safe = new Rect(sa.x, Screen.height - sa.yMax, sa.width, sa.height);

            DrawCounters();
            switch (Game.State)
            {
                case BattleGame.Phase.Setup: DrawSetup(); break;
                case BattleGame.Phase.Fight: DrawFight(); break;
                case BattleGame.Phase.Result: DrawFight(); DrawResult(); break;
            }
            DrawToast();
        }

        float Pad => 10f * s;
        float BtnH => 46f * s;

        void DrawCounters()
        {
            var b = Game.Battle;
            float w = 330f * s, h = 62f * s;
            var r = new Rect(safe.x + Pad, safe.y + Pad, w, h);
            Panel(r, Dark);
            bool fight = Game.State != BattleGame.Phase.Setup;
            int a0 = fight ? b.AliveCount[0] : b.PlanCount[0];
            int a1 = fight ? b.AliveCount[1] : b.PlanCount[1];
            string left = "<color=#" + Hex(Palette.BlueTeam * 1.35f) + ">Синие  " + a0 + (fight ? " / " + b.PlanCount[0] : "") + "</color>";
            string right = "<color=#" + Hex(Palette.RedTeam * 1.25f) + ">" + a1 + (fight ? " / " + b.PlanCount[1] : "") + "  Красные</color>";
            var inner = new Rect(r.x + 12f * s, r.y + 6f * s, r.width - 24f * s, 26f * s);
            GUI.Label(inner, left, label);
            var rs = new GUIStyle(label) { alignment = TextAnchor.MiddleRight };
            GUI.Label(inner, right, rs);

            var bar = new Rect(r.x + 12f * s, r.y + 38f * s, r.width - 24f * s, 12f * s);
            Bar(bar, new Color(0f, 0f, 0f, 0.45f));
            int total = Mathf.Max(1, a0 + a1);
            float bw = bar.width * a0 / total;
            if (a0 + a1 > 0)
            {
                Bar(new Rect(bar.x, bar.y, bw, bar.height), Palette.BlueTeam);
                Bar(new Rect(bar.x + bw, bar.y, bar.width - bw, bar.height), Palette.RedTeam);
            }
        }

        void DrawSetup()
        {
            // Верхний правый ряд
            string[] top = { "Новая карта", "Случайные армии", "Очистить" };
            float tw = Mathf.Min(170f * s, (safe.width - 330f * s - Pad * 6f) / 3f);
            float x = safe.xMax - Pad - tw;
            float y = safe.y + Pad;
            for (int i = top.Length - 1; i >= 0; i--)
            {
                if (Button(new Rect(x, y, tw, BtnH), top[i], Dark))
                {
                    if (i == 0) Game.NewMap();
                    else if (i == 1) Game.RandomArmies();
                    else Game.ClearArmies();
                }
                x -= tw + Pad * 0.6f;
            }
            if (Button(new Rect(safe.xMax - Pad - tw, y + BtnH + Pad * 0.6f, tw, BtnH * 0.8f), Atmosphere.PostEnabled ? "Эффекты: вкл" : "Эффекты: выкл", Dark))
                Atmosphere.PostEnabled = !Atmosphere.PostEnabled;

            // Нижняя панель
            float fightW = 190f * s;
            float bottomY = safe.yMax - Pad - BtnH * 1.25f;
            var fightRect = new Rect(safe.xMax - Pad - fightW, bottomY, fightW, BtnH * 1.25f);
            bool canFight = Game.Battle.PlanCount[0] > 0 && Game.Battle.PlanCount[1] > 0;
            if (Button(fightRect, "В БОЙ!", canFight ? Accent : new Color(0.5f, 0.5f, 0.5f, 0.8f), btnBig))
            {
                if (canFight) Game.StartBattle();
                else Game.ShowToast("Нужны обе армии: поставьте и синих, и красных");
            }

            int items = 2 + 4 + 1;
            float avail = fightRect.x - safe.x - Pad * 2.5f;
            float gap = Pad * 0.5f;
            float bw = Mathf.Min(140f * s, (avail - gap * (items - 1) - Pad) / items);
            float bx = safe.x + Pad;
            float by = safe.yMax - Pad - BtnH;

            for (int t = 0; t < 2; t++)
            {
                bool on = Game.SelectedTeam == t && !Game.Eraser;
                Color c = t == 0 ? Palette.BlueTeam : Palette.RedTeam;
                c.a = on ? 1f : 0.55f;
                if (Button(new Rect(bx, by, bw, BtnH), (on ? "● " : "") + (t == 0 ? "Синие" : "Красные"), c))
                {
                    Game.SelectedTeam = t;
                    Game.Eraser = false;
                }
                bx += bw + gap;
            }
            bx += Pad * 0.5f;
            for (int i = 0; i < UnitStats.All.Length; i++)
            {
                bool on = (int)Game.SelectedType == i && !Game.Eraser;
                if (Button(new Rect(bx, by, bw, BtnH), UnitStats.All[i].Name, on ? Selected : Dark))
                {
                    Game.SelectedType = (UnitType)i;
                    Game.Eraser = false;
                }
                if (on) Bar(new Rect(bx + bw * 0.2f, by + BtnH - 4f * s, bw * 0.6f, 3f * s), Accent);
                bx += bw + gap;
            }
            if (Button(new Rect(bx, by, bw, BtnH), "Ластик", Game.Eraser ? new Color(0.75f, 0.3f, 0.25f, 0.95f) : Dark))
                Game.Eraser = !Game.Eraser;

            string hint = Game.Eraser ? "Коснитесь солдат, чтобы убрать их"
                : InputBridge.UseTouch ? "Коснитесь земли — поставить отряд. Один палец — двигать, два — зум и поворот"
                : "Клик — поставить отряд. WASD — двигать, ПКМ — вращать, колесо — зум";
            var hr = new Rect(safe.x + Pad, by - 30f * s, avail, 26f * s);
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.9f);
            GUI.Label(new Rect(hr.x + 1f, hr.y + 1f, hr.width, hr.height), "<color=#000000aa>" + hint + "</color>", label);
            GUI.Label(hr, hint, label);
            GUI.color = old;
        }

        void DrawFight()
        {
            string[] names = { Game.Paused ? "Дальше" : "Пауза", "×0.25", "×1", "×2", Game.Rig.Cinematic ? "● Облёт" : "Облёт" };
            float bw = Mathf.Min(120f * s, (safe.width - 330f * s - Pad * 8f) / names.Length);
            float x = safe.xMax - Pad - bw * names.Length - Pad * 0.5f * (names.Length - 1);
            float y = safe.y + Pad;
            for (int i = 0; i < names.Length; i++)
            {
                bool on = (i == 0 && Game.Paused) ||
                          (i == 1 && Mathf.Approximately(Game.Speed, 0.25f)) ||
                          (i == 2 && Mathf.Approximately(Game.Speed, 1f)) ||
                          (i == 3 && Mathf.Approximately(Game.Speed, 2f)) ||
                          (i == 4 && Game.Rig.Cinematic);
                if (Button(new Rect(x, y, bw, BtnH), names[i], on ? Selected : Dark))
                {
                    switch (i)
                    {
                        case 0: Game.Paused = !Game.Paused; break;
                        case 1: Game.Speed = 0.25f; Game.Paused = false; break;
                        case 2: Game.Speed = 1f; Game.Paused = false; break;
                        case 3: Game.Speed = 2f; Game.Paused = false; break;
                        case 4: Game.Rig.Cinematic = !Game.Rig.Cinematic; break;
                    }
                }
                x += bw + Pad * 0.5f;
            }

            if (Game.State == BattleGame.Phase.Fight &&
                Button(new Rect(safe.x + Pad, safe.yMax - Pad - BtnH, 170f * s, BtnH), "■  К расстановке", Dark))
                Game.StopBattle();
        }

        void DrawResult()
        {
            float w = Mathf.Min(560f * s, safe.width - Pad * 2f), h = 250f * s;
            var r = new Rect(safe.center.x - w * 0.5f, safe.center.y - h * 0.5f, w, h);
            Panel(r, new Color(0.06f, 0.07f, 0.09f, 0.9f));

            var b = Game.Battle;
            string text;
            Color c;
            if (Game.Winner == 0) { text = "ПОБЕДА СИНИХ!"; c = Palette.BlueTeam * 1.4f; }
            else if (Game.Winner == 1) { text = "ПОБЕДА КРАСНЫХ!"; c = Palette.RedTeam * 1.3f; }
            else { text = "НИЧЬЯ"; c = Color.white; }
            GUI.Label(new Rect(r.x, r.y + 18f * s, r.width, 56f * s), "<color=#" + Hex(c) + ">" + text + "</color>", title);

            string sub = Game.Winner >= 0
                ? "Выжило " + b.AliveCount[Game.Winner] + " из " + b.PlanCount[Game.Winner] + " солдат. Время боя: " + Mathf.RoundToInt(Game.BattleTime) + " с"
                : "Никто не выжил";
            GUI.Label(new Rect(r.x + 20f * s, r.y + 80f * s, r.width - 40f * s, 50f * s), sub, labelCenter);

            float bw = (r.width - 40f * s - Pad * 2f) / 3f;
            float by = r.yMax - BtnH - 22f * s;
            float bx = r.x + 20f * s;
            if (Button(new Rect(bx, by, bw, BtnH), "Реванш", Accent, new GUIStyle(btn) { normal = { background = round, textColor = Color.black } })) Game.Rematch();
            bx += bw + Pad;
            if (Button(new Rect(bx, by, bw, BtnH), "Изменить армии", Selected)) Game.StopBattle();
            bx += bw + Pad;
            if (Button(new Rect(bx, by, bw, BtnH), "Новая карта", Selected)) { Game.StopBattle(); Game.NewMap(); }
        }

        void DrawToast()
        {
            if (Game.ToastTime <= 0f || string.IsNullOrEmpty(Game.Toast)) return;
            float a = Mathf.Clamp01(Game.ToastTime / 0.5f);
            float w = Mathf.Min(620f * s, safe.width - Pad * 2f), h = 44f * s;
            var r = new Rect(safe.center.x - w * 0.5f, safe.y + Pad + 70f * s, w, h);
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, a);
            GUI.backgroundColor = Dark;
            GUI.Box(r, GUIContent.none, panel);
            GUI.backgroundColor = Color.white;
            GUI.Label(r, Game.Toast, labelCenter);
            GUI.color = old;
        }
    }
}
