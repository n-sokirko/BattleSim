using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BattleSim.Core;

/// <summary>
/// Проверки ядра без Unity (запускаются в CI и локально):
///   models  — у каждого бойца каждой расы есть модель, снаряжение из Keep и клипы; все модели читаются;
///   env     — окружение: дом, ограда, ящик ломаются, мир это замечает (пути, видимость, укрытия);
///   battles — короткие бои на всех пяти картах: без исключений, NaN, скачков, бойцов внутри препятствий, и бой идёт.
/// dotnet run -c Release --project tests/CoreChecks [-- models|env|battles]
/// </summary>
static class Program
{
    static string Root, ModelsDir;
    static readonly List<string> Failures = new List<string>();
    static readonly StringBuilder Summary = new StringBuilder();

    static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Root = FindRoot();
        ModelsDir = Path.Combine(Root, "unity/Assets/BattleSim/Resources/Models");
        if (args.Length > 0 && args[0] == "battle") return BattleRun.Run(args.Skip(1).ToArray());

        bool all = args.Length == 0;
        var sw = Stopwatch.StartNew();
        if (all || args.Contains("models")) Models.Run();
        if (all || args.Contains("env")) EnvChecks.Run(Fail, CityDefs);
        if (all || args.Contains("battles")) Battles.Run();
        Console.WriteLine();
        Console.WriteLine($"Время: {sw.Elapsed.TotalSeconds:F0} с");
        WriteSummary();
        if (Failures.Count == 0) { Console.WriteLine("Все проверки пройдены."); return 0; }
        Console.WriteLine($"Не пройдено: {Failures.Count}");
        foreach (var f in Failures) Console.WriteLine("::error::" + f.Replace("%", "%25").Replace("\r", "").Replace("\n", "%0A"));
        return 1;
    }

    static string FindRoot()
    {
        for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "unity/Assets/BattleSim"))) return d.FullName;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "unity/Assets/BattleSim"))) return d.FullName;
        throw new Exception("Не найден корень репозитория (папка unity/Assets/BattleSim)");
    }

    static string Tail(string s, int n) => s.Length <= n ? s : "…" + s.Substring(s.Length - n);

    static void Fail(string what) { Failures.Add(what); Console.WriteLine("  ОШИБКА: " + what); }

    static void WriteSummary()
    {
        var path = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (string.IsNullOrEmpty(path)) return;
        var sb = new StringBuilder(Summary.ToString());
        sb.AppendLine(Failures.Count == 0 ? "**Все проверки ядра пройдены.**" : $"**Не пройдено: {Failures.Count}**");
        foreach (var f in Failures) sb.AppendLine("- " + f.Split('\n')[0]);
        File.AppendAllText(path, sb.ToString());
    }

    static List<CityDef> cityDefs;

    /// <summary>Размеры городских построек — из их моделей (как в игре).</summary>
    public static List<CityDef> CityDefs()
    {
        if (cityDefs != null) return cityDefs;
        cityDefs = new List<CityDef>();
        foreach (var (kind, name) in CityPlan.Models)
        {
            var doc = GltfDoc.Parse(File.ReadAllBytes(Path.Combine(ModelsDir, "city_" + name + ".bytes")));
            ModelKit.MergeStatic(doc, out float w, out float d, out float h);
            cityDefs.Add(new CityDef { Kind = kind, Name = name, W = w, D = d, H = h, Index = cityDefs.Count });
        }
        return cityDefs;
    }

    static GltfDoc Load(string name) => GltfDoc.Parse(File.ReadAllBytes(Path.Combine(ModelsDir, name + ".bytes")));

    /// <summary>Модели, снаряжение и клипы — то же, что берёт Game/ModelLibrary.</summary>
    static class Models
    {
        public static void Run()
        {
            Console.WriteLine("== Модели ==");
            var docs = new Dictionary<string, GltfDoc>();
            foreach (var f in Directory.GetFiles(ModelsDir, "*.bytes").OrderBy(f => f))
            {
                var name = Path.GetFileNameWithoutExtension(f);
                if (name.EndsWith(".png")) continue; // текстуры
                try { docs[name] = GltfDoc.Parse(File.ReadAllBytes(f)); }
                catch (Exception e) { Fail($"модель {name} не читается: {e.Message}"); }
            }
            Console.WriteLine($"  прочитано моделей: {docs.Count}");
            GltfDoc Doc(string n) => docs.TryGetValue(n, out var d) ? d : null;

            foreach (var (_, name) in CityPlan.Models)
                if (Doc("city_" + name) == null) Fail($"нет модели города city_{name}");
            var knight = Doc("Knight");
            foreach (var h in new[] { "Horse", "White_Horse" })
            {
                var d = Doc(h);
                if (d == null) { Fail($"нет модели коня {h}"); continue; }
                foreach (var c in new[] { "Idle", "Walk", "Gallop", "Death" })
                    if (d.Anim(c) == null) Fail($"{h}: нет клипа {c}");
            }

            int n = 0;
            var rows = new List<string>();
            foreach (var race in Defs.Races)
                foreach (var t in race.Units.Concat(new[] { race.Cmd, race.Msg, race.Cap, race.Hero }).Where(t => t != null).Distinct())
                {
                    n++;
                    string who = $"{race.Key}/{t.Key} «{t.Name}»";
                    var doc = Doc(t.Model);
                    if (doc == null) { Fail($"{who}: нет модели {t.Model}"); continue; }
                    SkinnedModel m;
                    try { m = ModelKit.PrepareCharacter(doc, t.Keep ?? new string[0], t.Mount ? 1.75f : 1.9f); }
                    catch (Exception e) { Fail($"{who}: модель {t.Model} не собирается: {e.Message}"); continue; }
                    foreach (var k in t.Keep ?? new string[0])
                        if (!m.Parts.Any(p => p.Name == k)) Fail($"{who}: в модели {t.Model} нет снаряжения {k}");
                    if (!(m.Max.y > 1 && m.Max.y < 3.5f) || !float.IsFinite(m.Min.y)) Fail($"{who}: странный рост модели {m.Min.y:F2}..{m.Max.y:F2}");

                    var a = t.Anim ?? new AnimSet();
                    var need = new List<string>();
                    if (t.Mount)
                    {
                        if (knight == null) continue;
                        need.AddRange(a.Attack.Length > 0 ? a.Attack : Defs.Types[3].Anim.Attack);
                        foreach (var c in need.Concat(new[] { "Idle", "Death_A", "Death_B" }))
                            if (knight.Anim(c) == null) Fail($"{who}: у всадников (Knight) нет клипа {c}");
                        if (knight.Anim("Ride") == null) Fail($"{who}: у Knight нет клипа посадки Ride");
                    }
                    else
                    {
                        need.AddRange(new[] { a.Idle, a.Run, a.Cheer, a.Aim, a.Reload, a.Melee }.Concat(a.Attack).Where(c => c != null));
                        if (t.ShieldWall) need.AddRange(new[] { "Shield_Wall_Idle", "Shield_Wall_Walk" });
                        if (t.Race != null && t.Race.Undead && t.Slot == 0) need.Add("Rise_Undead");
                        foreach (var c in need.Distinct())
                            if (doc.Anim(c) == null) Fail($"{who}: в модели {t.Model} нет клипа {c}");
                    }
                    rows.Add($"| {race.Name} | {t.Name} | {t.Model} | {m.Max.y:F2} | {string.Join(", ", t.Keep ?? new string[0])} |");
                }
            Console.WriteLine($"  бойцов проверено: {n}");
            Summary.AppendLine("### Модели бойцов").AppendLine()
                .AppendLine("| Раса | Боец | Модель | Рост, м | Снаряжение |").AppendLine("|---|---|---|---|---|");
            foreach (var r in rows) Summary.AppendLine(r);
            Summary.AppendLine();
        }
    }

    /// <summary>Бои: каждый в своём процессе (в ядре общий статический Rng), параллельно.</summary>
    static class Battles
    {
        // карта, зерно, расы: 5 карт × 3 зерна, расы по кругу — каждая встречается на каждой карте
        static IEnumerable<(MapType map, int seed, int r0, int r1)> Plan()
        {
            int i = 0;
            foreach (MapType map in Enum.GetValues(typeof(MapType)))
                foreach (var seed in new[] { 11, 37, 52 })
                {
                    int r = i % Defs.Races.Length;
                    yield return (map, seed, r, (r + 1 + i / Defs.Races.Length % (Defs.Races.Length - 1)) % Defs.Races.Length);
                    i++;
                }
        }

        public static void Run()
        {
            Console.WriteLine("== Бои ==");
            float secs = float.Parse(Environment.GetEnvironmentVariable("BATTLE_SECS") ?? "60", CultureInfo.InvariantCulture);
            var plan = Plan().ToArray();
            var results = new string[plan.Length];
            var host = Environment.ProcessPath;
            var self = typeof(Program).Assembly.Location;
            bool viaDotnet = Path.GetFileNameWithoutExtension(host) == "dotnet";
            Parallel.For(0, plan.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount) }, i =>
            {
                var (map, seed, r0, r1) = plan[i];
                var psi = new ProcessStartInfo(host) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = Root };
                if (viaDotnet) psi.ArgumentList.Add(self);
                foreach (var s in new[] { "battle", ((int)map).ToString(), seed.ToString(), "1", secs.ToString(CultureInfo.InvariantCulture), r0.ToString(), r1.ToString() }) psi.ArgumentList.Add(s);
                using var p = Process.Start(psi);
                var err = p.StandardError.ReadToEndAsync();
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                results[i] = p.ExitCode == 0 ? output.Trim() : $"CRASH {p.ExitCode}\n{output}\n{Tail(err.Result, 3000)}";
            });

            Summary.AppendLine($"### Бои ({secs:F0} с, сражение)").AppendLine()
                .AppendLine("| Карта | Зерно | Расы | Бойцов | Потери | Застревания | Скачки | В препятствиях | Глубже брода |").AppendLine("|---|---|---|---|---|---|---|---|---|");
            float stuckSum = 0; int stuckN = 0;
            for (int i = 0; i < plan.Length; i++)
            {
                var (map, seed, r0, r1) = plan[i];
                string title = $"{map} зерно {seed}, {Defs.Races[r0].Key} против {Defs.Races[r1].Key}";
                var line = results[i].Split('\n').FirstOrDefault(l => l.StartsWith("RESULT "));
                if (line == null) { Fail($"бой {title}: упал\n{results[i]}"); Summary.AppendLine($"| {map} | {seed} | {Defs.Races[r0].Key}–{Defs.Races[r1].Key} | упал | | | | | |"); continue; }
                var v = line.Substring(7).Split(' ').Select(kv => kv.Split('=')).ToDictionary(kv => kv[0], kv => kv[1]);
                Console.WriteLine($"  {title}: {line.Substring(7)}");
                foreach (var l in results[i].Split('\n').Where(l => l.StartsWith("  "))) Console.WriteLine("  " + l);
                int units = int.Parse(v["units"]), lost = int.Parse(v["lost"]), jumps = int.Parse(v["jumps"]), inObs = int.Parse(v["inObs"]), nan = int.Parse(v["nan"]), deep = int.Parse(v["deep"]);
                float stuck = float.Parse(v["stuck%"], CultureInfo.InvariantCulture);
                if (nan > 0) Fail($"бой {title}: у {nan} бойцов координаты NaN");
                if (jumps > 0) Fail($"бой {title}: скачков бойцов за кадр — {jumps}");
                if (inObs > 0) Fail($"бой {title}: бойцы внутри препятствий — {inObs} замеров");
                if (deep > 0) Fail($"бой {title}: живые бойцы глубже брода — {deep} замеров");
                if (lost == 0) Fail($"бой {title}: за {secs:F0} с никто не погиб — бой не завязался");
                if (stuck > MaxStuck) Fail($"бой {title}: застревания {stuck:F1}% (порог {MaxStuck}%)");
                stuckSum += stuck; stuckN++;
                Summary.AppendLine($"| {map} | {seed} | {Defs.Races[r0].Key}–{Defs.Races[r1].Key} | {units} | {lost} | {stuck:F1}% | {jumps} | {inObs} | {deep} |");
            }
            float avg = stuckSum / Math.Max(1, stuckN);
            Console.WriteLine($"  застревания в среднем {avg:F1}% (порог {MaxStuckAvg}%)");
            if (avg > MaxStuckAvg) Fail($"застревания в среднем по боям {avg:F1}% (порог {MaxStuckAvg}%)");
            Summary.AppendLine().AppendLine($"Застревания в среднем: {avg:F1}% (порог {MaxStuckAvg}%)").AppendLine();
        }

        /// <summary>Застревания — доля бойцов не в бою, которые хотят идти, но за секунду сдвинулись меньше чем на 25 см.
        /// Сейчас в среднем ~6%, в лесу до 12%: порог на средний и грубый — на один бой.</summary>
        const float MaxStuckAvg = 10, MaxStuck = 25;
    }
}
