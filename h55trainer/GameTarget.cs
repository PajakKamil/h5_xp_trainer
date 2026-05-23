using System;

namespace Heroes5Trainer
{
    /// <summary>
    /// Opisuje pojedyncza obslugiwana wersje gry: nazwe procesu, nazwe modulu
    /// oraz offset instrukcji zdobywania XP wewnatrz tego modulu.
    /// </summary>
    /// <param name="ProcessName">Nazwa procesu bez rozszerzenia .exe (do wyszukiwania procesu).</param>
    /// <param name="ModuleName">Nazwa modulu z rozszerzeniem .exe (do wyliczenia adresu bazowego).</param>
    /// <param name="CodeOffset">Offset instrukcji 'mov [edi+0x60], esi' wzgledem bazy modulu.</param>
    internal sealed record GameTarget(string ProcessName, string ModuleName, int CodeOffset)
    {
        /// <summary>
        /// Wszystkie wersje gry, ktore trainer potrafi rozpoznac i zmodyfikowac.
        /// Offsety pochodza z Cheat Engine; instrukcja pod kazdym z nich jest identyczna
        /// ('mov [edi+0x60], esi' + 'mov edx, [ebp+0x00]'), wiec patch dziala dla obu.
        /// </summary>
        public static readonly GameTarget[] KnownGames =
        {
            new GameTarget("MMH55_64", "MMH55_64.exe", 0x5FBCE3),
            new GameTarget("H5_Game",  "H5_Game.exe",  0x825BD8),
        };

        /// <summary>Nazwy procesow wszystkich obslugiwanych wersji gry (bez rozszerzenia .exe).</summary>
        public static readonly string[] ProcessNames =
            Array.ConvertAll(KnownGames, game => game.ProcessName);

        /// <summary>Nazwy modulow wszystkich obslugiwanych wersji gry (z rozszerzeniem .exe).</summary>
        public static readonly string[] ModuleNames =
            Array.ConvertAll(KnownGames, game => game.ModuleName);

        /// <summary>
        /// Dopasowuje obslugiwana wersje gry po nazwie procesu (bez uwzgledniania wielkosci liter).
        /// Zwraca null, jesli proces nie odpowiada zadnej znanej wersji.
        /// </summary>
        public static GameTarget? FromProcessName(string processName)
        {
            foreach (GameTarget game in KnownGames)
            {
                if (string.Equals(game.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
                    return game;
            }

            return null;
        }
    }
}