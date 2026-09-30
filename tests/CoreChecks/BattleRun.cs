using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BattleSim.Core;

/// <summary>Один бой в отдельном процессе: карта зерно размер секунды раса0 раса1 → строка RESULT k=v ...</summary>
static class BattleRun
{
    public static int Run(string[] a)
    {
        var map = (MapType)int.Parse(a[0]);
        int seed = int.Parse(a[1]), size = int.Parse(a[2]);
        float secs = float.Parse(a[3], CultureInfo.InvariantCulture);
        int r0 = int.Parse(a[4]), r1 = int.Parse(a[5]);
        var models = Path.Combine(Directory.GetCurrentDirectory(), "unity/Assets/BattleSim/Resources/Models");

        var cityDefs = new List<CityDef>();
        foreach (var (kind, name) in CityPlan.Models)
        {
            var doc = GltfDoc.Parse(File.ReadAllBytes(Path.Combine(models, "city_" + name + ".bytes")));
            ModelKit.MergeStatic(doc, out float w, out float d, out float h);
            cityDefs.Add(new CityDef { Kind = kind, Name = name, W = w, D = d, H = h, Index = cityDefs.Count });
        }

        Rng.Shared = new Rng(seed * 7 + 3);
        var style = Style.Make(seed % 4, 0);
        style.Tweak(map);
        var world = new World();
        world.Generate(seed, style, map, size == 3, cityDefs);
        var b = new Battle(world);
        b.Races[0] = Defs.Races[r0]; b.Races[1] = Defs.Races[r1];
        b.RandomArmies(size);
        b.StartFight();

        const float dt = 1f / 30;
        int ticks = (int)(secs / dt);
        long samples = 0, stuck = 0, stuckSamples = 0, inObs = 0, jumps = 0, nan = 0, deep = 0;
        var notes = new List<string>();
        var prev = new Dictionary<Unit, V3>();
        var lastSec = new Dictionary<Unit, V3>();
        var fallen = new HashSet<Unit>(); // погибшие в бою (гонцы, доехавшие до места, не в счёт)
        int insideMax = 0;
        for (int k = 0; k < ticks; k++)
        {
            b.Tick(dt);
            foreach (var u in b.Units)
            {
                if (!u.Alive)
                {
                    if (prev.Remove(u) && u.T.Special != Special.Messenger) fallen.Add(u);
                    continue;
                }
                if (u.Inside != null) { prev.Remove(u); insideMax = Math.Max(insideMax, u.Inside.Occupants.Count); continue; } // засел в доме: вход и выход — не скачок
                if (!float.IsFinite(u.Pos.x) || !float.IsFinite(u.Pos.y) || !float.IsFinite(u.Pos.z))
                {
                    if (nan++ < 3) notes.Add($"NaN t={b.Time:F1} {u.T.Name}");
                    prev.Remove(u);
                    continue;
                }
                // скачок: за кадр вверх-вниз больше 0,6 м или вбок дальше, чем позволяют скорость и отброс
                if (prev.TryGetValue(u, out var p) && (Math.Abs(u.Pos.y - p.y) > 0.6f
                    || M.Hypot(u.Pos.x - p.x, u.Pos.z - p.z) > (u.T.Speed * 1.6f + M.Hypot(u.Knock.x, u.Knock.z)) * dt + 0.3f))
                    if (jumps++ < 3) notes.Add($"скачок t={b.Time:F1} {u.T.Name} ({p.x:F1},{p.y:F1},{p.z:F1})->({u.Pos.x:F1},{u.Pos.y:F1},{u.Pos.z:F1})");
                prev[u] = u.Pos;
            }
            if (k % 30 == 0)
                foreach (var u in b.Units)
                {
                    if (!u.Alive) continue;
                    if (lastSec.TryGetValue(u, out var lp) && !u.Engaged && u.DownT <= 0)
                    {
                        stuckSamples++;
                        if (M.Hypot(u.Vel.x, u.Vel.z) > 0.6f && M.Hypot(u.Pos.x - lp.x, u.Pos.z - lp.z) < 0.25f) stuck++;
                    }
                    lastSec[u] = u.Pos;
                }
            if (k % 10 != 0) continue;
            foreach (var u in b.Units)
            {
                if (!u.Alive || !float.IsFinite(u.Pos.x) || u.Inside != null) continue;
                samples++;
                var o = world.Obs.Hit(u.Pos.x, u.Pos.z, -0.1f);
                if (o != null && o.Top > 0.3f && o.Ground + o.Top > u.Pos.y + 0.3f && inObs++ < 3)
                    notes.Add($"в препятствии t={b.Time:F1} {u.T.Name} ({u.Pos.x:F1},{u.Pos.y:F1},{u.Pos.z:F1})");
                float g = world.HeightAt(u.Pos.x, u.Pos.z), s = world.Decks.Surface(u.Pos.x, u.Pos.z, g);
                bool onDeck = s > g + 0.05f && u.Pos.y > g + 0.05f;
                if (!onDeck && World.Water - u.Pos.y > World.WadeMax + 0.08f && deep++ < 3)
                    notes.Add($"глубже брода t={b.Time:F1} {u.T.Name} ({u.Pos.x:F1},{u.Pos.y:F2},{u.Pos.z:F1})");
            }
        }
        int lost = fallen.Count;
        Console.WriteLine(FormattableString.Invariant(
            $"RESULT units={b.Units.Count} lost={lost} stuck%={100.0 * stuck / Math.Max(1, stuckSamples):F2} jumps={jumps} inObs={inObs} deep={deep} nan={nan} samples={samples} ruined={world.Env.Ruined} ignited={world.Env.Ignited}"));
        int garrisons = b.Log.Count(e => e.Text.Contains(" засели ")), smoked = b.Log.Count(e => e.Text.Contains("выкурили") || e.Text.Contains("рухнул на головы"));
        if (garrisons > 0) notes.Add($"гарнизонов {garrisons}, выкурено/завалено {smoked}, в одном доме до {insideMax}");
        var broken = world.Env.All.Where(o => o.State == EnvState.Ruined).GroupBy(o => $"{o.Kind}/{o.LastHarm}").Select(g => $"{g.Key}×{g.Count()}").ToArray();
        if (broken.Length > 0) notes.Add("разрушено: " + string.Join(", ", broken));
        foreach (var n in notes) Console.WriteLine("  " + n);
        return 0;
    }
}
