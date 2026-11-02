using CommunityToolkit.Mvvm.ComponentModel;
using GymForge.Application.UseCases.Dunning;

namespace GymForge.Desktop.ViewModels.Settings;

/// <summary>Una etapa del cobro automático en edición (Configuración → Cobranzas y recordatorios).</summary>
public partial class DunningStageEditor : ObservableObject
{
    [ObservableProperty] private int _daysOverdue;
    [ObservableProperty] private string _label;
    [ObservableProperty] private string _template;

    public DunningStageEditor(DunningStage stage)
    {
        _daysOverdue = stage.DaysOverdue;
        _label = stage.Label;
        _template = stage.Template;
    }
}
