using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BattleSim.Core
{
    /// <summary>Геометрия без движка: позиции, нормали, UV, цвета вершин (линейные), кости и веса (по 4 на вершину).</summary>
    public sealed class MeshData
    {
        public float[] Pos, Nrm, Uv, Col;
        public int[] Joints;
        public float[] Weights;
        public int[] Idx;
        public int Image = -1; // картинка материала (индекс в документе-источнике)
        public int VertexCount => Pos.Length / 3;

        public void Bounds(out V3 min, out V3 max)
        {
            min = new V3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            max = new V3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < Pos.Length; i += 3)
            {
                min.x = MathF.Min(min.x, Pos[i]); min.y = MathF.Min(min.y, Pos[i + 1]); min.z = MathF.Min(min.z, Pos[i + 2]);
                max.x = MathF.Max(max.x, Pos[i]); max.y = MathF.Max(max.y, Pos[i + 1]); max.z = MathF.Max(max.z, Pos[i + 2]);
            }
        }

        public void Transform(Mat4 m)
        {
            for (int i = 0; i < Pos.Length; i += 3)
            {
                var p = m.Point(Pos[i], Pos[i + 1], Pos[i + 2]);
                Pos[i] = p.x; Pos[i + 1] = p.y; Pos[i + 2] = p.z;
                if (Nrm != null)
                {
                    var n = m.Dir(Nrm[i], Nrm[i + 1], Nrm[i + 2]).Normalized;
                    Nrm[i] = n.x; Nrm[i + 1] = n.y; Nrm[i + 2] = n.z;
                }
            }
        }

        public static MeshData Merge(IList<MeshData> parts)
        {
            int nv = parts.Sum(p => p.VertexCount), ni = parts.Sum(p => p.Idx.Length);
            bool uv = parts.Any(p => p.Uv != null), col = parts.Any(p => p.Col != null), skin = parts.Any(p => p.Joints != null);
            var m = new MeshData { Pos = new float[nv * 3], Nrm = new float[nv * 3], Idx = new int[ni], Image = parts.Count > 0 ? parts[0].Image : -1 };
            if (uv) m.Uv = new float[nv * 2];
            if (col) m.Col = new float[nv * 3];
            if (skin) { m.Joints = new int[nv * 4]; m.Weights = new float[nv * 4]; }
            int vo = 0, io = 0;
            foreach (var p in parts)
            {
                int n = p.VertexCount;
                Array.Copy(p.Pos, 0, m.Pos, vo * 3, n * 3);
                if (p.Nrm != null) Array.Copy(p.Nrm, 0, m.Nrm, vo * 3, n * 3);
                if (uv && p.Uv != null) Array.Copy(p.Uv, 0, m.Uv, vo * 2, n * 2);
                if (col)
                {
                    if (p.Col != null) Array.Copy(p.Col, 0, m.Col, vo * 3, n * 3);
                    else for (int i = 0; i < n * 3; i++) m.Col[vo * 3 + i] = 1;
                }
                if (skin && p.Joints != null) { Array.Copy(p.Joints, 0, m.Joints, vo * 4, n * 4); Array.Copy(p.Weights, 0, m.Weights, vo * 4, n * 4); }
                for (int i = 0; i < p.Idx.Length; i++) m.Idx[io + i] = p.Idx[i] + vo;
                vo += n; io += p.Idx.Length;
            }
            return m;
        }

        public static MeshData FromPrim(GPrim p, GMaterial mat)
        {
            int n = p.Pos.Length / 3;
            var m = new MeshData
            {
                Pos = (float[])p.Pos.Clone(), Nrm = p.Nrm != null ? (float[])p.Nrm.Clone() : new float[n * 3],
                Uv = p.Uv != null ? (float[])p.Uv.Clone() : null, Idx = (int[])p.Idx.Clone(),
                Joints = p.Joints != null ? (int[])p.Joints.Clone() : null, Weights = p.Weights != null ? (float[])p.Weights.Clone() : null,
                Image = mat != null ? mat.Image : -1,
            };
            if (mat != null && mat.Image < 0)
            { // материал без текстуры — цвет в вершины
                m.Col = new float[n * 3];
                for (int i = 0; i < n; i++) { m.Col[i * 3] = mat.BaseColor[0]; m.Col[i * 3 + 1] = mat.BaseColor[1]; m.Col[i * 3 + 2] = mat.BaseColor[2]; }
            }
            if (m.Weights != null)
                for (int i = 0; i < m.Weights.Length; i += 4)
                { // веса в сумме — единица
                    float s = m.Weights[i] + m.Weights[i + 1] + m.Weights[i + 2] + m.Weights[i + 3];
                    if (s > 0) for (int k = 0; k < 4; k++) m.Weights[i + k] /= s;
                }
            return m;
        }
    }

    /// <summary>Поза скелета: TRS узлов (покой + анимации) и мировые матрицы.</summary>
    public sealed class Pose
    {
        readonly GltfDoc doc;
        readonly float[][] t, r, s;
        public readonly Mat4[] World;
        public Mat4 Root = Mat4.Identity;
        readonly Dictionary<string, int> byName = new Dictionary<string, int>();

        /// <summary>
        /// Пропорции людей KayKit не кукольные: голова меньше, ноги, корпус и руки длиннее. Кость тянется только
        /// вдоль себя (её потомки сдвигаются дальше, но не раздуваются), голова — целиком со шлемом.
        /// Клипы не меняются: это поправка к позе. У коней другие имена костей — их не касается.
        /// </summary>
        public static readonly Dictionary<string, float> Stretch = new Dictionary<string, float>
        {
            ["upperleg.l"] = 1.35f, ["upperleg.r"] = 1.35f, ["lowerleg.l"] = 1.35f, ["lowerleg.r"] = 1.35f,
            ["spine"] = 1.2f, ["upperarm.l"] = 1.1f, ["upperarm.r"] = 1.1f, ["lowerarm.l"] = 1.1f, ["lowerarm.r"] = 1.1f,
        };
        public static readonly Dictionary<string, float> Size = new Dictionary<string, float> { ["head"] = 0.7f };
        readonly float[] len, size;
        readonly float[][] dir;

        public Pose(GltfDoc d)
        {
            doc = d;
            int n = d.Nodes.Count;
            t = new float[n][]; r = new float[n][]; s = new float[n][];
            World = new Mat4[n];
            len = new float[n]; size = new float[n]; dir = new float[n][];
            for (int i = 0; i < n; i++) { t[i] = new float[3]; r[i] = new float[4]; s[i] = new float[3]; if (!byName.ContainsKey(d.Nodes[i].Name)) byName[d.Nodes[i].Name] = i; }
            for (int i = 0; i < n; i++)
            {
                var nd = d.Nodes[i];
                len[i] = 1; size[i] = Size.TryGetValue(nd.Name ?? "", out float sz) ? sz : 1;
                if (!Stretch.TryGetValue(nd.Name ?? "", out float f) || nd.Children.Count == 0) continue;
                // ось кости — к самому дальнему потомку-суставу в позе покоя
                var c = nd.Children.OrderByDescending(ch => ch.T[0] * ch.T[0] + ch.T[1] * ch.T[1] + ch.T[2] * ch.T[2]).First();
                float l = MathF.Sqrt(c.T[0] * c.T[0] + c.T[1] * c.T[1] + c.T[2] * c.T[2]);
                if (l < 1e-6f) continue;
                len[i] = f; dir[i] = new[] { c.T[0] / l, c.T[1] / l, c.T[2] / l };
            }
            Reset();
        }

        /// <summary>Матрица скиннинга кости: мир сустава, растянутый вдоль кости (для вершин, привязанных к ней).</summary>
        public Mat4 SkinMat(int i)
        {
            if (len[i] == 1) return World[i];
            var d = dir[i]; float k = len[i] - 1;
            var st = new Mat4();
            for (int col = 0; col < 3; col++)
                for (int row = 0; row < 3; row++) st.E[col * 4 + row] += k * d[row] * d[col];
            return World[i] * st;
        }

        public void Reset()
        {
            for (int i = 0; i < doc.Nodes.Count; i++)
            {
                var nd = doc.Nodes[i];
                Array.Copy(nd.T, t[i], 3); Array.Copy(nd.R, r[i], 4); Array.Copy(nd.S, s[i], 3);
            }
        }

        /// <summary>Применяет клип (возможно, из другого файла с тем же скелетом — узлы ищутся по имени).</summary>
        public void Apply(GltfDoc src, GAnim a, float time, Func<string, bool> filter)
        {
            foreach (var ch in a.Channels)
            {
                string name = src.Nodes[ch.Node].Name;
                if (filter != null && !filter(name)) continue;
                if (!byName.TryGetValue(name, out int ni)) continue;
                var tm = ch.Times;
                int w = ch.Path == 1 ? 4 : 3;
                var dst = ch.Path == 0 ? t[ni] : ch.Path == 1 ? r[ni] : s[ni];
                int last = tm.Length - 1;
                if (time <= tm[0] || last == 0) { Array.Copy(ch.Values, 0, dst, 0, w); continue; }
                if (time >= tm[last]) { Array.Copy(ch.Values, last * w, dst, 0, w); continue; }
                int lo = 0, hi = last;
                while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (tm[mid] <= time) lo = mid; else hi = mid; }
                float k = ch.Step ? 0 : (time - tm[lo]) / (tm[hi] - tm[lo]);
                if (ch.Path == 1) Quat.Slerp(ch.Values, lo * 4, hi * 4, k, dst, 0);
                else for (int c = 0; c < 3; c++) dst[c] = M.Lerp(ch.Values[lo * 3 + c], ch.Values[hi * 3 + c], k);
            }
        }

        public void Compute()
        {
            foreach (var root in doc.Roots) Walk(root, Root);
        }

        void Walk(GNode n, Mat4 parent)
        {
            int i = n.Index;
            float tx = t[i][0], ty = t[i][1], tz = t[i][2], z = size[i];
            var p = n.Parent;
            if (p != null && len[p.Index] != 1)
            { // родитель вытянут вдоль своей оси — сустав уезжает дальше по ней
                var d = dir[p.Index]; float along = (tx * d[0] + ty * d[1] + tz * d[2]) * (len[p.Index] - 1);
                tx += d[0] * along; ty += d[1] * along; tz += d[2] * along;
            }
            var local = n.Matrix ?? Mat4.Compose(tx, ty, tz, r[i][0], r[i][1], r[i][2], r[i][3], s[i][0] * z, s[i][1] * z, s[i][2] * z);
            World[i] = parent * local;
            foreach (var c in n.Children) Walk(c, World[i]);
        }
    }

    /// <summary>Слой клипа при запекании: какой клип, из какого файла и для каких костей.</summary>
    public sealed class ClipLayer
    {
        public GltfDoc Src;
        public GAnim Anim;
        public Func<string, bool> Filter;
    }

    public sealed class ClipDef
    {
        public string Name;
        public List<ClipLayer> Layers = new List<ClipLayer>();
        public bool Loop = true;
    }

    public sealed class ClipInfo { public int Start, Frames; public bool Loop; public float Dur; }

    /// <summary>Скиннованная модель, готовая к запеканию: меш, кости, их обратные матрицы привязки и корневое преобразование.</summary>
    public sealed class SkinnedModel
    {
        public GltfDoc Doc;
        public MeshData Mesh;
        public int[] BoneNodes;
        public Mat4[] BoneInv;
        public Mat4 Bind = Mat4.Identity;
        public Mat4 Root = Mat4.Identity;
        public float Height;
        public V3 Min, Max; // габариты в позе покоя (после масштаба)
        /// <summary>Из каких узлов собран меш: имя, род (0 тело, 1 носимое — плащ, шлем, шапка; 2 в руках), первая вершина, число.</summary>
        public List<(string Name, int Kind, int Start, int Count)> Parts = new List<(string, int, int, int)>();
        /// <summary>Опорные вершины для поправки по земле: крайние точки каждой кости по родам частей (см. Bake).</summary>
        public int[][] Probe;
    }

    /// <summary>Запечённые анимации: для каждого кадра — матрицы костей (строка = кадр).</summary>
    public sealed class BakedAnims
    {
        public const int Fps = 30;
        public int Bones, Rows;
        public float[] Data; // Rows × Bones × 16 (матрицы по столбцам, как в three.js)
        public Dictionary<string, ClipInfo> Clips = new Dictionary<string, ClipInfo>();

        public int Row(AnimState st)
        {
            if (!Clips.TryGetValue(st.Name, out var c)) return 0;
            int f = (int)MathF.Floor(st.T * Fps);
            if (f < 0) f = 0;
            f = c.Loop && !st.Once ? f % c.Frames : Math.Min(f, c.Frames - 1);
            return c.Start + f;
        }

        public float Dur(string name) => Clips.TryGetValue(name, out var c) ? c.Dur : 0;
    }

    public static class ModelKit
    {
        public static readonly Regex UpperBones = new Regex("^(upperarm|lowerarm|wrist|hand|handslot|chest|head)");

        static List<GNode> Traverse(GltfDoc doc, HashSet<GNode> drop)
        {
            var list = new List<GNode>();
            void Walk(GNode n) { if (drop != null && drop.Contains(n)) return; list.Add(n); foreach (var c in n.Children) Walk(c); }
            foreach (var r in doc.Roots) Walk(r);
            return list;
        }

        static MeshData MeshOf(GltfDoc doc, GNode n)
        {
            var parts = doc.Meshes[n.Mesh].Prims.Select(p => MeshData.FromPrim(p, p.Material >= 0 ? doc.Materials[p.Material] : null)).ToList();
            return parts.Count == 1 ? parts[0] : MeshData.Merge(parts);
        }

        /// <summary>
        /// Шаблон персонажа: оставляет нужное оружие и превращает все части (тело, шлем, щит, меч)
        /// в ОДИН скиннованный меш. Рост — targetHeight, ступни на нуле.
        /// </summary>
        public static SkinnedModel PrepareCharacter(GltfDoc doc, string[] keep, float targetHeight)
        {
            var drop = new HashSet<GNode>(doc.Nodes.Where(n => Defs.AllAttachments.Contains(n.Name) && !keep.Contains(n.Name)));
            var nodes = Traverse(doc, drop);
            var skinned = nodes.Where(n => n.Mesh >= 0 && n.Skin >= 0).ToList();
            var rigid = nodes.Where(n => n.Mesh >= 0 && n.Skin < 0).ToList();
            var refN = skinned[0];
            var skin = doc.Skins[refN.Skin];
            var pose = new Pose(doc);
            pose.Compute();
            var W = pose.World;
            var boneIndex = new Dictionary<int, int>();
            for (int i = 0; i < skin.Joints.Length; i++) boneIndex[skin.Joints[i]] = i;
            var boneInv = new Mat4[skin.Joints.Length];
            for (int i = 0; i < boneInv.Length; i++) boneInv[i] = Mat4.FromArray(skin.InvBind, i * 16);
            var refBind = W[refN.Index];
            var invRefBind = refBind.Inverse();
            var parts = new List<MeshData>();
            var names = new List<string>();
            var kinds = new List<int>();
            foreach (var part in skinned)
            {
                var m = MeshOf(doc, part);
                var ps = doc.Skins[part.Skin];
                for (int i = 0; i < m.Joints.Length; i++)
                    m.Joints[i] = boneIndex.TryGetValue(ps.Joints[m.Joints[i]], out int bi) ? bi : 0;
                m.Transform(invRefBind * W[part.Index]);
                parts.Add(m); names.Add(part.Name); kinds.Add(Defs.AllAttachments.Contains(part.Name) ? 1 : 0);
            }
            foreach (var mesh in rigid)
            {
                var bone = mesh.Parent;
                while (bone != null && !boneIndex.ContainsKey(bone.Index)) bone = bone.Parent;
                if (bone == null) continue;
                int bi = boneIndex[bone.Index];
                var m = MeshOf(doc, mesh);
                int n = m.VertexCount;
                m.Joints = new int[n * 4]; m.Weights = new float[n * 4];
                for (int v = 0; v < n; v++) { m.Joints[v * 4] = bi; m.Weights[v * 4] = 1; }
                var L = W[bone.Index].Inverse() * W[mesh.Index];
                m.Transform(invRefBind * boneInv[bi].Inverse() * L);
                parts.Add(m); names.Add(mesh.Name);
                kinds.Add(bone.Name != null && (bone.Name.StartsWith("hand") || bone.Name.StartsWith("wrist")) ? 2 : Defs.AllAttachments.Contains(mesh.Name) ? 1 : 0);
            }
            var merged = MeshData.Merge(parts);
            merged.Image = parts[0].Image;
            var model = new SkinnedModel { Doc = doc, Mesh = merged, BoneNodes = skin.Joints, BoneInv = boneInv, Bind = refBind, Height = targetHeight };
            for (int i = 0, v = 0; i < parts.Count; v += parts[i].VertexCount, i++) model.Parts.Add((names[i], kinds[i], v, parts[i].VertexCount));
            model.Probe = GroundProbe(merged, model.Parts);
            // Масштаб: заданный рост, ступни на нуле — по позе покоя с поправкой пропорций
            Skin(model, pose).Bounds(out var mn, out var mx);
            float k = targetHeight / (mx.y - mn.y);
            model.Root = Mat4.Translation(0, -mn.y * k, 0) * Mat4.Scale(k);
            model.Min = new V3(mn.x * k, 0, mn.z * k); model.Max = new V3(mx.x * k, (mx.y - mn.y) * k, mx.z * k);
            return model;
        }

        /// <summary>Конь: все части одного меша с цветами материалов в вершинах; длина 2,9 м, копыта на нуле.</summary>
        public static SkinnedModel PrepareAnimal(GltfDoc doc, float length)
        {
            var node = Traverse(doc, null).First(n => n.Mesh >= 0 && n.Skin >= 0);
            var skin = doc.Skins[node.Skin];
            var mesh = MeshOf(doc, node);
            var boneInv = new Mat4[skin.Joints.Length];
            for (int i = 0; i < boneInv.Length; i++) boneInv[i] = Mat4.FromArray(skin.InvBind, i * 16);
            var model = new SkinnedModel { Doc = doc, Mesh = mesh, BoneNodes = skin.Joints, BoneInv = boneInv, Bind = Mat4.Identity };
            // Поза покоя по правилам glTF: вершина = Σ w · (мир кости · обратная привязка) · v
            var pose = new Pose(doc);
            pose.Compute();
            var rest = Skin(model, pose);
            rest.Bounds(out var mn, out var mx);
            float k = length / MathF.Max(mx.x - mn.x, mx.z - mn.z);
            model.Root = Mat4.Translation(0, -mn.y * k, 0) * Mat4.Scale(k);
            model.Min = new V3(mn.x * k, 0, mn.z * k); model.Max = new V3(mx.x * k, (mx.y - mn.y) * k, mx.z * k);
            model.Height = (mx.y - mn.y) * k;
            model.Parts.Add(("body", 0, 0, mesh.VertexCount));
            model.Probe = GroundProbe(mesh, model.Parts);
            return model;
        }

        /// <summary>Скиннинг на процессоре (для габаритов и проверок): вершины в позе pose.</summary>
        public static MeshData Skin(SkinnedModel m, Pose pose)
        {
            var mats = new Mat4[m.BoneNodes.Length];
            for (int b = 0; b < mats.Length; b++) mats[b] = pose.SkinMat(m.BoneNodes[b]) * m.BoneInv[b] * m.Bind;
            var src = m.Mesh;
            var o = new MeshData { Pos = new float[src.Pos.Length], Idx = src.Idx };
            for (int v = 0; v < src.VertexCount; v++)
            {
                float x = 0, y = 0, z = 0;
                for (int k = 0; k < 4; k++)
                {
                    float w = src.Weights[v * 4 + k];
                    if (w == 0) continue;
                    var p = mats[src.Joints[v * 4 + k]].Point(src.Pos[v * 3], src.Pos[v * 3 + 1], src.Pos[v * 3 + 2]);
                    x += p.x * w; y += p.y * w; z += p.z * w;
                }
                o.Pos[v * 3] = x; o.Pos[v * 3 + 1] = y; o.Pos[v * 3 + 2] = z;
            }
            return o;
        }

        static readonly float[][] ProbeDirs = BuildProbeDirs();
        static float[][] BuildProbeDirs()
        {
            // 26 направлений: к граням, рёбрам и углам куба
            var l = new List<float[]>();
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                {
                    if (x == 0 && y == 0 && z == 0) continue;
                    float n = MathF.Sqrt(x * x + y * y + z * z);
                    l.Add(new[] { x / n, y / n, z / n });
                }
            return l.ToArray();
        }

        /// <summary>Для каждого рода частей — крайние вершины каждой кости по 26 направлениям: несколько сотен точек вместо тысяч вершин.</summary>
        static int[][] GroundProbe(MeshData m, List<(string Name, int Kind, int Start, int Count)> parts)
        {
            var res = new int[3][];
            for (int kind = 0; kind < 3; kind++)
            {
                var best = new Dictionary<(int, int), (float, int)>();
                foreach (var p in parts)
                {
                    if (p.Kind != kind) continue;
                    for (int v = p.Start; v < p.Start + p.Count; v++)
                    {
                        int bone = 0; float bw = -1;
                        for (int k = 0; k < 4; k++) if (m.Weights[v * 4 + k] > bw) { bw = m.Weights[v * 4 + k]; bone = m.Joints[v * 4 + k]; }
                        for (int d = 0; d < ProbeDirs.Length; d++)
                        {
                            var dir = ProbeDirs[d];
                            float s = m.Pos[v * 3] * dir[0] + m.Pos[v * 3 + 1] * dir[1] + m.Pos[v * 3 + 2] * dir[2];
                            if (!best.TryGetValue((bone, d), out var b) || s > b.Item1) best[(bone, d)] = (s, v);
                        }
                    }
                }
                res[kind] = best.Values.Select(b => b.Item2).Distinct().ToArray();
            }
            return res;
        }

        /// <summary>
        /// Сколько кадр должен подняться, чтобы не уходить в землю (и в настил моста): тело — не ниже земли, носимое
        /// (плащ, шлем) — не глубже 12 см, оружие в руках — не глубже 16 см (толщина доски настила).
        /// </summary>
        static float GroundLift(SkinnedModel m, float[] row)
        {
            if (m.Probe == null) return 0;
            float lift = 0;
            float[] tol = { 0f, 0.12f, 0.16f };
            var mesh = m.Mesh;
            for (int kind = 0; kind < 3; kind++)
            {
                float min = float.PositiveInfinity;
                foreach (int v in m.Probe[kind])
                {
                    float x = mesh.Pos[v * 3], y = mesh.Pos[v * 3 + 1], z = mesh.Pos[v * 3 + 2], yy = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        float w = mesh.Weights[v * 4 + k];
                        if (w == 0) continue;
                        int o = mesh.Joints[v * 4 + k] * 16;
                        yy += w * (row[o + 1] * x + row[o + 5] * y + row[o + 9] * z + row[o + 13]);
                    }
                    if (yy < min) min = yy;
                }
                if (-min - tol[kind] > lift) lift = -min - tol[kind];
            }
            return lift;
        }

        /// <summary>Запекаем позы: для каждого кадра — матрицы, переводящие вершину меша в пространство юнита.</summary>
        public static BakedAnims Bake(SkinnedModel m, IList<ClipDef> defs)
        {
            var pose = new Pose(m.Doc) { Root = m.Root };
            int nb = m.BoneNodes.Length;
            var rows = new List<float[]>();
            var baked = new BakedAnims { Bones = nb };
            foreach (var def in defs)
            {
                var layers = def.Layers.Where(l => l.Anim != null).ToList();
                if (layers.Count == 0) continue;
                float dur = layers[0].Anim.Duration;
                int frames = Math.Max(1, M.Round(dur * BakedAnims.Fps));
                baked.Clips[def.Name] = new ClipInfo { Start = rows.Count, Frames = frames, Loop = def.Loop, Dur = dur };
                for (int f = 0; f < frames; f++)
                {
                    float t = MathF.Min((float)f / BakedAnims.Fps, dur - 1e-4f);
                    pose.Reset();
                    foreach (var l in layers)
                    {
                        float d = l.Anim.Duration, lt = d > 0 && t >= d ? t % d : t;
                        pose.Apply(l.Src, l.Anim, lt, l.Filter);
                    }
                    pose.Compute();
                    var row = new float[nb * 16];
                    for (int b = 0; b < nb; b++)
                    {
                        var mm = pose.SkinMat(m.BoneNodes[b]) * m.BoneInv[b] * m.Bind;
                        Array.Copy(mm.E, 0, row, b * 16, 16);
                    }
                    // лёжа, падая и вздрагивая тело не уходит в землю и сквозь настил моста: кадр целиком поднимается
                    float lift = GroundLift(m, row);
                    if (lift > 0) for (int b = 0; b < nb; b++) row[b * 16 + 13] += lift;
                    rows.Add(row);
                }
            }
            baked.Rows = rows.Count;
            baked.Data = new float[rows.Count * nb * 16];
            for (int i = 0; i < rows.Count; i++) Array.Copy(rows[i], 0, baked.Data, i * nb * 16, nb * 16);
            return baked;
        }

        /// <summary>Мировая позиция узла в позе клипа (например, бёдра всадника в посадке).</summary>
        public static V3 NodeWorld(SkinnedModel m, string node, ClipLayer layer, float time)
        {
            var pose = new Pose(m.Doc) { Root = m.Root };
            pose.Reset();
            if (layer != null) pose.Apply(layer.Src, layer.Anim, time, layer.Filter);
            pose.Compute();
            var n = m.Doc.Find(node);
            return n == null ? new V3(0, 0, 0) : pose.World[n.Index].Point(0, 0, 0);
        }

        /// <summary>Упрощение меша кластеризацией вершин (для дальнего LOD). Цвета из разных клеток палитры не смешиваются.</summary>
        public static MeshData Decimate(MeshData src, float cell)
        {
            int n = src.VertexCount;
            var remap = new int[n];
            var keys = new Dictionary<(int, int, int, int, int, int), int>();
            var P = new List<float>(); var N = new List<float>(); var U = new List<float>(); var C = new List<float>(); var J = new List<int>(); var Wt = new List<float>();
            for (int i = 0; i < n; i++)
            {
                float x = src.Pos[i * 3], y = src.Pos[i * 3 + 1], z = src.Pos[i * 3 + 2];
                int s0 = 0, s1 = 0, s2 = 0;
                if (src.Uv != null) { s0 = M.Floor(src.Uv[i * 2] * 8); s1 = M.Floor(src.Uv[i * 2 + 1] * 4); s2 = -1; }
                else if (src.Col != null) { s0 = M.Round(src.Col[i * 3] * 20); s1 = M.Round(src.Col[i * 3 + 1] * 20); s2 = M.Round(src.Col[i * 3 + 2] * 20); }
                var key = (M.Floor(x / cell), M.Floor(y / cell), M.Floor(z / cell), s0, s1, s2);
                if (!keys.TryGetValue(key, out int k))
                {
                    k = P.Count / 3;
                    keys[key] = k;
                    P.Add(x); P.Add(y); P.Add(z); N.Add(0); N.Add(0); N.Add(0);
                    if (src.Uv != null) { U.Add(src.Uv[i * 2]); U.Add(src.Uv[i * 2 + 1]); }
                    if (src.Col != null) { C.Add(src.Col[i * 3]); C.Add(src.Col[i * 3 + 1]); C.Add(src.Col[i * 3 + 2]); }
                    for (int q = 0; q < 4; q++) { J.Add(src.Joints[i * 4 + q]); Wt.Add(src.Weights[i * 4 + q]); }
                }
                N[k * 3] += src.Nrm[i * 3]; N[k * 3 + 1] += src.Nrm[i * 3 + 1]; N[k * 3 + 2] += src.Nrm[i * 3 + 2];
                remap[i] = k;
            }
            var idx = new List<int>();
            for (int t = 0; t < src.Idx.Length; t += 3)
            {
                int a = remap[src.Idx[t]], b = remap[src.Idx[t + 1]], c = remap[src.Idx[t + 2]];
                if (a != b && b != c && a != c) { idx.Add(a); idx.Add(b); idx.Add(c); }
            }
            for (int k = 0; k < N.Count; k += 3)
            {
                float l = M.Hypot(N[k], N[k + 1], N[k + 2]);
                if (l == 0) l = 1;
                N[k] /= l; N[k + 1] /= l; N[k + 2] /= l;
            }
            return new MeshData
            {
                Pos = P.ToArray(), Nrm = N.ToArray(), Uv = src.Uv != null ? U.ToArray() : null, Col = src.Col != null ? C.ToArray() : null,
                Joints = J.ToArray(), Weights = Wt.ToArray(), Idx = idx.ToArray(), Image = src.Image,
            };
        }

        /// <summary>Статичная модель одним мешем: центр по x/z, основание на нуле. height — исходная высота.</summary>
        public static MeshData MergeStatic(GltfDoc doc, out float w, out float d, out float height)
        {
            var pose = new Pose(doc);
            pose.Compute();
            var parts = new List<MeshData>();
            foreach (var n in Traverse(doc, null))
            {
                if (n.Mesh < 0) continue;
                var m = MeshOf(doc, n);
                m.Joints = null; m.Weights = null;
                m.Transform(pose.World[n.Index]);
                parts.Add(m);
            }
            var merged = MeshData.Merge(parts);
            merged.Image = parts.Count > 0 ? parts[0].Image : -1;
            merged.Bounds(out var mn, out var mx);
            merged.Transform(Mat4.Translation(-(mn.x + mx.x) / 2, -mn.y, -(mn.z + mx.z) / 2));
            w = mx.x - mn.x; d = mx.z - mn.z; height = mx.y - mn.y;
            return merged;
        }

        // ---------------------------------------------------------------- перекраска текстур (RGBA, строки сверху вниз)

        /// <summary>Клетки текстуры-палитры (8×4), которые перекрашиваются в цвет команды.</summary>
        public static int[][] TeamCells(string model)
        {
            switch (model)
            {
                case "Knight":
                case "Skeleton": return new[] { new[] { 0, 1 }, new[] { 2, 2 } }; // скелет Нави — атлас Knight
                case "Barbarian": return new[] { new[] { 0, 1 }, new[] { 1, 1 }, new[] { 2, 2 } };
                default: return new[] { new[] { 0, 1 }, new[] { 1, 1 }, new[] { 1, 2 } };
            }
        }

        static byte B(float v) => (byte)M.Clamp(MathF.Round(v * 255), 0, 255);

        /// <summary>Перекрашивает клетки палитры в оттенок команды, сохраняя градиент светлоты.</summary>
        public static void RecolorCells(byte[] rgba, int w, int h, int[][] cells, TeamDef team)
        {
            int cw = w / 8, ch = h / 4;
            foreach (var c in cells)
                for (int y = c[1] * ch; y < (c[1] + 1) * ch; y++)
                    for (int x = c[0] * cw; x < (c[0] + 1) * cw; x++)
                    {
                        int i = (y * w + x) * 4;
                        new Rgb(rgba[i] / 255f, rgba[i + 1] / 255f, rgba[i + 2] / 255f).ToHsl(out _, out float s, out float l);
                        var o = Rgb.FromHsl(team.Hue / 360f, MathF.Max(s, team.Sat), M.Clamp(l * 0.95f + 0.03f, 0.08f, 0.8f));
                        rgba[i] = B(o.r); rgba[i + 1] = B(o.g); rgba[i + 2] = B(o.b);
                    }
        }

        /// <summary>
        /// Облик расы: кожа (клетки палитры (0,0) и (1,0) у всех моделей KayKit) и сталь рыцаря (3,0) перекрашиваются
        /// в оттенок расы с сохранением светлоты — зелёные орки, ржавое железо.
        /// </summary>
        public static void RecolorRace(byte[] rgba, int w, int h, string model, RaceDef race)
        {
            if (race == null) return;
            int cw = w / 8, ch = h / 4;
            void Cell(int cx, int cy, float hue, float sat, float lit = 1)
            {
                for (int y = cy * ch; y < (cy + 1) * ch; y++)
                    for (int x = cx * cw; x < (cx + 1) * cw; x++)
                    {
                        int i = (y * w + x) * 4;
                        new Rgb(rgba[i] / 255f, rgba[i + 1] / 255f, rgba[i + 2] / 255f).ToHsl(out _, out _, out float l);
                        var o = Rgb.FromHsl(hue / 360f, sat, l * lit);
                        rgba[i] = B(o.r); rgba[i + 1] = B(o.g); rgba[i + 2] = B(o.b);
                    }
            }
            if (race.SkinHue >= 0) { Cell(0, 0, race.SkinHue, race.SkinSat, race.SkinLit); Cell(1, 0, race.SkinHue, race.SkinSat, race.SkinLit); }
            if (race.MetalHue >= 0 && (model == "Knight" || model == "Skeleton")) Cell(3, 0, race.MetalHue, race.MetalSat);
        }

        /// <summary>Лёгкий оттенок команды на всём солдате — так армии различимы издалека.</summary>
        public static Rgb TeamTint(TeamDef team) => new Rgb(1, 1, 1).Lerp(Rgb.FromHsl(team.Hue / 360f, 0.7f, 0.6f), 0.14f);

        /// <summary>Меняет цвет листвы под биом: осень, зима, степь.</summary>
        public static void RecolorFoliage(byte[] rgba, LeafShift mode)
        {
            if (mode == LeafShift.Summer) return;
            for (int i = 0; i < rgba.Length; i += 4)
            {
                new Rgb(rgba[i] / 255f, rgba[i + 1] / 255f, rgba[i + 2] / 255f).ToHsl(out float hh, out float s, out float l);
                float hue = hh * 360;
                if (hue < 60 || hue > 170 || s < 0.15f) continue;
                float k = (hue - 60) / 110;
                Rgb o;
                if (mode == LeafShift.Autumn) o = Rgb.FromHsl((10 + k * 36) / 360, MathF.Max(s, 0.62f), l * 1.05f);
                else if (mode == LeafShift.Winter) o = Rgb.FromHsl(0.58f, s * 0.2f, M.Clamp(l * 0.45f + 0.52f, 0, 0.95f));
                else o = Rgb.FromHsl((42 + k * 20) / 360, MathF.Min(0.6f, s * 0.8f), l * 1.08f);
                rgba[i] = B(o.r); rgba[i + 1] = B(o.g); rgba[i + 2] = B(o.b);
            }
        }
    }

    public static class MeshDataExt
    {
        public static MeshData Clone(this MeshData m) => new MeshData
        {
            Pos = (float[])m.Pos.Clone(), Nrm = m.Nrm != null ? (float[])m.Nrm.Clone() : null, Uv = m.Uv, Col = m.Col,
            Joints = m.Joints, Weights = m.Weights, Idx = m.Idx, Image = m.Image,
        };
    }
}
