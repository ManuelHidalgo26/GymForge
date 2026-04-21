using GymForge.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Infrastructure.Persistence;

public class GymForgeDbContext : DbContext
{
    public GymForgeDbContext(DbContextOptions<GymForgeDbContext> options) : base(options) { }

    // Multi-tenancy
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Staff> Staff => Set<Staff>();

    // Members & Memberships
    public DbSet<Member> Members => Set<Member>();
    public DbSet<MembershipType> MembershipTypes => Set<MembershipType>();
    public DbSet<Membership> Memberships => Set<Membership>();

    // Payments
    public DbSet<Charge> Charges => Set<Charge>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();

    // Classes
    public DbSet<ClassDescription> ClassDescriptions => Set<ClassDescription>();
    public DbSet<ClassSchedule> ClassSchedules => Set<ClassSchedule>();
    public DbSet<Booking> Bookings => Set<Booking>();

    // Training
    public DbSet<Exercise> Exercises => Set<Exercise>();
    public DbSet<Routine> Routines => Set<Routine>();
    public DbSet<RoutineDay> RoutineDays => Set<RoutineDay>();
    public DbSet<RoutineItem> RoutineItems => Set<RoutineItem>();
    public DbSet<RoutineItemSet> RoutineItemSets => Set<RoutineItemSet>();
    public DbSet<WorkoutLog> WorkoutLogs => Set<WorkoutLog>();
    public DbSet<BodyMeasurement> BodyMeasurements => Set<BodyMeasurement>();

    // Products & POS
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockBySite> StockBySite => Set<StockBySite>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();

    // Operations
    public DbSet<AccessLog> AccessLogs => Set<AccessLog>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();

    // Audit
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all configurations in this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GymForgeDbContext).Assembly);

        // SQLite: use TEXT for DateOnly
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateOnly) || property.ClrType == typeof(DateOnly?))
                    property.SetColumnType("TEXT");
            }
        }
    }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries<Domain.Entities.BaseEntity>()
            .Where(e => e.State == EntityState.Modified);

        foreach (var entry in entries)
            entry.Entity.UpdatedAt = DateTime.UtcNow;
    }
}
