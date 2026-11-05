using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Catalog;
using GymForge.Application.UseCases.Members;
using GymForge.Application.UseCases.Sales;
using GymForge.Desktop.Services;
using GymForge.Domain.Enums;
using MediatR;
using System.Collections.ObjectModel;

namespace GymForge.Desktop.ViewModels.Members;

public partial class CreateMemberViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly SessionContext _session;
    private readonly ReceiptService _receipts;
    private readonly IMemberPhotoStore _photoStore;

    // Si el alta ya pasó pero el cobro falló, el reintento de Guardar
    // solo repite el cobro (evita duplicar al socio).
    private MemberDto? _createdMember;

    // Form fields
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _firstName = string.Empty;

    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _lastName = string.Empty;

    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _documentNumber = string.Empty;

    [ObservableProperty] private DocumentType _documentType = DocumentType.DNI;
    [ObservableProperty] private Gender _gender = Gender.PreferNotToSay;
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string? _mobile;
    [ObservableProperty] private DateOnly? _birthDate;
    [ObservableProperty] private MemberSource _source = MemberSource.WalkIn;
    [ObservableProperty] private bool _marketingConsent;
    [ObservableProperty] private bool _activateImmediately = true;

    // Credencial de acceso (carnet o llavero): opcional, se carga a mano o con el lector de códigos.
    [ObservableProperty] private string? _tagSerial;

    /// <summary>El code-behind enfoca el campo de credencial cuando se dispara (botón "Escanear").</summary>
    public event Action? CredentialFocusRequested;

    // Cobro inicial (alta + primera cuota en un solo paso)
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _chargeNow;
    [ObservableProperty] private ObservableCollection<MembershipTypeDto> _plans = [];
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private MembershipTypeDto? _selectedPlan;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private PaymentMethod _payMethod = PaymentMethod.Cash;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string? _payCardLast4;

    public IReadOnlyList<PaymentMethod> PayMethods { get; } =
        Enum.GetValues<PaymentMethod>().ToList();

    public bool IsCardRequired =>
        PayMethod is PaymentMethod.CreditCard or PaymentMethod.DebitCard;

    partial void OnPayMethodChanged(PaymentMethod value) =>
        OnPropertyChanged(nameof(IsCardRequired));

    partial void OnChargeNowChanged(bool value)
    {
        if (value && Plans.Count == 0)
            _ = LoadPlansCommand.ExecuteAsync(null);
    }

    // State
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _hasFingerprint;

    /// <summary>La foto: se elige acá y se guarda cuando el socio ya existe (necesita su Id).</summary>
    public PhotoPickerViewModel Photo { get; }

    // Enums for UI binding
    public IReadOnlyList<DocumentType> DocumentTypes { get; } =
        Enum.GetValues<DocumentType>().ToList();
    public IReadOnlyList<Gender> Genders { get; } =
        Enum.GetValues<Gender>().ToList();
    public IReadOnlyList<MemberSource> Sources { get; } =
        Enum.GetValues<MemberSource>().ToList();

    public event Action<MemberDto>? MemberCreated;
    public event Action? Cancelled;

    public CreateMemberViewModel(
        IMediator mediator, SessionContext session, ReceiptService receipts,
        IImagePicker imagePicker, IMemberPhotoStore photoStore)
    {
        _mediator = mediator;
        _session = session;
        _receipts = receipts;
        _photoStore = photoStore;
        Photo = new PhotoPickerViewModel(imagePicker);
    }

    private bool CanSave => !IsSaving
        && !string.IsNullOrWhiteSpace(FirstName)
        && !string.IsNullOrWhiteSpace(LastName)
        && !string.IsNullOrWhiteSpace(DocumentNumber)
        && (!ChargeNow || (SelectedPlan is not null
            && (!IsCardRequired || PayCardLast4?.Length == 4)));

    [RelayCommand]
    private async Task LoadPlansAsync(CancellationToken ct = default)
    {
        // Solo planes con precio: uno gratis no requiere cobro inicial.
        var plans = await _mediator.Send(new GetMembershipTypesQuery(_session.CompanyId), ct);
        Plans = new ObservableCollection<MembershipTypeDto>(plans.Where(p => p.Price > 0));
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        IsSaving = true;
        ErrorMessage = null;
        try
        {
            _createdMember ??= await _mediator.Send(new CreateMemberCommand(
                _session.CompanyId, _session.SiteId,
                FirstName.Trim(), LastName.Trim(),
                DocumentType, DocumentNumber.Trim(),
                Gender, Email?.Trim(), Mobile?.Trim(),
                BirthDate, Source,
                MarketingConsent: MarketingConsent,
                ActivateImmediately: ActivateImmediately,
                TagSerial: TagSerial), ct);

            if (!await TrySavePhotoAsync(_createdMember.Id, ct)) return;

            if (ChargeNow && SelectedPlan is not null)
            {
                try
                {
                    var payment = await _mediator.Send(new SellMembershipCommand(
                        _session.CompanyId, _session.SiteId,
                        _session.EffectiveCashierId, _session.OpenShiftId,
                        _createdMember.Id, SelectedPlan.Id, PayMethod,
                        IsCardRequired ? PayCardLast4 : null), ct);

                    await _receipts.TryGenerateAndOpenAsync(payment.Id, _session.CompanyId, ct);
                }
                catch (Exception ex)
                {
                    // El socio ya quedó guardado: informar y permitir reintentar solo el cobro.
                    var reason = ex is FluentValidation.ValidationException vex
                        ? string.Join(" ", vex.Errors.Select(e => e.ErrorMessage))
                        : ex.Message;
                    ErrorMessage = $"El socio se guardó, pero el cobro falló: {reason} " +
                                   "Reintentá con Guardar, o cobrale después desde Caja.";
                    return;
                }
            }

            MemberCreated?.Invoke(_createdMember);
        }
        catch (DuplicateMemberDocumentException dup)
        {
            ErrorMessage = dup.Message;
        }
        catch (FluentValidation.ValidationException vex)
        {
            ErrorMessage = string.Join("\n", vex.Errors.Select(e => e.ErrorMessage));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error al guardar: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();

    /// <summary>
    /// Guarda la foto elegida, ahora que el socio ya existe. Si falla, el socio queda creado y se avisa para
    /// reintentar con Guardar, quitar la foto o cargarla después desde la ficha: no se pierde en silencio.
    /// </summary>
    private async Task<bool> TrySavePhotoAsync(Guid memberId, CancellationToken ct)
    {
        if (Photo.PendingImage is not { } image) return true;

        try
        {
            var path = _photoStore.Save(memberId, image);
            await _mediator.Send(new UpdateMemberPhotoCommand(_session.CompanyId, memberId, path), ct);
            Photo.MarkSaved();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"El socio se guardó, pero la foto no: {ex.Message} " +
                           "Reintentá con Guardar, quitá la foto o cargala después desde su ficha.";
            return false;
        }
    }

    [RelayCommand]
    private void ScanCredential() => CredentialFocusRequested?.Invoke();

    [RelayCommand]
    private void EnrollFingerprint()
    {
        // Sprint 1: placeholder — Sprint 2 calls BioBroker /enroll
        HasFingerprint = false;
    }
}
