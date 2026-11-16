using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resolvers;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using VariableDefinition = Elsa.Expressions.Core.Models.VariableDefinition;
using VariableReference = Elsa.Expressions.Core.Models.VariableReference;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed partial class PublishWorkflowRequestHandlerTests
{
    private static readonly string[] WholeRequestForms = ["activity-request", "activity-input", "intrinsic-request", "intrinsic-input", "scoped-request"];

    public static IEnumerable<object[]> WholeRequestRefusals =>
        from form in WholeRequestForms from intent in WholeVariableConversionTests.ConversionIntents select new object[] { form, intent };

    public static IEnumerable<object[]> WholeRequestDefaults =>
        from form in WholeRequestForms from request in WholeVariableConversionTests.DefaultRequests select new object[] { form, request };

    [Theory]
    [MemberData(nameof(WholeRequestRefusals))]
    public async Task Whole_workflow_request_conversion_refuses_before_any_publication_write(string form, string intent)
    {
        var version = WholeRequestVersion(form, null);
        var handler = Handler(version, _writeLineActivity, _sequenceActivity);
        await handler.Handle(new PublishWorkflow(version.Id), CancellationToken.None);
        var before = await CountPublishWritesAsync();
        var activation = await _activationAuthority.FindAsync("definition-1", "default");
        Assert.NotNull(activation);
        version.State = WholeRequestVersion(form, WholeVariableConversionTests.Request(intent)).State;
        var authored = JsonSerializer.Serialize(version.State);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            handler.Handle(new PublishWorkflow(version.Id), CancellationToken.None));

        var refusal = Assert.IsType<ValueConversionPublicationException>(exception.InnerException);
        AssertWholeRequestRefusal(refusal, form.StartsWith("intrinsic", StringComparison.Ordinal) ? "request-set" : "consumer",
            form.StartsWith("intrinsic", StringComparison.Ordinal) ? WorkflowIntrinsicInputKeys.Value : "Text");
        Assert.Equal(before, await CountPublishWritesAsync());
        Assert.Equal(activation, await _activationAuthority.FindAsync("definition-1", "default"));
        Assert.Equal(authored, JsonSerializer.Serialize(version.State));
    }

    [Theory]
    [MemberData(nameof(WholeRequestDefaults))]
    public async Task Whole_workflow_request_ordinary_Auto_preserves_the_published_binding_and_read(string form, string request)
    {
        var version = WholeRequestVersion(form, null);
        var handler = Handler(version, _writeLineActivity, _sequenceActivity);
        var baselinePublication = await handler.Handle(new PublishWorkflow(version.Id), CancellationToken.None);
        var baseline = Assert.IsType<WorkflowExecutable>(await _store.FindAsync(baselinePublication.ArtifactId));
        version.State = WholeRequestVersion(form, WholeVariableConversionTests.Request(request)).State;

        var publication = await handler.Handle(new PublishWorkflow(version.Id), CancellationToken.None);

        var executable = Assert.IsType<WorkflowExecutable>(await _store.FindAsync(publication.ArtifactId));
        var node = Assert.Single(executable.RootActivity.DescendantsAndSelf(), candidate =>
            candidate.InputBindings.Values.Any(binding => binding.Source == RuntimeInputBindingSource.WorkflowRequest));
        var binding = Assert.Single(node.InputBindings.Values);
        Assert.Equal("customer", binding.WorkflowRequest!.MemberKey);
        Assert.Null(binding.WorkflowRequest.Path);
        Assert.Null(binding.ConversionRequest);
        var baselineNode = baseline.RootActivity.DescendantsAndSelf().Single(candidate => candidate.ExecutableNodeId == node.ExecutableNodeId);
        Assert.Equal(JsonSerializer.Serialize(Assert.Single(baselineNode.InputBindings.Values)), JsonSerializer.Serialize(binding));
        Assert.Equal(new Elsa.Workflows.Runtime.Services.Executables.WorkflowExecutableHasher().ComputeHash(baseline.RootActivity),
            new Elsa.Workflows.Runtime.Services.Executables.WorkflowExecutableHasher().ComputeHash(executable.RootActivity));
        if (form.StartsWith("intrinsic", StringComparison.Ordinal))
        {
            Assert.Equal(ValueConversionOperation.Identity, binding.ConversionPlan!.Operation);
            Assert.Equal(ValueConversionMode.Auto, binding.ConversionPlan.Mode);
            Assert.Equal("String", binding.ConversionPlan.SourceType.Alias);
        }
        else
            Assert.Null(binding.ConversionPlan);
        var resolved = new RuntimeInputBindingResolver().Resolve(binding,
            new RuntimeInputBindingResolutionContext("request-workflow", "request-consumer", executable: executable,
                workflowInputEnvelopes: new Dictionary<string, ValueEnvelope>
                {
                    ["customer"] = ValueEnvelope.Inline(new ValueTypeDescriptor("String"), JsonSerializer.SerializeToElement("customer-42"), ValueProtectionPolicy.InstanceInline)
                }));
        Assert.Equal("customer-42", resolved.Envelope!.InlineValue!.Value.GetString());
    }

    [Theory]
    [InlineData("Json")]
    [InlineData("Options")]
    public async Task Whole_workflow_request_conversion_on_a_placed_public_boundary_refuses_before_any_write(string intent)
    {
        var state = WholeWorkflowRequestConversionTests.State(false, null);
        var fixture = await ReusableDefaultAsync(new ActivityInputDefault("Literal", JsonSerializer.SerializeToElement("safe")),
            authored: state, rootFactory: consumer => consumer);
        fixture.Version.State = fixture.Version.State! with { Inputs = [CustomerInput] };
        await fixture.Handler.Handle(new PublishWorkflow(fixture.Version.Id), CancellationToken.None);
        var before = await CountPublishWritesAsync();
        var activation = await _activationAuthority.FindAsync("definition-1", "default");
        fixture.Version.State = fixture.Version.State with
        {
            RootActivity = fixture.Version.State.RootActivity! with
            {
                Inputs = [state with { Conversion = WholeVariableConversionTests.Request(intent) }]
            }
        };
        var authored = JsonSerializer.Serialize(fixture.Version.State);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            fixture.Handler.Handle(new PublishWorkflow(fixture.Version.Id), CancellationToken.None));

        AssertWholeRequestRefusal(Assert.IsType<ValueConversionPublicationException>(exception.InnerException), "consumer", "value");
        Assert.Equal(before, await CountPublishWritesAsync());
        Assert.Equal(activation, await _activationAuthority.FindAsync("definition-1", "default"));
        Assert.Equal(authored, JsonSerializer.Serialize(fixture.Version.State));
    }

    private static WorkflowDefinitionVersion WholeRequestVersion(string form, AuthoredValueConversionRequest? conversion)
    {
        var intrinsic = form.StartsWith("intrinsic", StringComparison.Ordinal);
        var inputRead = form.EndsWith("input", StringComparison.Ordinal);
        var read = new ArgumentState(intrinsic ? WorkflowIntrinsicInputKeys.Value : "Text",
            inputRead
                ? new ArgumentValue(JsonSerializer.SerializeToElement(new { referenceKey = CustomerInput.ReferenceKey }), "Input")
                : new ArgumentValue(JsonSerializer.SerializeToElement(new { memberKey = CustomerInput.Name }), "WorkflowRequest"),
            null, null, null, true, conversion);
        var node = intrinsic
            ? new ActivityNode("request-set", "elsa.intrinsic.set@1", [read], [])
            {
                Intrinsic = new AuthoredWorkflowIntrinsic(AuthoredWorkflowIntrinsicKind.Set, new TypeReference("String"), new VariableReference("target", "workflow"))
            }
            : Node("consumer", read);
        return InputReadVersion(form == "scoped-request" ? SequenceNode("sequence", [node]) : node,
            intrinsic ? [new VariableDefinition("target", "Target", new TypeReference("String"), null, null)] : null);
    }

    private static void AssertWholeRequestRefusal(ValueConversionPublicationException refusal, string node, string input)
    {
        Assert.Equal("WorkflowRequestSourceContractUnknown", refusal.ReasonCode.ToString());
        Assert.Equal(new ValueConversionBindingContext(node, input, ValueConversionBindingKind.Input), refusal.Binding);
        Assert.Null(refusal.SourceType);
        Assert.Null(refusal.SourceRepresentation);
        Assert.Equal("String", refusal.TargetType.Alias);
        Assert.Contains("customer", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("pinned", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("option-sentinel", refusal.Message, StringComparison.Ordinal);
    }
}
