using FluentAssertions;
using GymForge.Application;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Access;
using GymForge.Application.UseCases.Charges;
using GymForge.Application.UseCases.Memberships;
using GymForge.Infrastructure;
using GymForge.Infrastructure.Persistence;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace GymForge.Integration.Tests;

/// <summary>Los hosts (escritorio y API) arman estos servicios por DI: acá se verifica que se resuelvan.</summary>
public class ApplicationWiringTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton<ICurrentTenant, AmbientTenantProvider>();
        services.AddSingleton<ICurrentUser, AmbientCurrentUser>();
        services.AddInfrastructure("Data Source=:memory:");
        services.AddSingleton<GatekeeperConfig>();
        services.AddScoped<ValidateSwipeUseCase>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void CompanyCalendarAndArrearsRelief_ResolveAsScopedServices()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ICompanyCalendar>().Should().BeOfType<CompanyCalendar>();
        scope.ServiceProvider.GetRequiredService<IArrearsRelief>().Should().BeOfType<ArrearsRelief>();
    }

    [Fact]
    public void TheHandlersAndGatekeeperThatDependOnThem_ResolveFromTheContainer()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;

        services.GetRequiredService<IRequestHandler<ProcessPaymentCommand, PaymentDto>>().Should().NotBeNull();
        services.GetRequiredService<IRequestHandler<ApplyMemberCreditCommand, ApplyCreditResult>>().Should().NotBeNull();
        services.GetRequiredService<IRequestHandler<UnfreezeMembershipCommand, MembershipDto>>().Should().NotBeNull();
        services.GetRequiredService<ValidateSwipeUseCase>().Should().NotBeNull();
    }
}
