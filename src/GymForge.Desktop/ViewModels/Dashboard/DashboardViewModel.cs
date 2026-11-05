using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Desktop.Services;
using GymForge.Domain.Enums;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace GymForge.Desktop.ViewModels.Dashboard;

/// <summary>Barra del gráfico de recaudación (un día). Altura ya escalada en px.</summary>
public record RevenueBarVm(double BarHeight, string Tooltip, bool IsToday);

/// <summary>Referencia del eje Y: etiqueta de monto y su posición vertical (px desde el tope del plot, centro de la línea).</summary>
public record AxisTickVm(string Label, double Y)
{
    private const double LabelHalfHeight = 7;

    /// <summary>Holgura sobre el plot (y bajo él, la mitad de una etiqueta) para que las etiquetas de los extremos no queden cortadas.</summary>
    public const double PlotTopInset = 8;

    /// <summary>Posición del borde superior de la etiqueta (en el gutter, que incluye la holgura) para que quede centrada sobre su línea de guía.</summary>
    public double LabelTop => Y + PlotTopInset - LabelHalfHeight;
}

/// <summary>Membresía que vence en los próximos días.</summary>
public record ExpiringRowVm(string Initials, string MemberName, string PlanName, string WhenLabel, bool IsUrgent);

/// <summary>Acceso reciente del día (permitido o rechazado).</summary>
public record AccessRowVm(string Initials, string MemberName, string TimeLabel, bool Granted, string StatusLabel);

/// <summary>Repos de una carga del Dashboard: salen de un scope propio de esa carga (contexto fresco, no cautivo).</summary>
internal sealed record DashboardSources(
    IMemberRepository Members, IPaymentRepository Payments, IAccessLogRepository AccessLogs,
    IChargeRepository Charges, IMembershipRepository Memberships, ISiteRepository Sites)
{
    public static DashboardSources From(IServiceProvider scope) => new(
        scope.GetRequiredService<IMemberRepository>(), scope.GetRequiredService<IPaymentRepository>(),
        scope.GetRequiredService<IAccessLogRepository>(), scope.GetRequiredService<IChargeRepository>(),
        scope.GetRequiredService<IMembershipRepository>(), scope.GetRequiredService<ISiteRepository>());
}

public partial class DashboardViewModel : ObservableObject
{
    private const double ChartHeight = 130;

    // Singleton: los repos son Scoped, así que cada carga abre su propio scope (no se inyectan acá).
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SessionContext _session;

    // Zona del gimnasio de la última carga: convierte entre los instantes UTC guardados y el día local.
    private string _timezone = string.Empty;

    /// <summary>Acciones rápidas: las cablea MainWindowViewModel a la navegación.</summary>
    public event Action? CreateMemberRequested;
    public event Action? CheckInRequested;
    public event Action? ChargeRequested;

    // ── KPIs ────────────────────────────────────────────────────────────────
    [ObservableProperty] private int _totalActiveMembers;
    [ObservableProperty] private string _membersSubtitle = string.Empty;
    [ObservableProperty] private int _checkInsToday;
    [ObservableProperty] private string _checkInsSubtitle = string.Empty;
    [ObservableProperty] private int _overdueMembers;
    [ObservableProperty] private string _overdueSubtitle = string.Empty;
    [ObservableProperty] private decimal _revenueThisMonth;
    [ObservableProperty] private string _revenueTrendText = string.Empty;
    [ObservableProperty] private bool _revenueTrendUp;
    [ObservableProperty] private bool _hasRevenueTrend;

    // ── Gráfico de recaudación (últimos 30 días) ────────────────────────────
    // Sistema virtual 300×130; el Viewbox lo estira al ancho de la card.
    private const double ChartWidth = 300;
    [ObservableProperty] private ObservableCollection<RevenueBarVm> _revenueBars = [];
    [ObservableProperty] private string _revenue30DaysTotal = string.Empty;
    [ObservableProperty] private string _chartFromLabel = string.Empty;
    [ObservableProperty] private string _chartToLabel = string.Empty;
    // Área rellena + línea del gráfico de recaudación (área/línea premium).
    [ObservableProperty] private Geometry? _revenueAreaGeometry;
    [ObservableProperty] private Geometry? _revenueLineGeometry;
    // Eje Y: etiquetas (fuera del Viewbox, en un gutter fijo) y líneas de guía horizontales del plot.
    [ObservableProperty] private ObservableCollection<AxisTickVm> _axisTicks = [];
    [ObservableProperty] private Geometry? _revenueGuideGeometry;

    // ── Listas accionables ──────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<ExpiringRowVm> _expiringRows = [];
    [ObservableProperty] private ObservableCollection<AccessRowVm> _accessRows = [];

    [ObservableProperty] private string _todayLabel = string.Empty;
    [ObservableProperty] private bool _isLoading;

    public bool HasNoExpiring => ExpiringRows.Count == 0 && !IsLoading;
    public bool HasNoAccess => AccessRows.Count == 0 && !IsLoading;

    partial void OnIsLoadingChanged(bool value) => NotifyEmptyStates();

    private void NotifyEmptyStates()
    {
        OnPropertyChanged(nameof(HasNoExpiring));
        OnPropertyChanged(nameof(HasNoAccess));
    }

    public DashboardViewModel(
        IServiceScopeFactory scopeFactory, SessionContext session)
    {
        _scopeFactory = scopeFactory;
        _session = session;
    }

    [RelayCommand] private void QuickCreateMember() => CreateMemberRequested?.Invoke();
    [RelayCommand] private void QuickCheckIn() => CheckInRequested?.Invoke();
    [RelayCommand] private void QuickCharge() => ChargeRequested?.Invoke();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repos = DashboardSources.From(scope.ServiceProvider);
            var company = await repos.Sites.GetCompanyAsync(_session.CompanyId)
                ?? throw new InvalidOperationException("La empresa no existe.");
            _timezone = company.Timezone;
            var today = scope.ServiceProvider.GetRequiredService<ICompanyCalendar>().TodayFor(company);
            TodayLabel = Capitalize(today.ToString("dddd d 'de' MMMM"));

            // ── Socios ──────────────────────────────────────────────────────
            TotalActiveMembers = await repos.Members.CountAsync(
                _session.CompanyId, _session.SiteId, MemberStatus.Active);
            var totalMembers = await repos.Members.CountAsync(_session.CompanyId, _session.SiteId);
            MembersSubtitle = $"de {totalMembers} registrados";

            // ── Check-ins de hoy vs. ayer (guardados en UTC) ────────────────
            var dayStartUtc = StartOfDayUtc(today);
            var todayLogs = await repos.AccessLogs.GetBySiteAsync(
                _session.SiteId, dayStartUtc, StartOfDayUtc(today.AddDays(1)));
            CheckInsToday = todayLogs.Count(l => l.AccessGranted);

            var yesterdayLogs = await repos.AccessLogs.GetBySiteAsync(
                _session.SiteId, StartOfDayUtc(today.AddDays(-1)), dayStartUtc);
            CheckInsSubtitle = $"ayer: {yesterdayLogs.Count(l => l.AccessGranted)}";

            // ── Mora real: cobros vencidos impagos ──────────────────────────
            var overdueCharges = await repos.Charges.GetOverdueAsync(
                _session.CompanyId, today);
            OverdueMembers = overdueCharges.Select(c => c.MemberId).Distinct().Count();
            var owed = overdueCharges.Sum(c => c.AmountOutstanding);
            OverdueSubtitle = owed > 0 ? $"deben ${owed:N0} en total" : "sin deuda vencida";

            // ── Recaudación del mes + tendencia vs. mismo período anterior ──
            var monthStartUtc = StartOfDayUtc(new DateOnly(today.Year, today.Month, 1));
            RevenueThisMonth = await repos.Payments.SumReceivedAsync(
                _session.CompanyId, _session.SiteId, monthStartUtc, DateTime.UtcNow);

            var prevStartUtc = StartOfDayUtc(new DateOnly(today.Year, today.Month, 1).AddMonths(-1));
            var prevSamePeriod = await repos.Payments.SumReceivedAsync(
                _session.CompanyId, _session.SiteId, prevStartUtc,
                prevStartUtc.Add(DateTime.UtcNow - monthStartUtc));

            HasRevenueTrend = prevSamePeriod > 0;
            if (HasRevenueTrend)
            {
                var pct = (RevenueThisMonth - prevSamePeriod) / prevSamePeriod * 100m;
                RevenueTrendUp = pct >= 0;
                RevenueTrendText = $"{(pct >= 0 ? "▲" : "▼")} {Math.Abs(pct):N0}% vs. mes pasado";
            }

            await LoadRevenueChartAsync(repos, today);
            await LoadExpiringAsync(repos, today);
            LoadAccessRows(todayLogs);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private DateTime StartOfDayUtc(DateOnly day) => CompanyTime.StartOfDayUtc(day, _timezone);

    private async Task LoadRevenueChartAsync(DashboardSources repos, DateOnly today)
    {
        var fromLocal = today.AddDays(-29);
        var payments = await repos.Payments.GetByPeriodAsync(
            _session.CompanyId, _session.SiteId,
            StartOfDayUtc(fromLocal), DateTime.UtcNow);

        var byDay = payments
            .GroupBy(p => DateOnly.FromDateTime(CompanyTime.ToLocal(p.ReceivedAt, _timezone)))
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        var days = Enumerable.Range(0, 30).Select(i => fromLocal.AddDays(i)).ToList();
        var axis = ChartAxis.Build(byDay.Count == 0 ? 0m : byDay.Values.Max());
        var scaleTop = Math.Max(axis.Top, 1m);

        var bars = days.Select(d =>
        {
            var amount = byDay.GetValueOrDefault(d);
            var height = amount == 0 ? 3 : Math.Max(6, (double)(amount / scaleTop) * ChartHeight);
            return new RevenueBarVm(height, $"{d:ddd d/MM} — ${amount:N0}", d == today);
        }).ToList();

        RevenueBars = new ObservableCollection<RevenueBarVm>(bars);
        AxisTicks = new ObservableCollection<AxisTickVm>(
            axis.Ticks.Select(tick => new AxisTickVm(tick.Label, (1 - tick.Fraction) * ChartHeight)));
        BuildGuideGeometry(AxisTicks);
        BuildRevenueGeometry(bars.Select(b => b.BarHeight).ToList());

        Revenue30DaysTotal = $"${byDay.Values.Sum():N0}";
        ChartFromLabel = fromLocal.ToString("d MMM");
        ChartToLabel = today.ToString("d MMM");
    }

    /// <summary>Líneas de guía horizontales a la altura de cada referencia del eje (sistema virtual 300×130).</summary>
    private void BuildGuideGeometry(IEnumerable<AxisTickVm> ticks)
    {
        var path = string.Join(" ", ticks.Select(t =>
            string.Create(CultureInfo.InvariantCulture, $"M 0,{t.Y:0.##} L {ChartWidth:0.##},{t.Y:0.##}")));
        RevenueGuideGeometry = TryParseGeometry(path);
    }

    /// <summary>Arma el área rellena y la línea del gráfico en el sistema virtual
    /// 300×130 (invariante para el markup de Path; el Viewbox lo estira).</summary>
    private void BuildRevenueGeometry(IReadOnlyList<double> heights)
    {
        var n = heights.Count;
        if (n < 2) { RevenueAreaGeometry = RevenueLineGeometry = null; return; }

        static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        double X(int i) => i * (ChartWidth / (n - 1));
        double Y(int i) => ChartHeight - heights[i];

        var line = new StringBuilder($"M {N(X(0))},{N(Y(0))}");
        for (var i = 1; i < n; i++)
            line.Append($" L {N(X(i))},{N(Y(i))}");

        // Área: la línea + cierre hacia la base.
        var area = new StringBuilder($"M {N(X(0))},{N(ChartHeight)} L {N(X(0))},{N(Y(0))}");
        for (var i = 1; i < n; i++)
            area.Append($" L {N(X(i))},{N(Y(i))}");
        area.Append($" L {N(ChartWidth)},{N(ChartHeight)} Z");

        var lineGeometry = TryParseGeometry(line.ToString());
        var areaGeometry = TryParseGeometry(area.ToString());
        var drawable = lineGeometry is not null && areaGeometry is not null;
        RevenueLineGeometry = drawable ? lineGeometry : null;
        RevenueAreaGeometry = drawable ? areaGeometry : null;
    }

    /// <summary>Geometry.Parse necesita la plataforma de render inicializada; si aún no lo está (p. ej. pre-render
    /// fuera del hilo de UI) devuelve null y se reintenta al recargar.</summary>
    private static Geometry? TryParseGeometry(string path)
    {
        try
        {
            return Geometry.Parse(path);
        }
        catch
        {
            return null;
        }
    }

    private async Task LoadExpiringAsync(DashboardSources repos, DateOnly from)
    {
        var expiring = await repos.Memberships.GetExpiringAsync(_session.CompanyId, from, from.AddDays(7));

        ExpiringRows = new ObservableCollection<ExpiringRowVm>(expiring.Take(6).Select(m =>
        {
            var days = m.EndDate!.Value.DayNumber - from.DayNumber;
            var when = days switch
            {
                0 => "Vence hoy",
                1 => "Vence mañana",
                _ => $"En {days} días",
            };
            return new ExpiringRowVm(
                Initials(m.Member.FirstName, m.Member.LastName),
                $"{m.Member.FirstName} {m.Member.LastName}",
                m.MembershipType?.Name ?? "Membresía",
                when,
                IsUrgent: days <= 2);
        }));
        NotifyEmptyStates();
    }

    private void LoadAccessRows(IReadOnlyList<Domain.Entities.AccessLog> todayLogs)
    {
        AccessRows = new ObservableCollection<AccessRowVm>(todayLogs.Take(6).Select(l => new AccessRowVm(
            Initials(l.Member?.FirstName, l.Member?.LastName),
            l.Member is null ? "Desconocido" : $"{l.Member.FirstName} {l.Member.LastName}",
            CompanyTime.ToLocal(l.SwipedAt, _timezone).ToString("HH:mm"),
            l.AccessGranted,
            l.AccessGranted ? "Ingresó" : "Rechazado")));
        NotifyEmptyStates();
    }

    private static string Initials(string? first, string? last) =>
        $"{first?.FirstOrDefault()}{last?.FirstOrDefault()}".ToUpperInvariant() is { Length: > 0 } s
            ? s : "?";

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];
}
