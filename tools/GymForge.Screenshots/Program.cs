using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Access;
using GymForge.Application.UseCases.Cash;
using GymForge.Application.UseCases.Catalog;
using GymForge.Application.UseCases.Charges;
using GymForge.Application.UseCases.Dunning;
using GymForge.Application.UseCases.Licensing;
using GymForge.Application.UseCases.Members;
using GymForge.Application.UseCases.Reports;
using GymForge.Application.UseCases.Sales;
using GymForge.Application.UseCases.Staff;
using GymForge.Desktop;
using GymForge.Desktop.Services;
using GymForge.Desktop.ViewModels.Cash;
using GymForge.Desktop.ViewModels.Collections;
using GymForge.Desktop.ViewModels.Dashboard;
using GymForge.Desktop.ViewModels.Members;
using GymForge.Desktop.Views.Cash;
using GymForge.Desktop.Views.Collections;
using GymForge.Desktop.Views.Dashboard;
using GymForge.Desktop.Views.Members;
using GymForge.Domain.Enums;
using GymForge.Infrastructure;
using GymForge.Infrastructure.Services;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

// Renderiza vistas reales de la app (Avalonia headless + Skia) a PNG, con datos
// de ejemplo en una DB temporal. Permite revisar el UI sin abrir la ventana.

// Las capturas usan el gimnasio de demostración (sin él, el primer arranque muestra
// el onboarding y no habría tenant que poblar).
Environment.SetEnvironmentVariable("GYMFORGE_SEED_COMPANY", "1");

var outDir = Path.Combine(AppContext.BaseDirectory, "screenshots");
Directory.CreateDirectory(outDir);

// ── 1. DI + DB temporal + seed + datos de ejemplo ────────────────────────────
var tempDb = Path.Combine(Path.GetTempPath(), $"gymforge_shots_{Guid.NewGuid():N}.db");
var conn = $"Data Source={tempDb};Mode=ReadWriteCreate;Cache=Shared";

var services = new ServiceCollection();
services.AddGymForgeDesktop(conn);
// Settings de la PC en un archivo temporal: las capturas no dependen de (ni escriben en) la config real del dev.
services.AddSingleton<ILocalSettings>(new LocalSettingsService(
    NullLogger<LocalSettingsService>.Instance,
    Path.Combine(Path.GetTempPath(), $"gymforge_shots_{Guid.NewGuid():N}_settings.json")));
// Las fotos de las capturas van a una carpeta temporal: no ensucian %LOCALAPPDATA%\GymForge\fotos del dev.
services.AddSingleton<IMemberPhotoStore>(
    new MemberPhotoStore(Path.Combine(Path.GetTempPath(), $"gymforge_shots_{Guid.NewGuid():N}_fotos")));
// Origen de actualizaciones simulado: las capturas no tocan la red ni dependen de una instalación.
services.AddSingleton<SampleUpdateService>();
services.AddSingleton<IUpdateService>(provider => provider.GetRequiredService<SampleUpdateService>());
var sp = services.BuildServiceProvider();

await sp.InitialiseDatabaseAsync();
var session = sp.GetRequiredService<SessionContext>();
await session.InitializeAsync();

var samplePaymentId = await SeedSampleMembersAsync(sp, session);
var sampleScheduleId = await SeedSampleClassesAsync(sp, session);
var sampleRoutineMemberId = await SeedSampleRoutineAsync(sp, session);
await SeedSampleDebtorsAsync(sp, session);

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
    mediator2, session, sp.GetRequiredService<GymForge.Desktop.Services.ReceiptService>(),
    sp.GetRequiredService<GymForge.Application.Services.ICompanyCalendar>(),
    sp.GetRequiredService<GymForge.Desktop.Services.IReportExportService>(),
    sp.GetRequiredService<IClock>());
Pump(reportsVm.LoadCommand.ExecuteAsync(null));
Capture("09-reportes", new GymForge.Desktop.Views.Reports.ReportsView { DataContext = reportsVm }, 1180, 760, outDir);

// Los otros tres reportes del hub (desagregada y altas/bajas con alcance de toda la empresa)
reportsVm.CompanyWide = true;
foreach (var (kind, shot) in new[]
         {
             (GymForge.Desktop.ViewModels.Reports.ReportKind.RevenueBreakdown, "09b-reportes-desagregada"),
             (GymForge.Desktop.ViewModels.Reports.ReportKind.Overdue, "09c-reportes-morosos"),
             (GymForge.Desktop.ViewModels.Reports.ReportKind.MemberFlow, "09d-reportes-altas-bajas"),
         })
{
    reportsVm.SelectedReport = kind;
    Pump(reportsVm.LoadCommand.ExecuteAsync(null));
    Capture(shot, new GymForge.Desktop.Views.Reports.ReportsView { DataContext = reportsVm }, 1180, 760, outDir);
}

// Los mismos reportes como archivos exportables (PDF y Excel) para revisar el layout
{
    var reportToday = DateOnly.FromDateTime(DateTime.Today);
    var reportFrom = new DateOnly(reportToday.Year, reportToday.Month, 1);
    var siteContext = WaitFor(mediator2.Send(new GetReportContextQuery(session.CompanyId, session.SiteId)));
    var companyContext = WaitFor(mediator2.Send(new GetReportContextQuery(session.CompanyId, null)));
    var nowUtc = DateTime.UtcNow;
    var exporter = sp.GetRequiredService<IReportExporter>();

    ReportDocument[] sampleReports =
    [
        ReportBuilders.Payments(
            WaitFor(mediator2.Send(new GetPaymentsReportQuery(session.CompanyId, session.SiteId, reportFrom, reportToday))),
            reportFrom, reportToday, siteContext, nowUtc),
        ReportBuilders.Breakdown(
            WaitFor(mediator2.Send(new GetRevenueBreakdownQuery(session.CompanyId, null, reportFrom, reportToday))),
            reportFrom, reportToday, companyContext, nowUtc),
        ReportBuilders.Overdue(
            WaitFor(mediator2.Send(new GetOverdueMembersQuery(session.CompanyId, session.SiteId, reportToday))),
            reportToday, siteContext, nowUtc),
        ReportBuilders.Flow(
            WaitFor(mediator2.Send(new GetMemberFlowQuery(session.CompanyId, null, reportFrom, reportToday))),
            reportFrom, reportToday, companyContext, nowUtc),
    ];

    foreach (var report in sampleReports)
        foreach (var format in new[] { ReportFormat.Pdf, ReportFormat.Xlsx })
        {
            var file = $"{report.FileStem}.{exporter.Extension(format)}";
            File.WriteAllBytes(Path.Combine(outDir, file), exporter.Export(report, format));
            Console.WriteLine($"  {file}");
        }
}

// Configuración: datos del gimnasio + sedes
GymForge.Desktop.ViewModels.Settings.SettingsViewModel NewSettingsViewModel() => new(
    mediator2, sp.GetRequiredService<ISiteRepository>(),
    sp.GetRequiredService<IMemberRepository>(), session,
    sp.GetRequiredService<GymForge.Application.UseCases.Access.GatekeeperConfig>(),
    sp.GetRequiredService<CurrentLicense>(),
    sp.GetRequiredService<IDataTransfer>(), sp.GetRequiredService<IDatabaseBackup>(),
    sp.GetRequiredService<ILocalSettings>(), sp.GetRequiredService<IMachineFingerprint>(),
    sp.GetRequiredService<GymForge.Desktop.ViewModels.Shared.UpdateViewModel>(),
    sp.GetRequiredService<DunningConfig>(), sp.GetRequiredService<IBackupKeyStore>());

Capture("10-configuracion", new GymForge.Desktop.Views.Settings.SettingsView { DataContext = NewSettingsViewModel() }, 1180, 3300, outDir);

// Config en dark, con un error de Cobranzas visible (el danger global ahora usa DangerTextBrush).
var settingsWithError = NewSettingsViewModel();
settingsWithError.DunningMessage = "Los días de vencido no se pueden repetir entre etapas.";
settingsWithError.DunningSaved = false;
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
Capture("10-configuracion-dark", new GymForge.Desktop.Views.Settings.SettingsView { DataContext = settingsWithError }, 1180, 3300, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;

// Licencia emitida para otra PC: solo avisa, el plan sigue activo (se restablece el estado después).
var liveLicense = sp.GetRequiredService<CurrentLicense>();
var licenseBeforeMismatch = liveLicense.State;
liveLicense.State = new LicenseState(
    LicenseStatus.Active, "Pro", 3, 1000, DateOnly.FromDateTime(DateTime.Today).AddMonths(6),
    "Gimnasio Demo", MachineMismatch: true);
Capture("10b-configuracion-licencia-otra-pc", new GymForge.Desktop.Views.Settings.SettingsView { DataContext = NewSettingsViewModel() }, 1180, 3300, outDir);
liveLicense.State = licenseBeforeMismatch;

// Clases v2: agenda semanal con un horario seleccionado y sus reservas
var classesVm = new GymForge.Desktop.ViewModels.Classes.ClassesViewModel(
    mediator2, sp.GetRequiredService<IMemberRepository>(), session, sp.GetRequiredService<GymForge.Application.Services.ICompanyCalendar>());
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

// ── Fase 3: lector de códigos (keyboard-wedge), teclado numérico y foto por archivo ───────────────────────
// Lucas recibe una credencial y una foto real (un PNG generado); la ficha las muestra.
var lucas = membersVm.Members.First(m => m.DocumentNumber == "32333444");
WaitFor(mediator2.Send(new AssignCredentialCommand(session.CompanyId, lucas.Id, "CARNET-77")));
var photoPath = sp.GetRequiredService<IMemberPhotoStore>().Save(lucas.Id, SamplePhotoPng());
var lucasWithPhoto = WaitFor(mediator2.Send(new UpdateMemberPhotoCommand(session.CompanyId, lucas.Id, photoPath)));

var fichaVm = sp.GetRequiredService<MemberDetailViewModel>();
Pump(fichaVm.LoadAsync(lucasWithPhoto));
Capture("28-ficha-datos-foto-credencial", new MemberDetailView { DataContext = fichaVm }, 1180, 900, outDir);

// Regresión (qa): una foto de celular (12 MP) se decodifica a lo sumo a PhotoBitmap.DecodeWidth px de ancho. Entera eran
// ~48 MB por ingreso al kiosk: 60 ingresos seguidos llevaron el proceso de 260 MB a 3 GB.
var bigPhoto = PhotoBitmap.TryDecode(BigPhotoPng());
Check("foto: una de 12 MP se decodifica reducida y con su proporción",
    bigPhoto is { PixelSize: { Width: PhotoBitmap.DecodeWidth, Height: 360 } });

// Kiosk en reposo con el teclado numérico (táctil).
var idleKioskVm = new GymForge.Desktop.ViewModels.Checkin.CheckInKioskViewModel(
    sp.GetRequiredService<ValidateSwipeUseCase>(), mediator2, session);
Capture("29-checkin-teclado", new GymForge.Desktop.Views.Checkin.CheckInKioskView { DataContext = idleKioskVm }, 1180, 900, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
Capture("29-checkin-teclado-dark", new GymForge.Desktop.Views.Checkin.CheckInKioskView { DataContext = idleKioskVm }, 1180, 900, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;

// Escaneo de punta a punta: teclas reales a la ventana del kiosk (vista + listener + Gatekeeper + base).
var scanKioskVm = new GymForge.Desktop.ViewModels.Checkin.CheckInKioskViewModel(
    sp.GetRequiredService<ValidateSwipeUseCase>(), mediator2, session);
var scanWindow = new Window
{
    Width = 1180, Height = 900, SystemDecorations = SystemDecorations.None,
    Content = new GymForge.Desktop.Views.Checkin.CheckInKioskView { DataContext = scanKioskVm },
};
scanWindow.Show();
for (int i = 0; i < 25; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }

var rowsBeforeScan = scanKioskVm.TodaysAccess.Count;
TypeLikeAScanner(scanWindow, "CARNET-77");   // credencial del gimnasio → permitido (con su foto)
WaitUntil(() => scanKioskVm.State != GymForge.Desktop.ViewModels.Checkin.KioskState.Idle);
Check("scanner: credencial del carnet -> permitido", scanKioskVm.IsGranted && scanKioskVm.MemberName == "Lucas Fernández");
Check("scanner: el campo de DNI queda vacío", scanKioskVm.CredentialInput.Length == 0);
// Regresión (qa): el 1er carácter del lector llega al campo de DNI y el Enter final no debe disparar además una carga
// manual con ese carácter (quedaba una fila "Desconocido" por cada escaneo en el AccessLog, que es append-only).
Check("scanner: un escaneo deja UNA sola fila (sin carga manual espuria)",
    scanKioskVm.TodaysAccess.Count == rowsBeforeScan + 1 && scanKioskVm.TodaysAccess[0].MemberName == "Lucas Fernández");
SaveFrame("30-checkin-escaneo-aprobado-foto", scanWindow, outDir);

TypeLikeAScanner(scanWindow, "34555666");    // DNI de un socio sin membresía → lo identifica y lo rechaza
WaitUntil(() => scanKioskVm.MemberName == "Diego Ramírez");
Check("scanner: DNI sin credencial -> resuelve al socio por DNI", scanKioskVm.IsDenied && scanKioskVm.MemberName == "Diego Ramírez");

TypeLikeAScanner(scanWindow, "99999999");    // nadie
WaitUntil(() => scanKioskVm.DenialMessage == "Credencial no reconocida");
Check("scanner: código desconocido -> credencial no reconocida", scanKioskVm.IsDenied && scanKioskVm.MemberName.Length == 0);

scanKioskVm.ResetCommand.Execute(null);      // vuelve a reposo; ahora tecleo humano (lento): no se traga
foreach (var digit in "32333")
{
    scanWindow.KeyPress(Key.None, RawInputModifiers.None, PhysicalKey.None, digit.ToString());
    scanWindow.KeyTextInput(digit.ToString());
    Thread.Sleep(90);
}
Check("tecleo manual: llega al campo de DNI sin tragarse", scanKioskVm.CredentialInput == "32333");
foreach (var digit in "444")
{
    scanWindow.KeyPress(Key.None, RawInputModifiers.None, PhysicalKey.None, digit.ToString());
    scanWindow.KeyTextInput(digit.ToString());
    Thread.Sleep(90);
}
scanWindow.KeyPress(Key.Enter, RawInputModifiers.None);   // Enter a mano en el campo de DNI = Ingresar (carga manual)
WaitUntil(() => scanKioskVm.State != GymForge.Desktop.ViewModels.Checkin.KioskState.Idle);
Check("tecleo manual + Enter -> ingresa por DNI", scanKioskVm.MemberName == "Lucas Fernández");
scanWindow.Close();

// Regresión (qa): con teclas reales y el foco en el botón que abrió el modal (que NO es hijo del modal), el escaneo igual
// llega. Un listener colgado de la vista del modal no oía nada: la tecla solo viaja por los ancestros del control enfocado.
var posSale = cashVm.SaleModal!;
var posWindow = new Window
{
    Width = 1180, Height = 1000, SystemDecorations = SystemDecorations.None,
    Content = new CashView { DataContext = cashVm },
};
posWindow.Show();
for (int i = 0; i < 25; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
posWindow.GetVisualDescendants().OfType<Button>().First(b => b.Command == cashVm.OpenSaleModalCommand).Focus();
TypeLikeAScanner(posWindow, "7791234567890");
WaitUntil(() => posSale.SelectedProduct is not null);
Check("POS (teclas reales, foco fuera del modal): código de barras -> producto elegido, modal abierto",
    posSale.SelectedProduct?.Sku == "TOAL-GF" && cashVm.IsSaleModalOpen);
posWindow.Close();
posSale.SelectedProduct = null;   // los checks de abajo parten de "sin producto"
posSale.Quantity = 1;

// Caja: un código escaneado elige producto (el de la toalla, creado después de abrir el modal) o socio.
var sale = cashVm.SaleModal!;
Pump(sale.ScanAsync("7791234567890"));
Check("POS: código de barras -> producto elegido", sale.IsProduct && sale.SelectedProduct?.Sku == "TOAL-GF");
Pump(sale.ScanAsync("7791234567890"));
Check("POS: el mismo producto otra vez suma una unidad", sale.Quantity == 2);
Capture("31-caja-escaneo-producto", new CashView { DataContext = cashVm }, 1180, 1000, outDir);
Pump(sale.ScanAsync("CARNET-77"));
Check("POS: credencial -> socio elegido", sale.SelectedMember?.FullName == "Lucas Fernández" && !sale.IsWalkIn);
Pump(sale.ScanAsync("0000"));
Check("POS: código desconocido -> aviso", sale.ErrorMessage is not null);
Capture("32-caja-escaneo-socio-desconocido", new CashView { DataContext = cashVm }, 1180, 1000, outDir);

// Morosos: deudores de la sede (con un recordatorio ya enviado a uno) y el cobro con el modal de pago.
var collectionsVm = sp.GetRequiredService<CollectionsViewModel>();
Pump(collectionsVm.LoadCommand.ExecuteAsync(null));
Capture("25-morosos", new CollectionsView { DataContext = collectionsVm }, 1180, 720, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
Capture("25-morosos-dark", new CollectionsView { DataContext = collectionsVm }, 1180, 720, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
collectionsVm.ChargeCommand.Execute(collectionsVm.Rows.First());
Capture("26-morosos-cobrar", new CollectionsView { DataContext = collectionsVm }, 1180, 720, outDir);
collectionsVm.PaymentModal!.CancelCommand.Execute(null);

// Morosos con la caja cerrada + intento de cobro: los dos avisos apilados. A 1126 px (1366 con sidebar) y con
// un nombre de etapa de 40 caracteres. La vista recarga (y limpia el mensaje) al recibir el DataContext,
// así que el cobro se intenta recién cuando terminó de cargar.
var dunningConfig = sp.GetRequiredService<DunningConfig>();
var stagesBefore = dunningConfig.Stages;
dunningConfig.Stages = stagesBefore
    .Select(s => s.DaysOverdue == 15 ? s with { Label = "Etapa con nombre muy largo de 40 chars!!" } : s)
    .ToList();
session.SetOpenShift(null);

CollectionsView NewViewWithBlockedCharge()
{
    var vm = sp.GetRequiredService<CollectionsViewModel>();
    var view = new CollectionsView { DataContext = vm };
    for (int i = 0; i < 25; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
    vm.ChargeCommand.Execute(vm.Rows.First());
    return view;
}

Capture("25c-morosos-avisos-apilados-1126", NewViewWithBlockedCharge(), 1126, 720, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
Capture("25c-morosos-avisos-apilados-1126-dark", NewViewWithBlockedCharge(), 1126, 720, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
dunningConfig.Stages = stagesBefore;

// Morosos sin turno de caja (InfoBar informativo) y sede sin deudores (estado vacío, sin total), en light y dark.
// Cambiar de sede cierra la referencia al turno: hay que reabrirlo al volver a la sede principal.
var mainSite = session.CurrentSite;
session.CurrentSite = session.Sites[1];
var emptyCollectionsVm = sp.GetRequiredService<CollectionsViewModel>();
Pump(emptyCollectionsVm.LoadCommand.ExecuteAsync(null));
Capture("25b-morosos-vacio-caja-cerrada", new CollectionsView { DataContext = emptyCollectionsVm }, 1180, 720, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
Capture("25b-morosos-vacio-caja-cerrada-dark", new CollectionsView { DataContext = emptyCollectionsVm }, 1180, 720, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
session.CurrentSite = mainSite;
session.SetOpenShift(shift.Id);

// Morosos ordenado desde el encabezado: clic en "Deuda" (SortMemberPath anidado "Dto.TotalOutstanding" sobre una
// columna de plantilla). Orden esperado de menor a mayor: Carlos ($28.000), Diego ($35.000), Sofía ($70.000).
var sortVm = sp.GetRequiredService<CollectionsViewModel>();
var sortWindow = new Window
{
    Width = 1180, Height = 720, SystemDecorations = SystemDecorations.None,
    Content = new CollectionsView { DataContext = sortVm },
};
sortWindow.Show();
for (int i = 0; i < 25; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
var deudaHeader = sortWindow.GetVisualDescendants().OfType<DataGridColumnHeader>().First(h => h.Content as string == "Deuda");
var headerCenter = deudaHeader.TranslatePoint(new Point(deudaHeader.Bounds.Width / 2, deudaHeader.Bounds.Height / 2), sortWindow)!.Value;
sortWindow.MouseDown(headerCenter, MouseButton.Left);
sortWindow.MouseUp(headerCenter, MouseButton.Left);
for (int i = 0; i < 25; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
CaptureWindow("25d-morosos-orden-por-deuda", sortWindow, outDir);

// Cobros: el saldo total pasó a DangerTextBrush; se revisa en light y dark junto con Morosos.
var chargesVm = sp.GetRequiredService<GymForge.Desktop.ViewModels.Charges.ChargesViewModel>();
Capture("27-cobros", new GymForge.Desktop.Views.Charges.ChargesView { DataContext = chargesVm }, 1180, 720, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
Capture("27-cobros-dark", new GymForge.Desktop.Views.Charges.ChargesView { DataContext = chargesVm }, 1180, 720, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;

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

// Paleta de comandos (Ctrl+K): abierta, filtrada sin acentos, sin resultados y con un acento claro (amarillo).
var shellVm = sp.GetRequiredService<GymForge.Desktop.Views.Shell.MainWindowViewModel>();
var shellPalette = new GymForge.Desktop.Views.Shell.MainWindow { DataContext = shellVm, Width = 1366, Height = 800 };
shellPalette.Show();
shellVm.Palette.OpenCommand.Execute(null);
SaveFrame("38-paleta-abierta", shellPalette, outDir);
shellVm.Palette.Query = "configuracion";
SaveFrame("38-paleta-filtrada", shellPalette, outDir);
shellVm.Palette.Query = "zzz";
SaveFrame("38-paleta-vacia", shellPalette, outDir);
shellVm.Palette.CloseCommand.Execute(null);
GymForge.Desktop.App.ApplyBrandAccent("#FACC15");
shellVm.Palette.OpenCommand.Execute(null);
SaveFrame("38-paleta-marca-amarilla", shellPalette, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
SaveFrame("38-paleta-marca-amarilla-dark", shellPalette, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
shellPalette.Close();
GymForge.Desktop.App.ApplyBrandAccent("#6366F1");

// Auto-actualización: el origen simulado pasa a ofrecer una versión nueva y el shell muestra el aviso.
// La ventana ya busca sola al abrirse; acá se fuerza la búsqueda para dejar el estado "lista" listo antes.
sp.GetRequiredService<SampleUpdateService>().NextCheck = UpdateCheckResult.Found(new AvailableUpdate("0.4.0"));
Pump(sp.GetRequiredService<GymForge.Desktop.ViewModels.Shared.UpdateViewModel>().CheckAsync());
var shellUpdate = new GymForge.Desktop.Views.Shell.MainWindow
{
    DataContext = sp.GetRequiredService<GymForge.Desktop.Views.Shell.MainWindowViewModel>(),
    Width = 1366,
    Height = 800,
};
CaptureWindow("24-shell-actualizacion-lista", shellUpdate, outDir);

// Freeze: pestaña Membresías de la ficha, modal de congelamiento (normal y pasado del tope) y membresía congelada.
var freezeMember = membersVm.Members.First(m => m.Status == MemberStatus.Active);
var detailVm = sp.GetRequiredService<MemberDetailViewModel>();
Pump(detailVm.LoadAsync(freezeMember));
Pump(detailVm.SwitchTabCommand.ExecuteAsync(MemberDetailTab.Memberships));
Capture("19-socio-membresias", new MemberDetailView { DataContext = detailVm }, 1180, 720, outDir);

var activeMembership = detailVm.Memberships.First(m => m.Status == MembershipStatus.Active);
Pump(detailVm.OpenFreezeModalCommand.ExecuteAsync(activeMembership));
Capture("20-freeze-modal", new MemberDetailView { DataContext = detailVm }, 1180, 720, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
Capture("20-freeze-modal-dark", new MemberDetailView { DataContext = detailVm }, 1180, 720, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;

var freezeModal = detailVm.FreezeModal!;
freezeModal.ReactivationDate = freezeModal.StartDate.AddDays(freezeModal.Quota.RemainingDays + 1);
Capture("21-freeze-modal-tope", new MemberDetailView { DataContext = detailVm }, 1180, 720, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
Capture("21-freeze-modal-tope-dark", new MemberDetailView { DataContext = detailVm }, 1180, 720, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;

freezeModal.ReactivationDate = freezeModal.StartDate.AddDays(30);
Pump(freezeModal.ConfirmCommand.ExecuteAsync(null));
for (int i = 0; i < 15; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }  // recarga de la lista tras congelar
Capture("22-socio-membresia-congelada", new MemberDetailView { DataContext = detailVm }, 1180, 720, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
Capture("22-socio-membresia-congelada-dark", new MemberDetailView { DataContext = detailVm }, 1180, 720, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;

Pump(detailVm.ReactivateMembershipCommand.ExecuteAsync(detailVm.Memberships.First(m => m.Status == MembershipStatus.Frozen)));
Capture("23-socio-membresia-reactivada", new MemberDetailView { DataContext = detailVm }, 1180, 720, outDir);

// ── Fase 4 parte 2: editar socio, rutinas en la ficha y comisiones ───────────────────────────────────────
var commissionMember = membersVm.Members.First(m => m.Status == MemberStatus.Active);
var editVm = sp.GetRequiredService<MemberDetailViewModel>();
Pump(editVm.LoadAsync(commissionMember));
editVm.StartEditCommand.Execute(null);
Capture("33-ficha-editar", new MemberDetailView { DataContext = editVm }, 1180, 1700, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
Capture("33-ficha-editar-dark", new MemberDetailView { DataContext = editVm }, 1180, 1700, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
editVm.Edit.CancelCommand.Execute(null);
Pump(editVm.SwitchTabCommand.ExecuteAsync(MemberDetailTab.Routines));
for (int i = 0; i < 15; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }  // carga de las rutinas del socio
Capture("34-ficha-rutinas", new MemberDetailView { DataContext = editVm }, 1180, 820, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
Capture("34-ficha-rutinas-dark", new MemberDetailView { DataContext = editVm }, 1180, 820, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;

// Comisiones: tarifa del cajero + una venta de membresía y una de producto que generen comisión.
WaitFor(mediator2.Send(new UpdateStaffCommissionsCommand(session.CompanyId, staff.Id, 10m, 5m)));
var paidPlan = WaitFor(mediator2.Send(new GetMembershipTypesQuery(session.CompanyId))).First(p => p.Price > 0);
WaitFor(mediator2.Send(new SellMembershipCommand(
    session.CompanyId, session.SiteId, staff.Id, shift.Id, commissionMember.Id, paidPlan.Id, PaymentMethod.Cash)));
var commissionProduct = productsVm.Products.First(p => p.Sku == "AGUA-500");
WaitFor(mediator2.Send(new SellProductCommand(
    session.CompanyId, session.SiteId, staff.Id, shift.Id, commissionProduct.Id, 2m, null, PaymentMethod.Cash)));

var settingsWithCommissions = NewSettingsViewModel();
Pump(settingsWithCommissions.LoadCommand.ExecuteAsync(null));
Capture("35-config-comisiones", new GymForge.Desktop.Views.Settings.SettingsView { DataContext = settingsWithCommissions }, 1180, 3600, outDir);

Pump(productsVm.LoadCommand.ExecuteAsync(null));
productsVm.EditProductCommand.Execute(productsVm.Products.First(p => p.Sku == "AGUA-500"));
Capture("36-productos-comision", new GymForge.Desktop.Views.Products.ProductsView { DataContext = productsVm }, 1180, 760, outDir);

reportsVm.CompanyWide = false;
reportsVm.SelectedReport = GymForge.Desktop.ViewModels.Reports.ReportKind.StaffCommissions;
Pump(reportsVm.LoadCommand.ExecuteAsync(null));
Capture("37-reportes-comisiones", new GymForge.Desktop.Views.Reports.ReportsView { DataContext = reportsVm }, 1180, 760, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
Capture("37-reportes-comisiones-dark", new GymForge.Desktop.Views.Reports.ReportsView { DataContext = reportsVm }, 1180, 760, outDir);
Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
Check("comisiones: el reporte suma la membresía y el producto del cajero",
    reportsVm.CommissionRows.Count == 1 && reportsVm.CommissionRows[0].MembershipCommission > 0
    && reportsVm.CommissionRows[0].ProductCommission > 0
    && reportsVm.CommissionsTotal == reportsVm.CommissionRows[0].Total);

// Onboarding de primer arranque (asistente de configuración inicial).
var onboardingVm = sp.GetRequiredService<GymForge.Desktop.ViewModels.Onboarding.OnboardingViewModel>();
onboardingVm.GymName = "PowerGym";
onboardingVm.TaxId = "30-11223344-5";
onboardingVm.SiteAddress = "Av. Rivadavia 4500, CABA";
onboardingVm.AdminFirstName = "Lucía";
onboardingVm.AdminLastName = "Fernández";
onboardingVm.AdminPin = "1234";
onboardingVm.AdminPinConfirm = "1234";
var onboardingWindow = new GymForge.Desktop.Views.Onboarding.OnboardingWindow
{
    DataContext = onboardingVm,
    Width = 660,
    Height = 880,
};
CaptureWindow("18-onboarding", onboardingWindow, outDir);

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

static void WaitUntil(Func<bool> condition)
{
    for (var i = 0; i < 400 && !condition(); i++)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(5);
    }
}

static void Check(string what, bool ok)
{
    Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}");
    if (!ok) Environment.ExitCode = 1;
}

// Un lector keyboard-wedge: todos los caracteres pegados (sin pausas) y un Enter al final. Como en Windows, la tecla
// que el listener deja pasar (la primera) además escribe su carácter en el campo con foco; las tragadas no.
static void TypeLikeAScanner(Window window, string code)
{
    for (var i = 0; i < code.Length; i++)
    {
        window.KeyPress(Key.None, RawInputModifiers.None, PhysicalKey.None, code[i].ToString());
        if (i == 0) window.KeyTextInput(code[i].ToString());
    }
    window.KeyPress(Key.Enter, RawInputModifiers.None);
}

// Una foto del tamaño de las de un celular (4000x3000, 12 MP): un PNG liso, que pesa poco pero decodificado es enorme.
static byte[] BigPhotoPng()
{
    var face = new Border { Width = 4000, Height = 3000, Background = Avalonia.Media.Brushes.SteelBlue };
    face.Measure(new Size(4000, 3000));
    face.Arrange(new Rect(0, 0, 4000, 3000));

    using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(4000, 3000), new Vector(96, 96));
    bitmap.Render(face);
    using var stream = new MemoryStream();
    bitmap.Save(stream);
    return stream.ToArray();
}

// Una foto de muestra (PNG de verdad) para la ficha y el kiosk.
static byte[] SamplePhotoPng()
{
    var face = new Border
    {
        Width = 240, Height = 280, Background = Avalonia.Media.Brushes.SteelBlue,
        Child = new TextBlock
        {
            Text = "LF", FontSize = 96, Foreground = Avalonia.Media.Brushes.White,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        },
    };
    face.Measure(new Size(240, 280));
    face.Arrange(new Rect(0, 0, 240, 280));

    using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(240, 280), new Vector(96, 96));
    bitmap.Render(face);
    using var stream = new MemoryStream();
    bitmap.Save(stream);
    return stream.ToArray();
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
    SaveFrame(name, window, outDir);
    window.Close();
}

// Renderiza la ventana (ya mostrada) a PNG y la deja abierta: sirve para capturar a mitad de un recorrido.
static void SaveFrame(string name, Window window, string outDir)
{
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

/// <summary>Cuotas vencidas para la pantalla de morosos: dos socios con celular y uno sin, y un recordatorio ya enviado.</summary>
static async Task SeedSampleDebtorsAsync(IServiceProvider sp, SessionContext session)
{
    var mediator = sp.GetRequiredService<IMediator>();
    var memberRepo = sp.GetRequiredService<IMemberRepository>();
    var today = DateOnly.FromDateTime(DateTime.Today);

    var members = await memberRepo.GetPagedAsync(session.CompanyId, session.SiteId, 1, 10);
    var sofia = members.First(m => m.DocumentNumber == "33444555");
    var diego = members.First(m => m.DocumentNumber == "34555666");
    var carlos = await mediator.Send(new CreateMemberCommand(
        session.CompanyId, session.SiteId, "Carlos", "Suárez",
        DocumentType.DNI, "35666777", Gender.Male, null, null, new DateOnly(1990, 3, 2)));

    async Task OweAsync(Guid memberId, int daysOverdue, decimal amount) =>
        await mediator.Send(new CreateChargeCommand(
            session.CompanyId, session.SiteId, memberId, null, ConceptType.MembershipFee,
            "Cuota mensual", amount, today.AddDays(-daysOverdue)));

    await OweAsync(sofia.Id, 12, 35_000m);
    await OweAsync(sofia.Id, 42, 35_000m);
    await OweAsync(diego.Id, 4, 35_000m);
    await OweAsync(carlos.Id, 20, 28_000m);

    await mediator.Send(new SendWhatsAppReminderCommand(session.CompanyId, session.SiteId, sofia.Id));
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

/// <summary>Origen de actualizaciones de mentira: responde lo que se le indique, sin red.</summary>
sealed class SampleUpdateService : IUpdateService
{
    public UpdateCheckResult NextCheck { get; set; } = UpdateCheckResult.UpToDate;

    public string CurrentVersion => "0.3.0";

    public Task<UpdateCheckResult> CheckAsync() => Task.FromResult(NextCheck);

    public Task<bool> DownloadAsync(AvailableUpdate update) => Task.FromResult(true);

    public void ApplyAndRestart(AvailableUpdate update) { }

    public void ApplyOnExit(AvailableUpdate update) { }
}
