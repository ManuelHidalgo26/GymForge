using GymForge.Application.UseCases.Dunning;

namespace GymForge.Desktop.ViewModels.Collections;

/// <summary>Fila de la pantalla de morosos: el socio con su deuda y los textos ya listos para mostrar.</summary>
public sealed class OverdueMemberRow
{
    public OverdueMemberRow(OverdueMemberDto dto, DateOnly today)
    {
        Dto = dto;
        LastReminderText = DescribeLastReminder(dto.LastReminderAt, today);
    }

    public OverdueMemberDto Dto { get; }

    public Guid MemberId => Dto.MemberId;
    public string FullName => Dto.FullName;
    public bool HasUsableMobile => Dto.HasUsableMobile;

    /// <summary>El celular tal cual se cargó, o el motivo por el que no se puede avisar por WhatsApp.</summary>
    public string MobileText => HasUsableMobile && Dto.Mobile is { } mobile ? mobile : "Sin celular válido";

    public string LastReminderText { get; }

    /// <summary>SentAt viene en UTC; el "hace N días" se cuenta en días calendario locales.</summary>
    private static string DescribeLastReminder(DateTime? sentAtUtc, DateOnly today)
    {
        if (sentAtUtc is not { } sentAt) return "Sin recordatorios";

        var sentDay = DateOnly.FromDateTime(DateTime.SpecifyKind(sentAt, DateTimeKind.Utc).ToLocalTime());
        return (today.DayNumber - sentDay.DayNumber) switch
        {
            <= 0 => "Hoy",
            1 => "Ayer",
            var days => $"Hace {days} días",
        };
    }
}
