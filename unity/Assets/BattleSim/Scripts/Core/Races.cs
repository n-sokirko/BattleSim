using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleSim.Core
{
    /// <summary>
    /// Особенности рас: стена щитов Руси, колдуны Нави (поднимают павших, ужас нежити),
    /// ложное отступление и «карусель» конных лучников Степи.
    /// </summary>
    public sealed partial class Battle
    {
        float terrorT;

        void UpdateRaces(float dt)
        {
            bool terror = (terrorT -= dt) <= 0;
            if (terror) terrorT = 0.5f;
            for (int i = 0, n = Squads.Count; i < n; i++) // колдуны добавляют отряды — новые подождут до следующего шага
            {
                var sq = Squads[i];
                if (sq.Special || sq.Alive == 0) continue;
                if (sq.T.ShieldWall) UpdateShieldWall(sq);
                if (sq.T.Raise && sq.Order.Mode != Mode.Rout) UpdateRaise(sq, dt);
                if (sq.Master != null && sq.Master.Alive == 0) Crumble(sq);
                if (Races[sq.Team].Feign && sq.T.Mount && !sq.T.Ranged) TryFeign(sq);
                if (terror && Races[sq.Team].Undead && sq.Order.Mode != Mode.Rout) Terror(sq);
            }
            DuelRing();
            if (terror)
                foreach (var h in Heroes) // рядом с богатырём и дух крепче
                    if (h != null && h.Alive)
                        foreach (var q in Squads)
                            if (q.Team == h.Team && !q.Special && q.Alive > 0 && q.Order.Mode != Mode.Rout && D2d(q.Center, h.Pos) < 14) q.Morale = MathF.Min(100, q.Morale + 0.6f);
        }

        // ---------------------------------------------------------------- Русь: стена щитов

        /// <summary>
        /// Под обстрелом или перед натиском конницы мечники смыкают щиты: болты почти не берут спереди,
        /// натиск вязнет в упёртом строю, но и шагают вдвое медленнее. В рукопашной — размыкают.
        /// </summary>
        void UpdateShieldWall(Squad sq)
        {
            var o = sq.Order;
            bool can = o.Mode != Mode.Rout && !sq.Engaged && sq.Alive >= sq.Size * 0.4f && sq.Choke == null;
            bool shot = Time - sq.LastHitT < 2.5f && sq.FoeDist > 10;
            bool cav = false;
            if (can && !shot)
                foreach (var e in sq.Foes)
                    if (e.T.Mount && !e.T.Ranged && e.Alive > 0 && e.Order.Mode != Mode.Rout && D2d(e.Center, sq.Center) < 26
                        && (e.Order.Mode == Mode.Charge || e.Order.Mode == Mode.Advance)) { cav = true; break; }
            if (can && (shot || cav))
            {
                sq.WallUntil = Time + 4;
                if (!sq.ShieldWall)
                {
                    sq.ShieldWall = true;
                    Emit(FxKind.Wall, sq.Center, MathF.Sin(sq.Facing), MathF.Cos(sq.Facing), sq.Team);
                    if (Time - sq.ShieldT > 20) AddLog(sq.Team, $"{Defs.Cap(sq.Name)} смыкают щиты{(cav ? " — ждут натиска" : " под обстрелом")}");
                    sq.ShieldT = Time;
                }
            }
            else if (sq.ShieldWall && (!can || Time > sq.WallUntil)) sq.ShieldWall = false;
        }

        // ---------------------------------------------------------------- Навь: колдуны и ужас

        /// <summary>
        /// Колдуны раз в несколько секунд поднимают павших (своих и чужих) в радиусе 15 м — по одному на живого колдуна.
        /// Поднятые встают из земли и идут в бой отдельным отрядом; гибнут колдуны — отряд рассыпается прахом.
        /// </summary>
        void UpdateRaise(Squad sq, float dt)
        {
            if ((sq.RaiseT -= dt) > 0) return;
            sq.RaiseT = 8f + Rng.Rand() * 2;
            int budget = Math.Min(3, sq.Alive);
            var dead = Races[sq.Team].Units[0];
            // свой клип играется как есть, запасной Lie_StandUp — в 1,2 раза быстрее (см. Animate)
            float own = ClipDur != null ? ClipDur(dead.Id, "Rise_Undead") : 0;
            float riseDur = own > 0 ? own : (ClipDur != null ? ClipDur(dead.Id, "Lie_StandUp") : 0) / 1.2f;
            if (riseDur <= 0) riseDur = 2;
            int raised = 0;
            for (int i = 0; i < Units.Count && raised < budget; i++)
            {
                var c = Units[i];
                if (c.Alive || c.Gone || c.Flying || c.T.Mount || c.T.Special != Special.None || c.DeadT < 1.2f || c.DeadT > 20) continue;
                if (D2d(c.Pos, sq.Center) > 15 || MathF.Abs(c.Pos.y - sq.Center.y) > 4 || World.TooDeep(c.Pos.x, c.Pos.z)) continue; // утопленника не поднять
                if (MathF.Abs(World.GroundAt(c.Pos.x, c.Pos.z) - c.Pos.y) > 0.5f) continue; // лежит под мостом или стеной — встал бы на настил над собой
                if (Units.Count >= MaxUnits) break;
                if (sq.Raised == null)
                {
                    var rs = new Squad((++squadSeq).ToString(), dead.Id, sq.Team, sq.Yaw) { Master = sq, Order = new Order(OrderKind.Advance, Mode.Advance) { Why = "подняты колдунами" } };
                    rs.OrderT = Time;
                    Squads.Add(rs);
                    sq.Raised = rs;
                    NumberSquads();
                }
                var r = sq.Raised;
                var plan = new UnitPlan { Type = dead.Id, Team = sq.Team, X = c.Pos.x, Z = c.Pos.z, Yaw = c.Yaw, Variant = 0, Squad = -1 };
                var nu = new Unit(plan, World) { Squad = r, Risen = true };
                nu.PrevPos = nu.Pos; nu.PrevYaw = nu.Yaw;
                nu.Hp = dead.Hp * 0.55f;
                nu.DownT = riseDur; nu.DownAnim = 3; // встаёт из земли (анимация — в Animate)
                c.Gone = true;
                r.Units.Add(nu); r.Size++; r.Cells = null;
                Units.Add(nu);
                Emit(FxKind.Raise, c.Pos, 0, 0, sq.Team);
                raised++;
            }
            if (raised == 0) { sq.RaiseT = 2.5f; return; }
            foreach (var u in sq.Units) if (u.Alive && u.AtkT < 0) u.CastNew = true;
            if (Time - sq.RaiseLogT > 25)
            {
                sq.RaiseLogT = Time;
                AddLog(sq.Team, $"{Defs.Cap(sq.Name)} поднимают павших — мертвецы встают в строй!");
            }
            // живые враги рядом видят, как встают мертвецы
            foreach (var e in sq.Foes)
                if (e.Alive > 0 && !e.T.Fearless && D2d(e.Center, sq.Center) < 30) e.Morale -= 1.5f * raised;
        }

        /// <summary>Колдуны пали — их мертвецы валятся наземь и рассыпаются.</summary>
        void Crumble(Squad sq)
        {
            var m = sq.Master;
            sq.Master = null;
            if (m.Raised == sq) m.Raised = null;
            int n = 0;
            foreach (var u in sq.Units)
                if (u.Alive) { u.LastKnock = new V3(0, 0, 0); Kill(u); n++; }
            if (n > 0) AddLog(sq.Team, $"{Defs.Cap(m.Name)} пали — поднятые ими мертвецы рассыпаются прахом ({n})");
        }

        /// <summary>Рядом с нежитью у живых тает дух (кроме бесстрашных); вблизи костяных всадников — сильнее.</summary>
        void Terror(Squad sq)
        {
            float r = sq.T.Terror ? 16 : 10, hit = sq.T.Terror ? 0.35f : 0.12f;
            foreach (var e in sq.Foes)
                if (e.Alive > 0 && !e.T.Fearless && !Races[e.Team].Undead && D2d(e.Center, sq.Center) < r && Time >= RoarUntil[e.Team])
                    e.Morale -= hit;
        }

        // ---------------------------------------------------------------- Степь: ложное отступление и карусель

        /// <summary>
        /// Конница Степи, увязнув в рубке, «бежит» — враг бросается в погоню и растягивается, — а через
        /// несколько секунд разворачивается и бьёт преследователей с разбега.
        /// </summary>
        void TryFeign(Squad sq)
        {
            var o = sq.Order;
            if (sq.Feigning || o.Mode == Mode.Rout || !sq.Engaged || sq.EngagedFor < 4f || Time < 15 || Time - sq.FeignT < 40) return;
            if (sq.Morale > 75 && sq.Alive > sq.Size * 0.75f) return;
            if (sq.Morale < sq.T.RoutAt + 8) return; // уже не хитрость — настоящее бегство
            sq.Feigning = true; sq.FeignT = Time; sq.FeignUntil = Time + 4.5f + Rng.Rand() * 2;
            ApplyOrder(sq, new Order(OrderKind.Rout, Mode.Rout) { Why = "ложное отступление" });
            sq.Pending = null;
            AddLog(sq.Team, $"{Defs.Cap(sq.Name)} обратились в бегство…");
        }

        /// <summary>Конец ложного бегства: разворот и удар по ближайшей погоне.</summary>
        void EndFeign(Squad sq)
        {
            sq.Feigning = false;
            Squad best = null; float bd = 40;
            foreach (var e in Squads)
            {
                if (e.Team == sq.Team || e.Special || e.Alive == 0) continue;
                float d = D2d(e.Center, sq.Center);
                bool chasing = e.Order.Mode == Mode.Charge && e.Order.Target == sq;
                if (chasing) { e.Morale -= 12; e.FirstStrikeT = -99; d *= 0.5f; }
                if (d < bd) { bd = d; best = e; }
            }
            sq.Morale = MathF.Max(sq.Morale, 80);
            sq.RushUntil = Time + 5;
            foreach (var u in sq.Units) if (u.Alive) u.ChargeT = 1; // сразу с разбега
            if (best != null)
            {
                ApplyOrder(sq, new Order(OrderKind.Charge, Mode.Charge) { Target = best, Why = "ложное отступление" });
                AddLog(sq.Team, $"Хитрость! {Defs.Cap(sq.Name)} разворачиваются и бьют погоню ({best.Name})");
            }
            else ApplyOrder(sq, new Order(OrderKind.Advance, Mode.Advance));
        }

        /// <summary>
        /// Конный лучник: пешего врага близко — уходит, отстреливаясь через плечо; враг дальше выстрела — подъезжает;
        /// на дистанции — кружит («карусель»), стреляя на скаку. Возвращает false, если врагов нет (тогда — в строй).
        /// </summary>
        bool Skirmish(Unit u, Unit tg, float dt, out float dx, out float dz)
        {
            dx = 0; dz = 0;
            if (tg == null) return false;
            var t = u.T;
            float tx = tg.Pos.x - u.Pos.x, tz = tg.Pos.z - u.Pos.z, d = M.Hypot(tx, tz);
            if (d < 1e-3f) d = 1e-3f;
            float nx = tx / d, nz = tz / d, range = RangeOf(u, tg);
            if (d > range + 25) return false;
            var th = GridNearest(u, 13, false);
            float sp = t.Speed;
            float side = (u.Squad.Num & 1) == 0 ? 1 : -1;
            if (th != null && !th.T.Ranged && th.Squad != null && th.Squad.Order.Mode != Mode.Rout)
            { // уходим от погони, забирая к своим
                float ax = u.Pos.x - th.Pos.x, az = u.Pos.z - th.Pos.z, al = M.Hypot(ax, az);
                if (al < 1e-3f) al = 1e-3f;
                float home = u.Team == 0 ? -1 : 1;
                dx = ax / al + nz * side * 0.35f; dz = az / al - nx * side * 0.35f + home * 0.25f;
            }
            else if (d > range * 0.85f) { dx = nx; dz = nz; }                     // подъехать на выстрел
            else if (d < range * 0.5f) { dx = -nx + nz * side * 0.6f; dz = -nz - nx * side * 0.6f; } // слишком близко — отъехать
            else { dx = nz * side + nx * 0.15f; dz = -nx * side + nz * 0.15f; sp *= 0.7f; } // кружить
            // к краю поля не прижиматься
            float lim = World.Field - 10;
            if (MathF.Abs(u.Pos.x) > lim) dx -= M.Sign(u.Pos.x) * 1.2f;
            if (MathF.Abs(u.Pos.z) > lim) dz -= M.Sign(u.Pos.z) * 1.2f;
            float l = M.Hypot(dx, dz);
            if (l < 1e-3f) { dx = 0; dz = 0; }
            else
            {
                var w = NavTarget(u, u.Pos.x + dx / l * 8, u.Pos.z + dz / l * 8);
                float wx = w.x - u.Pos.x, wz = w.z - u.Pos.z, wl = M.Hypot(wx, wz);
                if (wl < 1e-3f) wl = 1;
                dx = wx / wl * sp; dz = wz / wl * sp;
            }
            if (d <= range && CanSee(u, tg) && u.Cooldown <= 0 && u.AtkT < 0) StartAttack(u, true);
            return true;
        }

        // ---------------------------------------------------------------- богатыри и поединки

        public readonly Unit[] Heroes = new Unit[2];

        /// <summary>Богатырь выходит перед строем своей армии (в начале боя, как и полководец).</summary>
        void SpawnHero(int team)
        {
            var hd = Races[team].Hero;
            var mine = Squads.Where(s => s.Team == team && !s.Special && s.Alive > 0).ToList();
            if (hd == null || mine.Count < 3 || HeroTitle[team] == "") return;
            var c = Commander.Center(mine).Value;
            float fwd = team == 0 ? 1 : -1, yaw = team == 0 ? 0 : M.PI;
            // перед центром, на свободном месте
            V2 p = c;
            for (int k = 0; k < 24; k++)
            {
                float a = k * 2.4f, r = 2 + k * 0.8f;
                var q = World.ClampField(c.x + MathF.Cos(a) * r, c.z + fwd * 8 + MathF.Sin(a) * r * 0.5f, 4);
                if (World.Walkable(q.x, q.z, hd.Radius) && !Occupied(q.x, q.z, hd.Radius)) { p = q; break; }
            }
            var u = new Unit(new UnitPlan { Type = hd.Id, Team = team, X = p.x, Z = p.z, Yaw = yaw, Squad = -1 }, World);
            u.PrevPos = u.Pos; u.PrevYaw = u.Yaw;
            var names = Races[team].HeroNames;
            var sq = new Squad("hero" + team, hd.Id, team, yaw) { Title = HeroTitle[team] ?? names[(int)(Rng.Rand() * names.Length)] };
            sq.Units.Add(u); u.Squad = sq; sq.Size = 1;
            Units.Add(u); Squads.Add(sq);
            Heroes[team] = u;
            AddLog(team, $"{hd.Name} {sq.Title} выходит перед строем {(team == 0 ? "синих" : "красных")}");
        }

        /// <summary>Богатырь высматривает вражеского богатыря: сошлись вплотную — поединок.</summary>
        Unit HeroTarget(Unit u)
        {
            if (u.Duel != null) return u.Duel.Alive ? u.Duel : null;
            var e = Heroes[1 - u.Team];
            if (e == null || !e.Alive || e.Duel != null || e.Squad.Order.Mode == Mode.Rout) return null;
            float d = D2d(u.Pos, e.Pos);
            if (d > 28 || MathF.Abs(e.Pos.y - u.Pos.y) > 3) return null;
            if (d < 5.5f)
            {
                u.Duel = e; e.Duel = u;
                var mid = new V3((u.Pos.x + e.Pos.x) / 2, (u.Pos.y + e.Pos.y) / 2, (u.Pos.z + e.Pos.z) / 2);
                Emit(FxKind.Duel, mid, e.Pos.x - u.Pos.x, e.Pos.z - u.Pos.z, u.Team);
                AddLog(u.Team, $"Поединок! {u.Squad.Title} и {e.Squad.Title} сошлись один на один — войска расступаются");
            }
            return e;
        }

        /// <summary>Вокруг поединка — круг: чужие и свои отходят, не мешают.</summary>
        void DuelRing()
        {
            var a = Heroes[0];
            if (a == null || !a.Alive || a.Duel == null || !a.Duel.Alive) return;
            var b = a.Duel;
            float mx = (a.Pos.x + b.Pos.x) / 2, mz = (a.Pos.z + b.Pos.z) / 2, R = 5.5f;
            CellOf(mx, mz, out int cx, out int cz);
            int rr = (int)MathF.Ceiling(R / GRID);
            for (int z = Math.Max(cz - rr, 0); z <= Math.Min(cz + rr, gridDim - 1); z++)
                for (int x = Math.Max(cx - rr, 0); x <= Math.Min(cx + rr, gridDim - 1); x++)
                    for (int j = head[z * gridDim + x]; j >= 0; j = next[j])
                    {
                        if (j >= Units.Count) continue;
                        var e = Units[j];
                        if (!e.Alive || e == a || e == b || e.T.Mount) continue;
                        float ex = e.Pos.x - mx, ez = e.Pos.z - mz, d = M.Hypot(ex, ez);
                        if (d > R || d < 1e-3f) continue;
                        float k = (R - d) / R * 0.35f;
                        e.Knock.x += ex / d * k; e.Knock.z += ez / d * k;
                        if (e.Target == a || e.Target == b) { e.Target = null; e.RetargetT = 0; }
                    }
        }

        /// <summary>Богатырь пал: свои в смятении, чужие воспряли; победитель поединка — герой дня.</summary>
        void HeroFell(Unit u)
        {
            Emit(FxKind.Hero, u.Pos, 0, 0, u.Team);
            var w = u.Duel;
            if (w != null) { w.Duel = null; u.Duel = null; }
            foreach (var q in Squads)
            {
                if (q.Special || q.Alive == 0 || q.T.Fearless) continue;
                if (D2d(q.Center, u.Pos) > 45) continue;
                if (q.Team == u.Team) q.Morale -= 14; else q.Morale = MathF.Min(100, q.Morale + 10);
            }
            if (w != null && w.Alive)
            {
                w.Hp = MathF.Min(w.T.Hp, w.Hp + w.T.Hp * 0.25f);
                AddLog(w.Team, $"{w.Squad.Title} победил в поединке — {u.Squad.Title} повержен! {(w.Team == 0 ? "Синие" : "Красные")} ликуют");
                foreach (var q in Squads)
                    if (q.Team == w.Team && !q.Special && q.Alive > 0 && D2d(q.Center, w.Pos) < 30 && q.Order.Mode != Mode.Rout) q.CryUntil = Time + 0.8f;
            }
            else AddLog(u.Team, $"{u.T.Name} {u.Squad.Title} пал!");
        }

        /// <summary>Тип для гарнизона на месте slot: конным на стенах и подъёмах не место — ставим пеших.</summary>
        int Foot(int team, int slot) => Races[team].Units[slot].Mount && slot != 3 ? 0 : slot;
    }
}
