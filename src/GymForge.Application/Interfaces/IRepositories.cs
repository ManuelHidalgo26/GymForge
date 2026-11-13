using GymForge.Domain.Entities;
using GymForge.Domain.Enums;

namespace GymForge.Application.Interfaces;

public interface IRepository<T> where T : Domain.Entities.BaseEntity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    void Update(T entity);
    void Remove(T entity);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IMemberRepository : IRepository<Member>
{
    Task<Member?> FindByDocumentAsync(DocumentType docType, string docNumber, Guid companyId, CancellationToken ct = default);
    Task<Member?> FindByTagSerialAsync(string tagSerial, Guid companyId, CancellationToken ct = default);
    /// <summary>
    /// ¿Algún socio de la empresa (de cualquier sede) ya tiene esa credencial? La unicidad es por empresa y no por
    /// sede, así que ignora el filtro de sede. <paramref name="exceptMemberId"/> excluye al propio socio.
    /// </summary>
    Task<bool> IsTagSerialInUseAsync(Guid companyId, string tagSerial, Guid? exceptMemberId = null, CancellationToken ct = default);
    Task<Member?> FindByEmailAsync(string email, Guid companyId, CancellationToken ct = default);
    Task<IReadOnlyList<Member>> SearchAsync(string query, Guid companyId, Guid siteId, int take = 50, CancellationToken ct = default);
    Task<IReadOnlyList<Member>> GetPagedAsync(Guid companyId, Guid siteId, int page, int pageSize, MemberStatus? status = null, CancellationToken ct = default);
    Task<int> CountAsync(Guid companyId, Guid siteId, MemberStatus? status = null, CancellationToken ct = default);
    /// <summary>Total de socios de la empresa (todas las sedes) — para el límite de licencia.</summary>
    Task<int> CountByCompanyAsync(Guid companyId, CancellationToken ct = default);
    /// <summary>Socios de la empresa (todas las sedes) en un estado dado — para el mantenimiento diario.</summary>
    Task<IReadOnlyList<Member>> GetByStatusAsync(Guid companyId, MemberStatus status, CancellationToken ct = default);
    /// <summary>True si el socio tiene movimientos (membresías, cobros, pagos o accesos).</summary>
    Task<bool> HasActivityAsync(Guid memberId, CancellationToken ct = default);
    /// <summary>
    /// Socios por estado, a hoy (reporte de altas y bajas). <paramref name="siteId"/> null = TODA la empresa:
    /// ignora el filtro de sede pero queda acotado a <paramref name="companyId"/>. Solo trae los estados con socios.
    /// </summary>
    Task<IReadOnlyDictionary<MemberStatus, int>> CountByStatusAsync(Guid companyId, Guid? siteId, CancellationToken ct = default);
    /// <summary>Socios cuya fecha de alta cae en [from, to] (ambos incluidos). <paramref name="siteId"/> null = toda la empresa.</summary>
    Task<IReadOnlyList<Member>> GetJoinedBetweenAsync(Guid companyId, Guid? siteId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

public interface IMembershipRepository : IRepository<Membership>
{
    Task<Membership?> GetCurrentActiveAsync(Guid memberId, CancellationToken ct = default);
    Task<IReadOnlyList<Membership>> GetByMemberAsync(Guid memberId, CancellationToken ct = default);
    Task<IReadOnlyList<Membership>> GetExpiringAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken ct = default);
    /// <summary>Membresías activas de la empresa (todas las sedes) cuyo vencimiento ya pasó a la fecha dada.</summary>
    Task<IReadOnlyList<Membership>> GetActiveExpiredAsync(Guid companyId, DateOnly asOf, CancellationToken ct = default);
    /// <summary>Membresías congeladas de la empresa (todas las sedes) cuyo congelamiento ya terminó a la fecha dada.</summary>
    Task<IReadOnlyList<Membership>> GetFreezeEndedAsync(Guid companyId, DateOnly asOf, CancellationToken ct = default);
    /// <summary>
    /// Membresías canceladas con la baja en [from, to) (UTC), con el socio y el plan. <paramref name="siteId"/> null =
    /// TODA la empresa: ignora el filtro de sede pero queda acotado a <paramref name="companyId"/>.
    /// </summary>
    Task<IReadOnlyList<Membership>> GetCancelledBetweenAsync(Guid companyId, Guid? siteId, DateTime from, DateTime to, CancellationToken ct = default);
    /// <summary>
    /// Membresías vendidas con comisión (CommissionAmount &gt; 0) con el alta en [from, to) (UTC). <paramref name="siteId"/>
    /// null = TODA la empresa: ignora el filtro de sede pero queda acotado a <paramref name="companyId"/>.
    /// </summary>
    Task<IReadOnlyList<Membership>> GetSoldBetweenAsync(Guid companyId, Guid? siteId, DateTime from, DateTime to, CancellationToken ct = default);
}

public interface IChargeRepository : IRepository<Charge>
{
    Task<decimal> SumOutstandingAsync(Guid memberId, CancellationToken ct = default);
    Task<IReadOnlyList<Charge>> GetPendingAsync(Guid memberId, CancellationToken ct = default);
    /// <summary>
    /// Mora de la empresa completa (todas las sedes): SOLO para dunning y mantenimiento. Las lecturas de UI
    /// (Dashboard, Cobros) usan <see cref="GetOverdueBySiteAsync"/>: el cobro exige que el cargo sea de la sede activa.
    /// Incluye el socio.
    /// </summary>
    Task<IReadOnlyList<Charge>> GetOverdueAsync(Guid companyId, DateOnly asOf, CancellationToken ct = default);
    /// <summary>
    /// Mora de UNA sede (pantalla de morosos): empresa y sede explícitas, no depende del tenant ambiente.
    /// Incluye el socio. Mismos criterios que <see cref="GetOverdueAsync"/>.
    /// </summary>
    Task<IReadOnlyList<Charge>> GetOverdueBySiteAsync(Guid companyId, Guid siteId, DateOnly asOf, CancellationToken ct = default);
}

public interface IPaymentRepository
{
    Task AddAsync(Payment payment, CancellationToken ct = default);
    /// <summary>Pago por id con sus asignaciones cargadas (para armar el recibo).</summary>
    Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<decimal> SumReceivedAsync(Guid companyId, Guid siteId, DateTime from, DateTime to, CancellationToken ct = default);
    /// <summary>Pagos completados del período, con el socio cargado, más recientes primero.</summary>
    Task<IReadOnlyList<Payment>> GetByPeriodAsync(Guid companyId, Guid siteId, DateTime from, DateTime to, CancellationToken ct = default);
    /// <summary>
    /// Pagos completados del período para desagregar la recaudación. <paramref name="siteId"/> null = TODA la empresa
    /// (reporte de dueño): ignora el filtro de sede pero queda acotado a <paramref name="companyId"/>. Incluye
    /// asignaciones → cargo → membresía → plan.
    /// </summary>
    Task<IReadOnlyList<Payment>> GetForBreakdownAsync(Guid companyId, Guid? siteId, DateTime from, DateTime to, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>
/// Cuenta corriente del socio (libro mayor append-only): solo se agregan movimientos, nunca se
/// modifican ni se borran. El saldo es la suma de los montos.
/// </summary>
public interface IMemberCreditRepository
{
    Task AddAsync(MemberCreditMovement movement, CancellationToken ct = default);
    /// <summary>Saldo a favor del socio = suma de sus movimientos (filtrado por tenant). Se espera >= 0.</summary>
    Task<decimal> GetBalanceAsync(Guid memberId, CancellationToken ct = default);
    /// <summary>Movimientos del socio, del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<MemberCreditMovement>> GetByMemberAsync(Guid memberId, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IMembershipTypeRepository
{
    Task<MembershipType?> GetByIdAsync(Guid id, CancellationToken ct = default);
    /// <summary>Solo planes activos (para vender).</summary>
    Task<IReadOnlyList<MembershipType>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
    /// <summary>Todos los planes, incluidos inactivos (para administrar).</summary>
    Task<IReadOnlyList<MembershipType>> GetAllByCompanyAsync(Guid companyId, CancellationToken ct = default);
    Task AddAsync(MembershipType plan, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default);
    /// <summary>Solo productos activos (para vender).</summary>
    Task<IReadOnlyList<Product>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
    /// <summary>Todos los productos con su stock, incluidos inactivos (para administrar).</summary>
    Task<IReadOnlyList<Product>> GetAllByCompanyAsync(Guid companyId, CancellationToken ct = default);
    Task<Product?> GetBySkuAsync(Guid companyId, string sku, CancellationToken ct = default);
    /// <summary>Producto activo (a la venta) con ese código de barras, o null.</summary>
    Task<Product?> GetByBarcodeAsync(Guid companyId, string barcode, CancellationToken ct = default);
    Task<StockBySite?> GetStockAsync(Guid productId, Guid siteId, CancellationToken ct = default);
    Task AddAsync(Product product, CancellationToken ct = default);
    Task AddStockAsync(StockBySite stock, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface ISaleRepository
{
    Task AddAsync(Sale sale, CancellationToken ct = default);
    /// <summary>Venta por id con sus líneas cargadas (para armar el recibo de venta).</summary>
    Task<Sale?> GetByIdAsync(Guid id, CancellationToken ct = default);
    void UpdateStock(StockBySite stock);
    /// <summary>
    /// Líneas de venta con comisión (vendedor y monto &gt; 0) de ventas en [from, to) (UTC), con su venta cargada.
    /// <paramref name="siteId"/> null = TODA la empresa: ignora el filtro de sede pero queda acotado a <paramref name="companyId"/>.
    /// </summary>
    Task<IReadOnlyList<SaleLine>> GetCommissionLinesBetweenAsync(Guid companyId, Guid? siteId, DateTime from, DateTime to, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IStaffRepository
{
    Task<Staff?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Staff>> GetActiveByCompanyAsync(Guid companyId, CancellationToken ct = default);
    /// <summary>Todo el personal de la empresa, también el dado de baja (un reporte histórico necesita sus nombres).</summary>
    Task<IReadOnlyList<Staff>> GetAllByCompanyAsync(Guid companyId, CancellationToken ct = default);
    void Update(Staff staff);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface ISiteRepository
{
    Task<IReadOnlyList<Company>> GetCompaniesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Site>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
    Task<Company?> GetCompanyAsync(Guid companyId, CancellationToken ct = default);
    Task<Site?> GetSiteAsync(Guid siteId, CancellationToken ct = default);
    /// <summary>Agrega la empresa (con sus sedes y staff en el grafo) al contexto. Onboarding.</summary>
    Task AddCompanyAsync(Company company, CancellationToken ct = default);
    Task AddSiteAsync(Site site, CancellationToken ct = default);
    void RemoveSite(Site site);
    /// <summary>True si la sede tiene datos (socios, turnos de caja o pagos).</summary>
    Task<bool> SiteHasDataAsync(Guid siteId, CancellationToken ct = default);
    Task<int> CountActiveSitesAsync(Guid companyId, CancellationToken ct = default);
    /// <summary>Stagea una fila de auditoría explícita (solo INSERT); la persiste el próximo SaveChangesAsync.</summary>
    Task AppendAuditLogAsync(AuditLog log, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IShiftRepository
{
    Task<Shift?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Shift?> GetOpenForSiteAsync(Guid siteId, CancellationToken ct = default);
    Task AddAsync(Shift shift, CancellationToken ct = default);
    /// <summary>
    /// Registra el movimiento como entidad nueva en el contexto. Necesario porque
    /// BaseEntity pre-asigna el Id: si el movimiento solo se agrega a la colección
    /// del shift trackeado, EF lo presume existente (Modified) y el UPDATE falla.
    /// </summary>
    Task AddMovementAsync(CashMovement movement, CancellationToken ct = default);
    void Update(Shift shift);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IAccessLogRepository
{
    Task AppendAsync(AccessLog log, CancellationToken ct = default);
    /// <summary>
    /// Último acceso CONCEDIDO del socio en la puerta (base del anti-passback). Los denegados
    /// se excluyen: quedan logueados como entradas pero nadie llegó a pasar.
    /// </summary>
    Task<AccessLog?> GetLastGrantedAsync(Guid memberId, int doorId, CancellationToken ct = default);
    Task<IReadOnlyList<AccessLog>> GetByMemberAsync(Guid memberId, int take = 50, CancellationToken ct = default);
    Task<IReadOnlyList<AccessLog>> GetBySiteAsync(Guid siteId, DateTime from, DateTime to, CancellationToken ct = default);
}

/// <summary>
/// Registro de recordatorios de cobranza (append-only): solo se agregan avisos, nunca se modifican ni
/// se borran. Las lecturas por socio pasan por el filtro de tenant (empresa + sede).
/// </summary>
public interface INotificationLogRepository
{
    Task AppendAsync(NotificationLog log, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Avisos del socio, del más reciente al más antiguo. Filtrado por tenant.</summary>
    Task<IReadOnlyList<NotificationLog>> GetByMemberAsync(Guid memberId, int take = 50, CancellationToken ct = default);

    /// <summary>
    /// Último recordatorio MANUAL (iniciado por el operador) de cada socio dado, en una sola lectura agrupada.
    /// Los avisos automáticos no cuentan: hoy el sender solo loguea y no le llegó nada al socio. Los socios sin
    /// recordatorio manual no figuran. Filtrado por tenant (empresa + sede).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, DateTime>> GetLastManualByMembersAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken ct = default);

    /// <summary>
    /// Dedup del job: ¿ya se envió ESTA etapa para ESTE cargo? Company-wide a propósito (el job corre
    /// sin sede): IgnoreQueryFilters() + CompanyId explícito, igual que <see cref="IChargeRepository.GetOverdueAsync"/>.
    /// </summary>
    Task<bool> WasSentAsync(Guid companyId, Guid chargeId, int stageDaysOverdue, CancellationToken ct = default);
}

public interface IClassRepository
{
    Task<IReadOnlyList<ClassDescription>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
    Task<ClassDescription?> GetClassAsync(Guid classDescriptionId, CancellationToken ct = default);
    Task AddAsync(ClassDescription cls, CancellationToken ct = default);

    // Horarios (ClassSchedule)
    Task AddScheduleAsync(ClassSchedule schedule, CancellationToken ct = default);
    /// <summary>Horario por id con su clase y reservas cargadas.</summary>
    Task<ClassSchedule?> GetScheduleAsync(Guid scheduleId, CancellationToken ct = default);
    /// <summary>Horarios de la sede en un rango, con clase y reservas (para contar ocupación).</summary>
    Task<IReadOnlyList<ClassSchedule>> GetSchedulesAsync(
        Guid companyId, Guid siteId, DateTime from, DateTime to, CancellationToken ct = default);

    // Reservas (Booking)
    Task AddBookingAsync(Booking booking, CancellationToken ct = default);
    Task<Booking?> GetBookingAsync(Guid bookingId, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IExerciseRepository
{
    /// <summary>Busca en la biblioteca global + ejercicios del tenant.</summary>
    Task<IReadOnlyList<Exercise>> SearchAsync(
        string? query, MuscleGroup? muscle, int take = 300, CancellationToken ct = default);
    Task<Exercise?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Exercise exercise, CancellationToken ct = default);
    void Remove(Exercise exercise);
    /// <summary>True si alguna rutina usa el ejercicio (no se puede borrar).</summary>
    Task<bool> IsInUseAsync(Guid exerciseId, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IRoutineRepository
{
    /// <summary>Rutinas del socio con sus días (para contar), sin items.</summary>
    Task<IReadOnlyList<Routine>> GetByMemberAsync(Guid companyId, Guid memberId, CancellationToken ct = default);
    /// <summary>Rutina completa: días → items → ejercicio + series.</summary>
    Task<Routine?> GetDetailAsync(Guid routineId, CancellationToken ct = default);
    /// <summary>Rutina con sus días (para validar tenant y numerar el día nuevo).</summary>
    Task<Routine?> GetWithDaysAsync(Guid routineId, CancellationToken ct = default);
    /// <summary>Día con su rutina (tenant) y sus items (para numerar el item nuevo).</summary>
    Task<RoutineDay?> GetDayAsync(Guid dayId, CancellationToken ct = default);
    Task AddRoutineAsync(Routine routine, CancellationToken ct = default);
    Task AddDayAsync(RoutineDay day, CancellationToken ct = default);
    Task AddItemAsync(RoutineItem item, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IEventBus
{
    Task PublishAsync<T>(T domainEvent, CancellationToken ct = default) where T : class;
}

public interface IClock
{
    DateTime Now { get; }

    /// <summary>
    /// Fecha UTC. NO es el "hoy" del negocio: para vigencias, vencimientos o mora usar
    /// <see cref="Services.ICompanyCalendar"/>, que respeta la zona horaria de la empresa.
    /// </summary>
    DateOnly Today { get; }
}

/// <summary>Resguardo de la base local. Best-effort: nunca lanza.</summary>
public interface IDatabaseBackup
{
    /// <summary>
    /// Crea un backup ya (p. ej. al cerrar caja). El motivo va al log. Si hay un destino externo
    /// configurado (<see cref="ILocalSettings.OffsiteBackupDir"/>) también lo copia ahí.
    /// Devuelve true si el backup local quedó hecho (la copia externa no cuenta); false si no se pudo crear.
    /// Nunca lanza: quien necesite un respaldo antes de mutar decide qué hacer con el false.
    /// </summary>
    bool BackupNow(string reason = "manual");

    /// <summary>"Copiar ahora": crea un backup nuevo y lo copia ya al destino externo, informando cómo salió.</summary>
    OffsiteBackupOutcome BackupOffsiteNow();

    /// <summary>
    /// Nombres de las copias externas anteriores al cifrado (bases SQLite EN CLARO) que quedaron en el destino externo.
    /// Vacío si no hay destino, no está disponible o no se pudo leer: ante la duda no se ofrece borrar nada.
    /// </summary>
    IReadOnlyList<string> FindLegacyPlainOffsiteCopies();

    /// <summary>
    /// Borra exactamente las copias que devuelve <see cref="FindLegacyPlainOffsiteCopies"/> (nunca los .gfbak cifrados
    /// ni los backups locales). Es destructivo: solo debe invocarse tras la confirmación explícita del usuario.
    /// Devuelve los nombres de las que se borraron.
    /// </summary>
    IReadOnlyList<string> DeleteLegacyPlainOffsiteCopies();
}

/// <summary>Resultado de la copia al destino externo (pendrive, red, carpeta sincronizada).</summary>
public enum OffsiteBackupOutcome
{
    /// <summary>No hay carpeta de destino configurada: no hay nada que copiar.</summary>
    NotConfigured,
    Copied,
    /// <summary>Destino no disponible (pendrive desenchufado, red caída, sin permisos): el backup local sí quedó hecho.</summary>
    Failed,
    /// <summary>No se pudo crear el backup en esta PC (disco lleno, base inaccesible): no hay nada que copiar.</summary>
    LocalBackupFailed,
    /// <summary>Falta la frase de acceso del respaldo: la copia externa se omite (nunca sale en claro). El backup local sí quedó hecho.</summary>
    MissingPassphrase,
}
