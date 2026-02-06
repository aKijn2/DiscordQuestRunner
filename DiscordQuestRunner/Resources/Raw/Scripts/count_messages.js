// Message Counter Script
// Placeholders CHANNEL_ID_PLACEHOLDER and USER_ID_PLACEHOLDER are replaced at runtime.
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
