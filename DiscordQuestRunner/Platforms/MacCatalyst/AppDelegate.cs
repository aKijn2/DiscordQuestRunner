using Foundation;

namespace DiscordQuestRunner
{
    /// <summary>
    /// Creates the MAUI application host for Mac Catalyst startup.
    /// </summary>
    [Register("AppDelegate")]
    public class AppDelegate : MauiUIApplicationDelegate
    {
        /// <summary>
        /// Builds the shared MAUI application instance.
        /// </summary>
        /// <returns>The configured <see cref="MauiApp"/>.</returns>
        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}
