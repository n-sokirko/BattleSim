using System.Collections.Generic;
using BattleSim.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattleSim
{
    /// <summary>Кольца под солдатами, «призрак» отряда при расстановке, знамёна полководцев, летящие болты.</summary>
    public sealed class Overlays
    {
        readonly Mesh ring, disc;
        readonly Material ringMat, ghostMat;
        readonly Matrix4x4[] rm = new Matrix4x4[CrowdModel.Batch], gm = new Matrix4x4[64];
        readonly Vector4[] rc = new Vector4[CrowdModel.Batch], gc = new Vector4[64];
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock(), gpb = new MaterialPropertyBlock();
        static readonly int InstColor = Shader.PropertyToID("_InstColor");
        readonly Vector4[] teamCol, hiddenCol;
        readonly Vector4 routCol;
        public int GhostCount;
        InstancedSet bolts;
        Quaternion boltBase;

        public Overlays()
        {
            ring = MakeRing(0.78f, 1f, 28);
            disc = MakeRing(0f, 0.55f, 18);
            var sh = Shader.Find("BattleSim/Unlit");
            ringMat = new Material(sh) { name = "rings", enableInstancing = true };
            ringMat.SetColor("_BaseColor", new Color(1, 1, 1, 0.6f));
            ghostMat = new Material(sh) { name = "ghost", enableInstancing = true };
            ghostMat.SetColor("_BaseColor", new Color(1, 1, 1, 0.5f));
            teamCol = new Vector4[2]; hiddenCol = new Vector4[2];
            for (int t = 0; t < 2; t++)
            {
                var c = Rgb.Hex(Defs.Teams[t].Color);
                teamCol[t] = new Vector4(c.r, c.g, c.b, 1);
                var h = c.Lerp(Rgb.Hex(0x222222), 0.65f);
                hiddenCol[t] = new Vector4(h.r, h.g, h.b, 1);
            }
            var r = Rgb.Hex(0xf2e6b0);
            routCol = new Vector4(r.r, r.g, r.b, 1);
        }

        static Mesh MakeRing(float inner, float outer, int seg)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2 / seg;
                v.Add(new Vector3(Mathf.Cos(a) * inner, 0, Mathf.Sin(a) * inner));
                v.Add(new Vector3(Mathf.Cos(a) * outer, 0, Mathf.Sin(a) * outer));
                if (i < seg) { int b = i * 2; t.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 }); }
            }
            var m = new Mesh { name = "ring" };
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        public void SetBolt(ModelLibrary lib)
        {
            bolts = new InstancedSet(lib.Bolt.Mesh, lib.Bolt.Mat, false);
            boltBase = Quaternion.identity;
        }

        public void DrawRings(Battle b)
        {
            int n = 0;
            foreach (var u in b.Units)
            {
                if (!u.Alive) continue;
                float k = u.T.Radius * 1.25f;
                rm[n] = Matrix4x4.TRS(Conv.U(u.Pos.x, u.Pos.y + 0.07f, u.Pos.z), Quaternion.identity, new Vector3(k, 1, k));
                var sq = u.Squad;
                rc[n] = sq != null && sq.Hidden ? hiddenCol[u.Team] : sq != null && sq.Order.Mode == Mode.Rout ? routCol : teamCol[u.Team];
                if (++n == CrowdModel.Batch) { Flush(n); n = 0; }
            }
            if (n > 0) Flush(n);
        }

        void Flush(int n)
        {
            mpb.Clear();
            mpb.SetVectorArray(InstColor, rc);
            Graphics.DrawMeshInstanced(ring, 0, ringMat, rm, n, mpb, ShadowCastingMode.Off, false, 0, null);
        }

        /// <summary>Призрак отряда под курсором при расстановке.</summary>
        public void SetGhost(World w, V3? p, int type, int team)
        {
            GhostCount = 0;
            if (p == null) return;
            var t = Defs.Types[type];
            float yaw = team == 0 ? 0 : M.PI, cos = Mathf.Cos(yaw), sin = Mathf.Sin(yaw);
            var c = teamCol[team];
            for (int r = 0; r < t.Rows; r++)
                for (int col = 0; col < t.Cols; col++)
                {
                    float ox = (col - (t.Cols - 1) / 2f) * t.Spacing, oz = -(r - (t.Rows - 1) / 2f) * t.Spacing;
                    float gx = p.Value.x + ox * cos + oz * sin, gz = p.Value.z - ox * sin + oz * cos, k = t.Radius * 1.6f;
                    gm[GhostCount] = Matrix4x4.TRS(Conv.U(gx, w.GroundAt(gx, gz) + 0.06f, gz), Quaternion.identity, new Vector3(k, 1, k));
                    gc[GhostCount] = c;
                    GhostCount++;
                }
        }

        public void DrawGhost()
        {
            if (GhostCount == 0) return;
            gpb.Clear();
            gpb.SetVectorArray(InstColor, gc);
            Graphics.DrawMeshInstanced(disc, 0, ghostMat, gm, GhostCount, gpb, ShadowCastingMode.Off, false, 0, null);
        }

        public void DrawBolts(Battle b, ModelLibrary lib)
        {
            if (bolts == null) return;
            bolts.Clear();
            var axis = Conv.U(lib.BoltAxis);
            var scale = Vector3.one * lib.BoltNorm;
            foreach (var a in b.Bolts.List)
            {
                var d = Conv.U(a.Dir);
                if (d.sqrMagnitude < 1e-8f) continue;
                bolts.Add(Matrix4x4.TRS(Conv.U(a.Pos), Quaternion.FromToRotation(axis, d.normalized), scale));
            }
            bolts.Draw();
        }
    }

    /// <summary>Знамя полководца: древко, полотнище цвета армии и золотое навершие; колышется на ветру.</summary>
    public sealed class Banner
    {
        public readonly GameObject Go;
        readonly Transform flagT;
        readonly Mesh flag;
        readonly Vector3[] baseV, v;

        public Banner(int team, float scale, float hipsY)
        {
            Go = new GameObject("Banner");
            var inner = new GameObject("flagpole").transform;
            inner.SetParent(Go.transform, false);
            inner.localPosition = new Vector3(-0.3f, hipsY, 0.38f);
            inner.localScale = Vector3.one * scale;
            var wood = ModelLibrary.NewLit(null, Conv.Col(Rgb.Hex(0x5b3a22)), "pole");
            var gold = ModelLibrary.NewLit(null, Conv.Col(Rgb.Hex(0xe8c15a)), "gold");
            gold.SetFloat("_Spec", 0.6f);
            var cloth = ModelLibrary.NewLit(null, Conv.Col(Rgb.Hex(Defs.Teams[team].Color)), "flag");
            cloth.SetFloat("_Cull", 0);

            var pole = Part(inner, "pole", Cylinder(0.035f, 0.045f, 3.4f, 6), wood);
            pole.localPosition = new Vector3(0, 1.7f, 0);
            flag = new Mesh { name = "flag" };
            int seg = 8;
            baseV = new Vector3[(seg + 1) * 2];
            for (int i = 0; i <= seg; i++)
            {
                float x = 1.25f * i / seg;
                baseV[i * 2] = new Vector3(x, -0.425f, 0);
                baseV[i * 2 + 1] = new Vector3(x, 0.425f, 0);
            }
            var tris = new List<int>();
            for (int i = 0; i < seg; i++) { int b = i * 2; tris.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 }); }
            v = (Vector3[])baseV.Clone();
            flag.vertices = v;
            flag.SetTriangles(tris, 0);
            flag.RecalculateNormals();
            flag.colors = new Color[v.Length].Fill(Color.white);
            flagT = Part(inner, "cloth", flag, cloth);
            flagT.localPosition = new Vector3(0, 2.95f, 0);
            var tip = Part(inner, "tip", Octahedron(0.11f), gold);
            tip.localPosition = new Vector3(0, 3.45f, 0);
        }

        static Transform Part(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.On;
            return go.transform;
        }

        static Mesh Cylinder(float rTop, float rBottom, float h, int seg)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2 / seg;
                v.Add(new Vector3(Mathf.Cos(a) * rBottom, -h / 2, Mathf.Sin(a) * rBottom));
                v.Add(new Vector3(Mathf.Cos(a) * rTop, h / 2, Mathf.Sin(a) * rTop));
                if (i < seg) { int b = i * 2; t.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 }); }
            }
            var m = new Mesh { name = "cyl" };
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.colors = new Color[v.Count].Fill(Color.white);
            return m;
        }

        static Mesh Octahedron(float r)
        {
            var p = new[] { new Vector3(r, 0, 0), new Vector3(-r, 0, 0), new Vector3(0, r, 0), new Vector3(0, -r, 0), new Vector3(0, 0, r), new Vector3(0, 0, -r) };
            int[] f = { 2, 4, 0, 2, 0, 5, 2, 5, 1, 2, 1, 4, 3, 0, 4, 3, 5, 0, 3, 1, 5, 3, 4, 1 };
            var v = new Vector3[f.Length];
            var t = new int[f.Length];
            for (int i = 0; i < f.Length; i++) { v[i] = p[f[i]]; t[i] = i; }
            var m = new Mesh { name = "tip", vertices = v, triangles = t };
            m.RecalculateNormals();
            m.colors = new Color[v.Length].Fill(Color.white);
            return m;
        }

        /// <summary>Ставим знамя на всадника и колышем полотнище волной (сила — от ветра).</summary>
        public void Update(Matrix4x4 rider, float time, float wind)
        {
            Go.transform.SetPositionAndRotation(rider.GetColumn(3), rider.rotation);
            Go.transform.localScale = rider.lossyScale;
            float amp = 0.06f + Mathf.Min(wind, 7) * 0.02f;
            for (int i = 0; i < v.Length; i++)
            {
                float x = baseV[i].x;
                v[i] = new Vector3(x, baseV[i].y, Mathf.Sin(x * 4 - time * 5) * amp * x);
            }
            flag.vertices = v;
            flag.RecalculateNormals();
            flag.RecalculateBounds();
        }
    }

    static class ArrayExt
    {
        public static T[] Fill<T>(this T[] a, T v) { for (int i = 0; i < a.Length; i++) a[i] = v; return a; }
    }
}
