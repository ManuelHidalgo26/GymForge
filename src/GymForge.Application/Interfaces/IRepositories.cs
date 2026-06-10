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
    Task<Member?> FindByEmailAsync(string email, Guid companyId, CancellationToken ct = default);
    Task<IReadOnlyList<Member>> SearchAsync(string query, Guid companyId, Guid siteId, int take = 50, CancellationToken ct = default);
    Task<IReadOnlyList<Member>> GetPagedAsync(Guid companyId, Guid siteId, int page, int pageSize, MemberStatus? status = null, CancellationToken ct = default);
    Task<int> CountAsync(Guid companyId, Guid siteId, MemberStatus? status = null, CancellationToken ct = default);
}

public interface IMembershipRepository : IRepository<Membership>
{
    Task<Membership?> GetCurrentActiveAsync(Guid memberId, CancellationToken ct = default);
    Task<IReadOnlyList<Membership>> GetByMemberAsync(Guid memberId, CancellationToken ct = default);
    Task<IReadOnlyList<Membership>> GetExpiringAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

public interface IChargeRepository : IRepository<Charge>
{
    Task<decimal> SumOutstandingAsync(Guid memberId, CancellationToken ct = default);
    Task<IReadOnlyList<Charge>> GetPendingAsync(Guid memberId, CancellationToken ct = default);
    Task<IReadOnlyList<Charge>> GetOverdueAsync(Guid companyId, DateOnly asOf, CancellationToken ct = default);
}

public interface IPaymentRepository
{
    Task AddAsync(Payment payment, CancellationToken ct = default);
    Task<decimal> SumReceivedAsync(Guid companyId, Guid siteId, DateTime from, DateTime to, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IMembershipTypeRepository
{
    Task<MembershipType?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<MembershipType>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
}

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Product>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
    Task<StockBySite?> GetStockAsync(Guid productId, Guid siteId, CancellationToken ct = default);
}

public interface ISaleRepository
{
    Task AddAsync(Sale sale, CancellationToken ct = default);
    void UpdateStock(StockBySite stock);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IStaffRepository
{
    Task<Staff?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Staff>> GetActiveByCompanyAsync(Guid companyId, CancellationToken ct = default);
}

public interface ISiteRepository
{
    Task<IReadOnlyList<Company>> GetCompaniesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Site>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
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
    Task<AccessLog?> GetLastAsync(Guid memberId, int doorId, CancellationToken ct = default);
    Task<IReadOnlyList<AccessLog>> GetByMemberAsync(Guid memberId, int take = 50, CancellationToken ct = default);
    Task<IReadOnlyList<AccessLog>> GetBySiteAsync(Guid siteId, DateTime from, DateTime to, CancellationToken ct = default);
}

public interface IEventBus
{
    Task PublishAsync<T>(T domainEvent, CancellationToken ct = default) where T : class;
}

public interface IClock
{
    DateTime Now { get; }
    DateOnly Today { get; }
}
