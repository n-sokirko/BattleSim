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
