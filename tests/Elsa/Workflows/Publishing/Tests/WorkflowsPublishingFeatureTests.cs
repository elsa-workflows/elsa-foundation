using Elsa.Events.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Reconciliation.Core;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Publishing.Tests;

/// <summary>
/// §2.23.1 registration test for the endpoint-free publish engine (US1 / FR-008). Composing the
/// <see cref="WorkflowsPublishingFeature"/> alone must yield a fully wired workflow-publish + compile
/// surface, and NOTHING from the transport boundary: no authorization context, no HTTP endpoints.
/// This is what makes the engine reusable by a headless runtime node (the PR2 enabler).
/// </summary>
public sealed class WorkflowsPublishingFeatureTests
{
    // The authorization context stays in the Api feature (Decision 3). Referenced by full name so this
    // test project needs no reference to …Publishing.Api — the engine's independence is structural.
    private const string AuthorizationContextTypeName =
        "Elsa.Workflows.Publishing.Api.Contracts.IActivityPublishingAuthorizationContext";

    private const string FastEndpointsAssemblyName = "Elsa.Api.FastEndpoints";

    private static ServiceCollection ComposeEngine()
    {
        var services = new ServiceCollection();
        new WorkflowsPublishingFeature().ConfigureServices(services);
        return services;
    }

    [Fact]
    public void Registers_the_workflow_publish_handler()
    {
        var services = ComposeEngine();

        // The PublishWorkflow command + its PublishedWorkflowView response now live in …Publishing.Core,
        // and the orchestration handler is owned by the engine — so the engine dispatches publish headless.
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRequestHandler<PublishWorkflow, PublishedWorkflowView>));
    }

    [Fact]
    public void Registers_the_executable_compiler_and_publication_stores()
    {
        var services = ComposeEngine();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowExecutableCompiler));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowExecutableStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowExecutableSourceReferenceStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowActivationAuthority));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IPublicationRecordStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IPublicationPolicyStore));
    }

    [Fact]
    public void Registers_the_publish_on_reconcile_subscriber()
    {
        var services = ComposeEngine();

        // spec 147: the engine subscribes to the Design-side reconcile completion event so sources with
        // PublishOnReconcile become executable at startup. Independent subscriber — no contributor interface.
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IEventHandler<WorkflowVersionsReconciled>) &&
            descriptor.ImplementationType == typeof(PublishReconciledWorkflowVersions));
    }

    [Fact]
    public void Does_not_register_the_transport_authorization_context()
    {
        var services = ComposeEngine();

        // SC-002: the engine is authorization-free. No neutral default is introduced; the context is
        // supplied only by the transport (Api) feature.
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType.FullName == AuthorizationContextTypeName);
    }

    [Fact]
    public void Does_not_register_any_FastEndpoints_publish_endpoints()
    {
        var services = ComposeEngine();

        // SC-003: no transport is mounted by the engine. Nothing it registers originates in the
        // FastEndpoints transport assembly.
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType.Assembly.GetName().Name == FastEndpointsAssemblyName ||
            descriptor.ImplementationType?.Assembly.GetName().Name == FastEndpointsAssemblyName);
    }

    [Fact]
    public void Does_not_reference_the_transport_assemblies_at_all()
    {
        // The two absence assertions above are satisfied by this project's reference list as much as by the
        // engine's own registrations, so they cannot fail while the engine stays reference-clean. This is the
        // assertion that can: it binds the engine assembly itself, so a future `using` that drags the transport
        // back into the engine fails here instead of silently re-coupling the two.
        var referenced = typeof(WorkflowsPublishingFeature).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToArray();

        Assert.DoesNotContain("Elsa.Workflows.Publishing.Api", referenced);
        Assert.DoesNotContain(FastEndpointsAssemblyName, referenced);
    }
}
