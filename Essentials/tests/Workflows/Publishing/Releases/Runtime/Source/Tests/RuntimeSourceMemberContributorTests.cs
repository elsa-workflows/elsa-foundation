using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Capture.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.Runtime.Source.Tests;

/// <summary>Actual SQLite acquisition/retention with controlled bodies; no published-root, import or activation claim.</summary>
public sealed class RuntimeSourceMemberContributorTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Actual_selected_provider_expands_complete_native_or_model_closure_under_held_grants(bool modelRoot, bool cache)
    {
        await using var h = await Harness.Create(cache); var child = Workflow("child"); await h.Store.SaveAsync(child);
        var root = Workflow("parent", [new(child.Identity.ArtifactId, child.Identity.ArtifactHash, ["node"])]); var model = Model(child.Identity);
        if (modelRoot) await h.Store.SaveModelAsync(model); else await h.Store.SaveAsync(root);
        var encoded = modelRoot ? h.Codec.EncodeModel(new(h.Policy.Current, model)) : h.Codec.EncodeWorkflow(new(h.Policy.Current, root));
        var captured = new Captured(encoded); var invoked = false;
        await h.Contributor.ExecuteAsync(captured.Members, captured, new(), async (added, token) =>
        {
            invoked = true; var member = Assert.Single(added.Members);
            Assert.Equal(child.Identity, h.Codec.DecodeWorkflow(added.GetPayload(member.Key)).Executable.Identity);
            Assert.Equal(h.Codec.EncodeWorkflow(new(h.Policy.Current, child)).GetPayloadBytes(), added.GetPayload(member.Key).ToArray());
            Assert.Equal(2, await h.Context.WorkflowExecutableRootWriteLeases.CountAsync(token));
            var now = DateTimeOffset.UtcNow;
            Assert.Null(await h.Store.TryBeginDeletionAsync(child.Identity.ArtifactId, "source-gc", now.AddMinutes(1), now, token));
        });
        Assert.True(invoked); Assert.Equal(0, await h.Context.WorkflowExecutableRootWriteLeases.CountAsync());
    }

    [Fact]
    public async Task Denied_child_is_not_read_even_when_the_retained_root_declares_it()
    {
        await using var h = await Harness.Create(); var child = Workflow("child"); await h.Store.SaveAsync(child);
        var root = Workflow("parent", [new(child.Identity.ArtifactId, child.Identity.ArtifactHash, ["node"])]); await h.Store.SaveAsync(root);
        h.Policy.DeniedMaterial = child.Identity.ArtifactId; var captured = h.Capture(root); var invoked = false;
        var counted = new CountingStore(h.Store);
        var contributor = h.WithStore(counted);
        var error = await Assert.ThrowsAsync<ReleaseCaptureException>(() => contributor.ExecuteAsync(captured.Members, captured, new(),
            (_, _) => { invoked = true; return ValueTask.CompletedTask; }).AsTask());
        Assert.Equal("runtime_source_export_forbidden", error.Code); Assert.False(invoked);
        Assert.DoesNotContain(child.Identity.ArtifactId, counted.Reads);
        Assert.Equal(0, await h.Context.WorkflowExecutableRootWriteLeases.CountAsync());
    }

    [Fact]
    public async Task Container_bytes_are_compared_with_the_actual_source_instead_of_relabeling_a_pin()
    {
        await using var h = await Harness.Create(); var actual = Workflow(); await h.Store.SaveAsync(actual);
        var changed = new WorkflowExecutable(actual.Identity, actual.RootActivity, actual.ResumeTargets, actual.CreatedAt.AddHours(1),
            actual.CompatibilityMetadata, actual.InputContract, actual.Dependencies, actual.IncidentStrategy);
        var captured = h.Capture(changed); var invoked = false;
        var error = await Assert.ThrowsAsync<ReleaseCaptureException>(() => h.Contributor.ExecuteAsync(captured.Members, captured, new(),
            (_, _) => { invoked = true; return ValueTask.CompletedTask; }).AsTask());
        Assert.Equal("runtime_source_container_conflict", error.Code); Assert.False(invoked);
        Assert.Equal(0, await h.Context.WorkflowExecutableRootWriteLeases.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changed_source_namespace_or_partition_never_enters_the_capture_callback(bool wrongPartition)
    {
        await using var h = await Harness.Create(); var root = Workflow(); await h.Store.SaveAsync(root); var captured = h.Capture(root);
        if (wrongPartition) h.Policy.Current = h.Policy.Current with { Partition = "other" };
        else h.Policy.OnAuthorize = () => h.Policy.Current = h.Policy.Current with { Authority = "changed" };
        var invoked = false;
        await Assert.ThrowsAsync<ReleaseCaptureException>(() => h.Contributor.ExecuteAsync(captured.Members, captured, new(),
            (_, _) => { invoked = true; return ValueTask.CompletedTask; }).AsTask());
        Assert.False(invoked); Assert.Equal(0, await h.Context.WorkflowExecutableRootWriteLeases.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Callback_failure_or_cancellation_releases_actual_source_grants(bool cancel)
    {
        await using var h = await Harness.Create(); var root = Workflow(); await h.Store.SaveAsync(root); var captured = h.Capture(root);
        using var cancellation = new CancellationTokenSource();
        async ValueTask Capture(IReleaseContentReader _, CancellationToken token)
        {
            Assert.Equal(1, await h.Context.WorkflowExecutableRootWriteLeases.CountAsync(token));
            if (cancel) { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
            throw new IOException("controlled callback failure");
        }
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Contributor.ExecuteAsync(captured.Members, captured, new(), Capture, cancellation.Token).AsTask());
        else await Assert.ThrowsAsync<IOException>(() => h.Contributor.ExecuteAsync(captured.Members, captured, new(), Capture).AsTask());
        Assert.Equal(0, await h.Context.WorkflowExecutableRootWriteLeases.CountAsync());
    }

    [Fact]
    public async Task Source_member_budget_refuses_a_dependency_before_acknowledging_capture()
    {
        await using var h = await Harness.Create(); var child = Workflow("child"); await h.Store.SaveAsync(child);
        var root = Workflow("parent", [new(child.Identity.ArtifactId, child.Identity.ArtifactHash, ["node"])]); await h.Store.SaveAsync(root);
        var captured = h.Capture(root); var invoked = false;
        var error = await Assert.ThrowsAsync<RuntimeSourceReadException>(() => h.Contributor.ExecuteAsync(captured.Members, captured, new() { MaxMembers = 1 },
            (_, _) => { invoked = true; return ValueTask.CompletedTask; }).AsTask());
        Assert.Equal("runtime_source_material_limit", error.Code); Assert.False(invoked); Assert.Equal(0, await h.Context.WorkflowExecutableRootWriteLeases.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_reusable_template_closure_is_copied_exactly_and_authorized_before_reads(bool deny)
    {
        await using var h = await Harness.Create(); var child = Template("child");
        var root = Template("parent", [new("child-definition", "child-version", "1.0.0", child.TemplateId, child.TemplateHash, "call", ActivityInvocationOrigin.Empty)], [new(child.TemplateId, child.TemplateHash)]);
        var writer = h.TemplateWriter; await writer.SaveAsync(child); await writer.SaveAsync(root);
        var counted = new CountingTemplates(h.TemplateReader);
        var contributor = h.WithTemplates(counted); h.Policy.DeniedTemplate = deny ? child.TemplateId : null;
        var captured = new Captured(h.Codec.EncodeTemplate(new(h.Policy.Current, root))); var invoked = false;
        async ValueTask Capture(IReleaseContentReader added, CancellationToken token)
        {
            invoked = true; var member = Assert.Single(added.Members);
            Assert.Equal(child.TemplateHash, h.Codec.DecodeTemplate(added.GetPayload(member.Key)).Template.TemplateHash);
            Assert.Equal(h.Codec.EncodeTemplate(new(h.Policy.Current, child)).GetPayloadBytes(), added.GetPayload(member.Key).ToArray());
            await Task.CompletedTask;
        }
        if (deny)
        {
            var error = await Assert.ThrowsAsync<ReleaseCaptureException>(() => contributor.ExecuteAsync(captured.Members, captured, new(), Capture).AsTask());
            Assert.Equal("runtime_source_export_forbidden", error.Code); Assert.False(invoked);
            Assert.DoesNotContain(child.TemplateId, counted.Reads);
        }
        else { await contributor.ExecuteAsync(captured.Members, captured, new(), Capture); Assert.True(invoked); }
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public async Task Actual_template_DAG_checks_the_longest_shared_descendant_path_before_callback(int maximumDepth, bool accepted)
    {
        await using var h = await Harness.Create(); var d = Template("D");
        ExecutableActivityTemplateDependency Edge(ExecutableActivityTemplate item, string occurrence) =>
            new("definition-" + occurrence, "version-" + occurrence, "1.0.0", item.TemplateId, item.TemplateHash, occurrence, ActivityInvocationOrigin.Empty);
        ExecutableActivityTemplateIdentity Pin(ExecutableActivityTemplate item) => new(item.TemplateId, item.TemplateHash);
        var c = Template("C", [Edge(d, "C-D")], [Pin(d)]);
        var b = Template("B", [Edge(c, "B-C")], [Pin(c), Pin(d)]);
        var a = Template("A", [Edge(b, "A-B")], [Pin(b), Pin(c), Pin(d)]);
        var root = Template("R", [Edge(c, "R-C"), Edge(a, "R-A")], [Pin(a), Pin(b), Pin(c), Pin(d)]);
        foreach (var item in new[] { d, c, b, a, root }) await h.TemplateWriter.SaveAsync(item);
        var captured = new Captured(h.Codec.EncodeTemplate(new(h.Policy.Current, root))); var invoked = false;
        ValueTask Capture(IReleaseContentReader added, CancellationToken token)
        { invoked = true; Assert.Equal(4, added.Members.Count); return ValueTask.CompletedTask; }
        if (accepted) { await h.Contributor.ExecuteAsync(captured.Members, captured, new() { MaxDependencyDepth = maximumDepth }, Capture); Assert.True(invoked); }
        else
        {
            var error = await Assert.ThrowsAsync<ReleaseCaptureException>(() => h.Contributor.ExecuteAsync(captured.Members, captured,
                new() { MaxDependencyDepth = maximumDepth }, Capture).AsTask());
            Assert.Equal("runtime_source_dependency_depth_limit", error.Code); Assert.False(invoked);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate-policy")]
    [InlineData("duplicate-context")]
    public void Feature_requires_unambiguous_host_selected_namespace_and_export_policy(string profile)
    {
        var services = new ServiceCollection(); var feature = new WorkflowReleaseRuntimeSourceClosureFeature(); feature.ConfigureServices(services);
        if (profile != "missing")
        {
            services.AddSingleton<IReleaseRuntimeSourceContextAccessor>(new Policy()); services.AddSingleton<IReleaseRuntimeSourceAuthorization>(new Policy());
            feature.PostConfigureServices(services);
            if (profile == "duplicate-policy") services.AddSingleton<IReleaseRuntimeSourceAuthorization>(new Policy());
            else services.AddSingleton<IReleaseRuntimeSourceContextAccessor>(new Policy());
        }
        Assert.Throws<InvalidOperationException>(() => feature.PostConfigureServices(services));
    }

    private static ExecutableActivityTemplate Template(string name, IReadOnlyCollection<ExecutableActivityTemplateDependency>? direct = null,
        IReadOnlyCollection<ExecutableActivityTemplateIdentity>? closed = null)
    {
        var root = new ExecutableNode("node", name, "fixture", "1", "fixture.consumer", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        var requirements = new[] { new RuntimeRequirement("fixture.consumer", "1") };
        var targets = new Dictionary<string, WorkflowExecutableResumeTarget>(); var metadata = new Dictionary<string, string>();
        var hash = ActivityTemplateHasher.Compute(root, targets, direct ?? [], closed ?? [], requirements, [], "fingerprint", metadata);
        return new("activity-template-" + hash[7..], hash, root, targets, direct ?? [], closed ?? [], requirements, "fingerprint", metadata, DateTimeOffset.UnixEpoch);
    }

    private static WorkflowExecutable Workflow(string id = "root", IReadOnlyCollection<WorkflowExecutableDependency>? dependencies = null)
    {
        var node = new ExecutableNode("node", "node", "fixture.Module", "1.0.0", "fixture.consumer", JsonSerializer.SerializeToElement(new { z = "a" }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        var input = new WorkflowExecutableInputContract(1, []); var edges = dependencies ?? []; var hasher = new WorkflowExecutableHasher(); var hash = hasher.ComputeHash(node, input, edges);
        return new(new(id + "-" + hash[7..], "producer", "producer-version", "1.0.0", hash), node, new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UnixEpoch, new Dictionary<string, string>(), input, edges, IncidentStrategyBuiltIns.FaultReference);
    }
    private static RuntimeModelMaterial Model(WorkflowExecutableIdentity child)
    {
        var provisional = new WorkflowExecutableIdentity("model", "model-producer", "model-version", "1.0.0", "initial");
        var seed = new RuntimeModelMaterial(1, provisional, new("fixture.protocol", 1), "fixture-profile", JsonSerializer.SerializeToElement(new { graph = "controlled" }), new(1, []), [], [new("worker", child)], []);
        return new(1, provisional with { ArtifactHash = seed.ComputeContentHash() }, seed.Protocol, seed.Profile, seed.Definition, seed.InputContract, [], seed.Dependencies, []);
    }
    private sealed class Policy : IReleaseRuntimeSourceContextAccessor, IReleaseRuntimeSourceAuthorization
    {
        public ReleaseRuntimeContentScope Current { get; set; } = new("controlled-source", null, "source-partition");
        public string? DeniedMaterial { get; set; }
        public string? DeniedTemplate { get; set; }
        public Action? OnAuthorize { get; set; }
        public List<string> AllowedMaterialReads { get; } = [];
        public ValueTask<bool> AuthorizeMaterialAsync(ReleaseRuntimeContentScope context, string id, string hash, WorkflowExecutableIdentity? fullPin,
            CancellationToken cancellationToken = default)
        {
            OnAuthorize?.Invoke();
            if (DeniedMaterial == id) return ValueTask.FromResult(false);
            AllowedMaterialReads.Add(id); return ValueTask.FromResult(true);
        }
        public ValueTask<bool> AuthorizeTemplateAsync(ReleaseRuntimeContentScope context, string id, string hash,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(id != DeniedTemplate);
    }
    private sealed class CountingStore(IWorkflowExecutableStore inner) : WorkflowExecutableStoreDecorator(inner), IWorkflowExecutableStore, IRuntimeBoundedSourceLeaseStore
    {
        public List<string> Reads { get; } = [];
        public Func<ValueTask>? BeforeAcquire { get; set; }
        public bool SupportsModelMaterial => inner.SupportsModelMaterial;
        public ValueTask<RuntimeMaterialSourceSnapshot?> FindMaterialBoundedAsync(string id, PersistenceScope scope, RuntimeSourceReadLimits limits, CancellationToken token = default)
        { Reads.Add(id); return inner.FindMaterialBoundedAsync(id, scope, limits, token); }
        public override ValueTask<RuntimeStorePage<WorkflowExecutable>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken token = default) =>
            throw new InvalidOperationException("Bounded source retention must never enumerate unrelated material.");
        public async ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireAsync(string id, string leaseId, DateTimeOffset expires, DateTimeOffset now, PersistenceScope scope, RuntimeSourceReadLimits limits, CancellationToken token = default)
        {
            var before = BeforeAcquire; BeforeAcquire = null;
            if (before is not null) await before();
            return await ((IRuntimeBoundedSourceLeaseStore)inner).TryAcquireAsync(id, leaseId, expires, now, scope, limits, token);
        }
        public ValueTask<bool> RenewAsync(WorkflowExecutableRootWriteLease lease, DateTimeOffset expires, DateTimeOffset now, PersistenceScope scope, RuntimeSourceReadLimits limits, CancellationToken token = default) => ((IRuntimeBoundedSourceLeaseStore)inner).RenewAsync(lease, expires, now, scope, limits, token);
        public ValueTask ReleaseAsync(WorkflowExecutableRootWriteLease lease, PersistenceScope scope, RuntimeSourceReadLimits limits, CancellationToken token = default) => ((IRuntimeBoundedSourceLeaseStore)inner).ReleaseAsync(lease, scope, limits, token);
    }
    private sealed class CountingTemplates(IExecutableActivityTemplateReader inner) : IExecutableActivityTemplateReader
    {
        public List<string> Reads { get; } = [];
        public ValueTask<RuntimeTemplateSourceSnapshot?> FindBoundedAsync(string id, PersistenceScope scope, RuntimeSourceReadLimits limits, CancellationToken token = default)
        { Reads.Add(id); return inner.FindBoundedAsync(id, scope, limits, token); }
        public ValueTask<ExecutableActivityTemplate?> FindAsync(string id, CancellationToken token = default) => throw new InvalidOperationException("No legacy template fallback.");
        public ValueTask<ExecutableActivityTemplate?> FindByHashAsync(string hash, CancellationToken token = default) => throw new InvalidOperationException("No hash-search substitution.");
        public ValueTask<RuntimeStorePage<ExecutableActivityTemplate>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken token = default) => throw new InvalidOperationException("No unrelated template enumeration.");
    }
    private sealed class Accessor : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new("source-partition")); }
    private sealed class Captured(ReleaseEncodedRuntimeMember encoded) : IReleaseContentReader
    {
        public IReadOnlyList<ReleaseContentMember> Members { get; } = [new(encoded.Descriptor, encoded.OwnerVersion)];
        public ReleaseContentMember GetMember(ReleaseMemberKey key) => Members.SingleOrDefault(member => member.Key == key) ?? throw new KeyNotFoundException();
        public ReadOnlyMemory<byte> GetPayload(ReleaseMemberKey key) { _ = GetMember(key); return encoded.GetPayloadBytes(); }
    }
    private sealed class Harness : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "elsa-runtime-source-" + Guid.NewGuid().ToString("N") + ".sqlite");
        private ServiceProvider _provider = null!;
        private AsyncServiceScope _scope;
        public Policy Policy { get; } = new();
        public RuntimeDbContext Context { get; private set; } = null!;
        public IWorkflowExecutableStore Store { get; private set; } = null!;
        public IReleaseRuntimeArtifactCodec Codec { get; private set; } = null!;
        public IReleaseSourceMemberContributor Contributor { get; private set; } = null!;
        public IExecutableActivityTemplateReader TemplateReader => _scope.ServiceProvider.GetRequiredService<IExecutableActivityTemplateReader>();
        public IExecutableActivityTemplateWriter TemplateWriter => _scope.ServiceProvider.GetRequiredService<IExecutableActivityTemplateWriter>();
        private WorkflowExecutableRootWriteLeaseManager Manager(IWorkflowExecutableStore store) => new(store,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions { RootWriteLeaseDuration = TimeSpan.FromMinutes(1) }), TimeProvider.System);
        public IReleaseSourceMemberContributor WithStore(IWorkflowExecutableStore store) => new RuntimeSourceMemberContributor(store, TemplateReader,
            Manager(store), Codec, Policy, Policy, new Accessor());
        public IReleaseSourceMemberContributor WithTemplates(IExecutableActivityTemplateReader templates) => new RuntimeSourceMemberContributor(Store, templates,
            Manager(Store), Codec, Policy, Policy, new Accessor());
        public Captured Capture(WorkflowExecutable item) => new(Codec.EncodeWorkflow(new(Policy.Current, item)));
        public static async Task<Harness> Create(bool cache = true)
        {
            var h = new Harness(); var services = new ServiceCollection(); services.AddWorkflowRuntime();
            services.AddSingleton<IPersistenceAccessContextAccessor>(new Accessor());
            new RuntimeEntityFrameworkCoreFeature
            {
                Provider = "Sqlite", ConnectionString = "Data Source=" + h._path + ";Pooling=False", CacheWorkflowExecutables = cache,
                RecoveryContinuationSigningKey = "source-contributor-recovery-key-32-bytes",
                HierarchyCursorSigningKey = "source-contributor-hierarchy-key-32-bytes"
            }.ConfigureServices(services);
            new WorkflowReleaseRuntimeFeature().ConfigureServices(services);
            services.AddSingleton<IReleaseRuntimeSourceContextAccessor>(h.Policy); services.AddSingleton<IReleaseRuntimeSourceAuthorization>(h.Policy);
            var feature = new WorkflowReleaseRuntimeSourceClosureFeature(); feature.ConfigureServices(services); feature.PostConfigureServices(services);
            h._provider = services.BuildServiceProvider(validateScopes: true); h._scope = h._provider.CreateAsyncScope();
            h.Context = h._scope.ServiceProvider.GetRequiredService<RuntimeDbContext>(); await h.Context.Database.EnsureCreatedAsync();
            h.Store = h._scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
            h.Codec = h._scope.ServiceProvider.GetRequiredService<IReleaseRuntimeArtifactCodec>();
            h.Contributor = h._scope.ServiceProvider.GetServices<IReleaseSourceMemberContributor>().Single(); return h;
        }
        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync(); await _provider.DisposeAsync(); SqliteConnection.ClearAllPools();
            File.Delete(_path); File.Delete(_path + "-wal"); File.Delete(_path + "-shm");
        }
    }
}
