async function autoAcceptQuests() {
    console.log("[INIT] Scanning for available quests...");

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
        // Filter out expired quests so we don't try to accept dead ones
        const quests = [...QuestsStore.quests.values()].filter(x => new Date(x.config.expiresAt).getTime() > Date.now());
        let acceptedCount = 0;

        for (const quest of quests) {
            // Check if userStatus exists and if enrolledAt is missing
            if (!quest.userStatus?.enrolledAt) {
                const questName = quest.config?.messages?.questName || quest.id;
                console.log(`[ACTION] Enrolling in: ${questName}`);
                
                try {
                    await api.post({ 
                        url: `/quests/${quest.id}/enroll`, 
                        body: { location: 13 } 
                    });
                    console.log(`[SUCCESS] Enrolled in ${questName}`);
                    acceptedCount++;
                } catch (enrollErr) {
                    const reason = enrollErr.body ? JSON.stringify(enrollErr.body) : enrollErr.message;
                    console.log(`[ERROR] Failed to enroll in ${questName}. Reason: ${reason}`);
                }
                
                await new Promise(r => setTimeout(r, 1500));
            }
        }

        if (acceptedCount === 0) {
            console.log("[STATUS] No new valid quests available to accept.");
        } else {
            console.log(`[SUCCESS] Automatically accepted ${acceptedCount} quest(s).`);
        }

    } catch (err) {
        console.log("[ERROR] Auto-Accept failed: " + err.message);
    }
}

autoAcceptQuests();