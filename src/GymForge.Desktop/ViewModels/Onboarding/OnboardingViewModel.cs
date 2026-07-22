using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentValidation;
using GymForge.Application.UseCases.Onboarding;
using GymForge.Desktop.Services;
using MediatR;

namespace GymForge.Desktop.ViewModels.Onboarding;

/// <summary>Asistente de primer arranque: crea el gimnasio real sobre una base limpia.</summary>
public partial class OnboardingViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly SessionContext _session;

    // Gimnasio
    [ObservableProperty] private string _gymName = string.Empty;
    [ObservableProperty] private string _taxId = string.Empty;
    // Sede
    [ObservableProperty] private string _siteName = "Sede Central";
    [ObservableProperty] private string _siteAddress = string.Empty;
    // Responsable + PIN
    [ObservableProperty] private string _adminFirstName = string.Empty;
    [ObservableProperty] private string _adminLastName = string.Empty;
    [ObservableProperty] private string _adminPin = string.Empty;
    [ObservableProperty] private string _adminPinConfirm = string.Empty;
    // Marca
    [ObservableProperty] private string _brandColorHex = SessionContext.DefaultBrandColorHex;
    // Opciones
    [ObservableProperty] private bool _createSamplePlans = true;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;

    /// <summary>Se dispara cuando el gimnasio quedó creado y la app puede abrir el shell.</summary>
    public event Action? Completed;

    public OnboardingViewModel(IMediator mediator, SessionContext session)
    {
        _mediator = mediator;
        _session = session;
    }

    /// <summary>Elige un acento y lo previsualiza en vivo (se persiste al terminar).</summary>
    [RelayCommand]
    private void SelectAccent(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return;
        BrandColorHex = hex;
        App.ApplyBrandAccent(hex);
    }

    [RelayCommand]
    private async Task CompleteAsync()
    {
        ErrorMessage = null;

        if (AdminPin != AdminPinConfirm)
        {
            ErrorMessage = "Los PIN no coinciden.";
            return;
        }

        IsBusy = true;
        try
        {
            await _mediator.Send(new CompleteOnboardingCommand(
                GymName, TaxId, SiteName, SiteAddress,
                AdminFirstName, AdminLastName, AdminPin, BrandColorHex, CreateSamplePlans));

            // Recarga el tenant recién creado y aplica el color de marca definitivo.
            await _session.InitializeAsync();
            App.ApplyBrandAccent(_session.BrandColorHex);

            Completed?.Invoke();
        }
        catch (ValidationException vex)
        {
            ErrorMessage = string.Join("\n", vex.Errors.Select(e => e.ErrorMessage));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
