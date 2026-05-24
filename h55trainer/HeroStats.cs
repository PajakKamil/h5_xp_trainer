using System;

namespace Heroes5Trainer
{
    /// <summary>
    /// Offsety statystyk bohatera od bazy jego struktury w pamieci gry
    /// oraz metody pomocnicze do odczytu i zapisu pojedynczych pol.
    /// Adresy znalezione w Cheat Engine (MMH55_64.exe); ta sama struktura
    /// wystepuje w obu obslugiwanych wersjach gry.
    /// </summary>
    internal static class HeroStats
    {
        public const int AttackOffset       = 0x30;
        public const int DefenseOffset      = 0x34;
        public const int SpellPowerOffset   = 0x38;
        public const int KnowledgeOffset    = 0x3C;
        public const int ExperienceOffset   = 0x60;
        public const int CurrentLevelOffset = 0x64;
        public const int ToLevelOffset      = 0xD8;
        public const int ManaOffset         = 0x13C;
        public const int MaxManaOffset      = 0x140;

        private const int Int32Size = sizeof(int);

        public static int ReadInt32(GameMemory memory, nint heroPtr, int fieldOffset)
        {
            byte[] bytes = memory.ReadBytes(heroPtr + fieldOffset, Int32Size);
            return BitConverter.ToInt32(bytes, 0);
        }

        public static void WriteInt32(GameMemory memory, nint heroPtr, int fieldOffset, int value)
        {
            memory.WriteBytes(heroPtr + fieldOffset, BitConverter.GetBytes(value));
        }

        /// <summary>Spojny snapshot wszystkich statow do wyswietlenia.</summary>
        public static HeroSnapshot Snapshot(GameMemory memory, nint heroPtr)
        {
            return new HeroSnapshot(
                Attack:       ReadInt32(memory, heroPtr, AttackOffset),
                Defense:      ReadInt32(memory, heroPtr, DefenseOffset),
                SpellPower:   ReadInt32(memory, heroPtr, SpellPowerOffset),
                Knowledge:    ReadInt32(memory, heroPtr, KnowledgeOffset),
                Experience:   ReadInt32(memory, heroPtr, ExperienceOffset),
                CurrentLevel: ReadInt32(memory, heroPtr, CurrentLevelOffset),
                ToLevel:      ReadInt32(memory, heroPtr, ToLevelOffset),
                Mana:         ReadInt32(memory, heroPtr, ManaOffset),
                MaxMana:      ReadInt32(memory, heroPtr, MaxManaOffset));
        }
    }

    internal sealed record HeroSnapshot(
        int Attack,
        int Defense,
        int SpellPower,
        int Knowledge,
        int Experience,
        int CurrentLevel,
        int ToLevel,
        int Mana,
        int MaxMana);
}
