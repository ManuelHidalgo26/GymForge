using FluentAssertions;
using GymForge.Infrastructure.Persistence;

namespace GymForge.Integration.Tests;

/// <summary>Barrido de arranque de los residuos de export/import que quedan en %TEMP% si el proceso muere.</summary>
public class DataTransferTempSweepTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gymforge_sweep_" + Guid.NewGuid().ToString("N"));

    public DataTransferTempSweepTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string OldTransferDir(string name)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, name));
        File.WriteAllText(Path.Combine(dir.FullName, "gymforge.db"), "residuo");
        dir.LastWriteTimeUtc = DateTime.UtcNow.AddDays(-2);
        return dir.FullName;
    }

    [Fact]
    public void Sweep_BorraLosResiduosViejos_YRespetaLoDemas()
    {
        var oldTransfer = OldTransferDir("gymforge_transfer_viejo");
        var recentTransfer = Directory.CreateDirectory(Path.Combine(_root, "gymforge_transfer_en_curso")).FullName;
        var unrelated = OldTransferDir("otra_cosa");

        DataTransferService.SweepOrphanTemps(TimeSpan.FromHours(1), _root);

        Directory.Exists(oldTransfer).Should().BeFalse();
        Directory.Exists(recentTransfer).Should().BeTrue("podría ser la operación en curso de otra instancia");
        Directory.Exists(unrelated).Should().BeTrue("no es un residuo de GymForge");
    }

    [Fact]
    public void Sweep_ConTempInexistente_NoLanza()
    {
        var act = () => DataTransferService.SweepOrphanTemps(TimeSpan.FromHours(1), Path.Combine(_root, "no-existe"));

        act.Should().NotThrow();
    }
}
