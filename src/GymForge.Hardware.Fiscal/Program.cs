// GymForge.Hardware.Fiscal — FiscalBroker sidecar
// x86 process bridging Hasar/Epson COM OCX to HTTP API on localhost:12000
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/fiscal-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
builder.WebHost.UseUrls("http://localhost:12000");

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// POST /ticket — print fiscal ticket and obtain CAE
app.MapPost("/ticket", (FiscalTicketRequest req) =>
{
    // TODO Sprint 2: wire up actual OCX COM interop (Hasar DLL / Epson ePOS-Fiscal)
    Log.Warning("FiscalBroker: /ticket called but OCX not yet wired (stub response)");

    var stub = new
    {
        cae = "12345678901234",
        caeExpiry = DateTime.Today.AddDays(10).ToString("yyyy-MM-dd"),
        invoiceNumber = "0001-00000001",
        pdfBase64 = Convert.ToBase64String("STUB PDF"u8.ToArray()),
        xmlBase64 = Convert.ToBase64String("<comprobante/>"u8.ToArray())
    };
    return Results.Ok(stub);
});

// POST /cancel — credit note
app.MapPost("/cancel", (CancelRequest req) =>
{
    Log.Warning("FiscalBroker: /cancel stub for invoice {Invoice}", req.InvoiceNumber);
    return Results.Ok(new { status = "ok" });
});

// GET /status — printer health
app.MapGet("/status", () => Results.Ok(new
{
    model = "Hasar 320F (stub)",
    firmware = "1.0.0",
    lastZReport = DateTime.Today.ToString("yyyy-MM-dd"),
    paperSensor = true
}));

// POST /z-report
app.MapPost("/z-report", () =>
{
    Log.Information("Z-Report requested");
    return Results.Ok(new { status = "ok" });
});

Log.Information("FiscalBroker listening on http://localhost:12000");
app.Run();

record FiscalTicketRequest(
    List<FiscalItemDto> Items,
    FiscalPaymentDto Payment,
    string? ClientCuit);

record FiscalItemDto(string Description, decimal Qty, decimal Price, decimal TaxRate);
record FiscalPaymentDto(string Method, decimal Amount, string? CardLast4);
record CancelRequest(string InvoiceNumber);
