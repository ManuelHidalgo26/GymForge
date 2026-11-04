using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Members;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace GymForge.Application.Tests.Handlers;

public class UpdateMemberCommandTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid SiteId = Guid.NewGuid();

    private readonly IMemberRepository _repo = Substitute.For<IMemberRepository>();

    private UpdateMemberCommandHandler Handler() => new(_repo);

    private Member HaveMember(Guid? companyId = null)
    {
        var member = Member.Create(companyId ?? CompanyId, SiteId, "Ana", "García",
            DocumentType.DNI, "30123456", Gender.Female);
        member.Activate(new DateOnly(2026, 1, 10));
        _repo.GetByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);
        return member;
    }

    private static UpdateMemberCommand Command(Guid memberId, Guid? companyId = null) => new(
        companyId ?? CompanyId, memberId, "Ana María", "García López", DocumentType.PASS, "AB123456", Gender.NonBinary,
        "ana@test.com", "+5491155550000", new DateOnly(1990, 5, 20), MarketingConsent: true,
        "Luis García", "+5491166660000", "Hermano",
        "Asma", "Salbutamol", "Penicilina", BloodType.OPositive);

    [Fact]
    public async Task Handle_AppliesEveryEditableField_Saves_AndReturnsTheUpdatedDto()
    {
        var ana = HaveMember();

        var dto = await Handler().Handle(Command(ana.Id), CancellationToken.None);

        dto.FirstName.Should().Be("Ana María");
        dto.LastName.Should().Be("García López");
        dto.DocumentType.Should().Be(DocumentType.PASS);
        dto.DocumentNumber.Should().Be("AB123456");
        dto.Gender.Should().Be(Gender.NonBinary);
        dto.Email.Should().Be("ana@test.com");
        dto.Mobile.Should().Be("+5491155550000");
        dto.BirthDate.Should().Be(new DateOnly(1990, 5, 20));
        dto.MarketingConsent.Should().BeTrue();
        dto.EmergencyName.Should().Be("Luis García");
        dto.EmergencyPhone.Should().Be("+5491166660000");
        dto.EmergencyRelation.Should().Be("Hermano");
        dto.MedicalConditions.Should().Be("Asma");
        dto.Medications.Should().Be("Salbutamol");
        dto.Allergies.Should().Be("Penicilina");
        dto.BloodType.Should().Be(BloodType.OPositive);
        _repo.Received(1).Update(ana);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DoesNotChangeStatusJoinDateSiteOrCredential()
    {
        var ana = HaveMember();
        ana.EnrollTag("TAG-1");

        await Handler().Handle(Command(ana.Id), CancellationToken.None);

        ana.Status.Should().Be(MemberStatus.Active);
        ana.JoinDate.Should().Be(new DateOnly(2026, 1, 10));
        ana.SiteId.Should().Be(SiteId);
        ana.TagSerial.Should().Be("TAG-1");
    }

    [Fact]
    public async Task Handle_KeepsTheCommercialProfile_WhileUpdatingTheConsent()
    {
        var ana = HaveMember();
        var rep = Guid.NewGuid();
        ana.CompleteProfile(null, MemberSource.Referral, rep, marketingConsent: false);

        await Handler().Handle(Command(ana.Id), CancellationToken.None);

        ana.Source.Should().Be(MemberSource.Referral);
        ana.SalesRepId.Should().Be(rep);
        ana.MarketingConsent.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_BlankOptionalFields_AreStoredAsNull_AndTextIsTrimmed()
    {
        var ana = HaveMember();
        var cmd = Command(ana.Id) with
        {
            FirstName = "  Ana ", Email = "  ", Mobile = "", EmergencyName = " ", MedicalConditions = "  ",
            Allergies = " Polen "
        };

        await Handler().Handle(cmd, CancellationToken.None);

        ana.FirstName.Should().Be("Ana");
        ana.Email.Should().BeNull();
        ana.Mobile.Should().BeNull();
        ana.EmergencyName.Should().BeNull();
        ana.MedicalConditions.Should().BeNull();
        ana.Allergies.Should().Be("Polen");
    }

    [Fact]
    public async Task Handle_UnknownMember_ThrowsNotFound()
    {
        var missing = Guid.NewGuid();
        _repo.GetByIdAsync(missing, Arg.Any<CancellationToken>()).ReturnsNull();

        var act = () => Handler().Handle(Command(missing), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_MemberOfAnotherCompany_IsTreatedAsNotFound_AndNothingChangesOrIsSaved()
    {
        var foreign = HaveMember(companyId: Guid.NewGuid());

        var act = () => Handler().Handle(Command(foreign.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        foreign.FirstName.Should().Be("Ana");
        foreign.DocumentNumber.Should().Be("30123456");
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── Validación ────────────────────────────────────────────────────────────

    private static bool IsValid(Func<UpdateMemberCommand, UpdateMemberCommand> change) =>
        new UpdateMemberCommandValidator().Validate(change(Command(Guid.NewGuid()))).IsValid;

    [Fact]
    public void Validator_AcceptsAFullyFilledCommand() => IsValid(c => c).Should().BeTrue();

    [Fact]
    public void Validator_AcceptsOptionalFieldsEmpty() =>
        IsValid(c => c with
        {
            Email = null, Mobile = null, BirthDate = null, EmergencyName = null, EmergencyPhone = null,
            EmergencyRelation = null, MedicalConditions = null, Medications = null, Allergies = null
        }).Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validator_RejectsBlankNamesAndDocument(string blank)
    {
        IsValid(c => c with { FirstName = blank }).Should().BeFalse();
        IsValid(c => c with { LastName = blank }).Should().BeFalse();
        IsValid(c => c with { DocumentNumber = blank }).Should().BeFalse();
    }

    [Fact]
    public void Validator_RejectsAnInvalidEmail() => IsValid(c => c with { Email = "no-es-un-mail" }).Should().BeFalse();

    [Fact]
    public void Validator_RejectsAFutureBirthDate() =>
        IsValid(c => c with { BirthDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1)) }).Should().BeFalse();

    [Fact]
    public void Validator_RejectsTextsLongerThanTheirLimit()
    {
        IsValid(c => c with { FirstName = new string('a', 101) }).Should().BeFalse();
        IsValid(c => c with { DocumentNumber = new string('1', 21) }).Should().BeFalse();
        IsValid(c => c with { Mobile = new string('1', 31) }).Should().BeFalse();
        IsValid(c => c with { EmergencyPhone = new string('1', 31) }).Should().BeFalse();
        IsValid(c => c with { MedicalConditions = new string('a', 1001) }).Should().BeFalse();
        IsValid(c => c with { Allergies = new string('a', 1001) }).Should().BeFalse();
    }

    [Fact]
    public void Validator_RejectsValuesOutsideTheEnums()
    {
        IsValid(c => c with { BloodType = (BloodType)99 }).Should().BeFalse();
        IsValid(c => c with { DocumentType = (DocumentType)99 }).Should().BeFalse();
        IsValid(c => c with { Gender = (Gender)99 }).Should().BeFalse();
    }
}
