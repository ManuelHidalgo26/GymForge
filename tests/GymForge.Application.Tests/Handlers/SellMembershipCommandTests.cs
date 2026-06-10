using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Sales;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class SellMembershipCommandTests
{
    private readonly IMembershipTypeRepository _planRepo = Substitute.For<IMembershipTypeRepository>();
    private readonly IChargeRepository _chargeRepo = Substitute.For<IChargeRepository>();
    private readonly IMembershipRepository _membershipRepo = Substitute.For<IMembershipRepository>();
    private readonly IMemberRepository _memberRepo = Substitute.For<IMemberRepository>();
    private readonly IPaymentRepository _paymentRepo = Substitute.For<IPaymentRepository>();
    private readonly ICashRegister _cashRegister = Substitute.For<ICashRegister>();

    private SellMembershipCommandHandler Sut() =>
        new(_planRepo, _chargeRepo, _membershipRepo, _memberRepo, _paymentRepo, _cashRegister);

    private static Member MakeProspect(Guid companyId, Guid siteId) =>
        Member.Create(companyId, siteId, "Juan", "Pérez", DocumentType.DNI, "30111222", Gender.Male);

    [Fact]
    public async Task Handle_CreatesChargeMembershipPaymentAndPostsCash_AndActivatesMember()
    {
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();
        var plan = MembershipType.Create(companyId, "Mensual", MembershipBasis.Renewal, 35_000m, 1, "Month");
        var member = MakeProspect(companyId, siteId);

        _planRepo.GetByIdAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
        _memberRepo.GetByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);

        var dto = await Sut().Handle(new SellMembershipCommand(
            companyId, siteId, Guid.NewGuid(), shiftId, member.Id, plan.Id, PaymentMethod.Cash),
            CancellationToken.None);

        dto.Amount.Should().Be(35_000m);
        dto.Allocations.Should().ContainSingle();

        // El socio deja de ser prospecto al comprar su membresía.
        member.Status.Should().Be(MemberStatus.Active);
        member.JoinDate.Should().NotBeNull();
        _memberRepo.Received(1).Update(member);

        await _chargeRepo.Received(1).AddAsync(
            Arg.Is<Charge>(c => c.ConceptType == ConceptType.MembershipFee && c.Status == ChargeStatus.Paid),
            Arg.Any<CancellationToken>());
        await _membershipRepo.Received(1).AddAsync(
            Arg.Is<Membership>(m => m.MemberId == member.Id && m.Status == MembershipStatus.Active),
            Arg.Any<CancellationToken>());
        await _paymentRepo.Received(1).AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>());
        await _cashRegister.Received(1).PostIfCashAsync(
            PaymentMethod.Cash, shiftId, CashMovementCategory.Membership, 35_000m,
            Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyActiveMember_DoesNotResetJoinDate()
    {
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var plan = MembershipType.Create(companyId, "Mensual", MembershipBasis.Renewal, 35_000m, 1, "Month");
        var member = MakeProspect(companyId, siteId);
        var originalJoin = new DateOnly(2024, 1, 10);
        member.Activate(originalJoin);

        _planRepo.GetByIdAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
        _memberRepo.GetByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);

        await Sut().Handle(new SellMembershipCommand(
            companyId, siteId, Guid.NewGuid(), null, member.Id, plan.Id, PaymentMethod.Cash),
            CancellationToken.None);

        member.JoinDate.Should().Be(originalJoin);
        _memberRepo.DidNotReceive().Update(Arg.Any<Member>());
    }

    [Fact]
    public async Task Handle_FreePlan_Throws()
    {
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var plan = MembershipType.Create(companyId, "Trial", MembershipBasis.Trial, 0m, 7, "Day");
        var member = MakeProspect(companyId, siteId);
        _planRepo.GetByIdAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
        _memberRepo.GetByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);

        var act = () => Sut().Handle(new SellMembershipCommand(
            companyId, siteId, Guid.NewGuid(), null, member.Id, plan.Id, PaymentMethod.Cash),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void Validator_NoMember_Fails() =>
        new SellMembershipCommandValidator().Validate(new SellMembershipCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Guid.Empty, Guid.NewGuid(), PaymentMethod.Cash))
            .IsValid.Should().BeFalse();
}
