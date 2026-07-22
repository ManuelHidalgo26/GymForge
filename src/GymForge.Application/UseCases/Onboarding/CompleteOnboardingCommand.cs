using FluentValidation;
using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Domain.ValueObjects;
using MediatR;

namespace GymForge.Application.UseCases.Onboarding;

/// <summary>
/// Primer arranque: crea el gimnasio real (empresa + primera sede + usuario admin con
/// PIN) sobre una base limpia, en lugar del gimnasio de demostración. Opcionalmente
/// carga planes de ejemplo para poder vender desde el día uno. Devuelve el CompanyId.
/// </summary>
public record CompleteOnboardingCommand(
    string GymName,
    string TaxId,
    string SiteName,
    string SiteAddress,
    string AdminFirstName,
    string AdminLastName,
    string AdminPin,
    string BrandColorHex,
    bool CreateSamplePlans) : IRequest<Guid>;

public class CompleteOnboardingCommandValidator : AbstractValidator<CompleteOnboardingCommand>
{
    public CompleteOnboardingCommandValidator()
    {
        RuleFor(x => x.GymName).NotEmpty().MaximumLength(200)
            .WithMessage("El nombre del gimnasio es obligatorio.");
        RuleFor(x => x.TaxId).NotEmpty().WithMessage("El CUIT es obligatorio.");
        RuleFor(x => x.SiteName).NotEmpty().MaximumLength(100)
            .WithMessage("El nombre de la sede es obligatorio.");
        RuleFor(x => x.SiteAddress).NotEmpty().MaximumLength(200)
            .WithMessage("La dirección de la sede es obligatoria.");
        RuleFor(x => x.AdminFirstName).NotEmpty().WithMessage("El nombre del responsable es obligatorio.");
        RuleFor(x => x.AdminLastName).NotEmpty().WithMessage("El apellido del responsable es obligatorio.");
        RuleFor(x => x.AdminPin).Matches("^[0-9]{4,8}$")
            .WithMessage("El PIN debe tener entre 4 y 8 dígitos.");
        RuleFor(x => x.BrandColorHex).Matches("^#(?:[0-9a-fA-F]{6})$")
            .WithMessage("El color debe ser un hex de 6 dígitos.");
    }
}

public class CompleteOnboardingCommandHandler : IRequestHandler<CompleteOnboardingCommand, Guid>
{
    private readonly ISiteRepository _sites;
    private readonly IMembershipTypeRepository _plans;
    private readonly IPinHasher _pinHasher;

    public CompleteOnboardingCommandHandler(
        ISiteRepository sites, IMembershipTypeRepository plans, IPinHasher pinHasher)
    {
        _sites = sites;
        _plans = plans;
        _pinHasher = pinHasher;
    }

    public async Task<Guid> Handle(CompleteOnboardingCommand cmd, CancellationToken ct)
    {
        // Valida el dígito verificador y normaliza el formato XX-XXXXXXXX-X.
        var cuit = new Cuit(cmd.TaxId);

        var company = Company.Create(cmd.GymName.Trim(), cuit);
        company.UpdateBranding(null, cmd.BrandColorHex.Trim().ToUpperInvariant());

        var site = Site.Create(company.Id, cmd.SiteName.Trim(), cmd.SiteAddress.Trim());
        var admin = Domain.Entities.Staff.Create(
            company.Id, cmd.AdminFirstName.Trim(), cmd.AdminLastName.Trim(),
            StaffRole.Admin, _pinHasher.Hash(cmd.AdminPin));

        // Grafo nuevo y no trackeado: al agregar la empresa raíz, EF inserta sede y staff.
        company.Sites.Add(site);
        company.Staff.Add(admin);
        await _sites.AddCompanyAsync(company, ct);

        if (cmd.CreateSamplePlans)
        {
            await _plans.AddAsync(
                MembershipType.Create(company.Id, "Mensual", MembershipBasis.Renewal, 35_000m, 1, "Month"), ct);
            await _plans.AddAsync(
                MembershipType.Create(company.Id, "Trimestral", MembershipBasis.Renewal, 90_000m, 3, "Month"), ct);
            await _plans.AddAsync(
                MembershipType.Create(company.Id, "Anual", MembershipBasis.Renewal, 320_000m, 12, "Month"), ct);
        }

        // Mismo DbContext (scope): persiste empresa + sede + staff + planes juntos.
        await _sites.SaveChangesAsync(ct);
        return company.Id;
    }
}
