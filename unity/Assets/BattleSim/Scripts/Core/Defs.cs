using System.Collections.Generic;

namespace BattleSim.Core
{
    public enum Special { None, Commander, Messenger, Captain }
    public enum Mode { Advance, Move, Hold, Ambush, Charge, Rout }
    public enum OrderKind { Advance, Hold, High, Cover, Ambush, Flank, Charge, Screen, Withdraw, Rally, Rout, Reserve, Support, Fire }
    public enum MissionKind { Attack, Hold, Flank, Support, Reserve }
    public enum Trait { Cautious, Fierce, Cunning }
    public enum Stance { None, Defend, Attack, Maneuver }
    public enum Role { Solo, General, Captain }
    public enum WingKey { Left, Center, Right }
    public enum MapType { Field, Forest, Mountains, Swamp, City }

    public sealed class AnimSet
    {
        public string Idle, Run, Cheer, Aim, Reload, Melee;
        public string[] Attack = new string[0];
    }

    /// <summary>Вид войск: баланс, строй, модель и анимации.</summary>
    public sealed class UnitDef
    {
        public string Key, Name, Model, Note;
        public bool Mount, Ranged;
        public Special Special;
        public float Hp, Armor, Speed, Accel, Radius, Mass, Reach, Dmg, Cd, AtkTime, ArrowBlock, VsCav = 1, Charge = 1, Range, Spacing;
        public int Cols, Rows;
        public string[] Keep;
        public AnimSet Anim;
    }

    public sealed class TeamDef
    {
        public string Name;
        public float Hue, Sat;
        public uint Color;
    }

    public sealed class TraitDef
    {
        public string Name, Note;
        public float Think;
    }

    public sealed class MapDef
    {
        public MapType Type;
        public string Key, Name, Note;
    }

    public static class Defs
    {
        public static readonly UnitDef[] Types =
        {
            new UnitDef { Key = "sword", Name = "Мечники", Model = "Knight", Hp = 110, Armor = 0.3f, Speed = 3.3f, Accel = 12, Radius = 0.58f, Mass = 1,
                Reach = 0.7f, Dmg = 18, Cd = 1.05f, AtkTime = 0.75f, ArrowBlock = 0.55f, VsCav = 1, Charge = 1, Cols = 5, Rows = 3, Spacing = 1.45f,
                Note = "Щит гасит болты", Keep = new[] { "1H_Sword", "Badge_Shield", "Knight_Helmet", "Knight_Cape" },
                Anim = new AnimSet { Idle = "Idle", Run = "Running_A", Attack = new[] { "1H_Melee_Attack_Chop", "1H_Melee_Attack_Slice_Diagonal" }, Cheer = "Cheer" } },
            new UnitDef { Key = "barb", Name = "Варвары", Model = "Barbarian", Hp = 130, Armor = 0.12f, Speed = 3.6f, Accel = 12, Radius = 0.6f, Mass = 1.1f,
                Reach = 1.0f, Dmg = 30, Cd = 1.5f, AtkTime = 0.85f, ArrowBlock = 0, VsCav = 2.2f, Charge = 1, Cols = 4, Rows = 3, Spacing = 1.5f,
                Note = "Топор рубит конницу", Keep = new[] { "2H_Axe", "Barbarian_Hat", "Barbarian_Cape" },
                Anim = new AnimSet { Idle = "2H_Melee_Idle", Run = "Running_A", Attack = new[] { "2H_Melee_Attack_Chop", "2H_Melee_Attack_Slice" }, Cheer = "Cheer" } },
            new UnitDef { Key = "xbow", Name = "Арбалетчики", Model = "Rogue_Hooded", Hp = 65, Armor = 0.05f, Speed = 3.4f, Accel = 12, Radius = 0.52f, Mass = 0.9f,
                Reach = 0.5f, Dmg = 26, Cd = 2.3f, AtkTime = 0.7f, Ranged = true, Range = 36, ArrowBlock = 0, VsCav = 1, Charge = 1, Cols = 5, Rows = 2, Spacing = 1.45f,
                Note = "Стреляют на 36 м", Keep = new[] { "2H_Crossbow", "Rogue_Cape" },
                Anim = new AnimSet { Idle = "2H_Ranged_Aiming", Run = "Running_A", Attack = new[] { "2H_Ranged_Shoot" }, Aim = "2H_Ranged_Aiming", Reload = "2H_Ranged_Reload", Melee = "1H_Melee_Attack_Stab", Cheer = "Cheer" } },
            new UnitDef { Key = "knight", Name = "Рыцари", Model = "Knight", Mount = true, Hp = 220, Armor = 0.38f, Speed = 7.5f, Accel = 5, Radius = 1.1f, Mass = 3.5f,
                Reach = 0.9f, Dmg = 24, Cd = 1.25f, AtkTime = 0.7f, ArrowBlock = 0.25f, VsCav = 1, Charge = 2.3f, Cols = 3, Rows = 2, Spacing = 2.7f,
                Note = "Натиск ×2,3", Keep = new[] { "1H_Sword", "Round_Shield", "Knight_Helmet", "Knight_Cape" },
                Anim = new AnimSet { Attack = new[] { "1H_Melee_Attack_Slice_Horizontal", "1H_Melee_Attack_Slice_Diagonal" } } },
        };

        /// <summary>Полководец: сидит на белом коне со знаменем, думает и рассылает приказы.</summary>
        public static readonly UnitDef Commander = new UnitDef { Key = "cmd", Name = "Полководец", Model = "Knight", Mount = true, Special = Special.Commander, Hp = 320, Armor = 0.45f,
            Speed = 6.5f, Accel = 6, Radius = 1.1f, Mass = 3.5f, Reach = 0.9f, Dmg = 22, Cd = 1.2f, AtkTime = 0.7f, ArrowBlock = 0.4f, VsCav = 1, Charge = 1,
            Keep = new[] { "2H_Sword", "Knight_Helmet", "Knight_Cape" }, Anim = new AnimSet { Attack = new[] { "1H_Melee_Attack_Slice_Horizontal" } } };

        /// <summary>Гонец: везёт приказ от полководца к отряду. Его можно перехватить.</summary>
        public static readonly UnitDef Messenger = new UnitDef { Key = "msg", Name = "Гонец", Model = "Rogue_Hooded", Mount = true, Special = Special.Messenger, Hp = 45, Armor = 0,
            Speed = 11, Accel = 10, Radius = 0.9f, Mass = 2.5f, Reach = 0.5f, Dmg = 0, Cd = 99, AtkTime = 0.5f, ArrowBlock = 0, VsCav = 1, Charge = 1,
            Keep = new[] { "Rogue_Cape" }, Anim = new AnimSet() };

        /// <summary>Воевода: командует крылом армии и сам раздаёт приказы отрядам.</summary>
        public static readonly UnitDef Captain = new UnitDef { Key = "cap", Name = "Воевода", Model = "Knight", Mount = true, Special = Special.Captain, Hp = 240, Armor = 0.4f,
            Speed = 6.8f, Accel = 6, Radius = 1.05f, Mass = 3.5f, Reach = 0.9f, Dmg = 22, Cd = 1.2f, AtkTime = 0.7f, ArrowBlock = 0.35f, VsCav = 1, Charge = 1,
            Keep = new[] { "1H_Sword", "Badge_Shield", "Knight_Helmet", "Knight_Cape" }, Anim = new AnimSet { Attack = new[] { "1H_Melee_Attack_Slice_Horizontal" } } };

        public static readonly UnitDef[] All = { Types[0], Types[1], Types[2], Types[3], Commander, Messenger, Captain };
        public const int TCmd = 4, TMsg = 5, TCap = 6;

        public static readonly string[] AllAttachments =
        {
            "1H_Sword_Offhand", "Badge_Shield", "Rectangle_Shield", "Round_Shield", "Spike_Shield", "1H_Sword", "2H_Sword",
            "Knight_Helmet", "Knight_Cape", "1H_Axe_Offhand", "Barbarian_Round_Shield", "1H_Axe", "2H_Axe", "Mug", "Barbarian_Hat", "Barbarian_Cape",
            "Knife_Offhand", "1H_Crossbow", "2H_Crossbow", "Knife", "Throwable", "Rogue_Cape",
        };

        public static readonly TeamDef[] Teams =
        {
            new TeamDef { Name = "Синие", Hue = 214, Sat = 0.62f, Color = 0x3f7fe6 },
            new TeamDef { Name = "Красные", Hue = 2, Sat = 0.66f, Color = 0xd9483b },
        };

        public static readonly MapDef[] Maps =
        {
            new MapDef { Type = MapType.Field, Key = "field", Name = "Поле", Note = "холмы, овраг и каменные ограды" },
            new MapDef { Type = MapType.Forest, Key = "forest", Name = "Лес", Note = "рощи прячут отряды и глушат болты, кони вязнут в чаще" },
            new MapDef { Type = MapType.Mountains, Key = "mountains", Name = "Горы", Note = "террасы и обрывы, серпантины, ущелье с рекой и мосты" },
            new MapDef { Type = MapType.Swamp, Key = "swamp", Name = "Болото", Note = "топи, островки и камыш — глубокую воду не перейти" },
            new MapDef { Type = MapType.City, Key = "city", Name = "Город", Note = "крепостные стены с башнями и воротами, детинец на холме, тесные кварталы" },
        };

        public static readonly Dictionary<Trait, TraitDef> Traits = new Dictionary<Trait, TraitDef>
        {
            [Trait.Cautious] = new TraitDef { Name = "осторожный", Think = 3.6f, Note = "бережёт людей, держит высоты и укрытия" },
            [Trait.Fierce] = new TraitDef { Name = "яростный", Think = 2.6f, Note = "рвётся в бой и давит числом" },
            [Trait.Cunning] = new TraitDef { Name = "хитрый", Think = 3.0f, Note = "устраивает засады, обходы и налёты" },
        };
        public static readonly Trait[] TraitKeys = { Trait.Cautious, Trait.Fierce, Trait.Cunning };

        public static readonly string[][] CmdNames =
        {
            new[] { "Ратибор", "Ярополк", "Мстислав", "Добрыня", "Святослав", "Вышата", "Путята", "Любомир" },
            new[] { "Всеслав", "Изяслав", "Горислав", "Судислав", "Братислав", "Ставр", "Радим", "Твердислав" },
        };

        public static string WingName(WingKey k) => k == WingKey.Left ? "левое крыло" : k == WingKey.Center ? "центр" : "правое крыло";

        public static string MissionText(MissionKind k)
        {
            switch (k)
            {
                case MissionKind.Attack: return "наступать";
                case MissionKind.Hold: return "держать позицию";
                case MissionKind.Flank: return "обойти врага с фланга";
                case MissionKind.Support: return "идти на помощь соседям";
                default: return "стоять в резерве";
            }
        }

        public static string OrderText(OrderKind k)
        {
            switch (k)
            {
                case OrderKind.Advance: return "Вперёд";
                case OrderKind.Hold: return "Держать строй";
                case OrderKind.High: return "Занять высоту";
                case OrderKind.Cover: return "В укрытие";
                case OrderKind.Ambush: return "Засада";
                case OrderKind.Flank: return "Обход с фланга";
                case OrderKind.Charge: return "Натиск";
                case OrderKind.Screen: return "Прикрыть стрелков";
                case OrderKind.Withdraw: return "Отход";
                case OrderKind.Rally: return "Сбор у знамени";
                case OrderKind.Rout: return "Бегут!";
                case OrderKind.Reserve: return "В резерв";
                case OrderKind.Support: return "На помощь";
                default: return "На рубеж стрельбы";
            }
        }

        public static string StanceText(Stance s)
        {
            switch (s)
            {
                case Stance.Defend: return "стоим на высотах — пусть враг лезет вверх под болтами";
                case Stance.Attack: return "все вперёд, давим числом!";
                case Stance.Maneuver: return "связать их фронт, а самим обойти фланги и ударить из засады";
                default: return "";
            }
        }

        public static readonly string[] SquadName = { "мечники", "варвары", "арбалетчики", "рыцари" };

        public static readonly string[] Biomes = { "Лето", "Осень", "Зима", "Степь" };
        public static readonly string[] Times = { "день", "закат", "туманное утро" };

        public static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

        /// <summary>Римские цифры для номеров отрядов: I, II, … XLIX.</summary>
        public static string Roman(int n)
        {
            var sb = new System.Text.StringBuilder();
            int[] v = { 40, 10, 9, 5, 4, 1 };
            string[] r = { "XL", "X", "IX", "V", "IV", "I" };
            for (int i = 0; i < v.Length; i++) while (n >= v[i]) { sb.Append(r[i]); n -= v[i]; }
            return sb.ToString();
        }
    }

    /// <summary>Приказ отряду. Координаты NaN — «не задано».</summary>
    public sealed class Order
    {
        public OrderKind Kind;
        public Mode Mode;
        public float X = float.NaN, Z = float.NaN, Face = float.NaN, Leash = float.NaN;
        public Order Then;
        public Squad Target;
        public string Claim, Why;

        public Order(OrderKind kind, Mode mode) { Kind = kind; Mode = mode; }
        public bool HasPos => !float.IsNaN(X);
        public bool HasFace => !float.IsNaN(Face);
        public bool HasLeash => !float.IsNaN(Leash);
        public Order At(float x, float z) { X = x; Z = z; return this; }
        public Order At(V2 p) { X = p.x; Z = p.z; return this; }
        public Order Clone() => (Order)MemberwiseClone();
        public V2 Pos => new V2(X, Z);
    }

    /// <summary>Задача крылу от главнокомандующего.</summary>
    public sealed class Mission
    {
        public MissionKind Kind;
        public float T, X = float.NaN, Z = float.NaN;
        public Wing Wing;
        public Mission(MissionKind k, float t = 0f) { Kind = k; T = t; }
        public bool HasPos => !float.IsNaN(X);
    }

    /// <summary>Крыло армии: отряды, воевода и текущая задача.</summary>
    public sealed class Wing
    {
        public WingKey Key;
        public List<Squad> Squads = new List<Squad>();
        public Commander Captain;
        public Mission Mission = new Mission(MissionKind.Attack);
        public Mission PendingMission;
    }

    /// <summary>Что везёт гонец: приказ отряду или задачу воеводе.</summary>
    public sealed class Carry
    {
        public Squad Squad;
        public Order Order;
        public Commander Cmd;
        public bool Delivered;
        public Commander Captain;
        public Mission Mission;
        public Wing Wing;
    }
}
