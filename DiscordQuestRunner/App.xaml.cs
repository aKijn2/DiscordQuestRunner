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
            
            const int newWidth = 600;
            const int newHeight = 450;

            window.Width = newWidth;
            window.Height = newHeight;
            window.MinimumWidth = newWidth;
            window.MinimumHeight = newHeight;
            
            return window;
        }
    }
}