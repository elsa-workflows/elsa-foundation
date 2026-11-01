using Elsa.Agent.Specialists.Core.Contracts;
using System.Security.Cryptography;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Recovery;

public sealed class SpecialistEfJournalTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 5, 0, 0, TimeSpan.Zero);
    private static SpecialistInvocationIdentity Identity => new("tenant", "tenant", "workflow", "activity", "operation", "artifact", "hash", "version", "template");
    private static SpecialistTurnDocument Document => new(new string('a', 64), SpecialistTurnDocument.CalculateOperationKey(Identity), new string('c', 64), Identity, "opaque-protected-request", Now.AddMinutes(5));

    [Fact]
    public async Task Malformed_native_identity_is_refused_before_it_can_alias_a_replacement_character_operation()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var malformed = Identity with { OperationKey = new string((char)0xD800, 1) };
        Assert.Throws<System.Text.EncoderFallbackException>(() => SpecialistTurnDocument.CalculateOperationKey(malformed));
        await Assert.ThrowsAsync<System.Text.EncoderFallbackException>(async () => await Store(context).StageAsync(Document with { Identity = malformed }, CancellationToken.None));
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
        unowned.AddSingleton<ISpecialistTurnStore>(new InMemorySpecialistTurnStore());
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
        for (var index = 0; index < count; index++) services.AddSingleton<ISpecialistTurnStore>(new InMemorySpecialistTurnStore());
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
        await Assert.ThrowsAsync<InvalidDataException>(async () => await store.StageAsync(Document with { Handle = new string('d', 64), Key = new string('e', 64) }, CancellationToken.None));
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
            Assert.True(await store.DispatchAsync(Document.Handle, Identity, "fence", Now, CancellationToken.None));
            Assert.True(await store.AcceptAsync(Document.Handle, Identity, "fence", "opaque-protected-result-and-session", Now, CancellationToken.None));
        }
        await using var restarted = database.Context();
        var restored = Store(restarted);
        await restored.FailAsync(Document.Handle, Identity, "fence", "late-failure", true, CancellationToken.None);
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
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.StageAsync(Document with { RequestHash = new string('e', 64) }, CancellationToken.None));
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
        Assert.False(await store.DispatchAsync(Document.Handle, Identity, "foreign", Now, CancellationToken.None));
        Assert.False(await store.AcceptAsync(Document.Handle, Identity, winner.Fence!, "premature", Now, CancellationToken.None));
        Assert.True(await store.DispatchAsync(Document.Handle, Identity, winner.Fence!, Now, CancellationToken.None));
        Assert.False(await store.AcceptAsync(Document.Handle, Identity, "foreign", "wrong-owner", Now, CancellationToken.None));
        Assert.False(await store.AcceptAsync(Document.Handle, Identity, winner.Fence!, "late", Document.Deadline, CancellationToken.None));
        Assert.True(await store.AcceptAsync(Document.Handle, Identity, winner.Fence!, "accepted", Now, CancellationToken.None));
    }

    [Fact]
    public async Task Exact_tenant_partition_and_native_identity_are_required_and_global_context_is_not_authority()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        await Store(context).StageAsync(Document, CancellationToken.None);
        Assert.Null(await Store(context).ReadAsync(Document.Handle, Identity with { ActivityExecutionId = "foreign" }, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Store(context, PersistenceAccessContext.Global).ReadAsync(Document.Handle, Identity, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Store(context).ReadAsync(Document.Handle, Identity with { TenantId = "foreign" }, CancellationToken.None));
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
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.ReadAsync(Document.Handle, Identity, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.ClaimAsync(Document.Handle, Identity, "child", "fence", Now, CancellationToken.None));
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
        public SpecialistsSqliteDbContext Context() => new(new DbContextOptionsBuilder<SpecialistsSqliteDbContext>().UseSqlite(ConnectionString).Options);
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
