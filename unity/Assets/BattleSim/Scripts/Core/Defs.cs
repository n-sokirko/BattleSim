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
        // --- расы
        /// <summary>Номер в Defs.All (тип бойца), место в армии (0 строевые, 1 ударные, 2 стрелки, 3 конница) и раса.</summary>
        public int Id, Slot;
        public RaceDef Race;
        /// <summary>Как называть отряд в летописи («мечники»).</summary>
        public string SquadName;
        /// <summary>Рост модели (×) и разброс роста; бесстрашие; при каком духе бегут.</summary>
        public float Scale = 1, ScaleJit = 0.1f, RoutAt = 18;
        public bool Fearless;
        /// <summary>Каждый SpinEvery-й удар — с разворота по всем вокруг в радиусе SpinR (громилы).</summary>
        public int SpinEvery;
        public float SpinR;
        /// <summary>Снаряд рвётся: радиус, урон, отброс (бомбы).</summary>
        public float AoeR, AoeDmg, AoeKnock;
        /// <summary>Во сколько раз сильнее отбрасывает удар (громилы).</summary>
        public float KnockMul = 1;
        /// <summary>Смыкает щиты под обстрелом и перед натиском конницы (мечники Руси).</summary>
        public bool ShieldWall;
        /// <summary>Колдун: поднимает павших обеих армий; удар — порча на расстоянии (без снаряда, щит не спасает).</summary>
        public bool Raise, Magic;
        /// <summary>Доля нанесённого урона, что возвращается здоровьем (упыри).</summary>
        public float Leech;
        /// <summary>Конный стрелок: бьёт на скаку и держит дистанцию — «карусель».</summary>
        public bool Skirmish;
        /// <summary>Наводит ужас: натиск и близость сильнее бьют по духу врага.</summary>
        public bool Terror;
    }

    /// <summary>
    /// Раса: четыре вида бойцов по местам в армии, вожди, боевой клич и облик (цвет кожи и стали в палитре KayKit).
    /// </summary>
    public sealed class RaceDef
    {
        public string Key, Name, Cry;
        public UnitDef[] Units = new UnitDef[4];
        public UnitDef Cmd, Msg, Cap;
        /// <summary>Кожа и сталь: оттенок (0..360, &lt;0 — не менять) и насыщенность; конь — множитель цвета.</summary>
        public float SkinHue = -1, SkinSat, MetalHue = -1, MetalSat;
        public float HorseTint = 1;
        /// <summary>Ярость орды: копится в бою, на пике армия ревёт (быстрее, сильнее, не бежит).</summary>
        public bool Rage;
        /// <summary>Нежить: рядом с ней у врага тает дух (ужас).</summary>
        public bool Undead;
        /// <summary>Конница умеет ложно отступать: побежит, втянет погоню — и развернётся.</summary>
        public bool Feign;
        /// <summary>Имена вождей для летописи.</summary>
        public string[] Names;
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
                ShieldWall = true, Note = "Смыкают стену щитов", Keep = new[] { "1H_Sword", "Badge_Shield", "Knight_Helmet", "Knight_Cape" },
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

        // ---------------------------------------------------------------- Орда: гоблины, громилы, бомбы, чёрные всадники
        static readonly UnitDef OrcGoblin = new UnitDef { Key = "goblin", Name = "Гоблины", SquadName = "гоблины", Model = "Rogue_Hooded", Hp = 56, Armor = 0.05f, Speed = 4.3f, Accel = 14, Radius = 0.4f, Mass = 0.55f,
            Reach = 0.5f, Dmg = 10, Cd = 0.65f, AtkTime = 0.45f, ArrowBlock = 0, VsCav = 1, Charge = 1, Cols = 7, Rows = 4, Spacing = 1.0f, Scale = 0.68f, ScaleJit = 0.12f, RoutAt = 26,
            Note = "Рой: много, быстро, трусливо", Keep = new[] { "Knife", "Knife_Offhand" },
            Anim = new AnimSet { Idle = "Idle", Run = "Running_B", Attack = new[] { "Dualwield_Melee_Attack_Stab", "Dualwield_Melee_Attack_Slice" }, Cheer = "Cheer" } };
        static readonly UnitDef OrcBrute = new UnitDef { Key = "brute", Name = "Громилы", SquadName = "громилы", Model = "Barbarian", Hp = 215, Armor = 0.15f, Speed = 3.2f, Accel = 10, Radius = 0.78f, Mass = 2.0f,
            Reach = 1.2f, Dmg = 32, Cd = 1.7f, AtkTime = 0.9f, ArrowBlock = 0, VsCav = 2.0f, Charge = 1, Cols = 4, Rows = 2, Spacing = 1.9f, Scale = 1.3f, ScaleJit = 0.08f, KnockMul = 2.5f,
            SpinEvery = 3, SpinR = 2.2f, Note = "Каждый третий удар — вихрем", Keep = new[] { "2H_Axe" },
            Anim = new AnimSet { Idle = "2H_Melee_Idle", Run = "Running_A", Attack = new[] { "2H_Melee_Attack_Chop", "2H_Melee_Attack_Slice" }, Melee = "2H_Melee_Attack_Spin", Cheer = "Cheer" } };
        static readonly UnitDef OrcBomber = new UnitDef { Key = "bomber", Name = "Бомбомёты", SquadName = "бомбомёты", Model = "Rogue_Hooded", Hp = 45, Armor = 0.05f, Speed = 3.8f, Accel = 12, Radius = 0.42f, Mass = 0.6f,
            Reach = 0.5f, Dmg = 6, Cd = 3.2f, AtkTime = 0.7f, Ranged = true, Range = 22, ArrowBlock = 0, VsCav = 1, Charge = 1, Cols = 7, Rows = 2, Spacing = 1.2f, Scale = 0.72f, ScaleJit = 0.1f, RoutAt = 26,
            AoeR = 2.2f, AoeDmg = 24, AoeKnock = 4.5f, Note = "Бомба рвёт кучу, тела летят", Keep = new[] { "Throwable" },
            Anim = new AnimSet { Idle = "Idle", Run = "Running_B", Attack = new[] { "Throw" }, Aim = "Idle", Reload = "Idle", Melee = "1H_Melee_Attack_Stab", Cheer = "Cheer" } };
        static readonly UnitDef OrcRider = new UnitDef { Key = "orcrider", Name = "Чёрные всадники", SquadName = "чёрные всадники", Model = "Barbarian", Mount = true, Hp = 240, Armor = 0.22f, Speed = 7.2f, Accel = 5, Radius = 1.1f, Mass = 3.6f,
            Reach = 0.9f, Dmg = 26, Cd = 1.3f, AtkTime = 0.7f, ArrowBlock = 0.2f, VsCav = 1, Charge = 2.0f, Cols = 3, Rows = 2, Spacing = 2.7f, Scale = 1.1f, ScaleJit = 0.05f,
            Note = "Натиск ×2", Keep = new[] { "1H_Axe", "Barbarian_Round_Shield" },
            Anim = new AnimSet { Attack = new[] { "1H_Melee_Attack_Chop", "1H_Melee_Attack_Slice_Diagonal" } } };
        static readonly UnitDef OrcWarlord = new UnitDef { Key = "warlord", Name = "Вождь", Model = "Barbarian", Mount = true, Special = Special.Commander, Hp = 340, Armor = 0.4f,
            Speed = 6.5f, Accel = 6, Radius = 1.1f, Mass = 3.6f, Reach = 1.0f, Dmg = 28, Cd = 1.2f, AtkTime = 0.7f, ArrowBlock = 0.3f, VsCav = 1, Charge = 1, Scale = 1.2f, ScaleJit = 0,
            Keep = new[] { "2H_Axe", "Barbarian_Hat" }, Anim = new AnimSet { Attack = new[] { "1H_Melee_Attack_Chop" } } };
        static readonly UnitDef OrcRunner = new UnitDef { Key = "runner", Name = "Посыльный", Model = "Rogue_Hooded", Mount = true, Special = Special.Messenger, Hp = 40, Armor = 0,
            Speed = 11.5f, Accel = 10, Radius = 0.9f, Mass = 2.5f, Reach = 0.5f, Dmg = 0, Cd = 99, AtkTime = 0.5f, ArrowBlock = 0, VsCav = 1, Charge = 1, Scale = 0.75f, ScaleJit = 0,
            Keep = new[] { "Knife" }, Anim = new AnimSet() };
        static readonly UnitDef OrcChief = new UnitDef { Key = "chief", Name = "Вожак", Model = "Barbarian", Mount = true, Special = Special.Captain, Hp = 260, Armor = 0.35f,
            Speed = 6.8f, Accel = 6, Radius = 1.05f, Mass = 3.6f, Reach = 0.9f, Dmg = 24, Cd = 1.2f, AtkTime = 0.7f, ArrowBlock = 0.3f, VsCav = 1, Charge = 1, Scale = 1.12f, ScaleJit = 0,
            Keep = new[] { "1H_Axe", "Barbarian_Round_Shield" }, Anim = new AnimSet { Attack = new[] { "1H_Melee_Attack_Chop" } } };

        // ---------------------------------------------------------------- Навь: мертвецы, упыри, колдуны, костяные всадники
        static readonly UnitDef NavDead = new UnitDef { Key = "dead", Name = "Мертвецы", SquadName = "мертвецы", Model = "Knight", Hp = 75, Armor = 0.15f, Speed = 2.7f, Accel = 9, Radius = 0.56f, Mass = 1,
            Reach = 0.7f, Dmg = 13, Cd = 1.2f, AtkTime = 0.8f, ArrowBlock = 0.45f, VsCav = 1, Charge = 1, Cols = 5, Rows = 3, Spacing = 1.45f, Fearless = true, ScaleJit = 0.06f,
            Note = "Не бегут, встают снова", Keep = new[] { "1H_Sword", "Round_Shield" },
            Anim = new AnimSet { Idle = "Idle", Run = "Walking_A", Attack = new[] { "1H_Melee_Attack_Chop", "1H_Melee_Attack_Slice_Diagonal" }, Cheer = "Cheer" } };
        static readonly UnitDef NavGhoul = new UnitDef { Key = "ghoul", Name = "Упыри", SquadName = "упыри", Model = "Barbarian", Hp = 95, Armor = 0.05f, Speed = 4.4f, Accel = 14, Radius = 0.6f, Mass = 1.1f,
            Reach = 0.7f, Dmg = 15, Cd = 0.75f, AtkTime = 0.5f, ArrowBlock = 0, VsCav = 1.4f, Charge = 1, Cols = 5, Rows = 3, Spacing = 1.4f, Fearless = true, Leech = 0.3f, Scale = 1.05f,
            Note = "Пьют жизнь врага", Keep = new[] { "1H_Axe", "1H_Axe_Offhand" },
            Anim = new AnimSet { Idle = "Idle", Run = "Running_B", Attack = new[] { "Dualwield_Melee_Attack_Chop", "Dualwield_Melee_Attack_Slice" }, Cheer = "Cheer" } };
        static readonly UnitDef NavNecro = new UnitDef { Key = "necro", Name = "Колдуны", SquadName = "колдуны", Model = "Rogue_Hooded", Hp = 60, Armor = 0.05f, Speed = 3.0f, Accel = 10, Radius = 0.5f, Mass = 0.9f,
            Reach = 0.5f, Dmg = 11, Cd = 2.6f, AtkTime = 0.8f, Ranged = true, Magic = true, Raise = true, Range = 24, ArrowBlock = 0, VsCav = 1, Charge = 1, Cols = 4, Rows = 2, Spacing = 1.6f, RoutAt = 22,
            Note = "Поднимают павших", Keep = new[] { "Rogue_Cape" },
            Anim = new AnimSet { Idle = "Spellcasting", Run = "Walking_A", Attack = new[] { "Spellcast_Shoot" }, Aim = "Spellcasting", Reload = "Spellcasting", Melee = "1H_Melee_Attack_Stab", Cheer = "Spellcast_Raise" } };
        static readonly UnitDef NavRider = new UnitDef { Key = "boneknight", Name = "Костяные всадники", SquadName = "костяные всадники", Model = "Knight", Mount = true, Hp = 190, Armor = 0.28f, Speed = 7.0f, Accel = 5, Radius = 1.1f, Mass = 3.5f,
            Reach = 0.9f, Dmg = 22, Cd = 1.3f, AtkTime = 0.7f, ArrowBlock = 0.2f, VsCav = 1, Charge = 2.0f, Cols = 3, Rows = 2, Spacing = 2.7f, Fearless = true, Terror = true,
            Note = "Натиск наводит ужас", Keep = new[] { "2H_Sword", "Knight_Helmet" },
            Anim = new AnimSet { Attack = new[] { "1H_Melee_Attack_Slice_Horizontal", "1H_Melee_Attack_Slice_Diagonal" } } };
        static readonly UnitDef NavKing = new UnitDef { Key = "deadking", Name = "Царь мёртвых", Model = "Knight", Mount = true, Special = Special.Commander, Hp = 330, Armor = 0.45f,
            Speed = 6.2f, Accel = 6, Radius = 1.1f, Mass = 3.5f, Reach = 0.9f, Dmg = 24, Cd = 1.2f, AtkTime = 0.7f, ArrowBlock = 0.4f, VsCav = 1, Charge = 1, Terror = true,
            Keep = new[] { "2H_Sword", "Knight_Helmet", "Knight_Cape" }, Anim = new AnimSet { Attack = new[] { "1H_Melee_Attack_Slice_Horizontal" } } };
        static readonly UnitDef NavHerald = new UnitDef { Key = "herald", Name = "Вестник", Model = "Rogue_Hooded", Mount = true, Special = Special.Messenger, Hp = 45, Armor = 0,
            Speed = 11, Accel = 10, Radius = 0.9f, Mass = 2.5f, Reach = 0.5f, Dmg = 0, Cd = 99, AtkTime = 0.5f, ArrowBlock = 0, VsCav = 1, Charge = 1,
            Keep = new[] { "Rogue_Cape" }, Anim = new AnimSet() };
        static readonly UnitDef NavLord = new UnitDef { Key = "bonelord", Name = "Костяной воевода", Model = "Knight", Mount = true, Special = Special.Captain, Hp = 240, Armor = 0.4f,
            Speed = 6.5f, Accel = 6, Radius = 1.05f, Mass = 3.5f, Reach = 0.9f, Dmg = 22, Cd = 1.2f, AtkTime = 0.7f, ArrowBlock = 0.35f, VsCav = 1, Charge = 1, Terror = true,
            Keep = new[] { "1H_Sword", "Round_Shield", "Knight_Helmet" }, Anim = new AnimSet { Attack = new[] { "1H_Melee_Attack_Slice_Horizontal" } } };

        // ---------------------------------------------------------------- Степь: нукеры, батыры, лучники, конные лучники
        static readonly UnitDef StNuker = new UnitDef { Key = "nuker", Name = "Нукеры", SquadName = "нукеры", Model = "Barbarian", Hp = 110, Armor = 0.15f, Speed = 3.8f, Accel = 13, Radius = 0.58f, Mass = 1,
            Reach = 0.7f, Dmg = 18, Cd = 1.0f, AtkTime = 0.7f, ArrowBlock = 0.35f, VsCav = 1, Charge = 1, Cols = 5, Rows = 3, Spacing = 1.45f,
            Note = "Лёгкие и быстрые", Keep = new[] { "1H_Axe", "Barbarian_Round_Shield", "Barbarian_Hat" },
            Anim = new AnimSet { Idle = "Idle", Run = "Running_A", Attack = new[] { "1H_Melee_Attack_Chop", "1H_Melee_Attack_Slice_Diagonal" }, Cheer = "Cheer" } };
        static readonly UnitDef StBatyr = new UnitDef { Key = "batyr", Name = "Батыры", SquadName = "батыры", Model = "Barbarian", Mount = true, Hp = 250, Armor = 0.3f, Speed = 7.8f, Accel = 5, Radius = 1.1f, Mass = 3.5f,
            Reach = 0.9f, Dmg = 24, Cd = 1.25f, AtkTime = 0.7f, ArrowBlock = 0.25f, VsCav = 1, Charge = 2.2f, Cols = 4, Rows = 2, Spacing = 2.7f,
            Note = "Натиск ×2,2, ложное бегство", Keep = new[] { "1H_Axe", "Barbarian_Round_Shield", "Barbarian_Hat" },
            Anim = new AnimSet { Attack = new[] { "1H_Melee_Attack_Chop", "1H_Melee_Attack_Slice_Diagonal" } } };
        static readonly UnitDef StArcher = new UnitDef { Key = "archer", Name = "Лучники", SquadName = "лучники", Model = "Rogue_Hooded", Hp = 58, Armor = 0.05f, Speed = 3.7f, Accel = 12, Radius = 0.5f, Mass = 0.85f,
            Reach = 0.5f, Dmg = 18, Cd = 1.6f, AtkTime = 0.6f, Ranged = true, Range = 34, ArrowBlock = 0, VsCav = 1, Charge = 1, Cols = 5, Rows = 2, Spacing = 1.45f,
            Note = "Стреляют часто", Keep = new[] { "2H_Crossbow", "Rogue_Cape" },
            Anim = new AnimSet { Idle = "2H_Ranged_Aiming", Run = "Running_A", Attack = new[] { "2H_Ranged_Shoot" }, Aim = "2H_Ranged_Aiming", Reload = "2H_Ranged_Reload", Melee = "1H_Melee_Attack_Stab", Cheer = "Cheer" } };
        static readonly UnitDef StHorseArcher = new UnitDef { Key = "horsearcher", Name = "Конные лучники", SquadName = "конные лучники", Model = "Rogue_Hooded", Mount = true, Ranged = true, Skirmish = true,
            Hp = 160, Armor = 0.1f, Speed = 8.2f, Accel = 6, Radius = 1.1f, Mass = 3.2f, Reach = 0.6f, Dmg = 16, Cd = 1.6f, AtkTime = 0.6f, Range = 30, ArrowBlock = 0.1f, VsCav = 1, Charge = 1,
            Cols = 4, Rows = 2, Spacing = 2.8f, Note = "Стреляют на скаку, не даются в руки", Keep = new[] { "2H_Crossbow", "Rogue_Cape" },
            Anim = new AnimSet { Attack = new[] { "2H_Ranged_Shoot" } } };
        static readonly UnitDef StKhan = new UnitDef { Key = "khan", Name = "Хан", Model = "Barbarian", Mount = true, Special = Special.Commander, Hp = 320, Armor = 0.4f,
            Speed = 7.0f, Accel = 6, Radius = 1.1f, Mass = 3.5f, Reach = 1.0f, Dmg = 24, Cd = 1.2f, AtkTime = 0.7f, ArrowBlock = 0.3f, VsCav = 1, Charge = 1, Scale = 1.08f, ScaleJit = 0,
            Keep = new[] { "2H_Axe", "Barbarian_Hat", "Barbarian_Cape" }, Anim = new AnimSet { Attack = new[] { "1H_Melee_Attack_Chop" } } };
        static readonly UnitDef StMessenger = new UnitDef { Key = "chapar", Name = "Гонец", Model = "Rogue_Hooded", Mount = true, Special = Special.Messenger, Hp = 45, Armor = 0,
            Speed = 12, Accel = 10, Radius = 0.9f, Mass = 2.5f, Reach = 0.5f, Dmg = 0, Cd = 99, AtkTime = 0.5f, ArrowBlock = 0, VsCav = 1, Charge = 1,
            Keep = new[] { "Rogue_Cape" }, Anim = new AnimSet() };
        static readonly UnitDef StMurza = new UnitDef { Key = "murza", Name = "Мурза", Model = "Barbarian", Mount = true, Special = Special.Captain, Hp = 240, Armor = 0.35f,
            Speed = 7.2f, Accel = 6, Radius = 1.05f, Mass = 3.5f, Reach = 0.9f, Dmg = 22, Cd = 1.2f, AtkTime = 0.7f, ArrowBlock = 0.3f, VsCav = 1, Charge = 1, ScaleJit = 0,
            Keep = new[] { "1H_Axe", "Barbarian_Round_Shield", "Barbarian_Hat" }, Anim = new AnimSet { Attack = new[] { "1H_Melee_Attack_Chop" } } };

        public static readonly RaceDef Rus = new RaceDef
        {
            Key = "rus", Name = "Русь", Cry = "За Русь!", Units = { [0] = Types[0], [1] = Types[1], [2] = Types[2], [3] = Types[3] },
            Cmd = Commander, Msg = Messenger, Cap = Captain,
        };
        public static readonly RaceDef Orcs = new RaceDef
        {
            Key = "orcs", Name = "Орда", Cry = "ВААГХ!", Units = { [0] = OrcGoblin, [1] = OrcBrute, [2] = OrcBomber, [3] = OrcRider },
            Cmd = OrcWarlord, Msg = OrcRunner, Cap = OrcChief, SkinHue = 95, SkinSat = 0.45f, MetalHue = 30, MetalSat = 0.08f, HorseTint = 0.45f, Rage = true,
            Names = new[] { "Грызь", "Хряк", "Шмяк", "Гнилозуб", "Рвач", "Бугай", "Кривоклык", "Жрун" },
        };
        public static readonly RaceDef Nav = new RaceDef
        {
            Key = "nav", Name = "Навь", Cry = "Навь идёт!", Units = { [0] = NavDead, [1] = NavGhoul, [2] = NavNecro, [3] = NavRider },
            Cmd = NavKing, Msg = NavHerald, Cap = NavLord, SkinHue = 60, SkinSat = 0.1f, MetalHue = 25, MetalSat = 0.3f, HorseTint = 0.45f, Undead = true,
            Names = new[] { "Кощей", "Мара", "Морок", "Карачун", "Вий", "Лихо", "Мор", "Чернава" },
        };
        public static readonly RaceDef Steppe = new RaceDef
        {
            Key = "steppe", Name = "Степь", Cry = "Урагх!", Units = { [0] = StNuker, [1] = StBatyr, [2] = StArcher, [3] = StHorseArcher },
            Cmd = StKhan, Msg = StMessenger, Cap = StMurza, SkinHue = 24, SkinSat = 0.42f, MetalHue = 38, MetalSat = 0.45f, Feign = true,
            Names = new[] { "Кончак", "Боняк", "Тугоркан", "Шарукан", "Итларь", "Котян", "Кобяк", "Гзак" },
        };
        public static readonly RaceDef[] Races = { Rus, Orcs, Nav, Steppe };

        /// <summary>Все виды бойцов всех рас; номер в массиве — тип бойца (Unit.Type, Squad.Type).</summary>
        public static readonly UnitDef[] All;
        public const int TCmd = 4, TMsg = 5, TCap = 6;

        static Defs()
        {
            var all = new List<UnitDef> { Types[0], Types[1], Types[2], Types[3], Commander, Messenger, Captain };
            string[] rusNames = { "мечники", "варвары", "арбалетчики", "рыцари" };
            for (int i = 0; i < 4; i++) { Types[i].Slot = i; Types[i].SquadName = rusNames[i]; Types[i].Race = Rus; }
            Commander.Race = Messenger.Race = Captain.Race = Rus;
            foreach (var r in Races)
            {
                if (r == Rus) continue;
                for (int i = 0; i < 4; i++) { r.Units[i].Slot = i; r.Units[i].Race = r; all.Add(r.Units[i]); }
                foreach (var s in new[] { r.Cmd, r.Msg, r.Cap }) { s.Race = r; all.Add(s); }
            }
            All = all.ToArray();
            for (int i = 0; i < All.Length; i++) All[i].Id = i;
        }

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
