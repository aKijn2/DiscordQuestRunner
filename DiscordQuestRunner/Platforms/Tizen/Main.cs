using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace DiscordQuestRunner
{
    /// <summary>
    /// Provides the native Tizen process entry point.
    /// </summary>
    internal class Program : MauiApplication
    {
        /// <summary>
        /// Builds the shared MAUI application instance.
        /// </summary>
        /// <returns>The configured <see cref="MauiApp"/>.</returns>
        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        /// <summary>
        /// Starts the MAUI application on Tizen.
        /// </summary>
        /// <param name="args">Process arguments supplied by the Tizen host.</param>
        public static void Main(string[] args)
        {
            var app = new Program();
            app.Run(args);
        }
    }
}
