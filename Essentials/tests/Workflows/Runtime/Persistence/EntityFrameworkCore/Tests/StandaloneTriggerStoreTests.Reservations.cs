using System.Data.Common;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class StandaloneTriggerStoreTests
{
    [Fact]
    public async Task Reservation_replacement_rolls_back_conflicting_ownership_and_reopens_with_the_original_exact_tenant()
    {
        await using var database = await Database.CreateAsync();
        var otherTenant = new TriggerScope(Scope.Persistence, "other-tenant");
        RuntimeIngressReservation Claim(TriggerScope scope, string owner, string route) =>
            new(RuntimeIngressReservation.CreateIdentity(scope.Persistence, "Http", route), scope, "Http", route,
                RuntimeIngressReservation.StandaloneTriggerOwner, owner, 1, 1, new Dictionary<string, string>());
        async Task Replace(TriggerScope scope, string owner, params RuntimeIngressReservation[] claims)
        {
            await using var fixture = database.Open(scope);
            var store = new EfIngressReservationStore(fixture.Context, new Access(scope));
            await using var transaction = await fixture.Context.Database.BeginTransactionAsync();
            await store.StageOwnerAsync(scope, RuntimeIngressReservation.StandaloneTriggerOwner, owner, claims, default);
            await fixture.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        var original = Claim(Scope, "original", "shared");
        await Replace(Scope, "original", original);
        await Replace(otherTenant, "other", Claim(otherTenant, "other", "other-route"));
        var error = await Assert.ThrowsAsync<TriggerOperationException>(() => Replace(otherTenant, "other", Claim(otherTenant, "other", "shared")));
        Assert.Equal("route-conflict", error.Code);

        await using var restarted = database.Open();
        var reader = new EfIngressReservationStore(restarted.Context, new Access(Scope));
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(await reader.FindAsync(Scope, "Http", "shared")));
        Assert.Null(await reader.FindAsync(otherTenant, "Http", "shared"));
        Assert.NotNull(await reader.FindAsync(otherTenant, "Http", "other-route"));
        Assert.Equal(2, (await reader.ListInScopeAsync(Scope.Persistence, new("Http"))).Items.Count);
        Assert.Single((await reader.ListAsync(Scope, new("Http"))).Items);
        await Replace(Scope, "original");
        await Replace(otherTenant, "other", Claim(otherTenant, "other", "shared"));
        Assert.Null(await reader.FindAsync(otherTenant, "Http", "other-route"));
        Assert.Equal("other", (await reader.FindAsync(otherTenant, "Http", "shared"))!.OwnerId);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("contract")]
    public async Task Malformed_stored_resource_is_a_contextual_domain_failure(string corruption)
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open();
        await fixture.Store.WriteAsync(Scope, Write(Resource("http"), []));
        var content = corruption == "json" ? "{" : "{\"id\":\"\"}";
        await fixture.Context.StandaloneTriggers.ExecuteUpdateAsync(set => set.SetProperty(row => row.ContentJson, content));
        var error = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Store.FindAsync(Scope, "http").AsTask());
        Assert.Equal("conflict", error.Code);
        Assert.Equal(Scope.Persistence.Value, error.Fields!["scope"]);
        Assert.Equal("http", error.Fields["identity"]);
        if (corruption == "json") Assert.IsType<JsonException>(error.InnerException);
        else Assert.IsType<InvalidDataException>(error.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Physical_database_failure_retains_the_cause_at_both_public_ports(bool executionStrategyWrapper)
    {
        await using var database = await Database.CreateAsync();
        var fault = new FailingRead(executionStrategyWrapper);
        await using var fixture = database.Open(interceptors: [fault]);
        var resource = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Store.FindAsync(Scope, "http").AsTask());
        Assert.Equal("unavailable", resource.Code);
        Assert.Same(fault.Cause, resource.InnerException);
        var reservations = new EfIngressReservationStore(fixture.Context, new Access(Scope));
        var reservation = await Assert.ThrowsAsync<TriggerOperationException>(() => reservations.FindAsync(Scope, "Http", "route").AsTask());
        Assert.Equal("unavailable", reservation.Code);
        Assert.Same(fault.Cause, reservation.InnerException);
        Assert.Equal("route", reservation.Fields!["identity"]);
    }

    [Fact]
    public async Task Exact_type_and_slot_filters_preserve_page_boundaries_under_case_insensitive_database_collation()
    {
        await using var database = await Database.CreateAsync(new CaseInsensitiveProjectionColumns());
        await using var fixture = database.Open();
        await fixture.Store.WriteAsync(Scope, Write(Resource("0-near", typeKey: "AG"), [], "near", "near"));
        await fixture.Store.WriteAsync(Scope, Write(Resource("1-exact", typeKey: "Aa"), [], "exact", "exact"));
        await fixture.Store.WriteAsync(Scope, Write(Resource("slots"),
            [Subscription("0-near", "slots", slot: "AG"), Subscription("1-exact", "slots", slot: "Aa")], "slots", "slots"));
        // Prove this physical database actually treats the distinct projections as equal.
        var projection = EfRuntimeOperationalStoreSupport.Encode("Aa");
        Assert.Equal(2, await fixture.Context.StandaloneTriggers.CountAsync(row => row.TypeKey == projection));
        Assert.Equal(2, await fixture.Context.TriggerSubscriptions.CountAsync(row => row.SlotKey == projection));
        var resources = await fixture.Store.ListAsync(Scope, new(1, TypeKey: "Aa"));
        Assert.Equal("1-exact", Assert.Single(resources.Items).Id);
        Assert.Null(resources.Next);
        var subscriptions = await fixture.Store.ListSubscriptionsAsync(Scope, new(DefinitionId: "definition", SlotName: "Aa", Limit: 1));
        Assert.Equal("1-exact", Assert.Single(subscriptions.Items).Id);
        Assert.Null(subscriptions.Next);
    }

    private sealed class CaseInsensitiveProjectionColumns : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            command.CommandText = command.CommandText
                .Replace("\"TypeKey\" TEXT NOT NULL", "\"TypeKey\" TEXT COLLATE NOCASE NOT NULL", StringComparison.Ordinal)
                .Replace("\"SlotKey\" TEXT NOT NULL", "\"SlotKey\" TEXT COLLATE NOCASE NOT NULL", StringComparison.Ordinal);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailingRead(bool wrapped) : DbCommandInterceptor
    {
        internal Exception Cause { get; } = wrapped
            ? new InvalidOperationException("execution strategy rejected the physical read", new SqliteException("physical read failed", 1))
            : new SqliteException("physical read failed", 1);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<InterceptionResult<DbDataReader>>(Cause);
    }
}
