using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

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

        /// <summary>
        /// Checks whether Discord is running with the debug port accessible.
        /// </summary>
        public async Task<(bool isReady, bool processFound, string message)> CheckDebugPortAsync()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("Discord");
                if (processes.Length == 0)
                    return (false, false, "Discord process not found.");

                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var response = await client.GetAsync($"{DEBUG_URL}/json/version");
                if (!response.IsSuccessStatusCode)
                    return (false, true, "Debug port unreachable. Discord restart required.");

                return (true, true, "Debug port accessible.");
            }
            catch
            {
                return (false, true, "Debug port 9222 unreachable.");
            }
        }

        /// <summary>
        /// Kills Discord and restarts it with the remote debugging port enabled.
        /// </summary>
        public async Task<(bool success, string message)> RestartDiscordAsync(Action<string>? onLog = null)
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("Discord");
                foreach (var p in processes)
                {
                    try { p.Kill(); } catch { }
                }
                await Task.Delay(1500);

                string discordPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord");
                if (!Directory.Exists(discordPath))
                    return (false, "Discord installation path not found.");

                var appDirs = Directory.GetDirectories(discordPath, "app-*");
                if (appDirs.Length == 0)
                    return (false, "No Discord version folder found.");

                string latestApp = appDirs.OrderByDescending(d => d).First();
                string exePath = Path.Combine(latestApp, "Discord.exe");
                if (!File.Exists(exePath))
                    return (false, $"Discord.exe not found at: {exePath}");

                Process.Start(exePath, $"--remote-debugging-port={DEBUG_PORT}");
                onLog?.Invoke($"Restarting: {exePath}");

                // Poll until debug port becomes available
                for (int i = 0; i < 15; i++)
                {
                    await Task.Delay(1000);
                    var check = await CheckDebugPortAsync();
                    if (check.isReady)
                        return (true, "Discord restarted with debug port enabled.");
                }

                return (false, "Discord restarted but debug port did not become available in time.");
            }
            catch (Exception ex)
            {
                return (false, $"Restart failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds the best Discord CDP target and returns its WebSocket URL.
        /// </summary>
        public async Task<(bool success, string message, string wsUrl)> InitConnectionAsync()
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                string json = await client.GetStringAsync($"{DEBUG_URL}/json");
                var pages = JsonSerializer.Deserialize<List<CdpResponse>>(json);

                if (pages == null || pages.Count == 0)
                    return (false, "No CDP targets found.", "");

                // 1. Filter to pages, exclude DevTools
                var candidates = pages
                    .Where(p => p.type == "page"
                        && !string.IsNullOrEmpty(p.webSocketDebuggerUrl)
                        && !(p.url?.StartsWith("devtools://") ?? false))
                    .ToList();

                // 2. Prioritize "Discord" title or main channels URL
                var page = candidates.FirstOrDefault(p =>
                    p.title == "Discord" || (p.url?.Contains("/channels/") ?? false));

                // 3. Fallback to any non-devtools page
                page ??= candidates.FirstOrDefault();

                // 4. Last resort: any target with "Discord" in title
                page ??= pages.FirstOrDefault(p =>
                    p.title?.Contains("Discord") == true
                    && !(p.url?.StartsWith("devtools://") ?? false)
                    && !string.IsNullOrEmpty(p.webSocketDebuggerUrl));

                if (page == null)
                    return (false, "No valid Discord target found. Make sure Discord is open.", "");

                string targetInfo = $"{page.type} - {page.title}";
                return (true, $"Attached to: {targetInfo}", page.webSocketDebuggerUrl!);
            }
            catch (Exception ex)
            {
                return (false, $"Connection failed: {ex.Message}", "");
            }
        }

        /// <summary>
        /// Executes a JavaScript script via CDP WebSocket with proper message framing.
        /// </summary>
        public async Task ExecuteScriptAsync(string wsUrl, string script, Action<string> onLog, CancellationToken cancellationToken = default)
        {
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri(wsUrl), cancellationToken);

            // Enable Runtime events for console.log capture
            await SendCommandAsync(ws, 1, "Runtime.enable", new { }, cancellationToken);

            // Execute the script
            await SendCommandAsync(ws, 100, "Runtime.evaluate", new
            {
                expression = script,
                awaitPromise = true
            }, cancellationToken);

            var buffer = new byte[1024 * 8];

            while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                // Accumulate WebSocket fragments into a complete message
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close) break;

                string responseJson = Encoding.UTF8.GetString(ms.ToArray());

                try
                {
                    using JsonDocument doc = JsonDocument.Parse(responseJson);
                    var root = doc.RootElement;

                    // Handle Console Logs (Real-time)
                    if (root.TryGetProperty("method", out var method)
                        && method.GetString() == "Runtime.consoleAPICalled")
                    {
                        if (root.TryGetProperty("params", out var p)
                            && p.TryGetProperty("args", out var args)
                            && args.GetArrayLength() > 0)
                        {
                            string logMsg = args[0].TryGetProperty("value", out var v)
                                ? v.GetString() ?? "" : "";
                            if (!string.IsNullOrWhiteSpace(logMsg))
                                onLog(logMsg);
                        }
                    }

                    // Handle Script Result (Final)
                    if (root.TryGetProperty("id", out var id) && id.GetInt32() == 100)
                    {
                        if (root.TryGetProperty("result", out var res)
                            && res.TryGetProperty("result", out var innerRes)
                            && innerRes.TryGetProperty("value", out var val))
                        {
                            string output = val.GetString() ?? "";
                            if (!string.IsNullOrWhiteSpace(output))
                                onLog(output);
                        }

                        // Report script exceptions
                        if (root.TryGetProperty("result", out var res2)
                            && res2.TryGetProperty("exceptionDetails", out var exc))
                        {
                            string excText = exc.TryGetProperty("text", out var t)
                                ? t.GetString() ?? "Unknown error" : "Script exception";
                            onLog($"ERROR: {excText}");
                        }

                        break;
                    }
                }
                catch (JsonException)
                {
                    // Malformed JSON frame - skip
                }
            }

            if (ws.State == WebSocketState.Open)
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
        }

        private static async Task SendCommandAsync(ClientWebSocket ws, int id, string method, object @params, CancellationToken cancellationToken = default)
        {
            var cmd = new { id, method, @params };
            string json = JsonSerializer.Serialize(cmd);
            var bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
        }
    }
}
