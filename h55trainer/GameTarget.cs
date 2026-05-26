using System;

namespace Heroes5Trainer
{
    /// <summary>
    /// Lancuch dereferencji w pamieci gry, w konwencji eksportu Cheat Engine.
    /// Resolve: p = [module + BaseOffset]; potem dla kazdego derefOffset (w kolejnosci Derefs[0..n-1])
    /// wykonujemy p = [p + derefOffset]; na koniec final_address = p + FinalOffset (bez dereferencji).
    ///
    /// Uwaga na konwencje CE: w XML pierwszy <Offset> to FinalOffset (stosowany jako ostatni),
    /// a kolejne odpowiadaja Derefs w odwrotnej kolejnosci. Tutaj zapisujemy je juz w kolejnosci stosowania.
    /// </summary>
    internal sealed record PointerPath(int BaseOffset, int[] Derefs, int FinalOffset);


    /// <summary>
    /// Opisuje pojedyncza obslugiwana wersje gry: nazwe procesu, nazwe modulu
    /// oraz offset instrukcji zdobywania XP wewnatrz tego modulu.
    /// </summary>
    /// <param name="ProcessName">Nazwa procesu bez rozszerzenia .exe (do wyszukiwania procesu).</param>
    /// <param name="ModuleName">Nazwa modulu z rozszerzeniem .exe (do wyliczenia adresu bazowego).</param>
    /// <param name="CodeOffset">Offset instrukcji 'mov [edi+0x60], esi' wzgledem bazy modulu.</param>
    /// <param name="ActiveHeroHookOffset">
    /// Offset instrukcji 'mov ecx, [esi+04]' wykonywanej setki razy/s, gdzie ECX zawiera
    /// wskaznik na aktualnie wybranego bohatera. Sluzy do prewencyjnej modyfikacji statow.
    /// Wartosc 0 oznacza "nieskonfigurowane dla tej wersji gry" - ActiveHeroTracker rzuci wyjatek.
    /// </param>
    /// <param name="ResourcesPath">
    /// Sciezka wskaznikow Cheat Engine do pola "gold" aktywnego gracza. null gdy nieznana
    /// dla danej wersji gry - akcje na zasobach beda wtedy zglaszac blad.
    /// </param>
    internal sealed record GameTarget(
        string ProcessName,
        string ModuleName,
        int CodeOffset,
        int ActiveHeroHookOffset,
        PointerPath? ResourcesPath = null)
    {
        /// <summary>
        /// Wszystkie wersje gry, ktore trainer potrafi rozpoznac i zmodyfikowac.
        /// Offsety pochodza z Cheat Engine; instrukcja pod kazdym z nich jest identyczna
        /// ('mov [edi+0x60], esi' + 'mov edx, [ebp+0x00]'), wiec patch dziala dla obu.
        /// </summary>
        public static readonly GameTarget[] KnownGames =
        {
            new GameTarget("MMH55_64", "MMH55_64.exe", 0x5FBCE3, 0x2A8E74,
                // Pointer scan z CE: [exe+009651F8] -> +0x340 -> +0x10 -> +0x0 -> +0xEC -> +0x8 -> +0x3C,
                // a na koncu +0x54 = adres pola "gold" w strukturze gracza. Pole leży pod sama tablica
                // 7 zasobow (drewno..gold), wiec drewno = gold - 0x18.
                ResourcesPath: new PointerPath(
                    BaseOffset: 0x009651F8,
                    Derefs:     new[] { 0x340, 0x10, 0x0, 0xEC, 0x8, 0x3C },
                    FinalOffset: 0x54)),
            // TODO: znajdz w CE analogiczna instrukcje 'mov reg, [reg+04]' z hero ptr dla H5_Game.exe
            // TODO: pointer scan do gold dla H5_Game.exe (jezeli adresy sa rozne).
            new GameTarget("H5_Game",  "H5_Game.exe",  0x825BD8, 0),
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