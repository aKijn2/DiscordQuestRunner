using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiscordQuestRunner.Services
{
    /// <summary>
    /// Describes the severity of a log entry emitted during Discord automation.
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        /// Indicates a neutral informational event.
        /// </summary>
        Info,

        /// <summary>
        /// Indicates a successful operation.
        /// </summary>
        Success,

        /// <summary>
        /// Indicates a recoverable warning condition.
        /// </summary>
        Warning,

        /// <summary>
        /// Indicates a failure or unrecoverable error.
        /// </summary>
        Error,

        /// <summary>
        /// Indicates output forwarded from the injected JavaScript runtime.
        /// </summary>
        Script
    }

    /// <summary>
    /// Represents a debuggable Chromium page exposed by Discord's remote debugging endpoint.
    /// </summary>
    /// <param name="Title">Visible title reported by the target.</param>
    /// <param name="Type">CDP target type reported by Chromium.</param>
    /// <param name="Url">Current page URL.</param>
    /// <param name="WebSocketDebuggerUrl">WebSocket endpoint used for CDP commands.</param>
    public record CdpTarget(
        string Title,
        string Type,
        string Url,
        string WebSocketDebuggerUrl
    );

    /// <summary>
    /// Represents the final result of a CDP script evaluation request.
    /// </summary>
    /// <param name="Success">Whether the evaluation completed without an exception payload.</param>
    /// <param name="Output">Returned script value when the script completed successfully.</param>
    /// <param name="Error">Exception text reported by CDP when evaluation failed.</param>
    public record ScriptResult(bool Success, string? Output, string? Error);

    /// <summary>
    /// Thrown when no running Discord process can be located.
    /// </summary>
    public sealed class DiscordNotFoundException()
        : Exception("Discord process was not found. Please launch Discord first.");

    /// <summary>
    /// Thrown when Discord's remote debugging port cannot be reached.
    /// </summary>
    /// <param name="reason">Low-level failure detail reported while probing the port.</param>
    public sealed class DebugPortException(string reason)
        : Exception($"Debug port unavailable: {reason}. Restart Discord with debug mode enabled.");

    /// <summary>
    /// Thrown when no usable Discord CDP page target can be selected.
    /// </summary>
    public sealed class CdpTargetException()
        : Exception("No valid Discord CDP target found. Ensure Discord is fully loaded.");

    /// <summary>
    /// Receives structured log entries from the service and its injected scripts.
    /// </summary>
    /// <param name="message">Log text emitted by the service or script runtime.</param>
    /// <param name="level">Severity associated with the message.</param>
    public delegate void LogHandler(string message, LogLevel level = LogLevel.Info);

    /// <summary>
    /// Manages Discord process discovery, CDP target selection, script loading, and WebSocket-based script execution.
    /// </summary>
    public sealed class DiscordService : IDisposable
    {
        private const int DEBUG_PORT = 9222;
        private const string DEBUG_BASE_URL = "http://127.0.0.1:9222";
        private const int RESTART_POLL_SECS = 15;
        private const int WS_BUFFER_BYTES = 1024 * 32; // 32 KB handles large Discord payloads
        private const int RUNTIME_ENABLE_COMMAND_ID = 1;
        private const int SCRIPT_COMMAND_ID = 100;
        private const int CAPTCHA_MOUSE_MOVE_COMMAND_ID = 9001;
        private const int CAPTCHA_MOUSE_DOWN_COMMAND_ID = 9002;
        private const int CAPTCHA_MOUSE_UP_COMMAND_ID = 9003;

        // Telemetry strings we silently drop from Discord's console output
        private static readonly string[] _noiseFilters =
        [
            "%c[", "[FAST CONNECT]", "audio subsystem",
            "service release channel", "libdiscore",
            "[Notification]", "GatewaySocket",
        ];
        private static readonly LogHandler _ignoreLogHandler = static (_, _) => { };

        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(4),
        };
        private static readonly ConcurrentDictionary<string, Task<string>> _scriptCache =
            new(StringComparer.OrdinalIgnoreCase);

        private bool _disposed;

        //  Script loading

        /// <summary>
        /// Loads a packaged JavaScript asset from the MAUI app bundle and caches the result for reuse.
        /// </summary>
        /// <param name="fileName">Asset filename stored under <c>Resources/Raw/Automation</c>.</param>
        /// <returns>The script content.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the requested asset is not packaged.</exception>
        /// <exception cref="DirectoryNotFoundException">Thrown when the automation asset folder cannot be resolved.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the packaged asset stream cannot be opened.</exception>
        public static async Task<string> LoadScriptAsync(string fileName)
        {
            if (_scriptCache.TryGetValue(fileName, out var cachedScript))
            {
                return await cachedScript;
            }

            var loadTask = LoadScriptCoreAsync(fileName);
            if (!_scriptCache.TryAdd(fileName, loadTask))
            {
                return await _scriptCache[fileName];
            }

            try
            {
                return await loadTask;
            }
            catch
            {
                _scriptCache.TryRemove(fileName, out _);
                throw;
            }
        }

        /// <summary>
        /// Loads a packaged script and prepends a banner so the CDP log stream can identify the active asset.
        /// </summary>
        /// <param name="fileName">Asset filename stored under <c>Resources/Raw/Automation</c>.</param>
        /// <returns>The wrapped script content.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the requested asset is not packaged.</exception>
        /// <exception cref="DirectoryNotFoundException">Thrown when the automation asset folder cannot be resolved.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the packaged asset stream cannot be opened.</exception>
        public static async Task<string> LoadScriptWithBannerAsync(string fileName)
        {
            var script = await LoadScriptAsync(fileName);
            return $"console.log('[DQR] Loaded script asset: {fileName}');\n{script}";
        }

        /// <summary>
        /// Reads a script from the packaged MAUI asset store without consulting the cache.
        /// </summary>
        /// <param name="fileName">Asset filename stored under <c>Resources/Raw/Automation</c>.</param>
        /// <returns>The script content.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the requested asset is not packaged.</exception>
        /// <exception cref="DirectoryNotFoundException">Thrown when the automation asset folder cannot be resolved.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the packaged asset stream cannot be opened.</exception>
        private static async Task<string> LoadScriptCoreAsync(string fileName)
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync(
                $"Automation/{fileName}");
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }

        //  Debug-port health check

        /// <summary>
        /// Verifies that Discord is running and that the remote debugging endpoint responds.
        /// </summary>
        /// <returns>A task that completes when the health check succeeds.</returns>
        /// <exception cref="DiscordNotFoundException">Thrown when no Discord process is running.</exception>
        /// <exception cref="DebugPortException">Thrown when the debug port is unreachable or returns a non-success status.</exception>
        public async Task CheckHealthAsync()
        {
            var procs = Process.GetProcessesByName("Discord");
            if (procs.Length == 0)
                throw new DiscordNotFoundException();

            try
            {
                var response = await _http.GetAsync($"{DEBUG_BASE_URL}/json/version");
                if (!response.IsSuccessStatusCode)
                    throw new DebugPortException("port returned non-success status");
            }
            catch (HttpRequestException ex)
            {
                throw new DebugPortException(ex.Message);
            }
            catch (TaskCanceledException)
            {
                throw new DebugPortException("request timed out");
            }
        }

        /// <summary>
        /// Checks whether Discord is reachable through the debug port without propagating typed exceptions.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> when the process and debug port are available; otherwise, <see langword="false"/>.
        /// </returns>
        public async Task<bool> IsHealthyAsync()
        {
            try { await CheckHealthAsync(); return true; }
            catch { return false; }
        }

        /// <summary>
        /// Runs a lightweight environment validation flow before automation starts.
        /// </summary>
        /// <param name="requiredCapabilities">Automation capabilities that must be available for the caller.</param>
        /// <param name="ct">Token that cancels the preflight run.</param>
        /// <returns>An ordered report describing each stage of the validation flow.</returns>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="ct"/> is cancelled.</exception>
        public async Task<DiscordPreflightReport> RunPreflightAsync(
            DiscordAutomationCapability requiredCapabilities,
            CancellationToken ct = default)
        {
            var steps = new List<DiscordPreflightStep>();

            var processes = Process.GetProcessesByName("Discord");
            if (processes.Length == 0)
            {
                steps.Add(new DiscordPreflightStep(
                    DiscordPreflightStage.Process,
                    false,
                    "Discord process not found. Launch Discord and open the desktop client first."));
                return new DiscordPreflightReport(null, steps);
            }

            steps.Add(new DiscordPreflightStep(
                DiscordPreflightStage.Process,
                true,
                $"Discord process detected ({processes.Length} instance(s))."));

            try
            {
                await CheckHealthAsync();
                steps.Add(new DiscordPreflightStep(
                    DiscordPreflightStage.DebugPort,
                    true,
                    $"CDP debug port {DEBUG_PORT} is reachable."));
            }
            catch (Exception ex) when (ex is DebugPortException or DiscordNotFoundException)
            {
                steps.Add(new DiscordPreflightStep(
                    DiscordPreflightStage.DebugPort,
                    false,
                    ex.Message));
                return new DiscordPreflightReport(null, steps);
            }

            CdpTarget target;
            try
            {
                (target, _) = await ResolveTargetAsync();
                steps.Add(new DiscordPreflightStep(
                    DiscordPreflightStage.Target,
                    true,
                    $"Renderer target ready: {target.Title}."));
            }
            catch (Exception ex) when (ex is CdpTargetException)
            {
                steps.Add(new DiscordPreflightStep(
                    DiscordPreflightStage.Target,
                    false,
                    ex.Message));
                return new DiscordPreflightReport(null, steps);
            }

            if (requiredCapabilities == DiscordAutomationCapability.None)
            {
                return new DiscordPreflightReport(target.WebSocketDebuggerUrl, steps);
            }

            var probeResult = await ProbeAutomationSurfaceAsync(
                target.WebSocketDebuggerUrl,
                requiredCapabilities,
                ct);

            steps.Add(new DiscordPreflightStep(
                DiscordPreflightStage.AutomationSurface,
                probeResult.Success,
                probeResult.Message));

            return new DiscordPreflightReport(target.WebSocketDebuggerUrl, steps);
        }

        //  Discord restart

        /// <summary>
        /// Restarts Discord with the remote debugging switch enabled and waits for the debug endpoint to become ready.
        /// </summary>
        /// <param name="log">Optional log sink used to report restart progress.</param>
        /// <returns>A task that completes when the restarted process exposes the debug endpoint.</returns>
        /// <exception cref="DirectoryNotFoundException">Thrown when the Discord installation folder cannot be located.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the Discord executable cannot be located.</exception>
        /// <exception cref="DebugPortException">Thrown when the restarted process never exposes the debug endpoint.</exception>
        public async Task RestartWithDebugAsync(LogHandler? log = null)
        {
            TerminateDiscordProcesses();
            await Task.Delay(2_000); // let Windows release file locks

            string exePath = FindDiscordExecutable();
            log?.Invoke($"Executing: {exePath}", LogLevel.Info);

            Process.Start(exePath, $"--remote-debugging-port={DEBUG_PORT}");

            for (int i = 0; i < RESTART_POLL_SECS; i++)
            {
                await Task.Delay(1_000);
                if (await IsHealthyAsync())
                {
                    log?.Invoke("Discord restarted in debug mode.", LogLevel.Success);
                    return;
                }
            }

            throw new DebugPortException("debug port did not become available after restart");
        }

        /// <summary>
        /// Terminates all running Discord processes before a debug-mode restart.
        /// </summary>
        private static void TerminateDiscordProcesses()
        {
            foreach (var p in Process.GetProcessesByName("Discord"))
            {
                try { p.Kill(entireProcessTree: true); }
                catch { /* ignore ghost processes */ }
            }
        }

        /// <summary>
        /// Resolves the newest Discord installation executable from the local app data folder.
        /// </summary>
        /// <returns>The absolute path to <c>Discord.exe</c>.</returns>
        /// <exception cref="DirectoryNotFoundException">
        /// Thrown when the Discord installation root or versioned application folder cannot be located.
        /// </exception>
        /// <exception cref="FileNotFoundException">Thrown when the resolved executable does not exist.</exception>
        private static string FindDiscordExecutable()
        {
            string discordRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Discord"
            );

            if (!Directory.Exists(discordRoot))
                throw new DirectoryNotFoundException("Discord installation directory not found.");

            var appDirs = Directory.GetDirectories(discordRoot, "app-*");
            if (appDirs.Length == 0)
                throw new DirectoryNotFoundException("No Discord version folder found.");

            string exePath = Path.Combine(
                appDirs.OrderByDescending(d => d).First(),
                "Discord.exe"
            );

            if (!File.Exists(exePath))
                throw new FileNotFoundException("Discord executable missing.", exePath);

            return exePath;
        }

        //  CDP target discovery

        /// <summary>
        /// Selects the most relevant Discord page target exposed by Chromium's debug endpoint.
        /// </summary>
        /// <returns>The selected target and a status message describing the attachment.</returns>
        /// <exception cref="CdpTargetException">
        /// Thrown when the target list cannot be queried or no suitable page target exists.
        /// </exception>
        public async Task<(CdpTarget target, string message)> ResolveTargetAsync()
        {
            string json;
            try
            {
                json = await _http.GetStringAsync($"{DEBUG_BASE_URL}/json");
            }
            catch (Exception)
            {
                throw new CdpTargetException(); // wrap low-level HTTP error
            }

            var pages = JsonSerializer.Deserialize<List<RawCdpPage>>(json) ?? [];

            var candidates = pages
                .Where(p =>
                    p.type == "page"
                    && !string.IsNullOrEmpty(p.webSocketDebuggerUrl)
                    && !(p.url?.StartsWith("devtools://") ?? false))
                .ToList();

            var best =
                candidates.FirstOrDefault(p => p.title == "Discord" || p.url?.Contains("/channels/") == true)
                ?? candidates.FirstOrDefault()
                ?? pages.FirstOrDefault(p =>
                    p.title?.Contains("Discord") == true
                    && !(p.url?.StartsWith("devtools://") ?? false)
                    && !string.IsNullOrEmpty(p.webSocketDebuggerUrl));

            if (best is null)
                throw new CdpTargetException();

            var target = new CdpTarget(
                best.title ?? "Unknown",
                best.type ?? "page",
                best.url ?? "",
                best.webSocketDebuggerUrl!
            );

            return (target, $"Attached to target: {target.Title}");
        }

        //  Script execution via CDP WebSocket

        /// <summary>
        /// Connects to the selected Discord CDP target and evaluates a JavaScript payload.
        /// </summary>
        /// <param name="wsUrl">CDP WebSocket target URL.</param>
        /// <param name="script">JavaScript payload to evaluate inside the Discord renderer.</param>
        /// <param name="log">Sink that receives forwarded console output and service messages.</param>
        /// <param name="ct">Token that cancels the WebSocket session.</param>
        /// <returns>The final script result returned by CDP.</returns>
        /// <exception cref="UriFormatException">Thrown when <paramref name="wsUrl"/> is not a valid absolute URI.</exception>
        /// <exception cref="WebSocketException">Thrown when the WebSocket connection cannot be established.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="ct"/> is cancelled.</exception>
        public async Task<ScriptResult> ExecuteScriptAsync(
            string wsUrl,
            string script,
            LogHandler log,
            CancellationToken ct = default)
        {
            using var ws = new ClientWebSocket();
            using var sendLock = new SemaphoreSlim(1, 1);
            await ws.ConnectAsync(new Uri(wsUrl), ct);

            // Enable Runtime domain so console.log events flow back to us
            await SendCdpAsync(
                ws,
                sendLock,
                RUNTIME_ENABLE_COMMAND_ID,
                "Runtime.enable",
                new { },
                ct);

            // Fire the script; awaitPromise handles async scripts correctly
            await SendCdpAsync(
                ws,
                sendLock,
                SCRIPT_COMMAND_ID,
                "Runtime.evaluate",
                new { expression = script, awaitPromise = true },
                ct);

            return await DrainMessagesAsync(ws, sendLock, log, ct);
        }

        /// <summary>
        /// Reads CDP frames until the evaluation result arrives and forwards console output to the caller.
        /// </summary>
        /// <param name="ws">Connected CDP WebSocket client.</param>
        /// <param name="sendLock">Semaphore that serializes outbound CDP commands.</param>
        /// <param name="log">Sink that receives forwarded console output and service messages.</param>
        /// <param name="ct">Token that cancels the WebSocket session.</param>
        /// <returns>The final script result returned by CDP.</returns>
        private async Task<ScriptResult> DrainMessagesAsync(
            ClientWebSocket ws,
            SemaphoreSlim sendLock,
            LogHandler log,
            CancellationToken ct)
        {
            var buffer = new byte[WS_BUFFER_BYTES];
            string? scriptOutput = null;
            string? scriptError = null;

            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                string frame;
                try
                {
                    frame = await ReceiveFullFrameAsync(ws, buffer, ct);
                }
                catch (WebSocketException) { break; }
                catch (OperationCanceledException) { break; }

                var root = JsonNode.Parse(frame);
                if (root is null) continue;

                string? method = root["method"]?.ToString();
                int? id = root["id"]?.GetValue<int>();

                // Console log events
                if (method == "Runtime.consoleAPICalled")
                {
                    if (TryExtractConsoleMessage(root, out var message)
                        && !string.IsNullOrWhiteSpace(message)
                        && !IsNoise(message))
                    {
                        var m = message.Trim();

                        // The injected scripts prefix console output so the bridge can distinguish
                        // operational markers from Discord's own renderer logs.
                        const string scriptPrefix = "[DQR SCRIPT] ";
                        var rawPayload = m.StartsWith(scriptPrefix)
                            ? m.Substring(scriptPrefix.Length).Trim()
                            : m;

                        if (await TryHandleControlPayloadAsync(
                            rawPayload,
                            ws,
                            sendLock,
                            log,
                            ct))
                        {
                            continue;
                        }

                        log(m, LogLevel.Script);
                    }
                    continue;
                }

                // Final script result
                if (id == SCRIPT_COMMAND_ID)
                {
                    scriptOutput = root["result"]?["result"]?["value"]?.ToString();
                    scriptError = root["result"]?["exceptionDetails"]?["text"]?.ToString();

                    if (!string.IsNullOrWhiteSpace(scriptOutput))
                        log(scriptOutput, LogLevel.Success);
                    if (!string.IsNullOrWhiteSpace(scriptError))
                        log($"Script exception: {scriptError}", LogLevel.Error);

                    break; // done
                }
            }

            await SafeCloseAsync(ws);

            return new ScriptResult(
                Success: string.IsNullOrWhiteSpace(scriptError),
                Output: scriptOutput,
                Error: scriptError
            );
        }

        /// <summary>
        /// Extracts a human-readable console message from a <c>Runtime.consoleAPICalled</c> payload.
        /// </summary>
        /// <param name="root">Parsed CDP message node.</param>
        /// <param name="message">Concatenated console message when extraction succeeds.</param>
        /// <returns>
        /// <see langword="true"/> when the CDP payload contains console arguments; otherwise, <see langword="false"/>.
        /// </returns>
        private static bool TryExtractConsoleMessage(JsonNode root, out string message)
        {
            var argsNode = root["params"]?["args"] as JsonArray;
            if (argsNode is null)
            {
                message = string.Empty;
                return false;
            }

            var stringParts = new List<string>();
            foreach (var arg in argsNode)
            {
                stringParts.Add(arg?["value"]?.ToString() ?? string.Empty);
            }

            message = string.Join(" ", stringParts);
            return true;
        }

        /// <summary>
        /// Handles service-specific control payloads emitted by the injected scripts.
        /// </summary>
        /// <param name="rawPayload">Console payload emitted by the script after the service banner has been removed.</param>
        /// <param name="ws">Connected CDP WebSocket client.</param>
        /// <param name="sendLock">Semaphore that serializes outbound CDP commands.</param>
        /// <param name="log">Sink that receives forwarded service messages.</param>
        /// <param name="ct">Token that cancels any follow-up CDP input dispatch.</param>
        /// <returns>
        /// <see langword="true"/> when the payload was consumed by the service; otherwise, <see langword="false"/>.
        /// </returns>
        private static async Task<bool> TryHandleControlPayloadAsync(
            string rawPayload,
            ClientWebSocket ws,
            SemaphoreSlim sendLock,
            LogHandler log,
            CancellationToken ct)
        {
            if (rawPayload == "[DQR] RESTORE_WINDOW")
            {
                var mainProcess = Process.GetProcessesByName("Discord")
                    .FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);

                if (mainProcess != null)
                {
                    log(
                        "Discord logic minimized. Restoring window to perform UI click...",
                        LogLevel.Warning);
                    WindowHelper.FocusWindow(mainProcess.MainWindowHandle);
                }

                return true;
            }

            if (rawPayload == "[DQR] CLICK_CAPTCHA_NOTFOUND")
            {
                log("Captcha iframe not found in DOM yet - waiting...", LogLevel.Warning);
                return true;
            }

            if (!rawPayload.StartsWith("[DQR] CLICK_CAPTCHA:", StringComparison.Ordinal))
            {
                return false;
            }

            var coords = rawPayload
                .Substring("[DQR] CLICK_CAPTCHA:".Length)
                .Split(',');

            if (coords.Length != 2
                || !int.TryParse(coords[0].Trim(), out var clickX)
                || !int.TryParse(coords[1].Trim(), out var clickY))
            {
                return false;
            }

            log($"Auto-clicking Captcha at X:{clickX} Y:{clickY}...", LogLevel.Success);

            // The script emits DOM-derived coordinates through console output because CDP script
            // evaluation and CDP input dispatch use different protocol commands. The service
            // converts the console marker into native CDP mouse events without blocking the read loop.
            _ = Task.Run(
                () => DispatchCaptchaClickAsync(ws, sendLock, clickX, clickY, ct),
                CancellationToken.None);

            return true;
        }

        /// <summary>
        /// Sends a synthesized mouse move and click sequence through CDP.
        /// </summary>
        /// <param name="ws">Connected CDP WebSocket client.</param>
        /// <param name="sendLock">Semaphore that serializes outbound CDP commands.</param>
        /// <param name="clickX">Horizontal click coordinate in renderer space.</param>
        /// <param name="clickY">Vertical click coordinate in renderer space.</param>
        /// <param name="ct">Token that cancels the click sequence.</param>
        /// <returns>A task that completes when the click sequence finishes.</returns>
        private static async Task DispatchCaptchaClickAsync(
            ClientWebSocket ws,
            SemaphoreSlim sendLock,
            int clickX,
            int clickY,
            CancellationToken ct)
        {
            try
            {
                await SendCdpAsync(
                    ws,
                    sendLock,
                    CAPTCHA_MOUSE_MOVE_COMMAND_ID,
                    "Input.dispatchMouseEvent",
                    new { type = "mouseMoved", x = clickX, y = clickY },
                    ct);
                await Task.Delay(80, ct);
                await SendCdpAsync(
                    ws,
                    sendLock,
                    CAPTCHA_MOUSE_DOWN_COMMAND_ID,
                    "Input.dispatchMouseEvent",
                    new
                    {
                        type = "mousePressed",
                        x = clickX,
                        y = clickY,
                        button = "left",
                        clickCount = 1
                    },
                    ct);
                await Task.Delay(80, ct);
                await SendCdpAsync(
                    ws,
                    sendLock,
                    CAPTCHA_MOUSE_UP_COMMAND_ID,
                    "Input.dispatchMouseEvent",
                    new
                    {
                        type = "mouseReleased",
                        x = clickX,
                        y = clickY,
                        button = "left",
                        clickCount = 1
                    },
                    ct);
            }
            catch
            {
                // Best-effort click sequence.
            }
        }

        //  WebSocket helpers

        /// <summary>
        /// Reads a complete WebSocket message, including messages fragmented across multiple CDP frames.
        /// </summary>
        /// <param name="ws">Connected CDP WebSocket client.</param>
        /// <param name="buffer">Reusable receive buffer.</param>
        /// <param name="ct">Token that cancels the receive operation.</param>
        /// <returns>The decoded UTF-8 message payload.</returns>
        /// <exception cref="WebSocketException">Thrown when the receive operation fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="ct"/> is cancelled.</exception>
        private static async Task<string> ReceiveFullFrameAsync(
            ClientWebSocket ws,
            byte[] buffer,
            CancellationToken ct)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            return Encoding.UTF8.GetString(ms.ToArray());
        }

        /// <summary>
        /// Sends a single CDP command over the shared WebSocket connection.
        /// </summary>
        /// <param name="ws">Connected CDP WebSocket client.</param>
        /// <param name="sendLock">Semaphore that serializes outbound CDP commands.</param>
        /// <param name="id">CDP command identifier.</param>
        /// <param name="method">CDP method name.</param>
        /// <param name="params">CDP method parameters.</param>
        /// <param name="ct">Token that cancels the send operation.</param>
        /// <returns>A task that completes when the command has been transmitted.</returns>
        /// <exception cref="WebSocketException">Thrown when the send operation fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="ct"/> is cancelled.</exception>
        private static async Task SendCdpAsync(
            ClientWebSocket ws,
            SemaphoreSlim sendLock,
            int id,
            string method,
            object @params,
            CancellationToken ct = default)
        {
            var payload = new { id, method, @params };
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));

            await sendLock.WaitAsync(ct);
            try
            {
                await ws.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    ct);
            }
            finally
            {
                sendLock.Release();
            }
        }

        /// <summary>
        /// Closes the CDP WebSocket connection without surfacing cleanup failures.
        /// </summary>
        /// <param name="ws">Connected CDP WebSocket client.</param>
        /// <returns>A task that completes after the best-effort close sequence.</returns>
        private static async Task SafeCloseAsync(ClientWebSocket ws)
        {
            if (ws.State == WebSocketState.Open)
            {
                try
                {
                    await ws.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Execution Complete",
                        CancellationToken.None);
                }
                catch { /* best-effort */ }
            }
        }

        /// <summary>
        /// Filters high-volume Discord telemetry that is not useful to the application log.
        /// </summary>
        /// <param name="message">Console message emitted by the renderer.</param>
        /// <returns>
        /// <see langword="true"/> when the message should be suppressed; otherwise, <see langword="false"/>.
        /// </returns>
        private static bool IsNoise(string message) =>
            _noiseFilters.Any(f => message.Contains(f, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Validates that the target renderer still exposes the internal Discord modules required by automation.
        /// </summary>
        /// <param name="wsUrl">CDP WebSocket target URL.</param>
        /// <param name="requiredCapabilities">Capabilities that must resolve successfully.</param>
        /// <param name="ct">Token that cancels the renderer probe.</param>
        /// <returns>A tuple describing whether the automation surface is ready and why.</returns>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="ct"/> is cancelled.</exception>
        private async Task<(bool Success, string Message)> ProbeAutomationSurfaceAsync(
            string wsUrl,
            DiscordAutomationCapability requiredCapabilities,
            CancellationToken ct)
        {
            var probeScript = await LoadScriptAsync(DiscordScriptCatalog.PreflightProbe);
            var scriptResult = await ExecuteScriptAsync(
                wsUrl,
                probeScript,
                _ignoreLogHandler,
                ct);

            if (!scriptResult.Success)
            {
                return (false, $"Automation probe failed: {scriptResult.Error ?? "Unknown renderer error."}");
            }

            if (string.IsNullOrWhiteSpace(scriptResult.Output))
            {
                return (false, "Automation probe returned no result.");
            }

            AutomationProbePayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<AutomationProbePayload>(scriptResult.Output);
            }
            catch (JsonException)
            {
                return (false, "Automation probe returned malformed data.");
            }

            if (payload is null)
            {
                return (false, "Automation probe returned an empty payload.");
            }

            var missingCapabilities = new List<string>();
            if (requiredCapabilities.HasFlag(DiscordAutomationCapability.RestApi) && !payload.HasRestApi)
            {
                missingCapabilities.Add("REST API");
            }

            if (requiredCapabilities.HasFlag(DiscordAutomationCapability.QuestsStore) && !payload.HasQuestsStore)
            {
                missingCapabilities.Add("Quests store");
            }

            if (missingCapabilities.Count == 0)
            {
                return (true, $"Automation surface verified for {DescribeCapabilities(requiredCapabilities)}.");
            }

            return (
                false,
                $"{BuildCapabilityFailureMessage(requiredCapabilities, missingCapabilities)} {payload.Detail}".Trim());
        }

        /// <summary>
        /// Formats a concise failure message for missing automation capabilities.
        /// </summary>
        /// <param name="requiredCapabilities">Capabilities requested by the caller.</param>
        /// <param name="missingCapabilities">Capabilities that were not resolved during probing.</param>
        /// <returns>A user-facing failure string.</returns>
        private static string BuildCapabilityFailureMessage(
            DiscordAutomationCapability requiredCapabilities,
            IReadOnlyList<string> missingCapabilities) =>
            $"Automation surface incomplete for {DescribeCapabilities(requiredCapabilities)}: missing {string.Join(", ", missingCapabilities)}.";

        /// <summary>
        /// Formats the capability flags into a user-facing label.
        /// </summary>
        /// <param name="capabilities">Capabilities to describe.</param>
        /// <returns>A short label describing the requested capability set.</returns>
        private static string DescribeCapabilities(DiscordAutomationCapability capabilities)
        {
            var labels = new List<string>();

            if (capabilities.HasFlag(DiscordAutomationCapability.RestApi))
            {
                labels.Add("REST API");
            }

            if (capabilities.HasFlag(DiscordAutomationCapability.QuestsStore))
            {
                labels.Add("Quests store");
            }

            return labels.Count == 0
                ? "base startup checks"
                : string.Join(" + ", labels);
        }

        /// <summary>
        /// Marks the service as disposed.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // HttpClient is static/shared - do not dispose here.
        }

        /// <summary>
        /// Loads a script with the legacy method name retained for existing callers.
        /// </summary>
        /// <param name="fileName">Asset filename stored under <c>Resources/Raw/Automation</c>.</param>
        /// <returns>The wrapped script content.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the requested asset is not packaged.</exception>
        /// <exception cref="DirectoryNotFoundException">Thrown when the automation asset folder cannot be resolved.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the packaged asset stream cannot be opened.</exception>
        public static Task<string> LoadScriptWithDebugBannerAsync(string fileName)
            => LoadScriptWithBannerAsync(fileName);

        /// <summary>
        /// Executes a script using a legacy log callback signature.
        /// </summary>
        /// <param name="wsUrl">CDP WebSocket target URL.</param>
        /// <param name="script">JavaScript payload to evaluate inside the Discord renderer.</param>
        /// <param name="onLog">Sink that receives string-only log output.</param>
        /// <param name="ct">Token that cancels the WebSocket session.</param>
        /// <returns>The final script result returned by CDP.</returns>
        /// <exception cref="UriFormatException">Thrown when <paramref name="wsUrl"/> is not a valid absolute URI.</exception>
        /// <exception cref="WebSocketException">Thrown when the WebSocket connection cannot be established.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="ct"/> is cancelled.</exception>
        public Task<ScriptResult> ExecuteScriptAsync(
            string wsUrl,
            string script,
            Action<string> onLog,
            CancellationToken ct = default)
            => ExecuteScriptAsync(wsUrl, script, (msg, _) => onLog(msg), ct);

        /// <summary>
        /// Performs the legacy debug-port readiness check and returns status as a tuple.
        /// </summary>
        /// <returns>
        /// A tuple describing whether the port is ready, whether the process exists, and a status message.
        /// </returns>
        public async Task<(bool isReady, bool processFound, string message)> CheckDebugPortAsync()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("Discord");
                if (processes.Length == 0)
                    return (false, false, "Discord process not found in system memory.");

                await CheckHealthAsync();
                return (true, true, "Debug port verified and active.");
            }
            catch (DebugPortException ex)
            {
                return (false, true, ex.Message);
            }
            catch
            {
                return (false, true, $"Debug port {DEBUG_PORT} unreachable.");
            }
        }

        /// <summary>
        /// Restarts Discord using the legacy tuple-based return contract.
        /// </summary>
        /// <param name="onLog">Optional sink that receives string-only restart progress output.</param>
        /// <returns>A tuple describing whether the restart succeeded and the resulting status message.</returns>
        public async Task<(bool success, string message)> RestartDiscordAsync(
            Action<string>? onLog = null)
        {
            try
            {
                LogHandler? handler = onLog is null
                    ? null
                    : (msg, _) => onLog(msg);

                await RestartWithDebugAsync(handler);
                return (true, "Discord successfully rebooted in Debug Mode.");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Resolves a CDP target using the legacy tuple-based return contract.
        /// </summary>
        /// <returns>A tuple describing success, a status message, and the WebSocket URL when available.</returns>
        public async Task<(bool success, string message, string wsUrl)> InitConnectionAsync()
        {
            try
            {
                var (target, message) = await ResolveTargetAsync();
                return (true, message, target.WebSocketDebuggerUrl);
            }
            catch (CdpTargetException ex)
            {
                return (false, ex.Message, "");
            }
            catch (Exception ex)
            {
                return (false, $"Handshake failed: {ex.Message}", "");
            }
        }

        /// <summary>
        /// Represents a raw CDP target object returned by Chromium's JSON endpoint.
        /// </summary>
        private sealed class RawCdpPage
        {
            /// <summary>
            /// Gets or sets the WebSocket endpoint used to communicate with the target.
            /// </summary>
            public string? webSocketDebuggerUrl { get; set; }

            /// <summary>
            /// Gets or sets the CDP target type.
            /// </summary>
            public string? type { get; set; }

            /// <summary>
            /// Gets or sets the visible title of the target.
            /// </summary>
            public string? title { get; set; }

            /// <summary>
            /// Gets or sets the URL loaded by the target.
            /// </summary>
            public string? url { get; set; }
        }

        /// <summary>
        /// Represents the serialized startup probe response returned by the renderer.
        /// </summary>
        private sealed class AutomationProbePayload
        {
            /// <summary>
            /// Gets or sets a value indicating whether the probe considered the surface fully ready.
            /// </summary>
            public bool Ok { get; set; }

            /// <summary>
            /// Gets or sets the diagnostic detail emitted by the probe.
            /// </summary>
            public string? Detail { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether the Webpack runtime was resolved.
            /// </summary>
            public bool HasWebpackRuntime { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether the internal REST client was resolved.
            /// </summary>
            public bool HasRestApi { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether the quests store was resolved.
            /// </summary>
            public bool HasQuestsStore { get; set; }
        }
    }
}
