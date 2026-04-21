using GymForge.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GymForge.Infrastructure.Services;

/// <summary>
/// Simple in-process event bus for Sprint 1.
/// Dispatches domain events to registered handlers via DI.
/// Replace with MediatR notifications or a proper message bus in Sprint 2+.
/// </summary>
public class InProcessEventBus : IEventBus
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<InProcessEventBus> _logger;

    public InProcessEventBus(IServiceProvider sp, ILogger<InProcessEventBus> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    public Task PublishAsync<T>(T domainEvent, CancellationToken ct = default) where T : class
    {
        _logger.LogDebug("Domain event published: {EventType}", typeof(T).Name);

        var handlerType = typeof(IDomainEventHandler<T>);
        var handlers = _sp.GetServices(handlerType);

        var tasks = handlers
            .OfType<IDomainEventHandler<T>>()
            .Select(h => h.HandleAsync(domainEvent, ct));

        return Task.WhenAll(tasks);
    }
}

public interface IDomainEventHandler<T> where T : class
{
    Task HandleAsync(T domainEvent, CancellationToken ct = default);
}
