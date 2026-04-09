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
            Log("Running preflight environment check...", "SYS");

            var preflight = await RunPreflightWithRecoveryAsync(
                DiscordAutomationCapability.RestApi | DiscordAutomationCapability.QuestsStore,
                cancellationToken);

            if (preflight is null || string.IsNullOrWhiteSpace(preflight.WebSocketDebuggerUrl))
            {
                return null;
            }

            Log("Preflight complete. Startup conditions verified.", "SYS");
            return (preflight.WebSocketDebuggerUrl, "Preflight complete.");
        }

        /// <summary>
        /// Runs the shared preflight checks and optionally restarts Discord when only the debug port is missing.
        /// </summary>
        /// <param name="requiredCapabilities">Automation capabilities required by the workflow.</param>
        /// <param name="cancellationToken">Token that cancels the preflight or restart path.</param>
        /// <returns>
        /// A successful preflight report when startup conditions are satisfied; otherwise, <see langword="null"/>.
        /// </returns>
        /// <exception cref="OperationCanceledException">
        /// Thrown when <paramref name="cancellationToken"/> is cancelled during the restart path.
        /// </exception>
        private async Task<DiscordPreflightReport?> RunPreflightWithRecoveryAsync(
            DiscordAutomationCapability requiredCapabilities,
            CancellationToken cancellationToken)
        {
            var preflight = await _discordService.RunPreflightAsync(requiredCapabilities, cancellationToken);
            LogPreflightReport(preflight);

            if (preflight.Success)
            {
                return preflight;
            }

            if (!preflight.ProcessFound)
            {
                await ShowNexusAlertAsync(
                    "DISCORD NOT FOUND",
                    preflight.FailureMessage,
                    "CLOSE");
                return null;
            }

            if (!preflight.DebugPortReady)
            {
                var proceed = await ShowNexusAlertAsync(
                    "RESTART REQUIRED",
                    "Discord must be restarted in Debug Mode to continue. Authorize restart?",
                    "AUTHORIZE",
                    "ABORT");

                if (!proceed)
                {
                    Log("Operation aborted by user.", "SYS");
                    return null;
                }

                cancellationToken.ThrowIfCancellationRequested();

                Log("Initiating restart protocol...", "SYS");
                var restart = await _discordService.RestartDiscordAsync(msg => Log(msg, "SYS"));

                if (!restart.success)
                {
                    Log($"FATAL: {restart.message}");
                    await ShowNexusAlertAsync("RESTART FAILED", restart.message, "CLOSE");
                    return null;
                }

                Log(restart.message, "SYS");
                preflight = await _discordService.RunPreflightAsync(requiredCapabilities, cancellationToken);
                LogPreflightReport(preflight);

                if (preflight.Success)
                {
                    return preflight;
                }
            }

            await ShowNexusAlertAsync("PREFLIGHT FAILED", preflight.FailureMessage, "CLOSE");
            return null;
        }

        /// <summary>
        /// Writes the preflight stage results to the runtime log in execution order.
        /// </summary>
        /// <param name="report">Report emitted by the shared preflight service.</param>
        private void LogPreflightReport(DiscordPreflightReport report)
        {
            foreach (var step in report.Steps)
            {
                var prefix = step.Success ? "CHK" : "WARN";
                Log($"{FormatPreflightStage(step.Stage)}: {step.Message}", prefix);
            }
        }

        /// <summary>
        /// Formats a preflight stage into a short log label.
        /// </summary>
        /// <param name="stage">Stage being written to the log.</param>
        /// <returns>A compact label for the stage.</returns>
        private static string FormatPreflightStage(DiscordPreflightStage stage) =>
            stage switch
            {
                DiscordPreflightStage.Process => "PROCESS",
                DiscordPreflightStage.DebugPort => "DEBUG PORT",
                DiscordPreflightStage.Target => "TARGET",
                DiscordPreflightStage.AutomationSurface => "AUTOMATION",
                _ => "PREFLIGHT",
            };

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
