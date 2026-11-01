using Elsa.Agent.Specialists.Core.Exceptions;
using Elsa.Agent.Specialists.Core.Contracts;
using System.Security.Cryptography;
using System.Data.Common;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Agent.Specialists.Journal;
using Elsa.Agent.Specialists.Services;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Features;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Entities;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Recovery;

public sealed class SpecialistEfJournalTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 5, 0, 0, TimeSpan.Zero);
    private static SpecialistInvocationIdentity Identity => new("tenant", "tenant", "workflow", "activity", "operation", "artifact", "hash", "version", "template");
    private static SpecialistTurnDocument Document => new(new string('a', 64), SpecialistTurnDocument.CalculateOperationKey(Identity), new string('c', 64), Identity, "opaque-protected-request", Now.AddMinutes(5));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Expired_predispatch_claim_can_recover_but_old_generation_never_regains_ownership(bool durable)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        ISpecialistTurnStore store = durable ? Store(context) : new InMemorySpecialistTurnStore(new Access(PersistenceAccessContext.Scoped(new("tenant"))), new());
        var candidate = Document with { Conversation = new(new string('e', 64), 0) };
        await store.StageAsync(candidate, default);
        var first = (await store.ClaimAsync(candidate.Handle, Identity, "child", "reused-nonce", Now, default))!;
        Assert.Equal(Now.AddSeconds(30), first.LeaseExpiresAt);
        Assert.Equal(1, first.AdmissionGeneration);
        Assert.Null(await store.ClaimAsync(candidate.Handle, Identity, "child", "other", Now.AddSeconds(29), default));
        Assert.Null(await store.ClaimAsync(candidate.Handle, Identity, "foreign-child", "other", Now.AddSeconds(30), default));
        var second = (await store.ClaimAsync(candidate.Handle, Identity, "child", "reused-nonce", Now.AddSeconds(30), default))!;
        Assert.Equal(2, second.AdmissionGeneration);
        Assert.Equal(first.Fence, second.Fence); // Even reuse of a nonce cannot revive an older generation.
        var stale = new SpecialistTurnLeaseToken(first.Fence!, first.AdmissionGeneration);
        var live = new SpecialistTurnLeaseToken(second.Fence!, second.AdmissionGeneration);
        Assert.False(await store.DispatchAsync(candidate.Handle, Identity, stale, Now.AddSeconds(31), default));
        Assert.Null(await store.RenewAsync(candidate.Handle, Identity, stale, Now.AddSeconds(31), default));
        await store.FailAsync(candidate.Handle, Identity, stale, "stale-failure", true, default);
        Assert.Equal(SpecialistTurnStatus.Claimed, (await store.ReadAsync(candidate.Handle, Identity, default))!.Status);
        var conversation = (await store.ReadConversationAsync(candidate.Conversation.ConversationId, Identity, default))!;
        Assert.Equal(2, conversation.ActiveAdmissionGeneration);
        Assert.True(await store.DispatchAsync(candidate.Handle, Identity, live, Now.AddSeconds(31), default));
        var reservation = Reservation(0) with { ReservedAt = Now.AddSeconds(31) };
        Assert.Null(await store.ReserveModelRequestAsync(candidate.Handle, Identity, stale, reservation, Now.AddSeconds(31), default));
        Assert.NotNull(await store.ReserveModelRequestAsync(candidate.Handle, Identity, live, reservation, Now.AddSeconds(31), default));
        var observation = new SpecialistModelResponseObservation(Now.AddSeconds(31), false, null, null, null, null);
        Assert.False(await store.CompleteModelResponseAsync(candidate.Handle, Identity, stale, [reservation.Id], observation, Now.AddSeconds(31), default));
        Assert.True(await store.CompleteModelResponseAsync(candidate.Handle, Identity, live, [reservation.Id], observation, Now.AddSeconds(31), default));
        Assert.False(await store.AcceptAsync(candidate.Handle, Identity, stale, "stale", Now.AddSeconds(31), default));
        Assert.True(await store.AcceptAsync(candidate.Handle, Identity, live, "accepted", Now.AddSeconds(31), default));
        Assert.Equal(1, (await store.ReadConversationAsync(candidate.Conversation.ConversationId, Identity, default))!.Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Renewals_extend_only_a_live_lease_and_preserve_the_absolute_deadline(bool durable)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        ISpecialistTurnStore store = durable ? Store(context) : new InMemorySpecialistTurnStore(new Access(PersistenceAccessContext.Scoped(new("tenant"))), new());
        var candidate = Document with { Deadline = Now.AddSeconds(55) };
        await store.StageAsync(candidate, default);
        var claim = (await store.ClaimAsync(candidate.Handle, Identity, "child", "fence", Now, default))!;
        var token = new SpecialistTurnLeaseToken(claim.Fence!, claim.AdmissionGeneration);
        var renewal = (await store.RenewAsync(candidate.Handle, Identity, token, Now.AddSeconds(20), default))!;
        Assert.Equal(Now.AddSeconds(50), renewal.LeaseExpiresAt);
        Assert.Equal(candidate.Deadline, renewal.Deadline);
        renewal = (await store.RenewAsync(candidate.Handle, Identity, token, Now.AddSeconds(40), default))!;
        Assert.Equal(candidate.Deadline, renewal.LeaseExpiresAt);
        Assert.Null(await store.RenewAsync(candidate.Handle, Identity, token, candidate.Deadline, default));
        Assert.False(await store.DispatchAsync(candidate.Handle, Identity, token, candidate.Deadline, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Expired_dispatch_retains_reservations_and_blocks_fresh_inference_and_history(bool durable)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        ISpecialistTurnStore store = durable ? Store(context) : new InMemorySpecialistTurnStore(new Access(PersistenceAccessContext.Scoped(new("tenant"))), new());
        var candidate = Document with { Conversation = new(new string('e', 64), 0) };
        await store.StageAsync(candidate, default);
        var claim = (await store.ClaimAsync(candidate.Handle, Identity, "child", "fence", Now, default))!;
        var token = new SpecialistTurnLeaseToken(claim.Fence!, claim.AdmissionGeneration);
        Assert.True(await store.DispatchAsync(candidate.Handle, Identity, token, Now, default));
        Assert.NotNull(await store.ReserveModelRequestAsync(candidate.Handle, Identity, token, Reservation(0), Now, default));
        Assert.Null(await store.RenewAsync(candidate.Handle, Identity, token, Now.AddSeconds(30), default));
        Assert.False(await store.AcceptAsync(candidate.Handle, Identity, token, "late", Now.AddSeconds(30), default));
        Assert.Null(await store.ClaimAsync(candidate.Handle, Identity, "child", "replacement", Now.AddSeconds(30), default));
        var restored = (await store.ReadAsync(candidate.Handle, Identity, default))!;
        Assert.Equal(SpecialistTurnStatus.OutcomeUnknown, restored.Status);
        Assert.Single(restored.ModelRequests!);
        Assert.Equal(1, restored.AdmissionGeneration);
        Assert.True((await store.ReadConversationAsync(candidate.Conversation.ConversationId, Identity, default))!.OutcomeUnknown);
        Assert.Null(await store.ReserveModelRequestAsync(candidate.Handle, Identity, token, Reservation(1), Now.AddSeconds(31), default));
    }

    [Fact]
    public async Task Usage_without_single_request_attribution_keeps_every_reserved_output_ceiling()
    {
        var store = new InMemorySpecialistTurnStore(new Access(PersistenceAccessContext.Scoped(new("tenant"))), new());
        await store.StageAsync(Document with { MaximumModelCalls = 2 }, CancellationToken.None);
        var claimed = (await store.ClaimAsync(Document.Handle, Identity, "child", "fence", Now, CancellationToken.None))!;
        Assert.True(await store.DispatchAsync(Document.Handle, Identity, new("fence", 1), Now, CancellationToken.None));
        var descriptor = new SpecialistRuntimeDescriptor("fixture", new("fixture", "connection", "model"), Json("{}"), Json("{}"), Json("{}"), "fixture/1",
            SpecialistRuntimeIds.SchemaProfile, new(MaximumModelCalls: 2, MaximumOutputTokens: 64), false);
        var contract = new ActivityContract("specialist", "1", SpecialistRuntimeIds.ConsumerKey, JsonSerializer.SerializeToElement(descriptor), [],
            new(new("System.Text.Json.JsonElement"), true, ActivityValuePolicy.Default, []), ["Done"], new(SpecialistRuntimeIds.ConsumerKey, SpecialistRuntimeIds.FacadeCapability));
        var authority = new SpecialistTurnBudgetAllocator(store, new FixedTime()).Create(claimed, new(Identity, descriptor, contract, Json("{}"), "bindings"));
        authority.BindOutputLimit(64);
        var observed = new SpecialistModelDispatchObservation("fixture", "model", "incarnation", 1, "fixture/1", "fixture-accounted/1");
        await authority.BeforeSendAsync(observed, CancellationToken.None);
        await authority.BeforeSendAsync(observed, CancellationToken.None);
        await authority.CompleteResponseAsync(new UsageDetails { InputTokenCount = 11, OutputTokenCount = 7 }, "reported-model", CancellationToken.None);
        Assert.Equal(2, authority.Reservations);
        Assert.Equal(7, authority.ResponseObservation!.OutputTokens);
        Assert.False(authority.IsOutputUsageKnown);
        Assert.Equal(128, authority.AccountedOutputTokens);
        await Assert.ThrowsAsync<SpecialistTurnException>(async () => await authority.BeforeSendAsync(observed, CancellationToken.None));
    }

    private sealed class FixedTime : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task An_exact_response_freezes_requests_and_observations_before_acceptance(bool durable, bool unknownUsage)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        ISpecialistTurnStore store = durable ? Store(context) : new InMemorySpecialistTurnStore(new Access(PersistenceAccessContext.Scoped(new("tenant"))), new());
        await store.StageAsync(Document with { MaximumModelCalls = 2 }, CancellationToken.None);
        Assert.NotNull(await store.ClaimAsync(Document.Handle, Identity, "child", "fence", Now, CancellationToken.None));
        Assert.True(await store.DispatchAsync(Document.Handle, Identity, new("fence", 1), Now, CancellationToken.None));
        Assert.NotNull(await store.ReserveModelRequestAsync(Document.Handle, Identity, new("fence", 1), Reservation(0), Now, CancellationToken.None));
        Assert.False(await store.AcceptAsync(Document.Handle, Identity, new("fence", 1), "premature", Now, CancellationToken.None));
        var observation = new SpecialistModelResponseObservation(Now, !unknownUsage, unknownUsage ? null : 11, unknownUsage ? null : 7,
            unknownUsage ? null : 18, "observed-model");
        Assert.False(await store.CompleteModelResponseAsync(Document.Handle, Identity, new("foreign", 1), [Reservation(0).Id], observation, Now, CancellationToken.None));
        Assert.False(await store.CompleteModelResponseAsync(Document.Handle, Identity, new("fence", 1), [Reservation(1).Id], observation, Now, CancellationToken.None));
        Assert.False(await store.CompleteModelResponseAsync(Document.Handle, Identity, new("fence", 1), [Reservation(0).Id], observation, Document.Deadline, CancellationToken.None));
        Assert.True(await store.CompleteModelResponseAsync(Document.Handle, Identity, new("fence", 1), [Reservation(0).Id], observation, Now, CancellationToken.None));
        Assert.Null(await store.ReserveModelRequestAsync(Document.Handle, Identity, new("fence", 1), Reservation(1), Now, CancellationToken.None));
        Assert.False(await store.CompleteModelResponseAsync(Document.Handle, Identity, new("fence", 1), [Reservation(0).Id], observation with { OutputTokens = 0 }, Now, CancellationToken.None));
        Assert.True(await store.AcceptAsync(Document.Handle, Identity, new("fence", 1), "accepted-pair", Now, CancellationToken.None));
        var restored = await (durable ? Store(context) : store).ReadAsync(Document.Handle, Identity, CancellationToken.None);
        Assert.Equal(observation, restored!.ResponseObservation);
        Assert.Equal(64, Assert.Single(restored.ModelRequests!).MaximumOutputTokens);
        Assert.Equal("accepted-pair", restored.ProtectedResult);
        Assert.Equal(SpecialistTurnStatus.Accepted, restored.Status);
    }

    [Fact]
    public async Task Concurrent_database_reservations_are_finite_fenced_and_retained_when_the_outcome_is_unknown()
    {
        await using var database = await Database.CreateAsync();
        await using (var seed = database.Context())
        {
            var store = Store(seed);
            await store.StageAsync(Document with { MaximumModelCalls = 2 }, CancellationToken.None);
            Assert.Null(await store.ReserveModelRequestAsync(Document.Handle, Identity, new("fence", 1), Reservation(0), Now, CancellationToken.None));
            Assert.NotNull(await store.ClaimAsync(Document.Handle, Identity, "child", "fence", Now, CancellationToken.None));
            Assert.True(await store.DispatchAsync(Document.Handle, Identity, new("fence", 1), Now, CancellationToken.None));
            Assert.Null(await store.ReserveModelRequestAsync(Document.Handle, Identity, new("wrong-fence", 1), Reservation(0), Now, CancellationToken.None));
            Assert.Null(await store.ReserveModelRequestAsync(Document.Handle, Identity, new("fence", 1), Reservation(0), Document.Deadline, CancellationToken.None));
        }
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
        {
            await using var context = database.Context();
            return await Store(context).ReserveModelRequestAsync(Document.Handle, Identity, new("fence", 1), Reservation(index), Now, CancellationToken.None);
        }));
        Assert.Equal(2, attempts.Count(value => value is not null));
        await using (var context = database.Context())
            await Store(context).FailAsync(Document.Handle, Identity, new("fence", 1), "response-lost", true, CancellationToken.None);
        await using var restarted = database.Context();
        var restored = await Store(restarted).ReadAsync(Document.Handle, Identity, CancellationToken.None);
        Assert.Equal(SpecialistTurnStatus.OutcomeUnknown, restored!.Status);
        Assert.Equal(2, restored.ModelRequests!.Count);
        Assert.Equal(128, restored.ModelRequests.Sum(value => value.MaximumOutputTokens));
        Assert.Null(await Store(restarted).ReserveModelRequestAsync(Document.Handle, Identity, new("fence", 1), Reservation(9), Now, CancellationToken.None));
        Assert.Null(await Store(restarted).ClaimAsync(Document.Handle, Identity, "replacement", "new-fence", Now, CancellationToken.None));
    }

    [Fact]
    public async Task Development_reservation_collections_cannot_be_mutated_through_staged_or_returned_values()
    {
        var store = new InMemorySpecialistTurnStore(new Access(PersistenceAccessContext.Scoped(new("tenant"))), new());
        var mutable = new List<SpecialistModelRequestReservation>();
        await store.StageAsync(Document with { MaximumModelCalls = 2, ModelRequests = mutable }, CancellationToken.None);
        mutable.Add(Reservation(0));
        Assert.Empty((await store.ReadAsync(Document.Handle, Identity, CancellationToken.None))!.ModelRequests!);
        Assert.NotNull(await store.ClaimAsync(Document.Handle, Identity, "child", "fence", Now, CancellationToken.None));
        Assert.True(await store.DispatchAsync(Document.Handle, Identity, new("fence", 1), Now, CancellationToken.None));
        Assert.NotNull(await store.ReserveModelRequestAsync(Document.Handle, Identity, new("fence", 1), Reservation(1), Now, CancellationToken.None));
        var exposed = (await store.ReadAsync(Document.Handle, Identity, CancellationToken.None))!.ModelRequests!;
        ((IList<SpecialistModelRequestReservation>)exposed)[0] = Reservation(2);
        Assert.Equal(Reservation(1), Assert.Single((await store.ReadAsync(Document.Handle, Identity, CancellationToken.None))!.ModelRequests!));
        Assert.Null(await store.ReserveModelRequestAsync(Document.Handle, Identity, new("fence", 1), Reservation(1), Now, CancellationToken.None));
    }

    private static SpecialistModelRequestReservation Reservation(int index) => new(
        Convert.ToHexString(SHA256.HashData(BitConverter.GetBytes(index))).ToLowerInvariant(), 64, Now,
        new("fixture", "model", "incarnation", 1, "fixture/1", "fixture-accounted/1"));

    [Theory]
    [InlineData(SpecialistTurnStatus.Prepared)]
    [InlineData(SpecialistTurnStatus.Dispatching)]
    public async Task Development_staging_cannot_forge_claim_or_dispatch_state(SpecialistTurnStatus status)
    {
        var store = new InMemorySpecialistTurnStore(new Access(PersistenceAccessContext.Scoped(new("tenant"))), new());
        await Assert.ThrowsAsync<SpecialistJournalException>(async () => await store.StageAsync(Document with
            { Status = status, ChildInvocationId = "forged-child", Fence = "forged-fence" }, CancellationToken.None));
        Assert.Null(await store.ReadAsync(Document.Handle, Identity, CancellationToken.None));
        Assert.False(await store.AcceptAsync(Document.Handle, Identity, new("forged-fence", 1), "forged-acceptance", Now, CancellationToken.None));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Noncanonical_handles_cannot_enter_development_storage_or_its_safe_exception_context(bool validKey)
    {
        var store = new InMemorySpecialistTurnStore(new Access(PersistenceAccessContext.Scoped(new("tenant"))), new());
        var error = await Assert.ThrowsAsync<SpecialistJournalException>(async () => await store.StageAsync(Document with
            { Handle = "private value", Key = validKey ? Document.Key : new string('d', 64) }, CancellationToken.None));
        Assert.Null(error.Handle);
        Assert.DoesNotContain("private", error.Message, StringComparison.Ordinal);
        Assert.Null(await store.ReadAsync("private value", Identity, CancellationToken.None));
    }

    [Fact]
    public async Task Scoped_development_adapters_share_one_host_lifetime_journal_without_capturing_a_scoped_accessor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPersistenceAccessContextAccessor>(new Access(PersistenceAccessContext.Scoped(new("tenant"))));
        new SpecialistsInMemoryJournalFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        var writer = first.ServiceProvider.GetRequiredService<ISpecialistTurnStore>();
        var reader = second.ServiceProvider.GetRequiredService<ISpecialistTurnStore>();
        Assert.NotSame(writer, reader);
        await writer.StageAsync(Document, CancellationToken.None);
        Assert.Equal(Document, await reader.ReadAsync(Document.Handle, Identity, CancellationToken.None));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_development_backend_enforces_the_selected_ordinary_scope_at_its_public_boundary(bool global)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPersistenceAccessContextAccessor>(new Access(global ? PersistenceAccessContext.Global : PersistenceAccessContext.Scoped(new("foreign"))));
        new SpecialistsInMemoryJournalFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ISpecialistTurnStore>();
        await Assert.ThrowsAsync<SpecialistJournalException>(async () => await store.StageAsync(Document, CancellationToken.None));
    }

    [Fact]
    public async Task A_failed_conflict_recovery_read_retains_its_infrastructure_cause_behind_the_domain_boundary()
    {
        await using var database = await Database.CreateAsync();
        await using (var seed = database.Context()) await Store(seed).StageAsync(Document, CancellationToken.None);
        var cause = new IOException("private infrastructure details");
        await using var failing = database.Context(new RecoveryReadFailure(cause));
        var different = Identity with { OperationKey = "different-operation" };
        var error = await Assert.ThrowsAsync<SpecialistJournalException>(async () => await Store(failing).StageAsync(
            Document with { Identity = different, Key = SpecialistTurnDocument.CalculateOperationKey(different) }, CancellationToken.None));
        Assert.Equal("specialist.journal.store", error.Code);
        Assert.Equal(Document.Handle, error.Handle);
        Assert.Same(cause, error.InnerException);
        Assert.DoesNotContain("private", error.Message, StringComparison.Ordinal);
        await using var verify = database.Context();
        Assert.Equal(Document, await Store(verify).ReadAsync(Document.Handle, Identity, CancellationToken.None));
    }

    [Fact]
    public async Task Record_validation_is_a_domain_failure_with_its_original_cause_and_cancellation_remains_cancellation()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var store = Store(context);
        var error = await Assert.ThrowsAsync<SpecialistJournalException>(async () => await store.StageAsync(Document with { Key = new string('d', 64) }, CancellationToken.None));
        Assert.IsType<InvalidDataException>(error.InnerException);
        Assert.Equal(Document.Handle, error.Handle);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await store.ReadAsync(Document.Handle, Identity, cancelled.Token));
        Assert.Equal(0, await context.Set<SpecialistTurnRecord>().CountAsync());
    }

    [Fact]
    public async Task Malformed_native_identity_is_refused_before_it_can_alias_a_replacement_character_operation()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var malformed = Identity with { OperationKey = new string((char)0xD800, 1) };
        Assert.Throws<SpecialistJournalException>(() => SpecialistTurnDocument.CalculateOperationKey(malformed));
        await Assert.ThrowsAsync<SpecialistJournalException>(async () => await Store(context).StageAsync(Document with { Identity = malformed }, CancellationToken.None));
        Assert.Equal(0, await context.Set<SpecialistTurnRecord>().CountAsync());
    }

    [Fact]
    public void A_second_explicit_or_unowned_journal_is_refused_without_replacing_the_first()
    {
        var services = new ServiceCollection();
        services.AddSpecialistTurnStore<SpecialistsSqliteDbContext>();
        var selected = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISpecialistTurnStore));
        Assert.Throws<InvalidOperationException>(() => services.AddSpecialistTurnStore<ConsumerContext>());
        Assert.Same(selected, Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISpecialistTurnStore)));
        var unowned = new ServiceCollection();
        unowned.AddSingleton<ISpecialistTurnStore>(new InMemorySpecialistTurnStore(new Access(PersistenceAccessContext.Scoped(new("tenant"))), new()));
        Assert.Throws<InvalidOperationException>(() => unowned.AddSpecialistTurnStore<ConsumerContext>());
        Assert.IsType<InMemorySpecialistTurnStore>(Assert.Single(unowned, descriptor => descriptor.ServiceType == typeof(ISpecialistTurnStore)).ImplementationInstance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Runtime_startup_refuses_missing_or_duplicate_backend_registrations(int count)
    {
        var services = new ServiceCollection();
        new SpecialistsRuntimeFeature().ConfigureServices(services);
        for (var index = 0; index < count; index++) services.AddSingleton<ISpecialistTurnStore>(new InMemorySpecialistTurnStore(new Access(PersistenceAccessContext.Scoped(new("tenant"))), new()));
        var registration = Assert.Single(services, descriptor => descriptor.ImplementationInstance is SpecialistJournalRegistrationStartupTask);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ((SpecialistJournalRegistrationStartupTask)registration.ImplementationInstance!).ExecuteAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_explicit_database_backend_survives_both_development_feature_registration_orders(bool memoryFirst)
    {
        await using var database = await Database.CreateAsync();
        var services = new ServiceCollection();
        services.AddScoped(_ => database.Context());
        services.AddSingleton<IPersistenceAccessContextAccessor>(new Access(PersistenceAccessContext.Scoped(new("tenant"))));
        if (memoryFirst) new SpecialistsInMemoryJournalFeature().ConfigureServices(services);
        services.AddSpecialistTurnStore<SpecialistsSqliteDbContext>();
        if (!memoryFirst) new SpecialistsInMemoryJournalFeature().ConfigureServices(services);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISpecialistTurnStore));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ISpecialistTurnStore>();
        Assert.IsType<EfSpecialistTurnStore>(store);
        Assert.True((await store.GetCapabilitiesAsync()).IsDurable);
    }

    [Fact]
    public async Task A_fresh_host_recovers_the_authenticated_business_and_conversation_pair_without_reinvocation()
    {
        await using var database = await Database.CreateAsync();
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var descriptor = new SpecialistRuntimeDescriptor("private instructions", new("fixture", "connection", "model"), Json("{}"), Json("{}"), Json("{}"), "fixture/1", SpecialistRuntimeIds.SchemaProfile, new(), true);
        var contract = new ActivityContract("specialist", "1.0.0", SpecialistRuntimeIds.ConsumerKey, JsonSerializer.SerializeToElement(descriptor), [],
            new(new("System.Text.Json.JsonElement"), true, ActivityValuePolicy.Default, []), ["Done"], new(SpecialistRuntimeIds.ConsumerKey, SpecialistRuntimeIds.FacadeCapability));
        var request = new SpecialistTurnRequest(Identity, descriptor, contract, Json("{\"Brief\":\"private brief\"}"), "binding-fingerprint");
        string handle;
        await using (var first = Host())
        await using (var scope = first.CreateAsyncScope())
        {
            var journal = scope.ServiceProvider.GetRequiredService<SpecialistTurnJournal>();
            handle = await journal.StageAsync(request, Now, CancellationToken.None);
            var claim = await journal.ClaimAsync(handle, Identity, "child", Now, CancellationToken.None);
            var codec = scope.ServiceProvider.GetRequiredService<SpecialistSessionCodec>();
            var candidate = codec.Capture([new(ChatRole.User, "private brief"), new(ChatRole.Assistant, "{\"Article\":\"private accepted article\"}")], Identity, new(handle, 1));
            Assert.True(await journal.DispatchAsync(claim, Now, CancellationToken.None));
            Assert.True(await journal.AcceptAsync(claim, new(Json("{\"Article\":\"private accepted article\"}"), 5, 3, 8, 1, 1, candidate), Now, CancellationToken.None));
        }
        await using (var verify = database.Context())
        {
            var stored = await verify.Set<SpecialistTurnRecord>().SingleAsync();
            Assert.DoesNotContain("private", stored.Payload, StringComparison.Ordinal);
        }
        await using var restarted = Host();
        await using var restartScope = restarted.CreateAsyncScope();
        var recovered = restartScope.ServiceProvider.GetRequiredService<SpecialistTurnJournal>();
        var result = await recovered.ResultAsync(handle, Identity, "child", CancellationToken.None);
        Assert.Equal("private accepted article", result.Result.GetProperty("Article").GetString());
        Assert.Equal(8, result.TotalTokens);
        var restored = restartScope.ServiceProvider.GetRequiredService<SpecialistSessionCodec>().Restore(result.Session!, Identity);
        Assert.Equal("private brief", restored[0].Text);
        Assert.Equal("{\"Article\":\"private accepted article\"}", restored[1].Text);
        Assert.Equal(handle, await recovered.StageAsync(request, Now.AddDays(1), CancellationToken.None));
        await Assert.ThrowsAsync<SpecialistTurnException>(async () => await recovered.ClaimAsync(handle, Identity, "second-child", Now.AddDays(1), CancellationToken.None));

        ServiceProvider Host()
        {
            var services = new ServiceCollection();
            new SecretsFeature { EncryptionKey = key }.ConfigureServices(services);
            services.AddSingleton<IPersistenceAccessContextAccessor>(new Access(PersistenceAccessContext.Scoped(new("tenant"))));
            services.AddScoped(_ => database.Context());
            services.AddSpecialistTurnStore<SpecialistsSqliteDbContext>();
            services.AddScoped<SpecialistTurnJournal>();
            services.AddScoped<SpecialistSessionCodec>();
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }
    }

    [Fact]
    public async Task A_caller_cannot_bypass_operation_uniqueness_by_substituting_the_index_key()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var store = Store(context);
        await store.StageAsync(Document, CancellationToken.None);
        await Assert.ThrowsAsync<SpecialistJournalException>(async () => await store.StageAsync(Document with { Handle = new string('d', 64), Key = new string('e', 64) }, CancellationToken.None));
        Assert.Equal(1, await context.Set<SpecialistTurnRecord>().CountAsync());
    }

    [Fact]
    public async Task A_new_context_restores_the_single_accepted_protected_pair_and_preserves_it_after_late_failure()
    {
        await using var database = await Database.CreateAsync();
        await using (var context = database.Context())
        {
            var store = Store(context);
            Assert.True((await store.GetCapabilitiesAsync()).IsDurable);
            await store.StageAsync(Document, CancellationToken.None);
            Assert.NotNull(await store.ClaimAsync(Document.Handle, Identity, "child", "fence", Now, CancellationToken.None));
            Assert.True(await store.DispatchAsync(Document.Handle, Identity, new("fence", 1), Now, CancellationToken.None));
            Assert.True(await store.AcceptAsync(Document.Handle, Identity, new("fence", 1), "opaque-protected-result-and-session", Now, CancellationToken.None));
        }
        await using var restarted = database.Context();
        var restored = Store(restarted);
        await restored.FailAsync(Document.Handle, Identity, new("fence", 1), "late-failure", true, CancellationToken.None);
        var accepted = await restored.ReadAsync(Document.Handle, Identity, CancellationToken.None);
        Assert.NotNull(accepted);
        Assert.Equal(SpecialistTurnStatus.Accepted, accepted.Status);
        Assert.Equal("opaque-protected-result-and-session", accepted.ProtectedResult);
        Assert.Null(await restored.ClaimAsync(Document.Handle, Identity, "replacement", "replacement-fence", Now, CancellationToken.None));
    }

    [Fact]
    public async Task Operation_replay_preserves_the_original_handle_ciphertext_and_deadline_and_conflicts_change_nothing()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var store = Store(context);
        await store.StageAsync(Document, CancellationToken.None);
        var replay = await store.StageAsync(Document with { Handle = new string('d', 64), ProtectedRequest = "different-encryption-nonce", Deadline = Now.AddMinutes(10) }, CancellationToken.None);
        Assert.Equal(Document, replay);
        await Assert.ThrowsAsync<SpecialistJournalException>(async () => await store.StageAsync(Document with { RequestHash = new string('e', 64) }, CancellationToken.None));
        Assert.Equal(1, await context.Set<SpecialistTurnRecord>().CountAsync());
        Assert.Equal(Document, await store.ReadAsync(Document.Handle, Identity, CancellationToken.None));
    }

    [Fact]
    public async Task Independent_workers_have_one_database_claim_winner_and_only_its_live_fence_can_accept()
    {
        await using var database = await Database.CreateAsync();
        await using (var seed = database.Context()) await Store(seed).StageAsync(Document, CancellationToken.None);
        var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
        {
            await using var context = database.Context();
            return await Store(context).ClaimAsync(Document.Handle, Identity, "child-" + index, "fence-" + index, Now, CancellationToken.None);
        }));
        var winner = Assert.Single(claims, item => item is not null)!;
        await using var verify = database.Context();
        var store = Store(verify);
        Assert.False(await store.DispatchAsync(Document.Handle, Identity, new("foreign", 1), Now, CancellationToken.None));
        Assert.False(await store.AcceptAsync(Document.Handle, Identity, new(winner.Fence!, winner.AdmissionGeneration), "premature", Now, CancellationToken.None));
        Assert.True(await store.DispatchAsync(Document.Handle, Identity, new(winner.Fence!, winner.AdmissionGeneration), Now, CancellationToken.None));
        Assert.False(await store.AcceptAsync(Document.Handle, Identity, new("foreign", 1), "wrong-owner", Now, CancellationToken.None));
        Assert.False(await store.AcceptAsync(Document.Handle, Identity, new(winner.Fence!, winner.AdmissionGeneration), "late", Document.Deadline, CancellationToken.None));
        Assert.True(await store.AcceptAsync(Document.Handle, Identity, new(winner.Fence!, winner.AdmissionGeneration), "accepted", Now, CancellationToken.None));
    }

    [Fact]
    public async Task Exact_tenant_partition_and_native_identity_are_required_and_global_context_is_not_authority()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        await Store(context).StageAsync(Document, CancellationToken.None);
        Assert.Null(await Store(context).ReadAsync(Document.Handle, Identity with { ActivityExecutionId = "foreign" }, CancellationToken.None));
        await Assert.ThrowsAsync<SpecialistJournalException>(async () => await Store(context, PersistenceAccessContext.Global).ReadAsync(Document.Handle, Identity, CancellationToken.None));
        await Assert.ThrowsAsync<SpecialistJournalException>(async () => await Store(context).ReadAsync(Document.Handle, Identity with { TenantId = "foreign" }, CancellationToken.None));
        var foreignIdentity = Identity with { TenantId = "foreign", PersistencePartition = "foreign" };
        var foreign = Store(context, PersistenceAccessContext.Scoped(new("foreign")));
        Assert.Null(await foreign.ReadAsync(Document.Handle, foreignIdentity, CancellationToken.None));
        var own = await foreign.StageAsync(Document with { Identity = foreignIdentity, Key = SpecialistTurnDocument.CalculateOperationKey(foreignIdentity) }, CancellationToken.None);
        Assert.Equal(foreignIdentity, own.Identity);
        Assert.Equal(2, await context.Set<SpecialistTurnRecord>().CountAsync());
    }

    [Fact]
    public async Task Unknown_record_schemas_refuse_reads_and_writes_without_overwriting_the_row()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var store = Store(context);
        await store.StageAsync(Document, CancellationToken.None);
        await context.Set<SpecialistTurnRecord>().ExecuteUpdateAsync(setters => setters.SetProperty(x => x.SchemaVersion, "99"));
        await Assert.ThrowsAsync<SpecialistJournalException>(async () => await store.ReadAsync(Document.Handle, Identity, CancellationToken.None));
        await Assert.ThrowsAsync<SpecialistJournalException>(async () => await store.ClaimAsync(Document.Handle, Identity, "child", "fence", Now, CancellationToken.None));
        Assert.Equal("99", (await context.Set<SpecialistTurnRecord>().SingleAsync()).SchemaVersion);
        Assert.Equal(0, (await context.Set<SpecialistTurnRecord>().SingleAsync()).Version);
    }

    [Fact]
    public async Task Consumer_owned_context_writes_do_not_flush_or_enlist_unrelated_tracked_work()
    {
        await using var database = await Database.CreateAsync(consumer: true);
        await using var context = database.Consumer();
        context.Add(new Unrelated { Id = "pending" });
        var services = new ServiceCollection();
        services.AddScoped(_ => context);
        services.AddSingleton<IPersistenceAccessContextAccessor>(new Access(PersistenceAccessContext.Scoped(new("tenant"))));
        services.AddSpecialistTurnStore<ConsumerContext>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISpecialistTurnStore>().StageAsync(Document, CancellationToken.None);
        Assert.Equal(EntityState.Added, context.Entry(context.Set<Unrelated>().Local.Single()).State);
        await using var verification = database.Consumer();
        Assert.Equal(0, await verification.Set<Unrelated>().CountAsync());
        Assert.Equal(1, await verification.Set<SpecialistTurnRecord>().CountAsync());
    }

    [Theory]
    [InlineData("Data Source=:memory:")]
    [InlineData("Data Source=memory-journal;Mode=Memory;Cache=Shared")]
    [InlineData("Data Source=file:temporary?mode=memory&cache=shared")]
    [InlineData("Data Source=file:temporary?%6dode=memory")]
    [InlineData("Data Source=file:?cache=shared")]
    [InlineData("Data Source=file:%3Amemory%3A")]
    [InlineData("Data Source=file:journal?vfs=custom")]
    public async Task Ephemeral_profiles_never_advertise_restart_durability(string connection)
    {
        await using var context = new SpecialistsSqliteDbContext(new DbContextOptionsBuilder<SpecialistsSqliteDbContext>().UseSqlite(connection).Options);
        Assert.False((await Store(context).GetCapabilitiesAsync()).IsDurable);
    }

    private static EfSpecialistTurnStore Store(DbContext context, PersistenceAccessContext? access = null) => new(context, new Access(access ?? PersistenceAccessContext.Scoped(new("tenant"))));
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
    private sealed class Access(PersistenceAccessContext current) : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current => current; }
    private sealed class RecoveryReadFailure(Exception cause) : DbCommandInterceptor
    {
        private bool insertAttempted;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (insertAttempted && command.CommandText.StartsWith("SELECT", StringComparison.Ordinal)) throw cause;
            if (command.CommandText.StartsWith("INSERT INTO \"elsa_specialist_turns\"", StringComparison.Ordinal)) insertAttempted = true;
            return ValueTask.FromResult(result);
        }
    }
    private sealed class Unrelated { public string Id { get; set; } = ""; }
    private sealed class ConsumerContext(DbContextOptions<ConsumerContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddSpecialistsModel(Database.ProviderName!);
            modelBuilder.Entity<Unrelated>().HasKey(x => x.Id);
        }
    }
    private sealed class Database(string directory) : IAsyncDisposable
    {
        private string ConnectionString => "Data Source=" + Path.Combine(directory, "journal.db") + ";Pooling=False;Default Timeout=30";
        public SpecialistsSqliteDbContext Context(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<SpecialistsSqliteDbContext>().UseSqlite(ConnectionString).AddInterceptors(interceptors).Options);
        public ConsumerContext Consumer() => new(new DbContextOptionsBuilder<ConsumerContext>().UseSqlite(ConnectionString).Options);
        public static async Task<Database> CreateAsync(bool consumer = false)
        {
            var directory = Path.Combine(Path.GetTempPath(), "elsa-specialist-journal-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var database = new Database(directory);
            await using var context = consumer ? (DbContext)database.Consumer() : database.Context();
            await context.Database.EnsureCreatedAsync();
            return database;
        }
        public ValueTask DisposeAsync() { Directory.Delete(directory, true); return ValueTask.CompletedTask; }
    }
}
