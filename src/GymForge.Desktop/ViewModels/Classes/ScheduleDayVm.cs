using GymForge.Application.UseCases.Classes;

namespace GymForge.Desktop.ViewModels.Classes;

/// <summary>Una columna (día) de la grilla semanal de Clases, con sus horarios ordenados por hora.</summary>
public sealed record ScheduleDayVm(
    DateOnly Date, string DayName, bool IsToday, IReadOnlyList<ScheduleRowDto> Schedules)
{
    public string DateLabel => $"{Date:dd/MM}";
    public bool IsEmpty => Schedules.Count == 0;
}
