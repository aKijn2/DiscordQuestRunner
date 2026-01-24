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
        }

        public class CdpCommand
        {
            public int id { get; set; }
            public string? method { get; set; }
            public object? @params { get; set; }
        }

        public class CdpParamsParams
        {
            public string? expression { get; set; }
            public bool awaitPromise { get; set; }
        }

        private const int DEBUG_PORT = 9222;
        private const string DEBUG_URL = "http://127.0.0.1:9222";

        public async Task<(bool success, string message, string wsUrl)> InitConnectionAsync()
        {
            try
            {
                // 1. Check Process
                Process[] processes = Process.GetProcessesByName("Discord");
                if (processes.Length == 0) return (false, "Discord process not found.", "");

                // 2. Check Port
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) })
                {
                    var response = await client.GetAsync($"{DEBUG_URL}/json/version");
                    if (!response.IsSuccessStatusCode) return (false, "Debug port unreachable (Restart required).", "");
                }

                // 3. Get WebSocket URL
                using (var client = new HttpClient())
                {
                    string json = await client.GetStringAsync($"{DEBUG_URL}/json");
                    var pages = JsonSerializer.Deserialize<List<CdpResponse>>(json);
                    var page = pages?.FirstOrDefault(p => !string.IsNullOrEmpty(p.webSocketDebuggerUrl));
                    
                    if (page == null) return (false, "No accessible tabs found.", "");
                    return (true, "Connected", page.webSocketDebuggerUrl!);
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message, "");
            }
        }

        public async Task ExecuteScriptAsync(string wsUrl, string script, Action<string> onLog)
        {
            using (var ws = new ClientWebSocket())
            {
                await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);

                // 1. Enable Runtime events to get console.log messages
                await SendCommandAsync(ws, 1, "Runtime.enable", new { });

                // 2. Execute the script
                var evaluateCmd = new CdpCommand
                {
                    id = 100,
                    method = "Runtime.evaluate",
                    @params = new CdpParamsParams
                    {
                        expression = script,
                        awaitPromise = true
                    }
                };

                await SendCommandAsync(ws, evaluateCmd.id, evaluateCmd.method, evaluateCmd.@params!);
                
                var buffer = new byte[1024 * 64]; 
                
                // Read responses until WebSocket closes or evaluation finishes
                while(ws.State == WebSocketState.Open) {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close) break;

                    string responseJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    
                    try {
                        using (JsonDocument doc = JsonDocument.Parse(responseJson))
                        {
                            var root = doc.RootElement;

                            // Handle Console Logs (Real-time)
                            if (root.TryGetProperty("method", out var method) && method.GetString() == "Runtime.consoleAPICalled")
                            {
                                if (root.TryGetProperty("params", out var p) && p.TryGetProperty("args", out var args) && args.GetArrayLength() > 0)
                                {
                                    string logMsg = args[0].TryGetProperty("value", out var v) ? v.GetString() ?? "" : "";
                                    if (!string.IsNullOrWhiteSpace(logMsg))
                                        onLog(logMsg);
                                }
                            }

                            // Handle Script Result (Final)
                            if (root.TryGetProperty("id", out var id) && id.GetInt32() == 100)
                            {
                                if (root.TryGetProperty("result", out var res) && res.TryGetProperty("result", out var innerRes) && innerRes.TryGetProperty("value", out var val))
                                {
                                    string output = val.GetString() ?? "";
                                    if (!string.IsNullOrWhiteSpace(output))
                                        onLog(output);
                                }
                                break; // Evaluation finished
                            }
                        }
                    } catch { }
                }

                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
            }
        }

        private async Task SendCommandAsync(ClientWebSocket ws, int id, string method, object @params)
        {
            var cmd = new { id, method, @params };
            string json = JsonSerializer.Serialize(cmd);
            var bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }
}
