using Android.App;
using Android.Runtime;

namespace DiscordQuestRunner
{
    /// <summary>
    /// Creates the MAUI application host for Android process startup.
    /// </summary>
    [Application]
    public class MainApplication : MauiApplication
    {
        /// <summary>
        /// Initializes the Android application wrapper.
        /// </summary>
        /// <param name="handle">Native Android runtime handle.</param>
        /// <param name="ownership">Ownership semantics for the native handle.</param>
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
        }

        /// <summary>
        /// Builds the shared MAUI application instance.
        /// </summary>
        /// <returns>The configured <see cref="MauiApp"/>.</returns>
        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}
