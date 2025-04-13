using System.Text.Json;

namespace OllamaConnector.Middlewares
{
    public class Authentication
    {
        public static string registeredCallbackId;
        public static string registeredRegistrationId;
        private readonly RequestDelegate _next;

        public Authentication(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Path.Value.EndsWith("/api/health"))
            {
                await _next(context);
                return;
            }

            var callbackId = context.Request.Cookies["callbackId"];
            var registrationId = context.Request.Cookies["registrationId"];
            if (callbackId == null)
            {
                if(registrationId == null || registrationId != registeredRegistrationId)
                {
                    Logger.Warn("Unauthenticated Connection attempt.");

                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json";

                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { cause = "not registered" }));
                    return;
                }
            }
            else
            {
                if (callbackId == null || callbackId != registeredCallbackId)
                {
                    Logger.Warn("Unauthenticated Connection attempt.");

                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json";

                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { cause = "not registered" }));
                    return;
                }
            }

            // Forward the request to the next middleware
            await _next(context);
        }
    }
}
