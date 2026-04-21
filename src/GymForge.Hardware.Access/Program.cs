// GymForge.Hardware.Access — AccessBroker sidecar
// Bridges TCP ZKTeco C3/Hikvision access controllers to HTTP/WS on localhost:12002
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/access-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
builder.WebHost.UseUrls("http://localhost:12002");

var app = builder.Build();

// POST /open-door
app.MapPost("/open-door", (OpenDoorRequest req) =>
{
    // TODO Sprint 2: send relay pulse via ZKTeco C3 TCP protocol
    Log.Information("AccessBroker: opening door {DoorId} (stub)", req.DoorId);
    return Results.Ok(new { status = "ok" });
});

// GET /doors
app.MapGet("/doors", () => Results.Ok(new[]
{
    new { doorId = 1, name = "Entrada Principal", controllerAddress = "192.168.1.100", isOnline = true },
    new { doorId = 2, name = "Vestuario Damas",   controllerAddress = "192.168.1.101", isOnline = true },
    new { doorId = 3, name = "Vestuario Varones",  controllerAddress = "192.168.1.102", isOnline = true }
}));

// WS /events — stream of access events from controllers
app.MapGet("/events", async (HttpContext ctx) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    { ctx.Response.StatusCode = 400; return; }

    using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    Log.Information("AccessBroker: WS /events client connected");

    while (!ctx.RequestAborted.IsCancellationRequested)
        await Task.Delay(5000, ctx.RequestAborted);
});

Log.Information("AccessBroker listening on http://localhost:12002");
app.Run();

record OpenDoorRequest(int DoorId);
