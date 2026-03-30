using DiscordQuestRunner.Services;

namespace DiscordQuestRunner
{
    public partial class MainPage : ContentPage
    {
        // ── State ─────────────────────────────────────────────────────────────
        private readonly DiscordService _discordService;
        private bool _isRunning;
        private TaskCompletionSource<bool>? _alertTcs;

        // ── Constructor ───────────────────────────────────────────────────────
        public MainPage(DiscordService discordService)
        {
            InitializeComponent();
            _discordService = discordService;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Custom alert modal
        // ══════════════════════════════════════════════════════════════════════

        private async Task<bool> ShowNexusAlertAsync(
            string title,
            string message,
            string confirmText,
            string? cancelText = null)
        {
            AlertTitleLbl.Text = title.ToUpper();
            AlertMessageLbl.Text = message;
            AlertConfirmBtn.Text = confirmText.ToUpper();

            bool hasCancel = !string.IsNullOrEmpty(cancelText);
            AlertCancelBtn.IsVisible = hasCancel;
            AlertCancelBtn.Text = hasCancel ? cancelText!.ToUpper() : string.Empty;
            Grid.SetColumnSpan(AlertConfirmBtn, hasCancel ? 1 : 2);

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

        // ══════════════════════════════════════════════════════════════════════
        //  Logging helpers
        // ══════════════════════════════════════════════════════════════════════

        private int _lineCount = 3; // matches the initial placeholder lines

        /// <summary>Appends a line to the log and scrolls to bottom.</summary>
        private void Log(string message, string prefix = "") =>
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                string line = string.IsNullOrEmpty(prefix) ? message : $"{prefix}: {message}";
                StatusLbl.Text += $"\n{line}";
                _lineCount++;
                UpdateLineCount();
                await LogScroll.ScrollToAsync(StatusLbl, ScrollToPosition.End, animated: true);
            });

        private void ResetLog(string firstLine)
        {
            StatusLbl.Text = firstLine;
            _lineCount = 1;
            UpdateLineCount();
        }

        private void UpdateLineCount()
        {
            // LineCountLbl is defined in the improved XAML; guard for old XAML compatibility
            if (LineCountLbl is not null)
                LineCountLbl.Text = $"{_lineCount} lines";
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Button handlers
        // ══════════════════════════════════════════════════════════════════════

        private async void OnOpenDeleterClicked(object sender, EventArgs e)
        {
#if WINDOWS
            await Navigation.PushAsync(new Pages.DeleterPage(_discordService));
#else
            await ShowNexusAlertAsync(
                "SYSTEM ERROR",
                "This feature only works on Windows architecture.",
                "ACKNOWLEDGE");
#endif
        }

        private async void OnCopyLogClicked(object sender, EventArgs e)
        {
            await Clipboard.SetTextAsync(StatusLbl.Text);
            await ShowNexusAlertAsync(
                "DATA EXPORTED",
                "Runtime log copied to system clipboard.",
                "OK");
        }

        private async void OnRunClicked(object sender, EventArgs e)
        {
#if WINDOWS
            if (_isRunning) return;
            SetRunningState(true);

            try
            {
                ResetLog("[SYS] Initializing sequence...");
                Log("Checking Discord process...");

                // ── 1. Health check ────────────────────────────────────────
                var portCheck = await _discordService.CheckDebugPortAsync();

                if (!portCheck.isReady)
                {
                    Log($"WARNING: {portCheck.message}");

                    bool proceed = await ShowNexusAlertAsync(
                        "RESTART REQUIRED",
                        "Discord must be restarted in Debug Mode to continue. Authorize restart?",
                        "AUTHORIZE",
                        "ABORT");

                    if (!proceed)
                    {
                        Log("Operation aborted by user.");
                        return;
                    }

                    Log("Initiating restart protocol...");
                    var restart = await _discordService.RestartDiscordAsync(
                        msg => Log(msg, "[SYS]"));

                    if (!restart.success)
                    {
                        Log($"FATAL: {restart.message}");
                        await ShowNexusAlertAsync("RESTART FAILED", restart.message, "CLOSE");
                        return;
                    }

                    Log(restart.message);
                }
                else
                {
                    Log("Connection established with Discord.");
                }

                // ── 2. Resolve CDP target ──────────────────────────────────
                Log("Acquiring WebSocket target...");
                var connection = await _discordService.InitConnectionAsync();

                if (!connection.success)
                {
                    Log($"ERROR: {connection.message}");
                    await ShowNexusAlertAsync("TARGET ERROR", connection.message, "CLOSE");
                    return;
                }

                Log(connection.message);

                // ── 3. Auto-accept ─────────────────────────────────────────
                if (AutoAcceptSwitch.IsToggled)
                {
                    Log("Injecting Auto-Accept payload...");
                    string autoScript = await DiscordService.LoadScriptWithDebugBannerAsync(
                        "auto_accept_v2.js");

                    await _discordService.ExecuteScriptAsync(
                        connection.wsUrl,
                        autoScript,
                        msg => Log(msg, "AUTO"));

                    await Task.Delay(500);
                    Log("Auto-Accept sequence completed.");
                }

                // ── 4. Quest runner ────────────────────────────────────────
                Log("Injecting Quest Runner payload...");
                string questScript = await DiscordService.LoadScriptWithDebugBannerAsync(
                    "quest_runner_v2.js");

                await _discordService.ExecuteScriptAsync(
                    connection.wsUrl,
                    questScript,
                    msg => Log(msg, "SCRIPT"));

                Log("Payload delivered. Monitoring background tasks...");
            }
            catch (Exception ex)
            {
                Log($"CRITICAL FAILURE: {ex.Message}");
                await ShowNexusAlertAsync("CRITICAL FAILURE", ex.Message, "CLOSE");
            }
            finally
            {
                SetRunningState(false);
            }
#else
            await ShowNexusAlertAsync(
                "SYSTEM ERROR",
                "This automation only works on Windows architecture.",
                "ACKNOWLEDGE");
#endif
        }

        // ══════════════════════════════════════════════════════════════════════
        //  UI state helpers
        // ══════════════════════════════════════════════════════════════════════

        private void SetRunningState(bool running)
        {
            _isRunning = running;
            RunBtn.IsEnabled = !running;
            RunBtn.Text = running ? "RUNNING..." : "INITIALIZE QUESTS";
            LoadingIndicator.IsVisible = running;
            LoadingIndicator.IsRunning = running;

            // Update status badge if the new XAML labels are present
            if (StatusBadgeLbl is not null)
                StatusBadgeLbl.Text = running ? "RUNNING" : "READY";
        }
    }
}