using DiscordQuestRunner.Services;
using DiscordQuestRunner.UI;

namespace DiscordQuestRunner.Pages
{
    public partial class MessagePurgePage : ContentPage
    {
        private readonly DiscordService _discordService;
        private readonly LogConsoleController _logConsole;
        private readonly OverlayAlertController _alertController;

        private bool _isAborting;
        private CancellationTokenSource? _purgeCts;

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

        private Task<bool> ShowNexusAlertAsync(
            string title,
            string message,
            string confirmText,
            string? cancelText = null) =>
            _alertController.ShowAsync(title, message, confirmText, cancelText);

        private void Log(string message) =>
            _ = _logConsole.AppendLineAsync($"> {message}");

        private async void OnAlertConfirmClicked(object sender, EventArgs e) =>
            await _alertController.ConfirmAsync();

        private async void OnAlertCancelClicked(object sender, EventArgs e) =>
            await _alertController.CancelAsync();

        private static bool IsValidSnowflakeId(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.Length >= 17 && value.Length <= 20 && value.All(char.IsDigit);
        }

        private async void OnCopyLogClicked(object sender, EventArgs e)
        {
            await Clipboard.SetTextAsync(_logConsole.Text);
            await ShowNexusAlertAsync(
                "DATA EXPORTED",
                "Purge log copied to system clipboard.",
                "OK");
        }

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

        private async Task<(string wsUrl, string message)?> TryInitializeConnectionAsync(
            CancellationToken cancellationToken)
        {
            var portCheck = await _discordService.CheckDebugPortAsync();
            if (!portCheck.isReady)
            {
                Log($"[WARN] {portCheck.message}");
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
                    return null;
                }

                Log(restartResult.message);
            }
            else
            {
                Log("Debug port accessible.");
            }

            Log("Acquiring WebSocket connection...");
            var connection = await _discordService.InitConnectionAsync();
            if (!connection.success)
            {
                Log($"[ERROR] {connection.message}");
                return null;
            }

            Log(connection.message);
            return (connection.wsUrl, connection.message);
        }

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
