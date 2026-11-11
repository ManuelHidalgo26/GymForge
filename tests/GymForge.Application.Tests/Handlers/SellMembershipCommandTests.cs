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
    private readonly IStaffRepository _staffRepo = Substitute.For<IStaffRepository>();
    private readonly ICompanyCalendar _calendar = Substitute.For<ICompanyCalendar>();
    private static readonly DateOnly Today = new(2026, 7, 21);

    public SellMembershipCommandTests() =>
        _calendar.TodayAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Today);

    private SellMembershipCommandHandler Sut() =>
        new(_planRepo, _chargeRepo, _membershipRepo, _memberRepo, _paymentRepo, _cashRegister, _staffRepo, _calendar,
            new ArrearsRelief(_memberRepo, _chargeRepo, _calendar));

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
    public async Task Handle_LaCuotaQuedaVinculadaALaMembresiaVendida_ParaQueLosReportesLaAtribuyanAlPlan()
    {
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var plan = MembershipType.Create(companyId, "Mensual", MembershipBasis.Renewal, 35_000m, 1, "Month");
        var member = MakeProspect(companyId, siteId);
        _planRepo.GetByIdAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
        _memberRepo.GetByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);
        Charge? charge = null;
        Membership? membership = null;
        await _chargeRepo.AddAsync(Arg.Do<Charge>(c => charge = c), Arg.Any<CancellationToken>());
        await _membershipRepo.AddAsync(Arg.Do<Membership>(m => membership = m), Arg.Any<CancellationToken>());

        await Sut().Handle(new SellMembershipCommand(
            companyId, siteId, Guid.NewGuid(), null, member.Id, plan.Id, PaymentMethod.Cash),
            CancellationToken.None);

        membership.Should().NotBeNull();
        charge.Should().NotBeNull();
        charge!.MembershipId.Should().Be(membership!.Id);
    }

    [Fact]
    public async Task Handle_VisitPack_LaMembresiaVendidaTraeLasVisitasDelPlan()
    {
        var membership = await SellCapturingMembershipAsync(MembershipBasis.VisitPack, visits: 10);

        membership.VisitsRemaining.Should().Be(10);
    }

    [Fact]
    public async Task Handle_PlanPorTiempo_LaMembresiaVendidaNoTieneVisitas()
    {
        var membership = await SellCapturingMembershipAsync(MembershipBasis.Renewal, visits: null);

        membership.VisitsRemaining.Should().BeNull();
    }

    private async Task<Membership> SellCapturingMembershipAsync(MembershipBasis basis, int? visits)
    {
        var companyId = Guid.NewGuid();
        var plan = MembershipType.Create(companyId, "Plan", basis, 10_000m, visitsIncluded: visits);
        var member = MakeProspect(companyId, Guid.NewGuid());
        _planRepo.GetByIdAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
        _memberRepo.GetByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);
        Membership? sold = null;
        await _membershipRepo.AddAsync(Arg.Do<Membership>(m => sold = m), Arg.Any<CancellationToken>());

        await Sut().Handle(new SellMembershipCommand(
            companyId, Guid.NewGuid(), Guid.NewGuid(), null, member.Id, plan.Id, PaymentMethod.Cash),
            CancellationToken.None);

        return sold!;
    }

    [Fact]
    public async Task Handle_LasFechasSalenDelDiaLocalDeLaEmpresa()
    {
        var companyId = Guid.NewGuid();
        var plan = MembershipType.Create(companyId, "Mensual", MembershipBasis.Renewal, 35_000m, 1, "Month");
        var member = MakeProspect(companyId, Guid.NewGuid());
        _planRepo.GetByIdAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
        _memberRepo.GetByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);
        Charge? charge = null;
        Membership? membership = null;
        await _chargeRepo.AddAsync(Arg.Do<Charge>(c => charge = c), Arg.Any<CancellationToken>());
        await _membershipRepo.AddAsync(Arg.Do<Membership>(m => membership = m), Arg.Any<CancellationToken>());

        await Sut().Handle(new SellMembershipCommand(
            companyId, Guid.NewGuid(), Guid.NewGuid(), null, member.Id, plan.Id, PaymentMethod.Cash),
            CancellationToken.None);

        membership!.StartDate.Should().Be(Today);
        charge!.DueDate.Should().Be(Today);
        member.JoinDate.Should().Be(Today);
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

    // ── Estado del socio según de dónde venga ─────────────────────────────────

    private async Task<Member> SellTo(Member member, params Charge[] pendingCharges)
    {
        var plan = MembershipType.Create(member.CompanyId, "Mensual", MembershipBasis.Renewal, 35_000m, 1, "Month");
        _planRepo.GetByIdAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
        _memberRepo.GetByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);
        _chargeRepo.GetPendingAsync(member.Id, Arg.Any<CancellationToken>()).Returns(pendingCharges);
        member.ClearDomainEvents();

        await Sut().Handle(new SellMembershipCommand(
            member.CompanyId, member.SiteId, Guid.NewGuid(), null, member.Id, plan.Id, PaymentMethod.Cash),
            CancellationToken.None);
        return member;
    }

    private static Member MakeOverdueMember()
    {
        var member = MakeProspect(Guid.NewGuid(), Guid.NewGuid());
        member.Activate(new DateOnly(2024, 1, 10));
        member.MarkOverdue();
        return member;
    }

    private static Charge OverdueCharge(Member member) =>
        Charge.Create(member.CompanyId, member.SiteId, member.Id, null, ConceptType.MembershipFee,
            "Cuota vieja", 10_000m, Today.AddDays(-30));

    [Fact]
    public async Task Handle_OverdueMemberWithOtherDebts_StaysOverdueAndEmitsNoActivationEvent()
    {
        var member = MakeOverdueMember();

        await SellTo(member, OverdueCharge(member));

        member.Status.Should().Be(MemberStatus.Overdue);
        member.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_OverdueMemberWithoutOtherDebts_LeavesArrearsWithoutActivationEvent()
    {
        var member = MakeOverdueMember();

        await SellTo(member);

        member.Status.Should().Be(MemberStatus.Active);
        member.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_FrozenMember_KeepsItsStatus()
    {
        var member = MakeProspect(Guid.NewGuid(), Guid.NewGuid());
        member.Activate(new DateOnly(2024, 1, 10));
        member.Freeze();

        await SellTo(member);

        member.Status.Should().Be(MemberStatus.Frozen);
        member.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SuspendedMember_KeepsItsStatus()
    {
        var member = MakeProspect(Guid.NewGuid(), Guid.NewGuid());
        member.Suspend();

        await SellTo(member);

        member.Status.Should().Be(MemberStatus.Suspended);
        member.DomainEvents.Should().BeEmpty();
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

    // ── Comisión del vendedor (snapshot al vender) ────────────────────────────

    private async Task<(Membership Sold, Guid CashierId)> SellWithSellerAsync(
        decimal planPrice, decimal? pctMemberships, Guid companyId, Guid? sellerCompanyId = null)
    {
        var siteId = Guid.NewGuid();
        var plan = MembershipType.Create(companyId, "Mensual", MembershipBasis.Renewal, planPrice, 1, "Month");
        var member = MakeProspect(companyId, siteId);
        _planRepo.GetByIdAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
        _memberRepo.GetByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);

        var cashier = Staff.Create(sellerCompanyId ?? companyId, "Caro", "Vendedora", StaffRole.Receptionist, "hash");
        if (pctMemberships is { } pct) cashier.SetCommissions(pct, 0m, 0m);
        _staffRepo.GetByIdAsync(cashier.Id, Arg.Any<CancellationToken>()).Returns(cashier);

        Membership? sold = null;
        await _membershipRepo.AddAsync(Arg.Do<Membership>(m => sold = m), Arg.Any<CancellationToken>());

        await Sut().Handle(new SellMembershipCommand(
            companyId, siteId, cashier.Id, null, member.Id, plan.Id, PaymentMethod.Cash),
            CancellationToken.None);
        return (sold!, cashier.Id);
    }

    [Fact]
    public async Task Handle_FreezesTheCommissionOfTheSellerOverThePlanPrice()
    {
        var (sold, cashierId) = await SellWithSellerAsync(35_000m, pctMemberships: 10m, Guid.NewGuid());

        sold.CommissionAmount.Should().Be(3_500m);
        sold.SoldByStaffId.Should().Be(cashierId);
    }

    [Fact]
    public async Task Handle_RoundsTheCommissionToCents()
    {
        // $12.345 * 7,5% = 925,875 -> 925,88
        var (sold, _) = await SellWithSellerAsync(12_345m, pctMemberships: 7.5m, Guid.NewGuid());

        sold.CommissionAmount.Should().Be(925.88m);
    }

    [Fact]
    public async Task Handle_SellerWithoutPercentage_EarnsNothing_ButStaysAsSeller()
    {
        var (sold, cashierId) = await SellWithSellerAsync(35_000m, pctMemberships: null, Guid.NewGuid());

        sold.CommissionAmount.Should().Be(0m);
        sold.SoldByStaffId.Should().Be(cashierId);
    }

    [Fact]
    public async Task Handle_SellerOfAnotherCompany_EarnsNothing_AndTheSaleStillGoesThrough()
    {
        var (sold, _) = await SellWithSellerAsync(
            35_000m, pctMemberships: 10m, Guid.NewGuid(), sellerCompanyId: Guid.NewGuid());

        sold.CommissionAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_UnknownSeller_EarnsNothing_AndTheSaleStillGoesThrough()
    {
        var companyId = Guid.NewGuid();
        var plan = MembershipType.Create(companyId, "Mensual", MembershipBasis.Renewal, 35_000m, 1, "Month");
        var member = MakeProspect(companyId, Guid.NewGuid());
        _planRepo.GetByIdAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
        _memberRepo.GetByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);

        var dto = await Sut().Handle(new SellMembershipCommand(
            companyId, member.SiteId, Guid.NewGuid(), null, member.Id, plan.Id, PaymentMethod.Cash),
            CancellationToken.None);

        dto.Amount.Should().Be(35_000m);
        await _membershipRepo.Received(1).AddAsync(
            Arg.Is<Membership>(m => m.CommissionAmount == 0m), Arg.Any<CancellationToken>());
    }
}
