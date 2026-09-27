using System.Collections.Generic;
using BattleSim.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattleSim
{
    /// <summary>
    /// Эффекты боя: пыль и щепки от ударов, искры от удара по щиту, клубы пыли при гибели и натиске,
    /// брызги в воде. Частицы — маленькие многогранники, все рисуются копиями одним вызовом на 1023 штуки.
    /// </summary>
    public sealed class Effects
    {
        const int Max = 3000;
        readonly Vector3[] pos = new Vector3[Max], vel = new Vector3[Max];
        readonly float[] life = new float[Max], maxLife = new float[Max], size = new float[Max], grow = new float[Max], grav = new float[Max];
        readonly Color[] col = new Color[Max];
        int count;

        readonly Mesh mesh;
        readonly Material mat;
        readonly Matrix4x4[] mats = new Matrix4x4[1023];
        readonly Vector4[] cols = new Vector4[1023];
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static readonly int InstColor = Shader.PropertyToID("_InstColor");

        static readonly Color Dust = new Color(0.62f, 0.55f, 0.45f, 0.7f), Chip = new Color(0.45f, 0.38f, 0.3f, 1f);
        static readonly Color Spark = new Color(1f, 0.85f, 0.45f, 1f), Flash = new Color(1f, 0.95f, 0.8f, 0.55f);
        static readonly Color Water = new Color(0.8f, 0.9f, 1f, 0.8f);

        public Effects()
        {
            var shape = WorldMeshes.Flower(1f);
            mesh = Conv.ToMesh(shape, "fx");
            mat = new Material(Shader.Find("BattleSim/Unlit")) { name = "fx", enableInstancing = true, renderQueue = 3100 };
            mat.SetColor("_BaseColor", Color.white);
        }

        void Add(Vector3 p, Vector3 v, float lifeS, float sz, float growS, float gravity, Color c)
        {
            if (count >= Max) return;
            int i = count++;
            pos[i] = p; vel[i] = v; life[i] = lifeS; maxLife[i] = lifeS; size[i] = sz; grow[i] = growS; grav[i] = gravity; col[i] = c;
        }

        static Vector3 Rnd(float s) => new Vector3(Random.Range(-s, s), Random.Range(-s, s), Random.Range(-s, s));

        /// <summary>Разворачивает события ядра в частицы.</summary>
        public void Spawn(List<FxEvent> events)
        {
            foreach (var e in events)
            {
                var p = Conv.U(e.Pos);
                var d = new Vector3(e.Dir.x, 0, -e.Dir.z);
                if (d.sqrMagnitude > 1e-4f) d.Normalize();
                switch (e.Kind)
                {
                    case FxKind.Hit:
                        for (int k = 0; k < 4; k++) Add(p + Rnd(0.15f), d * 0.6f + Vector3.up * 0.5f + Rnd(0.5f), Random.Range(0.35f, 0.6f), 0.12f, 0.5f, -0.5f, Dust);
                        for (int k = 0; k < 3; k++) Add(p, d * 2.2f + Vector3.up * 2.4f + Rnd(1.4f), 0.5f, 0.045f, 0, 9.8f, Chip);
                        break;
                    case FxKind.Block:
                        Add(p, Vector3.zero, 0.09f, 0.16f, 1.2f, 0, Flash);
                        for (int k = 0; k < 7; k++) Add(p + Rnd(0.1f), d * 2.5f + Vector3.up * 2f + Rnd(2.6f), Random.Range(0.25f, 0.4f), 0.035f, -0.05f, 9.8f, Spark);
                        break;
                    case FxKind.Kill:
                        for (int k = 0; k < 7; k++)
                            Add(p + new Vector3(Random.Range(-0.5f, 0.5f), 0.15f, Random.Range(-0.5f, 0.5f)), Rnd(0.35f) + Vector3.up * 0.35f, Random.Range(0.7f, 1.1f), 0.22f, 0.7f, -0.2f, Dust);
                        break;
                    case FxKind.Charge:
                        for (int k = 0; k < 6; k++) Add(p + Vector3.up * 0.6f + Rnd(0.3f), d * 3f + Vector3.up * 1.2f + Rnd(1f), Random.Range(0.4f, 0.7f), 0.16f, 0.9f, 1.5f, Dust);
                        break;
                    case FxKind.Splash:
                        for (int k = 0; k < 7; k++) Add(p, Vector3.up * Random.Range(2f, 3.4f) + Rnd(1.1f), Random.Range(0.4f, 0.6f), 0.05f, 0, 9.8f, Water);
                        Add(p, Vector3.zero, 0.4f, 0.15f, 1.6f, 0, new Color(1, 1, 1, 0.5f));
                        break;
                    case FxKind.BoltGround:
                        for (int k = 0; k < 2; k++) Add(p + Rnd(0.05f), Vector3.up * 0.4f + Rnd(0.3f), 0.45f, 0.08f, 0.35f, 0, Dust);
                        break;
                }
            }
        }

        /// <summary>Движение и угасание частиц; dt — время боя (в замедлении эффекты тоже медленные).</summary>
        public void Update(float dt)
        {
            if (dt <= 0) return;
            for (int i = 0; i < count; i++)
            {
                life[i] -= dt;
                if (life[i] <= 0)
                { // на место умершей — последняя
                    count--;
                    pos[i] = pos[count]; vel[i] = vel[count]; life[i] = life[count]; maxLife[i] = maxLife[count];
                    size[i] = size[count]; grow[i] = grow[count]; grav[i] = grav[count]; col[i] = col[count];
                    i--;
                    continue;
                }
                vel[i].y -= grav[i] * dt;
                vel[i] *= 1 - 1.8f * dt * (grav[i] > 5 ? 0.2f : 1f);
                pos[i] += vel[i] * dt;
                size[i] += grow[i] * dt;
            }
        }

        public void Draw()
        {
            for (int k = 0; k * 1023 < count; k++)
            {
                int n = Mathf.Min(1023, count - k * 1023);
                for (int j = 0; j < n; j++)
                {
                    int i = k * 1023 + j;
                    float t = life[i] / maxLife[i];
                    mats[j] = Matrix4x4.TRS(pos[i], Quaternion.identity, Vector3.one * Mathf.Max(0.01f, size[i]));
                    var c = col[i];
                    cols[j] = new Vector4(c.r, c.g, c.b, c.a * Mathf.Clamp01(t * 1.6f));
                }
                mpb.Clear();
                mpb.SetVectorArray(InstColor, cols);
                Graphics.DrawMeshInstanced(mesh, 0, mat, mats, n, mpb, ShadowCastingMode.Off, false, 0, null);
            }
        }

        public void Clear() => count = 0;
    }
}
