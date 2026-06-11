using FluentValidation;
using GymForge.Application.Interfaces;
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

public record SiteDto(Guid Id, string Name, string Address);

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
