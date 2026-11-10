using FluentAssertions;
using GymForge.Application.UseCases.Exercises;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Integration.Tests;

/// <summary>Borrar y modificar ejercicios contra SQLite real: ni la biblioteca global ni los de otra empresa se tocan.</summary>
public class ExerciseDeleteTenantTests : IAsyncLifetime
{
    private GymForgeDbContext _db = null!;
    private AmbientTenantProvider _tenant = null!;
    private readonly Guid _companyA = Guid.NewGuid();
    private readonly Guid _companyB = Guid.NewGuid();

    public Task InitializeAsync()
    {
        _db = TestDb.Create(out _tenant);
        _tenant.Set(_companyA, Guid.NewGuid());
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    private async Task<Exercise> SeedAsync(string name, Guid? tenantId)
    {
        var exercise = Exercise.Create(name, MuscleGroup.Chest, Equipment.Barbell, MovementType.Compound, tenantId: tenantId);
        await _db.Exercises.AddAsync(exercise);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return exercise;
    }

    private Task DeleteAsync(Guid id) =>
        new DeleteExerciseCommandHandler(new ExerciseRepository(_db)).Handle(new DeleteExerciseCommand(id), CancellationToken.None);

    private Task UpdateAsync(Guid id) =>
        new UpdateExerciseCommandHandler(new ExerciseRepository(_db)).Handle(
            new UpdateExerciseCommand(id, "Renombrado", MuscleGroup.Back, Equipment.Dumbbell, MovementType.Isolation, 4),
            CancellationToken.None);

    private async Task<string> NameOfAsync(Guid id) =>
        (await _db.Exercises.IgnoreQueryFilters().AsNoTracking().SingleAsync(e => e.Id == id)).Name;

    [Fact]
    public async Task UpdateGlobalExercise_IsRejected_AndItIsUnchanged()
    {
        var global = await SeedAsync("Press global", tenantId: null);

        var act = () => UpdateAsync(global.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*biblioteca global*");
        (await NameOfAsync(global.Id)).Should().Be("Press global");
    }

    [Fact]
    public async Task UpdateOwnExercise_ChangesIt()
    {
        var own = await SeedAsync("Press propio", _companyA);

        await UpdateAsync(own.Id);

        (await NameOfAsync(own.Id)).Should().Be("Renombrado");
    }

    [Fact]
    public async Task UpdateExerciseOfAnotherCompany_IsRejectedAsIfItDidNotExist_AndItIsUnchanged()
    {
        var foreign = await SeedAsync("Press ajeno", _companyB);

        var act = () => UpdateAsync(foreign.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no existe*");
        (await NameOfAsync(foreign.Id)).Should().Be("Press ajeno");
    }

    [Fact]
    public async Task DeleteGlobalExercise_IsRejected_AndItSurvives()
    {
        var global = await SeedAsync("Press global", tenantId: null);

        var act = () => DeleteAsync(global.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*biblioteca global*");
        (await _db.Exercises.IgnoreQueryFilters().CountAsync(e => e.Id == global.Id)).Should().Be(1);
    }

    [Fact]
    public async Task DeleteOwnExercise_RemovesIt()
    {
        var own = await SeedAsync("Press propio", _companyA);

        await DeleteAsync(own.Id);

        (await _db.Exercises.IgnoreQueryFilters().CountAsync(e => e.Id == own.Id)).Should().Be(0);
    }

    [Fact]
    public async Task DeleteExerciseOfAnotherCompany_IsRejectedAsIfItDidNotExist_AndItSurvives()
    {
        var foreign = await SeedAsync("Press ajeno", _companyB);

        var act = () => DeleteAsync(foreign.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no existe*");
        (await _db.Exercises.IgnoreQueryFilters().CountAsync(e => e.Id == foreign.Id)).Should().Be(1);
    }
}
