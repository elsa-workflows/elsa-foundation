using System.Runtime.CompilerServices;
using CShells.Features;
using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Api.Capabilities;
using Elsa.Api.Capabilities.Models;
using Elsa.Expressions.Api;
using Elsa.Expressions.Api.Capabilities;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Api.Capabilities;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Capabilities;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class DomainApiCapabilityRegistrationTests
{
    public static TheoryData<Type, ApiCapabilityDeclaration> DomainFeatures => new()
    {
        { typeof(WorkflowsDesignApiFeature), WorkflowDesignApiCapabilities.StaticDeclaration },
        { typeof(ActivitiesDesignApiFeature), ActivityDesignApiCapabilities.StaticDeclaration },
        { typeof(ExpressionsApiFeature), ExpressionsApiCapabilities.StaticDeclaration },
        { typeof(WorkflowsPublishingApiFeature), PublishingApiCapabilities.StaticDeclaration },
        { typeof(WorkflowsRuntimeApiFeature), RuntimeApiCapabilities.StaticDeclaration }
    };

    [Theory]
    [MemberData(nameof(DomainFeatures))]
    public void Canonical_domain_api_depends_on_capability_aggregation_and_registers_one_stable_declaration(
        Type featureType,
        ApiCapabilityDeclaration expected)
    {
        var featureAttribute = Assert.Single(featureType.GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false).Cast<ShellFeatureAttribute>());
        Assert.Contains("ApiCapabilities", featureAttribute.DependsOn.Select(dependency => dependency?.ToString()));

        var services = new ServiceCollection();
        ((IShellFeature)Activator.CreateInstance(featureType)!).ConfigureServices(services);
        var declaration = Assert.Single(
            services.Where(descriptor => descriptor.ServiceType == typeof(ApiCapabilityDeclaration))
                .Select(descriptor => descriptor.ImplementationInstance)
                .OfType<ApiCapabilityDeclaration>());

        Assert.Equal(expected, declaration);
        Assert.All(declaration.Links, link =>
        {
            Assert.False(link.Href.StartsWith('/'));
            Assert.False(Uri.TryCreate(link.Href, UriKind.Absolute, out _));
        });
    }

    [Fact]
    public async Task Workflow_design_operational_source_contributes_only_composed_authoring_links()
    {
        var unavailable = await new WorkflowDesignOperationalCapabilitySource().GetCapabilitiesAsync();
        var scopedVariables = (ScopedVariableAuthoringContract)RuntimeHelpers.GetUninitializedObject(typeof(ScopedVariableAuthoringContract));
        var scopedOnly = await new WorkflowDesignOperationalCapabilitySource(scopedVariables).GetCapabilitiesAsync();
        var complete = await new WorkflowDesignOperationalCapabilitySource(
            scopedVariables,
            [new StubInputOptionsProvider()]).GetCapabilitiesAsync();

        Assert.Empty(unavailable);
        Assert.Equal(["scoped-variable-analysis"], Assert.Single(scopedOnly).Links.Select(link => link.Rel));
        Assert.Equal(
            ["activity-input-options", "scoped-variable-analysis"],
            Assert.Single(complete).Links.Select(link => link.Rel).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Workflow_design_operational_source_advertises_expression_tooling_only_when_context_and_provider_are_composed()
    {
        var context = (IExpressionAuthoringContextService)RuntimeHelpers.GetUninitializedObject(typeof(ExpressionAuthoringContextService));
        var provider = new StubExpressionToolingProvider();
        var resolver = new StubExpressionToolingProviderResolver(provider);

        Assert.DoesNotContain(
            await new WorkflowDesignOperationalCapabilitySource(expressionTooling: context).GetCapabilitiesAsync(),
            declaration => declaration.CapabilityId == "expressions.tooling.v1");
        Assert.DoesNotContain(
            await new WorkflowDesignOperationalCapabilitySource(expressionToolingProviders: [provider]).GetCapabilitiesAsync(),
            declaration => declaration.CapabilityId == "expressions.tooling.v1");
        Assert.DoesNotContain(
            await new WorkflowDesignOperationalCapabilitySource(
                expressionTooling: context,
                expressionToolingProviders: [provider]).GetCapabilitiesAsync(),
            declaration => declaration.CapabilityId == "expressions.tooling.v1");

        var tooling = Assert.Single((await new WorkflowDesignOperationalCapabilitySource(
            expressionTooling: context,
            expressionToolingProviders: [provider],
            expressionToolingResolver: resolver).GetCapabilitiesAsync()).Where(declaration => declaration.CapabilityId == "expressions.tooling.v1"));

        Assert.Equal(
            ["expression-tooling-completions", "expression-tooling-context", "expression-tooling-descriptors", "expression-tooling-hover", "expression-tooling-symbols", "expression-tooling-validate"],
            tooling.Links.Select(link => link.Rel).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Workflow_design_capabilities_advertise_optional_promotion_version_operations()
    {
        var links = WorkflowDesignApiCapabilities.StaticDeclaration.Links;

        Assert.Contains(links, link => link is
        {
            Rel: "workflow-draft-promote-version-preflight",
            Href: "design/workflows/drafts/{draftId}/promotion-preflight",
            Templated: true
        });
        Assert.Contains(links, link => link is
        {
            Rel: "workflow-draft-promote-exact-version",
            Href: "design/workflows/drafts/{draftId}/promote",
            Templated: true
        });
    }

    [Fact]
    public void Publishing_capabilities_advertise_the_executable_closure_export()
    {
        // FR-B-010a. Rel, href and templated flag are pinned verbatim for elsa-foundation-studio#493; the
        // endpoint's own tests prove the href actually resolves, which nothing here can see.
        Assert.Contains(PublishingApiCapabilities.StaticDeclaration.Links, link => link is
        {
            Rel: "workflow-executable-export",
            Href: "publishing/workflows/{versionId}/executable-export",
            Templated: true
        });

        // Adding a link is additive: no existing rel changed meaning, so the contract major version holds.
        Assert.Equal(1, PublishingApiCapabilities.StaticDeclaration.ContractMajorVersion);
    }

    [Fact]
    public async Task Runtime_operational_source_contributes_diagnostics_only_when_a_store_is_composed()
    {
        Assert.Empty(await new RuntimeOperationalCapabilitySource().GetCapabilitiesAsync());

        var available = await new RuntimeOperationalCapabilitySource(new StubDiagnosticsStore()).GetCapabilitiesAsync();

        var link = Assert.Single(Assert.Single(available).Links);
        Assert.Equal("runtime-diagnostics", link.Rel);
        Assert.Equal("runtime/workflows/diagnostics/settings", link.Href);
    }

    private sealed class StubInputOptionsProvider : IActivityInputOptionsProvider
    {
        public string Key => "test";
        public ValueTask<IReadOnlyList<ActivityInputOption>> GetOptionsAsync(ActivityInputOptionsContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ActivityInputOption>>([]);
    }

    private sealed class StubExpressionToolingProvider : IExpressionToolingProvider
    {
        public string ExpressionType => "Test";
        public ExpressionToolingContractVersion SupportedVersion => ExpressionToolingContractVersion.V1;
        public ValueTask<ExpressionToolingOutcome<ExpressionToolingCapabilities>> GetCapabilitiesAsync(ExpressionToolingRequestScope scope, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>> GetCompletionsAsync(ExpressionCompletionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ExpressionToolingOutcome<ExpressionHover>> GetHoverAsync(ExpressionHoverRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ExpressionToolingOutcome<ExpressionDiagnosticSet>> ValidateAsync(ExpressionValidationRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubExpressionToolingProviderResolver(IExpressionToolingProvider provider) : IExpressionToolingProviderResolver
    {
        public IExpressionToolingProvider? Find(string expressionType) =>
            StringComparer.Ordinal.Equals(expressionType, provider.ExpressionType) ? provider : null;
    }

    private sealed class StubDiagnosticsStore : IRuntimeDiagnosticsSettingsStore
    {
        public Task<RuntimeDiagnosticsSettings?> LoadAsync(string scope, CancellationToken cancellationToken = default) =>
            Task.FromResult<RuntimeDiagnosticsSettings?>(null);
        public Task<RuntimeDiagnosticsSettings> SaveAsync(RuntimeDiagnosticsSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(settings);
    }
}
