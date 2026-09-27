namespace BattleSim
{
    public enum UnitType { Swordsman, Spearman, Archer, Knight }

    /// <summary>Характеристики типа войск. Меняйте цифры здесь, чтобы настроить баланс.</summary>
    public class UnitStats
    {
        public string Name;
        public float Hp;
        public float Armor;          // 0..1 — доля поглощённого урона
        public float Speed;          // м/с
        public float Accel;          // разгон, м/с²
        public float Radius;         // размер "тела" для толкотни
        public float Mass;
        public float Reach;          // досягаемость оружия ближнего боя (сверх радиусов)
        public float Damage;
        public float Cooldown;       // секунд между ударами
        public float AttackTime;     // длительность анимации удара
        public bool Ranged;
        public float Range;          // дальность стрельбы
        public float ArrowBlock;     // 0..1 — сколько урона стрел гасит щит
        public float VsCavalry = 1f; // множитель урона по коннице
        public float Charge = 1f;    // множитель урона после разгона (натиск)
        public int SquadCols, SquadRows;
        public float Spacing;

        public static readonly UnitStats[] All =
        {
            new UnitStats
            {
                Name = "Мечники", Hp = 110, Armor = 0.30f, Speed = 3.3f, Accel = 12f, Radius = 0.42f, Mass = 1f,
                Reach = 0.75f, Damage = 18f, Cooldown = 1.05f, AttackTime = 0.55f, ArrowBlock = 0.55f,
                SquadCols = 5, SquadRows = 3, Spacing = 1.25f
            },
            new UnitStats
            {
                Name = "Копейщики", Hp = 95, Armor = 0.22f, Speed = 3.1f, Accel = 11f, Radius = 0.42f, Mass = 1f,
                Reach = 1.9f, Damage = 15f, Cooldown = 1.2f, AttackTime = 0.6f, VsCavalry = 2.6f, ArrowBlock = 0.1f,
                SquadCols = 5, SquadRows = 3, Spacing = 1.2f
            },
            new UnitStats
            {
                Name = "Лучники", Hp = 65, Armor = 0.05f, Speed = 3.4f, Accel = 12f, Radius = 0.4f, Mass = 0.9f,
                Reach = 0.5f, Damage = 22f, Cooldown = 2.1f, AttackTime = 1.0f, Ranged = true, Range = 34f,
                SquadCols = 5, SquadRows = 2, Spacing = 1.35f
            },
            new UnitStats
            {
                Name = "Рыцари", Hp = 210, Armor = 0.38f, Speed = 7.2f, Accel = 5f, Radius = 0.85f, Mass = 3.5f,
                Reach = 0.9f, Damage = 24f, Cooldown = 1.25f, AttackTime = 0.55f, Charge = 2.3f, ArrowBlock = 0.25f,
                SquadCols = 3, SquadRows = 2, Spacing = 2.3f
            },
        };

        public static UnitStats Of(UnitType t) => All[(int)t];
    }
}
