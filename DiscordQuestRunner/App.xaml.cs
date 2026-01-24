namespace DiscordQuestRunner
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new MainPage());
            
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