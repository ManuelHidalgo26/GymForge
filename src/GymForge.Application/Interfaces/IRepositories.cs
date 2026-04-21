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
