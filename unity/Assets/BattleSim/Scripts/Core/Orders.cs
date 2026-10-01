using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleSim.Core
{
    /// <summary>Приказ игрока отряду.</summary>
    public enum Cmd { Advance, Hold, Move, Attack, Withdraw, Free, Duel }

    /// <summary>
    /// Приказы игрока (сюжетный поход) и расстановка армий по списку отрядов: враг по сценарию главы,
    /// «расставить за меня», подмога посреди боя.
    /// </summary>
    public sealed partial class Battle
    {
        /// <summary>Имя богатыря армии: null — случайное из списка расы, "" — без богатыря.</summary>
        public readonly string[] HeroTitle = new string[2];

        /// <summary>Полководец армии по сценарию (сбрасывается при ClearAll — задавать после него).</summary>
        public void SetCommander(int team, string name, Trait trait)
        {
            cmdSpec ??= new CmdSpec[2];
            cmdSpec[team] = new CmdSpec { Name = name, Trait = trait };
        }

        /// <summary>
        /// Приказ игрока. Отряд с приказом игрока полководец не трогает, пока игрок не отпустит его («Сам»)
        /// или отряд не побежит. Возвращает, почему приказ не принят (null — принят).
        /// </summary>
        public string PlayerOrder(Squad sq, Cmd cmd, V2 at = default, Squad target = null)
        {
            if (sq == null || sq.Alive == 0 || sq.Special) return "некому приказывать";
            if (sq.Order.Mode == Mode.Rout && !sq.Feigning) return $"{Defs.Cap(sq.Name)} бегут и приказов не слышат";
            bool ranged = sq.T.Ranged;
            Order o;
            string what;
            switch (cmd)
            {
                case Cmd.Advance:
                    o = new Order(OrderKind.Advance, Mode.Advance) { Why = "ваш приказ" };
                    what = "в атаку";
                    break;
                case Cmd.Hold:
                    o = new Order(ranged ? OrderKind.Fire : OrderKind.Hold, Mode.Hold) { Leash = sq.T.Mount ? 14 : 9, Why = "ваш приказ" }.At(sq.C);
                    what = ranged ? "стоять и стрелять" : "стоять здесь";
                    break;
                case Cmd.Move:
                {
                    var p = World.ClampField(at, 3);
                    // идут, не отвлекаясь на стрельбу; дошли — держат место (стрелки — стреляют с него)
                    o = new Order(OrderKind.Hold, Mode.Move) { Then = new Order(ranged ? OrderKind.Fire : OrderKind.Hold, Mode.Hold) { Leash = sq.T.Mount ? 14 : 9 }, Why = "ваш приказ" }.At(p);
                    what = "идти туда";
                    break;
                }
                case Cmd.Attack:
                    if (target == null || target.Alive == 0 || target.Team == sq.Team) return "бить некого";
                    o = new Order(OrderKind.Charge, Mode.Charge) { Target = target, Why = "ваш приказ" };
                    what = $"ударить по отряду «{target.Name}»";
                    break;
                case Cmd.Withdraw:
                {
                    float back = sq.Team == 0 ? -1 : 1;
                    var p = World.ClampField(new V2(sq.Center.x, sq.Center.z + back * 28), 6);
                    p = World.WalkableNear(p.x, p.z);
                    o = new Order(OrderKind.Withdraw, Mode.Move) { Then = new Order(ranged ? OrderKind.Fire : OrderKind.Hold, Mode.Hold) { Leash = 9 }, Why = "ваш приказ" }.At(p);
                    what = "отступить";
                    break;
                }
                case Cmd.Duel:
                {
                    var e = Heroes[1 - sq.Team];
                    if (!sq.T.Hero) return "на поединок выходит только богатырь";
                    if (e == null || !e.Alive) return "вражеского богатыря на поле нет";
                    o = new Order(OrderKind.Charge, Mode.Charge) { Target = e.Squad, Why = "вызов на поединок" };
                    what = $"вызвать на поединок {e.Squad.Title}";
                    break;
                }
                default: // Free
                    sq.Player = false;
                    sq.NextDecision = 0;
                    AddLog(sq.Team, $"{Defs.Cap(sq.Name)} снова под рукой воеводы");
                    return null;
            }
            sq.Player = true; sq.Reserve = false; sq.Garrison = false; sq.Pending = null;
            ApplyOrder(sq, o);
            AddLog(sq.Team, $"Ваш приказ → {sq.Name}: {what}");
            return null;
        }

        /// <summary>Приказ всему войску (кроме бегущих): в атаку, стоять или отдать всё воеводе.</summary>
        public int PlayerOrderAll(int team, Cmd cmd)
        {
            int n = 0;
            foreach (var sq in Squads.ToList())
            {
                if (sq.Team != team || sq.Special || sq.Alive == 0 || (sq.Order.Mode == Mode.Rout && !sq.Feigning)) continue;
                if (cmd == Cmd.Free) { if (sq.Player) { sq.Player = false; sq.NextDecision = 0; n++; } continue; }
                var saved = OnLog; OnLog = null; int logN = Log.Count; // одной строкой, а не по строке на отряд
                if (PlayerOrder(sq, cmd) == null) n++;
                OnLog = saved;
                if (Log.Count > logN) Log.RemoveRange(logN, Log.Count - logN);
            }
            string what = cmd == Cmd.Advance ? "всем в атаку" : cmd == Cmd.Hold ? "всем стоять" : "полки снова под рукой воеводы";
            if (n > 0) AddLog(team, $"Ваш приказ: {what}");
            return n;
        }

        /// <summary>Убрать отряд с поля при расстановке (вернуть в запас).</summary>
        public void RemoveSquad(Squad sq)
        {
            var gone = new HashSet<Unit>(sq.Units);
            Units = Units.Where(u => !gone.Contains(u)).ToList();
            var plans = new HashSet<UnitPlan>(sq.Units.Select(u => u.Plan));
            Plan = Plan.Where(p => !plans.Contains(p)).ToList();
            Squads.Remove(sq);
            NumberSquads();
            Recount();
        }

        /// <summary>Отряд, поставленный как надо: хотя бы 60% бойцов нашли место.</summary>
        bool PlaceFull(int type, int team, float x, float z, float yaw)
        {
            int full = Defs.All[type].Cols * Defs.All[type].Rows;
            int placed = PlaceSquad(type, team, x, z, yaw);
            if (placed >= full * 0.6f) return true;
            if (placed > 0) RemoveSquad(Squads[Squads.Count - 1]);
            return false;
        }

        /// <summary>Поставить отряд рядом с желаемым местом: если там тесно — чуть в сторону.</summary>
        bool PlaceNear(int type, int team, float x, float z, float yaw)
        {
            for (int k = 0; k < 9; k++)
            {
                float ox = k == 0 ? 0 : ((k & 1) == 1 ? 1 : -1) * ((k + 1) / 2) * 4.5f, oz = k < 5 ? 0 : (team == 0 ? -1 : 1) * 5;
                var p = World.ClampField(new V2(x + ox, z + oz), 4);
                if (PlaceFull(type, team, p.x, p.z, yaw)) return true;
            }
            return false;
        }

        /// <summary>
        /// Армия по списку отрядов counts[место 0..3]: пехота рядами (строевые вперемешку с ударными),
        /// стрелки за ней, конница по крыльям. z0 — расстояние от середины поля до первого ряда.
        /// Возвращает, сколько отрядов каждого места не нашли места.
        /// </summary>
        public int[] PlaceArmy(int team, int[] counts, float z0 = -1, float spread = 1)
        {
            var left = (int[])counts.Clone();
            if (z0 < 0) z0 = World.SpawnZ;
            float dir = team == 0 ? -1 : 1, yaw = team == 0 ? 0 : M.PI;
            int perLine = World.Big ? 8 : 6;
            // пехота: строевые и ударные вперемешку, ударные — ближе к середине
            var foot = new List<int>();
            int a = left[0], b = left[1];
            while (a + b > 0)
            {
                if (a > 0 && (b == 0 || a * 1f / Math.Max(1, counts[0]) >= b * 1f / Math.Max(1, counts[1]))) { foot.Add(0); a--; }
                else { foot.Add(1); b--; }
            }
            int lines = Math.Max(1, (int)Math.Ceiling(foot.Count / (float)perLine));
            for (int line = 0, k = 0; line < lines; line++)
            {
                int n = Math.Min(perLine, foot.Count - k);
                // ударные — в середине ряда, строевые — по краям
                int nb = foot.Skip(k).Take(n).Count(s => s == 1);
                var slots = new int[n];
                var byMid = Enumerable.Range(0, n).OrderBy(i => MathF.Abs(i - (n - 1) / 2f)).ToList();
                for (int j = 0; j < n; j++) slots[byMid[j]] = j < nb ? 1 : 0;
                for (int i = 0; i < n; i++)
                {
                    int slot = slots[i];
                    float x = (i - (n - 1) / 2f) * 10 * spread;
                    if (PlaceNear(Ty(team, slot), team, x, dir * (z0 + line * 8), yaw)) left[slot]--;
                }
                k += n;
            }
            // стрелки — за пехотой
            int xb = left[2];
            int xbRows = Math.Max(1, (int)Math.Ceiling(xb / (float)perLine));
            for (int row = 0, done = 0; row < xbRows && done < xb; row++)
            {
                int n = Math.Min(perLine, xb - done);
                for (int i = 0; i < n; i++)
                {
                    float x = (i - (n - 1) / 2f) * 11 * spread;
                    if (PlaceNear(Ty(team, 2), team, x, dir * (z0 + 2 + lines * 8 + row * 6), yaw)) left[2]--;
                }
                done += n;
            }
            // конница — по крыльям, за линией пехоты
            int cav = left[3];
            float wing = MathF.Min(World.Field * 0.62f, 18 + perLine * 5 * spread);
            for (int i = 0; i < cav; i++)
            {
                float side = i % 2 == 1 ? -1 : 1;
                int kk = i / 2;
                if (PlaceNear(Ty(team, 3), team, side * (wing + kk * 3), dir * (z0 + 4 + kk * 9), yaw)) left[3]--;
            }
            return left;
        }

        /// <summary>
        /// Подмога посреди боя: отряды встают позади своего войска и сразу идут в бой.
        /// В план расстановки не попадают (при «Заново» их на поле нет), но в счёт войска входят.
        /// </summary>
        public int Reinforce(int team, int[] counts)
        {
            int before = Plan.Count, sqBefore = Squads.Count;
            float z0 = MathF.Min(World.Field - 10, World.SpawnZ + 22);
            PlaceArmy(team, counts, z0, 0.8f);
            int added = Plan.Count - before;
            Plan.RemoveRange(before, added);
            PlanCount[team] += added;
            for (int i = sqBefore; i < Squads.Count; i++)
            {
                var sq = Squads[i];
                sq.Morale = 100;
                sq.NextDecision = Time + 4; // сперва — в бой, дальше решает полководец
                ApplyOrder(sq, new Order(OrderKind.Advance, Mode.Advance) { Why = "подмога — в бой!" });
            }
            return added;
        }

        /// <summary>
        /// Армия сломлена: от неё осталось не больше 15%, а у врага втрое больше — остатки бегут, и бой окончен
        /// (иначе горстка конных лучников может часами изводить победителей). Сломленная сторона или -1.
        /// </summary>
        public int BrokenArmy()
        {
            if (!Fighting || Time < 30) return -1;
            for (int t = 0; t < 2; t++)
                if (Alive[t] > 0 && Alive[t] <= PlanCount[t] * 0.15f && Alive[1 - t] >= Alive[t] * 3) return t;
            return -1;
        }

        /// <summary>Сломленная армия бежит с поля.</summary>
        public void Collapse(int team)
        {
            foreach (var sq in Squads)
                if (sq.Team == team && sq.Alive > 0 && sq.Order.Mode != Mode.Rout && sq.T.Special != Special.Messenger)
                {
                    sq.Feigning = false; sq.Player = false;
                    ApplyOrder(sq, new Order(OrderKind.Rout, Mode.Rout));
                }
            AddLog(1 - team, $"{Races[team].Name} сломлена — остатки бегут с поля");
        }

        /// <summary>Гарнизон города для сюжета: только защитники (как в осаде), штурмующих расставит игрок.</summary>
        public void GarrisonOnly(int size)
        {
            ClearAll();
            size = M.Clamp(size, 1, 3);
            int[][] cfgFront = { null, new[] { 3, 5 }, new[] { 6, 8 }, new[] { 11, 13 } };
            int[][] cfgXbow = { null, new[] { 2, 3 }, new[] { 4, 5 }, new[] { 7, 9 } };
            int[][] cfgCav = { null, new[] { 1, 2 }, new[] { 2, 4 }, new[] { 4, 6 } };
            int[] cfgLines = { 0, 1, 2, 4 };
            if (World.Town != null) SiegeArmies(size, cfgFront[size], cfgXbow[size], cfgCav[size], cfgLines[size], false);
        }
    }
}
