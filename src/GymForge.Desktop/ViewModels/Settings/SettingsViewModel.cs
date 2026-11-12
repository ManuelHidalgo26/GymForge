using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentValidation;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Access;
using GymForge.Application.UseCases.Dunning;
using GymForge.Application.UseCases.Licensing;
using GymForge.Application.UseCases.Settings;
using GymForge.Application.UseCases.Staff;
using GymForge.Desktop.Services;
using GymForge.Desktop.ViewModels.Shared;
using MediatR;

namespace GymForge.Desktop.ViewModels.Settings;

/// <summary>Configuración: datos del gimnasio, sedes (ABM), reglas de acceso, cobranzas, licencia y datos (backup/migración).</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly ISiteRepository _siteRepo;
    private readonly IMemberRepository _memberRepo;
    private readonly SessionContext _session;
    private readonly GatekeeperConfig _gatekeeper;
    private readonly DunningConfig _dunning;
    private readonly CurrentLicense _license;
    private readonly IDataTransfer _dataTransfer;
    private readonly IDatabaseBackup _backup;
    private readonly ILocalSettings _localSettings;
    private readonly IMachineFingerprint _fingerprint;
    private readonly IBackupKeyStore _keyStore;
    private Guid? _editingSiteId;   // null = agregando

    /// <summary>Card "Comisiones del personal": su propio VM, para no seguir engordando este.</summary>
    public StaffCommissionsViewModel Commissions { get; }

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

    // Cobranzas y recordatorios: cobro automático + las 5 etapas (días, nombre y mensaje)
    [ObservableProperty] private bool _dunningEnabled;
    [ObservableProperty] private ObservableCollection<DunningStageEditor> _dunningStages = [];
    [ObservableProperty] private string? _dunningMessage;
    [ObservableProperty] private bool _dunningSaved;

    // Seguridad — PIN del cajero/admin
    [ObservableProperty] private string _currentPin = string.Empty;
    [ObservableProperty] private string _newPin = string.Empty;
    [ObservableProperty] private string _newPinConfirm = string.Empty;
    [ObservableProperty] private string? _pinMessage;
    [ObservableProperty] private bool _pinSaved;
    [ObservableProperty] private bool _isDefaultPinInUse;

    // Licencia
    [ObservableProperty] private string _licensePlanDisplay = string.Empty;
    [ObservableProperty] private LicenseUsageLine _membersUsage = LicenseUsage.Members(0, LicenseState.Free);
    [ObservableProperty] private LicenseUsageLine _sitesUsage = LicenseUsage.Sites(0, LicenseState.Free);
    [ObservableProperty] private string _licenseKeyInput = string.Empty;
    [ObservableProperty] private string? _licenseMessage;
    [ObservableProperty] private bool _licenseSaved;
    // Código de ESTA PC (para que el gimnasio lo dicte al proveedor) y aviso si la clave es de otra PC.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThisPcCode), nameof(ThisPcCodeDisplay))]
    [NotifyCanExecuteChangedFor(nameof(CopyThisPcCodeCommand))]
    private string _thisPcCode = string.Empty;
    [ObservableProperty] private bool _licenseMachineMismatch;
    public bool HasThisPcCode => !string.IsNullOrEmpty(ThisPcCode);
    public string ThisPcCodeDisplay => HasThisPcCode ? ThisPcCode : "No disponible";

    /// <summary>Feedback inline de "Copiar" (junto al botón): se prende al copiar y se apaga solo.</summary>
    [ObservableProperty] private bool _thisPcCodeCopied;
    private static readonly TimeSpan CopiedFeedbackDuration = TimeSpan.FromSeconds(2.5);

    // Versión y actualizaciones: el estado vive en UpdateViewModel (compartido con el aviso del shell).
    public UpdateViewModel Updates { get; }
    [ObservableProperty] private string _updateFeedDisplay = string.Empty;
    [ObservableProperty] private string _updateFeedInput = string.Empty;
    [ObservableProperty] private string _updateFeedPin = string.Empty;
    [ObservableProperty] private string? _updateFeedMessage;
    [ObservableProperty] private bool _updateFeedSaved;

    // Datos — exportar / importar (migración a otra PC)
    [ObservableProperty] private string? _dataMessage;
    [ObservableProperty] private bool _dataOk;
    [ObservableProperty] private bool _isTransferBusy;
    [ObservableProperty] private bool _isImportPending;
    [ObservableProperty] private string _importSummary = string.Empty;
    [ObservableProperty] private string _importPin = string.Empty;
    // Paquete cifrado: primero se pide la frase para poder abrirlo y mostrar qué contiene.
    [ObservableProperty] private bool _isImportPassphraseNeeded;
    [ObservableProperty] private string _importPassphrase = string.Empty;
    private string? _pendingImportPath;         // paquete elegido, esperando frase y/o confirmación
    private string? _confirmedImportPassphrase; // frase con la que se pudo abrir; se reusa al importar

    // Datos — frase de acceso del respaldo (cifra lo que sale de la PC). Nunca se muestra, solo se fija.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackupPassphraseActionText))]
    private bool _hasBackupPassphrase;
    [ObservableProperty] private string _passphraseInput = string.Empty;
    [ObservableProperty] private string _passphraseConfirm = string.Empty;
    [ObservableProperty] private string _passphrasePin = string.Empty;
    [ObservableProperty] private string? _passphraseMessage;
    [ObservableProperty] private bool _passphraseOk;
    public string BackupPassphraseActionText => HasBackupPassphrase ? "Cambiar frase" : "Fijar frase";
    public int BackupPassphraseMinLength => BackupPassphrase.MinLength;

    // Datos — copia de seguridad externa (pendrive / red / carpeta sincronizada)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOffsiteBackupDir), nameof(OffsiteBackupDirDisplay))]
    [NotifyCanExecuteChangedFor(nameof(BackupOffsiteNowCommand))]
    private string? _offsiteBackupDir;
    [ObservableProperty] private string _offsiteStatusDisplay = string.Empty;
    [ObservableProperty] private bool _offsiteNeedsAttention;
    [ObservableProperty] private string? _offsiteMessage;
    [ObservableProperty] private bool _offsiteOk;

    // Copias externas EN CLARO de antes del cifrado: aviso + borrado solo tras confirmar (nunca automático).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLegacyPlainCopies), nameof(LegacyPlainCopiesText))]
    private int _legacyPlainCopyCount;
    [ObservableProperty] private bool _isLegacySweepConfirming;
    public bool HasLegacyPlainCopies => LegacyPlainCopyCount > 0;
    public string LegacyPlainCopiesText => LegacyPlainCopyCount == 1
        ? "Hay 1 copia vieja sin cifrar en el destino externo."
        : $"Hay {LegacyPlainCopyCount} copias viejas sin cifrar en el destino externo.";
    public bool HasOffsiteBackupDir => !string.IsNullOrWhiteSpace(OffsiteBackupDir);
    public string OffsiteBackupDirDisplay => OffsiteBackupDir ?? "Sin carpeta elegida";

    public SettingsViewModel(
        IMediator mediator, ISiteRepository siteRepo, IMemberRepository memberRepo,
        SessionContext session, GatekeeperConfig gatekeeper, CurrentLicense license,
        IDataTransfer dataTransfer, IDatabaseBackup backup, ILocalSettings localSettings,
        IMachineFingerprint fingerprint, UpdateViewModel updates, DunningConfig dunning,
        IBackupKeyStore keyStore)
    {
        _mediator = mediator;
        _siteRepo = siteRepo;
        _memberRepo = memberRepo;
        _session = session;
        _gatekeeper = gatekeeper;
        _license = license;
        _dataTransfer = dataTransfer;
        _backup = backup;
        _localSettings = localSettings;
        _fingerprint = fingerprint;
        _keyStore = keyStore;
        Updates = updates;
        _dunning = dunning;
        Commissions = new StaffCommissionsViewModel(mediator, session);
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

        DunningEnabled = _dunning.Enabled;
        DunningStages = new ObservableCollection<DunningStageEditor>(
            _dunning.Stages.OrderBy(s => s.DaysOverdue).Select(s => new DunningStageEditor(s)));

        IsDefaultPinInUse = await _mediator.Send(new CheckDefaultPinQuery(_session.CompanyId), ct);

        await Commissions.LoadAsync(ct);

        UpdateFeedInput = _localSettings.UpdateFeedUrl ?? string.Empty;
        RefreshUpdateFeedDisplay();

        HasBackupPassphrase = _keyStore.HasPassphrase;
        await RefreshOffsiteStatusAsync();
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
        MembersUsage = LicenseUsage.Members(members, state);
        SitesUsage = LicenseUsage.Sites(sites, state);

        ThisPcCode = _fingerprint.Current();
        LicenseMachineMismatch = state.MachineMismatch;
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
        if (MainStorage() is not { } storage) return;

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
                await _mediator.Send(new UpdateSiteCommand(_session.CompanyId, id, NewSiteName, NewSiteAddress, phone));
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
            var deleted = await _mediator.Send(new DeleteSiteCommand(_session.CompanyId, site.Id));
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

    [RelayCommand(CanExecute = nameof(HasThisPcCode))]
    private async Task CopyThisPcCodeAsync()
    {
        if (MainClipboard() is not { } clipboard) return;

        await clipboard.SetTextAsync(ThisPcCode);
        await ShowThisPcCodeCopiedAsync(CopiedFeedbackDuration);
    }

    /// <summary>Muestra "Copiado" junto al botón durante <paramref name="duration"/> y lo oculta.</summary>
    public async Task ShowThisPcCodeCopiedAsync(TimeSpan duration)
    {
        ThisPcCodeCopied = true;
        await Task.Delay(duration);
        ThisPcCodeCopied = false;
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

        // El paquete sale de la PC: sin frase de acceso no se genera (y se encamina a fijarla).
        if (!_keyStore.HasPassphrase)
        {
            DataMessage = "Antes de exportar, fijá la frase de acceso del respaldo (en esta misma pantalla, más abajo): el archivo sale cifrado con ella.";
            return;
        }

        if (MainStorage() is not { } storage) return;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar los datos del gimnasio",
            SuggestedFileName = _dataTransfer.SuggestedFileName(LegalName),
            DefaultExtension = "gfbak",
            FileTypeChoices = [new FilePickerFileType("Paquete GymForge (cifrado)") { Patterns = ["*.gfbak"] }],
        });

        if (file?.TryGetLocalPath() is not { } destino) return;

        try
        {
            IsTransferBusy = true;
            var info = await _dataTransfer.ExportAsync(destino);
            DataOk = true;
            DataMessage =
                $"Listo: {info.Members} socios y {info.Payments} cobros en {Path.GetFileName(destino)}. " +
                "Para abrirlo en otra PC vas a necesitar la frase de acceso del respaldo.";
        }
        catch (Exception ex) { DataMessage = ex.Message; }
        finally { IsTransferBusy = false; }
    }

    /// <summary>
    /// Paso 1 del import: elige el archivo. Si está cifrado pide la frase antes de abrirlo; si es un .zip viejo
    /// lo valida ya. En ambos casos muestra qué contiene sin tocar nada.
    /// </summary>
    [RelayCommand]
    private async Task ChooseImportFileAsync()
    {
        DataMessage = null;
        DataOk = false;

        if (MainStorage() is not { } storage) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Elegí el paquete a importar",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Paquete GymForge") { Patterns = ["*.gfbak", "*.zip"] }],
        });

        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } origen) return;

        ResetImport();
        _pendingImportPath = origen;
        if (_dataTransfer.IsEncrypted(origen))
        {
            IsImportPassphraseNeeded = true;
            return;
        }

        await InspectPendingImportAsync(passphrase: null);
    }

    /// <summary>Paso 1b (solo paquetes cifrados): con la frase tipeada abre el paquete y muestra qué contiene.</summary>
    [RelayCommand]
    private Task UnlockImportAsync() => InspectPendingImportAsync(ImportPassphrase);

    private async Task InspectPendingImportAsync(string? passphrase)
    {
        if (_pendingImportPath is null) return;
        DataMessage = null;
        DataOk = false;

        try
        {
            IsTransferBusy = true;
            var info = await _dataTransfer.InspectAsync(_pendingImportPath, passphrase);
            _confirmedImportPassphrase = passphrase;
            ImportSummary =
                $"{info.GymName} · {info.Members} socios · {info.Payments} cobros · " +
                $"exportado el {info.ExportedAt:dd/MM/yyyy}";
            IsImportPassphraseNeeded = false;
            ImportPassphrase = string.Empty;
            IsImportPending = true;
        }
        catch (Exception ex)
        {
            DataMessage = ex.Message;
            // Con frase incorrecta se deja el campo abierto para reintentar; cualquier otro error descarta el archivo.
            if (IsImportPassphraseNeeded) ImportPassphrase = string.Empty;
            else ResetImport();
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
            await _dataTransfer.ImportAsync(_pendingImportPath, _confirmedImportPassphrase);

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
    private void CancelImport() => ResetImport();

    private void ResetImport()
    {
        _pendingImportPath = null;
        _confirmedImportPassphrase = null;
        IsImportPending = false;
        IsImportPassphraseNeeded = false;
        ImportSummary = string.Empty;
        ImportPin = string.Empty;
        ImportPassphrase = string.Empty;
    }

    // ── Datos: frase de acceso del respaldo ─────────────────────────────────

    /// <summary>
    /// Fija o cambia la frase que cifra lo que sale de la PC. Pide el PIN de admin (misma barra que el import).
    /// La frase no se loguea ni se vuelve a mostrar; los respaldos hechos con la anterior siguen necesitándola.
    /// </summary>
    [RelayCommand]
    private async Task SetBackupPassphraseAsync()
    {
        PassphraseMessage = null;
        PassphraseOk = false;

        if (PassphraseInput.Trim().Length < BackupPassphrase.MinLength)
        {
            PassphraseMessage = $"La frase debe tener al menos {BackupPassphrase.MinLength} caracteres.";
            return;
        }

        if (PassphraseInput != PassphraseConfirm)
        {
            PassphraseMessage = "Las dos frases no coinciden.";
            return;
        }

        var staff = await _mediator.Send(new AuthenticateStaffCommand(_session.CompanyId, PassphrasePin.Trim()));
        if (staff is null)
        {
            PassphraseMessage = "PIN incorrecto.";
            return;
        }

        try
        {
            _keyStore.SetPassphrase(PassphraseInput);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            PassphraseMessage = "No se pudo guardar la frase en esta PC. Probá de nuevo.";
            return;
        }

        PassphraseInput = string.Empty;
        PassphraseConfirm = string.Empty;
        PassphrasePin = string.Empty;
        HasBackupPassphrase = true;
        PassphraseOk = true;
        PassphraseMessage =
            "Frase guardada. Anotala y guardala APARTE (no en el mismo pendrive): si la perdés, los respaldos " +
            "cifrados no se pueden recuperar.";
        await RefreshOffsiteStatusAsync();
    }

    // ── Actualizaciones: origen ─────────────────────────────────────────────

    private void RefreshUpdateFeedDisplay() =>
        UpdateFeedDisplay = UpdateFeed.Resolve(_localSettings) ?? "sin configurar";

    /// <summary>Guarda el origen de actualizaciones de esta PC. Se valida primero (sin pedir PIN) y luego se exige el PIN.</summary>
    [RelayCommand]
    private async Task SaveUpdateFeedAsync()
    {
        UpdateFeedMessage = null;
        UpdateFeedSaved = false;

        if (!UpdateFeed.IsAllowedSource(UpdateFeedInput, out var error))
        {
            UpdateFeedMessage = error;
            return;
        }

        var staff = await _mediator.Send(new AuthenticateStaffCommand(_session.CompanyId, UpdateFeedPin.Trim()));
        if (staff is null)
        {
            UpdateFeedMessage = "PIN incorrecto.";
            return;
        }

        _localSettings.UpdateFeedUrl = string.IsNullOrWhiteSpace(UpdateFeedInput) ? null : UpdateFeedInput.Trim();
        _localSettings.Save();

        UpdateFeedPin = string.Empty;
        RefreshUpdateFeedDisplay();
        UpdateFeedSaved = true;
        UpdateFeedMessage = "Origen guardado.";
    }

    private static void RestartApp()
    {
        if (Environment.ProcessPath is { } exe)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });

        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    // ── Datos: copia de seguridad externa ───────────────────────────────────

    [RelayCommand]
    private async Task PickOffsiteFolderAsync()
    {
        OffsiteMessage = null;
        if (MainStorage() is not { } storage) return;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Elegí la carpeta de la copia de seguridad externa",
            AllowMultiple = false,
        });

        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;

        // La fecha del último resguardo era de otro destino: no se arrastra a la carpeta nueva.
        if (!string.Equals(path, _localSettings.OffsiteBackupDir, StringComparison.OrdinalIgnoreCase))
            _localSettings.LastOffsiteBackupUtc = null;

        _localSettings.OffsiteBackupDir = path;
        _localSettings.Save();
        await RefreshOffsiteStatusAsync();
    }

    [RelayCommand]
    private async Task ClearOffsiteAsync()
    {
        OffsiteMessage = null;
        _localSettings.OffsiteBackupDir = null;
        _localSettings.LastOffsiteBackupUtc = null;
        _localSettings.Save();
        await RefreshOffsiteStatusAsync();
    }

    /// <summary>
    /// "Copiar ahora": hace un backup nuevo y lo copia al destino externo. Va en un hilo aparte porque
    /// un pendrive o una carpeta de red puede tardar y no debe congelar la ventana.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasOffsiteBackupDir))]
    private async Task BackupOffsiteNowAsync()
    {
        var outcome = await Task.Run(_backup.BackupOffsiteNow);

        OffsiteOk = outcome == OffsiteBackupOutcome.Copied;
        OffsiteMessage = outcome switch
        {
            OffsiteBackupOutcome.Copied => "Copia externa lista.",
            OffsiteBackupOutcome.NotConfigured => "Elegí primero la carpeta de destino.",
            OffsiteBackupOutcome.LocalBackupFailed =>
                "No se pudo crear el resguardo en esta PC (revisá el espacio en disco), así que no se copió nada al destino externo.",
            OffsiteBackupOutcome.MissingPassphrase =>
                "Fijá primero la frase de acceso del respaldo: sin ella no se copia nada fuera de esta PC. El resguardo en esta PC sí quedó hecho.",
            _ => "No se pudo copiar. Revisá que el pendrive o la carpeta estén disponibles; el resguardo en esta PC sí quedó hecho.",
        };
        await RefreshOffsiteStatusAsync();
    }

    [RelayCommand]
    private void AskLegacySweep() => IsLegacySweepConfirming = true;

    [RelayCommand]
    private void CancelLegacySweep() => IsLegacySweepConfirming = false;

    /// <summary>Segundo paso (ya confirmado): borra solo las copias en claro legadas del destino externo.</summary>
    [RelayCommand]
    private async Task ConfirmLegacySweepAsync()
    {
        IsLegacySweepConfirming = false;
        var detected = LegacyPlainCopyCount;
        var deleted = await Task.Run(_backup.DeleteLegacyPlainOffsiteCopies);

        OffsiteOk = deleted.Count > 0;
        OffsiteMessage = LegacySweepMessage(deleted.Count, Math.Max(0, detected - deleted.Count));
        await RefreshOffsiteStatusAsync();
    }

    private static string LegacySweepMessage(int deleted, int remaining) => (deleted, remaining) switch
    {
        (0, _) => "No se pudo borrar ninguna copia vieja. Revisá que la carpeta o el pendrive estén disponibles.",
        (1, 0) => "Listo: se borró 1 copia vieja sin cifrar.",
        (_, 0) => $"Listo: se borraron {deleted} copias viejas sin cifrar.",
        _ => $"Se {(deleted == 1 ? "borró 1 copia" : $"borraron {deleted} copias")}; "
             + $"{(remaining == 1 ? "1 no se pudo borrar" : $"{remaining} no se pudieron borrar")} "
             + "(revisá que la carpeta esté disponible).",
    };

    private async Task RefreshOffsiteStatusAsync()
    {
        var dir = _localSettings.OffsiteBackupDir;
        OffsiteBackupDir = string.IsNullOrWhiteSpace(dir) ? null : dir;

        // Fuera del hilo de UI: preguntar por una carpeta de red caída puede tardar en responder.
        var available = !string.IsNullOrWhiteSpace(dir) && await Task.Run(() => Directory.Exists(dir));
        var status = OffsiteBackupStatus.Evaluate(
            dir, _localSettings.LastOffsiteBackupUtc?.ToLocalTime(), available, DateTime.Now, _keyStore.HasPassphrase);
        OffsiteStatusDisplay = status.Display;
        OffsiteNeedsAttention = status.NeedsAttention;

        LegacyPlainCopyCount = await Task.Run(() => _backup.FindLegacyPlainOffsiteCopies().Count);
        if (!HasLegacyPlainCopies) IsLegacySweepConfirming = false;
    }

    private static IStorageProvider? MainStorage() =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow?.StorageProvider;

    private static IClipboard? MainClipboard() =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow?.Clipboard;

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

    // ── Cobranzas y recordatorios ───────────────────────────────────────────

    [RelayCommand]
    private async Task SaveDunningAsync()
    {
        DunningMessage = null;
        DunningSaved = false;
        try
        {
            var stages = DunningStages
                .Select(s => new DunningStageInput(s.DaysOverdue, s.Label, s.Template))
                .ToList();
            await _mediator.Send(new UpdateDunningSettingsCommand(_session.CompanyId, DunningEnabled, stages));
            DunningSaved = true;
            DunningMessage = "Cobranzas guardadas.";
        }
        catch (ValidationException vex)
        {
            DunningMessage = string.Join("\n", vex.Errors.Select(e => e.ErrorMessage));
        }
        catch (Exception ex) { DunningMessage = ex.Message; }
    }
}
