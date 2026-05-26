using System;

namespace Heroes5Trainer
{
    /// <summary>
    /// Rodzaje zasobow w kolejnosci, w jakiej leza w pamieci gry jako tablica 7 x int32.
    /// Wartosci enum'a sluza zarazem za indeks w tablicy (offset = index * 4 od bazy tablicy).
    /// </summary>
    internal enum ResourceKind
    {
        Wood    = 0,
        Ore     = 1,
        Mercury = 2,
        Sulfur  = 3,
        Crystal = 4,
        Gems    = 5,
        Gold    = 6,
    }

    /// <summary>
    /// Odczyt i zapis zasobow aktywnego gracza poprzez statyczna sciezke wskaznikow
    /// (PointerPath z GameTarget). Sciezka kończy się na polu "gold" w strukturze gracza;
    /// reszta zasobow lezy bezposrednio przed nim jako ciagla tablica 7 x int32.
    /// </summary>
    internal static class ResourceState
    {
        // Rozmiar pojedynczego pola zasobu w pamieci gry.
        private const int ResourceFieldSize = sizeof(int);

        // Liczba zasobow w tablicy (drewno, ruda, rtec, siarka, krysztaly, klejnoty, zloto).
        private const int ResourceCount = 7;

        // Offset pola "gold" wzgledem poczatku tablicy zasobow.
        // Gold to ostatni element tablicy 7 x int32 -> (7-1) * 4 = 24 = 0x18.
        private const int GoldOffsetFromBase = (ResourceCount - 1) * ResourceFieldSize;

        /// <summary>
        /// Rozwiazuje sciezke wskaznikow do adresu pola "gold", a nastepnie cofa o 0x18
        /// by uzyskac adres calej tablicy zasobow (drewno @ +0x00, gold @ +0x18).
        /// Zwraca false gdy sciezka nie jest skonfigurowana albo ktorys deref dal NULL/0.
        /// </summary>
        public static bool TryResolveResourcesBase(GameMemory memory, GameTarget target, out nint baseAddress)
        {
            baseAddress = 0;
            if (target.ResourcesPath is null)
                return false;

            if (!TryResolveGoldAddress(memory, target, out nint goldAddr))
                return false;

            baseAddress = goldAddr - GoldOffsetFromBase;
            return true;
        }

        /// <summary>
        /// Rozwiazuje pelny lancuch wskaznikow do adresu pola "gold" aktywnego gracza.
        /// </summary>
        public static bool TryResolveGoldAddress(GameMemory memory, GameTarget target, out nint goldAddress)
        {
            goldAddress = 0;
            PointerPath? path = target.ResourcesPath;
            if (path is null)
                return false;

            try
            {
                nint ptr = memory.ResolveAddress(target.ModuleName, path.BaseOffset);
                ptr = ReadPointer(memory, ptr);
                if (ptr == 0) return false;

                foreach (int deref in path.Derefs)
                {
                    ptr = ReadPointer(memory, ptr + deref);
                    if (ptr == 0) return false;
                }

                goldAddress = ptr + path.FinalOffset;
                return true;
            }
            catch
            {
                // Niepoprawny wskaznik posrodku lancucha -> sciezka chwilowo nieaktywna
                // (np. gra w menu, brak wybranego gracza). Sygnalizujemy false.
                return false;
            }
        }

        /// <summary>Odczytuje aktualna wartosc zasobu z adresu w tablicy zasobow.</summary>
        public static int Read(GameMemory memory, nint resourcesBase, ResourceKind kind)
        {
            byte[] bytes = memory.ReadBytes(FieldAddress(resourcesBase, kind), ResourceFieldSize);
            return BitConverter.ToInt32(bytes, 0);
        }

        /// <summary>Zapisuje nowa wartosc zasobu pod odpowiednim offsetem w tablicy.</summary>
        public static void Write(GameMemory memory, nint resourcesBase, ResourceKind kind, int value)
        {
            memory.WriteBytes(FieldAddress(resourcesBase, kind), BitConverter.GetBytes(value));
        }

        /// <summary>
        /// Dodaje delta do biezacej wartosci zasobu z saturacja przy przepelnieniu int32.
        /// Zwraca pare (wartosc przed, wartosc po) - przydatna do logowania.
        /// </summary>
        public static (int Before, int After) Add(GameMemory memory, nint resourcesBase,
                                                  ResourceKind kind, int delta)
        {
            int before = Read(memory, resourcesBase, kind);
            int after = SaturatingAdd(before, delta);
            Write(memory, resourcesBase, kind, after);
            return (before, after);
        }

        private static nint FieldAddress(nint resourcesBase, ResourceKind kind)
        {
            return resourcesBase + (int)kind * ResourceFieldSize;
        }

        private static nint ReadPointer(GameMemory memory, nint address)
        {
            byte[] bytes = memory.ReadBytes(address, sizeof(int));
            // Gra 32-bitowa - wskazniki to uint32, bezpiecznie poszerzamy do nint.
            return (nint)(uint)BitConverter.ToInt32(bytes, 0);
        }

        private static int SaturatingAdd(int a, int b)
        {
            long sum = (long)a + b;
            if (sum > int.MaxValue) return int.MaxValue;
            if (sum < int.MinValue) return int.MinValue;
            return (int)sum;
        }
    }
}
