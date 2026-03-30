using DiscordQuestRunner.Services;

namespace DiscordQuestRunner.Pages
{
    public partial class DeleterPage : ContentPage
    {
        private readonly DiscordService _discordService;
        private bool _isAborting = false;
        private TaskCompletionSource<bool> _alertTcs; // Powers the custom UI alerts

        public DeleterPage(DiscordService discordService)
        {
            InitializeComponent();
            _discordService = discordService;
        }

        // ==========================================
        // CUSTOM ALERT SYSTEM
        // ==========================================
        private async Task<bool> ShowNexusAlertAsync(
            string title,
            string message,
            string confirmText,
            string? cancelText = null
        )
        {
            AlertTitleLbl.Text = title.ToUpper();
            AlertMessageLbl.Text = message;
            AlertConfirmBtn.Text = confirmText.ToUpper();

            if (string.IsNullOrEmpty(cancelText))
            {
                AlertCancelBtn.IsVisible = false;
                Grid.SetColumnSpan(AlertConfirmBtn, 2); // Center single button
            }
            else
            {
                AlertCancelBtn.IsVisible = true;
                AlertCancelBtn.Text = cancelText.ToUpper();
                Grid.SetColumnSpan(AlertConfirmBtn, 1);
            }

            // Animate overlay in
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

        // ==========================================

        /// <summary>
        /// Validates that a string is a valid Discord snowflake ID (17-20 digit number).
        /// </summary>
        private static bool IsValidSnowflakeId(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            return value.Length >= 17 && value.Length <= 20 && value.All(char.IsDigit);
        }

        private async void OnCopyLogClicked(object sender, EventArgs e)
        {
            await Clipboard.SetTextAsync(StatusLbl.Text);
            await ShowNexusAlertAsync(
                "DATA EXPORTED",
                "Purge log copied to system clipboard.",
                "OK"
            );
        }

        private void OnAbortClicked(object sender, EventArgs e)
        {
            _isAborting = true;
            AbortBtn.IsEnabled = false;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                StatusLbl.Text += "\n> [WARN] ABORT REQUESTED - Halting after current operation...";
                await LogScroll.ScrollToAsync(StatusLbl, ScrollToPosition.End, true);
            });
        }

        private async void OnDeleteClicked(object sender, EventArgs e)
        {
#if WINDOWS
            string channelId = ChannelIdEntry.Text?.Trim() ?? "";
            string userId = UserIdEntry.Text?.Trim() ?? "";

            // 1. Validation
            if (!IsValidSnowflakeId(channelId) || !IsValidSnowflakeId(userId))
            {
                await ShowNexusAlertAsync(
                    "INVALID PARAMETERS",
                    "Please enter valid Discord IDs (17-20 digit numbers).",
                    "ACKNOWLEDGE"
                );
                return;
            }

            // Upgraded Log function with Auto-Scrolling
            void Log(string msg) =>
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    StatusLbl.Text += $"\n> {msg}";
                    await LogScroll.ScrollToAsync(StatusLbl, ScrollToPosition.End, true);
                });

            // 2. Initial Confirmation
            bool confirm = await ShowNexusAlertAsync(
                "CONFIRM TARGET",
                $"Analyze channel {channelId}\nfor messages from user {userId}?",
                "PROCEED",
                "CANCEL"
            );

            if (!confirm)
                return;

            DeleteBtn.IsEnabled = false;
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;
            StatusLbl.Text = "> Connecting to Discord...";
            Log("Checking Discord debug port...");

            // 3. Port Check & Restart Alert
            var portCheck = await _discordService.CheckDebugPortAsync();
            if (!portCheck.isReady)
            {
                Log($"[WARN] {portCheck.message}");
                bool restart = await ShowNexusAlertAsync(
                    "RESTART REQUIRED",
                    "Discord must be restarted with debug mode enabled. Proceed?",
                    "AUTHORIZE",
                    "ABORT"
                );

                if (!restart)
                {
                    Log("Aborted by user.");
                    ResetUI();
                    return;
                }

                Log("Restarting Discord...");
                var restartResult = await _discordService.RestartDiscordAsync(Log);
                if (!restartResult.success)
                {
                    Log($"[FATAL] {restartResult.message}");
                    ResetUI();
                    return;
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
                ResetUI();
                return;
            }

            Log(connection.message);
            Log("Executing count protocol...");

            string countScriptTemplate = await DiscordService.LoadScriptAsync("count_messages.js");
            string countScript = countScriptTemplate
                .Replace("CHANNEL_ID_PLACEHOLDER", channelId)
                .Replace("USER_ID_PLACEHOLDER", userId);

            string countResult = "";
            await _discordService.ExecuteScriptAsync(
                connection.wsUrl,
                countScript,
                (msg) =>
                {
                    Log(msg);
                    if (msg.Contains("COUNT_RESULT:"))
                    {
                        countResult = msg.Split(':')[1].Trim();
                    }
                }
            );

            // 4. Handle Count Results
            if (
                string.IsNullOrEmpty(countResult)
                || !int.TryParse(countResult, out int messageCount)
            )
            {
                Log("[ERROR] Could not determine message count.");
                ResetUI();
                return;
            }

            if (messageCount == 0)
            {
                await ShowNexusAlertAsync(
                    "TARGET CLEAR",
                    "No messages found for this user in the specified channel.",
                    "OK"
                );
                ResetUI();
                return;
            }

            // 5. Final Purge Confirmation Alert
            bool confirmDelete = await ShowNexusAlertAsync(
                "CONFIRM PURGE",
                $"Found {messageCount} message(s).\n\nAre you sure you want to permanently DELETE ALL of them?",
                "PURGE ALL",
                "CANCEL"
            );

            if (!confirmDelete)
            {
                Log("Purge cancelled by user.");
                ResetUI();
                return;
            }

            _isAborting = false;
            AbortBtn.IsEnabled = true;

            Log("Starting deletion sequence...");

            string deleteScriptTemplate = await DiscordService.LoadScriptAsync(
                "delete_messages.js"
            );
            string script = deleteScriptTemplate
                .Replace("CHANNEL_ID_PLACEHOLDER", channelId)
                .Replace("USER_ID_PLACEHOLDER", userId);

            await _discordService.ExecuteScriptAsync(
                connection.wsUrl,
                script,
                (msg) =>
                {
                    if (_isAborting)
                    {
                        Log("Aborted by user.");
                        return;
                    }
                    Log(msg);
                }
            );

            Log("Deletion sequence completed.");
            AbortBtn.IsEnabled = false;
            ResetUI();

#else
            await ShowNexusAlertAsync(
                "SYSTEM ERROR",
                "This automation only works on Windows architecture.",
                "ACKNOWLEDGE"
            );
#endif
        }

        // Helper method to clean up repetitive state resets
        private void ResetUI()
        {
            DeleteBtn.IsEnabled = true;
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
        }
    }
}
