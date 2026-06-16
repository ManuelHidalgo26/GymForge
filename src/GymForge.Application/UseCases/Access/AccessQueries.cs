using GymForge.Application.Interfaces;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Application.UseCases.Access;

/// <summary>Fila del historial de accesos del día, lista para mostrar en el kiosko.</summary>
public record AccessLogRowDto(string MemberName, DateTime At, bool Granted, string StatusText);

/// <summary>Textos de presentación de los accesos (compartidos entre kiosko e historial).</summary>
public static class AccessMessages
{
    public const string GrantedText = "Ingresó";

    public static string Denial(AccessDenialReason? reason) => reason switch
    {
        AccessDenialReason.TagUnknown           => "Credencial no reconocida",
        AccessDenialReason.IncompleteMembership => "Sin membresía activa",
        AccessDenialReason.Expired              => "Membresía vencida",
        AccessDenialReason.Owing                => "Deuda pendiente — ver recepción",
        AccessDenialReason.OutOfHours           => "Fuera del horario permitido",
        AccessDenialReason.DoorNotAllowed       => "Sin acceso a esta zona",
        AccessDenialReason.GenderRestricted     => "Área restringida",
        AccessDenialReason.AlreadyInside        => "Ya registrado como presente",
        AccessDenialReason.NoVisitsLeft         => "Pack de visitas agotado",
        AccessDenialReason.Suspended            => "Acceso suspendido — ver recepción",
        _                                       => "Acceso denegado",
    };

    public static string Status(bool granted, AccessDenialReason? reason) =>
        granted ? GrantedText : Denial(reason);
}

/// <summary>Accesos de hoy en la sede (más recientes primero) para el panel del kiosko.</summary>
public record GetTodaysAccessLogQuery(Guid CompanyId, Guid SiteId) : IRequest<IReadOnlyList<AccessLogRowDto>>;

public class GetTodaysAccessLogQueryHandler
    : IRequestHandler<GetTodaysAccessLogQuery, IReadOnlyList<AccessLogRowDto>>
{
    private readonly IAccessLogRepository _logs;
    public GetTodaysAccessLogQueryHandler(IAccessLogRepository logs) => _logs = logs;

    public async Task<IReadOnlyList<AccessLogRowDto>> Handle(GetTodaysAccessLogQuery q, CancellationToken ct)
    {
        // SwipedAt se guarda en UTC; "hoy" es el día local del kiosko (corre en la sede).
        var from = DateTime.Today.ToUniversalTime();
        var to = DateTime.Today.AddDays(1).ToUniversalTime();

        var logs = await _logs.GetBySiteAsync(q.SiteId, from, to, ct);
        return logs
            .OrderByDescending(l => l.SwipedAt)
            .Take(50)
            .Select(l => new AccessLogRowDto(
                l.Member?.FullName ?? "Desconocido",
                l.SwipedAt.ToLocalTime(),
                l.AccessGranted,
                AccessMessages.Status(l.AccessGranted, l.DenialReason)))
            .ToList();
    }
}
