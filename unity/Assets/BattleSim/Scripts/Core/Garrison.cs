using System;
using System.Collections.Generic;

namespace BattleSim.Core
{
    /// <summary>
    /// Гарнизоны: отряд, который держит место рядом с домом, при подходе врага засаживается в него. Внутри бойцов
    /// не видно и стрелы их не берут; стрелки бьют из окон, пехота рубится с теми, кто подошёл к стенам. Враг штурмует
    /// (удары у стен проходят вполсилы), жжёт деревянный дом огненными стрелами и бомбами. Загорелся или рухнул —
    /// гарнизон выбегает (рухнул — с увечьями). Получил другой приказ — выходит и идёт.
    /// </summary>
    public sealed partial class Battle
    {
        float garrisonT;

        /// <summary>Сколько бойцов влезает: по площади дома (~2,2 м² на бойца), не меньше четырёх.</summary>
        public static int CapOf(EnvObj o) => Math.Max(4, (int)(4 * o.Hx * o.Hz / 2.2f));

        /// <summary>В чём можно засесть: целый дом со стенами (не колодец, не рыночный навес, не развалины), не горит.</summary>
        public static bool Garrisonable(EnvObj o)
        {
            if (o.Kind != EnvKind.House || o.Building == null || !o.Rect || o.MaxHp <= 0 || o.State == EnvState.Ruined || o.Fire > 0) return false;
            string k = o.Building.Def.Kind;
            return k != "well" && k != "market" && k != "destroyed";
        }

        static string Where(EnvObj o)
        {
            switch (o.Building?.Def.Kind)
            {
                case "church": return "в церкви";
                case "tavern": return "в корчме";
                case "blacksmith": return "в кузне";
                case "tower_B": return "в башне";
                default: return "в доме";
            }
        }

        /// <summary>Раз в шаг — кого выкурили; раз в секунду — кто засаживается.</summary>
        void UpdateGarrisons(float dt)
        {
            foreach (var sq in Squads)
            {
                var h = sq.House;
                if (h == null) continue;
                var o = sq.Order;
                bool far = o.HasPos && h.Dist(o.X, o.Z) > 18;
                if (h.State == EnvState.Ruined) Release(sq, $"{sq.Name}: дом рухнул на головы засевшим", true);
                else if (h.Fire > 0.6f) Release(sq, $"{sq.Name}: выкурили огнём из дома", false);
                else if (sq.Alive == 0 || o.Mode == Mode.Rout || o.Mode != Mode.Hold || far) Release(sq, null, false);
            }
            if ((garrisonT -= dt) > 0) return;
            garrisonT = 1;
            if (World.Town == null) return;
            foreach (var sq in Squads)
            {
                if (sq.House != null || sq.Special || sq.T.Mount || sq.Alive < 3 || sq.Order.Mode != Mode.Hold || sq.Front != null || sq.FoeDist > 70) continue;
                var o = sq.Order;
                float px = o.HasPos ? o.X : sq.Center.x, pz = o.HasPos ? o.Z : sq.Center.z;
                EnvObj best = null;
                float bestS = float.MaxValue;
                // уже ведёт огонь — не бросает линию ради дома дальше нескольких шагов
                float reach = Time - sq.LastShotT < 3 ? 5 : 12;
                foreach (var e in World.Env.Near(px, pz, reach))
                {
                    if (!Garrisonable(e) || e.Holder != null || MathF.Abs(e.Y - sq.Center.y) > 2.5f) continue;
                    float sc = e.Dist(px, pz) + (e.Mat == EnvMat.Stone ? -4 : 0); // каменный лучше: не сгорит
                    if (sc < bestS) { bestS = sc; best = e; }
                }
                if (best == null) continue;
                sq.House = best;
                best.Holder = sq;
                AddLog(sq.Team, $"{sq.Name} засели {Where(best)}");
            }
        }

        // ------------------------------------------------------------ ворота

        float gateT;
        readonly HashSet<EnvObj> gateBroken = new HashSet<EnvObj>();

        /// <summary>
        /// Ворота крепости раз в секунду: обороняющиеся (чья армия ближе к середине города) запирают их, когда враг
        /// ближе 35 м, а в проёме никого нет. Выбили — в летопись.
        /// </summary>
        void UpdateGates(float dt)
        {
            if (World.Town == null || (gateT -= dt) > 0) return;
            gateT = 1;
            int def = -1;
            if (ArmyC[0] != null && ArmyC[1] != null) def = ArmyC[0].Value.x * ArmyC[0].Value.x + ArmyC[0].Value.z * ArmyC[0].Value.z < ArmyC[1].Value.x * ArmyC[1].Value.x + ArmyC[1].Value.z * ArmyC[1].Value.z ? 0 : 1;
            foreach (var g in World.Env.All)
            {
                if (g.Kind != EnvKind.Gate) continue;
                if (g.State == EnvState.Ruined)
                {
                    if (gateBroken.Add(g)) AddLog(def < 0 ? 0 : 1 - def, g.Arch != null && g.Arch.Citadel ? "Ворота замка выбиты!" : "Городские ворота выбиты!");
                    continue;
                }
                if (g.Closed || def < 0) continue;
                bool foe = false, busy = false;
                foreach (var u in Units)
                {
                    if (!u.Alive) continue;
                    float d = g.Dist(u.Pos.x, u.Pos.z);
                    if (d < u.T.Radius + 1) { busy = true; break; }
                    if (u.Team != def && d < 35 && u.T.Special == Special.None) foe = true;
                }
                if (!foe || busy) continue;
                World.Env.SetGate(g, true, this);
                AddLog(def, g.Arch != null && g.Arch.Citadel ? "Ворота замка заперты" : "Городские ворота заперты");
            }
        }

        /// <summary>Отряд покидает дом: все выходят к двери (рухнул — с увечьями), дом свободен.</summary>
        void Release(Squad sq, string why, bool collapsed)
        {
            var h = sq.House;
            sq.House = null;
            if (h == null) return;
            h.Holder = null;
            for (int i = h.Occupants.Count - 1; i >= 0; i--)
            {
                var u = h.Occupants[i];
                Exit(u);
                if (collapsed && u.Alive) Damage(u, 35, 0, 0, false, null);
            }
            if (why != null && sq.Alive > 0) AddLog(sq.Team, why);
        }

        void Enter(Unit u, EnvObj h)
        {
            u.Inside = h;
            h.Occupants.Add(u);
            // внутри — где-нибудь в пятне дома (не рисуется; отсюда — центр отряда, знамя, цель для врага)
            float c = MathF.Cos(h.Rot), s = MathF.Sin(h.Rot), lx = (Rng.Rand() * 2 - 1) * MathF.Max(0.2f, h.Hx - 0.8f), lz = (Rng.Rand() * 2 - 1) * MathF.Max(0.2f, h.Hz - 0.8f);
            u.Pos = new V3(h.X + lx * c - lz * s, h.Y + 0.2f, h.Z + lx * s + lz * c);
            u.PrevPos = u.Pos;
            u.Vel = new V3(0, 0, 0); u.Knock = new V3(0, 0, 0); u.CurSpeed = 0;
            u.Path = null; u.Settled = true; u.BlockT = 0;
        }

        void Exit(Unit u)
        {
            var h = u.Inside;
            if (h == null) return;
            h.Occupants.Remove(u);
            u.Inside = null;
            // к двери со стороны, куда отряду надо (его точка) — или просто наружу
            var o = u.Squad?.Order;
            V2 to = o != null && o.HasPos ? new V2(o.X, o.Z) : new V2(h.X + (u.Pos.x - h.X) * 3, h.Z + (u.Pos.z - h.Z) * 3);
            var d = h.Edge(to.x, to.z, u.T.Radius + 0.4f);
            var p = World.WalkableNear(d.x, d.z, u.T.Radius, h.Y);
            u.Pos = new V3(p.x, World.GroundAt(p.x, p.z), p.z);
            u.PrevPos = u.Pos;
        }

        /// <summary>
        /// Боец отряда-гарнизона: снаружи — идёт к двери и входит (пока есть место); внутри — стреляет из окна или
        /// рубится с теми, кто у стены. true — ход бойца на этом закончен.
        /// </summary>
        bool ThinkGarrison(Unit u, float dt)
        {
            var sq = u.Squad;
            var h = u.Inside;
            if (h != null)
            {
                if (sq.House != h) { Exit(u); return false; }
                ThinkInside(u, h, dt);
                AttackStep(u, dt);
                return true;
            }
            h = sq.House;
            if (h == null || h.Occupants.Count >= CapOf(h) || u.Engaged || u.IsLeader) return false;
            var door = h.Edge(u.Pos.x, u.Pos.z, u.T.Radius + 0.3f);
            float dd = M.Hypot(door.x - u.Pos.x, door.z - u.Pos.z);
            AttackStep(u, dt);
            if (dd < 0.9f) { Enter(u, h); return true; }
            var w = dd > 3 ? NavTarget(u, door.x, door.z) : door;
            float wx = w.x - u.Pos.x, wz = w.z - u.Pos.z, wl = M.Hypot(wx, wz);
            if (wl < 1e-3f) wl = 1;
            Steer(u, wx / wl * u.T.Speed, wz / wl * u.T.Speed, dt, true);
            return true;
        }

        void ThinkInside(Unit u, EnvObj h, float dt)
        {
            var t = u.T;
            u.Vel = new V3(0, 0, 0); u.CurSpeed = 0;
            if (u.Target == null || !u.Target.Alive || u.RetargetT <= 0)
            {
                u.RetargetT = 0.5f + Rng.Rand() * 0.4f;
                u.Target = PickTarget(u);
            }
            var tg = u.Target;
            if (tg == null) return;
            float nx = tg.Pos.x - u.Pos.x, nz = tg.Pos.z - u.Pos.z;
            if (t.Ranged)
            { // из окна (и по чужому гарнизону в доме напротив — огненными, по стене)
                if (M.Hypot(nx, nz) <= RangeOf(u, tg) + 2 && CanSee(u, tg))
                {
                    u.Aiming = true;
                    u.Face(nx, nz, dt);
                    if (u.Cooldown <= 0 && u.AtkT < 0) StartAttack(u, true);
                    return;
                }
            }
            // у двери и окон — рубим подошедших к стене
            if (tg.Inside == null && h.Dist(tg.Pos.x, tg.Pos.z) <= t.Reach + tg.T.Radius + 0.6f && MathF.Abs(tg.Pos.y - h.Y) < 2.5f)
            {
                u.Engaged = true;
                u.Face(nx, nz, dt);
                if (u.Cooldown <= 0 && u.AtkT < 0 && t.Dmg > 0) StartAttack(u, false);
            }
        }

        /// <summary>Расстояние для рукопашной: до засевшего в доме — до стены дома (он бьётся у двери и окон).</summary>
        static float MeleeDist(Unit u, Unit tg)
        {
            if (tg.Inside != null) return tg.Inside.Dist(u.Pos.x, u.Pos.z) + tg.T.Radius;
            if (u.Inside != null) return u.Inside.Dist(tg.Pos.x, tg.Pos.z) + u.T.Radius;
            return M.Hypot(tg.Pos.x - u.Pos.x, tg.Pos.z - u.Pos.z);
        }

        /// <summary>Откуда смотреть и куда: из дома — от окна со стороны цели, на засевшего — на стену его дома.</summary>
        void SightLine(Unit u, Unit tg, out V3 a, out V3 b)
        {
            a = new V3(u.Pos.x, u.Pos.y + 1.5f, u.Pos.z);
            b = new V3(tg.Pos.x, tg.Pos.y + 1.0f, tg.Pos.z);
            if (u.Inside != null)
            {
                var w = u.Inside.Edge(tg.Pos.x, tg.Pos.z, 0.35f);
                a = new V3(w.x, u.Inside.Y + MathF.Min(u.Inside.Top * 0.45f, 3), w.z);
            }
            if (tg.Inside != null)
            {
                var w = tg.Inside.Edge(a.x, a.z, 0.35f);
                b = new V3(w.x, tg.Inside.Y + MathF.Min(tg.Inside.Top * 0.45f, 3), w.z);
            }
        }
    }
}
