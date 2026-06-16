using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.UseCases.Reports;
using GymForge.Desktop.Services;
using MediatR;

namespace GymForge.Desktop.ViewModels.Reports;

/// <summary>Reporte de recaudación: rango de fechas + pagos del período.</summary>
public partial class ReportsViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly SessionContext _session;
    private readonly ReceiptService _receipts;

    [ObservableProperty] private DateOnly? _from;
    [ObservableProperty] private DateOnly? _to;
    [ObservableProperty] private decimal _total;
    [ObservableProperty] private int _count;
    [ObservableProperty] private ObservableCollection<ReportPaymentRow> _rows = [];
    [ObservableProperty] private bool _isLoading;

    public bool IsEmpty => !IsLoading && Rows.Count == 0;

    public ReportsViewModel(IMediator mediator, SessionContext session, ReceiptService receipts)
    {
        _mediator = mediator;
        _session = session;
        _receipts = receipts;

        var today = DateOnly.FromDateTime(DateTime.Today);
        _from = new DateOnly(today.Year, today.Month, 1);
        _to = today;
    }

    partial void OnRowsChanged(ObservableCollection<ReportPaymentRow> value) => OnPropertyChanged(nameof(IsEmpty));
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));
    partial void OnFromChanged(DateOnly? value) => _ = LoadAsync();
    partial void OnToChanged(DateOnly? value) => _ = LoadAsync();

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (From is null || To is null || _session.SiteId == Guid.Empty) return;

        IsLoading = true;
        try
        {
            var report = await _mediator.Send(
                new GetPaymentsReportQuery(_session.CompanyId, _session.SiteId, From.Value, To.Value), ct);
            Total = report.Total;
            Count = report.Count;
            Rows = new ObservableCollection<ReportPaymentRow>(report.Rows);
        }
        finally { IsLoading = false; }
    }

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
