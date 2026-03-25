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

            // Wrap the main page in a NavigationPage
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

            return window;
        }
    }
}
