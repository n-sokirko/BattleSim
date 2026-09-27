using System;
using System.Collections.Generic;

namespace BattleSim.Core
{
    /// <summary>Собирает меш из коробок с цветами вершин (кладка стен, доски мостов, камни оград).</summary>
    public sealed class BoxBuilder
    {
        readonly List<float> pos = new List<float>(), nrm = new List<float>(), col = new List<float>();
        readonly List<int> idx = new List<int>();

        // Грани единичного куба: нормаль и 4 угла (против часовой стрелки снаружи)
        static readonly float[][] Faces =
        {
            new float[] { 1, 0, 0,   1, -1, 1,  1, -1, -1,  1, 1, -1,  1, 1, 1 },
            new float[] { -1, 0, 0,  -1, -1, -1, -1, -1, 1, -1, 1, 1, -1, 1, -1 },
            new float[] { 0, 1, 0,   -1, 1, 1,  1, 1, 1,  1, 1, -1,  -1, 1, -1 },
            new float[] { 0, -1, 0,  -1, -1, -1, 1, -1, -1, 1, -1, 1, -1, -1, 1 },
            new float[] { 0, 0, 1,   -1, -1, 1, 1, -1, 1,  1, 1, 1,  -1, 1, 1 },
            new float[] { 0, 0, -1,  1, -1, -1, -1, -1, -1, -1, 1, -1, 1, 1, -1 },
        };

        public int Count => idx.Count / 36;

        /// <summary>Коробка размером (sx, sy, sz) с преобразованием m (центр куба в нуле).</summary>
        public void Box(Mat4 m, Rgb c)
        {
            foreach (var f in Faces)
            {
                int b = pos.Count / 3;
                var n = m.Dir(f[0], f[1], f[2]).Normalized;
                for (int k = 0; k < 4; k++)
                {
                    var p = m.Point(f[3 + k * 3] * 0.5f, f[4 + k * 3] * 0.5f, f[5 + k * 3] * 0.5f);
                    pos.Add(p.x); pos.Add(p.y); pos.Add(p.z);
                    nrm.Add(n.x); nrm.Add(n.y); nrm.Add(n.z);
                    col.Add(c.r); col.Add(c.g); col.Add(c.b);
                }
                idx.Add(b); idx.Add(b + 1); idx.Add(b + 2);
                idx.Add(b); idx.Add(b + 2); idx.Add(b + 3);
            }
        }

        /// <summary>Как BoxGeometry(sx,sy,sz).rotateY(yaw).translate(x,y,z) в веб-версии.</summary>
        public void Box(float x, float y, float z, float sx, float sy, float sz, float yaw, Rgb c) =>
            Box(Mat4.Translation(x, y, z) * Mat4.RotationY(yaw) * Mat4.Compose(0, 0, 0, 0, 0, 0, 1, sx, sy, sz), c);

        public MeshData Build() => new MeshData { Pos = pos.ToArray(), Nrm = nrm.ToArray(), Col = col.ToArray(), Idx = idx.ToArray() };
    }

    /// <summary>Геометрия мира: рельеф кусками, крепость, мосты, ограды, трава. Всё в координатах ядра.</summary>
    public static class WorldMeshes
    {
        /// <summary>Кусок рельефа: вершины сетки высот [x0..x0+n] × [z0..z0+n], UV — доля карты.</summary>
        public static MeshData TerrainChunk(World w, int x0, int z0, int n)
        {
            int R = w.Res, nx = Math.Min(n, R - x0), nz = Math.Min(n, R - z0), V = nx + 1;
            var m = new MeshData { Pos = new float[V * (nz + 1) * 3], Nrm = new float[V * (nz + 1) * 3], Uv = new float[V * (nz + 1) * 2], Idx = new int[nx * nz * 6] };
            for (int z = 0; z <= nz; z++)
                for (int x = 0; x <= nx; x++)
                {
                    int gx = x0 + x, gz = z0 + z, i = z * V + x;
                    m.Pos[i * 3] = gx * w.Cell - w.Half; m.Pos[i * 3 + 1] = w.Hv(gx, gz); m.Pos[i * 3 + 2] = gz * w.Cell - w.Half;
                    float hl = w.Hv(Math.Max(gx - 1, 0), gz), hr = w.Hv(Math.Min(gx + 1, R), gz);
                    float hd = w.Hv(gx, Math.Max(gz - 1, 0)), hu = w.Hv(gx, Math.Min(gz + 1, R));
                    float ax = hl - hr, ay = 2 * w.Cell, az = hd - hu, l = M.Hypot(ax, ay, az);
                    m.Nrm[i * 3] = ax / l; m.Nrm[i * 3 + 1] = ay / l; m.Nrm[i * 3 + 2] = az / l;
                    m.Uv[i * 2] = (float)gx / R; m.Uv[i * 2 + 1] = (float)gz / R;
                }
            int k = 0;
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    int i00 = z * V + x, i10 = i00 + 1, i01 = i00 + V, i11 = i01 + 1;
                    m.Idx[k++] = i00; m.Idx[k++] = i01; m.Idx[k++] = i11;
                    m.Idx[k++] = i00; m.Idx[k++] = i11; m.Idx[k++] = i10;
                }
            return m;
        }

        /// <summary>Каменная кладка: стены с зубцами, башни, лестницы и арки ворот — один меш.</summary>
        public static MeshData Fortress(World w)
        {
            if (w.Town == null) return null;
            var F = w.Town.Fort;
            var bb = new BoxBuilder();
            var R = new Rng(4242);
            Rgb stone = Rgb.Hex(0x857d70), dark = Rgb.Hex(0x6c655b);
            void Box(float x, float y, float z, float sx, float sy, float sz, float yaw, Rgb c) => bb.Box(x, y, z, sx, sy, sz, yaw, c * M.Lerp(0.88f, 1.08f, R.F()));
            foreach (var wl in F.Walls)
            {
                float len = M.Hypot(wl.Bx - wl.Ax, wl.Bz - wl.Az), ux = (wl.Bx - wl.Ax) / len, uz = (wl.Bz - wl.Az) / len, yaw = MathF.Atan2(ux, uz) - M.PI / 2;
                for (float t = 0; t < len; t += 2)
                {
                    float l = MathF.Min(2.05f, len - t + 0.05f), x = wl.Ax + ux * (t + l / 2), z = wl.Az + uz * (t + l / 2), g = w.HeightAt(x, z);
                    float bas = g - 1.5f, top = g + wl.H;
                    Box(x, (bas + top) / 2, z, l, top - bas, wl.W, yaw, stone);
                }
                for (float t = 0.7f; t < len - 0.3f; t += 1.45f)
                {
                    float x = wl.Ax + ux * t + wl.Out.x * (wl.W / 2 - 0.28f), z = wl.Az + uz * t + wl.Out.z * (wl.W / 2 - 0.28f);
                    Box(x, w.HeightAt(wl.Ax + ux * t, wl.Az + uz * t) + wl.H + 0.55f, z, 0.75f, 1.1f, 0.55f, yaw, stone);
                }
            }
            foreach (var t in F.Towers)
            {
                float g = w.HeightAt(t.X, t.Z), bas = MathF.Min(g, MathF.Min(w.HeightAt(t.X + t.S / 2, t.Z + t.S / 2), w.HeightAt(t.X - t.S / 2, t.Z - t.S / 2))) - 1.5f, top = g + t.H;
                Box(t.X, (bas + top) / 2, t.Z, t.S, top - bas, t.S, 0, dark);
                Box(t.X, top - 0.25f, t.Z, t.S + 0.5f, 0.5f, t.S + 0.5f, 0, stone);
                foreach (var (ex, ez) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    for (int k = -2; k <= 2; k++)
                    {
                        float off = k * (t.S / 5);
                        Box(t.X + ex * (t.S / 2) + ez * off, top + 0.55f, t.Z + ez * (t.S / 2) + ex * off, ez != 0 ? 0.7f : 0.5f, 1.1f, ex != 0 ? 0.7f : 0.5f, 0, stone);
                    }
            }
            foreach (var r in F.Ramps)
            {
                float len = M.Hypot(r.Bx - r.Ax, r.Bz - r.Az), ux = (r.Bx - r.Ax) / len, uz = (r.Bz - r.Az) / len, yaw = MathF.Atan2(ux, uz) - M.PI / 2;
                int n = (int)MathF.Ceiling(len / 0.45f);
                for (int k = 0; k < n; k++)
                {
                    float t = (k + 0.5f) / n, x = r.Ax + ux * len * t, z = r.Az + uz * len * t, g = w.HeightAt(x, z), top = g + 0.15f + (r.H - 0.15f) * t;
                    Box(x, (top + g - 0.6f) / 2, z, len / n + 0.02f, top - g + 0.6f, r.W, yaw, stone);
                }
            }
            foreach (var a in F.Arches)
            {
                float g = w.HeightAt(a.X, a.Z), yaw = MathF.Atan2(a.Ux, a.Uz) - M.PI / 2;
                Box(a.X, g + a.H - 0.9f, a.Z, a.Citadel ? 7.6f : 8.6f, 1.8f, a.W, yaw, dark); // арка над воротами
            }
            return bb.Count > 0 ? bb.Build() : null;
        }

        /// <summary>Деревянные мосты: доски настила, перила и опоры до дна ущелья.</summary>
        public static MeshData Bridges(World w)
        {
            if (w.Bridges.Count == 0) return null;
            var bb = new BoxBuilder();
            var R = new Rng(777);
            Rgb wood = Rgb.Hex(0x7a5534), beam = Rgb.Hex(0x5b3e25);
            void Box(float x, float y, float z, float sx, float sy, float sz, float yaw, Rgb c) => bb.Box(x, y, z, sx, sy, sz, yaw, c * M.Lerp(0.85f, 1.1f, R.F()));
            foreach (var d in w.Bridges)
            {
                float yaw = MathF.Atan2(d.Ux, d.Uz), nx = -d.Uz, nz = d.Ux;
                int n = (int)MathF.Ceiling(d.Len / 0.55f);
                for (int k = 0; k < n; k++)
                {
                    float t = (k + 0.5f) / n, x = d.Ax + d.Ux * d.Len * t, z = d.Az + d.Uz * d.Len * t, y = M.Lerp(d.HA, d.HB, t);
                    Box(x, y - 0.08f, z, d.W, 0.16f, d.Len / n - 0.06f, yaw, wood);
                }
                foreach (float side in new[] { -1f, 1f })
                {
                    float ox = nx * side * (d.W / 2 - 0.15f), oz = nz * side * (d.W / 2 - 0.15f);
                    float mid = (d.HA + d.HB) / 2, pitch = MathF.Atan2(d.HB - d.HA, d.Len);
                    var rail = Mat4.Translation(d.X + ox, mid + 1.05f, d.Z + oz) * Mat4.RotationY(yaw)
                        * Mat4.Compose(0, 0, 0, MathF.Sin(-pitch / 2), 0, 0, MathF.Cos(-pitch / 2), 1, 1, 1) * Mat4.Compose(0, 0, 0, 0, 0, 0, 1, 0.12f, 0.12f, d.Len);
                    bb.Box(rail, new Rgb(0.4f, 0.4f, 0.4f));
                    for (float t = 0; t <= d.Len; t += 2.2f)
                    {
                        float x = d.Ax + d.Ux * t + ox, z = d.Az + d.Uz * t + oz, y = M.Lerp(d.HA, d.HB, t / d.Len);
                        Box(x, y + 0.55f, z, 0.14f, 1.1f, 0.14f, 0, beam);
                        if (t > 1.5f && t < d.Len - 1.5f && M.Round(t / 2.2f) % 2 == 0)
                        {
                            float bottom = w.HeightAt(x, z) - 1;
                            Box(x, (y + bottom) / 2, z, 0.35f, y - bottom, 0.35f, 0, beam); // опора до дна
                        }
                    }
                }
            }
            return bb.Build();
        }

        /// <summary>Каменные ограды, завалы и баррикады: камни рядами, верх местами осыпался.</summary>
        public static MeshData DryWalls(World w)
        {
            var R = new Rng(991);
            var stones = new List<(float x, float y, float z, float yaw, float sx, float sy, float sz, float c)>();
            foreach (var wl in w.Features.Walls)
            {
                float len = M.Hypot(wl.Bx - wl.Ax, wl.Bz - wl.Az);
                if (len < 0.3f) continue;
                float dx = (wl.Bx - wl.Ax) / len, dz = (wl.Bz - wl.Az) / len, yaw = MathF.Atan2(dx, dz);
                int courses = wl.H > 1.2f ? 3 : 2;
                for (int course = 0; course < courses; course++)
                {
                    float sy = course == 2 ? 0.3f : 0.52f, y0 = course == 0 ? 0.26f : course == 1 ? 0.78f : 1.2f;
                    for (float s = (course % 2) * 0.35f; s < len; s += M.Lerp(0.62f, 0.8f, R.F()))
                    {
                        if (course == courses - 1 && R.Next() < 0.35) continue; // верх ограды местами осыпался
                        float x = wl.Ax + dx * s, z = wl.Az + dz * s;
                        float yw = yaw + M.Lerp(-0.12f, 0.12f, R.F()), sx = M.Lerp(0.62f, 0.82f, R.F()), sz = wl.T * M.Lerp(0.9f, 1.1f, R.F()), c = M.Lerp(0.78f, 1.05f, R.F());
                        stones.Add((x, w.HeightAt(x, z) + y0, z, yw, sx, sy, sz, c));
                    }
                }
            }
            if (stones.Count == 0) return null;
            var bb = new BoxBuilder();
            var baseC = Rgb.Hex(0x8c8479);
            foreach (var st in stones)
            {
                float ex = M.Lerp(-0.05f, 0.05f, R.F()), ez = M.Lerp(-0.05f, 0.05f, R.F());
                var rot = Mat4.Compose(0, 0, 0, MathF.Sin(ex / 2), 0, 0, MathF.Cos(ex / 2), 1, 1, 1)
                        * Mat4.RotationY(st.yaw + M.PI / 2)
                        * Mat4.Compose(0, 0, 0, 0, 0, MathF.Sin(ez / 2), MathF.Cos(ez / 2), 1, 1, 1);
                bb.Box(Mat4.Translation(st.x, st.y, st.z) * rot * Mat4.Compose(0, 0, 0, 0, 0, 0, 1, st.sx, st.sy, st.sz), baseC * st.c);
            }
            return bb.Build();
        }

        /// <summary>Пучок травы: 5 изогнутых травинок с двух сторон (нормаль вверх).</summary>
        public static MeshData GrassTuft()
        {
            var pos = new List<float>();
            for (int b = 0; b < 5; b++)
            {
                float a = b * M.PI * 2 / 5 + (b % 2) * 0.4f;
                float sx = MathF.Cos(a), sz = MathF.Sin(a), ax = -sz * 0.05f, az = sx * 0.05f;
                float bx = sx * 0.06f, bz = sz * 0.06f, tx = sx * 0.22f, tz = sz * 0.22f, th = 0.42f + (b % 3) * 0.08f;
                pos.AddRange(new[] { bx - ax, 0, bz - az, bx + ax, 0, bz + az, tx, th, tz });
                pos.AddRange(new[] { bx + ax, 0, bz + az, bx - ax, 0, bz - az, tx, th, tz });
            }
            int n = pos.Count / 3;
            var m = new MeshData { Pos = pos.ToArray(), Nrm = new float[n * 3], Idx = new int[n] };
            for (int i = 0; i < n; i++) { m.Nrm[i * 3 + 1] = 1; m.Idx[i] = i; }
            return m;
        }

        /// <summary>Много копий меша (трава, цветы) с цветом каждой копии — в один меш.</summary>
        public static MeshData Scatter(MeshData proto, IList<Tuft> list, int from, int count)
        {
            int nv = proto.VertexCount, ni = proto.Idx.Length;
            var m = new MeshData { Pos = new float[count * nv * 3], Nrm = new float[count * nv * 3], Col = new float[count * nv * 3], Idx = new int[count * ni] };
            for (int k = 0; k < count; k++)
            {
                var t = list[from + k];
                var tr = Mat4.Translation(t.X, t.Y, t.Z) * Mat4.RotationY(t.Rot) * Mat4.Compose(0, 0, 0, 0, 0, 0, 1, t.Sx, t.Sy, t.Sz);
                for (int v = 0; v < nv; v++)
                {
                    var p = tr.Point(proto.Pos[v * 3], proto.Pos[v * 3 + 1], proto.Pos[v * 3 + 2]);
                    var n = tr.Dir(proto.Nrm[v * 3], proto.Nrm[v * 3 + 1], proto.Nrm[v * 3 + 2]).Normalized;
                    int o = (k * nv + v) * 3;
                    m.Pos[o] = p.x; m.Pos[o + 1] = p.y; m.Pos[o + 2] = p.z;
                    m.Nrm[o] = n.x; m.Nrm[o + 1] = n.y; m.Nrm[o + 2] = n.z;
                    m.Col[o] = t.Color.r; m.Col[o + 1] = t.Color.g; m.Col[o + 2] = t.Color.b;
                }
                for (int i = 0; i < ni; i++) m.Idx[k * ni + i] = proto.Idx[i] + k * nv;
            }
            return m;
        }

        /// <summary>Копия меша с преобразованием (для слияния домов города в один меш).</summary>
        public static MeshData Placed(MeshData src, float x, float y, float z, float rot, float k)
        {
            var m = src.Clone();
            m.Transform(Mat4.Translation(x, y, z) * Mat4.RotationY(rot) * Mat4.Scale(k));
            return m;
        }

        /// <summary>Икосаэдр радиуса r (цветок).</summary>
        public static MeshData Flower(float r = 0.07f)
        {
            float t = (1 + MathF.Sqrt(5)) / 2;
            float[] v = { -1, t, 0, 1, t, 0, -1, -t, 0, 1, -t, 0, 0, -1, t, 0, 1, t, 0, -1, -t, 0, 1, -t, t, 0, -1, t, 0, 1, -t, 0, -1, -t, 0, 1 };
            int[] f = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            var m = new MeshData { Pos = new float[f.Length * 3], Nrm = new float[f.Length * 3], Idx = new int[f.Length] };
            for (int i = 0; i < f.Length; i += 3)
            {
                var a = new V3(v[f[i] * 3], v[f[i] * 3 + 1], v[f[i] * 3 + 2]).Normalized;
                var b = new V3(v[f[i + 1] * 3], v[f[i + 1] * 3 + 1], v[f[i + 1] * 3 + 2]).Normalized;
                var c = new V3(v[f[i + 2] * 3], v[f[i + 2] * 3 + 1], v[f[i + 2] * 3 + 2]).Normalized;
                var n = ((a + b + c) * (1f / 3)).Normalized;
                foreach (var (p, k) in new[] { (a, 0), (b, 1), (c, 2) })
                {
                    m.Pos[(i + k) * 3] = p.x * r; m.Pos[(i + k) * 3 + 1] = p.y * r; m.Pos[(i + k) * 3 + 2] = p.z * r;
                    m.Nrm[(i + k) * 3] = n.x; m.Nrm[(i + k) * 3 + 1] = n.y; m.Nrm[(i + k) * 3 + 2] = n.z;
                    m.Idx[i + k] = i + k;
                }
            }
            return m;
        }
    }
}
