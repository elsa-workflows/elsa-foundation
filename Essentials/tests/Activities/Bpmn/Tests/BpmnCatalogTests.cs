using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Elsa.Activities.Bpmn.Api.Endpoints;
using Elsa.Activities.Bpmn.Api;
using Elsa.Activities.Bpmn.Core;
using Elsa.Api.Capabilities.Models;
using Elsa.Foundation.Identity;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>Bounded read/authorization controls; not a host-recovery or browser witness.</summary>
public sealed class BpmnCatalogTests
{
    [Fact]
    public async Task A_host_without_a_selected_model_reader_composes_but_refuses_execution_discovery()
    {
        var fixture = new Fixture(missingReader: true);
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => fixture.Catalog.ExecutionsAsync(new(), default).AsTask());
        Assert.Empty(fixture.ExecutionQueries);
    }
    [Fact]
    public async Task Optional_hosting_feature_maps_catalog_routes_and_registers_one_scoped_catalog_and_declaration()
    {
        var builder = WebApplication.CreateBuilder();
        var feature = new ActivitiesBpmnHostingApiFeature();
        feature.ConfigureServices(builder.Services); feature.ConfigureServices(builder.Services);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(builder.Services, d => d.ServiceType == typeof(BpmnCatalog)).Lifetime);
        await using var app = builder.Build(); feature.MapEndpoints(app, null);
        var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText).ToArray();
        Assert.Contains(routes, route => route!.TrimStart('/') == "bpmn/publications");
        Assert.Contains(routes, route => route!.TrimStart('/') == "bpmn/executions");
        var capability = Assert.Single(app.Services.GetServices<ApiCapabilityDeclaration>(), c => c.CapabilityId == "elsa.api.bpmn-catalog");
        Assert.Equal(["bpmn-executions", "bpmn-publications"], capability.Links.Select(link => link.Rel));
        var inspection = Assert.Single(app.Services.GetServices<ApiCapabilityDeclaration>(), c => c.CapabilityId == "elsa.api.bpmn-inspection");
        Assert.Equal("bpmn-inspection", Assert.Single(inspection.Links).Rel);
        Assert.Contains(routes, route => route!.TrimStart('/') == "bpmn/executions/{id}/inspection");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_full_filtered_page_examines_at_most_its_requested_raw_budget(bool publications)
    {
        var f = new Fixture();
        if (publications)
        {
            f.SourceItems = Enumerable.Range(0, 500).Select(i => f.Source() with { SourceReferenceId = "foreign-" + i, TenantId = "other" }).ToArray();
            var page = await f.Catalog.PublicationsAsync(new(500), default);
            Assert.Empty(page.Items); Assert.NotNull(page.NextContinuationToken);
            Assert.Equal(500, Assert.Single(f.SourceQueries).Limit); Assert.Empty(f.MaterialReads);
        }
        else
        {
            f.ExecutionItems = Enumerable.Range(0, 500).Select(i => f.State("foreign-" + i, tenant: "other")).ToArray();
            var page = await f.Catalog.ExecutionsAsync(new(500), default);
            Assert.Empty(page.Items); Assert.NotNull(page.NextContinuationToken);
            Assert.Equal(500, Assert.Single(f.ExecutionQueries).Limit);
        }
        Assert.Empty(f.Authorized);
    }

    [Fact]
    public async Task Filtered_source_page_may_be_empty_with_a_protected_continuation_without_refilling()
    {
        var f = new Fixture();
        f.SourceItems = [f.Source() with { SourceKind = "NativeWorkflow" }, f.Source() with { TenantId = "foreign" }];
        var page = await f.Catalog.PublicationsAsync(new(), default);
        Assert.Empty(page.Items); Assert.NotNull(page.NextContinuationToken);
        Assert.NotEqual("source-next", page.NextContinuationToken);
        Assert.Single(f.SourceQueries); Assert.Equal(50, f.SourceQueries[0].Limit);
        Assert.Empty(f.MaterialReads); Assert.Empty(f.Authorized);
    }

    [Fact]
    public async Task Source_continuation_preserves_the_initial_live_clock_and_provider_cursor()
    {
        var f = new Fixture(); f.SourceItems = [f.Source()];
        var first = await f.Catalog.PublicationsAsync(new(), default);
        f.Clock.Now += TimeSpan.FromDays(1);
        var second = await f.Catalog.PublicationsAsync(new(ContinuationToken: first.NextContinuationToken), default);
        Assert.Single(first.Items); Assert.Single(second.Items);
        Assert.Equal(2, f.SourceQueries.Count);
        Assert.Equal(f.SourceQueries[0].Now, f.SourceQueries[1].Now);
        Assert.Equal("source-next", f.SourceQueries[1].ContinuationToken);
        Assert.All(f.SourceQueries, q => { Assert.Equal(WorkflowExecutableReferenceScope.Published, q.Scope); Assert.True(q.LiveOnly); });
    }

    [Fact]
    public async Task Source_resource_denial_precedes_material_reads_and_reauthorization_applies_on_later_pages()
    {
        var f = new Fixture(); f.SourceItems = [f.Source()];
        var first = await f.Catalog.PublicationsAsync(new(), default);
        Assert.Single(first.Items); Assert.Single(f.MaterialReads);
        f.Denied = true;
        var second = await f.Catalog.PublicationsAsync(new(ContinuationToken: first.NextContinuationToken), default);
        Assert.Empty(second.Items); Assert.Single(f.MaterialReads); Assert.Equal(2, f.Authorized.Count);
        Assert.All(f.Authorized, c => { Assert.Equal("bpmn:read", c.Permission); Assert.Equal("tenant", c.TenantId); });
    }

    [Theory]
    [InlineData("subject")]
    [InlineData("tenant")]
    [InlineData("scope")]
    [InlineData("filter")]
    [InlineData("limit")]
    [InlineData("purpose")]
    public async Task Cursor_reuse_in_another_context_is_refused_before_a_provider_read(string change)
    {
        var f = new Fixture(); f.SourceItems = [f.Source()];
        if (change == "scope") { f.Tenant = null; f.SetPrincipal(); }
        var first = await f.Catalog.PublicationsAsync(new(), default);
        if (change == "subject") f.Subject = "other";
        if (change == "tenant") { f.Tenant = "other"; f.Scope.Current = PersistenceAccessContext.Scoped(new("other")); }
        if (change == "scope") f.Scope.Current = PersistenceAccessContext.Scoped(new("other-scope"));
        f.SetPrincipal();
        var request = new BpmnCatalogRequest(change == "limit" ? 2 : 50, first.NextContinuationToken, change == "filter" ? "different" : null);
        await Assert.ThrowsAsync<ArgumentException>(() => change == "purpose"
            ? f.Catalog.ExecutionsAsync(request, default).AsTask()
            : f.Catalog.PublicationsAsync(request, default).AsTask());
        Assert.Single(f.SourceQueries); Assert.Empty(f.ExecutionQueries);
    }

    [Fact]
    public async Task Execution_page_filters_protocol_scope_tenant_and_resource_without_disclosing_private_payload()
    {
        var f = new Fixture();
        f.ExecutionItems = [f.State("visible"), f.State("denied"), f.State("foreign", tenant: "other"),
            f.State("scope", scope: "other"), f.State("other-protocol", protocol: new("other", 1))];
        var page = await f.Catalog.ExecutionsAsync(new(), default);
        var view = Assert.Single(page.Items); Assert.Equal("visible", view.ExecutionId);
        Assert.Equal(["visible", "denied"], f.Authorized.Select(c => Assert.IsType<RuntimeModelExecutionState>(c.Resource).ExecutionId));
        Assert.Single(f.ExecutionQueries); Assert.NotNull(page.NextContinuationToken);
        Assert.DoesNotContain("privateValue", JsonSerializer.Serialize(view));
    }

    [Fact]
    public async Task Execution_continuation_is_live_and_resumes_only_one_page_with_the_same_filter()
    {
        var f = new Fixture(); f.ExecutionItems = [f.State("visible"), f.State("unrelated", artifact: "another")];
        var first = await f.Catalog.ExecutionsAsync(new(ArtifactId: "artifact"), default);
        Assert.Equal("visible", Assert.Single(first.Items).ExecutionId);
        f.Denied = true;
        var next = await f.Catalog.ExecutionsAsync(new(ContinuationToken: first.NextContinuationToken, ArtifactId: "artifact"), default);
        Assert.Empty(next.Items); Assert.Equal(2, f.ExecutionQueries.Count);
        Assert.Equal("execution-next", f.ExecutionQueries[1].ContinuationToken);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("retired")]
    [InlineData("test-run")]
    [InlineData("missing-material")]
    [InlineData("foreign-pin")]
    [InlineData("other-protocol")]
    public async Task Unavailable_or_inconsistent_sources_are_not_offered_as_publications(string change)
    {
        var f = new Fixture(); var source = f.Source();
        source = change switch
        {
            "expired" => source with { ExpiresAt = f.Clock.Now },
            "retired" => source with { DeletedAt = f.Clock.Now },
            "test-run" => source with { Scope = WorkflowExecutableReferenceScope.TestRun },
            "foreign-pin" => source with { DefinitionVersionId = "another" },
            _ => source
        };
        f.SourceItems = [source];
        if (change == "missing-material") f.Material = null;
        if (change == "other-protocol") f.Material = f.BuildMaterial(new("other", 1));
        Assert.Empty((await f.Catalog.PublicationsAsync(new(), default)).Items);
        Assert.Single(f.SourceQueries);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(501, null)]
    [InlineData(50, " ")]
    [InlineData(50, "unprotected")]
    public async Task Invalid_budgets_and_cursors_are_refused_before_reading(int limit, string? cursor)
    {
        var f = new Fixture();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => f.Catalog.PublicationsAsync(new(limit, cursor), default).AsTask());
        Assert.Empty(f.SourceQueries);
    }

    [Fact]
    public async Task Unnormalized_identity_and_global_scope_are_refused_before_reading()
    {
        var f = new Fixture(); f.Context.HttpContext!.User = new ClaimsPrincipal();
        await Assert.ThrowsAsync<BpmnModelResourceMissingException>(() => f.Catalog.ExecutionsAsync(new(), default).AsTask());
        f.SetPrincipal(); f.Scope.Current = PersistenceAccessContext.Global;
        await Assert.ThrowsAnyAsync<Exception>(() => f.Catalog.ExecutionsAsync(new(), default).AsTask());
        Assert.Empty(f.ExecutionQueries);
    }

    [Fact]
    public async Task Provider_failures_and_requested_cancellation_are_not_empty_success()
    {
        var f = new Fixture(); var error = new InvalidOperationException("provider failure"); f.ReadError = error;
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => f.Catalog.ExecutionsAsync(new(), default).AsTask()));
        f.ReadError = null;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Catalog.ExecutionsAsync(new(), cancellation.Token).AsTask());
    }

    private sealed class Fixture
    {
        public readonly Clock Clock = new(); public readonly Scope Scope = new();
        public readonly HttpContextAccessor Context = new() { HttpContext = new DefaultHttpContext() };
        public string Subject = "reader"; public string? Tenant = "tenant"; public bool Denied; public Exception? ReadError;
        public readonly List<WorkflowExecutableSourceReferencePageQuery> SourceQueries = [];
        public readonly List<RuntimeStorePageRequest> ExecutionQueries = [];
        public readonly List<string> MaterialReads = []; public readonly List<PermissionEvaluationContext> Authorized = [];
        public IReadOnlyList<WorkflowExecutableSourceReference> SourceItems = [];
        public IReadOnlyList<RuntimeModelExecutionState> ExecutionItems = [];
        public RuntimeExecutionMaterial? Material;
        public BpmnCatalog Catalog { get; }
        public Fixture(bool missingReader = false)
        {
            SetPrincipal(); Material = BuildMaterial();
            var sources = Proxy<IWorkflowExecutableSourceReferenceStore>((method, args) =>
            {
                Assert.Equal("ListPageAsync", method); var query = (WorkflowExecutableSourceReferencePageQuery)args[0]!;
                SourceQueries.Add(query); return ValueTask.FromResult(new RuntimeStorePage<WorkflowExecutableSourceReference>(query, SourceItems,
                    query.ContinuationToken is null ? "source-next" : null));
            });
            var material = Proxy<IWorkflowExecutableStore>((method, args) =>
            {
                Assert.Equal("FindMaterialAsync", method); MaterialReads.Add((string)args[0]!); return ValueTask.FromResult(Material);
            });
            var executions = Proxy<IRuntimeModelExecutionReader>((method, args) =>
            {
                Assert.Equal("ListPageAsync", method); if (ReadError is not null) throw ReadError;
                ((CancellationToken)args[1]!).ThrowIfCancellationRequested();
                var query = (RuntimeStorePageRequest)args[0]!; ExecutionQueries.Add(query);
                return ValueTask.FromResult(new RuntimeStorePage<RuntimeModelExecutionState>(query, ExecutionItems,
                    query.ContinuationToken is null && ExecutionItems.Count > 0 ? "execution-next" : null));
            });
            var permissions = new Permission(c => { Authorized.Add(c); return !Denied && c.Resource is not RuntimeModelExecutionState { ExecutionId: "denied" }; });
            var caller = new BpmnApiCaller(Context, new(Options.Create(new FoundationIdentityOptions { NormalizedAuthenticationTypes = new HashSet<string>(["catalog-unit"]) })), permissions,
                Proxy<IBpmnModelRuntime>((_, _) => throw new InvalidOperationException("Catalog must use bounded readers.")));
            Catalog = new(sources, material, Scope, caller, new Codec(), Clock, missingReader ? null : executions);
        }
        public void SetPrincipal()
        {
            var claims = new List<Claim> { new(IdentityClaimTypes.Normalized, "v1"), new(ClaimTypes.NameIdentifier, Subject) };
            if (Tenant is not null) claims.Add(new(IdentityClaimTypes.TenantId, Tenant));
            Context.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "catalog-unit"));
        }
        public WorkflowExecutableSourceReference Source() => new("source", "artifact", "BpmnModel", "authored-version", "2.0.0",
            "definition", "version", "1.0.0", Clock.Now - TimeSpan.FromDays(2), Clock.Now - TimeSpan.FromDays(2), WorkflowExecutableReferenceScope.Published, TenantId: "tenant");
        public RuntimeExecutionMaterial BuildMaterial(RuntimeExecutionProtocol? protocol = null) => new(new RuntimeModelMaterial(1, Pin("artifact"),
            protocol ?? new(BpmnModelProtocol.Key, BpmnModelProtocol.Version), BpmnModelProtocol.Profile, JsonSerializer.SerializeToElement(new { }), new(1, []), [], [], []), "incarnation");
        public RuntimeModelExecutionState State(string id, string tenant = "tenant", string scope = "tenant", RuntimeExecutionProtocol? protocol = null, string artifact = "artifact") =>
            new(1, new(scope), id, tenant, protocol ?? new(BpmnModelProtocol.Key, BpmnModelProtocol.Version), Pin(artifact), "incarnation", 1,
                RuntimeModelLifecycle.Running, new("system", "initiator"), Clock.Now, Clock.Now, JsonSerializer.SerializeToElement(new { privateValue = "not disclosed" }), null);
        private static WorkflowExecutableIdentity Pin(string artifact) => new(artifact, "definition", "version", "1.0.0", "hash");
    }
    private sealed class Scope : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current { get; set; } = PersistenceAccessContext.Scoped(new("tenant")); }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.Parse("2026-11-01T10:00:00Z"); public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Permission(Func<PermissionEvaluationContext, bool> allow) : IPermissionAuthorizationService
    {
        public ValueTask<PermissionEvaluationResult> AuthorizeAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(allow(context) ? PermissionEvaluationResult.Success : PermissionEvaluationResult.Denied());
    }
    private sealed class Codec : IRuntimeRecoveryContinuationCodec
    {
        private readonly Dictionary<string, (string Purpose, byte[] Bytes)> _values = [];
        public string Encode(string purpose, ReadOnlySpan<byte> payload) { var id = Guid.NewGuid().ToString(); _values[id] = (purpose, payload.ToArray()); return id; }
        public byte[] Decode(string purpose, string token) => _values.TryGetValue(token, out var value) && value.Purpose == purpose ? value.Bytes.ToArray() : throw new ArgumentException("Invalid protected cursor.");
    }
    private static T Proxy<T>(Func<string, object?[], object?> invoke) where T : class
    { var proxy = DispatchProxy.Create<T, ReadProxy>(); ((ReadProxy)(object)proxy).InvokeRead = invoke; return proxy; }
    public class ReadProxy : DispatchProxy
    {
        public Func<string, object?[], object?> InvokeRead { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => InvokeRead(method!.Name, args!);
    }
}
