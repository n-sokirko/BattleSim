using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleSim.Core
{
    /// <summary>
    /// Полководец видит поле (кроме затаившихся в низинах), раз в несколько секунд
    /// оценивает силы и рельеф и рассылает отрядам приказы через гонцов.
    /// Role.Solo — единственный полководец небольшой армии; General — главнокомандующий:
    /// ставит задачи крыльям, держит резерв; Captain — воевода крыла.
    /// </summary>
    public sealed class Commander
    {
        public readonly Battle Battle;
        public readonly int Team;
        public readonly Unit Unit;
        public readonly Trait Trait;
        public readonly string Name;
        public readonly Role Role;
        public readonly Wing Wing;
        public V2 Post;
        public float NextThink, StanceT = -99, NextWings = 2;
        public Stance Stance = Stance.None;
        public bool AmbushSet, FlankDone, ReserveCommitted;
        public Mission Mission;
        HashSet<string> claims = new HashSet<string>();
        readonly List<(Squad sq, Order order, bool voice)> outbox = new List<(Squad, Order, bool)>();

        World W => Battle.World;

        public Commander(Battle battle, int team, Unit unit, Trait trait, string name, Role role = Role.Solo, Wing wing = null)
        {
            Battle = battle; Team = team; Unit = unit; Trait = trait; Name = name; Role = role; Wing = wing;
            unit.Cmd = this;
            Post = unit.P;
            NextThink = 1.2f + Rng.Rand();
            Mission = role == Role.Captain ? new Mission(MissionKind.Attack) : null;
        }

        public string Title => Role == Role.Captain ? "Воевода " + Name : Name;

        List<Squad> AllMine() => Battle.Squads.Where(s => s.Team == Team && !s.Special && s.Alive > 0).ToList();

        /// <summary>Отряды, которыми полководец командует сам.</summary>
        List<Squad> MySquads()
        {
            var all = AllMine();
            if (Role == Role.Captain) return all.Where(s => s.Wing == Wing).ToList();
            if (Role == Role.General) return all.Where(s => s.Wing == null || s.Wing.Captain == null || !s.Wing.Captain.Unit.Alive).ToList();
            return all;
        }

        List<Squad> FoeSquads() => Battle.Squads.Where(s => s.Team != Team && !s.Special && s.Alive > 0 && !s.Hidden).ToList();

        public static V2? Center(IEnumerable<Squad> list)
        {
            float x = 0, z = 0;
            int n = 0;
            foreach (var s in list) { x += s.Center.x * s.Alive; z += s.Center.z * s.Alive; n += s.Alive; }
            return n > 0 ? new V2(x / n, z / n) : (V2?)null;
        }

        public static Squad Nearest(IEnumerable<Squad> list, V2 p)
        {
            Squad best = null;
            float bd = float.PositiveInfinity;
            foreach (var s in list) { float d = D(s.C, p); if (d < bd) { bd = d; best = s; } }
            return best;
        }

        static int Count(IEnumerable<Squad> list, Func<Squad, bool> pred) => list.Sum(q => pred(q) ? q.Alive : 0);
        static float D(V2 a, V2 b) => V2.Dist(a, b);

        public void Tick(float dt)
        {
            if (!Unit.Alive) return;
            var all = AllMine();
            if (all.Count == 0) return;
            var wingSq = Role == Role.Captain ? all.Where(s => s.Wing == Wing).ToList() : all;
            UpdatePost(wingSq.Count > 0 ? wingSq : all);
            if (Role == Role.General && (NextWings -= dt) <= 0)
            {
                NextWings = 6.5f + Rng.Rand() * 1.5f;
                ThinkWings();
                FlushLog();
            }
            if ((NextThink -= dt) > 0) return;
            NextThink = Defs.Traits[Trait].Think + Rng.Rand() * 0.8f;

            var my = MySquads();
            if (my.Count == 0) return;
            var en = FoeSquads();
            if (Role == Role.Captain)
            {
                switch (Mission.Kind)
                {
                    case MissionKind.Hold: case MissionKind.Reserve: Stance = Stance.Defend; break;
                    case MissionKind.Flank: Stance = Stance.Maneuver; break;
                    default: Stance = Stance.Attack; break;
                }
            }
            else if (Stance == Stance.None || Battle.Time - StanceT > 20) ChooseStance(my, en);
            // Роли, уже розданные отрядам: одну задачу не поручаем двоим
            claims = new HashSet<string>();
            foreach (var q in my) { var o = q.Pending ?? q.Order; if (o.Claim != null) claims.Add(o.Claim); }
            outbox.Clear();
            foreach (var sq in my)
            {
                if (sq.Order.Mode == Mode.Rout || sq.Pending != null || Battle.Time < sq.NextDecision) continue;
                if (sq.Reserve && !ReserveCommitted) continue; // резерв ждёт своего часа
                if (sq.Garrison) continue;                      // гарнизон держит свой рубеж
                var order = Decide(sq, my, en);
                if (order != null && Differs(sq.Order, order))
                {
                    if (order.Claim != null) claims.Add(order.Claim);
                    if (Send(sq, order)) sq.NextDecision = Battle.Time + 5 + Rng.Rand() * 2;
                }
            }
            FlushLog();
        }

        /// <summary>Одинаковые приказы разным отрядам — одной строкой летописи.</summary>
        void FlushLog()
        {
            var groups = new List<(string key, Order order, bool voice, List<string> names)>();
            foreach (var e in outbox)
            {
                string key = (e.voice ? "v" : "m") + "|" + e.order.Kind + "|" + (e.order.Why ?? "");
                int gi = groups.FindIndex(g => g.key == key);
                if (gi < 0) { groups.Add((key, e.order, e.voice, new List<string>())); gi = groups.Count - 1; }
                groups[gi].names.Add(e.sq.Name);
            }
            foreach (var g in groups)
            {
                string who = string.Join(", ", g.names);
                string what = Defs.OrderText(g.order.Kind).ToLowerInvariant() + (g.order.Why != null ? " (" + g.order.Why + ")" : "");
                Battle.AddLog(Team, g.voice ? $"{Title} → {who}: {what}" : $"{Title} шлёт {(g.names.Count > 1 ? "гонцов" : "гонца")} → {who}: {what}");
            }
            outbox.Clear();
        }

        /// <summary>Полководец держится позади войска, по возможности на высоте, и уходит от опасности.</summary>
        void UpdatePost(List<Squad> my)
        {
            var c = Center(my).Value;
            var all = Battle.Squads.Where(s => s.Team != Team && !s.Special && s.Alive > 0).ToList();
            var e = Center(all) ?? new V2(c.x, -(Team == 0 ? -1 : 1) * W.Field);
            var away = M.Norm2(c.x - e.x, c.z - e.z);
            float back = Role == Role.Captain ? 11 : 18;
            var p = new V2(c.x + away.x * back, c.z + away.z * back);
            var hill = W.An.High.FirstOrDefault(h => D(new V2(h.X, h.Z), p) < 22 && !all.Any(s => D(s.C, new V2(h.X, h.Z)) < 20));
            if (hill != null) p = new V2(hill.X, hill.Z);
            var u = Unit;
            foreach (var foe in Battle.Teams[1 - Team])
            {
                if (foe.T.Special == Special.None && D(foe.P, u.P) < 14)
                {
                    var a = M.Norm2(u.Pos.x - foe.Pos.x, u.Pos.z - foe.Pos.z);
                    p = new V2(u.Pos.x + a.x * 20, u.Pos.z + a.z * 20);
                    break;
                }
            }
            Post = W.ClampField(p);
        }

        void ChooseStance(List<Squad> my, List<Squad> en)
        {
            int myR = Count(my, q => q.T.Ranged), enR = Count(en, q => q.T.Ranged);
            int myAll = Count(my, _ => true), enAll = Count(en, _ => true);
            var st = Trait == Trait.Fierce ? Stance.Attack : Trait == Trait.Cunning ? Stance.Maneuver : myAll > enAll * 1.4f ? Stance.Attack : Stance.Defend;
            // Стоять под превосходящим обстрелом глупо — сближаемся
            if (st == Stance.Defend && enR > myR * 1.5f && Battle.Time > 15) st = Stance.Maneuver;
            if (st != Stance) Battle.AddLog(Team, $"{Title}: {Defs.StanceText(st)}");
            Stance = st;
            StanceT = Battle.Time;
        }

        Order Decide(Squad sq, List<Squad> my, List<Squad> en)
        {
            // Никто не отсиживается: давно без дела и враг далеко — вперёд (стрелки — на рубеж стрельбы)
            var B = Battle;
            if (en.Count > 0 && B.Time > 12 && !sq.Engaged && !sq.Reserve && sq.Order.Mode != Mode.Ambush && B.Time - sq.LastActiveT > 14)
            {
                if (sq.T.Ranged)
                {
                    var ec0 = Center(en).Value;
                    var pos = FiringPosition(sq, my, en, ec0, false);
                    if (pos != null && D(pos.P, sq.C) > 5)
                        return new Order(OrderKind.Fire, Mode.Move) { Then = new Order(OrderKind.Fire, Mode.Hold), Why = "стрелять не по кому — " + pos.Why }.At(pos.P);
                }
                else if (sq.Order.Mode != Mode.Advance && sq.Order.Mode != Mode.Charge)
                    return new Order(OrderKind.Advance, Mode.Advance) { Why = "без дела стоять нельзя — в бой" };
            }
            if (Role == Role.Captain && en.Count > 0)
            {
                var o = MissionOrder(sq, my, en, out bool decided);
                if (decided) return o;
            }
            if (en.Count == 0) return sq.Order.Mode == Mode.Advance ? null : new Order(OrderKind.Advance, Mode.Advance) { Why = "враг скрылся — искать его" };
            var ec = Center(en).Value;
            if (sq.T.Ranged) return DecideRanged(sq, my, en, ec);
            if (sq.T.Mount) return DecideCavalry(sq, my, en, ec);
            return DecideInfantry(sq, my, en, ec);
        }

        // ---------------------------------------------------------------- задачи крыльев

        /// <summary>Приказ, прямо вытекающий из задачи крыла (decided = false — решать как обычно).</summary>
        Order MissionOrder(Squad sq, List<Squad> my, List<Squad> en, out bool decided)
        {
            decided = false;
            var m = Mission;
            var B = Battle;
            var wc = Center(my);
            if (m.Kind == MissionKind.Flank && !FlankDone && wc.HasValue)
            {
                if (D(wc.Value, new V2(m.X, m.Z)) < 14 || B.Time - m.T > 40)
                {
                    FlankDone = true;
                    B.AddLog(Team, $"{Title}: {Defs.WingName(Wing.Key)} вышло во фланг — бьём!");
                    return null;
                }
                var p = W.ClampField(m.X + (sq.Center.x - wc.Value.x) * 0.6f, m.Z + (sq.Center.z - wc.Value.z) * 0.6f);
                decided = true;
                return new Order(OrderKind.Flank, Mode.Move) { Then = new Order(OrderKind.Advance, Mode.Advance), Why = $"{Defs.WingName(Wing.Key)} идёт в обход" }.At(p);
            }
            if (m.Kind == MissionKind.Support && m.Wing != null)
            {
                var tc = Center(m.Wing.Squads.Where(s => s.Alive > 0));
                if (tc.HasValue && D(sq.C, tc.Value) > 22 && !sq.Engaged)
                {
                    decided = true;
                    var p = W.ClampField(M.Lerp(sq.Center.x, tc.Value.x, 0.8f), M.Lerp(sq.Center.z, tc.Value.z, 0.8f));
                    return new Order(OrderKind.Support, Mode.Move) { Then = new Order(OrderKind.Advance, Mode.Advance), Why = $"на помощь: {Defs.WingName(m.Wing.Key)}" }.At(p);
                }
            }
            return null;
        }

        sealed class WingInfo
        {
            public Wing W;
            public V2 C;
            public float Str, Ratio;
            public bool Engaged;
            public int Cav;
        }

        static float Val(Squad q) => q.Alive * (q.T.Mount ? 2.2f : q.T.Ranged ? 0.8f : q.T.Slot == 1 ? 1.1f : 1);

        /// <summary>Главнокомандующий: сравнивает силы на каждом крыле и ставит воеводам задачи.</summary>
        void ThinkWings()
        {
            var B = Battle;
            var wings = B.Wings[Team].Where(w => w.Squads.Any(s => s.Alive > 0)).ToList();
            var en = B.Squads.Where(s => s.Team != Team && !s.Special && s.Alive > 0).ToList();
            if (wings.Count == 0 || en.Count == 0) return;
            var info = wings.Select(w =>
            {
                var sq = w.Squads.Where(s => s.Alive > 0).ToList();
                var c = Center(sq).Value;
                float x0 = sq.Min(q => q.Center.x) - 12, x1 = sq.Max(q => q.Center.x) + 12;
                float str = sq.Sum(Val);
                float opp = en.Where(e => e.Center.x >= x0 && e.Center.x <= x1).Sum(Val);
                return new WingInfo { W = w, C = c, Str = str, Ratio = str / MathF.Max(1, opp), Engaged = sq.Any(q => q.Engaged), Cav = sq.Where(q => q.T.Mount).Sum(q => q.Alive) };
            }).ToList();
            var ec = Center(en).Value;
            float myTot = info.Sum(i => i.Str), enTot = en.Sum(Val);
            var plan = new List<(Wing w, Mission m)>();
            void Set(Wing w, Mission m)
            {
                int i = plan.FindIndex(p => p.w == w);
                if (i >= 0) plan[i] = (w, m); else plan.Add((w, m));
            }
            int myR = Count(AllMine(), q => q.T.Ranged), enR = Count(en, q => q.T.Ranged);
            foreach (var i in info)
            {
                var kind = MissionKind.Attack;
                // Держать позицию — только осторожному, когда крыло слабее, а стрелков у нас больше: пусть враг идёт под болты
                if (Trait == Trait.Cautious && i.Ratio < 0.75f && myR > enR * 1.1f && !i.Engaged) kind = MissionKind.Hold;
                var cur = i.W.Mission;
                if (kind == MissionKind.Hold && cur != null && cur.Kind == MissionKind.Hold && B.Time - cur.T > 28) kind = MissionKind.Attack; // ждали достаточно
                Set(i.W, kind == MissionKind.Hold && cur != null && cur.Kind == MissionKind.Hold ? cur : new Mission(kind, B.Time));
            }
            // Хитрый посылает сильнейшее по коннице крыло в обход
            if (Trait == Trait.Cunning)
            {
                var outer = info.Where(i => i.W.Key != WingKey.Center && i.Ratio > 0.9f && !i.Engaged).OrderByDescending(i => i.Cav).ThenByDescending(i => i.Ratio).FirstOrDefault();
                if (outer != null)
                {
                    float side = M.Sign(outer.C.x - ec.x);
                    if (side == 0) side = 1;
                    var p = W.ClampField(ec.x + side * 32, ec.z + (Team == 0 ? 1 : -1) * 6, 6);
                    Set(outer.W, new Mission(MissionKind.Flank, B.Time) { X = p.x, Z = p.z });
                }
            }
            // Проигрывающему крылу — помощь соседа и резерв
            var losing = info.Where(i => i.Engaged && i.Ratio < 0.6f).OrderBy(i => i.Ratio).FirstOrDefault();
            if (losing != null)
            {
                var helper = info.Where(i => i != losing && !i.Engaged && i.Ratio > 1).OrderBy(i => D(i.C, losing.C)).FirstOrDefault();
                if (helper != null && Trait != Trait.Fierce) Set(helper.W, new Mission(MissionKind.Support) { Wing = losing.W });
                CommitReserve(losing);
            }
            else if ((myTot > enTot * 1.3f && B.Time > 15) || B.Time > 25) CommitReserve(null);
            foreach (var (w, m) in plan) SendMission(w, m);
        }

        void CommitReserve(WingInfo target)
        {
            if (ReserveCommitted) return;
            var res = AllMine().Where(s => s.Reserve).ToList();
            if (res.Count == 0) return;
            ReserveCommitted = true;
            foreach (var sq in res)
            {
                Order o;
                if (target != null)
                    o = new Order(OrderKind.Support, Mode.Move) { Then = new Order(OrderKind.Advance, Mode.Advance), Why = $"резерв на помощь: {Defs.WingName(target.W.Key)}" }.At(W.ClampField(target.C));
                else o = new Order(OrderKind.Advance, Mode.Advance) { Why = "резерв — в бой!" };
                Send(sq, o);
            }
            Battle.AddLog(Team, $"{Title}: ввожу резерв{(target != null ? " — на помощь, " + Defs.WingName(target.W.Key) : ", пора добивать")}");
        }

        void SendMission(Wing w, Mission m)
        {
            var cur = w.PendingMission ?? w.Mission;
            if (cur != null && cur.Kind == m.Kind && cur.Wing == m.Wing &&
                (!m.HasPos || M.Hypot((cur.HasPos ? cur.X : 1e9f) - m.X, (cur.HasPos ? cur.Z : 1e9f) - m.Z) < 12)) return;
            var cap = w.Captain;
            var B = Battle;
            if (cap == null || !cap.Unit.Alive) { w.Mission = m; return; }
            string text = $"воевода {cap.Name}, {Defs.WingName(w.Key)}: {Defs.MissionText(m.Kind)}{(m.Kind == MissionKind.Support ? " (" + Defs.WingName(m.Wing.Key) + ")" : "")}";
            if (D(cap.Unit.P, Unit.P) < 16) { cap.Receive(m); B.AddLog(Team, $"{Title} → {text}"); return; }
            w.PendingMission = m;
            B.SpawnMessenger(this, null, null, new Carry { Captain = cap, Mission = m, Wing = w });
            B.AddLog(Team, $"{Title} шлёт гонца → {text}");
        }

        /// <summary>Воевода получил новую задачу крыла: пересматриваем приказы отрядам сразу.</summary>
        public void Receive(Mission m)
        {
            Mission = m;
            Wing.Mission = m;
            Wing.PendingMission = null;
            FlankDone = false;
            foreach (var q in MySquads()) q.NextDecision = 0;
            NextThink = 0.2f;
        }

        // ---------------------------------------------------------------- стрелки

        Order DecideRanged(Squad sq, List<Squad> my, List<Squad> en, V2 ec)
        {
            var B = Battle;
            var threat = en.FirstOrDefault(e => !e.T.Ranged && D(e.C, sq.C) < 20 && MathF.Abs(e.Center.y - sq.Center.y) < 3);
            if (threat != null && !my.Any(q => !q.T.Ranged && !q.T.Mount && q.Order.Mode != Mode.Rout && D(q.C, sq.C) < 12))
            {
                var guards = my.Where(q => !q.T.Ranged && !q.T.Mount && q.Order.Mode != Mode.Rout).ToList();
                var guard = Nearest(guards, sq.C);
                var away = M.Norm2(sq.Center.x - threat.Center.x, sq.Center.z - threat.Center.z);
                var p = guard != null ? new V2(guard.Center.x + away.x * 7, guard.Center.z + away.z * 7) : new V2(sq.Center.x + away.x * 20, sq.Center.z + away.z * 20);
                return new Order(OrderKind.Withdraw, Mode.Move) { Then = new Order(OrderKind.Hold, Mode.Hold), Why = $"к ним рвутся {threat.Name}" }.At(W.ClampField(p));
            }
            int enR = Count(en, q => q.T.Ranged), myR = Count(my, q => q.T.Ranged);
            bool underFire = B.Time - sq.LastHitT < 3 && enR > myR * 1.1f;
            // Лучший рубеж стрельбы: в пределах выстрела, с прямой видимостью, повыше,
            // за своей пехотой, подальше от вражеских мечей, под обстрелом — в укрытии
            var pos = FiringPosition(sq, my, en, ec, underFire);
            if (pos != null && D(pos.P, sq.C) > 6)
                return new Order(OrderKind.Fire, Mode.Move) { Then = new Order(OrderKind.Fire, Mode.Hold), Why = pos.Why }.At(pos.P);
            if (sq.Order.Mode == Mode.Advance) return new Order(OrderKind.Fire, Mode.Hold) { Why = "стоять и стрелять" };
            return null;
        }

        sealed class FirePos { public V2 P; public float Dh; public bool Walls; public string Why; }

        FirePos FiringPosition(Squad sq, List<Squad> my, List<Squad> en, V2 ec, bool underFire)
        {
            float range = sq.T.Range;
            var pick = en.Where(e => e.Engaged && !e.T.Ranged).ToList();
            var target = Nearest(pick.Count > 0 ? pick : en, sq.C);
            if (target == null) return null;
            V2 tc = target.C;
            float ty = W.GroundAt(tc.x, tc.z);
            var away = M.Norm2(sq.Center.x - tc.x, sq.Center.z - tc.z);
            var ideal = new V2(tc.x + away.x * range * 0.7f, tc.z + away.z * range * 0.7f);
            var cands = new List<V2> { ideal, sq.C };
            foreach (float r in new[] { 5f, 10f, 16f })
                for (int a = 0; a < 8; a++) cands.Add(new V2(ideal.x + MathF.Cos(a * M.PI / 4) * r, ideal.z + MathF.Sin(a * M.PI / 4) * r));
            foreach (var h in W.An.High)
            {
                float d = D(new V2(h.X, h.Z), tc);
                if (d < range * 0.95f && d > range * 0.35f) cands.Add(new V2(h.X, h.Z));
            }
            // боевой ход стен и башни — лучшие места для стрелков
            foreach (var ws in W.WallSpots)
            {
                float d = D(ws.P, tc);
                if (d < range * 1.05f && d > range * 0.2f && ws.Out.x * (tc.x - ws.P.x) + ws.Out.z * (tc.z - ws.P.z) > 0) cands.Add(ws.P);
            }
            var melee = en.Where(e => !e.T.Ranged).ToList();
            FirePos best = null;
            float bs = float.NegativeInfinity;
            foreach (var c in cands)
            {
                var p = W.ClampField(c, 3);
                if (!W.Walkable(p.x, p.z, 0.5f)) continue;
                float dt = D(p, tc);
                if (dt > range * 0.92f || dt < range * 0.35f) continue;
                float gy = W.GroundAt(p.x, p.z);
                if (!W.Los(p.x, gy + 1.5f, p.z, tc.x, ty + 1.0f, tc.z, 2.5f)) continue;
                float s = M.Clamp((gy - ty) * 1.2f, -6, 10);
                float danger = float.PositiveInfinity;
                foreach (var e in melee) danger = MathF.Min(danger, D(e.C, p));
                if (danger < 15) s -= (15 - danger) * 2;
                if (my.Any(q => !q.T.Ranged && !q.T.Mount && D(q.C, tc) < dt - 3 && D(q.C, p) < 24)) s += 4;
                bool onWall = gy > W.HeightAt(p.x, p.z) + 3;
                if (W.ConcealedAt(p.x, p.z) || W.InWall(p.x, p.z) || onWall) s += underFire ? 5 : 1.5f;
                s -= D(p, sq.C) * 0.08f;
                if (s > bs) { bs = s; best = new FirePos { P = p, Dh = gy - ty, Walls = onWall }; }
            }
            if (best == null) return null;
            best.Why = best.Walls ? "на стену — сверху и за зубцами" : best.Dh > 2.5f ? "на высоту в пределах выстрела" : underFire ? "на рубеж под прикрытием" : "вперёд, на рубеж выстрела";
            return best;
        }

        // ---------------------------------------------------------------- конница

        Order DecideCavalry(Squad sq, List<Squad> my, List<Squad> en, V2 ec)
        {
            var o = sq.Order;
            if (o.Kind == OrderKind.Charge && sq.EngagedFor > 3.5f && Trait != Trait.Fierce)
            {
                var back = M.Norm2(sq.Center.x - ec.x, sq.Center.z - ec.z);
                return new Order(OrderKind.Withdraw, Mode.Move) { Then = new Order(OrderKind.Hold, Mode.Hold) { Leash = 14 }, Why = "выйти из свалки и разогнаться снова" }
                    .At(W.ClampField(sq.Center.x + back.x * 24, sq.Center.z + back.z * 24));
            }
            var exposed = en.Where(e => e.T.Ranged && !en.Any(q => !q.T.Ranged && q != e && D(q.C, e.C) < 14)).ToList();
            if (exposed.Count > 0)
            {
                var target = Nearest(exposed, sq.C);
                if (Trait == Trait.Cunning && D(target.C, sq.C) > 30)
                {
                    var fp = FlankPoint(target, sq.C, my);
                    return new Order(OrderKind.Flank, Mode.Move) { Then = new Order(OrderKind.Charge, Mode.Charge) { Target = target }, Why = $"в обход, цель — {target.Name}" }.At(fp);
                }
                return new Order(OrderKind.Charge, Mode.Charge) { Target = target, Why = $"{target.Name} остались без прикрытия" };
            }
            var pinned = en.Where(e => e.Engaged && !e.T.Ranged).ToList();
            if (pinned.Count > 0)
            {
                var target = Nearest(pinned, sq.C);
                return new Order(OrderKind.Charge, Mode.Charge) { Target = target, Why = $"{target.Name} связаны боем — удар во фланг" };
            }
            if (Stance == Stance.Defend && Battle.Time < 25)
            {
                var inf = my.Where(q => !q.T.Ranged && !q.T.Mount).ToList();
                var c = Center(inf) ?? sq.C;
                var back = M.Norm2(c.x - ec.x, c.z - ec.z);
                float side = sq.Center.x >= c.x ? 1 : -1;
                var p = W.ClampField(c.x + back.x * 10 - back.z * 14 * side, c.z + back.z * 10 + back.x * 14 * side);
                return new Order(OrderKind.Reserve, Mode.Hold) { Leash = 18, Why = "ждать, пока враг ввяжется" }.At(p);
            }
            return new Order(OrderKind.Advance, Mode.Advance);
        }

        // ---------------------------------------------------------------- пехота

        Order DecideInfantry(Squad sq, List<Squad> my, List<Squad> en, V2 ec)
        {
            var B = Battle;
            // 1. Закрыть стрелков от конницы (варвары — лучше всех)
            foreach (var xb in my.Where(q => q.T.Ranged))
            {
                var cav = en.FirstOrDefault(e => e.T.Mount && D(e.C, xb.C) < 34);
                if (cav == null || D(sq.C, xb.C) > 40) continue;
                string claim = "screen:" + xb.Id;
                bool barbNear = my.Any(q => q.T.Slot == 1 && q != sq && D(q.C, xb.C) < 40 && !claims.Contains(claim));
                if (claims.Contains(claim)) continue;
                if (sq.T.Slot == 1 || !barbNear)
                {
                    var v = M.Norm2(cav.Center.x - xb.Center.x, cav.Center.z - xb.Center.z);
                    return new Order(OrderKind.Screen, Mode.Move)
                    {
                        Claim = claim, Then = new Order(OrderKind.Hold, Mode.Hold) { Leash = 12, Claim = claim }, Why = $"{cav.Name} угрожают стрелкам ({xb.Name})",
                    }.At(W.ClampField(xb.Center.x + v.x * 6, xb.Center.z + v.z * 6));
                }
            }
            // 2. Засада в низине (хитрый, в начале боя)
            if (Trait == Trait.Cunning && !AmbushSet && B.Time < 30 && !sq.Engaged)
            {
                var hollow = W.An.Hide
                    .Where(l => D(new V2(l.X, l.Z), sq.C) < 45 && D(new V2(l.X, l.Z), ec) < D(sq.C, ec) + 5 && D(new V2(l.X, l.Z), ec) > 25)
                    .OrderByDescending(l => l.Depth).FirstOrDefault();
                if (hollow != null)
                {
                    AmbushSet = true;
                    string why = W.Type == MapType.Forest ? "затаиться в чаще и ждать" : W.Type == MapType.Swamp ? "затаиться в камышах" : "затаиться в низине и ждать";
                    return new Order(OrderKind.Ambush, Mode.Move) { Then = new Order(OrderKind.Ambush, Mode.Ambush) { Leash = 14 }, Why = why }.At(hollow.X, hollow.Z);
                }
            }
            // 3. Оборона: занять холм и ждать
            if (Stance == Stance.Defend && !sq.Engaged)
            {
                bool holding = sq.Order.Mode == Mode.Hold || (sq.Order.Mode == Mode.Move && sq.Order.Kind == OrderKind.High);
                float fd = float.IsInfinity(sq.FoeDist) ? 99 : sq.FoeDist;
                if (holding && (B.Time - sq.OrderT > 22 || fd < 28))
                    return new Order(OrderKind.Advance, Mode.Advance) { Why = fd < 28 ? "враг рядом — контратака" : "ждали достаточно — вперёд" };
                if (sq.Order.Kind == OrderKind.Advance || sq.Order.Kind == OrderKind.Charge || B.Time > 20) return null; // уже в бою — не отзываем
                if (W.ProminenceAt(sq.Center.x, sq.Center.z) < 1.0f)
                {
                    var h = HighNear(sq.C, 32, en, ec);
                    if (h != null) return new Order(OrderKind.High, Mode.Move) { Then = new Order(OrderKind.Hold, Mode.Hold) { Leash = 11 }, Why = "пусть враг лезет вверх" }.At(h.X, h.Z);
                }
                if (sq.Order.Mode == Mode.Advance) return new Order(OrderKind.Hold, Mode.Hold) { Leash = 11, Why = "держим строй" };
                return null;
            }
            // 4. Свои связали врага боем — обойти и ударить во фланг
            if (!sq.Engaged && (Trait != Trait.Fierce || Rng.Rand() < 0.3f))
            {
                var pinned = en.Where(e => e.Engaged && !e.T.Ranged && D(e.C, sq.C) > 14 && D(e.C, sq.C) < 70 && !claims.Contains("flank:" + e.Id)).ToList();
                if (pinned.Count > 0)
                {
                    var tgt = Nearest(pinned, sq.C);
                    var fp = FlankPoint(tgt, sq.C, my);
                    string claim = "flank:" + tgt.Id;
                    return new Order(OrderKind.Flank, Mode.Move) { Claim = claim, Then = new Order(OrderKind.Charge, Mode.Charge) { Target = tgt, Claim = claim }, Why = $"{tgt.Name} связаны боем" }.At(fp);
                }
            }
            // 5. Враг засел на холме: не лезть в лоб, а обойти склон
            var target = Nearest(en, sq.C);
            if (Trait != Trait.Fierce && W.ProminenceAt(target.Center.x, target.Center.z) > 1.2f && !sq.Engaged)
            {
                if (sq.Order.Kind != OrderKind.Flank && sq.Order.Kind != OrderKind.Advance)
                {
                    var fp = FlankPoint(target, sq.C, my);
                    return new Order(OrderKind.Flank, Mode.Move) { Then = new Order(OrderKind.Charge, Mode.Charge) { Target = target }, Why = $"{target.Name} на холме — обойти склон" }.At(fp);
                }
            }
            return sq.Order.Mode == Mode.Advance || sq.Order.Mode == Mode.Charge ? null : new Order(OrderKind.Advance, Mode.Advance) { Why = "в атаку" };
        }

        // ---------------------------------------------------------------- рельеф глазами полководца

        HighSpot HighNear(V2 p, float maxD, List<Squad> en, V2 ec)
        {
            float dp = D(p, ec);
            HighSpot best = null;
            float bs = float.PositiveInfinity;
            foreach (var h in W.An.High)
            {
                var hp = new V2(h.X, h.Z);
                float d = D(hp, p);
                if (d > maxD || en.Any(e => D(e.C, hp) < 14)) continue;
                if (D(hp, ec) < dp - 25) continue; // не лезть к врагу в пасть
                float s = d - h.Prom * 3;
                if (s < bs) { bs = s; best = h; }
            }
            return best;
        }

        V2 FlankPoint(Squad target, V2 from, List<Squad> my)
        {
            var mc = Center(my) ?? from;
            var v = M.Norm2(target.Center.x - mc.x, target.Center.z - mc.z);
            var perp = new V2(-v.z, v.x);
            float side = M.Sign(perp.x * (from.x - target.Center.x) + perp.z * (from.z - target.Center.z));
            if (side == 0) side = 1;
            return W.ClampField(target.Center.x + perp.x * 16 * side + v.x * 6, target.Center.z + perp.z * 16 * side + v.z * 6);
        }

        static bool Differs(Order cur, Order nx)
        {
            if (cur.Kind != nx.Kind) return true;
            if (nx.Target != cur.Target) return true;
            if (nx.HasPos && cur.HasPos && M.Hypot(nx.X - cur.X, nx.Z - cur.Z) > 8) return true;
            return false;
        }

        /// <summary>Рядом — приказ голосом, далеко — с гонцом (его могут перехватить).</summary>
        bool Send(Squad sq, Order order)
        {
            var B = Battle;
            if (D(Unit.P, sq.C) < 16)
            {
                B.ApplyOrder(sq, order);
                outbox.Add((sq, order, true));
                return true;
            }
            int busy = B.Units.Count(m => m.Alive && m.Carry != null && m.Carry.Cmd == this && !m.Carry.Delivered);
            if (busy >= 5) return false;
            sq.Pending = order;
            B.SpawnMessenger(this, sq, order);
            outbox.Add((sq, order, false));
            return true;
        }
    }
}
