using System.Text.Json;
using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Internal;
using Elsa.Activities.Compensation.Models;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Compensation.Tests;

/// <summary>
/// Spec 261 FR-514 (OD20 A), the quickstart's W1–W5: a replay-safe leaf as a step that has a compensation, or as a
/// handler, gets a non-blocking warning on the step row; a step without a compensation, a structural step or handler, an
/// External activity and an activity without a resolvable profile get none.
/// </summary>
public sealed class CompensationReplaySafeValidatorTests : IDisposable
{
    private const string WriteLine = "activity-write-line";
    private const string Sequence = "activity-sequence";
    private const string Participant = "activity-participant";
    private const string Unknown = "activity-unknown";

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);
    private readonly ServiceProvider _provider;
    private readonly IActivityStructureService _structureService;
    private readonly ProfileReader _reader = new(new Dictionary<string, SideEffectProfile?>
    {
        [WriteLine] = SideEffectProfile.ReplaySafe,
        [Sequence] = SideEffectProfile.ReplaySafe,
        [Participant] = SideEffectProfile.External,
        [Unknown] = null
    });

    public CompensationReplaySafeValidatorTests()
    {
        var services = new ServiceCollection();
        new ActivitiesCompensationFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
        _structureService = new DefaultActivityStructureService(_provider.GetServices<IActivityStructureHandler>());
    }

    public void Dispose() => _provider.Dispose();

    // W1
    [Fact]
    public async Task A_replay_safe_step_with_a_compensation_gets_a_warning_on_its_row()
    {
        var warnings = await ValidateAsync(Scope("scope", ("Charge card", Leaf("charge", WriteLine), Leaf("refund", Participant))));

        var warning = Assert.Single(warnings);
        Assert.Equal((CompensationReplaySafeValidator.ReplaySafeStepType, "scope/steps/0", ValidationSeverities.Warning), (warning.Type, warning.Path, warning.Severity));
        Assert.Equal(
            "\"Charge card\" is marked safe to repeat, so its call may not be committed before it runs; if the process stops, recovery may not know it happened. Mark it as an external effect unless it has none.",
            warning.Message);
    }

    // W2
    [Fact]
    public async Task A_replay_safe_handler_gets_a_warning_on_its_step_row()
    {
        var warnings = await ValidateAsync(Scope("scope",
            ("Reserve", Leaf("reserve", Participant), null),
            ("Charge card", Leaf("charge", Participant), Leaf("refund", WriteLine))));

        var warning = Assert.Single(warnings);
        Assert.Equal((CompensationReplaySafeValidator.ReplaySafeHandlerType, "scope/steps/1", ValidationSeverities.Warning), (warning.Type, warning.Path, warning.Severity));
        Assert.Equal(
            "The compensation of \"Charge card\" is marked safe to repeat, so an undo may run again after a restart without a committed claim. Mark it as an external effect unless it is idempotent and has no effect of its own.",
            warning.Message);
    }

    [Fact]
    public async Task A_replay_safe_step_and_handler_get_one_warning_each()
    {
        var warnings = await ValidateAsync(Scope("scope", ("Log", Leaf("log", WriteLine), Leaf("unlog", WriteLine))));

        Assert.Equal(
            [CompensationReplaySafeValidator.ReplaySafeStepType, CompensationReplaySafeValidator.ReplaySafeHandlerType],
            warnings.Select(warning => warning.Type));
    }

    // W3
    [Fact]
    public async Task A_replay_safe_step_without_a_compensation_has_no_warning()
    {
        Assert.Empty(await ValidateAsync(Scope("scope", ("Log", Leaf("log", WriteLine), null))));
    }

    // W4
    [Fact]
    public async Task A_structural_step_or_handler_has_no_warning()
    {
        Assert.Empty(await ValidateAsync(Scope("scope",
            ("Batch", Structural("batch", Sequence), Structural("unbatch", Sequence)),
            ("Inner", Scope("inner", ("Reserve", Leaf("reserve", Participant), Leaf("release", Participant))), Leaf("undo-inner", Participant)))));
    }

    // W5, and an activity whose profile cannot be resolved
    [Fact]
    public async Task An_external_or_unresolvable_activity_has_no_warning()
    {
        Assert.Empty(await ValidateAsync(Scope("scope",
            ("Reserve", Leaf("reserve", Participant), Leaf("release", Participant)),
            ("Mystery", Leaf("mystery", Unknown), Leaf("unmystery", Unknown)))));
    }

    [Fact]
    public async Task A_nested_scope_is_checked_too_and_its_rows_are_its_own()
    {
        var warning = Assert.Single(await ValidateAsync(Scope("outer",
            ("Inner", Scope("inner", ("Log", Leaf("log", WriteLine), Leaf("release", Participant))), null))));

        Assert.Equal("inner/steps/0", warning.Path);
    }

    [Fact]
    public async Task Without_a_profile_reader_there_is_no_warning()
    {
        var validator = new CompensationReplaySafeValidator(_structureService);

        var warnings = await validator.Validate(Draft(Scope("scope", ("Log", Leaf("log", WriteLine), Leaf("unlog", WriteLine)))), CancellationToken.None);

        Assert.Empty(warnings);
    }

    [Fact]
    public async Task Each_activity_version_is_read_once()
    {
        await ValidateAsync(Scope("scope",
            ("One", Leaf("one", WriteLine), Leaf("undo-one", WriteLine)),
            ("Two", Leaf("two", WriteLine), Leaf("undo-two", Participant))));

        Assert.Equal(1, _reader.Reads[WriteLine]);
        Assert.Equal(1, _reader.Reads[Participant]);
    }

    [Fact]
    public void The_feature_registers_the_validator()
    {
        var services = new ServiceCollection();
        new ActivitiesCompensationFeature().ConfigureServices(services);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IDraftValidator) &&
                                                descriptor.ImplementationType == typeof(CompensationReplaySafeValidator) &&
                                                descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    private async Task<IReadOnlyList<ValidationError>> ValidateAsync(ActivityNode root) =>
        [.. await new CompensationReplaySafeValidator(_structureService, _reader).Validate(Draft(root), CancellationToken.None)];

    private static IWorkflowDefinitionDraft Draft(ActivityNode root) => new StubDraft(new WorkflowDefinitionState([], root, [], [], null));

    private static ActivityNode Leaf(string nodeId, string activityVersionId) => new(nodeId, activityVersionId, [], []);

    private static ActivityNode Structural(string nodeId, string activityVersionId) =>
        new(nodeId, activityVersionId, [], [], new ActivityNodeStructure("elsa.sequence.structure", "1.0.0", JsonSerializer.SerializeToElement(new { activities = Array.Empty<object>() })));

    private static ActivityNode Scope(string nodeId, params (string Name, ActivityNode Activity, ActivityNode? Compensation)[] steps)
    {
        var structure = new CompensationScopeAuthoredStructure(
            steps.Select(step => new CompensationScopeAuthoredStep(step.Name, step.Activity, step.Compensation)).ToArray());
        return new ActivityNode(nodeId, "activity-scope", [], [],
            new ActivityNodeStructure(CompensationScope.StructureKind, CompensationScope.StructureSchemaVersion, JsonSerializer.SerializeToElement(structure, WebOptions)));
    }

    private sealed class ProfileReader(IReadOnlyDictionary<string, SideEffectProfile?> profiles) : IActivitySideEffectProfileReader
    {
        public Dictionary<string, int> Reads { get; } = new(StringComparer.Ordinal);

        public ValueTask<SideEffectProfile?> ReadAsync(string activityVersionId, CancellationToken cancellationToken = default)
        {
            Reads[activityVersionId] = Reads.GetValueOrDefault(activityVersionId) + 1;
            return ValueTask.FromResult(profiles.GetValueOrDefault(activityVersionId));
        }
    }

    private sealed class StubDraft(WorkflowDefinitionState state) : IWorkflowDefinitionDraft
    {
        public string Id => "draft-1";
        public string WorkflowDefinitionId => "wf-1";
        public WorkflowDefinitionState State { get; } = state;
        public DateTimeOffset CreatedAt => DateTimeOffset.UtcNow;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UtcNow;
    }
}
