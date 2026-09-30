using System;
using System.Collections.Generic;

namespace BattleSim.Core
{
    // ------------------------------------------------------------------ окружение: дома, мелочи, ограды, деревья — то, что ломается и горит

    public enum EnvKind { House, Prop, Wall, Tree, Gate }
    public enum EnvMat { Wood, Stone }
    public enum EnvState { Intact, Damaged, Ruined }
    /// <summary>Чем бьют по окружению: взрыв, натиск конницы, топор и меч, огонь.</summary>
    public enum Harm { Blast, Charge, Blade, Fire }

    /// <summary>
    /// Предмет окружения: форма (круг или повёрнутый прямоугольник), материал, прочность и состояние.
    /// Пока цел — это препятствие (Obs): сквозь него не пройти, он закрывает видимость. Разрушен — препятствия нет:
    /// дом оседает в руины (по ним идут медленно, прячутся за обломками), ограда — в пролом, ящик — в щепки.
    /// </summary>
    public sealed class EnvObj
    {
        public int Id;
        public EnvKind Kind;
        public EnvMat Mat;
        public EnvState State;
        /// <summary>Центр, земля под ним, полуразмеры и поворот (для круга — радиус R), высота над землёй.</summary>
        public float X, Z, Y, Hx, Hz, Rot, R, Top;
        public bool Rect;
        /// <summary>Прочность; MaxHp 0 — не ломается (развалины, колодец-сруб из камня на площади и т. п.).</summary>
        public float Hp, MaxHp;
        public Obstacle Obs;
        /// <summary>Препятствие целого предмета и где он стоял — чтобы вернуть на реванш (бочку могло отшвырнуть).</summary>
        public Obstacle Home;
        public float HomeX, HomeZ, HomeY;
        public Building Building;
        public Prop Prop;
        public FeatureWall Wall;
        public ForestTree Tree;
        /// <summary>Ворота крепости: арка и заперты ли (запертые — препятствие поперёк проёма).</summary>
        public FortArch Arch;
        public bool Closed;
        /// <summary>Растёт при каждом видимом изменении (повреждён, рухнул) — Unity-слой перестраивает свой меш.</summary>
        public int Version;
        /// <summary>Чем ударили последний раз (от этого — как рушится: взрыв разбрасывает, огонь оставляет головешки).</summary>
        public Harm LastHarm;
        /// <summary>Огонь: сила пламени (0..1), сколько ещё гореть (с), выгорел дотла (дерево — чёрный ствол).</summary>
        public float Fire, Fuel;
        public bool Burnt;
        /// <summary>Гарнизон: чей отряд засел (или null) и кто внутри.</summary>
        public Squad Holder;
        public readonly List<Unit> Occupants = new List<Unit>();

        /// <summary>Точка у стены напротив (x, z), на out метров снаружи: дверь, окно, место у стены для штурма.</summary>
        public V2 Edge(float x, float z, float @out)
        {
            float c = MathF.Cos(Rot), s = MathF.Sin(Rot), dx = x - X, dz = z - Z;
            float lx = dx * c + dz * s, lz = -dx * s + dz * c;
            if (!Rect)
            {
                float l = M.Hypot(dx, dz);
                if (l < 1e-3f) { dx = 1; l = 1; }
                return new V2(X + dx / l * (R + @out), Z + dz / l * (R + @out));
            }
            // на ближайшую грань (или угол), затем наружу
            float ex = M.Clamp(lx, -Hx, Hx), ez = M.Clamp(lz, -Hz, Hz);
            if (MathF.Abs(lx) <= Hx && MathF.Abs(lz) <= Hz)
            { // точка внутри: на ближайшую грань
                if (Hx - MathF.Abs(lx) < Hz - MathF.Abs(lz)) ex = M.Sign(lx == 0 ? 1 : lx) * Hx; else ez = M.Sign(lz == 0 ? 1 : lz) * Hz;
            }
            float ox = lx - ex, oz = lz - ez, ol = M.Hypot(ox, oz);
            if (ol < 1e-3f) { if (MathF.Abs(ex) >= Hx) { ox = M.Sign(ex); oz = 0; } else { ox = 0; oz = M.Sign(ez == 0 ? 1 : ez); } ol = 1; }
            ex += ox / ol * @out; ez += oz / ol * @out;
            return new V2(X + ex * c - ez * s, Z + ex * s + ez * c);
        }

        public bool Breakable => MaxHp > 0 && State != EnvState.Ruined;
        /// <summary>Радиус описанного круга.</summary>
        public float Bound => Rect ? M.Hypot(Hx, Hz) : R;

        /// <summary>Расстояние от точки до края предмета (0 — внутри).</summary>
        public float Dist(float x, float z)
        {
            float dx = x - X, dz = z - Z;
            if (!Rect) return MathF.Max(0, M.Hypot(dx, dz) - R);
            float c = MathF.Cos(Rot), s = MathF.Sin(Rot);
            float lx = MathF.Abs(dx * c + dz * s) - Hx, lz = MathF.Abs(-dx * s + dz * c) - Hz;
            return M.Hypot(MathF.Max(0, lx), MathF.Max(0, lz));
        }
    }

    /// <summary>
    /// Всё окружение, которое можно сломать: реестр предметов, их прочность и что происходит, когда они рушатся.
    /// Сетка путей, препятствия, видимость и укрытия пересчитываются только там, где что-то изменилось.
    /// Unity-слой смотрит список Changed (и Version предметов), эффекты и звук — события FxKind.Collapse/Shatter/Crumble.
    /// </summary>
    public sealed class Env
    {
        public readonly List<EnvObj> All = new List<EnvObj>();
        /// <summary>Предметы, чей вид изменился; Unity-слой забирает и очищает.</summary>
        public readonly List<EnvObj> Changed = new List<EnvObj>();
        /// <summary>Сколько предметов уже разрушено за бой.</summary>
        public int Ruined;
        /// <summary>Что горит сейчас (и тлеет после того, как рухнуло).</summary>
        public readonly List<EnvObj> Burning = new List<EnvObj>();
        /// <summary>Сколько всего загоралось за бой.</summary>
        public int Ignited;
        float spreadT, heatT;
        readonly World w;
        readonly SpatialGrid<EnvObj> grid;
        readonly List<EnvObj> near = new List<EnvObj>();
        readonly List<EnvObj> rubble = new List<EnvObj>();

        public Env(World world)
        {
            w = world;
            grid = new SpatialGrid<EnvObj>(world.Field);
        }

        public EnvObj Add(EnvObj o)
        {
            o.Id = All.Count;
            o.Hp = o.MaxHp;
            o.Home = o.Obs;
            o.HomeX = o.X; o.HomeZ = o.Z; o.HomeY = o.Y;
            o.Fuel = FuelOf(o);
            All.Add(o);
            grid.Insert(o, o.X, o.Z, o.Bound);
            return o;
        }

        /// <summary>Предметы, чей край ближе r к точке (общий буфер — не хранить между вызовами).</summary>
        public List<EnvObj> Near(float x, float z, float r)
        {
            near.Clear();
            grid.Around(x, z, r, near);
            for (int i = near.Count - 1; i >= 0; i--)
                if (near[i].Dist(x, z) > r) near.RemoveAt(i);
            return near;
        }

        /// <summary>Точка на руинах дома — завал: идут медленно, прячутся за обломками.</summary>
        public bool RubbleAt(float x, float z)
        {
            foreach (var o in rubble)
                if (o.Dist(x, z) <= 0.3f) return true;
            return false;
        }

        /// <summary>Сколько урона материал берёт от удара: камень взрыв держит втрое лучше, а топор и натиск ему нипочём.</summary>
        public static float Factor(EnvObj o, Harm h)
        {
            if (o.Kind == EnvKind.Tree) return h == Harm.Fire ? 1 : h == Harm.Blast ? 0.1f : 0;
            if (o.Kind == EnvKind.Gate) return h == Harm.Blade ? 0.35f : h == Harm.Charge ? 0.3f : h == Harm.Blast ? 0.6f : 1; // окованные створки
            if (o.Mat == EnvMat.Stone) return h == Harm.Blast ? 0.35f : h == Harm.Blade ? 0.05f : 0;
            if (o.Kind == EnvKind.Wall && h == Harm.Charge) return 0.5f; // баррикада держит коня вдвое лучше ящика
            return 1;
        }

        /// <summary>Урон предмету. Возвращает true, если от этого удара он рухнул.</summary>
        public bool Hurt(EnvObj o, float amount, Harm h, Battle b = null)
        {
            if (!o.Breakable) return false;
            float dmg = amount * Factor(o, h);
            if (dmg <= 0) return false;
            float before = o.Hp;
            o.Hp -= dmg;
            o.LastHarm = h;
            if (o.Hp <= 0) { Ruin(o, b); return true; }
            if (o.State == EnvState.Intact && o.Hp < o.MaxHp * 0.5f)
            {
                o.State = EnvState.Damaged;
                Touch(o);
            }
            // от дома отлетают куски — раз на каждую десятую прочности
            if (o.Kind == EnvKind.House && (int)(before / o.MaxHp * 10) != (int)(o.Hp / o.MaxHp * 10))
                b?.Emit(FxKind.Crumble, new V3(o.X, o.Y + o.Top * 0.6f, o.Z));
            return false;
        }

        /// <summary>Всем предметам в радиусе взрыва — урон со спадом к краю; деревянное может загореться.</summary>
        public void Blast(V3 p, float r, float amount, Battle b)
        {
            var L = Near(p.x, p.z, r);
            for (int i = L.Count - 1; i >= 0; i--)
            {
                var o = L[i];
                if (p.y > o.Y + o.Top + 1 || p.y < o.Y - 3) continue;
                float fall = 1 - M.Clamp(o.Dist(p.x, p.z) / r, 0, 1) * 0.6f;
                if (o.Breakable) Hurt(o, amount * fall, Harm.Blast, b);
                if (Rng.Rand() < 0.2f * fall) Ignite(o, 0.25f, b);
                if (o.Kind == EnvKind.Prop && o.State != EnvState.Ruined)
                { // уцелевшую бочку или ящик отшвыривает
                    float dx = o.X - p.x, dz = o.Z - p.z, l = MathF.Max(0.2f, M.Hypot(dx, dz));
                    Shove(o, dx / l * 1.6f * fall, dz / l * 1.6f * fall);
                }
            }
        }

        // ------------------------------------------------------------ огонь

        /// <summary>Сколько секунд гореть в полную силу: дом — около минуты, ящик — несколько секунд.</summary>
        static float FuelOf(EnvObj o)
        {
            if (o.Mat != EnvMat.Wood) return 0;
            switch (o.Kind)
            {
                case EnvKind.House: return 50 + 4 * o.Hx * o.Hz * 0.5f;
                case EnvKind.Tree: return 35;
                case EnvKind.Wall: return 25;
                case EnvKind.Gate: return 60;
                default: return 12;
            }
        }

        /// <summary>За сколько секунд полного пламени сгорает целиком (дом рушится раньше, чем выгорит).</summary>
        static float BurnTime(EnvObj o) => o.Kind == EnvKind.House ? 40 : o.Kind == EnvKind.Gate ? 60 : o.Kind == EnvKind.Tree ? 30 : o.Kind == EnvKind.Wall ? 15 : 6;

        /// <summary>Поджечь: горит только дерево, и только если есть чему гореть.</summary>
        public bool Ignite(EnvObj o, float strength, Battle b = null)
        {
            if (o.Mat != EnvMat.Wood || o.Fuel <= 0 || o.MaxHp <= 0) return false;
            if (o.Fire > 0) { o.Fire = MathF.Max(o.Fire, strength); return false; }
            o.Fire = M.Clamp(strength, 0.05f, 1);
            Burning.Add(o);
            Ignited++;
            Touch(o);
            b?.Emit(FxKind.Ignite, new V3(o.X, o.Y + MathF.Min(o.Top, 3), o.Z));
            return true;
        }

        /// <summary>
        /// Огонь за шаг: пламя разгорается, съедает прочность (дом через ~40 с рушится и тлеет дальше), перекидывается
        /// на соседнее дерево — по ветру дальше, против ветра ближе. Раз в полсекунды размечается жар в сетке путей.
        /// </summary>
        public void Tick(float dt, Battle b)
        {
            if (Burning.Count == 0) return;
            for (int i = Burning.Count - 1; i >= 0; i--)
            {
                var o = Burning[i];
                if (o.Fuel > 0)
                {
                    o.Fire = MathF.Min(1, o.Fire + dt * (o.Kind == EnvKind.House ? 0.05f : 0.15f)); // дом разгорается ~15 с, ящик — за несколько
                    o.Fuel -= dt * o.Fire;
                    if (o.Breakable) Hurt(o, o.MaxHp / BurnTime(o) * o.Fire * dt, Harm.Fire, b);
                }
                else
                {
                    o.Fire -= dt * 0.12f;
                    if (o.Fire <= 0)
                    {
                        o.Fire = 0;
                        o.Burnt = true;
                        Burning.RemoveAt(i);
                        Touch(o);
                    }
                }
            }
            if ((spreadT -= dt) <= 0)
            {
                spreadT = 0.5f;
                var wind = w.Wind;
                for (int i = Burning.Count - 1; i >= 0; i--)
                {
                    var o = Burning[i];
                    if (o.Fire < 0.5f) continue;
                    float px = o.X + wind.x * 1.5f, pz = o.Z + wind.z * 1.5f, reach = 3.5f + M.Hypot(wind.x, wind.z) * 0.8f;
                    var L = Near(px, pz, o.Bound + reach);
                    for (int k = L.Count - 1; k >= 0; k--)
                    {
                        var n = L[k];
                        if (n == o || n.Fire > 0 || n.Fuel <= 0 || n.Mat != EnvMat.Wood) continue;
                        float d = MathF.Max(0, n.Dist(px, pz) - o.Bound * 0.5f);
                        if (Rng.Rand() < 0.35f * o.Fire * MathF.Max(0, 1 - d / reach)) Ignite(n, 0.15f, b);
                    }
                }
            }
            if ((heatT -= dt) <= 0)
            {
                heatT = 0.5f;
                w.Nav.SetHeat(Burning);
            }
        }

        /// <summary>
        /// Сколько дыма на прямой (a → b) выше земли: густой столб над горящим домом закрывает видимость,
        /// как чаща. 0 — чисто, от 1 — не видно.
        /// </summary>
        public float Smoke(float ax, float ay, float az, float bx, float by, float bz)
        {
            float sum = 0;
            foreach (var o in Burning)
            {
                if (o.Fire < 0.3f) continue;
                float dx = bx - ax, dz = bz - az, l2 = dx * dx + dz * dz;
                float t = l2 > 1e-6f ? M.Clamp(((o.X - ax) * dx + (o.Z - az) * dz) / l2, 0, 1) : 0;
                float x = ax + dx * t, z = az + dz * t, y = ay + (by - ay) * t;
                float r = o.Bound + 2.5f, d = M.Hypot(x - o.X, z - o.Z);
                if (d > r || y > o.Y + o.Top + 6 || y < o.Y - 1) continue;
                sum += o.Fire * (1 - d / r) * (o.Kind == EnvKind.House ? 1.6f : 0.8f);
            }
            return sum;
        }

        void Touch(EnvObj o)
        {
            o.Version++;
            if (!Changed.Contains(o)) Changed.Add(o);
        }

        /// <summary>Запереть или распахнуть ворота: запертые — препятствие поперёк проёма (в сетке путей — дорогой, но проходимый участок: ворота можно выбить).</summary>
        public void SetGate(EnvObj g, bool closed, Battle b = null)
        {
            if (g.Kind != EnvKind.Gate || g.State == EnvState.Ruined || g.Closed == closed) return;
            g.Closed = closed;
            if (closed) { g.Obs = w.Obs.AddRect(g.X, g.Z, g.Hx, g.Hz, g.Rot, g.Top, g.Y); g.Obs.Env = g; }
            else if (g.Obs != null) { w.Obs.Remove(g.Obs); g.Obs = null; }
            float rr = g.Bound + 0.5f;
            w.Nav.Refresh(w, g.X - rr, g.Z - rr, g.X + rr, g.Z + rr);
            Touch(g);
            b?.Emit(FxKind.Wall, new V3(g.X, g.Y + 1, g.Z));
        }

        /// <summary>
        /// Отшвырнуть мелочь (бочку, ящик) на (dx, dz) — от взрыва или конём: только туда, где ей есть место
        /// (не в стену, не в дом, не в воду).
        /// </summary>
        public void Shove(EnvObj o, float dx, float dz)
        {
            if (o.Kind != EnvKind.Prop || o.State == EnvState.Ruined || o.Obs == null) return;
            float nx = o.X + dx, nz = o.Z + dz;
            if (w.TooDeep(nx, nz) || w.OnDeck(nx, nz) != w.OnDeck(o.X, o.Z) || w.Nav.SpeedAt(nx, nz, 0) == 0) return;
            var hit = w.Obs.Hit(nx, nz, o.R);
            if (hit != null && hit != o.Obs) return;
            w.Obs.Remove(o.Obs);
            grid.Remove(o, o.X, o.Z, o.Bound);
            o.X = nx; o.Z = nz; o.Y = w.HeightAt(nx, nz);
            o.Obs.X = nx; o.Obs.Z = nz; o.Obs.Ground = o.Y;
            w.Obs.Restore(o.Obs);
            grid.Insert(o, o.X, o.Z, o.Bound);
            if (o.Prop != null) { o.Prop.X = nx; o.Prop.Z = nz; o.Prop.Y = o.Y; }
            Touch(o);
        }

        /// <summary>Реванш на той же карте: всё целое, как до боя.</summary>
        public void Restore()
        {
            bool any = false;
            foreach (var o in All)
            {
                if (o.Kind == EnvKind.Prop && (o.X != o.HomeX || o.Z != o.HomeZ))
                { // отшвырнутую бочку — на место
                    bool present = o.Obs != null;
                    if (present) w.Obs.Remove(o.Obs);
                    grid.Remove(o, o.X, o.Z, o.Bound);
                    o.X = o.HomeX; o.Z = o.HomeZ; o.Y = o.HomeY;
                    if (o.Home != null) { o.Home.X = o.X; o.Home.Z = o.Z; o.Home.Ground = o.Y; }
                    if (present) w.Obs.Restore(o.Obs);
                    grid.Insert(o, o.X, o.Z, o.Bound);
                    if (o.Prop != null) { o.Prop.X = o.X; o.Prop.Z = o.Z; o.Prop.Y = o.Y; }
                    Touch(o);
                }
                if (o.Kind == EnvKind.Gate && (o.Closed || o.Obs != null))
                { // ворота — распахнуты, как до боя
                    if (o.Obs != null) { w.Obs.Remove(o.Obs); o.Obs = null; }
                    o.Closed = false;
                    any = true;
                    Touch(o);
                }
                if (o.State == EnvState.Intact && o.Hp == o.MaxHp && o.Fire == 0 && !o.Burnt) continue;
                bool was = o.State == EnvState.Ruined;
                o.Hp = o.MaxHp;
                o.State = EnvState.Intact;
                if (was)
                {
                    if (o.Obs == null && o.Home != null) { w.Obs.Restore(o.Home); o.Obs = o.Home; }
                    if (o.Wall != null)
                    {
                        var wl = o.Wall;
                        wl.Broken = false;
                        w.WallGrid.Insert(wl, (wl.Ax + wl.Bx) / 2, (wl.Az + wl.Bz) / 2, M.Hypot(wl.Bx - wl.Ax, wl.Bz - wl.Az) / 2 + wl.T);
                    }
                    any = true;
                }
                Touch(o);
            }
            foreach (var o in All) { o.Fire = 0; o.Fuel = FuelOf(o); o.Burnt = false; o.Holder = null; o.Occupants.Clear(); }
            Burning.Clear();
            w.Nav.SetHeat(Burning);
            rubble.Clear();
            Ruined = 0;
            Ignited = 0;
            if (any) w.Nav.Refresh(w, -w.Field - 2, -w.Field - 2, w.Field + 2, w.Field + 2);
        }

        /// <summary>Предмет рухнул: убираем препятствие, пересчитываем пути и видимость вокруг.</summary>
        void Ruin(EnvObj o, Battle b)
        {
            o.Hp = 0;
            o.State = EnvState.Ruined;
            Ruined++;
            // сгоревшее дерево стоит чёрным стволом — препятствие остаётся
            if (o.Obs != null && o.Kind != EnvKind.Tree) { w.Obs.Remove(o.Obs); o.Obs = null; }
            var at = new V3(o.X, o.Y + 0.3f, o.Z);
            switch (o.Kind)
            {
                case EnvKind.House:
                    rubble.Add(o);
                    b?.Emit(FxKind.Collapse, at);
                    break;
                case EnvKind.Wall:
                    var wl = o.Wall;
                    wl.Broken = true;
                    w.WallGrid.Remove(wl, (wl.Ax + wl.Bx) / 2, (wl.Az + wl.Bz) / 2, M.Hypot(wl.Bx - wl.Ax, wl.Bz - wl.Az) / 2 + wl.T);
                    b?.Emit(FxKind.Shatter, at);
                    break;
                case EnvKind.Tree:
                    break;
                case EnvKind.Gate:
                    o.Closed = false;
                    b?.Emit(FxKind.Shatter, at);
                    break;
                default:
                    b?.Emit(FxKind.Shatter, at);
                    break;
            }
            float rr = o.Bound + 0.5f;
            w.Nav.Refresh(w, o.X - rr, o.Z - rr, o.X + rr, o.Z + rr);
            Touch(o);
        }
    }
}
