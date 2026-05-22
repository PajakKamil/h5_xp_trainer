using System;
using System.Text;
using System.Threading;

namespace Heroes5Trainer
{
    internal static class Program
    {
        // Kod klawisza F2 w systemie Windows oraz maska "najwyzszego bitu" stanu klawisza.
        private const int ToggleKey = 0x71;
        private const int KeyPressedMask = 0x8000;

        // Nazwa procesu gry (bez rozszerzenia .exe).
        private const string GameProcessName = "MMH55_64";

        // Czasy uspienia petli w milisekundach.
        private const int WaitForGameDelayMs = 1000;
        private const int DebounceDelayMs = 500;
        private const int IdleDelayMs = 10;

        private static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "Heroes 5 XP Trainer - Console Edition";
            Console.WriteLine("=============================================");
            Console.WriteLine("  Heroes 5 XP Trainer (Console App)");
            Console.WriteLine("=============================================");
            Console.WriteLine($"Oczekiwanie na uruchomienie gry ({GameProcessName}.exe)...");

            using GameMemory memory = new GameMemory();

            // Pętla czekająca, aż gra zostanie uruchomiona.
            while (!memory.TryFindProcess(GameProcessName))
                Thread.Sleep(WaitForGameDelayMs);

            // Otwieramy proces gry z uprawnieniami do edycji pamięci.
            if (!memory.OpenForEditing())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[BŁĄD] Nie udało się uzyskać dostępu do pamięci gry!");
                Console.WriteLine("Uruchom ten program jako Administrator.");
                Console.ResetColor();
                Console.ReadLine();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[SUKCES] Podpięto pod Heroes 5!");
            Console.ResetColor();
            Console.WriteLine("---------------------------------------------");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("INSTRUKCJA:");
            Console.WriteLine("1. Wciśnij [F2] w dowolnym momencie gry, aby AKTYWOWAĆ modyfikację.");
            Console.WriteLine("2. Wejdź w grze w jakąkolwiek interakcję dającą XP (walka, skrzynia).");
            Console.WriteLine($"3. Twój bohater natychmiast otrzyma {XpPatch.XpBonus:N0} XP i wbije poziomy!");
            Console.ResetColor();
            Console.WriteLine("---------------------------------------------");

            XpPatch patch = new XpPatch(memory);

            // Główna pętla programu działająca w tle.
            while (true)
            {
                // Sprawdzamy, czy klawisz F5 został wciśnięty (najwyższy bit stanu klawisza).
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
    }
}