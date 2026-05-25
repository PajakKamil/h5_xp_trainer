using System;

namespace Heroes5Trainer
{
    /// <summary>
    /// Hookuje instrukcje gry wykonywana setki razy/s, w ktorej rejestr ECX
    /// niesie wskaznik na aktualnie wybranego bohatera. Code cave duplikuje
    /// ten wskaznik do alokowanego slotu pamieci, dzieki czemu C# moze go
    /// odczytac w dowolnym momencie i prewencyjnie modyfikowac statystyki.
    ///
    /// Layout alokowanej pamieci (jeden blok, jeden Free):
    ///   [0..4)   - slot na wskaznik bohatera (zapisywany przez code cave)
    ///   [4..)    - kod code cave
    /// </summary>
    internal sealed class ActiveHeroTracker
    {
        // Oryginalne bajty pod adresem hooka: 'mov ecx,[esi+04]' + 'cmp ecx,[edi+04]'.
        private static readonly byte[] ExpectedOriginalBytes = { 0x8B, 0x4E, 0x04, 0x3B, 0x4F, 0x04 };

        // Tyle bajtow nadpisujemy w miejscu hooka (rowne sumie dlugosci dwoch instrukcji).
        // 3 (mov) + 3 (cmp) = 6 - mieszci 5-bajtowy 'jmp rel32' + 1 NOP.
        private const int StolenByteCount = 6;

        // Opcody x86 uzyte do zbudowania code cave.
        private const byte JumpOpcode = 0xE9;          // jmp rel32
        private const byte NopOpcode = 0x90;           // nop
        private const int JumpInstructionLength = 5;   // 0xE9 + 4-bajtowy offset
        // 'mov [imm32], ecx' - opcode 89 0D, potem 4-bajtowy adres bezwzgledny.
        private static readonly byte[] MovMemEcxOpcode = { 0x89, 0x0D };

        // Rozmiar slotu na wskaznik bohatera (32-bitowy proces gry).
        private const int HeroPtrSlotSize = sizeof(int);

        private readonly GameMemory memory;
        private readonly GameTarget target;
        private nint hookAddress;
        private nint allocationBase;   // poczatek alokowanego bloku (slot)
        private nint slotAddress;      // adres slotu na wskaznik (= allocationBase)
        private nint caveAddress;      // adres code cave (= allocationBase + HeroPtrSlotSize)
        private byte[]? originalBytes;

        public ActiveHeroTracker(GameMemory memory, GameTarget target)
        {
            this.memory = memory;
            this.target = target;
        }

        public bool IsInstalled => allocationBase != 0;

        public void Install()
        {
            if (IsInstalled)
                return;

            if (target.ActiveHeroHookOffset == 0)
            {
                throw new InvalidOperationException(
                    $"Offset hooka aktywnego bohatera nie jest skonfigurowany dla wersji gry '{target.ModuleName}'. " +
                    "Znajdz w Cheat Engine analogiczna instrukcje (mov reg, [reg+04] z wskaznikiem bohatera) i uzupelnij GameTarget.");
            }

            hookAddress = memory.ResolveAddress(target.ModuleName, target.ActiveHeroHookOffset);

            originalBytes = memory.ReadBytes(hookAddress, StolenByteCount);

            if (!BytesMatchExpected(originalBytes))
            {
                // Bajty nie sa oryginalne. Jesli pierwszy bajt to nasz JMP, znaczy to ze poprzednia
                // instancja trainera zostala zamknieta bez Uninstall - przywracamy oryginal i
                // instalujemy swiezo (stara alokacja code cave przepadnie z procesem gry).
                if (originalBytes[0] == JumpOpcode)
                {
                    memory.WriteBytes(hookAddress, ExpectedOriginalBytes);
                    originalBytes = (byte[])ExpectedOriginalBytes.Clone();
                }
                else
                {
                    throw new InvalidOperationException(
                        "Bajty pod adresem hooka aktywnego bohatera nie pasuja do oczekiwanej instrukcji. " +
                        "Sprawdz adres lub wersje gry - instalacje przerwano dla bezpieczenstwa.");
                }
            }

            int caveCodeLength = CalculateCaveCodeLength();
            int totalAllocationSize = HeroPtrSlotSize + caveCodeLength;
            allocationBase = memory.Allocate(totalAllocationSize);
            slotAddress = allocationBase;
            caveAddress = allocationBase + HeroPtrSlotSize;

            try
            {
                // Zerujemy slot - przed pierwszym fire hooka wskaznik jest "nieznany".
                memory.WriteBytes(slotAddress, new byte[HeroPtrSlotSize]);

                byte[] caveCode = BuildCaveCode();
                memory.WriteBytes(caveAddress, caveCode);

                memory.WriteBytes(hookAddress, BuildHookJump());
            }
            catch
            {
                memory.Free(allocationBase);
                allocationBase = 0;
                slotAddress = 0;
                caveAddress = 0;
                throw;
            }
        }

        public void Uninstall()
        {
            if (!IsInstalled)
                return;

            memory.WriteBytes(hookAddress, originalBytes!);
            memory.Free(allocationBase);
            allocationBase = 0;
            slotAddress = 0;
            caveAddress = 0;
        }

        /// <summary>
        /// Zwraca wskaznik na aktualnie wybranego bohatera, zapisany do slotu przez code cave.
        /// false, gdy patch nie jest zainstalowany lub slot jest jeszcze zerowy (hook jeszcze nie odpalil).
        /// </summary>
        public bool TryGetHeroPtr(out nint heroPtr)
        {
            heroPtr = 0;
            if (!IsInstalled)
                return false;

            byte[] bytes = memory.ReadBytes(slotAddress, HeroPtrSlotSize);
            int raw = BitConverter.ToInt32(bytes, 0);
            if (raw == 0)
                return false;

            // Adres w grze 32-bitowej - bezpiecznie konwertujemy do nint.
            heroPtr = (nint)(uint)raw;
            return true;
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

        private static int CalculateCaveCodeLength()
        {
            // ExpectedOriginalBytes (6 B) + mov [slot], ecx (2 + 4 B) + jmp rel32 (5 B)
            return ExpectedOriginalBytes.Length + MovMemEcxOpcode.Length + sizeof(int) + JumpInstructionLength;
        }

        private byte[] BuildCaveCode()
        {
            byte[] cave = new byte[CalculateCaveCodeLength()];
            int pos = 0;

            // 1. Pierwsza skradziona instrukcja: mov ecx, [esi+04]  (zalewa ECX swiezym wskaznikiem)
            Array.Copy(originalBytes!, 0, cave, pos, 3);
            pos += 3;

            // 2. mov [slot], ecx  -> zapisz aktualny wskaznik do naszego slotu
            MovMemEcxOpcode.CopyTo(cave, pos);
            pos += MovMemEcxOpcode.Length;
            BitConverter.GetBytes((uint)slotAddress).CopyTo(cave, pos);
            pos += sizeof(int);

            // 3. Druga skradziona instrukcja: cmp ecx, [edi+04]
            Array.Copy(originalBytes!, 3, cave, pos, 3);
            pos += 3;

            // 4. jmp rel32 -> powrot do gry tuz za skradzionymi bajtami
            cave[pos] = JumpOpcode;
            nint jumpInstruction = caveAddress + pos;
            nint returnTarget = hookAddress + StolenByteCount;
            BitConverter.GetBytes(RelativeOffset(jumpInstruction, returnTarget)).CopyTo(cave, pos + 1);

            return cave;
        }

        private byte[] BuildHookJump()
        {
            byte[] patch = new byte[StolenByteCount];
            Array.Fill(patch, NopOpcode);

            patch[0] = JumpOpcode;
            BitConverter.GetBytes(RelativeOffset(hookAddress, caveAddress)).CopyTo(patch, 1);
            return patch;
        }

        private static uint RelativeOffset(nint fromInstruction, nint toTarget)
        {
            return unchecked((uint)toTarget - (uint)fromInstruction - (uint)JumpInstructionLength);
        }
    }
}
