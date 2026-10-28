using FluentValidation;
using GymForge.Application.Behaviors;
using GymForge.Application.Services;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace GymForge.Application;

public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly);
        services.AddScoped<ICashRegister, CashRegister>();
        services.AddScoped<ICompanyCalendar, CompanyCalendar>();
        services.AddScoped<IArrearsRelief, ArrearsRelief>();

        // Licenciamiento: estado vivo + validador de claves firmadas (offline). La huella de la PC la aporta
        // Infraestructura; si el host no la registra, el atado a máquina simplemente no se chequea.
        services.AddSingleton<UseCases.Licensing.CurrentLicense>();
        services.AddSingleton<Interfaces.ILicenseService>(sp =>
            new UseCases.Licensing.LicenseService(sp.GetService<Interfaces.IMachineFingerprint>()));

        return services;
    }
}
