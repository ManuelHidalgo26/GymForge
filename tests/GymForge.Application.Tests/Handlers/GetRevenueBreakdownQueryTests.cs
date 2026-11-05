using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Reports;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

/// <summary>Recaudación desagregada por método, concepto/plan y sede (ADR-007, Pieza D).</summary>
public class GetRevenueBreakdownQueryTests
{
    private static readonly DateOnly From = new(2026, 11, 1);
    private static readonly DateOnly To = new(2026, 11, 3);

    private readonly IPaymentRepository _payments = Substitute.For<IPaymentRepository>();
    private readonly ISiteRepository _sites = Substitute.For<ISiteRepository>();
    private readonly Company _company = Company.Create("Power Gym", "30-71234567-3");
    private readonly Site _centro;
    private readonly Site _norte;

    public GetRevenueBreakdownQueryTests()
    {
        _centro = Site.Create(_company.Id, "Centro", "Calle 1");
        _norte = Site.Create(_company.Id, "Norte", "Calle 2");
        _sites.GetByCompanyAsync(_company.Id, Arg.Any<CancellationToken>()).Returns(new List<Site> { _centro, _norte });
        _sites.GetCompanyAsync(_company.Id, Arg.Any<CancellationToken>()).Returns(_company);
    }

    private void HavePayments(Guid? siteId, params Payment[] payments) =>
        _payments.GetForBreakdownAsync(_company.Id, siteId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(payments.ToList());

    private Task<RevenueBreakdown> RunAsync(Guid? siteId = null) =>
        new GetRevenueBreakdownQueryHandler(_payments, _sites).Handle(
            new GetRevenueBreakdownQuery(_company.Id, siteId, From, To), CancellationToken.None);

    // ── Armado de pagos con el grafo que carga el repositorio ────────────────

    private Payment Cobro(
        Site site, decimal amount, PaymentMethod method = PaymentMethod.Cash, Guid? memberId = null,
        Guid? saleId = null) =>
        Payment.Create(_company.Id, site.Id, memberId ?? Guid.NewGuid(), Guid.NewGuid(), amount, method, saleId: saleId);

    private Payment Venta(Site site, decimal amount, PaymentMethod method = PaymentMethod.Cash) =>
        Payment.Create(_company.Id, site.Id, memberId: null, Guid.NewGuid(), amount, method, saleId: Guid.NewGuid());

    /// <summary>Asigna parte del pago a un cargo del concepto dado; si es de membresía lo ata al plan.</summary>
    private static Payment Allocate(
        Payment payment, ConceptType concept, decimal amount, string? plan = null)
    {
        var memberId = payment.MemberId ?? Guid.NewGuid();
        Membership? membership = null;
        if (plan is not null)
        {
            var type = MembershipType.Create(payment.CompanyId, plan, MembershipBasis.Renewal, amount);
            membership = Membership.Create(payment.CompanyId, payment.SiteId, memberId, type.Id,
                new DateOnly(2026, 11, 1), null);
            Set(membership, nameof(Membership.MembershipType), type);
        }

        var charge = Charge.Create(payment.CompanyId, payment.SiteId, memberId, membership?.Id,
            concept, "Cargo", amount, new DateOnly(2026, 11, 1));
        Set(charge, nameof(Charge.Membership), membership);

        var allocation = PaymentAllocation.Create(payment.Id, charge.Id, amount);
        Set(allocation, nameof(PaymentAllocation.Charge), charge);
        payment.Allocations.Add(allocation);
        return payment;
    }

    private static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property)!.SetValue(target, value);

    private static decimal Sum(IEnumerable<RevenueBreakdownRow> rows) => rows.Sum(r => r.Amount);

    // ── Por método ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ByMethod_AgrupaSumaYCuentaLosPagos_DeMayorAMenorMonto()
    {
        HavePayments(null,
            Cobro(_centro, 10_000m, PaymentMethod.Cash),
            Cobro(_centro, 5_000m, PaymentMethod.Cash),
            Cobro(_norte, 40_000m, PaymentMethod.BankTransfer));

        var breakdown = await RunAsync();

        breakdown.ByMethod.Should().Equal(
            new RevenueBreakdownRow("Transferencia", 40_000m, 1),
            new RevenueBreakdownRow("Efectivo", 15_000m, 2));
        breakdown.Total.Should().Be(55_000m);
    }

    // ── Por concepto / plan ──────────────────────────────────────────────────

    [Fact]
    public async Task ByConcept_LaMembresiaVaAlPlan_ElRestoASuConcepto()
    {
        HavePayments(null,
            Allocate(Cobro(_centro, 35_000m), ConceptType.MembershipFee, 35_000m, plan: "Mensual Full"),
            Allocate(Cobro(_centro, 8_000m), ConceptType.ProductSale, 8_000m),
            Allocate(Cobro(_centro, 6_000m), ConceptType.ClassDropIn, 6_000m),
            Allocate(Cobro(_centro, 4_000m), ConceptType.PersonalTraining, 4_000m),
            Allocate(Cobro(_centro, 2_000m), ConceptType.LateFee, 2_000m),
            Allocate(Cobro(_centro, 1_000m), ConceptType.Adjustment, 1_000m));

        var breakdown = await RunAsync();

        breakdown.ByConcept.Select(r => (r.Label, r.Amount)).Should().Equal(
            ("Mensual Full", 35_000m),
            ("Productos", 8_000m),
            ("Clases sueltas", 6_000m),
            ("Personal training", 4_000m),
            ("Recargos por mora", 2_000m),
            ("Ajustes", 1_000m));
    }

    [Fact]
    public async Task ByConcept_CuotaEInscripcionDelMismoPlanSeSumanEnLaFilaDelPlan()
    {
        var payment = Allocate(Cobro(_centro, 45_000m), ConceptType.MembershipFee, 35_000m, plan: "Mensual Full");
        Allocate(payment, ConceptType.SignupFee, 10_000m, plan: "Mensual Full");
        HavePayments(null, payment);

        var breakdown = await RunAsync();

        breakdown.ByConcept.Should().ContainSingle()
            .Which.Should().Be(new RevenueBreakdownRow("Mensual Full", 45_000m, 1));
    }

    [Fact]
    public async Task ByConcept_UnaCuotaSinMembresiaCargadaCaeEnSuConcepto_NoSePierde()
    {
        HavePayments(null, Allocate(Cobro(_centro, 20_000m), ConceptType.MembershipFee, 20_000m));

        var breakdown = await RunAsync();

        breakdown.ByConcept.Should().ContainSingle(r => r.Label == "Cuotas (sin plan asignado)" && r.Amount == 20_000m);
    }

    [Fact]
    public async Task ByConcept_LaEtiquetaSinPlanSoloAplicaALasCuotasSinPlan_LasDeUnPlanSiguenConSuNombre()
    {
        HavePayments(null,
            Allocate(Cobro(_centro, 20_000m), ConceptType.MembershipFee, 20_000m),
            Allocate(Cobro(_centro, 35_000m), ConceptType.MembershipFee, 35_000m, plan: "Mensual Full"));

        var breakdown = await RunAsync();

        breakdown.ByConcept.Select(r => (r.Label, r.Amount)).Should().Equal(
            ("Mensual Full", 35_000m),
            ("Cuotas (sin plan asignado)", 20_000m));
    }

    [Fact]
    public async Task ByConcept_LoNoCubiertoPorAsignacionesVaACuenta()
    {
        HavePayments(null, Allocate(Cobro(_centro, 50_000m), ConceptType.MembershipFee, 35_000m, plan: "Mensual Full"));

        var breakdown = await RunAsync();

        breakdown.ByConcept.Should().Contain(new RevenueBreakdownRow("Mensual Full", 35_000m, 1));
        breakdown.ByConcept.Should().Contain(new RevenueBreakdownRow("A cuenta (saldo a favor)", 15_000m, 1));
    }

    [Fact]
    public async Task ByConcept_UnPagoSinAsignacionesNiVenta_EsTodoACuenta()
    {
        HavePayments(null, Cobro(_centro, 12_000m));

        var breakdown = await RunAsync();

        breakdown.ByConcept.Should().ContainSingle()
            .Which.Should().Be(new RevenueBreakdownRow("A cuenta (saldo a favor)", 12_000m, 1));
    }

    [Fact]
    public async Task ByConcept_UnaVentaSinSocioEsProductosVentaDirecta()
    {
        HavePayments(null, Venta(_centro, 3_630m));

        var breakdown = await RunAsync();

        breakdown.ByConcept.Should().ContainSingle()
            .Which.Should().Be(new RevenueBreakdownRow("Productos (venta directa)", 3_630m, 1));
    }

    [Fact]
    public async Task ByConcept_UnaVentaDePosAUnSocioTambienEsVentaDirecta_NoSaldoAFavor()
    {
        // El POS a un socio crea Payment con SaleId pero sin cargos ni asignaciones.
        HavePayments(null, Cobro(_centro, 2_500m, saleId: Guid.NewGuid()));

        var breakdown = await RunAsync();

        breakdown.ByConcept.Should().ContainSingle(r => r.Label == "Productos (venta directa)" && r.Amount == 2_500m);
        breakdown.ByConcept.Should().NotContain(r => r.Label.StartsWith("A cuenta"));
    }

    [Fact]
    public async Task ByConcept_CuentaCadaPagoUnaSolaVezPorFila()
    {
        var payment = Allocate(Cobro(_centro, 30_000m), ConceptType.ProductSale, 10_000m);
        Allocate(payment, ConceptType.ProductSale, 20_000m);
        HavePayments(null, payment);

        var breakdown = await RunAsync();

        breakdown.ByConcept.Should().ContainSingle()
            .Which.Should().Be(new RevenueBreakdownRow("Productos", 30_000m, 1));
    }

    // ── Por sede ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task BySite_ConAlcanceDeEmpresa_TraeCadaSedeConSuNombre()
    {
        HavePayments(null, Cobro(_centro, 10_000m), Cobro(_norte, 30_000m), Cobro(_norte, 5_000m));

        var breakdown = await RunAsync(siteId: null);

        breakdown.BySite.Should().Equal(
            new RevenueBreakdownRow("Norte", 35_000m, 2),
            new RevenueBreakdownRow("Centro", 10_000m, 1));
    }

    [Fact]
    public async Task BySite_ConUnaSede_PideSoloEsaSedeYDevuelveUnaFila()
    {
        HavePayments(_centro.Id, Cobro(_centro, 10_000m), Cobro(_centro, 2_000m));

        var breakdown = await RunAsync(_centro.Id);

        breakdown.BySite.Should().ContainSingle()
            .Which.Should().Be(new RevenueBreakdownRow("Centro", 12_000m, 2));
        await _payments.Received(1).GetForBreakdownAsync(
            _company.Id, _centro.Id, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BySite_UnaSedeYaNoActiva_ConservaSuMontoConUnaEtiquetaGenerica()
    {
        var inactive = Site.Create(_company.Id, "Cerrada", "Calle 3");
        HavePayments(null, Cobro(inactive, 7_000m));

        var breakdown = await RunAsync();

        breakdown.BySite.Should().ContainSingle()
            .Which.Should().Be(new RevenueBreakdownRow("Sede inactiva", 7_000m, 1));
    }

    // ── Período ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rango_VaDelInicioDelDesdeAlInicioDelDiaSiguienteAlHasta_DelGimnasioExpresadoEnUtc()
    {
        HavePayments(null);

        await RunAsync();

        // Del 01/11 a las 00:00 al 04/11 a las 00:00 en Argentina (UTC-3).
        await _payments.Received(1).GetForBreakdownAsync(
            _company.Id, null,
            new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 4, 3, 0, 0, DateTimeKind.Utc),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SinPagos_TodoEnCero()
    {
        HavePayments(null);

        var breakdown = await RunAsync();

        breakdown.Total.Should().Be(0m);
        breakdown.ByMethod.Should().BeEmpty();
        breakdown.ByConcept.Should().BeEmpty();
        breakdown.BySite.Should().BeEmpty();
    }

    // ── Reconciliación: los tres cortes suman el total ───────────────────────

    [Fact]
    public async Task LosTresCortesSumanElTotal_EnUnEscenarioMixto()
    {
        HavePayments(null,
            Allocate(Cobro(_centro, 35_000m, PaymentMethod.Cash), ConceptType.MembershipFee, 35_000m, plan: "Mensual Full"),
            Allocate(Cobro(_norte, 70_000m, PaymentMethod.BankTransfer), ConceptType.MembershipFee, 50_000m, plan: "Trimestral"),
            Allocate(Cobro(_centro, 8_000m, PaymentMethod.DebitCard), ConceptType.ProductSale, 8_000m),
            Venta(_norte, 3_630m, PaymentMethod.Cash),
            Cobro(_centro, 1_234.56m, PaymentMethod.MercadoPago, saleId: Guid.NewGuid()),
            Cobro(_norte, 900m, PaymentMethod.Cash));

        var breakdown = await RunAsync();

        breakdown.Total.Should().Be(35_000m + 70_000m + 8_000m + 3_630m + 1_234.56m + 900m);
        Sum(breakdown.ByMethod).Should().Be(breakdown.Total);
        Sum(breakdown.ByConcept).Should().Be(breakdown.Total);
        Sum(breakdown.BySite).Should().Be(breakdown.Total);
    }
}
