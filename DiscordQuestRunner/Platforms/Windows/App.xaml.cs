using Microsoft.UI.Xaml;

namespace DiscordQuestRunner.WinUI
{
    /// <summary>
    /// Hosts the MAUI application inside the WinUI bootstrapper process.
    /// </summary>
    public partial class App : MauiWinUIApplication
    {
        /// <summary>
        /// Initializes the WinUI application wrapper.
        /// </summary>
        public App()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Builds the shared MAUI application instance.
        /// </summary>
        /// <returns>The configured <see cref="MauiApp"/>.</returns>
        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}
