// Quest Runner & Claimer Script (V3)
(async function() {
    const stateKey = "__DQR_QUEST_RUNNER_STATE__";
    const autoAcceptStateKey = "__DQR_AUTO_ACCEPT_STATE__";
    const runId = `runner-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
    const previousState = window[stateKey];

    if (previousState?.cancel) {
        previousState.cancel("superseded");
    }

    if (window[autoAcceptStateKey]?.cancel) {
        window[autoAcceptStateKey].cancel("runner-started");
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

    let internalLog = "";
    const originalConsole = window.console;
    const log = (msg, ...args) => { 
        internalLog += msg + " " + args.join(" ") + "\n"; 
        originalConsole.log("[DQR SCRIPT] " + msg + " " + args.join(" "));
    };
    const console = { log };
    const isCancelled = () => state.cancelled || window[stateKey]?.runId !== runId;
    const cancelReason = () => state.reason || "superseded";
    const getQuestName = (quest) => quest?.config?.messages?.questName || quest?.config?.application?.name || quest?.id || "Unknown Quest";
    const getErrorDetails = (error) => {
        if (!error) return "Unknown error";

        const parts = [];
        if (error.status != null) parts.push(`status=${error.status}`);
        if (error.message) parts.push(`message=${error.message}`);
        if (error.body) parts.push(`body=${JSON.stringify(error.body)}`);

        return parts.length > 0 ? parts.join(" | ") : String(error);
    };

    try {
        log("--- QUEST RUNNER & CLAIMER (V3) ---");

        if (isCancelled()) {
            log(`[STATUS] Runner cancelled: ${cancelReason()}.`);
            return internalLog;
        }

        let wpRequire;
        try {
            wpRequire = window.webpackChunkdiscord_app.push([[Symbol()], {}, r => r]);
            webpackChunkdiscord_app.pop();
        } catch(e) { return "Webpack error: " + e.message; }

        let ApplicationStreamingStore = Object.values(wpRequire.c).find(x => x?.exports?.Z?.__proto__?.getStreamerActiveStreamMetadata)?.exports?.Z;
        let RunningGameStore, QuestsStore, ChannelStore, GuildChannelStore, FluxDispatcher, api;
        
        if(!ApplicationStreamingStore) {
            ApplicationStreamingStore = Object.values(wpRequire.c).find(x => x?.exports?.A?.__proto__?.getStreamerActiveStreamMetadata).exports.A;
            RunningGameStore = Object.values(wpRequire.c).find(x => x?.exports?.Ay?.getRunningGames).exports.Ay;
            QuestsStore = Object.values(wpRequire.c).find(x => x?.exports?.A?.__proto__?.getQuest).exports.A;
            ChannelStore = Object.values(wpRequire.c).find(x => x?.exports?.A?.__proto__?.getAllThreadsForParent).exports.A;
            GuildChannelStore = Object.values(wpRequire.c).find(x => x?.exports?.Ay?.getSFWDefaultChannel).exports.Ay;
            FluxDispatcher = Object.values(wpRequire.c).find(x => x?.exports?.h?.__proto__?.flushWaitQueue).exports.h;
            api = Object.values(wpRequire.c).find(x => x?.exports?.Bo?.get).exports.Bo;
        } else {
            RunningGameStore = Object.values(wpRequire.c).find(x => x?.exports?.ZP?.getRunningGames).exports.ZP;
            QuestsStore = Object.values(wpRequire.c).find(x => x?.exports?.Z?.__proto__?.getQuest).exports.Z;
            ChannelStore = Object.values(wpRequire.c).find(x => x?.exports?.Z?.__proto__?.getAllThreadsForParent).exports.Z;
            GuildChannelStore = Object.values(wpRequire.c).find(x => x?.exports?.ZP?.getSFWDefaultChannel).exports.ZP;
            FluxDispatcher = Object.values(wpRequire.c).find(x => x?.exports?.Z?.__proto__?.flushWaitQueue).exports.Z;
            api = Object.values(wpRequire.c).find(x => x?.exports?.tn?.get).exports.tn;
        }

        const claimQuest = async (quest) => {
            const questName = getQuestName(quest);
            log(`Claiming reward for: ${questName}...`);

            // Discord shows the captcha UI WHILE the api.post is still pending.
            // We run the iframe clicker concurrently so it can act during that window.
            let stopAutoClicker = false;
            const autoClickerTask = (async () => {
                await new Promise(r => setTimeout(r, 600));
                while (!stopAutoClicker) {
                    try {
                        const allFrames = Array.from(document.querySelectorAll("iframe"));
                        originalConsole.log("[DQR] iframe count: " + allFrames.length);
                        for (let i = 0; i < allFrames.length; i++) {
                            const f = allFrames[i];
                            const r = f.getBoundingClientRect();
                            originalConsole.log("[DQR] iframe[" + i + "] src=" + f.src + " title=" + f.title + " w=" + Math.round(r.width) + " h=" + Math.round(r.height) + " l=" + Math.round(r.left) + " t=" + Math.round(r.top));
                        }
                        let target = allFrames.find(f =>
                            (f.src && f.src.includes("hcaptcha")) ||
                            (f.title && f.title.toLowerCase().includes("hcaptcha"))
                        ) || allFrames.find(f => {
                            const r = f.getBoundingClientRect();
                            return r.width > 100 && r.width < 500 && r.height > 30 && r.height < 120 && r.top > 0;
                        });
                        if (target) {
                            const rect = target.getBoundingClientRect();
                            const cx = Math.round(rect.left + 35);
                            const cy = Math.round(rect.top + rect.height / 2);
                            originalConsole.log("[DQR] CLICK_CAPTCHA:" + cx + "," + cy);
                            await new Promise(r => setTimeout(r, 3000));
                        } else {
                            originalConsole.log("[DQR] CLICK_CAPTCHA_NOTFOUND");
                            await new Promise(r => setTimeout(r, 1500));
                        }
                    } catch(err) {
                        originalConsole.log("[DQR] clicker error: " + err.message);
                        await new Promise(r => setTimeout(r, 1500));
                    }
                }
            })();

            try {
                await api.post({
                    url: `/quests/${quest.id}/claim-reward`,
                    body: { platform: 0, location: 11, is_targeted: false, metadata_raw: null, metadata_sealed: null }
                });
                log(`REWARD CLAIMED: ${questName}`);
            } catch(e) {
                if(e.body && (e.body.code === 50035 || e.body.captcha_key)) {
                    log(`CAPTCHA REQUIRED for ${questName}. Waiting for user to solve...`);
                    while (true) {
                        if (isCancelled()) break;
                        let freshQuest;
                        try { freshQuest = QuestsStore.quests.get(quest.id); } catch(e2) {}
                        if (freshQuest && freshQuest.userStatus?.claimedAt) {
                            log(`SUCCESS: Captcha solved. REWARD CLAIMED for ${questName}!`);
                            break;
                        }
                        await new Promise(r => setTimeout(r, 2000));
                    }
                } else {
                    log(`Claim failed: ${getErrorDetails(e)}`);
                }
            } finally {
                stopAutoClicker = true;
            }
        };

        const supportedTasks = ["WATCH_VIDEO", "PLAY_ON_DESKTOP", "STREAM_ON_DESKTOP", "PLAY_ACTIVITY", "WATCH_VIDEO_ON_MOBILE"];
        let quests = [...QuestsStore.quests.values()].filter(x => x.userStatus?.enrolledAt && !x.userStatus?.completedAt && new Date(x.config.expiresAt).getTime() > Date.now() && supportedTasks.find(y => Object.keys((x.config.taskConfig ?? x.config.taskConfigV2).tasks).includes(y)));
        
        let isApp = typeof DiscordNative !== "undefined";
        
        const unclaimed = [...QuestsStore.quests.values()].filter(x => x.userStatus?.completedAt && !x.userStatus?.claimedAt);
        if(unclaimed.length > 0) {
            log(`${unclaimed.length} pending claims found.`);
            for(const q of unclaimed) {
                if (isCancelled()) {
                    log(`[STATUS] Runner cancelled: ${cancelReason()}.`);
                    return internalLog;
                }
                await claimQuest(q);
                await new Promise(r => setTimeout(r, 1000));
            }
        }

        if(quests.length === 0) {
            log("No uncompleted quests found.");
        } else {
            log(`${quests.length} active quests found. Starting runner...`);
            let doJob = async function() {
                if (isCancelled()) {
                    log(`[STATUS] Runner cancelled: ${cancelReason()}.`);
                    return internalLog;
                }

                const quest = quests.pop();
                if(!quest) {
                    log("All jobs done. Sequence complete.");
                    return internalLog;
                }

                const pid = Math.floor(Math.random() * 30000) + 1000;
                const applicationId = quest.config.application.id;
                const applicationName = quest.config.application.name;
                const questName = getQuestName(quest);
                const taskConfig = quest.config.taskConfig ?? quest.config.taskConfigV2;
                const taskName = supportedTasks.find(x => taskConfig.tasks[x] != null);
                const secondsNeeded = taskConfig.tasks[taskName].target;
                let secondsDone = quest.userStatus?.progress?.[taskName]?.value ?? 0;

                log(`Starting: ${questName} [${taskName}]`);

                if(taskName === "WATCH_VIDEO" || taskName === "WATCH_VIDEO_ON_MOBILE") {
                    const maxFuture = 10, speed = 7, interval = 1;
                    const enrolledAt = new Date(quest.userStatus.enrolledAt).getTime();
                    let completed = false;
                    
                    while(true) {
                        if (isCancelled()) {
                            log(`[STATUS] Runner cancelled: ${cancelReason()}.`);
                            return internalLog;
                        }

                        const maxAllowed = Math.floor((Date.now() - enrolledAt)/1000) + maxFuture;
                        const diff = maxAllowed - secondsDone;
                        const timestamp = secondsDone + speed;
                        if(diff >= speed) {
                            try {
                                const res = await api.post({url: `/quests/${quest.id}/video-progress`, body: {timestamp: Math.min(secondsNeeded, timestamp + Math.random())}});
                                completed = res.body.completed_at != null;
                                secondsDone = Math.min(secondsNeeded, timestamp);
                            } catch (err) {
                                log(`[ERROR] Video progress failed: ${getErrorDetails(err)}. Retrying...`);
                                await new Promise(resolve => setTimeout(resolve, 5000)); // sleep a bit longer on error
                            }
                        }
                        
                        if(secondsDone >= secondsNeeded) break;
                        await new Promise(resolve => setTimeout(resolve, interval * 1000));
                    }
                    if(!completed) {
                        try {
                            await api.post({url: `/quests/${quest.id}/video-progress`, body: {timestamp: secondsNeeded}});
                        } catch (err) {
                            log(`[ERROR] Final video progress failed: ${getErrorDetails(err)}`);
                        }
                    }
                    log(`Quest completed: ${questName}`);
                    await claimQuest(quest); 
                    await doJob(); 
                } else if(taskName === "PLAY_ON_DESKTOP") {
                    if(!isApp) {
                        log(`This no longer works in browser for non-video quests. Use the discord desktop app!`);
                        await doJob();
                    } else {
                        await api.get({url: `/applications/public?application_ids=${applicationId}`}).then(async res => {
                            const appData = res.body[0];
                            const exeName = appData.executables.find(x => x.os === "win32").name.replace(">","");
                            
                            const fakeGame = {
                                cmdLine: `C:\\Program Files\\${appData.name}\\${exeName}`,
                                exeName,
                                exePath: `c:/program files/${appData.name.toLowerCase()}/${exeName}`,
                                hidden: false,
                                isLauncher: false,
                                id: applicationId,
                                name: appData.name,
                                pid: pid,
                                pidPath: [pid],
                                processName: appData.name,
                                start: Date.now(),
                            };
                            const realGames = RunningGameStore.getRunningGames();
                            const fakeGames = [fakeGame];
                            const realGetRunningGames = RunningGameStore.getRunningGames;
                            const realGetGameForPID = RunningGameStore.getGameForPID;
                            RunningGameStore.getRunningGames = () => fakeGames;
                            RunningGameStore.getGameForPID = (pid) => fakeGames.find(x => x.pid === pid);
                            FluxDispatcher.dispatch({type: "RUNNING_GAMES_CHANGE", removed: realGames, added: [fakeGame], games: fakeGames});
                            
                            await new Promise(resolve => {
                            let fn = data => {
                                let progress = quest.config.configVersion === 1 ? data.userStatus.streamProgressSeconds : Math.floor(data.userStatus.progress.PLAY_ON_DESKTOP.value);
                                log(`Quest progress: ${progress}/${secondsNeeded}`);
                                
                                if(progress >= secondsNeeded) {
                                    log("Quest completed!");
                                    RunningGameStore.getRunningGames = realGetRunningGames;
                                    RunningGameStore.getGameForPID = realGetGameForPID;
                                    FluxDispatcher.dispatch({type: "RUNNING_GAMES_CHANGE", removed: [fakeGame], added: [], games: []});
                                    FluxDispatcher.unsubscribe("QUESTS_SEND_HEARTBEAT_SUCCESS", fn);
                                    claimQuest(quest).then(() => { log("Claimed desktop quest."); resolve(); });
                                }
                            };
                            FluxDispatcher.subscribe("QUESTS_SEND_HEARTBEAT_SUCCESS", fn);
                            log(`Spoofed your game to ${applicationName}. Wait for ${Math.ceil((secondsNeeded - secondsDone) / 60)} more minutes.`);
                        });
                        await doJob();
                        }).catch(async error => {
                            log(`Failed to load application data for ${questName}: ${getErrorDetails(error)}`);
                            await doJob();
                        });
                    }
                } else if(taskName === "STREAM_ON_DESKTOP") {
                    if(!isApp) {
                        log(`This no longer works in browser. Use desktop app!`);
                        await doJob();
                    } else {
                        let realFunc = ApplicationStreamingStore.getStreamerActiveStreamMetadata;
                        ApplicationStreamingStore.getStreamerActiveStreamMetadata = () => ({
                            id: applicationId,
                            pid,
                            sourceName: null
                        });
                        
                        await new Promise(resolve => {
                        let fn = data => {
                            let progress = quest.config.configVersion === 1 ? data.userStatus.streamProgressSeconds : Math.floor(data.userStatus.progress.STREAM_ON_DESKTOP.value);
                            log(`Quest progress: ${progress}/${secondsNeeded}`);
                            if(progress >= secondsNeeded) {
                                log("Quest completed!");
                                ApplicationStreamingStore.getStreamerActiveStreamMetadata = realFunc;
                                FluxDispatcher.unsubscribe("QUESTS_SEND_HEARTBEAT_SUCCESS", fn);
                                claimQuest(quest).then(() => { log("Claimed stream quest."); resolve(); });
                            }
                        };
                        FluxDispatcher.subscribe("QUESTS_SEND_HEARTBEAT_SUCCESS", fn);
                        log(`Spoofed stream. Stream in vc for ${Math.ceil((secondsNeeded - secondsDone) / 60)} mins.`);
                        });
                        await doJob();
                    }
                } else if(taskName === "PLAY_ACTIVITY") {
                    const channelId = ChannelStore.getSortedPrivateChannels()[0]?.id ?? Object.values(GuildChannelStore.getAllGuilds()).find(x => x != null && x.VOCAL.length > 0).VOCAL[0].channel.id;
                    const streamKey = `call:${channelId}:1`;
                    let fn = async () => {
                        log(`Completing activity quest...`);
                        try {
                            while(true) {
                                if (isCancelled()) {
                                    log(`[STATUS] Runner cancelled: ${cancelReason()}.`);
                                    return;
                                }

                                const res = await api.post({url: `/quests/${quest.id}/heartbeat`, body: {stream_key: streamKey, terminal: false}});
                                const progress = res.body.progress.PLAY_ACTIVITY.value;
                                log(`Quest progress: ${progress}/${secondsNeeded}`);
                                await new Promise(resolve => setTimeout(resolve, 20 * 1000));
                                if(progress >= secondsNeeded) {
                                    await api.post({url: `/quests/${quest.id}/heartbeat`, body: {stream_key: streamKey, terminal: true}});
                                    break;
                                }
                            }
                            log("Quest completed!");
                            await claimQuest(quest);
                        } catch (error) {
                            log(`Activity quest failed for ${questName}: ${getErrorDetails(error)}`);
                        }
                        await doJob();
                    };
                    await fn();
                } else {
                   log(`Unknown task type: ${taskName}`);
                   await doJob();
                }
            };
            await doJob();
        }

        await new Promise(r => setTimeout(r, 2000));
        return internalLog;

    } catch(e) { return "Global Error: " + e.message; }
    finally {
        if (window[stateKey]?.runId === runId) {
            delete window[stateKey];
        }
    }
})();                                         


