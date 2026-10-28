using FluentAssertions;
using GymForge.Infrastructure.Persistence;

namespace GymForge.Integration.Tests;

public class AmbientTenantProviderTests
{
    private static readonly Guid CompanyA = Guid.NewGuid();
    private static readonly Guid SiteA = Guid.NewGuid();
    private static readonly Guid CompanyB = Guid.NewGuid();
    private static readonly Guid SiteB = Guid.NewGuid();

    [Fact]
    public void WithoutSet_TheTenantIsEmpty()
    {
        var tenant = new AmbientTenantProvider();

        tenant.CompanyId.Should().BeEmpty();
        tenant.SiteId.Should().BeEmpty();
    }

    [Fact]
    public void Set_ChangesTheBaseTenant()
    {
        var tenant = new AmbientTenantProvider();

        tenant.Set(CompanyA, SiteA);

        tenant.CompanyId.Should().Be(CompanyA);
        tenant.SiteId.Should().Be(SiteA);
    }

    [Fact]
    public void BeginScope_OverridesTheTenant_AndDisposeRestoresIt()
    {
        var tenant = new AmbientTenantProvider();
        tenant.Set(CompanyA, SiteA);

        using (tenant.BeginScope(CompanyB, SiteB))
        {
            tenant.CompanyId.Should().Be(CompanyB);
            tenant.SiteId.Should().Be(SiteB);
        }

        tenant.CompanyId.Should().Be(CompanyA);
        tenant.SiteId.Should().Be(SiteA);
    }

    [Fact]
    public void NestedScopes_RestoreTheirImmediateParent()
    {
        var tenant = new AmbientTenantProvider();
        tenant.Set(CompanyA, SiteA);

        using (tenant.BeginScope(CompanyB, SiteB))
        {
            var innerSite = Guid.NewGuid();
            using (tenant.BeginScope(CompanyB, innerSite))
                tenant.SiteId.Should().Be(innerSite);

            tenant.SiteId.Should().Be(SiteB);
        }

        tenant.SiteId.Should().Be(SiteA);
    }

    [Fact]
    public void Dispose_IsIdempotent_AndDoesNotClobberAnOuterScope()
    {
        var tenant = new AmbientTenantProvider();
        tenant.Set(CompanyA, SiteA);
        using var outer = tenant.BeginScope(CompanyB, SiteB);

        var inner = tenant.BeginScope(CompanyA, SiteA);
        inner.Dispose();
        inner.Dispose();

        tenant.CompanyId.Should().Be(CompanyB);
    }

    [Fact]
    public async Task Scope_SurvivesAwait()
    {
        var tenant = new AmbientTenantProvider();

        using (tenant.BeginScope(CompanyB, SiteB))
        {
            await Task.Delay(10);
            tenant.CompanyId.Should().Be(CompanyB);
        }
    }

    [Fact]
    public async Task ConcurrentFlows_DoNotSeeEachOthersScope()
    {
        var tenant = new AmbientTenantProvider();
        tenant.Set(CompanyA, SiteA);

        async Task<Guid> ReadSiteInsideScopeAsync(Guid company, Guid site, int delayMs)
        {
            using var scope = tenant.BeginScope(company, site);
            await Task.Delay(delayMs);
            return tenant.SiteId;
        }

        var results = await Task.WhenAll(
            ReadSiteInsideScopeAsync(CompanyA, SiteA, delayMs: 30),
            ReadSiteInsideScopeAsync(CompanyB, SiteB, delayMs: 10));

        results.Should().Equal(SiteA, SiteB);
        tenant.SiteId.Should().Be(SiteA);
    }
}
