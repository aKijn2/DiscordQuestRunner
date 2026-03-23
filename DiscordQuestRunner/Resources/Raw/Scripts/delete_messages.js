// Message Deleter Script
// Placeholders CHANNEL_ID_PLACEHOLDER and USER_ID_PLACEHOLDER are replaced at runtime.
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
