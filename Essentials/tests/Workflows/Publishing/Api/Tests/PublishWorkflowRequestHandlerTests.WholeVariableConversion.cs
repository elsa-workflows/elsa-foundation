using System.Text.Json;
using Elsa.Activities.Sequence.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using VariableDefinition = Elsa.Expressions.Core.Models.VariableDefinition;
using VariableReference = Elsa.Expressions.Core.Models.VariableReference;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed partial class PublishWorkflowRequestHandlerTests
{
    public static IEnumerable<object[]> WholeVariablePublicationCases =>
        from scoped in new[] { false, true } from intent in WholeVariableConversionTests.ConversionIntents select new object[] { scoped, intent };

    public static IEnumerable<object[]> WholeVariableAutoCases =>
        WholeVariableConversionTests.DefaultRequests.Select(request => new object[] { request });

    public static IEnumerable<object[]> WholeVariableIntrinsicCases =>
        WholeVariableConversionTests.ConversionIntents.Select(request => new object[] { request });

    [Theory]
    [MemberData(nameof(WholeVariablePublicationCases))]
    public async Task A_whole_variable_conversion_refuses_publication_before_any_write(bool scoped, string intent)
    {
        var read = new ArgumentState("Text", new ArgumentValue(new VariableReference("customer", scoped ? "sequence" : "workflow"), "Variable"),
            null, null, null, null, WholeVariableConversionTests.Request(intent));
        var variable = new VariableDefinition("customer", "Customer", new TypeReference("String"), null, null);
        var root = scoped
            ? SequenceNode("sequence", [Node("consumer", read)]) with
            {
                Structure = new(SequenceActivity.StructureKind, SequenceActivity.StructureSchemaVersion,
                    JsonSerializer.SerializeToElement(new SequenceAuthoredStructure([Node("consumer", read)], [variable])))
            }
            : Node("consumer", read);
        var version = WorkflowVersion(root);
        version.State = version.State! with { Variables = scoped ? [] : [variable] };
        var authored = JsonSerializer.Serialize(version.State);
        var before = await CountPublishWritesAsync();

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            Handler(version, _writeLineActivity, _sequenceActivity).Handle(new PublishWorkflow(version.Id), CancellationToken.None));

        var refusal = Assert.IsType<ValueConversionPublicationException>(exception.InnerException);
        Assert.Equal(ValueConversionRejectionReason.VariableSourceContractUnknown, refusal.ReasonCode);
        Assert.Equal(new ValueConversionBindingContext("consumer", "Text", ValueConversionBindingKind.Input), refusal.Binding);
        Assert.Null(refusal.SourceType);
        Assert.Null(refusal.SourceRepresentation);
        Assert.Equal(before, await CountPublishWritesAsync());
        Assert.Null(await _activationAuthority.FindAsync("definition-1", "default"));
        Assert.Equal(authored, JsonSerializer.Serialize(version.State));
    }

    [Theory]
    [MemberData(nameof(WholeVariableIntrinsicCases))]
    public async Task A_whole_variable_conversion_on_an_intrinsic_refuses_before_any_write(string intent)
    {
        var set = WholeVariableSet(WholeVariableConversionTests.Request(intent));
        var version = WorkflowVersion(set);
        version.State = version.State! with { Variables = [new VariableDefinition("counter", "Counter", new TypeReference("Int32"), null, null)] };
        var before = await CountPublishWritesAsync();

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            Handler(version).Handle(new PublishWorkflow(version.Id), CancellationToken.None));

        var refusal = Assert.IsType<ValueConversionPublicationException>(exception.InnerException);
        Assert.Equal(ValueConversionRejectionReason.VariableSourceContractUnknown, refusal.ReasonCode);
        Assert.Equal(new ValueConversionBindingContext("set-counter", WorkflowIntrinsicInputKeys.Value, ValueConversionBindingKind.Input), refusal.Binding);
        Assert.Equal(before, await CountPublishWritesAsync());
        Assert.Null(await _activationAuthority.FindAsync("definition-1", "default"));
    }

    [Theory]
    [MemberData(nameof(WholeVariableAutoCases))]
    public async Task A_whole_variable_with_ordinary_Auto_publishes_and_executes_its_read(string request)
    {
        var set = WholeVariableSet(WholeVariableConversionTests.Request(request));
        var version = WorkflowVersion(set);
        version.State = version.State! with { Variables = [new VariableDefinition("counter", "Counter", new TypeReference("Int32"), null, null)] };

        var publication = await Handler(version).Handle(new PublishWorkflow(version.Id), CancellationToken.None);

        var executable = Assert.IsType<WorkflowExecutable>(await _store.FindAsync(publication.ArtifactId));
        var binding = executable.RootActivity.InputBindings[WorkflowIntrinsicInputKeys.Value];
        Assert.Equal(RuntimeInputBindingSource.VariableRead, binding.Source);
        Assert.Equal(("counter", "workflow"), (binding.Variable!.VariableKey, binding.Variable.DeclaringScopeId));
        Assert.Null(binding.ConversionRequest);
        Assert.Equal(ValueConversionOperation.Identity, binding.ConversionPlan!.Operation);
        Assert.Equal(ValueConversionMode.Auto, binding.ConversionPlan.Mode);
        Assert.Equal("Int32", binding.ConversionPlan.SourceType.Alias);
        Assert.Equal(42, await ExecutePublishedSetAndReadAsync(executable, initialCounter: 42));
    }

    private static ActivityNode WholeVariableSet(AuthoredValueConversionRequest? conversion) =>
        new("set-counter", "elsa.intrinsic.set@1",
            [new ArgumentState(WorkflowIntrinsicInputKeys.Value, new ArgumentValue(new VariableReference("counter", "workflow"), "Variable"),
                null, null, null, null, conversion)], [])
        {
            Intrinsic = new AuthoredWorkflowIntrinsic(AuthoredWorkflowIntrinsicKind.Set, new TypeReference("Int32"), new VariableReference("counter", "workflow"))
        };
}
