using DiscordQuestRunner.Services;

namespace DiscordQuestRunner.Pages
{
    public partial class DeleterPage : ContentPage
    {
        private readonly DiscordService _discordService;
        private bool _isAborting = false;

        const string CountMessagesScript = """
(async function() {
    try {
        console.log("--- COUNTING MESSAGES ---");

        // 1. WEBPACK & API
        let wpRequire;
        try {
            wpRequire = webpackChunkdiscord_app.push([[Symbol()], {}, r => r]);
            webpackChunkdiscord_app.pop();
        } catch(e) { console.log("Webpack error: " + e.message); return; }

        let api = Object.values(wpRequire.c).find(x => x?.exports?.tn?.get)?.exports?.tn || 
                  Object.values(wpRequire.c).find(x => x?.exports?.Bo?.get)?.exports?.Bo;
        
        if(!api) {
            console.log("ERROR: Could not find Discord API module.");
            return;
        }

        const channelId = "CHANNEL_ID_PLACEHOLDER";
        const userId = "USER_ID_PLACEHOLDER";

        console.log(`Target Channel: ${channelId}`);
        console.log(`Target User: ${userId}`);
        console.log("Counting messages...");

        // 2. COUNT MESSAGES
        let totalCount = 0;
        let lastId = null;
        let fetchCount = 0;
        const maxFetches = 15;

        while(fetchCount < maxFetches) {
            try {
                const url = lastId 
                    ? `/channels/${channelId}/messages?before=${lastId}&limit=100`
                    : `/channels/${channelId}/messages?limit=100`;
                
                const response = await api.get({ url });
                const batch = response.body;
                
                if(!batch || batch.length === 0) break;
                
                const userMessages = batch.filter(m => m.author.id === userId);
                totalCount += userMessages.length;
                
                lastId = batch[batch.length - 1].id;
                fetchCount++;
                
                console.log(`Batch ${fetchCount}: +${userMessages.length} (Total: ${totalCount})`);
                
                if(batch.length < 100) break;
                await new Promise(r => setTimeout(r, 500));
            } catch(e) {
                console.log(`Fetch error: ${e.message}`);
                break;
            }
        }

        console.log(`COUNT_RESULT:${totalCount}`);

    } catch(e) { 
        console.log("Global Error: " + e.message); 
    }
})();
""";

        const string DeletionScript = """
(async function() {
    try {
        console.log("--- MESSAGE DELETER ACTIVE ---");

        // 1. WEBPACK & API
        let wpRequire;
        try {
            wpRequire = webpackChunkdiscord_app.push([[Symbol()], {}, r => r]);
            webpackChunkdiscord_app.pop();
        } catch(e) { console.log("Webpack error: " + e.message); return; }

        let api = Object.values(wpRequire.c).find(x => x?.exports?.tn?.get)?.exports?.tn || 
                  Object.values(wpRequire.c).find(x => x?.exports?.Bo?.get)?.exports?.Bo;
        
        if(!api) {
            console.log("ERROR: Could not find Discord API module.");
            return;
        }

        const channelId = "CHANNEL_ID_PLACEHOLDER";
        const userId = "USER_ID_PLACEHOLDER";

        console.log("Re-fetching message list for deletion...");

        // 2. FETCH MESSAGES
        let messages = [];
        let lastId = null;
        let fetchCount = 0;
        const maxFetches = 15;

        while(fetchCount < maxFetches) {
            try {
                const url = lastId 
                    ? `/channels/${channelId}/messages?before=${lastId}&limit=100`
                    : `/channels/${channelId}/messages?limit=100`;
                
                const response = await api.get({ url });
                const batch = response.body;
                
                if(!batch || batch.length === 0) break;
                
                const userMessages = batch.filter(m => m.author.id === userId);
                messages.push(...userMessages);
                
                lastId = batch[batch.length - 1].id;
                fetchCount++;
                
                if(batch.length < 100) break;
                await new Promise(r => setTimeout(r, 400));
            } catch(e) { break; }
        }

        console.log(`Ready to purge ${messages.length} messages.`);

        if(messages.length === 0) {
            console.log("No targets found.");
            return;
        }

        // 3. PURGE
        let deleted = 0;
        for(const msg of messages) {
            try {
                await api.del({
                    url: `/channels/${channelId}/messages/${msg.id}`
                });
                deleted++;
                console.log(`[${deleted}/${messages.length}] Purged message: ${msg.id}`);
                
                await new Promise(r => setTimeout(r, 1100)); // Safer delay
            } catch(e) {
                if(e.status === 429) {
                    console.log("Rate limited. Pausing for 5s...");
                    await new Promise(r => setTimeout(r, 5000));
                } else {
                    console.log(`Failed for ${msg.id}: ${e.message}`);
                }
            }
        }

        console.log(`PURGE COMPLETE. ${deleted} messages neutralized.`);

    } catch(e) { 
        console.log("Critical Purge Error: " + e.message); 
    }
})();
""";

        public DeleterPage()
        {
            InitializeComponent();
            _discordService = new DiscordService();
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
            string channelId = ChannelIdEntry.Text?.Trim();
            string userId = UserIdEntry.Text?.Trim();

            if (string.IsNullOrEmpty(channelId) || string.IsNullOrEmpty(userId))
            {
                await DisplayAlert("Error", "Please enter both Channel ID and User ID.", "OK");
                return;
            }

            void Log(string msg) => MainThread.BeginInvokeOnMainThread(() => StatusLbl.Text += $"\n{msg}");
            
            bool confirm = await DisplayAlert("Confirm Action", 
                $"Count messages from user {userId} in channel {channelId}?", 
                "Yes", "No");
            
            if (!confirm) return;

            DeleteBtn.IsEnabled = false;
            StatusLbl.Text = "Counting messages...";
            Log("Connecting to Discord...");

            var connection = await _discordService.InitConnectionAsync();
            if (!connection.success)
            {
                Log($"ERROR: {connection.message}");
                DeleteBtn.IsEnabled = true;
                return;
            }

            Log(connection.message);
            Log("Counting messages...");

            string countScript = CountMessagesScript
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

            string script = DeletionScript
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
#else
            await DisplayAlert("Error", "This automation only works on Windows.", "OK");
#endif
        }
    }
}
