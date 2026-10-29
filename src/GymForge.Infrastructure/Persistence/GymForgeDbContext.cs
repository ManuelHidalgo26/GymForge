using System.Reflection;
using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Infrastructure.Persistence;

public class GymForgeDbContext : DbContext
{
    // Tenant vivo para los query filters: EF lo lee en cada ejecución (no queda fijo en el modelo cacheado).
    private readonly ICurrentTenant _tenant;

    public GymForgeDbContext(DbContextOptions<GymForgeDbContext> options, ICurrentTenant tenant)
        : base(options) => _tenant = tenant;

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
    public DbSet<MemberCreditMovement> MemberCreditMovements => Set<MemberCreditMovement>();

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
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

    // Audit
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all configurations in this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GymForgeDbContext).Assembly);

        ApplyTenantFilters(modelBuilder);

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

    /// <summary>
    /// Aislamiento multi-tenant por defecto: toda lectura LINQ se filtra por el tenant activo según el
    /// marker de la entidad (ISiteScoped = empresa+sede, ITenantScoped = empresa). Company, los hijos sin
    /// CompanyId (se alcanzan por su padre ya filtrado) y las escrituras no se filtran. Para una lectura
    /// cross-tenant legítima hay que pedir IgnoreQueryFilters() de forma explícita.
    /// Al crear el modelo EF avisa (PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning)
    /// de esos hijos sin filtro propio; es esperado: nunca existen fuera de un padre del mismo tenant.
    /// </summary>
    private void ApplyTenantFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            var clrType = entityType.ClrType;
            if (typeof(ISiteScoped).IsAssignableFrom(clrType))
                InvokeFilter(nameof(ApplySiteFilter), clrType, modelBuilder);
            else if (typeof(ITenantScoped).IsAssignableFrom(clrType))
                InvokeFilter(nameof(ApplyCompanyFilter), clrType, modelBuilder);
        }

        // Ejercicios: biblioteca global (sin tenant) + los propios de la empresa.
        modelBuilder.Entity<Exercise>()
            .HasQueryFilter(e => e.TenantId == null || e.TenantId == _tenant.CompanyId);
    }

    private void InvokeFilter(string filterMethodName, Type entityClrType, ModelBuilder modelBuilder) =>
        typeof(GymForgeDbContext)
            .GetMethod(filterMethodName, BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(entityClrType)
            .Invoke(this, [modelBuilder]);

    // Los filtros leen _tenant (miembro de instancia): EF lo parametriza por ejecución y cachea un único modelo.
    private void ApplyCompanyFilter<T>(ModelBuilder modelBuilder) where T : class, ITenantScoped =>
        modelBuilder.Entity<T>().HasQueryFilter(e => e.CompanyId == _tenant.CompanyId);

    private void ApplySiteFilter<T>(ModelBuilder modelBuilder) where T : class, ISiteScoped =>
        modelBuilder.Entity<T>().HasQueryFilter(
            e => e.CompanyId == _tenant.CompanyId && e.SiteId == _tenant.SiteId);

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
