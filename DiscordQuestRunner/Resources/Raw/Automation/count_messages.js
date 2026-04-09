// Message Counter Script
// Placeholders CHANNEL_ID_PLACEHOLDER and USER_ID_PLACEHOLDER are replaced at runtime.
(async function () {
    const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

    const resolveWebpackRequire = () => {
        const wpRequire = webpackChunkdiscord_app.push([[Symbol()], {}, (runtime) => runtime]);
        webpackChunkdiscord_app.pop();
        return wpRequire;
    };

    const resolveApi = (wpRequire) =>
        Object.values(wpRequire.c).find((entry) => entry?.exports?.tn?.get)?.exports?.tn
        || Object.values(wpRequire.c).find((entry) => entry?.exports?.Bo?.get)?.exports?.Bo;

    const fetchBatch = async (api, channelId, beforeId) => {
        const url = beforeId
            ? `/channels/${channelId}/messages?before=${beforeId}&limit=100`
            : `/channels/${channelId}/messages?limit=100`;

        const response = await api.get({ url });
        return Array.isArray(response?.body) ? response.body : [];
    };

    try {
        console.log("--- COUNTING MESSAGES ---");

        let wpRequire;
        try {
            wpRequire = resolveWebpackRequire();
        } catch (error) {
            console.log(`Webpack error: ${error.message}`);
            return;
        }

        const api = resolveApi(wpRequire);
        if (!api) {
            console.log("ERROR: Could not find Discord API module.");
            return;
        }

        const channelId = "CHANNEL_ID_PLACEHOLDER";
        const userId = "USER_ID_PLACEHOLDER";
        const maxFetches = 15;

        console.log(`Target Channel: ${channelId}`);
        console.log(`Target User: ${userId}`);
        console.log("Counting messages...");

        let totalCount = 0;
        let beforeId = null;

        for (let fetchIndex = 1; fetchIndex <= maxFetches; fetchIndex++) {
            try {
                const batch = await fetchBatch(api, channelId, beforeId);
                if (batch.length === 0) {
                    break;
                }

                const userMessageCount = batch.filter((message) => message?.author?.id === userId).length;
                totalCount += userMessageCount;
                beforeId = batch[batch.length - 1]?.id ?? null;

                console.log(`Batch ${fetchIndex}: +${userMessageCount} (Total: ${totalCount})`);

                if (batch.length < 100 || !beforeId) {
                    break;
                }

                await sleep(500);
            } catch (error) {
                console.log(`Fetch error: ${error.message}`);
                break;
            }
        }

        console.log(`COUNT_RESULT:${totalCount}`);
    } catch (error) {
        console.log(`Global Error: ${error.message}`);
    }
})();
