using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Agent.Specialists.JsonSchema;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Journal;
using Elsa.Agent.Specialists.Services;
using Elsa.Agent.Specialists.Tests.Runtime;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Features;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Recovery;

public sealed partial class SpecialistEfJournalTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Fresh_readers_recover_accepted_ef_content_with_rotated_keys_disabled_creation_and_no_model_contribution(bool retireOldKey, bool legacyProtection)
    {
        await using var database = await Database.CreateAsync();
        var request = CompatibilityRequest();
        var access = new Access(PersistenceAccessContext.Scoped(new("tenant")));
        var now = DateTimeOffset.UtcNow;
        string handle;
        SpecialistSessionReference retainedSession;
        SpecialistTurnDocument before;
        await using (var original = CompatibilityProtection("old", includeOld: true))
        await using (var context = database.Context())
        {
            ISecretValueProtector protector = original.GetRequiredService<ISecretValueProtector>();
            if (legacyProtection) protector = new LegacyProtectionWriter(protector);
            var journal = new SpecialistTurnJournal(Store(context), protector, access);
            handle = await journal.StageAsync(request, now, default);
            var claim = await journal.ClaimAsync(handle, request.Identity, "original-child", now, default);
            retainedSession = new SpecialistSessionCodec(protector, access).Capture(
                [new(ChatRole.User, "retained brief"), new(ChatRole.Assistant, "{\"Article\":\"retained\"}")], request.Identity, new(handle, 1));
            Assert.True(await journal.DispatchAsync(claim, now, default));
            Assert.True(await journal.AcceptAsync(claim, new(Json("{\"Article\":\"retained\"}"), null, null, null, 1, 1, retainedSession), now, default));
            before = (await journal.ReadAsync(handle, request.Identity, default)).Document;
            Assert.StartsWith(legacyProtection ? "v1:" : "v2:old:", before.ProtectedRequest!);
            Assert.StartsWith(legacyProtection ? "v1:" : "v2:old:", retainedSession.ProtectedValue);
        }
        // Both the old provider and its context are disposed. Only the file-backed journal and
        // explicit configured key ring cross this reader-provider boundary; no runtime/model state is reused.
        await using var recovered = CompatibilityProtection("new", includeOld: !retireOldKey);
        await using var freshContext = database.Context();
        Assert.Empty(recovered.GetServices<ISpecialistModelClientFactory>());
        var store = Store(freshContext);
        var newProtector = recovered.GetRequiredService<ISecretValueProtector>();
        var disabled = new SpecialistTurnJournal(store, newProtector, access, new(false));
        var codec = new SpecialistSessionCodec(newProtector, access);
        var successor = request with { Identity = request.Identity with { ActivityExecutionId = "recovered-parent" } };
        Assert.Equal(handle, await disabled.StageAsync(successor, now.AddDays(1), default));
        if (retireOldKey)
        {
            var failure = await Assert.ThrowsAsync<SpecialistTurnException>(() => disabled.ReplayAsync(handle, successor.Identity, "recovered-child", default).AsTask());
            Assert.Equal("specialist.journal.protection", failure.Code);
            Assert.NotNull(failure.InnerException);
            Assert.Equal("specialist.session.unavailable", Assert.Throws<SpecialistTurnException>(() => codec.Restore(retainedSession, successor.Identity)).Code);
        }
        else
        {
            var result = await disabled.ReplayAsync(handle, successor.Identity, "recovered-child", default);
            Assert.Equal("retained", result.Result.GetProperty("Article").GetString());
            Assert.Equal(retainedSession, result.Session);
            Assert.Equal(new[] { "retained brief", "{\"Article\":\"retained\"}" }, codec.Restore(result.Session!, successor.Identity).Select(message => message.Text));
            var newSnapshot = codec.Capture([new(ChatRole.User, "new brief"), new(ChatRole.Assistant, "new result")], successor.Identity, new(handle, 2));
            Assert.StartsWith("v2:new:", newSnapshot.ProtectedValue);
        }
        var after = (await store.ReadAsync(handle, request.Identity, default))!;
        Assert.Equal(SpecialistTurnStatus.Accepted, after.Status);
        Assert.Equal(before.ProtectedRequest, after.ProtectedRequest);
        Assert.Equal(before.ProtectedResult, after.ProtectedResult);
        var conversation = (await store.ReadConversationAsync(handle, request.Identity, default))!;
        Assert.Equal(1, conversation.Revision);
        Assert.Equal(handle, conversation.AcceptedTurn!.Handle);
        Assert.Null(conversation.ActiveTurnHandle);
        var refused = await Assert.ThrowsAsync<SpecialistTurnException>(() => disabled.StageAsync(request with { Identity = request.Identity with { OperationKey = "new-operation" } }, now, default).AsTask());
        Assert.Equal("specialist.turn.admission-disabled", refused.Code);
        // Cancellation settlement authenticates native source separately and needs no content key.
        Assert.Equal(SpecialistCancellationOutcome.AlreadyAccepted,
            (await store.CancelAsync(new(request.Identity, "original-child", "retained-cancel-source", new string('f', 64), now), default)).Outcome);
    }

    [Theory]
    [InlineData("codec")]
    [InlineData("purpose")]
    public async Task Unsupported_retained_session_fails_before_a_model_opens_after_reader_restart(string field)
    {
        await using var database = await Database.CreateAsync();
        var access = new Access(PersistenceAccessContext.Scoped(new("tenant")));
        var firstRequest = CompatibilityRequest();
        var now = DateTimeOffset.UtcNow;
        string firstHandle;
        string nextHandle;
        SpecialistTurnDocument nextClaim;
        SpecialistTurnRequest nextRequest;
        SpecialistTurnDocument accepted;
        await using (var original = CompatibilityProtection("old", includeOld: true))
        await using (var context = database.Context())
        {
            var protector = original.GetRequiredService<ISecretValueProtector>();
            var journal = new SpecialistTurnJournal(Store(context), protector, access);
            firstHandle = await journal.StageAsync(firstRequest, now, default);
            var firstClaim = await journal.ClaimAsync(firstHandle, firstRequest.Identity, "first-child", now, default);
            var session = new SpecialistSessionCodec(protector, access).Capture([new(ChatRole.User, "retained brief"), new(ChatRole.Assistant, "retained result")], firstRequest.Identity, new(firstHandle, 1));
            // An authenticated future-writer fixture; no claim of running a future module binary.
            var plaintext = JsonNode.Parse(protector.Unprotect(session.ProtectedValue))!;
            plaintext[field] = "future-profile/99";
            session = session with { ProtectedValue = protector.Protect(plaintext.ToJsonString()) };
            Assert.True(await journal.DispatchAsync(firstClaim, now, default));
            Assert.True(await journal.AcceptAsync(firstClaim, new(Json("{\"Article\":\"retained\"}"), null, null, null, 1, 1, session), now, default));
            accepted = (await journal.ReadAsync(firstHandle, firstRequest.Identity, default)).Document;
            nextRequest = firstRequest with { Identity = firstRequest.Identity with { ActivityExecutionId = "next-parent", OperationKey = "next-operation" }, Continuation = new(firstHandle, 1) };
            nextHandle = await journal.StageAsync(nextRequest, now, default);
            nextClaim = await journal.ClaimAsync(nextHandle, nextRequest.Identity, "next-child", now, default);
        }
        await using var recovered = CompatibilityProtection("new", includeOld: true);
        await using var fresh = database.Context();
        var store = Store(fresh);
        var newProtector = recovered.GetRequiredService<ISecretValueProtector>();
        var disabled = new SpecialistTurnJournal(store, newProtector, access, new(false));
        var request = (await disabled.ReadAsync(nextHandle, nextRequest.Identity, default)).Request;
        var prepared = await disabled.PrepareSessionAsync(nextClaim, request, default);
        Assert.True(await disabled.DispatchAsync(nextClaim, now, default));
        var factory = new CompatibilityNeverOpenFactory();
        var runner = new SpecialistModelRunner([factory], new SpecialistSchemaValidator(), new(newProtector, access));
        var failure = await Assert.ThrowsAsync<SpecialistTurnException>(() => runner.RunAsync(prepared, default).AsTask());
        Assert.Equal("specialist.session.codec", failure.Code);
        Assert.Equal(0, factory.Opens);
        await disabled.FailAsync(nextClaim, failure.Code, false, default);
        var conversation = (await store.ReadConversationAsync(firstHandle, firstRequest.Identity, default))!;
        Assert.Equal(1, conversation.Revision);
        Assert.Equal(firstHandle, conversation.AcceptedTurn!.Handle);
        Assert.Null(conversation.ActiveTurnHandle);
        Assert.False(conversation.OutcomeUnknown);
        var after = (await store.ReadAsync(firstHandle, firstRequest.Identity, default))!;
        Assert.Equal(accepted.ProtectedRequest, after.ProtectedRequest);
        Assert.Equal(accepted.ProtectedResult, after.ProtectedResult);
        Assert.Equal(SpecialistTurnStatus.Failed, (await store.ReadAsync(nextHandle, nextRequest.Identity, default))!.Status);
    }

    private sealed class CompatibilityNeverOpenFactory : ISpecialistModelClientFactory
    {
        internal int Opens;
        public string ProviderKey => "fixture";
        public ValueTask<SpecialistModelClientLease> OpenAsync(SpecialistModelOpenRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Opens);
            throw new InvalidOperationException("The unsupported retained session must fail before model construction.");
        }
    }

    private static ServiceProvider CompatibilityProtection(string active, bool includeOld)
    {
        var keys = new Dictionary<string, string> { ["new"] = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("synthetic-specialist-compatibility-new-key"))) };
        if (includeOld) keys["old"] = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("synthetic-specialist-compatibility-old-key")));
        var services = new ServiceCollection();
        new SecretsFeature { ActiveKeyId = active, Keys = keys, EncryptionKey = includeOld ? keys["old"] : null }.ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    // A retained protection-format fixture, not a claim of an older module binary run.
    private sealed class LegacyProtectionWriter(ISecretValueProtector inner) : ISecretValueProtector
    {
        public string Protect(string value)
        {
            var parts = inner.Protect(value).Split(':');
            Assert.Equal("v2", parts[0]);
            Assert.Equal("old", parts[1]);
            return string.Join(':', "v1", parts[2], parts[3], parts[4]);
        }
        public string Unprotect(string value) => inner.Unprotect(value);
    }

    private static SpecialistTurnRequest CompatibilityRequest()
    {
        var request = SpecialistJournalTests.Request();
        var descriptor = request.Descriptor with { AllowContinuity = true };
        var contract = request.Contract;
        var updated = new ActivityContract(contract.ActivityTypeKey, contract.ContractVersion, contract.DescriptorKind, JsonSerializer.SerializeToElement(descriptor, SpecialistJson.Options),
            contract.Inputs.Values, contract.Result, contract.Outcomes, contract.Activation, contract.SideEffectProfile, contract.SupportsBackgroundExecution);
        return request with { Descriptor = descriptor, Contract = updated };
    }
}
