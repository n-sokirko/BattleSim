using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleSim.Core
{
    // ------------------------------------------------------------------ горы

    /// <summary>Гора: скальное ядро (не пройти) и пологое предгорье вокруг; или холм — пологий целиком.</summary>
    public sealed class Peak
    {
        public float X, Z, R, H, Foot, Seed;
        /// <summary>Холм: пологий, на вершину можно подняться (высота для стрелков и полководцев).</summary>
        public bool Hill;
        /// <summary>Доля радиуса, где начинается скальное ядро.</summary>
        public const float Core = 0.55f;
        public float CoreR => R * Core;
    }

    /// <summary>Проход между горами: ось от южного устья к северному и ширина дна.</summary>
    public sealed class Pass
    {
        public V2 A, B;
        public float W;
    }

    /// <summary>
    /// Горы: поперёк поля — цепь скальных вершин, между ними узкие ущелья-проходы (6–12 м по дну);
    /// впереди и позади цепи — отдельные вершины и пологие холмы (высоты для стрелков). На сами скалы
    /// не забраться, предгорья и холмы проходимы: бой идёт в проходах и на склонах.
    /// </summary>
    public sealed class MountainPlan
    {
        public readonly List<Peak> Peaks = new List<Peak>();
        public readonly List<Pass> Passes = new List<Pass>();

        public static MountainPlan Make(Rng R, float F)
        {
            var m = new MountainPlan();
            // цепь: слева направо, между соседними ядрами — проход заданной ширины
            float x = -F * 1.05f;
            Peak prev = null;
            while (x < F * 1.05f)
            {
                float r = M.Lerp(20, 30, R.F()) * MathF.Min(1.3f, F / 100);
                var p = new Peak { R = r, H = M.Lerp(18, 30, R.F()), Foot = M.Lerp(3.5f, 6, R.F()), Seed = R.F() * 100 };
                float gap = M.Lerp(7, 12, R.F());
                p.X = prev == null ? x : prev.X + prev.CoreR + gap + p.CoreR;
                p.Z = M.Lerp(-0.1f, 0.1f, R.F()) * F;
                if (prev != null)
                {
                    // ось прохода — через середину промежутка между ядрами, поперёк цепи
                    float mx = prev.X + prev.CoreR + gap / 2, mz = (prev.Z + p.Z) / 2, L = MathF.Max(prev.R, p.R) + 8;
                    m.Passes.Add(new Pass { A = new V2(mx, mz - L), B = new V2(mx, mz + L), W = gap });
                }
                m.Peaks.Add(p);
                prev = p;
                x = p.X + p.CoreR;
            }
            // ущелья у самой кромки поля бесполезны (туда не дойти) — оставляем только внутренние
            m.Passes.RemoveAll(q => MathF.Abs(q.A.x) > F - 10);
            // одиночные вершины перед цепью и за ней — не на линии проходов, чтобы не затыкать устья
            int lone = F > 100 ? 4 : 2;
            for (int k = 0, tries = 0; k < lone && tries < 60; tries++)
            {
                float side = k % 2 == 0 ? -1 : 1;
                var p = new Peak { R = M.Lerp(14, 20, R.F()), H = M.Lerp(12, 20, R.F()), Foot = M.Lerp(2.5f, 4, R.F()), Seed = R.F() * 100 };
                p.X = M.Lerp(-F * 0.8f, F * 0.8f, R.F()); p.Z = side * M.Lerp(0.34f, 0.46f, R.F()) * F;
                bool ok = true;
                foreach (var q in m.Passes) if (MathF.Abs(q.A.x - p.X) < p.CoreR + q.W + 10) ok = false;
                foreach (var q in m.Peaks) if (M.Hypot(q.X - p.X, q.Z - p.Z) < q.R + p.R * 0.8f) ok = false;
                if (!ok) continue;
                m.Peaks.Add(p); k++;
            }
            // пологие холмы у армий — на них встают стрелки и полководцы
            int hills = F > 100 ? 6 : 4;
            for (int k = 0, tries = 0; k < hills && tries < 80; tries++)
            {
                float side = k % 2 == 0 ? -1 : 1;
                var p = new Peak { Hill = true, R = M.Lerp(16, 26, R.F()), H = M.Lerp(4, 8, R.F()), Seed = R.F() * 100 };
                p.X = M.Lerp(-F * 0.85f, F * 0.85f, R.F()); p.Z = side * M.Lerp(0.3f, 0.55f, R.F()) * F;
                bool ok = true;
                foreach (var q in m.Peaks) if (M.Hypot(q.X - p.X, q.Z - p.Z) < (q.Hill ? q.R : q.CoreR) + p.R * 0.6f) ok = false;
                if (!ok) continue;
                m.Peaks.Add(p); k++;
            }
            return m;
        }
    }

    // ------------------------------------------------------------------ город

    /// <summary>Вид постройки города: размеры модели (ширина, глубина, высота) до масштабирования.</summary>
    public sealed class CityDef
    {
        public string Kind, Name;
        public float W, D, H;
        public int Index;
    }

    public sealed class Building
    {
        public CityDef Def;
        public float X, Z, Rot, S, Hx, Hz, Top, Y;
        public EnvObj Env;
    }

    public sealed class Prop
    {
        public CityDef Def;
        public float X, Z, Rot, S, Y;
        public EnvObj Env;
    }

    /// <summary>Каменная ограда, завал в проломе или баррикада: через неё перелезают.</summary>
    public sealed class FeatureWall
    {
        public float Ax, Az, Bx, Bz, H, T;
        /// <summary>Сплошная ограда: сквозь неё не пройти, только через проёмы. Завал (false) — перелезают медленно.</summary>
        public bool Solid = true;
        /// <summary>Деревянная баррикада (иначе — каменная ограда): горит и ломается легче.</summary>
        public bool Wood;
        /// <summary>Проломлена: больше не преграда (осталась осыпь камней или обломки).</summary>
        public bool Broken;
        public EnvObj Env;
    }

    public sealed class Seg
    {
        public float Ax, Az, Bx, Bz, W;
        public Seg Copy() => (Seg)MemberwiseClone();
    }

    public sealed class Rect4
    {
        public float X0, X1, Z0, Z1;
        public Rect4(float x0, float x1, float z0, float z1) { X0 = x0; X1 = x1; Z0 = z0; Z1 = z1; }
    }

    /// <summary>Крепостная стена; Top — высота боевого хода (абсолютная) через каждый метр от A к B, её строит мир.</summary>
    public sealed class FortWall
    {
        public float Ax, Az, Bx, Bz, W, H; public V2 Out; public bool Citadel;
        public float[] Top;
        /// <summary>Высота боевого хода в t метрах от A.</summary>
        public float TopAt(float t) => M.Sample(Top, t / M.Hypot(Bx - Ax, Bz - Az));
    }
    public sealed class FortTower { public float X, Z, S, H; public bool Citadel, Keep; }
    /// <summary>Лестница на боевой ход; YA, YB — высоты концов (абсолютные), их задаёт мир по стене.</summary>
    public sealed class FortRamp
    {
        public float Ax, Az, Bx, Bz, W, H, YA, YB; public bool Citadel;
        /// <summary>Всход по боевому ходу на крышу донжона (а не с земли на стену).</summary>
        public bool Keep;
        /// <summary>Верх лестницы в доле f длины: подъём от YA, последние 2,5 м — площадка вровень с боевым ходом (сойти на него вбок).</summary>
        public float TopAt(float f)
        {
            float len = M.Hypot(Bx - Ax, Bz - Az), flat = MathF.Min(2.5f, len * 0.25f), along = M.Clamp01(f) * len;
            return along >= len - flat ? YB : M.Lerp(YA, YB, along / MathF.Max(0.01f, len - flat));
        }
    }
    public sealed class FortArch { public float X, Z, Ux, Uz, H, W; public bool Citadel; }

    public sealed class Fort
    {
        public List<FortWall> Walls = new List<FortWall>();
        public List<FortTower> Towers = new List<FortTower>();
        public List<FortRamp> Ramps = new List<FortRamp>();
        public List<FortArch> Arches = new List<FortArch>();
    }

    /// <summary>Подъём: улица, идущая вверх через уступ — от A (уровень Low) до B (уровень High), шириной W.</summary>
    public sealed class Climb { public float Ax, Az, Bx, Bz, W, Low, High; }

    /// <summary>Река через нижний посад: русло (ломаная с запада на восток), мосты на улицах и брод у стены.</summary>
    public sealed class RiverPlan
    {
        public List<V2> Pts = new List<V2>();
        public float W, Bed;
        public List<Seg> Bridges = new List<Seg>();
        public V2 Ford;

        /// <summary>Середина русла на долготе x.</summary>
        public float ZAt(float x)
        {
            for (int i = 1; i < Pts.Count; i++)
                if (x <= Pts[i].x) { float t = (x - Pts[i - 1].x) / (Pts[i].x - Pts[i - 1].x); return M.Lerp(Pts[i - 1].z, Pts[i].z, M.Clamp(t, 0, 1)); }
            return Pts[Pts.Count - 1].z;
        }
    }

    /// <summary>Замок на скале: стены [X0..X1]×[Z0..Z1], скала [MX0..MX1]×[MZ0..MZ1], ров перед воротами, донжон.</summary>
    public sealed class Castle
    {
        public float X0, X1, Z0, Z1, MX0, MX1, MZ0, MZ1, WallH, GateX, KeepS, KeepH;
        public Rect4 Moat;
        public V2 Keep;
        public Seg KeepRamp;
    }

    public sealed class Square { public float X, Z, Hx, Hz; }

    /// <summary>
    /// Город уступами: нижний посад за южной стеной, через него река с мостами и бродом;
    /// отвесный уступ с тремя улицами-подъёмами в верхний город; над ним замок на скале —
    /// насыпь, мост через ров, надвратные башни, двор, стены с боевым ходом и донжон.
    /// </summary>
    public sealed class CityPlan
    {
        public float CX, CZ, T, H, TX, TZ;
        public List<Seg> Streets = new List<Seg>();
        public List<Square> Squares = new List<Square>();
        public List<Building> Buildings = new List<Building>();
        public List<Prop> Props = new List<Prop>();
        public List<FeatureWall> Walls = new List<FeatureWall>();
        public List<Rect4> Gardens = new List<Rect4>();
        public Fort Fort = new Fort();
        /// <summary>Уровни уступов над нижним посадом и дно рва.</summary>
        public float L1, L2, MoatBottom;
        /// <summary>Край уступа между посадом и верхним городом: z = ZA + AmpA·sin(x·FreqA + PhaseA).</summary>
        public float ZA, AmpA, FreqA, PhaseA;
        public List<Climb> Climbs = new List<Climb>();
        public RiverPlan River;
        public Castle Castle;
        public Seg CastleBridge;
        public int DebugBlocks, DebugPlaced;
        /// <summary>Входы в город с юга (ворота и пролом) — сюда идёт штурм.</summary>
        public List<V2> SouthEntries = new List<V2>();

        /// <summary>Модели города по видам (порядок важен: от него зависит раскладка кварталов при том же сиде).</summary>
        public static readonly (string kind, string name)[] Models =
        {
            ("home", "home_A_red"), ("home", "home_A_blue"), ("home", "home_A_yellow"), ("home", "home_A_green"),
            ("home", "home_B_red"), ("home", "home_B_blue"), ("home", "home_B_yellow"), ("home", "home_B_green"),
            ("church", "church"), ("tavern", "tavern"), ("blacksmith", "blacksmith"), ("market", "market"), ("well", "well"),
            ("tower_B", "tower_B"), ("destroyed", "destroyed"),
            ("prop", "barrel"), ("prop", "crate_A_big"), ("prop", "crate_B_small"), ("prop", "crate_long_A"), ("prop", "wheelbarrow"), ("prop", "sack"),
        };

        /// <summary>Высота модели в метрах для каждого вида постройки.</summary>
        public static readonly Dictionary<string, float> Heights = new Dictionary<string, float>
        {
            ["home"] = 8.2f, ["church"] = 17, ["tavern"] = 9.5f, ["blacksmith"] = 8.5f, ["market"] = 7, ["tower_B"] = 15,
            ["destroyed"] = 7, ["well"] = 3.2f, ["prop"] = 1.1f,
        };

        static bool Overlaps(Rect4 r, Rect4 o, float m) => r.X0 < o.X1 + m && r.X1 > o.X0 - m && r.Z0 < o.Z1 + m && r.Z1 > o.Z0 - m;

        sealed class Side
        {
            public V2 A, B, Out;
            public List<float> Gates = new List<float>();
        }

        /// <summary>Край уступа: высота города подскакивает при переходе через край (ZA + волна по x).</summary>
        public float EdgeZ(float x) => ZA + AmpA * MathF.Sin(x * FreqA + PhaseA);

        /// <summary>
        /// Подъём уступов над уровнем нижнего посада в точке (без шума рельефа): посад — 0,
        /// верхний город — L1, скала замка — L2; улицы-подъёмы — плавный пандус; ров — глубоко вниз.
        /// </summary>
        public float Lift(float x, float z)
        {
            float l = L1 * M.Smooth(-0.9f, 0.9f, z - EdgeZ(x));
            var c = Castle;
            if (c != null)
            {
                float dx = MathF.Max(MathF.Max(c.MX0 - x, 0), x - c.MX1), dz = MathF.Max(MathF.Max(c.MZ0 - z, 0), z - c.MZ1);
                float k = M.Smooth(1.1f, 0f, M.Hypot(dx, dz));
                if (k > 0) l = M.Lerp(l, L2, k);
            }
            foreach (var cl in Climbs)
            {
                float len = M.Hypot(cl.Bx - cl.Ax, cl.Bz - cl.Az), ux = (cl.Bx - cl.Ax) / len, uz = (cl.Bz - cl.Az) / len;
                float along = (x - cl.Ax) * ux + (z - cl.Az) * uz, lat = MathF.Abs(-(x - cl.Ax) * uz + (z - cl.Az) * ux);
                if (along < -3 || along > len + 3 || lat > cl.W / 2 + 0.9f) continue;
                float ramp = cl.Low + (cl.High - cl.Low) * M.Clamp(along / len, 0, 1);
                float k = M.Smooth(cl.W / 2 + 0.9f, cl.W / 2, lat) * M.Smooth(-3, -1, along) * M.Smooth(len + 3, len + 1, along);
                l = M.Lerp(l, ramp, k);
            }
            if (c != null)
            {
                var m = c.Moat;
                float dx = MathF.Max(MathF.Max(m.X0 - x, 0), x - m.X1), dz = MathF.Max(MathF.Max(m.Z0 - z, 0), z - m.Z1);
                float k = M.Smooth(1.0f, 0f, M.Hypot(dx, dz));
                if (k > 0) l = M.Lerp(l, MoatBottom, k);
            }
            return l;
        }

        public static CityPlan Make(Rng R, IList<CityDef> defs, float FIELD)
        {
            float CX = FIELD * 0.8f, CZ = FIELD * 0.6f, T = 3.2f, H = 7;
            var city = new CityPlan { CX = CX, CZ = CZ, T = T, H = H, TX = CX, TZ = CZ };
            var F = city.Fort;
            float s = CZ / 48f; // масштаб планировки (большая карта — просторнее)

            // --- ворота внешней стены и главные улицы
            float gx = M.Lerp(-CX * 0.12f, CX * 0.12f, R.F());
            float g2 = gx + (gx >= 0 ? -1 : 1) * CX * M.Lerp(0.48f, 0.58f, R.F());
            float x3 = gx - (g2 - gx) * M.Lerp(0.8f, 1f, R.F()); // третья улица — по другую сторону от главной
            x3 = M.Clamp(x3, -CX + 14, CX - 14);

            // --- уступы: нижний посад (с рекой), верхний город, скала замка
            city.L1 = 6; city.L2 = 12.5f; city.MoatBottom = 2f;
            city.ZA = -2 * s; city.AmpA = M.Lerp(1.5f, 3f, R.F()); city.FreqA = M.Lerp(0.035f, 0.06f, R.F()); city.PhaseA = R.F() * 6.28f;
            float castleD = 20 * s, castleHX = MathF.Min(CX * 0.42f, 30 * s);
            float ccx = M.Clamp(gx, -CX + castleHX + 12, CX - castleHX - 12);
            var cas = city.Castle = new Castle { X0 = ccx - castleHX, X1 = ccx + castleHX, Z1 = CZ - 2.5f, WallH = 8, GateX = ccx };
            cas.Z0 = cas.Z1 - castleD;
            cas.MX0 = cas.X0 - 5; cas.MX1 = cas.X1 + 5; cas.MZ0 = cas.Z0 - 2; cas.MZ1 = CZ + 8;
            cas.Moat = new Rect4(cas.MX0, cas.MX1, cas.MZ0 - 6.5f, cas.MZ0);
            // главная улица идёт от южных ворот прямо к замку
            gx = ccx;
            // подъёмы с посада в верхний город — на трёх улицах; к замку — насыпь до рва, дальше мост
            foreach (float x in new[] { gx, g2, x3 })
            {
                float ze = city.EdgeZ(x);
                city.Climbs.Add(new Climb { Ax = x, Az = ze - 7 * s, Bx = x, Bz = ze + 7 * s, W = x == gx ? 7 : 6, Low = 0, High = city.L1 });
            }
            float causeEnd = cas.Moat.Z0; // здесь начинается ров
            city.Climbs.Add(new Climb { Ax = gx, Az = causeEnd - 10 * s, Bx = gx, Bz = causeEnd + 0.5f, W = 7, Low = city.L1, High = city.L2 - 0.5f });
            city.CastleBridge = new Seg { Ax = gx, Az = causeEnd - 1.5f, Bx = gx, Bz = cas.Moat.Z1 + 2.5f, W = 6 };

            // --- река вдоль южной стены снаружи (как ров): к воротам — мосты, перед проломом — брод
            var rv = city.River = new RiverPlan { W = 8 * MathF.Min(1.25f, s), Bed = -1.8f };
            float rz = -CZ - T / 2 - 0.6f - rv.W / 2;
            for (float x = -FIELD - 30; x <= FIELD + 30; x += 6)
                rv.Pts.Add(new V2(x, rz - 1.2f + 1.2f * MathF.Cos(x * 0.05f + city.PhaseA * 1.7f)));
            foreach (float x in new[] { gx, g2 })
            {
                float z = rv.ZAt(x);
                rv.Bridges.Add(new Seg { Ax = x, Az = z - rv.W / 2 - 2.5f, Bx = x, Bz = -CZ + 1.5f, W = 8 });
            }
            rv.Ford = new V2(x3, rv.ZAt(x3));

            // --- внешняя стена: башни, ворота, проломы (как прежде)
            var sides = new List<Side>();
            sides.Add(new Side { A = new V2(-CX, -CZ), B = new V2(CX, -CZ), Out = new V2(0, -1), Gates = { gx, g2 } });
            city.SouthEntries.Add(new V2(gx, -CZ));
            city.SouthEntries.Add(new V2(g2, -CZ));
            sides.Add(new Side { A = new V2(CX, -CZ), B = new V2(CX, CZ), Out = new V2(1, 0) });
            sides.Add(new Side { A = new V2(CX, CZ), B = new V2(-CX, CZ), Out = new V2(0, 1) });
            sides.Add(new Side { A = new V2(-CX, CZ), B = new V2(-CX, -CZ), Out = new V2(-1, 0) });

            void Tower(float x, float z, float sz = 6.2f, float h = 8, bool cit = false) => F.Towers.Add(new FortTower { X = x, Z = z, S = sz, H = h, Citadel = cit });
            int breaches = 1;
            foreach (var sd in sides)
            {
                float ax = sd.A.x, az = sd.A.z, bx = sd.B.x, bz = sd.B.z, len = M.Hypot(bx - ax, bz - az), ux = (bx - ax) / len, uz = (bz - az) / len;
                var gatesT = sd.Gates.Select(g => sd.Out.x != 0 ? (g - az) / uz : (g - ax) / ux).ToList();
                var cuts = new List<(float a, float b)>();
                var towerTs = new List<float> { 0, len };
                foreach (float t in gatesT)
                {
                    cuts.Add((t - 4f, t + 4f));
                    Tower(ax + ux * (t - 7.2f), az + uz * (t - 7.2f));
                    Tower(ax + ux * (t + 7.2f), az + uz * (t + 7.2f));
                    towerTs.Add(t - 7.2f); towerTs.Add(t + 7.2f);
                    F.Arches.Add(new FortArch { X = ax + ux * t, Z = az + uz * t, Ux = ux, Uz = uz, H = H, W = T });
                }
                Tower(ax, az, 7);
                int nT = Math.Max(1, M.Round(len / 26));
                for (int k = 1; k < nT; k++)
                {
                    float t = k * len / nT;
                    if (gatesT.Any(g => MathF.Abs(g - t) < 12)) continue;
                    // башня не встаёт над рекой и на краю уступа
                    float tx = ax + ux * t, tz = az + uz * t;
                    if (sd.Out.x != 0 && (MathF.Abs(tz - rv.ZAt(tx)) < rv.W / 2 + 5 || MathF.Abs(tz - city.EdgeZ(tx)) < 5)) continue;
                    Tower(tx, tz);
                    towerTs.Add(t);
                }
                bool south = sd == sides[0];
                if (breaches > 0 && south)
                {
                    float t = M.Lerp(len * 0.2f, len * 0.8f, R.F());
                    for (int tries = 0; tries < 12 && gatesT.Any(g => MathF.Abs(g - t) < 16); tries++) t = M.Lerp(len * 0.12f, len * 0.88f, R.F());
                    if (!gatesT.Any(g => MathF.Abs(g - t) < 16))
                    {
                        city.SouthEntries.Add(new V2(ax + ux * t, az + uz * t));
                        rv.Ford = new V2(ax + ux * t, rv.ZAt(ax + ux * t));
                        cuts.Add((t - 3, t + 3)); breaches--;
                        city.Walls.Add(new FeatureWall { Ax = ax + ux * (t - 2.5f), Az = az + uz * (t - 2.5f), Bx = ax + ux * (t + 2.5f), Bz = az + uz * (t + 2.5f), H = 1.1f, T = 1.4f, Solid = false });
                    }
                }
                cuts = cuts.OrderBy(c => c.a).ToList();
                float t0 = 0;
                foreach (var (c0, c1) in cuts.Concat(new[] { (len, len) }))
                {
                    if (c0 - t0 > 0.5f) F.Walls.Add(new FortWall { Ax = ax + ux * t0, Az = az + uz * t0, Bx = ax + ux * c0, Bz = az + uz * c0, W = T, H = H, Out = sd.Out });
                    t0 = c1;
                }
                // широкие лестницы на боевой ход — только на ровном (не через реку и не через уступ);
                // на южной стене, которую штурмуют, — чаще
                const float rw = 7f, rl = 14f;
                float inX = -sd.Out.x, inZ = -sd.Out.z, off = T / 2 + rw / 2 - 0.3f;
                var rampTs = gatesT.Select(g => g + 7.2f + 3.6f).Concat(gatesT.Select(g => g - 7.2f - 3.6f - rl)).ToList();
                int nr = Math.Max(1, M.Round(len / (south ? 22 : 30)));
                for (int k = 0; k < nr; k++) rampTs.Add(len * (k + 0.5f) / nr - rl / 2);
                var placed = new List<float>();
                foreach (float t in rampTs)
                {
                    float ta = M.Clamp(t, 5, len - rl - 5), tb = ta + rl;
                    foreach (float tt in towerTs) if (tb > tt - 4 && ta < tt + 4) { ta = tt + 4; tb = ta + rl; }
                    if (tb > len - 4 || cuts.Any(c => tb > c.a - 1 && ta < c.b + 1) || towerTs.Any(tt => tb > tt - 4 && ta < tt + 4)) continue;
                    if (placed.Any(q => MathF.Abs(q - ta) < rl + 2)) continue;
                    bool flat = true;
                    for (float q = ta - 1; q <= tb + 1; q += 2)
                    {
                        float px = ax + ux * q + inX * off, pz = az + uz * q + inZ * off;
                        float l0 = city.Lift(px, pz);
                        if (MathF.Abs(l0 - city.Lift(ax + ux * ta + inX * off, az + uz * ta + inZ * off)) > 0.3f || MathF.Abs(pz - rv.ZAt(px)) < rv.W / 2 + 3) flat = false;
                    }
                    if (!flat) continue;
                    // не внутри замка: там северная стена города слита с его стеной, лестница легла бы на двор и донжон
                    float rx0 = MathF.Min(ax + ux * ta, ax + ux * tb) + inX * off - rw, rx1 = MathF.Max(ax + ux * ta, ax + ux * tb) + inX * off + rw;
                    float rz0 = MathF.Min(az + uz * ta, az + uz * tb) + inZ * off - rw, rz1 = MathF.Max(az + uz * ta, az + uz * tb) + inZ * off + rw;
                    if (rx1 > cas.X0 - 4 && rx0 < cas.X1 + 4 && rz1 > cas.Z0 - 4 && rz0 < CZ + 4) continue;
                    placed.Add(ta);
                    F.Ramps.Add(new FortRamp { Ax = ax + ux * ta + inX * off, Az = az + uz * ta + inZ * off, Bx = ax + ux * tb + inX * off, Bz = az + uz * tb + inZ * off, W = rw, H = H });
                }
            }

            // --- замок: стены с башнями, надвратные башни, двор, донжон со всходом с боевого хода
            {
                float W = 3.4f, CH = cas.WallH;
                var cp = new[] { new V2(cas.X0, cas.Z0), new V2(cas.X1, cas.Z0), new V2(cas.X1, cas.Z1), new V2(cas.X0, cas.Z1), new V2(cas.X0, cas.Z0) };
                float mx = (cas.X0 + cas.X1) / 2, mz = (cas.Z0 + cas.Z1) / 2;
                for (int k = 0; k < 4; k++)
                {
                    float ax = cp[k].x, az = cp[k].z, bx = cp[k + 1].x, bz = cp[k + 1].z, len = M.Hypot(bx - ax, bz - az), ux = (bx - ax) / len, uz = (bz - az) / len;
                    var outv = MathF.Abs(ux) > 0.5f ? new V2(0, M.Sign((az + bz) / 2 - mz)) : new V2(M.Sign((ax + bx) / 2 - mx), 0);
                    Tower(ax, az, 7.5f, CH + 1.5f, true); // на 1,5 м выше хода — по стенам можно обойти весь замок
                    if (k == 0)
                    { // южная стена: ворота и две надвратные башни
                        float tg = cas.GateX - ax;
                        F.Walls.Add(new FortWall { Ax = ax, Az = az, Bx = ax + ux * (tg - 3.3f), Bz = az, W = W, H = CH, Out = outv, Citadel = true });
                        F.Walls.Add(new FortWall { Ax = ax + ux * (tg + 3.3f), Az = az, Bx = bx, Bz = bz, W = W, H = CH, Out = outv, Citadel = true });
                        F.Arches.Add(new FortArch { X = cas.GateX, Z = az, Ux = ux, Uz = uz, H = CH, W = W, Citadel = true });
                        Tower(cas.GateX - 6.3f, az, 6.4f, CH + 3, true);
                        Tower(cas.GateX + 6.3f, az, 6.4f, CH + 3, true);
                    }
                    else
                    {
                        F.Walls.Add(new FortWall { Ax = ax, Az = az, Bx = bx, Bz = bz, W = W, H = CH, Out = outv, Citadel = true });
                        if (len > 30 && k != 2) Tower((ax + bx) / 2, (az + bz) / 2, 6.4f, CH + 1.5f, true);
                    }
                }
                // всходы на стены изнутри — у восточной и западной стен
                // широкие лестницы на стену — вдоль южной стены изнутри, по обе стороны от ворот
                float rl = MathF.Min(CH * 1.8f, castleHX - 9.8f - 4.3f), off = W / 2 + 6f / 2 - 0.3f;
                foreach (float side in new[] { -1f, 1f })
                {
                    float xa = cas.GateX + side * 9.8f, xb = xa + side * rl, z = cas.Z0 + off;
                    if (rl > CH * 1.4f)
                        F.Ramps.Add(new FortRamp { Ax = xa, Az = z, Bx = xb, Bz = z, W = 6f, H = CH, Citadel = true });
                }
                // донжон встроен в северную стену (внешняя грань вровень с ней): боевой ход упирается в него, а на крышу ведут
                // две лестницы по ходу — с запада и с востока, так что стены по-прежнему можно обойти кругом, через крышу
                cas.KeepS = MathF.Min(13, castleD * 0.5f); cas.KeepH = CH + 4;
                float kx = cas.GateX + (R.Next() < 0.5 ? -1 : 1) * castleHX * 0.38f, kz = cas.Z1 + W / 2 - cas.KeepS / 2;
                cas.Keep = new V2(kx, kz);
                F.Towers.Add(new FortTower { X = kx, Z = kz, S = cas.KeepS, H = cas.KeepH, Citadel = true, Keep = true });
                foreach (float side in new[] { -1f, 1f })
                {
                    float kb = kx + side * (cas.KeepS / 2 - 1.2f);                              // верх — уже на крыше
                    float end = side < 0 ? cas.X0 + 3.75f + 1.5f : cas.X1 - 3.75f - 1.5f;      // не заходя на всход к угловой башне
                    float rlen = MathF.Min(11f, MathF.Abs(kb - end));
                    if (rlen < 6) continue;
                    var kr = new FortRamp { Ax = kb + side * rlen, Az = cas.Z1, Bx = kb, Bz = cas.Z1, W = W, H = cas.KeepH, Citadel = true, Keep = true };
                    F.Ramps.Add(kr);
                    if (cas.KeepRamp == null || side < 0) cas.KeepRamp = new Seg { Ax = kr.Ax, Az = kr.Az, Bx = kr.Bx, Bz = kr.Bz, W = W };
                }
            }

            // --- улицы
            var inner = new Rect4(-CX + 9.5f, CX - 9.5f, -CZ + 9.5f, CZ - 9.5f);
            float lowZ = (rv.ZAt(0) + rv.W / 2 + city.ZA - city.AmpA) / 2;
            city.Streets.Add(new Seg { Ax = gx, Az = -CZ - 12, Bx = gx, Bz = cas.Moat.Z0, W = 8 });
            city.Streets.Add(new Seg { Ax = g2, Az = -CZ - 12, Bx = g2, Bz = city.EdgeZ(g2) + 12 * s, W = 6 });
            city.Streets.Add(new Seg { Ax = x3, Az = -CZ + 7, Bx = x3, Bz = city.EdgeZ(x3) + 12 * s, W = 6 });
            float upZ = cas.Moat.Z0 - 1.5f; // улица верхнего города вдоль рва
            city.Streets.Add(new Seg { Ax = -CX + 7, Az = upZ, Bx = CX - 7, Bz = upZ, W = 4.5f });
            var ring = new[] { new V2(inner.X0 - 2.5f, inner.Z0 - 2.5f), new V2(inner.X1 + 2.5f, inner.Z0 - 2.5f), new V2(inner.X1 + 2.5f, inner.Z1 + 2.5f), new V2(inner.X0 - 2.5f, inner.Z1 + 2.5f), new V2(inner.X0 - 2.5f, inner.Z0 - 2.5f) };
            for (int k = 0; k < 4; k++) city.Streets.Add(new Seg { Ax = ring[k].x, Az = ring[k].z, Bx = ring[k + 1].x, Bz = ring[k + 1].z, W = 4.5f });
            // площадь — в верхнем городе перед насыпью к замку; двор замка — тоже «площадь» (мощёный)
            var sq = new Square { X = gx + (R.Next() < 0.5 ? -1 : 1) * 15 * s, Z = lowZ, Hx = 8 * s, Hz = 5f };
            city.Squares.Add(sq);
            city.Squares.Add(new Square { X = (cas.X0 + cas.X1) / 2, Z = (cas.Z0 + cas.Z1) / 2, Hx = (cas.X1 - cas.X0) / 2 - 3.4f, Hz = (cas.Z1 - cas.Z0) / 2 - 3.4f });

            // --- кварталы: по зонам между улицами, рекой и уступами
            var blocks = new List<Rect4>();
            void Split(Rect4 r, int depth)
            {
                float w = r.X1 - r.X0, h = r.Z1 - r.Z0;
                if (w < 8 || h < 8) return;
                if ((w < 24 && h < 22) || depth > 6) { blocks.Add(r); return; }
                float sw = depth < 2 ? 4.5f : 3.6f, f = M.Lerp(0.38f, 0.62f, R.F());
                if (w >= h)
                {
                    float x = r.X0 + w * f;
                    city.Streets.Add(new Seg { Ax = x, Az = r.Z0 - 1, Bx = x, Bz = r.Z1 + 1, W = sw });
                    Split(new Rect4(r.X0, x - sw / 2, r.Z0, r.Z1), depth + 1);
                    Split(new Rect4(x + sw / 2, r.X1, r.Z0, r.Z1), depth + 1);
                }
                else
                {
                    float z = r.Z0 + h * f;
                    city.Streets.Add(new Seg { Ax = r.X0 - 1, Az = z, Bx = r.X1 + 1, Bz = z, W = sw });
                    Split(new Rect4(r.X0, r.X1, r.Z0, z - sw / 2), depth + 1);
                    Split(new Rect4(r.X0, r.X1, z + sw / 2, r.Z1), depth + 1);
                }
            }
            float rvMin = rv.Pts.Where(p => MathF.Abs(p.x) < CX).Min(p => p.z), rvMax = rv.Pts.Where(p => MathF.Abs(p.x) < CX).Max(p => p.z);
            float eMin = city.ZA - city.AmpA, eMax = city.ZA + city.AmpA;
            var zones = new List<(float z0, float z1)>
            {
                (inner.Z0, eMin - 2.5f),                        // нижний посад: от южной стены до уступа
                (eMax + 2.5f, upZ - 2.25f),                     // верхний город у края уступа
                (upZ + 2.25f, inner.Z1),                        // верхний город по бокам от скалы замка
            };
            foreach (var (z0, z1) in zones)
            {
                if (z1 - z0 < 8) continue;
                var cutsX = new List<float> { gx, g2, x3 };
                var bounds = new List<float> { inner.X0 };
                foreach (float c in cutsX.OrderBy(v => v)) { bounds.Add(c - 3.5f); bounds.Add(c + 3.5f); }
                bounds.Add(inner.X1);
                for (int i = 0; i + 1 < bounds.Count; i += 2)
                    if (bounds[i + 1] - bounds[i] > 8) Split(new Rect4(bounds[i], bounds[i + 1], z0, z1), 1);
            }

            var homes = defs.Where(d => d.Kind == "home").ToList();
            var extraKinds = new[] { "tavern", "blacksmith", "destroyed", "tower_B" };
            var extra = defs.Where(d => extraKinds.Contains(d.Kind)).ToList();
            CityDef ByKind(string k) => defs.FirstOrDefault(d => d.Kind == k);
            void Place(CityDef def, float x, float z, float rot)
            {
                float sc = Heights[def.Kind] / def.H;
                bool turned = MathF.Abs(MathF.Sin(rot)) > 0.5f;
                city.Buildings.Add(new Building { Def = def, X = x, Z = z, Rot = rot, S = sc, Hx = (turned ? def.D : def.W) * sc * 0.46f, Hz = (turned ? def.W : def.D) * sc * 0.46f, Top = Heights[def.Kind] * 0.9f });
            }
            void FillBlock(Rect4 b)
            {
                var sb = new[]
                {
                    (ax: b.X0, az: b.Z1, bx: b.X1, bz: b.Z1, rot: 0f, inX: 0f, inZ: -1f),
                    (ax: b.X0, az: b.Z0, bx: b.X1, bz: b.Z0, rot: M.PI, inX: 0f, inZ: 1f),
                    (ax: b.X1, az: b.Z0, bx: b.X1, bz: b.Z1, rot: M.PI / 2, inX: -1f, inZ: 0f),
                    (ax: b.X0, az: b.Z0, bx: b.X0, bz: b.Z1, rot: -M.PI / 2, inX: 1f, inZ: 0f),
                };
                var mine = new List<(float x, float z, float hx, float hz)>();
                bool Clash(float x, float z, float hx, float hz) => mine.Any(o => MathF.Abs(o.x - x) < o.hx + hx + 0.15f && MathF.Abs(o.z - z) < o.hz + hz + 0.15f);
                foreach (var sd in sb)
                {
                    float len = M.Hypot(sd.bx - sd.ax, sd.bz - sd.az), ux = (sd.bx - sd.ax) / len, uz = (sd.bz - sd.az) / len;
                    float blockDepth = sd.inX != 0 ? b.X1 - b.X0 : b.Z1 - b.Z0;
                    float t = R.F() * 0.6f;
                    while (t < len - 3)
                    {
                        CityDef def = R.Next() < 0.1 && extra.Count > 0 ? extra[(int)(R.Next() * extra.Count)] : homes[(int)(R.Next() * homes.Count)];
                        float s2 = Heights[def.Kind] / def.H, fw = def.W * s2, fd = def.D * s2;
                        if (t + fw > len + 0.5f) break;
                        if (fd > blockDepth - 0.4f) { t += 1.5f; continue; }
                        float along = t + fw / 2, depth = fd / 2 + 0.3f;
                        float x = sd.ax + ux * along + sd.inX * depth, z = sd.az + uz * along + sd.inZ * depth;
                        bool turned = MathF.Abs(MathF.Sin(sd.rot)) > 0.5f;
                        float hx = (turned ? fd : fw) / 2, hz = (turned ? fw : fd) / 2;
                        if (Clash(x, z, hx, hz)) { t += 1.5f; continue; }
                        mine.Add((x, z, hx, hz));
                        Place(def, x, z, sd.rot);
                        t += fw + 0.2f + R.F() * 0.7f;
                    }
                }
            }
            foreach (var b in blocks)
            {
                if (R.Next() < 0.06) { city.Gardens.Add(b); continue; }
                FillBlock(b);
            }
            city.DebugBlocks = blocks.Count; city.DebugPlaced = city.Buildings.Count;

            // Дома не стоят на краю уступа, у реки, на скале замка, на подъёмах, улицах и площадях
            Rect4 RectOf(Building bd) => new Rect4(bd.X - bd.Hx, bd.X + bd.Hx, bd.Z - bd.Hz, bd.Z + bd.Hz);
            var mound = new Rect4(cas.MX0 - 1, cas.MX1 + 1, cas.Moat.Z0 - 1, CZ + 10);
            bool Bad(Building bd)
            {
                var r = RectOf(bd);
                if (Overlaps(r, mound, 1)) return true;
                foreach (var cr in new[] { (r.X0, r.Z0), (r.X1, r.Z0), (r.X0, r.Z1), (r.X1, r.Z1), (bd.X, bd.Z) })
                {
                    if (MathF.Abs(cr.Item2 - city.EdgeZ(cr.Item1)) < 2.2f) return true;
                    if (MathF.Abs(cr.Item2 - rv.ZAt(cr.Item1)) < rv.W / 2 + 2) return true;
                }
                if (M.Sign(r.Z0 - city.EdgeZ(bd.X)) != M.Sign(r.Z1 - city.EdgeZ(bd.X))) return true;
                foreach (var cl in city.Climbs) if (M.SegDist(bd.X, bd.Z, cl.Ax, cl.Az, cl.Bx, cl.Bz) < cl.W / 2 + MathF.Max(bd.Hx, bd.Hz) + 1) return true;
                foreach (var q in city.Squares) if (Overlaps(r, new Rect4(q.X - q.Hx, q.X + q.Hx, q.Z - q.Hz, q.Z + q.Hz), 0.5f)) return true;
                // и не на лестнице на стену: к её нижнему концу — свободный подход
                foreach (var rp in F.Ramps)
                {
                    if (rp.Keep) continue;
                    float rl = M.Hypot(rp.Bx - rp.Ax, rp.Bz - rp.Az), ux = (rp.Bx - rp.Ax) / rl, uz = (rp.Bz - rp.Az) / rl, sx = rp.Ax - ux * 4, sz = rp.Az - uz * 4;
                    var rr = new Rect4(MathF.Min(sx, rp.Bx) - rp.W / 2 * MathF.Abs(uz), MathF.Max(sx, rp.Bx) + rp.W / 2 * MathF.Abs(uz),
                                       MathF.Min(sz, rp.Bz) - rp.W / 2 * MathF.Abs(ux), MathF.Max(sz, rp.Bz) + rp.W / 2 * MathF.Abs(ux));
                    if (Overlaps(r, rr, 0.8f)) return true;
                }
                return city.Streets.Any(st => M.SegDist(bd.X, bd.Z, st.Ax, st.Az, st.Bx, st.Bz) < st.W / 2 + MathF.Min(bd.Hx, bd.Hz) * 0.6f);
            }
            city.Buildings = city.Buildings.Where(bd => !Bad(bd)).ToList();

            // Площадь: колодец и рынок; в замке — собор и терема у стен
            var wl = ByKind("well"); if (wl != null) Place(wl, sq.X, sq.Z, 0);
            var mk = ByKind("market"); if (mk != null) Place(mk, sq.X + sq.Hx * 0.55f, sq.Z - sq.Hz * 0.2f, M.PI);
            float yardX0 = cas.X0 + 3.4f + 2, yardX1 = cas.X1 - 3.4f - 2;
            var ch = ByKind("church");
            float keepSide = M.Sign(cas.Keep.x - cas.GateX);
            if (ch != null) Place(ch, cas.GateX - keepSide * (cas.X1 - cas.X0) * 0.3f, cas.Z1 - 3.4f - 7.5f, 0);
            for (int k = 0; k < 2; k++)
            {
                var d = homes[(int)(R.Next() * homes.Count)];
                float px = k == 0 ? yardX0 + 4.5f : yardX1 - 4.5f;
                if (MathF.Abs(px - cas.Keep.x) < cas.KeepS / 2 + 5) continue;
                Place(d, px, cas.Z0 + 3.4f + 12, k == 0 ? -M.PI / 2 : M.PI / 2);
            }

            // Баррикады у мостов и на подъёмах; бочки и ящики вдоль улиц
            foreach (var br in rv.Bridges)
            {
                float z = MathF.Max(br.Az, br.Bz) + 3.5f, x = br.Ax;
                city.Walls.Add(new FeatureWall { Ax = x - br.W / 2 - 0.5f, Az = z, Bx = x - 1.7f, Bz = z, H = 1.25f, T = 0.6f, Wood = true }); // проём 3,4 м — пройдёт и всадник
                city.Walls.Add(new FeatureWall { Ax = x + 1.7f, Az = z, Bx = x + br.W / 2 + 0.5f, Bz = z, H = 1.25f, T = 0.6f, Wood = true });
            }
            var props = defs.Where(d => d.Kind == "prop").ToList();
            for (int k = 0; k < 60 && props.Count > 0; k++)
            {
                var st = city.Streets[(int)(R.Next() * city.Streets.Count)];
                float t = R.F();
                float x = M.Lerp(st.Ax, st.Bx, t), z = M.Lerp(st.Az, st.Bz, t), side = R.Next() < 0.5 ? -1 : 1;
                bool vertical = MathF.Abs(st.Bx - st.Ax) < 0.1f;
                float px = vertical ? x + side * (st.W / 2 - 0.7f) : x, pz = vertical ? z : z + side * (st.W / 2 - 0.7f);
                if (MathF.Abs(px) > inner.X1 || MathF.Abs(pz) > inner.Z1 || Overlaps(new Rect4(px, px, pz, pz), mound, 1)) continue;
                if (MathF.Abs(pz - rv.ZAt(px)) < rv.W / 2 + 2 || MathF.Abs(pz - city.EdgeZ(px)) < 2) continue;
                if (city.Climbs.Any(cl => M.SegDist(px, pz, cl.Ax, cl.Az, cl.Bx, cl.Bz) < cl.W / 2 + 1)) continue;
                city.Props.Add(new Prop { Def = props[(int)(R.Next() * props.Count)], X = px, Z = pz, Rot = R.F() * M.PI * 2 });
            }
            return city;
        }
    }
}
