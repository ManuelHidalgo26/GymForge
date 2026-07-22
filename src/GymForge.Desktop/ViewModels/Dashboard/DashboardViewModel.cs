using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.Interfaces;
using GymForge.Desktop.Services;
using GymForge.Domain.Enums;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace GymForge.Desktop.ViewModels.Dashboard;

/// <summary>Barra del gráfico de recaudación (un día). Altura ya escalada en px.</summary>
public record RevenueBarVm(double BarHeight, string Tooltip, bool IsToday);

/// <summary>Membresía que vence en los próximos días.</summary>
public record ExpiringRowVm(string Initials, string MemberName, string PlanName, string WhenLabel, bool IsUrgent);

/// <summary>Acceso reciente del día (permitido o rechazado).</summary>
public record AccessRowVm(string Initials, string MemberName, string TimeLabel, bool Granted, string StatusLabel);

public partial class DashboardViewModel : ObservableObject
{
    private const double ChartHeight = 130;

    private readonly IMemberRepository _members;
    private readonly IPaymentRepository _payments;
    private readonly IAccessLogRepository _accessLogs;
    private readonly IChargeRepository _charges;
    private readonly IMembershipRepository _memberships;
    private readonly SessionContext _session;

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
        IMemberRepository members, IPaymentRepository payments,
        IAccessLogRepository accessLogs, IChargeRepository charges,
        IMembershipRepository memberships, SessionContext session)
    {
        _members = members;
        _payments = payments;
        _accessLogs = accessLogs;
        _charges = charges;
        _memberships = memberships;
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
            var today = DateTime.Today;
            TodayLabel = Capitalize(today.ToString("dddd d 'de' MMMM"));

            // ── Socios ──────────────────────────────────────────────────────
            TotalActiveMembers = await _members.CountAsync(
                _session.CompanyId, _session.SiteId, MemberStatus.Active);
            var totalMembers = await _members.CountAsync(_session.CompanyId, _session.SiteId);
            MembersSubtitle = $"de {totalMembers} registrados";

            // ── Check-ins de hoy vs. ayer (guardados en UTC) ────────────────
            var dayStartUtc = today.ToUniversalTime();
            var todayLogs = await _accessLogs.GetBySiteAsync(
                _session.SiteId, dayStartUtc, dayStartUtc.AddDays(1));
            CheckInsToday = todayLogs.Count(l => l.AccessGranted);

            var yesterdayLogs = await _accessLogs.GetBySiteAsync(
                _session.SiteId, dayStartUtc.AddDays(-1), dayStartUtc);
            CheckInsSubtitle = $"ayer: {yesterdayLogs.Count(l => l.AccessGranted)}";

            // ── Mora real: cobros vencidos impagos ──────────────────────────
            var overdueCharges = await _charges.GetOverdueAsync(
                _session.CompanyId, DateOnly.FromDateTime(today));
            OverdueMembers = overdueCharges.Select(c => c.MemberId).Distinct().Count();
            var owed = overdueCharges.Sum(c => c.AmountOutstanding);
            OverdueSubtitle = owed > 0 ? $"deben ${owed:N0} en total" : "sin deuda vencida";

            // ── Recaudación del mes + tendencia vs. mismo período anterior ──
            var monthStartUtc = new DateTime(today.Year, today.Month, 1).ToUniversalTime();
            RevenueThisMonth = await _payments.SumReceivedAsync(
                _session.CompanyId, _session.SiteId, monthStartUtc, DateTime.UtcNow);

            var prevStartUtc = new DateTime(today.Year, today.Month, 1).AddMonths(-1).ToUniversalTime();
            var prevSamePeriod = await _payments.SumReceivedAsync(
                _session.CompanyId, _session.SiteId, prevStartUtc,
                prevStartUtc.Add(DateTime.UtcNow - monthStartUtc));

            HasRevenueTrend = prevSamePeriod > 0;
            if (HasRevenueTrend)
            {
                var pct = (RevenueThisMonth - prevSamePeriod) / prevSamePeriod * 100m;
                RevenueTrendUp = pct >= 0;
                RevenueTrendText = $"{(pct >= 0 ? "▲" : "▼")} {Math.Abs(pct):N0}% vs. mes pasado";
            }

            await LoadRevenueChartAsync(today);
            await LoadExpiringAsync(today);
            LoadAccessRows(todayLogs);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadRevenueChartAsync(DateTime today)
    {
        var fromLocal = today.AddDays(-29);
        var payments = await _payments.GetByPeriodAsync(
            _session.CompanyId, _session.SiteId,
            fromLocal.ToUniversalTime(), DateTime.UtcNow);

        var byDay = payments
            .GroupBy(p => p.ReceivedAt.ToLocalTime().Date)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        var days = Enumerable.Range(0, 30).Select(i => fromLocal.AddDays(i)).ToList();
        var max = Math.Max(byDay.Count == 0 ? 0m : byDay.Values.Max(), 1m);

        var bars = days.Select(d =>
        {
            var amount = byDay.GetValueOrDefault(d.Date);
            var height = amount == 0 ? 3 : Math.Max(6, (double)(amount / max) * ChartHeight);
            return new RevenueBarVm(height, $"{d:ddd d/MM} — ${amount:N0}", d.Date == today);
        }).ToList();

        RevenueBars = new ObservableCollection<RevenueBarVm>(bars);
        BuildRevenueGeometry(bars.Select(b => b.BarHeight).ToList());

        Revenue30DaysTotal = $"${byDay.Values.Sum():N0}";
        ChartFromLabel = fromLocal.ToString("d MMM");
        ChartToLabel = today.ToString("d MMM");
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

        try
        {
            RevenueLineGeometry = Geometry.Parse(line.ToString());
            RevenueAreaGeometry = Geometry.Parse(area.ToString());
        }
        catch
        {
            // Geometry.Parse necesita la plataforma de render inicializada; si aún no
            // lo está (p. ej. pre-render fuera del hilo de UI), se reintenta al recargar.
            RevenueAreaGeometry = RevenueLineGeometry = null;
        }
    }

    private async Task LoadExpiringAsync(DateTime today)
    {
        var from = DateOnly.FromDateTime(today);
        var expiring = await _memberships.GetExpiringAsync(_session.CompanyId, from, from.AddDays(7));

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
            l.SwipedAt.ToLocalTime().ToString("HH:mm"),
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
