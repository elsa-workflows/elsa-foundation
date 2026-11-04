using System.Data.Common;
using System.Net;
using System.Text.Json;
using Elsa.Foundation.Identity.Core.Security;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests.ManagedOidc;

public sealed partial class ExternalAuthenticationHttpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HTTP_unlink_storage_failure_reports_unavailable_without_false_commit_claims_and_can_be_refreshed(bool acknowledgementLost)
    {
        var fault = new LinkRemovalFailure(acknowledgementLost); var events = new CapturedSecurityEvents();
        await using var host = await HttpHost.Start(services =>
        {
            services.AddSingleton<IAuthEventSink>(events);
            services.AddDbContext<IdentityIamSqliteDbContext>((_, builder) => builder.AddInterceptors(fault));
        });
        using var administrator = host.Browser(); using var company = host.Browser(); using var partner = host.Browser();
        await host.LoginAdministrator(administrator); var user = await SignInCompanyAndPartner(host, company, partner);
        var state = await AccessDocument(administrator, $"/users/{user}/links"); fault.Armed = true;
        using var response = await ManagementPost(host, administrator, $"/users/{user}/links/remove", RemoveRequest(state, "company"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        using (var json = JsonDocument.Parse(text))
        {
            Assert.Equal("link-removal-unavailable", json.RootElement.GetProperty("error").GetString());
            Assert.False(json.RootElement.TryGetProperty("removed", out _));
        }
        Assert.DoesNotContain("unlink-private-canary", text, StringComparison.Ordinal);
        var after = await AccessDocument(administrator, $"/users/{user}/links");
        Assert.Equal(acknowledgementLost ? 1 : 2, after.GetProperty("items").GetArrayLength());
        Assert.Equal(acknowledgementLost, after.GetProperty("catalogRevision").GetString() != state.GetProperty("catalogRevision").GetString());
        Assert.Equal(user, (await Session(partner)).Subject);
        Assert.Contains(events.Items, item => item.Detail["action"] == "link.remove" && item.Outcome == AuthEventOutcome.Failure && item.Detail["result"] == "storage-outcome-unavailable");
        Assert.DoesNotContain("unlink-private-canary", JsonSerializer.Serialize(events.Items), StringComparison.Ordinal);
        if (!acknowledgementLost)
        {
            using var retried = await ManagementPost(host, administrator, $"/users/{user}/links/remove", RemoveRequest(after, "company"));
            Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        }
    }

    private sealed class LinkRemovalFailure(bool acknowledgementLost) : SaveChangesInterceptor, IDbTransactionInterceptor
    {
        public bool Armed;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && !acknowledgementLost && eventData.Context!.ChangeTracker.Entries<ExternalIdentityEntity>().Any(item => item.State == EntityState.Deleted))
            { Armed = false; throw new InvalidOperationException("unlink-private-canary"); }
            return ValueTask.FromResult(result);
        }
        public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Armed && acknowledgementLost) { Armed = false; throw new DbUpdateConcurrencyException("unlink-private-canary"); }
            return Task.CompletedTask;
        }
    }
}
