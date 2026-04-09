// Message Deleter Script
// Placeholders CHANNEL_ID_PLACEHOLDER and USER_ID_PLACEHOLDER are replaced at runtime.
(async function () {
    /**
     * Suspends execution between paged requests and delete attempts.
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

    /**
     * Collects message identifiers authored by the target user.
     *
     * @param {any} api Discord's internal REST client.
     * @param {string} channelId Discord channel identifier.
     * @param {string} userId Discord user identifier.
     * @param {number} maxFetches Maximum number of history pages to inspect.
     * @returns {Promise<string[]>} Ordered list of message identifiers to delete.
     * @throws {Error} Propagates Discord API transport and validation failures.
     */
    const collectTargetMessageIds = async (api, channelId, userId, maxFetches) => {
        const messageIds = [];
        let beforeId = null;

        for (let fetchIndex = 0; fetchIndex < maxFetches; fetchIndex++) {
            const batch = await fetchBatch(api, channelId, beforeId);
            if (batch.length === 0) {
                break;
            }

            for (const message of batch) {
                if (message?.author?.id === userId && message?.id) {
                    messageIds.push(message.id);
                }
            }

            beforeId = batch[batch.length - 1]?.id ?? null;
            if (batch.length < 100 || !beforeId) {
                break;
            }

            await sleep(400);
        }

        return messageIds;
    };

    /**
     * Deletes a single message and retries when Discord returns a rate-limit response.
     *
     * @param {any} api Discord's internal REST client.
     * @param {string} channelId Discord channel identifier.
     * @param {string} messageId Discord message identifier.
     * @returns {Promise<boolean>} True when the message was deleted; otherwise, false.
     */
    const deleteMessageWithRetry = async (api, channelId, messageId) => {
        while (true) {
            try {
                await api.del({ url: `/channels/${channelId}/messages/${messageId}` });
                return true;
            } catch (error) {
                if (error?.status !== 429) {
                    console.log(`Failed for ${messageId}: ${error.message}`);
                    return false;
                }

                const retryAfterSeconds = Number(error?.body?.retry_after ?? error?.retry_after ?? 5);
                const retryDelayMs = Math.max(1000, Math.ceil(retryAfterSeconds * 1000));
                console.log(`Rate limited. Pausing for ${Math.ceil(retryDelayMs / 1000)}s...`);
                await sleep(retryDelayMs);
            }
        }
    };

    try {
        console.log("--- MESSAGE DELETER ACTIVE ---");

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

        console.log("Re-fetching message list for deletion...");
        const messageIds = await collectTargetMessageIds(api, channelId, userId, maxFetches);

        console.log(`Ready to purge ${messageIds.length} messages.`);
        if (messageIds.length === 0) {
            console.log("No targets found.");
            return;
        }

        let deletedCount = 0;
        for (const messageId of messageIds) {
            const deleted = await deleteMessageWithRetry(api, channelId, messageId);
            if (!deleted) {
                continue;
            }

            deletedCount++;
            console.log(`[${deletedCount}/${messageIds.length}] Purged message: ${messageId}`);
            await sleep(1100);
        }

        console.log(`PURGE COMPLETE. ${deletedCount} messages neutralized.`);
    } catch (error) {
        console.log(`Critical Purge Error: ${error.message}`);
    }
})();
