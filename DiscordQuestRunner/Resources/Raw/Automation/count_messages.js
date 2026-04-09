// Message Counter Script
// Placeholders CHANNEL_ID_PLACEHOLDER and USER_ID_PLACEHOLDER are replaced at runtime.
(async function () {
    /**
     * Suspends execution between paged Discord API requests.
     *
     * @param {number} ms Delay in milliseconds.
     * @returns {Promise<void>} Promise resolved after the delay completes.
     */
    const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

    /**
     * Extracts the internal Webpack runtime so the script can locate Discord stores and API clients.
     *
     * @returns {any} Discord's Webpack runtime require function.
     * @throws {Error} Thrown when the Webpack chunk bootstrap fails.
     */
    const resolveWebpackRequire = () => {
        const wpRequire = webpackChunkdiscord_app.push([[Symbol()], {}, (runtime) => runtime]);
        webpackChunkdiscord_app.pop();
        return wpRequire;
    };

    /**
     * Resolves Discord's internal REST client from the Webpack module cache.
     *
     * @param {any} wpRequire Discord's Webpack runtime require function.
     * @returns {any | undefined} Internal REST client used by the desktop renderer.
     */
    const resolveApi = (wpRequire) =>
        Object.values(wpRequire.c).find((entry) => entry?.exports?.tn?.get)?.exports?.tn
        || Object.values(wpRequire.c).find((entry) => entry?.exports?.Bo?.get)?.exports?.Bo;

    /**
     * Fetches a single page of channel messages.
     *
     * @param {any} api Discord's internal REST client.
     * @param {string} channelId Discord channel identifier.
     * @param {string | null} beforeId Message identifier used for backward pagination.
     * @returns {Promise<any[]>} Message batch returned by Discord.
     * @throws {Error} Propagates Discord API transport and validation failures.
     */
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
