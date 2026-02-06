using DiscordQuestRunner.Services;

namespace DiscordQuestRunner
{
    public partial class MainPage : ContentPage
    {
        private readonly DiscordService _discordService;
        private bool _isRunning;

        public MainPage(DiscordService discordService)
        {
            InitializeComponent();
            _discordService = discordService;
        }

        private void OnOpenDeleterClicked(object sender, EventArgs e)
        {
#if WINDOWS
            var deleterWindow = new Window
            {
                Page = new Pages.DeleterPage(_discordService),
                Title = "Discord Message Deleter",
                Width = 550,
                Height = 650,
                MinimumWidth = 450,
                MinimumHeight = 550
            };
            Application.Current?.OpenWindow(deleterWindow);
#else
            DisplayAlert("Error", "This feature only works on Windows.", "OK");
#endif
        }

        private async void OnCopyLogClicked(object sender, EventArgs e)
        {
            await Clipboard.SetTextAsync(StatusLbl.Text);
            await DisplayAlert("Copied", "Log copied to clipboard.", "OK");
        }

        private async void OnRunClicked(object sender, EventArgs e)
        {
#if WINDOWS
            if (_isRunning) return;
            _isRunning = true;
            RunBtn.IsEnabled = false;
            RunBtn.Text = "RUNNING...";
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;

            try
            {
                void Log(string msg) => MainThread.BeginInvokeOnMainThread(() =>
                {
                    StatusLbl.Text += $"\n{msg}";
                    LogScroll.ScrollToAsync(StatusLbl, ScrollToPosition.End, true);
                });

                StatusLbl.Text = "Initializing sequence...";
                Log("Checking Discord process...");

                // 1. Check if debug port is available
                var portCheck = await _discordService.CheckDebugPortAsync();

                if (!portCheck.isReady)
                {
                    Log($"WARNING: {portCheck.message}");
                    Log("INITIATING RESTART PROTOCOL...");

                    bool answer = await DisplayAlert("System Alert",
                        "Discord must be restarted in Debug Mode. Proceed?", "Yes", "No");

                    if (!answer)
                    {
                        Log("Aborted by user.");
                        return;
                    }

                    var restart = await _discordService.RestartDiscordAsync(Log);
                    if (!restart.success)
                    {
                        Log($"FATAL: {restart.message}");
                        return;
                    }

                    Log(restart.message);
                }
                else
                {
                    Log("Connection established with Discord.");
                }

                Log("Acquiring WebSocket URL...");

                // 2. Get WebSocket URL
                var connection = await _discordService.InitConnectionAsync();
                if (!connection.success)
                {
                    Log($"ERROR: {connection.message}");
                    return;
                }

                Log(connection.message);
                Log("Injecting payload...");

                // 3. Load and execute script from resource file
                string script = await DiscordService.LoadScriptAsync("quest_runner.js");
                await _discordService.ExecuteScriptAsync(connection.wsUrl, script, (msg) =>
                {
                    Log("SCRIPT: " + msg);
                });

                Log("Payload delivered successfully.");
                Log("Monitoring background tasks...");
            }
            catch (Exception ex)
            {
                await DisplayAlert("System Failure", ex.Message, "OK");
                StatusLbl.Text += $"\nCRITICAL FAILURE: {ex.Message}";
            }
            finally
            {
                _isRunning = false;
                RunBtn.IsEnabled = true;
                RunBtn.Text = "RUN AUTOMATION";
                LoadingIndicator.IsVisible = false;
                LoadingIndicator.IsRunning = false;
            }
#else
            await DisplayAlert("Error", "This automation only works on Windows.", "OK");
#endif
        }
    }
}
