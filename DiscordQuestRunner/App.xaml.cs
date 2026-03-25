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
                Title = "Discord Quest Runner",

                // Pin all dimensions to the exact same size
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
