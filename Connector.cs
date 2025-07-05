using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

public class Connector
{
    ClientWebSocket client;
    List<ServerMessage> queue = new List<ServerMessage>();
    string sessionId = "";
    User currentUser;
    OllamaConfig config;
    NotesManager notes;
    bool stopped = false;

    public Connector(OllamaConfig config, NotesManager notes)
    {
        this.config = config;
        this.notes = notes;
    }

    public async Task Run()
    {
        var login = new
        {
            name = config.Name,
            password = config.Password,
        };
        string json = JsonSerializer.Serialize(login);
        StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

        var httpClient = new HttpClient();
        Logger.Info("Getting Session...");
        var result = await httpClient.PostAsync("http://localhost/api/identity/login", content);

        sessionId = "";
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

        httpClient.DefaultRequestHeaders.Add("Cookie", "sessionId=" + sessionId);
        Logger.Info("Getting User info...");
        var validResult = await httpClient.GetAsync("http://localhost/api/identity/login/valid");
        if (validResult.IsSuccessStatusCode)
        {
            currentUser = JsonSerializer.Deserialize<User>(await validResult.Content.ReadAsStringAsync());
        }
        else
        {
            Logger.Error("Invalid User");
            return;
        }

        var url = new Uri("ws://localhost/api/chatrouter?sessionId=" + sessionId);

        var retries = 0;
        while (retries < 5 && !stopped)
        {
            using (client = new ClientWebSocket())
            {
                client.Options.KeepAliveInterval = TimeSpan.FromHours(2);
                try
                {
                    Logger.Info("Trying to establish connection...");
                    await client.ConnectAsync(url, CancellationToken.None);
                    Logger.Info("WebSocket connection established.");
                    retries = 0;

                    Task[] tasks = {
                        Task.Run(async () =>
                        {
                            while (client.State == WebSocketState.Open && !stopped)
                            {
                                await ReadMessage();
                            }
                            Logger.Warn("Done Reading");
                        }),
                        Task.Run(async () =>
                        {
                            while(client.State == WebSocketState.Open && !stopped)
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
                    retries++;
                }
            }
        }
    }

    public void Stop()
    {
        stopped = true;

        try
        {
            client.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).Wait();
        }
        catch
        {
            Logger.Warn("Couln't close Socket connection.");
        }

        try
        {
            client.Dispose();
        }
        catch
        {
            Logger.Warn("Couldn't dispose client");
        }
    }

    async Task ReadMessage()
    {
        var receiveBuffer = new byte[65535];
        var result = await client.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), CancellationToken.None);
        var message = JsonSerializer.Deserialize<ServerMessage>(Encoding.UTF8.GetString(receiveBuffer, 0, result.Count));
        Logger.Info("Received Message");
        await SendToOllama(message, config.UseNotes == true ? await notes.GetForChat(sessionId, message.chatId) : new List<Note>());
    }

    async Task SendMessage(ServerMessage message)
    {
        if(message.content.Length <= 0)
        {
            Logger.Warn("Not Sending Empty Message");
            return;
        }
        var content = JsonSerializer.Serialize(message);
        try
        {
            var buffer = new ArraySegment<byte>(Encoding.UTF8.GetBytes(content));
            await client.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception e)
        {
            Logger.Error(e.Message);
        }
    }

    async Task SendToOllama(ServerMessage message, List<Note> notes)
    {
        var storageClient = new HttpClient();
        storageClient.DefaultRequestHeaders.Add("Cookie", "sessionId=" + sessionId);
        var storageMessagesResponse = await storageClient.GetAsync("http://localhost/api/chat/storage/" + message.chatId);
        var messages = new List<OllamaMessage>();
        if (storageMessagesResponse.IsSuccessStatusCode)
        {
            var chat = JsonSerializer.Deserialize<StorageAllMessagesResponse>(await storageMessagesResponse.Content.ReadAsStringAsync());
            var storageMessages = chat.messages;
            if (!chat.users.Contains(currentUser.Id))
            {
                Logger.Warn("Received message for wrong User!");
                return;
            }
            foreach (StorageMessage storageMessage in storageMessages)
            {
                if (storageMessage.messageId != message.messageId)
                {
                    messages.Add(new OllamaMessage()
                    {
                        content = storageMessage.content,
                        role = currentUser.Id == storageMessage.userId ? "assistant" : "user"
                    });
                }
            }
        }
        else
        {
            Logger.Warn("Answering without context.");
        }

        if (config.UseNotes == true && notes.Count > 0)
        {
            string notesString = string.Join("\n\n", notes.Select(note => $"Title: {note.Name}\nContent:{note.Content}"));
            messages.Add(new OllamaMessage
            {
                role = "system",
                content = $"These are the users notes in this chat:\n\n{notesString}"
            });
        }

        messages.Add(new OllamaMessage
        {
            role = "system",
            content = config.Message,
        });

        messages.Add(new OllamaMessage
        {
            role = "user",
            content = message.content
        });

        var httpClient = new HttpClient()
        {
            Timeout = TimeSpan.FromMinutes(60)
        };
        var payloadObj = new OllamaPayload
        {
            model = config.Model,
            options = new OllamaOptions
            {
                temperature = 1.0
            },
            stream = true,
            messages = messages.ToArray()
        };

        var payload = JsonSerializer.Serialize(payloadObj);

        Logger.Info($"Sending {payloadObj.messages.Length} messages to Ollama...");

        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/ai/chat")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        try
        {
            using (var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                Logger.Info("Getting Response");

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

                        queue.Add(new ServerMessage()
                        {
                            content = oRes.message.content,
                            messageId = messageId.ToString(),
                            version = version,
                            chatId = message.chatId,
                            userId = currentUser.Id
                        });

                        version++;
                    }
                    Logger.Info("Finished Response");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex.Message);
        }
    }

    async Task HandleQueue()
    {
        if(queue.Count > 0)
        {
            ServerMessage[] lines = new ServerMessage[queue.Count];
            queue.CopyTo(lines);
            queue.Clear();

            Dictionary<string, ServerMessage> messages = new Dictionary<string, ServerMessage>();
            foreach (ServerMessage line in lines)
            {
                if (messages.ContainsKey(line.messageId))
                {
                    messages[line.messageId] = new ServerMessage()
                    {
                        content = messages[line.messageId].content + line.content,
                        version = messages[line.messageId].version,
                        userId = messages[line.messageId].userId,
                        chatId = messages[line.messageId].chatId,
                        messageId = line.messageId,
                    };
                }
                else
                {
                    messages.Add(line.messageId, new ServerMessage()
                    {
                        content = line.content,
                        version = line.version,
                        userId = line.userId,
                        chatId = line.chatId,
                        messageId = line.messageId,
                    });
                }
            }

            List<Task> tasks = new List<Task>();
            foreach (KeyValuePair<string, ServerMessage> message in messages)
            {
                tasks.Add(SendMessage(message.Value));
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

    struct StorageAllMessagesResponse
    {
        public string id { set; get; }
        public string name { set; get; }
        public string[] users { get; set; }
        public List<StorageMessage> messages { get; set; }
    }

    struct StorageMessage
    {
        public string messageId { set; get; }
        public string content { get; set; }
        public int version { get; set; }
        public string userId { get; set; }
    }
}

public struct OllamaConfig
{
    [JsonPropertyName("configId")]
    public string ConfigId { get; set; }
    [JsonPropertyName("configUserId")]
    public string ConfigUserId { get; set; }
    [JsonPropertyName("name")]
    public string Name { get; set; }
    [JsonPropertyName("password")]
    public string Password { get; set; }
    [JsonPropertyName("model")]
    public string Model { get; set; }
    [JsonPropertyName("message")]
    public string Message { get; set; }
    [JsonPropertyName("useNotes")]
    public bool UseNotes { get; set; }
}