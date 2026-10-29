using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Dunning;
using GymForge.Desktop.Services;
using GymForge.Desktop.ViewModels.Charges;
using MediatR;

namespace GymForge.Desktop.ViewModels.Collections;

/// <summary>
/// Pantalla de morosos de la sede activa: quién debe, cuánto y desde cuándo, con el recordatorio por
/// WhatsApp y el cobro (reusa el modal de pago, que necesita turno de caja abierto) a un click.
/// </summary>
public partial class CollectionsViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly ICompanyCalendar _calendar;
    private readonly IWhatsAppLinkSender _whatsApp;
    private readonly ReceiptService _receipts;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(DebtorCount), nameof(IsEmpty))]
    private ObservableCollection<OverdueMemberRow> _rows = [];

    [ObservableProperty] private decimal _totalOutstanding;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _isPaymentModalOpen;
    [ObservableProperty] private PaymentModalViewModel? _paymentModal;

    public CollectionsViewModel(
        IMediator mediator, ICompanyCalendar calendar, IWhatsAppLinkSender whatsApp,
        SessionContext session, ReceiptService receipts)
    {
        _mediator = mediator;
        _calendar = calendar;
        _whatsApp = whatsApp;
        _receipts = receipts;
        Session = session;
    }

    /// <summary>Expuesta para que la vista muestre si hay turno de caja abierto (hace falta para cobrar).</summary>
    public SessionContext Session { get; }

    public int DebtorCount => Rows.Count;

    /// <summary>Estado vacío: sin morosos y sin carga en curso.</summary>
    public bool IsEmpty => !IsLoading && Rows.Count == 0;

    // ── Carga ────────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (Session.SiteId == Guid.Empty) return;

        IsLoading = true;
        Message = null;
        try
        {
            var today = await _calendar.TodayAsync(Session.CompanyId, ct);
            var debtors = await _mediator.Send(
                new GetOverdueMembersQuery(Session.CompanyId, Session.SiteId, today), ct);

            Rows = new ObservableCollection<OverdueMemberRow>(debtors.Select(d => new OverdueMemberRow(d, today)));
            TotalOutstanding = debtors.Sum(d => d.TotalOutstanding);
        }
        catch (Exception ex)
        {
            Message = $"No se pudo cargar la lista de morosos: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Recordatorio por WhatsApp ────────────────────────────────────────────

    private static bool CanRemind(OverdueMemberRow? row) => row?.HasUsableMobile == true;

    /// <summary>Arma el mensaje de la etapa que le toca al socio, deja traza y abre WhatsApp con el link.</summary>
    [RelayCommand(CanExecute = nameof(CanRemind))]
    private async Task RemindWhatsAppAsync(OverdueMemberRow? row, CancellationToken ct = default)
    {
        if (row is null) return;

        Message = null;
        try
        {
            var result = await _mediator.Send(
                new SendWhatsAppReminderCommand(Session.CompanyId, Session.SiteId, row.MemberId), ct);

            if (result.WhatsAppUrl is null)
            {
                Message = result.Reason;
                return;
            }

            var opened = _whatsApp.Open(result.WhatsAppUrl);
            await LoadAsync(ct);   // quedó registrado el aviso: refresca el "último recordatorio"
            if (!opened) Message ??= "No se pudo abrir WhatsApp en esta PC.";
        }
        catch (Exception ex)
        {
            Message = $"No se pudo preparar el recordatorio: {ex.Message}";
        }
    }

    // ── Cobro ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Charge(OverdueMemberRow? row)
    {
        if (row is null) return;

        if (!Session.HasOpenShift)
        {
            Message = "Abrí la caja para cobrar.";
            return;
        }

        Message = null;
        var modal = new PaymentModalViewModel(_mediator, Session, _receipts);
        modal.PreSelectMember(row.MemberId, row.FullName, row.Dto.TotalOutstanding);
        modal.PaymentRegistered += async _ =>
        {
            CloseModal();
            await LoadAsync();
        };
        modal.Cancelled += CloseModal;

        PaymentModal = modal;
        IsPaymentModalOpen = true;
    }

    private void CloseModal()
    {
        IsPaymentModalOpen = false;
        PaymentModal = null;
    }
}
