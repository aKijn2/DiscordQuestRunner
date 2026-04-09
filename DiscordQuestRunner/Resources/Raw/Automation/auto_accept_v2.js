(async function () {
    const stateKey = "__DQR_AUTO_ACCEPT_STATE__";
    const runId = `auto-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
    const previousState = window[stateKey];

    if (previousState?.cancel) {
        previousState.cancel("superseded");
    }

    const state = {
        runId,
        cancelled: false,
        reason: null,
        cancel(reason) {
            this.cancelled = true;
            this.reason = reason || "cancelled";
        }
    };
    window[stateKey] = state;

    const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
    const isCancelled = () => state.cancelled || window[stateKey]?.runId !== runId;
    const cancelReason = () => state.reason || "superseded";
    const getQuestName = (quest) =>
        quest?.config?.messages?.questName
        || quest?.config?.application?.name
        || quest?.id
        || "Unknown Quest";
    const getErrorDetails = (error) => {
        if (!error) {
            return "Unknown error";
        }

        const parts = [];
        if (error.status != null) parts.push(`status=${error.status}`);
        if (error.message) parts.push(`message=${error.message}`);
        if (error.body) parts.push(`body=${JSON.stringify(error.body)}`);
        return parts.length > 0 ? parts.join(" | ") : String(error);
    };

    const resolveWebpackRequire = () => {
        const wpRequire = window.webpackChunkdiscord_app.push([[Symbol()], {}, (runtime) => runtime]);
        window.webpackChunkdiscord_app.pop();
        return wpRequire;
    };

    const resolveApi = (wpRequire) =>
        Object.values(wpRequire.c).find((entry) => entry?.exports?.Bo?.get)?.exports?.Bo
        || Object.values(wpRequire.c).find((entry) => entry?.exports?.tn?.get)?.exports?.tn;

    const resolveQuestsStore = (wpRequire) =>
        Object.values(wpRequire.c).find((entry) => entry?.exports?.Z?.__proto__?.getQuest)?.exports?.Z
        || Object.values(wpRequire.c).find((entry) => entry?.exports?.A?.__proto__?.getQuest)?.exports?.A;

    const getEnrollableQuests = (questsStore) =>
        [...questsStore.quests.values()].filter((quest) => {
            const expiresAt = new Date(quest?.config?.expiresAt).getTime();
            return Number.isFinite(expiresAt) && expiresAt > Date.now();
        });

    try {
        console.log("[INIT] Scanning for available quests...");

        let wpRequire;
        try {
            wpRequire = resolveWebpackRequire();
        } catch (error) {
            console.log(`[ERROR] Webpack error: ${error.message}`);
            return;
        }

        const api = resolveApi(wpRequire);
        const questsStore = resolveQuestsStore(wpRequire);

        if (!api || !questsStore) {
            console.log("[ERROR] Could not hook into Discord's internal API.");
            return;
        }

        const quests = getEnrollableQuests(questsStore);
        let acceptedCount = 0;
        let skippedCount = 0;

        for (const quest of quests) {
            if (isCancelled()) {
                console.log(`[STATUS] Auto-accept cancelled: ${cancelReason()}.`);
                return;
            }

            if (quest.userStatus?.enrolledAt) {
                continue;
            }

            const taskConfig = quest.config?.taskConfig ?? quest.config?.taskConfigV2;
            const taskNames = Object.keys(taskConfig?.tasks ?? {});
            const questName = getQuestName(quest);

            if (!taskConfig || taskNames.length === 0) {
                skippedCount++;
                console.log(`[SKIP] ${questName} has no supported task configuration.`);
                continue;
            }

            console.log(`[ACTION] Enrolling in: ${questName}`);

            try {
                const response = await api.post({
                    url: `/quests/${quest.id}/enroll`,
                    body: { location: 13 }
                });
                const enrolledAt = response?.body?.enrolled_at ?? response?.body?.user_status?.enrolled_at;
                console.log(`[SUCCESS] Enrolled in ${questName}${enrolledAt ? ` at ${enrolledAt}` : ""}`);
                acceptedCount++;
            } catch (error) {
                console.log(`[ERROR] Failed to enroll in ${questName}. ${getErrorDetails(error)}`);
            }

            await sleep(1500);
        }

        if (acceptedCount === 0) {
            console.log(
                skippedCount > 0
                    ? `[STATUS] No new valid quests available to accept. Skipped ${skippedCount} unsupported quest(s).`
                    : "[STATUS] No new valid quests available to accept.");
            return;
        }

        console.log(`[SUCCESS] Automatically accepted ${acceptedCount} quest(s).`);
    } catch (error) {
        console.log(`[ERROR] Auto-Accept failed: ${error.message}`);
    } finally {
        if (window[stateKey]?.runId === runId) {
            delete window[stateKey];
        }
    }
})();
