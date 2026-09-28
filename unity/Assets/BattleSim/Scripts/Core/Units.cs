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
        /// <summary>Гарнизон рубежа (верх подъёма, замок, край уступа): командиры его не переставляют — он держит своё место.</summary>
        public bool Garrison;
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
        // постоянная сетка мест: Cells[шеренга * CellFiles + колонна]; бреши закрывают задние, а не перераздача
        /// <summary>Сцепка строй-на-строй, в которой отряд держит линию фронта (или null — свободный бой).</summary>
        public Engagement Front;
        /// <summary>Боевой клич: до CryUntil отряд стоит и кричит, до RushUntil — разбег; PanicT — когда последний раз ловил волну паники.</summary>
        public float CryT = -99, CryUntil, RushUntil, PanicT = -99;
        public Unit[] Cells;
        public int CellFiles, CellRows;
        public bool CellMarch;
        public float LayoutT, CellShift;
        public int RowTick = -1;
        public V2[] RowP = new V2[0], RowF = new V2[0];
        public bool[] RowOk = new bool[0];
        /// <summary>Сдвиг от назначенной точки, чтобы не стоять на месте соседнего отряда.</summary>
        public V2 Offset;
        /// <summary>Стена щитов: сомкнуты до WallUntil (ShieldT — когда последний раз смыкали, для летописи).</summary>
        public bool ShieldWall;
        public float WallUntil, ShieldT = -99;
        /// <summary>Колдуны: отряд поднятых ими мертвецов (Raised) и когда колдовать снова; у поднятых — их хозяин (Master).</summary>
        public Squad Raised, Master;
        public float RaiseT = 3, RaiseLogT = -99;
        /// <summary>Ложное отступление: бегут понарошку до FeignUntil, FeignT — когда затевали последний раз.</summary>
        public bool Feigning;
        public float FeignUntil, FeignT = -99;
        /// <summary>Имя отряда вместо «мечники II» (богатырь — по имени).</summary>
        public string Title;

        public Squad(string id, int type, int team, float yaw)
        {
            Id = id; Type = type; T = Defs.All[type]; Team = team; Yaw = yaw;
            Special = T.Special != Core.Special.None;
        }

        public string Name
        {
            get
            {
                if (Title != null) return Title;
                string b = T.SquadName ?? T.Name.ToLowerInvariant();
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
        /// <summary>Сглаженная скорость (~0,4 с): по ней выбирается походка, чтобы шаг и бег не мельтешили от толкотни.</summary>
        public float Pace;
        public bool Shot, HitDone, Aiming, AtkNew, Engaged, DeathShown, LosOk, Gone;
        /// <summary>Реакция на удар для анимации: 1 — вздрогнул, 2 — принял удар на щит (показывается один раз).</summary>
        public int HitReact;
        /// <summary>Сколько ударов нанёс (у громил каждый третий — вихрем) и будет ли следующий вихрем.</summary>
        public int AtkCount;
        public bool SpinNext;
        public float HitAnimT;
        /// <summary>Стоит на своём месте в строю (гистерезис: встал ближе 0,2 м — стоит, пока не сдвинут дальше 0,6 м).</summary>
        public bool Settled;
        /// <summary>Сбит с ног натиском: лежит DownT секунд (DownAnim: 1 — упал, 2 — встаёт).</summary>
        public float DownT, LastImpactT = -9;
        public int DownAnim;
        /// <summary>Последний отброс (для полёта тела при гибели) и скорость летящего тела.</summary>
        public V3 LastKnock, Fly;
        public bool Flying;
        /// <summary>Колонна в строю (номер места в шеренге) и зерно «дрейфа» — строй не выглядит роботом.</summary>
        public int File;
        public float DriftSeed;
        /// <summary>Время боя, когда солдата последний раз ранили (для вспышки удара на экране).</summary>
        public float FlashT = -99;
        /// <summary>Поднят колдуном из павших (рассыпается, если колдуны гибнут); CastNew — колдун начинает поднимать мёртвых (анимация).</summary>
        public bool Risen, CastNew;
        /// <summary>Богатырь в поединке: с кем бьётся один на один (остальные расступаются и не лезут).</summary>
        public Unit Duel;
        public int Kills;
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
            // прошлое положение = текущее: иначе первый кадр новичок (гонец, полководец, поднятый) рисуется
            // на полпути от центра карты — мелькает и «пропадает»
            PrevPos = Pos; PrevYaw = Yaw;
            RetargetT = Rng.Rand() * 0.5f; Cooldown = Rng.Rand() * 0.8f;
            Phase = Rng.Rand() * 6;
            Scale = T.Special == Special.Commander ? 1.12f : T.Special == Special.Captain ? 1.06f : 0.95f + Rng.Rand() * 0.1f;
            Scale *= T.Scale * (1 + (Rng.Rand() - 0.5f) * 2 * (T.Special == Special.None ? T.ScaleJit : 0));
            if (T.Mount)
            {
                Horse = T.Special == Special.Commander ? 1 : T.Special != Special.None ? 0 : Variant % 2;
                if (T.Race != null && T.Race.HorseTint < 0.8f) Horse = 0; // у орды кони тёмные
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

    /// <summary>
    /// Сцепка двух пехотных отрядов: линия фронта (точка P, нормаль N от A к B), зазор между первыми шеренгами.
    /// Строи стоят друг против друга, рубятся первые шеренги парами, линия медленно поворачивается и смещается
    /// в сторону слабейшего — видно, кто кого теснит.
    /// </summary>
    public sealed class Engagement
    {
        public Squad A, B;
        public V2 P, N;
        public float Gap, PairT, LastFightT, Born;
    }

    /// <summary>Что случилось в бою — для эффектов на экране (пыль, искры, брызги).</summary>
    public enum FxKind { Hit, Block, Kill, Charge, Splash, BoltGround, Cry, Rout, Down, Explosion, Roar, Raise, Curse, Wall, Duel, Hero }

    public struct FxEvent
    {
        public FxKind Kind;
        public V3 Pos, Dir;
        public int Team;
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
        /// <summary>Бомба: рвётся там, где упала (радиус, отброс).</summary>
        public float AoeR, AoeKnock;
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
            // на скаку метят хуже
            float spread = (0.3f + d * 0.03f) * (glare ? 2.4f : 1) * (src.CurSpeed > 2 ? 1.5f : 1), ang = Rng.Rand() * M.PI * 2, rad = MathF.Sqrt(Rng.Rand()) * spread;
            var to = new V3(dst.Pos.x + dst.Vel.x * dur + MathF.Cos(ang) * rad + W.Wind.x * dur * 0.8f, 0,
                            dst.Pos.z + dst.Vel.z * dur + MathF.Sin(ang) * rad + W.Wind.z * dur * 0.8f);
            to.y = W.GroundAt(to.x, to.z) + 0.9f;
            if (src.T.AoeR > 0)
            { // бомба: навесом и медленнее, рвётся при падении
                List.Add(new Bolt { From = from, To = to, Age = 0, Dur = d / 16 + 0.45f, Arc = 1.5f + d * 0.16f, Team = src.Team, Dmg = src.T.AoeDmg, Flying = true, Pos = from, Dir = new V3(dx, 0, dz),
                    AoeR = src.T.AoeR, AoeKnock = src.T.AoeKnock });
                return;
            }
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
                        if (a.AoeR > 0) { battle.Explode(a.Pos, a.AoeR, a.Dmg, a.AoeKnock, a.Team); L.RemoveAt(i); continue; }
                        a.Flying = false; a.Stuck = 8; a.Dir = a.Dir.Normalized;
                        battle.Emit(W.HeightAt(a.Pos.x, a.Pos.z) < World.Water && a.Pos.y < World.Water + 0.3f ? FxKind.Splash : FxKind.BoltGround, a.Pos);
                        continue;
                    }
                    if (s < 1) continue;
                    if (a.AoeR > 0) { battle.Explode(a.To, a.AoeR, a.Dmg, a.AoeKnock, a.Team); L.RemoveAt(i); continue; }
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
