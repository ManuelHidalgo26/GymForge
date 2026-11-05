using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Reports;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class GetPaymentsReportQueryHandlerTests
{
    private readonly IPaymentRepository _payments = Substitute.For<IPaymentRepository>();
    private readonly ISiteRepository _sites = Substitute.For<ISiteRepository>();

    [Fact]
    public async Task Handle_IncludesPaymentIdForReprint_AndTotals()
    {
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();

        var withMember = Payment.Create(companyId, siteId, Guid.NewGuid(), Guid.NewGuid(), 10_000m, PaymentMethod.Cash);
        var walkIn = Payment.Create(companyId, siteId, memberId: null, Guid.NewGuid(), 3_630m, PaymentMethod.Cash);
        _payments.GetByPeriodAsync(companyId, siteId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new[] { withMember, walkIn });

        var report = await new GetPaymentsReportQueryHandler(_payments, _sites).Handle(
            new GetPaymentsReportQuery(companyId, siteId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)),
            CancellationToken.None);

        report.Count.Should().Be(2);
        report.Total.Should().Be(13_630m);
        // El PaymentId viaja en la fila para poder reimprimir el recibo.
        report.Rows.Should().Contain(r => r.PaymentId == withMember.Id);
        report.Rows.Should().Contain(r => r.PaymentId == walkIn.Id && r.MemberName == "—");
    }

    [Fact]
    public async Task Handle_MuestraLaHoraDelCobroEnLaZonaHorariaDelGimnasio()
    {
        var company = Company.Create("Power Gym", "30-71234567-3");
        var siteId = Guid.NewGuid();
        var payment = Payment.Create(company.Id, siteId, memberId: null, Guid.NewGuid(), 5_000m, PaymentMethod.Cash);
        typeof(Payment).GetProperty(nameof(Payment.ReceivedAt))!.SetValue(
            payment, new DateTime(2026, 11, 3, 2, 30, 0, DateTimeKind.Utc));   // 23:30 del 02/11 en Argentina
        _sites.GetCompanyAsync(company.Id, Arg.Any<CancellationToken>()).Returns(company);
        _payments.GetByPeriodAsync(company.Id, siteId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new[] { payment });

        var report = await new GetPaymentsReportQueryHandler(_payments, _sites).Handle(
            new GetPaymentsReportQuery(company.Id, siteId, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3)),
            CancellationToken.None);

        report.Rows.Should().ContainSingle().Which.ReceivedAt.Should().Be(new DateTime(2026, 11, 2, 23, 30, 0));
    }

    [Fact]
    public async Task Handle_PideAlRepositorioLosDiasLocalesDelGimnasioExpresadosEnUtc()
    {
        var company = Company.Create("Power Gym", "30-71234567-3");
        var siteId = Guid.NewGuid();
        _sites.GetCompanyAsync(company.Id, Arg.Any<CancellationToken>()).Returns(company);

        await new GetPaymentsReportQueryHandler(_payments, _sites).Handle(
            new GetPaymentsReportQuery(company.Id, siteId, new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 3)),
            CancellationToken.None);

        // El 03/11 en Argentina (UTC-3) va de las 03:00 UTC del 03/11 a las 03:00 UTC del 04/11.
        await _payments.Received(1).GetByPeriodAsync(
            company.Id, siteId,
            new DateTime(2026, 11, 3, 3, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 4, 3, 0, 0, DateTimeKind.Utc),
            Arg.Any<CancellationToken>());
    }
}
