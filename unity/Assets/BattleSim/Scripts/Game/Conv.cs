using System.Collections.Generic;
using BattleSim.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattleSim
{
    /// <summary>
    /// Ядро считает в правой системе координат (как three.js: +z к зрителю), Unity — в левой.
    /// Переводим на границе: z → −z, у треугольников меняем порядок вершин, повороты — с обратным знаком.
    /// </summary>
    public static class Conv
    {
        public static Vector3 U(V3 p) => new Vector3(p.x, p.y, -p.z);
        public static Vector3 U(float x, float y, float z) => new Vector3(x, y, -z);
        public static V3 C(Vector3 p) => new V3(p.x, p.y, -p.z);

        /// <summary>Поворот вокруг вертикали: yaw ядра → кватернион Unity.</summary>
        public static Quaternion Yaw(float yaw) => Quaternion.Euler(0f, -yaw * Mathf.Rad2Deg, 0f);

        /// <summary>Матрица ядра (по столбцам) → Matrix4x4 Unity с учётом смены оси z.</summary>
        public static Matrix4x4 U(Mat4 m)
        {
            var e = m.E;
            var r = new Matrix4x4();
            for (int c = 0; c < 4; c++)
                for (int row = 0; row < 4; row++)
                {
                    float v = e[c * 4 + row];
                    if ((row == 2) != (c == 2)) v = -v;
                    r[row, c] = v;
                }
            return r;
        }

        /// <summary>Линейный цвет ядра → цвет для свойств Unity (в sRGB; Unity сам переведёт в линейный).</summary>
        public static Color Col(Rgb c, float a = 1f) { var s = c.Srgb; return new Color(s.r, s.g, s.b, a); }
        /// <summary>Линейный цвет как есть (для цветов вершин и копий — в шейдер без перевода).</summary>
        public static Color Lin(Rgb c, float a = 1f) => new Color(c.r, c.g, c.b, a);

        /// <summary>Меш Unity из геометрии ядра. skin — кости и веса в UV2/UV3 для запечённой анимации.</summary>
        public static Mesh ToMesh(MeshData d, string name = "mesh", bool skin = false)
        {
            int n = d.VertexCount;
            var mesh = new Mesh { name = name };
            if (n > 65000) mesh.indexFormat = IndexFormat.UInt32;
            var v = new Vector3[n];
            var nr = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = new Vector3(d.Pos[i * 3], d.Pos[i * 3 + 1], -d.Pos[i * 3 + 2]);
                if (d.Nrm != null) nr[i] = new Vector3(d.Nrm[i * 3], d.Nrm[i * 3 + 1], -d.Nrm[i * 3 + 2]);
            }
            mesh.vertices = v;
            mesh.normals = nr;
            if (d.Uv != null)
            {
                var uv = new Vector2[n];
                for (int i = 0; i < n; i++) uv[i] = new Vector2(d.Uv[i * 2], d.Uv[i * 2 + 1]);
                mesh.uv = uv;
            }
            else mesh.uv = new Vector2[n];
            var col = new Color[n];
            for (int i = 0; i < n; i++) col[i] = d.Col != null ? new Color(d.Col[i * 3], d.Col[i * 3 + 1], d.Col[i * 3 + 2], 1f) : Color.white;
            mesh.colors = col;
            if (skin && d.Joints != null)
            {
                var bi = new List<Vector4>(n);
                var bw = new List<Vector4>(n);
                for (int i = 0; i < n; i++)
                {
                    bi.Add(new Vector4(d.Joints[i * 4], d.Joints[i * 4 + 1], d.Joints[i * 4 + 2], d.Joints[i * 4 + 3]));
                    bw.Add(new Vector4(d.Weights[i * 4], d.Weights[i * 4 + 1], d.Weights[i * 4 + 2], d.Weights[i * 4 + 3]));
                }
                mesh.SetUVs(2, bi);
                mesh.SetUVs(3, bw);
            }
            var tri = new int[d.Idx.Length];
            for (int t = 0; t < tri.Length; t += 3) { tri[t] = d.Idx[t]; tri[t + 1] = d.Idx[t + 2]; tri[t + 2] = d.Idx[t + 1]; }
            mesh.triangles = tri;
            mesh.RecalculateBounds();
            if (skin)
            { // кости двигают вершины в шейдере — границы с запасом, чтобы не отсекало
                var b = mesh.bounds;
                b.Expand(4f);
                mesh.bounds = b;
            }
            return mesh;
        }

        /// <summary>UV моделей glTF отсчитываются сверху, в Unity — снизу: переворачиваем v.</summary>
        public static void FlipV(MeshData d)
        {
            if (d.Uv == null) return;
            var uv = (float[])d.Uv.Clone();
            for (int i = 1; i < uv.Length; i += 2) uv[i] = 1f - uv[i];
            d.Uv = uv;
        }
    }
}
