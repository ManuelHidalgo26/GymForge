using FluentValidation;
using GymForge.Application.Interfaces;
using MediatR;

namespace GymForge.Application.UseCases.Dunning;

public record DunningStageInput(int DaysOverdue, string Label, string Template);

public record UpdateDunningSettingsCommand(Guid CompanyId, bool Enabled, IReadOnlyList<DunningStageInput> Stages)
    : IRequest;

public class UpdateDunningSettingsCommandValidator : AbstractValidator<UpdateDunningSettingsCommand>
{
    private const int MaxTemplateLength = 1000;
    private const int MaxLabelLength = 40;

    public UpdateDunningSettingsCommandValidator()
    {
        RuleFor(x => x.Stages).NotEmpty().WithMessage("Tiene que haber al menos una etapa.");
        RuleFor(x => x.Stages)
            .Must(stages => stages.Select(s => s.DaysOverdue).Distinct().Count() == stages.Count)
            .WithMessage("Los días de vencido no se pueden repetir entre etapas.");

        RuleForEach(x => x.Stages).ChildRules(stage =>
        {
            stage.RuleFor(s => s.DaysOverdue).GreaterThanOrEqualTo(1)
                .WithMessage("Los días de vencido tienen que ser 1 o más.");
            stage.RuleFor(s => s.Label)
                .NotEmpty().WithMessage("El nombre de la etapa es obligatorio.")
                .MaximumLength(MaxLabelLength)
                .WithMessage($"El nombre de la etapa admite hasta {MaxLabelLength} caracteres.");
            stage.RuleFor(s => s.Template)
                .NotEmpty().WithMessage("El mensaje de cada etapa es obligatorio.")
                .MaximumLength(MaxTemplateLength)
                .WithMessage($"El mensaje admite hasta {MaxTemplateLength} caracteres.");
        });
    }
}

/// <summary>Persiste la configuración del cobro en la empresa y la aplica en caliente al singleton del job.</summary>
public class UpdateDunningSettingsCommandHandler : IRequestHandler<UpdateDunningSettingsCommand>
{
    private readonly ISiteRepository _repo;
    private readonly DunningConfig _config;

    public UpdateDunningSettingsCommandHandler(ISiteRepository repo, DunningConfig config)
    {
        _repo = repo;
        _config = config;
    }

    public async Task Handle(UpdateDunningSettingsCommand cmd, CancellationToken ct)
    {
        var company = await _repo.GetCompanyAsync(cmd.CompanyId, ct)
            ?? throw new InvalidOperationException("La empresa no existe.");

        var settings = new DunningSettings(cmd.Enabled, cmd.Stages
            .OrderBy(s => s.DaysOverdue)
            .Select(s => new DunningStageSettings(s.DaysOverdue, s.Label.Trim(), s.Template.Trim()))
            .ToList());

        company.SetDunningConfig(settings.ToJson());
        await _repo.SaveChangesAsync(ct);

        settings.ApplyTo(_config);
    }
}
