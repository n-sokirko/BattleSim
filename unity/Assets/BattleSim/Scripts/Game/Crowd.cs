using System.Collections.Generic;
using BattleSim.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattleSim
{
    /// <summary>
    /// Толпа одного вида (пехота, всадник или конь): запечённые анимации в текстуре,
    /// два уровня детализации и материал на каждую армию. Каждый кадр солдаты
    /// раскладываются по пачкам и рисуются инстансингом — без отдельных скелетов.
    /// </summary>
    public sealed class CrowdModel
    {
        public const int Batch = 1023;
        public readonly BakedAnims Baked;
        public const int Lods = 3;
        public readonly Mesh[] Lod = new Mesh[Lods];
        public readonly Material[] Mats; // по армиям (у коня — один)
        public readonly Texture2D BakeTex;
        public readonly int[] Verts = new int[Lods];

        sealed class Bucket
        {
            public readonly List<Matrix4x4[]> M = new List<Matrix4x4[]>();
            public readonly List<Vector4[]> A = new List<Vector4[]>();
            public readonly List<Vector4[]> C = new List<Vector4[]>();
            public int Count;
        }

        readonly Bucket[,] buckets;
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static readonly int AnimId = Shader.PropertyToID("_Anim");
        static readonly int ColorId = Shader.PropertyToID("_InstColor");

        /// <summary>
        /// Цвет копии по умолчанию. В шейдере: rgb до 1 — множитель цвета, выше 1 — свечение поверх
        /// освещения (вспышка удара); альфа меньше 1 — выцветание (бегущие).
        /// </summary>
        public static readonly Color White = new Color(1, 1, 1, 1);

        public CrowdModel(SkinnedModel model, IList<ClipDef> defs, Material[] mats)
        {
            Baked = ModelKit.Bake(model, defs);
            // Детальные модели (десятки тысяч вершин) упрощаем уже вблизи; простые — только вдали
            model.Mesh.Bounds(out var mn, out var mx);
            float diag = M.Hypot(mx.x - mn.x, mx.y - mn.y, mx.z - mn.z);
            bool heavy = model.Mesh.VertexCount > 10000;
            // лёгкие модели (скелет — 2 тыс. треугольников из тонких костей) не упрощаем на среднем плане:
            // сетка упрощения крупнее кости, и руки-ноги схлопывались — скелеты «пропадали» вдали
            bool light = model.Mesh.Idx.Length / 3 < 3000;
            float[] cells = heavy ? new[] { 0.012f, 0.02f, 0.034f } : light ? new[] { 0f, 0f, 0.034f } : new[] { 0f, 0.034f, 0.06f };
            for (int l = 0; l < Lods; l++)
            {
                var m = cells[l] > 0 ? ModelKit.Decimate(model.Mesh, diag * cells[l]) : model.Mesh.Clone();
                Conv.FlipV(m);
                Lod[l] = Conv.ToMesh(m, "crowd" + l, true);
                Verts[l] = m.VertexCount;
            }

            // Строка = кадр, 3 текселя на кость = строки матрицы 3×4 (уже в координатах Unity)
            int nb = Baked.Bones;
            BakeTex = new Texture2D(nb * 3, Mathf.Max(1, Baked.Rows), TextureFormat.RGBAFloat, false, true)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "bake",
            };
            var px = new Color[nb * 3 * Mathf.Max(1, Baked.Rows)];
            var tmp = new Mat4();
            for (int r = 0; r < Baked.Rows; r++)
                for (int b = 0; b < nb; b++)
                {
                    System.Array.Copy(Baked.Data, (r * nb + b) * 16, tmp.E, 0, 16);
                    var u = Conv.U(tmp);
                    int o = r * nb * 3 + b * 3;
                    px[o] = new Color(u.m00, u.m01, u.m02, u.m03);
                    px[o + 1] = new Color(u.m10, u.m11, u.m12, u.m13);
                    px[o + 2] = new Color(u.m20, u.m21, u.m22, u.m23);
                }
            BakeTex.SetPixels(px);
            BakeTex.Apply(false, true);

            Mats = mats;
            foreach (var m in Mats)
            {
                m.EnableKeyword("_BAKED_SKIN");
                m.SetTexture("_BakeTex", BakeTex);
                m.enableInstancing = true;
            }
            buckets = new Bucket[Mats.Length, Lods];
            for (int t = 0; t < Mats.Length; t++) for (int l = 0; l < Lods; l++) buckets[t, l] = new Bucket();
        }

        public int Row(AnimState st) => Baked.Row(st);

        public void Begin()
        {
            foreach (var b in buckets) b.Count = 0;
        }

        public void Add(int team, int lod, Matrix4x4 m, int rowA, int rowB, float blend) => Add(team, lod, m, rowA, rowB, blend, White);

        public void Add(int team, int lod, Matrix4x4 m, int rowA, int rowB, float blend, Color color)
        {
            var b = buckets[Mathf.Min(team, Mats.Length - 1), lod];
            int bi = b.Count / Batch, i = b.Count % Batch;
            if (bi >= b.M.Count) { b.M.Add(new Matrix4x4[Batch]); b.A.Add(new Vector4[Batch]); b.C.Add(new Vector4[Batch]); }
            b.M[bi][i] = m;
            b.A[bi][i] = new Vector4(rowA, rowB, blend, 0);
            b.C[bi][i] = new Vector4(color.r, color.g, color.b, color.a);
            b.Count++;
        }

        /// <summary>Рисуем пачки; тени — только у ближних уровней (дальние тени не видны, а стоят как вторая армия).</summary>
        public void End(bool shadows)
        {
            for (int t = 0; t < Mats.Length; t++)
                for (int l = 0; l < Lods; l++)
                {
                    bool sh = shadows && l < Lods - 1;
                    var b = buckets[t, l];
                    for (int k = 0; k * Batch < b.Count; k++)
                    {
                        int n = Mathf.Min(Batch, b.Count - k * Batch);
                        mpb.Clear();
                        mpb.SetVectorArray(AnimId, b.A[k]);
                        mpb.SetVectorArray(ColorId, b.C[k]);
                        Graphics.DrawMeshInstanced(Lod[l], 0, Mats[t], b.M[k], n, mpb,
                            sh ? ShadowCastingMode.On : ShadowCastingMode.Off, true, 0, null);
                    }
                }
        }
    }

    /// <summary>Много копий статичного меша (деревья, камни, болты) — пачками по 1023.</summary>
    public sealed class InstancedSet
    {
        public readonly Mesh Mesh;
        public readonly Material Mat;
        public readonly bool Shadows;
        readonly List<Matrix4x4[]> batches = new List<Matrix4x4[]>();
        public int Count;

        public InstancedSet(Mesh mesh, Material mat, bool shadows) { Mesh = mesh; Mat = mat; Shadows = shadows; mat.enableInstancing = true; }

        public void Clear() => Count = 0;

        public void Add(Matrix4x4 m)
        {
            int bi = Count / CrowdModel.Batch;
            if (bi >= batches.Count) batches.Add(new Matrix4x4[CrowdModel.Batch]);
            batches[bi][Count % CrowdModel.Batch] = m;
            Count++;
        }

        public void Draw()
        {
            for (int k = 0; k * CrowdModel.Batch < Count; k++)
                Graphics.DrawMeshInstanced(Mesh, 0, Mat, batches[k], Mathf.Min(CrowdModel.Batch, Count - k * CrowdModel.Batch), null,
                    Shadows ? ShadowCastingMode.On : ShadowCastingMode.Off, true, 0, null);
        }
    }
}
