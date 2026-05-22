using System;
using System.Runtime.InteropServices;

namespace Heroes5Trainer
{
    /// <summary>
    /// Deklaracje funkcji WinAPI uzywanych do operacji na pamieci innego procesu.
    /// Zastepuja brakujaca biblioteke "Memory.dll", do ktorej odwolywal sie kod.
    /// </summary>
    internal static class NativeMethods
    {
        // Prawa dostepu przekazywane do OpenProcess (laczone bitowo).
        public const int PROCESS_VM_OPERATION = 0x0008;
        public const int PROCESS_VM_READ = 0x0010;
        public const int PROCESS_VM_WRITE = 0x0020;
        public const int PROCESS_QUERY_INFORMATION = 0x0400;

        // Flagi alokacji pamieci dla VirtualAllocEx / VirtualFreeEx.
        public const int MEM_COMMIT = 0x1000;
        public const int MEM_RESERVE = 0x2000;
        public const int MEM_RELEASE = 0x8000;

        // Ochrona stron pamieci (zapis + wykonywanie kodu).
        public const int PAGE_EXECUTE_READWRITE = 0x40;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern nint OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(nint hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadProcessMemory(
            nint hProcess, nint lpBaseAddress, byte[] lpBuffer, int nSize, out nint lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WriteProcessMemory(
            nint hProcess, nint lpBaseAddress, byte[] lpBuffer, int nSize, out nint lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern nint VirtualAllocEx(
            nint hProcess, nint lpAddress, int dwSize, int flAllocationType, int flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool VirtualFreeEx(nint hProcess, nint lpAddress, int dwSize, int dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool VirtualProtectEx(
            nint hProcess, nint lpAddress, int dwSize, int flNewProtect, out int lpflOldProtect);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);
    }
}