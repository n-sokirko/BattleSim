using BattleSim.Core;
using UnityEngine;

namespace BattleSim
{
    /// <summary>Интерфейс героя: полоски здоровья и опыта, джойстик слева, четыре круглые кнопки способностей справа.</summary>
    public partial class Hud
    {
        Texture2D circle;

        void Disc(Vector2 c, float r, Color col)
        {
            if (circle == null) circle = MakeRound(64, 32);
            GUI.DrawTexture(new Rect(c.x - r, c.y - r, 2 * r, 2 * r), circle, ScaleMode.StretchToFill, true, 0, col, 0, 0);
        }

        Vector2 ToGui(Vector2 p) => new Vector2(p.x, Screen.height - p.y);

        void DrawHeroPad()
        {
            var b = Game.Battle;
            var pad = Game.Pad;
            var h = b.PlayerHero;
            if (h == null) return;
            var sb = pad.SafeBL();

            // --- герой: имя, уровень, здоровье, опыт
            float w = Mathf.Min(330 * s, safe.width - 2 * Pad);
            var r = new Rect(safe.x + Pad, safe.y + Pad + 94 * s, w, 58 * s);
            Box(r, Panel);
            var nm = new GUIStyle(label) { fontSize = Mathf.RoundToInt(15 * s) };
            string who = h.Squad.Title ?? "Герой";
            GUI.Label(new Rect(r.x + 10 * s, r.y + 3 * s, r.width - 20 * s, 22 * s), $"<b>{who}</b>  <color=#f2c955>ур. {b.HeroLevel}</color>", nm);
            float hp = h.Alive ? Mathf.Clamp01(h.Hp / h.T.Hp) : 0;
            var bar = new Rect(r.x + 10 * s, r.y + 28 * s, r.width - 20 * s, 12 * s);
            Fill(bar, new Color(1, 1, 1, 0.1f));
            Fill(new Rect(bar.x, bar.y, bar.width * hp, bar.height), hp > 0.5f ? new Color(0.36f, 0.72f, 0.38f) : hp > 0.25f ? new Color(0.9f, 0.7f, 0.2f) : new Color(0.87f, 0.31f, 0.25f));
            var xb = new Rect(r.x + 10 * s, r.y + 44 * s, r.width - 20 * s, 5 * s);
            Fill(xb, new Color(1, 1, 1, 0.08f));
            Fill(new Rect(xb.x, xb.y, xb.width * Mathf.Clamp01(b.XpFrac), xb.height), Brass);

            if (b.CampMode)
            { // задача и сколько лагерей осталось
                var q = new Rect(r.x, r.yMax + 6 * s, w, 44 * s);
                Box(q, Panel);
                string goal = b.BossName != null ? $"Победить: {b.BossName}" : Game.Story?.GoalText ?? "";
                GUI.Label(new Rect(q.x + 10 * s, q.y + 2 * s, q.width - 20 * s, 22 * s), $"<b>{goal}</b>", nm);
                GUI.Label(new Rect(q.x + 10 * s, q.y + 22 * s, q.width - 20 * s, 18 * s), $"Лагерей осталось: {b.CampsLeft} из {b.Camps.Count}", small);
            }
            if (!h.Alive)
            {
                var dead = new GUIStyle(caption) { fontSize = Mathf.RoundToInt(22 * s) };
                dead.normal.textColor = new Color(0.94f, 0.54f, 0.49f);
                GUI.Label(new Rect(safe.center.x - 150 * s, safe.yMax - 160 * s, 300 * s, 40 * s), "Герой пал", dead);
                return;
            }

            // --- джойстик (виден всегда: подсказка, где его брать)
            if (Application.isMobilePlatform || pad.Active)
            {
                var zone = pad.Zone(sb);
                var basePos = pad.Active ? pad.Base : new Vector2(zone.x + zone.width * 0.45f, zone.y + 105 * s);
                var knobPos = pad.Active ? pad.Knob : basePos;
                var bc = ToGui(basePos);
                Disc(bc, pad.Reach, new Color(1, 1, 1, pad.Active ? 0.16f : 0.09f));
                Disc(bc, pad.Reach - 3 * s, new Color(0.05f, 0.06f, 0.08f, pad.Active ? 0.4f : 0.25f));
                Vector2 off = pad.Active ? pad.Stick * pad.Reach * 0.9f : Vector2.zero;
                Disc(ToGui(basePos + off), pad.Reach * 0.42f, new Color(0.84f, 0.64f, 0.25f, pad.Active ? 0.75f : 0.4f));
            }

            // --- способности
            var st = new GUIStyle(caption) { fontSize = Mathf.RoundToInt(13 * s) };
            var sec = new GUIStyle(caption) { fontSize = Mathf.RoundToInt(22 * s) };
            for (int i = 0; i < 4; i++)
            {
                var c = ToGui(pad.ButtonCenter(i, sb));
                float rad = pad.Radius;
                bool open = b.AbilityOpen(i);
                float left = b.AbilityLeft(i);
                bool ready = open && left <= 0;
                Disc(c, rad, ready ? (pad.Held(i) ? Color.white : Brass) : new Color(0.3f, 0.3f, 0.32f, 0.9f));
                Disc(c, rad - 3.5f * s, pad.Held(i) ? new Color(0.45f, 0.35f, 0.12f, 0.95f) : new Color(0.05f, 0.06f, 0.08f, 0.9f));
                st.normal.textColor = ready ? Ink : Muted;
                if (!open)
                {
                    GUI.Label(new Rect(c.x - rad, c.y - rad * 0.55f, 2 * rad, rad), HeroPad.Short[i], st);
                    GUI.Label(new Rect(c.x - rad, c.y + 2 * s, 2 * rad, rad * 0.7f), $"ур. {Battle.AbilityLevel[i]}", st);
                }
                else if (left > 0)
                {
                    sec.normal.textColor = Ink;
                    GUI.Label(new Rect(c.x - rad, c.y - rad * 0.55f, 2 * rad, rad * 1.1f), Mathf.CeilToInt(left).ToString(), sec);
                    GUI.Label(new Rect(c.x - rad, c.y + rad * 0.45f, 2 * rad, rad * 0.5f), HeroPad.Short[i], st);
                }
                else
                {
                    GUI.Label(new Rect(c.x - rad, c.y - rad * 0.4f, 2 * rad, rad * 0.8f), HeroPad.Short[i], st);
                    var kb = new GUIStyle(st) { fontSize = Mathf.RoundToInt(11 * s) };
                    kb.normal.textColor = Muted;
                    if (!Application.isMobilePlatform) GUI.Label(new Rect(c.x - rad, c.y + rad * 0.35f, 2 * rad, rad * 0.5f), (i + 1).ToString(), kb);
                }
            }
        }
    }
}
