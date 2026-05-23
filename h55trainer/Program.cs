using System;
using System.Text;
using System.Threading;

namespace Heroes5Trainer
{
    internal static class Program
    {
        // Kod klawisza F2 w systemie Windows oraz maska "najwyższego bitu" stanu klawisza.
        private const int ToggleKey = 0x71;
        private const int KeyPressedMask = 0x8000;

        // Czasy uśpienia pętli w milisekundach.
        private const int WaitForGameDelayMs = 1000;
        private const int DebounceDelayMs = 500;
        private const int IdleDelayMs = 10;

        private static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "Heroes 5 XP Trainer - Console Edition";
            PrintHeader();

            // Każdy obieg pętli to jedna sesja gry: czekamy na grę, podpinamy się,
            // działamy aż do jej zamknięcia, po czym wracamy do oczekiwania.
            while (true)
            {
                using GameMemory memory = new GameMemory();

                if (!AttachToGame(memory))
                    return;

                GameTarget target = GameTarget.FromProcessName(memory.GameProcess!.ProcessName)!;
                PrintReady(target);

                RunTrainerLoop(memory, new XpPatch(memory, target));

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine();
                Console.WriteLine("[INFO] Gra została zamknięta. Powrót do oczekiwania na grę...");
                Console.ResetColor();
            }
        }

        /// <summary>
        /// Czeka, aż któraś z obsługiwanych wersji gry zostanie uruchomiona,
        /// po czym podpina się do jej pamięci.
        /// Zwraca false, gdy brak uprawnień administratora - program powinien się wtedy zakończyć.
        /// </summary>
        private static bool AttachToGame(GameMemory memory)
        {
            Console.WriteLine(
                $"Oczekiwanie na uruchomienie gry ({string.Join(" / ", GameTarget.ModuleNames)})...");

            while (!memory.TryFindProcess(GameTarget.ProcessNames))
                Thread.Sleep(WaitForGameDelayMs);

            if (memory.OpenForEditing())
                return true;

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[BŁĄD] Nie udało się uzyskać dostępu do pamięci gry!");
            Console.WriteLine("Uruchom ten program jako Administrator.");
            Console.ResetColor();
            Console.WriteLine("Naciśnij Enter, aby zamknąć...");
            Console.ReadLine();
            return false;
        }

        /// <summary>Główna pętla sesji: reaguje na klawisz F2 aż do zamknięcia gry.</summary>
        private static void RunTrainerLoop(GameMemory memory, XpPatch patch)
        {
            while (memory.IsGameRunning)
            {
                // Najwyższy bit stanu klawisza oznacza, że F2 jest właśnie wciśnięty.
                if ((NativeMethods.GetAsyncKeyState(ToggleKey) & KeyPressedMask) != 0)
                {
                    ToggleTrainer(patch);

                    // Zabezpieczenie przed wielokrotnym przełączeniem przy jednym wciśnięciu.
                    Thread.Sleep(DebounceDelayMs);
                }

                // Mały odpoczynek dla procesora.
                Thread.Sleep(IdleDelayMs);
            }
        }

        /// <summary>Włącza lub wyłącza modyfikację XP i wypisuje wynik na konsolę.</summary>
        private static void ToggleTrainer(XpPatch patch)
        {
            try
            {
                if (!patch.IsInstalled)
                {
                    patch.Install();
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine($"[{DateTime.Now.ToLongTimeString()}] Trainer AKTYWNY! " +
                                      $"Następne zdobycie XP = +{XpPatch.XpBonus:N0}.");
                    Console.WriteLine($"  Nadpisane bajty gry: {BitConverter.ToString(patch.StolenOriginalBytes!)}");
                }
                else
                {
                    patch.Uninstall();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[{DateTime.Now.ToLongTimeString()}] " +
                                      "Trainer WYŁĄCZONY. Przywrócono normalne zdobywanie XP.");
                }
            }
            catch (Exception ex)
            {
                // Błąd iniekcji nie powinien wywrócić całego programu - tylko go zgłaszamy.
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[BŁĄD] {ex.Message}");
            }
            finally
            {
                Console.ResetColor();
            }
        }

        private static void PrintHeader()
        {
            Console.WriteLine("=============================================");
            Console.WriteLine("  Heroes 5 XP Trainer (Console App)");
            Console.WriteLine("=============================================");
        }

        /// <summary>Wypisuje potwierdzenie podpięcia oraz instrukcję obsługi.</summary>
        private static void PrintReady(GameTarget target)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[SUKCES] Podpięto pod grę: {target.ModuleName}");
            Console.ResetColor();
            Console.WriteLine("---------------------------------------------");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("INSTRUKCJA:");
            Console.WriteLine("1. Wciśnij [F2] w dowolnym momencie gry, aby AKTYWOWAĆ modyfikację.");
            Console.WriteLine("2. Wejdź w grze w jakąkolwiek interakcję dającą XP (walka, skrzynia).");
            Console.WriteLine($"3. Twój bohater natychmiast otrzyma {XpPatch.XpBonus:N0} XP i wbije poziomy!");
            Console.ResetColor();
            Console.WriteLine("---------------------------------------------");
        }
    }
}