using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentValidation;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Access;
using GymForge.Application.UseCases.Licensing;
using GymForge.Application.UseCases.Settings;
using GymForge.Application.UseCases.Staff;
using GymForge.Desktop.Services;
using MediatR;

namespace GymForge.Desktop.ViewModels.Settings;

/// <summary>Configuración: datos del gimnasio, sedes (ABM) y reglas de acceso.</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly ISiteRepository _siteRepo;
    private readonly IMemberRepository _memberRepo;
    private readonly SessionContext _session;
    private readonly GatekeeperConfig _gatekeeper;
    private readonly CurrentLicense _license;
    private readonly IDataTransfer _dataTransfer;
    private Guid? _editingSiteId;   // null = agregando

    // Datos del gimnasio
    [ObservableProperty] private string _legalName = string.Empty;
    [ObservableProperty] private string _taxId = string.Empty;
    [ObservableProperty] private string? _companyMessage;
    [ObservableProperty] private bool _companySaved;

    // Marca (logo + color de acento)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLogoPreview))]
    private Bitmap? _logoPreview;
    [ObservableProperty] private string _brandColorHex = SessionContext.DefaultBrandColorHex;
    [ObservableProperty] private string? _brandMessage;
    [ObservableProperty] private bool _brandSaved;
    private string? _pendingLogoPath;   // ruta elegida, se persiste al guardar
    public bool HasLogoPreview => LogoPreview is not null;

    // Sedes
    [ObservableProperty] private ObservableCollection<SiteDto> _sites = [];
    [ObservableProperty] private string _siteFormTitle = "Agregar sede";
    [ObservableProperty] private string _newSiteName = string.Empty;
    [ObservableProperty] private string _newSiteAddress = string.Empty;
    [ObservableProperty] private string _newSitePhone = string.Empty;
    [ObservableProperty] private string? _siteMessage;
    [ObservableProperty] private bool _siteSaved;
    [ObservableProperty] private bool _isEditingSite;

    // Reglas de acceso
    [ObservableProperty] private decimal _stopOnOweAmount;
    [ObservableProperty] private decimal _warnOnOweAmount;
    [ObservableProperty] private int _antiPassbackMinutes;
    [ObservableProperty] private string? _accessMessage;
    [ObservableProperty] private bool _accessSaved;

    // Seguridad — PIN del cajero/admin
    [ObservableProperty] private string _currentPin = string.Empty;
    [ObservableProperty] private string _newPin = string.Empty;
    [ObservableProperty] private string _newPinConfirm = string.Empty;
    [ObservableProperty] private string? _pinMessage;
    [ObservableProperty] private bool _pinSaved;
    [ObservableProperty] private bool _isDefaultPinInUse;

    // Licencia
    [ObservableProperty] private string _licensePlanDisplay = string.Empty;
    [ObservableProperty] private string _licenseUsageDisplay = string.Empty;
    [ObservableProperty] private string _licenseKeyInput = string.Empty;
    [ObservableProperty] private string? _licenseMessage;
    [ObservableProperty] private bool _licenseSaved;

    // Datos — exportar / importar (migración a otra PC)
    [ObservableProperty] private string? _dataMessage;
    [ObservableProperty] private bool _dataOk;
    [ObservableProperty] private bool _isTransferBusy;
    [ObservableProperty] private bool _isImportPending;
    [ObservableProperty] private string _importSummary = string.Empty;
    [ObservableProperty] private string _importPin = string.Empty;
    private string? _pendingImportPath;   // paquete validado, esperando confirmación

    public SettingsViewModel(
        IMediator mediator, ISiteRepository siteRepo, IMemberRepository memberRepo,
        SessionContext session, GatekeeperConfig gatekeeper, CurrentLicense license,
        IDataTransfer dataTransfer)
    {
        _mediator = mediator;
        _siteRepo = siteRepo;
        _memberRepo = memberRepo;
        _session = session;
        _gatekeeper = gatekeeper;
        _license = license;
        _dataTransfer = dataTransfer;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var company = await _siteRepo.GetCompanyAsync(_session.CompanyId, ct);
        if (company is not null)
        {
            LegalName = company.LegalName;
            TaxId = company.TaxId;
        }

        // Marca: refleja lo que ya cargó la sesión.
        BrandColorHex = _session.BrandColorHex;
        LogoPreview = _session.LogoImage;
        _pendingLogoPath = _session.LogoPath;

        var sites = await _siteRepo.GetByCompanyAsync(_session.CompanyId, ct);
        Sites = new ObservableCollection<SiteDto>(
            sites.Select(s => new SiteDto(s.Id, s.Name, s.Address, s.Phone)));

        StopOnOweAmount = _gatekeeper.StopOnOweAmount;
        WarnOnOweAmount = _gatekeeper.WarnOnOweAmount;
        AntiPassbackMinutes = _gatekeeper.AntiPassbackMinutes;

        IsDefaultPinInUse = await _mediator.Send(new CheckDefaultPinQuery(_session.CompanyId), ct);

        await RefreshLicenseAsync(ct);
    }

    private async Task RefreshLicenseAsync(CancellationToken ct = default)
    {
        var state = _license.State;
        LicensePlanDisplay = state.Status switch
        {
            LicenseStatus.Active => $"Plan {state.Tier} — vence el {state.ExpiresOn:dd/MM/yyyy}",
            LicenseStatus.Grace =>
                $"Plan {state.Tier} — venció el {state.ExpiresOn:dd/MM/yyyy} (período de gracia, renovala)",
            LicenseStatus.Expired => $"Plan {state.Tier} vencido — operando con límites Free",
            _ => "Plan Free (sin licencia)",
        };

        var members = await _memberRepo.CountByCompanyAsync(_session.CompanyId, ct);
        var sites = await _siteRepo.CountActiveSitesAsync(_session.CompanyId, ct);
        LicenseUsageDisplay = $"Socios: {members} de {state.MaxMembers} · Sedes: {sites} de {state.MaxSites}";
    }

    // ── Datos del gimnasio ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task SaveCompanyAsync()
    {
        CompanyMessage = null;
        CompanySaved = false;
        try
        {
            await _mediator.Send(new UpdateCompanyCommand(_session.CompanyId, LegalName, TaxId));
            await _session.InitializeAsync();
            CompanySaved = true;
            CompanyMessage = "Datos guardados.";
        }
        catch (ValidationException vex)
        {
            CompanyMessage = string.Join("\n", vex.Errors.Select(e => e.ErrorMessage));
        }
        catch (Exception ex) { CompanyMessage = ex.Message; }
    }

    // ── Marca ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task PickLogoAsync()
    {
        BrandMessage = null;
        var top = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (top?.StorageProvider is not { } storage) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Elegí el logo del gimnasio",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Imágenes") { Patterns = ["*.png", "*.jpg", "*.jpeg"] }],
        });

        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;

        _pendingLogoPath = path;
        LogoPreview = LoadBitmap(path);
    }

    [RelayCommand]
    private void RemoveLogo()
    {
        _pendingLogoPath = null;
        LogoPreview = null;
        BrandMessage = null;
    }

    /// <summary>Elige un color de acento y lo previsualiza en vivo en toda la app.
    /// Se persiste recién al guardar.</summary>
    [RelayCommand]
    private void SelectAccent(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return;
        BrandColorHex = hex;
        App.ApplyBrandAccent(hex);
    }

    [RelayCommand]
    private async Task SaveBrandingAsync()
    {
        BrandMessage = null;
        BrandSaved = false;
        try
        {
            string? finalLogo = null;
            if (_pendingLogoPath is { } src && File.Exists(src))
            {
                var brandDir = BrandDir();
                var dest = Path.Combine(brandDir, "logo" + Path.GetExtension(src).ToLowerInvariant());
                if (!string.Equals(Path.GetFullPath(src), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                {
                    // Reemplaza cualquier logo previo (posible otra extensión).
                    foreach (var old in Directory.GetFiles(brandDir, "logo.*"))
                        try { File.Delete(old); } catch { /* en uso: se ignora */ }
                    File.Copy(src, dest, overwrite: true);
                }
                finalLogo = dest;
            }

            await _mediator.Send(new UpdateBrandingCommand(_session.CompanyId, finalLogo, BrandColorHex));
            await _session.InitializeAsync();
            App.ApplyBrandAccent(BrandColorHex);

            LogoPreview = _session.LogoImage;
            _pendingLogoPath = _session.LogoPath;
            BrandSaved = true;
            BrandMessage = "Marca guardada.";
        }
        catch (ValidationException vex)
        {
            BrandMessage = string.Join("\n", vex.Errors.Select(e => e.ErrorMessage));
        }
        catch (Exception ex) { BrandMessage = ex.Message; }
    }

    private static string BrandDir()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GymForge", "brand");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static Bitmap? LoadBitmap(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return new Bitmap(fs);
        }
        catch { return null; }
    }

    // ── Sedes ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private void EditSite(SiteDto? site)
    {
        if (site is null) return;
        _editingSiteId = site.Id;
        IsEditingSite = true;
        SiteFormTitle = $"Modificar: {site.Name}";
        NewSiteName = site.Name;
        NewSiteAddress = site.Address;
        NewSitePhone = site.Phone ?? string.Empty;
        SiteMessage = null;
    }

    [RelayCommand]
    private void CancelSiteEdit()
    {
        _editingSiteId = null;
        IsEditingSite = false;
        SiteFormTitle = "Agregar sede";
        NewSiteName = string.Empty;
        NewSiteAddress = string.Empty;
        NewSitePhone = string.Empty;
        SiteMessage = null;
    }

    [RelayCommand]
    private async Task SaveSiteAsync()
    {
        SiteMessage = null;
        SiteSaved = false;
        try
        {
            var phone = string.IsNullOrWhiteSpace(NewSitePhone) ? null : NewSitePhone;
            string message;

            if (_editingSiteId is { } id)
            {
                await _mediator.Send(new UpdateSiteCommand(id, NewSiteName, NewSiteAddress, phone));
                message = "Sede actualizada.";
            }
            else
            {
                await _mediator.Send(new CreateSiteCommand(_session.CompanyId, NewSiteName, NewSiteAddress));
                message = "Sede agregada.";
            }

            CancelSiteEdit();
            SiteSaved = true;
            SiteMessage = message;
            await _session.InitializeAsync();   // refresca el selector del topbar
            await LoadAsync();
        }
        catch (ValidationException vex)
        {
            SiteMessage = string.Join("\n", vex.Errors.Select(e => e.ErrorMessage));
        }
        catch (Exception ex) { SiteMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteSiteAsync(SiteDto? site)
    {
        if (site is null) return;
        SiteMessage = null;
        SiteSaved = false;
        try
        {
            var deleted = await _mediator.Send(new DeleteSiteCommand(site.Id));
            SiteSaved = true;
            SiteMessage = deleted
                ? "Sede eliminada."
                : "La sede tenía socios o movimientos: se desactivó y ya no aparece (el historial se conserva).";
            await _session.InitializeAsync();
            await LoadAsync();
        }
        catch (Exception ex) { SiteMessage = ex.Message; }
    }

    // ── Seguridad — PIN ─────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ChangePinAsync()
    {
        PinMessage = null;
        PinSaved = false;

        if (NewPin != NewPinConfirm)
        {
            PinMessage = "El nuevo PIN y su confirmación no coinciden.";
            return;
        }

        try
        {
            await _mediator.Send(new ChangePinCommand(_session.CompanyId, CurrentPin.Trim(), NewPin.Trim()));
            PinSaved = true;
            PinMessage = "PIN actualizado.";
            CurrentPin = string.Empty;
            NewPin = string.Empty;
            NewPinConfirm = string.Empty;
            IsDefaultPinInUse = await _mediator.Send(new CheckDefaultPinQuery(_session.CompanyId));
        }
        catch (ValidationException vex)
        {
            PinMessage = string.Join("\n", vex.Errors.Select(e => e.ErrorMessage));
        }
        catch (Exception ex) { PinMessage = ex.Message; }
    }

    // ── Licencia ────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ActivateLicenseAsync()
    {
        LicenseMessage = null;
        LicenseSaved = false;
        try
        {
            var state = await _mediator.Send(
                new ActivateLicenseCommand(_session.CompanyId, LicenseKeyInput.Trim()));
            LicenseSaved = true;
            LicenseMessage = $"Licencia {state.Tier} activada. Vence el {state.ExpiresOn:dd/MM/yyyy}.";
            LicenseKeyInput = string.Empty;
            await RefreshLicenseAsync();
        }
        catch (ValidationException vex)
        {
            LicenseMessage = string.Join("\n", vex.Errors.Select(e => e.ErrorMessage));
        }
        catch (Exception ex) { LicenseMessage = ex.Message; }
    }

    // ── Datos: exportar / importar ──────────────────────────────────────────

    /// <summary>
    /// Arma el paquete con la base y los archivos del gimnasio. La app sigue usable
    /// mientras tanto: la copia de la base es consistente aunque haya gente fichando.
    /// </summary>
    [RelayCommand]
    private async Task ExportDataAsync()
    {
        DataMessage = null;
        DataOk = false;

        var top = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (top?.StorageProvider is not { } storage) return;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar los datos del gimnasio",
            SuggestedFileName = _dataTransfer.SuggestedFileName(LegalName),
            DefaultExtension = "zip",
            FileTypeChoices = [new FilePickerFileType("Paquete GymForge") { Patterns = ["*.zip"] }],
        });

        if (file?.TryGetLocalPath() is not { } destino) return;

        try
        {
            IsTransferBusy = true;
            var info = await _dataTransfer.ExportAsync(destino);
            DataOk = true;
            DataMessage = $"Listo: {info.Members} socios y {info.Payments} cobros en {Path.GetFileName(destino)}.";
        }
        catch (Exception ex) { DataMessage = ex.Message; }
        finally { IsTransferBusy = false; }
    }

    /// <summary>Paso 1 del import: valida el paquete y muestra qué contiene, sin tocar nada.</summary>
    [RelayCommand]
    private async Task ChooseImportFileAsync()
    {
        DataMessage = null;
        DataOk = false;
        ImportPin = string.Empty;

        var top = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (top?.StorageProvider is not { } storage) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Elegí el paquete a importar",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Paquete GymForge") { Patterns = ["*.zip"] }],
        });

        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } origen) return;

        try
        {
            IsTransferBusy = true;
            var info = await _dataTransfer.InspectAsync(origen);
            _pendingImportPath = origen;
            ImportSummary =
                $"{info.GymName} · {info.Members} socios · {info.Payments} cobros · " +
                $"exportado el {info.ExportedAt:dd/MM/yyyy}";
            IsImportPending = true;
        }
        catch (Exception ex)
        {
            _pendingImportPath = null;
            IsImportPending = false;
            DataMessage = ex.Message;
        }
        finally { IsTransferBusy = false; }
    }

    /// <summary>Paso 2: con el PIN confirmado, reemplaza los datos y reinicia la app.</summary>
    [RelayCommand]
    private async Task ConfirmImportAsync()
    {
        if (_pendingImportPath is null) return;
        DataMessage = null;
        DataOk = false;

        var staff = await _mediator.Send(new AuthenticateStaffCommand(_session.CompanyId, ImportPin.Trim()));
        if (staff is null)
        {
            DataMessage = "PIN incorrecto.";
            return;
        }

        try
        {
            IsTransferBusy = true;
            await _dataTransfer.ImportAsync(_pendingImportPath);

            // El DbContext y las pantallas quedaron con los datos del gimnasio anterior:
            // reiniciar es más seguro que intentar refrescar todo en caliente.
            RestartApp();
        }
        catch (Exception ex)
        {
            DataMessage = ex.Message;
        }
        finally { IsTransferBusy = false; }
    }

    [RelayCommand]
    private void CancelImport()
    {
        _pendingImportPath = null;
        IsImportPending = false;
        ImportSummary = string.Empty;
        ImportPin = string.Empty;
    }

    private static void RestartApp()
    {
        if (Environment.ProcessPath is { } exe)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });

        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    // ── Reglas de acceso ────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SaveAccessAsync()
    {
        AccessMessage = null;
        AccessSaved = false;
        try
        {
            await _mediator.Send(new UpdateAccessSettingsCommand(
                _session.CompanyId, StopOnOweAmount, WarnOnOweAmount, AntiPassbackMinutes));
            AccessSaved = true;
            AccessMessage = "Reglas de acceso guardadas.";
        }
        catch (ValidationException vex)
        {
            AccessMessage = string.Join("\n", vex.Errors.Select(e => e.ErrorMessage));
        }
        catch (Exception ex) { AccessMessage = ex.Message; }
    }
}
