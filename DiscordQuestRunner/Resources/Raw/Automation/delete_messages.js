// Message Deleter Script
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
