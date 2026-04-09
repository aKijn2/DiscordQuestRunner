using DiscordQuestRunner.Services;

namespace DiscordQuestRunner.Pages
{
    public partial class QuestRunnerPage : ContentPage
    {
        // State
        private readonly DiscordService _discordService;
        private bool _isRunning;
        private CancellationTokenSource? _cts;
        private TaskCompletionSource<bool>? _alertTcs;

        // Quest runner retry configuration
        private const int MAX_RETRIES = 40;   // max cycles before giving up
        private const int RETRY_DELAY_MS = 4000; // wait between script re-injections
        private const int POST_ACCEPT_MS = 800;  // settle time after auto-accept

        // Strings that mean "all done, stop looping"
        private static readonly string[] _terminalPhrases =
        [
            "No uncompleted quests found",
            "No new valid quests",
            "All quests completed",
            "Quest reward claimed",
            "All jobs done",
        ];

        // Constructor
        public QuestRunnerPage(DiscordService discordService)
        {
            InitializeComponent();
            _discordService = discordService;
        }

        //  Custom alert modal
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

        //  Logging helpers
        private int _lineCount = 3;

        private void Log(string message, string prefix = "") =>
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                string line = string.IsNullOrEmpty(prefix) ? message : $"[{prefix}] {message}";
                StatusLbl.Text += $"\n{line}";
                _lineCount++;
                if (LineCountLbl is not null)
                    LineCountLbl.Text = $"{_lineCount} lines";
                await LogScroll.ScrollToAsync(StatusLbl, ScrollToPosition.End, animated: true);
            });

        private void ResetLog(string firstLine) =>
            MainThread.BeginInvokeOnMainThread(() =>
            {
                StatusLbl.Text = firstLine;
                _lineCount = 1;
                if (LineCountLbl is not null)
                    LineCountLbl.Text = "1 line";
            });

        //  Button handlers
        private async void OnOpenDeleterClicked(object sender, EventArgs e)
        {
#if WINDOWS
            await Navigation.PushAsync(new MessagePurgePage(_discordService));
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
            await ShowNexusAlertAsync("DATA EXPORTED", "Runtime log copied to system clipboard.", "OK");
        }

        private async void OnRunClicked(object sender, EventArgs e)
        {
#if WINDOWS
            if (_isRunning) return;

            _cts = new CancellationTokenSource();
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
                        msg => Log(msg, "SYS"));

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

                // ── 3. Auto-accept (single pass, no retry needed) ──────────
                if (AutoAcceptSwitch.IsToggled)
                {
                    Log("Injecting Auto-Accept payload...");
                    string autoScript = await DiscordService.LoadScriptWithDebugBannerAsync(
                        "auto_accept_v2.js");

                    await _discordService.ExecuteScriptAsync(
                        connection.wsUrl,
                        autoScript,
                        msg => Log(msg, "AUTO"),
                        _cts.Token);

                    await Task.Delay(POST_ACCEPT_MS, _cts.Token);
                    Log("Auto-Accept sequence completed.");
                }

                // ── 4. Quest runner — auto retry loop ─────────────────────
                Log("Loading Quest Runner script...");
                string questScript = await DiscordService.LoadScriptWithDebugBannerAsync(
                    "quest_runner_v2.js");

                await RunQuestLoopAsync(connection.wsUrl, questScript, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                Log("Run cancelled by user.", "SYS");
            }
            catch (Exception ex)
            {
                Log($"CRITICAL FAILURE: {ex.Message}");
                await ShowNexusAlertAsync("CRITICAL FAILURE", ex.Message, "CLOSE");
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                SetRunningState(false);
            }
#else
            await ShowNexusAlertAsync(
                "SYSTEM ERROR",
                "This automation only works on Windows architecture.",
                "ACKNOWLEDGE");
#endif
        }

        //  Quest runner retry loop
        //  Discord's video quest only advances ~14 % per script run because the
        //  JS payload returns after one progress tick. We keep re-injecting on

        private async Task RunQuestLoopAsync(
            string wsUrl,
            string script,
            CancellationToken ct)
        {
            int attempt = 0;
            bool allComplete = false;

            while (!allComplete && attempt < MAX_RETRIES && !ct.IsCancellationRequested)
            {
                attempt++;
                Log($"Cycle {attempt}/{MAX_RETRIES}", "SCRIPT");

                // Collect all output lines produced during this execution
                var outputLines = new List<string>();

                var scriptResult = await _discordService.ExecuteScriptAsync(
                    wsUrl,
                    script,
                    msg =>
                    {
                        outputLines.Add(msg);
                        Log(msg, "SCRIPT");
                    },
                    ct);

                // Use the returned scriptOutput instead of all console messages to avoid false positives 
                // from DevTools replaying old console logs.
                allComplete = _terminalPhrases.Any(phrase =>
                    scriptResult.Output?.Contains(phrase, StringComparison.OrdinalIgnoreCase) == true);

                if (allComplete)
                {
                    Log("All quests processed. Sequence complete.", "SYS");
                    break;
                }

                // Not done yet — wait then re-resolve WebSocket URL before
                // next cycle (Discord can rotate its CDP target mid-session)
                if (attempt < MAX_RETRIES && !ct.IsCancellationRequested)
                {
                    Log($"Still in progress. Next cycle in {RETRY_DELAY_MS / 1000}s...", "SYS");
                    await Task.Delay(RETRY_DELAY_MS, ct);

                    var recheck = await _discordService.InitConnectionAsync();
                    if (recheck.success && recheck.wsUrl != wsUrl)
                    {
                        wsUrl = recheck.wsUrl;
                        Log("WebSocket target refreshed.", "SYS");
                    }
                }
            }

            if (!allComplete && attempt >= MAX_RETRIES)
                Log($"Max retry limit ({MAX_RETRIES}) reached. Check Discord manually.", "WARN");
        }

        //  UI state

        private void SetRunningState(bool running)
        {
            _isRunning = running;
            RunBtn.IsEnabled = !running;
            RunBtn.Text = running ? "RUNNING..." : "INITIALIZE QUESTS";
            LoadingIndicator.IsVisible = running;
            LoadingIndicator.IsRunning = running;

            if (StatusBadgeLbl is not null)
                StatusBadgeLbl.Text = running ? "RUNNING" : "READY";
        }
    }
}
