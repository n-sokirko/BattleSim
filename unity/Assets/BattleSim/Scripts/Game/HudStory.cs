using System.Collections.Generic;
using BattleSim.Core;
using UnityEngine;

namespace BattleSim
{
    /// <summary>
    /// Интерфейс похода: заставка, список глав, рассказ перед главой, расстановка из запаса,
    /// приказы в бою, реплики героев и итог главы со звёздами.
    /// </summary>
    public partial class Hud
    {
        static readonly Color Gold = new Color(0.96f, 0.84f, 0.57f);

        /// <summary>Звёзды строкой: заработанные — золотом, остальные — серым.</summary>
        static string StarRow(int n, int of = 3)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < of; i++) sb.Append(i < n ? "<color=#f2c955>★</color>" : "<color=#80848a>★</color>");
            return sb.ToString();
        }

        static string Star(bool on) => on ? "<color=#f2c955>★</color>" : "<color=#80848a>★</color>";

        void Veil(float a) => Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.03f, 0.04f, 0.055f, a));

        /// <summary>Полки списком: «мечники ×3, арбалетчики ×2».</summary>
        static string RosterText(int[] counts)
        {
            var parts = new List<string>();
            for (int i = 0; i < counts.Length; i++)
                if (counts[i] > 0) parts.Add($"{Defs.Rus.Units[i].Name.ToLowerInvariant()} ×{counts[i]}");
            return string.Join(", ", parts);
        }

        GUIStyle Shadowed(GUIStyle st) => new GUIStyle(st) { normal = { textColor = new Color(0, 0, 0, 0.8f) } };

        // ---------------------------------------------------------------- заставка

        void DrawTitle()
        {
            Veil(0.3f);
            float w = Mathf.Min(420 * s, safe.width - 2 * Pad - 40 * s), x = safe.x + Mathf.Max(Pad + 20 * s, safe.width * 0.07f), cy = safe.center.y;
            Box(new Rect(x - 20 * s, cy - 222 * s, w + 40 * s, 440 * s), new Color(0.04f, 0.05f, 0.07f, 0.78f));
            var t = new GUIStyle(title) { fontSize = Mathf.RoundToInt(84 * s), alignment = TextAnchor.MiddleLeft };
            GUI.Label(new Rect(x, cy - 200 * s, w, 100 * s), "Сеча", t);
            var sub = new GUIStyle(center) { alignment = TextAnchor.MiddleLeft, fontSize = Mathf.RoundToInt(15 * s) };
            sub.normal.textColor = Muted;
            GUI.Label(new Rect(x + 4 * s, cy - 108 * s, w, 24 * s), "битвы Руси, Степи, Орды и Нави", sub);

            int total = 0;
            for (int i = 0; i < Campaign.Chapters.Length; i++) total += GameMain.StarsOf(i);
            float bh = 58 * s, y = cy - 60 * s;
            var note = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter };
            var bst = new GUIStyle(big) { fontSize = Mathf.RoundToInt(21 * s) };
            if (Button(new Rect(x, y, w, bh), "Поход: " + Campaign.Name, false, bst, Brass)) Game.OpenChapters();
            GUI.Label(new Rect(x, y + bh + 2 * s, w, 20 * s), $"{Campaign.Chapters.Length} глав за Русь · звёзд {total} из {Campaign.Chapters.Length * 3}", note);
            y += bh + 34 * s;
            if (Button(new Rect(x, y, w, bh * 0.85f), "Вольная битва")) Game.StartSandbox();
            GUI.Label(new Rect(x, y + bh * 0.85f + 2 * s, w, 20 * s), "любые армии и местность — ставьте и смотрите", note);
            y += bh + 30 * s;
            bool muted = Game.Sound != null && Game.Sound.Muted;
            float hw = w / 2 - 4 * s;
            if (Button(new Rect(x, y, hw, BtnH), muted ? "Звук: выкл" : "Звук: вкл", !muted)) Game.ToggleSound();
            if (Button(new Rect(x + hw + 8 * s, y, hw, BtnH), "Как устроен бой")) Game.OpenHelp(true);
        }

        // ---------------------------------------------------------------- главы

        Vector2 chapScroll;

        void DrawChapters()
        {
            Veil(0.45f);
            float w = Mathf.Min(780 * s, safe.width - 2 * Pad), h = safe.height - 2 * Pad;
            var r = new Rect(safe.center.x - w / 2, safe.y + Pad, w, h);
            Box(r, new Color(0.047f, 0.06f, 0.08f, 0.94f));
            var ts = new GUIStyle(title) { fontSize = Mathf.RoundToInt(28 * s), alignment = TextAnchor.MiddleLeft };
            GUI.Label(new Rect(r.x + 22 * s, r.y + 8 * s, r.width - 44 * s, 44 * s), "Поход: " + Campaign.Name, ts);
            float rowH = 60 * s, gap = 6 * s, top = r.y + 58 * s, bottom = r.yMax - BtnH - 22 * s;
            var view = new Rect(r.x + 16 * s, top, r.width - 32 * s, bottom - top);
            float cw = view.width - 16 * s, ch = Campaign.Chapters.Length * (rowH + gap);
            if (ch <= view.height) cw = view.width;
            chapScroll = GUI.BeginScrollView(view, chapScroll, new Rect(0, 0, cw, ch));
            var stRight = new GUIStyle(label) { alignment = TextAnchor.MiddleRight, fontSize = Mathf.RoundToInt(20 * s) };
            for (int i = 0; i < Campaign.Chapters.Length; i++)
            {
                var c = Campaign.Chapters[i];
                bool open = GameMain.Unlocked(i);
                int st = GameMain.StarsOf(i);
                var rr = new Rect(0, i * (rowH + gap), cw, rowH);
                if (Button(rr, "", false, null, open ? new Color(0.16f, 0.19f, 0.24f, 0.95f) : new Color(0.07f, 0.075f, 0.085f, 0.8f)) && open) Game.StartChapter(i);
                string col = open ? "" : "<color=#6d6a64>";
                string end = open ? "" : "</color>";
                GUI.Label(new Rect(rr.x + 14 * s, rr.y + 6 * s, rr.width - 150 * s, 24 * s), $"{col}<b>{c.No}. {c.Title}</b>{end}", label);
                string where = $"{Defs.Maps[(int)c.Map].Name} · {Defs.Biomes[c.Biome]}, {Defs.Times[c.Time]} · против: {c.Foe.Name}";
                GUI.Label(new Rect(rr.x + 14 * s, rr.y + 32 * s, rr.width - 150 * s, 20 * s), open ? where : "откроется после главы " + i, small);
                if (open) GUI.Label(new Rect(rr.xMax - 140 * s, rr.y, 126 * s, rr.height), StarRow(st), stRight);
                else GUI.Label(new Rect(rr.xMax - 140 * s, rr.y, 126 * s, rr.height), "<color=#6d6a64>закрыто</color>", new GUIStyle(small) { alignment = TextAnchor.MiddleRight });
            }
            GUI.EndScrollView();
            if (Button(new Rect(r.x + 20 * s, r.yMax - BtnH - 12 * s, 150 * s, BtnH), "Назад")) Game.OpenTitle();
            var hint = new GUIStyle(small) { alignment = TextAnchor.MiddleRight };
            GUI.Label(new Rect(r.x + 180 * s, r.yMax - BtnH - 12 * s, r.width - 200 * s, BtnH), "★ победа · ★ особая цель главы · ★ потери меньше половины", hint);
        }

        // ---------------------------------------------------------------- рассказ перед главой

        Vector2 introScroll;

        void DrawIntro()
        {
            var c = Game.Story;
            if (c == null) return;
            Veil(0.2f);
            float w = Mathf.Min(720 * s, safe.width - 2 * Pad);
            var introSt = new GUIStyle(small) { fontSize = Mathf.RoundToInt(14 * s), normal = { textColor = Ink }, alignment = TextAnchor.UpperLeft };
            float textH = introSt.CalcHeight(new GUIContent(IntroText(c)), w - 48 * s);
            float h = Mathf.Min(90 * s + textH + BtnH + 40 * s, safe.height - 2 * Pad);
            var r = new Rect(safe.center.x - w / 2, safe.center.y - h / 2, w, h);
            Box(r, new Color(0.047f, 0.06f, 0.08f, 0.95f));
            GUI.Label(new Rect(r.x + 24 * s, r.y + 12 * s, r.width - 48 * s, 22 * s),
                $"<color=#a59d8b>ГЛАВА {c.No} · {Defs.Maps[(int)c.Map].Name} · {Defs.Biomes[c.Biome]}, {Defs.Times[c.Time]}</color>", small);
            var best = new GUIStyle(label) { alignment = TextAnchor.MiddleRight, fontSize = Mathf.RoundToInt(18 * s) };
            GUI.Label(new Rect(r.xMax - 150 * s, r.y + 10 * s, 126 * s, 26 * s), StarRow(GameMain.StarsOf(c.No - 1)), best);
            var ts = new GUIStyle(title) { fontSize = Mathf.RoundToInt(32 * s), alignment = TextAnchor.MiddleLeft };
            GUI.Label(new Rect(r.x + 24 * s, r.y + 36 * s, r.width - 48 * s, 46 * s), c.Title, ts);

            string text = IntroText(c);
            var st = introSt;
            var body = new Rect(r.x + 24 * s, r.y + 90 * s, r.width - 48 * s, r.height - 90 * s - BtnH - 30 * s);
            float cw = body.width - 16 * s, chh = st.CalcHeight(new GUIContent(text), cw);
            if (chh <= body.height) cw = body.width;
            chh = st.CalcHeight(new GUIContent(text), cw);
            introScroll = GUI.BeginScrollView(body, introScroll, new Rect(0, 0, cw, chh));
            GUI.Label(new Rect(0, 0, cw, chh), text, st);
            GUI.EndScrollView();

            float by = r.yMax - BtnH - 16 * s;
            if (Button(new Rect(r.xMax - 220 * s, by, 196 * s, BtnH), "К расстановке", false, btnGold, Brass)) Game.Menu = GameMain.Ui.None;
            if (Button(new Rect(r.x + 24 * s, by, 130 * s, BtnH), "Главы")) Game.OpenChapters();
        }

        /// <summary>Рассказ главы: сама история, враг, свои полки, звёзды и совет.</summary>
        static string IntroText(Chapter c)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(string.Join("\n\n", c.Intro)).Append("\n\n");
            var foeHero = c.FoeHero != null ? $", с ним — {c.Foe.Hero.Name.ToLowerInvariant()} {c.FoeHero}" : "";
            sb.Append($"<color=#f08a7e><b>Враг:</b></color> {c.Foe.Name}, ведёт {c.FoeCmd}{foeHero}\n");
            string mine = RosterText(c.Mine) + (c.Hero != null ? $" и богатырь {c.Hero}" : "");
            sb.Append($"<color=#8db4f5><b>Ваши полки:</b></color> {mine}\n");
            if (c.Reinf != null) sb.Append($"<color=#8db4f5><b>Подмога:</b></color> {RosterText(c.Reinf)} — на {c.ReinfT:0}-й секунде боя\n");
            sb.Append($"<b>Звёзды:</b> {Star(true)} победа · {Star(true)} {c.GoalText} · {Star(true)} потери меньше половины\n\n");
            sb.Append($"<color=#cdbb8f><b>Совет.</b> {c.Tip}</color>");
            return sb.ToString();
        }

        // ---------------------------------------------------------------- расстановка из запаса

        void DrawStorySetup()
        {
            var b = Game.Battle;
            float y = safe.y + Pad, x = safe.xMax - Pad, bw = 124 * s;
            if (Button(new Rect(x - bw * 1.2f, y, bw * 1.2f, BtnH), "Как устроен бой")) Game.OpenHelp(true);
            x -= bw * 1.2f + 6 * s;
            if (Button(new Rect(x - bw, y, bw, BtnH), "Задача")) Game.Menu = GameMain.Ui.Intro;
            x -= bw + 6 * s;
            if (Button(new Rect(x - bw, y, bw, BtnH), "Главы")) Game.OpenChapters();

            float fightW = 190 * s, fightH = 64 * s, bottom = safe.yMax - Pad;
            var fr = new Rect(safe.xMax - Pad - fightW, bottom - fightH, fightW, fightH);
            bool can = b.PlanCount[0] > 0;
            if (Button(fr, "В бой!", false, big, can ? Brass : new Color(0.45f, 0.45f, 0.45f, 0.9f))) Game.StartBattle();

            float cardH = 64 * s, bx = safe.x + Pad, by = bottom - cardH;
            float autoW = 128 * s, eraseW = 84 * s, avail = fr.x - bx - Pad;
            float cardW = Mathf.Min(160 * s, (avail - autoW - eraseW - 24 * s) / 4f);
            Box(new Rect(bx - 4 * s, by - 4 * s, avail + 8 * s, cardH + 8 * s), Panel);
            var units = b.Races[0].Units;
            var cnt = new GUIStyle(label) { alignment = TextAnchor.MiddleRight, fontSize = Mathf.RoundToInt(18 * s) };
            float cx = bx;
            int leftAll = 0;
            for (int i = 0; i < 4; i++)
            {
                var t = units[i];
                int left = Game.Roster[i];
                leftAll += left;
                var r = new Rect(cx, by, cardW, cardH);
                bool on = Game.Type == i && !Game.Eraser && left > 0;
                if (Button(r, "", on, null, left > 0 ? (Color?)null : new Color(0.07f, 0.07f, 0.08f, 0.7f)) && left > 0) { Game.Type = i; Game.Eraser = false; }
                string dim = left > 0 ? "" : "<color=#6d6a64>";
                string dimEnd = left > 0 ? "" : "</color>";
                GUI.Label(new Rect(r.x + 8 * s, r.y + 4 * s, r.width - 16 * s, 22 * s), $"{dim}<b>{t.Name}</b>{dimEnd}", label);
                GUI.Label(new Rect(r.x + 8 * s, r.y + 4 * s, r.width - 16 * s, 22 * s), left > 0 ? $"<color=#f2c955>×{left}</color>" : "<color=#6d6a64>×0</color>", cnt);
                GUI.Label(new Rect(r.x + 8 * s, r.y + 26 * s, r.width - 16 * s, 18 * s), $"{t.Hp:0} ОЗ · урон {t.Dmg:0}{(t.Ranged ? " · " + t.Range + " м" : "")}", small);
                GUI.Label(new Rect(r.x + 8 * s, r.y + 43 * s, r.width - 16 * s, 18 * s), "<color=#cdbb8f>" + t.Note + "</color>", small);
                cx += cardW + 6 * s;
            }
            if (Button(new Rect(cx, by, eraseW, cardH), "Ластик", Game.Eraser)) Game.Eraser = !Game.Eraser;
            cx += eraseW + 6 * s;
            if (Button(new Rect(cx, by, autoW, cardH), leftAll > 0 ? "Расставить\nза меня" : "Все полки\nна поле") && leftAll > 0) Game.AutoPlace();

            string hint = Game.Eraser ? "Нажмите на свой отряд — он вернётся в запас"
                : leftAll > 0 ? $"Нажмите на землю ниже золотой черты — поставить отряд (в запасе {leftAll})"
                : "Все полки на поле. Жмите «В бой!» — в бою можно отдавать приказы";
            var hs = new GUIStyle(center) { fontSize = Mathf.RoundToInt(13 * s) };
            var hr = new Rect(safe.x + Pad, by - 32 * s, safe.width - 2 * Pad, 24 * s);
            GUI.Label(new Rect(hr.x + 1, hr.y + 1, hr.width, hr.height), "<color=#000000cc>" + hint + "</color>", hs);
            GUI.Label(hr, hint, hs);
        }

        // ---------------------------------------------------------------- приказы в бою

        void DrawOrders()
        {
            var sel = Game.Selected;
            var cmds = new List<(string text, System.Action act, bool on)>();
            string head, sub;
            if (sel != null)
            {
                var o = sel.Order;
                head = sel.T.Hero && sel.Title != null ? $"{sel.T.Name} {sel.Title}" : Defs.Cap(sel.Name);
                string morale = sel.T.Fearless ? "не знают страха" : $"дух {Mathf.RoundToInt(sel.Morale)}%";
                string doing = o.Mode == Mode.Move && sel.Player ? (o.Kind == OrderKind.Withdraw ? "отходят" : "идут на место")
                    : o.Mode == Mode.Charge && o.Target != null ? "бьют «" + o.Target.Name + "»" : Defs.OrderText(o.Kind).ToLowerInvariant();
                sub = $"{sel.Alive} из {sel.Size} · {morale} · {(sel.Player ? "ваш приказ" : "воевода")}: {doing}";
                bool mine = sel.Player;
                cmds.Add(("В атаку", () => Game.GiveOrder(Cmd.Advance), mine && o.Mode == Mode.Advance));
                cmds.Add(("Стоять", () => Game.GiveOrder(Cmd.Hold), mine && o.Mode == Mode.Hold));
                cmds.Add(("Отступить", () => Game.GiveOrder(Cmd.Withdraw), mine && o.Kind == OrderKind.Withdraw));
                if (sel.T.Hero) cmds.Add(("На поединок", () => Game.GiveOrder(Cmd.Duel), mine && o.Mode == Mode.Charge));
                cmds.Add(("Сам", () => Game.GiveOrder(Cmd.Free), !mine));
                cmds.Add(("×", () => Game.Selected = null, false));
            }
            else
            {
                head = "Приказы";
                sub = "Коснитесь своего отряда — потом земли или врага";
                cmds.Add(("Все в атаку", () => Game.OrderAll(Cmd.Advance), false));
                cmds.Add(("Все стоять", () => Game.OrderAll(Cmd.Hold), false));
                cmds.Add(("Воле воеводы", () => Game.OrderAll(Cmd.Free), false));
            }
            float gap = 6 * s, total = 0;
            var widths = new float[cmds.Count];
            for (int i = 0; i < cmds.Count; i++)
            {
                widths[i] = cmds[i].text == "×" ? 44 * s : Mathf.Max(70 * s, btn.CalcSize(new GUIContent(cmds[i].text)).x + 22 * s);
                total += widths[i] + (i > 0 ? gap : 0);
            }
            float pw = Mathf.Max(total, 300 * s) + 16 * s, ph = BtnH + 56 * s;
            var panel = new Rect(safe.xMax - Pad - pw, safe.yMax - Pad - ph, pw, ph);
            Box(panel, new Color(0.047f, 0.06f, 0.08f, 0.88f));
            if (sel != null) Fill(new Rect(panel.x, panel.y + 8 * s, 4 * s, 40 * s), Gold);
            GUI.Label(new Rect(panel.x + 12 * s, panel.y + 4 * s, pw - 20 * s, 22 * s), $"<b>{head}</b>", label);
            GUI.Label(new Rect(panel.x + 12 * s, panel.y + 26 * s, pw - 20 * s, 20 * s), sub, small);
            float x = panel.xMax - 8 * s - total, y = panel.yMax - BtnH - 8 * s;
            for (int i = 0; i < cmds.Count; i++)
            {
                if (Button(new Rect(x, y, widths[i], BtnH), cmds[i].text, cmds[i].on)) cmds[i].act();
                x += widths[i] + gap;
            }
        }

        // ---------------------------------------------------------------- реплики

        void DrawSpeech()
        {
            var l = Game.Speech;
            if (l == null || Game.SpeechT <= 0) return;
            float a = Mathf.Clamp01(Game.SpeechT / 0.5f);
            float w = Mathf.Min(640 * s, safe.width - 2 * Pad);
            var body = new GUIStyle(caption) { alignment = TextAnchor.UpperLeft, fontStyle = FontStyle.Normal, fontSize = Mathf.RoundToInt(16 * s) };
            var content = new GUIContent("«" + l.Text + "»");
            float th = body.CalcHeight(content, w - 36 * s);
            var r = new Rect(safe.center.x - w / 2, safe.y + Pad + 100 * s, w, th + 36 * s);
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, a);
            Box(r, new Color(0.047f, 0.06f, 0.08f, 0.9f), false);
            Fill(new Rect(r.x, r.y + 6 * s, 4 * s, r.height - 12 * s), l.Team == 0 ? Blue : Red);
            var who = new GUIStyle(label) { fontSize = Mathf.RoundToInt(13 * s) };
            who.normal.textColor = l.Team == 0 ? Gold : new Color(0.95f, 0.6f, 0.52f);
            GUI.Label(new Rect(r.x + 18 * s, r.y + 6 * s, r.width - 30 * s, 20 * s), l.Who, who);
            GUI.Label(new Rect(r.x + 18 * s, r.y + 28 * s, r.width - 36 * s, th), content, body);
            GUI.color = old;
        }

        // ---------------------------------------------------------------- итог главы

        void DrawStoryResult()
        {
            var c = Game.Story;
            var run = Game.Run;
            if (c == null || run == null) { DrawResult(); return; }
            bool won = Game.Winner == 0;
            float w = Mathf.Min(640 * s, safe.width - 2 * Pad), h = Mathf.Min(540 * s, safe.height - 2 * Pad);
            var r = new Rect(safe.center.x - w / 2, safe.center.y - h / 2, w, h);
            Box(r, new Color(0.047f, 0.06f, 0.08f, 0.94f));
            var ts = new GUIStyle(title);
            ts.normal.textColor = won ? Gold : new Color(0.94f, 0.54f, 0.49f);
            GUI.Label(new Rect(r.x, r.y + 10 * s, r.width, 50 * s), won ? "Победа!" : "Поражение", ts);
            GUI.Label(new Rect(r.x, r.y + 58 * s, r.width, 22 * s), $"<color=#a59d8b>Глава {c.No}. {c.Title} · бой длился {Mathf.RoundToInt(Game.BattleTime)} с</color>", center);
            var big3 = new GUIStyle(center) { fontSize = Mathf.RoundToInt(44 * s) };
            GUI.Label(new Rect(r.x, r.y + 84 * s, r.width, 54 * s), StarRow(Game.StoryStars), big3);
            var ls = new GUIStyle(small) { fontSize = Mathf.RoundToInt(14 * s), normal = { textColor = Ink }, alignment = TextAnchor.UpperLeft };
            string goals = $"{Star(won)} победа\n{Star(won && run.GoalMet())} {c.GoalText}\n{Star(won && run.Losses < 0.5f)} потери меньше половины (потери {Mathf.RoundToInt(run.Losses * 100)}%)";
            GUI.Label(new Rect(r.x + 40 * s, r.y + 142 * s, r.width - 80 * s, 70 * s), goals, ls);
            var ep = new GUIStyle(ls) { wordWrap = true };
            ep.normal.textColor = new Color(0.86f, 0.8f, 0.66f);
            GUI.Label(new Rect(r.x + 40 * s, r.y + 220 * s, r.width - 80 * s, h - 220 * s - BtnH - 30 * s), won ? c.Win : c.Lose, ep);

            float bw = (r.width - 40 * s - 16 * s) / 3, by = r.yMax - BtnH - 16 * s, bx = r.x + 20 * s;
            bool last = c.No >= Campaign.Chapters.Length;
            if (won)
            {
                if (Button(new Rect(bx, by, bw, BtnH), last ? "К главам" : "Дальше", false, btnGold, Brass)) { if (last) Game.OpenChapters(); else Game.NextChapter(); }
                if (Button(new Rect(bx + bw + 8 * s, by, bw, BtnH), "Ещё раз")) Game.StartBattle();
                if (Button(new Rect(bx + 2 * (bw + 8 * s), by, bw, BtnH), "Главы")) Game.OpenChapters();
            }
            else
            {
                if (Button(new Rect(bx, by, bw, BtnH), "Ещё раз", false, btnGold, Brass)) Game.StartBattle();
                if (Button(new Rect(bx + bw + 8 * s, by, bw, BtnH), "Расстановка")) Game.StopBattle();
                if (Button(new Rect(bx + 2 * (bw + 8 * s), by, bw, BtnH), "Главы")) Game.OpenChapters();
            }
        }
    }
}
