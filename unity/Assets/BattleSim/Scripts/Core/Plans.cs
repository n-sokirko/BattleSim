using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleSim.Core
{
    // ------------------------------------------------------------------ горы

    public sealed class MountainPath
    {
        public List<V2> Ctrl;
        public float W, Side;
        /// <summary>Тропа к броду: спускается к самой реке, а не к мосту.</summary>
        public bool Ford;
        public MountainBridge Bridge;
        public List<V2> Pts;
        public float[] Prof;
    }

    public sealed class MountainBridge
    {
        public V2 A, B;
        public List<MountainPath> Paths = new List<MountainPath>();
    }

    /// <summary>
    /// Горный перевал: армии стоят в долинах у краёв, между ними — массив террасами
    /// с обрывами, посередине — ущелье с рекой. Через массив вьются серпантины к мостам.
    /// </summary>
    public sealed class MountainPlan
    {
        public float GorgeW;
        public V2[] GorgePts;
        public List<MountainBridge> Bridges = new List<MountainBridge>();
        public List<MountainPath> Paths = new List<MountainPath>();
        /// <summary>Броды: здесь река мелкая, к воде спускаются тропы.</summary>
        public List<V2> Fords = new List<V2>();
        float z0, amp, ph, fr;

        public float Gz(float x) => z0 + MathF.Sin(x * fr + ph) * amp;

        public static MountainPlan Make(Rng R, float F)
        {
            var m = new MountainPlan();
            float gw = M.Lerp(12, 16, R.F());
            m.z0 = M.Lerp(-5, 5, R.F()); m.amp = M.Lerp(4, 8, R.F()); m.ph = R.F() * 6; m.fr = M.Lerp(0.035f, 0.06f, R.F());
            m.GorgeW = gw;
            m.GorgePts = new V2[41];
            for (int i = 0; i < 41; i++) { float x = -F * 1.3f + i * F * 2.6f / 40; m.GorgePts[i] = new V2(x, m.Gz(x)); }
            int nb = F > 100 ? 3 : 2 + (R.Next() < 0.5 ? 1 : 0);
            for (int k = 0; k < nb; k++)
            {
                float bx = (k - (nb - 1) / 2f) * (F * 1.3f / (nb - 1)) + M.Lerp(-6, 6, R.F());
                float rim = gw / 2 + 1.5f, zc = m.Gz(bx), tilt = M.Lerp(-3, 3, R.F());
                var a = new V2(bx - tilt, zc - rim);
                var b = new V2(bx + tilt, zc + rim);
                var br = new MountainBridge { A = a, B = b };
                m.Bridges.Add(br);
                foreach (var (side, end) in new[] { (-1f, a), (1f, b) })
                {
                    var start = new V2(M.Clamp(end.x + M.Lerp(-18, 18, R.F()), -F + 8, F - 8), side * F * 0.66f);
                    int n = 4 + (int)(R.Next() * 3);
                    var ctrl = new List<V2> { start };
                    for (int j = 1; j < n; j++)
                    {
                        float t = (float)j / n, zig = (j % 2 == 1 ? 1 : -1) * M.Lerp(9, 17, R.F()) * MathF.Sin(M.PI * t);
                        ctrl.Add(new V2(M.Clamp(M.Lerp(start.x, end.x, t) + zig, -F + 6, F - 6), M.Lerp(start.z, end.z, t)));
                    }
                    ctrl.Add(new V2(end.x, end.z - side * 3));
                    ctrl.Add(end);
                    var p = new MountainPath { Ctrl = ctrl, W = 6f, Side = side, Bridge = br };
                    m.Paths.Add(p);
                    br.Paths.Add(p);
                }
            }
            // Броды — между мостами, где их нет: спуск к воде с обеих сторон
            var xs = m.Bridges.Select(b => b.A.x).OrderBy(x => x).ToList();
            var gaps = new List<float>();
            float prev = -F * 0.85f;
            foreach (var x in xs.Concat(new[] { F * 0.85f })) { if (x - prev > 34) gaps.Add((prev + x) / 2); prev = x; }
            int nf = Math.Min(gaps.Count, F > 100 ? 3 : 2);
            foreach (var fx0 in gaps.OrderBy(_ => R.Next()).Take(nf))
            {
                float fx = fx0 + M.Lerp(-5, 5, R.F()), fzc = m.Gz(fx);
                var ford = new V2(fx, fzc);
                m.Fords.Add(ford);
                foreach (float side in new[] { -1f, 1f })
                {
                    var start = new V2(M.Clamp(fx + M.Lerp(-16, 16, R.F()), -F + 8, F - 8), side * F * 0.66f);
                    int n = 3 + (int)(R.Next() * 3);
                    var ctrl = new List<V2> { start };
                    for (int j = 1; j < n; j++)
                    {
                        float t = (float)j / n, zig = (j % 2 == 1 ? 1 : -1) * M.Lerp(8, 15, R.F()) * MathF.Sin(M.PI * t);
                        ctrl.Add(new V2(M.Clamp(M.Lerp(start.x, fx, t) + zig, -F + 6, F - 6), M.Lerp(start.z, fzc, t)));
                    }
                    ctrl.Add(new V2(fx, fzc + side * (m.GorgeW / 2 + 4)));
                    ctrl.Add(ford);
                    m.Paths.Add(new MountainPath { Ctrl = ctrl, W = 7f, Side = side, Ford = true });
                }
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
    }

    public sealed class Prop
    {
        public CityDef Def;
        public float X, Z, Rot, S, Y;
    }

    /// <summary>Каменная ограда, завал в проломе или баррикада: через неё перелезают.</summary>
    public sealed class FeatureWall
    {
        public float Ax, Az, Bx, Bz, H, T;
        /// <summary>Сплошная ограда: сквозь неё не пройти, только через проёмы. Завал (false) — перелезают медленно.</summary>
        public bool Solid = true;
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

    public sealed class FortWall { public float Ax, Az, Bx, Bz, W, H; public V2 Out; public bool Citadel; }
    public sealed class FortTower { public float X, Z, S, H; public bool Citadel; }
    public sealed class FortRamp { public float Ax, Az, Bx, Bz, W, H; public bool Citadel; }
    public sealed class FortArch { public float X, Z, Ux, Uz, H, W; public bool Citadel; }

    public sealed class Fort
    {
        public List<FortWall> Walls = new List<FortWall>();
        public List<FortTower> Towers = new List<FortTower>();
        public List<FortRamp> Ramps = new List<FortRamp>();
        public List<FortArch> Arches = new List<FortArch>();
    }

    public sealed class Citadel
    {
        public float X0, X1, Z0, Z1, H, WallH, Side, Cz;
        public Rect4 Rect => new Rect4(X0, X1, Z0, Z1);
    }

    public sealed class Square { public float X, Z, Hx, Hz; }

    /// <summary>
    /// Крепость-город: стена с башнями, воротами и проломами, лестницы на боевой ход,
    /// детинец на насыпном холме со своей стеной и въездом, кварталы и переулки,
    /// площадь с рынком и колодцем, баррикады у ворот.
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
        public Citadel Citadel;
        public Seg Road;
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

        public static CityPlan Make(Rng R, IList<CityDef> defs, float FIELD)
        {
            float CX = FIELD * 0.8f, CZ = FIELD * 0.55f, T = 3.2f, H = 7;
            var city = new CityPlan { CX = CX, CZ = CZ, T = T, H = H, TX = CX, TZ = CZ };
            var F = city.Fort;

            // --- детинец: насыпной холм у одной из боковых стен
            float cs = R.Next() < 0.5 ? -1 : 1, cw = M.Lerp(24, 30, R.F()), cd = M.Lerp(26, 34, R.F());
            float cz0 = M.Lerp(-CZ * 0.25f, CZ * 0.25f, R.F());
            float xa = cs * (CX - 10), xb = cs * (CX - 10 - cw);
            var cit = city.Citadel = new Citadel { X0 = MathF.Min(xa, xb), X1 = MathF.Max(xa, xb), Z0 = cz0 - cd / 2, Z1 = cz0 + cd / 2, H = 6, WallH = 5.5f, Side = cs, Cz = cz0 };
            float gateX = cs > 0 ? cit.X0 : cit.X1; // ворота детинца смотрят к центру города
            city.Road = new Seg { Ax = gateX - cs * 16, Az = cz0, Bx = gateX, Bz = cz0, W = 6.5f }; // въезд-пандус на холм

            // --- внешняя стена: башни, ворота, проломы
            float gx = M.Lerp(-CX * 0.25f, CX * 0.25f, R.F());
            var sides = new List<Side>();
            // южная стена — под штурм: двое ворот (и пролом ниже)
            float g2 = gx + (gx >= 0 ? -1 : 1) * CX * 0.55f;
            sides.Add(new Side { A = new V2(-CX, -CZ), B = new V2(CX, -CZ), Out = new V2(0, -1), Gates = { gx, g2 } });
            city.SouthEntries.Add(new V2(gx, -CZ));
            city.SouthEntries.Add(new V2(g2, -CZ));
            var s1 = new Side { A = new V2(CX, -CZ), B = new V2(CX, CZ), Out = new V2(1, 0) };
            if (cs < 0 && R.Next() < 0.6) s1.Gates.Add(M.Lerp(-CZ * 0.3f, CZ * 0.3f, R.F()));
            sides.Add(s1);
            sides.Add(new Side { A = new V2(CX, CZ), B = new V2(-CX, CZ), Out = new V2(0, 1), Gates = { gx } });
            var s3 = new Side { A = new V2(-CX, CZ), B = new V2(-CX, -CZ), Out = new V2(-1, 0) };
            if (cs > 0 && R.Next() < 0.6) s3.Gates.Add(M.Lerp(-CZ * 0.3f, CZ * 0.3f, R.F()));
            sides.Add(s3);

            void Tower(float x, float z, float s = 6.2f) => F.Towers.Add(new FortTower { X = x, Z = z, S = s, H = H + 1 });
            int breaches = 1 + (R.Next() < 0.5 ? 1 : 0);
            foreach (var sd in sides)
            {
                float ax = sd.A.x, az = sd.A.z, bx = sd.B.x, bz = sd.B.z, len = M.Hypot(bx - ax, bz - az), ux = (bx - ax) / len, uz = (bz - az) / len;
                var gatesT = sd.Gates.Select(g => sd.Out.x != 0 ? (g - az) / uz : (g - ax) / ux).ToList();
                var cuts = new List<(float a, float b)>(); // промежутки без стены
                var towerTs = new List<float> { 0, len };
                foreach (float t in gatesT)
                {
                    cuts.Add((t - 4f, t + 4f)); // широкие ворота — отряд проходит строем
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
                    Tower(ax + ux * t, az + uz * t);
                    towerTs.Add(t);
                }
                bool south = sd == sides[0];
                if (breaches > 0 && (south || R.Next() < 0.45))
                {
                    float t = M.Lerp(len * 0.2f, len * 0.8f, R.F());
                    for (int tries = 0; tries < 12 && gatesT.Any(g => MathF.Abs(g - t) < 16); tries++) t = M.Lerp(len * 0.12f, len * 0.88f, R.F());
                    if (!gatesT.Any(g => MathF.Abs(g - t) < 16))
                    {
                        if (south) city.SouthEntries.Add(new V2(ax + ux * t, az + uz * t));
                        cuts.Add((t - 3, t + 3)); breaches--;
                        city.Walls.Add(new FeatureWall { Ax = ax + ux * (t - 2.5f), Az = az + uz * (t - 2.5f), Bx = ax + ux * (t + 2.5f), Bz = az + uz * (t + 2.5f), H = 1.1f, T = 1.4f, Solid = false }); // завал в проломе: перелезают
                    }
                }
                cuts = cuts.OrderBy(c => c.a).ToList();
                float t0 = 0;
                foreach (var (c0, c1) in cuts.Concat(new[] { (len, len) }))
                {
                    if (c0 - t0 > 0.5f) F.Walls.Add(new FortWall { Ax = ax + ux * t0, Az = az + uz * t0, Bx = ax + ux * c0, Bz = az + uz * c0, W = T, H = H, Out = sd.Out });
                    t0 = c1;
                }
                // Лестницы на боевой ход: широкие и пологие, у каждых ворот и через каждые ~38 м, в обход башен
                const float rw = 4f, rl = 14f;
                float inX = -sd.Out.x, inZ = -sd.Out.z, off = T / 2 + rw / 2 - 0.3f; // лестница чуть заходит на стену, без щели
                var rampTs = gatesT.Select(g => g + 7.2f + 3.6f).ToList();
                int nr = Math.Max(1, M.Round(len / 38));
                for (int k = 0; k < nr; k++) rampTs.Add(len * (k + 0.5f) / nr - rl / 2);
                var placed = new List<float>();
                foreach (float t in rampTs)
                {
                    float ta = M.Clamp(t, 5, len - rl - 5), tb = ta + rl;
                    // башня стоит поперёк — сдвигаем лестницу за неё
                    foreach (float tt in towerTs) if (tb > tt - 4 && ta < tt + 4) { ta = tt + 4; tb = ta + rl; }
                    if (tb > len - 4 || cuts.Any(c => tb > c.a - 1 && ta < c.b + 1) || towerTs.Any(tt => tb > tt - 4 && ta < tt + 4)) continue;
                    if (placed.Any(q => MathF.Abs(q - ta) < rl + 4)) continue;
                    placed.Add(ta);
                    F.Ramps.Add(new FortRamp { Ax = ax + ux * ta + inX * off, Az = az + uz * ta + inZ * off, Bx = ax + ux * tb + inX * off, Bz = az + uz * tb + inZ * off, W = rw, H = H });
                }
            }

            // --- стена детинца (на холме, ниже внешней); стены чуть внутри края холма, чтобы стояли на ровном
            var ci = new Rect4(cit.X0 + 1.4f, cit.X1 - 1.4f, cit.Z0 + 1.4f, cit.Z1 - 1.4f);
            float gateXi = cs > 0 ? ci.X0 : ci.X1;
            var cp = new[] { new V2(ci.X0, ci.Z0), new V2(ci.X1, ci.Z0), new V2(ci.X1, ci.Z1), new V2(ci.X0, ci.Z1), new V2(ci.X0, ci.Z0) };
            float ccx = (cit.X0 + cit.X1) / 2;
            for (int k = 0; k < 4; k++)
            {
                float ax = cp[k].x, az = cp[k].z, bx = cp[k + 1].x, bz = cp[k + 1].z, len = M.Hypot(bx - ax, bz - az), ux = (bx - ax) / len, uz = (bz - az) / len;
                var outv = MathF.Abs(ux) > 0.5f ? new V2(0, M.Sign((az + bz) / 2 - cz0)) : new V2(M.Sign((ax + bx) / 2 - ccx), 0);
                F.Towers.Add(new FortTower { X = ax, Z = az, S = 5.2f, H = cit.WallH + 1, Citadel = true });
                bool isGateSide = MathF.Abs(ax - gateXi) < 0.1f && MathF.Abs(bx - gateXi) < 0.1f;
                if (isGateSide)
                {
                    float tg = MathF.Abs(cz0 - az);
                    F.Walls.Add(new FortWall { Ax = ax, Az = az, Bx = ax + ux * (tg - 3.5f), Bz = az + uz * (tg - 3.5f), W = 2.6f, H = cit.WallH, Out = outv, Citadel = true });
                    F.Walls.Add(new FortWall { Ax = ax + ux * (tg + 3.5f), Az = az + uz * (tg + 3.5f), Bx = bx, Bz = bz, W = 2.6f, H = cit.WallH, Out = outv, Citadel = true });
                    F.Arches.Add(new FortArch { X = gateXi, Z = cz0, Ux = ux, Uz = uz, H = cit.WallH, W = 2.6f, Citadel = true });
                }
                else F.Walls.Add(new FortWall { Ax = ax, Az = az, Bx = bx, Bz = bz, W = 2.6f, H = cit.WallH, Out = outv, Citadel = true });
                // лестница на стену детинца изнутри
                if (k == 0 || k == 2)
                {
                    float inZ = -outv.z, off = 2.6f / 2 + 3.4f / 2 - 0.3f, t0 = len * 0.2f;
                    F.Ramps.Add(new FortRamp { Ax = ax + ux * t0, Az = az + inZ * off, Bx = ax + ux * (t0 + 10), Bz = az + inZ * off, W = 3.4f, H = cit.WallH, Citadel = true });
                }
            }

            // --- улицы: главная (от ворот до ворот), поперечная, кольцевая у стен и кварталы делением
            var inner = new Rect4(-CX + 9.5f, CX - 9.5f, -CZ + 9.5f, CZ - 9.5f);
            float crossZ = M.Lerp(-CZ * 0.2f, CZ * 0.2f, R.F());
            city.Streets.Add(new Seg { Ax = gx, Az = -CZ - 12, Bx = gx, Bz = CZ + 12, W = 8 });
            city.Streets.Add(new Seg { Ax = -CX, Az = crossZ, Bx = CX, Bz = crossZ, W = 6.5f });
            city.Streets.Add(new Seg { Ax = g2, Az = -CZ - 12, Bx = g2, Bz = crossZ, W = 6 });
            var ring = new[] { new V2(inner.X0 - 2.5f, inner.Z0 - 2.5f), new V2(inner.X1 + 2.5f, inner.Z0 - 2.5f), new V2(inner.X1 + 2.5f, inner.Z1 + 2.5f), new V2(inner.X0 - 2.5f, inner.Z1 + 2.5f), new V2(inner.X0 - 2.5f, inner.Z0 - 2.5f) };
            for (int k = 0; k < 4; k++) city.Streets.Add(new Seg { Ax = ring[k].x, Az = ring[k].z, Bx = ring[k + 1].x, Bz = ring[k + 1].z, W = 4.5f });
            foreach (var sd in sides) foreach (float g in sd.Gates) if (sd.Out.x != 0) city.Streets.Add(new Seg { Ax = sd.A.x, Az = g, Bx = 0, Bz = g, W = 6 });
            city.Streets.Add(city.Road.Copy());
            var sq = new Square { X = gx + M.Lerp(-4, 4, R.F()), Z = crossZ };
            sq.Hx = M.Lerp(8.5f, 11, R.F()); sq.Hz = M.Lerp(6.5f, 8.5f, R.F());
            city.Squares.Add(sq);

            var blocks = new List<Rect4>();
            void Split(Rect4 r, int depth)
            {
                float w = r.X1 - r.X0, h = r.Z1 - r.Z0;
                if ((w < 24 && h < 24) || depth > 6) { blocks.Add(r); return; }
                float sw = depth < 2 ? 5 : 3.6f, f = M.Lerp(0.38f, 0.62f, R.F());
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
            foreach (var (x0, x1) in new[] { (inner.X0, gx - 4), (gx + 4, inner.X1) })
                foreach (var (z0, z1) in new[] { (inner.Z0, crossZ - 3.25f), (crossZ + 3.25f, inner.Z1) })
                    if (x1 - x0 > 8 && z1 - z0 > 8) Split(new Rect4(x0, x1, z0, z1), 1);

            var road = new Rect4(MathF.Min(city.Road.Ax, city.Road.Bx), MathF.Max(city.Road.Ax, city.Road.Bx), cz0 - 3, cz0 + 3);
            var sqr = new Rect4(sq.X - sq.Hx, sq.X + sq.Hx, sq.Z - sq.Hz, sq.Z + sq.Hz);
            var citR = cit.Rect;

            var homes = defs.Where(d => d.Kind == "home").ToList();
            var extraKinds = new[] { "tavern", "blacksmith", "destroyed", "tower_B" };
            var extra = defs.Where(d => extraKinds.Contains(d.Kind)).ToList();
            CityDef ByKind(string k) => defs.FirstOrDefault(d => d.Kind == k);
            void Place(CityDef def, float x, float z, float rot)
            {
                float s = Heights[def.Kind] / def.H;
                bool turned = MathF.Abs(MathF.Sin(rot)) > 0.5f;
                city.Buildings.Add(new Building { Def = def, X = x, Z = z, Rot = rot, S = s, Hx = (turned ? def.D : def.W) * s * 0.46f, Hz = (turned ? def.W : def.D) * s * 0.46f, Top = Heights[def.Kind] * 0.9f });
            }
            /// Дома вдоль всех сторон квартала фасадом на улицу; дом, наезжающий на соседа, пропускаем.
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
                        CityDef def;
                        if (R.Next() < 0.1 && extra.Count > 0) def = extra[(int)(R.Next() * extra.Count)];
                        else def = homes[(int)(R.Next() * homes.Count)];
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
                        t += fw + 0.2f + R.F() * 0.7f; // дома стоят стена к стене — это город, а не деревня
                    }
                }
            }
            foreach (var b in blocks)
            {
                if (R.Next() < 0.06 && !Overlaps(b, citR, 3)) { city.Gardens.Add(b); continue; }
                FillBlock(b);
            }
            city.DebugBlocks = blocks.Count; city.DebugPlaced = city.Buildings.Count;
            // Дома, попавшие на холм детинца, въезд или площадь, убираем
            Rect4 RectOf(Building bd) => new Rect4(bd.X - bd.Hx, bd.X + bd.Hx, bd.Z - bd.Hz, bd.Z + bd.Hz);
            city.Buildings = city.Buildings.Where(bd => { var r = RectOf(bd); return !Overlaps(r, citR, 3) && !Overlaps(r, road, 1.5f) && !Overlaps(r, sqr, 0.5f); }).ToList();
            // Площадь: колодец и рынок; в детинце — собор, башня и терема
            var wl = ByKind("well"); if (wl != null) Place(wl, sq.X, sq.Z, 0);
            var mk = ByKind("market"); if (mk != null) Place(mk, sq.X + sq.Hx * 0.55f, sq.Z - sq.Hz * 0.4f, M.PI);
            var ch = ByKind("church"); if (ch != null) Place(ch, (cit.X0 + cit.X1) / 2 + cs * 3, cz0, cs > 0 ? -M.PI / 2 : M.PI / 2);
            var tw = ByKind("tower_B"); if (tw != null) Place(tw, cs > 0 ? cit.X1 - 7 : cit.X0 + 7, cit.Z1 - 7, 0);
            for (int k = 0; k < 2; k++)
            {
                var d = homes[(int)(R.Next() * homes.Count)];
                float px = M.Lerp(cit.X0 + 8, cit.X1 - 8, R.F());
                Place(d, px, k == 1 ? cit.Z0 + 6.5f : cit.Z1 - 6.5f, k == 1 ? M.PI : 0);
            }
            // Дома не должны залезать на улицы, площадь и въезд
            city.Buildings = city.Buildings.Where(bd => !city.Streets.Any(st => M.SegDist(bd.X, bd.Z, st.Ax, st.Az, st.Bx, st.Bz) < st.W / 2 + MathF.Min(bd.Hx, bd.Hz) * 0.6f)).ToList();

            // Баррикады на главной улице у ворот и в переулках
            foreach (float dz in new[] { -1f, 1f })
            {
                float z = dz * (CZ - 16);
                city.Walls.Add(new FeatureWall { Ax = gx - 4, Az = z, Bx = gx - 1.5f, Bz = z, H = 1.3f, T = 0.6f });
                city.Walls.Add(new FeatureWall { Ax = gx + 1.5f, Az = z, Bx = gx + 4, Bz = z, H = 1.3f, T = 0.6f });
            }
            for (int k = 0; k < 6; k++)
            {
                int si = 4 + (int)(R.Next() * (city.Streets.Count - 4));
                var st = si < city.Streets.Count ? city.Streets[si] : null;
                if (st == null || st.W > 5.5f) continue;
                float t = M.Lerp(0.3f, 0.7f, R.F()), x = M.Lerp(st.Ax, st.Bx, t), z = M.Lerp(st.Az, st.Bz, t), vx = MathF.Abs(st.Bx - st.Ax) < 0.1f ? 1 : 0, vz = 1 - vx;
                city.Walls.Add(new FeatureWall { Ax = x - vx * st.W / 2, Az = z - vz * st.W / 2, Bx = x - vx * 0.9f, Bz = z - vz * 0.9f, H = 1.2f, T = 0.55f });
            }
            // Бочки и ящики вдоль улиц
            var props = defs.Where(d => d.Kind == "prop").ToList();
            for (int k = 0; k < 60 && props.Count > 0; k++)
            {
                var st = city.Streets[(int)(R.Next() * city.Streets.Count)];
                float t = R.F();
                float x = M.Lerp(st.Ax, st.Bx, t), z = M.Lerp(st.Az, st.Bz, t), side = R.Next() < 0.5 ? -1 : 1;
                bool vertical = MathF.Abs(st.Bx - st.Ax) < 0.1f;
                float px = vertical ? x + side * (st.W / 2 - 0.7f) : x, pz = vertical ? z : z + side * (st.W / 2 - 0.7f);
                if (MathF.Abs(px) > inner.X1 || MathF.Abs(pz) > inner.Z1 || Overlaps(new Rect4(px, px, pz, pz), citR, 1)) continue;
                city.Props.Add(new Prop { Def = props[(int)(R.Next() * props.Count)], X = px, Z = pz, Rot = R.F() * M.PI * 2 });
            }
            return city;
        }
    }
}
