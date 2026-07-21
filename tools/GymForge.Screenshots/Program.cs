using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Access;
using GymForge.Application.UseCases.Cash;
using GymForge.Application.UseCases.Catalog;
using GymForge.Application.UseCases.Charges;
using GymForge.Application.UseCases.Licensing;
using GymForge.Application.UseCases.Members;
using GymForge.Application.UseCases.Sales;
using GymForge.Application.UseCases.Staff;
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

var samplePaymentId = await SeedSampleMembersAsync(sp, session);
var sampleScheduleId = await SeedSampleClassesAsync(sp, session);
var sampleRoutineMemberId = await SeedSampleRoutineAsync(sp, session);

// Con una clave real en el entorno, Configuración se captura en estado licenciado
// (prueba de punta a punta de la clave pública embebida en LicenseService).
if (Environment.GetEnvironmentVariable("GYMFORGE_SAMPLE_LICENSE") is { Length: > 0 } licenseKey)
    await sp.GetRequiredService<IMediator>().Send(
        new ActivateLicenseCommand(session.CompanyId, licenseKey));

// Recibo PDF de muestra del último cobro (verificación visual del layout)
if (samplePaymentId is { } paymentId)
{
    var receipt = await sp.GetRequiredService<IMediator>().Send(
        new BuildReceiptQuery(paymentId, session.CompanyId));
    var pdf = sp.GetRequiredService<IReceiptPdfWriter>().Generate(receipt);
    await File.WriteAllBytesAsync(Path.Combine(outDir, "recibo-sample.pdf"), pdf);
    Console.WriteLine("  recibo-sample.pdf");
}

// Pre-cargar las ViewModels (queries a DB) ANTES de tocar el hilo de UI.
var dashboardVm = sp.GetRequiredService<DashboardViewModel>();
await dashboardVm.LoadCommand.ExecuteAsync(null);

var membersVm = sp.GetRequiredService<MembersListViewModel>();
await membersVm.LoadCommand.ExecuteAsync(null);

var createVm = sp.GetRequiredService<CreateMemberViewModel>();
// Mostrar la sección "Membresía inicial" con un plan elegido
await createVm.LoadPlansCommand.ExecuteAsync(null);
createVm.ChargeNow = true;
createVm.SelectedPlan = createVm.Plans.FirstOrDefault();

var cashVm = sp.GetRequiredService<CashViewModel>();
await cashVm.LoadCommand.ExecuteAsync(null);

// ── 2. Avalonia headless + Skia ──────────────────────────────────────────────
AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

// ── 3. Capturar cada vista (el dashboard se captura al final, con actividad) ─
Capture("02-socios-lista", new MembersListView { DataContext = membersVm }, 1180, 720, outDir);
Capture("03-socio-alta", new CreateMemberView { DataContext = createVm }, 1180, 720, outDir);
Capture("04-caja-login", new CashView { DataContext = cashVm }, 1180, 720, outDir);

// ── 4. Estados ricos de caja: cajero logueado + turno abierto + movimientos ──
// (después del setup de Avalonia, los await se bombean con WaitFor/RunJobs)
var mediator2 = sp.GetRequiredService<IMediator>();
var staff = WaitFor(mediator2.Send(new AuthenticateStaffCommand(session.CompanyId, "1234")))
    ?? throw new InvalidOperationException("Login de cajero falló (PIN 1234).");
session.SignIn(staff);

var shift = WaitFor(mediator2.Send(new OpenShiftCommand(
    session.CompanyId, session.SiteId, staff.Id, 5_000m)));
WaitFor(mediator2.Send(new AddCashMovementCommand(
    shift.Id, CashMovementType.Income, CashMovementCategory.Sale, 12_000m, "Venta proteína")));
WaitFor(mediator2.Send(new AddCashMovementCommand(
    shift.Id, CashMovementType.Expense, CashMovementCategory.PettyCash, 3_000m, "Artículos de limpieza")));
session.SetOpenShift(shift.Id);

Pump(cashVm.LoadCommand.ExecuteAsync(null));
Capture("05-caja-operando", new CashView { DataContext = cashVm }, 1180, 900, outDir);

Pump(cashVm.OpenSaleModalCommand.ExecuteAsync(null));
// Mostrar el modo "Producto" con la venta a consumidor final (feature de POS sin socio)
cashVm.SaleModal!.IsProduct = true;
cashVm.SaleModal.SelectedProduct = cashVm.SaleModal.Products.FirstOrDefault();
cashVm.SaleModal.IsWalkIn = true;
Capture("06-caja-venta-modal", new CashView { DataContext = cashVm }, 1180, 900, outDir);

// Estado vacío de la lista de socios (búsqueda sin coincidencias)
var emptyMembersVm = new MembersListViewModel(mediator2, session) { SearchText = "zzz-sin-resultados" };
Capture("07-socios-vacio", new MembersListView { DataContext = emptyMembersVm }, 1180, 720, outDir);

// Planes: listado con el formulario de alta abierto
var plansVm = new GymForge.Desktop.ViewModels.Plans.PlansViewModel(mediator2, session) { IsFormOpen = true };
Capture("08-planes", new GymForge.Desktop.Views.Plans.PlansView { DataContext = plansVm }, 1180, 760, outDir);

// Reportes: recaudación del mes con los pagos del seed
var reportsVm = new GymForge.Desktop.ViewModels.Reports.ReportsViewModel(
    mediator2, session, sp.GetRequiredService<GymForge.Desktop.Services.ReceiptService>());
Capture("09-reportes", new GymForge.Desktop.Views.Reports.ReportsView { DataContext = reportsVm }, 1180, 760, outDir);

// Configuración: datos del gimnasio + sedes
var settingsVm = new GymForge.Desktop.ViewModels.Settings.SettingsViewModel(
    mediator2, sp.GetRequiredService<ISiteRepository>(),
    sp.GetRequiredService<IMemberRepository>(), session,
    sp.GetRequiredService<GymForge.Application.UseCases.Access.GatekeeperConfig>(),
    sp.GetRequiredService<GymForge.Application.UseCases.Licensing.CurrentLicense>());
Capture("10-configuracion", new GymForge.Desktop.Views.Settings.SettingsView { DataContext = settingsVm }, 1180, 1560, outDir);

// Clases v2: agenda semanal con un horario seleccionado y sus reservas
var classesVm = new GymForge.Desktop.ViewModels.Classes.ClassesViewModel(
    mediator2, sp.GetRequiredService<IMemberRepository>(), session);
Pump(classesVm.LoadCommand.ExecuteAsync(null));
classesVm.SelectedSchedule = classesVm.Schedules.FirstOrDefault(s => s.Id == sampleScheduleId)
    ?? classesVm.Schedules.FirstOrDefault();
for (int i = 0; i < 15; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }  // dejar cargar las reservas
Capture("11-clases", new GymForge.Desktop.Views.Classes.ClassesView { DataContext = classesVm }, 1180, 760, outDir);

// Rutinas: biblioteca de ejercicios (80 del seed)
var libraryVm = new GymForge.Desktop.ViewModels.Routines.ExerciseLibraryViewModel(mediator2, session);
Capture("12-rutinas-biblioteca", new GymForge.Desktop.Views.Routines.ExerciseLibraryView { DataContext = libraryVm }, 1180, 760, outDir);

// Rutinas v2: armador con un socio + rutina (días con ejercicios)
var builderVm = new GymForge.Desktop.ViewModels.Routines.RoutineBuilderViewModel(
    mediator2, sp.GetRequiredService<IMemberRepository>(), session);
Pump(builderVm.LoadCommand.ExecuteAsync(null));
builderVm.SelectedMember = builderVm.Members.FirstOrDefault(m => m.Id == sampleRoutineMemberId);
for (int i = 0; i < 25; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }  // cargar rutinas del socio
builderVm.SelectedRoutine = builderVm.Routines.FirstOrDefault();
for (int i = 0; i < 25; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }  // cargar el detalle
Capture("16-rutinas-armador", new GymForge.Desktop.Views.Routines.RoutineBuilderView { DataContext = builderVm }, 1180, 820, outDir);

// Productos: catálogo con stock por sede (alta nueva + uno con aviso de reposición)
WaitFor(mediator2.Send(new GymForge.Application.UseCases.Products.CreateProductCommand(
    session.CompanyId, "TOAL-GF", "Toalla GymForge", 12_000m, 6_000m, "7791234567890")));
var productsVm = new GymForge.Desktop.ViewModels.Products.ProductsViewModel(mediator2, session);
Pump(productsVm.LoadCommand.ExecuteAsync(null));
var agua = productsVm.Products.First(p => p.Sku == "AGUA-500");
WaitFor(mediator2.Send(new GymForge.Application.UseCases.Products.AdjustStockCommand(
    session.CompanyId, session.SiteId, agua.Id, 0m, agua.StockQty + 5m)));  // → pill "Reponer"
Pump(productsVm.LoadCommand.ExecuteAsync(null));
Capture("15-productos", new GymForge.Desktop.Views.Products.ProductsView { DataContext = productsVm }, 1180, 760, outDir);

// Check-in: varios accesos del día (poblan el panel "Accesos de hoy"); el último
// permitido queda como resultado central. Sirve de actividad para el dashboard.
var kioskVm = new GymForge.Desktop.ViewModels.Checkin.CheckInKioskViewModel(
    sp.GetRequiredService<ValidateSwipeUseCase>(), mediator2, session);
Pump(kioskVm.ProcessCredentialAsync("33444555", AccessMethod.Manual));  // sin membresía → rechazo
Pump(kioskVm.ProcessCredentialAsync("31222333", AccessMethod.Manual));  // permitido
Pump(kioskVm.ProcessCredentialAsync("30111222", AccessMethod.Manual));  // permitido (queda en pantalla)
Capture("13-checkin-aprobado", new GymForge.Desktop.Views.Checkin.CheckInKioskView { DataContext = kioskVm }, 1180, 760, outDir);

// Dashboard premium: KPIs con tendencia + gráfico 30 días + actividad del día
Pump(dashboardVm.LoadCommand.ExecuteAsync(null));
Capture("01-dashboard", new DashboardView { DataContext = dashboardVm }, 1180, 820, outDir);

// Auditoría del tema oscuro: dashboard + vistas nuevas (Fase 7, consistencia dark)
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
Capture("14-dashboard-dark", new DashboardView { DataContext = dashboardVm }, 1180, 820, outDir);
Capture("15-productos-dark", new GymForge.Desktop.Views.Products.ProductsView { DataContext = productsVm }, 1180, 760, outDir);
Capture("11-clases-dark", new GymForge.Desktop.Views.Classes.ClassesView { DataContext = classesVm }, 1180, 760, outDir);
Capture("16-rutinas-armador-dark", new GymForge.Desktop.Views.Routines.RoutineBuilderView { DataContext = builderVm }, 1180, 820, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;

// Shell completo: sidebar + topbar + dashboard (la ventana real de la app)
var shell = new GymForge.Desktop.Views.Shell.MainWindow
{
    DataContext = sp.GetRequiredService<GymForge.Desktop.Views.Shell.MainWindowViewModel>(),
    Width = 1366,
    Height = 800,
};
CaptureWindow("00-shell", shell, outDir);

// Branding: aplica un acento distinto (verde) y re-captura el shell para verificar
// que FluentAvaloniaTheme.CustomAccentColor re-tinta toda la UI en caliente.
GymForge.Desktop.App.ApplyBrandAccent("#059669");
var shellBrand = new GymForge.Desktop.Views.Shell.MainWindow
{
    DataContext = sp.GetRequiredService<GymForge.Desktop.Views.Shell.MainWindowViewModel>(),
    Width = 1366,
    Height = 800,
};
CaptureWindow("17-marca-verde", shellBrand, outDir);
GymForge.Desktop.App.ApplyBrandAccent("#6366F1");

Console.WriteLine($"OK -> {outDir}");
return;

// Bombea el dispatcher headless hasta que la tarea termine (los await posteriores
// al setup de Avalonia postean continuaciones a la cola del UI thread).
static T WaitFor<T>(Task<T> task)
{
    Pump(task);
    return task.GetAwaiter().GetResult();
}

static void Pump(Task task)
{
    while (!task.IsCompleted)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(5);
    }
    task.GetAwaiter().GetResult();
}

static void Capture(string name, Control view, int width, int height, string outDir)
{
    var window = new Window
    {
        Width = width,
        Height = height,
        SystemDecorations = SystemDecorations.None,
        Content = view,
    };
    CaptureWindow(name, window, outDir);
}

static void CaptureWindow(string name, Window window, string outDir)
{
    window.Show();

    // Forzar layout + render headless, dando tiempo a las cargas async de las
    // vistas que auto-cargan en OnDataContextChanged.
    for (int i = 0; i < 10; i++)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(20);
    }
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    Dispatcher.UIThread.RunJobs();

    var frame = window.CaptureRenderedFrame();
    var path = Path.Combine(outDir, $"{name}.png");
    frame?.Save(path);
    window.Close();
    Console.WriteLine($"  {name}.png  {frame?.PixelSize}");
}

static async Task<Guid> SeedSampleRoutineAsync(IServiceProvider sp, SessionContext session)
{
    var mediator = sp.GetRequiredService<IMediator>();
    var memberRepo = sp.GetRequiredService<IMemberRepository>();

    var members = await memberRepo.GetPagedAsync(session.CompanyId, session.SiteId, 1, 10);
    var member = members.First();

    var routine = await mediator.Send(new GymForge.Application.UseCases.Routines.CreateRoutineCommand(
        session.CompanyId, member.Id, "Full Body", WorkoutGoal.Hypertrophy, 3));

    await mediator.Send(new GymForge.Application.UseCases.Routines.AddRoutineDayCommand(
        session.CompanyId, routine.Id, "Tren superior"));
    await mediator.Send(new GymForge.Application.UseCases.Routines.AddRoutineDayCommand(
        session.CompanyId, routine.Id, "Tren inferior"));

    var detail = await mediator.Send(new GymForge.Application.UseCases.Routines.GetRoutineDetailQuery(
        session.CompanyId, routine.Id));
    var exercises = await mediator.Send(
        new GymForge.Application.UseCases.Exercises.SearchExercisesQuery(null, null));
    var ex = exercises.Take(6).ToList();

    if (detail is { Days.Count: >= 2 } && ex.Count >= 5)
    {
        var (d1, d2) = (detail.Days[0].Id, detail.Days[1].Id);
        await mediator.Send(new GymForge.Application.UseCases.Routines.AddRoutineItemCommand(
            session.CompanyId, d1, ex[0].Id, 4, 8, 12));
        await mediator.Send(new GymForge.Application.UseCases.Routines.AddRoutineItemCommand(
            session.CompanyId, d1, ex[1].Id, 3, 10, 12));
        await mediator.Send(new GymForge.Application.UseCases.Routines.AddRoutineItemCommand(
            session.CompanyId, d1, ex[2].Id, 3, 12, 15));
        await mediator.Send(new GymForge.Application.UseCases.Routines.AddRoutineItemCommand(
            session.CompanyId, d2, ex[3].Id, 4, 8, 10));
        await mediator.Send(new GymForge.Application.UseCases.Routines.AddRoutineItemCommand(
            session.CompanyId, d2, ex[4].Id, 3, 10, 12));
    }

    return member.Id;
}

static async Task<Guid> SeedSampleClassesAsync(IServiceProvider sp, SessionContext session)
{
    var mediator = sp.GetRequiredService<IMediator>();
    var memberRepo = sp.GetRequiredService<IMemberRepository>();

    var cls = await mediator.Send(new GymForge.Application.UseCases.Classes.CreateClassCommand(
        session.CompanyId, "Funcional", 60, 12));
    await mediator.Send(new GymForge.Application.UseCases.Classes.CreateClassCommand(
        session.CompanyId, "Spinning", 45, 16));

    var today = DateTime.Today;
    var monday = DateOnly.FromDateTime(today).AddDays(-(((int)today.DayOfWeek + 6) % 7));
    var first = await mediator.Send(new GymForge.Application.UseCases.Classes.CreateScheduleCommand(
        session.CompanyId, session.SiteId, cls.Id, monday.ToDateTime(new TimeOnly(18, 0)), 12));
    await mediator.Send(new GymForge.Application.UseCases.Classes.CreateScheduleCommand(
        session.CompanyId, session.SiteId, cls.Id, monday.AddDays(2).ToDateTime(new TimeOnly(19, 0)), 12));

    var members = await memberRepo.GetPagedAsync(session.CompanyId, session.SiteId, 1, 10);
    foreach (var m in members.Take(3))
        await mediator.Send(new GymForge.Application.UseCases.Classes.BookMemberCommand(
            session.CompanyId, first.Id, m.Id));

    return first.Id;
}

static async Task<Guid?> SeedSampleMembersAsync(IServiceProvider sp, SessionContext session)
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

    var ids = new List<Guid>();
    foreach (var s in samples)
    {
        var dto = await mediator.Send(new CreateMemberCommand(
            session.CompanyId, session.SiteId, s.First, s.Last,
            DocumentType.DNI, s.Doc, s.G, s.Mail, "+54 9 11 5555-0000",
            new DateOnly(1995, 6, 15)));
        ids.Add(dto.Id);
    }

    // Vender una membresía a los primeros 3 → quedan activos + generan recaudación.
    var plans = await mediator.Send(new GetMembershipTypesQuery(session.CompanyId));
    var plan = plans.FirstOrDefault(p => p.Price > 0);
    if (plan is null) return null;

    Guid? lastPaymentId = null;
    var buyers = ids.Take(3).ToList();
    for (int i = 0; i < buyers.Count; i++)
    {
        var isCard = i == buyers.Count - 1; // el último con tarjeta → recibo más completo
        var payment = await mediator.Send(new SellMembershipCommand(
            session.CompanyId, session.SiteId, session.EffectiveCashierId, null,
            buyers[i], plan.Id,
            isCard ? PaymentMethod.CreditCard : PaymentMethod.Cash,
            isCard ? "4321" : null));
        lastPaymentId = payment.Id;
    }

    return lastPaymentId;
}
