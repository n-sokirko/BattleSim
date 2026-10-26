using System;
using System.Collections.Generic;

namespace BattleSim.Core
{
    /// <summary>
    /// Герой игрока: ходит туда, куда ведёт джойстик (сам бьёт, кто рядом), растёт от убитых врагов
    /// и имеет четыре способности вокруг себя. Войско при нём стоит на приказах или под рукой воеводы.
    /// </summary>
    public sealed partial class Battle
    {
        /// <summary>Героем (первым богатырём синих) управляет игрок напрямую.</summary>
        public bool HeroControl;
        /// <summary>С какого уровня и опыта начинается бой (переходит из главы в главу).</summary>
        public int HeroStartLevel = 1;
        public float HeroStartXp;
        public int HeroLevel = 1;
        public float HeroXp;
        /// <summary>Куда ведёт героя джойстик, в осях ядра (длина 0..1).</summary>
        public float JoyX, JoyZ;
        public readonly float[] AbilityReady = new float[4];
        public float HeroGuardUntil;
        /// <summary>События героя для интерфейса (новый уровень, способность открыта).</summary>
        public readonly Queue<string> HeroEvents = new Queue<string>();

        public Unit PlayerHero => HeroControl ? Heroes[0] : null;
        public bool HeroMode_() => PlayerHero != null && PlayerHero.Alive;

        public const int MaxLevel = 10;
        public static readonly float[] XpAt = { 0, 40, 110, 210, 350, 530, 750, 1020, 1350, 1750 };
        public static readonly float[] AbilityCd = { 40, 30, 45, 90 };
        public static readonly int[] AbilityLevel = { 1, 3, 5, 8 };
        public static readonly string[] AbilityName = { "Боевой клич", "Удар о землю", "Вызов", "Непоколебимые" };
        public static readonly string[] AbilityNote =
        {
            "Свои отряды рядом: +20% урона и дух на 10 с",
            "Враги вокруг оглушены на 2 с и отброшены",
            "Враги вокруг бьют только героя 6 с, урон по нему −40%",
            "Свои отряды рядом не бегут и получают −30% урона 12 с",
        };

        public float HeroDmgMul => 1 + 0.12f * (HeroLevel - 1);
        public float HeroDefMul => MathF.Pow(0.93f, HeroLevel - 1);

        /// <summary>Сколько секунд до готовности способности (0 — готова).</summary>
        public float AbilityLeft(int i) => MathF.Max(0, AbilityReady[i] - Time);
        public bool AbilityOpen(int i) => HeroLevel >= AbilityLevel[i];
        /// <summary>Доля опыта до следующего уровня (1 — предел).</summary>
        public float XpFrac => HeroLevel >= MaxLevel ? 1 : (HeroXp - XpAt[HeroLevel - 1]) / (XpAt[HeroLevel] - XpAt[HeroLevel - 1]);

        /// <summary>Приказ касанием: идти в точку или бить конкретного врага (джойстик отменяет).</summary>
        public V2? HeroGoal;
        public Unit HeroFoe;

        public void HeroMove(V2 p) { HeroGoal = World.ClampField(p, 2); HeroFoe = null; }
        public void HeroAttack(Unit e) { HeroFoe = e; HeroGoal = null; }

        void HeroInit()
        {
            HeroGoal = null; HeroFoe = null;
            HeroLevel = M.Clamp(HeroStartLevel, 1, MaxLevel);
            HeroXp = MathF.Max(HeroStartXp, XpAt[HeroLevel - 1]);
            Array.Clear(AbilityReady, 0, 4);
            HeroGuardUntil = 0; JoyX = JoyZ = 0;
            HeroEvents.Clear();
        }

        void HeroKillXp(Unit dead, Unit src)
        {
            float a = dead.T.Hero ? 80 : dead.T.Special != Special.None ? 6 : 1;
            if (src == PlayerHero) a *= 1.5f;
            HeroXp += a;
            while (HeroLevel < MaxLevel && HeroXp >= XpAt[HeroLevel])
            {
                HeroLevel++;
                var h = PlayerHero;
                if (h != null && h.Alive)
                {
                    h.Hp = MathF.Min(h.T.Hp, h.Hp + h.T.Hp * 0.4f);
                    Emit(FxKind.Cry, h.Pos, 0, 0, 0);
                }
                string msg = $"Новый уровень: {HeroLevel}";
                for (int i = 0; i < 4; i++) if (AbilityLevel[i] == HeroLevel) msg += $" · открыта способность «{AbilityName[i]}»";
                HeroEvents.Enqueue(msg);
                AddLog(0, $"{h?.Squad.Title ?? "Герой"}: {msg.ToLowerInvariant()}");
            }
        }

        /// <summary>Каждый шаг: герой не бежит, «Непоколебимые» держат дух.</summary>
        void HeroTick(float dt)
        {
            var h = PlayerHero;
            if (h == null) return;
            if (h.Alive) h.Squad.Morale = 100;
            foreach (var s in Squads)
                if (s.Team == 0 && !s.Special && s.Alive > 0 && Time < s.GuardUntil && s.Order.Mode != Mode.Rout) s.Morale = MathF.Max(s.Morale, 55);
        }

        /// <summary>Применяет способность героя i (0..3). Возвращает, почему не вышло (null — вышло).</summary>
        public string Cast(int i)
        {
            var h = PlayerHero;
            if (h == null || !h.Alive || !Fighting) return "герой не в бою";
            if (!AbilityOpen(i)) return $"«{AbilityName[i]}» откроется на {AbilityLevel[i]}-м уровне";
            if (Time < AbilityReady[i]) return $"«{AbilityName[i]}» ещё не готова";
            if (h.DownT > 0) return "герой сбит с ног";
            AbilityReady[i] = Time + AbilityCd[i];
            switch (i)
            {
                case 0: // боевой клич
                    foreach (var s in Squads)
                    {
                        if (s.Team != 0 || s.Special || s.Alive == 0 || D2d(s.Center, h.Pos) > 20) continue;
                        s.Morale = MathF.Min(100, s.Morale + 15);
                        s.BuffUntil = Time + 10;
                        Emit(FxKind.Cry, s.Center, 0, 0, 0);
                    }
                    Emit(FxKind.Roar, h.Pos, 0, 0, 0);
                    break;
                case 1: // удар о землю
                    Emit(FxKind.Explosion, h.Pos, 0, 0, 0);
                    foreach (var e in Teams[1].ToArray())
                    {
                        if (!e.Alive || e.T.Special == Special.Messenger) continue;
                        float ex = e.Pos.x - h.Pos.x, ez = e.Pos.z - h.Pos.z, d = M.Hypot(ex, ez);
                        if (d > 6 + e.T.Radius || MathF.Abs(e.Pos.y - h.Pos.y) > 2.5f) continue;
                        float dd = MathF.Max(d, 0.1f), k = 4f / e.T.Mass;
                        e.DownT = 2f; e.DownAnim = 0; e.AtkT = -1;
                        e.Knock.x += ex / dd * k; e.Knock.z += ez / dd * k;
                        Damage(e, 30, 0, 0, false, h);
                    }
                    break;
                case 2: // вызов
                    foreach (var e in Teams[1])
                    {
                        if (!e.Alive || e.T.Special != Special.None || e.T.Hero) continue;
                        if (D2d(e.Pos, h.Pos) > 15) continue;
                        e.TauntUntil = Time + 6; e.TauntBy = h; e.Target = h; e.RetargetT = 1;
                    }
                    HeroGuardUntil = Time + 6;
                    Emit(FxKind.Roar, h.Pos, 0, 0, 0);
                    break;
                default: // непоколебимые
                    foreach (var s in Squads)
                    {
                        if (s.Team != 0 || s.Special || s.Alive == 0 || D2d(s.Center, h.Pos) > 25) continue;
                        s.GuardUntil = Time + 12;
                        Emit(FxKind.Wall, s.Center, MathF.Sin(s.Facing), MathF.Cos(s.Facing), 0);
                    }
                    break;
            }
            AddLog(0, $"Способность героя: «{AbilityName[i]}»");
            return null;
        }

        Unit NearestFoe(Unit u, float r)
        {
            Unit best = null;
            float bd = r * r;
            foreach (var e in Teams[1 - u.Team])
            {
                if (!e.Alive || e.T.Special == Special.Messenger) continue;
                if (e.Duel != null && e.Duel != u) continue;
                if (MathF.Abs(e.Pos.y - u.Pos.y) > 2.5f) continue;
                float dx = e.Pos.x - u.Pos.x, dz = e.Pos.z - u.Pos.z, d2 = dx * dx + dz * dz;
                if (d2 < bd) { bd = d2; best = e; }
            }
            return best;
        }

        /// <summary>
        /// Герой игрока: джойстик ведёт его, куда указано; рядом враг — бьёт; джойстик отпущен —
        /// сам встречает ближнего врага (до 10 м) и держится рядом.
        /// </summary>
        void ThinkPlayerHero(Unit u, float dt)
        {
            var t = u.T;
            u.Settled = false;
            float jl = M.Hypot(JoyX, JoyZ);
            bool joy = jl > 0.15f;
            if (u.Duel != null && !u.Duel.Alive) u.Duel = null;
            if (joy) { HeroGoal = null; HeroFoe = null; }
            if (HeroFoe != null && !HeroFoe.Alive) HeroFoe = null;
            if (HeroFoe != null) { u.Target = HeroFoe; u.RetargetT = 0.25f; }
            else if (u.Target == null || !u.Target.Alive || u.RetargetT <= 0)
            {
                u.RetargetT = 0.25f;
                u.Target = u.Duel ?? NearestFoe(u, 14);
            }
            HeroTarget(u); // сошлись вплотную с вражеским богатырём — поединок
            if (u.Duel != null) u.Target = u.Duel;
            var tg = u.Target;
            float dx = 0, dz = 0;
            if (joy)
            {
                float k = MathF.Min(1, jl);
                dx = JoyX / jl * k * t.Speed; dz = JoyZ / jl * k * t.Speed;
            }
            if (tg != null)
            {
                float tx = tg.Pos.x - u.Pos.x, tz = tg.Pos.z - u.Pos.z, d = M.Hypot(tx, tz);
                float nx = d > 1e-4f ? tx / d : MathF.Sin(u.Yaw), nz = d > 1e-4f ? tz / d : MathF.Cos(u.Yaw);
                float contact = t.Radius + tg.T.Radius + t.Reach;
                if (d <= contact && MathF.Abs(tg.Pos.y - u.Pos.y) < 1.8f)
                {
                    u.Engaged = true;
                    u.Face(nx, nz, dt);
                    if (u.Cooldown <= 0 && u.AtkT < 0 && t.Dmg > 0) StartAttack(u, false);
                }
                else if (!joy && (d <= 10 && HeroGoal == null || HeroFoe == tg))
                {
                    var w = d > 6 ? NavTarget(u, tg.Pos.x, tg.Pos.z) : tg.P;
                    float ll = M.Hypot(w.x - u.Pos.x, w.z - u.Pos.z);
                    if (ll == 0) ll = 1;
                    dx = (w.x - u.Pos.x) / ll * t.Speed; dz = (w.z - u.Pos.z) / ll * t.Speed;
                }
            }
            if (!joy && HeroFoe == null && HeroGoal.HasValue)
            { // шаг к точке, куда послал игрок
                var g = HeroGoal.Value;
                float gd = M.Hypot(g.x - u.Pos.x, g.z - u.Pos.z);
                if (gd < 1.2f) HeroGoal = null;
                else
                {
                    var w = gd > 4 ? NavTarget(u, g.x, g.z) : g;
                    float ll = M.Hypot(w.x - u.Pos.x, w.z - u.Pos.z);
                    if (ll == 0) ll = 1;
                    dx = (w.x - u.Pos.x) / ll * t.Speed; dz = (w.z - u.Pos.z) / ll * t.Speed;
                }
            }
            if (u.AtkT >= 0) { dx *= 0.3f; dz *= 0.3f; }
            Steer(u, dx, dz, dt, !u.Engaged && dx * dx + dz * dz > 0.04f);
            if (u.AtkT >= 0)
            {
                u.AtkT += dt / u.AtkDur;
                if (!u.HitDone && u.AtkT >= 0.5f) { u.HitDone = true; ResolveHit(u); }
                if (u.AtkT >= 1) u.AtkT = -1;
            }
        }
    }
}
