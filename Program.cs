using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

class Program
{
    static ClientWebSocket client;
    static List<Message> queue = new List<Message>();

    static async Task Main()
    {
        Logger.Info("Enter Connection URL. Leave empty for default.");
        var inputUrl = Console.ReadLine();

        Logger.Info("Enter Username. Leave empty for default.");
        var userName = Console.ReadLine();
        userName = userName.Length > 0 ? userName : "Ollama";
        Logger.Info("Enter Password. Leave empty for default.");
        var password = Console.ReadLine();
        password = password?.Length > 0 ? password : "password";

        var login = new
        {
            name = userName,
            password
        };
        string json = JsonSerializer.Serialize(login);
        StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

        var httpClient = new HttpClient();
        var result = await httpClient.PostAsync("http://localhost/api/identity/login", content);

        var sessionId = "";
        foreach (var header in result.Headers)
        {
            if (header.Key.ToLower() == "set-cookie")
            {
                sessionId = string.Join("", header.Value).Substring(10, 36);
                Logger.Info($"{sessionId}");
            }
        }

        if (sessionId == null || sessionId.Length <= 0)
        {
            Logger.Error("No SessionId. Exiting...");
            return;
        }

        var url = new Uri((inputUrl == null || inputUrl.Length <= 0 ? "ws://localhost/api/chatrouter" : inputUrl) + "?sessionId=" + sessionId);

        while (true)
        {
            using (client = new ClientWebSocket())
            {
                client.Options.KeepAliveInterval = TimeSpan.FromHours(2);
                try
                {
                    Logger.Info("Trying to establish connection...");
                    await client.ConnectAsync(url, CancellationToken.None);
                    Logger.Info("WebSocket connection established.");

                    Task[] tasks = {
                    Task.Run(async () =>
                    {
                        while (client.State == WebSocketState.Open)
                        {
                            await ReadMessage();
                        }
                        Logger.Warn("Done Reading");
                    }),
                    Task.Run(async () =>
                    {
                        while(client.State == WebSocketState.Open)
                        {
                            await HandleQueue();
                        }
                        Logger.Warn("Done Handling");
                    })
                };

                    Task.WaitAll(tasks);

                }
                catch (Exception ex)
                {
                    Logger.Error(ex.Message);
                    if (client.State == WebSocketState.Open)
                    {
                        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    }
                    client.Dispose();

                    Logger.Info("Trying to reconnect...");
                }
            }
        }
    }

    static async Task ReadMessage()
    {
        var receiveBuffer = new byte[65535];
        var result = await client.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), CancellationToken.None);
        var receivedMessage = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);
        string[] split = receivedMessage.Split("\n");
        var message = string.Join("\n",split, 2, split.Length - 2);
        Logger.Info($"MSG-ID: {split[0]}");
        Logger.Info($"MSG-VS: {split[1]}");
        Logger.Message(message);
        await SendToOllama(message);
    }

    static async Task SendMessage(Message message)
    {
        if(message.content.Length <= 0)
        {
            Logger.Warn("Not Sending Empty Message");
            return;
        }
        var content = $"{message.messageId}\n{message.version}\n{message.content}";
        Logger.Info($"Sending message\n{content}");
        try
        {
            var buffer = new ArraySegment<byte>(Encoding.UTF8.GetBytes(content));
            Logger.Info("Converted Message.");
            await client.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
            Logger.Info("Sent Message.");
        }
        catch (Exception e)
        {
            Logger.Error(e.Message);
        }
    }

    static async Task SendToOllama(string message)
    {
        var httpClient = new HttpClient();
        var payloadObj = new OllamaPayload
        {
            model = "deepseek-r1:7B",
            options = new OllamaOptions
            {
                temperature = 1.0
            },
            stream = true,
            messages = new OllamaMessage[1]{
                new OllamaMessage
                {
                    role = "user",
                    content = message
                }
            }
        };

        var payload = JsonSerializer.Serialize(payloadObj);

        Logger.Info(payload);

        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/ai/chat")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        try
        {
            using (var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    bool done = false;
                    Guid messageId = Guid.NewGuid();
                    int version = 0;
                    while (!reader.EndOfStream && !done)
                    {
                        var line = await reader.ReadLineAsync();
                        if (line == null) continue; 
                        
                        var oRes = JsonSerializer.Deserialize<OllamaResponse>(line);
                        done = oRes.done;

                        queue.Add(new Message()
                        {
                            content = oRes.message.content,
                            messageId = messageId.ToString(),
                            version = version,
                        });

                        version++;

                        Logger.Info(oRes.message.content);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex.Message);
        }
    }

    static async Task HandleQueue()
    {
        if(queue.Count > 0)
        {
            Message[] lines = new Message[queue.Count];
            queue.CopyTo(lines);
            queue.Clear();

            Dictionary<string, VersionedContent> messages = new Dictionary<string, VersionedContent>();
            foreach (Message line in lines)
            {
                if (messages.ContainsKey(line.messageId))
                {
                    messages[line.messageId] = new VersionedContent()
                    {
                        content = messages[line.messageId].content + line.content,
                        version = messages[line.messageId].version,
                    };
                }
                else
                {
                    messages.Add(line.messageId, new VersionedContent()
                    {
                        content = line.content,
                        version = line.version,
                    });
                }
            }

            List<Task> tasks = new List<Task>();
            foreach (KeyValuePair<string, VersionedContent> message in messages)
            {
                tasks.Add(SendMessage(new Message()
                {
                    content = message.Value.content,
                    version = message.Value.version,
                    messageId = message.Key
                }));
            }
            Task.WaitAll(tasks.ToArray());
        }

        await Task.Delay(100);
    }

    struct OllamaPayload
    {
        public string model { get; set; }
        public bool stream { get; set; }
        public OllamaOptions options { get; set; }
        public OllamaMessage[] messages { get; set; }
    }

    struct OllamaMessage
    {
        public string role { get; set; }
        public string content { get; set; }
    }

    struct OllamaOptions
    {
        public double temperature { get; set; }
    }

    struct OllamaResponse
    {
        public string model { set; get; }
        public string created_at { get; set; }
        public OllamaMessage message { get; set; }
        public bool done { get; set; }
        public string? done_reason { get; set; }
        public long? total_duration { get; set; }
        public long? load_duration { get; set; }
        public int? prompt_eval_count { get; set; }
        public long? prompt_eval_duration { get; set; }
        public int? eval_count { get; set; }
        public long? eval_duration { get; set; }
    }

    struct Message
    {
        public string messageId { set; get; }
        public string content { get; set; }
        public int version { get; set; }
    }

    struct VersionedContent
    {
        public string content { get; set; }
        public int version { get; set; }
    }
}
