using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes; // Added for cleaner JSON parsing

namespace DiscordQuestRunner.Services
{
    public class DiscordService
    {
        public class CdpResponse
        {
            public string? webSocketDebuggerUrl { get; set; }
            public string? type { get; set; }
            public string? title { get; set; }
            public string? url { get; set; }
        }

        private const int DEBUG_PORT = 9222;
        private const string DEBUG_URL = "http://127.0.0.1:9222";

        // 1. Singleton HttpClient to prevent socket exhaustion
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(3),
        };

        /// <summary>
        /// Loads a JavaScript file from the app's bundled Resources/Raw/Scripts folder.
        /// </summary>
        public static async Task<string> LoadScriptAsync(string fileName)
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync($"Scripts/{fileName}");
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }

        /// <summary>
        /// Checks whether Discord is running with the debug port accessible.
        /// </summary>
        public async Task<(bool isReady, bool processFound, string message)> CheckDebugPortAsync()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("Discord");
                if (processes.Length == 0)
                    return (false, false, "Discord process not found in system memory.");

                var response = await _httpClient.GetAsync($"{DEBUG_URL}/json/version");
                if (!response.IsSuccessStatusCode)
                    return (false, true, "Debug port blocked or inactive. Restart required.");

                return (true, true, "Debug port verified and active.");
            }
            catch
            {
                return (false, true, $"Debug port {DEBUG_PORT} unreachable.");
            }
        }

        /// <summary>
        /// Kills Discord and restarts it with the remote debugging port enabled.
        /// </summary>
        public async Task<(bool success, string message)> RestartDiscordAsync(
            Action<string>? onLog = null
        )
        {
            try
            {
                // 2. Aggressive process termination (kills the whole process tree)
                Process[] processes = Process.GetProcessesByName("Discord");
                foreach (var p in processes)
                {
                    try
                    {
                        p.Kill(entireProcessTree: true);
                    }
                    catch
                    { /* Ignore access denied on ghost processes */
                    }
                }

                // Give Windows a moment to release file locks
                await Task.Delay(2000);

                string discordPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Discord"
                );

                if (!Directory.Exists(discordPath))
                    return (false, "Discord installation directory not found.");

                var appDirs = Directory.GetDirectories(discordPath, "app-*");
                if (appDirs.Length == 0)
                    return (false, "No valid Discord version folder found.");

                string latestApp = appDirs.OrderByDescending(d => d).First();
                string exePath = Path.Combine(latestApp, "Discord.exe");

                if (!File.Exists(exePath))
                    return (false, $"Discord executable missing at: {exePath}");

                // Launch with remote debugging
                Process.Start(exePath, $"--remote-debugging-port={DEBUG_PORT}");
                onLog?.Invoke($"[SYS] Executing: {exePath}");

                // Poll until debug port becomes available
                for (int i = 0; i < 15; i++)
                {
                    await Task.Delay(1000);
                    var check = await CheckDebugPortAsync();
                    if (check.isReady)
                        return (true, "Discord successfully rebooted in Debug Mode.");
                }

                return (false, "Discord restarted, but debug port initialization timed out.");
            }
            catch (Exception ex)
            {
                return (false, $"Restart protocol failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds the best Discord CDP target and returns its WebSocket URL.
        /// </summary>
        public async Task<(bool success, string message, string wsUrl)> InitConnectionAsync()
        {
            try
            {
                string json = await _httpClient.GetStringAsync($"{DEBUG_URL}/json");
                var pages = JsonSerializer.Deserialize<List<CdpResponse>>(json);

                if (pages == null || pages.Count == 0)
                    return (false, "No active CDP targets found.", "");

                var candidates = pages
                    .Where(p =>
                        p.type == "page"
                        && !string.IsNullOrEmpty(p.webSocketDebuggerUrl)
                        && !(p.url?.StartsWith("devtools://") ?? false)
                    )
                    .ToList();

                var page = candidates.FirstOrDefault(p =>
                    p.title == "Discord" || (p.url?.Contains("/channels/") ?? false)
                );
                page ??= candidates.FirstOrDefault();
                page ??= pages.FirstOrDefault(p =>
                    p.title?.Contains("Discord") == true
                    && !(p.url?.StartsWith("devtools://") ?? false)
                    && !string.IsNullOrEmpty(p.webSocketDebuggerUrl)
                );

                if (page == null)
                    return (false, "No valid Discord interface target located.", "");

                return (true, $"Attached to target: {page.title}", page.webSocketDebuggerUrl!);
            }
            catch (Exception ex)
            {
                return (false, $"Handshake failed: {ex.Message}", "");
            }
        }

        /// <summary>
        /// Executes a JavaScript script via CDP WebSocket with proper message framing.
        /// </summary>
        public async Task ExecuteScriptAsync(
            string wsUrl,
            string script,
            Action<string> onLog,
            CancellationToken cancellationToken = default
        )
        {
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri(wsUrl), cancellationToken);

            // Enable Runtime events for console.log capture
            await SendCommandAsync(ws, 1, "Runtime.enable", new { }, cancellationToken);

            // Execute the script
            await SendCommandAsync(
                ws,
                100,
                "Runtime.evaluate",
                new { expression = script, awaitPromise = true },
                cancellationToken
            );

            var buffer = new byte[1024 * 16]; // Increased buffer size for larger data payloads

            while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        cancellationToken
                    );
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                string responseJson = Encoding.UTF8.GetString(ms.ToArray());

                try
                {
                    // 3. Upgraded to JsonNode for much cleaner, null-safe parsing
                    var root = JsonNode.Parse(responseJson);
                    if (root == null)
                        continue;

                    string? method = root["method"]?.ToString();
                    int? id = root["id"]?.GetValue<int>();

                    // Handle Real-time Console Logs
                    if (method == "Runtime.consoleAPICalled")
                    {
                        var logMsg = root["params"]?["args"]?[0]?["value"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(logMsg))
                        {
                            onLog(logMsg);
                        }
                    }

                    // Handle Final Script Result (ID: 100)
                    if (id == 100)
                    {
                        // Check for successful return value
                        var scriptOutput = root["result"]?["result"]?["value"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(scriptOutput))
                        {
                            onLog(scriptOutput);
                        }

                        // Check for script exceptions
                        var exceptionText = root["result"]
                            ?["exceptionDetails"]?["text"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(exceptionText))
                        {
                            onLog($"[ERROR] Script Exception: {exceptionText}");
                        }

                        break; // Execution complete, exit loop
                    }
                }
                catch (JsonException)
                {
                    // Malformed JSON frame - skip
                }
            }

            if (ws.State == WebSocketState.Open)
            {
                await ws.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Execution Complete",
                    CancellationToken.None
                );
            }
        }

        private static async Task SendCommandAsync(
            ClientWebSocket ws,
            int id,
            string method,
            object @params,
            CancellationToken cancellationToken = default
        )
        {
            var cmd = new
            {
                id,
                method,
                @params,
            };
            string json = JsonSerializer.Serialize(cmd);
            var bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                cancellationToken
            );
        }
    }
}
