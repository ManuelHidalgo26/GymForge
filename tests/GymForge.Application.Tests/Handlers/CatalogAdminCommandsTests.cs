using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Exercises;
using GymForge.Application.UseCases.Settings;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class CatalogAdminCommandsTests
{
    // ── Ejercicios ──────────────────────────────────────────────────────────

    private readonly IExerciseRepository _exRepo = Substitute.For<IExerciseRepository>();

    [Fact]
    public async Task UpdateExercise_ChangesFields()
    {
        var ex = Exercise.Create("Press plano", MuscleGroup.Chest, Equipment.Barbell, MovementType.Compound);
        _exRepo.GetByIdAsync(ex.Id, Arg.Any<CancellationToken>()).Returns(ex);

        var dto = await new UpdateExerciseCommandHandler(_exRepo).Handle(
            new UpdateExerciseCommand(ex.Id, "Press inclinado", MuscleGroup.Chest,
                Equipment.Dumbbell, MovementType.Compound, 4), CancellationToken.None);

        dto.Name.Should().Be("Press inclinado");
        dto.Equipment.Should().Be(Equipment.Dumbbell);
        dto.Difficulty.Should().Be(4);
        await _exRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteExercise_InUse_Throws()
    {
        var ex = Exercise.Create("Sentadilla", MuscleGroup.Quads, Equipment.Barbell, MovementType.Compound);
        _exRepo.GetByIdAsync(ex.Id, Arg.Any<CancellationToken>()).Returns(ex);
        _exRepo.IsInUseAsync(ex.Id, Arg.Any<CancellationToken>()).Returns(true);

        var act = () => new DeleteExerciseCommandHandler(_exRepo).Handle(
            new DeleteExerciseCommand(ex.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _exRepo.DidNotReceive().Remove(Arg.Any<Exercise>());
    }

    // ── Sedes ───────────────────────────────────────────────────────────────

    private readonly ISiteRepository _siteRepo = Substitute.For<ISiteRepository>();

    private Site MakeSite(out Guid companyId)
    {
        companyId = Guid.NewGuid();
        var site = Site.Create(companyId, "Sede Test", "Calle 1");
        _siteRepo.GetSiteAsync(site.Id, Arg.Any<CancellationToken>()).Returns(site);
        return site;
    }

    [Fact]
    public async Task DeleteSite_LastActiveSite_Throws()
    {
        var site = MakeSite(out var companyId);
        _siteRepo.CountActiveSitesAsync(companyId, Arg.Any<CancellationToken>()).Returns(1);

        var act = () => new DeleteSiteCommandHandler(_siteRepo).Handle(
            new DeleteSiteCommand(site.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteSite_WithData_DeactivatesInsteadOfRemoving()
    {
        var site = MakeSite(out var companyId);
        _siteRepo.CountActiveSitesAsync(companyId, Arg.Any<CancellationToken>()).Returns(2);
        _siteRepo.SiteHasDataAsync(site.Id, Arg.Any<CancellationToken>()).Returns(true);

        var deleted = await new DeleteSiteCommandHandler(_siteRepo).Handle(
            new DeleteSiteCommand(site.Id), CancellationToken.None);

        deleted.Should().BeFalse();
        site.IsActive.Should().BeFalse();
        _siteRepo.DidNotReceive().RemoveSite(Arg.Any<Site>());
    }

    [Fact]
    public async Task DeleteSite_Empty_RemovesPermanently()
    {
        var site = MakeSite(out var companyId);
        _siteRepo.CountActiveSitesAsync(companyId, Arg.Any<CancellationToken>()).Returns(2);
        _siteRepo.SiteHasDataAsync(site.Id, Arg.Any<CancellationToken>()).Returns(false);

        var deleted = await new DeleteSiteCommandHandler(_siteRepo).Handle(
            new DeleteSiteCommand(site.Id), CancellationToken.None);

        deleted.Should().BeTrue();
        _siteRepo.Received(1).RemoveSite(site);
    }
}
