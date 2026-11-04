using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.DTOs;
using GymForge.Application.UseCases.Members;
using GymForge.Desktop.Services;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Desktop.ViewModels.Members;

/// <summary>
/// Modo edición de la pestaña Datos de la ficha. Trabaja sobre copias de los campos del socio: cancelar
/// descarta sin tocar el <see cref="MemberDto"/> de la ficha, y guardar manda <see cref="UpdateMemberCommand"/>.
/// </summary>
public partial class MemberEditViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly SessionContext _session;
    private Guid _memberId;

    [ObservableProperty] private bool _isEditing;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isSaving;

    [ObservableProperty] private string? _errorMessage;

    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _firstName = string.Empty;

    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _lastName = string.Empty;

    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _documentNumber = string.Empty;

    [ObservableProperty] private DocumentType _documentType;
    [ObservableProperty] private Gender _gender;
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string? _mobile;
    [ObservableProperty] private DateOnly? _birthDate;
    [ObservableProperty] private bool _marketingConsent;

    // Contacto de emergencia y datos médicos (datos de salud: el AuditLog los registra sin valor).
    [ObservableProperty] private string? _emergencyName;
    [ObservableProperty] private string? _emergencyPhone;
    [ObservableProperty] private string? _emergencyRelation;
    [ObservableProperty] private string? _medicalConditions;
    [ObservableProperty] private string? _medications;
    [ObservableProperty] private string? _allergies;
    [ObservableProperty] private BloodType _bloodType;

    public IReadOnlyList<DocumentType> DocumentTypes { get; } = Enum.GetValues<DocumentType>().ToList();
    public IReadOnlyList<Gender> Genders { get; } = Enum.GetValues<Gender>().ToList();
    public IReadOnlyList<BloodType> BloodTypes { get; } = Enum.GetValues<BloodType>().ToList();

    /// <summary>El socio ya guardado, para que la ficha refresque lo que muestra.</summary>
    public event Action<MemberDto>? Saved;

    public MemberEditViewModel(IMediator mediator, SessionContext session)
    {
        _mediator = mediator;
        _session = session;
    }

    /// <summary>Entra en modo edición con los valores actuales del socio.</summary>
    public void Begin(MemberDto member)
    {
        _memberId = member.Id;
        FirstName = member.FirstName;
        LastName = member.LastName;
        DocumentType = member.DocumentType;
        DocumentNumber = member.DocumentNumber;
        Gender = member.Gender;
        Email = member.Email;
        Mobile = member.Mobile;
        BirthDate = member.BirthDate;
        MarketingConsent = member.MarketingConsent;
        EmergencyName = member.EmergencyName;
        EmergencyPhone = member.EmergencyPhone;
        EmergencyRelation = member.EmergencyRelation;
        MedicalConditions = member.MedicalConditions;
        Medications = member.Medications;
        Allergies = member.Allergies;
        BloodType = member.BloodType;
        ErrorMessage = null;
        IsEditing = true;
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
            var saved = await _mediator.Send(new UpdateMemberCommand(
                _session.CompanyId, _memberId,
                FirstName.Trim(), LastName.Trim(), DocumentType, DocumentNumber.Trim(), Gender,
                Email, Mobile, BirthDate, MarketingConsent,
                EmergencyName, EmergencyPhone, EmergencyRelation,
                MedicalConditions, Medications, Allergies, BloodType), ct);

            IsEditing = false;
            Saved?.Invoke(saved);
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
    private void Cancel()
    {
        IsEditing = false;
        ErrorMessage = null;
    }
}
