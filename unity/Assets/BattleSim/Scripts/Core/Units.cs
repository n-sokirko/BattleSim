using System;
using System.Collections.Generic;

namespace BattleSim.Core
{
    /// <summary>Состояние анимации солдата: имя клипа и время. Позы берутся из запечённой текстуры (Unity-слой).</summary>
    public sealed class AnimState
    {
        public string Name;
        public float T, Speed = 1, Blend;
        public bool Once;
        public int PrevRow, LastRow;

        public AnimState(string name) { Name = name; T = Rng.Rand() * 10; }

        public void Play(string name, bool once = false, float speed = 1f, bool restart = false)
        {
            if (Name == name && !restart) { Speed = speed; return; }
            PrevRow = LastRow; Blend = 1;
            Name = name; T = once ? 0 : Rng.Rand() * 10; Speed = speed; Once = once;
        }

        public void Step(float dt)
        {
            T += dt * Speed;
            if (Blend > 0) Blend = MathF.Max(0, Blend - dt / 0.14f);
        }
    }

    /// <summary>Место солдата в плане расстановки (переживает реванш и смену карты).</summary>
    public sealed class UnitPlan
    {
        public int Type, Team, Variant, Squad = -1;
        public float X, Z, Yaw, Ox, Oz;
    }

    public sealed class PathCache { public int Cls; public float Gx, Gz, T; public List<V2> Path; }

    /// <summary>Отряд: у него общий приказ, боевой дух и точки строя.</summary>
    public sealed class Squad
    {
        public string Id;
        public int Type, Team, Size, Alive, Num;
        public UnitDef T;
        public float Yaw;
        public List<Unit> Units = new List<Unit>();
        public V3 Center;
        public Order Order = new Order(OrderKind.Advance, Mode.Advance);
        public Order Pending;
        public float OrderT, Morale = 100, LastHitT = -99, LastShotT = -99, FirstStrikeT = -99, LastActiveT, EngagedFor, NextDecision, LabelT;
        public bool Engaged, Hidden, Special, Reserve;
        public List<Squad> Foes = new List<Squad>();
        public float FoeDist = float.PositiveInfinity;
        public Wing Wing;
        public PathCache PathCache;
        // строй: точка отряда, её путь и след, ширина строя
        public bool FormInit, FormMarch, AnchorArrived;
        public V2 Anchor, AnchorVel, APathGoal;
        public float Facing, FormT, Lag, APathT;
        public int Files, Rows, APathI;
        public List<V2> APath;
        public readonly List<V2> Trail = new List<V2>();
        // очередь у узкого места: ждём, пока свой отряд впереди не втянется в проход
        public Choke Choke;
        public bool ChokeGo;
        public V2 ChokeE, ChokeDir, AvoidP;
        public float ChokeD, ChokeHold, ChokeWaitT, ChokeGoT, AvoidUntil, LagT;
        // места шеренг на следе колонны: считаются один раз за шаг на шеренгу, а не на каждого солдата
        // заявка на путь точки отряда: считается понемногу за несколько шагов
        public bool APathQueued;
        public V2 APathWant;
        public int RowTick = -1;
        public V2[] RowP = new V2[0], RowF = new V2[0];
        public bool[] RowOk = new bool[0];
        /// <summary>Сдвиг от назначенной точки, чтобы не стоять на месте соседнего отряда.</summary>
        public V2 Offset;

        public Squad(string id, int type, int team, float yaw)
        {
            Id = id; Type = type; T = Defs.All[type]; Team = team; Yaw = yaw;
            Special = T.Special != Core.Special.None;
        }

        public string Name
        {
            get
            {
                string b = Type < Defs.SquadName.Length ? Defs.SquadName[Type] : T.Name.ToLowerInvariant();
                return Num > 0 ? b + " " + Defs.Roman(Num) : b;
            }
        }

        public V2 C => new V2(Center.x, Center.z);

        public void Refresh(float dt)
        {
            int n = 0;
            bool eng = false;
            float x = 0, y = 0, z = 0;
            foreach (var u in Units)
                if (u.Alive) { x += u.Pos.x; y += u.Pos.y; z += u.Pos.z; n++; if (u.Engaged) eng = true; }
            Alive = n;
            Center = n > 0 ? new V3(x / n, y / n, z / n) : new V3(0, 0, 0);
            Engaged = eng;
            EngagedFor = eng ? EngagedFor + dt : 0;
            LabelT -= dt;
        }

        /// <summary>Место солдата в строю вокруг точки приказа.</summary>
        public V2 Slot(Unit u)
        {
            var o = Order;
            float fy = o.HasFace ? o.Face : Yaw, c = MathF.Cos(fy), s = MathF.Sin(fy);
            float gx = o.HasPos ? o.X : Center.x, gz = o.HasPos ? o.Z : Center.z;
            return new V2(gx + u.Ox * c + u.Oz * s, gz - u.Ox * s + u.Oz * c);
        }
    }

    public sealed class Unit
    {
        public UnitPlan Plan;
        public int Type, Team, Variant, Horse;
        public UnitDef T;
        public float Ox, Oz;
        public V3 Pos, Vel, Knock;
        /// <summary>Положение и поворот на начало шага — для плавной отрисовки между шагами расчёта.</summary>
        public V3 PrevPos;
        public float PrevYaw;
        public V3 RenderPos(float a) => new V3(PrevPos.x + (Pos.x - PrevPos.x) * a, PrevPos.y + (Pos.y - PrevPos.y) * a, PrevPos.z + (Pos.z - PrevPos.z) * a);
        public float RenderYaw(float a) => PrevYaw + M.WrapAngle(Yaw - PrevYaw) * a;
        public float Yaw, Hp;
        public bool Alive = true;
        public Unit Target, FleeFrom;
        public float RetargetT, Cooldown, AtkT = -1, AtkDur = 0.6f, ChargeT, DeadT, Phase, CurSpeed, LosT = -9, Scale;
        public bool Shot, HitDone, Aiming, AtkNew, Engaged, DeathShown, LosOk, Gone;
        public Unit LosTarget;
        public Carry Carry;
        public Squad Squad;
        public AnimState Anim, Ride;
        public Commander Cmd;
        // путь
        public float NavT, NavGX = float.NaN, NavGZ = float.NaN;
        public List<V2> Path;
        public int PathI;
        // застрял и не может дойти до цели — на время забываем этот отряд
        public float StuckT, IgnoreT;
        public Squad IgnoreSquad;
        // место в строю: шеренга, смещение вбок и назад от точки отряда
        public int Row = -1;
        public float SlotLat, SlotBack;

        public Unit(UnitPlan plan, World world)
        {
            Plan = plan;
            Type = plan.Type; T = Defs.All[plan.Type]; Team = plan.Team; Variant = plan.Variant;
            Ox = plan.Ox; Oz = plan.Oz;
            Pos = new V3(plan.X, world.GroundAt(plan.X, plan.Z), plan.Z);
            Yaw = plan.Yaw; Hp = T.Hp;
            RetargetT = Rng.Rand() * 0.5f; Cooldown = Rng.Rand() * 0.8f;
            Phase = Rng.Rand() * 6;
            Scale = T.Special == Special.Commander ? 1.12f : T.Special == Special.Captain ? 1.06f : 0.95f + Rng.Rand() * 0.1f;
            if (T.Mount)
            {
                Horse = T.Special == Special.Commander ? 1 : T.Special != Special.None ? 0 : Variant % 2;
                Anim = new AnimState("Idle");
                Ride = new AnimState("ride");
            }
            else Anim = new AnimState(T.Anim.Idle);
        }

        public bool IsLeader => T.Special == Special.Commander || T.Special == Special.Captain;
        public V2 P => new V2(Pos.x, Pos.z);
        public V2 Forward => new V2(MathF.Sin(Yaw), MathF.Cos(Yaw));

        public void Face(float dx, float dz, float dt, float rate = 9.5f)
        {
            if (dx * dx + dz * dz < 1e-6f) return;
            float d = M.WrapAngle(MathF.Atan2(dx, dz) - Yaw);
            float step = rate * dt;
            Yaw += MathF.Abs(d) <= step ? d : M.Sign(d) * step;
        }
    }

    /// <summary>Узкое место (мост, брод, проём, ворота, тропа): отряды одной стороны входят в него по очереди.</summary>
    public sealed class Choke
    {
        public V2 E, Dir;
        public int Team;
        public readonly List<Squad> Queue = new List<Squad>();
    }

    public sealed class LogEntry
    {
        public float T;
        public int Team;
        public string Text;
    }

    // ------------------------------------------------------------------ арбалетные болты

    public sealed class Bolt
    {
        public V3 From, To, Pos, Dir;
        public float Age, Dur, Arc, Dmg, Stuck;
        public int Team;
        public bool Flying = true;
    }

    public sealed class Bolts
    {
        readonly Battle battle;
        public int Cap = 1600;
        public readonly List<Bolt> List = new List<Bolt>();

        public Bolts(Battle b) { battle = b; }

        public void Fire(Unit src, Unit dst)
        {
            if (List.Count >= Cap)
            {
                int i = List.FindIndex(a => !a.Flying);
                if (i < 0) return;
                List.RemoveAt(i);
            }
            var B = battle;
            var W = B.World;
            var f = src.Forward;
            var from = new V3(src.Pos.x + f.x * 0.6f, src.Pos.y + 1.3f, src.Pos.z + f.z * 0.6f);
            float dx = dst.Pos.x - src.Pos.x, dz = dst.Pos.z - src.Pos.z, d = M.Hypot(dx, dz);
            if (d == 0) d = 1;
            float dur = d / 42 + 0.22f;
            bool glare = B.Glare(dx / d, dz / d);
            if (glare && !B.GlareLogged[src.Team])
            {
                B.GlareLogged[src.Team] = true;
                B.AddLog(src.Team, "Низкое солнце бьёт арбалетчикам в глаза — болты уходят мимо");
            }
            float spread = (0.3f + d * 0.03f) * (glare ? 2.4f : 1), ang = Rng.Rand() * M.PI * 2, rad = MathF.Sqrt(Rng.Rand()) * spread;
            var to = new V3(dst.Pos.x + dst.Vel.x * dur + MathF.Cos(ang) * rad + W.Wind.x * dur * 0.8f, 0,
                            dst.Pos.z + dst.Vel.z * dur + MathF.Sin(ang) * rad + W.Wind.z * dur * 0.8f);
            to.y = W.GroundAt(to.x, to.z) + 0.9f;
            List.Add(new Bolt { From = from, To = to, Age = 0, Dur = dur, Arc = 0.4f + d * 0.07f, Team = src.Team, Dmg = src.T.Dmg, Flying = true, Pos = from, Dir = new V3(dx, 0, dz) });
        }

        public void Tick(float dt)
        {
            var W = battle.World;
            var L = List;
            for (int i = L.Count - 1; i >= 0; i--)
            {
                var a = L[i];
                if (a.Flying)
                {
                    a.Age += dt;
                    float s = MathF.Min(1, a.Age / a.Dur);
                    a.Pos = V3.Lerp(a.From, a.To, s);
                    a.Pos.y += a.Arc * 4 * s * (1 - s);
                    a.Dir = (a.To - a.From) * (1f / a.Dur);
                    a.Dir.y += a.Arc * 4 * (1 - 2 * s) / a.Dur;
                    // Болт врезается в склон, ограду или стену — так и работает укрытие
                    float gy = W.GroundAt(a.Pos.x, a.Pos.z);
                    bool inTrees = W.Type == MapType.Forest && a.Pos.y < gy + 7 && W.Nav.CanopyAt(a.Pos.x, a.Pos.z) && Rng.Rand() < dt * 2.2f;
                    if (s > 0.08f && s < 1 && (a.Pos.y < gy + 0.05f || W.WallTop(a.Pos.x, a.Pos.z) > a.Pos.y || W.Obs.Blocks(a.Pos.x, a.Pos.z, a.Pos.y)
                        || W.Decks.SolidTop(a.Pos.x, a.Pos.z, W.HeightAt(a.Pos.x, a.Pos.z)) > a.Pos.y || inTrees))
                    {
                        a.Flying = false; a.Stuck = 8; a.Dir = a.Dir.Normalized;
                        continue;
                    }
                    if (s < 1) continue;
                    var hit = battle.EnemyNear(a.To.x, a.To.z, a.Team, 0.35f);
                    if (hit != null)
                    {
                        float l = M.Hypot(a.Dir.x, a.Dir.z);
                        if (l == 0) l = 1;
                        bool back = MathF.Sin(hit.Yaw) * a.Dir.x / l + MathF.Cos(hit.Yaw) * a.Dir.z / l > 0.35f;
                        battle.Damage(hit, a.Dmg, a.Dir.x / l * 0.6f / hit.T.Mass, a.Dir.z / l * 0.6f / hit.T.Mass, true, null, back);
                        L.RemoveAt(i);
                        continue;
                    }
                    a.Flying = false; a.Stuck = 8;
                    float g = W.GroundAt(a.To.x, a.To.z);
                    a.Dir = a.Dir.Normalized;
                    float down = MathF.Max(0.3f, -a.Dir.y);
                    a.Pos = a.To + a.Dir * (MathF.Max(0, a.To.y - g) / down - 0.25f);
                }
                else if ((a.Stuck -= dt) <= 0) L.RemoveAt(i);
            }
        }

        public void Clear() => List.Clear();
    }
}
