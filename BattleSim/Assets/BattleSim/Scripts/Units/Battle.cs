using System.Collections.Generic;
using UnityEngine;

namespace BattleSim
{
    /// <summary>
    /// Симуляция боя: выбор целей, движение, толкотня, удары, стрелы, гибель.
    /// Все солдаты обновляются здесь централизованно — так быстрее, чем Update() у каждого.
    /// </summary>
    public class Battle
    {
        public const int MaxUnits = 900;

        public readonly List<Unit> Units = new List<Unit>();
        public readonly List<Placement> Plan = new List<Placement>();
        public readonly int[] AliveCount = new int[2];
        public readonly int[] PlanCount = new int[2];
        public bool Fighting;
        public Arrows Arrows { get; }

        readonly WorldGen world;
        readonly Transform root;
        readonly List<Unit>[] teams = { new List<Unit>(), new List<Unit>() };
        readonly System.Random rnd = new System.Random();
        float time;

        // Сетка для поиска соседей
        const float GridCell = 2.5f;
        const float GridHalf = WorldGen.FieldHalf + 10f;
        readonly int gridDim;
        readonly int[] head;
        int[] next = new int[256];
        Vector3[] push = new Vector3[256];

        public Battle(WorldGen world)
        {
            this.world = world;
            root = new GameObject("Armies").transform;
            gridDim = Mathf.CeilToInt(GridHalf * 2f / GridCell);
            head = new int[gridDim * gridDim];
            Arrows = new Arrows(this, world);
        }

        float R() => (float)rnd.NextDouble();

        // ---------------------------------------------------------------- расстановка

        public int PlaceSquad(UnitType type, int team, Vector3 center, float yaw)
        {
            var s = UnitStats.Of(type);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            int placed = 0;
            for (int r = 0; r < s.SquadRows; r++)
            {
                for (int c = 0; c < s.SquadCols; c++)
                {
                    if (Units.Count >= MaxUnits) return placed;
                    var off = new Vector3((c - (s.SquadCols - 1) * 0.5f) * s.Spacing + (R() - 0.5f) * 0.2f, 0f,
                                          -(r - (s.SquadRows - 1) * 0.5f) * s.Spacing + (R() - 0.5f) * 0.2f);
                    Vector3 p = center + rot * off;
                    if (!WorldGen.InField(p, 1f) || Occupied(p, s.Radius)) continue;
                    var pl = new Placement { Type = type, Team = team, Pos = p, Yaw = yaw, Variant = rnd.Next(UnitArt.HorseVariants) };
                    Plan.Add(pl);
                    Spawn(pl);
                    placed++;
                }
            }
            RecountPlan();
            return placed;
        }

        bool Occupied(Vector3 p, float radius)
        {
            foreach (var u in Units)
            {
                float min = (radius + u.S.Radius) * 0.9f;
                float dx = u.Pos.x - p.x, dz = u.Pos.z - p.z;
                if (dx * dx + dz * dz < min * min) return true;
            }
            return false;
        }

        Unit Spawn(Placement pl)
        {
            var u = new Unit
            {
                Plan = pl, Type = pl.Type, S = UnitStats.Of(pl.Type), Team = pl.Team,
                Pos = new Vector3(pl.Pos.x, world.HeightAt(pl.Pos.x, pl.Pos.z), pl.Pos.z),
                Yaw = pl.Yaw, Seed = R(), Scale = 0.94f + R() * 0.12f,
                RetargetT = R() * 0.5f, Cooldown = R() * 0.8f
            };
            u.Hp = u.S.Hp;
            u.View = UnitArt.Create(pl.Type, pl.Team, pl.Variant, root);
            u.View.Root.localScale = Vector3.one * u.Scale;
            UnitAnim.Pose(u, time);
            Units.Add(u);
            return u;
        }

        public int RemoveNear(Vector3 p, float radius)
        {
            int removed = 0;
            for (int i = Units.Count - 1; i >= 0; i--)
            {
                var u = Units[i];
                float dx = u.Pos.x - p.x, dz = u.Pos.z - p.z;
                if (dx * dx + dz * dz > radius * radius) continue;
                DestroyView(u);
                Units.RemoveAt(i);
                removed++;
            }
            Plan.Clear();
            foreach (var u in Units) Plan.Add(u.Plan);
            RecountPlan();
            return removed;
        }

        public void ClearAll()
        {
            ClearUnits();
            Plan.Clear();
            RecountPlan();
        }

        /// <summary>Вернуть армии в исходную расстановку (реванш / смена карты).</summary>
        public void ResetToPlan()
        {
            ClearUnits();
            foreach (var pl in Plan) Spawn(pl);
            RecountPlan();
        }

        void ClearUnits()
        {
            foreach (var u in Units) DestroyView(u);
            Units.Clear();
            Arrows.Clear();
            Fighting = false;
            AliveCount[0] = AliveCount[1] = 0;
        }

        static void DestroyView(Unit u)
        {
            if (u.View != null && u.View.Go != null) Object.Destroy(u.View.Go);
            u.View = null;
        }

        void RecountPlan()
        {
            PlanCount[0] = PlanCount[1] = 0;
            foreach (var p in Plan) PlanCount[p.Team]++;
            AliveCount[0] = PlanCount[0];
            AliveCount[1] = PlanCount[1];
        }

        /// <summary>Случайные армии: пехота в центре, лучники сзади, рыцари на флангах.</summary>
        public void RandomArmies()
        {
            ClearAll();
            for (int team = 0; team < 2; team++)
            {
                float dir = team == 0 ? -1f : 1f;
                float yaw = team == 0 ? 0f : 180f;
                int front = 3 + rnd.Next(3);
                for (int i = 0; i < front; i++)
                {
                    float x = (i - (front - 1) * 0.5f) * 8.5f + (R() - 0.5f) * 3f;
                    PlaceSquad(R() < 0.5f ? UnitType.Swordsman : UnitType.Spearman, team, new Vector3(x, 0f, dir * (26f + R() * 6f)), yaw);
                }
                int back = 2 + rnd.Next(3);
                for (int i = 0; i < back; i++)
                {
                    float x = (i - (back - 1) * 0.5f) * 9f + (R() - 0.5f) * 3f;
                    PlaceSquad(UnitType.Archer, team, new Vector3(x, 0f, dir * (40f + R() * 5f)), yaw);
                }
                int flanks = rnd.Next(3);
                for (int i = 0; i < flanks; i++)
                {
                    float side = i % 2 == 0 ? 1f : -1f;
                    PlaceSquad(UnitType.Knight, team, new Vector3(side * (32f + R() * 12f), 0f, dir * (34f + R() * 8f)), yaw);
                }
            }
        }

        // ---------------------------------------------------------------- главный цикл

        public void Tick(float dt)
        {
            time += dt;
            teams[0].Clear(); teams[1].Clear();
            foreach (var u in Units) if (u.Alive) teams[u.Team].Add(u);
            AliveCount[0] = teams[0].Count;
            AliveCount[1] = teams[1].Count;
            BuildGrid();

            if (Fighting && dt > 0f)
            {
                foreach (var u in Units) if (u.Alive) Think(u, dt);
                foreach (var u in Units) if (u.Alive) Integrate(u, dt);
                Separate();
            }

            // Стрелы используют сетку, поэтому обновляем их до удаления павших из списка.
            Arrows.Tick(dt);

            for (int i = Units.Count - 1; i >= 0; i--)
            {
                var u = Units[i];
                if (!u.Alive)
                {
                    u.DeadT += dt;
                    if (u.DeadT > 27f) { DestroyView(u); Units.RemoveAt(i); continue; }
                }
                UnitAnim.Pose(u, time);
            }
        }

        void Think(Unit u, float dt)
        {
            var s = u.S;
            u.Cooldown -= dt;
            u.RetargetT -= dt;
            if (u.Target == null || !u.Target.Alive || u.RetargetT <= 0f)
            {
                u.RetargetT = 0.5f + R() * 0.5f;
                Retarget(u);
            }

            Vector3 desired = Vector3.zero;
            bool engaged = false;
            u.Aiming = false;
            var t = u.Target;
            if (t != null)
            {
                Vector3 to = t.Pos - u.Pos; to.y = 0f;
                float d = to.magnitude;
                Vector3 dir = d > 1e-4f ? to / d : u.Forward;
                float contact = s.Radius + t.S.Radius + s.Reach;

                if (s.Ranged && d > contact + 1.5f)
                {
                    if (d <= s.Range)
                    {
                        u.Aiming = true;
                        u.FaceTowards(dir, dt);
                        if (u.Cooldown <= 0f && u.AttackT < 0f) StartAttack(u, true);
                    }
                    else desired = dir * s.Speed;
                }
                else if (d <= contact)
                {
                    engaged = true;
                    u.FaceTowards(dir, dt);
                    if (u.Cooldown <= 0f && u.AttackT < 0f) StartAttack(u, false);
                }
                else
                {
                    desired = dir * s.Speed;
                }
            }

            // Во время удара не бегаем.
            if (u.AttackT >= 0f && !s.Ranged && s.Charge <= 1f) desired *= 0.3f;
            u.Vel = Vector3.MoveTowards(u.Vel, desired, s.Accel * dt);
            if (!engaged && !u.Aiming && u.Vel.sqrMagnitude > 0.05f) u.FaceTowards(u.Vel, dt, s.Charge > 1f ? 180f : 540f);

            if (s.Charge > 1f)
            {
                if (u.Vel.magnitude > s.Speed * 0.7f) u.ChargeT += dt;
                else if (!engaged) u.ChargeT = Mathf.Max(0f, u.ChargeT - dt * 2f);
            }

            if (u.AttackT >= 0f)
            {
                u.AttackT += dt / u.AttackDur;
                float hitAt = u.AttackIsShot ? 0.8f : 0.45f;
                if (!u.HitDone && u.AttackT >= hitAt)
                {
                    u.HitDone = true;
                    ResolveHit(u);
                }
                if (u.AttackT >= 1f) u.AttackT = -1f;
            }
        }

        void StartAttack(Unit u, bool shot)
        {
            u.AttackT = 0f;
            u.HitDone = false;
            u.AttackIsShot = shot;
            u.AttackDur = shot || !u.S.Ranged ? u.S.AttackTime : 0.5f;
            float cd = shot || !u.S.Ranged ? u.S.Cooldown : 1.0f;
            u.Cooldown = cd * (0.85f + R() * 0.3f);
        }

        void Retarget(Unit u)
        {
            var enemies = teams[1 - u.Team];
            Unit best = null;
            float bestD = float.MaxValue;
            foreach (var e in enemies)
            {
                float dx = e.Pos.x - u.Pos.x, dz = e.Pos.z - u.Pos.z;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = e; }
            }
            if (best == null) { u.Target = null; return; }
            if (u.Target != null && u.Target.Alive)
            {
                float dx = u.Target.Pos.x - u.Pos.x, dz = u.Target.Pos.z - u.Pos.z;
                if (dx * dx + dz * dz < bestD * 1.5f) return; // не дёргаемся без нужды
            }
            u.Target = best;
        }

        void ResolveHit(Unit u)
        {
            var t = u.Target;
            if (t == null || !t.Alive) return;
            if (u.AttackIsShot)
            {
                Arrows.Fire(u, t);
                return;
            }
            Vector3 to = t.Pos - u.Pos; to.y = 0f;
            float d = to.magnitude;
            float contact = u.S.Radius + t.S.Radius + u.S.Reach;
            if (d > contact + 0.6f) return; // промах — цель отошла

            var s = u.S;
            float dmg = s.Ranged ? 7f : s.Damage;
            if (t.Type == UnitType.Knight) dmg *= s.VsCavalry;
            Vector3 dir = d > 1e-4f ? to / d : u.Forward;
            bool charge = s.Charge > 1f && u.ChargeT > 0.8f;
            if (charge)
            {
                dmg *= s.Charge;
                u.ChargeT = 0f;
                Trample(u, t);
            }
            Damage(t, dmg, dir * (charge ? 5f : 1.3f) / t.S.Mass, false, u);
        }

        /// <summary>Натиск рыцаря задевает и сбивает соседей цели.</summary>
        void Trample(Unit knight, Unit main)
        {
            Vector3 fwd = knight.Forward;
            foreach (var e in teams[1 - knight.Team])
            {
                if (e == main || !e.Alive) continue;
                Vector3 to = e.Pos - knight.Pos; to.y = 0f;
                float d = to.magnitude;
                if (d > 2.4f || Vector3.Dot(to, fwd) < 0f) continue;
                Damage(e, knight.S.Damage * 0.6f, (to / Mathf.Max(d, 0.1f) + fwd) * 2.5f / e.S.Mass, false, knight);
            }
        }

        public void Damage(Unit t, float amount, Vector3 knock, bool arrow, Unit src)
        {
            if (!t.Alive) return;
            amount *= 0.8f + R() * 0.4f;
            if (arrow) amount *= 1f - t.S.ArrowBlock;
            amount *= 1f - t.S.Armor;
            t.Hp -= amount;
            knock.y = 0f;
            t.Knock += knock;
            if (t.Hp <= 0f)
            {
                Kill(t, knock);
                return;
            }
            // Отвечаем обидчику, если он рядом.
            if (src != null && !arrow && t.Target != src && R() < 0.5f) t.Target = src;
        }

        void Kill(Unit u, Vector3 knock)
        {
            u.Alive = false;
            u.DeadT = 0f;
            u.Target = null;
            u.AttackT = -1f;
            u.Vel = Vector3.zero;
            if (u.View != null && u.View.Rig.Horse) u.FallSign = R() < 0.5f ? 1f : -1f;
            else u.FallSign = Vector3.Dot(knock, u.Forward) >= 0f ? 1f : -1f;
        }

        void Integrate(Unit u, float dt)
        {
            Vector3 v = u.Vel + u.Knock;
            u.Knock *= Mathf.Exp(-7f * dt);
            u.Pos += v * dt;
            float lim = WorldGen.FieldHalf - 0.5f;
            u.Pos.x = Mathf.Clamp(u.Pos.x, -lim, lim);
            u.Pos.z = Mathf.Clamp(u.Pos.z, -lim, lim);
            u.Pos.y = world.HeightAt(u.Pos.x, u.Pos.z);
            float speed = new Vector2(u.Vel.x, u.Vel.z).magnitude;
            u.Move = Mathf.Lerp(u.Move, Mathf.Clamp01(speed / u.S.Speed), 1f - Mathf.Exp(-10f * dt));
            u.Phase += dt * speed * (u.S.Charge > 1f ? 1.4f : 3.3f);
        }

        // ---------------------------------------------------------------- соседи

        int CellOf(Vector3 p)
        {
            int cx = Mathf.Clamp((int)((p.x + GridHalf) / GridCell), 0, gridDim - 1);
            int cz = Mathf.Clamp((int)((p.z + GridHalf) / GridCell), 0, gridDim - 1);
            return cz * gridDim + cx;
        }

        void BuildGrid()
        {
            for (int i = 0; i < head.Length; i++) head[i] = -1;
            if (next.Length < Units.Count)
            {
                next = new int[Units.Count * 2];
                push = new Vector3[Units.Count * 2];
            }
            for (int i = 0; i < Units.Count; i++)
            {
                if (!Units[i].Alive) continue;
                int c = CellOf(Units[i].Pos);
                next[i] = head[c];
                head[c] = i;
            }
        }

        void Separate()
        {
            for (int i = 0; i < Units.Count; i++)
            {
                push[i] = Vector3.zero;
                var u = Units[i];
                if (!u.Alive) continue;
                int cx = Mathf.Clamp((int)((u.Pos.x + GridHalf) / GridCell), 0, gridDim - 1);
                int cz = Mathf.Clamp((int)((u.Pos.z + GridHalf) / GridCell), 0, gridDim - 1);
                for (int z = Mathf.Max(cz - 1, 0); z <= Mathf.Min(cz + 1, gridDim - 1); z++)
                {
                    for (int x = Mathf.Max(cx - 1, 0); x <= Mathf.Min(cx + 1, gridDim - 1); x++)
                    {
                        for (int j = head[z * gridDim + x]; j >= 0; j = next[j])
                        {
                            if (j == i) continue;
                            var o = Units[j];
                            float dx = u.Pos.x - o.Pos.x, dz = u.Pos.z - o.Pos.z;
                            float min = u.S.Radius + o.S.Radius;
                            float d2 = dx * dx + dz * dz;
                            if (d2 >= min * min) continue;
                            float d = Mathf.Sqrt(d2);
                            Vector3 n = d > 1e-4f ? new Vector3(dx / d, 0f, dz / d) : new Vector3(Mathf.Cos(i), 0f, Mathf.Sin(i));
                            float share = o.S.Mass / (u.S.Mass + o.S.Mass);
                            push[i] += n * ((min - d) * share * 0.5f);
                        }
                    }
                }
            }
            float lim = WorldGen.FieldHalf - 0.5f;
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units[i];
                if (!u.Alive || push[i] == Vector3.zero) continue;
                u.Pos += Vector3.ClampMagnitude(push[i], 0.4f);
                u.Pos.x = Mathf.Clamp(u.Pos.x, -lim, lim);
                u.Pos.z = Mathf.Clamp(u.Pos.z, -lim, lim);
                u.Pos.y = world.HeightAt(u.Pos.x, u.Pos.z);
            }
        }

        /// <summary>Ближайший живой враг команды team рядом с точкой (для попадания стрел).</summary>
        public Unit EnemyNear(Vector3 p, int team, float extra)
        {
            int cx = Mathf.Clamp((int)((p.x + GridHalf) / GridCell), 0, gridDim - 1);
            int cz = Mathf.Clamp((int)((p.z + GridHalf) / GridCell), 0, gridDim - 1);
            Unit best = null;
            float bestD = float.MaxValue;
            for (int z = Mathf.Max(cz - 1, 0); z <= Mathf.Min(cz + 1, gridDim - 1); z++)
            {
                for (int x = Mathf.Max(cx - 1, 0); x <= Mathf.Min(cx + 1, gridDim - 1); x++)
                {
                    for (int j = head[z * gridDim + x]; j >= 0; j = next[j])
                    {
                        if (j >= Units.Count) continue;
                        var o = Units[j];
                        if (!o.Alive || o.Team == team) continue;
                        float dx = o.Pos.x - p.x, dz = o.Pos.z - p.z;
                        float r = o.S.Radius + extra;
                        float d2 = dx * dx + dz * dz;
                        if (d2 < r * r && d2 < bestD) { bestD = d2; best = o; }
                    }
                }
            }
            return best;
        }

        /// <summary>Центр живых армий (для кинокамеры).</summary>
        public bool Centroid(out Vector3 c)
        {
            c = Vector3.zero;
            int n = 0;
            foreach (var u in Units) if (u.Alive) { c += u.Pos; n++; }
            if (n == 0) return false;
            c /= n;
            return true;
        }
    }
}
