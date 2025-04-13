using Microsoft.AspNetCore.Mvc;
using OllamaConnector.Managers;
using System.Text.Json;

namespace OllamaConnector.Controllers
{
    [ApiController]
    [Route("/api/[controller]")]
    public class CallbackController : Controller
    {
        ConnectorManager _connectors;

        public CallbackController(ConnectorManager connectors)
        {
            _connectors = connectors;
        }

        [HttpGet]
        public ActionResult VerifyCallback()
        {
            return StatusCode(200);
        }

        [HttpPost]
        public async Task<dynamic> Callback()
        {
            OllamaConfig data;

            try
            {
                var content = await new StreamReader(Request.Body).ReadToEndAsync();
                data = JsonSerializer.Deserialize<OllamaConfig>(content);
            }
            catch
            {
                return StatusCode(400, new { cause = "wrong format" });
            }

            _connectors.CreateConnector(data);

            return StatusCode(200);
        }

        [HttpDelete("{id}")]
        public ActionResult StopClient(string id)
        {
            _connectors.StopConnector(id);

            return StatusCode(200);
        }
    }
}
