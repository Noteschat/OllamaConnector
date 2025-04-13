using Microsoft.AspNetCore.Mvc;

namespace OllamaConnector.Controllers
{
    [ApiController]
    [Route("/api/[controller]")]
    public class HealthController : Controller
    {
        [HttpGet]
        public ActionResult Health()
        {
            return StatusCode(200);
        }
    }
}
