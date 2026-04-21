using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.DTOs;
using GymForge.Application.UseCases.Members;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Desktop.ViewModels.Members;

public partial class CreateMemberViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private Guid _companyId;
    private Guid _siteId;

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

    // State
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _hasFingerprint;
    [ObservableProperty] private string? _photoUrl;

    // Enums for UI binding
    public IReadOnlyList<DocumentType> DocumentTypes { get; } =
        Enum.GetValues<DocumentType>().ToList();
    public IReadOnlyList<Gender> Genders { get; } =
        Enum.GetValues<Gender>().ToList();
    public IReadOnlyList<MemberSource> Sources { get; } =
        Enum.GetValues<MemberSource>().ToList();

    public event Action<MemberDto>? MemberCreated;
    public event Action? Cancelled;

    public CreateMemberViewModel(IMediator mediator) => _mediator = mediator;

    public void Initialize(Guid companyId, Guid siteId)
    {
        _companyId = companyId;
        _siteId = siteId;
    }

    private bool CanSave => !IsSaving
        && !string.IsNullOrWhiteSpace(FirstName)
        && !string.IsNullOrWhiteSpace(LastName)
        && !string.IsNullOrWhiteSpace(DocumentNumber);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        IsSaving = true;
        ErrorMessage = null;
        try
        {
            var dto = await _mediator.Send(new CreateMemberCommand(
                _companyId, _siteId,
                FirstName.Trim(), LastName.Trim(),
                DocumentType, DocumentNumber.Trim(),
                Gender, Email?.Trim(), Mobile?.Trim(),
                BirthDate, Source,
                MarketingConsent: MarketingConsent), ct);

            MemberCreated?.Invoke(dto);
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

    [RelayCommand]
    private void CapturePhoto()
    {
        // Sprint 1: placeholder — Sprint 2 wires webcam via AForge.NET or MediaCapture
        PhotoUrl = null;
    }

    [RelayCommand]
    private void EnrollFingerprint()
    {
        // Sprint 1: placeholder — Sprint 2 calls BioBroker /enroll
        HasFingerprint = false;
    }
}
