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
            var window = new Window(mainPage);
            
            const int defaultWidth = 500;
            const int defaultHeight = 700;

            window.Width = defaultWidth;
            window.Height = defaultHeight;
            window.MinimumWidth = 400;
            window.MinimumHeight = 500;
            
            return window;
        }
    }
}