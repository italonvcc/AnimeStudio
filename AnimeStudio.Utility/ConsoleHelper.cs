using System;
using System.Runtime.InteropServices;

namespace AnimeStudio
{
    public static class ConsoleHelper
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AllocConsole();

        [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int handle);
        [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(IntPtr handle, out uint mode);
        [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(IntPtr handle, uint mode);
        public static void DisableQuickEdit()
        {
            var input = GetStdHandle(-10);
            if (GetConsoleMode(input, out uint mode)) SetConsoleMode(input, (mode | 0x80u) & ~0x40u);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetConsoleTitle([MarshalAs(UnmanagedType.LPUTF8Str)] string lpConsoleTitle);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public const int SW_HIDE = 0;
        public const int SW_SHOW = 5;
    }
}
