using System.Collections;
using System.Collections.Generic;
using BattleSim.Core;
using UnityEngine;

namespace BattleSim
{
    public enum Phase { Loading, Setup, Fight, Result }

    /// <summary>
    /// Точка входа: повесьте на пустой объект в сцене (это делает меню BattleSim → Настроить проект)
    /// и нажмите Play. Мир, армии, камера и интерфейс создаются из кода.
    /// </summary>
    public class GameMain : MonoBehaviour
    {
        [Tooltip("0 — случайная карта при каждом запуске")]
        public int Seed;

        public Phase Phase = Phase.Loading;
        public World World = new World();
        public Battle Battle;
        public ModelLibrary Lib = new ModelLibrary();
        public CameraRig Rig;
        public Camera Cam;
        public Style Style;
        public bool LowEnd;
        /// <summary>Режиссёр боя: сам выбирает планы, когда включён автоматический режим камеры.</summary>
        public Director Director;
        /// <summary>Автоматическая камера — режиссёр (иначе — простой облёт).</summary>
        public bool DirectorOn = true;
        /// <summary>Замедление времени от режиссёра (1 — нет), множится на скорость боя.</summary>
        public float SlowMo = 1;
        bool directorWanted; // игрок включил режиссёра: вернуть ему камеру после простоя

        // выбор игрока
        public int Type, Team, MapSel = -1, ArmySize = 2;
        public bool Eraser, Paused, Big;
        public float Speed = 1;
        public int Winner = -1;
        public float BattleTime;
        public MapType MapType;
        public int MapSeed;
        public string MapName = "Готовим поле…";
        public string Toast;
        public float ToastT;
        public readonly List<LogEntry> Chronicle = new List<LogEntry>();
        public bool HelpOpen;
        public float LoadProgress;
        public string LoadText = "Собираем войска и рисуем карту…";

        WorldView view = new WorldView();
        Overlays overlays;
        Light sun;
        Hud hud;
        float resultDelay;
        int lastBiome = -1;
        bool pausedByHelp;
        readonly Dictionary<Unit, Banner> banners = new Dictionary<Unit, Banner>();
        readonly Dictionary<Unit, Matrix4x4> bannerM = new Dictionary<Unit, Matrix4x4>();
        Plane[] frustum = new Plane[6];

        const float LodDistHigh = 55f, LodDistLow = 38f;

        void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            LowEnd = Application.isMobilePlatform || SystemInfo.processorCount <= 4;
            Battle.MaxUnits = LowEnd ? 1200 : 3200;
            InputBridge.Init();

            Cam = Camera.main;
            if (Cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                Cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            sun = RenderSettings.sun;
            if (sun == null)
            {
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
                if (sun == null) sun = new GameObject("Sun").AddComponent<Light>();
            }
            Atmosphere.SetupCamera(Cam, LowEnd);
            Rig = Cam.GetComponent<CameraRig>();
            if (Rig == null) Rig = Cam.gameObject.AddComponent<CameraRig>();
            Rig.OnTap = OnTap;
            Rig.OnHover = OnHover;
            Rig.Focus = () => Battle?.Centroid();
            hud = gameObject.AddComponent<Hud>();
            hud.Game = this;
            Rig.IsOverUI = hud.IsOverUI;
            Battle = new Battle(World);
            Battle.OnLog += e => { Chronicle.Insert(0, e); if (Chronicle.Count > 12) Chronicle.RemoveAt(Chronicle.Count - 1); };
            Director = new Director(this);
            Battle.Bolts.Cap = LowEnd ? 700 : 1600;
            StartCoroutine(Load());
        }

        IEnumerator Load()
        {
            yield return null;
            // расы из командной строки (снимки) — до загрузки, чтобы грузить только нужные модели
            string shotsArg = Arg("-shots");
            for (int tm = 0; tm < 2 && shotsArg != null; tm++)
            {
                var rk = Arg("-race" + tm);
                if (rk != null) foreach (var r in Defs.Races) if (r.Key == rk) Battle.Races[tm] = r;
            }
            yield return Lib.Load((p, text) => { LoadProgress = p; LoadText = text; }, Battle.Races);
            Battle.ClipDur = (type, name) => Lib.Inf.TryGetValue(type, out var cm) ? cm.Baked.Dur(name) : 0;
            overlays = new Overlays();
            fx = new Effects();
            squadBanners = new SquadBanners();
            weather = new Weather();
            if (Style != null) weather.Setup(Style, LowEnd);
            Sound = new Sound(Cam) { Muted = PlayerPrefs.GetInt("muted", 0) == 1 || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-mute") >= 0 };
            overlays.SetBolt(Lib);
            LoadText = "Рисуем карту…";
            yield return null;
            ArmySize = LowEnd ? 1 : 2;
            string shots = Arg("-shots");
            if (shots != null)
            {
                if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-uncapped") >= 0) Application.targetFrameRate = -1;
                if (Arg("-map") != null) MapSel = int.Parse(Arg("-map"));
                if (Arg("-size") != null) ArmySize = int.Parse(Arg("-size"));
                if (Arg("-seed") != null) Seed = int.Parse(Arg("-seed"));
            }
            NewMap(Seed != 0 ? Seed : 0);
            Battle.RandomArmies(ArmySize);
            ShowToast("Карта: " + Style.Title + ". Расставьте армии и жмите «В бой!»", 4.2f);
            if (shots != null) StartCoroutine(AutoShots(shots));
        }

        static string Arg(string name)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        /// <summary>Автосъёмка для проверки: расстановка, начало боя, разгар, итог — и выход.</summary>
        IEnumerator AutoShots(string dir)
        {
            // -director: снимки в бою делает режиссёр (что он сам выбрал), плюс промежуточные кадры
            bool directed = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-director") >= 0;
            System.IO.Directory.CreateDirectory(dir);
            string tag = MapType.ToString().ToLowerInvariant();
            yield return new WaitForSeconds(2f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, tag + "_0setup.png"));
            yield return new WaitForSeconds(1f);
            // общий план карты с юго-востока
            Rig.LookAt(0, 4, 0.55f, 36 * M.DEG, World.Field * 1.25f, true);
            yield return new WaitForSeconds(0.8f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, tag + "_0overview.png"));
            yield return new WaitForSeconds(0.6f);
            if (World.Town != null)
            { // крепость вблизи: лестница на стену, башня, донжон
                var fort = World.Town.Fort;
                var shotsAt = new System.Collections.Generic.List<(float x, float z, float yaw, float pitch, float dist, string name)>();
                if (fort.Ramps.Count > 0) { var r0 = fort.Ramps[fort.Ramps.Count / 2]; shotsAt.Add(((r0.Ax + r0.Bx) / 2, (r0.Az + r0.Bz) / 2, 0.8f, 30 * M.DEG, 22, "castle_ramp")); }
                foreach (var tw in fort.Towers) if (!tw.Keep && tw.S >= 4.5f) { shotsAt.Add((tw.X, tw.Z, 2.4f, 22 * M.DEG, 26, "castle_tower")); break; }
                foreach (var tw in fort.Towers) if (tw.Keep) { shotsAt.Add((tw.X, tw.Z, 3.6f, 26 * M.DEG, 40, "castle_keep")); break; }
                foreach (var v in shotsAt)
                {
                    Rig.LookAt(v.x, v.z, v.yaw, v.pitch, v.dist, true);
                    yield return new WaitForSeconds(0.8f);
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"{tag}_{v.name}.png"));
                    yield return new WaitForSeconds(0.4f);
                }
            }
            StartBattle();
            Speed = 2;
            Perf.Clear();
            if (directed) SetDirector(true);
            foreach (int t in directed ? new[] { 8, 12, 16, 20, 24, 28, 35, 42, 50 } : new[] { 8, 20, 35 })
            {
                while (BattleTime < t && Phase == Phase.Fight) yield return null;
                if (!directed)
                {
                    var focus = Battle.Centroid();
                    if (focus.HasValue) Rig.LookAt(focus.Value.x, focus.Value.z - 30, 0.4f * t, (28 + t) * M.DEG, 60 + t, true);
                    yield return new WaitForSeconds(0.6f);
                }
                string name = !directed || t == 20 || t == 35 ? $"{tag}_{t:00}s.png" : $"{tag}_dir{t:00}s.png";
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name));
                yield return new WaitForSeconds(directed ? 0.1f : 0.4f);
            }
            if (directed) SetDirector(false);
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-seq") >= 0)
            { // серия кадров вблизи схватки: видно, не пропадают ли бойцы в движении
                var eng = Battle.Units.Find(u => u.Alive && u.Engaged && !u.T.Mount) ?? Battle.Units.Find(u => u.Alive);
                if (eng != null)
                {
                    Speed = 1;
                    for (int k = 0; k < 24; k++)
                    {
                        Rig.LookAt(eng.Pos.x, eng.Pos.z, 1.2f, 20 * M.DEG, 11, true);
                        yield return new WaitForSeconds(0.12f);
                        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"{tag}_seq{k:00}.png"));
                        // состояние бойцов у камеры: по нему видно, в каком клипе боец пропадает
                        var sb = new System.Text.StringBuilder();
                        foreach (var u in Battle.Units)
                        {
                            if (M.Hypot(u.Pos.x - eng.Pos.x, u.Pos.z - eng.Pos.z) > 9) continue;
                            CrowdModel cm = null;
                            if (u.T.Mount) Lib.Rider.TryGetValue(u.Type, out cm); else Lib.Inf.TryGetValue(u.Type, out cm);
                            var st = u.T.Mount ? u.Ride : u.Anim;
                            sb.AppendLine($"{k:00} #{u.GetHashCode() % 10000:0000} {u.T.Key} alive {u.Alive} {st.Name} T {st.T:F2} once {st.Once} row {(cm != null ? cm.Row(st) : -1)} prev {st.PrevRow} blend {st.Blend:F2} rows {(cm != null ? cm.Baked.Rows : -1)} has {(cm != null && cm.Baked.Clips.ContainsKey(st.Name))} down {u.DownT:F1}/{u.DownAnim} fly {u.Flying} pos {u.Pos.x:F1},{u.Pos.y:F2},{u.Pos.z:F1} scale {u.Scale:F2}");
                        }
                        System.IO.File.AppendAllText(System.IO.Path.Combine(dir, tag + "_seq.txt"), sb.ToString());
                        yield return null;
                    }
                    Speed = 2;
                }
            }
            var c = Battle.Centroid();
            if (c.HasValue) Rig.LookAt(c.Value.x, c.Value.z, 2.2f, 22 * M.DEG, 22, true);
            yield return new WaitForSeconds(0.8f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, tag + "_close.png"));
            yield return new WaitForSeconds(1f);
            // совсем близко — разглядеть бойцов
            var near = Battle.Units.Find(u => u.Alive && u.Engaged) ?? Battle.Units.Find(u => u.Alive);
            if (near != null) Rig.LookAt(near.Pos.x, near.Pos.z, 1.3f, 14 * M.DEG, 7, true);
            yield return new WaitForSeconds(0.8f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, tag + "_macro.png"));
            yield return new WaitForSeconds(1f);
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-toend") >= 0)
            { // досмотреть бой до конца (быстро) и снять экран итогов
                Speed = 4;
                float wait = 0;
                while (Phase == Phase.Fight && wait < 240) { wait += Time.unscaledDeltaTime; yield return null; }
                yield return new WaitForSeconds(1.5f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, tag + "_result.png"));
                yield return new WaitForSeconds(1f);
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, tag + "_log.txt"),
                $"fps {1f / Mathf.Max(0.001f, Time.smoothDeltaTime):F0}; alive {Battle.Alive[0]}/{Battle.Alive[1]}; units {Battle.Units.Count}\n" +
                $"perf: {Perf}\n" + $"нет модели: {(NoModel.Count > 0 ? string.Join(", ", NoModel) : "—")}; чужая текстура анимаций: {CrowdModel.WrongBake}\n" +
                $"звуки: {(Sound != null ? string.Join(", ", System.Linq.Enumerable.Select(Sound.Played, kv => kv.Key + " " + kv.Value)) : "—")}\n" +
                string.Join("\n", Chronicle.ConvertAll(e => $"{e.T:F0} [{e.Team}] {e.Text}")) +
                (directed ? "\nрежиссёр:\n" + string.Join("\n", Director.History) : ""));
            Application.Quit();
        }

        public void ShowToast(string text, float sec = 2.6f) { Toast = text; ToastT = sec; }

        /// <summary>Tab / кнопка «Режиссёр»: камера сама выбирает, что показать.</summary>
        public void SetDirector(bool on) { Rig.Cinematic = on; DirectorOn = true; directorWanted = on; }

        /// <summary>Кнопка «Облёт»: простой облёт над центром боя.</summary>
        public void SetOrbit(bool on) { Rig.Cinematic = on; DirectorOn = false; directorWanted = false; }

        Effects fx;
        SquadBanners squadBanners;
        Weather weather;
        /// <summary>Звук боя (синтезирован в коде); Muted — кнопка «Звук» и клавиша M.</summary>
        public Sound Sound;

        public void ToggleSound()
        {
            if (Sound == null) return;
            Sound.Muted = !Sound.Muted;
            PlayerPrefs.SetInt("muted", Sound.Muted ? 1 : 0);
            ShowToast(Sound.Muted ? "Звук выключен" : "Звук включён");
        }

        const float SimStep = 1f / 30;
        float simAcc;
        /// <summary>Доля пути между прошлым и текущим шагом расчёта — для плавной отрисовки.</summary>
        public static float Alpha = 1;

        /// <summary>Замер кадров в бою: полный кадр, расчёт боя, подготовка толпы (остальное — видеокарта и интерфейс).</summary>
        public readonly PerfStats Perf = new PerfStats();

        public sealed class PerfStats
        {
            readonly List<float> frame = new List<float>();
            double sim, crowd;
            public void Clear() { frame.Clear(); sim = crowd = 0; }
            public void Add(float frameMs, double simMs, double crowdMs) { frame.Add(frameMs); sim += simMs; crowd += crowdMs; }
            public override string ToString()
            {
                if (frame.Count == 0) return "нет данных";
                var f = new List<float>(frame); f.Sort();
                float avg = 0; foreach (var x in f) avg += x; avg /= f.Count;
                return $"кадров {f.Count}, кадр в среднем {avg:F1} мс ({1000 / avg:F0} fps), 95% {f[(int)(f.Count * 0.95f)]:F1} мс, худший {f[f.Count - 1]:F1} мс; " +
                       $"расчёт боя {sim / f.Count:F2} мс, толпа {crowd / f.Count:F2} мс";
            }
        }

        public string MapTypeName => Defs.Maps[(int)MapType].Name;

        /// <summary>Новая карта: биом и время суток по сиду, местность — выбранная или случайная.</summary>
        public void NewMap(int seed = 0)
        {
            MapSeed = seed != 0 ? seed : Random.Range(1, 99999) | 1;
            var r = new Rng(MapSeed);
            int biome = (int)(r.Next() * Defs.Biomes.Length);
            if (biome == lastBiome) biome = (biome + 1 + (int)(r.Next() * (Defs.Biomes.Length - 1))) % Defs.Biomes.Length;
            lastBiome = biome;
            Style = Style.Make(biome, (int)(r.Next() * Defs.Times.Length));
            MapType = MapSel < 0 ? Defs.Maps[(int)(r.Next() * Defs.Maps.Length)].Type : (MapType)MapSel;
            Style.Tweak(MapType);
            Big = ArmySize == 3;
            ClearBanners();
            World.Generate(MapSeed, Style, MapType, Big, Lib.CityDefs, LowEnd);
            view.Build(World, Style, Lib, LowEnd);
            Atmosphere.Apply(Style, World.SunDir, sun, Cam);
            Rig.World = World;
            Battle.ResetToPlan();
            Phase = Phase.Setup;
            Rig.Cinematic = false;
            Rig.LookAt(0, -(World.SpawnZ + 22), 0, 36 * M.DEG, Big ? 115 : 85, true);
            MapName = $"{MapTypeName} · {Style.Title} · карта №{MapSeed}";
            weather?.Setup(Style, LowEnd);
        }

        public void NewMapButton()
        {
            NewMap();
            if (Battle.Plan.Count == 0 || MapType == MapType.City) Battle.RandomArmies(ArmySize);
            ShowToast(MapName);
        }

        /// <summary>Сменить расу армии и заново собрать войска.</summary>
        public void CycleRace(int team)
        {
            int i = System.Array.IndexOf(Defs.Races, Battle.Races[team]);
            Battle.Races[team] = Defs.Races[(i + 1) % Defs.Races.Length];
            Lib.EnsureRace(Battle.Races[team]);
            MakeArmies();
            ShowToast($"{Defs.Teams[team].Name}: {Battle.Races[team].Name}");
        }

        public void MakeArmies()
        {
            if ((ArmySize == 3) != Big) NewMap(MapSeed); // для великой сечи — большое поле
            Battle.RandomArmies(ArmySize);
            ShowToast($"Армии: {Battle.PlanCount[0]} синих против {Battle.PlanCount[1]} красных");
        }

        public void ChangeMapType(int sel)
        {
            MapSel = sel;
            NewMap();
            Battle.RandomArmies(ArmySize);
            var d = Defs.Maps[(int)MapType];
            ShowToast($"{d.Name}: {d.Note}", 3.6f);
        }

        public void StartBattle()
        {
            if (Battle.PlanCount[0] == 0 || Battle.PlanCount[1] == 0) { ShowToast("Нужны обе армии: поставьте и синих, и красных"); return; }
            if (Phase != Phase.Setup) Battle.ResetToPlan();
            ClearBanners();
            Chronicle.Clear();
            Battle.StartFight();
            Director.Reset();
            SlowMo = 1;
            Phase = Phase.Fight; Paused = false; Speed = 1; Winner = -1; BattleTime = 0; resultDelay = 0; Eraser = false;
            overlays.GhostCount = 0;
        }

        public void StopBattle()
        {
            fx?.Clear();
            ClearBanners();
            Battle.ResetToPlan();
            Phase = Phase.Setup;
            Paused = false;
            Rig.Cinematic = false;
        }

        public void OpenHelp(bool open)
        {
            HelpOpen = open;
            if (open) { pausedByHelp = Phase == Phase.Fight && !Paused; if (pausedByHelp) Paused = true; }
            else { if (pausedByHelp) Paused = false; pausedByHelp = false; }
        }

        void ClearBanners()
        {
            foreach (var b in banners.Values) Destroy(b.Go);
            banners.Clear();
        }

        void OnTap(Vector2 screen)
        {
            if (Phase != Phase.Setup || HelpOpen) return;
            var p = Rig.GroundPoint(screen);
            if (p == null) return;
            if (Eraser)
            {
                if (Battle.RemoveNear(p.Value.x, p.Value.z, 4) == 0) ShowToast("Здесь никого нет");
                return;
            }
            if (!World.InField(p.Value.x, p.Value.z, 1)) { ShowToast("Ставить отряды можно только на поле боя"); return; }
            int n = Battle.PlaceSquad(Battle.Ty(Team, Type), Team, p.Value.x, p.Value.z, Team == 0 ? 0 : M.PI);
            if (n == 0) ShowToast(Battle.Units.Count >= Battle.MaxUnits ? $"Предел — {Battle.MaxUnits} солдат" : "Здесь тесно — выберите другое место");
        }

        void OnHover(Vector2 screen)
        {
            if (overlays == null) return;
            if (Phase != Phase.Setup || Eraser || HelpOpen) { overlays.GhostCount = 0; return; }
            var p = Rig.GroundPoint(screen);
            if (p == null || !World.InField(p.Value.x, p.Value.z, 1)) { overlays.GhostCount = 0; return; }
            overlays.SetGhost(World, p, Battle.Ty(Team, Type), Team);
        }

        void Update()
        {
            InputBridge.Poll();
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            ToastT -= Time.unscaledDeltaTime;
            if (Phase == Phase.Loading) return;

            if (!HelpOpen)
            {
                if (InputBridge.Pressed(K.Space) && Phase == Phase.Fight) Paused = !Paused;
                if (InputBridge.Pressed(K.Tab)) SetDirector(!Rig.Cinematic);
                if (InputBridge.Pressed(K.M)) ToggleSound();
                if (InputBridge.Pressed(K.N) && Phase == Phase.Fight) { SetDirector(true); Director.Next(); }
                if (Phase == Phase.Setup)
                {
                    if (InputBridge.Pressed(K.D1)) { Type = 0; Eraser = false; }
                    if (InputBridge.Pressed(K.D2)) { Type = 1; Eraser = false; }
                    if (InputBridge.Pressed(K.D3)) { Type = 2; Eraser = false; }
                    if (InputBridge.Pressed(K.D4)) { Type = 3; Eraser = false; }
                    if (InputBridge.Pressed(K.T)) Team = 1 - Team;
                    if (InputBridge.Pressed(K.Enter)) StartBattle();
                }
            }

            // игрок тронул камеру и 10 с её не трогает — режиссёр забирает её обратно
            if (Phase == Phase.Fight && directorWanted && !Rig.Cinematic && Time.unscaledTime - Rig.InputT > 10) { Rig.Cinematic = true; DirectorOn = true; }

            // Расчёт боя — ровными шагами по 1/30 с (не зависит от частоты кадров);
            // между шагами солдаты рисуются плавно (Alpha — доля пути до следующего шага).
            // SlowMo — замедление от режиссёра в ударные мгновения (эффекты и анимации замедляются вместе с боем)
            float simDt = Phase == Phase.Setup || Paused ? 0 : dt * Speed * SlowMo;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            if (simDt <= 0) { Battle.Tick(0); simAcc = 0; Alpha = 1; }
            else
            {
                simAcc += simDt;
                int n = 0;
                while (simAcc >= SimStep && n < 4) { Battle.Tick(SimStep); simAcc -= SimStep; n++; }
                if (n == 4) simAcc = 0; // не успеваем — лучше замедлить бой, чем копить отставание
                Alpha = simAcc / SimStep;
            }
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            if (Phase == Phase.Fight)
            {
                BattleTime += simDt;
                if (Battle.Disengaged && Battle.Alive[0] > 0 && Battle.Alive[1] > 0)
                {
                    Winner = Battle.Alive[0] > Battle.Alive[1] ? 0 : Battle.Alive[1] > Battle.Alive[0] ? 1 : -1;
                    Battle.AddLog(Winner, Winner < 0 ? "Армии разошлись — ничья" : $"Армии разошлись: поле боя осталось за {(Winner == 0 ? "синими" : "красными")}");
                    Phase = Phase.Result;
                    Rig.Cinematic = true;
                }
                else if (Battle.Alive[0] == 0 || Battle.Alive[1] == 0)
                {
                    resultDelay += simDt;
                    if (resultDelay > 1.5f)
                    {
                        Winner = Battle.Alive[0] > 0 ? 0 : Battle.Alive[1] > 0 ? 1 : -1;
                        Phase = Phase.Result;
                        Rig.Cinematic = true;
                    }
                }
            }

            // Режиссёр читает события боя этого кадра и, если ему доверена камера, ставит её
            // до отрисовки толпы — чтобы отсечение по видимости уже видело новый ракурс
            Director.Update(dt, simDt);
            Rig.Directed = Director.Driving;
            SlowMo = Director.TimeScale;

            // Анимации и отрисовка толпы
            float animDt = Phase == Phase.Setup ? dt : Paused ? 0 : dt * Speed * SlowMo;
            bool cheer = Phase == Phase.Result;
            foreach (var u in Battle.Units) if (u.Alive) Battle.Animate(u, cheer && u.Team == Winner);
            RenderCrowd(animDt);
            squadBanners?.Draw(Battle, Alpha, Time.time, Lib.RiderHipsY, Cam);
            weather?.Draw(Conv.U(Rig.Target), new Vector3(World.Wind.x, 0, -World.Wind.z), Time.deltaTime, Time.time);
            long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
            if (Phase == Phase.Fight && !Paused) Perf.Add(Time.unscaledDeltaTime * 1000, (t1 - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency, (t2 - t1) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            overlays.DrawRings(Battle, Phase == Phase.Setup);
            overlays.DrawGhost();
            overlays.DrawBolts(Battle, Lib);
            Sound?.Update(Battle, Cam.transform.position, Conv.U(Rig.Target), SlowMo, Paused || Phase == Phase.Setup, Phase == Phase.Fight);
            if (!Paused)
                foreach (var e in Battle.Fx) // тряска камеры от самого увесистого
                    switch (e.Kind)
                    {
                        case FxKind.Explosion: Rig.Shake(0.45f, e.Pos); break;
                        case FxKind.Hero: Rig.Shake(0.5f, e.Pos); break;
                        case FxKind.Roar: Rig.Shake(0.35f, e.Pos); break;
                        case FxKind.Charge: Rig.Shake(0.06f, e.Pos); break;
                    }
            if (fx != null)
            {
                fx.Spawn(Battle.Fx);
                fx.Update(animDt);
                fx.Draw();
            }
            Battle.Fx.Clear();
            UpdateBanners();
            view.Draw();
        }

        /// <summary>Раскладывает всех видимых солдат по пачкам: вид × армия × детализация.</summary>
        /// <summary>Виды бойцов, которых нечем нарисовать (нет модели) — для проверки в логе снимков.</summary>
        public readonly HashSet<string> NoModel = new HashSet<string>();

        void RenderCrowd(float animDt)
        {
            foreach (var m in Lib.Inf.Values) m.Begin();
            foreach (var m in Lib.Rider.Values) m.Begin();
            foreach (var m in Lib.Horse) m.Begin();
            GeometryUtility.CalculateFrustumPlanes(Cam, frustum);
            var cp = Cam.transform.position;
            float lod = LowEnd ? LodDistLow : LodDistHigh, lod2 = lod * lod;
            // вспышка удара — ~0,12 с на экране при любой скорости боя (в замедлении — дольше)
            float now = Battle.Time, flash = FlashDur * Mathf.Max(1, Speed);
            bannerM.Clear();
            foreach (var u in Battle.Units)
            {
                u.Anim.Step(animDt);
                u.Ride?.Step(animDt);
                var p = Conv.U(u.RenderPos(Alpha));
                if (!GeometryUtility.TestPlanesAABB(frustum, new Bounds(p + Vector3.up, Vector3.one * 5))) continue;
                float dd = (p - cp).sqrMagnitude;
                int l = dd < lod2 ? 0 : dd < lod2 * 6.25f ? 1 : 2;
                float sink = u.Alive ? 0 : Mathf.Max(0, u.DeadT - 18) * 0.25f;
                var m = Matrix4x4.TRS(new Vector3(p.x, p.y - sink, p.z), Conv.Yaw(u.RenderYaw(Alpha)), Vector3.one * u.Scale);
                var col = UnitColor(u, now, flash);
                if (u.T.Mount)
                {
                    var hm = Lib.Horse[u.Horse];
                    int hr = hm.Row(u.Anim);
                    hm.Add(0, l, m, hr, u.Anim.PrevRow, u.Anim.Blend, col);
                    u.Anim.LastRow = hr;
                    if (!Lib.Rider.TryGetValue(u.Type, out var rm)) { NoModel.Add(u.T.Key); continue; }
                    int rr = rm.Row(u.Ride);
                    Vector3 off;
                    if (u.Alive)
                    {
                        var s = Lib.Saddle[u.Horse];
                        float bob = u.CurSpeed > 3.5f ? Mathf.Abs(Mathf.Sin(u.Phase)) * 0.12f : 0;
                        off = new Vector3(s.x, s.y - Lib.RiderHipsY + bob, -s.z);
                    }
                    else off = new Vector3(1.2f, 0, 0.3f); // всадник падает рядом с конём
                    var riderM = m * Matrix4x4.Translate(off);
                    rm.Add(u.Team, l, riderM, rr, u.Ride.PrevRow, u.Ride.Blend, col);
                    u.Ride.LastRow = rr;
                    if (u.IsLeader && u.Alive) bannerM[u] = riderM;
                }
                else
                {
                    if (!Lib.Inf.TryGetValue(u.Type, out var im)) { NoModel.Add(u.T.Key); continue; }
                    int r = im.Row(u.Anim);
                    im.Add(u.Team, l, m, r, u.Anim.PrevRow, u.Anim.Blend, col);
                    u.Anim.LastRow = r;
                }
            }
            bool shadows = true;
            foreach (var m in Lib.Inf.Values) m.End(shadows);
            foreach (var m in Lib.Rider.Values) m.End(shadows);
            foreach (var m in Lib.Horse) m.End(shadows);
        }

        const float FlashDur = 0.12f;

        /// <summary>Цвет копии солдата: белая вспышка при ударе (свечение поверх света), бегущие — бледные, выцветшие.</summary>
        static Color UnitColor(Unit u, float now, float flash)
        {
            var c = CrowdModel.White;
            float since = now - u.FlashT;
            if (since >= 0 && since < flash)
            {
                float f = 1 - since / flash;
                // тёплая вспышка, а не белая: светлые модели (скелеты) не превращаются в белые силуэты
                c = new Color(1 + 0.45f * f, 1 + 0.22f * f, 1 + 0.1f * f, 1);
            }
            var sq = u.Squad;
            if (u.Alive && sq != null && !sq.Special && sq.Order.Mode == Mode.Rout)
                c.a = 1 - 0.3f * Mathf.Clamp01((now - sq.OrderT) / 0.8f); // бегущие чуть блёкнут, но не «призраки»
            return c;
        }

        /// <summary>Знамёна едут за всадником: у главнокомандующего большое, у воевод поменьше.</summary>
        void UpdateBanners()
        {
            float wind = M.Hypot(World.Wind.x, World.Wind.z);
            var seen = new HashSet<Unit>();
            foreach (var kv in bannerM)
            {
                var u = kv.Key;
                if (!banners.TryGetValue(u, out var b))
                {
                    b = new Banner(u.Team, u.T.Special == Special.Captain ? 0.72f : 1f, Lib.RiderHipsY);
                    banners[u] = b;
                }
                seen.Add(u);
                b.Update(kv.Value, Time.time, wind);
            }
            var gone = new List<Unit>();
            foreach (var kv in banners) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var u in gone) { Destroy(banners[u].Go); banners.Remove(u); }
        }
    }
}
