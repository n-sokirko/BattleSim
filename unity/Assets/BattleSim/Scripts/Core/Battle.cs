using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleSim.Core
{
    public sealed class CmdSpec { public string Name; public Trait Trait; }

    /// <summary>Бой: солдаты, отряды, приказы, боевой дух, полководцы и гонцы.</summary>
    public sealed partial class Battle
    {
        public readonly World World;
        public List<Unit> Units = new List<Unit>();
        public List<UnitPlan> Plan = new List<UnitPlan>();
        public List<Squad> Squads = new List<Squad>();
        public int[] Alive = new int[2], PlanCount = new int[2];
        public bool Fighting, UseCommanders = true;
        public readonly List<Unit>[] Teams = { new List<Unit>(), new List<Unit>() };
        public float Time;
        int squadSeq;
        public Commander[] Commanders = new Commander[2];
        public List<Commander>[] Captains = { new List<Commander>(), new List<Commander>() };
        public List<Wing>[] Wings = { new List<Wing>(), new List<Wing>() };
        public Squad[] Couriers = new Squad[2];
        public List<LogEntry> Log = new List<LogEntry>();
        public event Action<LogEntry> OnLog;
        public bool[] GlareLogged = new bool[2];
        public readonly Bolts Bolts;
        public V2?[] ArmyC = new V2?[2];
        public int PathBudget;
        /// <summary>Бюджет перебора клеток поиском пути на один шаг: длинные поиски разносим по шагам, без рывков.</summary>
        public int PathWork;
        /// <summary>Когда кого-то последний раз ранили (для правила «армии разошлись»).</summary>
        public float LastHitT;

        /// <summary>Армии разошлись: 50 с никто не пал — поле за тем, у кого больше войск.</summary>
        public bool Disengaged => Fighting && Time > 60 && Time - LastKillT > 50;
        public float LastKillT;
        float foesT;
        CmdSpec[] cmdSpec;
        /// <summary>Раса каждой армии.</summary>
        public RaceDef[] Races = { Defs.Rus, Defs.Rus };
        /// <summary>Тип бойца армии team на месте slot (0 строевые, 1 ударные, 2 стрелки, 3 конница).</summary>
        public int Ty(int team, int slot) => Races[team].Units[slot].Id;
        /// <summary>Ярость орды (0–100) и до какого времени армия ревёт.</summary>
        public readonly float[] Rage = new float[2], RoarUntil = new float[2];

        /// <summary>Предел солдат на поле (на слабых телефонах меньше).</summary>
        public static int MaxUnits = 3200;

        /// <summary>Длительность клипа атаки (для темпа анимации удара); задаёт Unity-слой.</summary>
        public Func<int, string, float> ClipDur;

        // сетка соседей
        const float GRID = 2.5f;
        float gridHalf = -1;
        int gridDim;
        int[] head = new int[0];
        int[] next = new int[1024];
        float[] push = new float[2048];

        public Battle(World world)
        {
            World = world;
            Bolts = new Bolts(this);
        }

        public void AddLog(int team, string text)
        {
            var e = new LogEntry { T = Time, Team = team, Text = text };
            Log.Add(e);
            OnLog?.Invoke(e);
        }

        // ---------------------------------------------------------------- расстановка

        public int PlaceSquad(int type, int team, float cx, float cz, float yaw)
        {
            var t = Defs.All[type];
            float cos = MathF.Cos(yaw), sin = MathF.Sin(yaw);
            int id = ++squadSeq;
            var sq = new Squad(id.ToString(), type, team, yaw);
            // бойца — только туда, откуда можно дойти до середины отряда (не на отрезанный уступ рядом с ней),
            // и на её уровне: не на башню или боевой ход над ней (туда тоже можно дойти — но кругом, по лестнице)
            var nav = World.Nav; int cls = t.Mount ? 1 : 0, ci = nav.Idx(cx, cz);
            int region = ci >= 0 && nav.Speed[cls][ci] > 0 ? nav.Region[cls][ci] : -1;
            float cy = World.GroundAt(cx, cz);
            for (int r = 0; r < t.Rows; r++)
                for (int c = 0; c < t.Cols; c++)
                {
                    if (Units.Count >= MaxUnits) break;
                    float ox = (c - (t.Cols - 1) / 2f) * t.Spacing, oz = -(r - (t.Rows - 1) / 2f) * t.Spacing;
                    float jx = ox + (Rng.Rand() - 0.5f) * 0.2f, jz = oz + (Rng.Rand() - 0.5f) * 0.2f;
                    float x = cx + jx * cos + jz * sin, z = cz - jx * sin + jz * cos;
                    if (!World.InField(x, z, 1) || !World.Walkable(x, z, t.Radius) || Occupied(x, z, t.Radius)) continue;
                    int ui = nav.Idx(x, z);
                    if (region >= 0 && ui >= 0 && nav.Region[cls][ui] != region) continue;
                    if (MathF.Abs(World.GroundAt(x, z) - cy) > 1.5f) continue;
                    var plan = new UnitPlan { Type = type, Team = team, X = x, Z = z, Yaw = yaw, Variant = (int)(Rng.Rand() * 2), Squad = id, Ox = ox, Oz = oz };
                    Plan.Add(plan);
                    var u = new Unit(plan, World) { Squad = sq };
                    sq.Units.Add(u);
                    Units.Add(u);
                }
            if (sq.Units.Count > 0) { sq.Size = sq.Units.Count; sq.Refresh(0); Squads.Add(sq); }
            NumberSquads();
            Recount();
            return sq.Units.Count;
        }

        bool Occupied(float x, float z, float radius)
        {
            foreach (var u in Units)
            {
                float min = (radius + u.T.Radius) * 0.9f, dx = u.Pos.x - x, dz = u.Pos.z - z;
                if (dx * dx + dz * dz < min * min) return true;
            }
            return false;
        }

        public int RemoveNear(float x, float z, float radius)
        {
            int before = Units.Count;
            Units = Units.Where(u => { float dx = u.Pos.x - x, dz = u.Pos.z - z; return dx * dx + dz * dz > radius * radius; }).ToList();
            var keep = new HashSet<Unit>(Units);
            foreach (var sq in Squads) sq.Units = sq.Units.Where(keep.Contains).ToList();
            Squads = Squads.Where(sq => sq.Units.Count > 0).ToList();
            NumberSquads();
            Plan = Units.Select(u => u.Plan).ToList();
            Recount();
            return before - Units.Count;
        }

        void ClearUnits()
        {
            Units = new List<Unit>(); Squads = new List<Squad>();
            Commanders = new Commander[2]; Couriers = new Squad[2];
            Captains = new[] { new List<Commander>(), new List<Commander>() };
            Wings = new[] { new List<Wing>(), new List<Wing>() };
            Bolts.Clear();
            Fighting = false;
            Time = 0; Log = new List<LogEntry>(); GlareLogged = new bool[2];
        }

        public void ClearAll() { ClearUnits(); Plan = new List<UnitPlan>(); cmdSpec = null; Recount(); }

        public void ResetToPlan()
        {
            ClearUnits();
            var map = new Dictionary<string, Squad>();
            Plan = Plan.Where(p => World.InField(p.X, p.Z, 1) && World.Walkable(p.X, p.Z, 0.4f)).ToList();
            foreach (var p in Plan)
            {
                var u = new Unit(p, World);
                Units.Add(u);
                string key = p.Squad >= 0 ? p.Squad.ToString() : "t" + p.Team + "_" + p.Type;
                if (!map.TryGetValue(key, out var sq)) { sq = new Squad(key, p.Type, p.Team, p.Yaw); map[key] = sq; Squads.Add(sq); }
                sq.Units.Add(u); u.Squad = sq;
                if (p.Squad >= 0) squadSeq = Math.Max(squadSeq, p.Squad);
            }
            foreach (var sq in Squads) { sq.Size = sq.Units.Count; sq.Refresh(0); }
            NumberSquads();
            Recount();
        }

        /// <summary>«Мечники I, II, III» — чтобы в летописи было понятно, кому какой приказ.</summary>
        void NumberSquads()
        {
            for (int team = 0; team < 2; team++)
                for (int type = 0; type < Defs.All.Length; type++)
                {
                    var list = Squads.Where(q => q.Team == team && q.Type == type).OrderBy(q => q.Center.x).ToList();
                    for (int i = 0; i < list.Count; i++) list[i].Num = list.Count > 1 ? i + 1 : 0;
                }
        }

        public void SetPlan(List<UnitPlan> plan) { Plan = plan.ToList(); ResetToPlan(); }

        void Recount()
        {
            PlanCount = new int[2];
            foreach (var p in Plan) PlanCount[p.Team]++;
            Alive = (int[])PlanCount.Clone();
        }

        /// <summary>Случайные армии: size 1 — стычка, 2 — сражение, 3 — великая сеча.</summary>
        public void RandomArmies(int size = 1)
        {
            ClearAll();
            int[][] cfgFront = { null, new[] { 3, 5 }, new[] { 6, 8 }, new[] { 11, 13 } };
            int[][] cfgXbow = { null, new[] { 2, 3 }, new[] { 4, 5 }, new[] { 7, 9 } };
            int[][] cfgCav = { null, new[] { 1, 2 }, new[] { 2, 4 }, new[] { 4, 6 } };
            int[] cfgLines = { 0, 1, 2, 4 };
            size = M.Clamp(size, 1, 3);
            int Pick(int[] ab) => ab[0] + (int)(Rng.Rand() * (ab[1] - ab[0] + 1));
            if (World.Town != null) { SiegeArmies(size, cfgFront[size], cfgXbow[size], cfgCav[size], cfgLines[size]); return; }
            float z0 = World.SpawnZ;
            int lines = cfgLines[size];
            for (int team = 0; team < 2; team++)
            {
                float dir = team == 0 ? -1 : 1, yaw = team == 0 ? 0 : M.PI;
                for (int line = 0; line < lines; line++)
                {
                    int front = Pick(cfgFront[size]);
                    for (int i = 0; i < front; i++)
                    {
                        int type = Ty(team, Rng.Rand() < 0.55f ? 0 : 1);
                        float x = (i - (front - 1) / 2f) * 10 + (Rng.Rand() - 0.5f) * 2;
                        PlaceSquad(type, team, x, dir * (z0 + line * 8 + Rng.Rand() * 2), yaw);
                    }
                }
                int xbRows = Math.Max(1, (int)Math.Ceiling(lines / 2f));
                for (int row = 0; row < xbRows; row++)
                {
                    int n = Pick(cfgXbow[size]);
                    for (int i = 0; i < n; i++)
                    {
                        float x = (i - (n - 1) / 2f) * 11 + (Rng.Rand() - 0.5f) * 2;
                        PlaceSquad(Ty(team, 2), team, x, dir * (z0 + 2 + lines * 8 + row * 6 + Rng.Rand() * 2), yaw);
                    }
                }
                int cav = Pick(cfgCav[size]);
                for (int i = 0; i < cav; i++)
                {
                    float side = i % 2 == 1 ? -1 : 1;
                    int k = i / 2;
                    float x = side * (World.Field * 0.72f + k * 3 - Rng.Rand() * 4);
                    PlaceSquad(Ty(team, 3), team, x, dir * (z0 + 4 + k * 10 + Rng.Rand() * 4), yaw);
                }
            }
        }

        /// <summary>
        /// Осада города: красные — гарнизон (пехота у ворот и на улицах, стрелки у южной стены,
        /// конница в резерве у площади), синие — штурмуют с юга через двое ворот и пролом.
        /// </summary>
        void SiegeArmies(int size, int[] front, int[] xbow, int[] cav, int lines)
        {
            var town = World.Town;
            int Pick(int[] ab) => ab[0] + (int)(Rng.Rand() * (ab[1] - ab[0] + 1));
            // Штурм: как обычная армия, но на треть больше пехоты
            float z0 = town.CZ + 17; // за рекой, что течёт вдоль южной стены
            for (int line = 0; line < lines; line++)
            {
                int n = Pick(front) + 1;
                for (int i = 0; i < n; i++)
                    PlaceSquad(Ty(0, Rng.Rand() < 0.55f ? 0 : 1), 0, (i - (n - 1) / 2f) * 10 + (Rng.Rand() - 0.5f) * 2, -(z0 + line * 8 + Rng.Rand() * 2), 0);
            }
            int xr = Pick(xbow);
            for (int i = 0; i < xr; i++) PlaceSquad(Ty(0, 2), 0, (i - (xr - 1) / 2f) * 11, -(z0 - 5), 0); // стрелки впереди, у берега
            int cv = Pick(cav);
            for (int i = 0; i < cv; i++)
            {
                float side = i % 2 == 1 ? -1 : 1;
                PlaceSquad(Ty(0, 3), 0, side * (town.CX * 0.85f + (i / 2) * 3), -(z0 + 6 + (i / 2) * 10), 0);
            }

            // Гарнизон: места на улицах южной половины города
            var spots = new List<V2>();
            foreach (var st in town.Streets)
            {
                float len = M.Hypot(st.Bx - st.Ax, st.Bz - st.Az);
                for (float t = 4; t < len - 4; t += 9)
                {
                    float x = M.Lerp(st.Ax, st.Bx, t / len), z = M.Lerp(st.Az, st.Bz, t / len);
                    if (MathF.Abs(x) < town.CX - 6 && z > -town.CZ + 6 && z < town.CZ * 0.4f && World.Walkable(x, z, 1)) spots.Add(new V2(x, z));
                }
            }
            // ближе к южной стене — важнее
            spots = spots.OrderBy(p => p.z + Rng.Rand() * 12).ToList();
            var used = new List<V2>();
            bool Put(int type, V2 near, float maxR)
            {
                foreach (var p in spots.OrderBy(q => V2.Dist(q, near)))
                {
                    if (V2.Dist(p, near) > maxR) break;
                    if (used.Any(q => V2.Dist(q, p) < 9)) continue;
                    int placed = PlaceSquad(Ty(1, type), 1, p.x, p.z, M.PI);
                    if (placed >= Defs.All[Ty(1, type)].Cols * Defs.All[Ty(1, type)].Rows * 0.6f) { used.Add(p); return true; }
                    if (placed > 0) RemoveSquadAt(p);
                }
                return false;
            }
            // Гарнизон рубежа: ставим прямо в точку, держит её (командиры его не трогают)
            bool Hold(int type, float x, float z, float yaw, float leash, string why)
            {
                int placed = PlaceSquad(Ty(1, type), 1, x, z, yaw);
                if (placed >= Defs.All[Ty(1, type)].Cols * Defs.All[Ty(1, type)].Rows * 0.6f)
                {
                    var g = Squads[Squads.Count - 1];
                    g.Garrison = true;
                    g.Order = new Order(type == 2 ? OrderKind.Fire : OrderKind.Hold, Mode.Hold) { Leash = leash, Why = why }.At(x, z);
                    return true;
                }
                if (placed > 0) RemoveSquadAt(new V2(x, z));
                return false;
            }
            int def = Math.Max(3, (Pick(front) * lines) * 2 / 3);
            // у каждого входа — заслон
            foreach (var e in town.SouthEntries) Put(Rng.Rand() < 0.5f ? 0 : 1, new V2(e.x, e.z + 10), 14);
            int placedDef = town.SouthEntries.Count;
            // наверху каждого подъёма в верхний город — заслон; у ворот замка — стража
            foreach (var cl in town.Climbs)
            {
                if (placedDef >= def + 2) break;
                if (cl.High > town.L1 + 0.1f) continue;
                if (Hold(Foot(1, Rng.Rand() < 0.5f ? 0 : 1), cl.Bx, cl.Bz + 3.5f, M.PI, 10, "держать подъём")) placedDef++;
            }
            var cas = town.Castle;
            if (cas != null)
            {
                Hold(0, cas.GateX, cas.Z0 + 7, M.PI, 9, "держать ворота замка");
                Hold(Foot(1, 1), cas.Keep.x + (cas.Keep.x > cas.GateX ? -1 : 1) * (cas.KeepS / 2 + 6), cas.Z0 + 8, M.PI, 12, "оборонять двор замка");
            }
            for (int i = placedDef; i < def; i++) Put(Rng.Rand() < 0.55f ? 0 : 1, new V2((Rng.Rand() - 0.5f) * town.CX * 1.6f, -town.CZ * 0.3f), 60);
            int dx = Pick(xbow);
            // стрелки: один отряд — на краю уступа над посадом, один — на стене замка, остальные — на южной стене
            if (dx > 1)
            {
                float lx = town.Climbs.Count > 1 ? (town.Climbs[0].Ax + town.Climbs[1].Ax) / 2 : 0;
                if (Hold(2, lx, town.EdgeZ(lx) + 3, M.PI, 6, "стрелять с уступа")) dx--;
            }
            if (dx > 1 && cas != null)
            {
                var cw = World.WallSpots.Where(w => w.Out.z < -0.5f && w.P.z > cas.Z0 - 3 && MathF.Abs(w.P.x - cas.GateX) > 9).OrderBy(w => MathF.Abs(w.P.x - cas.GateX)).FirstOrDefault();
                if (cw.Out.z < 0 && Hold(2, cw.P.x, cw.P.z, M.PI, 4, "держать стену замка")) dx--;
            }
            // стрелки гарнизона — на боевом ходу южной стены, у зубцов; кому не хватило места — на улицах у стены
            var wallSpots = World.WallSpots.Where(w => w.Out.z < -0.5f && w.P.z < -town.CZ + 3).Select(w => w.P).OrderBy(p => p.x).ToList();
            var usedWall = new List<V2>();
            for (int i = 0; i < dx; i++)
            {
                var want = new V2((i - (dx - 1) / 2f) * town.CX * 1.6f / Math.Max(1, dx), -town.CZ);
                bool ok = false;
                foreach (var p in wallSpots.OrderBy(q => V2.Dist(q, want)))
                {
                    if (usedWall.Any(q => V2.Dist(q, p) < 9)) continue;
                    int placed = PlaceSquad(Ty(1, 2), 1, p.x, p.z, M.PI);
                    if (placed >= Defs.All[Ty(1, 2)].Cols * Defs.All[Ty(1, 2)].Rows * 0.6f)
                    {
                        usedWall.Add(p);
                        var ws = Squads[Squads.Count - 1];
                        ws.Order = new Order(OrderKind.Fire, Mode.Hold) { Why = "держать стену", Face = M.PI }.At(p);
                        ws.Garrison = true;
                        ok = true;
                        break;
                    }
                    if (placed > 0) RemoveSquadAt(p);
                }
                if (!ok) Put(2, new V2(want.x, -town.CZ + 9), 30);
            }
            int dc = Math.Max(1, Pick(cav) / 2);
            var sq = town.Squares.Count > 0 ? new V2(town.Squares[0].X, town.Squares[0].Z) : new V2(0, 0);
            for (int i = 0; i < dc; i++) Put(3, sq, 40);
        }

        void RemoveSquadAt(V2 p)
        {
            var sq = Squads.LastOrDefault();
            if (sq == null) return;
            var gone = new HashSet<Unit>(sq.Units);
            Units = Units.Where(u => !gone.Contains(u)).ToList();
            Plan = Plan.Where(pl => !sq.Units.Any(u => u.Plan == pl)).ToList();
            Squads.Remove(sq);
            NumberSquads();
            Recount();
        }

        /// <summary>Имя и характер полководца армии (сохраняются между реваншами).</summary>
        public CmdSpec CommanderSpec(int team)
        {
            cmdSpec ??= new CmdSpec[2];
            if (cmdSpec[team] == null)
            {
                var names = Races[team].Names ?? Defs.CmdNames[team];
                cmdSpec[team] = new CmdSpec { Name = names[(int)(Rng.Rand() * names.Length)], Trait = Defs.TraitKeys[(int)(Rng.Rand() * Defs.TraitKeys.Length)] };
            }
            return cmdSpec[team];
        }

        // ---------------------------------------------------------------- начало боя, полководцы, гонцы

        public void StartFight()
        {
            Fighting = true;
            Time = 0; LastHitT = 0; LastKillT = 0; Log = new List<LogEntry>(); GlareLogged = new bool[2];
            foreach (var sq in Squads) { sq.Order = new Order(OrderKind.Advance, Mode.Advance); sq.Morale = 100; }
            Heroes[0] = Heroes[1] = null;
            for (int team = 0; team < 2; team++) SpawnHero(team);
            if (!UseCommanders) { AddLog(-1, "Полководцев нет — каждый отряд бьётся сам по себе"); return; }
            for (int team = 0; team < 2; team++) SpawnCommander(team);
        }

        Unit Leader(int type, float x, float z, string key, int team, float yaw)
        {
            var p = World.ClampField(x, z, 4);
            p = World.WalkableNear(p.x, p.z); // не в реку и не в дом
            var u = new Unit(new UnitPlan { Type = type, Team = team, X = p.x, Z = p.z, Yaw = yaw, Variant = Defs.All[type].Special == Special.Commander ? 1 : 0 }, World);
            Units.Add(u);
            var sq = new Squad(key, type, team, yaw);
            sq.Units.Add(u); u.Squad = sq; sq.Size = 1;
            sq.Order = new Order(OrderKind.Hold, Mode.Hold).At(p.x, p.z);
            Squads.Add(sq);
            return u;
        }

        /// <summary>
        /// Главнокомандующий за центром армии. У большой армии (от 7 отрядов) — три крыла
        /// с воеводами и резерв из самых дальних отрядов при главнокомандующем.
        /// </summary>
        void SpawnCommander(int team)
        {
            var mine = Squads.Where(s => s.Team == team && !s.Special && s.Alive > 0).ToList();
            if (mine.Count == 0) return;
            var spec = CommanderSpec(team);
            float back = team == 0 ? -1 : 1, yaw = team == 0 ? 0 : M.PI;
            var c = Commander.Center(mine).Value;
            var role = mine.Count >= 7 ? Role.General : Role.Solo;
            var gen = new Commander(this, team, Leader(Races[team].Cmd.Id, c.x, c.z + back * 18, "cmd" + team, team, yaw), spec.Trait, spec.Name, role);
            Commanders[team] = gen;
            Couriers[team] = new Squad("msg" + team, Races[team].Msg.Id, team, yaw);
            string army = team == 0 ? "синих" : "красных";
            var tr = Defs.Traits[spec.Trait];
            if (role == Role.Solo)
            {
                AddLog(team, $"{spec.Name}, {tr.Name} полководец, ведёт {army}: {tr.Note}");
                return;
            }
            // Резерв: самые дальние от врага отряды
            var rest = mine.OrderByDescending(a => a.Center.z * back).ToList();
            var reserve = new List<Squad>();
            if (mine.Count >= 12)
            {
                int nr = Math.Max(1, M.Round(mine.Count * 0.15f));
                reserve = rest.Take(nr).ToList();
                rest = rest.Skip(nr).ToList();
                foreach (var sq in reserve)
                {
                    sq.Reserve = true;
                    sq.Order = new Order(OrderKind.Reserve, Mode.Hold) { Leash = 18 }.At(sq.Center.x, sq.Center.z);
                }
            }
            // Три крыла по ширине фронта. Синие смотрят на +z, их правое крыло — со стороны -x.
            rest = rest.OrderBy(a => a.Center.x).ToList();
            int n = rest.Count;
            int[] cuts = { 0, M.Round(n / 3f), M.Round(2 * n / 3f), n };
            var keys = team == 0 ? new[] { WingKey.Right, WingKey.Center, WingKey.Left } : new[] { WingKey.Left, WingKey.Center, WingKey.Right };
            var names = (Races[team].Names ?? Defs.CmdNames[team]).Where(x => x != spec.Name).OrderBy(_ => Rng.Rand()).ToList();
            var desc = new List<string>();
            for (int k = 0; k < 3; k++)
            {
                var list = rest.Skip(cuts[k]).Take(cuts[k + 1] - cuts[k]).ToList();
                if (list.Count == 0) continue;
                var w = new Wing { Key = keys[k], Squads = list };
                foreach (var sq in list) sq.Wing = w;
                var wc = Commander.Center(list).Value;
                var trait = Defs.TraitKeys[(int)(Rng.Rand() * Defs.TraitKeys.Length)];
                var cap = new Commander(this, team, Leader(Races[team].Cap.Id, wc.x, wc.z + back * 11, "cap" + team + keys[k], team, yaw), trait, names[k], Role.Captain, w);
                w.Captain = cap;
                Captains[team].Add(cap);
                Wings[team].Add(w);
                desc.Add($"{Defs.WingName(w.Key)} — {cap.Name} ({Defs.Traits[trait].Name})");
            }
            AddLog(team, $"{spec.Name}, {tr.Name} главнокомандующий, ведёт {army}. Воеводы: {string.Join("; ", desc)}{(reserve.Count > 0 ? $". Резерв — {reserve.Count} отр." : "")}");
        }

        public void SpawnMessenger(Commander cmd, Squad sq, Order order, Carry extra = null)
        {
            var c = cmd.Unit.Pos;
            float side = Rng.Rand() < 0.5f ? -1.5f : 1.5f;
            var at = World.WalkableNear(M.Clamp(c.x + side, -World.Field + 1, World.Field - 1), c.z, 0.5f, c.y); // рядом и на том же уровне
            var plan = new UnitPlan { Type = Races[cmd.Team].Msg.Id, Team = cmd.Team, X = at.x, Z = at.z, Yaw = cmd.Unit.Yaw, Variant = 0 };
            var u = new Unit(plan, World);
            var carry = extra ?? new Carry();
            carry.Squad = sq; carry.Order = order; carry.Cmd = cmd; carry.Delivered = false;
            u.Carry = carry;
            u.Squad = Couriers[cmd.Team];
            Couriers[cmd.Team].Units.Add(u);
            Units.Add(u);
        }

        public void ApplyOrder(Squad sq, Order order)
        {
            if (order.Mode != Mode.Advance && order.Mode != Mode.Rout && order.Mode != Mode.Charge && !order.HasPos) { order.X = sq.Center.x; order.Z = sq.Center.z; }
            if (order.HasPos && !order.HasFace)
            {
                var e = ArmyC[1 - sq.Team];
                if (e.HasValue) order.Face = MathF.Atan2(e.Value.x - order.X, e.Value.z - order.Z);
            }
            sq.Order = order;
            sq.OrderT = Time;
            sq.LabelT = 4;
            sq.APath = null; sq.AnchorArrived = false; sq.Offset = new V2(0, 0);
        }

        void ComputeArmyCenters()
        {
            for (int team = 0; team < 2; team++)
            {
                float x = 0, z = 0;
                int n = 0;
                foreach (var u in Teams[team]) if (u.T.Special == Special.None) { x += u.Pos.x; z += u.Pos.z; n++; }
                ArmyC[team] = n > 0 ? new V2(x / n, z / n) : (V2?)null;
            }
        }

        // ---------------------------------------------------------------- главный цикл

        /// <summary>События для эффектов; Unity-слой забирает и очищает их каждый кадр.</summary>
        public readonly List<FxEvent> Fx = new List<FxEvent>();
        public void Emit(FxKind k, V3 p, float dx = 0, float dz = 0, int team = 0)
        {
            if (Fx.Count < 600) Fx.Add(new FxEvent { Kind = k, Pos = p, Dir = new V3(dx, 0, dz), Team = team });
        }

        /// <summary>Сколько миллисекунд ушло на каждую часть шага (накопительно) — для замеров.</summary>
        public readonly double[] Prof = new double[8];
        public static readonly string[] ProfNames = { "подготовка", "враги", "приказы", "строй", "решения", "движение", "расталкивание", "болты" };
        long profT;
        void Lap(int k) { long now = System.Diagnostics.Stopwatch.GetTimestamp(); Prof[k] += (now - profT) * 1000.0 / System.Diagnostics.Stopwatch.Frequency; profT = now; }

        int tickNo;

        public void Tick(float dt)
        {
            profT = System.Diagnostics.Stopwatch.GetTimestamp();
            tickNo++;
            foreach (var u in Units) { u.PrevPos = u.Pos; u.PrevYaw = u.Yaw; }
            Time += dt;
            PathBudget = 4;
            PathWork = 6000;
            Teams[0].Clear(); Teams[1].Clear();
            int a0 = 0, a1 = 0;
            foreach (var u in Units)
                if (u.Alive)
                {
                    Teams[u.Team].Add(u);
                    if (u.T.Special == Special.None) { if (u.Team == 0) a0++; else a1++; }
                }
            Alive = new[] { a0, a1 };
            ComputeArmyCenters();
            BuildGrid();
            foreach (var sq in Squads)
            {
                sq.Refresh(dt);
                if (sq.Engaged || Time - sq.LastShotT < 1.5f) sq.LastActiveT = Time;
            }
            foreach (var cs in Couriers) cs?.Refresh(dt);

            Lap(0);
            if (Fighting && dt > 0)
            {
                if ((foesT -= dt) <= 0) { foesT = 0.8f; UpdateFoes(); World.Nav.DecayCrowd(0.93f); }
                Lap(1);
                UpdateSquads(dt);
                foreach (var c in Commanders) c?.Tick(dt);
                foreach (var list in Captains) foreach (var c in list) c.Tick(dt);
                Lap(2);
                UpdateFormations(dt);
                Lap(3);
                // решения — через шаг (половина армии на чётных шагах, половина на нечётных), движение — каждый шаг
                for (int i = 0; i < Units.Count; i++) if (Units[i].Alive && ((i + tickNo) & 1) == 0) Think(Units[i], dt * 2);
                Lap(4);
                for (int i = 0; i < Units.Count; i++) if (Units[i].Alive) Integrate(Units[i], dt);
                Lap(5);
                ResolveContacts();
                Lap(6);
            }
            Bolts.Tick(dt);
            Lap(7);

            for (int i = Units.Count - 1; i >= 0; i--)
            {
                var u = Units[i];
                if (u.Gone) { u.Alive = false; Units.RemoveAt(i); continue; }
                if (!u.Alive)
                {
                    if (u.Flying && dt > 0)
                    {
                        u.Fly.y -= 20 * dt;
                        float fx = M.Clamp(u.Pos.x + u.Fly.x * dt, -World.Field + 1, World.Field - 1), fz = M.Clamp(u.Pos.z + u.Fly.z * dt, -World.Field + 1, World.Field - 1);
                        // в дом, ствол, кладку стены или склон выше себя тело не влетает — падает у преграды
                        float fg = World.HeightAt(fx, fz);
                        if (World.Obs.Hit(fx, fz, 0.2f) == null && fg < u.Pos.y + 0.3f && World.Decks.SolidTop(fx, fz, fg, false) < u.Pos.y + 0.1f) { u.Pos.x = fx; u.Pos.z = fz; }
                        else { u.Fly.x = 0; u.Fly.z = 0; }
                        u.Pos.y += u.Fly.y * dt;
                        // приземляется на то, что под ним, а не на настил над головой
                        float g = World.GroundBelow(u.Pos.x, u.Pos.z, u.Pos.y - u.Fly.y * dt + 0.3f);
                        if (u.Pos.y <= g)
                        {
                            u.Pos.y = g; u.Flying = false;
                            // тело, сброшенное с моста или берега, уходит под воду с плеском
                            if (g < World.Water - 0.3f) Emit(FxKind.Splash, new V3(u.Pos.x, World.Water, u.Pos.z));
                            else Emit(FxKind.Kill, u.Pos, 0, 0, u.Team);
                        }
                    }
                    u.DeadT += dt;
                    if (u.DeadT > 24) Units.RemoveAt(i);
                }
            }
        }

        static float D2d(V3 a, V3 b) => M.Hypot(a.x - b.x, a.z - b.z);
        static float D2d(V3 a, V2 b) => M.Hypot(a.x - b.x, a.z - b.z);

        /// <summary>Боевой дух, скрытность в низинах и смена фаз приказа.</summary>
        void UpdateRage(float dt)
        {
            for (int team = 0; team < 2; team++)
            {
                if (!Races[team].Rage || Time < RoarUntil[team]) continue;
                int fighting = 0;
                foreach (var q in Squads) if (q.Team == team && !q.Special && q.Alive > 0 && q.Engaged) fighting++;
                Rage[team] = M.Clamp(Rage[team] + (fighting > 0 ? 0.3f * fighting : -1f) * dt, 0, 100);
                if (Rage[team] < 100) continue;
                Rage[team] = 0; RoarUntil[team] = Time + 10;
                var cmd = Commanders[team];
                var at = cmd != null && cmd.Unit.Alive ? cmd.Unit.Pos : new V3(ArmyC[team]?.x ?? 0, 0, ArmyC[team]?.z ?? 0);
                Emit(FxKind.Roar, at, 0, 0, team);
                AddLog(team, $"{Races[team].Cry} {Races[team].Name} ревёт — быстрее, злее, не бежит!");
                foreach (var q in Squads)
                    if (q.Team == team && !q.Special && q.Alive > 0 && q.Order.Mode != Mode.Rout)
                    { q.Morale = 100; q.CryUntil = Time + 0.6f; q.RushUntil = Time + 10; }
            }
        }

        void UpdateSquads(float dt)
        {
            UpdateRage(dt);
            UpdateRaces(dt);
            foreach (var sq in Squads.ToList())
            {
                if (sq.Special || sq.Alive == 0) continue;
                var o = sq.Order;
                var cmd = Commanders[sq.Team];
                var wcap = sq.Wing?.Captain;
                bool cmdNear = (cmd != null && cmd.Unit.Alive && D2d(cmd.Unit.Pos, sq.Center) < 26) || (wcap != null && wcap.Unit.Alive && D2d(wcap.Unit.Pos, sq.Center) < 22);
                if (!sq.Engaged && Time - sq.LastHitT > 3) sq.Morale += dt * (cmdNear ? 4 : 1.2f);
                if (sq.Alive < sq.Size * 0.3f) sq.Morale = MathF.Min(sq.Morale, 55);
                sq.Morale = M.Clamp(sq.Morale, 0, 100);

                bool enemyNear = sq.Foes.Count > 0 && sq.Foes[0].Alive > 0 && D2d(sq.Foes[0].Center, sq.Center) < 20;

                bool roaring = Time < RoarUntil[sq.Team];
                if (roaring) sq.Morale = MathF.Max(sq.Morale, 60);
                if ((o.Mode != Mode.Rout || sq.Feigning) && sq.Morale < sq.T.RoutAt && !sq.T.Fearless && !roaring)
                {
                    sq.Feigning = false;
                    ApplyOrder(sq, new Order(OrderKind.Rout, Mode.Rout));
                    sq.Pending = null;
                    AddLog(sq.Team, $"{Defs.Cap(sq.Name)} дрогнули и бегут!");
                    Emit(FxKind.Rout, sq.Center, 0, 0, sq.Team);
                    SpreadPanic(sq);
                    continue;
                }
                // Боевой клич перед сшибкой: отряд замирает, кричит — и срывается в разбег
                if (!sq.T.Ranged && !sq.T.Mount && (o.Mode == Mode.Advance || o.Mode == Mode.Charge) && !sq.Engaged && sq.Choke == null
                    && sq.FoeDist > 11 && sq.FoeDist < 18 && Time - sq.CryT > 30 && sq.Alive >= sq.Size * 0.5f
                    && sq.Foes.Count > 0 && World.Nav.LineClear(sq.Center.x, sq.Center.z, sq.Foes[0].Center.x, sq.Foes[0].Center.z, 0))
                {
                    sq.CryT = Time; sq.CryUntil = Time + 0.7f; sq.RushUntil = Time + 4f;
                    Emit(FxKind.Cry, sq.Center, 0, 0, sq.Team);
                    foreach (var e in Squads)
                        if (e.Team != sq.Team && !e.Special && e.Alive > 0 && D2d(e.Center, sq.Center) < 16) e.Morale -= 4;
                }
                // Бегущих рядом — добивать: конница бросается в погоню
                if (sq.T.Mount && !sq.T.Ranged && !sq.Engaged && o.Mode != Mode.Charge && o.Mode != Mode.Rout && sq.Morale > sq.T.RoutAt + 20)
                {
                    Squad prey = null; float best = 45;
                    foreach (var e in Squads)
                        if (e.Team != sq.Team && !e.Special && e.Alive > 0 && e.Order.Mode == Mode.Rout && D2d(e.Center, sq.Center) < best) { best = D2d(e.Center, sq.Center); prey = e; }
                    if (prey != null)
                    {
                        ApplyOrder(sq, new Order(OrderKind.Charge, Mode.Charge) { Target = prey, Why = "добить бегущих" });
                        AddLog(sq.Team, $"{Defs.Cap(sq.Name)} гонятся за бегущими ({prey.Name})");
                    }
                }
                if (o.Mode == Mode.Rout)
                {
                    if (sq.Feigning) { if (Time > sq.FeignUntil) EndFeign(sq); continue; }
                    if (sq.Morale > 45)
                    {
                        V2 p = cmd != null && cmd.Unit.Alive ? cmd.Unit.P : new V2(sq.Center.x, (sq.Team == 0 ? -1 : 1) * (World.Field - 12));
                        ApplyOrder(sq, new Order(OrderKind.Rally, Mode.Move) { Then = new Order(OrderKind.Hold, Mode.Hold) }.At(p));
                        AddLog(sq.Team, $"{Defs.Cap(sq.Name)} опомнились и собираются у знамени");
                    }
                    continue;
                }

                sq.Hidden = World.ConcealedAt(sq.Center.x, sq.Center.z) && !sq.Engaged && Time - sq.LastShotT > 3 && !enemyNear;

                if (o.Mode == Mode.Move && (D2d(sq.Center, o.Pos) < 4 || (sq.AnchorArrived && sq.Lag < 2.5f && Time - sq.OrderT > 2) || Time - sq.OrderT > 35))
                {
                    var nx = o.Then != null ? o.Then.Clone() : new Order(OrderKind.Advance, Mode.Advance);
                    if (nx.Mode != Mode.Advance && nx.Mode != Mode.Charge && !nx.HasPos) { nx.X = o.X; nx.Z = o.Z; nx.Face = o.Face; }
                    ApplyOrder(sq, nx);
                }
                else if (o.Mode == Mode.Charge && (o.Target == null || o.Target.Alive == 0))
                {
                    ApplyOrder(sq, new Order(OrderKind.Advance, Mode.Advance));
                }
                else if (o.Mode == Mode.Ambush && enemyNear)
                {
                    sq.FirstStrikeT = Time;
                    ApplyOrder(sq, new Order(OrderKind.Charge, Mode.Advance));
                    AddLog(sq.Team, $"Засада! {Defs.Cap(sq.Name)} бьют из укрытия");
                }
            }
        }

        /// <summary>Отряд побежал — страх волной расходится по соседям (воевода рядом вдвое смягчает).</summary>
        void SpreadPanic(Squad src)
        {
            foreach (var q in Squads)
            {
                if (q == src || q.Team != src.Team || q.Special || q.Alive == 0 || q.Order.Mode == Mode.Rout) continue;
                float d = D2d(q.Center, src.Center);
                if (d > 20 || Time - q.PanicT < 5) continue;
                float hit = 8 + 0.4f * src.Size * (1 - d / 20);
                var cap = q.Wing?.Captain;
                if (cap != null && cap.Unit.Alive && D2d(cap.Unit.Pos, q.Center) < 22) hit *= 0.5f;
                q.Morale -= hit; q.PanicT = Time;
            }
        }

        void Think(Unit u, float dt)
        {
            var t = u.T;
            var sq = u.Squad;
            var o = sq.Order;
            u.Cooldown -= dt; u.RetargetT -= dt; u.HitAnimT -= dt;
            u.Engaged = false; u.Aiming = false;
            if (t.Special == Special.Messenger) { ThinkMessenger(u, dt); return; }
            if (u.DownT > 0)
            { // сбит с ног — лежит и встаёт
                u.DownT -= dt; u.AtkT = -1; u.Settled = false;
                Steer(u, 0, 0, dt, false);
                return;
            }
            if (o.Mode == Mode.Rout) { Flee(u, dt); return; }

#if PROF
            SubLap(-1);
#endif
            if (u.Target == null || !u.Target.Alive || u.RetargetT <= 0)
            { // враг далеко — цель можно пересматривать реже
                u.RetargetT = sq.FoeDist > 60 ? 1.2f + Rng.Rand() * 0.6f : 0.45f + Rng.Rand() * 0.45f;
                u.Target = PickTarget(u);
            }
            if (t.Hero) { var ht = HeroTarget(u); if (ht != null) u.Target = ht; } // богатырь ищет поединка
#if PROF
            SubLap(0);
#endif
            var tg = u.Target;
            float dx = 0, dz = 0;
            bool advance = o.Mode == Mode.Advance || o.Mode == Mode.Charge;
            bool wantSlot = true, chasing = false;
            // Кто рубится: первая шеренга — с 7 м, задние — только если враг вплотную
            // (а когда свалка затянулась — с 5 м); конница врезается сама
            float engageR = float.MaxValue;
            if (advance && !t.Ranged && !u.IsLeader)
                engageR = t.Mount ? 14 : u.Row <= 0 ? 7 : u.Row == 1 ? 4.5f : sq.EngagedFor > 4 ? 5.5f : 3f;
            if (sq.Front != null && !u.IsLeader)
                engageR = u.Row <= 0 ? t.Radius * 2 + t.Reach + 0.8f : 0; // строй держит линию: только выпад первой шеренги
            if (t.Hero && tg != null && tg.T.Hero) engageR = 30; // к вражескому богатырю — через всё поле
            if (t.Skirmish && Skirmish(u, tg, dt, out dx, out dz)) { wantSlot = false; tg = null; }
            if (tg != null)
            {
                float tx = tg.Pos.x - u.Pos.x, tz = tg.Pos.z - u.Pos.z, d = M.Hypot(tx, tz);
                float nx = d > 1e-4f ? tx / d : MathF.Sin(u.Yaw), nz = d > 1e-4f ? tz / d : MathF.Cos(u.Yaw);
                float contact = t.Radius + tg.T.Radius + t.Reach;
                if (t.Ranged && d > contact + 1.5f)
                {
                    if (d <= RangeOf(u, tg) && CanSee(u, tg))
                    {
                        u.Aiming = true; wantSlot = false;
                        u.Face(nx, nz, dt);
                        if (u.Cooldown <= 0 && u.AtkT < 0) StartAttack(u, true);
                    }
                }
                else if (d <= contact && MathF.Abs(tg.Pos.y - u.Pos.y) < 1.8f)
                {
                    u.Engaged = true; wantSlot = false;
                    u.Face(nx, nz, dt);
                    if (u.Cooldown <= 0 && u.AtkT < 0 && t.Dmg > 0) StartAttack(u, false);
                }
                // стрелок бросается врукопашную, только если враг рядом и на одном с ним уровне —
                // не бежит со стены к тем, кто стоит под ней
                else if (d <= engageR && MayChase(u, tg) && (!t.Ranged || (d <= contact + 3 && MathF.Abs(tg.Pos.y - u.Pos.y) < 1.8f)))
                {
                    wantSlot = false; chasing = true;
                    if (d > 6)
                    {
                        var w = NavTarget(u, tg.Pos.x, tg.Pos.z);
                        float ll = M.Hypot(w.x - u.Pos.x, w.z - u.Pos.z);
                        if (ll == 0) ll = 1;
                        dx = (w.x - u.Pos.x) / ll * t.Speed; dz = (w.z - u.Pos.z) / ll * t.Speed;
                    }
                    else { dx = nx * t.Speed; dz = nz * t.Speed; }
                    YieldAhead(u, ref dx, ref dz);
                }
            }
#if PROF
            SubLap(1);
#endif
            if (!wantSlot) u.Settled = false;
            if (wantSlot)
            {
                if (u.IsLeader)
                {
                    V2 g = u.Cmd != null ? u.Cmd.Post : u.P;
                    float sd0 = M.Hypot(g.x - u.Pos.x, g.z - u.Pos.z);
                    if (sd0 > 4) g = NavTarget(u, g.x, g.z);
                    float sx = g.x - u.Pos.x, sz = g.z - u.Pos.z, sd = M.Hypot(sx, sz);
                    if (sd == 0) sd = 1;
                    if (sd0 > 0.7f) { dx = sx / sd * t.Speed * (sd0 > 5 ? 0.6f : 0.33f); dz = sz / sd * t.Speed * (sd0 > 5 ? 0.6f : 0.33f); }
                }
                else
                {
                    MoveToSlot(u, dt, out dx, out dz);
                    // стоим на месте — смотрим туда же, куда строй
                    if (dx * dx + dz * dz < 0.04f && !u.Aiming) u.Face(MathF.Sin(sq.Facing), MathF.Cos(sq.Facing), dt, 3);
                }
            }
#if PROF
            SubLap(2);
#endif
            if (u.AtkT >= 0 && !t.Ranged && t.Charge <= 1) { dx *= 0.3f; dz *= 0.3f; }
            Steer(u, dx, dz, dt, !u.Engaged && !u.Aiming && dx * dx + dz * dz > 0.04f);
            // Бежим к врагу, а с места не сдвинулись: цель недостижима (обрыв, стена, река) — выбираем другую
            if (chasing)
            {
                if (u.CurSpeed < 0.25f) u.StuckT += dt; else u.StuckT = MathF.Max(0, u.StuckT - dt * 2);
                if (u.StuckT > 2.5f && tg != null)
                {
                    u.IgnoreSquad = tg.Squad; u.IgnoreT = Time + 8;
                    u.Target = null; u.RetargetT = 0; u.StuckT = 0;
                    u.Path = null; u.NavT = 0;
                }
            }
            else u.StuckT = 0;

            if (t.Charge > 1)
            {
                if (u.CurSpeed > t.Speed * 0.7f) u.ChargeT += dt;
                else if (!u.Engaged) u.ChargeT = MathF.Max(0, u.ChargeT - dt * 2);
            }
            if (u.AtkT >= 0)
            {
                u.AtkT += dt / u.AtkDur;
                if (!u.HitDone && u.AtkT >= (u.Shot ? 0.55f : 0.5f)) { u.HitDone = true; ResolveHit(u); }
                if (u.AtkT >= 1) u.AtkT = -1;
            }
#if PROF
            SubLap(3);
#endif
        }
#if PROF
        public static readonly double[] SubProf = new double[4];
        static long subT;
        static void SubLap(int k) { long now = System.Diagnostics.Stopwatch.GetTimestamp(); if (k >= 0) SubProf[k] += (now - subT) * 1000.0 / System.Diagnostics.Stopwatch.Frequency; subT = now; }
#endif

        /// <summary>
        /// Куда идти сейчас, чтобы добраться до (gx, gz): прямо, если путь свободен,
        /// иначе к следующей точке пути A*. Путь считается один раз на отряд.
        /// </summary>
        V2 NavTarget(Unit u, float gx, float gz)
        {
            var nav = World.Nav;
            if (nav.Barriers == 0) return new V2(gx, gz);
            int cls = u.T.Mount ? 1 : 0;
            if (Time > u.NavT || float.IsNaN(u.NavGX) || M.Hypot(gx - u.NavGX, gz - u.NavGZ) > 5)
            {
                u.NavT = Time + 0.7f + Rng.Rand() * 0.4f;
                u.NavGX = gx; u.NavGZ = gz;
                if (nav.LineClear(u.Pos.x, u.Pos.z, gx, gz, cls, NavGrid.Gentle)) u.Path = null;
                else
                {
                    var sq = u.Squad;
                    var pc = sq.PathCache;
                    List<V2> path;
                    if (pc != null && pc.Cls == cls && M.Hypot(pc.Gx - gx, pc.Gz - gz) < 6 && Time - pc.T < 3 && D2d(sq.Center, u.Pos) < 12
                        && nav.LineClear(u.Pos.x, u.Pos.z, sq.Center.x, sq.Center.z, cls)) path = pc.Path;
                    else if (PathBudget <= 0 || PathWork <= 0)
                    {
                        u.NavT = Time + 0.2f;
                        return u.Path != null ? u.Path[Math.Min(u.PathI, u.Path.Count - 1)] : new V2(gx, gz);
                    }
                    else
                    {
                        // не больше нескольких поисков пути за шаг — иначе рывки при сотнях отрядов
                        PathBudget--;
                        // общий путь от середины строя — только если до неё прямая свободна: не с другого витка серпантина
                        bool fromCenter = D2d(sq.Center, u.Pos) < 12 && nav.SpeedAt(sq.Center.x, sq.Center.z, cls) > 0
                            && nav.LineClear(u.Pos.x, u.Pos.z, sq.Center.x, sq.Center.z, cls);
                        float fx = fromCenter ? sq.Center.x : u.Pos.x, fz = fromCenter ? sq.Center.z : u.Pos.z;
                        path = nav.FindPath(fx, fz, gx, gz, cls, 0, 0, 0, 0, 8000, 1.35f, fromCenter ? sq.Center.y : u.Pos.y);
                        PathWork -= nav.LastExpanded;
                        if (fromCenter) sq.PathCache = new PathCache { Cls = cls, Gx = gx, Gz = gz, T = Time, Path = path };
                    }
                    u.Path = path;
                    u.PathI = 1;
                    if (path != null)
                    { // начинаем с ближайшей точки пути, до которой прямая свободна: ближняя может быть над обрывом, на следующем витке.
                      // Начало пути — тоже кандидат: поиск начинается с клетки на уровне бойца, а она бывает соседней
                        float best = float.PositiveInfinity;
                        int pick = -1;
                        for (int i = 0; i < path.Count; i++)
                        {
                            float d = M.Hypot(path[i].x - u.Pos.x, path[i].z - u.Pos.z);
                            if (i == 0 && d < 1f) continue;
                            if (d < best && nav.LineClear(u.Pos.x, u.Pos.z, path[i].x, path[i].z, cls)) { best = d; pick = i; }
                        }
                        u.PathI = pick >= 0 ? pick : M.Hypot(path[0].x - u.Pos.x, path[0].z - u.Pos.z) >= 1f ? 0 : 1;
                    }
                }
            }
            var P = u.Path;
            if (P == null) return new V2(gx, gz);
            // к следующей вершине — если к ней прямая свободна, иначе дойти до этой (не срезать угол обрыва, ограды, моста)
            while (u.PathI < P.Count - 1 && M.Hypot(P[u.PathI].x - u.Pos.x, P[u.PathI].z - u.Pos.z) < 2.5f
                && (M.Hypot(P[u.PathI].x - u.Pos.x, P[u.PathI].z - u.Pos.z) < 0.5f || nav.LineClear(u.Pos.x, u.Pos.z, P[u.PathI + 1].x, P[u.PathI + 1].z, cls))) u.PathI++;
            if (u.PathI >= P.Count - 1 && M.Hypot(P[P.Count - 1].x - u.Pos.x, P[P.Count - 1].z - u.Pos.z) < 3) return new V2(gx, gz);
            return P[Math.Min(u.PathI, P.Count - 1)];
        }

        void Steer(Unit u, float dx, float dz, float dt, bool faceMove)
        {
            float ax = dx - u.Vel.x, az = dz - u.Vel.z, al = M.Hypot(ax, az), step = u.T.Accel * dt;
            if (al <= step) { u.Vel.x = dx; u.Vel.z = dz; }
            else { u.Vel.x += ax / al * step; u.Vel.z += az / al * step; }
            if (faceMove && M.Hypot(u.Vel.x, u.Vel.z) > 0.2f) u.Face(u.Vel.x, u.Vel.z, dt, u.T.Mount ? 3 : 9.5f);
        }

        bool MayChase(Unit u, Unit tg)
        {
            var o = u.Squad.Order;
            if (u.IsLeader) return D2d(u.Pos, tg.Pos) < 7;
            if (u.Duel == tg) return true;
            if (o.Mode == Mode.Advance || o.Mode == Mode.Charge) return true;
            if (o.Mode == Mode.Hold || o.Mode == Mode.Ambush) return D2d(tg.Pos, o.Pos) < (o.HasLeash ? o.Leash : u.T.Mount ? 16 : 10);
            return false;
        }

        // Кого можно выбрать целью: зависит от приказа, скрытности и роли
        bool Allowed(Unit u, Unit e, float d2, Order o, bool ranged, float gx, float gz, float leash)
        {
            if (e.Duel != null && e.Duel != u) return false; // в поединок не лезут
            if (e.Squad != null && e.Squad.Hidden && d2 > 196 && u.Pos.y - e.Pos.y < 4) return false; // в низине не видно (сверху — видно)
            if (e.T.Special == Special.Messenger && !ranged && d2 > 64) return false;
            if (u.IsLeader && d2 > 64) return false;
            if (e.Squad != null && e.Squad == u.IgnoreSquad && Time < u.IgnoreT) return false;
            // враг на мосту над головой или на стене — рукой не достать, ищем другого
            if (!ranged && MathF.Abs(e.Pos.y - u.Pos.y) > 1.8f + 0.5f * MathF.Sqrt(d2)) return false;
            // стрелку отвесно вниз из-за кладки не выстрелить (мёртвая зона под стеной)
            if (ranged && MathF.Abs(e.Pos.y - u.Pos.y) > 1.8f && MathF.Abs(e.Pos.y - u.Pos.y) > MathF.Sqrt(d2) * 0.9f) return false;
            if (o.Mode == Mode.Move && d2 > 10 && !ranged) return false; // на марше не отвлекаться (стрелки стреляют и на ходу)
            if ((o.Mode == Mode.Hold || o.Mode == Mode.Ambush) && !ranged)
            {
                float lx = e.Pos.x - gx, lz = e.Pos.z - gz;
                if (lx * lx + lz * lz > leash * leash && d2 > 9) return false;
            }
            return true;
        }

        readonly Unit[] bestE = new Unit[13];
        readonly float[] bestS = new float[13];

        /// <summary>
        /// Цель с учётом приказа, скрытности и прямой видимости. Ищем не по всей армии,
        /// а в ближайших клетках сетки и среди вражеских отрядов, известных своему отряду.
        /// </summary>
        Unit PickTarget(Unit u)
        {
            var o = u.Squad.Order;
            bool ranged = u.T.Ranged;
            float gx = o.HasPos ? o.X : u.Pos.x, gz = o.HasPos ? o.Z : u.Pos.z, leash = o.HasLeash ? o.Leash : u.T.Mount ? 16 : 10;
            // соседей по сетке перебираем, только если вражеский отряд где-то рядом
            var near = u.Squad.FoeDist < (ranged ? 4 : 10) + 30 ? GridNearest(u, ranged ? 4 : 10, true, o, gx, gz, leash) : null;
            if (near != null) return near;
            if (u.T.Special != Special.None) return null;

            var foes = u.Squad.Foes;
            bool chargeT = o.Mode == Mode.Charge && o.Target != null && o.Target.Alive > 0;
            int nc = Math.Min(foes.Count + (chargeT ? 1 : 0), ranged ? 8 : 4), nb = 0, cap = ranged ? 12 : 5;
            for (int qi = 0; qi < nc; qi++)
            {
                var q = chargeT ? (qi == 0 ? o.Target : foes[qi - 1]) : foes[qi];
                foreach (var e in q.Units)
                {
                    if (!e.Alive) continue;
                    float dx = e.Pos.x - u.Pos.x, dz = e.Pos.z - u.Pos.z, d2 = dx * dx + dz * dz;
                    if (!Allowed(u, e, d2, o, ranged, gx, gz, leash)) continue;
                    float sc = o.Mode == Mode.Charge && o.Target == q ? d2 * 0.2f : d2;
                    if (nb < cap || sc < bestS[nb - 1])
                    {
                        int i = nb < cap ? nb++ : cap - 1;
                        while (i > 0 && bestS[i - 1] > sc) { bestS[i] = bestS[i - 1]; bestE[i] = bestE[i - 1]; i--; }
                        bestS[i] = sc; bestE[i] = e;
                    }
                }
            }
            if (nb == 0) return null;
            if (!ranged) return bestE[0];
            for (int i = 0; i < nb; i++) if (D2d(bestE[i].Pos, u.Pos) <= RangeOf(u, bestE[i]) && CanSee(u, bestE[i])) return bestE[i];
            return o.Mode == Mode.Advance || o.Mode == Mode.Charge ? bestE[0] : null;
        }

        /// <summary>Ближайший враг в радиусе R по сетке соседей.</summary>
        Unit GridNearest(Unit u, float R, bool filter, Order o = null, float gx = 0, float gz = 0, float leash = 0)
        {
            CellOf(u.Pos.x, u.Pos.z, out int cx, out int cz);
            int r = (int)MathF.Ceiling(R / GRID);
            Unit best = null;
            float bs = R * R;
            bool ranged = u.T.Ranged;
            for (int z = Math.Max(cz - r, 0); z <= Math.Min(cz + r, gridDim - 1); z++)
                for (int x = Math.Max(cx - r, 0); x <= Math.Min(cx + r, gridDim - 1); x++)
                    for (int j = head[z * gridDim + x]; j >= 0; j = next[j])
                    {
                        var e = Units[j];
                        if (!e.Alive || e.Team == u.Team) continue;
                        float dx = e.Pos.x - u.Pos.x, dz = e.Pos.z - u.Pos.z, d2 = dx * dx + dz * dz;
                        if (d2 >= bs || (filter && !Allowed(u, e, d2, o, ranged, gx, gz, leash))) continue;
                        best = e; bs = d2;
                    }
            return best;
        }

        /// <summary>Раз в 0,8 с каждому отряду — список ближайших видимых вражеских отрядов.</summary>
        void UpdateFoes()
        {
            var S = Squads.Where(q => !q.Special && q.Alive > 0).ToList();
            var tmp = new List<(Squad e, float d)>();
            foreach (var sq in S)
            {
                tmp.Clear();
                foreach (var e in S)
                {
                    if (e.Team == sq.Team) continue;
                    float d = D2d(e.Center, sq.Center);
                    if (!e.Hidden || d < 16) tmp.Add((e, d));
                }
                tmp.Sort((a, b) => a.d.CompareTo(b.d));
                sq.Foes = tmp.Take(4).Select(x => x.e).ToList();
                sq.FoeDist = tmp.Count > 0 ? tmp[0].d : float.PositiveInfinity;
            }
        }

        bool CanSee(Unit u, Unit tg)
        {
            if (u.LosTarget == tg && Time - u.LosT < 0.5f) return u.LosOk;
            u.LosTarget = tg; u.LosT = Time;
            return u.LosOk = World.Los(u.Pos.x, u.Pos.y + 1.5f, u.Pos.z, tg.Pos.x, tg.Pos.y + 1.0f, tg.Pos.z, 2.5f);
        }

        /// <summary>Дальность выстрела: высота добавляет, ветер помогает или мешает, солнце слепит.</summary>
        float RangeOf(Unit u, Unit tg)
        {
            float r = u.T.Range;
            float dx = tg.Pos.x - u.Pos.x, dz = tg.Pos.z - u.Pos.z, d = M.Hypot(dx, dz);
            if (d == 0) d = 1;
            r += M.Clamp((u.Pos.y - tg.Pos.y) * 1.2f, -6, 12);
            r += (World.Wind.x * dx + World.Wind.z * dz) / d * 1.2f;
            if (Glare(dx / d, dz / d)) r *= 0.85f;
            return r;
        }

        public bool Glare(float nx, float nz)
        {
            if (World.Style == null || World.Style.Elev > 26) return false;
            float sx = World.SunDir.x, sz = World.SunDir.z, l = M.Hypot(sx, sz);
            if (l == 0) l = 1;
            return (nx * sx + nz * sz) / l > 0.77f;
        }

        void Flee(Unit u, float dt)
        {
            if (u.RetargetT <= 0 || u.FleeFrom == null || !u.FleeFrom.Alive)
            {
                u.RetargetT = 0.5f;
                u.FleeFrom = GridNearest(u, 16, false);
            }
            var ex = NavTarget(u, u.Pos.x, (u.Team == 0 ? -1 : 1) * (World.Field - 4));
            float fx = ex.x - u.Pos.x, fz = ex.z - u.Pos.z, fl = M.Hypot(fx, fz);
            if (fl == 0) fl = 1;
            fx /= fl; fz /= fl;
            if (u.FleeFrom != null)
            {
                float ax = u.Pos.x - u.FleeFrom.Pos.x, az = u.Pos.z - u.FleeFrom.Pos.z, l0 = M.Hypot(ax, az);
                if (l0 == 0) l0 = 1;
                fx += ax / l0 * 1.5f; fz += az / l0 * 1.5f;
            }
            float l = M.Hypot(fx, fz);
            if (l == 0) l = 1;
            Steer(u, fx / l * u.T.Speed * 1.05f, fz / l * u.T.Speed * 1.05f, dt, true);
            if (u.AtkT >= 0) { u.AtkT += dt / u.AtkDur; if (u.AtkT >= 1) u.AtkT = -1; }
        }

        void ThinkMessenger(Unit u, float dt)
        {
            var c = u.Carry;
            float gx = u.Pos.x, gz = u.Pos.z;
            if (!c.Delivered && c.Captain != null)
            {
                var cap = c.Captain;
                if (!cap.Unit.Alive) { c.Delivered = true; if (c.Wing.PendingMission == c.Mission) c.Wing.PendingMission = null; }
                else
                {
                    gx = cap.Unit.Pos.x; gz = cap.Unit.Pos.z;
                    if (D2d(cap.Unit.Pos, u.Pos) < 6) { c.Delivered = true; if (c.Wing.PendingMission == c.Mission) cap.Receive(c.Mission); }
                }
            }
            else if (!c.Delivered)
            {
                var sq = c.Squad;
                if (sq == null || sq.Alive == 0 || sq.Order.Mode == Mode.Rout) { c.Delivered = true; if (sq != null && sq.Pending == c.Order) sq.Pending = null; }
                else
                {
                    gx = sq.Center.x; gz = sq.Center.z;
                    if (D2d(sq.Center, u.Pos) < 6)
                    {
                        c.Delivered = true;
                        if (sq.Pending == c.Order) { sq.Pending = null; ApplyOrder(sq, c.Order); }
                    }
                }
            }
            if (c.Delivered)
            {
                var cu = c.Cmd.Unit;
                if (cu.Alive) { gx = cu.Pos.x; gz = cu.Pos.z; if (D2d(cu.Pos, u.Pos) < 5) u.Gone = true; }
                else { gz = (u.Team == 0 ? -1 : 1) * World.Field; if (MathF.Abs(u.Pos.z) > World.Field - 3) u.Gone = true; }
            }
            // Гонец объезжает врагов стороной
            if (u.RetargetT <= 0)
            {
                u.RetargetT = 0.3f;
                u.FleeFrom = GridNearest(u, 12, false);
            }
            var w = NavTarget(u, gx, gz);
            float vx = w.x - u.Pos.x, vz = w.z - u.Pos.z, l = M.Hypot(vx, vz);
            if (l == 0) l = 1;
            vx /= l; vz /= l;
            if (u.FleeFrom != null && u.FleeFrom.Alive)
            {
                float ax = u.Pos.x - u.FleeFrom.Pos.x, az = u.Pos.z - u.FleeFrom.Pos.z, al = M.Hypot(ax, az);
                if (al == 0) al = 1;
                float k = M.Clamp((12 - al) / 12, 0, 1) * 1.6f;
                vx += ax / al * k; vz += az / al * k;
            }
            float vl = M.Hypot(vx, vz);
            if (vl == 0) vl = 1;
            Steer(u, vx / vl * u.T.Speed, vz / vl * u.T.Speed, dt, true);
        }

        void StartAttack(Unit u, bool shot)
        {
            u.AtkT = 0; u.HitDone = false; u.Shot = shot; u.AtkNew = true;
            u.SpinNext = !shot && u.T.SpinEvery > 0 && (u.AtkCount + 1) % u.T.SpinEvery == 0;
            u.AtkDur = shot || !u.T.Ranged ? u.T.AtkTime : 0.55f;
            float cd = shot || !u.T.Ranged ? u.T.Cd : 1.0f;
            u.Cooldown = cd * (0.85f + Rng.Rand() * 0.3f);
            if (shot) u.Squad.LastShotT = Time;
        }

        void ResolveHit(Unit u)
        {
            var tg = u.Target;
            if (tg == null || !tg.Alive) return;
            if (u.Shot)
            {
                if (u.T.Magic)
                { // порча: бьёт сразу, щит не спасает
                    Emit(FxKind.Curse, new V3(tg.Pos.x, tg.Pos.y + 1.2f, tg.Pos.z), 0, 0, u.Team);
                    Damage(tg, u.T.Dmg, 0, 0, false, u);
                    if (tg.Squad != null && !tg.T.Fearless) tg.Squad.Morale -= 0.4f;
                    return;
                }
                Bolts.Fire(u, tg); return;
            }
            float tx = tg.Pos.x - u.Pos.x, tz = tg.Pos.z - u.Pos.z, d = M.Hypot(tx, tz);
            if (d > u.T.Radius + tg.T.Radius + u.T.Reach + 0.6f || MathF.Abs(tg.Pos.y - u.Pos.y) > 2) return; // промах: цель отошла
            float nx = d > 1e-4f ? tx / d : 0, nz = d > 1e-4f ? tz / d : 1;
            float dmg = u.T.Ranged ? 8 : u.T.Dmg;
            if (tg.T.Mount) dmg *= u.T.VsCav;
            dmg *= 1 + M.Clamp((u.Pos.y - tg.Pos.y) * 0.1f, -0.3f, 0.35f);   // сверху бить легче
            bool rear = MathF.Sin(tg.Yaw) * nx + MathF.Cos(tg.Yaw) * nz > 0.35f; // цель смотрит от нас
            if (rear) dmg *= 1.35f;
            if (Time - u.Squad.FirstStrikeT < 2.5f) dmg *= 1.6f;               // удар из засады
            if (Time < u.Squad.RushUntil) dmg *= 1.25f;                        // удар с разбега после клича
            if (u.Duel == tg) dmg *= 2.5f;                                     // поединок богатырей — недолгий
            bool charge = u.T.Charge > 1 && u.ChargeT > 0.8f;
            if (charge) { dmg *= u.T.Charge; u.ChargeT = 0; Trample(u, tg); }
            float kb = (charge ? 5 : 1.3f) * u.T.KnockMul / tg.T.Mass;
            u.AtkCount++;
            if (u.SpinNext)
            { // удар вихрем: всех врагов вокруг
                u.SpinNext = false;
                CellOf(u.Pos.x, u.Pos.z, out int scx, out int scz);
                for (int z = Math.Max(scz - 1, 0); z <= Math.Min(scz + 1, gridDim - 1); z++)
                    for (int x = Math.Max(scx - 1, 0); x <= Math.Min(scx + 1, gridDim - 1); x++)
                        for (int j = head[z * gridDim + x]; j >= 0; j = next[j])
                        {
                            if (j >= Units.Count) continue;
                            var e = Units[j];
                            if (e == tg || !e.Alive || e.Team == u.Team || MathF.Abs(e.Pos.y - u.Pos.y) > 1.5f || u.Duel != null) continue;
                            float ex = e.Pos.x - u.Pos.x, ez = e.Pos.z - u.Pos.z, ed = M.Hypot(ex, ez);
                            if (ed > u.T.SpinR + e.T.Radius || ed < 1e-3f) continue;
                            float ek = kb * 0.8f * tg.T.Mass / e.T.Mass;
                            Damage(e, dmg * 0.7f, ex / ed * ek, ez / ed * ek, false, u);
                        }
            }
            if (tg.Squad != null && tg.Squad.ShieldWall && !rear && charge) dmg *= 0.5f; // натиск вязнет в стене щитов
            Damage(tg, dmg, nx * kb, nz * kb, false, u, rear);
            if (u.T.Leech > 0) u.Hp = MathF.Min(u.T.Hp, u.Hp + dmg * u.T.Leech * (1 - tg.T.Armor));
        }

        /// <summary>Натиск рыцаря задевает и сбивает соседей цели.</summary>
        void Trample(Unit k, Unit main)
        {
            float fx = MathF.Sin(k.Yaw), fz = MathF.Cos(k.Yaw);
            foreach (var e in Teams[1 - k.Team].ToList())
            {
                if (e == main || !e.Alive) continue;
                float tx = e.Pos.x - k.Pos.x, tz = e.Pos.z - k.Pos.z, d = M.Hypot(tx, tz);
                if (d > 2.6f || tx * fx + tz * fz < 0) continue;
                float s = 2.5f / e.T.Mass, dd = MathF.Max(d, 0.1f);
                Emit(FxKind.Charge, e.Pos, fx, fz, e.Team);
                Damage(e, k.T.Dmg * 0.6f, (tx / dd + fx) * s, (tz / dd + fz) * s, false, k);
                if (e.Squad != null && !e.T.Fearless) e.Squad.Morale -= k.T.Terror ? 5 : 2;
            }
        }

        public void Damage(Unit t, float amount, float kx, float kz, bool arrow, Unit src, bool rear = false)
        {
            if (!t.Alive) return;
            amount *= 0.8f + Rng.Rand() * 0.4f;
            if (arrow && !rear) amount *= 1 - t.T.ArrowBlock;
            if (arrow && !rear && t.Squad != null && t.Squad.ShieldWall) amount *= 0.3f; // стена щитов
            if (arrow && t.Pos.y > World.HeightAt(t.Pos.x, t.Pos.z) + 3) amount *= 0.55f; // за зубцами стены
            amount *= 1 - t.T.Armor;
            t.Hp -= amount;
            t.LastKnock = new V3(t.Knock.x + kx, 0, t.Knock.z + kz);
            t.FlashT = Time;
            LastHitT = Time;
            t.Knock.x += kx; t.Knock.z += kz;
            var sq = t.Squad;
            if (sq != null && !sq.Special)
            {
                if (arrow) { sq.LastHitT = Time; sq.Morale -= 0.6f; }
                if (rear) sq.Morale -= 1.5f;
            }
            if (!arrow)
            { // пыль и искры в точке удара (на уровне груди)
                float kl = M.Hypot(kx, kz);
                var hp = new V3(t.Pos.x - (kl > 1e-4f ? kx / kl : 0) * t.T.Radius * 0.6f, t.Pos.y + (t.T.Mount ? 1.9f : 1.1f), t.Pos.z - (kl > 1e-4f ? kz / kl : 0) * t.T.Radius * 0.6f);
                Emit(t.T.ArrowBlock > 0.3f && !rear && t.Hp > 0 ? FxKind.Block : FxKind.Hit, hp, kx, kz, t.Team);
            }
            if (t.Hp <= 0)
            {
                if (src != null && Races[src.Team].Rage && Time >= RoarUntil[src.Team]) Rage[src.Team] += src.T.Slot == 0 ? 0.5f : 1f;
                if (src != null) src.Kills++;
                Kill(t);
                return;
            }
            // видимая реакция на удар: щитоносец спереди принимает удар на щит, остальные вздрагивают
            if (!arrow && t.AtkT < 0 && t.HitAnimT <= 0) t.HitReact = t.T.ArrowBlock > 0.3f && !rear ? 2 : 1;
            else if (arrow && t.AtkT < 0 && t.HitAnimT <= 0 && Rng.Rand() < 0.5f) t.HitReact = 1;
            if (src != null && !arrow && t.Target != src && Rng.Rand() < 0.5f) t.Target = src;
        }

        void Kill(Unit u)
        {
            Emit(FxKind.Kill, u.Pos, 0, 0, u.Team);
            float kl = M.Hypot(u.LastKnock.x, u.LastKnock.z);
            if (kl > 2.5f && !u.T.Mount)
            { // сильный удар (натиск, громила, взрыв) — тело взлетает и падает поодаль
                u.Fly = new V3(u.LastKnock.x * 1.1f, 2.5f + kl * 0.45f, u.LastKnock.z * 1.1f);
                u.Flying = true;
            }
            u.Alive = false; u.DeadT = 0; u.Target = null; u.AtkT = -1; u.DownT = 0;
            if (u.T.Special != Special.Messenger) LastKillT = Time;
            u.Vel = new V3(0, 0, 0); u.CurSpeed = 0;
            var sq = u.Squad;
            if (u.T.Special == Special.Messenger)
            {
                var c = u.Carry;
                if (c != null && !c.Delivered && c.Captain != null)
                {
                    if (c.Wing.PendingMission == c.Mission) c.Wing.PendingMission = null;
                    AddLog(u.Team, $"Гонец к воеводе {c.Captain.Name} перехвачен — приказ «{Defs.MissionText(c.Mission.Kind)}» не дошёл");
                }
                else if (c != null && !c.Delivered)
                {
                    if (c.Squad.Pending == c.Order) c.Squad.Pending = null;
                    AddLog(u.Team, $"Гонец к отряду «{c.Squad.Name}» перехвачен — приказ «{Defs.OrderText(c.Order.Kind).ToLowerInvariant()}» не дошёл");
                }
            }
            else if (u.T.Special == Special.Captain)
            {
                var cp = u.Cmd;
                var gen = Commanders[u.Team];
                string wing = Defs.Cap(Defs.WingName(cp.Wing.Key));
                foreach (var s in cp.Wing.Squads) s.Morale -= 15;
                AddLog(u.Team, gen != null && gen.Unit.Alive
                    ? $"Воевода {cp.Name} пал! {wing}: теперь приказы отдаёт {gen.Name}"
                    : $"Воевода {cp.Name} пал! {wing} осталось без начальства");
            }
            else if (u.T.Special == Special.Commander)
            {
                var cmd = Commanders[u.Team];
                foreach (var s in Squads) if (s.Team == u.Team && !s.Special) s.Morale -= 25;
                foreach (var s in Squads)
                    if (s.Team == u.Team && s.Reserve) { s.Reserve = false; if (s.Order.Mode != Mode.Rout) ApplyOrder(s, new Order(OrderKind.Advance, Mode.Advance)); }
                int caps = Captains[u.Team].Count(c => c.Unit.Alive);
                AddLog(u.Team, $"{(cmd != null ? cmd.Name : "Полководец")} пал! {(caps > 0 ? "Воеводы бьются дальше своим умом" : "Армия осталась без приказов")}");
            }
            else if (sq != null) sq.Morale -= 6;
            if (u.T.Hero) HeroFell(u);
            Animate(u, false);
        }

        void Integrate(Unit u, float dt)
        {
            float vx = u.Vel.x, vz = u.Vel.z, sp = M.Hypot(vx, vz);
            int cls = u.T.Mount ? 1 : 0;
            var nav = World.Nav;
            float f = 1;
            if (sp > 0.05f)
            {
                // в гору тяжело, под пологую горку легче, по крутому спуску — осторожно
                f = World.SlopeSpeed(World.GroundAt(u.Pos.x + vx / sp, u.Pos.z + vz / sp) - u.Pos.y);
            }
            f *= MathF.Max(0.2f, nav.SpeedAt(u.Pos.x, u.Pos.z, cls)); // топь, брод, чаща
            if (World.InWall(u.Pos.x, u.Pos.z)) f *= 0.35f; // перелезаем ограду
            u.CurSpeed = sp * f;
            u.Pace += (u.CurSpeed - u.Pace) * (1 - MathF.Exp(-dt / 0.4f));
            float ox = u.Pos.x, oz = u.Pos.z, oy = u.Pos.y, lim = World.Field - 0.5f;
            float nx = M.Clamp(ox + (vx * f + u.Knock.x) * dt, -lim, lim), nz = M.Clamp(oz + (vz * f + u.Knock.z) * dt, -lim, lim);
            // Не заходим в непроходимое, в ствол и в камень, не прыгаем с обрыва или со стены — скользим вдоль
            if (!Place(u, nx, nz, cls) && !Place(u, nx, oz, cls) && !Place(u, ox, nz, cls) && !Veer(u, nx - ox, nz - oz, cls)) u.Pos.y = World.GroundAt(ox, oz);
            float k = MathF.Exp(-7 * dt);
            u.Knock.x *= k; u.Knock.z *= k;
            u.Phase += dt * u.CurSpeed * (u.T.Mount ? 1.4f : 3.3f);
        }

        /// <summary>
        /// Упёрся и вдоль осей не скользит (крутой склон, куда вниз можно только наискось; угол настила) — шагаем,
        /// отклонившись на 30–90° в ту или другую сторону: на склоне так и спускаются, «змейкой».
        /// </summary>
        bool Veer(Unit u, float dx, float dz, int cls)
        {
            float l = M.Hypot(dx, dz);
            if (l < 1e-4f) return false;
            for (int k = 1; k <= 3; k++)
                foreach (float sg in new[] { 1f, -1f })
                {
                    float a = sg * k * M.PI / 6, c = MathF.Cos(a), s = MathF.Sin(a), f = MathF.Max(0.5f, c);
                    if (Place(u, u.Pos.x + (dx * c - dz * s) * f, u.Pos.z + (dx * s + dz * c) * f, cls)) return true;
                }
            return false;
        }

        /// <summary>
        /// Переставить бойца в (x, z), если туда можно шагнуть; из ствола, камня, угла дома его выталкивает вдоль края.
        /// Вытолкнуло туда, куда шагнуть нельзя (обрыв, край стены), — остаётся на месте, а не внутри препятствия.
        /// </summary>
        bool Place(Unit u, float x, float z, int cls)
        {
            float ox = u.Pos.x, oz = u.Pos.z, oy = u.Pos.y;
            if (!CanMove(ox, oz, oy, x, z, cls)) return false;
            float r = u.T.Radius * 0.8f, ix = x, iz = z;
            World.Obs.PushOut(ref x, ref z, r, oy);
            // препятствие отклоняет шаг, а не переносит бойца: выталкивание не длиннее самого шага (иначе конь в тесной
            // роще или богатырь у ограды прыгали на полметра из стороны в сторону)
            float px = x - ix, pz = z - iz, pl = M.Hypot(px, pz), cap = MathF.Max(0.12f, M.Hypot(ix - ox, iz - oz) * 1.5f);
            if (pl > cap) { x = ix + px / pl * cap; z = iz + pz / pl * cap; }
            // туда, откуда не вытолкнуть, не шагаем (если только уже не стоим внутри — тогда выбираемся понемногу)
            if (World.Obs.Hit(x, z, r - 0.05f, oy) != null && World.Obs.Hit(ox, oz, r - 0.05f, oy) == null) return false;
            if ((x != ox || z != oz) && !CanMove(ox, oz, oy, x, z, cls)) return false;
            u.Pos.x = x; u.Pos.z = z;
            u.Pos.y = World.GroundAt(x, z);
            return true;
        }

        /// <summary>Можно ли шагнуть из (ox, oz) на высоте oy в (nx, nz): не в дом, не в воду, не с обрыва.</summary>
        bool CanMove(float ox, float oz, float oy, float nx, float nz, int cls)
        {
            var nav = World.Nav;
            if (nav.SpeedAt(nx, nz, cls) == 0 && nav.SpeedAt(ox, oz, cls) > 0) return false;
            // переход между клетками — только там, где его допускает и поиск пути (иначе забредают на отрезанные уступы)
            int a = nav.Idx(ox, oz), b = nav.Idx(nx, nz);
            float g = World.GroundAt(nx, nz);
            if (a >= 0 && b >= 0 && a != b && !nav.Step(a, b, a % nav.Dim != b % nav.Dim && a / nav.Dim != b / nav.Dim))
            { // Сетка путей переход не допускает. Кроме одного случая: клетка полтора метра, её центр на мосту или стене,
              // а боец стоит у края на земле — не на её уровне. Такого выпускаем в соседнюю клетку его уровня, иначе он заперт.
                if (MathF.Abs(oy - nav.Surf[a]) < 1f || MathF.Abs(g - nav.Surf[b]) > 0.6f) return false;
            }
            // через перила моста не шагаем — ни с моста, ни на мост
            if (World.Decks.RailCross(ox, oz, nx, nz)) return false;
            // в омут не шагаем, даже если клетка в целом проходима (край моста, крутая набережная); выбираться — можно
            if (World.Water - g > World.WadeMax && g < oy) return false;
            // уклон не круче ~52° — как у поиска пути; иначе по крутому берегу сползали бы в реку мелкими шагами
            float dy = MathF.Abs(g - oy), d = M.Hypot(nx - ox, nz - oz);
            if (dy <= d * 1.3f + 0.005f) return true;
            // ступенька круче уклона — только на настиле или на его край (въезд на мост, с лестницы на боевой ход), не выше
            // DeckStep, как и в сетке путей. На склоне такой поблажки нет: толкотня дёргает бойца по 1–2 см несколько раз
            // за шаг, и с поблажкой он сползал по отвесу ущелья. Сам уступ меряется отдельно (World.Ledge): за длинный шаг
            // конного «DeckStep + уклон» набегал до 0,6 м, и у конца моста над крутым берегом всадник сходил с настила вбок
            return dy <= World.DeckStep + d * 1.3f && (World.OnDeck(ox, oz) || World.OnDeck(nx, nz)) && !World.Ledge(ox, oz, nx, nz);
        }

        // ---------------------------------------------------------------- соседи

        void CellOf(float x, float z, out int cx, out int cz)
        {
            cx = M.Clamp((int)((x + gridHalf) / GRID), 0, gridDim - 1);
            cz = M.Clamp((int)((z + gridHalf) / GRID), 0, gridDim - 1);
        }

        void BuildGrid()
        {
            if (gridHalf != World.Field + 10)
            {
                gridHalf = World.Field + 10;
                gridDim = (int)Math.Ceiling(gridHalf * 2 / GRID);
                head = new int[gridDim * gridDim];
            }
            for (int i = 0; i < head.Length; i++) head[i] = -1;
            if (next.Length < Units.Count) { next = new int[Units.Count * 2]; push = new float[Units.Count * 4]; }
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units[i];
                if (!u.Alive) continue;
                CellOf(u.Pos.x, u.Pos.z, out int cx, out int cz);
                int c = cz * gridDim + cx;
                next[i] = head[c]; head[c] = i;
            }
        }

        float[] rcStart = new float[0], rcAcc = new float[0];

        /// <summary>
        /// Подвижность солдата в паре (обратная «жёсткость»): кто уступает место. Стоящий в строю и рубящийся
        /// почти не уступают идущему своему — тот обтекает; сбитый и бегущий уступают всем; враги — по массе.
        /// </summary>
        static float Mob(Unit a, bool foe)
        {
            var o = a.Squad?.Order;
            if (a.DownT > 0) return 1.5f;
            if (foe) return (a.Settled && o != null && o.Mode == Mode.Hold ? 0.35f : 1f) / a.T.Mass;
            if (o != null && o.Mode == Mode.Rout) return 1.2f;
            if (a.Engaged) return 0.15f;
            if (a.Settled) return 0.25f;
            return 1f / a.T.Mass;
        }

        /// <summary>
        /// Столкновения (по образцу PBD-толпы): две итерации поправок позиций по парам соседей, каждая пара — один раз,
        /// сдвиг не больше 12 см за итерацию. Главное — после поправки гасим скорость «в соседа»: кто упёрся, перестаёт
        /// давить, и вечного «трения» нет. Между врагами — зазор 15 см, чтобы линии не прорастали друг в друга.
        /// </summary>
        void ResolveContacts()
        {
            var U = Units;
            int n = U.Count;
            if (rcStart.Length < n * 2) { rcStart = new float[n * 4]; rcAcc = new float[n * 4]; }
            for (int i = 0; i < n; i++) { rcStart[i * 2] = U[i].Pos.x; rcStart[i * 2 + 1] = U[i].Pos.z; }
            float lim = World.Field - 0.5f;
            for (int it = 0; it < 2; it++)
            {
                Array.Clear(rcAcc, 0, n * 2);
                for (int i = 0; i < n; i++)
                {
                    var u = U[i];
                    if (!u.Alive) continue;
                    CellOf(u.Pos.x, u.Pos.z, out int cx, out int cz);
                    for (int z = Math.Max(cz - 1, 0); z <= Math.Min(cz + 1, gridDim - 1); z++)
                        for (int x = Math.Max(cx - 1, 0); x <= Math.Min(cx + 1, gridDim - 1); x++)
                            for (int j = head[z * gridDim + x]; j >= 0; j = next[j])
                            {
                                if (j <= i || j >= n) continue; // каждая пара — один раз
                                var o = U[j];
                                if (!o.Alive) continue;
                                bool foe = o.Team != u.Team;
                                float min = u.T.Radius + o.T.Radius + (foe ? 0.15f : 0f);
                                float dx = u.Pos.x - o.Pos.x, dz = u.Pos.z - o.Pos.z, d2 = dx * dx + dz * dz;
                                if (d2 >= min * min || MathF.Abs(u.Pos.y - o.Pos.y) > 1.5f) continue; // на стене и под стеной не толкаются
                                float d = MathF.Sqrt(d2) + 1e-5f;
                                float nx = d > 2e-5f ? dx / d : MathF.Cos(i), nz = d > 2e-5f ? dz / d : MathF.Sin(i);
                                float pen = (min - d) * 0.8f;
                                if (foe && it == 0 && u.T.Mount != o.T.Mount)
                                { // конь врезается в пехотинца: удар с импульсом
                                    if (u.T.Mount) ChargeImpact(u, o, -nx, -nz); else ChargeImpact(o, u, nx, nz);
                                }
                                if (!foe && u.Squad == o.Squad && u.Squad != null && u.Squad.FormMarch)
                                { // своя колонна на марше: не «гармошка» — продольную часть поправки ослабляем
                                    float fx = MathF.Sin(u.Squad.Facing), fz = MathF.Cos(u.Squad.Facing), al = nx * fx + nz * fz;
                                    nx -= fx * al * 0.7f; nz -= fz * al * 0.7f;
                                }
                                float wi = Mob(u, foe), wj = Mob(o, foe), ws = wi + wj;
                                rcAcc[i * 2] += nx * pen * wi / ws; rcAcc[i * 2 + 1] += nz * pen * wi / ws;
                                rcAcc[j * 2] -= nx * pen * wj / ws; rcAcc[j * 2 + 1] -= nz * pen * wj / ws;
                            }
                }
                for (int i = 0; i < n; i++)
                {
                    var u = U[i];
                    float px = rcAcc[i * 2], pz = rcAcc[i * 2 + 1];
                    if (!u.Alive || (px == 0 && pz == 0)) continue;
                    float l = M.Hypot(px, pz);
                    if (l > 0.12f) { px *= 0.12f / l; pz *= 0.12f / l; }
                    // толкотня не сбрасывает со стены и в воду и не вдавливает в ствол
                    Place(u, M.Clamp(u.Pos.x + px, -lim, lim), M.Clamp(u.Pos.z + pz, -lim, lim), u.T.Mount ? 1 : 0);
                }
            }
            // упёрся — перестаём давить: гасим составляющую скорости против поправки; в сцепке — «трение»
            for (int i = 0; i < n; i++)
            {
                var u = U[i];
                if (!u.Alive) continue;
                float cx = u.Pos.x - rcStart[i * 2], cz = u.Pos.z - rcStart[i * 2 + 1], cl = M.Hypot(cx, cz);
                if (cl < 1e-4f) continue;
                float nx = cx / cl, nz = cz / cl, vn = u.Vel.x * nx + u.Vel.z * nz;
                if (vn < 0) { u.Vel.x -= nx * vn; u.Vel.z -= nz * vn; }
                if (u.Engaged) { u.Vel.x *= 0.7f; u.Vel.z *= 0.7f; }
            }
        }

        /// <summary>
        /// Удар коня о пехотинца (n — от коня к пехотинцу): неупругий удар масс. Конь теряет скорость и вязнет
        /// в глубоком строю; пехотинец отлетает, а при сильном ударе падает с ног. Строй «на упоре» (стоит,
        /// держит место, лицом к удару) тяжелее — выдерживает.
        /// </summary>
        void ChargeImpact(Unit k, Unit e, float nx, float nz)
        {
            float vRel = (k.Vel.x - e.Vel.x) * nx + (k.Vel.z - e.Vel.z) * nz;
            if (vRel < 2.5f || e.DownT > 0 || Time - k.LastImpactT < 0.12f) return;
            float fx = MathF.Sin(e.Yaw), fz = MathF.Cos(e.Yaw);
            bool braced = e.Squad != null && -(fx * nx + fz * nz) > 0.7f && (e.Squad.ShieldWall || e.Settled && e.Squad.Order.Mode == Mode.Hold);
            float brace = braced ? MathF.Min(4, 1 + 0.4f * Math.Max(0, (e.Squad?.Rows ?? 1) - 1 - e.Row)) : 1;
            float me = e.T.Mass * brace, mk = k.T.Mass;
            float J = mk * me / (mk + me) * vRel * 1.2f;
            k.Vel.x -= nx * J / mk; k.Vel.z -= nz * J / mk;                // конь вязнет
            e.Knock.x += nx * J / me * 0.8f; e.Knock.z += nz * J / me * 0.8f; // пехотинец отлетает
            if (J / me > 3f && brace < 1.5f) { e.DownT = 1f + Rng.Rand() * 1.5f; e.DownAnim = 0; Emit(FxKind.Down, e.Pos, nx, nz, e.Team); }
            float slope = M.Clamp((k.Pos.y - e.Pos.y) * 0.15f, -0.3f, 0.5f);  // под гору — сильнее
            Emit(FxKind.Charge, e.Pos, nx, nz, e.Team);
            Damage(e, J * 1.6f * (1 + slope), 0, 0, false, k);
            if (e.Squad != null) e.Squad.Morale -= 0.8f;
            k.LastImpactT = Time;
        }

        /// <summary>
        /// Взрыв: всем в радиусе урон и отброс со спадом к краю (своим — вполовину). Сильно отброшенные
        /// летят (полёт тел), выжившие падают с ног.
        /// </summary>
        public void Explode(V3 p, float r, float dmg, float knock, int team)
        {
            Emit(FxKind.Explosion, p, 0, 0, team);
            CellOf(p.x, p.z, out int cx, out int cz);
            int rr = (int)MathF.Ceiling(r / GRID);
            for (int z = Math.Max(cz - rr, 0); z <= Math.Min(cz + rr, gridDim - 1); z++)
                for (int x = Math.Max(cx - rr, 0); x <= Math.Min(cx + rr, gridDim - 1); x++)
                    for (int j = head[z * gridDim + x]; j >= 0; j = next[j])
                    {
                        if (j >= Units.Count) continue;
                        var e = Units[j];
                        if (!e.Alive || MathF.Abs(e.Pos.y - p.y) > 3) continue;
                        float ex = e.Pos.x - p.x, ez = e.Pos.z - p.z, d = M.Hypot(ex, ez);
                        if (d > r + e.T.Radius) continue;
                        float fall = 1 - M.Clamp(d / (r + e.T.Radius), 0, 1) * 0.6f, own = e.Team == team ? 0.5f : 1f;
                        float k = knock * fall / e.T.Mass, nx = d > 1e-3f ? ex / d : 0, nz = d > 1e-3f ? ez / d : 0;
                        if (!e.T.Mount && k > 3 && e.DownT <= 0) { e.DownT = 1.2f + Rng.Rand() * 1.2f; e.DownAnim = 0; }
                        Damage(e, dmg * fall * own, nx * k, nz * k, false, null);
                        if (e.Squad != null) e.Squad.Morale -= 1.5f * own;
                    }
        }

        void Separate()
        {
            var U = Units;
            var P = push;
            if (P.Length < U.Count * 2) P = push = new float[U.Count * 4];
            for (int i = 0; i < U.Count; i++)
            {
                P[i * 2] = 0; P[i * 2 + 1] = 0;
                var u = U[i];
                if (!u.Alive) continue;
                CellOf(u.Pos.x, u.Pos.z, out int cx, out int cz);
                for (int z = Math.Max(cz - 1, 0); z <= Math.Min(cz + 1, gridDim - 1); z++)
                    for (int x = Math.Max(cx - 1, 0); x <= Math.Min(cx + 1, gridDim - 1); x++)
                        for (int j = head[z * gridDim + x]; j >= 0; j = next[j])
                        {
                            if (j == i || j >= U.Count) continue;
                            var o = U[j];
                            float dx = u.Pos.x - o.Pos.x, dz = u.Pos.z - o.Pos.z, min = u.T.Radius + o.T.Radius, d2 = dx * dx + dz * dz;
                            if (d2 >= min * min || MathF.Abs(u.Pos.y - o.Pos.y) > 1.5f) continue; // на стене и под стеной не толкаются
                            float d = MathF.Sqrt(d2);
                            float nx = d > 1e-4f ? dx / d : MathF.Cos(i), nz = d > 1e-4f ? dz / d : MathF.Sin(i);
                            float s = (min - d) * (o.T.Mass / (u.T.Mass + o.T.Mass)) * 0.5f;
                            P[i * 2] += nx * s; P[i * 2 + 1] += nz * s;
                        }
            }
            float lim = World.Field - 0.5f;
            for (int i = 0; i < U.Count; i++)
            {
                var u = U[i];
                float px = P[i * 2], pz = P[i * 2 + 1];
                if (!u.Alive || (px == 0 && pz == 0)) continue;
                float l = M.Hypot(px, pz);
                if (l > 0.4f) { px *= 0.4f / l; pz *= 0.4f / l; }
                Place(u, M.Clamp(u.Pos.x + px, -lim, lim), M.Clamp(u.Pos.z + pz, -lim, lim), u.T.Mount ? 1 : 0);
            }
        }

        /// <summary>Ближайший живой враг команды team у точки (для попадания болтов).</summary>
        public Unit EnemyNear(float x, float z, int team, float extra)
        {
            if (gridDim == 0) return null;
            CellOf(x, z, out int cx, out int cz);
            Unit best = null;
            float bestD = float.PositiveInfinity;
            for (int gz = Math.Max(cz - 1, 0); gz <= Math.Min(cz + 1, gridDim - 1); gz++)
                for (int gx = Math.Max(cx - 1, 0); gx <= Math.Min(cx + 1, gridDim - 1); gx++)
                    for (int j = head[gz * gridDim + gx]; j >= 0; j = next[j])
                    {
                        if (j >= Units.Count) continue;
                        var o = Units[j];
                        if (!o.Alive || o.Team == team) continue;
                        float dx = o.Pos.x - x, dz = o.Pos.z - z, r = o.T.Radius + extra, d2 = dx * dx + dz * dz;
                        if (d2 < r * r && d2 < bestD) { bestD = d2; best = o; }
                    }
            return best;
        }

        public V3? Centroid()
        {
            float x = 0, y = 0, z = 0;
            int n = 0;
            foreach (var u in Units) if (u.Alive && u.T.Special == Special.None) { x += u.Pos.x; y += u.Pos.y; z += u.Pos.z; n++; }
            return n > 0 ? new V3(x / n, y / n, z / n) : (V3?)null;
        }

        // ---------------------------------------------------------------- анимация

        /// <summary>
        /// Выбирает анимацию по состоянию солдата. Здесь только имена клипов и время —
        /// сами позы берутся из запечённой текстуры.
        /// </summary>
        // Скорость «земли» под опорной ногой в клипах при росте модели 1,9 м и пропорциях Pose.Stretch (замер по костям стоп,
        // у коней — по копытам): юнит, идущий с такой скоростью, играет клип в темпе 1, и ноги не скользят. Масштаб модели удлиняет шаг.
        const float WalkV = 0.85f, JogV = 2.55f, RunV = 4.8f, HorseWalkV = 1.43f, GallopV = 5.1f;

        static float Tempo(float speed, float native, float scale, float lo, float hi) => M.Clamp(speed / (native * scale), lo, hi);

        /// <summary>
        /// Походка пешего: 0 стоит, 1 шаг, 2 трусца (Running_B), 3 бег (свой клип бега). Пороги с запасом в обе стороны,
        /// чтобы на границе не мельтешил шаг-бег; у кого «бег» — это шаг (мертвецы), выше трусцы не поднимается.
        /// </summary>
        static int Gait(Unit u, string run)
        {
            float speed = u.Pace, now = u.CurSpeed;
            string cur = u.Anim.Name;
            bool fast = run != "Walking_A" && run != "Running_B";
            int g = fast && cur == run ? 3 : cur == "Running_B" ? 2 : cur == "Walking_A" || cur == "Shield_Wall_Walk" ? 1 : 0;
            // тронуться и встать — сразу (по мгновенной скорости), шаг/трусца/бег — по сглаженной
            if (g == 0) { if (now > 0.35f) g = 1; }
            else if (now < 0.2f && speed < 0.4f) g = 0;
            if (g >= 1) g = speed > 1.5f ? Math.Max(g, 2) : speed < 1.05f ? 1 : g;
            if (g >= 2) g = !fast ? 2 : speed > 3.4f ? 3 : speed < 2.9f ? 2 : g;
            return g;
        }

        public void Animate(Unit u, bool cheer)
        {
            var a = u.T.Anim;
            float speed = u.CurSpeed;
            if (u.T.Mount)
            {
                if (!u.Alive)
                {
                    if (!u.DeathShown)
                    {
                        u.Anim.Play("Death", once: true);
                        u.Ride.Play(Rng.Rand() < 0.5f ? "Death_A" : "Death_B", once: true);
                        u.DeathShown = true;
                    }
                    return;
                }
                // шаг или галоп (рыси у коня нет) — с запасом по порогам
                int hg = u.Anim.Name == "Gallop" ? 2 : u.Anim.Name == "Walk" ? 1 : 0;
                float pace = u.Pace;
                if (hg == 0) { if (speed > 0.4f) hg = 1; }
                else if (speed < 0.25f && pace < 0.5f) hg = 0;
                if (hg >= 1) hg = pace > 3.2f ? 2 : pace < 2.6f ? 1 : hg;
                if (hg == 2) u.Anim.Play("Gallop", speed: Tempo(speed, GallopV, u.Scale, 0.65f, 2.0f));
                else if (hg == 1) u.Anim.Play("Walk", speed: Tempo(speed, HorseWalkV, u.Scale, 0.5f, 2.2f));
                else u.Anim.Play("Idle");
                if (u.AtkNew && a.Attack.Length > 0)
                {
                    u.AtkNew = false;
                    u.Ride.Play("atk" + (int)(Rng.Rand() * Math.Min(2, a.Attack.Length)), once: true, restart: true, speed: 1.1f);
                }
                else if (u.AtkT < 0 && u.Ride.Name != "ride") u.Ride.Play("ride");
                return;
            }
            if (!u.Alive)
            {
                if (!u.DeathShown) { u.Anim.Play(Rng.Rand() < 0.5f ? "Death_A" : "Death_B", once: true); u.DeathShown = true; }
                return;
            }
            if (cheer) { u.Anim.Play(a.Cheer); return; }
            if (u.DownT > 0)
            { // упал — лежит — встаёт (поднятый колдуном — встаёт из земли)
                if (u.DownAnim == 3)
                {
                    bool own = ClipDur != null && ClipDur(u.Type, "Rise_Undead") > 0;
                    u.Anim.Play(own ? "Rise_Undead" : "Lie_StandUp", once: true, restart: true, speed: own ? 1 : 1.2f);
                    u.DownAnim = 4;
                    return;
                }
                if (u.DownAnim == 4) return;
                float up = ClipDur != null ? ClipDur(u.Type, "Lie_StandUp") : 0;
                if (u.DownAnim == 0) { u.Anim.Play("Lie_Down", once: true, restart: true, speed: 1.6f); u.DownAnim = 1; }
                else if (u.DownAnim == 1 && up > 0 && u.DownT < up / 1.4f) { u.Anim.Play("Lie_StandUp", once: true, restart: true, speed: 1.4f); u.DownAnim = 2; }
                return;
            }
            if (u.Squad != null && Time < u.Squad.CryUntil && !u.Engaged) { u.Anim.Play(a.Cheer); return; }
            if (u.HitReact > 0 && !u.AtkNew)
            { // вздрогнул или принял удар на щит — короткая анимация поверх стойки
                string hit = u.HitReact == 2 ? "Block_Hit" : Rng.Rand() < 0.5f ? "Hit_A" : "Hit_B";
                u.HitReact = 0;
                float hd = ClipDur != null ? ClipDur(u.Type, hit) : 0;
                if (hd > 0) { u.Anim.Play(hit, once: true, restart: true, speed: 1.3f); u.HitAnimT = hd / 1.3f; return; }
            }
            if (u.CastNew)
            { // колдун поднимает мёртвых
                u.CastNew = false;
                float cd = ClipDur != null ? ClipDur(u.Type, "Spellcast_Raise") : 0;
                if (cd > 0 && !u.AtkNew) { u.Anim.Play("Spellcast_Raise", once: true, restart: true); u.HitAnimT = cd; return; }
            }
            if (u.HitAnimT > 0 && !u.AtkNew) return; // реакция ещё идёт
            if (u.AtkNew)
            {
                u.AtkNew = false;
                string name = a.Attack[(int)(Rng.Rand() * a.Attack.Length)];
                if (u.T.Ranged && !u.Shot) name = a.Melee;
                if (u.SpinNext && a.Melee != null) name = a.Melee;
                float dur = ClipDur != null ? ClipDur(u.Type, name) : 0;
                u.Anim.Play(name, once: true, restart: true, speed: dur > 0 ? M.Clamp(dur / u.AtkDur, 0.8f, 2.2f) : 1);
                return;
            }
            if (u.AtkT >= 0) return; // удар ещё идёт
            if (u.T.Ranged && u.Aiming) { u.Anim.Play(u.Cooldown > 0.6f ? a.Reload : a.Aim); return; }
            if (u.Squad != null && u.Squad.ShieldWall && !u.Engaged)
            { // стена щитов: щит вперёд, шаг медленный
                bool own = ClipDur != null && ClipDur(u.Type, "Shield_Wall_Idle") > 0;
                if (speed > 0.3f) u.Anim.Play(own && ClipDur(u.Type, "Shield_Wall_Walk") > 0 ? "Shield_Wall_Walk" : "Walking_A", speed: Tempo(speed, WalkV, u.Scale, 0.5f, 2.0f));
                else u.Anim.Play(own ? "Shield_Wall_Idle" : "Blocking");
                return;
            }
            string run = a.Run ?? "Running_A";
            switch (Gait(u, run))
            {
                case 3: u.Anim.Play(run, speed: Tempo(speed, RunV, u.Scale, 0.7f, 1.5f)); break;
                // трусца; у кого это и есть бег (гоблины, упыри, лучники) — темп выше
                case 2: u.Anim.Play("Running_B", speed: Tempo(speed, JogV, u.Scale, 0.6f, run == "Running_A" ? 1.6f : 2.2f)); break;
                case 1: u.Anim.Play("Walking_A", speed: Tempo(speed, WalkV, u.Scale, 0.5f, 1.8f)); break;
                default: u.Anim.Play(a.Idle); break;
            }
        }
    }
}
