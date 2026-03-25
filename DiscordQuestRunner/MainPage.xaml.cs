using DiscordQuestRunner.Services;

namespace DiscordQuestRunner
{
    public partial class MainPage : ContentPage
    {
        private readonly DiscordService _discordService;
        private bool _isRunning;
        private TaskCompletionSource<bool> _alertTcs; // Added for custom alerts

        public MainPage(DiscordService discordService)
        {
            InitializeComponent();
            _discordService = discordService;
        }

        // --- CUSTOM ALERT LOGIC ---
        private async Task<bool> ShowNexusAlertAsync(string title, string message, string confirmText, string cancelText = null)
        {
            AlertTitleLbl.Text = title.ToUpper();
            AlertMessageLbl.Text = message;
            AlertConfirmBtn.Text = confirmText.ToUpper();

            if (string.IsNullOrEmpty(cancelText))
            {
                AlertCancelBtn.IsVisible = false;
                Grid.SetColumnSpan(AlertConfirmBtn, 2); // Make confirm button take full width
            }
            else
            {
                AlertCancelBtn.IsVisible = true;
                AlertCancelBtn.Text = cancelText.ToUpper();
                Grid.SetColumnSpan(AlertConfirmBtn, 1); // Reset column span
            }

            // Fade in animation
            ModalOverlay.IsVisible = true;
            await ModalOverlay.FadeTo(1, 200, Easing.CubicOut);

            _alertTcs = new TaskCompletionSource<bool>();
            return await _alertTcs.Task;
        }

        private async void OnAlertConfirmClicked(object sender, EventArgs e)
        {
            await ModalOverlay.FadeTo(0, 150, Easing.CubicIn);
            ModalOverlay.IsVisible = false;
            _alertTcs?.TrySetResult(true);
        }

        private async void OnAlertCancelClicked(object sender, EventArgs e)
        {
            await ModalOverlay.FadeTo(0, 150, Easing.CubicIn);
            ModalOverlay.IsVisible = false;
            _alertTcs?.TrySetResult(false);
        }
        // --------------------------

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
            // Updated to use custom alert
            _ = ShowNexusAlertAsync("SYSTEM ERROR", "This feature only works on Windows architecture.", "ACKNOWLEDGE");
#endif
        }

        private async void OnCopyLogClicked(object sender, EventArgs e)
        {
            await Clipboard.SetTextAsync(StatusLbl.Text);
            // Updated to use custom alert
            await ShowNexusAlertAsync("DATA EXPORTED", "Runtime log copied to system clipboard.", "OK");
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
                void Log(string msg) => MainThread.BeginInvokeOnMainThread(async () =>
                {
                    StatusLbl.Text += $"\n{msg}";
                    await LogScroll.ScrollToAsync(StatusLbl, ScrollToPosition.End, true);
                });

                StatusLbl.Text = "Initializing sequence...";
                Log("Checking Discord process...");

                var portCheck = await _discordService.CheckDebugPortAsync();

                if (!portCheck.isReady)
                {
                    Log($"WARNING: {portCheck.message}");
                    Log("INITIATING RESTART PROTOCOL...");

                    // Updated to use custom alert
                    bool answer = await ShowNexusAlertAsync(
                        "RESTART REQUIRED", 
                        "Discord must be restarted in Debug Mode. Proceed with protocol?", 
                        "AUTHORIZE", 
                        "ABORT");

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

                var connection = await _discordService.InitConnectionAsync();
                if (!connection.success)
                {
                    Log($"ERROR: {connection.message}");
                    return;
                }

                Log(connection.message);
                Log("Injecting payload...");

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
                // Updated to use custom alert
                await ShowNexusAlertAsync("CRITICAL FAILURE", ex.Message, "CLOSE");
                StatusLbl.Text += $"\nCRITICAL FAILURE: {ex.Message}";
            }
            finally
            {
                _isRunning = false;
                RunBtn.IsEnabled = true;
                RunBtn.Text = "INITIALIZE QUESTS";
                LoadingIndicator.IsVisible = false;
                LoadingIndicator.IsRunning = false;
            }
#else
            await ShowNexusAlertAsync("SYSTEM ERROR", "This automation only works on Windows architecture.", "ACKNOWLEDGE");
#endif
        }
    }
}