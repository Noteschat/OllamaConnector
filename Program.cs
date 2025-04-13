using OllamaConnector;
using OllamaConnector.Managers;
using OllamaConnector.Middlewares;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddSingleton<ConnectorManager>();

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseMiddleware<Authentication>();

app.UseAuthorization();

app.MapControllers();

List<Task> tasks = new List<Task>
{
    Task.Run(() => CommandHandler.Run(app)),
    Task.Run(app.Run)
};

Task.WaitAll(tasks.ToArray());

