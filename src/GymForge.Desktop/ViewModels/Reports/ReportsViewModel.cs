using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Dunning;
using GymForge.Application.UseCases.Reports;
using GymForge.Desktop.Services;
using GymForge.Desktop.ViewModels.Collections;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Desktop.ViewModels.Reports;

/// <summary>
/// Hub de Reportes: el dueño elige un reporte (recaudación, desagregada, morosos, altas y bajas), ve los datos
/// y los exporta a PDF o Excel. Lo exportado es lo que está en pantalla: el documento se arma con los datos
/// ya cargados, no se vuelve a consultar.
/// </summary>
public partial class ReportsViewModel : ObservableObject
{
    /// <summary>Los reportes del selector, en orden (el texto de cada uno lo pone EnumDisplayConverter).</summary>
    public static IReadOnlyList<ReportKind> ReportKinds { get; } = Enum.GetValues<ReportKind>();

    /// <summary>Cómo armar el documento del reporte cargado, y de qué sede es (null = toda la empresa).</summary>
    private sealed record ExportPlan(Guid? SiteId, Func<ReportContext, DateTime, ReportDocument> Build);

    private readonly IMediator _mediator;
    private readonly SessionContext _session;
    private readonly ReceiptService _receipts;
    private readonly ICompanyCalendar _calendar;
    private readonly IReportExportService _exporter;
    private readonly IClock _clock;

    // Cada carga lleva un número: una respuesta tardía de un reporte o rango anterior no pisa lo vigente.
    private int _loadVersion;
    private ExportPlan? _exportPlan;

    // ── Selección ────────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsPaymentsReport), nameof(IsBreakdownReport), nameof(IsOverdueReport), nameof(IsFlowReport),
        nameof(UsesDateRange), nameof(SupportsScope), nameof(EmptyTitle), nameof(EmptyBody), nameof(IsEmpty))]
    private ReportKind _selectedReport = ReportKind.Payments;

    [ObservableProperty] private DateOnly? _from;
    [ObservableProperty] private DateOnly? _to;

    /// <summary>Alcance de la desagregada y de altas/bajas: false = esta sede, true = toda la empresa.</summary>
    [ObservableProperty] private bool _companyWide;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private string? _exportedPath;

    // ── Recaudación del período ──────────────────────────────────────────────
    [ObservableProperty] private decimal _total;
    [ObservableProperty] private int _count;
    [ObservableProperty] private ObservableCollection<ReportPaymentRow> _rows = [];

    // ── Recaudación desagregada ──────────────────────────────────────────────
    [ObservableProperty] private decimal _breakdownTotal;
    [ObservableProperty] private ObservableCollection<BreakdownLine> _byMethod = [];
    [ObservableProperty] private ObservableCollection<BreakdownLine> _byConcept = [];
    [ObservableProperty] private ObservableCollection<BreakdownLine> _bySite = [];

    // ── Morosos ──────────────────────────────────────────────────────────────
    [ObservableProperty] private decimal _debtTotal;
    [ObservableProperty] private ObservableCollection<OverdueMemberRow> _debtors = [];

    // ── Altas y bajas ────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveNow), nameof(FrozenNow), nameof(OverdueNow), nameof(SuspendedNow))]
    private MemberFlow? _flow;

    public ReportsViewModel(
        IMediator mediator, SessionContext session, ReceiptService receipts,
        ICompanyCalendar calendar, IReportExportService exporter, IClock clock)
    {
        _mediator = mediator;
        _session = session;
        _receipts = receipts;
        _calendar = calendar;
        _exporter = exporter;
        _clock = clock;

        var today = DateOnly.FromDateTime(DateTime.Today);
        _from = new DateOnly(today.Year, today.Month, 1);
        _to = today;
    }

    // ── Estado derivado para la vista ────────────────────────────────────────

    public bool IsPaymentsReport => SelectedReport == ReportKind.Payments;
    public bool IsBreakdownReport => SelectedReport == ReportKind.RevenueBreakdown;
    public bool IsOverdueReport => SelectedReport == ReportKind.Overdue;
    public bool IsFlowReport => SelectedReport == ReportKind.MemberFlow;

    /// <summary>Los morosos son "a hoy": no tienen rango de fechas.</summary>
    public bool UsesDateRange => SelectedReport != ReportKind.Overdue;

    /// <summary>Solo la desagregada y altas/bajas se pueden pedir de toda la empresa.</summary>
    public bool SupportsScope => SelectedReport is ReportKind.RevenueBreakdown or ReportKind.MemberFlow;

    /// <summary>El límite de "altas y bajas" (foto de hoy, bajas = cancelaciones), el mismo texto que lleva el archivo.</summary>
    public string FlowNote => ReportBuilders.FlowNote;

    public int ActiveNow => Flow?.ActiveNow ?? 0;
    public int FrozenNow => StatusCount(MemberStatus.Frozen);
    public int OverdueNow => StatusCount(MemberStatus.Overdue);
    public int SuspendedNow => StatusCount(MemberStatus.Suspended);

    private int StatusCount(MemberStatus status) => Flow?.StatusCounts.GetValueOrDefault(status) ?? 0;

    private bool HasData => SelectedReport switch
    {
        ReportKind.Payments => Rows.Count > 0,
        ReportKind.RevenueBreakdown => ByMethod.Count > 0,
        ReportKind.Overdue => Debtors.Count > 0,
        ReportKind.MemberFlow => Flow is not null,
        _ => false,
    };

    public bool IsEmpty => !IsLoading && !HasData;

    public string EmptyTitle => SelectedReport switch
    {
        ReportKind.Overdue => "Sin morosos",
        ReportKind.MemberFlow => "Sin datos para mostrar",
        _ => "Sin pagos en el período",
    };

    public string EmptyBody => SelectedReport switch
    {
        ReportKind.Overdue => "Ningún socio de esta sede tiene cuotas vencidas.",
        ReportKind.MemberFlow => "No se pudo calcular el reporte. Probá actualizarlo.",
        _ => "Ajustá el rango de fechas o registrá cobros desde Caja.",
    };

    partial void OnIsLoadingChanged(bool value) => RefreshState();
    partial void OnFromChanged(DateOnly? value) => _ = LoadAsync();
    partial void OnToChanged(DateOnly? value) => _ = LoadAsync();
    partial void OnCompanyWideChanged(bool value) => _ = LoadAsync();

    partial void OnSelectedReportChanged(ReportKind value)
    {
        Message = null;
        ExportedPath = null;
        CompleteLoad(plan: null);
        _ = LoadAsync();
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(IsEmpty));
        ExportCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Deja lista la exportación del reporte recién cargado (o nada que exportar) y refresca la vista.</summary>
    private void CompleteLoad(ExportPlan? plan)
    {
        _exportPlan = plan;
        RefreshState();
    }

    // ── Carga ────────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_session.SiteId == Guid.Empty) return;

        var version = ++_loadVersion;
        IsLoading = true;
        Message = null;
        try
        {
            await LoadSelectedAsync(version, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (IsStale(version)) return;

            ClearLoaded();
            Message = $"No se pudo cargar el reporte: {ex.Message}";
        }
        finally
        {
            if (!IsStale(version)) IsLoading = false;
        }
    }

    private bool IsStale(int version) => version != _loadVersion;

    private Task LoadSelectedAsync(int version, CancellationToken ct)
    {
        if (SelectedReport == ReportKind.Overdue) return LoadOverdueAsync(version, ct);
        if (From is not { } from || To is not { } to) return Task.CompletedTask;

        return SelectedReport switch
        {
            ReportKind.Payments => LoadPaymentsAsync(from, to, version, ct),
            ReportKind.RevenueBreakdown => LoadBreakdownAsync(from, to, version, ct),
            ReportKind.MemberFlow => LoadFlowAsync(from, to, version, ct),
            _ => Task.CompletedTask,
        };
    }

    /// <summary>La sede del reporte según el alcance elegido: null = toda la empresa.</summary>
    private Guid? ScopedSiteId() => CompanyWide ? null : _session.SiteId;

    private async Task LoadPaymentsAsync(DateOnly from, DateOnly to, int version, CancellationToken ct)
    {
        var report = await _mediator.Send(
            new GetPaymentsReportQuery(_session.CompanyId, _session.SiteId, from, to), ct);
        if (IsStale(version)) return;

        Total = report.Total;
        Count = report.Count;
        Rows = new ObservableCollection<ReportPaymentRow>(report.Rows);
        CompleteLoad(report.Rows.Count > 0
            ? new ExportPlan(_session.SiteId, (context, now) => ReportBuilders.Payments(report, from, to, context, now))
            : null);
    }

    private async Task LoadBreakdownAsync(DateOnly from, DateOnly to, int version, CancellationToken ct)
    {
        var siteId = ScopedSiteId();
        var data = await _mediator.Send(new GetRevenueBreakdownQuery(_session.CompanyId, siteId, from, to), ct);
        if (IsStale(version)) return;

        BreakdownTotal = data.Total;
        ByMethod = Lines(data.ByMethod, data.Total);
        ByConcept = Lines(data.ByConcept, data.Total);
        BySite = Lines(data.BySite, data.Total);
        CompleteLoad(data.ByMethod.Count > 0
            ? new ExportPlan(siteId, (context, now) => ReportBuilders.Breakdown(data, from, to, context, now))
            : null);
    }

    private async Task LoadOverdueAsync(int version, CancellationToken ct)
    {
        var asOf = await _calendar.TodayAsync(_session.CompanyId, ct);
        var debtors = await _mediator.Send(new GetOverdueMembersQuery(_session.CompanyId, _session.SiteId, asOf), ct);
        if (IsStale(version)) return;

        DebtTotal = debtors.Sum(d => d.TotalOutstanding);
        Debtors = new ObservableCollection<OverdueMemberRow>(debtors.Select(d => new OverdueMemberRow(d, asOf)));
        CompleteLoad(debtors.Count > 0
            ? new ExportPlan(_session.SiteId, (context, now) => ReportBuilders.Overdue(debtors, asOf, context, now))
            : null);
    }

    private async Task LoadFlowAsync(DateOnly from, DateOnly to, int version, CancellationToken ct)
    {
        var siteId = ScopedSiteId();
        var flow = await _mediator.Send(new GetMemberFlowQuery(_session.CompanyId, siteId, from, to), ct);
        if (IsStale(version)) return;

        Flow = flow;
        CompleteLoad(new ExportPlan(siteId, (context, now) => ReportBuilders.Flow(flow, from, to, context, now)));
    }

    private static ObservableCollection<BreakdownLine> Lines(IEnumerable<RevenueBreakdownRow> rows, decimal total) =>
        new(rows.Select(r => BreakdownLine.From(r, total)));

    /// <summary>Tras una carga fallida no queda nada viejo a la vista ni para exportar.</summary>
    private void ClearLoaded()
    {
        Total = 0;
        Count = 0;
        Rows = [];
        BreakdownTotal = 0;
        ByMethod = [];
        ByConcept = [];
        BySite = [];
        DebtTotal = 0;
        Debtors = [];
        Flow = null;
        CompleteLoad(plan: null);
    }

    // ── Exportar ─────────────────────────────────────────────────────────────

    private bool CanExport => !IsLoading && _exportPlan is not null;

    /// <summary>Arma el documento del reporte que se ve y lo guarda como PDF o Excel donde elija el usuario.</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync(ReportFormat format, CancellationToken ct = default)
    {
        if (_exportPlan is not { } plan) return;

        Message = null;
        ExportedPath = null;
        try
        {
            var context = await _mediator.Send(new GetReportContextQuery(_session.CompanyId, plan.SiteId), ct);
            var document = plan.Build(context, _clock.Now);
            ExportedPath = await _exporter.ExportAsync(document, format, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Message = $"No se pudo exportar el reporte: {ex.Message}";
        }
    }

    // ── Recibos y atajos de fecha ────────────────────────────────────────────

    /// <summary>Reimprime el recibo de un pago: regenera el PDF y lo abre.</summary>
    [RelayCommand]
    private async Task ReprintReceiptAsync(Guid paymentId)
    {
        if (paymentId == Guid.Empty) return;
        await _receipts.TryGenerateAndOpenAsync(paymentId, _session.CompanyId);
    }

    [RelayCommand]
    private void ThisMonth()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        From = new DateOnly(today.Year, today.Month, 1);
        To = today;
    }

    [RelayCommand]
    private void Today()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        From = today;
        To = today;
    }
}
