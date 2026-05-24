using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Heroes5Trainer
{
    internal static class Program
    {
        // Kody klawiszy Windows Virtual-Key (F1..F12).
        private const int KeyRefillMovement = 0x70;    // F1 - napelnij punkty ruchu
        private const int KeyXpPatchToggle = 0x71;     // F2 - cave XP bonus przy zdobyciu XP
        private const int KeyTrackerToggle = 0x72;     // F3 - install/uninstall ActiveHeroTracker
        private const int KeyShowSnapshot  = 0x73;     // F4 - wypisz statystyki aktywnego bohatera
        private const int KeySetAttack     = 0x74;     // F5
        private const int KeySetDefense    = 0x75;     // F6
        private const int KeySetSpell      = 0x76;     // F7
        private const int KeySetKnowledge  = 0x77;     // F8
        private const int KeyRefillMana    = 0x78;     // F9
        private const int KeyToggleFreezeAttack = 0x79;// F10
        private const int KeyAddXp         = 0x7A;     // F11
        private const int KeyDumpHero      = 0x7B;     // F12 - hex dump struktury bohatera
        private const int KeyMaxMoraleLuck = 0xBB;     // '=' (VK_OEM_PLUS) - morale + luck = DefaultStatValue

        private const int KeyPressedMask = 0x8000;

        // Czasy uspienia oraz wartosci docelowe (zeby nie bylo magic numbers).
        private const int WaitForGameDelayMs = 1000;
        private const int PerKeyDebounceMs = 350;
        private const int IdleDelayMs = 10;
        private const int DefaultStatValue = 99;
        private const int XpQuickBonus = 1_000_000;
        // Ile bajtow struktury bohatera zrzucac na zadanie F12.
        // 0x180 pokrywa znane pola (atak..maxmana na 0x140) z zapasem na sasiednie.
        private const int HeroDumpLength = 0x180;

        private static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "Heroes 5 Trainer - Console Edition";
            PrintHeader();

            while (true)
            {
                using GameMemory memory = new GameMemory();

                if (!AttachToGame(memory))
                    return;

                GameTarget target = GameTarget.FromProcessName(memory.GameProcess!.ProcessName)!;
                PrintReady(target);

                XpPatch xpPatch = new XpPatch(memory, target);
                ActiveHeroTracker tracker = new ActiveHeroTracker(memory, target);
                using FreezeManager freeze = new FreezeManager(memory, tracker);
                freeze.Start();

                RunTrainerLoop(memory, xpPatch, tracker, freeze);

                // Gra znikla - zatrzymujemy watek freeze, nie probujemy uninstall hookow
                // (proces juz nie zyje, zapisy by sie wywrocily).
                freeze.Stop();

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine();
                Console.WriteLine("[INFO] Gra zostala zamknieta. Powrot do oczekiwania na gre...");
                Console.ResetColor();
            }
        }

        private static bool AttachToGame(GameMemory memory)
        {
            Console.WriteLine(
                $"Oczekiwanie na uruchomienie gry ({string.Join(" / ", GameTarget.ModuleNames)})...");

            while (!memory.TryFindProcess(GameTarget.ProcessNames))
                Thread.Sleep(WaitForGameDelayMs);

            if (memory.OpenForEditing())
                return true;

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[BLAD] Nie udalo sie uzyskac dostepu do pamieci gry!");
            Console.WriteLine("Uruchom ten program jako Administrator.");
            Console.ResetColor();
            Console.WriteLine("Nacisnij Enter, aby zamknac...");
            Console.ReadLine();
            return false;
        }

        private static void RunTrainerLoop(
            GameMemory memory, XpPatch xpPatch, ActiveHeroTracker tracker, FreezeManager freeze)
        {
            // Per-klawisz debounce - inaczej rozne klawisze blokowalyby sie nawzajem
            // przez globalny Thread.Sleep.
            Dictionary<int, long> lastPressedAtMs = new Dictionary<int, long>();

            while (memory.IsGameRunning)
            {
                HandleKey(KeyRefillMovement,     lastPressedAtMs, () => RefillMovement(memory, tracker));
                HandleKey(KeyXpPatchToggle,      lastPressedAtMs, () => ToggleXpPatch(xpPatch));
                HandleKey(KeyTrackerToggle,      lastPressedAtMs, () => ToggleTracker(tracker));
                HandleKey(KeyShowSnapshot,       lastPressedAtMs, () => ShowSnapshot(memory, tracker));
                HandleKey(KeySetAttack,          lastPressedAtMs, () => SetStat(memory, tracker, "atak",       HeroStats.AttackOffset,     DefaultStatValue));
                HandleKey(KeySetDefense,         lastPressedAtMs, () => SetStat(memory, tracker, "obrona",     HeroStats.DefenseOffset,    DefaultStatValue));
                HandleKey(KeySetSpell,           lastPressedAtMs, () => SetStat(memory, tracker, "spell power",HeroStats.SpellPowerOffset, DefaultStatValue));
                HandleKey(KeySetKnowledge,       lastPressedAtMs, () => SetStat(memory, tracker, "wiedza",     HeroStats.KnowledgeOffset,  DefaultStatValue));
                HandleKey(KeyRefillMana,         lastPressedAtMs, () => RefillMana(memory, tracker));
                HandleKey(KeyToggleFreezeAttack, lastPressedAtMs, () => ToggleFreezeAttack(freeze));
                HandleKey(KeyAddXp,              lastPressedAtMs, () => AddXp(memory, tracker, XpQuickBonus));
                HandleKey(KeyDumpHero,           lastPressedAtMs, () => DumpHero(memory, tracker));
                HandleKey(KeyMaxMoraleLuck,      lastPressedAtMs, () => MaxMoraleAndLuck(memory, tracker));

                Thread.Sleep(IdleDelayMs);
            }
        }

        private static void HandleKey(int virtualKey, Dictionary<int, long> lastPressedAtMs, Action action)
        {
            if ((NativeMethods.GetAsyncKeyState(virtualKey) & KeyPressedMask) == 0)
                return;

            long nowMs = Environment.TickCount64;
            if (lastPressedAtMs.TryGetValue(virtualKey, out long lastMs) && nowMs - lastMs < PerKeyDebounceMs)
                return;

            lastPressedAtMs[virtualKey] = nowMs;
            SafeRun(action);
        }

        private static void SafeRun(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[BLAD] {ex.Message}");
                Console.ResetColor();
            }
        }

        private static void ToggleXpPatch(XpPatch patch)
        {
            if (!patch.IsInstalled)
            {
                patch.Install();
                Print(ConsoleColor.Magenta,
                    $"Trainer XP AKTYWNY - nastepne zdobycie XP = +{XpPatch.XpBonus:N0}.");
            }
            else
            {
                patch.Uninstall();
                Print(ConsoleColor.Yellow, "Trainer XP WYLACZONY.");
            }
        }

        private static void ToggleTracker(ActiveHeroTracker tracker)
        {
            if (!tracker.IsInstalled)
            {
                tracker.Install();
                Print(ConsoleColor.Magenta,
                    "Tracker aktywnego bohatera AKTYWNY - wybierz bohatera w grze, by zlapac wskaznik.");
            }
            else
            {
                tracker.Uninstall();
                Print(ConsoleColor.Yellow, "Tracker aktywnego bohatera WYLACZONY.");
            }
        }

        private static bool TryRequireHero(ActiveHeroTracker tracker, out nint heroPtr)
        {
            if (!tracker.IsInstalled)
            {
                Print(ConsoleColor.Yellow, "Najpierw wlacz tracker bohatera (F3).");
                heroPtr = 0;
                return false;
            }

            if (!tracker.TryGetHeroPtr(out heroPtr))
            {
                Print(ConsoleColor.Yellow, "Brak aktywnego bohatera - wybierz bohatera w grze (klik na mapie).");
                return false;
            }

            return true;
        }

        private static void ShowSnapshot(GameMemory memory, ActiveHeroTracker tracker)
        {
            if (!TryRequireHero(tracker, out nint heroPtr))
                return;

            HeroSnapshot s = HeroStats.Snapshot(memory, heroPtr);
            Print(ConsoleColor.Cyan,
                $"Bohater @ 0x{heroPtr:X}: " +
                $"ATK={s.Attack} DEF={s.Defense} SP={s.SpellPower} KN={s.Knowledge} " +
                $"LVL={s.CurrentLevel}->{s.ToLevel} XP={s.Experience:N0} " +
                $"MANA={s.Mana}/{s.MaxMana} RUCH={s.Movement}/{s.MovementMax}");
        }

        private static void SetStat(GameMemory memory, ActiveHeroTracker tracker, string name, int fieldOffset, int value)
        {
            if (!TryRequireHero(tracker, out nint heroPtr))
                return;

            HeroStats.WriteInt32(memory, heroPtr, fieldOffset, value);
            Print(ConsoleColor.Green, $"{name} = {value}");
        }

        private static void RefillMovement(GameMemory memory, ActiveHeroTracker tracker)
        {
            if (!TryRequireHero(tracker, out nint heroPtr))
                return;

            int max = HeroStats.ReadInt32(memory, heroPtr, HeroStats.MovementMaxOffset);
            HeroStats.WriteInt32(memory, heroPtr, HeroStats.MovementCurrentOffset, max);
            Print(ConsoleColor.Green, $"Ruch napelniony ({max}/{max}).");
        }

        private static void RefillMana(GameMemory memory, ActiveHeroTracker tracker)
        {
            if (!TryRequireHero(tracker, out nint heroPtr))
                return;

            int max = HeroStats.ReadInt32(memory, heroPtr, HeroStats.MaxManaOffset);
            HeroStats.WriteInt32(memory, heroPtr, HeroStats.ManaOffset, max);
            Print(ConsoleColor.Green, $"Mana napelniona ({max}/{max}).");
        }

        private static void ToggleFreezeAttack(FreezeManager freeze)
        {
            bool nowFrozen = freeze.Toggle(HeroStats.AttackOffset, DefaultStatValue);
            if (nowFrozen)
                Print(ConsoleColor.Magenta, $"Atak ZAMROZONY na {DefaultStatValue}.");
            else
                Print(ConsoleColor.Yellow, "Atak ODMROZONY.");
        }

        private static void AddXp(GameMemory memory, ActiveHeroTracker tracker, int bonus)
        {
            if (!TryRequireHero(tracker, out nint heroPtr))
                return;

            int current = HeroStats.ReadInt32(memory, heroPtr, HeroStats.ExperienceOffset);
            int updated = current + bonus;
            HeroStats.WriteInt32(memory, heroPtr, HeroStats.ExperienceOffset, updated);
            Print(ConsoleColor.Green, $"XP {current:N0} -> {updated:N0} (+{bonus:N0}).");
        }

        private static void MaxMoraleAndLuck(GameMemory memory, ActiveHeroTracker tracker)
        {
            if (!TryRequireHero(tracker, out nint heroPtr))
                return;

            // Slot A wystarcza by podniesc statystyke; slot B nie ruszamy, by nie nadpisac
            // bonusow z innych zrodel ktore gra moze tam trzymac (sklad armii, skille).
            HeroStats.WriteInt32(memory, heroPtr, HeroStats.MoraleBonusOffsetA, DefaultStatValue);
            HeroStats.WriteInt32(memory, heroPtr, HeroStats.LuckBonusOffsetA,   DefaultStatValue);
            Print(ConsoleColor.Green, $"Morale i Luck = {DefaultStatValue} (bonus do bazy).");
        }

        private static void DumpHero(GameMemory memory, ActiveHeroTracker tracker)
        {
            if (!TryRequireHero(tracker, out nint heroPtr))
                return;

            string dump = HeroStats.DumpHex(memory, heroPtr, HeroDumpLength);
            Print(ConsoleColor.Cyan, $"Dump struktury bohatera @ 0x{heroPtr:X} ({HeroDumpLength} bajtow):");
            Console.Write(dump);
        }

        private static void Print(ConsoleColor color, string message)
        {
            Console.ForegroundColor = color;
            Console.WriteLine($"[{DateTime.Now.ToLongTimeString()}] {message}");
            Console.ResetColor();
        }

        private static void PrintHeader()
        {
            Console.WriteLine("=============================================");
            Console.WriteLine("  Heroes 5 Trainer (Console App)");
            Console.WriteLine("=============================================");
        }

        private static void PrintReady(GameTarget target)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[SUKCES] Podpieto pod gre: {target.ModuleName}");
            Console.ResetColor();
            Console.WriteLine("---------------------------------------------");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("HOTKEYE:");
            Console.WriteLine( "  [F1]  napelnij punkty ruchu");
            Console.WriteLine($"  [F2]  cave-bonus XP (kazde zdobycie XP = +{XpPatch.XpBonus:N0})");
            Console.WriteLine( "  [F3]  wlacz/wylacz tracker aktywnego bohatera (warunek dla F4-F11)");
            Console.WriteLine( "  [F4]  pokaz statystyki aktywnego bohatera");
            Console.WriteLine($"  [F5]  atak       = {DefaultStatValue}");
            Console.WriteLine($"  [F6]  obrona     = {DefaultStatValue}");
            Console.WriteLine($"  [F7]  spell pow. = {DefaultStatValue}");
            Console.WriteLine($"  [F8]  wiedza     = {DefaultStatValue}");
            Console.WriteLine( "  [F9]  pelna mana");
            Console.WriteLine($"  [F10] freeze ataku na {DefaultStatValue} (toggle)");
            Console.WriteLine($"  [F11] +{XpQuickBonus:N0} XP");
            Console.WriteLine($"  [F12] hex dump struktury bohatera ({HeroDumpLength} bajtow) - do szukania nieznanych pol");
            Console.WriteLine($"  [=]   morale + luck = {DefaultStatValue} (bonus do bazy)");
            Console.ResetColor();
            Console.WriteLine("---------------------------------------------");
        }
    }
}
