using Elsa.Activities.Runtime.Core.Serialization;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.Runtime.Tests.Verification;

public sealed class RuntimeReadSelectionReleaseTests
{
    [Theory]
    [InlineData(RuntimeInputBindingSource.WorkflowRequestMember, true)]
    [InlineData(RuntimeInputBindingSource.VariableMember, true)]
    [InlineData(RuntimeInputBindingSource.ActivityResultMember, true)]
    [InlineData(RuntimeInputBindingSource.WorkflowRequestMember, false)]
    [InlineData(RuntimeInputBindingSource.VariableMember, false)]
    [InlineData(RuntimeInputBindingSource.ActivityResultMember, false)]
    public async Task Captured_member_reads_preserve_exact_paths_and_report_actual_type_profile_and_engine_facts(
        RuntimeInputBindingSource source, bool known)
    {
        var fixture = new RuntimeReleaseFixture();
        var selection = new RuntimeReadSelection(1, ["literal.dot", "", "注文"],
            known ? RuntimeReadSelectionMode.Known : RuntimeReadSelectionMode.DeferredFit,
            selectedType: known ? new ValueTypeDescriptor("selected.SourceType") : null,
            mayBeAbsent: known ? true : null, mayBeNull: known ? false : null,
            profileRevision: known ? ActivityValueJsonProfile.Revision : null);
        var binding = new RuntimeInputBinding("value", new("String"), ValueProtectionPolicy.InstanceInline, source, selection,
            workflowRequest: source == RuntimeInputBindingSource.WorkflowRequestMember ? new("request") : null,
            variable: source == RuntimeInputBindingSource.VariableMember ? new("variable", "workflow") : null,
            activityResult: source == RuntimeInputBindingSource.ActivityResultMember ? new("producer", "Result", null, true) : null);
        var node = RuntimeReleaseFixture.Node(bindings: new Dictionary<string, RuntimeInputBinding> { ["value"] = binding });
        var workflow = fixture.Codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, RuntimeReleaseFixture.Workflow(node)));
        var template = fixture.Codec.EncodeTemplate(new(RuntimeReleaseFixture.Scope, RuntimeReleaseFixture.Template(node)));
        foreach (var encoded in new[] { workflow, template })
        {
            var member = fixture.Add(encoded);
            var facts = await fixture.Verify(member);
            Assert.Equal(ReleaseSemanticStatus.Verified, facts.Status);
            Assert.Contains(facts.Prerequisites, x => x.Kind == "runtime-consumer" &&
                x.Identity == RuntimeReadSelection.RequirementKey && x.Scheme == "exact" && x.Constraint == "1");
            Assert.Equal(known, facts.Prerequisites.Any(x => x.Kind == "value-type" && x.Identity == "selected.SourceType"));
            Assert.Equal(known, facts.Prerequisites.Any(x => x.Kind == "activity-value-profile" &&
                x.Identity == "activity-value-json" && x.Scheme == "exact" && x.Constraint == ActivityValueJsonProfile.Revision));
            var decodedNode = encoded.Descriptor.Kind == ReleaseRuntimeCodecs.WorkflowKind
                ? fixture.Codec.DecodeWorkflow(encoded.GetPayloadBytes()).Executable.RootActivity
                : fixture.Codec.DecodeTemplate(encoded.GetPayloadBytes()).Template.Root;
            var decoded = decodedNode.InputBindings["value"];
            Assert.Equal(source, decoded.Source);
            Assert.Equal(selection.Segments, decoded.Selection!.Segments);
            Assert.Equal(selection.Mode, decoded.Selection.Mode);
            Assert.Equal(selection.ProfileRevision, decoded.Selection.ProfileRevision);
            Assert.Equal(selection.SelectedType?.Alias, decoded.Selection.SelectedType?.Alias);
            Assert.Equal(selection.MayBeAbsent, decoded.Selection.MayBeAbsent);
            Assert.Equal(selection.MayBeNull, decoded.Selection.MayBeNull);
            Assert.Empty(facts.Resources);
        }
    }
}
