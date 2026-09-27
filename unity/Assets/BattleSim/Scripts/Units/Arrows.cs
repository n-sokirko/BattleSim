using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattleSim
{
    /// <summary>Стрелы летят по дуге с упреждением. Промахнувшиеся втыкаются в землю.</summary>
    public class Arrows
    {
        class Arrow
        {
            public Transform T;
            public Vector3 From, To;
            public float Age, Dur, Arc, Damage, Stuck;
            public int Team;
            public bool Flying;
        }

        const int MaxArrows = 700;
        const float StuckTime = 9f;

        readonly Battle battle;
        readonly WorldGen world;
        readonly Transform root;
        readonly List<Arrow> active = new List<Arrow>();
        readonly Stack<Arrow> pool = new Stack<Arrow>();
        readonly System.Random rnd = new System.Random();

        public Arrows(Battle battle, WorldGen world)
        {
            this.battle = battle;
            this.world = world;
            root = new GameObject("Arrows").transform;
        }

        float R() => (float)rnd.NextDouble();

        public void Fire(Unit src, Unit dst)
        {
            Arrow a = Take();
            if (a == null) return;
            Vector3 from = src.Pos + Vector3.up * 1.45f + Quaternion.Euler(0f, src.Yaw, 0f) * new Vector3(-0.25f, 0f, 0.5f);
            Vector3 flat = dst.Pos - src.Pos; flat.y = 0f;
            float d = flat.magnitude;
            float dur = d / 26f + 0.35f;
            Vector3 aim = dst.Pos + dst.Vel * dur;
            float spread = 0.35f + d * 0.035f;
            float ang = R() * Mathf.PI * 2f, rad = Mathf.Sqrt(R()) * spread;
            aim.x += Mathf.Cos(ang) * rad;
            aim.z += Mathf.Sin(ang) * rad;
            aim.y = world.HeightAt(aim.x, aim.z) + 1.0f;

            a.From = from; a.To = aim; a.Age = 0f; a.Dur = dur;
            a.Arc = 1.2f + d * 0.22f;
            a.Team = src.Team; a.Damage = src.S.Damage; a.Flying = true;
            a.T.gameObject.SetActive(true);
            Place(a, 0f);
            active.Add(a);
        }

        Arrow Take()
        {
            if (pool.Count > 0) return pool.Pop();
            if (active.Count < MaxArrows) return Create();
            // Переиспользуем самую старую воткнувшуюся стрелу.
            for (int i = 0; i < active.Count; i++)
            {
                if (!active[i].Flying)
                {
                    var a = active[i];
                    active.RemoveAt(i);
                    return a;
                }
            }
            return null;
        }

        Arrow Create()
        {
            var go = new GameObject("Arrow");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = UnitArt.ArrowMesh();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = GameMaterials.Units;
            r.shadowCastingMode = ShadowCastingMode.Off;
            go.SetActive(false);
            return new Arrow { T = go.transform };
        }

        void Place(Arrow a, float s)
        {
            Vector3 p = Vector3.Lerp(a.From, a.To, s) + Vector3.up * (a.Arc * 4f * s * (1f - s));
            Vector3 v = (a.To - a.From) / a.Dur + Vector3.up * (a.Arc * 4f * (1f - 2f * s) / a.Dur);
            a.T.SetPositionAndRotation(p, Quaternion.LookRotation(v));
        }

        public void Tick(float dt)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var a = active[i];
                if (a.Flying)
                {
                    a.Age += dt;
                    float s = a.Age / a.Dur;
                    if (s < 1f) { Place(a, s); continue; }
                    Place(a, 1f);
                    var hit = battle.EnemyNear(a.To, a.Team, 0.35f);
                    if (hit != null)
                    {
                        Vector3 dir = a.To - a.From; dir.y = 0f;
                        battle.Damage(hit, a.Damage, dir.normalized * 0.6f / hit.S.Mass, true, null);
                        Release(i);
                        continue;
                    }
                    // Промах: стрела втыкается в землю под углом.
                    a.Flying = false;
                    a.Stuck = StuckTime;
                    Vector3 fwd = a.T.forward;
                    float ground = world.HeightAt(a.To.x, a.To.z);
                    Vector3 tip = a.To + fwd * (Mathf.Max(0f, a.To.y - ground) / Mathf.Max(0.3f, -fwd.y));
                    tip.y = world.HeightAt(tip.x, tip.z);
                    a.T.position = tip - fwd * 0.32f;
                }
                else
                {
                    a.Stuck -= dt;
                    if (a.Stuck <= 0f) Release(i);
                }
            }
        }

        void Release(int index)
        {
            var a = active[index];
            active.RemoveAt(index);
            a.T.gameObject.SetActive(false);
            pool.Push(a);
        }

        public void Clear()
        {
            for (int i = active.Count - 1; i >= 0; i--) Release(i);
        }
    }
}
