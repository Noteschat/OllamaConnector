using OllamaConnector.Managers;
using OllamaConnector.Middlewares;
using System.Text;
using System.Text.Json;

namespace OllamaConnector
{
    public static class CommandHandler
    {
        static bool running = true;
        static ConnectorManager connectors;
        public static async Task Run(WebApplication app)
        {
            connectors = app.Services.GetRequiredService<ConnectorManager>();

            var registerBody = new
            {
                callback = new {
                    uri = "http://localhost/api/ollamaconnector/callback",
                    id = Guid.NewGuid().ToString(),
                }
            };
            Authentication.registeredCallbackId = registerBody.callback.id;

            try
            {
                HttpClient client = new HttpClient();
                var registerRes = await client.PostAsync("http://localhost/api/ollamaconfig/register", new StringContent(JsonSerializer.Serialize(registerBody), Encoding.UTF8, "application/json"));

                if (!registerRes.IsSuccessStatusCode)
                {
                    Logger.Error("Couldn't register at Config Service.");
                    return;
                }
                var res = JsonSerializer.Deserialize<Registration>(await registerRes.Content.ReadAsStringAsync());
                Authentication.registeredRegistrationId = res.id;
                Logger.Info("Registration-Id: |" + res.id + "|");
                Logger.Info("Accepted? (Press Enter if accepted)");
                Console.ReadLine();
            }
            catch (Exception e)
            {
                Logger.Error($"Couldn't register: " + e.Message);
                return;
            }

            Logger.Clear();

            while(running)
            {
                var command = Console.ReadLine();
                if (command == null || command.Length <= 0)
                {
                    continue;
                }
                switch (command.Substring(0, 3))
                {
                    case "CNT":
                        Logger.Info("Current Connections: " + connectors.GetConnectorCount());
                        break;
                    case "END":
                        running = false;
                        break;
                    default:
                        Logger.Warn("Unknown Command");
                        break;
                }
            }
        }
    }

    public struct Registration
    {
        public string id { get; set; }
    }
}
