using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleSim.Core
{
    /// <summary>Особая цель главы — третья звезда.</summary>
    public enum Goal { NoRout, FewLosses, HeroAlive, KillHero, KillMages }

    /// <summary>Когда звучит реплика: по времени боя или по событию.</summary>
    public enum Cue { Time, Contact, OwnRout, FoeRout, Duel, FoeHeroDown, HeroDown, Half, Reinforce, MagesDown, FoeHalf }

    /// <summary>Реплика в бою: кто говорит (Team — чей), что и когда.</summary>
    public sealed class Line
    {
        public Cue When;
        public float T;
        public int Team;
        public string Who, Text;
        public Line(Cue when, int team, string who, string text, float t = 0) { When = when; Team = team; Who = who; Text = text; T = t; }
    }

    /// <summary>Глава похода: местность, враг, свои полки, рассказ до и после, реплики в бою.</summary>
    public sealed class Chapter
    {
        public int No;
        public string Title, Where;
        public string[] Intro;
        public string Tip, Win, Lose;
        public MapType Map;
        public int Seed, Biome, Time;
        public bool Big;
        public RaceDef Foe;
        public string FoeCmd;
        public Trait FoeTrait;
        /// <summary>Отряды по местам 0..3 (строевые, ударные, стрелки, конница).</summary>
        public int[] Mine, Foes;
        /// <summary>Гарнизон города (для осады) вместо Foes: размер 1–3.</summary>
        public int Garrison;
        /// <summary>Богатыри по имени; null — без богатыря.</summary>
        public string Hero, FoeHero;
        public Goal Goal;
        public string GoalText;
        public int[] Reinf;
        public float ReinfT;
        public string ReinfWho;
        public Line[] Lines = new Line[0];
    }

    public static class Campaign
    {
        public const string Name = "Кощеева рать";
        public const string Cmd = "Ратибор";
        public const Trait CmdTrait = Trait.Cunning;

        static Line L(Cue c, string who, string text, float t = 0) => new Line(c, 0, who, text, t);
        static Line E(Cue c, string who, string text, float t = 0) => new Line(c, 1, who, text, t);

        public static readonly Chapter[] Chapters =
        {
            new Chapter
            {
                No = 1, Title = "Дым над засечной чертой", Map = MapType.Field, Seed = 1101, Biome = 3, Time = 0,
                Foe = Defs.Steppe, FoeCmd = "Кобяк", FoeTrait = Trait.Fierce,
                Mine = new[] { 3, 1, 2, 1 }, Foes = new[] { 2, 0, 1, 2 },
                Intro = new[]
                {
                    "Над засечной чертой поднялся дым: степняки хана Кобяка жгут сторожевые сёла и уводят полон.",
                    "Воевода Ратибор перехватил набег в поле, пока тот не ушёл за реку. Степняки быстры и бьют из луков на скаку — прижмите их пехотой, а арбалетчиков держите за спинами мечников.",
                },
                Tip = "Коснитесь своего отряда, потом земли — отряд пойдёт туда. Коснитесь вражеского отряда — ударит по нему. Полками без приказа распоряжается ваш воевода.",
                Goal = Goal.FewLosses, GoalText = "потери меньше трети",
                Win = "Набег отбит, полон освобождён. Пленный степняк проговорился: Кобяк шёл не сам — его послал кто-то с севера, «тот, кто не умирает».",
                Lose = "Степняки ушли с полоном. Соберите полки и попробуйте снова.",
                Lines = new[]
                {
                    L(Cue.Time, "Ратибор", "Держать строй! Пусть сами лезут на мечи.", 1.5f),
                    E(Cue.Time, "Кобяк", "Урусы стоят как пни. Засыпать их стрелами!", 6),
                    L(Cue.Contact, "Ратибор", "Сошлись! Арбалетчики — бить по конным!"),
                    L(Cue.FoeRout, "Ратибор", "Дрогнули! Рыцари — вдогон, не дать уйти с полоном!"),
                    L(Cue.OwnRout, "Ратибор", "Не робеть! Все к знамени!"),
                },
            },
            new Chapter
            {
                No = 2, Title = "Сторожевой лес", Map = MapType.Forest, Seed = 2203, Biome = 1, Time = 2,
                Foe = Defs.Steppe, FoeCmd = "Тугоркан", FoeTrait = Trait.Cunning,
                Mine = new[] { 4, 1, 3, 2 }, Foes = new[] { 3, 2, 3, 2 },
                Hero = "Алёша Попович", FoeHero = "Тугарин Змеевич",
                Intro = new[]
                {
                    "Орда хана Тугоркана вошла в сторожевой лес, чтобы обойти заставы. В тумане и чаще степные кони вязнут — это наш случай.",
                    "С дружиной едет Алёша Попович: он давно ищет встречи с Тугарином Змеевичем, степным богатуром.",
                },
                Tip = "Выберите богатыря и нажмите «На поединок» — он пойдёт искать вражеского. Пока богатыри бьются, войска расступаются.",
                Goal = Goal.KillHero, GoalText = "Тугарин Змеевич повержен",
                Win = "Тугоркан бежит в степь. На теле Тугарина нашли чёрный костяной оберег — такие носят слуги Нави.",
                Lose = "Орда прорвалась через лес. Попробуйте встретить её иначе.",
                Lines = new[]
                {
                    L(Cue.Time, "Ратибор", "В лесу их конница — не конница. Встретим в чаще.", 1.5f),
                    E(Cue.Time, "Тугарин Змеевич", "Где ваш Алёша? Выходи, поповский сын!", 8),
                    L(Cue.Duel, "Алёша Попович", "Ну, Тугарин, один на один — как уговаривались!"),
                    L(Cue.FoeHeroDown, "Алёша Попович", "Отлетался Змеевич!"),
                    L(Cue.HeroDown, "Ратибор", "Алёша пал… Отомстим за него!"),
                    L(Cue.FoeRout, "Ратибор", "Гоните их из леса!"),
                },
            },
            new Chapter
            {
                No = 3, Title = "Ущелья", Map = MapType.Mountains, Seed = 3307, Biome = 2, Time = 0,
                Foe = Defs.Orcs, FoeCmd = "Грызь", FoeTrait = Trait.Fierce,
                Mine = new[] { 4, 2, 3, 2 }, Foes = new[] { 3, 1, 2, 1 },
                Intro = new[]
                {
                    "Следы костяного оберега ведут за горы. В ущельях путь заступила орда Грызя: гоблины, громилы и бомбомёты.",
                    "Проходы между скалами узкие. Кто первым займёт ущелье, тот в нём и хозяин.",
                },
                Tip = "Бомбомёты бьют навесом по плотному строю. Не держите полки кучей под их огнём — пошлите на них конницу.",
                Goal = Goal.NoRout, GoalText = "ни один полк не побежал",
                Win = "Орда отброшена от ущелий. Пленные гоблины шепчутся о вожде посильнее — и о тролле, что стережёт перевал.",
                Lose = "Орда удержала ущелья. Попробуйте другой проход.",
                Lines = new[]
                {
                    L(Cue.Time, "Ратибор", "Ущелья узкие — в каждое по полку, не толпой.", 1.5f),
                    E(Cue.Time, "Грызь", "ВААГХ! Бомбы на них, бомбы!", 7),
                    L(Cue.Contact, "Ратибор", "Держать проходы!"),
                    L(Cue.FoeHalf, "Ратибор", "Орда редеет — дожимай!"),
                    L(Cue.OwnRout, "Ратибор", "Назад, к знамени! Собраться и снова в строй!"),
                },
            },
            new Chapter
            {
                No = 4, Title = "Тролль у перевала", Map = MapType.Mountains, Seed = 4409, Biome = 2, Time = 1,
                Foe = Defs.Orcs, FoeCmd = "Кривоклык", FoeTrait = Trait.Fierce,
                Mine = new[] { 4, 2, 3, 0 }, Foes = new[] { 7, 4, 2, 2 },
                Hero = "Добрыня Никитич", FoeHero = "Костолом",
                Reinf = new[] { 0, 0, 0, 3 }, ReinfT = 45, ReinfWho = "Мстислав",
                Intro = new[]
                {
                    "На перевале стоит сам вождь Кривоклык, а с ним тролль Костолом — он ломает щиты как лучину.",
                    "Рыцари Мстислава идут следом, но подойдут не сразу. Продержитесь до их прихода и сберегите Добрыню Никитича.",
                },
                Tip = "Конницы у вас пока нет — она подойдёт на 45-й секунде. Можно встать на холме и отбиваться до подмоги.",
                Goal = Goal.HeroAlive, GoalText = "Добрыня Никитич жив",
                Win = "Перевал взят. В шатре Кривоклыка нашли бересту, писанную мёртвой рукой: «Идите на Переяславль. Навь придёт следом».",
                Lose = "Перевал остался за ордой. Соберитесь и попробуйте снова.",
                Lines = new[]
                {
                    L(Cue.Time, "Ратибор", "Держимся! Мстислав уже в пути.", 1.5f),
                    E(Cue.Time, "Костолом", "ГРААХ! Где тут ваш богатырь? Сломаю!", 9),
                    L(Cue.Duel, "Добрыня Никитич", "Ну, каменная башка, поглядим, кто кого!"),
                    L(Cue.Reinforce, "Мстислав", "Рыцари, за мной! Ударим им в бок!"),
                    L(Cue.FoeHeroDown, "Добрыня Никитич", "Отломался Костолом."),
                    L(Cue.HeroDown, "Ратибор", "Добрыня пал! Не отдавать его тела!"),
                    L(Cue.Half, "Ратибор", "Тяжко… Стоять! Подмога близко!"),
                },
            },
            new Chapter
            {
                No = 5, Title = "Туман над погостом", Map = MapType.Swamp, Seed = 5501, Biome = 1, Time = 2,
                Foe = Defs.Nav, FoeCmd = "Морок", FoeTrait = Trait.Cunning,
                Mine = new[] { 5, 2, 3, 1 }, Foes = new[] { 12, 6, 3, 3 },
                Intro = new[]
                {
                    "Путь к Переяславлю лежит через старый погост на болотах. Из тумана встают мертвецы: колдуны Нави поднимают павших.",
                    "Пока живы колдуны, мёртвые не кончатся.",
                },
                Tip = "Пошлите рыцарей или арбалетчиков на колдунов: с их гибелью поднятые мертвецы рассыпаются.",
                Goal = Goal.KillMages, GoalText = "колдуны перебиты",
                Win = "Погост затих. Умирающий колдун прохрипел имя хозяина: Кощей.",
                Lose = "Туман поглотил полки. Попробуйте снова — и сперва о колдунах.",
                Lines = new[]
                {
                    L(Cue.Time, "Ратибор", "Не бояться! Мёртвый враг — тоже враг.", 1.5f),
                    E(Cue.Time, "Морок", "Ваши павшие станут нашими…", 10),
                    L(Cue.Contact, "Ратибор", "Руби их! Кто упал — пусть лежит!"),
                    L(Cue.MagesDown, "Ратибор", "Колдунов нет — мертвецы рассыпаются!"),
                    L(Cue.OwnRout, "Ратибор", "Это лишь страх! К знамени!"),
                },
            },
            new Chapter
            {
                No = 6, Title = "Чёрный волхв", Map = MapType.Forest, Seed = 6607, Biome = 2, Time = 1,
                Foe = Defs.Nav, FoeCmd = "Чернава", FoeTrait = Trait.Cautious,
                Mine = new[] { 5, 2, 3, 2 }, Foes = new[] { 7, 4, 3, 2 },
                Hero = "Пересвет", FoeHero = "Вурдалак",
                Intro = new[]
                {
                    "Чёрный волхв Чернава стережёт зимний лес под Переяславлем. С ним Вурдалак — умертвие в старом шлеме.",
                    "Мёртвые не знают страха и не бегут: их можно только изрубить. С дружиной — инок Пересвет.",
                },
                Tip = "Мертвецы наводят ужас на живых — держите полки рядом с воеводой, у знамени дух крепче.",
                Goal = Goal.KillHero, GoalText = "Вурдалак повержен",
                Win = "Лес свободен. Но над Переяславлем дым: город уже в руках орды, её привёл Кощей.",
                Lose = "Нежить удержала лес. Попробуйте снова.",
                Lines = new[]
                {
                    L(Cue.Time, "Пересвет", "С нами крестная сила. Вперёд!", 1.5f),
                    E(Cue.Time, "Чернава", "Живые… тёплые… идите к нам.", 8),
                    L(Cue.Duel, "Пересвет", "Изыди, нечисть!"),
                    L(Cue.FoeHeroDown, "Пересвет", "Лежи, где лежал, мертвец."),
                    L(Cue.MagesDown, "Ратибор", "Волхвы пали — теперь их просто рубить!"),
                    L(Cue.Half, "Ратибор", "Сомкнуться! Не дать им разорвать строй!"),
                },
            },
            new Chapter
            {
                No = 7, Title = "Осада Переяславля", Map = MapType.City, Seed = 7703, Biome = 0, Time = 0,
                Foe = Defs.Orcs, FoeCmd = "Бугай", FoeTrait = Trait.Cautious, Garrison = 1,
                Mine = new[] { 5, 2, 3, 2 },
                Hero = "Добрыня Никитич",
                Intro = new[]
                {
                    "Орда засела в Переяславле: стрелки на стенах, заслоны в воротах и на подъёмах, вожак Бугай — в детинце.",
                    "Город надо взять, пока Кощей не привёл Навь.",
                },
                Tip = "Стрелки на стенах бьют сверху. Подавите их арбалетчиками, пехоту ведите через ворота и проломы.",
                Goal = Goal.HeroAlive, GoalText = "Добрыня Никитич жив",
                Win = "Переяславль свободен. Со стены видно, как с запада ползёт туман: Кощеева рать идёт к Калинову мосту.",
                Lose = "Стены устояли. Попробуйте другой приступ.",
                Lines = new[]
                {
                    L(Cue.Time, "Ратибор", "Город наш — вернём его! На приступ!", 1.5f),
                    E(Cue.Time, "Бугай", "Лезьте, лезьте! Со стен вас удобно бить!", 8),
                    L(Cue.Contact, "Ратибор", "Вперёд, через ворота!"),
                    L(Cue.FoeHalf, "Ратибор", "Гарнизон тает — не останавливаться!"),
                    L(Cue.OwnRout, "Ратибор", "Отойти, собраться — и снова на стены!"),
                },
            },
            new Chapter
            {
                No = 8, Title = "Калинов мост", Map = MapType.Field, Seed = 8819, Biome = 1, Time = 1, Big = true,
                Foe = Defs.Nav, FoeCmd = "Кощей", FoeTrait = Trait.Cunning,
                Mine = new[] { 8, 3, 5, 3 }, Foes = new[] { 14, 7, 5, 4 },
                Hero = "Илья Муромец", FoeHero = "Костяной Змей",
                Intro = new[]
                {
                    "Последняя сеча. Кощей вывел всю свою рать: мертвецов, упырей, колдунов и костяных всадников.",
                    "Против него — вся дружина Ратибора и Илья Муромец. Кощея не убить, но без войска он лишь тень.",
                },
                Tip = "Колдуны поднимают павших — чем раньше они падут, тем меньше врагов. Илью держите против Костяного Змея.",
                Goal = Goal.KillHero, GoalText = "Костяной Змей повержен",
                Win = "Кощеева рать рассыпалась прахом, над полем взошло солнце. Русь выстояла, а о сече у Калинова моста ещё долго будут петь гусляры.",
                Lose = "Навь взяла верх. Но Русь не сдаётся — попробуйте снова.",
                Lines = new[]
                {
                    L(Cue.Time, "Ратибор", "За Русь! За всех, кто пал на этом пути!", 1.5f),
                    E(Cue.Time, "Кощей", "Смерти моей не найти. А вашу я уже нашёл.", 7),
                    L(Cue.Duel, "Илья Муромец", "Выходи, Змей! Посмотрим, крепки ли твои кости."),
                    L(Cue.FoeHeroDown, "Илья Муромец", "Одним змеем меньше."),
                    L(Cue.MagesDown, "Ратибор", "Колдуны пали — Кощей остался без мёртвых!"),
                    L(Cue.FoeHalf, "Ратибор", "Рать Кощея тает! Ещё натиск!"),
                    L(Cue.Half, "Ратибор", "Стоять! Отступать некуда!"),
                },
            },
        };

        /// <summary>
        /// Готовит бой главы на уже созданной карте: расы, полководцы, богатыри, вражеское войско.
        /// Свои полки игрок ставит сам (или PlaceArmy).
        /// </summary>
        public static void Prepare(Battle b, Chapter c)
        {
            b.Races[0] = Defs.Rus; b.Races[1] = c.Foe;
            b.UseCommanders = true;
            if (c.Garrison > 0) b.GarrisonOnly(c.Garrison);
            else { b.ClearAll(); b.PlaceArmy(1, c.Foes); }
            b.SetCommander(0, Cmd, CmdTrait);
            b.SetCommander(1, c.FoeCmd, c.FoeTrait);
            b.HeroTitle[0] = c.Hero ?? "";
            b.HeroTitle[1] = c.FoeHero ?? "";
        }

        /// <summary>Сколько всего отрядов у игрока в главе (подмога не в счёт).</summary>
        public static int Total(Chapter c) => c.Mine.Sum();

        /// <summary>Место отряда в войске расы (0..3), -1 — не строевой отряд.</summary>
        public static int SlotOf(Battle b, Squad sq)
        {
            var units = b.Races[sq.Team].Units;
            for (int i = 0; i < units.Length; i++) if (units[i] == sq.T) return i;
            return -1;
        }
    }

    /// <summary>Ход главы в бою: подмога, реплики, особая цель и звёзды.</summary>
    public sealed class StoryRun
    {
        public readonly Chapter C;
        readonly Battle B;
        readonly HashSet<Line> done = new HashSet<Line>();
        /// <summary>Сказанные реплики — интерфейс забирает их отсюда.</summary>
        public readonly Queue<Line> Said = new Queue<Line>();
        public bool OwnRouted, ReinfDone, HadMages;
        int start0, start1;

        public StoryRun(Chapter c, Battle b)
        {
            C = c; B = b;
            start0 = b.PlanCount[0]; start1 = b.PlanCount[1];
            HadMages = b.Squads.Any(q => q.Team == 1 && q.T.Raise);
        }

        bool Mages() => B.Squads.Any(q => q.Team == 1 && q.T.Raise && q.Alive > 0);

        public void Tick()
        {
            if (!B.Fighting) return;
            foreach (var q in B.Squads)
                if (q.Team == 0 && !q.Special && q.Order.Mode == Mode.Rout && !q.Feigning) OwnRouted = true;
            if (C.Reinf != null && !ReinfDone && B.Time >= C.ReinfT)
            {
                ReinfDone = true;
                int n = B.Reinforce(0, C.Reinf);
                start0 += n;
                B.AddLog(0, $"Подмога! {C.ReinfWho ?? "Свежие полки"} приводит {n} бойцов");
            }
            foreach (var l in C.Lines)
            {
                if (done.Contains(l) || !Fired(l)) continue;
                done.Add(l);
                Said.Enqueue(l);
                B.AddLog(l.Team, $"{l.Who}: «{l.Text}»");
            }
        }

        bool Fired(Line l)
        {
            var h0 = B.Heroes[0]; var h1 = B.Heroes[1];
            switch (l.When)
            {
                case Cue.Time: return B.Time >= l.T;
                case Cue.Contact: return B.LastKillT > 0;
                case Cue.OwnRout: return OwnRouted;
                case Cue.FoeRout: return B.Squads.Any(q => q.Team == 1 && !q.Special && q.Order.Mode == Mode.Rout && !q.Feigning);
                case Cue.Duel: return h0 != null && h0.Duel != null;
                case Cue.FoeHeroDown: return h1 != null && !h1.Alive;
                case Cue.HeroDown: return h0 != null && !h0.Alive;
                case Cue.Half: return B.Time > 5 && B.Alive[0] < start0 / 2;
                case Cue.FoeHalf: return B.Time > 5 && B.Alive[1] < start1 / 2;
                case Cue.Reinforce: return ReinfDone;
                case Cue.MagesDown: return HadMages && !Mages();
            }
            return false;
        }

        /// <summary>Доля погибших у игрока (подмога входит в войско).</summary>
        public float Losses => start0 > 0 ? 1 - B.Alive[0] / (float)start0 : 0;

        public bool GoalMet()
        {
            switch (C.Goal)
            {
                case Goal.NoRout: return !OwnRouted;
                case Goal.FewLosses: return Losses < 1 / 3f;
                case Goal.HeroAlive: return B.Heroes[0] != null && B.Heroes[0].Alive;
                case Goal.KillHero: return B.Heroes[1] != null && !B.Heroes[1].Alive;
                case Goal.KillMages: return HadMages && !Mages();
            }
            return false;
        }

        /// <summary>Звёзды: победа, особая цель, потери меньше половины.</summary>
        public int Stars(bool won) => !won ? 0 : 1 + (GoalMet() ? 1 : 0) + (Losses < 0.5f ? 1 : 0);
    }
}
