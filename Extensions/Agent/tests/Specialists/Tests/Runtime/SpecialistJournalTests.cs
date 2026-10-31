using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Journal;
using Elsa.Agent.Specialists.Services;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Features;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Runtime;

public sealed class SpecialistJournalTests
{
    [Fact]
    public async Task Repeated_staging_preserves_deadline_and_encrypts_business_material()
    {
        await using var services = Services();
        var store = new InMemorySpecialistTurnStore();
        var journal = Journal(store, services);
        var request = Request();
        var now = DateTimeOffset.UtcNow;
        var handle = await journal.StageAsync(request, now, CancellationToken.None);
        Assert.Equal(handle, await journal.StageAsync(request, now.AddMinutes(1), CancellationToken.None));
        var (document, restored) = await journal.ReadAsync(handle, request.Identity, CancellationToken.None);
        Assert.Equal(now.AddMilliseconds(request.Descriptor.Limits.TimeoutMilliseconds), document.Deadline);
        Assert.Equal(request.Contract.SchemaFingerprint, restored.Contract.SchemaFingerprint);
        Assert.Equal("very-private-brief", restored.Inputs.GetProperty("Brief").GetString());
        Assert.DoesNotContain("very-private", JsonSerializer.Serialize(document), StringComparison.Ordinal);
        var changed = request with { Inputs = Json("{\"Brief\":\"changed\"}") };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await journal.StageAsync(changed, now, CancellationToken.None));
    }

    [Fact]
    public async Task Only_one_worker_can_claim_and_only_its_live_fence_can_accept()
    {
        await using var services = Services();
        var store = new InMemorySpecialistTurnStore();
        var journal = Journal(store, services);
        var request = Request();
        var now = DateTimeOffset.UtcNow;
        var handle = await journal.StageAsync(request, now, CancellationToken.None);
        var claims = await Task.WhenAll(Enumerable.Range(0, 32).Select(async i =>
            await store.ClaimAsync(handle, request.Identity, "child-" + i, "fence-" + i, now, CancellationToken.None)));
        var claim = Assert.Single(claims.Where(x => x is not null))!;
        var result = new SpecialistTurnResult(Json("{\"Article\":\"accepted\"}"), 5, 3, 8, 1, 1);
        Assert.False(await journal.AcceptAsync(claim, result, now, CancellationToken.None));
        Assert.False(await journal.DispatchAsync(claim with { Fence = "foreign" }, now, CancellationToken.None));
        Assert.True(await journal.DispatchAsync(claim, now, CancellationToken.None));
        Assert.False(await journal.AcceptAsync(claim with { Fence = "foreign" }, result, now, CancellationToken.None));
        Assert.True(await journal.AcceptAsync(claim, result, now, CancellationToken.None));
        await journal.FailAsync(claim, "late-error", true, CancellationToken.None);
        var accepted = await journal.ResultAsync(handle, request.Identity, claim.ChildInvocationId!, CancellationToken.None);
        Assert.Equal("accepted", accepted.Result.GetProperty("Article").GetString());
        Assert.Equal(8, accepted.TotalTokens);
        await Assert.ThrowsAsync<SpecialistTurnException>(async () => await journal.ResultAsync(handle, request.Identity, "other-child", CancellationToken.None));
    }

    [Fact]
    public async Task Expired_claims_and_late_acceptance_are_refused()
    {
        await using var services = Services();
        var store = new InMemorySpecialistTurnStore();
        var journal = Journal(store, services);
        var request = Request();
        var now = DateTimeOffset.UtcNow;
        var handle = await journal.StageAsync(request, now, CancellationToken.None);
        var claim = await journal.ClaimAsync(handle, request.Identity, "child", now, CancellationToken.None);
        Assert.True(await journal.DispatchAsync(claim, now, CancellationToken.None));
        Assert.False(await journal.AcceptAsync(claim, new(Json("{}"), null, null, null, 1, 1), claim.Deadline, CancellationToken.None));
        await journal.FailAsync(claim, "deadline", true, CancellationToken.None);
        Assert.Equal(SpecialistTurnStatus.OutcomeUnknown, (await journal.ReadAsync(handle, request.Identity, CancellationToken.None)).Document.Status);
        await Assert.ThrowsAsync<SpecialistTurnException>(async () => await journal.ClaimAsync(handle, request.Identity, "replacement", claim.Deadline, CancellationToken.None));
    }

    [Fact]
    public async Task Tenant_partition_and_native_identity_are_independent_guards()
    {
        await using var services = Services();
        var store = new InMemorySpecialistTurnStore();
        var request = Request();
        var journal = Journal(store, services);
        var handle = await journal.StageAsync(request, DateTimeOffset.UtcNow, CancellationToken.None);
        await Assert.ThrowsAsync<SpecialistTurnException>(async () => await Journal(store, services, "other").ReadAsync(handle, request.Identity, CancellationToken.None));
        await Assert.ThrowsAsync<SpecialistTurnException>(async () => await journal.ReadAsync(handle, request.Identity with { ActivityExecutionId = "foreign" }, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await journal.ReadAsync(handle, request.Identity with { TenantId = "foreign" }, CancellationToken.None));
    }

    [Fact]
    public async Task Authenticated_ciphertext_with_another_purpose_is_not_a_turn_request()
    {
        await using var services = Services();
        var request = Request();
        var store = new InMemorySpecialistTurnStore();
        var journal = Journal(store, services);
        var handle = await journal.StageAsync(request, DateTimeOffset.UtcNow, CancellationToken.None);
        var (document, _) = await journal.ReadAsync(handle, request.Identity, CancellationToken.None);
        var protector = services.GetRequiredService<ISecretValueProtector>();
        var payload = System.Text.Json.Nodes.JsonNode.Parse(protector.Unprotect(document.ProtectedRequest))!.AsObject();
        payload["purpose"] = "elsa.specialist.turn/result/1";
        var substituted = new InMemorySpecialistTurnStore();
        await substituted.StageAsync(document with { ProtectedRequest = protector.Protect(payload.ToJsonString()) }, CancellationToken.None);
        var exception = await Assert.ThrowsAsync<SpecialistTurnException>(async () => await Journal(substituted, services).ReadAsync(handle, request.Identity, CancellationToken.None));
        Assert.Equal("specialist.journal.protection", exception.Code);
    }

    [Fact]
    public async Task Malformed_ciphertext_retains_the_cause_without_exposing_it_in_the_business_message()
    {
        await using var services = Services();
        var request = Request();
        var store = new InMemorySpecialistTurnStore();
        var journal = Journal(store, services);
        var handle = await journal.StageAsync(request, DateTimeOffset.UtcNow, CancellationToken.None);
        var (document, _) = await journal.ReadAsync(handle, request.Identity, CancellationToken.None);
        var substituted = new InMemorySpecialistTurnStore();
        await substituted.StageAsync(document with { ProtectedRequest = "malformed-private-ciphertext" }, CancellationToken.None);
        var exception = await Assert.ThrowsAsync<SpecialistTurnException>(async () => await Journal(substituted, services).ReadAsync(handle, request.Identity, CancellationToken.None));
        Assert.Equal("specialist.journal.protection", exception.Code);
        Assert.NotNull(exception.InnerException);
        Assert.Equal("The staged specialist request cannot be authenticated.", exception.Message);
    }

    [Fact]
    public async Task Restarted_process_local_journal_refuses_missing_handles()
    {
        await using var services = Services();
        var request = Request();
        var handle = await Journal(new InMemorySpecialistTurnStore(), services).StageAsync(request, DateTimeOffset.UtcNow, CancellationToken.None);
        var exception = await Assert.ThrowsAsync<SpecialistTurnException>(async () => await Journal(new InMemorySpecialistTurnStore(), services).ReadAsync(handle, request.Identity, CancellationToken.None));
        Assert.Equal("specialist.journal.missing", exception.Code);
    }

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        new SecretsFeature { EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }.ConfigureServices(services);
        return services.BuildServiceProvider();
    }
    private static SpecialistTurnJournal Journal(ISpecialistTurnStore store, IServiceProvider services, string scope = "tenant") =>
        new(store, services.GetRequiredService<ISecretValueProtector>(), new Access(scope));
    private sealed class Access(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new(scope));
    }
    private static SpecialistTurnRequest Request()
    {
        var descriptor = new SpecialistRuntimeDescriptor("very-private-instructions", new("fixture", "connection", "model"), Json("{}"), Json("{}"), Json("{}"), "fixture/1", SpecialistRuntimeIds.SchemaProfile, new(), false);
        var contract = new ActivityContract("specialist", "1.0.0", SpecialistRuntimeIds.ConsumerKey, JsonSerializer.SerializeToElement(descriptor), [],
            new(new("System.Text.Json.JsonElement"), true, ActivityValuePolicy.Default, []), ["Done"], new(SpecialistRuntimeIds.ConsumerKey, SpecialistRuntimeIds.FacadeCapability));
        return new(new("tenant", "tenant", "workflow", "activity", "operation", "artifact", "hash", "version", "template"), descriptor, contract,
            Json("{\"Brief\":\"very-private-brief\"}"), "binding-fingerprint");
    }
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}
