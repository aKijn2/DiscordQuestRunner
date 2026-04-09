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

    const isCancelled = () => state.cancelled || window[stateKey]?.runId !== runId;
    const cancelReason = () => state.reason || "superseded";
    const delay = (ms) => new Promise(resolve => setTimeout(resolve, ms));

    async function autoAcceptQuests() {
    console.log("[INIT] Scanning for available quests...");

    const getQuestName = (quest) => quest?.config?.messages?.questName || quest?.config?.application?.name || quest?.id || "Unknown Quest";
    const getErrorDetails = (error) => {
        if (!error) return "Unknown error";

        const parts = [];
        if (error.status != null) parts.push(`status=${error.status}`);
        if (error.message) parts.push(`message=${error.message}`);
        if (error.body) parts.push(`body=${JSON.stringify(error.body)}`);

        return parts.length > 0 ? parts.join(" | ") : String(error);
    };

    let wpRequire;
    try {
        wpRequire = window.webpackChunkdiscord_app.push([[Symbol()], {}, r => r]);
        window.webpackChunkdiscord_app.pop();
    } catch(e) { 
        console.log("[ERROR] Webpack error: " + e.message); 
        return; 
    }

    let api = Object.values(wpRequire.c).find(x => x?.exports?.Bo?.get)?.exports?.Bo;
    if (!api) api = Object.values(wpRequire.c).find(x => x?.exports?.tn?.get)?.exports?.tn;

    let QuestsStore = Object.values(wpRequire.c).find(x => x?.exports?.Z?.__proto__?.getQuest)?.exports?.Z;
    if (!QuestsStore) QuestsStore = Object.values(wpRequire.c).find(x => x?.exports?.A?.__proto__?.getQuest)?.exports?.A;

    if (!api || !QuestsStore) {
        console.log("[ERROR] Could not hook into Discord's internal API.");
        return;
    }

    try {
        // Filter out expired quests and only keep quests that look enrollable.
        const quests = [...QuestsStore.quests.values()].filter(x => {
            const expiresAt = new Date(x?.config?.expiresAt).getTime();
            return Number.isFinite(expiresAt) && expiresAt > Date.now();
        });
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

            const questName = getQuestName(quest);
            const taskConfig = quest.config?.taskConfig ?? quest.config?.taskConfigV2;
            const taskNames = Object.keys(taskConfig?.tasks ?? {});

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
            } catch (enrollErr) {
                console.log(`[ERROR] Failed to enroll in ${questName}. ${getErrorDetails(enrollErr)}`);
            }

            await delay(1500);
        }

        if (acceptedCount === 0) {
            console.log(skippedCount > 0
                ? `[STATUS] No new valid quests available to accept. Skipped ${skippedCount} unsupported quest(s).`
                : "[STATUS] No new valid quests available to accept.");
        } else {
            console.log(`[SUCCESS] Automatically accepted ${acceptedCount} quest(s).`);
        }

    } catch (err) {
        console.log("[ERROR] Auto-Accept failed: " + err.message);
    }
    }

    try {
        await autoAcceptQuests();
    } finally {
        if (window[stateKey]?.runId === runId) {
            delete window[stateKey];
        }
    }
})();