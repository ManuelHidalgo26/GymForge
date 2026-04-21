// GymForge.Hardware.Bio — BioBroker sidecar
// x86 process bridging ZKTeco libzkfpcsharp + zkemkeeper to HTTP/WS on localhost:12001
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/bio-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
builder.WebHost.UseUrls("http://localhost:12001");

var app = builder.Build();

// POST /enroll
app.MapPost("/enroll", (EnrollRequest req) =>
{
    // TODO Sprint 2: wire ZKTeco libzkfpcsharp for 3-capture enrollment
    Log.Warning("BioBroker: /enroll stub for member {MemberId}", req.MemberId);
    var fakeTemplate = new byte[512];
    new Random().NextBytes(fakeTemplate);
    return Results.Ok(new { templateBase64 = Convert.ToBase64String(fakeTemplate) });
});

// POST /verify
app.MapPost("/verify", (VerifyRequest req) =>
{
    Log.Warning("BioBroker: /verify stub");
    return Results.Ok(new { matched = true, score = 0.95f });
});

// WS /swipes — stream of fingerprint events from ZKTeco network terminals
app.MapGet("/swipes", async (HttpContext ctx) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    { ctx.Response.StatusCode = 400; return; }

    using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    Log.Information("BioBroker: WS /swipes client connected");

    // Keep connection alive until cancelled — real events injected by ZKTeco SDK thread
    var buf = new byte[4];
    while (!ctx.RequestAborted.IsCancellationRequested)
        await Task.Delay(5000, ctx.RequestAborted);
});

Log.Information("BioBroker listening on http://localhost:12001");
app.Run();

record EnrollRequest(Guid MemberId, int FingerIndex);
record VerifyRequest(string TemplateBase64);
