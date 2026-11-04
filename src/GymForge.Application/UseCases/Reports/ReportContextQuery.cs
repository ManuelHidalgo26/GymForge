using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using MediatR;

namespace GymForge.Application.UseCases.Reports;

/// <summary>Lo que un reporte necesita de la empresa que no son sus datos: la marca, el alcance y la zona horaria.</summary>
/// <param name="Scope">Qué abarca el reporte, para el subtítulo: el nombre de la sede tal cual ("Centro") o "Toda la empresa".</param>
/// <param name="Timezone">Zona horaria de la empresa, para mostrar en hora local lo que se guarda en UTC.</param>
public record ReportContext(ReportBranding Branding, string Scope, string? Timezone)
{
    public DateTime ToLocal(DateTime utc) => CompanyTime.ToLocal(utc, Timezone);
}

/// <summary>Marca y alcance de un reporte. SiteId null = toda la empresa (sin sede en el encabezado).</summary>
public record GetReportContextQuery(Guid CompanyId, Guid? SiteId) : IRequest<ReportContext>;

public class GetReportContextQueryHandler : IRequestHandler<GetReportContextQuery, ReportContext>
{
    private const string WholeCompany = "Toda la empresa";

    private readonly ISiteRepository _sites;
    public GetReportContextQueryHandler(ISiteRepository sites) => _sites = sites;

    public async Task<ReportContext> Handle(GetReportContextQuery query, CancellationToken ct)
    {
        var company = await _sites.GetCompanyAsync(query.CompanyId, ct)
            ?? throw new KeyNotFoundException("Empresa no encontrada.");

        var siteName = query.SiteId is { } siteId ? await SiteNameAsync(siteId, query.CompanyId, ct) : null;

        var branding = new ReportBranding(
            company.LegalName,
            company.TaxId,
            siteName,
            string.IsNullOrWhiteSpace(company.LogoUrl) ? null : company.LogoUrl,
            company.BrandColorHex);

        return new ReportContext(branding, siteName ?? WholeCompany, company.Timezone);
    }

    // GetSiteAsync no filtra por empresa: una sede ajena se trata como inexistente.
    private async Task<string> SiteNameAsync(Guid siteId, Guid companyId, CancellationToken ct)
    {
        var site = await _sites.GetSiteAsync(siteId, ct);
        return site is not null && site.CompanyId == companyId
            ? site.Name
            : throw new KeyNotFoundException("Sede no encontrada.");
    }
}
