using System;
using System.Collections.Generic;

namespace BattleSim.Core
{
    /// <summary>
    /// Строй отряда. Отряд ведёт «точка отряда»: она идёт по найденному пути со скоростью
    /// строя и ждёт отставших. На марше шеренги идут по её следу — колонна изгибается
    /// вдоль дороги, а не срезает углы. Перед узким местом (проём, ворота, мост) строй
    /// сужается до ширины прохода и солдаты проходят по очереди, потом снова разворачиваются.
    /// В бою рубится первая шеренга, задние держат строй и занимают места павших.
    /// </summary>
    public sealed partial class Battle
    {
        const float FormEvery = 0.5f;
        /// <summary>Проход уже этого — узкое место, отряды входят в него по одному.</summary>
        const float NarrowW = 7.5f;
        /// <summary>Как далеко вперёд по пути точка отряда высматривает узкие места.</summary>
        const float ChokeLook = 30f;
        readonly List<Choke> Chokes = new List<Choke>();
        public int StatAnchorPaths;
#if PROF
        public static readonly double[] FormProf = new double[4];
#endif

        /// <summary>Строй на марше: точка отряда впереди, шеренги — по её следу.</summary>
        static bool Marching(Squad sq) => sq.FormMarch;

        void UpdateFormations(float dt)
        {
            var nav = World.Nav;
            ProcessPaths();
            UpdateEngagements(dt);
            foreach (var sq in Squads)
            {
                if (sq.Special || sq.Alive == 0) { if (sq.Choke != null) LeaveChoke(sq); continue; }
                var o = sq.Order;
                if (o.Mode == Mode.Rout) { sq.FormInit = false; if (sq.Choke != null) LeaveChoke(sq); continue; }
                int cls = sq.T.Mount ? 1 : 0;
                if (!sq.FormInit)
                {
                    sq.FormInit = true;
                    sq.Anchor = sq.C;
                    sq.Facing = sq.Yaw;
                    sq.Trail.Clear();
                    sq.FormT = Rng.Rand() * FormEvery; // отряды пересчитывают строй вразнобой, не все в один шаг
                    sq.APath = null;
                }

                // Куда ведёт точка отряда
                Squad foe = null;
                V2? goal;
                float stop = 0.5f;
                bool advance = o.Mode == Mode.Advance || o.Mode == Mode.Charge;
                if (advance)
                {
                    foe = o.Mode == Mode.Charge && o.Target != null && o.Target.Alive > 0 ? o.Target : sq.Foes.Count > 0 ? sq.Foes[0] : null;
                    goal = foe != null ? foe.C : ArmyC[1 - sq.Team];
                    if (sq.T.Ranged) stop = sq.T.Range * 0.7f;
                }
                else goal = o.HasPos ? o.Pos : sq.C;
                // сосед уже стоит там — встаём рядом (сдвиг потихоньку забывается)
                sq.Offset = new V2(sq.Offset.x * (1 - 0.1f * dt), sq.Offset.z * (1 - 0.1f * dt));
                if (goal.HasValue && (o.HasPos || advance)) goal = new V2(goal.Value.x + sq.Offset.x, goal.Value.z + sq.Offset.z);

                // Сошлись с врагом — строй держится вокруг самого отряда
                // (через ущелье или стену — не сошлись: до врага ещё идти в обход)
                bool contact = advance && !sq.T.Ranged && (sq.Engaged || (foe != null && V2.Dist(foe.C, sq.C) < (sq.T.Mount ? 16 : 10)
                    && nav.LineClear(sq.C.x, sq.C.z, foe.C.x, foe.C.z, cls)));
                float speed = sq.T.Speed * (sq.T.Mount ? 0.95f : 0.9f);
                if (Time < sq.CryUntil) speed = 0;                 // кричат — стоят
                else if (Time < sq.RushUntil) speed *= 1.35f;      // разбег после клича
                if (sq.ShieldWall) speed *= 0.6f;                  // стена щитов идёт медленнее
                // точка отряда ждёт своих; если строй долго не собирается (кто-то застрял) — идёт дальше потихоньку
                sq.LagT = sq.Lag > 6 ? sq.LagT + dt : 0;
                float lagK = M.Clamp(1.25f - sq.Lag / 6f, sq.LagT > 8 ? 0.3f : 0f, 1f);
                float wantFacing = sq.Facing;
                bool moved = false;

                if (contact && sq.Front == null && foe != null) TryEngage(sq, foe);
                if (sq.Front != null)
                { // держим линию фронта: точка отряда — за линией на полгазора и полглубины строя
                    var e = sq.Front;
                    float side = e.A == sq ? -1 : 1;
                    var tn = new V2(e.N.z, -e.N.x);
                    var other = e.A == sq ? e.B : e.A;
                    float lat = (sq.Anchor.x - e.P.x) * tn.x + (sq.Anchor.z - e.P.z) * tn.z;
                    float olat = (other.Anchor.x - e.P.x) * tn.x + (other.Anchor.z - e.P.z) * tn.z;
                    lat += (olat - lat) * MathF.Min(1, 0.35f * dt); // строи понемногу выравниваются друг против друга
                    float depth = e.Gap / 2 + (sq.Rows - 1) * sq.T.Spacing / 2;
                    var target = new V2(e.P.x + tn.x * lat + e.N.x * side * depth, e.P.z + tn.z * lat + e.N.z * side * depth);
                    float k = 1 - MathF.Exp(-3f * dt);
                    sq.Anchor = new V2(M.Lerp(sq.Anchor.x, target.x, k), M.Lerp(sq.Anchor.z, target.z, k));
                    sq.AnchorVel = new V2(0, 0);
                    sq.FormMarch = false;
                    wantFacing = MathF.Atan2(-side * e.N.x, -side * e.N.z);
                }
                else if (contact)
                {
                    sq.FormMarch = false;
                    float k = 1 - MathF.Exp(-1.5f * dt);
                    sq.Anchor = new V2(M.Lerp(sq.Anchor.x, sq.Center.x, k), M.Lerp(sq.Anchor.z, sq.Center.z, k));
                    sq.AnchorVel = new V2(0, 0);
                    if (foe != null) wantFacing = MathF.Atan2(foe.Center.x - sq.Center.x, foe.Center.z - sq.Center.z);
                }
                else if (goal.HasValue)
                {
                    var g = goal.Value;
                    float dist = V2.Dist(sq.Anchor, g);
                    if (dist > stop)
                    {
                        var wp = AnchorWaypoint(sq, g, cls);
                        float wx = wp.x - sq.Anchor.x, wz = wp.z - sq.Anchor.z, wl = M.Hypot(wx, wz);
                        if (wl > 1e-3f)
                        {
                            // в гору, по броду и чаще точка отряда идёт так же медленно, как солдаты
                            float gy = World.GroundAt(sq.Anchor.x + wx / wl, sq.Anchor.z + wz / wl) - World.GroundAt(sq.Anchor.x, sq.Anchor.z);
                            float tf = (gy > 0 ? MathF.Max(0.35f, 1 - gy * 1.4f) : MathF.Min(1f, 1 - gy * 0.5f)) * MathF.Max(0.2f, nav.SpeedAt(sq.Anchor.x, sq.Anchor.z, cls));
                            lagK *= tf;
                            float step = MathF.Min(speed * lagK * dt, MathF.Min(wl, dist - stop));
                            bool queued = sq.Choke != null && !sq.ChokeGo;
                            if (queued)
                            { // в очереди к проходу: подходим только до своего места в ней
                                step = MathF.Min(step, MathF.Max(0, sq.ChokeHold));
                                sq.ChokeHold -= step;
                                if (step < speed * lagK * dt * 0.5f) sq.ChokeWaitT += dt;
                            }
                            var na = new V2(sq.Anchor.x + wx / wl * step, sq.Anchor.z + wz / wl * step);
                            if (step > 0 && (nav.SpeedAt(na.x, na.z, cls) > 0 || nav.SpeedAt(sq.Anchor.x, sq.Anchor.z, cls) == 0))
                            {
                                sq.Anchor = na;
                                moved = true;
                            }
                            float v = queued && step <= 1e-4f ? 0 : speed * lagK;
                            sq.AnchorVel = new V2(wx / wl * v, wz / wl * v);
                            wantFacing = MathF.Atan2(wx, wz);
                        }
                    }
                    else sq.AnchorVel = new V2(0, 0);
                    sq.AnchorArrived = dist <= stop + 0.6f;
                    if (sq.AnchorArrived && sq.T.Ranged)
                    { // стоят на стене — лицом наружу, вдоль зубцов
                        var wd = World.Decks.WallAt(sq.Anchor.x, sq.Anchor.z);
                        if (wd != null) { var o2 = new V2(-wd.Uz * wd.OutSign, wd.Ux * wd.OutSign); wantFacing = MathF.Atan2(o2.x, o2.z); }
                    }
                    // На марше — колонной по следу; пришли или стоим — строем вокруг точки
                    sq.FormMarch = !sq.AnchorArrived && dist > 6;
                    if (sq.AnchorArrived && !(sq.T.Ranged && World.Decks.WallAt(sq.Anchor.x, sq.Anchor.z) != null))
                    {
                        if (o.HasFace) wantFacing = o.Face;
                        else if (foe != null) wantFacing = MathF.Atan2(foe.Center.x - sq.Anchor.x, foe.Center.z - sq.Anchor.z);
                    }
                }

                // Поворот строя — не мгновенно; враг сзади (больше 120°) — разворот кругом на месте
                float turn = (sq.T.Mount ? 1.0f : 1.3f) * dt, dF = M.WrapAngle(wantFacing - sq.Facing);
                if (MathF.Abs(dF) > 2.1f && !sq.FormMarch && !sq.T.Mount) AboutFace(sq, wantFacing);
                else sq.Facing += MathF.Abs(dF) <= turn ? dF : M.Sign(dF) * turn;

                // След точки отряда: по нему идут шеренги колонны
                if (moved)
                {
                    if (sq.Trail.Count == 0 || V2.Dist(sq.Trail[sq.Trail.Count - 1], sq.Anchor) > 0.5f) sq.Trail.Add(sq.Anchor);
                    float need = sq.Units.Count * sq.T.Spacing + 12;
                    if (sq.Trail.Count > need / 0.5f + 4) sq.Trail.RemoveRange(0, sq.Trail.Count - (int)(need / 0.5f) - 4);
                }

                if ((sq.FormT -= dt) <= 0)
                {
                    sq.FormT = FormEvery + Rng.Rand() * 0.1f;
                    if (sq.T.Ranged && o.Kind == OrderKind.Fire && o.Mode == Mode.Move)
                    { // идут на рубеж, а больше трети уже целится — встают и стреляют здесь
                        int aim = 0;
                        foreach (var u in sq.Units) if (u.Alive && u.Aiming) aim++;
                        if (aim * 3 >= sq.Alive) { sq.Order = new Order(OrderKind.Fire, Mode.Hold) { Why = "стрелять с места" }.At(sq.C.x, sq.C.z); sq.APath = null; }
                    }
#if PROF
                    long fp1 = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                    UpdateChoke(sq, cls, goal, stop, contact);
#if PROF
                    long fp2 = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                    AssignSlots(sq, cls);
#if PROF
                    long fp3 = System.Diagnostics.Stopwatch.GetTimestamp();
                    FormProf[1] += (fp2 - fp1) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    FormProf[2] += (fp3 - fp2) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
#endif
                }
            }
            // пустые очереди больше не нужны
            for (int i = Chokes.Count - 1; i >= 0; i--) if (Chokes[i].Queue.Count == 0) Chokes.RemoveAt(i);
            SeparateSquads(dt);
        }

        public readonly List<Engagement> Engagements = new List<Engagement>();

        /// <summary>Отряд, который бьётся строем: пехота ближнего боя, не бежит.</summary>
        static bool LineSquad(Squad s) => s != null && !s.Special && !s.T.Ranged && !s.T.Mount && s.Alive > 0 && s.Order.Mode != Mode.Rout;

        static float ContactOf(Squad s) => s.T.Radius * 2 + s.T.Reach;

        /// <summary>Два пехотных строя сошлись — заводим сцепку (если оба ещё свободны, на одной высоте и между ними нет стены).</summary>
        void TryEngage(Squad a, Squad b)
        {
            if (!LineSquad(a) || !LineSquad(b) || b.Front != null || a.Team == b.Team) return;
            float d = V2.Dist(a.C, b.C);
            if (d > 14 || MathF.Abs(a.Center.y - b.Center.y) > 2) return;
            // фронты сблизились (или уже рубятся)
            float reach = (a.Rows + b.Rows) * 0.5f * (a.T.Spacing + b.T.Spacing) * 0.5f + 4;
            if (!(a.Engaged || b.Engaged || d < reach)) return;
            if (!World.Nav.LineClear(a.C.x, a.C.z, b.C.x, b.C.z, 0)) return;
            var n = new V2((b.C.x - a.C.x) / MathF.Max(d, 0.01f), (b.C.z - a.C.z) / MathF.Max(d, 0.01f));
            float da = (a.Rows - 1) * a.T.Spacing / 2, db = (b.Rows - 1) * b.T.Spacing / 2;
            // линия — между передними краями строёв
            float t = M.Clamp((d - da - db) / 2 + da, 0, d);
            var e = new Engagement
            {
                A = a, B = b, N = n, P = new V2(a.C.x + n.x * t, a.C.z + n.z * t),
                Gap = MathF.Min(ContactOf(a), ContactOf(b)) - 0.3f, LastFightT = Time, Born = Time,
            };
            a.Front = e; b.Front = e;
            Engagements.Add(e);
        }

        static readonly List<Unit> frontA = new List<Unit>(), frontB = new List<Unit>();

        /// <summary>Сцепки: поворот линии, давление, пары бойцов первых шеренг; распад, когда бой кончился.</summary>
        void UpdateEngagements(float dt)
        {
            for (int i = Engagements.Count - 1; i >= 0; i--)
            {
                var e = Engagements[i];
                if (e.A.Engaged || e.B.Engaged) e.LastFightT = Time;
                bool alive = LineSquad(e.A) && LineSquad(e.B) && e.A.Front == e && e.B.Front == e
                    && V2.Dist(e.A.C, e.B.C) < 18 && Time - e.LastFightT < 6
                    && e.A.Alive > e.A.Size * 0.25f && e.B.Alive > e.B.Size * 0.25f; // остатки — в свалку
                if (!alive)
                {
                    if (e.A.Front == e) e.A.Front = null;
                    if (e.B.Front == e) e.B.Front = null;
                    Engagements.RemoveAt(i);
                    continue;
                }
                // линия медленно поворачивается к оси между строями и держится между ними
                float d = V2.Dist(e.A.C, e.B.C);
                if (d > 0.5f)
                {
                    float wa = MathF.Atan2(e.B.C.x - e.A.C.x, e.B.C.z - e.A.C.z), ca = MathF.Atan2(e.N.x, e.N.z);
                    float dA = M.WrapAngle(wa - ca), turn = 0.3f * dt;
                    ca += MathF.Abs(dA) <= turn ? dA : M.Sign(dA) * turn;
                    e.N = new V2(MathF.Sin(ca), MathF.Cos(ca));
                }
                var tn = new V2(e.N.z, -e.N.x);
                var mid = new V2((e.A.C.x + e.B.C.x) / 2, (e.A.C.z + e.B.C.z) / 2);
                float lm = (mid.x - e.P.x) * tn.x + (mid.z - e.P.z) * tn.z;
                e.P = new V2(e.P.x + tn.x * lm * MathF.Min(1, 0.5f * dt), e.P.z + tn.z * lm * MathF.Min(1, 0.5f * dt));
                // давление: кто сильнее первой шеренгой, свежестью, глубиной и духом — теснит линию
                float pa = Pressure(e.A), pb = Pressure(e.B), k = (pa - pb) / MathF.Max(0.01f, pa + pb);
                if (MathF.Abs(k) > 0.08f)
                {
                    float v = k * 0.35f * dt;
                    e.P = new V2(e.P.x + e.N.x * v, e.P.z + e.N.z * v);
                    var loser = k > 0 ? e.B : e.A;
                    loser.Morale -= MathF.Abs(k) * 1.2f * dt; // теснят — дух падает
                }
                if ((e.PairT -= dt) <= 0) { e.PairT = 0.5f; RepairPairs(e); }
            }
        }

        float Pressure(Squad s)
        {
            float fm = 0;
            foreach (var u in s.Units) if (u.Alive && u.Row == 0) fm += u.T.Mass * u.Hp / u.T.Hp;
            return fm * (0.4f + s.Morale / 160f) * (1 + 0.15f * Math.Min(s.Rows - 1, 3)) * (s.Order.Mode == Mode.Hold ? 1.2f : 1f);
        }

        /// <summary>Пары бойцов первых шеренг: каждому — ближайший по линии противник (не больше двоих на одного).</summary>
        void RepairPairs(Engagement e)
        {
            var tn = new V2(e.N.z, -e.N.x);
            float Lat(Unit u) => (u.Pos.x - e.P.x) * tn.x + (u.Pos.z - e.P.z) * tn.z;
            frontA.Clear(); frontB.Clear();
            foreach (var u in e.A.Units) if (u.Alive && u.Row == 0) frontA.Add(u);
            foreach (var u in e.B.Units) if (u.Alive && u.Row == 0) frontB.Add(u);
            if (frontA.Count == 0 || frontB.Count == 0) return;
            frontA.Sort((x, y) => Lat(x).CompareTo(Lat(y)));
            frontB.Sort((x, y) => Lat(x).CompareTo(Lat(y)));
            Pair(frontA, frontB, Lat);
            Pair(frontB, frontA, Lat);
        }

        static void Pair(List<Unit> from, List<Unit> to, Func<Unit, float> lat)
        {
            int j = 0;
            foreach (var a in from)
            {
                float la = lat(a);
                while (j + 1 < to.Count && MathF.Abs(lat(to[j + 1]) - la) <= MathF.Abs(lat(to[j]) - la)) j++;
                a.Target = to[j];
                a.RetargetT = 1.2f;
            }
        }

        /// <summary>Сколько места занимает строй (радиус пятна).</summary>
        static float FormRadius(Squad sq)
        {
            int files = Math.Max(1, sq.Files), rows = Math.Max(1, sq.Rows);
            return MathF.Max(files, rows) * sq.T.Spacing * 0.5f + 0.6f;
        }

        /// <summary>
        /// Строи своих отрядов не налезают друг на друга: если двум отрядам велено в одно место
        /// или они сошлись у одного врага, они встают рядом, а не одной кучей. Колонны на марше
        /// не раздвигаем — их разводит очередь у проходов.
        /// </summary>
        void SeparateSquads(float dt)
        {
            var nav = World.Nav;
            for (int i = 0; i < Squads.Count; i++)
            {
                var a = Squads[i];
                if (a.Special || a.Alive == 0 || !a.FormInit || a.FormMarch || a.Order.Mode == Mode.Rout) continue;
                float ra = FormRadius(a);
                for (int j = 0; j < Squads.Count; j++)
                {
                    var b = Squads[j];
                    if (j == i || b.Team != a.Team || b.Special || b.Alive == 0 || !b.FormInit || b.FormMarch || b.Order.Mode == Mode.Rout) continue;
                    float need = ra + FormRadius(b) + 0.8f;
                    float dx = a.Anchor.x - b.Anchor.x, dz = a.Anchor.z - b.Anchor.z, d = M.Hypot(dx, dz);
                    if (d >= need) continue;
                    if (d < 1e-3f) { dx = MathF.Sin(a.Facing + 1.57f); dz = MathF.Cos(a.Facing + 1.57f); d = 1; }
                    else { dx /= d; dz /= d; }
                    float push = MathF.Min(need - d, a.T.Speed * 0.6f * dt);
                    // вперёд-назад не толкаем — только вбок, чтобы строй вставал в линию, а не в затылок
                    var f = new V2(MathF.Sin(a.Facing), MathF.Cos(a.Facing));
                    float along = dx * f.x + dz * f.z;
                    float sx = dx - f.x * along * 0.7f, sz = dz - f.z * along * 0.7f, sl = M.Hypot(sx, sz);
                    if (sl < 1e-3f) { sx = f.z; sz = -f.x; sl = 1; }
                    // рубящийся строй не сдвигаем ради подошедшего; равные расходятся поровну
                    float ka = a.Engaged == b.Engaged ? 0.5f : a.Engaged ? 0f : 1f;
                    if (ka <= 0) continue;
                    float px = sx / sl * push * ka, pz = sz / sl * push * ka;
                    var na = new V2(a.Anchor.x + px, a.Anchor.z + pz);
                    int cls = a.T.Mount ? 1 : 0;
                    if (nav.SpeedAt(na.x, na.z, cls) > 0 && nav.LineClear(a.Anchor.x, a.Anchor.z, na.x, na.z, cls))
                    {
                        a.Anchor = na;
                        // сдвиг запоминаем: отряд встаёт рядом с соседом, а не возвращается в ту же точку
                        a.Offset = new V2(a.Offset.x + px, a.Offset.z + pz);
                        float ol = M.Hypot(a.Offset.x, a.Offset.z);
                        if (ol > 25) a.Offset = new V2(a.Offset.x * 25 / ol, a.Offset.z * 25 / ol);
                    }
                }
            }
        }

        /// <summary>
        /// Очередь у узкого места. Если на пути впереди мост, брод, проём или тропа и туда уже
        /// входит свой отряд, ждём строем позади него, пока его хвост не втянется в проход.
        /// Так отряды идут друг за другом колоннами, а не сливаются у входа в одну толпу.
        /// Слишком долго стоим — ищем другую дорогу.
        /// </summary>
        void UpdateChoke(Squad sq, int cls, V2? goal, float stop, bool contact)
        {
            var ch = sq.Choke;
            if (ch != null && sq.ChokeGo)
            { // наш черёд: держим очередь, пока последняя шеренга не зайдёт в проход
                if (RearIn(sq) || Time - sq.ChokeGoT > 40 || sq.AnchorArrived || sq.Order.Mode == Mode.Rout) LeaveChoke(sq);
                return;
            }
            float dE = -1;
            V2 e = default, dir = default;
            if (!contact && sq.FormMarch && goal.HasValue) dE = FindChoke(sq, cls, goal.Value, stop, out e, out dir);
            if (dE < 0) { if (ch != null) LeaveChoke(sq); return; }
            if (ch != null && V2.Dist(sq.ChokeE, e) > 8) { LeaveChoke(sq); ch = null; }
            if (ch == null)
            {
                foreach (var c in Chokes)
                    if (c.Team == sq.Team && V2.Dist(c.E, e) < 8 && c.Dir.x * dir.x + c.Dir.z * dir.z > 0) { ch = c; break; }
                if (ch == null) { ch = new Choke { E = e, Dir = dir, Team = sq.Team }; Chokes.Add(ch); }
                ch.Queue.Add(sq);
                sq.Choke = ch; sq.ChokeGo = false; sq.ChokeWaitT = 0;
            }
            sq.ChokeE = e; sq.ChokeDir = dir; sq.ChokeD = dE;

            // кто впереди в очереди: уже входящий и те, кто ближе к проходу
            float hold = 3;
            bool free = true;
            foreach (var o in ch.Queue)
            {
                if (o == sq || o.Alive == 0) continue;
                if (o.ChokeGo || o.ChokeD < dE || (o.ChokeD == dE && string.CompareOrdinal(o.Id, sq.Id) < 0))
                {
                    free = false;
                    hold += o.Rows * o.T.Spacing + 2.5f;
                }
            }
            if (free)
            {
                sq.ChokeGo = true; sq.ChokeGoT = Time;
                return;
            }
            sq.ChokeHold = dE - hold;
            if (sq.ChokeWaitT > 25)
            { // стоим давно — пробуем другой мост, брод или проём
                sq.AvoidP = e; sq.AvoidUntil = Time + 45;
                sq.APath = null; sq.APathT = -99;
                LeaveChoke(sq);
            }
        }

        void LeaveChoke(Squad sq)
        {
            sq.Choke?.Queue.Remove(sq);
            sq.Choke = null; sq.ChokeGo = false; sq.ChokeWaitT = 0;
        }

        /// <summary>Последняя шеренга уже в проходе (или за ним).</summary>
        bool RearIn(Squad sq)
        {
            Unit rear = null;
            foreach (var u in sq.Units) if (u.Alive && (rear == null || u.SlotBack > rear.SlotBack)) rear = u;
            if (rear == null) return true;
            float ox = rear.Pos.x - sq.ChokeE.x, oz = rear.Pos.z - sq.ChokeE.z;
            return ox * sq.ChokeDir.x + oz * sq.ChokeDir.z > -1 || M.Hypot(ox, oz) < 2.5f;
        }

        /// <summary>
        /// Первое узкое место на пути точки отряда в пределах ChokeLook: расстояние до него, точка входа
        /// и направление пути там; -1 — впереди свободно (или мы уже в проходе).
        /// </summary>
        float FindChoke(Squad sq, int cls, V2 goal, float stop, out V2 e, out V2 dir)
        {
            e = default; dir = default;
            var pts = new List<V2>(8) { sq.Anchor };
            if (sq.APath != null) for (int i = Math.Max(1, sq.APathI); i < sq.APath.Count; i++) pts.Add(sq.APath[i]);
            else pts.Add(goal);
            float total = 0;
            for (int i = 1; i < pts.Count; i++) total += V2.Dist(pts[i - 1], pts[i]);
            float limit = MathF.Min(ChokeLook, total - stop - 1);
            if (limit < 2) return -1;
            float acc = 0, s = 0;
            for (int i = 1; i < pts.Count && s <= limit; i++)
            {
                var a = pts[i - 1]; var b = pts[i];
                float d = V2.Dist(a, b);
                if (d < 1e-3f) continue;
                var f = new V2((b.x - a.x) / d, (b.z - a.z) / d);
                for (; s <= acc + d && s <= limit; s += 1.5f)
                {
                    var p = new V2(a.x + f.x * (s - acc), a.z + f.z * (s - acc));
                    Corridor(p, f, cls, out float L, out float R);
                    if (L + R < NarrowW)
                    {
                        if (s < 0.5f) return -1; // уже идём узким местом
                        e = p; dir = f;
                        return s;
                    }
                }
                acc += d;
            }
            return -1;
        }

        /// <summary>
        /// Путь точки отряда: напрямую, если свободно, иначе по A*. Путь не ищется сразу: отряд
        /// ставит заявку в очередь, а поиск идёт понемногу каждый шаг (PathSlice клеток) — пока
        /// новый путь считается, отряд идёт по старому.
        /// </summary>
        V2 AnchorWaypoint(Squad sq, V2 g, int cls)
        {
            var nav = World.Nav;
            if (nav.Barriers == 0) return g;
            // цель сдвинулась заметно относительно расстояния до неё (далёкую цель не гоняем за каждым шагом)
            bool stale = V2.Dist(g, sq.APathGoal) > MathF.Max(5, V2.Dist(sq.Anchor, g) * 0.15f);
            // цель чуть сдвинулась, а от предпоследней точки пути до неё прямая дорога — поправляем только конец пути
            if (stale && sq.APath != null && sq.APath.Count >= 2)
            {
                var pre = sq.APath[sq.APath.Count - 2];
                if (nav.LineClear(pre.x, pre.z, g.x, g.z, cls)) { sq.APath[sq.APath.Count - 1] = g; sq.APathGoal = g; stale = false; }
            }
            bool need = sq.APath == null ? Time - sq.APathT > (stale ? 0.5f : 1.5f) : Time - sq.APathT > 6 || stale;
            if (need && !sq.APathQueued)
            {
                bool avoid = Time < sq.AvoidUntil;
                if (!avoid && nav.LineClear(sq.Anchor.x, sq.Anchor.z, g.x, g.z, cls)) { sq.APath = null; sq.APathT = Time; sq.APathGoal = g; return g; }
                sq.APathQueued = true; sq.APathWant = g;
                pathQueue.Enqueue(sq);
            }
            var P = sq.APath;
            if (P == null) return g;
            while (sq.APathI < P.Count - 1 && V2.Dist(P[sq.APathI], sq.Anchor) < 1.2f) sq.APathI++;
            return P[Math.Min(sq.APathI, P.Count - 1)];
        }

        /// <summary>Сколько клеток поиск путей отрядов перебирает за один шаг боя.</summary>
        const int PathSlice = 2500;
        readonly Queue<Squad> pathQueue = new Queue<Squad>();
        NavGrid.Search pathSearch;
        Squad pathFor;

        /// <summary>Ведём поиск путей по очереди заявок, не больше PathSlice клеток за шаг.</summary>
        void ProcessPaths()
        {
            var nav = World.Nav;
            if (pathSearch == null) pathSearch = nav.NewSearch();
            int budget = PathSlice;
            while (budget > 0)
            {
                if (!pathSearch.Active)
                {
                    if (pathQueue.Count == 0) return;
                    var sq = pathQueue.Dequeue();
                    if (sq.Alive == 0 || sq.Special || sq.Order.Mode == Mode.Rout) { sq.APathQueued = false; continue; }
                    pathFor = sq;
                    StatAnchorPaths++;
                    bool avoid = Time < sq.AvoidUntil;
                    // пути других отрядов «дороже» — армия расходится по разным подъёмам, мостам и воротам
                    if (!nav.Begin(pathSearch, sq.Anchor.x, sq.Anchor.z, sq.APathWant.x, sq.APathWant.z, sq.T.Mount ? 1 : 0, 0.9f,
                                   sq.AvoidP.x, sq.AvoidP.z, avoid ? 14 : 0, 16000, 2f))
                    {
                        PathDone(sq, null);
                        continue;
                    }
                    budget -= 100;
                }
                int before = pathSearch.Expanded;
                bool finished = nav.Continue(pathSearch, budget, out var path);
                budget -= Math.Max(1, pathSearch.Expanded - before);
                if (finished) PathDone(pathFor, path);
            }
        }

        void PathDone(Squad sq, List<V2> path)
        {
            sq.APathQueued = false;
            sq.APath = path; sq.APathT = Time; sq.APathGoal = sq.APathWant; sq.APathI = 1;
            if (path != null) World.Nav.MarkCrowd(path, sq.Alive / 12f);
        }

        /// <summary>Точка на следе отряда в s метрах позади точки отряда и направление следа там.</summary>
        void TrailAt(Squad sq, float s, out V2 p, out V2 fwd)
        {
            var T = sq.Trail;
            var prev = sq.Anchor;
            float acc = 0;
            fwd = new V2(MathF.Sin(sq.Facing), MathF.Cos(sq.Facing));
            for (int i = T.Count - 1; i >= 0; i--)
            {
                var q = T[i];
                float d = V2.Dist(prev, q);
                if (d > 1e-4f)
                {
                    var dir = new V2((prev.x - q.x) / d, (prev.z - q.z) / d);
                    if (acc + d >= s)
                    {
                        float k = (s - acc) / d;
                        p = new V2(prev.x - dir.x * d * k, prev.z - dir.z * d * k);
                        fwd = dir;
                        return;
                    }
                    fwd = dir;
                    acc += d;
                }
                prev = q;
            }
            // след короче колонны — дальше прямо назад
            p = new V2(prev.x - fwd.x * (s - acc), prev.z - fwd.z * (s - acc));
        }

        /// <summary>Свободная ширина прохода поперёк направления f в точке p: влево и вправо (до 9 м).</summary>
        void Corridor(V2 p, V2 f, int cls, out float left, out float right)
        {
            var nav = World.Nav;
            var r = new V2(f.z, -f.x);
            left = right = 9;
            for (int side = -1; side <= 1; side += 2)
            {
                int prev = nav.Idx(p.x, p.z);
                float free = 9;
                for (float k = 0.75f; k <= 9; k += 0.75f)
                {
                    float x = p.x + r.x * k * side, z = p.z + r.z * k * side;
                    int i = nav.Idx(x, z);
                    if (i < 0 || nav.Speed[cls][i] == 0 || (prev >= 0 && i != prev && !nav.Step(prev, i, true))) { free = k - 0.75f; break; }
                    prev = i;
                }
                if (side < 0) left = free; else right = free;
            }
        }

        /// <summary>
        /// Раздаём места в строю. Ширина строя — по самому узкому месту прохода впереди и на следе;
        /// кто впереди, тот и в первой шеренге — так отряд втекает в проём без толкотни.
        /// </summary>
        void AssignSlots(Squad sq, int cls)
        {
            var alive = new List<Unit>(sq.Units.Count);
            foreach (var u in sq.Units) if (u.Alive) alive.Add(u);
            int n = alive.Count;
            if (n == 0) return;
            float sp = sq.T.Spacing, rad = sq.T.Radius;
            int maxCols = Math.Min(sq.T.Cols > 0 ? sq.T.Cols : 3, n);
            if (sq.T.Ranged && !sq.FormMarch && World.Decks.WallAt(sq.Anchor.x, sq.Anchor.z) != null) maxCols = n; // на стене — в одну шеренгу у зубцов
            var f = new V2(MathF.Sin(sq.Facing), MathF.Cos(sq.Facing));
            var r = new V2(f.z, -f.x);

            // ширина прохода: впереди точки отряда и вдоль всей колонны
            float width = 99, shift = 0;
            if (sq.FormMarch)
            {
                int depth = (n + maxCols - 1) / maxCols;
                for (float s = -6; s <= depth * sp + 1; s += 3)
                {
                    V2 p, fw;
                    if (s < 0) { p = new V2(sq.Anchor.x - f.x * s, sq.Anchor.z - f.z * s); fw = f; }
                    else TrailAt(sq, s, out p, out fw);
                    Corridor(p, fw, cls, out float L, out float R);
                    if (L + R < width) { width = L + R; shift = (R - L) / 2; }
                }
            }
            else if (World.Nav.Barriers > 0)
            {
                float half = (n + maxCols - 1) / maxCols * sp / 2 + 1;
                for (float s = -half; s <= half; s += 2)
                {
                    Corridor(new V2(sq.Anchor.x + f.x * s, sq.Anchor.z + f.z * s), f, cls, out float L, out float R);
                    if (L + R < width) { width = L + R; shift = (R - L) / 2; }
                }
            }
            int files = width >= 98 ? maxCols : M.Clamp((int)MathF.Floor((width - 2 * rad) / sp) + 1, 1, maxCols);
            if (files >= maxCols) shift = 0;
            else shift = M.Clamp(shift, -width / 2, width / 2);
            sq.Files = files;
            int rows = (n + files - 1) / files;

            // Ширина строя та же и строй тот же (марш/стоим) — места не перераздаём: бреши закрывают задние
            // той же колонны («шаг вперёд»), последняя шеренга остаётся рваной. Полная раскладка — при смене
            // ширины/строя, а у стоящего отряда, разбредшегося по полю, — не чаще раза в 3 с.
            bool relayout = sq.Cells == null || files != sq.CellFiles || sq.FormMarch != sq.CellMarch
                || MathF.Abs(shift - sq.CellShift) > 0.6f || (sq.Lag > 3f && !sq.Engaged && Time - sq.LayoutT > 3);
            if (!relayout && FillForward(sq, sp, shift))
            {
                var lags0 = new List<float>(n);
                foreach (var u in alive) { var s0 = FormSlot(u); lags0.Add(M.Hypot(s0.x - u.Pos.x, s0.z - u.Pos.z)); }
                lags0.Sort();
                sq.Lag = lags0[Math.Min(n - 1, (int)(n * 0.7f))];
                return;
            }
            sq.LayoutT = Time; sq.CellFiles = files; sq.CellRows = rows; sq.CellMarch = sq.FormMarch; sq.CellShift = shift;
            sq.Cells = new Unit[files * rows];

            // кто впереди — тот в первой шеренге; в шеренге — по порядку слева направо
            var along = new float[n];
            for (int i = 0; i < n; i++)
            {
                var u = alive[i];
                along[i] = (u.Pos.x - sq.Anchor.x) * f.x + (u.Pos.z - sq.Anchor.z) * f.z;
            }
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, (a, b) => along[b].CompareTo(along[a]));
            var lags = new List<float>(n);
            for (int row = 0; row < rows; row++)
            {
                int from = row * files, cnt = Math.Min(files, n - from);
                var rowIdx = new int[cnt];
                Array.Copy(order, from, rowIdx, 0, cnt);
                Array.Sort(rowIdx, (a, b) =>
                {
                    var ua = alive[a]; var ub = alive[b];
                    float la = (ua.Pos.x - sq.Anchor.x) * r.x + (ua.Pos.z - sq.Anchor.z) * r.z;
                    float lb = (ub.Pos.x - sq.Anchor.x) * r.x + (ub.Pos.z - sq.Anchor.z) * r.z;
                    return la.CompareTo(lb);
                });
                int c0 = (files - cnt) / 2; // неполная шеренга — по центру (только при полной раскладке)
                for (int c = 0; c < cnt; c++)
                {
                    var u = alive[rowIdx[c]];
                    u.Row = row; u.File = c0 + c; u.Settled = false;
                    sq.Cells[row * files + c0 + c] = u;
                    u.SlotLat = (c - (cnt - 1) / 2f) * sp + shift;
                    u.SlotBack = row * sp;
                    var s = FormSlot(u);
                    lags.Add(M.Hypot(s.x - u.Pos.x, s.z - u.Pos.z));
                }
            }
            sq.Rows = rows;
            lags.Sort();
            sq.Lag = lags[Math.Min(n - 1, (int)(n * 0.7f))];
        }

        /// <summary>
        /// Закрыть бреши: на пустое место встаёт первый живой сзади в той же колонне, а если такого нет —
        /// ближайший по колонне из последней шеренги. false — сетка испорчена, нужна полная раскладка.
        /// </summary>
        bool FillForward(Squad sq, float sp, float shift)
        {
            var C = sq.Cells;
            int F = sq.CellFiles, R = sq.CellRows;
            if (C == null || C.Length != F * R) return false;
            int last = -1;
            for (int i = 0; i < C.Length; i++)
            {
                if (C[i] != null && (!C[i].Alive || C[i].Squad != sq)) C[i] = null;
                if (C[i] != null) last = i / F;
            }
            if (last < 0) return false;
            for (int r = 0; r < last; r++)
                for (int c = 0; c < F; c++)
                {
                    if (C[r * F + c] != null) continue;
                    int from = -1;
                    for (int rr = r + 1; rr <= last && from < 0; rr++) if (C[rr * F + c] != null) from = rr * F + c;
                    if (from < 0)
                    {
                        int best = int.MaxValue;
                        for (int cc = 0; cc < F; cc++)
                            if (C[last * F + cc] != null && Math.Abs(cc - c) < best) { best = Math.Abs(cc - c); from = last * F + cc; }
                    }
                    if (from < 0 || from / F <= r) continue;
                    var u = C[from];
                    C[from] = null; C[r * F + c] = u;
                    u.Row = r; u.File = c; u.Settled = false;
                    u.SlotLat = (c - (F - 1) / 2f) * sp + shift; u.SlotBack = r * sp;
                    // последняя шеренга могла опустеть
                    while (last > 0 && !HasRow(C, F, last)) last--;
                }
            sq.Rows = last + 1;
            // в сетке должен быть каждый живой (новички, вернувшиеся из погони) — иначе полная раскладка
            int inGrid = 0;
            foreach (var u in C) if (u != null) inGrid++;
            return inGrid == sq.Alive;
        }

        static bool HasRow(Unit[] C, int F, int r)
        {
            for (int c = 0; c < F; c++) if (C[r * F + c] != null) return true;
            return false;
        }

        /// <summary>Разворот кругом: строй не вращается сквозь себя — шеренги и колонны меняются местами, бойцы только поворачиваются.</summary>
        void AboutFace(Squad sq, float want)
        {
            sq.Facing = want;
            var C = sq.Cells;
            int F = sq.CellFiles, R = sq.CellRows;
            if (C == null || C.Length != F * R) return;
            var N = new Unit[C.Length];
            int rows = Math.Max(1, sq.Rows);
            for (int r = 0; r < R; r++)
                for (int c = 0; c < F; c++)
                {
                    var u = C[r * F + c];
                    if (u == null || r >= rows) continue;
                    int nr = rows - 1 - r, nc = F - 1 - c;
                    N[nr * F + nc] = u;
                    u.Row = nr; u.File = nc; u.SlotLat = -u.SlotLat; u.SlotBack = nr * sq.T.Spacing;
                }
            sq.Cells = N;
        }

        /// <summary>Место солдата в строю сейчас (в мире).</summary>
        V2 FormSlot(Unit u)
        {
            var sq = u.Squad;
            if (!sq.FormInit || u.Row < 0) return sq.Slot(u);
            if (sq.FormMarch)
            {
                int r = u.Row;
                if (sq.RowTick != tickNo || sq.RowP.Length <= r)
                {
                    if (sq.RowP.Length <= r)
                    {
                        int n = Math.Max(r + 1, sq.Rows);
                        sq.RowP = new V2[n]; sq.RowF = new V2[n]; sq.RowOk = new bool[n];
                    }
                    else Array.Clear(sq.RowOk, 0, sq.RowOk.Length);
                    sq.RowTick = tickNo;
                }
                if (!sq.RowOk[r]) { TrailAt(sq, u.SlotBack, out sq.RowP[r], out sq.RowF[r]); sq.RowOk[r] = true; }
                V2 p = sq.RowP[r], fw = sq.RowF[r];
                return new V2(p.x + fw.z * u.SlotLat, p.z - fw.x * u.SlotLat);
            }
            // строем вокруг точки отряда: шеренги симметрично вперёд и назад
            var f = new V2(MathF.Sin(sq.Facing), MathF.Cos(sq.Facing));
            float back = u.SlotBack - (sq.Rows - 1) * sq.T.Spacing / 2;
            return new V2(sq.Anchor.x + f.z * u.SlotLat - f.x * back, sq.Anchor.z - f.x * u.SlotLat - f.z * back);
        }

        /// <summary>
        /// Идти на своё место в строю. Стоящий отряд: боец встаёт на место и «успокаивается» (гистерезис 0,2/0,6 м) —
        /// соседи могут его чуть сдвинуть, но он не шаркает туда-сюда. Ошибку вдоль движения строя выбираем сильнее,
        /// поперёк — мягче (как в Company of Heroes); у места — лёгкий личный «дрейф».
        /// </summary>
        void MoveToSlot(Unit u, float dt, out float dx, out float dz)
        {
            var sq = u.Squad;
            var t = u.T;
            var slot = FormSlot(u);
            if (!sq.FormMarch)
            {
                if (u.DriftSeed == 0) u.DriftSeed = 1 + Rng.Rand() * 100;
                float ph = Time * 0.12f + u.DriftSeed;
                slot = new V2(slot.x + 0.13f * MathF.Sin(ph * 2.3f), slot.z + 0.13f * MathF.Cos(ph * 1.7f));
            }
            float sd = M.Hypot(slot.x - u.Pos.x, slot.z - u.Pos.z);
            dx = 0; dz = 0;
            bool still = M.Hypot(sq.AnchorVel.x, sq.AnchorVel.z) < 0.05f;
            if (u.Settled)
            {
                if (sd < 0.6f && still) return;
                u.Settled = false;
            }
            else if (sd < 0.2f && still) { u.Settled = true; return; }
            if (sd > 4.5f)
            { // отстал или отбился — догоняем по пути
                if (sq.FormMarch)
                {
                    TrailAt(sq, u.SlotBack, out var cp, out _);
                    if (M.Hypot(cp.x - u.Pos.x, cp.z - u.Pos.z) < sd) slot = cp;
                }
                var w = NavTarget(u, slot.x, slot.z);
                float wl = M.Hypot(w.x - u.Pos.x, w.z - u.Pos.z);
                if (wl > 0) { dx = (w.x - u.Pos.x) / wl * t.Speed; dz = (w.z - u.Pos.z) / wl * t.Speed; }
            }
            else
            {
                float vl = M.Hypot(sq.AnchorVel.x, sq.AnchorVel.z);
                float fx = vl > 0.05f ? sq.AnchorVel.x / vl : MathF.Sin(sq.Facing), fz = vl > 0.05f ? sq.AnchorVel.z / vl : MathF.Cos(sq.Facing);
                float ex = slot.x - u.Pos.x, ez = slot.z - u.Pos.z, al = ex * fx + ez * fz;
                float lx = ex - fx * al, lz = ez - fz * al;
                dx = sq.AnchorVel.x + fx * al * 1.6f + lx * 0.9f;
                dz = sq.AnchorVel.z + fz * al * 1.6f + lz * 0.9f;
                float l = M.Hypot(dx, dz), max = t.Speed * (Time < sq.RushUntil ? 1.4f : 1.08f);
                if (l > max) { dx *= max / l; dz *= max / l; }
            }
            YieldAhead(u, ref dx, ref dz, sd > 2.5f);
        }

        /// <summary>Сколько солдату до его места в строю (для проверок «стоит, а место далеко»).</summary>
        public float SlotGap(Unit u) { var s = FormSlot(u); return M.Hypot(s.x - u.Pos.x, s.z - u.Pos.z); }

        /// <summary>Не лезть в спину идущему впереди: притормаживаем, а не толкаемся.</summary>
        void YieldAhead(Unit u, ref float dx, ref float dz, bool sidestep = true)
        {
            float l = M.Hypot(dx, dz);
            if (l < 0.3f) return;
            float fx = dx / l, fz = dz / l, k = 1, bov = 0;
            Unit blk = null;
            CellOf(u.Pos.x, u.Pos.z, out int cx, out int cz);
            for (int z = Math.Max(cz - 1, 0); z <= Math.Min(cz + 1, gridDim - 1); z++)
                for (int x = Math.Max(cx - 1, 0); x <= Math.Min(cx + 1, gridDim - 1); x++)
                    for (int j = head[z * gridDim + x]; j >= 0; j = next[j])
                    {
                        if (j >= Units.Count) continue;
                        var o = Units[j];
                        if (o == u || !o.Alive || o.Team != u.Team) continue;
                        float ox = o.Pos.x - u.Pos.x, oz = o.Pos.z - u.Pos.z, d = M.Hypot(ox, oz);
                        float min = u.T.Radius + o.T.Radius;
                        if (d > min * 1.7f || d < 1e-3f) continue;
                        float ahead = (ox * fx + oz * fz) / d;
                        if (ahead < 0.7f) continue;
                        // тот, кто впереди, сам идёт туда же — держим дистанцию
                        float ov = o.Vel.x * fx + o.Vel.z * fz;
                        float free = M.Clamp((d - min) / (min * 0.7f), 0, 1);
                        float mine = M.Clamp(ov / l, 0, 1), kk = MathF.Max(free, mine);
                        if (kk < k) { k = kk; blk = o; bov = ov; }
                    }
            // упёрлись в стоящего из чужого отряда — не ждём вечно, а обходим его сбоку
            // (в своём строю ждём: там впереди своя шеренга, её не обгоняют)
            if (sidestep && blk != null && k < 0.4f && blk.Squad != u.Squad && bov < 0.3f * l)
            {
                float ox = blk.Pos.x - u.Pos.x, oz = blk.Pos.z - u.Pos.z;
                float lat = ox * fz - oz * fx;
                float sx = lat > 0 ? -fz : fz, sz = lat > 0 ? fx : -fx;
                dx = (fx * 0.35f + sx * 0.94f) * l * 0.7f;
                dz = (fz * 0.35f + sz * 0.94f) * l * 0.7f;
                return;
            }
            dx *= k; dz *= k;
        }
    }
}
