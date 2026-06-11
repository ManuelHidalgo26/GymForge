using System.Text.Json;
using FluentValidation;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Access;
using GymForge.Domain.Entities;
using GymForge.Domain.ValueObjects;
using MediatR;

namespace GymForge.Application.UseCases.Settings;

// ── Datos del gimnasio ────────────────────────────────────────────────────────

public record UpdateCompanyCommand(Guid CompanyId, string LegalName, string TaxId) : IRequest;

public class UpdateCompanyCommandValidator : AbstractValidator<UpdateCompanyCommand>
{
    public UpdateCompanyCommandValidator()
    {
        RuleFor(x => x.LegalName).NotEmpty().MaximumLength(200).WithMessage("El nombre es obligatorio.");
        RuleFor(x => x.TaxId).NotEmpty().WithMessage("El CUIT es obligatorio.");
    }
}

public class UpdateCompanyCommandHandler : IRequestHandler<UpdateCompanyCommand>
{
    private readonly ISiteRepository _repo;
    public UpdateCompanyCommandHandler(ISiteRepository repo) => _repo = repo;

    public async Task Handle(UpdateCompanyCommand cmd, CancellationToken ct)
    {
        var company = await _repo.GetCompanyAsync(cmd.CompanyId, ct)
            ?? throw new InvalidOperationException("La empresa no existe.");

        // Valida dígito verificador y normaliza el formato XX-XXXXXXXX-X.
        var cuit = new Cuit(cmd.TaxId);

        company.UpdateIdentity(cmd.LegalName.Trim(), cuit);
        await _repo.SaveChangesAsync(ct);
    }
}

// ── Sedes ─────────────────────────────────────────────────────────────────────

public record SiteDto(Guid Id, string Name, string Address, string? Phone = null);

public record CreateSiteCommand(Guid CompanyId, string Name, string Address) : IRequest<SiteDto>;

public class CreateSiteCommandValidator : AbstractValidator<CreateSiteCommand>
{
    public CreateSiteCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("El nombre de la sede es obligatorio.");
        RuleFor(x => x.Address).NotEmpty().MaximumLength(200).WithMessage("La dirección es obligatoria.");
    }
}

public class CreateSiteCommandHandler : IRequestHandler<CreateSiteCommand, SiteDto>
{
    private readonly ISiteRepository _repo;
    public CreateSiteCommandHandler(ISiteRepository repo) => _repo = repo;

    public async Task<SiteDto> Handle(CreateSiteCommand cmd, CancellationToken ct)
    {
        var site = Site.Create(cmd.CompanyId, cmd.Name.Trim(), cmd.Address.Trim());
        await _repo.AddSiteAsync(site, ct);
        await _repo.SaveChangesAsync(ct);
        return new SiteDto(site.Id, site.Name, site.Address);
    }
}

public record UpdateSiteCommand(Guid SiteId, string Name, string Address, string? Phone = null) : IRequest<SiteDto>;

public class UpdateSiteCommandValidator : AbstractValidator<UpdateSiteCommand>
{
    public UpdateSiteCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("El nombre de la sede es obligatorio.");
        RuleFor(x => x.Address).NotEmpty().MaximumLength(200).WithMessage("La dirección es obligatoria.");
    }
}

public class UpdateSiteCommandHandler : IRequestHandler<UpdateSiteCommand, SiteDto>
{
    private readonly ISiteRepository _repo;
    public UpdateSiteCommandHandler(ISiteRepository repo) => _repo = repo;

    public async Task<SiteDto> Handle(UpdateSiteCommand cmd, CancellationToken ct)
    {
        var site = await _repo.GetSiteAsync(cmd.SiteId, ct)
            ?? throw new InvalidOperationException("La sede no existe.");

        site.Update(cmd.Name.Trim(), cmd.Address.Trim(), cmd.Phone?.Trim());
        await _repo.SaveChangesAsync(ct);
        return new SiteDto(site.Id, site.Name, site.Address);
    }
}

/// <summary>
/// Elimina una sede. Devuelve true si se borró definitivamente (sin datos);
/// false si tenía datos y se desactivó para conservar el historial.
/// </summary>
public record DeleteSiteCommand(Guid SiteId) : IRequest<bool>;

public class DeleteSiteCommandHandler : IRequestHandler<DeleteSiteCommand, bool>
{
    private readonly ISiteRepository _repo;
    public DeleteSiteCommandHandler(ISiteRepository repo) => _repo = repo;

    public async Task<bool> Handle(DeleteSiteCommand cmd, CancellationToken ct)
    {
        var site = await _repo.GetSiteAsync(cmd.SiteId, ct)
            ?? throw new InvalidOperationException("La sede no existe.");

        if (await _repo.CountActiveSitesAsync(site.CompanyId, ct) <= 1)
            throw new InvalidOperationException("No se puede eliminar la única sede del gimnasio.");

        if (await _repo.SiteHasDataAsync(cmd.SiteId, ct))
        {
            // Tiene socios/caja/pagos: baja lógica para no romper el historial.
            site.Deactivate();
            await _repo.SaveChangesAsync(ct);
            return false;
        }

        _repo.RemoveSite(site);
        await _repo.SaveChangesAsync(ct);
        return true;
    }
}

// ── Reglas de acceso (Gatekeeper) ────────────────────────────────────────────

public record AccessSettings(decimal StopOnOweAmount, decimal WarnOnOweAmount, int AntiPassbackMinutes)
{
    public static AccessSettings? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<AccessSettings>(json); }
        catch (JsonException) { return null; }
    }
}

public record UpdateAccessSettingsCommand(
    Guid CompanyId, decimal StopOnOweAmount, decimal WarnOnOweAmount, int AntiPassbackMinutes) : IRequest;

public class UpdateAccessSettingsCommandValidator : AbstractValidator<UpdateAccessSettingsCommand>
{
    public UpdateAccessSettingsCommandValidator()
    {
        RuleFor(x => x.WarnOnOweAmount).GreaterThanOrEqualTo(0)
            .WithMessage("El monto de aviso no puede ser negativo.");
        RuleFor(x => x.StopOnOweAmount).GreaterThanOrEqualTo(x => x.WarnOnOweAmount)
            .WithMessage("El monto que bloquea debe ser mayor o igual al de aviso.");
        RuleFor(x => x.AntiPassbackMinutes).InclusiveBetween(0, 120)
            .WithMessage("El anti-passback va de 0 a 120 minutos.");
    }
}

public class UpdateAccessSettingsCommandHandler : IRequestHandler<UpdateAccessSettingsCommand>
{
    private readonly ISiteRepository _repo;
    private readonly GatekeeperConfig _gatekeeper;

    public UpdateAccessSettingsCommandHandler(ISiteRepository repo, GatekeeperConfig gatekeeper)
    {
        _repo = repo;
        _gatekeeper = gatekeeper;
    }

    public async Task Handle(UpdateAccessSettingsCommand cmd, CancellationToken ct)
    {
        var company = await _repo.GetCompanyAsync(cmd.CompanyId, ct)
            ?? throw new InvalidOperationException("La empresa no existe.");

        var settings = new AccessSettings(cmd.StopOnOweAmount, cmd.WarnOnOweAmount, cmd.AntiPassbackMinutes);
        company.SetSettings(JsonSerializer.Serialize(settings));
        await _repo.SaveChangesAsync(ct);

        // Aplica en caliente al gatekeeper en memoria.
        _gatekeeper.StopOnOweAmount = settings.StopOnOweAmount;
        _gatekeeper.WarnOnOweAmount = settings.WarnOnOweAmount;
        _gatekeeper.AntiPassbackMinutes = settings.AntiPassbackMinutes;
    }
}
