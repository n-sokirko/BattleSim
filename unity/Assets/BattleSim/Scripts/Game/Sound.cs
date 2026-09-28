using System.Collections.Generic;
using BattleSim.Core;
using UnityEngine;

namespace BattleSim
{
    /// <summary>
    /// Звук боя. Все звуки синтезируются в коде при запуске (без файлов): лязг стали, глухие удары, свист стрел,
    /// взрывы, рог, барабан, боевой клич, рёв орды, колдовство Нави, всплески. События берутся из тех же FxEvent,
    /// что и частицы; громкость и панорама — по положению относительно камеры. Фоновый гул сечи зависит от того,
    /// сколько бойцов рубится рядом с камерой. Замедление режиссёра опускает тон — как в кино.
    /// </summary>
    public sealed class Sound
    {
        const int Rate = 22050, Voices = 24;
        const float Hear = 95f; // дальше — не слышно

        public bool Muted;
        public float Volume = 0.8f;

        readonly AudioSource[] voices = new AudioSource[Voices];
        readonly float[] busyUntil = new float[Voices];
        readonly AudioSource ambient;
        readonly Transform root;
        readonly Dictionary<string, AudioClip[]> bank = new Dictionary<string, AudioClip[]>();
        readonly Dictionary<string, float> cool = new Dictionary<string, float>();
        readonly HashSet<Bolt> heardBolts = new HashSet<Bolt>();
        readonly List<Bolt> tmpBolts = new List<Bolt>();
        int started;
        /// <summary>Сколько раз звучал каждый звук — для лога снимков.</summary>
        public readonly Dictionary<string, int> Played = new Dictionary<string, int>();

        public Sound(Camera cam)
        {
            if (cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            root = new GameObject("Sound").transform;
            for (int i = 0; i < Voices; i++)
            {
                var go = new GameObject("voice" + i);
                go.transform.SetParent(root);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false; s.spatialBlend = 1; s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 10; s.maxDistance = Hear; s.dopplerLevel = 0;
                voices[i] = s;
            }
            var ag = new GameObject("ambient");
            ag.transform.SetParent(root);
            ambient = ag.AddComponent<AudioSource>();
            ambient.loop = true; ambient.spatialBlend = 0; ambient.volume = 0; ambient.playOnAwake = false;

            var rnd = new System.Random(7);
            bank["clang"] = Many(4, k => Clang(rnd, 900 + k * 170));
            bank["thud"] = Many(3, k => Thud(rnd, 70 + k * 18));
            bank["whoosh"] = Many(3, k => Whoosh(rnd, 0.28f + k * 0.05f));
            bank["thunk"] = Many(2, k => Thunk(rnd, 170 + k * 60));
            bank["boom"] = Many(2, k => Boom(rnd, 1.3f + k * 0.3f));
            bank["horn"] = new[] { Horn(rnd, 146, 1.7f), Horn(rnd, 110, 2.2f) };
            bank["drum"] = Many(2, k => Drum(rnd, 80 + k * 12));
            bank["cry"] = Many(2, k => Crowd(rnd, 1.3f, 190 + k * 30));
            bank["roar"] = new[] { Roar(rnd) };
            bank["spell"] = new[] { Spell(rnd) };
            bank["curse"] = Many(2, k => Curse(rnd, 700 + k * 150));
            bank["splash"] = Many(2, k => Splash(rnd));
            ambient.clip = Murmur(rnd);
        }

        static AudioClip[] Many(int n, System.Func<int, AudioClip> f) { var a = new AudioClip[n]; for (int i = 0; i < n; i++) a[i] = f(i); return a; }

        // ---------------------------------------------------------------- воспроизведение

        /// <summary>События боя этого кадра → звуки. focus — куда смотрит камера (для гула сечи).</summary>
        public void Update(Battle b, Vector3 cam, Vector3 focus, float slowMo, bool paused, bool fighting)
        {
            AudioListener.volume = Muted ? 0 : Volume;
            float pitch = Mathf.Lerp(0.72f, 1f, Mathf.InverseLerp(0.3f, 1f, slowMo));
            started = 0;
            if (!paused)
            {
                foreach (var e in b.Fx)
                {
                    var p = Conv.U(e.Pos);
                    switch (e.Kind)
                    {
                        case FxKind.Block: Play("clang", p, cam, 0.55f, 0.07f, pitch); break;
                        case FxKind.Hit: Play(Random.value < 0.35f ? "clang" : "thud", p, cam, 0.45f, 0.06f, pitch); break;
                        case FxKind.Kill: Play("thud", p, cam, 0.5f, 0.1f, pitch * 0.85f); break;
                        case FxKind.Charge: Play("thud", p, cam, 0.9f, 0.08f, pitch * 0.7f); Play("drum", p, cam, 0.5f, 0.4f, pitch); break;
                        case FxKind.Explosion: Play("boom", p, cam, 1f, 0.15f, pitch); break;
                        case FxKind.Cry: Play("cry", p, cam, 0.8f, 1.2f, pitch * Random.Range(0.92f, 1.08f)); break;
                        case FxKind.Roar: Play("roar", p, cam, 1f, 3f, pitch, far: true); break;
                        case FxKind.Rout: Play("horn", p, cam, 0.45f, 4f, pitch * 0.85f, far: true, clip: 1); break;
                        case FxKind.Duel: Play("horn", p, cam, 0.8f, 3f, pitch, far: true, clip: 0); break;
                        case FxKind.Hero: Play("boom", p, cam, 0.7f, 1f, pitch * 0.6f); Play("drum", p, cam, 0.8f, 1f, pitch * 0.8f); break;
                        case FxKind.Wall: Play("clang", p, cam, 0.7f, 1.5f, pitch * 0.8f); Play("drum", p, cam, 0.5f, 1.5f, pitch * 1.2f); break;
                        case FxKind.Raise: Play("spell", p, cam, 0.7f, 1.5f, pitch); break;
                        case FxKind.Curse: Play("curse", p, cam, 0.45f, 0.2f, pitch); break;
                        case FxKind.Splash: Play("splash", p, cam, 0.5f, 0.1f, pitch); break;
                        case FxKind.BoltGround: Play("thunk", p, cam, 0.3f, 0.05f, pitch); break;
                        case FxKind.Down: Play("thud", p, cam, 0.5f, 0.1f, pitch * 0.75f); break;
                    }
                }
                // стрелы и болты: свист при вылете (не каждый — залп и так слышен)
                tmpBolts.Clear();
                foreach (var a in b.Bolts.List)
                {
                    if (!a.Flying) continue;
                    tmpBolts.Add(a);
                    if (heardBolts.Contains(a)) continue;
                    heardBolts.Add(a);
                    if (Random.value < 0.35f) Play("whoosh", Conv.U(a.From), cam, a.AoeR > 0 ? 0.5f : 0.3f, 0.05f, pitch * (a.AoeR > 0 ? 0.6f : 1f));
                }
                heardBolts.IntersectWith(tmpBolts);
            }
            // гул сечи: сколько бойцов рубится рядом с тем местом, куда смотрит камера
            int near = 0;
            if (fighting && !paused)
                foreach (var u in b.Units)
                    if (u.Alive && u.Engaged)
                    {
                        float dx = u.Pos.x - focus.x, dz = -u.Pos.z - focus.z;
                        if (dx * dx + dz * dz < 40 * 40) near++;
                    }
            float camDist = Vector3.Distance(cam, focus);
            float want = Mathf.Clamp01(near / 40f) * Mathf.Lerp(1f, 0.35f, Mathf.InverseLerp(20, 120, camDist)) * 0.55f;
            ambient.volume = Mathf.MoveTowards(ambient.volume, want, Time.unscaledDeltaTime * 0.4f);
            ambient.pitch = pitch;
            if (ambient.volume > 0.001f && !ambient.isPlaying) ambient.Play();
            else if (ambient.volume <= 0.001f && ambient.isPlaying) ambient.Stop();
        }

        /// <summary>Сыграть звук в точке p. gap — не чаще раза в gap секунд на вид звука (толпа не ревёт тысячей голосов).</summary>
        void Play(string name, Vector3 p, Vector3 cam, float vol, float gap, float pitch, bool far = false, int clip = -1)
        {
            if (Muted || started >= 5) return;
            float d = Vector3.Distance(p, cam);
            if (d > (far ? Hear * 2 : Hear)) return;
            float now = Time.unscaledTime;
            if (cool.TryGetValue(name, out float t) && now < t) return;
            cool[name] = now + gap * Random.Range(0.8f, 1.2f);
            int best = -1;
            for (int i = 0; i < Voices; i++) if (!voices[i].isPlaying || now > busyUntil[i]) { best = i; break; }
            if (best < 0) return;
            var clips = bank[name];
            var c = clips[clip >= 0 ? Mathf.Min(clip, clips.Length - 1) : Random.Range(0, clips.Length)];
            var s = voices[best];
            s.transform.position = far ? Vector3.Lerp(cam, p, Mathf.Min(1, 30 / Mathf.Max(1, d))) : p; // дальние сигналы слышны, но тише
            s.clip = c; s.volume = vol; s.pitch = pitch * Random.Range(0.93f, 1.07f);
            s.Play();
            busyUntil[best] = now + c.length / Mathf.Max(0.3f, s.pitch);
            started++;
            Played[name] = Played.TryGetValue(name, out int pn) ? pn + 1 : 1;
        }

        // ---------------------------------------------------------------- синтез

        static AudioClip Clip(string name, float[] d)
        {
            float peak = 1e-4f;
            foreach (var v in d) peak = Mathf.Max(peak, Mathf.Abs(v));
            for (int i = 0; i < d.Length; i++) d[i] = d[i] / peak * 0.9f;
            var c = AudioClip.Create(name, d.Length, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        static float Noise(System.Random r) => (float)(r.NextDouble() * 2 - 1);
        const float Tau = Mathf.PI * 2;

        /// <summary>Лязг стали: негармонические обертоны с быстрым затуханием и щелчок удара.</summary>
        static AudioClip Clang(System.Random r, float f0)
        {
            int n = (int)(Rate * 0.5f);
            var d = new float[n];
            float[] ratio = { 1f, 2.76f, 5.4f, 8.93f, 13.3f }, amp = { 1f, 0.7f, 0.45f, 0.3f, 0.18f }, tau = { 0.3f, 0.2f, 0.12f, 0.08f, 0.05f };
            float det = 1 + (float)r.NextDouble() * 0.01f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, v = 0;
                for (int k = 0; k < ratio.Length; k++)
                    v += amp[k] * Mathf.Exp(-t / tau[k]) * (Mathf.Sin(Tau * f0 * ratio[k] * t) + 0.3f * Mathf.Sin(Tau * f0 * ratio[k] * det * t));
                if (t < 0.004f) v += Noise(r) * 2.5f * (1 - t / 0.004f);
                d[i] = v;
            }
            return Clip("clang", d);
        }

        /// <summary>Глухой удар по телу/щиту: низкий тон и приглушённый шум.</summary>
        static AudioClip Thud(System.Random r, float f0)
        {
            int n = (int)(Rate * 0.3f);
            var d = new float[n];
            float lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                lp += (Noise(r) - lp) * 0.09f;
                d[i] = lp * 3f * Mathf.Exp(-t / 0.045f) + Mathf.Sin(Tau * f0 * (1 - 0.3f * t) * t) * Mathf.Exp(-t / 0.08f);
            }
            return Clip("thud", d);
        }

        /// <summary>Свист стрелы: полосовой шум с нарастанием и спадом.</summary>
        static AudioClip Whoosh(System.Random r, float len)
        {
            int n = (int)(Rate * len);
            var d = new float[n];
            float lp = 0, lp2 = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, x = t / len;
                float a = 0.15f + 0.35f * x; // выше к концу — стрела пролетает мимо
                lp += (Noise(r) - lp) * a; lp2 += (lp - lp2) * 0.05f;
                float env = Mathf.Sin(Mathf.PI * x); env *= env;
                d[i] = (lp - lp2) * env;
            }
            return Clip("whoosh", d);
        }

        /// <summary>Стрела вонзилась: короткий щелчок и деревянный стук.</summary>
        static AudioClip Thunk(System.Random r, float f0)
        {
            int n = (int)(Rate * 0.12f);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                d[i] = (t < 0.005f ? Noise(r) * (1 - t / 0.005f) : 0) + 0.8f * Mathf.Sin(Tau * f0 * t) * Mathf.Exp(-t / 0.03f);
            }
            return Clip("thunk", d);
        }

        /// <summary>Взрыв бомбы: низкий гул, раскат шума и падающий тон.</summary>
        static AudioClip Boom(System.Random r, float len)
        {
            int n = (int)(Rate * len);
            var d = new float[n];
            float lp = 0, lp2 = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                lp += (Noise(r) - lp) * 0.06f; lp2 += (lp - lp2) * 0.2f;
                float env = Mathf.Min(1, t / 0.004f) * Mathf.Exp(-t / (len * 0.28f));
                d[i] = lp2 * 4f * env + 0.9f * Mathf.Sin(Tau * (55 - 25 * Mathf.Min(1, t / len)) * t) * Mathf.Exp(-t / (len * 0.35f));
            }
            return Clip("boom", d);
        }

        /// <summary>Боевой рог: пила с подъёмом тона и вибрато, приглушённая, с квинтой.</summary>
        static AudioClip Horn(System.Random r, float f0, float len)
        {
            int n = (int)(Rate * len);
            var d = new float[n];
            float ph = 0, ph2 = 0, lp = 0, lp2 = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = f0 * (0.94f + 0.06f * Mathf.Min(1, t / 0.18f)) * (1 + 0.006f * Mathf.Sin(Tau * 5.2f * t));
                ph += f / Rate; ph2 += f * 1.5f / Rate;
                float saw = 2 * (ph - Mathf.Floor(ph)) - 1, saw2 = 2 * (ph2 - Mathf.Floor(ph2)) - 1;
                float v = saw + 0.35f * saw2 + 0.05f * Noise(r);
                lp += (v - lp) * 0.12f; lp2 += (lp - lp2) * 0.18f;
                float env = Mathf.Min(1, t / 0.14f) * Mathf.Min(1, (len - t) / 0.35f);
                d[i] = lp2 * env;
            }
            return Clip("horn", d);
        }

        /// <summary>Барабан: падающий низкий тон и хлопок.</summary>
        static AudioClip Drum(System.Random r, float f0)
        {
            int n = (int)(Rate * 0.5f);
            var d = new float[n];
            float ph = 0, lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                ph += f0 * (0.6f + 0.4f * Mathf.Exp(-t * 12)) / Rate;
                lp += (Noise(r) - lp) * 0.2f;
                d[i] = Mathf.Sin(Tau * ph) * Mathf.Exp(-t / 0.2f) + lp * 1.5f * Mathf.Exp(-t / 0.025f);
            }
            return Clip("drum", d);
        }

        /// <summary>Боевой клич: десяток голосов с разным тоном, подъём и дыхание толпы.</summary>
        static AudioClip Crowd(System.Random r, float len, float center)
        {
            int n = (int)(Rate * len), vn = 12;
            var d = new float[n];
            var f = new float[vn]; var ph = new float[vn]; var vib = new float[vn]; var start = new float[vn];
            for (int k = 0; k < vn; k++) { f[k] = center * (0.75f + 0.6f * (float)r.NextDouble()); vib[k] = 4 + 3 * (float)r.NextDouble(); start[k] = 0.12f * (float)r.NextDouble(); }
            float lp = 0, lp2 = 0, br = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, v = 0;
                for (int k = 0; k < vn; k++)
                {
                    if (t < start[k]) continue;
                    float tt = t - start[k];
                    ph[k] += f[k] * (1 + 0.12f * Mathf.Min(1, tt / 0.35f)) * (1 + 0.012f * Mathf.Sin(Tau * vib[k] * t)) / Rate;
                    v += 2 * (ph[k] - Mathf.Floor(ph[k])) - 1;
                }
                br += (Noise(r) - br) * 0.3f;
                v = v / vn + br * 0.35f;
                lp += (v - lp) * 0.22f; lp2 += (lp - lp2) * 0.35f; // «гласная»: срез верхов
                float env = Mathf.Min(1, t / 0.12f) * Mathf.Min(1, (len - t) / 0.45f);
                d[i] = lp2 * env;
            }
            return Clip("cry", d);
        }

        /// <summary>Рёв орды: низкие пилы с рычащей модуляцией и хрипом.</summary>
        static AudioClip Roar(System.Random r)
        {
            float len = 2f;
            int n = (int)(Rate * len);
            var d = new float[n];
            float ph = 0, ph2 = 0, lp = 0, br = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = 72 * (1 + 0.2f * Mathf.Min(1, t / 0.5f) - 0.25f * Mathf.Max(0, t - 1.3f));
                ph += f / Rate; ph2 += f * 1.51f / Rate;
                float growl = 0.6f + 0.4f * Mathf.Sin(Tau * 27 * t);
                br += (Noise(r) - br) * 0.25f;
                float v = ((2 * (ph - Mathf.Floor(ph)) - 1) + 0.6f * (2 * (ph2 - Mathf.Floor(ph2)) - 1)) * growl + br * 0.8f;
                lp += (v - lp) * 0.15f;
                d[i] = lp * Mathf.Min(1, t / 0.2f) * Mathf.Min(1, (len - t) / 0.5f);
            }
            return Clip("roar", d);
        }

        /// <summary>Колдовство Нави: скользящий вниз «потусторонний» тон с дрожью и гул земли.</summary>
        static AudioClip Spell(System.Random r)
        {
            float len = 1.5f;
            int n = (int)(Rate * len);
            var d = new float[n];
            float ph = 0, ph2 = 0, lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, x = t / len;
                float f = 900 * Mathf.Pow(0.33f, x);
                ph += f / Rate; ph2 += f * 1.49f / Rate;
                lp += (Noise(r) - lp) * 0.02f;
                float trem = 0.6f + 0.4f * Mathf.Sin(Tau * 11 * t);
                d[i] = (Mathf.Sin(Tau * ph) + 0.5f * Mathf.Sin(Tau * ph2)) * trem * 0.5f * Mathf.Min(1, t / 0.1f) * (1 - x) + lp * 5f * Mathf.Sin(Mathf.PI * x);
            }
            return Clip("spell", d);
        }

        /// <summary>Порча: короткий нисходящий «вжих» с искрами.</summary>
        static AudioClip Curse(System.Random r, float f0)
        {
            float len = 0.35f;
            int n = (int)(Rate * len);
            var d = new float[n];
            float ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, x = t / len;
                ph += f0 * (1 - 0.65f * x) / Rate;
                d[i] = (Mathf.Sin(Tau * ph) + 0.2f * Noise(r) * (1 - x)) * Mathf.Exp(-t / 0.12f);
            }
            return Clip("curse", d);
        }

        /// <summary>Всплеск: шипящий шум и пара «пузырей».</summary>
        static AudioClip Splash(System.Random r)
        {
            float len = 0.5f;
            int n = (int)(Rate * len);
            var d = new float[n];
            float lp = 0;
            float b1 = 400 + 400 * (float)r.NextDouble(), b2 = 600 + 500 * (float)r.NextDouble();
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float nz = Noise(r); lp += (nz - lp) * 0.5f;
                float v = (nz - lp) * Mathf.Exp(-t / 0.1f);
                if (t > 0.08f) v += 0.3f * Mathf.Sin(Tau * b1 * (1 + 2 * (t - 0.08f)) * t) * Mathf.Exp(-(t - 0.08f) / 0.04f);
                if (t > 0.18f) v += 0.25f * Mathf.Sin(Tau * b2 * (1 + 2 * (t - 0.18f)) * t) * Mathf.Exp(-(t - 0.18f) / 0.03f);
                d[i] = v;
            }
            return Clip("splash", d);
        }

        /// <summary>
        /// Гул сечи (петля 4 с): бурый шум толпы и россыпь далёкого лязга. Концы сведены — петля без щелчка.
        /// </summary>
        static AudioClip Murmur(System.Random r)
        {
            float len = 4f;
            int n = (int)(Rate * len);
            var d = new float[n];
            float br = 0, lp = 0;
            for (int i = 0; i < n; i++)
            {
                br = Mathf.Clamp(br + Noise(r) * 0.04f, -1, 1) * 0.998f;
                lp += (Noise(r) - lp) * 0.08f;
                d[i] = br * 0.8f + lp * 0.5f;
            }
            // далёкий лязг и удары
            for (int k = 0; k < 26; k++)
            {
                int at = r.Next(n);
                float f0 = 900 + (float)r.NextDouble() * 900, a = 0.08f + 0.12f * (float)r.NextDouble();
                bool clang = r.NextDouble() < 0.6;
                for (int i = 0; i < Rate / 3 && at + i < n; i++)
                {
                    float t = i / (float)Rate;
                    d[at + i] += clang ? a * Mathf.Sin(Tau * f0 * t) * Mathf.Exp(-t / 0.08f) + a * 0.6f * Mathf.Sin(Tau * f0 * 2.76f * t) * Mathf.Exp(-t / 0.05f)
                                       : a * 2 * Mathf.Sin(Tau * 90 * t) * Mathf.Exp(-t / 0.06f);
                }
            }
            // сводим концы петли
            int fade = Rate / 4;
            for (int i = 0; i < fade; i++)
            {
                float w = i / (float)fade;
                d[i] = d[i] * w + d[n - fade + i] * (1 - w);
            }
            System.Array.Resize(ref d, n - fade);
            return Clip("murmur", d);
        }
    }
}
