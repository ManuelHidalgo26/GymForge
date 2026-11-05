using FluentValidation;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Application.UseCases.Members;

/// <summary>
/// Corrige los datos de un socio desde su ficha: identidad, contacto, nacimiento, consentimiento, contacto de
/// emergencia y datos médicos. No toca estado, sede, fecha de alta, credencial ni foto (tienen su propio camino).
/// Los datos médicos son datos de salud: el AuditLog registra que cambiaron, nunca su valor.
/// </summary>
public record UpdateMemberCommand(
    Guid CompanyId,
    Guid MemberId,
    string FirstName,
    string LastName,
    DocumentType DocumentType,
    string DocumentNumber,
    Gender Gender,
    string? Email,
    string? Mobile,
    DateOnly? BirthDate,
    bool MarketingConsent,
    string? EmergencyName,
    string? EmergencyPhone,
    string? EmergencyRelation,
    string? MedicalConditions,
    string? Medications,
    string? Allergies,
    BloodType BloodType) : IRequest<MemberDto>;

public class UpdateMemberCommandValidator : AbstractValidator<UpdateMemberCommand>
{
    private const int EmergencyRelationMaxLength = 50;
    private const int HealthTextMaxLength = 1000;

    public UpdateMemberCommandValidator()
    {
        RuleFor(x => x.FirstName).ValidName();
        RuleFor(x => x.LastName).ValidName();
        RuleFor(x => x.DocumentNumber).ValidDocumentNumber();
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Mobile).ValidPhone();
        RuleFor(x => x.BirthDate).ValidBirthDate();
        RuleFor(x => x.EmergencyName).MaximumLength(100);
        RuleFor(x => x.EmergencyPhone).ValidPhone();
        RuleFor(x => x.EmergencyRelation).MaximumLength(EmergencyRelationMaxLength);
        RuleFor(x => x.MedicalConditions).MaximumLength(HealthTextMaxLength);
        RuleFor(x => x.Medications).MaximumLength(HealthTextMaxLength);
        RuleFor(x => x.Allergies).MaximumLength(HealthTextMaxLength);
        RuleFor(x => x.BloodType).IsInEnum();
        RuleFor(x => x.DocumentType).IsInEnum();
        RuleFor(x => x.Gender).IsInEnum();
    }
}

public class UpdateMemberCommandHandler : IRequestHandler<UpdateMemberCommand, MemberDto>
{
    private readonly IMemberRepository _repo;
    public UpdateMemberCommandHandler(IMemberRepository repo) => _repo = repo;

    public async Task<MemberDto> Handle(UpdateMemberCommand cmd, CancellationToken ct)
    {
        var member = await _repo.GetByIdAsync(cmd.MemberId, ct);
        if (member is null || member.CompanyId != cmd.CompanyId)
            throw new KeyNotFoundException("El socio no existe.");

        member.EditIdentity(
            cmd.FirstName.Trim(), cmd.LastName.Trim(), cmd.DocumentType, cmd.DocumentNumber.Trim(), cmd.Gender);
        member.UpdateContact(Clean(cmd.Email), Clean(cmd.Mobile));
        member.CompleteProfile(cmd.BirthDate, member.Source, member.SalesRepId, cmd.MarketingConsent);
        member.UpdateEmergencyContact(Clean(cmd.EmergencyName), Clean(cmd.EmergencyPhone), Clean(cmd.EmergencyRelation));
        member.UpdateMedicalInfo(Clean(cmd.MedicalConditions), Clean(cmd.Medications), Clean(cmd.Allergies), cmd.BloodType);

        _repo.Update(member);
        await _repo.SaveChangesAsync(ct);

        return MemberDto.FromEntity(member);
    }

    /// <summary>Un campo opcional vacío se guarda como null, no como cadena vacía.</summary>
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
