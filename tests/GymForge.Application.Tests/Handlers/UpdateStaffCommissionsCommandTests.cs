using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Staff;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace GymForge.Application.Tests.Handlers;

public class UpdateStaffCommissionsCommandTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private readonly IStaffRepository _repo = Substitute.For<IStaffRepository>();

    private UpdateStaffCommissionsCommandHandler Handler() => new(_repo);

    private Staff HaveStaff(Guid? companyId = null)
    {
        var staff = Staff.Create(companyId ?? CompanyId, "Caro", "Paz", StaffRole.Receptionist, "hash");
        _repo.GetByIdAsync(staff.Id, Arg.Any<CancellationToken>()).Returns(staff);
        return staff;
    }

    [Fact]
    public async Task Handle_AppliesThePercentages_Saves_AndReturnsThem()
    {
        var staff = HaveStaff();

        var dto = await Handler().Handle(
            new UpdateStaffCommissionsCommand(CompanyId, staff.Id, 10m, 5.5m), CancellationToken.None);

        staff.CommissionPctMemberships.Should().Be(10m);
        staff.CommissionPctProducts.Should().Be(5.5m);
        dto.Should().Be(new StaffCommissionRatesDto(staff.Id, "Caro Paz", StaffRole.Receptionist, 10m, 5.5m));
        _repo.Received(1).Update(staff);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LeavesThePersonalTrainingPercentUntouched()
    {
        var staff = HaveStaff();
        staff.SetCommissions(1m, 25m, 1m);

        await Handler().Handle(new UpdateStaffCommissionsCommand(CompanyId, staff.Id, 10m, 5m), CancellationToken.None);

        staff.CommissionPctPt.Should().Be(25m, "PT queda fuera de v1: ni se edita ni se pisa");
    }

    [Fact]
    public async Task Handle_StaffOfAnotherCompany_Throws_AndChangesNothing()
    {
        var foreign = HaveStaff(companyId: Guid.NewGuid());

        var act = () => Handler().Handle(
            new UpdateStaffCommissionsCommand(CompanyId, foreign.Id, 10m, 10m), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        foreign.CommissionPctMemberships.Should().Be(0m);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownStaff_Throws()
    {
        var missing = Guid.NewGuid();
        _repo.GetByIdAsync(missing, Arg.Any<CancellationToken>()).ReturnsNull();

        var act = () => Handler().Handle(
            new UpdateStaffCommissionsCommand(CompanyId, missing, 10m, 10m), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(100, 100, true)]
    [InlineData(12.5, 7.25, true)]
    [InlineData(-1, 0, false)]
    [InlineData(0, 100.01, false)]
    [InlineData(101, 0, false)]
    public void Validator_AcceptsOnlyPercentagesFromZeroToHundred(double memberships, double products, bool valid) =>
        new UpdateStaffCommissionsCommandValidator()
            .Validate(new UpdateStaffCommissionsCommand(CompanyId, Guid.NewGuid(), (decimal)memberships, (decimal)products))
            .IsValid.Should().Be(valid);

    [Fact]
    public async Task Query_ListsTheActiveStaffOfTheCompany_WithTheirRates_OrderedByName()
    {
        var zoe = Staff.Create(CompanyId, "Zoe", "Alva", StaffRole.Trainer, "hash");
        var ana = Staff.Create(CompanyId, "Ana", "Paz", StaffRole.Admin, "hash");
        ana.SetCommissions(8m, 3m, 2m);
        _repo.GetActiveByCompanyAsync(CompanyId, Arg.Any<CancellationToken>()).Returns(new List<Staff> { zoe, ana });

        var rows = await new GetStaffCommissionRatesQueryHandler(_repo)
            .Handle(new GetStaffCommissionRatesQuery(CompanyId), CancellationToken.None);

        rows.Select(r => r.FullName).Should().Equal("Ana Paz", "Zoe Alva");
        rows[0].PctMemberships.Should().Be(8m);
        rows[0].PctProducts.Should().Be(2m);
    }
}
