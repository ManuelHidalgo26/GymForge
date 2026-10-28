using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.DTOs;
using GymForge.Application.UseCases.Memberships;
using MediatR;

namespace GymForge.Desktop.ViewModels.Members;

/// <summary>
/// ViewModel del modal "Congelar membresía". El congelamiento arranca hoy (día local de la empresa) y el
/// cajero elige la fecha en que la membresía se reactiva. Muestra el cupo anual y avisa antes de pasarse del
/// tope; si igual el dominio lo rechaza (pantalla desactualizada) muestra su mensaje en vez de romper.
/// </summary>
public partial class FreezeModalViewModel : ObservableObject
{
    public const string DefaultReason = "Congelamiento manual";

    private const int DefaultFreezeDays = 30;

    private readonly IMediator _mediator;
    private readonly MembershipDto _membership;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequestedDays), nameof(NewEndDate), nameof(ValidationMessage))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private DateOnly? _reactivationDate;

    [ObservableProperty] private string _reason = string.Empty;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _isSaving;
    [ObservableProperty] private string? _errorMessage;

    public FreezeModalViewModel(IMediator mediator, MembershipDto membership, FreezeQuotaDto quota)
    {
        _mediator = mediator;
        _membership = membership;
        Quota = quota;
        ReactivationDate = ProposedReactivationDate();
    }

    public event Action? MembershipFrozen;
    public event Action? Cancelled;

    public FreezeQuotaDto Quota { get; }

    public string MembershipTypeName => _membership.MembershipTypeName;

    public DateOnly? CurrentEndDate => _membership.EndDate;

    /// <summary>El congelamiento arranca hoy, según el día local de la empresa.</summary>
    public DateOnly StartDate => Quota.Today;

    public string QuotaSummary =>
        $"Usaste {Quota.UsedDays} de {Quota.MaxDays} días de congelamiento en {Quota.Year}; quedan {Quota.RemainingDays}.";

    public int RequestedDays => ReactivationDate is { } end ? end.DayNumber - StartDate.DayNumber : 0;

    /// <summary>Nuevo vencimiento: el actual corrido por los días congelados.</summary>
    public DateOnly? NewEndDate => CurrentEndDate is { } end && RequestedDays > 0 ? end.AddDays(RequestedDays) : null;

    /// <summary>Por qué no se puede confirmar todavía; null si el pedido es válido.</summary>
    public string? ValidationMessage
    {
        get
        {
            if (Quota.RemainingDays == 0)
                return $"No quedan días de congelamiento en {Quota.Year} (tope: {Quota.MaxDays} por año).";
            if (ReactivationDate is null)
                return "Elegí la fecha en que se reactiva la membresía.";
            if (RequestedDays <= 0)
                return "La fecha de reactivación tiene que ser posterior a hoy.";
            if (RequestedDays > Quota.RemainingDays)
                return $"Supera el tope de {Quota.MaxDays} días de congelamiento por año: " +
                       $"te quedan {Quota.RemainingDays} y pediste {RequestedDays}.";
            return null;
        }
    }

    private bool CanConfirm => ValidationMessage is null && !IsSaving;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync(CancellationToken ct = default)
    {
        if (ReactivationDate is not { } reactivation) return;

        IsSaving = true;
        ErrorMessage = null;
        try
        {
            await _mediator.Send(
                new FreezeMembershipCommand(_membership.Id, StartDate, reactivation, ReasonOrDefault()), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = ex.Message;
            return;
        }
        finally
        {
            IsSaving = false;
        }

        MembershipFrozen?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();

    /// <summary>Un mes por defecto, o lo que quede del cupo si es menos. Sin cupo no se propone nada.</summary>
    private DateOnly? ProposedReactivationDate() =>
        Quota.RemainingDays > 0 ? StartDate.AddDays(Math.Min(DefaultFreezeDays, Quota.RemainingDays)) : null;

    private string ReasonOrDefault() => string.IsNullOrWhiteSpace(Reason) ? DefaultReason : Reason.Trim();
}
