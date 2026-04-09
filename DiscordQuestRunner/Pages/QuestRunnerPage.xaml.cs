using DiscordQuestRunner.Services;
using DiscordQuestRunner.UI;

namespace DiscordQuestRunner.Pages
{
    /// <summary>
    /// Hosts the main quest automation workflow and bridges UI actions to the Discord CDP service.
    /// </summary>
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

        /// <summary>
        /// Initializes the page and binds the shared alert and log controllers to the view.
        /// </summary>
        /// <param name="discordService">Service that manages CDP discovery and script execution.</param>
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

        /// <summary>
        /// Shows a page-scoped modal alert.
        /// </summary>
        /// <param name="title">Alert title.</param>
        /// <param name="message">Alert body text.</param>
        /// <param name="confirmText">Text rendered on the confirm button.</param>
        /// <param name="cancelText">Optional text rendered on the cancel button.</param>
        /// <returns>
        /// <see langword="true"/> when the user confirms the dialog; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a second alert is requested before the active alert has been resolved.
        /// </exception>
        private Task<bool> ShowNexusAlertAsync(
            string title,
            string message,
            string confirmText,
            string? cancelText = null) =>
            _alertController.ShowAsync(title, message, confirmText, cancelText);

        /// <summary>
        /// Appends a formatted line to the runtime log.
        /// </summary>
        /// <param name="message">Message text to append.</param>
        /// <param name="prefix">Optional prefix rendered in brackets ahead of the message.</param>
        private void Log(string message, string prefix = "") =>
            _ = _logConsole.AppendLineAsync(message, prefix);

        /// <summary>
        /// Resets the runtime log to a single starting line.
        /// </summary>
        /// <param name="firstLine">Initial line displayed after the reset.</param>
        private void ResetLog(string firstLine) =>
            _ = _logConsole.ResetAsync(firstLine);

        /// <summary>
        /// Resolves the active overlay alert as confirmed.
        /// </summary>
        /// <param name="sender">Button that raised the event.</param>
        /// <param name="e">Event data supplied by MAUI.</param>
        private async void OnAlertConfirmClicked(object sender, EventArgs e) =>
            await _alertController.ConfirmAsync();

        /// <summary>
        /// Resolves the active overlay alert as cancelled.
        /// </summary>
        /// <param name="sender">Button that raised the event.</param>
        /// <param name="e">Event data supplied by MAUI.</param>
        private async void OnAlertCancelClicked(object sender, EventArgs e) =>
            await _alertController.CancelAsync();

        /// <summary>
        /// Opens the message purge page on supported desktop targets.
        /// </summary>
        /// <param name="sender">Button that raised the event.</param>
        /// <param name="e">Event data supplied by MAUI.</param>
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

        /// <summary>
        /// Copies the buffered runtime log to the system clipboard.
        /// </summary>
        /// <param name="sender">Button that raised the event.</param>
        /// <param name="e">Event data supplied by MAUI.</param>
        private async void OnCopyLogClicked(object sender, EventArgs e)
        {
            await Clipboard.SetTextAsync(_logConsole.Text);
            await ShowNexusAlertAsync("DATA EXPORTED", "Runtime log copied to system clipboard.", "OK");
        }

        /// <summary>
        /// Starts the quest automation sequence after validating Discord connectivity.
        /// </summary>
        /// <param name="sender">Button that raised the event.</param>
        /// <param name="e">Event data supplied by MAUI.</param>
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

        /// <summary>
        /// Ensures Discord is reachable through the debug port and resolves the active CDP target.
        /// </summary>
        /// <param name="cancellationToken">Token that cancels the startup sequence.</param>
        /// <returns>
        /// A tuple containing the WebSocket URL and status message when initialization succeeds; otherwise, <see langword="null"/>.
        /// </returns>
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

        /// <summary>
        /// Verifies that Discord exposes the CDP debug endpoint and optionally restarts the client when required.
        /// </summary>
        /// <param name="cancellationToken">Token that cancels the restart path before the client is relaunched.</param>
        /// <returns>
        /// <see langword="true"/> when the debug port is available; otherwise, <see langword="false"/>.
        /// </returns>
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

        /// <summary>
        /// Executes the auto-enrollment script before the main quest loop starts.
        /// </summary>
        /// <param name="wsUrl">CDP WebSocket target used to evaluate the script.</param>
        /// <param name="cancellationToken">Token that cancels the script execution or the post-run delay.</param>
        /// <returns>A task that completes after the auto-enrollment sequence finishes.</returns>
        /// <exception cref="OperationCanceledException">
        /// Thrown when <paramref name="cancellationToken"/> is cancelled during execution.
        /// </exception>
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

        /// <summary>
        /// Replays the quest runner script until the emitted terminal markers indicate completion.
        /// </summary>
        /// <param name="wsUrl">Initial CDP WebSocket target used to evaluate the quest runner script.</param>
        /// <param name="cancellationToken">Token that cancels the retry loop.</param>
        /// <returns>A task that completes when the loop exits.</returns>
        /// <exception cref="OperationCanceledException">
        /// Thrown when <paramref name="cancellationToken"/> is cancelled during a retry delay or script execution.
        /// </exception>
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

        /// <summary>
        /// Re-resolves the CDP target because Discord can rotate page targets during long-running automation.
        /// </summary>
        /// <param name="currentWsUrl">Current WebSocket target URL.</param>
        /// <returns>The refreshed WebSocket URL when a new target is found; otherwise, the original URL.</returns>
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

        /// <summary>
        /// Applies the current run state to the primary controls.
        /// </summary>
        /// <param name="running">Whether quest automation is currently active.</param>
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
