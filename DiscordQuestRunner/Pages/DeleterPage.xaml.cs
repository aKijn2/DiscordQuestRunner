using DiscordQuestRunner.Services;

namespace DiscordQuestRunner.Pages
{
    public partial class DeleterPage : ContentPage
    {
        private readonly DiscordService _discordService;
        private bool _isAborting = false;

        public DeleterPage(DiscordService discordService)
        {
            InitializeComponent();
            _discordService = discordService;
        }

        /// <summary>
        /// Validates that a string is a valid Discord snowflake ID (17-20 digit number).
        /// </summary>
        private static bool IsValidSnowflakeId(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return value.Length >= 17 && value.Length <= 20 && value.All(char.IsDigit);
        }

        private async void OnCopyLogClicked(object sender, EventArgs e)
        {
            await Clipboard.SetTextAsync(StatusLbl.Text);
            await DisplayAlert("Copied", "Log copied to clipboard.", "OK");
        }

        private void OnAbortClicked(object sender, EventArgs e)
        {
            _isAborting = true;
            AbortBtn.IsEnabled = false;
            MainThread.BeginInvokeOnMainThread(() => StatusLbl.Text += "\nABORT REQUESTED - Stopping after current operation...");
        }

        private async void OnDeleteClicked(object sender, EventArgs e)
        {
#if WINDOWS
            string channelId = ChannelIdEntry.Text?.Trim() ?? "";
            string userId = UserIdEntry.Text?.Trim() ?? "";

            if (!IsValidSnowflakeId(channelId) || !IsValidSnowflakeId(userId))
            {
                await DisplayAlert("Error", "Please enter valid Discord IDs (17-20 digit numbers).", "OK");
                return;
            }

            void Log(string msg) => MainThread.BeginInvokeOnMainThread(() => StatusLbl.Text += $"\n{msg}");
            
            bool confirm = await DisplayAlert("Confirm Action", 
                $"Count messages from user {userId} in channel {channelId}?", 
                "Yes", "No");
            
            if (!confirm) return;

            DeleteBtn.IsEnabled = false;
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;
            StatusLbl.Text = "Connecting to Discord...";
            Log("Checking Discord debug port...");

            // Check if debug port is available, restart if needed
            var portCheck = await _discordService.CheckDebugPortAsync();
            if (!portCheck.isReady)
            {
                Log($"WARNING: {portCheck.message}");
                bool restart = await DisplayAlert("Debug Port Unavailable",
                    "Discord must be restarted with debug mode enabled. Proceed?", "Yes", "No");

                if (!restart)
                {
                    Log("Aborted by user.");
                    DeleteBtn.IsEnabled = true;
                    LoadingIndicator.IsVisible = false;
                    LoadingIndicator.IsRunning = false;
                    return;
                }

                Log("Restarting Discord...");
                var restartResult = await _discordService.RestartDiscordAsync(Log);
                if (!restartResult.success)
                {
                    Log($"FATAL: {restartResult.message}");
                    DeleteBtn.IsEnabled = true;
                    LoadingIndicator.IsVisible = false;
                    LoadingIndicator.IsRunning = false;
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
                Log($"ERROR: {connection.message}");
                DeleteBtn.IsEnabled = true;
                LoadingIndicator.IsVisible = false;
                LoadingIndicator.IsRunning = false;
                return;
            }

            Log(connection.message);
            Log("Counting messages...");

            string countScriptTemplate = await DiscordService.LoadScriptAsync("count_messages.js");
            string countScript = countScriptTemplate
                .Replace("CHANNEL_ID_PLACEHOLDER", channelId)
                .Replace("USER_ID_PLACEHOLDER", userId);

            string countResult = "";
            await _discordService.ExecuteScriptAsync(connection.wsUrl, countScript, (msg) => {
                Log(msg);
                if (msg.Contains("COUNT_RESULT:"))
                {
                    countResult = msg.Split(':')[1].Trim();
                }
            });

            if (string.IsNullOrEmpty(countResult) || !int.TryParse(countResult, out int messageCount))
            {
                Log("ERROR: Could not determine message count.");
                DeleteBtn.IsEnabled = true;
                return;
            }

            if (messageCount == 0)
            {
                await DisplayAlert("No Messages", "No messages found for this user in this channel.", "OK");
                DeleteBtn.IsEnabled = true;
                return;
            }

            bool confirmDelete = await DisplayAlert("Confirm Deletion", 
                $"Found {messageCount} message(s) to delete.\n\nAre you sure you want to DELETE ALL {messageCount} messages?", 
                "Yes, Delete All", "No, Cancel");
            
            if (!confirmDelete)
            {
                Log("Deletion cancelled by user.");
                DeleteBtn.IsEnabled = true;
                return;
            }

            _isAborting = false;
            AbortBtn.IsEnabled = true;

            Log("Starting deletion...");

            string deleteScriptTemplate = await DiscordService.LoadScriptAsync("delete_messages.js");
            string script = deleteScriptTemplate
                .Replace("CHANNEL_ID_PLACEHOLDER", channelId)
                .Replace("USER_ID_PLACEHOLDER", userId);

            await _discordService.ExecuteScriptAsync(connection.wsUrl, script, (msg) => {
                if (_isAborting)
                {
                    Log("Aborted by user.");
                    return;
                }
                Log(msg);
            });

            Log("Deletion sequence completed.");
            DeleteBtn.IsEnabled = true;
            AbortBtn.IsEnabled = false;
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
#else
            await DisplayAlert("Error", "This automation only works on Windows.", "OK");
#endif
        }
    }
}
