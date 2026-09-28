using System.Collections.Generic;
using BattleSim.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattleSim
{
    /// <summary>
    /// Знамёна отрядов: у каждого отряда знаменосец в первой шеренге несёт флаг цвета своей армии —
    /// на общем плане сразу видно, где чей отряд и куда он идёт. Бегущий отряд знамя роняет набок,
    /// погиб знаменосец — знамя подхватывает сосед. Все знамёна — три вызова отрисовки (древко, полотнище, навершие).
    /// </summary>
    public sealed class SquadBanners
    {
        const int Batch = 1023;
        readonly Mesh pole, flag, tip;
        readonly Material poleMat, flagMat, tipMat;
        readonly Matrix4x4[] mp = new Matrix4x4[Batch], mf = new Matrix4x4[Batch], mt = new Matrix4x4[Batch];
        readonly Vector4[] cf = new Vector4[Batch];
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static readonly int InstColor = Shader.PropertyToID("_InstColor");
        readonly Dictionary<Squad, Unit> bearer = new Dictionary<Squad, Unit>();
        readonly Dictionary<Squad, float> tilt = new Dictionary<Squad, float>();
        readonly Vector4[] teamCol = new Vector4[2];

        public SquadBanners()
        {
            pole = Banner.Cylinder(0.03f, 0.04f, 3.1f, 6);
            tip = Banner.Octahedron(0.08f);
            flag = MakeFlag();
            poleMat = ModelLibrary.NewLit(null, Conv.Col(Rgb.Hex(0x5b3a22)), "banner-pole");
            flagMat = ModelLibrary.NewLit(null, Color.white, "banner-flag");
            flagMat.SetFloat("_Cull", 0);
            tipMat = ModelLibrary.NewLit(null, Conv.Col(Rgb.Hex(0xe8c15a)), "banner-tip");
            tipMat.SetFloat("_Spec", 0.6f);
            foreach (var m in new[] { poleMat, flagMat, tipMat }) m.enableInstancing = true;
            for (int t = 0; t < 2; t++)
            {
                var c = Conv.Col(Rgb.Hex(Defs.Teams[t].Color));
                teamCol[t] = new Vector4(c.r, c.g, c.b, 1);
            }
        }

        /// <summary>
        /// Полотнище 0,95 × 0,62 м от древка вбок, с застывшей волной и тёмной каймой снизу
        /// (цвет вершин × цвет армии копии).
        /// </summary>
        static Mesh MakeFlag()
        {
            int seg = 8;
            var v = new List<Vector3>(); var c = new List<Color>(); var tr = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float x = 0.95f * i / seg, wave = 0.07f * Mathf.Sin(i * 0.9f) * (i / (float)seg);
                v.Add(new Vector3(x, -0.62f, wave)); c.Add(new Color(0.62f, 0.62f, 0.62f));
                v.Add(new Vector3(x, -0.5f, wave)); c.Add(Color.white);
                v.Add(new Vector3(x, 0f, wave)); c.Add(Color.white);
            }
            for (int i = 0; i < seg; i++)
                for (int k = 0; k < 2; k++)
                {
                    int a = i * 3 + k, b = a + 3;
                    tr.AddRange(new[] { a, a + 1, b + 1, a, b + 1, b });
                }
            var m = new Mesh { name = "banner-flag" };
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(tr, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Знаменосец — живой в первой шеренге ближе всех к середине строя (держим, пока жив).</summary>
        Unit Bearer(Squad sq)
        {
            if (bearer.TryGetValue(sq, out var u) && u.Alive && u.Squad == sq) return u;
            Unit best = null; float bs = float.MaxValue;
            foreach (var x in sq.Units)
            {
                if (!x.Alive) continue;
                float dx = x.Pos.x - sq.Center.x, dz = x.Pos.z - sq.Center.z, s = dx * dx + dz * dz + (x.Row > 0 ? 4 * x.Row : 0);
                if (s < bs) { bs = s; best = x; }
            }
            if (best != null) bearer[sq] = best; else bearer.Remove(sq);
            return best;
        }

        public void Draw(Battle b, float alpha, float time, float riderHips, Camera cam)
        {
            int n = 0;
            var cp = cam != null ? cam.transform.position : Vector3.zero;
            foreach (var sq in b.Squads)
            {
                if (sq.Special || sq.T.Hero || sq.Alive < 2 || sq.Hidden || sq.Master != null) continue; // поднятым мертвецам знамя не положено
                var u = Bearer(sq);
                if (u == null) continue;
                var p = Conv.U(u.RenderPos(alpha));
                // вблизи камеры знамя только мешает разглядеть бойцов
                if (cam != null && (p - cp).sqrMagnitude < 14 * 14) continue;
                float yaw = u.RenderYaw(alpha);
                bool rout = sq.Order.Mode == Mode.Rout;
                tilt.TryGetValue(sq, out float tl);
                tl = Mathf.MoveTowards(tl, rout ? 50 : 0, Time.deltaTime * 90);
                tilt[sq] = tl;
                float h = u.T.Mount ? riderHips + 0.35f : 0.9f;
                var baseRot = Conv.Yaw(yaw);
                // древко за правым плечом, чуть назад; при бегстве клонится вбок
                var at = p + baseRot * new Vector3(0.28f, h * u.Scale, -0.18f);
                var rot = baseRot * Quaternion.Euler(0, 0, -tl);
                float s = u.Scale;
                mp[n] = Matrix4x4.TRS(at + rot * new Vector3(0, 1.55f * s, 0), rot, Vector3.one * s); // цилиндр древка — от середины
                var top = at + rot * new Vector3(0, 3.1f * s, 0);
                mt[n] = Matrix4x4.TRS(top + rot * new Vector3(0, 0.05f * s, 0), rot, Vector3.one * s);
                // полотнище колышется: поворот вокруг древка по ветру и времени
                float sway = 18 * Mathf.Sin(time * 2.1f + sq.GetHashCode() % 17) + 10 * Mathf.Sin(time * 3.7f + n);
                mf[n] = Matrix4x4.TRS(top - rot * new Vector3(0, 0.06f * s, 0), rot * Quaternion.Euler(0, 90 + sway, 0), Vector3.one * s);
                cf[n] = teamCol[sq.Team];
                if (++n == Batch) { Flush(n); n = 0; }
            }
            if (n > 0) Flush(n);
            // забываем знаменосцев исчезнувших отрядов
            if (bearer.Count > b.Squads.Count * 2) { bearer.Clear(); tilt.Clear(); }
        }

        void Flush(int n)
        {
            Graphics.DrawMeshInstanced(pole, 0, poleMat, mp, n, null, ShadowCastingMode.On, true, 0, null);
            Graphics.DrawMeshInstanced(tip, 0, tipMat, mt, n, null, ShadowCastingMode.Off, true, 0, null);
            mpb.Clear();
            mpb.SetVectorArray(InstColor, cf);
            Graphics.DrawMeshInstanced(flag, 0, flagMat, mf, n, mpb, ShadowCastingMode.On, true, 0, null);
        }

        public void Clear() { bearer.Clear(); tilt.Clear(); }
    }
}
