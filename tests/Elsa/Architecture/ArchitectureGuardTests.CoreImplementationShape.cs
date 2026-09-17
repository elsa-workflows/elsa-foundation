using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed partial class ArchitectureGuardTests
{
    /// <summary>
    /// Ratchet of implementation-shaped types that predate <see cref="Core_projects_contain_no_implementation_shaped_types"/>.
    /// Entries may only be removed: moving a type into its implementation project, or reshaping it into a model, makes the
    /// guard fail until its entry is deleted here.
    /// </summary>
    private static readonly Dictionary<string, string[]> BaselinedCoreImplementations = new(StringComparer.Ordinal)
    {
        ["Elsa.Activities.Design.Core"] =
        [
            "Elsa.Activities.Design.Core.Services.ActivityDraftValidator",
            "Elsa.Activities.Design.Core.Services.ActivityProviderRegistry",
            "Elsa.Activities.Design.Core.Services.ActivityProviderRegistry+GuardedActivityProvider",
            "Elsa.Activities.Design.Core.Services.ActivityVersionDiffer",
            "Elsa.Activities.Design.Core.Services.DefaultActivityAvailabilityDiagnosticsProjector",
            "Elsa.Activities.Design.Core.Services.DefaultActivityAvailabilityEvaluator",
            "Elsa.Activities.Design.Core.Services.DefaultActivityTypeKeyPolicy",
            "Elsa.Activities.Design.Core.Stores.InMemoryActivityAvailabilitySettingsStore"
        ],
        ["Elsa.Activities.Design.Persistence.Core"] =
        [
            "Elsa.Activities.Design.Persistence.Core.Services.ActivityDefinitionFactory",
            "Elsa.Activities.Design.Persistence.Core.Services.ActivityDefinitionLookup",
            "Elsa.Activities.Design.Persistence.Core.Services.ActivityDefinitionVersionFactory",
            "Elsa.Activities.Design.Persistence.Core.Services.DefaultActivityDefinitionHasher"
        ],
        ["Elsa.Activities.Runtime.Core"] =
        [
            "Elsa.Activities.Runtime.Contracts.ActivityActivationLease"
        ],
        ["Elsa.Agent.Core"] =
        [
            "Elsa.Agent.Core.Services.AgentTurnRegistry",
            "Elsa.Agent.Core.Services.DefaultAgentCapabilityCatalog",
            "Elsa.Agent.Core.Services.DefaultAgentContextCollector",
            "Elsa.Agent.Core.Services.DefaultAgentContextSanitizer",
            "Elsa.Agent.Core.Services.DefaultAgentPolicyEvaluator",
            "Elsa.Agent.Core.Services.DefaultAgentProviderRegistry",
            "Elsa.Agent.Core.Services.DefaultAgentToolInvoker",
            "Elsa.Agent.Core.Services.DefaultAgentToolRegistry",
            "Elsa.Agent.Core.Services.DefaultAgentTurnOrchestrator",
            "Elsa.Agent.Core.Services.DeterministicAgentProvider",
            "Elsa.Agent.Core.Services.InMemoryAgentAuditStore",
            "Elsa.Agent.Core.Services.InMemoryAgentFeedbackService",
            "Elsa.Agent.Core.Services.InMemoryAgentProposalService",
            "Elsa.Agent.Core.Services.InMemoryAgentSessionService",
            "Elsa.Agent.Core.Services.InMemoryAgentTurnStateStore",
            "Elsa.Agent.Core.Services.NoopAgentActionProposalExecutor"
        ],
        ["Elsa.Attention.Core"] =
        [
            "Elsa.Attention.Core.AttentionAggregationService",
            "Elsa.Attention.Core.AttentionContributorRegistration",
            "Elsa.Attention.Core.AttentionContributorRegistry"
        ],
        ["Elsa.Mediator.Core"] =
        [
            "Elsa.Mediator.Core.Models.MessageContext`1"
        ],
        ["Elsa.Pipelines.Core"] =
        [
            "Elsa.Pipelines.Core.PipelineBuilder`1"
        ],
        ["Elsa.Studio.Preferences.Core"] =
        [
            "Elsa.Studio.Preferences.Core.AttentionPreferenceNamespace",
            "Elsa.Studio.Preferences.Core.DashboardPreferenceNamespace",
            "Elsa.Studio.Preferences.Core.Services.InMemoryStudioPreferenceStore",
            "Elsa.Studio.Preferences.Core.Services.StudioPreferenceNamespaceRegistry",
            "Elsa.Studio.Preferences.Core.Services.StudioPreferenceService"
        ],
        ["Elsa.Workflows.Design.Core"] =
        [
            "Elsa.Workflows.Design.Core.Authoring.WorkflowBuilder`2",
            "Elsa.Workflows.Design.Core.Authoring.WorkflowBuilder`2+ActivityStructureBuilder",
            "Elsa.Workflows.Design.Core.Authoring.WorkflowBuilder`2+SequenceBuilder",
            "Elsa.Workflows.Design.Core.Authoring.WorkflowBuilder`2+SequenceBuilder+ActivityInputBuilder`1",
            "Elsa.Workflows.Design.Core.Services.DefaultActivityStructureService",
            "Elsa.Workflows.Design.Core.Services.ExpressionAuthoringContextService",
            "Elsa.Workflows.Design.Core.Services.ScopedVariableReferenceRemapper",
            "Elsa.Workflows.Design.Core.Services.ScopedVariableResolver"
        ],
        ["Elsa.Workflows.Design.Persistence.Core"] =
        [
            "Elsa.Workflows.Design.Persistence.Core.Entities.WorkflowDefinition",
            "Elsa.Workflows.Design.Persistence.Core.Services.DraftStateDiffEngine",
            "Elsa.Workflows.Design.Persistence.Core.Services.ValidateDesignPersistenceReplacementContractsStartupTask",
            "Elsa.Workflows.Design.Persistence.Core.Services.WorkflowDefinitionDraftFactory",
            "Elsa.Workflows.Design.Persistence.Core.Services.WorkflowDefinitionFactory",
            "Elsa.Workflows.Design.Persistence.Core.Services.WorkflowDefinitionLookup",
            "Elsa.Workflows.Design.Persistence.Core.Services.WorkflowDefinitionVersionFactory"
        ],
        ["Elsa.Workflows.Publishing.Core"] =
        [
            "Elsa.Workflows.Publishing.Core.Services.ActivityTemplateDependencyDiscovererRegistry",
            "Elsa.Workflows.Publishing.Core.Services.ActivityTemplateDependencyDiscovererRegistry+GuardedDiscoverer",
            "Elsa.Workflows.Publishing.Core.Services.ActivityTemplateProviderCompilerRegistry",
            "Elsa.Workflows.Publishing.Core.Services.ActivityTemplateProviderCompilerRegistry+GuardedCompiler",
            "Elsa.Workflows.Publishing.Core.Services.InMemoryPublicationSnapshotReviewStore"
        ],
        ["Elsa.Workflows.Runtime.Core"] =
        [
            // AddPersistenceCore is also called by Identity, Design, Activities.Design and Elsa3 import, none of which
            // reference Elsa.Workflows.Runtime, so its default scope bridges cannot move without a new shared home.
            "Elsa.Workflows.Runtime.Core.Extensions.PersistenceCoreServiceCollectionExtensions+DefaultPersistenceAccessContextAccessor",
            "Elsa.Workflows.Runtime.Core.Services.PersistenceOperationScopeFactory",
            "Elsa.Workflows.Runtime.Core.Services.PersistenceScopeRunner",
            "Elsa.Workflows.Runtime.Core.Services.SinglePersistenceScopeSource",
            // Empty slot-placement markers; ADR 0033 keeps the pipeline contract, including its slots, in Core.
            "Elsa.Workflows.Runtime.Core.Middleware.RuntimeActivityInputEvaluationMiddleware",
            "Elsa.Workflows.Runtime.Core.Middleware.RuntimeActivityLoadStateMiddleware",
            "Elsa.Workflows.Runtime.Core.Middleware.RuntimeActivityOutputCaptureMiddleware",
            "Elsa.Workflows.Runtime.Core.Middleware.RuntimeActivityPostCommitMiddleware",
            "Elsa.Workflows.Runtime.Core.Middleware.RuntimeActivitySchedulingMiddleware",
            "Elsa.Workflows.Runtime.Core.Middleware.RuntimeWorkflowLoadStateMiddleware",
            "Elsa.Workflows.Runtime.Core.Middleware.RuntimeWorkflowPostCommitMiddleware",
            "Elsa.Workflows.Runtime.Core.Middleware.RuntimeWorkflowSchedulingMiddleware",
            // Models that carry a caller-owned handle or scoped provider rather than resolving services.
            "Elsa.Workflows.Runtime.Core.Models.Alterations.WorkflowAlterationProjectedState",
            "Elsa.Workflows.Runtime.Core.Models.RuntimeAdmissionDecision",
            "Elsa.Workflows.Runtime.Core.Models.RuntimeSchedulerDrainRequest",
            "Elsa.Workflows.Runtime.Core.Models.WorkflowExecutionCommandDispatchOptions"
        ]
    };

    private static readonly HashSet<Type> InfrastructureDependencies =
    [
        typeof(IServiceProvider),
        typeof(IServiceScopeFactory),
        typeof(ILogger),
        typeof(ILogger<>),
        typeof(ILoggerFactory),
        typeof(IOptions<>),
        typeof(IOptionsMonitor<>),
        typeof(IOptionsSnapshot<>)
    ];

    /// <summary>
    /// A <c>.Core</c> project holds contracts and models. A non-abstract class that implements a service contract, or
    /// whose constructor takes injected services, is an implementation and belongs in the implementation project.
    /// </summary>
    [Fact]
    public void Core_projects_contain_no_implementation_shaped_types()
    {
        var offenders = CoreProjectAssemblies()
            .SelectMany(assembly => assembly.GetTypes()
                .Where(IsImplementationShaped)
                .Select(type => (Project: assembly.GetName().Name!, Type: type.FullName!)))
            .ToArray();

        var unbaselined = offenders
            .Where(offender => !BaselinedCoreImplementations.GetValueOrDefault(offender.Project, []).Contains(offender.Type))
            .Select(offender => $"{offender.Project}: {offender.Type}");
        var stale = BaselinedCoreImplementations
            .SelectMany(entry => entry.Value.Select(type => (Project: entry.Key, Type: type)))
            .Except(offenders)
            .Select(entry => $"{entry.Project}: {entry.Type} (baselined, no longer implementation-shaped; remove the entry)");
        var violations = unbaselined.Concat(stale).ToArray();

        Assert.True(
            violations.Length == 0,
            "Implementation-shaped types belong in the implementation project, not a .Core project:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<Assembly> CoreProjectAssemblies() => ProjectFiles()
        .Where(project => project.RelativePath.StartsWith("src/", StringComparison.Ordinal) &&
                          project.Name.EndsWith(".Core", StringComparison.Ordinal))
        .Select(project => Assembly.Load(new AssemblyName(project.Name)));

    private static bool IsImplementationShaped(Type type) =>
        type is { IsClass: true, IsAbstract: false } &&
        !IsCompilerGenerated(type) &&
        !IsRecord(type) &&
        !typeof(Exception).IsAssignableFrom(type) &&
        !typeof(Attribute).IsAssignableFrom(type) &&
        (type.GetInterfaces().Any(IsServiceContract) || type
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Any(parameter => IsInjectedDependency(parameter.ParameterType)));

    // A service contract declares behavior; data-only interfaces and markers such as IEvent describe models.
    private static bool IsServiceContract(Type type) =>
        type.IsInterface &&
        type.Assembly.GetName().Name?.StartsWith("Elsa.", StringComparison.Ordinal) == true &&
        type.GetInterfaces().Prepend(type).Any(contract => contract.GetMethods().Any(method => !method.IsSpecialName));

    private static bool IsInjectedDependency(Type type) =>
        IsServiceContract(type) ||
        InfrastructureDependencies.Contains(type.IsGenericType ? type.GetGenericTypeDefinition() : type) ||
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>) && IsServiceContract(type.GetGenericArguments()[0]);

    private static bool IsRecord(Type type) =>
        type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is not null;

    private static bool IsCompilerGenerated(Type type) =>
        type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) ||
        type.Name.Contains('<') ||
        type.DeclaringType is { } declaringType && IsCompilerGenerated(declaringType);
}
