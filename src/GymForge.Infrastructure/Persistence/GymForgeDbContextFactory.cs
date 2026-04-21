using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GymForge.Infrastructure.Persistence;

/// <summary>Used by EF Core CLI tools (dotnet ef migrations add) to instantiate the DbContext.</summary>
public class GymForgeDbContextFactory : IDesignTimeDbContextFactory<GymForgeDbContext>
{
    public GymForgeDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<GymForgeDbContext>();
        optionsBuilder.UseSqlite("Data Source=gymforge_design.db");
        return new GymForgeDbContext(optionsBuilder.Options);
    }
}
