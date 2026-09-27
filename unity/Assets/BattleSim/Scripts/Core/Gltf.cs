using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BattleSim.Core
{
    // ------------------------------------------------------------------ матрицы и кватернионы (как в three.js: столбцы подряд)

    public sealed class Mat4
    {
        public readonly float[] E = new float[16];

        public Mat4() { E[0] = E[5] = E[10] = E[15] = 1; }
        public static Mat4 Identity => new Mat4();

        public static Mat4 FromArray(float[] a, int o)
        {
            var m = new Mat4();
            Array.Copy(a, o, m.E, 0, 16);
            return m;
        }

        public Mat4 Clone() { var m = new Mat4(); Array.Copy(E, m.E, 16); return m; }

        /// <summary>Позиция, поворот (кватернион x,y,z,w) и масштаб — как Matrix4.compose.</summary>
        public static Mat4 Compose(float tx, float ty, float tz, float qx, float qy, float qz, float qw, float sx, float sy, float sz)
        {
            var m = new Mat4();
            var te = m.E;
            float x2 = qx + qx, y2 = qy + qy, z2 = qz + qz;
            float xx = qx * x2, xy = qx * y2, xz = qx * z2;
            float yy = qy * y2, yz = qy * z2, zz = qz * z2;
            float wx = qw * x2, wy = qw * y2, wz = qw * z2;
            te[0] = (1 - (yy + zz)) * sx; te[1] = (xy + wz) * sx; te[2] = (xz - wy) * sx; te[3] = 0;
            te[4] = (xy - wz) * sy; te[5] = (1 - (xx + zz)) * sy; te[6] = (yz + wx) * sy; te[7] = 0;
            te[8] = (xz + wy) * sz; te[9] = (yz - wx) * sz; te[10] = (1 - (xx + yy)) * sz; te[11] = 0;
            te[12] = tx; te[13] = ty; te[14] = tz; te[15] = 1;
            return m;
        }

        public static Mat4 Translation(float x, float y, float z) => Compose(x, y, z, 0, 0, 0, 1, 1, 1, 1);
        public static Mat4 Scale(float s) => Compose(0, 0, 0, 0, 0, 0, 1, s, s, s);
        public static Mat4 RotationY(float a) => Compose(0, 0, 0, 0, MathF.Sin(a / 2), 0, MathF.Cos(a / 2), 1, 1, 1);

        /// <summary>a × b.</summary>
        public static Mat4 operator *(Mat4 a, Mat4 b)
        {
            var r = new Mat4();
            float[] ae = a.E, be = b.E, te = r.E;
            for (int c = 0; c < 4; c++)
                for (int row = 0; row < 4; row++)
                    te[c * 4 + row] = ae[row] * be[c * 4] + ae[4 + row] * be[c * 4 + 1] + ae[8 + row] * be[c * 4 + 2] + ae[12 + row] * be[c * 4 + 3];
            return r;
        }

        public Mat4 Inverse()
        {
            var te = E;
            float n11 = te[0], n21 = te[1], n31 = te[2], n41 = te[3];
            float n12 = te[4], n22 = te[5], n32 = te[6], n42 = te[7];
            float n13 = te[8], n23 = te[9], n33 = te[10], n43 = te[11];
            float n14 = te[12], n24 = te[13], n34 = te[14], n44 = te[15];
            float t11 = n23 * n34 * n42 - n24 * n33 * n42 + n24 * n32 * n43 - n22 * n34 * n43 - n23 * n32 * n44 + n22 * n33 * n44;
            float t12 = n14 * n33 * n42 - n13 * n34 * n42 - n14 * n32 * n43 + n12 * n34 * n43 + n13 * n32 * n44 - n12 * n33 * n44;
            float t13 = n13 * n24 * n42 - n14 * n23 * n42 + n14 * n22 * n43 - n12 * n24 * n43 - n13 * n22 * n44 + n12 * n23 * n44;
            float t14 = n14 * n23 * n32 - n13 * n24 * n32 - n14 * n22 * n33 + n12 * n24 * n33 + n13 * n22 * n34 - n12 * n23 * n34;
            float det = n11 * t11 + n21 * t12 + n31 * t13 + n41 * t14;
            var r = new Mat4();
            if (det == 0) { Array.Clear(r.E, 0, 16); return r; }
            float di = 1 / det;
            var o = r.E;
            o[0] = t11 * di;
            o[1] = (n24 * n33 * n41 - n23 * n34 * n41 - n24 * n31 * n43 + n21 * n34 * n43 + n23 * n31 * n44 - n21 * n33 * n44) * di;
            o[2] = (n22 * n34 * n41 - n24 * n32 * n41 + n24 * n31 * n42 - n21 * n34 * n42 - n22 * n31 * n44 + n21 * n32 * n44) * di;
            o[3] = (n23 * n32 * n41 - n22 * n33 * n41 - n23 * n31 * n42 + n21 * n33 * n42 + n22 * n31 * n43 - n21 * n32 * n43) * di;
            o[4] = t12 * di;
            o[5] = (n13 * n34 * n41 - n14 * n33 * n41 + n14 * n31 * n43 - n11 * n34 * n43 - n13 * n31 * n44 + n11 * n33 * n44) * di;
            o[6] = (n14 * n32 * n41 - n12 * n34 * n41 - n14 * n31 * n42 + n11 * n34 * n42 + n12 * n31 * n44 - n11 * n32 * n44) * di;
            o[7] = (n12 * n33 * n41 - n13 * n32 * n41 + n13 * n31 * n42 - n11 * n33 * n42 - n12 * n31 * n43 + n11 * n32 * n43) * di;
            o[8] = t13 * di;
            o[9] = (n14 * n23 * n41 - n13 * n24 * n41 - n14 * n21 * n43 + n11 * n24 * n43 + n13 * n21 * n44 - n11 * n23 * n44) * di;
            o[10] = (n12 * n24 * n41 - n14 * n22 * n41 + n14 * n21 * n42 - n11 * n24 * n42 - n12 * n21 * n44 + n11 * n22 * n44) * di;
            o[11] = (n13 * n22 * n41 - n12 * n23 * n41 - n13 * n21 * n42 + n11 * n23 * n42 + n12 * n21 * n43 - n11 * n22 * n43) * di;
            o[12] = t14 * di;
            o[13] = (n13 * n24 * n31 - n14 * n23 * n31 + n14 * n21 * n33 - n11 * n24 * n33 - n13 * n21 * n34 + n11 * n23 * n34) * di;
            o[14] = (n14 * n22 * n31 - n12 * n24 * n31 - n14 * n21 * n32 + n11 * n24 * n32 + n12 * n21 * n34 - n11 * n22 * n34) * di;
            o[15] = (n12 * n23 * n31 - n13 * n22 * n31 + n13 * n21 * n32 - n11 * n23 * n32 - n12 * n21 * n33 + n11 * n22 * n33) * di;
            return r;
        }

        public V3 Point(float x, float y, float z)
        {
            var e = E;
            float w = e[3] * x + e[7] * y + e[11] * z + e[15];
            if (w == 0) w = 1;
            return new V3((e[0] * x + e[4] * y + e[8] * z + e[12]) / w, (e[1] * x + e[5] * y + e[9] * z + e[13]) / w, (e[2] * x + e[6] * y + e[10] * z + e[14]) / w);
        }

        public V3 Dir(float x, float y, float z)
        {
            var e = E;
            return new V3(e[0] * x + e[4] * y + e[8] * z, e[1] * x + e[5] * y + e[9] * z, e[2] * x + e[6] * y + e[10] * z);
        }
    }

    public static class Quat
    {
        /// <summary>Сферическая интерполяция (x,y,z,w) — как Quaternion.slerpFlat в three.js.</summary>
        public static void Slerp(float[] src, int a, int b, float t, float[] dst, int o)
        {
            float x0 = src[a], y0 = src[a + 1], z0 = src[a + 2], w0 = src[a + 3];
            float x1 = src[b], y1 = src[b + 1], z1 = src[b + 2], w1 = src[b + 3];
            if (t <= 0) { dst[o] = x0; dst[o + 1] = y0; dst[o + 2] = z0; dst[o + 3] = w0; return; }
            if (t >= 1) { dst[o] = x1; dst[o + 1] = y1; dst[o + 2] = z1; dst[o + 3] = w1; return; }
            if (w0 != w1 || x0 != x1 || y0 != y1 || z0 != z1)
            {
                float s = 1 - t;
                float cos = x0 * x1 + y0 * y1 + z0 * z1 + w0 * w1, dir = cos >= 0 ? 1 : -1, sqrSin = 1 - cos * cos;
                if (sqrSin > 1.1920929e-7f)
                {
                    float sin = MathF.Sqrt(sqrSin), len = MathF.Atan2(sin, cos * dir);
                    s = MathF.Sin(s * len) / sin;
                    t = MathF.Sin(t * len) / sin;
                }
                float tDir = t * dir;
                x0 = x0 * s + x1 * tDir; y0 = y0 * s + y1 * tDir; z0 = z0 * s + z1 * tDir; w0 = w0 * s + w1 * tDir;
                if (s == 1 - t)
                {
                    float f = 1 / MathF.Sqrt(x0 * x0 + y0 * y0 + z0 * z0 + w0 * w0);
                    x0 *= f; y0 *= f; z0 *= f; w0 *= f;
                }
            }
            dst[o] = x0; dst[o + 1] = y0; dst[o + 2] = z0; dst[o + 3] = w0;
        }
    }

    // ------------------------------------------------------------------ JSON (минимальный разбор для glTF)

    public static class Json
    {
        public static object Parse(string s)
        {
            int i = 0;
            return Value(s, ref i);
        }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++;
                Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                for (;;)
                {
                    Ws(s, ref i);
                    string k = Str(s, ref i);
                    Ws(s, ref i); i++; // ':'
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; return d; // '}'
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++;
                Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                for (;;)
                {
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; return l;
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
        }

        static string Str(string s, ref int i)
        {
            i++; // '"'
            var sb = new StringBuilder();
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            i++;
            return sb.ToString();
        }
    }

    // ------------------------------------------------------------------ glTF 2.0 (GLB)

    public sealed class GNode
    {
        public int Index, Mesh = -1, Skin = -1;
        public string Name;
        public GNode Parent;
        public List<GNode> Children = new List<GNode>();
        public float[] T = { 0, 0, 0 }, R = { 0, 0, 0, 1 }, S = { 1, 1, 1 };
        public Mat4 Matrix; // если задана матрица вместо TRS
        public Mat4 Local() => Matrix ?? Mat4.Compose(T[0], T[1], T[2], R[0], R[1], R[2], R[3], S[0], S[1], S[2]);
    }

    public sealed class GPrim
    {
        public float[] Pos, Nrm, Uv;
        public int[] Joints;
        public float[] Weights;
        public int[] Idx;
        public int Material = -1;
    }

    public sealed class GMesh { public string Name; public List<GPrim> Prims = new List<GPrim>(); }
    public sealed class GSkin { public int[] Joints; public float[] InvBind; }
    public sealed class GMaterial { public string Name; public float[] BaseColor = { 1, 1, 1, 1 }; public int Image = -1; }

    public sealed class GChannel
    {
        public int Node, Path; // 0 — перенос, 1 — поворот, 2 — масштаб
        public float[] Times, Values;
        public bool Step;
    }

    public sealed class GAnim
    {
        public string Name;
        public List<GChannel> Channels = new List<GChannel>();
        public float Duration;
    }

    /// <summary>Разобранный GLB: узлы, меши, скелеты, анимации, материалы и имена картинок.</summary>
    public sealed class GltfDoc
    {
        public List<GNode> Nodes = new List<GNode>();
        public List<GNode> Roots = new List<GNode>();
        public List<GMesh> Meshes = new List<GMesh>();
        public List<GSkin> Skins = new List<GSkin>();
        public List<GAnim> Anims = new List<GAnim>();
        public List<GMaterial> Materials = new List<GMaterial>();
        public List<string> Images = new List<string>();
        Dictionary<string, object> j;
        byte[] bin;

        static List<object> L(object o) => o as List<object> ?? new List<object>();
        static Dictionary<string, object> D(object o) => o as Dictionary<string, object>;
        static int I(object o) => Convert.ToInt32(o, CultureInfo.InvariantCulture);
        static float F(object o) => Convert.ToSingle(o, CultureInfo.InvariantCulture);
        static object Get(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) ? v : null;

        public static GltfDoc Parse(byte[] glb)
        {
            var doc = new GltfDoc();
            if (BitConverter.ToUInt32(glb, 0) != 0x46546C67) throw new Exception("не GLB");
            int jl = BitConverter.ToInt32(glb, 12);
            doc.j = (Dictionary<string, object>)Json.Parse(Encoding.UTF8.GetString(glb, 20, jl));
            int o = 20 + jl;
            if (o + 8 <= glb.Length)
            {
                int bl = BitConverter.ToInt32(glb, o);
                doc.bin = new byte[bl];
                Array.Copy(glb, o + 8, doc.bin, 0, bl);
            }
            doc.Build();
            return doc;
        }

        void Build()
        {
            foreach (var im in L(Get(j, "images"))) Images.Add(Get(D(im), "uri") as string);
            var textures = L(Get(j, "textures"));
            foreach (var mo in L(Get(j, "materials")))
            {
                var md = D(mo);
                var m = new GMaterial { Name = Get(md, "name") as string };
                var pbr = D(Get(md, "pbrMetallicRoughness"));
                if (Get(pbr, "baseColorFactor") is List<object> bc) for (int k = 0; k < 4; k++) m.BaseColor[k] = F(bc[k]);
                if (Get(pbr, "baseColorTexture") is Dictionary<string, object> bt)
                {
                    int ti = I(bt["index"]);
                    if (ti < textures.Count && Get(D(textures[ti]), "source") != null) m.Image = I(Get(D(textures[ti]), "source"));
                }
                Materials.Add(m);
            }
            var nodes = L(Get(j, "nodes"));
            for (int i = 0; i < nodes.Count; i++)
            {
                var nd = D(nodes[i]);
                var n = new GNode { Index = i, Name = Get(nd, "name") as string ?? ("node" + i) };
                if (Get(nd, "mesh") != null) n.Mesh = I(nd["mesh"]);
                if (Get(nd, "skin") != null) n.Skin = I(nd["skin"]);
                if (Get(nd, "translation") is List<object> t) for (int k = 0; k < 3; k++) n.T[k] = F(t[k]);
                if (Get(nd, "rotation") is List<object> r) for (int k = 0; k < 4; k++) n.R[k] = F(r[k]);
                if (Get(nd, "scale") is List<object> s) for (int k = 0; k < 3; k++) n.S[k] = F(s[k]);
                if (Get(nd, "matrix") is List<object> mt) { var a = new float[16]; for (int k = 0; k < 16; k++) a[k] = F(mt[k]); n.Matrix = Mat4.FromArray(a, 0); }
                Nodes.Add(n);
            }
            for (int i = 0; i < nodes.Count; i++)
                foreach (var c in L(Get(D(nodes[i]), "children"))) { var ch = Nodes[I(c)]; ch.Parent = Nodes[i]; Nodes[i].Children.Add(ch); }
            var scenes = L(Get(j, "scenes"));
            int sc = Get(j, "scene") != null ? I(j["scene"]) : 0;
            if (scenes.Count > 0) foreach (var r in L(Get(D(scenes[sc]), "nodes"))) Roots.Add(Nodes[I(r)]);
            else foreach (var n in Nodes) if (n.Parent == null) Roots.Add(n);

            foreach (var mo in L(Get(j, "meshes")))
            {
                var md = D(mo);
                var mesh = new GMesh { Name = Get(md, "name") as string };
                foreach (var po in L(Get(md, "primitives")))
                {
                    var pd = D(po);
                    var at = D(pd["attributes"]);
                    var p = new GPrim();
                    p.Pos = Floats(I(at["POSITION"]));
                    if (Get(at, "NORMAL") != null) p.Nrm = Floats(I(at["NORMAL"]));
                    if (Get(at, "TEXCOORD_0") != null) p.Uv = Floats(I(at["TEXCOORD_0"]));
                    if (Get(at, "JOINTS_0") != null) p.Joints = Ints(I(at["JOINTS_0"]));
                    if (Get(at, "WEIGHTS_0") != null) p.Weights = Floats(I(at["WEIGHTS_0"]));
                    if (Get(pd, "indices") != null) p.Idx = Ints(I(pd["indices"]));
                    else { p.Idx = new int[p.Pos.Length / 3]; for (int k = 0; k < p.Idx.Length; k++) p.Idx[k] = k; }
                    if (Get(pd, "material") != null) p.Material = I(pd["material"]);
                    mesh.Prims.Add(p);
                }
                Meshes.Add(mesh);
            }
            foreach (var so in L(Get(j, "skins")))
            {
                var sd = D(so);
                var js = L(sd["joints"]);
                var sk = new GSkin { Joints = new int[js.Count] };
                for (int k = 0; k < js.Count; k++) sk.Joints[k] = I(js[k]);
                if (Get(sd, "inverseBindMatrices") != null) sk.InvBind = Floats(I(sd["inverseBindMatrices"]));
                else { sk.InvBind = new float[16 * js.Count]; for (int k = 0; k < js.Count; k++) Array.Copy(Mat4.Identity.E, 0, sk.InvBind, k * 16, 16); }
                Skins.Add(sk);
            }
            foreach (var ao in L(Get(j, "animations")))
            {
                var ad = D(ao);
                var a = new GAnim { Name = Get(ad, "name") as string };
                var samplers = L(ad["samplers"]);
                foreach (var co in L(ad["channels"]))
                {
                    var cd = D(co);
                    var tg = D(cd["target"]);
                    if (Get(tg, "node") == null) continue;
                    string path = tg["path"] as string;
                    int pi = path == "translation" ? 0 : path == "rotation" ? 1 : path == "scale" ? 2 : -1;
                    if (pi < 0) continue;
                    var smp = D(samplers[I(cd["sampler"])]);
                    var ch = new GChannel { Node = I(tg["node"]), Path = pi, Times = Floats(I(smp["input"])), Values = Floats(I(smp["output"])), Step = (Get(smp, "interpolation") as string) == "STEP" };
                    if ((Get(smp, "interpolation") as string) == "CUBICSPLINE")
                    { // берём только значения (без касательных)
                        int w = pi == 1 ? 4 : 3, n = ch.Times.Length;
                        var v = new float[n * w];
                        for (int k = 0; k < n; k++) Array.Copy(ch.Values, (k * 3 + 1) * w, v, k * w, w);
                        ch.Values = v;
                    }
                    a.Channels.Add(ch);
                    if (ch.Times.Length > 0) a.Duration = MathF.Max(a.Duration, ch.Times[ch.Times.Length - 1]);
                }
                Anims.Add(a);
            }
        }

        static int Comps(string type) => type == "SCALAR" ? 1 : type == "VEC2" ? 2 : type == "VEC3" ? 3 : type == "VEC4" ? 4 : type == "MAT4" ? 16 : type == "MAT3" ? 9 : 4;

        void Accessor(int ai, out int count, out int comps, out int ctype, out bool norm, out int offset, out int stride)
        {
            var a = D(L(j["accessors"])[ai]);
            count = I(a["count"]);
            comps = Comps(a["type"] as string);
            ctype = I(a["componentType"]);
            norm = Get(a, "normalized") is bool b && b;
            int csize = ctype == 5126 || ctype == 5125 ? 4 : ctype == 5123 || ctype == 5122 ? 2 : 1;
            offset = Get(a, "byteOffset") != null ? I(a["byteOffset"]) : 0;
            stride = comps * csize;
            if (Get(a, "bufferView") != null)
            {
                var bv = D(L(j["bufferViews"])[I(a["bufferView"])]);
                offset += Get(bv, "byteOffset") != null ? I(bv["byteOffset"]) : 0;
                if (Get(bv, "byteStride") != null) stride = I(bv["byteStride"]);
            }
            else offset = -1;
        }

        float Comp(int ctype, bool norm, int at)
        {
            switch (ctype)
            {
                case 5126: return BitConverter.ToSingle(bin, at);
                case 5125: return BitConverter.ToUInt32(bin, at);
                case 5123: { float v = BitConverter.ToUInt16(bin, at); return norm ? v / 65535f : v; }
                case 5122: { float v = BitConverter.ToInt16(bin, at); return norm ? MathF.Max(v / 32767f, -1) : v; }
                case 5121: { float v = bin[at]; return norm ? v / 255f : v; }
                default: { float v = (sbyte)bin[at]; return norm ? MathF.Max(v / 127f, -1) : v; }
            }
        }

        public float[] Floats(int ai)
        {
            Accessor(ai, out int count, out int comps, out int ctype, out bool norm, out int offset, out int stride);
            var r = new float[count * comps];
            if (offset < 0) return r;
            int csize = ctype == 5126 || ctype == 5125 ? 4 : ctype == 5123 || ctype == 5122 ? 2 : 1;
            for (int i = 0; i < count; i++)
                for (int c = 0; c < comps; c++)
                    r[i * comps + c] = Comp(ctype, norm, offset + i * stride + c * csize);
            return r;
        }

        public int[] Ints(int ai)
        {
            var f = Floats(ai);
            var r = new int[f.Length];
            for (int i = 0; i < f.Length; i++) r[i] = (int)f[i];
            return r;
        }

        public GNode Find(string name) => Nodes.Find(n => n.Name == name);
        public GAnim Anim(string name) => Anims.Find(a => a.Name == name);
    }
}
