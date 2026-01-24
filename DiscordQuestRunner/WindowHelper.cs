using System.Runtime.InteropServices;

namespace DiscordQuestRunner
{
    public static class WindowHelper
    {
        // Leihoen kudeaketa
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        public static void FocusWindow(IntPtr handle)
        {
            ShowWindow(handle, SW_RESTORE);
            SetForegroundWindow(handle);
        }
    }
}
