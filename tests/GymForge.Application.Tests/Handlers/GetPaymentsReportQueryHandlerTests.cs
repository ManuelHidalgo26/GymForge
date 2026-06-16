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

    [Fact]
    public async Task Handle_IncludesPaymentIdForReprint_AndTotals()
    {
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();

        var withMember = Payment.Create(companyId, siteId, Guid.NewGuid(), Guid.NewGuid(), 10_000m, PaymentMethod.Cash);
        var walkIn = Payment.Create(companyId, siteId, memberId: null, Guid.NewGuid(), 3_630m, PaymentMethod.Cash);
        _payments.GetByPeriodAsync(companyId, siteId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new[] { withMember, walkIn });

        var report = await new GetPaymentsReportQueryHandler(_payments).Handle(
            new GetPaymentsReportQuery(companyId, siteId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)),
            CancellationToken.None);

        report.Count.Should().Be(2);
        report.Total.Should().Be(13_630m);
        // El PaymentId viaja en la fila para poder reimprimir el recibo.
        report.Rows.Should().Contain(r => r.PaymentId == withMember.Id);
        report.Rows.Should().Contain(r => r.PaymentId == walkIn.Id && r.MemberName == "—");
    }
}
