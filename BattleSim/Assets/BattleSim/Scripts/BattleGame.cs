using UnityEngine;

namespace BattleSim
{
    /// <summary>
    /// Точка входа. Повесьте на пустой объект в сцене (это делает меню BattleSim → Настроить проект)
    /// и нажмите Play: мир, армии, камера и интерфейс создаются из кода.
    /// </summary>
    public class BattleGame : MonoBehaviour
    {
        public enum Phase { Setup, Fight, Result }

        [Tooltip("0 = случайная карта при каждом запуске")]
        public int Seed = 0;

        public Phase State { get; private set; } = Phase.Setup;
        public WorldGen World { get; private set; }
        public Battle Battle { get; private set; }
        public CameraRig Rig { get; private set; }
        public WorldStyle Style { get; private set; }

        public UnitType SelectedType = UnitType.Swordsman;
        public int SelectedTeam;
        public bool Eraser;
        public float Speed = 1f;
        public bool Paused;
        public int Winner = -1;
        public float BattleTime;

        public string Toast;
        public float ToastTime;

        Camera cam;
        Light sun;
        BattleHud hud;
        float resultDelay;
        int lastBiome = -1;
        readonly System.Random rnd = new System.Random();

        void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;

            InputBridge.Init();

            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            sun = RenderSettings.sun;
            if (sun == null)
            {
                var go = new GameObject("Sun");
                sun = go.AddComponent<Light>();
                sun.type = LightType.Directional;
            }

            GameMaterials.Init();
            Atmosphere.SetupCamera(cam);

            World = new WorldGen();
            Battle = new Battle(World);

            Rig = cam.GetComponent<CameraRig>();
            if (Rig == null) Rig = cam.gameObject.AddComponent<CameraRig>();
            Rig.World = World;
            Rig.OnTap = OnGroundTap;
            Rig.CinematicFocus = () => Battle.Centroid(out var c) ? c : (Vector3?)null;

            hud = gameObject.AddComponent<BattleHud>();
            hud.Game = this;
            Rig.IsOverUI = hud.IsOverUI;

            NewMap(Seed != 0 ? Seed : rnd.Next(1, int.MaxValue));
            Battle.RandomArmies();
            ShowToast("Карта: " + Style.Title + ". Расставьте армии и жмите «В БОЙ!»", 5f);
        }

        void OnDestroy()
        {
            World?.Cleanup();
            UnitArt.ClearCache();
        }

        public void NewMap() => NewMap(rnd.Next(1, int.MaxValue));

        public void NewMap(int seed)
        {
            var r = new System.Random(seed);
            int biome = r.Next(WorldStyle.BiomeNames.Length);
            if (biome == lastBiome) biome = (biome + 1 + r.Next(WorldStyle.BiomeNames.Length - 1)) % WorldStyle.BiomeNames.Length;
            lastBiome = biome;
            Style = WorldStyle.Create(biome, r.Next(WorldStyle.TimeNames.Length));

            GameMaterials.ApplyPalette(Style);
            World.Generate(seed, Style, GameMaterials.Lit, GameMaterials.Units, GameMaterials.Water);
            Atmosphere.Apply(Style, sun, cam);

            // Армии переезжают на новую карту в той же расстановке.
            Battle.ResetToPlan();
            State = Phase.Setup;
            Rig.Cinematic = false;
            Rig.LookAt(new Vector3(0f, 0f, -48f), 0f, 34f, 85f, true);
            if (Battle.Plan.Count > 0) ShowToast("Карта: " + Style.Title);
        }

        public void RandomArmies()
        {
            Battle.RandomArmies();
            ShowToast("Армии: " + Battle.PlanCount[0] + " синих против " + Battle.PlanCount[1] + " красных");
        }

        public void ClearArmies()
        {
            Battle.ClearAll();
            ShowToast("Поле очищено");
        }

        public void StartBattle()
        {
            // После расстановки солдаты и так стоят "как в плане"; пересоздаём их только для реванша.
            if (State != Phase.Setup) Battle.ResetToPlan();
            Battle.Fighting = true;
            State = Phase.Fight;
            Paused = false;
            Speed = 1f;
            Winner = -1;
            BattleTime = 0f;
            resultDelay = 0f;
            Eraser = false;
        }

        public void StopBattle()
        {
            Battle.ResetToPlan();
            State = Phase.Setup;
            Rig.Cinematic = false;
            Paused = false;
        }

        public void Rematch() => StartBattle();

        public void ShowToast(string text, float seconds = 3f)
        {
            Toast = text;
            ToastTime = seconds;
        }

        void OnGroundTap(Vector3 p)
        {
            if (State != Phase.Setup) return;
            if (Eraser)
            {
                int n = Battle.RemoveNear(p, 4f);
                if (n == 0) ShowToast("Здесь никого нет", 1.2f);
                return;
            }
            if (!WorldGen.InField(p, 1f))
            {
                ShowToast("Ставить отряды можно только на поле боя", 1.5f);
                return;
            }
            float yaw = SelectedTeam == 0 ? 0f : 180f;
            int placed = Battle.PlaceSquad(SelectedType, SelectedTeam, p, yaw);
            if (placed == 0)
                ShowToast(Battle.Units.Count >= Battle.MaxUnits ? "Достигнут лимит: " + Battle.MaxUnits + " солдат" : "Здесь тесно — выберите другое место", 1.5f);
        }

        void Update()
        {
            InputBridge.Poll();
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            ToastTime -= Time.unscaledDeltaTime;

            if (InputBridge.Pressed(K.Space) && State == Phase.Fight) Paused = !Paused;
            if (InputBridge.Pressed(K.Tab)) Rig.Cinematic = !Rig.Cinematic;

            // На расстановке Battle.Fighting == false, поэтому идёт только анимация "дыхания".
            float simDt = Paused && State != Phase.Setup ? 0f : dt * (State == Phase.Setup ? 1f : Speed);
            // При ускорении делим шаг на части, чтобы бой оставался точным.
            int steps = Mathf.Max(1, Mathf.CeilToInt(simDt / 0.034f));
            for (int i = 0; i < steps; i++) Battle.Tick(simDt / steps);

            if (State == Phase.Fight)
            {
                BattleTime += simDt;
                int a0 = Battle.AliveCount[0], a1 = Battle.AliveCount[1];
                if (a0 == 0 || a1 == 0)
                {
                    resultDelay += simDt;
                    if (resultDelay > 1.5f || simDt == 0f && resultDelay > 0f)
                    {
                        Winner = a0 > 0 ? 0 : a1 > 0 ? 1 : -1;
                        State = Phase.Result;
                    }
                }
            }
            else if (State == Phase.Result)
            {
                // Бой на заднем плане продолжает "доигрываться" (падения, стрелы).
            }
        }
    }
}
