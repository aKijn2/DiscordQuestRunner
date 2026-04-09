(async () => {
    /**
     * Captures Discord's internal Webpack runtime so the preflight check can verify that
     * the renderer still exposes the modules required by the automation payloads.
     * @returns {any | null} Webpack runtime require function when available; otherwise, null.
     */
    const captureWebpackRuntime = () => {
        if (!Array.isArray(globalThis.webpackChunkdiscord_app)) {
            return null;
        }

        let wpRequire = null;
        globalThis.webpackChunkdiscord_app.push([
            [Symbol("dqr-preflight")],
            {},
            (runtime) => {
                wpRequire = runtime;
            },
        ]);

        return wpRequire;
    };

    /**
     * Determines whether Discord's internal REST client is still discoverable in the module cache.
     * @param {any} wpRequire Discord's Webpack runtime require function.
     * @returns {boolean} True when a compatible REST client export is available.
     */
    const hasRestApiModule = (wpRequire) =>
        Object.values(wpRequire?.c ?? {}).some((entry) => {
            const api = entry?.exports?.tn;
            return typeof api?.get === "function" && typeof api?.post === "function";
        });

    /**
     * Determines whether the quests store used by the runner is still discoverable.
     * @param {any} wpRequire Discord's Webpack runtime require function.
     * @returns {boolean} True when a compatible quests store export is available.
     */
    const hasQuestsStoreModule = (wpRequire) =>
        Object.values(wpRequire?.c ?? {}).some((entry) =>
            Boolean(entry?.exports?.Z?.__proto__?.getQuest)
            || Boolean(entry?.exports?.A?.__proto__?.getQuest));

    /**
     * Serializes the probe outcome so the CDP bridge can parse it as a primitive result value.
     * @param {boolean} ok Whether the probe succeeded for all discovered capabilities.
     * @param {string} detail Technical detail describing the probe outcome.
     * @param {boolean} hasWebpackRuntime Whether the Webpack runtime was resolved.
     * @param {boolean} hasRestApi Whether the REST client module was resolved.
     * @param {boolean} hasQuestsStore Whether the quests store module was resolved.
     * @returns {string} JSON payload describing the probe outcome.
     */
    const serializeResult = (ok, detail, hasWebpackRuntime, hasRestApi, hasQuestsStore) =>
        JSON.stringify({
            ok,
            detail,
            hasWebpackRuntime,
            hasRestApi,
            hasQuestsStore,
        });

    try {
        const wpRequire = captureWebpackRuntime();
        if (!wpRequire?.c) {
            return serializeResult(
                false,
                "Discord Webpack runtime is not available yet.",
                false,
                false,
                false);
        }

        const restApiReady = hasRestApiModule(wpRequire);
        const questsStoreReady = hasQuestsStoreModule(wpRequire);

        if (restApiReady && questsStoreReady) {
            return serializeResult(
                true,
                "Required Discord automation modules are available.",
                true,
                true,
                true);
        }

        const missing = [];
        if (!restApiReady) {
            missing.push("REST API");
        }

        if (!questsStoreReady) {
            missing.push("Quests store");
        }

        return serializeResult(
            false,
            `Missing automation modules: ${missing.join(", ")}.`,
            true,
            restApiReady,
            questsStoreReady);
    } catch (error) {
        return serializeResult(
            false,
            error?.message ?? String(error),
            false,
            false,
            false);
    }
})()
