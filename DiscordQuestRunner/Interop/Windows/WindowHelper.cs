using System.Runtime.InteropServices;

namespace DiscordQuestRunner
{
    /// <summary>
    /// Exposes the Win32 calls required to restore and foreground the Discord window during captcha automation.
    /// </summary>
    public static class WindowHelper
    {
        private const int SwRestore = 9;

        /// <summary>
        /// Brings the specified top-level window to the foreground.
        /// </summary>
        /// <param name="hWnd">Handle of the target window.</param>
        /// <returns>
        /// <see langword="true"/> when the request was accepted by the window manager; otherwise, <see langword="false"/>.
        /// </returns>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>
        /// Updates the show state of the specified window.
        /// </summary>
        /// <param name="hWnd">Handle of the target window.</param>
        /// <param name="nCmdShow">Window show command passed to User32.</param>
        /// <returns>
        /// <see langword="true"/> when the window was previously visible; otherwise, <see langword="false"/>.
        /// </returns>
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        /// <summary>
        /// Restores a minimized window and requests focus so CDP-driven mouse input lands on the expected Discord surface.
        /// </summary>
        /// <param name="handle">Handle of the Discord main window.</param>
        public static void FocusWindow(IntPtr handle)
        {
            ShowWindow(handle, SwRestore);
            SetForegroundWindow(handle);
        }
    }
}
