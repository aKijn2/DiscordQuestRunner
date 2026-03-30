using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiscordQuestRunner.Services
{
    // â”€â”€â”€ Enums & Models â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public enum LogLevel { Info, Success, Warning, Error, Script }

    public record CdpTarget(
        string Title,
        string Type,
        string Url,
        string WebSocketDebuggerUrl
    );

    public record ScriptResult(bool Success, string? Output, string? Error);

    // â”€â”€â”€ Custom Exceptions â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public sealed class DiscordNotFoundException()
        : Exception("Discord process was not found. Please launch Discord first.");

    public sealed class DebugPortException(string reason)
        : Exception($"Debug port unavailable: {reason}. Restart Discord with debug mode enabled.");

    public sealed class CdpTargetException()
        : Exception("No valid Discord CDP target found. Ensure Discord is fully loaded.");

    // â”€â”€â”€ Logger Delegate â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Structured log callback. Provides both raw message and severity.
    /// </summary>
    public delegate void LogHandler(string message, LogLevel level = LogLevel.Info);

    // â”€â”€â”€ DiscordService â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public sealed class DiscordService : IDisposable
    {
        // â”€â”€ Constants â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private const int DEBUG_PORT = 9222;
        private const string DEBUG_BASE_URL = "http://127.0.0.1:9222";
        private const int RESTART_POLL_SECS = 15;
        private const int WS_BUFFER_BYTES = 1024 * 32; // 32 KB â€“ handles large Discord payloads
        private const int SCRIPT_COMMAND_ID = 100;

        // Telemetry strings we silently drop from Discord's console output
        private static readonly string[] _noiseFilters =
        [
            "%c[", "[FAST CONNECT]", "audio subsystem",
            "service release channel", "libdiscore",
            "[Notification]", "GatewaySocket",
        ];

        // â”€â”€ HTTP â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(4),
        };

        private bool _disposed;

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  Script loading
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        /// <summary>Loads a bundled script from Resources/Raw/Scripts/.</summary>
        public static async Task<string> LoadScriptAsync(string fileName)
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync($"Scripts/{fileName}");
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }

        /// <summary>Loads a script and prepends a DQR console banner.</summary>
        public static async Task<string> LoadScriptWithBannerAsync(string fileName)
        {
            var script = await LoadScriptAsync(fileName);
            return $"console.log('[DQR] Loaded script asset: {fileName}');\n{script}";
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  Debug-port health check
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        /// <summary>
        /// Returns whether Discord is running and the CDP debug port is reachable.
        /// Throws typed exceptions so callers can react specifically.
        /// </summary>
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

        /// <summary>Convenience wrapper â€“ returns a simple bool instead of throwing.</summary>
        public async Task<bool> IsHealthyAsync()
        {
            try { await CheckHealthAsync(); return true; }
            catch { return false; }
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  Discord restart
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        /// <summary>
        /// Kills Discord and relaunches it with <c>--remote-debugging-port</c> set.
        /// Polls until the port is ready or the timeout expires.
        /// </summary>
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

        private static void TerminateDiscordProcesses()
        {
            foreach (var p in Process.GetProcessesByName("Discord"))
            {
                try { p.Kill(entireProcessTree: true); }
                catch { /* ignore ghost processes */ }
            }
        }

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

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  CDP target discovery
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        /// <summary>
        /// Resolves the best Discord CDP page target and returns its WebSocket URL.
        /// Priority: exact "Discord" title â†’ /channels/ URL â†’ any non-devtools page.
        /// </summary>
        public async Task<(CdpTarget target, string message)> ResolveTargetAsync()
        {
            string json;
            try
            {
                json = await _http.GetStringAsync($"{DEBUG_BASE_URL}/json");
            }
            catch (Exception ex)
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

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  Script execution via CDP WebSocket
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        /// <summary>
        /// Opens a CDP WebSocket connection, injects <paramref name="script"/>,
        /// streams console output to <paramref name="log"/>, and returns the result.
        /// </summary>
        public async Task<ScriptResult> ExecuteScriptAsync(
            string wsUrl,
            string script,
            LogHandler log,
            CancellationToken ct = default)
        {
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri(wsUrl), ct);

            // Enable Runtime domain so console.log events flow back to us
            await SendCdpAsync(ws, 1, "Runtime.enable", new { }, ct);

            // Fire the script; awaitPromise handles async scripts correctly
            await SendCdpAsync(ws, SCRIPT_COMMAND_ID, "Runtime.evaluate",
                new { expression = script, awaitPromise = true }, ct);

            return await DrainMessagesAsync(ws, log, ct);
        }

        private async Task<ScriptResult> DrainMessagesAsync(
            ClientWebSocket ws,
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

                // â”€â”€ Console log events â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                if (method == "Runtime.consoleAPICalled")
                {
                    var argsNode = root["params"]?["args"] as JsonArray;
var msg = "";
if (argsNode != null)
{
    var stringParts = new System.Collections.Generic.List<string>();
    foreach (var arg in argsNode)
    {
        var val = arg?["value"]?.ToString() ?? "";
        stringParts.Add(val);
    }
    msg = string.Join(" ", stringParts);
}
                    if (!string.IsNullOrWhiteSpace(msg) && !IsNoise(msg))
                    {
                        var m = msg.Trim();

                        // Strip the [DQR SCRIPT] wrapper the JS log helper prepends
                        const string scriptPrefix = "[DQR SCRIPT] ";
                        var rawPayload = m.StartsWith(scriptPrefix)
                            ? m.Substring(scriptPrefix.Length).Trim()
                            : m;

                        if (rawPayload.StartsWith("[DQR] CLICK_CAPTCHA:"))
                        {
                            try
                            {
                                var coords = rawPayload.Substring("[DQR] CLICK_CAPTCHA:".Length).Split(',');
                                if (coords.Length == 2 && int.TryParse(coords[0].Trim(), out int cx) && int.TryParse(coords[1].Trim(), out int cy))
                                {
                                    log($"Auto-clicking Captcha at X:{cx} Y:{cy}...", LogLevel.Success);

                                    // Non-blocking so the websocket reader loop is never stalled
                                    _ = Task.Run(async () =>
                                    {
                                        try
                                        {
                                            await SendCdpAsync(ws, 9001, "Input.dispatchMouseEvent", new { type = "mouseMoved", x = cx, y = cy }, ct);
                                            await Task.Delay(80, ct);
                                            await SendCdpAsync(ws, 9002, "Input.dispatchMouseEvent", new { type = "mousePressed", x = cx, y = cy, button = "left", clickCount = 1 }, ct);
                                            await Task.Delay(80, ct);
                                            await SendCdpAsync(ws, 9003, "Input.dispatchMouseEvent", new { type = "mouseReleased", x = cx, y = cy, button = "left", clickCount = 1 }, ct);
                                        }
                                        catch { }
                                    }, ct);

                                    continue;
                                }
                            }
                            catch { }
                        }

                        log(m, LogLevel.Script);
                    }
                    continue;
                }


                // â”€â”€ Final script result â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  WebSocket helpers
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        /// <summary>Reads a complete (potentially multi-chunk) WebSocket frame.</summary>
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

        private static async Task SendCdpAsync(
            ClientWebSocket ws,
            int id,
            string method,
            object @params,
            CancellationToken ct = default)
        {
            var payload = new { id, method, @params };
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }

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

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  Noise filter
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        private static bool IsNoise(string message) =>
            _noiseFilters.Any(f => message.Contains(f, StringComparison.OrdinalIgnoreCase));

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  IDisposable
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // HttpClient is static/shared â€“ do not dispose here.
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  Backward-compatibility shims
        //  These keep existing code-behind files compiling without changes.
        //  New code should call the primary methods above directly.
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        /// <inheritdoc cref="LoadScriptWithBannerAsync"/>
        public static Task<string> LoadScriptWithDebugBannerAsync(string fileName)
            => LoadScriptWithBannerAsync(fileName);

        /// <summary>
        /// Legacy overload: accepts a plain <see cref="Action{String}"/> instead of
        /// <see cref="LogHandler"/>. All log entries are forwarded with
        /// <see cref="LogLevel.Script"/>.
        /// </summary>
        public Task<ScriptResult> ExecuteScriptAsync(
            string wsUrl,
            string script,
            Action<string> onLog,
            CancellationToken ct = default)
            => ExecuteScriptAsync(wsUrl, script, (msg, _) => onLog(msg), ct);

        /// <summary>
        /// Legacy signature: returns (isReady, processFound, message) tuple.
        /// Prefer <see cref="CheckHealthAsync"/> or <see cref="IsHealthyAsync"/>.
        /// </summary>
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
        /// Legacy signature: returns (success, message) tuple.
        /// Prefer <see cref="RestartWithDebugAsync"/>.
        /// </summary>
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
        /// Legacy signature: returns (success, message, wsUrl) tuple.
        /// Prefer <see cref="ResolveTargetAsync"/>.
        /// </summary>
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

        // â”€â”€ Private DTO for JSON deserialisation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private sealed class RawCdpPage
        {
            public string? webSocketDebuggerUrl { get; set; }
            public string? type { get; set; }
            public string? title { get; set; }
            public string? url { get; set; }
        }
    }
}
