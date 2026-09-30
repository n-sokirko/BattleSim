using System;
using System.Collections.Generic;

namespace BattleSim.Core
{
    // ------------------------------------------------------------------ окружение: дома, мелочи, ограды, деревья — то, что ломается и горит

    public enum EnvKind { House, Prop, Wall, Tree }
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
        public Building Building;
        public Prop Prop;
        public FeatureWall Wall;
        public ForestTree Tree;
        /// <summary>Растёт при каждом видимом изменении (повреждён, рухнул) — Unity-слой перестраивает свой меш.</summary>
        public int Version;

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
            if (o.Mat == EnvMat.Stone) return h == Harm.Blast ? 0.35f : h == Harm.Blade ? 0.05f : 0;
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

        /// <summary>Всем предметам в радиусе взрыва — урон со спадом к краю.</summary>
        public void Blast(V3 p, float r, float amount, Battle b)
        {
            var L = Near(p.x, p.z, r);
            for (int i = L.Count - 1; i >= 0; i--)
            {
                var o = L[i];
                if (!o.Breakable || p.y > o.Y + o.Top + 1 || p.y < o.Y - 3) continue;
                float fall = 1 - M.Clamp(o.Dist(p.x, p.z) / r, 0, 1) * 0.6f;
                Hurt(o, amount * fall, Harm.Blast, b);
            }
        }

        void Touch(EnvObj o)
        {
            o.Version++;
            if (!Changed.Contains(o)) Changed.Add(o);
        }

        /// <summary>Предмет рухнул: убираем препятствие, пересчитываем пути и видимость вокруг.</summary>
        void Ruin(EnvObj o, Battle b)
        {
            o.Hp = 0;
            o.State = EnvState.Ruined;
            Ruined++;
            if (o.Obs != null) { w.Obs.Remove(o.Obs); o.Obs = null; }
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
