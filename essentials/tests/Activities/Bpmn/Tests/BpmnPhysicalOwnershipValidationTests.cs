using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Design;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Events;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Core.Extensions;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Core;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnPhysicalOwnershipValidationTests
{
    private const string OwnerKind = "test.physical-document";
    private const string OwnerSchema = "1.0.0";
    private static readonly JsonSerializerOptions Json = BpmnDocumentStructureHandler.SerializerOptions;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inline_source_gate_refuses_genuine_scope_views_owned_outside_lexical_children(bool useRegisteredService)
    {
        var source = Source();
        var view = BpmnDocumentScopeProjection.CreateProcessView(source, "P");
        Assert.True(BpmnDocumentScopeProjection.IsView(view));
        var owner = Owner(view);
        using var provider = Services(useRegisteredService);
        using var scope = provider.CreateScope();
        AssertPhysicalProjection(scope.ServiceProvider.GetRequiredService<IActivityStructureService>(), owner, view);
        var gate = scope.ServiceProvider.GetRequiredService<IInlineEventPublisher>();
        Assert.Contains(await gate.DeriveValidationErrorsAsync(new Draft(source), CancellationToken.None),
            error => error.Path == source.NodeId && error.Type == "Bpmn/ExecutionCapability");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.DeriveValidationErrorsAsync(new Draft(owner), CancellationToken.None));

        Assert.Contains("internal projected scope structures cannot be physically authored or persisted",
            exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inline_admission_gate_reports_complete_source_owned_outside_lexical_children(bool useRegisteredService)
    {
        var source = Source();
        var owner = Owner(source);
        using var provider = Services(useRegisteredService);
        using var scope = provider.CreateScope();
        AssertPhysicalProjection(scope.ServiceProvider.GetRequiredService<IActivityStructureService>(), owner, source);
        var gate = scope.ServiceProvider.GetRequiredService<IInlineEventPublisher>();
        Assert.Contains(await gate.DeriveValidationErrorsAsync(new Draft(source), CancellationToken.None),
            error => error.Path == source.NodeId && error.Type == "Bpmn/ExecutionCapability");

        var errors = await gate.DeriveValidationErrorsAsync(new Draft(owner), CancellationToken.None);

        Assert.Contains(errors, error => error.Path == source.NodeId && error.Type == "Bpmn/ExecutionCapability");
    }

    private static ServiceProvider Services(bool useRegisteredService)
    {
        var services = new ServiceCollection();
        new ActivitiesBpmnDesignFeature().ConfigureServices(services);
        new EventsFeature().ConfigureServices(services);
        services.AddEventHandler<DraftValidating, ExecuteValidations>();
        if (useRegisteredService)
            services.AddSingleton<IActivityStructureService>(provider =>
                new PhysicalService(provider.GetServices<IActivityStructureHandler>()));
        else
        {
            services.AddSingleton<IActivityStructureHandler>(new PhysicalHandler());
            services.AddSingleton<IActivityStructureService>(provider =>
                new DefaultActivityStructureService(provider.GetServices<IActivityStructureHandler>()));
        }
        return services.BuildServiceProvider();
    }

    private static void AssertPhysicalProjection(IActivityStructureService structures, ActivityNode owner, ActivityNode expected)
    {
        Assert.Empty(structures.ProjectChildren(owner));
        var projection = structures.ProjectOwnership(owner);
        var physical = Assert.Single(projection.Activities);
        Assert.Empty(projection.AdditionalNodeIds);
        Assert.Equal(expected.NodeId, physical.NodeId);
        Assert.True(JsonElement.DeepEquals(expected.Structure!.Payload, physical.Structure!.Payload));
    }

    private static ActivityNode Source()
    {
        var xml = $"<b:definitions xmlns:b='{BpmnDocumentStructureHandler.ModelNamespace}'><b:process id='P'/></b:definitions>";
        var source = new BpmnAuthoredDocument(xml, "P", [], [], []);
        return new("physical-bpmn", "bpmn@1", [], [], new(BpmnActivityIdentities.StructureKind,
            BpmnAuthoredDocument.SchemaVersion, JsonSerializer.SerializeToElement(source, Json)));
    }

    private static ActivityNode Owner(ActivityNode child) => new("physical-owner", "physical-owner@1", [], [],
        new(OwnerKind, OwnerSchema, JsonSerializer.SerializeToElement(new { body = child }, Json)));

    private static bool IsOwner(ActivityNode activity) => activity.Structure is { Kind: OwnerKind, SchemaVersion: OwnerSchema };

    private static ActivityNode StoredChild(ActivityNode activity) =>
        activity.Structure!.Payload.GetProperty("body").Deserialize<ActivityNode>(Json)
        ?? throw new InvalidOperationException("The test document requires its physically stored child.");

    private sealed class PhysicalHandler : IActivityStructureHandler
    {
        public string Kind => OwnerKind;
        public string SchemaVersion => OwnerSchema;
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => [];
        public ActivityOwnershipProjection ProjectOwnership(ActivityNode activity) => new([StoredChild(activity)], []);
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) =>
            throw new NotSupportedException();
        public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity) => throw new NotSupportedException();
    }

    private sealed class PhysicalService(IEnumerable<IActivityStructureHandler> handlers) : IActivityStructureService
    {
        private readonly DefaultActivityStructureService _default = new(handlers);
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) =>
            IsOwner(activity) ? [] : _default.ProjectChildren(activity);
        public ActivityOwnershipProjection ProjectOwnership(ActivityNode activity) =>
            IsOwner(activity) ? new([StoredChild(activity)], []) : _default.ProjectOwnership(activity);
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) =>
            throw new NotSupportedException();
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => throw new NotSupportedException();
        public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public bool SupportsScopedVariables(ActivityNode activity) => false;
    }

    private sealed class Draft(ActivityNode root) : IWorkflowDefinitionDraft
    {
        public string Id => "physical-validation-draft";
        public string WorkflowDefinitionId => "physical-validation-definition";
        public WorkflowDefinitionState State => new([], root, [], [], null);
        public DateTimeOffset CreatedAt => DateTimeOffset.UnixEpoch;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UnixEpoch;
    }
}
