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
            var window = new Window(mainPage)
            {
                Title = "Discord Quest Runner", // Adds a clean title to the window
                Width = 500,
                Height = 700,
                MinimumWidth = 450, // Slightly wider minimum so our UI never squishes
                MinimumHeight = 600
            };
            
            return window;
        }
    }
}