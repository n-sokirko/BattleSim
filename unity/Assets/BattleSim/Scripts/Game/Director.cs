using System.Collections.Generic;
using BattleSim.Core;
using UnityEngine;

namespace BattleSim
{
    /// <summary>
    /// Режиссёр боя: сам решает, что показать. Ловит события (натиск конницы — ещё до удара,
    /// первая сшибка свежих отрядов, бегство и цепная паника, гибель полководца или воеводы,
    /// жаркие места сечи по тепловой карте) и оценивает каждое:
    /// вес × размах √(min(N,60)/10) × свежесть e^(−t/4) × новизна 0,5^k × близость 1/(1+d/80).
    /// Правила монтажа: план держится не меньше 4 с и не дольше 12 с, сменяется на кандидата
    /// в 1,5 раза ярче, перебивается — если ярче в 2,5 раза; раз в ~45 с — общий план.
    /// Камера не перескакивает ось между армиями (правило 180°) — сменить сторону можно только
    /// через общий план вдоль оси. Дальние переброски (больше 50 м или поворот больше 90°) — склейкой,
    /// ближние — плавным проездом. В ударные мгновения — замедление времени.
    /// Ведёт камеру, когда включён автоматический режим (Tab); касание камеры возвращает её игроку.
    /// Всё считается без выделения памяти в кадре: события — заготовки, тепловая карта — массив.
    /// </summary>
    public sealed class Director
    {
        public enum Kind { None, Wide, Heat, Front, Contact, Charge, Rout, Cascade, Leader }

        /// <summary>Событие-кандидат на показ. Заготовки переиспользуются.</summary>
        sealed class Ev
        {
            public Kind Kind;
            public bool Live, Hit;
            public float T, N, W, Eta, HitT;
            public V2 P, Dir, HitP;
            public Squad Sq, Foe;
            public Unit U;
            public int Team;
            public string Caption;

            public void CopyFrom(Ev e)
            {
                Kind = e.Kind; Live = e.Live; Hit = e.Hit; T = e.T; N = e.N; W = e.W; Eta = e.Eta; HitT = e.HitT;
                P = e.P; Dir = e.Dir; HitP = e.HitP; Sq = e.Sq; Foe = e.Foe; U = e.U; Team = e.Team; Caption = e.Caption;
            }
        }

        /// <summary>Что мы помним об отряде: прошлый режим приказа и был ли он уже в рукопашной.</summary>
        sealed class Memo { public Mode Mode; public bool Ever; public float EverT; }

        readonly GameMain G;
        Battle B => G.Battle;
        CameraRig Rig => G.Rig;

        readonly Ev[] evs = new Ev[32];
        readonly Ev heatEv = new Ev { Kind = Kind.Heat }, frontEv = new Ev { Kind = Kind.Front };
        readonly Dictionary<Squad, Memo> memo = new Dictionary<Squad, Memo>();
        readonly HashSet<Unit> fallen = new HashSet<Unit>();
        readonly LogEntry[] logs = new LogEntry[24];
        int logHead;
        // бегства по армиям за последние секунды — для цепной паники
        const int RoutRing = 8;
        readonly float[,] routT = new float[2, RoutRing];
        readonly Squad[,] routSq = new Squad[2, RoutRing];
        readonly int[] routHead = new int[2];

        // тепловая карта сечи: клетки 8×8 м, +3 за павшего, +0,4 за удар, остывает ×0,9 в секунду
        const float HCell = 8f;
        float[] heat = new float[0];
        int hDim;
        float hHalf = -1;

        // текущий план (копия события: заготовка может уйти под новое)
        readonly Ev shot = new Ev();
        Ev shotSrc;
        Kind kind;
        float shotT0, lockUntil, baseYaw, yawOff, pitchAdd, occT, sideSign = 1, headYaw, orbitDir = 1;
        V2 aim;
        bool axial, flipAfter;

        // недавние планы — для новизны
        readonly Kind[] recentK = new Kind[16];
        readonly float[] recentT = new float[16];
        int recentHead;

        // положение камеры, которое ведёт режиссёр
        float cx, cz, cyaw, cpitch, cdist;
        bool cut;

        float now, evalT, lastWideT = -99, lastFlipT;
        int wideCount;
        V2 axis = new V2(0, 1);
        int side = 1;
        bool forceNext, wasDriving;

        // замедление: разгон 0,15 с, выдержка, возврат 0,5 с; не чаще раза в 12 с и не больше трёх в минуту
        float slow = 1, smFrom = 1, smTo = 1, smHold, smT;
        int smPhase, smHead;
        readonly float[] smStarts = { -999, -999, -999 };

        static readonly float[] Offs = { 0, 20 * M.DEG, -20 * M.DEG, 40 * M.DEG, -40 * M.DEG };
        static readonly string[] AccName = { "мечников", "варваров", "арбалетчиков", "рыцарей" };

        /// <summary>Режиссёр сейчас ведёт камеру.</summary>
        public bool Driving { get; private set; }
        /// <summary>Множитель скорости времени (замедление в ударные мгновения).</summary>
        public float TimeScale => slow;
        /// <summary>Подпись внизу кадра: текст, сколько ещё показывать и чья армия (-1 — ничья).</summary>
        public string Caption;
        public float CaptionT;
        public int CaptionTeam = -1;
        /// <summary>Журнал планов — для автосъёмки и отладки.</summary>
        public readonly List<string> History = new List<string>();

        public Director(GameMain game)
        {
            G = game;
            for (int i = 0; i < evs.Length; i++) evs[i] = new Ev();
            for (int i = 0; i < recentT.Length; i++) recentT[i] = -99;
            game.Battle.OnLog += e => { logs[logHead] = e; logHead = (logHead + 1) % logs.Length; };
        }

        /// <summary>Новый бой: забываем всё прошлое.</summary>
        public void Reset()
        {
            foreach (var e in evs) e.Live = false;
            heatEv.Live = frontEv.Live = false;
            memo.Clear();
            fallen.Clear();
            for (int i = 0; i < logs.Length; i++) logs[i] = null;
            for (int t = 0; t < 2; t++) for (int i = 0; i < RoutRing; i++) routSq[t, i] = null;
            for (int i = 0; i < recentT.Length; i++) recentT[i] = -99;
            HeatInit(true);
            kind = Kind.None; shotSrc = null;
            now = 0; evalT = 0; lastWideT = -99; lastFlipT = 0; wideCount = 0;
            forceNext = wasDriving = flipAfter = false;
            Driving = false;
            History.Clear();
            Caption = null; CaptionT = 0;
            smPhase = 0; slow = 1;
        }

        /// <summary>Клавиша N: сразу следующий лучший план.</summary>
        public void Next() { forceNext = true; evalT = 0; }

        /// <summary>Каждый кадр после шагов расчёта (события боя ещё не очищены).</summary>
        public void Update(float dt, float simDt)
        {
            bool fight = G.Phase == Phase.Fight;
            if (fight)
            {
                if (!G.Paused) now += dt;
                Collect(simDt);
            }
            Driving = fight && Rig.Cinematic && G.DirectorOn;
            if (Driving)
            {
                if (!wasDriving) Begin();
                if (!G.Paused && (evalT -= dt) <= 0) { evalT = 0.2f; Evaluate(); }
                Frame(dt);
            }
            else if (wasDriving) { kind = Kind.None; CaptionT = 0; }
            wasDriving = Driving;
            if (!G.Paused) StepSlowMo(dt);
            CaptionT -= dt;
        }

        // ---------------------------------------------------------------- сбор событий

        void Collect(float simDt)
        {
            HeatInit(false);
            if (simDt > 0)
            {
                float k = Mathf.Pow(0.9f, simDt);
                for (int i = 0; i < heat.Length; i++) heat[i] *= k;
            }
            var fx = B.Fx;
            for (int i = 0; i < fx.Count; i++)
            {
                var e = fx[i];
                switch (e.Kind)
                {
                    case FxKind.Hit:
                    case FxKind.Block: AddHeat(e.Pos.x, e.Pos.z, 0.4f); break;
                    case FxKind.Kill: AddHeat(e.Pos.x, e.Pos.z, 3f); break;
                    case FxKind.Charge: OnTrample(e); break;
                }
            }
            foreach (var sq in B.Squads)
            {
                if (sq.Special || sq.Size == 0) continue;
                if (!memo.TryGetValue(sq, out var m)) { m = new Memo { Mode = sq.Order.Mode }; memo[sq] = m; }
                var mode = sq.Order.Mode;
                if (mode == Mode.Rout && m.Mode != Mode.Rout && sq.Alive > 0) OnRout(sq);
                m.Mode = mode;
                if (sq.Engaged && !m.Ever) { m.Ever = true; m.EverT = now; OnContact(sq); }
            }
            for (int t = 0; t < 2; t++)
            {
                var c = B.Commanders[t];
                if (c != null) CheckLeader(c);
                foreach (var cap in B.Captains[t]) CheckLeader(cap);
            }
        }

        Ev Spawn(Kind k, V2 p, float w, float n)
        {
            Ev slot = null;
            foreach (var e in evs)
            {
                if (e == shotSrc) continue;
                if (!e.Live) { slot = e; break; }
                if (slot == null || e.T < slot.T) slot = e; // все заняты — вытесняем самое старое
            }
            slot.Kind = k; slot.Live = true; slot.Hit = false;
            slot.T = now; slot.P = p; slot.W = w; slot.N = n; slot.Eta = 0; slot.HitT = 0;
            slot.Dir = axis; slot.HitP = p; slot.Sq = slot.Foe = null; slot.U = null; slot.Team = -1; slot.Caption = null;
            return slot;
        }

        /// <summary>Рыцарь на скаку сбил соседей цели: натиск дошёл.</summary>
        void OnTrample(FxEvent e)
        {
            var p = new V2(e.Pos.x, e.Pos.z);
            AddHeat(p.x, p.z, 1f);
            if (kind == Kind.Charge && !shot.Hit && shot.Sq != null && D(shot.Sq.C, p) < 14) Impact(p, true);
            Ev ev = null;
            foreach (var x in evs)
                if (x.Live && x.Kind == Kind.Charge && ((x.Sq != null && D(x.Sq.C, p) < 14) || (x.Hit && D(x.HitP, p) < 12))) { ev = x; break; }
            if (ev == null)
            { // удар, который мы не предсказали
                var cav = NearestCav(p, 1 - e.Team, 16);
                ev = Spawn(Kind.Charge, p, 2.6f, 8);
                ev.Sq = cav; ev.Team = 1 - e.Team;
                ev.Dir = Norm(e.Dir.x, e.Dir.z, axis);
                ev.Caption = cav != null ? $"Удар конницы! {Defs.Cap(cav.Name)} {TeamGen(cav.Team)} врубились в строй" : "Удар конницы!";
            }
            if (!ev.Hit) { ev.Hit = true; ev.HitT = now; ev.HitP = p; ev.T = now; }
            ev.N = Mathf.Min(ev.N + 2, 60);
        }

        void OnRout(Squad sq)
        {
            int t = sq.Team;
            routT[t, routHead[t]] = now; routSq[t, routHead[t]] = sq;
            routHead[t] = (routHead[t] + 1) % RoutRing;
            int n = RoutCenter(t, 10, out var c, out float men);
            if (n >= 3)
            { // цепная паника: одно событие на армию, пока она длится
                Ev ev = null;
                foreach (var e in evs) if (e.Live && e.Kind == Kind.Cascade && e.Team == t) { ev = e; break; }
                if (ev == null) ev = Spawn(Kind.Cascade, c, 4f, men);
                ev.P = c; ev.N = men; ev.T = now; ev.Team = t; ev.Dir = FleeDir(sq);
                ev.Caption = $"Бегство! Дрогнули {n} {(n < 5 ? "отряда" : "отрядов")} {TeamGen(t)}";
                foreach (var e in evs) if (e.Live && e.Kind == Kind.Rout && e.Team == t && e != shotSrc) e.Live = false;
                return;
            }
            var r = Spawn(Kind.Rout, sq.C, 2.2f, sq.Size);
            r.Sq = sq; r.Team = t; r.Dir = FleeDir(sq);
            r.Caption = FindLog(Defs.Cap(sq.Name), "бегут", 3) ?? $"{Defs.Cap(sq.Name)} дрогнули и бегут!";
        }

        /// <summary>Центр отрядов армии, бежавших за последние sec секунд.</summary>
        int RoutCenter(int t, float sec, out V2 c, out float men)
        {
            int n = 0;
            float x = 0, z = 0;
            men = 0;
            for (int i = 0; i < RoutRing; i++)
            {
                var q = routSq[t, i];
                if (q == null || now - routT[t, i] > sec) continue;
                n++; x += q.Center.x; z += q.Center.z; men += q.Size;
            }
            c = n > 0 ? new V2(x / n, z / n) : new V2(0, 0);
            return n;
        }

        V2 FleeDir(Squad sq)
        {
            V2? from = sq.Foes.Count > 0 && sq.Foes[0].Alive > 0 ? sq.Foes[0].C : B.ArmyC[1 - sq.Team];
            var def = new V2(0, sq.Team == 0 ? -1 : 1);
            return from.HasValue ? Norm(sq.Center.x - from.Value.x, sq.Center.z - from.Value.z, def) : def;
        }

        void OnContact(Squad sq)
        {
            var foe = sq.Foes.Count > 0 && sq.Foes[0].Alive > 0 ? sq.Foes[0] : null;
            bool fresh = foe != null && (!memo.TryGetValue(foe, out var fm) || !fm.Ever || now - fm.EverT < 1.5f);
            V2 p = foe != null && D(foe.C, sq.C) < 25 ? Mid(sq.C, foe.C) : sq.C;
            int n = sq.Alive + (foe != null ? foe.Alive : 0);
            // обе стороны сходятся почти разом — это одно событие
            foreach (var e in evs)
                if (e.Live && e.Kind == Kind.Contact && now - e.T < 3 && D(e.P, p) < 16)
                {
                    e.N = Mathf.Max(e.N, n);
                    if (fresh && e.W < 2.6f) e.W = 2.6f;
                    return;
                }
            bool ambush = B.Time - sq.FirstStrikeT < 3;
            var ev = Spawn(Kind.Contact, p, ambush ? 3.2f : fresh ? 2.6f : 1.3f, n);
            ev.Sq = sq; ev.Foe = foe; ev.Team = sq.Team;
            ev.Dir = foe != null ? Norm(foe.C.x - sq.C.x, foe.C.z - sq.C.z, axis) : axis;
            if (ambush) ev.Caption = FindLog("Засада", Defs.Cap(sq.Name), 5) ?? $"Засада! {Defs.Cap(sq.Name)} бьют из укрытия";
            else if (fresh) ev.Caption = $"{Defs.Cap(TeamAdj(sq.Team))} {sq.Name} и {TeamAdj(foe.Team)} {foe.Name} сошлись врукопашную";
        }

        void CheckLeader(Commander c)
        {
            var u = c.Unit;
            if (u.Alive || fallen.Contains(u)) return;
            fallen.Add(u);
            bool general = c.Role != Role.Captain;
            var ev = Spawn(Kind.Leader, u.P, general ? 5f : 3.5f, 20);
            ev.U = u; ev.Team = c.Team;
            ev.Caption = FindLog(c.Name, "пал", 3) ?? $"{c.Title} пал!";
        }

        /// <summary>Строка летописи за последние maxAge секунд боя, где есть обе подстроки.</summary>
        string FindLog(string a, string b, float maxAge)
        {
            for (int k = 1; k <= logs.Length; k++)
            {
                var e = logs[(logHead - k + logs.Length) % logs.Length];
                if (e == null || B.Time - e.T > maxAge) break;
                if (e.Text.Contains(a) && (b == null || e.Text.Contains(b))) return e.Text;
            }
            return null;
        }

        Squad NearestCav(V2 p, int team, float r)
        {
            Squad best = null;
            float bd = r;
            foreach (var sq in B.Squads)
            {
                if (sq.Team != team || sq.Special || sq.Alive == 0 || sq.T.Charge <= 1) continue;
                float d = D(sq.C, p);
                if (d < bd) { bd = d; best = sq; }
            }
            return best;
        }

        // ---------------------------------------------------------------- тепловая карта

        void HeatInit(bool clear)
        {
            float half = B.World.Field + 10;
            if (half != hHalf)
            {
                hHalf = half;
                hDim = Mathf.CeilToInt(half * 2 / HCell);
                heat = new float[hDim * hDim];
            }
            else if (clear) System.Array.Clear(heat, 0, heat.Length);
        }

        void AddHeat(float x, float z, float v)
        {
            int ix = (int)((x + hHalf) / HCell), iz = (int)((z + hHalf) / HCell);
            if (ix < 0 || iz < 0 || ix >= hDim || iz >= hDim) return;
            heat[iz * hDim + ix] += v;
        }

        /// <summary>Сумма тепла в квадрате 3×3 клетки вокруг (ix, iz) и её «центр тяжести».</summary>
        float Block(int ix, int iz, out V2 c)
        {
            float s = 0, x = 0, z = 0;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int X = ix + dx, Z = iz + dz;
                    if (X < 0 || Z < 0 || X >= hDim || Z >= hDim) continue;
                    float h = heat[Z * hDim + X];
                    s += h; x += h * ((X + 0.5f) * HCell - hHalf); z += h * ((Z + 0.5f) * HCell - hHalf);
                }
            c = s > 1e-4f ? new V2(x / s, z / s) : new V2((ix + 0.5f) * HCell - hHalf, (iz + 0.5f) * HCell - hHalf);
            return s;
        }

        /// <summary>Самое жаркое место в радиусе r клеток от (ix, iz) (r &lt; 0 — по всей карте).</summary>
        float Hottest(int ix, int iz, int r, out V2 c)
        {
            int x0 = 0, x1 = hDim - 1, z0 = 0, z1 = hDim - 1;
            if (r >= 0) { x0 = Mathf.Max(0, ix - r); x1 = Mathf.Min(hDim - 1, ix + r); z0 = Mathf.Max(0, iz - r); z1 = Mathf.Min(hDim - 1, iz + r); }
            float best = 0;
            c = new V2((ix + 0.5f) * HCell - hHalf, (iz + 0.5f) * HCell - hHalf);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    if (heat[z * hDim + x] <= 0.05f) continue; // холодные клетки не проверяем
                    float s = Block(x, z, out var bc);
                    if (s > best) { best = s; c = bc; }
                }
            return best;
        }

        void UpdateHeat()
        {
            float s = Hottest(0, 0, -1, out var p);
            heatEv.Live = s > 1.5f;
            heatEv.P = p; heatEv.N = s / 2.5f; heatEv.T = now; heatEv.W = 1f; heatEv.Team = -1; heatEv.Dir = axis;
            if (kind == Kind.Heat)
            { // жаркое место плана ведём за самой сечей, но не дальше двух клеток за шаг
                int ix = (int)((aim.x + hHalf) / HCell), iz = (int)((aim.z + hHalf) / HCell);
                float ls = Hottest(ix, iz, 2, out var lp);
                if (ls > 0.3f) shot.P = new V2(M.Lerp(shot.P.x, lp.x, 0.35f), M.Lerp(shot.P.z, lp.z, 0.35f));
                shot.N = ls / 2.5f; shot.T = now;
            }
        }

        // ---------------------------------------------------------------- оценка и выбор плана

        void UpdateAxis()
        {
            var a = B.ArmyC[0];
            var b = B.ArmyC[1];
            if (!a.HasValue || !b.HasValue) return;
            float dx = b.Value.x - a.Value.x, dz = b.Value.z - a.Value.z, l = M.Hypot(dx, dz);
            if (l < 1) return;
            dx /= l; dz /= l;
            if (dx * axis.x + dz * axis.z < -0.3f)
            { // армии разошлись крест-накрест: ось развернулась — сторона съёмки та же самая
                axis = new V2(dx, dz); side = -side;
                return;
            }
            // ось поворачивается плавно, чтобы сторона съёмки не прыгала
            axis = Norm(axis.x + (dx - axis.x) * 0.1f, axis.z + (dz - axis.z) * 0.1f, axis);
        }

        /// <summary>Натиск, который вот-вот случится: отряд несётся на врага и будет у него меньше чем через 3 с.</summary>
        void PredictCharges()
        {
            foreach (var sq in B.Squads)
            {
                if (sq.Special || sq.Alive == 0 || sq.Engaged) continue;
                var o = sq.Order;
                if (o.Mode == Mode.Rout) continue;
                bool cav = sq.T.Charge > 1;
                Squad tg = null;
                if (o.Mode == Mode.Charge && o.Target != null && o.Target.Alive > 0) tg = o.Target;
                else if (cav && o.Mode == Mode.Advance && sq.Foes.Count > 0 && sq.Foes[0].Alive > 0 && sq.FoeDist < 45) tg = sq.Foes[0];
                if (tg == null) continue;
                float eta = Eta(sq, tg, out _, out var head, out float speed);
                if (speed < sq.T.Speed * 0.55f || eta > 3f || eta < 0) continue;
                Ev ev = null;
                foreach (var e in evs) if (e.Live && e.Kind == Kind.Charge && e.Sq == sq && !e.Hit) { ev = e; break; }
                if (ev == null)
                {
                    ev = Spawn(Kind.Charge, sq.C, cav ? 3.2f : 1.6f, 0);
                    ev.Sq = sq; ev.Team = sq.Team;
                    ev.Caption = $"Натиск {TeamGen(sq.Team)}! {Defs.Cap(sq.Name)} несутся на {Acc(tg)}";
                }
                ev.Foe = tg; ev.T = now; ev.Eta = eta; ev.P = sq.C; ev.Dir = head;
                ev.N = sq.Alive * (cav ? 2 : 1) + tg.Alive;
            }
        }

        /// <summary>Через сколько секунд передний боец отряда достанет строй цели; заодно — он сам, направление и скорость.</summary>
        float Eta(Squad sq, Squad tg, out V2 lead, out V2 head, out float speed)
        {
            float sp = 0, vx = 0, vz = 0, best = float.MaxValue;
            int n = 0;
            lead = sq.C;
            foreach (var u in sq.Units)
            {
                if (!u.Alive) continue;
                sp += u.CurSpeed; vx += u.Vel.x; vz += u.Vel.z; n++;
                float d = M.Hypot(u.Pos.x - tg.Center.x, u.Pos.z - tg.Center.z);
                if (d < best) { best = d; lead = u.P; }
            }
            var to = Norm(tg.Center.x - sq.Center.x, tg.Center.z - sq.Center.z, axis);
            if (n == 0) { head = to; speed = 0; return 99; }
            speed = sp / n;
            head = Norm(vx, vz, to);
            float reach = tg.T.Rows * tg.T.Spacing * 0.5f + 1.5f; // полглубины строя цели
            return (best - reach) / Mathf.Max(speed, 0.5f);
        }

        void UpdateFront()
        {
            Squad a = null;
            float bd = float.MaxValue;
            foreach (var sq in B.Squads)
            {
                if (sq.Special || sq.Alive == 0 || sq.Order.Mode == Mode.Rout || sq.Foes.Count == 0) continue;
                var f = sq.Foes[0];
                if (f.Alive == 0 || f.Order.Mode == Mode.Rout) continue;
                if (sq.FoeDist < bd) { bd = sq.FoeDist; a = sq; }
            }
            frontEv.Live = a != null;
            if (a == null) return;
            var foe = a.Foes[0];
            frontEv.Sq = a; frontEv.Foe = foe; frontEv.P = Mid(a.C, foe.C);
            frontEv.Dir = Norm(foe.Center.x - a.Center.x, foe.Center.z - a.Center.z, axis);
            frontEv.N = a.Alive + foe.Alive; frontEv.W = bd < 25 ? 1f : 0.6f; frontEv.T = now; frontEv.Team = -1;
            if (kind == Kind.Front) shot.T = now; // линия фронта — план «живой», пока пара рядом
        }

        void Evaluate()
        {
            UpdateAxis();
            PredictCharges();
            UpdateHeat();
            UpdateFront();
            foreach (var e in evs) if (e.Live && now - e.T > 12 && e != shotSrc) e.Live = false;

            float cur = CurrentScore();
            Ev best = null;
            float bs = 0;
            foreach (var e in evs) if (e.Live) Consider(e, ref best, ref bs);
            if (heatEv.Live) Consider(heatEv, ref best, ref bs);
            if (frontEv.Live) Consider(frontEv, ref best, ref bs);

            float held = now - shotT0;
            if (forceNext)
            {
                forceNext = false;
                if (best != null && kind != Kind.None) Start(best, bs); else StartWide();
                return;
            }
            if (kind == Kind.None) { if (best != null && bs > 1.2f && now > 6) Start(best, bs); else StartWide(); return; }
            if (now < lockUntil) return;
            if (kind != Kind.Wide && held >= 4 && now - lastWideT >= 45) { StartWide(); return; }
            if (best != null && held >= 1 && bs >= 2.5f * cur) { Start(best, bs); return; }
            if (best != null && held >= 4 && bs >= 1.5f * cur) { Start(best, bs); return; }
            if (held >= (kind == Kind.Wide ? 8 : 12))
            {
                if (best != null) Start(best, bs);
                else if (kind != Kind.Wide) StartWide();
            }
        }

        void Consider(Ev e, ref Ev best, ref float bs)
        {
            if (e == shotSrc || Same(e)) return;
            float s = Score(e, false);
            if (s > bs) { bs = s; best = e; }
        }

        /// <summary>Кандидат показывает то же, что и текущий план.</summary>
        bool Same(Ev e)
        {
            switch (kind)
            {
                case Kind.Heat: return e.Kind == Kind.Heat && D(e.P, shot.P) < 24;
                case Kind.Front:
                case Kind.Contact: return (e.Kind == Kind.Front || e.Kind == Kind.Contact) && ((e.Sq == shot.Sq && e.Foe == shot.Foe) || (e.Sq == shot.Foe && e.Foe == shot.Sq));
                case Kind.Charge: return e.Kind == Kind.Charge && e.Sq != null && e.Sq == shot.Sq;
                case Kind.Rout: return e.Kind == Kind.Rout && e.Sq == shot.Sq;
                case Kind.Cascade: return e.Kind == Kind.Cascade && e.Team == shot.Team;
                default: return false;
            }
        }

        float Score(Ev e, bool current)
        {
            float age = Mathf.Max(0, now - e.T);
            float s = e.W * Mathf.Sqrt(Mathf.Min(e.N, 60) / 10f) * Mathf.Exp(-age / 4f);
            int k = Recent(e.Kind) - (current ? 1 : 0);
            if (k > 0) s *= Mathf.Pow(0.5f, k);
            if (!current) s /= 1 + M.Hypot(e.P.x - cx, e.P.z - cz) / 80f;
            return s;
        }

        float CurrentScore()
        {
            switch (kind)
            {
                case Kind.None: return 0;
                case Kind.Wide: return 0.5f;
                case Kind.Front:
                case Kind.Contact:
                    if ((shot.Sq == null || shot.Sq.Alive == 0) && (shot.Foe == null || shot.Foe.Alive == 0)) return 0;
                    break;
                case Kind.Charge:
                    if (!shot.Hit && (shot.Sq == null || shot.Sq.Alive == 0)) return 0;
                    if (!shot.Hit && shotSrc != null && shotSrc.Live && shotSrc.Sq == shot.Sq) { shot.T = shotSrc.T; shot.N = shotSrc.N; }
                    break;
                case Kind.Rout:
                    if (shot.Sq == null || shot.Sq.Alive == 0) return 0;
                    if (shot.Sq.Order.Mode != Mode.Rout) return 0.3f; // опомнились
                    break;
            }
            return Score(shot, true);
        }

        int Recent(Kind k)
        {
            int n = 0;
            for (int i = 0; i < recentK.Length; i++) if (recentK[i] == k && now - recentT[i] <= 30) n++;
            return n;
        }

        // ---------------------------------------------------------------- планы

        void Begin()
        {
            cx = Rig.Target.x; cz = Rig.Target.z; cyaw = Rig.Yaw; cpitch = Rig.Pitch; cdist = Rig.Dist;
            UpdateAxis();
            var f = Fwd(cyaw);
            side = f.x * axis.z - f.z * axis.x >= 0 ? 1 : -1; // остаёмся на той стороне оси, где уже стоит камера
            kind = Kind.None; shotSrc = null; evalT = 0.2f; forceNext = false;
            Evaluate();
        }

        void Leave()
        {
            // показанное не повторяем — кроме натиска, который ещё не случился
            if (shotSrc != null && shotSrc != heatEv && shotSrc != frontEv && !(shotSrc.Kind == Kind.Charge && !shotSrc.Hit)) shotSrc.Live = false;
            shotSrc = null;
            if (flipAfter) { side = -side; flipAfter = false; lastFlipT = now; }
            lockUntil = 0; pitchAdd = 0; yawOff = 0; axial = false; occT = 1f;
        }

        void Remember(Kind k)
        {
            recentK[recentHead] = k; recentT[recentHead] = now;
            recentHead = (recentHead + 1) % recentK.Length;
        }

        void Start(Ev e, float score)
        {
            float keepYaw = cyaw;
            bool near = M.Hypot(e.P.x - cx, e.P.z - cz) < 20 && Fwd(cyaw).x * SideN.x + Fwd(cyaw).z * SideN.z > 0.1f;
            Leave();
            shot.CopyFrom(e);
            shotSrc = e;
            kind = e.Kind;
            shotT0 = now;
            aim = e.P;
            Remember(kind);
            var n = SideN;
            switch (kind)
            {
                case Kind.Heat:
                    // вдоль линии сечи, чуть наискосок; если уже смотрим рядом — не крутим камеру зря
                    baseYaw = near ? keepYaw : ClampSide(Yaw(n) + (Random.value < 0.5f ? 25 : -25) * M.DEG, 0.17f);
                    break;
                case Kind.Front:
                case Kind.Contact:
                {
                    var along = new V2(e.Dir.z, -e.Dir.x);
                    if (along.x * n.x + along.z * n.z < 0) along = new V2(-along.x, -along.z);
                    baseYaw = near ? keepYaw : ClampSide(Yaw(along) + (Random.value < 0.5f ? 15 : -15) * M.DEG, 0.17f);
                    break;
                }
                case Kind.Charge:
                {
                    // три четверти сзади-сбоку: камера за отрядом, чуть в стороне от оси армий
                    headYaw = Yaw(e.Dir);
                    sideSign = TowardSide(headYaw, 35 * M.DEG) > headYaw ? 1 : -1;
                    baseYaw = headYaw + sideSign * 35 * M.DEG;
                    break;
                }
                case Kind.Rout:
                case Kind.Cascade:
                    // из-за спин победителей — вслед бегущим
                    baseYaw = ClampSide(TowardSide(Yaw(e.Dir), 25 * M.DEG), 0);
                    break;
                case Kind.Leader:
                {
                    // облёт павшего: начинаем в стороне и обходим через «свою» сторону
                    float y0 = Yaw(n);
                    orbitDir = Random.value < 0.5f ? 1 : -1;
                    baseYaw = y0 - orbitDir * 40 * M.DEG;
                    RequestSlowMo(0.35f, 1.5f);
                    lockUntil = now + 2f;
                    break;
                }
            }
            if (kind == Kind.Charge && shot.Hit) Impact(shot.HitP, now - shot.HitT < 0.6f, false); // удар уже был — сразу сбоку
            else
            {
                Desired(0, out float x, out float z, out float yaw, out float pitch, out float dist, false);
                if (kind != Kind.Charge) yawOff = FindClear(x, z, yaw, pitch, dist);
                Cut(x, z, yaw + yawOff);
            }
            Announce(score);
        }

        void StartWide()
        {
            Leave();
            kind = Kind.Wide;
            shot.Kind = Kind.Wide; shot.Caption = null; shot.T = now; shot.Team = -1; shot.Sq = shot.Foe = null; shot.U = null;
            shotT0 = now; lastWideT = now; wideCount++;
            Remember(kind);
            // каждый второй общий план (не чаще раза в минуту) — вдоль оси из-за спин армии: после него можно перейти на другую сторону
            axial = wideCount % 2 == 0 && now - lastFlipT > 60;
            if (axial)
            {
                var f = Fwd(cyaw);
                var ax = f.x * axis.x + f.z * axis.z >= 0 ? axis : new V2(-axis.x, -axis.z);
                baseYaw = Yaw(ax) + (Random.value < 0.5f ? 12 : -12) * M.DEG;
                flipAfter = true;
            }
            else baseYaw = ClampSide(Yaw(SideN) + (wideCount % 2 == 1 ? 28 : -28) * M.DEG, 0.17f);
            Desired(0, out float x, out float z, out float yaw, out float pitch, out float dist, false);
            aim = new V2(x, z);
            yawOff = FindClear(x, z, yaw, pitch, dist);
            Cut(x, z, yaw + yawOff);
            Announce(0.5f);
        }

        /// <summary>Дальний перенос или крутой поворот — склейка, иначе — плавный проезд.</summary>
        void Cut(float x, float z, float yaw)
        {
            if (M.Hypot(x - cx, z - cz) > 50 || Mathf.Abs(M.WrapAngle(yaw - cyaw)) > 90 * M.DEG) cut = true;
        }

        void Announce(float score)
        {
            if (!string.IsNullOrEmpty(shot.Caption) && kind != Kind.Wide)
            {
                Caption = shot.Caption; CaptionT = 2.5f; CaptionTeam = shot.Team;
            }
            if (History.Count >= 80) History.RemoveAt(0);
            History.Add($"{B.Time:F1} с: {KindName(kind)} ({score:F2}){(cut ? ", склейка" : "")}{(kind != Kind.Wide && shot.Caption != null ? " — " + shot.Caption : "")}");
        }

        /// <summary>Удар натиска: камера сбоку, поперёк удара, вплотную — и замедление.</summary>
        void Impact(V2 p, bool slowMo, bool log = true)
        {
            shot.Hit = true; shot.HitP = p; shot.HitT = now;
            var h = Fwd(headYaw);
            var n = SideN;
            var a = new V2(h.z, -h.x);
            if (a.x * n.x + a.z * n.z < 0) a = new V2(-a.x, -a.z);
            baseYaw = Yaw(a);
            pitchAdd = 0;
            Desired(0, out float x, out float z, out float yaw, out float pitch, out float dist, false);
            yawOff = FindClear(x, z, yaw, pitch, dist);
            cut = true;
            if (slowMo) RequestSlowMo(0.3f, 1.2f);
            lockUntil = now + 2.5f;
            shotT0 = now;
            if (log && History.Count < 80) History.Add($"{B.Time:F1} с: удар натиска{(slowMo && slow < 1 ? ", замедление" : "")}");
        }

        /// <summary>Каждый кадр: следим за героем плана и ведём камеру.</summary>
        void Frame(float dt)
        {
            if (kind == Kind.None) return;
            if (kind == Kind.Charge && !shot.Hit && shot.Sq != null && shot.Sq.Alive > 0)
            { // натиск вот-вот ударит: склейка сбоку заранее, чтобы удар пришёлся на замедление
                var tg = shot.Foe;
                if (tg != null && tg.Alive > 0)
                {
                    float eta = Eta(shot.Sq, tg, out var lead, out var head, out _);
                    if (eta < 0.3f || shot.Sq.Engaged) Impact(new V2(lead.x + head.x * 1.5f, lead.z + head.z * 1.5f), true);
                }
                else if (shot.Sq.Engaged) Impact(shot.Sq.C, true);
            }
            Desired(dt, out float x, out float z, out float yaw, out float pitch, out float dist, true);
            if ((occT -= dt) <= 0 && !cut)
            { // герой ушёл за дом или стену — ищем другой угол
                occT = 1f;
                if (!Clear(x, z, yaw, pitch, dist))
                {
                    float y0 = yaw - yawOff;
                    yawOff = FindClear(x, z, y0, pitch - pitchAdd, dist);
                    yaw = y0 + yawOff;
                }
            }
            if (cut)
            {
                cut = false;
                cx = x; cz = z; cyaw = yaw; cpitch = pitch; cdist = dist;
                Rig.LookAt(x, z, yaw, pitch, dist, true);
            }
            else
            {
                float kp = 1 - Mathf.Exp(-2.5f * dt), ka = 1 - Mathf.Exp(-1.5f * dt);
                cx += (x - cx) * kp; cz += (z - cz) * kp; cdist += (dist - cdist) * kp;
                cyaw += M.WrapAngle(yaw - cyaw) * ka; cpitch += (pitch - cpitch) * ka;
                Rig.Drive(cx, cz, cyaw, cpitch, cdist);
            }
            Rig.Apply();
        }

        /// <summary>Куда должна смотреть камера по текущему плану.</summary>
        void Desired(float dt, out float x, out float z, out float yaw, out float pitch, out float dist, bool withOff)
        {
            V2 p = aim;
            float y = baseYaw;
            pitch = 24; dist = 28;
            switch (kind)
            {
                case Kind.Wide:
                {
                    var a = B.ArmyC[0];
                    var b = B.ArmyC[1];
                    float sep = 60;
                    if (a.HasValue && b.HasValue) { p = Mid(a.Value, b.Value); sep = V2.Dist(a.Value, b.Value); }
                    else { var c = B.Centroid(); if (c.HasValue) p = new V2(c.Value.x, c.Value.z); }
                    pitch = 38; dist = Mathf.Clamp(1.1f * sep, 45, 150);
                    break;
                }
                case Kind.Heat:
                    p = shot.P; pitch = 24; dist = 28;
                    break;
                case Kind.Front:
                case Kind.Contact:
                {
                    bool a = shot.Sq != null && shot.Sq.Alive > 0, b = shot.Foe != null && shot.Foe.Alive > 0;
                    if (a && b && D(shot.Sq.C, shot.Foe.C) < 40) p = Mid(shot.Sq.C, shot.Foe.C);
                    else if (a) p = shot.Sq.C;
                    else if (b) p = shot.Foe.C;
                    pitch = 20; dist = 30;
                    break;
                }
                case Kind.Charge:
                    if (shot.Hit) { p = shot.HitP; pitch = 12; dist = 13; }
                    else if (shot.Sq != null && shot.Sq.Alive > 0)
                    { // ведём с упреждением 1,2 с, разворачиваясь за направлением скачки
                        float vx = 0, vz = 0;
                        int n = 0;
                        foreach (var u in shot.Sq.Units) if (u.Alive) { vx += u.Vel.x; vz += u.Vel.z; n++; }
                        if (n > 0) { vx /= n; vz /= n; }
                        var c = shot.Sq.C;
                        p = new V2(c.x + vx * 1.2f, c.z + vz * 1.2f);
                        if (vx * vx + vz * vz > 1 && dt > 0) headYaw += M.WrapAngle(Mathf.Atan2(vx, vz) - headYaw) * (1 - Mathf.Exp(-3 * dt));
                        y = headYaw + sideSign * 35 * M.DEG;
                        pitch = 16; dist = 22;
                    }
                    else { pitch = 16; dist = 22; }
                    break;
                case Kind.Rout:
                    if (shot.Sq != null && shot.Sq.Alive > 0) p = shot.Sq.C;
                    pitch = 30; dist = 45;
                    break;
                case Kind.Cascade:
                    if (RoutCenter(shot.Team, 20, out var rc, out _) > 0) p = rc;
                    pitch = 34; dist = 55;
                    break;
                case Kind.Leader:
                    if (shot.U != null) p = shot.U.P;
                    y = baseYaw + orbitDir * 10 * M.DEG * (now - shotT0);
                    pitch = 22; dist = 14;
                    break;
            }
            pitch *= M.DEG;
            if (B.World.Type == MapType.City) pitch = Mathf.Max(pitch, 32 * M.DEG); // в городе — поверх крыш
            pitch += pitchAdd;
            aim = p;
            x = p.x; z = p.z;
            yaw = y + (withOff ? yawOff : 0);
            if (!axial && kind != Kind.Wide) yaw = ClampSide(yaw, 0); // правило 180°: ось армий не переходим (граница — без скачка)
        }

        /// <summary>
        /// Угол без помех: сам угол, ±20°, ±40° (не переходя ось), затем то же чуть сверху.
        /// Возвращает поправку к углу; pitchAdd — сколько добавили к наклону.
        /// </summary>
        float FindClear(float x, float z, float yaw, float pitch, float dist)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                float add = pass * 14 * M.DEG;
                for (int i = 0; i < Offs.Length; i++)
                {
                    float y = axial ? yaw + Offs[i] : ClampSide(yaw + Offs[i], 0);
                    if (Clear(x, z, y, pitch + add, dist)) { pitchAdd += add; return M.WrapAngle(y - yaw); }
                }
            }
            return 0;
        }

        bool Clear(float x, float z, float yaw, float pitch, float dist)
        {
            var W = B.World;
            float gy = W.GroundAt(x, z) + 1;
            float cp = Mathf.Cos(pitch);
            float px = x - Mathf.Sin(yaw) * cp * dist, pz = z - Mathf.Cos(yaw) * cp * dist, py = gy + Mathf.Sin(pitch) * dist;
            float min = W.GroundAt(px, pz) + 1.5f;
            if (py < min) py = min;
            return W.Los(px, py, pz, x, gy + 0.4f, z);
        }

        // ---------------------------------------------------------------- замедление

        bool RequestSlowMo(float k, float hold)
        {
            if (!Driving) return false;
            float t = Time.unscaledTime, last = -999;
            int recent = 0;
            foreach (var s in smStarts) { if (t - s < 60) recent++; if (s > last) last = s; }
            if (t - last < 12 || recent >= 3) return false;
            smStarts[smHead] = t; smHead = (smHead + 1) % smStarts.Length;
            smFrom = slow; smTo = k; smHold = hold; smT = 0; smPhase = 1;
            return true;
        }

        void StepSlowMo(float dt)
        {
            if (!Driving && (smPhase == 1 || smPhase == 2)) { smPhase = 3; smT = 0; smFrom = slow; }
            switch (smPhase)
            {
                case 1:
                    smT += dt;
                    slow = Mathf.Lerp(smFrom, smTo, Mathf.SmoothStep(0, 1, smT / 0.15f));
                    if (smT >= 0.15f) { smPhase = 2; smT = 0; slow = smTo; }
                    break;
                case 2:
                    smT += dt;
                    if (smT >= smHold) { smPhase = 3; smT = 0; smFrom = slow; }
                    break;
                case 3:
                    smT += dt;
                    slow = Mathf.Lerp(smFrom, 1, Mathf.SmoothStep(0, 1, smT / 0.5f));
                    if (smT >= 0.5f) { smPhase = 0; slow = 1; }
                    break;
            }
        }

        // ---------------------------------------------------------------- помощники

        /// <summary>Нормаль к оси армий в ту сторону, куда смотрит камера (камера стоит по другую сторону).</summary>
        V2 SideN => new V2(axis.z * side, -axis.x * side);

        /// <summary>Не даём камере перейти ось: взгляд должен смотреть в «свою» сторону хотя бы на min (синус угла).</summary>
        float ClampSide(float yaw, float min)
        {
            var n = SideN;
            var f = Fwd(yaw);
            float d = f.x * n.x + f.z * n.z;
            if (d >= min) return yaw;
            if (d < 0) { f = new V2(f.x - 2 * d * n.x, f.z - 2 * d * n.z); d = -d; } // отражаем через ось
            if (d < min)
            {
                float al = f.x * axis.x + f.z * axis.z, s = Mathf.Sqrt(Mathf.Max(0, 1 - min * min)) * (al >= 0 ? 1 : -1);
                f = new V2(axis.x * s + n.x * min, axis.z * s + n.z * min);
            }
            return Yaw(f);
        }

        /// <summary>Поворот yaw на ±deg — в ту сторону, что ближе к «своей» стороне оси.</summary>
        float TowardSide(float yaw, float deg)
        {
            var n = SideN;
            var a = Fwd(yaw + deg);
            var b = Fwd(yaw - deg);
            return a.x * n.x + a.z * n.z >= b.x * n.x + b.z * n.z ? yaw + deg : yaw - deg;
        }

        static float Yaw(V2 f) => Mathf.Atan2(f.x, f.z);
        static V2 Fwd(float yaw) => new V2(Mathf.Sin(yaw), Mathf.Cos(yaw));
        static float D(V2 a, V2 b) => M.Hypot(a.x - b.x, a.z - b.z);
        static V2 Mid(V2 a, V2 b) => new V2((a.x + b.x) / 2, (a.z + b.z) / 2);
        static V2 Norm(float x, float z, V2 def) { float l = M.Hypot(x, z); return l > 1e-4f ? new V2(x / l, z / l) : def; }
        static string TeamGen(int t) => t == 0 ? "синих" : "красных";
        static string TeamAdj(int t) => t == 0 ? "синие" : "красные";
        static string Acc(Squad q) => (q.Type < AccName.Length ? AccName[q.Type] : q.Name) + (q.Num > 0 ? " " + Defs.Roman(q.Num) : "");

        static string KindName(Kind k)
        {
            switch (k)
            {
                case Kind.Wide: return "общий план";
                case Kind.Heat: return "гуща сечи";
                case Kind.Front: return "линия фронта";
                case Kind.Contact: return "сшибка";
                case Kind.Charge: return "натиск";
                case Kind.Rout: return "бегство";
                case Kind.Cascade: return "паника";
                case Kind.Leader: return "гибель вождя";
                default: return "—";
            }
        }
    }
}
