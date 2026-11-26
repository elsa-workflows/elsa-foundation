using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Testing;

public static class TestRunTriggerFixture
{
    public static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    public static TestRunTriggerContext Create(string activityType, string inputName, string inputValue)
    {
        var type = new ValueTypeDescriptor("String");
        var node = new ExecutableNode("node", "authored-node", activityType, "1.0.0",
            new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>
            {
                [inputName] = new(inputName, type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(inputValue), ValueProtectionPolicy.InstanceInline))
            }, new Dictionary<string, string>());
        var identity = new WorkflowExecutableIdentity("artifact-test", "definition-test", "draft:test", "draft", "hash-test");
        var executable = new WorkflowExecutable(identity, node, new Dictionary<string, WorkflowExecutableResumeTarget>(), Now,
            new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
        var binding = new WorkflowTriggerBinding("binding-test", identity.ArtifactId, identity.DefinitionId, identity.ArtifactVersion,
            identity.ArtifactHash, node.ExecutableNodeId, "test", "hash", null, new Dictionary<string, string>(), Now);
        return new(executable, activityType, node, binding);
    }

    public static TestRunTriggerPrepareContext Prepare(TestRunTriggerContext trigger, string mode, JsonElement? data = null) =>
        new(trigger, new WorkflowTestScope("test-run", Now.AddMinutes(30), null, new("default")), "test-run", mode, data, "test-run");
}
