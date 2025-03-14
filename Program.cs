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
        var url = new Uri(inputUrl == null || inputUrl.Length <= 0 ? "ws://localhost:6798" : inputUrl);

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
                await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                client.Dispose();
            }
        }
    }

    static async Task ReadMessage()
    {
        var receiveBuffer = new byte[65535];
        var result = await client.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), CancellationToken.None);
        var receivedMessage = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);
        string[] split = receivedMessage.Split("\n");
        var message = string.Join("\n",split, 1, split.Length - 1);
        Logger.Info($"MSG-ID: {split[0]}");
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
        var content = $"{message.messageId}\n{message.content}";
        Logger.Info($"Sending message {content}");
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

        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:11434/api/chat")
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
                    while (!reader.EndOfStream && !done)
                    {
                        var line = await reader.ReadLineAsync();
                        if (line == null) continue; 
                        
                        var oRes = JsonSerializer.Deserialize<OllamaResponse>(line);
                        done = oRes.done;

                        queue.Add(new Message()
                        {
                            content = oRes.message.content,
                            messageId = messageId.ToString()
                        });

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

            Dictionary<string, string> messages = new Dictionary<string, string>();
            foreach (Message line in lines)
            {
                if (messages.ContainsKey(line.messageId))
                {
                    messages[line.messageId] += line.content;
                }
                else
                {
                    messages.Add(line.messageId, line.content);
                }
            }

            List<Task> tasks = new List<Task>();
            foreach (KeyValuePair<string, string> message in messages)
            {
                tasks.Add(SendMessage(new Message()
                {
                    content = message.Value,
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
    }
}
