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
        public const int ManaOffset            = 0x13C;
        public const int MaxManaOffset         = 0x140;
        public const int MovementCurrentOffset = 0x12C;
        public const int MovementMaxOffset     = 0x130;

        // Region +0x40..+0x5C - tablica BONUSOW do statow (UI wyswietla base+bonus).
        // Mapowanie potwierdzone eksperymentem (zapis roznych wartosci, odczyt UI gry):
        public const int AttackBonusOffset     = 0x48;
        public const int DefenseBonusOffset    = 0x4C;
        public const int SpellPowerBonusOffset = 0x50;
        public const int KnowledgeBonusOffset  = 0x54;

        // Morale i luck maja po DWA slotow bonusow - oba sumuja sie do wyswietlanej wartosci.
        // Prawdopodobnie z roznych zrodel (artefakty / skille / sklad armii).
        // Zapis do A wystarcza by zwiekszyc statystyke; B zostawiamy w spokoju zeby nie nadpisac
        // bonusow z innych zrodel ktore gra moze tam trzymac.
        public const int MoraleBonusOffsetA = 0x44;
        public const int MoraleBonusOffsetB = 0x5C;
        public const int LuckBonusOffsetA   = 0x40;
        public const int LuckBonusOffsetB   = 0x58;

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

        /// <summary>
        /// Zrzuca surowe bajty struktury bohatera (od bazy do bazy+length) w formacie
        /// "offset: bajty hex | dekodowane jako int32" - 16 bajtow na linie, czyli 4 dwordy.
        /// Pomaga zlokalizowac nieznane jeszcze pola (morale, luck, movement itp.):
        /// patrzysz na wartosc w UI gry i szukasz jej w kolumnie int32 dumpu.
        /// </summary>
        public static string DumpHex(GameMemory memory, nint heroPtr, int length)
        {
            const int bytesPerRow = 16;
            const int dwordsPerRow = bytesPerRow / Int32Size;

            byte[] bytes = memory.ReadBytes(heroPtr, length);
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            for (int rowStart = 0; rowStart < bytes.Length; rowStart += bytesPerRow)
            {
                sb.Append("  +0x").Append(rowStart.ToString("X3")).Append(": ");

                int rowLen = Math.Min(bytesPerRow, bytes.Length - rowStart);

                for (int i = 0; i < bytesPerRow; i++)
                {
                    if (i > 0 && i % Int32Size == 0)
                        sb.Append(' ');

                    if (i < rowLen)
                        sb.Append(bytes[rowStart + i].ToString("X2")).Append(' ');
                    else
                        sb.Append("   ");
                }

                sb.Append(" |");
                for (int dword = 0; dword < dwordsPerRow; dword++)
                {
                    int byteOffset = dword * Int32Size;
                    if (byteOffset + Int32Size <= rowLen)
                    {
                        int value = BitConverter.ToInt32(bytes, rowStart + byteOffset);
                        sb.Append(' ').Append(value.ToString().PadLeft(12));
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString();
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
                MaxMana:      ReadInt32(memory, heroPtr, MaxManaOffset),
                Movement:     ReadInt32(memory, heroPtr, MovementCurrentOffset),
                MovementMax:  ReadInt32(memory, heroPtr, MovementMaxOffset));
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
        int MaxMana,
        int Movement,
        int MovementMax);
}
