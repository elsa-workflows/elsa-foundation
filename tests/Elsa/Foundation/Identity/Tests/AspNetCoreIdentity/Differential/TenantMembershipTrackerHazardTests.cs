using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Extensions;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Foundation.Identity.Tests.AspNetCoreIdentity.Differential;

/// <summary>
/// Pins the hazard that makes the reopen in <c>TenantMembershipDifferentialProbes.ProducerOrderingAsync</c>
/// load-bearing, and pins that a fresh context is what removes it.
/// </summary>
/// <remarks>
/// <para>
/// Issue #1204: the ProducerOrdering differential intermittently failed with
/// <c>UNIQUE constraint failed: TenantMemberships.TenantId, TenantMemberships.UserId</c> at roughly
/// 1-in-100, which is far too rare to chase by re-running. The cause is deterministic once the
/// precondition is built directly rather than raced into existence, which is what these two tests do.
/// </para>
/// <para>
/// <see cref="EfCoreTenantMembershipStore.SaveAsync"/> reads, adds when the read came back null, then
/// saves. A tracking query cannot see an <c>Added</c> entity that was never written, so a context
/// carrying one — the state a writer that threw from EF's concurrency detector leaves behind — makes the
/// read return null and a second entity get added for the same key. <c>SaveChangesAsync</c> flushes the
/// whole tracker, so both go out as INSERTs and the second trips <c>IX_TenantMemberships_TenantId_UserId</c>.
/// </para>
/// <para>
/// Shares the SQLite identity collection because the EF registration hard-codes one data source.
/// </para>
/// </remarks>
[Collection(SqliteIdentityFileCollection.Name)]
[Trait("Category", "Sqlite")]
public sealed class TenantMembershipTrackerHazardTests : IAsyncLifetime
{
    private const string UserId = "user-tracker-hazard";

    private ServiceProvider _provider = default!;
    private IServiceScope _scope = default!;
    private ApplicationIdentityDbContext _db = default!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddFoundationAspNetCoreIdentityEntityFrameworkCore();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();

        await _db.Database.EnsureDeletedAsync();
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        _scope.Dispose();
        await _provider.DisposeAsync();
    }

    /// <summary>The failure #1204 reported, produced on demand instead of once every hundred runs.</summary>
    [Fact]
    public async Task Saving_through_a_context_that_still_tracks_an_unsaved_membership_trips_the_unique_index()
    {
        TrackUnsavedMembership();

        var exception = await Record.ExceptionAsync(() => SaveAsync(_db));

        var failure = Assert.IsType<DbUpdateException>(exception);
        Assert.Contains(
            "UNIQUE constraint failed: TenantMemberships.TenantId, TenantMemberships.UserId",
            failure.InnerException?.Message);
    }

    /// <summary>
    /// The guard. A new scope yields a new <see cref="ApplicationIdentityDbContext"/> with an empty change
    /// tracker, which is the one precondition the failure above needs — and is exactly what the probe's
    /// <c>CloseAsync</c>/<c>OpenAsync</c> pair produces before the serial re-drive. Removing that reopen
    /// puts the re-drive back on the faulted context and reopens issue #1204.
    /// </summary>
    [Fact]
    public async Task Re_driving_the_same_write_through_a_reopened_context_succeeds()
    {
        TrackUnsavedMembership();

        using var reopened = _provider.CreateScope();
        var db = reopened.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();

        var exception = await Record.ExceptionAsync(() => SaveAsync(db));

        Assert.Null(exception);
        Assert.Equal(1, await db.TenantMemberships.CountAsync());

        // The probe reads back after its re-drive, so prove the surviving row is the right one and
        // not merely that exactly one row exists.
        var readBack = await new EfCoreTenantMembershipStore(db)
            .FindAsync(TenantMembershipDifferentialTarget.TenantId, UserId);

        Assert.NotNull(readBack);
        Assert.Equal("role-00", Assert.Single(readBack.RoleIds));
    }

    /// <summary>The state a concurrent writer leaves behind when its <c>SaveChangesAsync</c> throws after its <c>Add</c>.</summary>
    private void TrackUnsavedMembership() =>
        _db.TenantMemberships.Add(new TenantMembershipEntity
        {
            Id = Guid.NewGuid().ToString("n"),
            TenantId = TenantMembershipDifferentialTarget.TenantId,
            UserId = UserId
        });

    private static Task SaveAsync(ApplicationIdentityDbContext db) =>
        new EfCoreTenantMembershipStore(db).SaveAsync(new(
            TenantMembershipDifferentialTarget.TenantId, UserId, TenantMembershipStatus.Active,
            new HashSet<string> { "role-00" }, new HashSet<string>())).AsTask();
}
