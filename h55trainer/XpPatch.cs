using System;

namespace Heroes5Trainer
{
    /// <summary>
    /// Instaluje i usuwa modyfikacje XP metoda "code cave".
    ///
    /// Oryginalna instrukcja gry ('mov [edi+0x60], esi') ma tylko 3 bajty,
    /// a nasz kod ('add esi, bonus' + 'mov ...') zajmuje wiecej niz sie tam miesci.
    /// Dlatego w miejscu hooka wstawiamy 5-bajtowy skok do nowo zaalokowanej pamieci,
    /// w ktorej najpierw dodajemy bonus XP do rejestru ESI, potem wykonujemy
    /// oryginalne instrukcje gry, a na koniec wracamy do gry.
    /// </summary>
    internal sealed class XpPatch
    {
        /// <summary>Ilosc XP dodawana przy kazdym zdobyciu doswiadczenia.</summary>
        public const int XpBonus = 2_000_000_000;

        // Oryginalna instrukcja gry pod adresem hooka: 'mov [edi+0x60], esi'.
        private static readonly byte[] ExpectedOriginalBytes = { 0x89, 0x77, 0x60 };

        // Pelne 6 bajtow nadpisanych przez hook ('mov [edi+0x60],esi' + 'mov edx,[ebp+0x00]').
        // Uzywane do recovery po niedoczyszczonej poprzedniej instancji trainera, gdy
        // druga instrukcja w grze jest juz nadpisana NOP-em i nie da sie jej odczytac.
        private static readonly byte[] FullOriginalBytes = { 0x89, 0x77, 0x60, 0x8B, 0x55, 0x00 };

        // WAZNE: tyle bajtow nadpisujemy w miejscu hooka. Wartosc musi byc:
        //  - co najmniej 5 (tyle zajmuje instrukcja 'jmp rel32'),
        //  - rowna pelnej liczbie CALYCH instrukcji liczonych od adresu hooka.
        // Dezasemblacja spod adresu hooka jest identyczna w obu obslugiwanych grach:
        //   89 77 60 - mov [edi+60],esi  (3 bajty)
        //   8B 55 00 - mov edx,[ebp+00]  (3 bajty)
        // 3 bajty to za malo na 'jmp rel32', wiec doliczamy druga instrukcje:
        // 3 + 3 = 6. Hook nadpisuje wiec dwie pelne instrukcje (6 bajtow).
        // KONIECZNIE zweryfikuj te liczbe przy dodawaniu nowej wersji gry - bledna
        // wartosc rozjedzie sie z granica instrukcji i spowoduje crash gry.
        private const int StolenByteCount = 6;

        // Opcody x86 uzywane do zbudowania code cave.
        private const byte JumpOpcode = 0xE9;          // jmp rel32
        private const byte NopOpcode = 0x90;           // nop
        private const int JumpInstructionLength = 5;   // 0xE9 + 4-bajtowy offset
        private static readonly byte[] AddEsiOpcode = { 0x81, 0xC6 }; // add esi, imm32

        private readonly GameMemory memory;
        private readonly GameTarget target;
        private nint hookAddress;
        private nint caveAddress;
        private byte[]? originalBytes;

        public XpPatch(GameMemory memory, GameTarget target)
        {
            this.memory = memory;
            this.target = target;
        }

        /// <summary>Czy modyfikacja jest obecnie zainstalowana w grze.</summary>
        public bool IsInstalled => caveAddress != 0;

        /// <summary>Bajty gry nadpisane przez ostatnia instalacje hooka (do podgladu).</summary>
        public byte[]? StolenOriginalBytes => originalBytes;

        /// <summary>Wstawia hook i code cave do pamieci gry.</summary>
        public void Install()
        {
            if (IsInstalled)
                return;

            hookAddress = memory.ResolveAddress(target.ModuleName, target.CodeOffset);

            // Zapamietujemy oryginalne bajty, by moc je pozniej przywrocic.
            originalBytes = memory.ReadBytes(hookAddress, StolenByteCount);

            // Bezpiecznik: sprawdzamy, czy pod adresem faktycznie jest spodziewana instrukcja.
            // Jesli nie, ale pierwszy bajt to nasz JMP - znaczy ze poprzednia instancja trainera
            // nie zrobila Uninstall (np. zamknieta przez X). Przywracamy oryginal i instalujemy
            // swiezo (stara alokacja code cave przepadnie z procesem gry).
            if (!BytesMatchExpected(originalBytes))
            {
                if (originalBytes[0] == JumpOpcode)
                {
                    // Przywracamy pelne 6 bajtow z hardkodu - druga instrukcja w grze jest
                    // juz NOP-em po naszym poprzednim hooku, nie da sie jej odczytac.
                    memory.WriteBytes(hookAddress, FullOriginalBytes);
                    originalBytes = (byte[])FullOriginalBytes.Clone();
                }
                else
                {
                    throw new InvalidOperationException(
                        "Bajty pod adresem hooka nie pasuja do oczekiwanej instrukcji gry. " +
                        "Sprawdz adres lub wersje gry - instalacje przerwano dla bezpieczenstwa.");
                }
            }

            // Najpierw alokujemy code cave, bo offsety skokow zaleza od jego adresu.
            byte[] caveCode = BuildCaveCode();
            caveAddress = memory.Allocate(caveCode.Length);

            try
            {
                WriteReturnJump(caveCode);
                memory.WriteBytes(caveAddress, caveCode);

                // Na koniec podmieniamy oryginalna instrukcje na skok do code cave.
                memory.WriteBytes(hookAddress, BuildHookJump());
            }
            catch
            {
                // Instalacja przerwana w polowie - zwalniamy code cave i czyscimy stan,
                // by nie zostawic w grze osieroconej pamieci ani falszywego "zainstalowano".
                memory.Free(caveAddress);
                caveAddress = 0;
                throw;
            }
        }

        /// <summary>Przywraca oryginalny kod gry i zwalnia code cave.</summary>
        public void Uninstall()
        {
            if (!IsInstalled)
                return;

            memory.WriteBytes(hookAddress, originalBytes!);
            memory.Free(caveAddress);
            caveAddress = 0;
        }

        private static bool BytesMatchExpected(byte[] bytes)
        {
            for (int i = 0; i < ExpectedOriginalBytes.Length; i++)
            {
                if (bytes[i] != ExpectedOriginalBytes[i])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Buduje zawartosc code cave: 'add esi, XpBonus' + oryginalne instrukcje + 'jmp' powrotny.
        /// Offset skoku powrotnego uzupelnia sie pozniej (WriteReturnJump), gdy znany jest adres cave.
        /// </summary>
        private byte[] BuildCaveCode()
        {
            int caveLength = AddEsiOpcode.Length + sizeof(int) + StolenByteCount + JumpInstructionLength;
            byte[] cave = new byte[caveLength];
            int pos = 0;

            // add esi, XpBonus
            AddEsiOpcode.CopyTo(cave, pos);
            pos += AddEsiOpcode.Length;
            BitConverter.GetBytes(XpBonus).CopyTo(cave, pos);
            pos += sizeof(int);

            // oryginalne instrukcje gry, ktore nadpisze hook
            originalBytes!.CopyTo(cave, pos);
            pos += StolenByteCount;

            // jmp powrotny - opcode teraz, offset w WriteReturnJump
            cave[pos] = JumpOpcode;
            return cave;
        }

        /// <summary>Uzupelnia offset skoku powrotnego z code cave do gry.</summary>
        private void WriteReturnJump(byte[] caveCode)
        {
            int jumpOffset = caveCode.Length - JumpInstructionLength;
            nint jumpInstruction = caveAddress + jumpOffset;
            nint returnTarget = hookAddress + StolenByteCount;
            BitConverter.GetBytes(RelativeOffset(jumpInstruction, returnTarget)).CopyTo(caveCode, jumpOffset + 1);
        }

        /// <summary>Buduje 5-bajtowy skok z miejsca hooka do code cave (reszta bajtow to NOP-y).</summary>
        private byte[] BuildHookJump()
        {
            byte[] patch = new byte[StolenByteCount];
            Array.Fill(patch, NopOpcode);

            patch[0] = JumpOpcode;
            BitConverter.GetBytes(RelativeOffset(hookAddress, caveAddress)).CopyTo(patch, 1);
            return patch;
        }

        /// <summary>
        /// Liczy 32-bitowy offset dla instrukcji 'jmp rel32'. Procesor liczy adres
        /// docelowy wzgledem konca instrukcji skoku, a arytmetyka jest 32-bitowa
        /// (z zawijaniem) - dlatego operujemy na typie uint w kontekscie unchecked.
        /// </summary>
        private static uint RelativeOffset(nint fromInstruction, nint toTarget)
        {
            return unchecked((uint)toTarget - (uint)fromInstruction - (uint)JumpInstructionLength);
        }
    }
}