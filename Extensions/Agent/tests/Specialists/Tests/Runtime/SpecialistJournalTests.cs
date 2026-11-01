using Elsa.Agent.Specialists.Core.Exceptions;
using Elsa.Agent.Specialists.Core.Contracts;
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
using Microsoft.Extensions.AI;

namespace Elsa.Agent.Specialists.Tests.Runtime;

public sealed class SpecialistJournalTests
{
    [Fact]
    public async Task Logical_fingerprint_normalization_does_not_allow_protected_request_origin_relocation()
    {
        await using var services = Services();
        var state = new InMemorySpecialistTurnState();
        var journal = Journal(new InMemorySpecialistTurnStore(new Access("tenant"), state), services);
        var request = Request();
        var handle = await journal.StageAsync(request, DateTimeOffset.UtcNow, default);
        var retry = request with { Identity = request.Identity with { ActivityExecutionId = "registered-retry" } };
        await journal.StageAsync(retry, DateTimeOffset.UtcNow, default);
        var protector = services.GetRequiredService<ISecretValueProtector>();
        var frame = System.Text.Json.Nodes.JsonNode.Parse(protector.Unprotect(state.Rows[handle].ProtectedRequest))!;
        frame["identity"]!["activityExecutionId"] = retry.Identity.ActivityExecutionId;
        frame["request"]!["identity"]!["activityExecutionId"] = retry.Identity.ActivityExecutionId;
        state.Rows[handle] = state.Rows[handle] with { ProtectedRequest = protector.Protect(frame.ToJsonString()) };
        var failure = await Assert.ThrowsAsync<SpecialistTurnException>(async () => await journal.ReadAsync(handle, retry.Identity, default));
        Assert.Equal("specialist.journal.protection", failure.Code);
    }

    [Fact]
    public async Task Protected_result_dispatch_identity_cannot_be_replaced_by_a_replay_delivery()
    {
        await using var services = Services();
        var state = new InMemorySpecialistTurnState();
        var journal = Journal(new InMemorySpecialistTurnStore(new Access("tenant"), state), services);
        var request = Request();
        var now = DateTimeOffset.UtcNow;
        var handle = await journal.StageAsync(request, now, default);
        var claim = await journal.ClaimAsync(handle, request.Identity, "original-child", now, default);
        Assert.True(await journal.DispatchAsync(claim, now, default));
        Assert.True(await journal.AcceptAsync(claim, new(Json("{}"), null, null, null, 1, 1), now, default));
        var retry = request with { Identity = request.Identity with { ActivityExecutionId = "registered-retry" } };
        await journal.StageAsync(retry, now, default);
        await journal.ReplayAsync(handle, retry.Identity, "retry-child", default);
        var protector = services.GetRequiredService<ISecretValueProtector>();
        var frame = System.Text.Json.Nodes.JsonNode.Parse(protector.Unprotect(state.Rows[handle].ProtectedResult!))!;
        frame["dispatchIdentity"]!["activityExecutionId"] = retry.Identity.ActivityExecutionId;
        state.Rows[handle] = state.Rows[handle] with { ProtectedResult = protector.Protect(frame.ToJsonString()) };
        var failure = await Assert.ThrowsAsync<SpecialistTurnException>(async () => await journal.ResultAsync(handle, retry.Identity, "retry-child", default));
        Assert.Equal("specialist.journal.protection", failure.Code);
    }

    [Fact]
    public async Task Recovered_predispatch_delivery_accepts_under_its_current_identity_but_preserves_the_origin_request()
    {
        await using var services = Services();
        var journal = Journal(Memory(), services);
        var request = Request();
        var now = DateTimeOffset.UtcNow;
        var handle = await journal.StageAsync(request, now, default);
        await journal.ClaimAsync(handle, request.Identity, "original-child", now, default);
        var retry = request with { Identity = request.Identity with { ActivityExecutionId = "registered-retry" } };
        await journal.StageAsync(retry, now, default);
        var current = await journal.ClaimAsync(handle, retry.Identity, "retry-child", now.AddSeconds(30), default);
        Assert.Equal(request.Identity, current.Identity);
        Assert.Equal(retry.Identity.ActivityExecutionId, current.ActiveDeliveryActivityId);
        Assert.True(await journal.DispatchAsync(current, now.AddSeconds(31), default));
        Assert.True(await journal.AcceptAsync(current, new(Json("{\"Article\":\"recovered\"}"), null, null, null, 1, 1), now.AddSeconds(31), default));
        Assert.Equal("recovered", (await journal.ResultAsync(handle, retry.Identity, "retry-child", default)).Result.GetProperty("Article").GetString());
        await Assert.ThrowsAsync<SpecialistTurnException>(async () => await journal.ResultAsync(handle, request.Identity, "retry-child", default));
    }

    [Theory]
    [InlineData("business")]
    [InlineData("limits")]
    [InlineData("binding")]
    [InlineData("settings")]
    [InlineData("model")]
    [InlineData("version")]
    [InlineData("template")]
    [InlineData("artifact")]
    public async Task A_registered_native_retry_must_preserve_every_logical_request_field(string changed)
    {
        await using var services = Services();
        var journal = Journal(Memory(), services);
        var original = Request();
        var now = DateTimeOffset.UtcNow;
        var handle = await journal.StageAsync(original, now, default);
        var retry = original with { Identity = original.Identity with { ActivityExecutionId = "retry-parent" } };
        Assert.Equal(handle, await journal.StageAsync(retry, now.AddSeconds(1), default));
        var read = await journal.ReadAsync(handle, retry.Identity, default);
        Assert.Equal(original.Identity, read.Document.Identity);
        Assert.Equal(retry.Identity, read.Request.Identity); // Current point-of-use provider provenance.
        Assert.Equal(2, read.Document.Deliveries!.Count);
        var modified = changed switch
        {
            "business" => retry with { Inputs = Json("{\"Brief\":\"changed\"}") },
            "limits" => retry with { EffectiveLimits = new(1, 1000, 32) },
            "binding" => retry with { BindingFingerprint = "changed-binding" },
            "settings" => retry with { Descriptor = retry.Descriptor with { Settings = Json("{\"changed\":true}") } },
            "model" => retry with { Descriptor = retry.Descriptor with { Model = retry.Descriptor.Model with { ModelId = "another-model" } } },
            "version" => retry with { Identity = retry.Identity with { DefinitionVersionId = "another-version" } },
            "template" => retry with { Identity = retry.Identity with { TemplateHash = "another-template" } },
            _ => retry with { Identity = retry.Identity with { ArtifactHash = "another-artifact" } }
        };
        var error = await Assert.ThrowsAsync<SpecialistTurnException>(async () => await journal.StageAsync(modified, now, default));
        Assert.Equal("specialist.turn.conflict", error.Code);
        Assert.Equal(read.Document.ProtectedRequest, (await journal.ReadAsync(handle, original.Identity, default)).Document.ProtectedRequest);
    }

    [Fact]
    public async Task Replaying_acceptance_requires_a_registered_exact_child_and_keeps_original_receipt_provenance()
    {
        await using var services = Services();
        var journal = Journal(Memory(), services);
        var request = Request();
        var now = DateTimeOffset.UtcNow;
        var handle = await journal.StageAsync(request, now, default);
        var claim = await journal.ClaimAsync(handle, request.Identity, "original-child", now, default);
        Assert.True(await journal.DispatchAsync(claim, now, default));
        Assert.True(await journal.AcceptAsync(claim, new(Json("{\"Article\":\"accepted\"}"), null, null, null, 1, 1), now, default));
        var accepted = (await journal.ReadAsync(handle, request.Identity, default)).Document;
        var retry = request with { Identity = request.Identity with { ActivityExecutionId = "retry-parent" } };
        await Assert.ThrowsAsync<SpecialistTurnException>(async () => await journal.ReplayAsync(handle, retry.Identity, "retry-child", default));
        Assert.Equal(handle, await journal.StageAsync(retry, now.AddDays(1), default));
        await Assert.ThrowsAsync<SpecialistTurnException>(async () => await journal.ResultAsync(handle, retry.Identity, "retry-child", default));
        var result = await journal.ReplayAsync(handle, retry.Identity, "retry-child", default);
        Assert.Equal("accepted", result.Result.GetProperty("Article").GetString());
        Assert.True(JsonElement.DeepEquals(result.Result, (await journal.ResultAsync(handle, retry.Identity, "retry-child", default)).Result));
        await Assert.ThrowsAsync<SpecialistTurnException>(async () => await journal.ReplayAsync(handle, retry.Identity, "foreign-child", default));
        var replayed = (await journal.ReadAsync(handle, retry.Identity, default)).Document;
        Assert.Equal(accepted.ProtectedResult, replayed.ProtectedResult);
        Assert.Equal(accepted.ChildInvocationId, replayed.ChildInvocationId);
        Assert.Equal(accepted.AdmissionGeneration, replayed.AdmissionGeneration);
        Assert.Equal(accepted.Deadline, replayed.Deadline);
    }

    [Fact]
    public async Task An_accepted_receipt_authenticates_its_claim_generation()
    {
        await using var services = Services();
        var state = new InMemorySpecialistTurnState();
        var store = new InMemorySpecialistTurnStore(new Access("tenant"), state);
        var journal = Journal(store, services);
        var request = Request();
        var now = DateTimeOffset.UtcNow;
        var handle = await journal.StageAsync(request, now, default);
        _ = await store.ClaimAsync(handle, request.Identity, "child", "reused", now, default);
        var claim = (await store.ClaimAsync(handle, request.Identity, "child", "reused", now.AddSeconds(30), default))!;
        Assert.True(await journal.DispatchAsync(claim, now.AddSeconds(31), default));
        Assert.True(await journal.AcceptAsync(claim, new(Json("{}"), null, null, null, 1, 1), now.AddSeconds(31), default));
        state.Rows[handle] = state.Rows[handle] with { AdmissionGeneration = 1 };
        var error = await Assert.ThrowsAsync<SpecialistTurnException>(async () => await journal.ResultAsync(handle, request.Identity, "child", default));
        Assert.Equal("specialist.journal.protection", error.Code);
    }

    [Fact]
    public async Task Legacy_accepted_unleased_receipts_remain_readable_without_new_dispatch()
    {
        await using var services = Services();
        var state = new InMemorySpecialistTurnState();
        var store = new InMemorySpecialistTurnStore(new Access("tenant"), state);
        var journal = Journal(store, services);
        var request = Request();
        var serialized = JsonSerializer.Serialize(request, SpecialistJson.Options);
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(serialized))).ToLowerInvariant();
        var protector = services.GetRequiredService<ISecretValueProtector>();
        var protectedRequest = protector.Protect(JsonSerializer.Serialize(new { purpose = "elsa.specialist.turn/request/1", identity = request.Identity, hash, request }, SpecialistJson.Options));
        var protectedResult = protector.Protect(JsonSerializer.Serialize(new { purpose = "elsa.specialist.turn/result/1", identity = request.Identity, requestHash = hash,
            childInvocationId = "child", fence = "legacy", result = new SpecialistTurnResult(Json("{}"), null, null, null, 1, 1) }, SpecialistJson.Options));
        var handle = new string('a', 64);
        var document = new SpecialistTurnDocument(handle, SpecialistTurnDocument.CalculateOperationKey(request.Identity), hash, request.Identity, protectedRequest,
            DateTimeOffset.UtcNow.AddDays(-1), SpecialistTurnStatus.Accepted, "child", "legacy", protectedResult) { MaximumModelCalls = request.Descriptor.Limits.MaximumModelCalls };
        state.Rows[handle] = document;
        state.Keys[document.Key] = handle;
        Assert.Equal(JsonValueKind.Object, (await journal.ResultAsync(handle, request.Identity, "child", default)).Result.ValueKind);
        Assert.Null(await store.ClaimAsync(handle, request.Identity, "child", "new", DateTimeOffset.UtcNow, default));
        Assert.Null(await store.ReplayAcceptedAsync(handle, request.Identity, "foreign-child", default));
        var conflict = await Assert.ThrowsAsync<SpecialistTurnException>(async () => await journal.StageAsync(request with { Identity = request.Identity with { ActivityExecutionId = "retry" } }, DateTimeOffset.UtcNow, default));
        Assert.Equal("specialist.turn.conflict", conflict.Code);
    }

    [Fact]
    public async Task Conversation_admission_header_must_match_the_protected_request()
    {
        await using var services = Services();
        var state = new InMemorySpecialistTurnState();
        var store = new InMemorySpecialistTurnStore(new Access("tenant"), state);
        var journal = Journal(store, services);
        var request = Request();
        request = request with { Descriptor = request.Descriptor with { AllowContinuity = true } };
        var handle = await journal.StageAsync(request, DateTimeOffset.UtcNow, default);
        state.Rows[handle] = state.Rows[handle] with { Conversation = new(new string('b', 64), 9) };
        var error = await Assert.ThrowsAsync<SpecialistTurnException>(async () => await journal.ReadAsync(handle, request.Identity, default));
        Assert.Equal("specialist.journal.protection", error.Code);
    }

    [Fact]
    public async Task Acceptance_requires_the_candidate_session_to_match_the_admitted_revision()
    {
        await using var services = Services();
        var store = Memory();
        var journal = Journal(store, services);
        var request = Request();
        request = request with { Descriptor = request.Descriptor with { AllowContinuity = true } };
        var now = DateTimeOffset.UtcNow;
        var handle = await journal.StageAsync(request, now, default);
        var claim = await journal.ClaimAsync(handle, request.Identity, "child", now, default);
        Assert.True(await journal.DispatchAsync(claim, now, default));
        var candidate = new SpecialistTurnResult(Json("{}"), null, null, null, 1, 1, new(handle, 2, "candidate"));
        var error = await Assert.ThrowsAsync<SpecialistTurnException>(async () => await journal.AcceptAsync(claim, candidate, now, default));
        Assert.Equal("specialist.session.admission", error.Code);
        Assert.Null((await journal.ReadAsync(handle, request.Identity, default)).Document.ProtectedResult);
        Assert.Equal(0, (await store.ReadConversationAsync(handle, request.Identity, default))!.Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_mutated_plain_journal_maximum_cannot_raise_the_authenticated_request_budget(bool lowered)
    {
        await using var services = Services();
        var state = new InMemorySpecialistTurnState();
        var store = new InMemorySpecialistTurnStore(new Access("tenant"), state);
        var journal = Journal(store, services);
        var original = Request();
        var request = lowered ? original with { EffectiveLimits = new(1, 1000, 32) } : original;
        var handle = await journal.StageAsync(request, DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.Equal(lowered ? 1 : original.Descriptor.Limits.MaximumModelCalls, state.Rows[handle].MaximumModelCalls);
        state.Rows[handle] = state.Rows[handle] with { MaximumModelCalls = lowered ? original.Descriptor.Limits.MaximumModelCalls : 32 };
        var error = await Assert.ThrowsAsync<SpecialistTurnException>(async () => await journal.ReadAsync(handle, request.Identity, CancellationToken.None));
        Assert.Equal("specialist.journal.protection", error.Code);
    }

    [Fact]
    public async Task Accepted_business_result_and_bound_session_share_the_same_protected_journal_commit()
    {
        await using var services = Services();
        var store = Memory();
        var journal = Journal(store, services);
        var request = Request();
        var now = DateTimeOffset.UtcNow;
        var handle = await journal.StageAsync(request, now, CancellationToken.None);
        var claim = await journal.ClaimAsync(handle, request.Identity, "child", now, CancellationToken.None);
        var codec = new SpecialistSessionCodec(services.GetRequiredService<ISecretValueProtector>(), new Access("tenant"));
        var session = codec.Capture([new(ChatRole.User, "private brief"), new(ChatRole.Assistant, "{\"Article\":\"accepted\"}")], request.Identity, new(handle, 1));
        var result = new SpecialistTurnResult(Json("{\"Article\":\"accepted\"}"), 5, 3, 8, 1, 1, session);
        Assert.False(await journal.AcceptAsync(claim, result, now, CancellationToken.None));
        Assert.Null((await journal.ReadAsync(handle, request.Identity, CancellationToken.None)).Document.ProtectedResult);
        Assert.True(await journal.DispatchAsync(claim, now, CancellationToken.None));
        Assert.True(await journal.AcceptAsync(claim, result, now, CancellationToken.None));
        var accepted = await journal.ResultAsync(handle, request.Identity, "child", CancellationToken.None);
        Assert.Equal("accepted", accepted.Result.GetProperty("Article").GetString());
        Assert.Equal(session, accepted.Session);
        Assert.Equal("private brief", codec.Restore(accepted.Session!, request.Identity)[0].Text);
        await journal.FailAsync(claim, "late-failure", true, CancellationToken.None);
        var replay = await journal.ResultAsync(handle, request.Identity, "child", CancellationToken.None);
        Assert.True(JsonElement.DeepEquals(accepted.Result, replay.Result));
        Assert.Equal(accepted.Session, replay.Session);
    }

    [Fact]
    public async Task Repeated_staging_preserves_deadline_and_encrypts_business_material()
    {
        await using var services = Services();
        var store = Memory();
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
        var conflict = await Assert.ThrowsAsync<SpecialistTurnException>(async () => await journal.StageAsync(changed, now, CancellationToken.None));
        Assert.Equal("specialist.turn.conflict", conflict.Code);
    }

    [Fact]
    public async Task Only_one_worker_can_claim_and_only_its_live_fence_can_accept()
    {
        await using var services = Services();
        var store = Memory();
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
        var store = Memory();
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
        var store = Memory();
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
        var store = Memory();
        var journal = Journal(store, services);
        var handle = await journal.StageAsync(request, DateTimeOffset.UtcNow, CancellationToken.None);
        var (document, _) = await journal.ReadAsync(handle, request.Identity, CancellationToken.None);
        var protector = services.GetRequiredService<ISecretValueProtector>();
        var payload = System.Text.Json.Nodes.JsonNode.Parse(protector.Unprotect(document.ProtectedRequest))!.AsObject();
        payload["purpose"] = "elsa.specialist.turn/result/1";
        var substituted = Memory();
        await substituted.StageAsync(document with { ProtectedRequest = protector.Protect(payload.ToJsonString()) }, CancellationToken.None);
        var exception = await Assert.ThrowsAsync<SpecialistTurnException>(async () => await Journal(substituted, services).ReadAsync(handle, request.Identity, CancellationToken.None));
        Assert.Equal("specialist.journal.protection", exception.Code);
    }

    [Fact]
    public async Task Malformed_ciphertext_retains_the_cause_without_exposing_it_in_the_business_message()
    {
        await using var services = Services();
        var request = Request();
        var store = Memory();
        var journal = Journal(store, services);
        var handle = await journal.StageAsync(request, DateTimeOffset.UtcNow, CancellationToken.None);
        var (document, _) = await journal.ReadAsync(handle, request.Identity, CancellationToken.None);
        var substituted = Memory();
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
        var handle = await Journal(Memory(), services).StageAsync(request, DateTimeOffset.UtcNow, CancellationToken.None);
        var exception = await Assert.ThrowsAsync<SpecialistTurnException>(async () => await Journal(Memory(), services).ReadAsync(handle, request.Identity, CancellationToken.None));
        Assert.Equal("specialist.journal.missing", exception.Code);
    }

    [Fact]
    public async Task A_legacy_request_without_effective_limits_keeps_its_original_authenticated_shape()
    {
        await using var services = Services();
        var request = Request();
        var body = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(request, SpecialistJson.Options))!.AsObject();
        body.Remove("effectiveLimits");
        var oldJson = body.ToJsonString(SpecialistJson.Options);
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(oldJson))).ToLowerInvariant();
        var protector = services.GetRequiredService<ISecretValueProtector>();
        var envelope = JsonSerializer.Serialize(new { purpose = "elsa.specialist.turn/request/1", identity = request.Identity, hash, request = Json(oldJson) }, SpecialistJson.Options);
        var document = new SpecialistTurnDocument(new string('a', 64), SpecialistTurnDocument.CalculateOperationKey(request.Identity), hash, request.Identity,
            protector.Protect(envelope), DateTimeOffset.UtcNow.AddMinutes(5)) { MaximumModelCalls = request.Descriptor.Limits.MaximumModelCalls };
        var store = Memory();
        await store.StageAsync(document, default);
        var read = await Journal(store, services).ReadAsync(document.Handle, request.Identity, default);
        Assert.Null(read.Request.EffectiveLimits);
        Assert.Equal(hash, read.Document.RequestHash);
    }

    [Fact]
    public async Task Lowered_limits_remain_pinned_across_stage_replay_and_changed_bounds_conflict()
    {
        await using var services = Services();
        var journal = Journal(Memory(), services);
        var request = Request() with { EffectiveLimits = new(1, 20_000, 32) };
        var now = DateTimeOffset.UtcNow;
        var handle = await journal.StageAsync(request, now, default);
        Assert.Equal(handle, await journal.StageAsync(request, now.AddDays(1), default));
        var read = await journal.ReadAsync(handle, request.Identity, default);
        Assert.Equal(now.AddSeconds(20), read.Document.Deadline);
        Assert.Equal(request.EffectiveLimits, read.Request.EffectiveLimits);
        var conflict = await Assert.ThrowsAsync<SpecialistTurnException>(async () =>
            await journal.StageAsync(request with { EffectiveLimits = new(2, 20_000, 32) }, now, default));
        Assert.Equal("specialist.turn.conflict", conflict.Code);
    }

    private static InMemorySpecialistTurnStore Memory() => new(new Access("tenant"), new());
    internal static ServiceProvider Services()
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
    internal static SpecialistTurnRequest Request()
    {
        var descriptor = new SpecialistRuntimeDescriptor("very-private-instructions", new("fixture", "connection", "model"), Json("{}"), Json("{}"), Json("{}"), "fixture/1", SpecialistRuntimeIds.SchemaProfile, new(), false);
        var contract = new ActivityContract("specialist", "1.0.0", SpecialistRuntimeIds.ConsumerKey, JsonSerializer.SerializeToElement(descriptor), [],
            new(new("System.Text.Json.JsonElement"), true, ActivityValuePolicy.Default, []), ["Done"], new(SpecialistRuntimeIds.ConsumerKey, SpecialistRuntimeIds.FacadeCapability));
        return new(new("tenant", "tenant", "workflow", "activity", "operation", "artifact", "hash", "version", "template"), descriptor, contract,
            Json("{\"Brief\":\"very-private-brief\"}"), "binding-fingerprint");
    }
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}
