using System;
using System.Diagnostics;

namespace Heroes5Trainer
{
    /// <summary>
    /// Obsluguje podpiecie do procesu gry oraz odczyt, zapis i alokacje jego pamieci.
    /// </summary>
    internal sealed class GameMemory : IDisposable
    {
        private nint processHandle;

        /// <summary>Proces gry, do ktorego jestesmy podpieci (null przed znalezieniem).</summary>
        public Process? GameProcess { get; private set; }

        /// <summary>
        /// Szuka uruchomionego procesu o podanej nazwie (bez rozszerzenia .exe).
        /// Zwraca true, jesli proces zostal znaleziony.
        /// </summary>
        public bool TryFindProcess(string processName)
        {
            Process[] matches = Process.GetProcessesByName(processName);
            if (matches.Length == 0)
                return false;

            GameProcess = matches[0];
            return true;
        }

        /// <summary>
        /// Otwiera znaleziony wczesniej proces z prawami do edycji pamieci.
        /// Zwraca false, jesli zabraklo uprawnien (program nalezy uruchomic jako Administrator).
        /// </summary>
        public bool OpenForEditing()
        {
            if (GameProcess is null)
                throw new InvalidOperationException("Najpierw nalezy znalezc proces gry (TryFindProcess).");

            const int desiredAccess = NativeMethods.PROCESS_VM_OPERATION
                                    | NativeMethods.PROCESS_VM_READ
                                    | NativeMethods.PROCESS_VM_WRITE
                                    | NativeMethods.PROCESS_QUERY_INFORMATION;

            processHandle = NativeMethods.OpenProcess(desiredAccess, false, GameProcess.Id);
            return processHandle != 0;
        }

        /// <summary>
        /// Zamienia zapis "Modul.exe+offset" na realny adres w pamieci procesu,
        /// dodajac offset do adresu bazowego wskazanego modulu.
        /// </summary>
        public nint ResolveAddress(string moduleName, int offset)
        {
            if (GameProcess is null)
                throw new InvalidOperationException("Brak podpiecia do procesu gry.");

            foreach (ProcessModule module in GameProcess.Modules)
            {
                if (string.Equals(module.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
                    return module.BaseAddress + offset;
            }

            throw new InvalidOperationException($"Nie znaleziono modulu '{moduleName}' w procesie gry.");
        }

        /// <summary>Odczytuje <paramref name="count"/> bajtow spod podanego adresu.</summary>
        public byte[] ReadBytes(nint address, int count)
        {
            byte[] buffer = new byte[count];
            if (!NativeMethods.ReadProcessMemory(processHandle, address, buffer, count, out nint read)
                || read != count)
            {
                throw new InvalidOperationException($"Odczyt pamieci spod adresu 0x{address:X} nie powiodl sie.");
            }

            return buffer;
        }

        /// <summary>
        /// Zapisuje bajty pod podany adres. Strona pamieci jest na czas zapisu
        /// przelaczana w tryb zapisywalny, a nastepnie przywracana.
        /// </summary>
        public void WriteBytes(nint address, byte[] data)
        {
            if (!NativeMethods.VirtualProtectEx(
                    processHandle, address, data.Length, NativeMethods.PAGE_EXECUTE_READWRITE, out int oldProtect))
            {
                throw new InvalidOperationException(
                    $"Zmiana ochrony pamieci pod adresem 0x{address:X} nie powiodla sie.");
            }

            bool ok = NativeMethods.WriteProcessMemory(processHandle, address, data, data.Length, out nint written);

            // Przywracamy pierwotna ochrone strony niezaleznie od wyniku zapisu.
            NativeMethods.VirtualProtectEx(processHandle, address, data.Length, oldProtect, out _);

            if (!ok || written != data.Length)
                throw new InvalidOperationException($"Zapis pamieci pod adresem 0x{address:X} nie powiodl sie.");
        }

        /// <summary>Alokuje w procesie gry blok wykonywalnej pamieci na "code cave".</summary>
        public nint Allocate(int size)
        {
            nint address = NativeMethods.VirtualAllocEx(
                processHandle, 0, size,
                NativeMethods.MEM_COMMIT | NativeMethods.MEM_RESERVE,
                NativeMethods.PAGE_EXECUTE_READWRITE);

            if (address == 0)
                throw new InvalidOperationException("Alokacja pamieci w procesie gry nie powiodla sie.");

            return address;
        }

        /// <summary>Zwalnia wczesniej zaalokowany blok pamieci.</summary>
        public void Free(nint address)
        {
            if (address != 0)
                NativeMethods.VirtualFreeEx(processHandle, address, 0, NativeMethods.MEM_RELEASE);
        }

        public void Dispose()
        {
            if (processHandle != 0)
            {
                NativeMethods.CloseHandle(processHandle);
                processHandle = 0;
            }

            GameProcess?.Dispose();
            GameProcess = null;
        }
    }
}