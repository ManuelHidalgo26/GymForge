using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Charges;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class BuildReceiptQueryHandlerTests
{
    private readonly IPaymentRepository _paymentRepo = Substitute.For<IPaymentRepository>();
    private readonly IChargeRepository _chargeRepo = Substitute.For<IChargeRepository>();
    private readonly IMemberRepository _memberRepo = Substitute.For<IMemberRepository>();
    private readonly ISiteRepository _siteRepo = Substitute.For<ISiteRepository>();
    private readonly IStaffRepository _staffRepo = Substitute.For<IStaffRepository>();
    private readonly ISaleRepository _saleRepo = Substitute.For<ISaleRepository>();

    private BuildReceiptQueryHandler Sut() =>
        new(_paymentRepo, _chargeRepo, _memberRepo, _siteRepo, _staffRepo, _saleRepo);

    private (Company Company, Site Site, Member Member, Staff Staff) SeedTenant()
    {
        var company = Company.Create("Iron Temple SRL", "30-71234567-8");
        var site = Site.Create(company.Id, "Centro", "Av. Siempre Viva 123");
        var member = Member.Create(company.Id, site.Id, "Juan", "Pérez",
            DocumentType.DNI, "30111222", Gender.Male);
        var staff = Staff.Create(company.Id, "Ana", "Admin", StaffRole.Admin, "hash");

        _siteRepo.GetCompanyAsync(company.Id, Arg.Any<CancellationToken>()).Returns(company);
        _siteRepo.GetSiteAsync(site.Id, Arg.Any<CancellationToken>()).Returns(site);
        _memberRepo.GetByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);
        _staffRepo.GetByIdAsync(staff.Id, Arg.Any<CancellationToken>()).Returns(staff);

        return (company, site, member, staff);
    }

    [Fact]
    public async Task Handle_ArmaElReciboConItemsYDatosDelGimnasio()
    {
        var (company, site, member, staff) = SeedTenant();

        var charge = Charge.Create(company.Id, site.Id, member.Id, null,
            ConceptType.MembershipFee, "Cuota mensual Full", 35_000m, new DateOnly(2026, 6, 1));
        _chargeRepo.GetByIdAsync(charge.Id, Arg.Any<CancellationToken>()).Returns(charge);

        var payment = Payment.Create(company.Id, site.Id, member.Id, staff.Id,
            35_000m, PaymentMethod.Cash);
        payment.Allocations.Add(PaymentAllocation.Create(payment.Id, charge.Id, 35_000m));
        _paymentRepo.GetByIdAsync(payment.Id, Arg.Any<CancellationToken>()).Returns(payment);

        var receipt = await Sut().Handle(
            new BuildReceiptQuery(payment.Id, company.Id), CancellationToken.None);

        receipt.GymName.Should().Be("Iron Temple SRL");
        receipt.GymTaxId.Should().Be("30-71234567-8");
        receipt.SiteName.Should().Be("Centro");
        receipt.MemberName.Should().Be("Juan Pérez");
        receipt.MemberDocument.Should().Be("DNI 30111222");
        receipt.CashierName.Should().Be("Ana Admin");
        receipt.Items.Should().ContainSingle(i =>
            i.Description == "Cuota mensual Full" && i.Amount == 35_000m);
        receipt.OnAccount.Should().Be(0m);
        receipt.Total.Should().Be(35_000m);
        receipt.Code.Should().StartWith("REC-");
    }

    [Fact]
    public async Task Handle_PagoSinAsignaciones_QuedaComoPagoACuenta()
    {
        var (company, site, member, staff) = SeedTenant();

        var payment = Payment.Create(company.Id, site.Id, member.Id, staff.Id,
            10_000m, PaymentMethod.BankTransfer);
        _paymentRepo.GetByIdAsync(payment.Id, Arg.Any<CancellationToken>()).Returns(payment);

        var receipt = await Sut().Handle(
            new BuildReceiptQuery(payment.Id, company.Id), CancellationToken.None);

        receipt.Items.Should().BeEmpty();
        receipt.OnAccount.Should().Be(10_000m);
        receipt.Total.Should().Be(10_000m);
    }

    [Fact]
    public async Task Handle_VentaDeProducto_ArmaItemsDesdeLasLineasConIva()
    {
        var (company, site, _, staff) = SeedTenant();

        // Venta a consumidor final (sin socio): Agua 1500 x2 + IVA 21% = 3630.
        var sale = Sale.Create(company.Id, site.Id, staff.Id, memberId: null);
        sale.Lines.Add(SaleLine.Create(sale.Id, "Agua mineral 500ml", 2, 1_500m, 0.21m));
        sale.RecalculateTotals();
        _saleRepo.GetByIdAsync(sale.Id, Arg.Any<CancellationToken>()).Returns(sale);

        var payment = Payment.Create(company.Id, site.Id, memberId: null, staff.Id,
            sale.Total, PaymentMethod.Cash, saleId: sale.Id);
        _paymentRepo.GetByIdAsync(payment.Id, Arg.Any<CancellationToken>()).Returns(payment);

        var receipt = await Sut().Handle(
            new BuildReceiptQuery(payment.Id, company.Id), CancellationToken.None);

        receipt.MemberName.Should().Be("Consumidor Final");
        receipt.MemberDocument.Should().BeEmpty();
        receipt.Items.Should().ContainSingle(i =>
            i.Description == "Agua mineral 500ml x2" && i.Amount == 3_630m);
        receipt.OnAccount.Should().Be(0m);
        receipt.Total.Should().Be(3_630m);
    }

    [Fact]
    public async Task Handle_PagoDeOtraCompany_NoSeExpone()
    {
        var (company, site, member, staff) = SeedTenant();

        var payment = Payment.Create(company.Id, site.Id, member.Id, staff.Id,
            5_000m, PaymentMethod.Cash);
        _paymentRepo.GetByIdAsync(payment.Id, Arg.Any<CancellationToken>()).Returns(payment);

        var act = () => Sut().Handle(
            new BuildReceiptQuery(payment.Id, Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
