using System.Security.Claims;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Api.Endpoints.Definitions;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Tests;

public sealed class WorkflowDefinitionAuthorizationBoundaryTests
{
    [Theory]
    [InlineData("permission", false)]
    [InlineData("permission", true)]
    [InlineData("metadata", false)]
    [InlineData("metadata", true)]
    [InlineData("reader", false)]
    [InlineData("reader", true)]
    [InlineData("manage-permission", true)]
    public async Task Infrastructure_failures_cross_the_owner_boundary_with_the_original_cause_and_resource_operation(string stage, bool manage)
    {
        var original = new IOException("Private infrastructure detail " + Guid.NewGuid().ToString("N"));
        using var fixture = new Fixture(stage, original);

        var observed = await Record.ExceptionAsync(() => fixture.AuthorizeAsync(manage));

        var failure = Assert.IsType<WorkflowDefinitionAuthorizationException>(observed);
        Assert.Equal("wf-1", failure.DefinitionId);
        Assert.Equal(manage ? "manage" : "read", failure.Operation);
        Assert.Same(original, failure.InnerException);
        Assert.DoesNotContain(original.Message, failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_visible_normalized_resource_keeps_its_native_authorization_decision(bool manage)
    {
        using var fixture = new Fixture();

        Assert.True(await fixture.AuthorizeAsync(manage));
        Assert.True(fixture.PermissionChecks > 0);
        Assert.Equal(1, fixture.MetadataReads);
        Assert.Equal(1, fixture.ReaderChecks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_native_permission_refusal_remains_false_without_reaching_resource_reads(bool manage)
    {
        using var fixture = new Fixture(deny: true);

        Assert.False(await fixture.AuthorizeAsync(manage));
        Assert.Equal(0, fixture.MetadataReads);
        Assert.Equal(0, fixture.ReaderChecks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_known_reader_authorization_refusal_remains_false(bool manage)
    {
        using var fixture = new Fixture("reader", new UnauthorizedAccessException("The owning reader refused access."));

        Assert.False(await fixture.AuthorizeAsync(manage));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_cancellation_propagates_without_becoming_an_infrastructure_failure(bool manage)
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.AuthorizeAsync(manage, cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(0, fixture.PermissionChecks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_existing_owner_boundary_failure_is_not_wrapped_again(bool manage)
    {
        var cause = new IOException("Owned infrastructure failure.");
        var original = new WorkflowDefinitionAuthorizationException("wf-1", "read", cause);
        using var fixture = new Fixture("reader", original);

        var observed = await Record.ExceptionAsync(() => fixture.AuthorizeAsync(manage));

        Assert.Same(original, observed);
        Assert.Same(cause, original.InnerException);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider root;
        private readonly IServiceScope scope;
        private readonly Boundary boundary;
        internal int PermissionChecks => boundary.PermissionChecks;
        internal int MetadataReads => boundary.MetadataReads;
        internal int ReaderChecks => boundary.ReaderChecks;

        internal Fixture(string? stage = null, Exception? failure = null, bool deny = false)
        {
            boundary = new(stage, failure, deny);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddRouting();
            services.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string>(["OwnerBoundary"], StringComparer.Ordinal));
            new WorkflowsDesignApiFeature().ConfigureServices(services);
            services.AddSingleton<IPermissionResourceHandler>(new PermissionHandler(boundary));
            services.AddSingleton<IWorkflowDefinitionStore>(new MetadataStore(boundary));
            services.Replace(ServiceDescriptor.Scoped<IWorkflowDefinitionDetailsReader>(_ => new Reader(boundary)));
            root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            scope = root.CreateScope();
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider,
                User = new(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, "actor"), new Claim(IdentityClaimTypes.Normalized, "v1"),
                    new Claim(IdentityClaimTypes.TenantId, "tenant"), new Claim(IdentityClaimTypes.Permission, "workflows/design:manage")], "OwnerBoundary"))
            };
            Assert.IsType<PermissionAuthorizationService>(scope.ServiceProvider.GetRequiredService<IPermissionAuthorizationService>());
        }

        internal Task<bool> AuthorizeAsync(bool manage, CancellationToken cancellationToken = default)
        {
            var owner = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionAuthorizer>();
            return (manage ? owner.CanManageAsync("wf-1", cancellationToken) : owner.CanReadAsync("wf-1", cancellationToken)).AsTask();
        }

        public void Dispose()
        {
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = null;
            scope.Dispose();
            root.Dispose();
        }
    }

    private sealed class Boundary(string? stage, Exception? failure, bool deny)
    {
        internal bool Deny => deny;
        internal int PermissionChecks, MetadataReads, ReaderChecks;
        internal void Observe(string current)
        {
            if (stage == current && failure is not null) throw failure;
        }
    }

    private sealed class PermissionHandler(Boundary boundary) : IPermissionResourceHandler
    {
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default)
        {
            boundary.PermissionChecks++;
            Assert.IsType<WorkflowLibraryResource>(context.Resource);
            boundary.Observe("permission");
            if (context.Permission == "workflows/design:manage") boundary.Observe("manage-permission");
            return ValueTask.FromResult<PermissionEvaluationResult?>(boundary.Deny ? PermissionEvaluationResult.Denied() : null);
        }
    }

    private sealed class MetadataStore(Boundary boundary) : IWorkflowDefinitionStore
    {
        public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            boundary.MetadataReads++;
            boundary.Observe("metadata");
            return Task.FromResult<WorkflowDefinition?>(new() { Id = id, TenantId = "tenant", Name = "Owned definition" });
        }
        public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Authorizing metadata must not read the authored definition.");
        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("No inventory is needed for one authorization decision.");
    }

    private sealed class Reader(Boundary boundary) : IWorkflowDefinitionDetailsReader
    {
        public Task AuthorizeReadAsync(string definitionId, CancellationToken cancellationToken)
        {
            boundary.ReaderChecks++;
            boundary.Observe("reader");
            return Task.CompletedTask;
        }
        public Task<WorkflowDefinitionDetailsView> ReadAsync(string definitionId, CancellationToken cancellationToken)
            => throw new NotSupportedException("The authorizer uses the reader's authority preflight.");
    }
}
