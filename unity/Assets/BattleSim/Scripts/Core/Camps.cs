using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleSim.Core
{
    /// <summary>Лагерь врага на карте: стоит и спит, пока к нему не подошли или не тронули; тогда дерётся один он.</summary>
    public sealed class Camp
    {
        public string Name;
        public float X, Z, Aggro = 22;
        /// <summary>Сколько бойцов по местам 0..3 (строевые, ударные, стрелки, конница).</summary>
        public int[] Slots;
        public bool Boss;
        public float Xp;
        public bool Awake, Cleared;
        public readonly List<Squad> Squads = new List<Squad>();
    }

    public sealed class Chest
    {
        public string Name;
        public float X, Z, Xp;
        public bool Opened;
    }

    /// <summary>
    /// Поход в духе Warcraft 3: небольшой отряд с героем идёт по карте, враги стоят лагерями
    /// и просыпаются по одному. Победа — вожак (или все лагеря) повержен, поражение — герой пал.
    /// </summary>
    public sealed partial class Battle
    {
        public bool CampMode;
        public readonly List<Camp> Camps = new List<Camp>();
        public readonly List<Chest> Chests = new List<Chest>();
        public string BossName;
        public bool CampWon;
        float campT;

        public int CampsLeft => Camps.Count(c => !c.Cleared);

        V2 Walk(float x, float z)
        {
            var p = World.ClampField(x, z, 6);
            return World.WalkableNear(p.x, p.z);
        }

        /// <summary>Группа из n бойцов вокруг точки; если там тесно — рядом.</summary>
        bool PlaceGroup(int type, int team, float x, float z, float yaw, int n)
        {
            var t = Defs.All[type];
            int cols = Math.Min(n, t.Ranged ? 4 : 3), rows = (n + cols - 1) / cols;
            for (int k = 0; k < 14; k++)
            {
                float a = k * 2.4f, r = k == 0 ? 0 : 2 + k * 0.9f;
                var p = World.ClampField(x + MathF.Cos(a) * r, z + MathF.Sin(a) * r, 4);
                int placed = PlaceSquad(type, team, p.x, p.z, yaw, cols, rows, n);
                if (placed >= n * 0.6f) return true;
                if (placed > 0) RemoveSquad(Squads[Squads.Count - 1]);
            }
            return false;
        }

        /// <summary>Карта похода: отряд игрока у края, лагеря и сундуки по полю (доли поля fx, fz в −1..1).</summary>
        public void BuildSkirmish(IEnumerable<(string name, float fx, float fz, int[] slots, bool boss, float xp)> camps,
            IEnumerable<(string name, float fx, float fz, float xp)> chests, int[][] party, string boss)
        {
            ClearAll();
            CampMode = true; UseCommanders = false; CampWon = false; BossName = boss;
            Camps.Clear(); Chests.Clear();
            float F = World.Field;
            var start = Walk(0, -F * 0.78f);
            int g = 0;
            foreach (var p in party)
            {
                float ox = ((g % 4) - 1.5f) * 5.5f, oz = (g / 4) * -5f;
                PlaceGroup(Ty(0, p[0]), 0, start.x + ox, start.z + oz, 0, p[1]);
                g++;
            }
            foreach (var c in camps)
            {
                var pos = Walk(c.fx * F, c.fz * F);
                var camp = new Camp { Name = c.name, X = pos.x, Z = pos.z, Slots = c.slots, Boss = c.boss, Xp = c.xp };
                int k = 0;
                for (int slot = 0; slot < 4; slot++)
                {
                    if (c.slots[slot] <= 0) continue;
                    float a = k++ * 2.1f;
                    PlaceGroup(Ty(1, slot), 1, pos.x + MathF.Cos(a) * 4.5f, pos.z + MathF.Sin(a) * 4.5f, M.PI, c.slots[slot]);
                }
                Camps.Add(camp);
            }
            foreach (var c in chests)
            {
                var pos = Walk(c.fx * F, c.fz * F);
                Chests.Add(new Chest { Name = c.name, X = pos.x, Z = pos.z, Xp = c.xp });
            }
        }

        public V2 SkirmishStartPos()
        {
            var a = Squads.Where(s => s.Team == 0 && !s.Special && s.Alive > 0).ToList();
            return Commander.Center(a) ?? new V2(0, 0);
        }

        Order HoldAt(Squad sq, float leash) =>
            new Order(sq.T.Ranged ? OrderKind.Fire : OrderKind.Hold, Mode.Hold) { Leash = leash, Why = "лагерь" }.At(sq.Center.x, sq.Center.z);

        /// <summary>Начало боя в походе: свои стоят на месте, лагеря спят, вожак выходит в своём лагере.</summary>
        void SkirmishStart()
        {
            foreach (var c in Camps) { c.Awake = false; c.Cleared = false; c.Squads.Clear(); }
            foreach (var c in Chests) c.Opened = false;
            CampWon = false;
            foreach (var sq in Squads)
            {
                if (sq.Special || sq.Alive == 0) continue;
                if (sq.Team == 0) { sq.Player = true; ApplyOrder(sq, HoldAt(sq, 9)); continue; }
                Camp best = null; float bd = 16;
                foreach (var c in Camps) { float d = M.Hypot(sq.Center.x - c.X, sq.Center.z - c.Z); if (d < bd) { bd = d; best = c; } }
                if (best == null) continue;
                best.Squads.Add(sq);
                sq.Asleep = true; sq.Player = true;
                ApplyOrder(sq, HoldAt(sq, 6));
            }
            var boss = Camps.FirstOrDefault(c => c.Boss);
            var hd = Races[1].Hero;
            if (boss != null && hd != null)
            {
                var u = new Unit(new UnitPlan { Type = hd.Id, Team = 1, X = boss.X, Z = boss.Z, Yaw = M.PI, Squad = -1 }, World);
                u.PrevPos = u.Pos; u.PrevYaw = u.Yaw;
                var sq = new Squad("boss", hd.Id, 1, M.PI) { Title = BossName ?? "Вожак" };
                sq.Units.Add(u); u.Squad = sq; sq.Size = 1; sq.Asleep = true; sq.Player = true;
                Units.Add(u); Squads.Add(sq);
                Heroes[1] = u;
                sq.Refresh(0);
                ApplyOrder(sq, HoldAt(sq, 6));
                boss.Squads.Add(sq);
            }
        }

        void WakeCamp(Camp c, string why)
        {
            c.Awake = true;
            foreach (var sq in c.Squads)
            {
                if (sq.Alive == 0) continue;
                sq.Asleep = false;
                ApplyOrder(sq, new Order(OrderKind.Advance, Mode.Advance) { Why = "лагерь проснулся" });
            }
            AddLog(1, $"Лагерь «{c.Name}» {why}");
            Emit(FxKind.Roar, new V3(c.X, World.GroundAt(c.X, c.Z), c.Z), 0, 0, 1);
        }

        /// <summary>Каждые 0,3 с: лагеря просыпаются, возвращаются, падают; сундуки открываются.</summary>
        void CampTick(float dt)
        {
            if (!CampMode) return;
            if ((campT -= dt) > 0) return;
            campT = 0.3f;
            var hero = PlayerHero;
            foreach (var c in Camps)
            {
                if (c.Cleared) continue;
                int alive = 0;
                foreach (var sq in c.Squads) alive += sq.Alive;
                if (alive == 0)
                {
                    c.Cleared = true;
                    AddLog(0, $"Лагерь «{c.Name}» разгромлен");
                    if (c.Xp > 0 && HeroControl) HeroGainXp(c.Xp);
                    HeroEvents.Enqueue($"Лагерь «{c.Name}» разгромлен");
                    if (c.Boss || CampsLeft == 0) CampWon = true;
                    continue;
                }
                float aggro = c.Aggro;
                foreach (var sq in c.Squads) if (sq.T.Ranged) aggro = MathF.Max(aggro, sq.T.Range - 2);
                bool hurt = false, near = false, far = true;
                foreach (var sq in c.Squads) if (sq.Alive < sq.Size) hurt = true;
                foreach (var sq in c.Squads)
                    foreach (var u in sq.Units)
                        if (u.Alive && u.Hp < u.T.Hp - 0.5f) hurt = true;
                foreach (var u in Teams[0])
                {
                    if (!u.Alive || u.T.Special != Special.None) continue;
                    float d = M.Hypot(u.Pos.x - c.X, u.Pos.z - c.Z);
                    if (d < aggro) near = true;
                    if (d < 55) far = false;
                }
                if (!c.Awake && (hurt || near)) WakeCamp(c, hurt && !near ? "поднят по тревоге" : "заметил вас");
                else if (c.Awake && far)
                { // враг далеко: лагерь возвращается на место и засыпает
                    c.Awake = false;
                    foreach (var sq in c.Squads)
                    {
                        if (sq.Alive == 0) continue;
                        sq.Asleep = true;
                        ApplyOrder(sq, new Order(OrderKind.Hold, Mode.Move) { Then = new Order(sq.T.Ranged ? OrderKind.Fire : OrderKind.Hold, Mode.Hold) { Leash = 6 }, Why = "вернулись в лагерь" }.At(c.X, c.Z));
                    }
                }
            }
            if (hero != null && hero.Alive)
                foreach (var ch in Chests)
                {
                    if (ch.Opened || M.Hypot(hero.Pos.x - ch.X, hero.Pos.z - ch.Z) > 3.5f) continue;
                    ch.Opened = true;
                    hero.Hp = hero.T.Hp;
                    HeroGainXp(ch.Xp);
                    HeroEvents.Enqueue($"Сундук «{ch.Name}»: здоровье восстановлено, опыт +{ch.Xp:0}");
                    AddLog(0, $"Каэл открыл сундук «{ch.Name}»");
                }
        }
    }
}
