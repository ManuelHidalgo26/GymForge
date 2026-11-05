using FluentAssertions;
using GymForge.Desktop.Views.Shell;

namespace GymForge.Desktop.Tests;

public class CommandPaletteTests
{
    private readonly List<NavSection> _navigated = [];
    private int _createMemberCalls;
    private int _themeToggles;

    private CommandPaletteViewModel NewPalette() =>
        new(PaletteCatalog.Build(_navigated.Add, () => _createMemberCalls++, () => _themeToggles++));

    [Fact]
    public void Catalog_HasOneNavigationCommandPerSectionPlusTheFourQuickActions()
    {
        var commands = PaletteCatalog.Build(_ => { }, () => { }, () => { });

        commands.Where(c => c.Group == PaletteCatalog.NavigationGroup).Should().HaveCount(Enum.GetValues<NavSection>().Length);
        commands.Where(c => c.Group == PaletteCatalog.ActionsGroup).Select(c => c.Title)
            .Should().BeEquivalentTo("Nuevo socio", "Registrar check-in", "Cobrar", "Cambiar tema");
    }

    [Fact]
    public void Catalog_NavigationCommandsReachTheirSection()
    {
        var commands = PaletteCatalog.Build(_navigated.Add, () => { }, () => { });

        foreach (var command in commands.Where(c => c.Group == PaletteCatalog.NavigationGroup))
            command.Run();

        _navigated.Should().BeEquivalentTo(Enum.GetValues<NavSection>());
    }

    [Fact]
    public void Open_TurnsOnThePaletteWithAllCommandsAndTheFirstSelected()
    {
        var palette = NewPalette();

        palette.OpenCommand.Execute(null);

        palette.IsOpen.Should().BeTrue();
        palette.Results.Should().HaveCount(15);
        palette.Selected.Should().BeSameAs(palette.Results[0]);
    }

    [Theory]
    [InlineData("config", "Configuración")]
    [InlineData("CONFIGURACION", "Configuración")]
    [InlineData("  socio ", "Socios")]
    [InlineData("rutina", "Rutinas")]
    public void Filter_IgnoresCaseAccentsAndSurroundingSpaces(string query, string expectedTitle)
    {
        var palette = NewPalette();
        palette.OpenCommand.Execute(null);

        palette.Query = query;

        palette.Results.Select(c => c.Title).Should().Contain(expectedTitle);
    }

    [Fact]
    public void Filter_ByTextReducesTheListAndKeepsTheFirstMatchSelected()
    {
        var palette = NewPalette();
        palette.OpenCommand.Execute(null);

        palette.Query = "socio";

        palette.Results.Select(c => c.Title).Should().BeEquivalentTo("Socios", "Nuevo socio");
        palette.Selected.Should().BeSameAs(palette.Results[0]);
        palette.HasNoResults.Should().BeFalse();
    }

    [Fact]
    public void Filter_WithoutMatchesLeavesAnEmptyStateAndNoSelection()
    {
        var palette = NewPalette();
        palette.OpenCommand.Execute(null);

        palette.Query = "zzzz";

        palette.Results.Should().BeEmpty();
        palette.HasNoResults.Should().BeTrue();
        palette.Selected.Should().BeNull();
    }

    [Fact]
    public void MoveSelection_WrapsAroundBothEnds()
    {
        var palette = NewPalette();
        palette.OpenCommand.Execute(null);

        palette.MoveSelection(-1);
        palette.Selected.Should().BeSameAs(palette.Results[^1]);

        palette.MoveSelection(1);
        palette.Selected.Should().BeSameAs(palette.Results[0]);
    }

    [Fact]
    public void Execute_RunsTheSelectedCommandAndClosesThePalette()
    {
        var palette = NewPalette();
        palette.OpenCommand.Execute(null);
        palette.Query = "caja";

        palette.ExecuteCommand.Execute(null);

        _navigated.Should().Equal(NavSection.Cash);
        palette.IsOpen.Should().BeFalse();
        palette.Query.Should().BeEmpty();
    }

    [Fact]
    public void Execute_WithAnExplicitCommandRunsThatOne()
    {
        var palette = NewPalette();
        palette.OpenCommand.Execute(null);
        var newMember = palette.Results.Single(c => c.Title == "Nuevo socio");

        palette.ExecuteCommand.Execute(newMember);

        _createMemberCalls.Should().Be(1);
        _navigated.Should().BeEmpty();
    }

    [Fact]
    public void Execute_WithNothingSelectedDoesNothing()
    {
        var palette = NewPalette();
        palette.OpenCommand.Execute(null);
        palette.Query = "zzzz";

        palette.ExecuteCommand.Execute(null);

        _navigated.Should().BeEmpty();
        palette.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void Close_ResetsTheQuery_AndIsOnlyAvailableWhileOpen()
    {
        var palette = NewPalette();
        palette.CloseCommand.CanExecute(null).Should().BeFalse("con la paleta cerrada Escape no debe consumirse");

        palette.OpenCommand.Execute(null);
        palette.Query = "caja";
        palette.CloseCommand.CanExecute(null).Should().BeTrue();
        palette.CloseCommand.Execute(null);

        palette.IsOpen.Should().BeFalse();
        palette.Query.Should().BeEmpty();
        palette.CloseCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Reopening_StartsFromACleanListAgain()
    {
        var palette = NewPalette();
        palette.OpenCommand.Execute(null);
        palette.Query = "caja";
        palette.CloseCommand.Execute(null);

        palette.OpenCommand.Execute(null);

        palette.Query.Should().BeEmpty();
        palette.Results.Should().HaveCount(15);
    }
}
