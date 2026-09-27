using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattleSim
{
    /// <summary>
    /// Собирает low-poly меши с плоскими гранями. Цвет каждой грани берётся из палитры (через UV),
    /// а индекс кости позволяет делать "жёсткий" скиннинг для анимации солдат.
    /// </summary>
    public class MeshKit
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> tris = new List<int>();
        readonly List<BoneWeight> weights = new List<BoneWeight>();

        /// <summary>Матрица, которой трансформируются все добавляемые точки.</summary>
        public Matrix4x4 M = Matrix4x4.identity;
        /// <summary>Кость, к которой привязываются новые вершины.</summary>
        public int Bone;
        /// <summary>Индекс цвета в палитре для новых граней.</summary>
        public int Color;

        public int VertexCount => verts.Count;

        public void Clear()
        {
            verts.Clear(); normals.Clear(); uvs.Clear(); tris.Clear(); weights.Clear();
            M = Matrix4x4.identity; Bone = 0; Color = 0;
        }

        public MeshKit Set(int color) { Color = color; return this; }

        void AddWorldTri(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            float len = n.magnitude;
            if (len < 1e-9f) return;
            n /= len;
            Vector2 uv = Palette.UV(Color);
            var w = new BoneWeight { boneIndex0 = Bone, weight0 = 1f };
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            normals.Add(n); normals.Add(n); normals.Add(n);
            uvs.Add(uv); uvs.Add(uv); uvs.Add(uv);
            weights.Add(w); weights.Add(w); weights.Add(w);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        /// <summary>Треугольник, лицевая сторона которого смотрит от точки inside (наружу фигуры).</summary>
        public void TriOut(Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
        {
            Vector3 wa = M.MultiplyPoint3x4(a), wb = M.MultiplyPoint3x4(b), wc = M.MultiplyPoint3x4(c);
            Vector3 wi = M.MultiplyPoint3x4(inside);
            Vector3 n = Vector3.Cross(wb - wa, wc - wa);
            if (Vector3.Dot(n, (wa + wb + wc) / 3f - wi) < 0f) { var t = wb; wb = wc; wc = t; }
            AddWorldTri(wa, wb, wc);
        }

        public void QuadOut(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 inside)
        {
            TriOut(a, b, c, inside);
            TriOut(a, c, d, inside);
        }

        /// <summary>Двусторонний треугольник (трава, тетива).</summary>
        public void TriBoth(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 wa = M.MultiplyPoint3x4(a), wb = M.MultiplyPoint3x4(b), wc = M.MultiplyPoint3x4(c);
            AddWorldTri(wa, wb, wc);
            AddWorldTri(wa, wc, wb);
        }

        public void Box(Vector3 center, Vector3 size) => Box(center, size, Quaternion.identity);

        public void Box(Vector3 center, Vector3 size, Quaternion rot)
        {
            Vector3 h = size * 0.5f;
            Vector3 P(float x, float y, float z) => center + rot * new Vector3(x * h.x, y * h.y, z * h.z);
            Vector3 p000 = P(-1, -1, -1), p100 = P(1, -1, -1), p010 = P(-1, 1, -1), p110 = P(1, 1, -1);
            Vector3 p001 = P(-1, -1, 1), p101 = P(1, -1, 1), p011 = P(-1, 1, 1), p111 = P(1, 1, 1);
            QuadOut(p000, p100, p110, p010, center); // -Z
            QuadOut(p001, p011, p111, p101, center); // +Z
            QuadOut(p000, p010, p011, p001, center); // -X
            QuadOut(p100, p101, p111, p110, center); // +X
            QuadOut(p010, p110, p111, p011, center); // +Y
            QuadOut(p000, p001, p101, p100, center); // -Y
        }

        /// <summary>Гранёный цилиндр/конус от a до b. Если rb == 0 — конус.</summary>
        public void Cylinder(Vector3 a, Vector3 b, float ra, float rb, int sides, bool caps = true)
        {
            Vector3 axis = b - a;
            if (axis.sqrMagnitude < 1e-10f) return;
            Vector3 d = axis.normalized;
            Vector3 u = Vector3.Cross(d, Mathf.Abs(d.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(d, u);
            Vector3 mid = (a + b) * 0.5f;
            for (int k = 0; k < sides; k++)
            {
                float t0 = k * Mathf.PI * 2f / sides, t1 = (k + 1) * Mathf.PI * 2f / sides;
                Vector3 r0 = u * Mathf.Cos(t0) + v * Mathf.Sin(t0);
                Vector3 r1 = u * Mathf.Cos(t1) + v * Mathf.Sin(t1);
                Vector3 a0 = a + r0 * ra, a1 = a + r1 * ra, b0 = b + r0 * rb, b1 = b + r1 * rb;
                if (rb > 0f) QuadOut(a0, a1, b1, b0, mid);
                else TriOut(a0, a1, b, mid);
                if (caps)
                {
                    if (ra > 0f) TriOut(a, a0, a1, mid);
                    if (rb > 0f) TriOut(b, b0, b1, mid);
                }
            }
        }

        /// <summary>Гранёный эллипсоид. jitter &gt; 0 делает из него камень.</summary>
        public void Ellipsoid(Vector3 center, Vector3 radii, int seg = 7, int rings = 5, float jitter = 0f, int seed = 0)
        {
            var rnd = new System.Random(seed);
            var pts = new Vector3[(rings + 1) * seg];
            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.PI * r / rings;
                for (int s = 0; s < seg; s++)
                {
                    float th = Mathf.PI * 2f * s / seg + (r % 2) * Mathf.PI / seg;
                    Vector3 dir = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                    float j = 1f + (jitter > 0f ? ((float)rnd.NextDouble() * 2f - 1f) * jitter : 0f);
                    pts[r * seg + s] = center + Vector3.Scale(dir, radii) * j;
                }
            }
            // Полюса делаем общими точками, чтобы не было щелей.
            Vector3 top = pts[0], bottom = pts[rings * seg];
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < seg; s++)
                {
                    int s1 = (s + 1) % seg;
                    Vector3 p00 = r == 0 ? top : pts[r * seg + s];
                    Vector3 p01 = r == 0 ? top : pts[r * seg + s1];
                    Vector3 p10 = r + 1 == rings ? bottom : pts[(r + 1) * seg + s];
                    Vector3 p11 = r + 1 == rings ? bottom : pts[(r + 1) * seg + s1];
                    if (r == 0) TriOut(top, p10, p11, center);
                    else if (r + 1 == rings) TriOut(p00, p01, bottom, center);
                    else QuadOut(p00, p01, p11, p10, center);
                }
            }
        }

        public Mesh ToMesh(string name, Matrix4x4[] bindposes = null)
        {
            var mesh = new Mesh { name = name };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            if (bindposes != null)
            {
                mesh.boneWeights = weights.ToArray();
                mesh.bindposes = bindposes;
            }
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
