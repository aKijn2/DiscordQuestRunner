// Quest Runner & Claimer Script (V3)
(async function () {
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

    /**
     * Suspends execution between renderer API calls and heartbeat intervals.
     *
     * @param {number} ms Delay in milliseconds.
     * @returns {Promise<void>} Promise resolved after the delay completes.
     */
    const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

    /**
     * Records script output locally and forwards it with a stable prefix for the CDP bridge.
     *
     * @param {string} message Primary log message.
     * @param {...any} args Additional values appended to the message.
     * @returns {void}
     */
    const log = (message, ...args) => {
        internalLog += `${message} ${args.join(" ")}\n`;
        originalConsole.log(`[DQR SCRIPT] ${message} ${args.join(" ")}`);
    };

    /**
     * Checks whether this script instance has been superseded or explicitly cancelled.
     *
     * @returns {boolean} True when the current run should stop.
     */
    const isCancelled = () => state.cancelled || window[stateKey]?.runId !== runId;

    /**
     * Returns the current cancellation reason.
     *
     * @returns {string} Reason recorded by the latest cancellation request.
     */
    const cancelReason = () => state.reason || "superseded";
    const supportedTasks = [
        "WATCH_VIDEO",
        "PLAY_ON_DESKTOP",
        "STREAM_ON_DESKTOP",
        "PLAY_ACTIVITY",
        "WATCH_VIDEO_ON_MOBILE"
    ];

    /**
     * Resolves a display name for a Discord quest.
     *
     * @param {any} quest Raw quest object from Discord's internal store.
     * @returns {string} Human-readable quest name.
     */
    const getQuestName = (quest) =>
        quest?.config?.messages?.questName
        || quest?.config?.application?.name
        || quest?.id
        || "Unknown Quest";

    /**
     * Formats a Discord API or runtime error into a compact diagnostic string.
     *
     * @param {any} error Error payload returned by the renderer or REST client.
     * @returns {string} Structured error description.
     */
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
    /**
     * Selects the task configuration version exposed by the current quest record.
     *
     * @param {any} quest Raw quest object from Discord's internal store.
     * @returns {any} Active task configuration object.
     */
    const getTaskConfig = (quest) => quest.config.taskConfig ?? quest.config.taskConfigV2;

    /**
     * Selects the first supported task exposed by the current quest configuration.
     *
     * @param {any} quest Raw quest object from Discord's internal store.
     * @returns {string | undefined} Supported task name when one is available.
     */
    const getTaskName = (quest) => supportedTasks.find((task) => getTaskConfig(quest)?.tasks?.[task] != null);

    /**
     * Reads the current progress value for a quest task.
     *
     * @param {any} quest Raw quest object from Discord's internal store.
     * @param {string} taskName Supported task identifier.
     * @returns {number} Integer progress value reported by Discord.
     */
    const getProgressValue = (quest, taskName) =>
        Math.floor(quest?.userStatus?.progress?.[taskName]?.value ?? 0);

    /**
     * Emits a cancellation marker once and signals whether the current run should stop.
     *
     * @returns {boolean} True when the caller should abort further work.
     */
    const logCancellationAndExit = () => {
        if (!isCancelled()) {
            return false;
        }

        log(`[STATUS] Runner cancelled: ${cancelReason()}.`);
        return true;
    };

    /**
     * Resolves the internal stores and API clients needed by the runner.
     *
     * The script uses Webpack cache discovery because Discord does not expose a stable public
     * API for quest stores. The lookup is version-tolerant and prefers capability checks over
     * fixed module identifiers.
     *
     * @returns {object} Collection of internal Discord modules required by the runner.
     * @throws {Error} Thrown when the Webpack runtime cannot be extracted.
     */
    const resolveDiscordModules = () => {
        let wpRequire;
        try {
            wpRequire = window.webpackChunkdiscord_app.push([[Symbol()], {}, (runtime) => runtime]);
            window.webpackChunkdiscord_app.pop();
        } catch (error) {
            throw new Error(`Webpack error: ${error.message}`);
        }

        const findModule = (predicate) =>
            Object.values(wpRequire.c).find((entry) => predicate(entry?.exports))?.exports;

        let ApplicationStreamingStore = findModule((exports) => exports?.Z?.__proto__?.getStreamerActiveStreamMetadata)?.Z;
        let RunningGameStore;
        let QuestsStore;
        let ChannelStore;
        let GuildChannelStore;
        let FluxDispatcher;
        let api;

        if (!ApplicationStreamingStore) {
            ApplicationStreamingStore = findModule((exports) => exports?.A?.__proto__?.getStreamerActiveStreamMetadata)?.A;
            RunningGameStore = findModule((exports) => exports?.Ay?.getRunningGames)?.Ay;
            QuestsStore = findModule((exports) => exports?.A?.__proto__?.getQuest)?.A;
            ChannelStore = findModule((exports) => exports?.A?.__proto__?.getAllThreadsForParent)?.A;
            GuildChannelStore = findModule((exports) => exports?.Ay?.getSFWDefaultChannel)?.Ay;
            FluxDispatcher = findModule((exports) => exports?.h?.__proto__?.flushWaitQueue)?.h;
            api = findModule((exports) => exports?.Bo?.get)?.Bo;
        } else {
            RunningGameStore = findModule((exports) => exports?.ZP?.getRunningGames)?.ZP;
            QuestsStore = findModule((exports) => exports?.Z?.__proto__?.getQuest)?.Z;
            ChannelStore = findModule((exports) => exports?.Z?.__proto__?.getAllThreadsForParent)?.Z;
            GuildChannelStore = findModule((exports) => exports?.ZP?.getSFWDefaultChannel)?.ZP;
            FluxDispatcher = findModule((exports) => exports?.Z?.__proto__?.flushWaitQueue)?.Z;
            api = findModule((exports) => exports?.tn?.get)?.tn;
        }

        return {
            ApplicationStreamingStore,
            RunningGameStore,
            QuestsStore,
            ChannelStore,
            GuildChannelStore,
            FluxDispatcher,
            api
        };
    };

    /**
     * Claims a completed quest reward and coordinates captcha assistance when Discord blocks the request.
     *
     * The renderer script emits control markers through console output because DOM inspection and CDP
     * input dispatch happen in different layers. The C# bridge converts those markers into native CDP
     * mouse events without exposing the transport details to the script.
     *
     * @param {any} quest Raw quest object from Discord's internal store.
     * @param {object} modules Resolved Discord module collection.
     * @returns {Promise<void>} Promise resolved after the claim path completes.
     */
    const claimQuest = async (quest, modules) => {
        const { api, QuestsStore } = modules;
        const questName = getQuestName(quest);

        log(`Claiming reward for: ${questName}...`);

        let stopAutoClicker = false;
        const autoClickerTask = (async () => {
            await sleep(600);
            while (!stopAutoClicker) {
                try {
                    const allFrames = Array.from(document.querySelectorAll("iframe"));
                    originalConsole.log(`[DQR] iframe count: ${allFrames.length}`);
                    for (let index = 0; index < allFrames.length; index++) {
                        const frame = allFrames[index];
                        const rect = frame.getBoundingClientRect();
                        originalConsole.log(
                            `[DQR] iframe[${index}] src=${frame.src} title=${frame.title} w=${Math.round(rect.width)} h=${Math.round(rect.height)} l=${Math.round(rect.left)} t=${Math.round(rect.top)}`
                        );
                    }

                    if (document.hidden || allFrames.some((frame) => frame.getBoundingClientRect().top < -100)) {
                        originalConsole.log("[DQR] RESTORE_WINDOW");
                        await sleep(1000);
                    }

                    const targetFrame = allFrames.find((frame) =>
                        (frame.src && frame.src.includes("hcaptcha"))
                        || (frame.title && frame.title.toLowerCase().includes("hcaptcha"))
                    ) || allFrames.find((frame) => {
                        const rect = frame.getBoundingClientRect();
                        return rect.width > 100 && rect.width < 500 && rect.height > 30 && rect.height < 120 && rect.top > 0;
                    });

                    if (targetFrame) {
                        const rect = targetFrame.getBoundingClientRect();
                        const clickX = Math.round(rect.left + 35);
                        const clickY = Math.round(rect.top + rect.height / 2);
                        originalConsole.log(`[DQR] CLICK_CAPTCHA:${clickX},${clickY}`);
                        await sleep(3000);
                    } else {
                        originalConsole.log("[DQR] CLICK_CAPTCHA_NOTFOUND");
                        await sleep(1500);
                    }
                } catch (error) {
                    originalConsole.log(`[DQR] clicker error: ${error.message}`);
                    await sleep(1500);
                }
            }
        })();

        try {
            await api.post({
                url: `/quests/${quest.id}/claim-reward`,
                body: {
                    platform: 0,
                    location: 11,
                    is_targeted: false,
                    metadata_raw: null,
                    metadata_sealed: null
                }
            });
            log(`REWARD CLAIMED: ${questName}`);
        } catch (error) {
            if (error?.body && (error.body.code === 50035 || error.body.captcha_key)) {
                log(`CAPTCHA REQUIRED for ${questName}. Waiting for user to solve...`);
                while (!isCancelled()) {
                    let freshQuest;
                    try {
                        freshQuest = QuestsStore.quests.get(quest.id);
                    } catch {
                        freshQuest = null;
                    }

                    if (freshQuest?.userStatus?.claimedAt) {
                        log(`SUCCESS: Captcha solved. REWARD CLAIMED for ${questName}!`);
                        break;
                    }

                    await sleep(2000);
                }
            } else {
                log(`Claim failed: ${getErrorDetails(error)}`);
            }
        } finally {
            stopAutoClicker = true;
            await autoClickerTask;
        }
    };

    /**
     * Claims rewards for quests that are already completed before new execution begins.
     *
     * @param {object} modules Resolved Discord module collection.
     * @returns {Promise<boolean | undefined>} False when the run is cancelled; otherwise, true or undefined.
     */
    const processPendingClaims = async (modules) => {
        const pendingClaims = [...modules.QuestsStore.quests.values()].filter(
            (quest) => quest.userStatus?.completedAt && !quest.userStatus?.claimedAt
        );

        if (pendingClaims.length === 0) {
            return;
        }

        log(`${pendingClaims.length} pending claims found.`);
        for (const quest of pendingClaims) {
            if (logCancellationAndExit()) {
                return false;
            }

            await claimQuest(quest, modules);
            await sleep(1000);
        }

        return true;
    };

    /**
     * Advances a video quest by sending synthetic progress heartbeats that respect Discord's timing rules.
     *
     * @param {any} quest Raw quest object from Discord's internal store.
     * @param {object} modules Resolved Discord module collection.
     * @param {string} taskName Supported task identifier.
     * @param {number} secondsNeeded Total progress required by the quest.
     * @returns {Promise<void>} Promise resolved after the quest completes or the run is cancelled.
     */
    const runVideoQuest = async (quest, modules, taskName, secondsNeeded) => {
        const { api } = modules;
        const maxFuture = 10;
        const speed = 7;
        const intervalSeconds = 1;
        const enrolledAt = new Date(quest.userStatus.enrolledAt).getTime();
        let secondsDone = getProgressValue(quest, taskName);
        let completed = false;

        while (secondsDone < secondsNeeded) {
            if (logCancellationAndExit()) {
                return;
            }

            const maxAllowed = Math.floor((Date.now() - enrolledAt) / 1000) + maxFuture;
            const difference = maxAllowed - secondsDone;
            const nextTimestamp = secondsDone + speed;

            if (difference >= speed) {
                try {
                    const response = await api.post({
                        url: `/quests/${quest.id}/video-progress`,
                        body: { timestamp: Math.min(secondsNeeded, nextTimestamp + Math.random()) }
                    });
                    completed = response.body.completed_at != null;
                    secondsDone = Math.min(secondsNeeded, nextTimestamp);
                } catch (error) {
                    log(`[ERROR] Video progress failed: ${getErrorDetails(error)}. Retrying...`);
                    await sleep(5000);
                }
            }

            if (secondsDone < secondsNeeded) {
                await sleep(intervalSeconds * 1000);
            }
        }

        if (!completed) {
            try {
                await api.post({
                    url: `/quests/${quest.id}/video-progress`,
                    body: { timestamp: secondsNeeded }
                });
            } catch (error) {
                log(`[ERROR] Final video progress failed: ${getErrorDetails(error)}`);
            }
        }

        log(`Quest completed: ${getQuestName(quest)}`);
        await claimQuest(quest, modules);
    };

    /**
     * Spoofs a desktop game session by patching the running-game store and listening for quest heartbeats.
     *
     * @param {any} quest Raw quest object from Discord's internal store.
     * @param {object} modules Resolved Discord module collection.
     * @param {number} secondsNeeded Total progress required by the quest.
     * @returns {Promise<void>} Promise resolved after the quest completes or the script exits early.
     */
    const runDesktopQuest = async (quest, modules, secondsNeeded) => {
        if (typeof DiscordNative === "undefined") {
            log("This no longer works in browser for non-video quests. Use the discord desktop app!");
            return;
        }

        const { api, RunningGameStore, FluxDispatcher } = modules;
        const applicationId = quest.config.application.id;
        const applicationName = quest.config.application.name;
        const processId = Math.floor(Math.random() * 30000) + 1000;

        try {
            const response = await api.get({
                url: `/applications/public?application_ids=${applicationId}`
            });
            const appData = response.body[0];
            const executableName = appData.executables.find((entry) => entry.os === "win32").name.replace(">", "");

            const fakeGame = {
                cmdLine: `C:\\Program Files\\${appData.name}\\${executableName}`,
                exeName: executableName,
                exePath: `c:/program files/${appData.name.toLowerCase()}/${executableName}`,
                hidden: false,
                isLauncher: false,
                id: applicationId,
                name: appData.name,
                pid: processId,
                pidPath: [processId],
                processName: appData.name,
                start: Date.now()
            };

            const realGames = RunningGameStore.getRunningGames();
            const fakeGames = [fakeGame];
            const realGetRunningGames = RunningGameStore.getRunningGames;
            const realGetGameForPid = RunningGameStore.getGameForPID;

            RunningGameStore.getRunningGames = () => fakeGames;
            RunningGameStore.getGameForPID = (pid) => fakeGames.find((game) => game.pid === pid);
            FluxDispatcher.dispatch({
                type: "RUNNING_GAMES_CHANGE",
                removed: realGames,
                added: [fakeGame],
                games: fakeGames
            });

            await new Promise((resolve) => {
                const handleHeartbeat = (data) => {
                    const progress = quest.config.configVersion === 1
                        ? data.userStatus.streamProgressSeconds
                        : Math.floor(data.userStatus.progress.PLAY_ON_DESKTOP.value);

                    log(`Quest progress: ${progress}/${secondsNeeded}`);
                    if (progress < secondsNeeded) {
                        return;
                    }

                    log("Quest completed!");
                    RunningGameStore.getRunningGames = realGetRunningGames;
                    RunningGameStore.getGameForPID = realGetGameForPid;
                    FluxDispatcher.dispatch({
                        type: "RUNNING_GAMES_CHANGE",
                        removed: [fakeGame],
                        added: [],
                        games: []
                    });
                    FluxDispatcher.unsubscribe("QUESTS_SEND_HEARTBEAT_SUCCESS", handleHeartbeat);
                    claimQuest(quest, modules).then(() => {
                        log("Claimed desktop quest.");
                        resolve();
                    });
                };

                FluxDispatcher.subscribe("QUESTS_SEND_HEARTBEAT_SUCCESS", handleHeartbeat);
                log(`Spoofed your game to ${applicationName}. Wait for ${Math.ceil((secondsNeeded - getProgressValue(quest, "PLAY_ON_DESKTOP")) / 60)} more minutes.`);
            });
        } catch (error) {
            log(`Failed to load application data for ${getQuestName(quest)}: ${getErrorDetails(error)}`);
        }
    };

    /**
     * Spoofs desktop streaming metadata until Discord reports quest completion.
     *
     * @param {any} quest Raw quest object from Discord's internal store.
     * @param {object} modules Resolved Discord module collection.
     * @param {number} secondsNeeded Total progress required by the quest.
     * @returns {Promise<void>} Promise resolved after the quest completes or the script exits early.
     */
    const runStreamQuest = async (quest, modules, secondsNeeded) => {
        if (typeof DiscordNative === "undefined") {
            log("This no longer works in browser. Use desktop app!");
            return;
        }

        const { ApplicationStreamingStore, FluxDispatcher } = modules;
        const applicationId = quest.config.application.id;
        const processId = Math.floor(Math.random() * 30000) + 1000;
        const realMetadataResolver = ApplicationStreamingStore.getStreamerActiveStreamMetadata;

        ApplicationStreamingStore.getStreamerActiveStreamMetadata = () => ({
            id: applicationId,
            pid: processId,
            sourceName: null
        });

        await new Promise((resolve) => {
            const handleHeartbeat = (data) => {
                const progress = quest.config.configVersion === 1
                    ? data.userStatus.streamProgressSeconds
                    : Math.floor(data.userStatus.progress.STREAM_ON_DESKTOP.value);

                log(`Quest progress: ${progress}/${secondsNeeded}`);
                if (progress < secondsNeeded) {
                    return;
                }

                log("Quest completed!");
                ApplicationStreamingStore.getStreamerActiveStreamMetadata = realMetadataResolver;
                FluxDispatcher.unsubscribe("QUESTS_SEND_HEARTBEAT_SUCCESS", handleHeartbeat);
                claimQuest(quest, modules).then(() => {
                    log("Claimed stream quest.");
                    resolve();
                });
            };

            FluxDispatcher.subscribe("QUESTS_SEND_HEARTBEAT_SUCCESS", handleHeartbeat);
            log(`Spoofed stream. Stream in vc for ${Math.ceil((secondsNeeded - getProgressValue(quest, "STREAM_ON_DESKTOP")) / 60)} mins.`);
        });
    };

    /**
     * Advances an activity quest by sending periodic heartbeat payloads against a selected voice channel.
     *
     * @param {any} quest Raw quest object from Discord's internal store.
     * @param {object} modules Resolved Discord module collection.
     * @param {number} secondsNeeded Total progress required by the quest.
     * @returns {Promise<void>} Promise resolved after the quest completes or the run is cancelled.
     */
    const runActivityQuest = async (quest, modules, secondsNeeded) => {
        const { api, ChannelStore, GuildChannelStore } = modules;
        const channelId = ChannelStore.getSortedPrivateChannels()[0]?.id
            ?? Object.values(GuildChannelStore.getAllGuilds()).find((guild) => guild != null && guild.VOCAL.length > 0).VOCAL[0].channel.id;
        const streamKey = `call:${channelId}:1`;

        log("Completing activity quest...");
        try {
            while (true) {
                if (logCancellationAndExit()) {
                    return;
                }

                const response = await api.post({
                    url: `/quests/${quest.id}/heartbeat`,
                    body: { stream_key: streamKey, terminal: false }
                });
                const progress = response.body.progress.PLAY_ACTIVITY.value;
                log(`Quest progress: ${progress}/${secondsNeeded}`);

                if (progress >= secondsNeeded) {
                    await api.post({
                        url: `/quests/${quest.id}/heartbeat`,
                        body: { stream_key: streamKey, terminal: true }
                    });
                    break;
                }

                await sleep(20 * 1000);
            }

            log("Quest completed!");
            await claimQuest(quest, modules);
        } catch (error) {
            log(`Activity quest failed for ${getQuestName(quest)}: ${getErrorDetails(error)}`);
        }
    };

    /**
     * Dispatches a quest to the task-specific execution path selected from its configuration.
     *
     * @param {any} quest Raw quest object from Discord's internal store.
     * @param {object} modules Resolved Discord module collection.
     * @returns {Promise<void>} Promise resolved after the quest handler exits.
     */
    const runQuest = async (quest, modules) => {
        const questName = getQuestName(quest);
        const taskConfig = getTaskConfig(quest);
        const taskName = getTaskName(quest);

        if (!taskName) {
            log(`Unknown task type for ${questName}.`);
            return;
        }

        const secondsNeeded = taskConfig.tasks[taskName].target;
        log(`Starting: ${questName} [${taskName}]`);

        if (taskName === "WATCH_VIDEO" || taskName === "WATCH_VIDEO_ON_MOBILE") {
            await runVideoQuest(quest, modules, taskName, secondsNeeded);
            return;
        }

        if (taskName === "PLAY_ON_DESKTOP") {
            await runDesktopQuest(quest, modules, secondsNeeded);
            return;
        }

        if (taskName === "STREAM_ON_DESKTOP") {
            await runStreamQuest(quest, modules, secondsNeeded);
            return;
        }

        if (taskName === "PLAY_ACTIVITY") {
            await runActivityQuest(quest, modules, secondsNeeded);
            return;
        }

        log(`Unknown task type: ${taskName}`);
    };

    try {
        log("--- QUEST RUNNER & CLAIMER (V3) ---");

        if (logCancellationAndExit()) {
            return internalLog;
        }

        const modules = resolveDiscordModules();
        const activeQuests = [...modules.QuestsStore.quests.values()].filter((quest) =>
            quest.userStatus?.enrolledAt
            && !quest.userStatus?.completedAt
            && new Date(quest.config.expiresAt).getTime() > Date.now()
            && supportedTasks.find((task) => Object.keys(getTaskConfig(quest).tasks).includes(task))
        );

        const claimsProcessed = await processPendingClaims(modules);
        if (claimsProcessed === false) {
            return internalLog;
        }

        if (activeQuests.length === 0) {
            log("No uncompleted quests found.");
        } else {
            log(`${activeQuests.length} active quests found. Starting runner...`);

            for (let index = activeQuests.length - 1; index >= 0; index--) {
                if (logCancellationAndExit()) {
                    return internalLog;
                }

                await runQuest(activeQuests[index], modules);
            }

            if (!isCancelled()) {
                log("All jobs done. Sequence complete.");
            }
        }

        await sleep(2000);
        return internalLog;
    } catch (error) {
        return `Global Error: ${error.message}`;
    } finally {
        if (window[stateKey]?.runId === runId) {
            delete window[stateKey];
        }
    }
})();
