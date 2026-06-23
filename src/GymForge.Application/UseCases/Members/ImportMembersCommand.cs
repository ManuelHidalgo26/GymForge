using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Licensing;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Application.UseCases.Members;

public sealed record ImportMembersResult(int Imported, int Skipped, IReadOnlyList<string> Errors)
{
    public int Total => Imported + Skipped;
}

/// <summary>
/// Alta masiva de socios (migración del padrón). Importa fila por fila: omite
/// duplicados por DNI (en la base y dentro del mismo archivo) y filas inválidas
/// sin abortar el resto, y respeta el límite de socios de la licencia. Los socios
/// importados quedan activos con la fecha de alta de hoy.
/// </summary>
public record ImportMembersCommand(
    Guid CompanyId, Guid SiteId, IReadOnlyList<ImportMemberRow> Rows) : IRequest<ImportMembersResult>;

public class ImportMembersCommandHandler : IRequestHandler<ImportMembersCommand, ImportMembersResult>
{
    private readonly IMemberRepository _repo;
    private readonly CurrentLicense _license;

    public ImportMembersCommandHandler(IMemberRepository repo, CurrentLicense license)
    {
        _repo = repo;
        _license = license;
    }

    public async Task<ImportMembersResult> Handle(ImportMembersCommand cmd, CancellationToken ct)
    {
        int imported = 0, skipped = 0;
        var errors = new List<string>();
        var seenDocs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var existing = await _repo.CountByCompanyAsync(cmd.CompanyId, ct);
        var max = _license.State.MaxMembers;
        var today = DateOnly.FromDateTime(DateTime.Today);

        foreach (var row in cmd.Rows)
        {
            ct.ThrowIfCancellationRequested();
            var doc = row.DocumentNumber.Trim();

            if (!seenDocs.Add(doc))
            {
                skipped++;
                errors.Add($"DNI {doc}: repetido en el archivo — se omite.");
                continue;
            }

            if (existing + imported >= max)
            {
                skipped++;
                errors.Add($"DNI {doc}: se alcanzó el límite de {max} socios del plan {_license.State.Tier} — se omite.");
                continue;
            }

            if (await _repo.FindByDocumentAsync(DocumentType.DNI, doc, cmd.CompanyId, ct) is not null)
            {
                skipped++;
                errors.Add($"DNI {doc}: ya existe un socio con ese documento — se omite.");
                continue;
            }

            var member = Member.Create(
                cmd.CompanyId, cmd.SiteId,
                row.FirstName.Trim(), row.LastName.Trim(),
                DocumentType.DNI, doc, row.Gender);

            member.UpdateContact(row.Email?.Trim(), row.Mobile?.Trim());
            member.CompleteProfile(row.BirthDate, MemberSource.Other, null, false);
            member.Activate(today);

            await _repo.AddAsync(member, ct);
            imported++;
        }

        if (imported > 0)
            await _repo.SaveChangesAsync(ct);

        return new ImportMembersResult(imported, skipped, errors);
    }
}
