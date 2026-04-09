using DiscordQuestRunner.Services;
using DiscordQuestRunner.UI;

namespace DiscordQuestRunner.Pages
{
    /// <summary>
    /// Hosts the message counting and deletion workflow for a specific Discord channel and user.
    /// </summary>
    public partial class MessagePurgePage : ContentPage
    {
        private readonly DiscordService _discordService;
        private readonly LogConsoleController _logConsole;
        private readonly OverlayAlertController _alertController;

        private bool _isAborting;
        private CancellationTokenSource? _purgeCts;

        /// <summary>
        /// Initializes the page and binds the shared alert and log controllers to the view.
        /// </summary>
        /// <param name="discordService">Service that manages CDP discovery and script execution.</param>
        public MessagePurgePage(DiscordService discordService)
        {
            InitializeComponent();

            _discordService = discordService;
            _logConsole = new LogConsoleController(StatusLbl, LogScroll);
            _alertController = new OverlayAlertController(
                ModalOverlay,
                AlertTitleLbl,
                AlertMessageLbl,
                AlertConfirmBtn,
                AlertCancelBtn);

            Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoBorder", (handler, view) =>
            {
#if WINDOWS
                handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
                handler.PlatformView.Background = null;
#endif
            });
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
        /// Appends a line to the purge log.
        /// </summary>
        /// <param name="message">Message text to append.</param>
        private void Log(string message) =>
            _ = _logConsole.AppendLineAsync($"> {message}");

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
        /// Validates that a value matches the length and character constraints of a Discord snowflake identifier.
        /// </summary>
        /// <param name="value">Identifier candidate supplied by the user.</param>
        /// <returns>
        /// <see langword="true"/> when the value looks like a Discord snowflake; otherwise, <see langword="false"/>.
        /// </returns>
        private static bool IsValidSnowflakeId(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.Length >= 17 && value.Length <= 20 && value.All(char.IsDigit);
        }

        /// <summary>
        /// Copies the buffered purge log to the system clipboard.
        /// </summary>
        /// <param name="sender">Button that raised the event.</param>
        /// <param name="e">Event data supplied by MAUI.</param>
        private async void OnCopyLogClicked(object sender, EventArgs e)
        {
            await Clipboard.SetTextAsync(_logConsole.Text);
            await ShowNexusAlertAsync(
                "DATA EXPORTED",
                "Purge log copied to system clipboard.",
                "OK");
        }

        /// <summary>
        /// Requests cancellation for the active purge sequence.
        /// </summary>
        /// <param name="sender">Button that raised the event.</param>
        /// <param name="e">Event data supplied by MAUI.</param>
        private void OnAbortClicked(object sender, EventArgs e)
        {
            if (_purgeCts is null || _purgeCts.IsCancellationRequested)
            {
                return;
            }

            _isAborting = true;
            AbortBtn.IsEnabled = false;
            _purgeCts.Cancel();
            Log("[WARN] ABORT REQUESTED - Halting after current operation...");
        }

        /// <summary>
        /// Starts the count and purge workflow after validating the supplied identifiers.
        /// </summary>
        /// <param name="sender">Button that raised the event.</param>
        /// <param name="e">Event data supplied by MAUI.</param>
        private async void OnDeleteClicked(object sender, EventArgs e)
        {
#if WINDOWS
            var channelId = ChannelIdEntry.Text?.Trim() ?? string.Empty;
            var userId = UserIdEntry.Text?.Trim() ?? string.Empty;

            if (!IsValidSnowflakeId(channelId) || !IsValidSnowflakeId(userId))
            {
                await ShowNexusAlertAsync(
                    "INVALID PARAMETERS",
                    "Please enter valid Discord IDs (17-20 digit numbers).",
                    "ACKNOWLEDGE");
                return;
            }

            var confirm = await ShowNexusAlertAsync(
                "CONFIRM TARGET",
                $"Analyze channel {channelId}\nfor messages from user {userId}?",
                "PROCEED",
                "CANCEL");

            if (!confirm)
            {
                return;
            }

            _purgeCts = new CancellationTokenSource();
            _isAborting = false;

            try
            {
                SetBusyState(true);
                Log("Checking Discord debug port...");

                var connection = await TryInitializeConnectionAsync(_purgeCts.Token);
                if (connection is null)
                {
                    return;
                }

                var messageCount = await CountMessagesAsync(
                    connection.Value.wsUrl,
                    channelId,
                    userId,
                    _purgeCts.Token);

                if (messageCount is null)
                {
                    return;
                }

                if (messageCount.Value == 0)
                {
                    await ShowNexusAlertAsync(
                        "TARGET CLEAR",
                        "No messages found for this user in the specified channel.",
                        "OK");
                    return;
                }

                UpdateCounters(messageCount.Value, 0);

                var confirmDelete = await ShowNexusAlertAsync(
                    "CONFIRM PURGE",
                    $"Found {messageCount.Value} message(s).\n\nAre you sure you want to permanently DELETE ALL of them?",
                    "PURGE ALL",
                    "CANCEL");

                if (!confirmDelete)
                {
                    Log("Purge cancelled by user.");
                    return;
                }

                AbortBtn.IsEnabled = true;
                Log("Starting deletion sequence...");

                await ExecuteDeleteAsync(
                    connection.Value.wsUrl,
                    channelId,
                    userId,
                    _purgeCts.Token);

                if (!_isAborting)
                {
                    Log("Deletion sequence completed.");
                }
            }
            catch (OperationCanceledException)
            {
                Log("Purge cancelled by user.");
            }
            finally
            {
                AbortBtn.IsEnabled = false;
                SetBusyState(false);
                ResetCounters();
                _purgeCts?.Dispose();
                _purgeCts = null;
                _isAborting = false;
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
            Log("Running preflight environment check...");

            var preflight = await RunPreflightWithRecoveryAsync(
                DiscordAutomationCapability.RestApi,
                cancellationToken);

            if (preflight is null || string.IsNullOrWhiteSpace(preflight.WebSocketDebuggerUrl))
            {
                return null;
            }

            Log("Preflight complete. Startup conditions verified.");
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
                var restart = await ShowNexusAlertAsync(
                    "RESTART REQUIRED",
                    "Discord must be restarted with debug mode enabled. Proceed?",
                    "AUTHORIZE",
                    "ABORT");

                if (!restart)
                {
                    Log("Aborted by user.");
                    return null;
                }

                cancellationToken.ThrowIfCancellationRequested();

                Log("Restarting Discord...");
                var restartResult = await _discordService.RestartDiscordAsync(Log);
                if (!restartResult.success)
                {
                    Log($"[FATAL] {restartResult.message}");
                    await ShowNexusAlertAsync("RESTART FAILED", restartResult.message, "CLOSE");
                    return null;
                }

                Log(restartResult.message);
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
        /// Writes the preflight stage results to the purge log in execution order.
        /// </summary>
        /// <param name="report">Report emitted by the shared preflight service.</param>
        private void LogPreflightReport(DiscordPreflightReport report)
        {
            foreach (var step in report.Steps)
            {
                var level = step.Success ? "[CHK]" : "[WARN]";
                Log($"{level} {FormatPreflightStage(step.Stage)}: {step.Message}");
            }
        }

        /// <summary>
        /// Formats a preflight stage into a short purge-log label.
        /// </summary>
        /// <param name="stage">Stage being written to the purge log.</param>
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
        /// Executes the count script and extracts the message total from the emitted log markers.
        /// </summary>
        /// <param name="wsUrl">CDP WebSocket target used to evaluate the script.</param>
        /// <param name="channelId">Discord channel identifier.</param>
        /// <param name="userId">Discord user identifier.</param>
        /// <param name="cancellationToken">Token that cancels the script execution.</param>
        /// <returns>
        /// The parsed message count when the script completes successfully; otherwise, <see langword="null"/>.
        /// </returns>
        /// <exception cref="OperationCanceledException">
        /// Thrown when <paramref name="cancellationToken"/> is cancelled during script execution.
        /// </exception>
        private async Task<int?> CountMessagesAsync(
            string wsUrl,
            string channelId,
            string userId,
            CancellationToken cancellationToken)
        {
            Log("Executing count protocol...");

            var countScriptTemplate = await DiscordService.LoadScriptAsync(
                DiscordScriptCatalog.CountMessages);
            var countScript = DiscordScriptCatalog.BindPlaceholders(
                countScriptTemplate,
                ("CHANNEL_ID_PLACEHOLDER", channelId),
                ("USER_ID_PLACEHOLDER", userId));

            int? countResult = null;

            await _discordService.ExecuteScriptAsync(
                wsUrl,
                countScript,
                msg =>
                {
                    Log(msg);
                    if (DiscordScriptOutputParser.TryParseCountResult(msg, out var count))
                    {
                        countResult = count;
                    }
                },
                cancellationToken);

            if (!countResult.HasValue)
            {
                Log("[ERROR] Could not determine message count.");
            }

            return countResult;
        }

        /// <summary>
        /// Executes the delete script and updates the progress counters from emitted log markers.
        /// </summary>
        /// <param name="wsUrl">CDP WebSocket target used to evaluate the script.</param>
        /// <param name="channelId">Discord channel identifier.</param>
        /// <param name="userId">Discord user identifier.</param>
        /// <param name="cancellationToken">Token that cancels the script execution.</param>
        /// <returns>A task that completes when the delete script exits.</returns>
        /// <exception cref="OperationCanceledException">
        /// Thrown when <paramref name="cancellationToken"/> is cancelled during script execution.
        /// </exception>
        private async Task ExecuteDeleteAsync(
            string wsUrl,
            string channelId,
            string userId,
            CancellationToken cancellationToken)
        {
            var deleteScriptTemplate = await DiscordService.LoadScriptAsync(
                DiscordScriptCatalog.DeleteMessages);
            var deleteScript = DiscordScriptCatalog.BindPlaceholders(
                deleteScriptTemplate,
                ("CHANNEL_ID_PLACEHOLDER", channelId),
                ("USER_ID_PLACEHOLDER", userId));

            await _discordService.ExecuteScriptAsync(
                wsUrl,
                deleteScript,
                msg =>
                {
                    Log(msg);

                    if (DiscordScriptOutputParser.TryParsePurgeProgress(
                        msg,
                        out var deleted,
                        out var total))
                    {
                        UpdateCounters(total, deleted);
                    }
                },
                cancellationToken);
        }

        /// <summary>
        /// Applies the busy state to the page controls.
        /// </summary>
        /// <param name="isBusy">Whether the purge workflow is currently active.</param>
        private void SetBusyState(bool isBusy)
        {
            DeleteBtn.IsEnabled = !isBusy;
            LoadingIndicator.IsVisible = isBusy;
            LoadingIndicator.IsRunning = isBusy;

            if (isBusy)
            {
                _ = _logConsole.ResetAsync("> Connecting to Discord...");
            }
        }

        /// <summary>
        /// Updates the counter widgets from the latest delete progress.
        /// </summary>
        /// <param name="totalMessages">Total number of messages scheduled for deletion.</param>
        /// <param name="deletedMessages">Number of messages deleted so far.</param>
        private void UpdateCounters(int totalMessages, int deletedMessages)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                FoundCountLbl.Text = totalMessages.ToString();
                DeletedCountLbl.Text = deletedMessages.ToString();

                var percent = totalMessages == 0
                    ? 0
                    : (int)Math.Round((double)deletedMessages / totalMessages * 100);
                ProgressPctLbl.Text = $"{percent}%";
            });
        }

        /// <summary>
        /// Clears the counter widgets after the purge workflow finishes.
        /// </summary>
        private void ResetCounters()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                FoundCountLbl.Text = "—";
                DeletedCountLbl.Text = "—";
                ProgressPctLbl.Text = "—";
            });
        }
    }
}
