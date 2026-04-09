namespace DiscordQuestRunner
{
    /// <summary>
    /// Configures the root MAUI window and resolves the startup page from dependency injection.
    /// </summary>
    public partial class App : Application
    {
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// Initializes the MAUI application shell.
        /// </summary>
        /// <param name="serviceProvider">Service provider used to resolve page dependencies.</param>
        public App(IServiceProvider serviceProvider)
        {
            InitializeComponent();
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Creates the primary application window and centers it on the active display.
        /// </summary>
        /// <param name="activationState">Platform-specific activation context.</param>
        /// <returns>The configured main application window.</returns>
        protected override Window CreateWindow(IActivationState? activationState)
        {
            var mainPage = _serviceProvider.GetRequiredService<Pages.QuestRunnerPage>();
            var navPage = new NavigationPage(mainPage);

            var window = new Window(navPage)
            {
                Title = "Discord Quest Runner",
                Width = 500,
                Height = 700,
                MinimumWidth = 500,
                MaximumWidth = 500,
                MinimumHeight = 700,
                MaximumHeight = 700,
            };

            window.Created += (s, e) =>
            {
                Application.Current?.Dispatcher.Dispatch(() =>
                {
                    var displayInfo = DeviceDisplay.Current.MainDisplayInfo;
                    window.X = (displayInfo.Width / displayInfo.Density - window.Width) / 2;
                    window.Y = (displayInfo.Height / displayInfo.Density - window.Height) / 2;
                });
            };

            return window;
        }
    }
}
