using FluentAssertions;
using GymForge.Application;
using GymForge.Application.Interfaces;
using GymForge.Infrastructure;
using GymForge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymForge.Integration.Tests;

/// <summary>CLAUDE.md: SQLite local en WAL. Tiene que quedar activo tras el arranque, incluso en una base preexistente.</summary>
public class JournalModeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gymforge_wal_" + Guid.NewGuid().ToString("N"));

    public JournalModeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best-effort: carpeta temporal */ }
    }

    private string DbPath => Path.Combine(_dir, "gymforge.db");

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton<ICurrentTenant, AmbientTenantProvider>();
        services.AddSingleton<ICurrentUser, AmbientCurrentUser>();
        services.AddInfrastructure($"Data Source={DbPath};Mode=ReadWriteCreate");
        services.AddSingleton<ILocalSettings>(new InMemoryLocalSettings());
        return services.BuildServiceProvider();
    }

    private string JournalMode()
    {
        using var connection = new SqliteConnection($"Data Source={DbPath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode";
        return (string)command.ExecuteScalar()!;
    }

    [Fact]
    public async Task InitialiseDatabase_OnAFreshDatabase_LeavesItInWal()
    {
        await using var provider = BuildProvider();

        await provider.InitialiseDatabaseAsync();

        JournalMode().Should().Be("wal");
    }

    [Fact]
    public async Task InitialiseDatabase_OnAnExistingDatabaseInRollbackMode_SwitchesItToWal()
    {
        await using (var provider = BuildProvider())
            await provider.InitialiseDatabaseAsync();
        SqliteConnection.ClearAllPools();
        using (var connection = new SqliteConnection($"Data Source={DbPath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode = DELETE";
            command.ExecuteScalar();
        }
        JournalMode().Should().Be("delete", "la precondición del test: una base legada en modo rollback");

        await using var restarted = BuildProvider();
        await restarted.InitialiseDatabaseAsync();

        JournalMode().Should().Be("wal");
    }
}
