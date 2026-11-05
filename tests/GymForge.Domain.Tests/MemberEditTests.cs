using FluentAssertions;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;

namespace GymForge.Domain.Tests;

public class MemberEditTests
{
    private static Member NewMember() =>
        Member.Create(Guid.NewGuid(), Guid.NewGuid(), "Ana", "García", DocumentType.DNI, "30123456", Gender.Female);

    [Fact]
    public void EditIdentity_ChangesIdentity_AndKeepsStatusAndSite()
    {
        var member = NewMember();
        member.Activate(new DateOnly(2026, 1, 1));
        var siteId = member.SiteId;

        member.EditIdentity("Anita", "Pérez", DocumentType.PASS, "X999", Gender.NonBinary);

        member.FullName.Should().Be("Anita Pérez");
        member.DocumentType.Should().Be(DocumentType.PASS);
        member.DocumentNumber.Should().Be("X999");
        member.Gender.Should().Be(Gender.NonBinary);
        member.Status.Should().Be(MemberStatus.Active);
        member.SiteId.Should().Be(siteId);
    }

    [Theory]
    [InlineData("", "García", "30123456")]
    [InlineData("Ana", " ", "30123456")]
    [InlineData("Ana", "García", "")]
    public void EditIdentity_RejectsBlankRequiredFields_AndChangesNothing(string first, string last, string document)
    {
        var member = NewMember();

        var act = () => member.EditIdentity(first, last, DocumentType.CE, document, Gender.Male);

        act.Should().Throw<ArgumentException>();
        member.FirstName.Should().Be("Ana");
        member.DocumentType.Should().Be(DocumentType.DNI);
    }

    [Fact]
    public void UpdateMedicalInfo_SetsTheHealthData()
    {
        var member = NewMember();

        member.UpdateMedicalInfo("Asma", "Salbutamol", null, BloodType.ABNegative);

        member.MedicalConditions.Should().Be("Asma");
        member.Medications.Should().Be("Salbutamol");
        member.Allergies.Should().BeNull();
        member.BloodType.Should().Be(BloodType.ABNegative);
    }
}
