namespace DiscordQuestRunner
{
    public partial class App : Application
    {
        private readonly IServiceProvider _serviceProvider;

        public App(IServiceProvider serviceProvider)
        {
            InitializeComponent();
            _serviceProvider = serviceProvider;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var mainPage = _serviceProvider.GetRequiredService<MainPage>();
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

            // Hook into the window creation event to center it
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
