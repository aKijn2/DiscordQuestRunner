using DiscordQuestRunner.Services;
using DiscordQuestRunner.UI;

namespace DiscordQuestRunner.Pages
{
    public partial class QuestRunnerPage : ContentPage
    {
        private const int MaxRetries = 40;
        private const int RetryDelayMs = 4000;
        private const int PostAcceptDelayMs = 800;

        private static readonly string[] TerminalPhrases =
        [
            "No uncompleted quests found",
            "No new valid quests",
            "All quests completed",
            "Quest reward claimed",
            "All jobs done",
        ];

        private readonly DiscordService _discordService;
        private readonly LogConsoleController _logConsole;
        private readonly OverlayAlertController _alertController;

        private bool _isRunning;
        private CancellationTokenSource? _runCts;

        public QuestRunnerPage(DiscordService discordService)
        {
            InitializeComponent();

            _discordService = discordService;
            _logConsole = new LogConsoleController(StatusLbl, LogScroll, LineCountLbl);
            _alertController = new OverlayAlertController(
                ModalOverlay,
                AlertTitleLbl,
                AlertMessageLbl,
                AlertConfirmBtn,
                AlertCancelBtn);
        }

        private Task<bool> ShowNexusAlertAsync(
            string title,
            string message,
            string confirmText,
            string? cancelText = null) =>
            _alertController.ShowAsync(title, message, confirmText, cancelText);

        private void Log(string message, string prefix = "") =>
            _ = _logConsole.AppendLineAsync(message, prefix);

        private void ResetLog(string firstLine) =>
            _ = _logConsole.ResetAsync(firstLine);

        private async void OnAlertConfirmClicked(object sender, EventArgs e) =>
            await _alertController.ConfirmAsync();

        private async void OnAlertCancelClicked(object sender, EventArgs e) =>
            await _alertController.CancelAsync();

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
            await Clipboard.SetTextAsync(_logConsole.Text);
            await ShowNexusAlertAsync("DATA EXPORTED", "Runtime log copied to system clipboard.", "OK");
        }

        private async void OnRunClicked(object sender, EventArgs e)
        {
#if WINDOWS
            if (_isRunning)
            {
                return;
            }

            _runCts = new CancellationTokenSource();
            SetRunningState(true);

            try
            {
                ResetLog("[SYS] Initializing sequence...");

                var connection = await TryInitializeConnectionAsync(_runCts.Token);
                if (connection is null)
                {
                    return;
                }

                if (AutoAcceptSwitch.IsToggled)
                {
                    await RunAutoAcceptAsync(connection.Value.wsUrl, _runCts.Token);
                }

                await RunQuestLoopAsync(connection.Value.wsUrl, _runCts.Token);
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
                _runCts?.Dispose();
                _runCts = null;
                SetRunningState(false);
            }
#else
            await ShowNexusAlertAsync(
                "SYSTEM ERROR",
                "This automation only works on Windows architecture.",
                "ACKNOWLEDGE");
#endif
        }

        private async Task<(string wsUrl, string message)?> TryInitializeConnectionAsync(
            CancellationToken cancellationToken)
        {
            Log("Checking Discord process...");

            if (!await EnsureDebugPortAsync(cancellationToken))
            {
                return null;
            }

            Log("Acquiring WebSocket target...");
            var connection = await _discordService.InitConnectionAsync();

            if (!connection.success)
            {
                Log($"ERROR: {connection.message}");
                await ShowNexusAlertAsync("TARGET ERROR", connection.message, "CLOSE");
                return null;
            }

            Log(connection.message);
            return (connection.wsUrl, connection.message);
        }

        private async Task<bool> EnsureDebugPortAsync(CancellationToken cancellationToken)
        {
            var portCheck = await _discordService.CheckDebugPortAsync();

            if (portCheck.isReady)
            {
                Log("Connection established with Discord.");
                return true;
            }

            Log($"WARNING: {portCheck.message}");

            var proceed = await ShowNexusAlertAsync(
                "RESTART REQUIRED",
                "Discord must be restarted in Debug Mode to continue. Authorize restart?",
                "AUTHORIZE",
                "ABORT");

            if (!proceed)
            {
                Log("Operation aborted by user.");
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();

            Log("Initiating restart protocol...");
            var restart = await _discordService.RestartDiscordAsync(msg => Log(msg, "SYS"));

            if (!restart.success)
            {
                Log($"FATAL: {restart.message}");
                await ShowNexusAlertAsync("RESTART FAILED", restart.message, "CLOSE");
                return false;
            }

            Log(restart.message);
            return true;
        }

        private async Task RunAutoAcceptAsync(string wsUrl, CancellationToken cancellationToken)
        {
            Log("Injecting Auto-Accept payload...");

            var autoScript = await DiscordService.LoadScriptWithDebugBannerAsync(
                DiscordScriptCatalog.AutoAccept);

            await _discordService.ExecuteScriptAsync(
                wsUrl,
                autoScript,
                msg => Log(msg, "AUTO"),
                cancellationToken);

            await Task.Delay(PostAcceptDelayMs, cancellationToken);
            Log("Auto-Accept sequence completed.");
        }

        private async Task RunQuestLoopAsync(
            string wsUrl,
            CancellationToken cancellationToken)
        {
            Log("Loading Quest Runner script...");
            var questScript = await DiscordService.LoadScriptWithDebugBannerAsync(
                DiscordScriptCatalog.QuestRunner);

            var attempt = 0;

            while (attempt < MaxRetries && !cancellationToken.IsCancellationRequested)
            {
                attempt++;
                Log($"Cycle {attempt}/{MaxRetries}", "SCRIPT");

                var scriptResult = await _discordService.ExecuteScriptAsync(
                    wsUrl,
                    questScript,
                    msg => Log(msg, "SCRIPT"),
                    cancellationToken);

                if (DiscordScriptOutputParser.ContainsTerminalPhrase(
                    scriptResult.Output,
                    TerminalPhrases))
                {
                    Log("All quests processed. Sequence complete.", "SYS");
                    return;
                }

                if (attempt >= MaxRetries || cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                Log($"Still in progress. Next cycle in {RetryDelayMs / 1000}s...", "SYS");
                await Task.Delay(RetryDelayMs, cancellationToken);
                wsUrl = await RefreshWebSocketTargetAsync(wsUrl);
            }

            Log($"Max retry limit ({MaxRetries}) reached. Check Discord manually.", "WARN");
        }

        private async Task<string> RefreshWebSocketTargetAsync(string currentWsUrl)
        {
            var recheck = await _discordService.InitConnectionAsync();
            if (recheck.success && recheck.wsUrl != currentWsUrl)
            {
                Log("WebSocket target refreshed.", "SYS");
                return recheck.wsUrl;
            }

            return currentWsUrl;
        }

        private void SetRunningState(bool running)
        {
            _isRunning = running;
            RunBtn.IsEnabled = !running;
            RunBtn.Text = running ? "RUNNING..." : "INITIALIZE QUESTS";
            LoadingIndicator.IsVisible = running;
            LoadingIndicator.IsRunning = running;

            if (StatusBadgeLbl is not null)
            {
                StatusBadgeLbl.Text = running ? "RUNNING" : "READY";
            }
        }
    }
}
