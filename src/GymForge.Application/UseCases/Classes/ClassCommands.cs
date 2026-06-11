using FluentValidation;
using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using MediatR;

namespace GymForge.Application.UseCases.Classes;

public record ClassDto(Guid Id, string Name, int DurationMin, int Capacity)
{
    public static ClassDto FromEntity(ClassDescription c) =>
        new(c.Id, c.Name, c.DefaultDurationMin, c.DefaultCapacity);
}

public record GetClassesQuery(Guid CompanyId) : IRequest<IReadOnlyList<ClassDto>>;

public class GetClassesQueryHandler : IRequestHandler<GetClassesQuery, IReadOnlyList<ClassDto>>
{
    private readonly IClassRepository _repo;
    public GetClassesQueryHandler(IClassRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<ClassDto>> Handle(GetClassesQuery q, CancellationToken ct) =>
        (await _repo.GetByCompanyAsync(q.CompanyId, ct)).Select(ClassDto.FromEntity).ToList();
}

public record CreateClassCommand(Guid CompanyId, string Name, int DurationMin, int Capacity) : IRequest<ClassDto>;

public class CreateClassCommandValidator : AbstractValidator<CreateClassCommand>
{
    public CreateClassCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("El nombre de la clase es obligatorio.");
        RuleFor(x => x.DurationMin).InclusiveBetween(10, 240).WithMessage("La duración debe estar entre 10 y 240 minutos.");
        RuleFor(x => x.Capacity).GreaterThan(0).WithMessage("El cupo debe ser positivo.");
    }
}

public class CreateClassCommandHandler : IRequestHandler<CreateClassCommand, ClassDto>
{
    private readonly IClassRepository _repo;
    public CreateClassCommandHandler(IClassRepository repo) => _repo = repo;

    public async Task<ClassDto> Handle(CreateClassCommand cmd, CancellationToken ct)
    {
        var cls = ClassDescription.Create(cmd.CompanyId, cmd.Name.Trim(), cmd.DurationMin, cmd.Capacity);
        await _repo.AddAsync(cls, ct);
        await _repo.SaveChangesAsync(ct);
        return ClassDto.FromEntity(cls);
    }
}
