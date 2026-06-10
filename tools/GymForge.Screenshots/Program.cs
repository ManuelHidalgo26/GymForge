using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Members;
using GymForge.Desktop;
using GymForge.Desktop.Services;
using GymForge.Desktop.ViewModels.Cash;
using GymForge.Desktop.ViewModels.Dashboard;
using GymForge.Desktop.ViewModels.Members;
using GymForge.Desktop.Views.Cash;
using GymForge.Desktop.Views.Dashboard;
using GymForge.Desktop.Views.Members;
using GymForge.Domain.Enums;
using GymForge.Infrastructure;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

// Renderiza vistas reales de la app (Avalonia headless + Skia) a PNG, con datos
// de ejemplo en una DB temporal. Permite revisar el UI sin abrir la ventana.

var outDir = Path.Combine(AppContext.BaseDirectory, "screenshots");
Directory.CreateDirectory(outDir);

// ── 1. DI + DB temporal + seed + datos de ejemplo ────────────────────────────
var tempDb = Path.Combine(Path.GetTempPath(), $"gymforge_shots_{Guid.NewGuid():N}.db");
var conn = $"Data Source={tempDb};Mode=ReadWriteCreate;Cache=Shared";

var services = new ServiceCollection();
services.AddGymForgeDesktop(conn);
var sp = services.BuildServiceProvider();

await sp.InitialiseDatabaseAsync();
var session = sp.GetRequiredService<SessionContext>();
await session.InitializeAsync();

await SeedSampleMembersAsync(sp, session);

// Pre-cargar las ViewModels (queries a DB) ANTES de tocar el hilo de UI.
var dashboardVm = sp.GetRequiredService<DashboardViewModel>();
await dashboardVm.LoadCommand.ExecuteAsync(null);

var membersVm = sp.GetRequiredService<MembersListViewModel>();
await membersVm.LoadCommand.ExecuteAsync(null);

var createVm = sp.GetRequiredService<CreateMemberViewModel>();

var cashVm = sp.GetRequiredService<CashViewModel>();
await cashVm.LoadCommand.ExecuteAsync(null);

// ── 2. Avalonia headless + Skia ──────────────────────────────────────────────
AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

// ── 3. Capturar cada vista ───────────────────────────────────────────────────
Capture("01-dashboard", new DashboardView { DataContext = dashboardVm }, 1180, 720, outDir);
Capture("02-socios-lista", new MembersListView { DataContext = membersVm }, 1180, 720, outDir);
Capture("03-socio-alta", new CreateMemberView { DataContext = createVm }, 1180, 720, outDir);
Capture("04-caja-login", new CashView { DataContext = cashVm }, 1180, 720, outDir);

Console.WriteLine($"OK -> {outDir}");
return;

static void Capture(string name, Control view, int width, int height, string outDir)
{
    var window = new Window
    {
        Width = width,
        Height = height,
        SystemDecorations = SystemDecorations.None,
        Content = view,
    };
    window.Show();

    // Forzar layout + render headless.
    Dispatcher.UIThread.RunJobs();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    Dispatcher.UIThread.RunJobs();

    var frame = window.CaptureRenderedFrame();
    var path = Path.Combine(outDir, $"{name}.png");
    frame?.Save(path);
    window.Close();
    Console.WriteLine($"  {name}.png  {frame?.PixelSize}");
}

static async Task SeedSampleMembersAsync(IServiceProvider sp, SessionContext session)
{
    var mediator = sp.GetRequiredService<IMediator>();

    var samples = new (string First, string Last, string Doc, Gender G, string Mail)[]
    {
        ("Juan", "Pérez", "30111222", Gender.Male, "juan.perez@mail.com"),
        ("María", "Gómez", "31222333", Gender.Female, "maria.gomez@mail.com"),
        ("Lucas", "Fernández", "32333444", Gender.Male, "lucas.f@mail.com"),
        ("Sofía", "Martínez", "33444555", Gender.Female, "sofia.m@mail.com"),
        ("Diego", "Ramírez", "34555666", Gender.Male, "diego.r@mail.com"),
    };

    foreach (var s in samples)
        await mediator.Send(new CreateMemberCommand(
            session.CompanyId, session.SiteId, s.First, s.Last,
            DocumentType.DNI, s.Doc, s.G, s.Mail, "+54 9 11 5555-0000",
            new DateOnly(1995, 6, 15)));
}
