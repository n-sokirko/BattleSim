using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BattleSim.Core;

/// <summary>
/// Окружение: ломаем дом, ограду и ящик и проверяем, что мир это заметил — препятствия нет, по руинам проходят,
/// сквозь них видно, а сетка путей, пересчитанная на месте, совпадает с построенной заново.
/// </summary>
static class EnvChecks
{
    public static void Run(Action<string> fail, Func<List<CityDef>> cityDefs)
    {
        Console.WriteLine("== Окружение ==");
        int n = 0;
        foreach (var seed in new[] { 11, 37 })
        {
            var w = Make(MapType.City, seed, cityDefs());
            string at = $"город, зерно {seed}";
            // дом: деревянный, у которого по обе стороны свободно
            var house = w.Env.All.FirstOrDefault(o => o.Kind == EnvKind.House && o.Breakable && o.Mat == EnvMat.Wood && o.Rect && Across(w, o, out _, out _));
            if (house == null) { fail($"{at}: не нашлось деревянного дома для проверки"); continue; }
            Across(w, house, out var a, out var b);
            if (w.Obs.Hit(house.X, house.Z) == null) fail($"{at}: дом до разрушения — не препятствие");
            if (w.Nav.SpeedAt(house.X, house.Z, 0) > 0) fail($"{at}: клетка дома проходима до разрушения");
            if (w.Los(a.x, w.GroundAt(a.x, a.z) + 1.5f, a.z, b.x, w.GroundAt(b.x, b.z) + 1.5f, b.z)) fail($"{at}: сквозь целый дом видно");
            int ver = w.Nav.Version;
            w.Env.Hurt(house, house.MaxHp * 0.3f, Harm.Blast);
            if (house.State != EnvState.Intact) fail($"{at}: дом рухнул или повреждён от 30% урона");
            w.Env.Hurt(house, house.MaxHp * 0.3f, Harm.Blast);
            if (house.State != EnvState.Damaged) fail($"{at}: дом не повреждён после 60% урона");
            w.Env.Hurt(house, house.MaxHp, Harm.Blast);
            if (house.State != EnvState.Ruined) fail($"{at}: дом не рухнул");
            if (w.Obs.Hit(house.X, house.Z) != null) fail($"{at}: на месте рухнувшего дома осталось препятствие");
            float sp = w.Nav.SpeedAt(house.X, house.Z, 0);
            if (!(sp > 0 && sp < 1)) fail($"{at}: по руинам не идут медленно (скорость {sp:F2})");
            if (!w.Los(a.x, w.GroundAt(a.x, a.z) + 1.5f, a.z, b.x, w.GroundAt(b.x, b.z) + 1.5f, b.z)) fail($"{at}: сквозь руины не видно");
            if (w.Nav.Version != ver + 1) fail($"{at}: сетка путей не пересчитана");
            if (!w.Env.Changed.Contains(house)) fail($"{at}: Unity-слой не узнает о рухнувшем доме");
            if (w.Nav.Region[0][w.Nav.Idx(a.x, a.z)] != w.Nav.Region[0][w.Nav.Idx(b.x, b.z)]) fail($"{at}: через руины не пройти насквозь");
            if (!w.CoverCandidates(new V2(0, -200)).Any(c => c.Kind == CoverKind.Low && c.X == house.X && c.Z == house.Z)) fail($"{at}: руины не стали низким укрытием");
            n++;

            // ограда и ящик
            var wall = w.Env.All.FirstOrDefault(o => o.Kind == EnvKind.Wall && o.Breakable);
            if (wall != null)
            {
                w.Env.Hurt(wall, 1e6f, Harm.Blast);
                if (!wall.Wall.Broken || w.Obs.Hit(wall.X, wall.Z) != null || w.WallTop(wall.X, wall.Z) > float.NegativeInfinity) fail($"{at}: проломленная ограда осталась преградой");
                n++;
            }
            var prop = w.Env.All.FirstOrDefault(o => o.Kind == EnvKind.Prop && o.Breakable);
            if (prop != null)
            {
                w.Env.Hurt(prop, 5, Harm.Blade);
                if (prop.State == EnvState.Ruined) fail($"{at}: ящик разбит одним слабым ударом");
                w.Env.Hurt(prop, 100, Harm.Charge);
                var left = w.Obs.Hit(prop.X, prop.Z);
                if (prop.State != EnvState.Ruined || (left != null && left.Env == prop)) fail($"{at}: натиск не разбил ящик (состояние {prop.State}, препятствие {(left == null ? "нет" : left.Env == null ? "камень" : left.Env.Kind.ToString())})");
                n++;
            }
            // камень не рубят и не топчут
            var stone = w.Env.All.FirstOrDefault(o => o.Kind == EnvKind.House && o.Breakable && o.Mat == EnvMat.Stone);
            if (stone != null)
            {
                w.Env.Hurt(stone, 1e6f, Harm.Charge);
                if (stone.State != EnvState.Intact) fail($"{at}: каменный дом рухнул от натиска конницы");
                n++;
            }
            // пересчёт на месте = постройка заново
            var fresh = new NavGrid(w);
            string d = Diff(w.Nav, fresh);
            if (d != null) fail($"{at}: сетка путей после разрушений не совпадает с построенной заново: {d}");
            // реванш: всё как до боя
            w.Env.Restore();
            var clean = Make(MapType.City, seed, cityDefs());
            d = Diff(w.Nav, clean.Nav);
            if (d != null) fail($"{at}: после восстановления сетка путей не как у нетронутого мира: {d}");
            if (w.Obs.List.Count != clean.Obs.List.Count || w.WallGrid.All.Count != clean.WallGrid.All.Count || w.Env.All.Any(o => o.State != EnvState.Intact || o.Hp != o.MaxHp))
                fail($"{at}: после восстановления не всё целое");
            n++;
        }
        Console.WriteLine($"  проверок разрушения: {n}");
        Fire(fail, cityDefs);
        Garrison(fail, cityDefs);
    }

    /// <summary>
    /// Гарнизон: арбалетчики Руси держат место у деревянного дома — засаживаются в него, бьют из окон; на них идут
    /// пехота и лучники Степи: пехота рубится у стен, лучники жгут дом огненными стрелами, пока не выкурят.
    /// </summary>
    static void Garrison(Action<string> fail, Func<List<CityDef>> cityDefs)
    {
        const string at = "гарнизон, город, зерно 11";
        var w = Make(MapType.City, 11, cityDefs());
        var b = new Battle(w);
        b.Races[0] = Defs.Races[0]; b.Races[1] = Defs.Races[3];
        // дом: деревянный, у которого с одной стороны есть место для отряда, а в 35 м — для врага
        EnvObj house = null; V2 hold = default, foe = default;
        foreach (var o in w.Env.All.Where(o => Battle.Garrisonable(o) && o.Mat == EnvMat.Wood))
        {
            foreach (var dir in new[] { new V2(0, 1), new V2(0, -1), new V2(1, 0), new V2(-1, 0) })
            {
                var e = o.Edge(o.X + dir.x * 50, o.Z + dir.z * 50, 4);
                var f = new V2(o.X + dir.x * 40, o.Z + dir.z * 40);
                if (!w.Walkable(e.x, e.z, 1.5f) || !w.Walkable(f.x, f.z, 3) || !w.InField(f.x, f.z, 4) || w.Nav.Region[0][w.Nav.Idx(e.x, e.z)] != w.Nav.Region[0][w.Nav.Idx(f.x, f.z)]) continue;
                // из окна виден подступ (не стена замка и не соседний дом), и отряд выберет именно этот дом
                var win = o.Edge(f.x, f.z, 0.35f);
                float wy = o.Y + System.MathF.Min(o.Top * 0.45f, 3), fy = w.GroundAt(f.x, f.z) + 1;
                if (!w.Los(win.x, wy, win.z, f.x, fy, f.z) || !w.Los(f.x, fy, f.z, win.x, wy, win.z)) continue;
                float mine = o.Dist(e.x, e.z);
                if (w.Env.Near(e.x, e.z, 12).Any(q => q != o && Battle.Garrisonable(q) && q.Dist(e.x, e.z) + (q.Mat == EnvMat.Stone ? -4 : 0) <= mine)) continue;
                house = o; hold = e; foe = f; break;
            }
            if (house != null) break;
        }
        if (house == null) { fail($"{at}: не нашлось дома для проверки"); return; }
        var rus = Defs.Races[0]; var st = Defs.Races[3];
        float yawToFoe = System.MathF.Atan2(foe.x - hold.x, foe.z - hold.z);
        b.PlaceSquad(rus.Units[2].Id, 0, hold.x, hold.z, yawToFoe);
        b.PlaceSquad(st.Units[0].Id, 1, foe.x, foe.z, yawToFoe + System.MathF.PI);
        // лучники — на 24 м от дома, на подступе, откуда видно окна
        // точка без домов вокруг (иначе лучники сами засядут), в 12–24 м от дома
        var arc = new V2(house.X + (foe.x - house.X) * 0.45f, house.Z + (foe.z - house.Z) * 0.45f);
        for (float f = 0.3f; f <= 0.6f; f += 0.05f)
        {
            var p = new V2(house.X + (foe.x - house.X) * f, house.Z + (foe.z - house.Z) * f);
            if (w.Walkable(p.x, p.z, 2) && !w.Env.Near(p.x, p.z, 13).Any(Battle.Garrisonable)) { arc = p; break; }
        }
        b.PlaceSquad(st.Units[2].Id, 1, arc.x, arc.z, yawToFoe + System.MathF.PI);
        b.StartFight();
        var def = b.Squads.First(q => q.Team == 0);
        def.Garrison = true;
        def.Order = new Order(OrderKind.Hold, Mode.Hold) { Leash = 12 }.At(hold);
        var archers = b.Squads.First(q => q.Team == 1 && q.T.Ranged);
        archers.Garrison = true;
        archers.Order = new Order(OrderKind.Fire, Mode.Hold) { Leash = 6 }.At(arc);
        float claimed = -1, maxIn = 0, ignited = -1, released = -1, shotsOut = 0, wallFights = 0, fireArrows = 0;
        const float dt = 1f / 30;
        var seen = new HashSet<Bolt>();
        for (int k = 0; k < 150 * 30 && def.Alive > 0; k++)
        {
            b.Tick(dt);
            if (claimed < 0 && def.House != null) { claimed = b.Time; house = def.House; }
            maxIn = System.Math.Max(maxIn, house.Occupants.Count);
            if (ignited < 0 && house.Fire > 0) ignited = b.Time;
            if (claimed >= 0 && released < 0 && def.House == null) released = b.Time;
            foreach (var bl in b.Bolts.List)
                if (seen.Add(bl))
                {
                    if (bl.Team == 0 && house.Dist(bl.From.x, bl.From.z) < 1) shotsOut++;
                    if (bl.Burning) fireArrows++;
                }
            foreach (var u in b.Units) if (u.Alive && u.Team == 1 && u.Engaged && u.Target?.Inside != null) wallFights++;
            // пока сидят — не видны и стоят внутри; вышли — никого внутри не числится
            if (def.House == null && def.Units.Any(u => u.Inside != null)) { fail($"{at}: отряд вышел, а бойцы числятся в доме"); break; }
        }
        Console.WriteLine($"  гарнизон: засели через {claimed:F0} с, внутри до {maxIn}, выстрелов из окон {shotsOut}, огненных стрел {fireArrows}, схваток у стен {wallFights / 30:F0} с, дом загорелся на {ignited:F0} с, вышли на {released:F0} с; в отряде {def.Alive} из {def.Units.Count}");
        if (claimed < 0 || claimed > 20) fail($"{at}: арбалетчики не засели в дом (через {claimed:F0} с)");
        if (maxIn < 4) fail($"{at}: в дом вошло только {maxIn}");
        if (shotsOut == 0) fail($"{at}: из окон не стреляли");
        if (fireArrows == 0) fail($"{at}: по засевшим в деревянном доме не пускали огненных стрел");
        if (ignited < 0 && house.State != EnvState.Ruined && def.Alive > 0) fail($"{at}: за 150 с дом не подожгли и гарнизон цел");
        if (b.Units.Any(u => u.Inside != null && !u.Alive)) fail($"{at}: мёртвые числятся в доме");
    }

    /// <summary>Огонь: дом горит и рушится, огонь переходит на соседей, жар и дым есть, пока горит, камень не горит.</summary>
    static void Fire(Action<string> fail, Func<List<CityDef>> cityDefs)
    {
        var w = Make(MapType.City, 11, cityDefs());
        const string at = "огонь, город, зерно 11";
        var house = w.Env.All.Where(o => o.Kind == EnvKind.House && o.Breakable && o.Mat == EnvMat.Wood && o.Rect && Across(w, o, out _, out _))
            .OrderByDescending(o => w.Env.All.Count(n => n != o && n.Mat == EnvMat.Wood && n.Kind == EnvKind.House && n.Dist(o.X, o.Z) < o.Bound + 4)).FirstOrDefault();
        if (house == null) { fail($"{at}: нет деревянного дома"); return; }
        Across(w, house, out var a, out var b);
        var stone = w.Env.All.FirstOrDefault(o => o.Kind == EnvKind.House && o.Mat == EnvMat.Stone && o.Breakable);
        if (stone != null && w.Env.Ignite(stone, 1)) fail($"{at}: загорелся каменный дом");
        if (!w.Env.Ignite(house, 0.3f)) fail($"{at}: деревянный дом не загорелся");
        float t = 0, ruinedAt = -1, smokeAt = -1, heat = 0;
        const float dt = 1f / 30;
        while (t < 150 && w.Env.Burning.Count > 0)
        {
            w.Env.Tick(dt, null);
            t += dt;
            if (ruinedAt < 0 && house.State == EnvState.Ruined) ruinedAt = t;
            heat = System.MathF.Max(heat, w.Nav.Heat[w.Nav.Idx(house.X, house.Z)]);
            if (smokeAt < 0 && house.Fire > 0.9f && !w.Los(a.x, w.GroundAt(a.x, a.z) + 1.5f, a.z, b.x, w.GroundAt(b.x, b.z) + 1.5f, b.z)) smokeAt = t;
        }
        Console.WriteLine($"  огонь: дом рухнул через {ruinedAt:F0} с, загоралось {w.Env.Ignited}, догорело за {t:F0} с");
        if (ruinedAt < 25 || ruinedAt > 90) fail($"{at}: горящий дом рухнул через {ruinedAt:F0} с (ждём 25–90)");
        if (heat < 0.5f) fail($"{at}: у горящего дома нет жара в сетке путей");
        if (smokeAt < 0) fail($"{at}: сквозь дым горящего дома видно");
        if (w.Env.Burning.Count > 0) fail($"{at}: пожар не догорел за 150 с");
        if (w.Nav.Heat.Any(h => h > 0)) fail($"{at}: после пожара в сетке путей остался жар");
        if (!house.Burnt) fail($"{at}: дом не выгорел дотла");
        // лес: огонь бежит по деревьям, но не весь лес за минуту
        var f = Make(MapType.Forest, 11, cityDefs());
        var tree = f.Env.All.Where(o => o.Kind == EnvKind.Tree).OrderBy(o => System.MathF.Abs(o.X) + System.MathF.Abs(o.Z)).First();
        f.Env.Ignite(tree, 1);
        for (t = 0; t < 60; t += dt) f.Env.Tick(dt, null);
        int trees = f.Env.All.Count(o => o.Kind == EnvKind.Tree);
        Console.WriteLine($"  огонь в лесу: за 60 с загорелось {f.Env.Ignited} деревьев из {trees}, горит {f.Env.Burning.Count}");
        if (f.Env.Ignited < 2) fail("огонь в лесу не перекинулся ни на одно дерево");
        if (f.Env.Ignited > trees / 3) fail($"огонь в лесу за минуту охватил {f.Env.Ignited} деревьев из {trees} — слишком быстро");
    }

    static World Make(MapType map, int seed, List<CityDef> defs)
    {
        Rng.Shared = new Rng(seed * 7 + 3);
        var style = Style.Make(seed % 4, 0);
        style.Tweak(map);
        var w = new World();
        w.Generate(seed, style, map, false, defs);
        return w;
    }

    /// <summary>Точки по обе стороны дома вдоль его оси, на свободной проходимой земле.</summary>
    static bool Across(World w, EnvObj o, out V2 a, out V2 b)
    {
        float c = System.MathF.Cos(o.Rot), s = System.MathF.Sin(o.Rot), d = o.Hx + 2.2f;
        a = new V2(o.X - c * d, o.Z - s * d);
        b = new V2(o.X + c * d, o.Z + s * d);
        bool Free(V2 p) => w.Obs.Hit(p.x, p.z, 0.5f) == null && w.Nav.SpeedAt(p.x, p.z, 0) > 0 && !w.OnDeck(p.x, p.z);
        if (!Free(a) || !Free(b)) return false;
        // между ними — только этот дом
        for (float t = 0.05f; t < 1; t += 0.05f)
        {
            float x = a.x + (b.x - a.x) * t, z = a.z + (b.z - a.z) * t;
            var h = w.Obs.Hit(x, z, 0.3f);
            if (h != null && h.Env != o) return false;
            if (w.WallTop(x, z) > float.NegativeInfinity || w.OnDeck(x, z)) return false;
        }
        return System.MathF.Abs(w.GroundAt(a.x, a.z) - w.GroundAt(b.x, b.z)) < 0.8f;
    }

    static string Diff(NavGrid x, NavGrid y)
    {
        int D = x.Dim;
        for (int i = 0; i < D * D; i++)
        {
            if (x.Speed[0][i] != y.Speed[0][i] || x.Speed[1][i] != y.Speed[1][i]) return $"скорость в клетке {i}";
            if (x.Surf[i] != y.Surf[i] || x.Conceal[i] != y.Conceal[i] || x.Canopy[i] != y.Canopy[i]) return $"клетка {i}";
            int ix = i % D, iz = i / D;
            if (ix + 1 < D && x.Step(i, i + 1, false) != y.Step(i, i + 1, false)) return $"шаг {i}→x+1";
            if (iz + 1 < D && x.Step(i, i + D, false) != y.Step(i, i + D, false)) return $"шаг {i}→z+1";
            if (ix + 1 < D && iz + 1 < D && x.Step(i, i + D + 1, true) != y.Step(i, i + D + 1, true)) return $"шаг {i}→диагональ";
            if (x.Region[0][i] >= 0 != y.Region[0][i] >= 0) return $"связность {i}";
        }
        return x.Barriers != y.Barriers ? $"барьеров {x.Barriers} и {y.Barriers}" : null;
    }
}
