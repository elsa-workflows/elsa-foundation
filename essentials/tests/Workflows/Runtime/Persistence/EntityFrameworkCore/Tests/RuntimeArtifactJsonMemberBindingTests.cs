using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Input bindings through the options the EF stores persist runtime artifacts with. A whole-value binding of every kind
/// keeps the exact bytes it had before member reads existed (#4078 slice A); the expected strings were written by
/// a9771131c3, the commit before the model change.
/// </summary>
public sealed class RuntimeArtifactJsonMemberBindingTests
{
    private static readonly ValueTypeDescriptor Text = new("String");

    public static TheoryData<string, string> WholeValueBindings => new()
    {
        { "literal", """{"inputName":"bABpAHQAZQByAGEAbAA=","inputKey":"bABpAHQAZQByAGEAbAA=","targetType":{"alias":"UwB0AHIAaQBuAGcA","collectionKind":"Single","schema":null,"schemaVersion":null},"effectivePolicy":{"lifecycle":"Instance","storage":"Inline","isSensitive":false,"requiresEncryption":false,"redactionMode":null,"retentionPolicy":null,"metadata":{}},"source":"Literal","literal":{"type":{"alias":"UwB0AHIAaQBuAGcA","collectionKind":"Single","schema":null,"schemaVersion":null},"presence":"Present","inlineValue":"hello","externalReference":null,"policy":{"lifecycle":"Instance","storage":"Inline","isSensitive":false,"requiresEncryption":false,"redactionMode":null,"retentionPolicy":null,"metadata":{}}},"workflowRequest":null,"variable":null,"activityResult":null,"literalValue":"hello","expression":null,"metadata":{}}""" },
        { "request", """{"inputName":"cgBlAHEAdQBlAHMAdAA=","inputKey":"cgBlAHEAdQBlAHMAdAA=","targetType":{"alias":"UwB0AHIAaQBuAGcA","collectionKind":"Single","schema":null,"schemaVersion":null},"effectivePolicy":{"lifecycle":"Instance","storage":"Inline","isSensitive":false,"requiresEncryption":false,"redactionMode":null,"retentionPolicy":null,"metadata":{}},"source":"WorkflowRequest","literal":null,"workflowRequest":{"memberKey":"YwB1AHMAdABvAG0AZQByAA==","path":"YwB1AHMAdABvAG0AZQByAC4AaQBkAA=="},"variable":null,"activityResult":null,"literalValue":null,"expression":null,"metadata":{}}""" },
        { "variable", """{"inputName":"dgBhAHIAaQBhAGIAbABlAA==","inputKey":"dgBhAHIAaQBhAGIAbABlAA==","targetType":{"alias":"UwB0AHIAaQBuAGcA","collectionKind":"Single","schema":null,"schemaVersion":null},"effectivePolicy":{"lifecycle":"Instance","storage":"Inline","isSensitive":false,"requiresEncryption":false,"redactionMode":null,"retentionPolicy":null,"metadata":{}},"source":"VariableRead","literal":null,"workflowRequest":null,"variable":{"variableKey":"YwB1AHMAdABvAG0AZQByAA==","declaringScopeId":"dwBvAHIAawBmAGwAbwB3AA=="},"activityResult":null,"literalValue":null,"expression":null,"metadata":{}}""" },
        { "scoped-result", """{"inputName":"cwBjAG8AcABlAGQALQByAGUAcwB1AGwAdAA=","inputKey":"cwBjAG8AcABlAGQALQByAGUAcwB1AGwAdAA=","targetType":{"alias":"UwB0AHIAaQBuAGcA","collectionKind":"Single","schema":null,"schemaVersion":null},"effectivePolicy":{"lifecycle":"Instance","storage":"Inline","isSensitive":false,"requiresEncryption":false,"redactionMode":null,"retentionPolicy":null,"metadata":{}},"source":"ActivityResult","literal":null,"workflowRequest":null,"variable":null,"activityResult":{"producerExecutableNodeId":"cAByAG8AZAB1AGMAZQByAA==","projectionKey":"JAByAGUAcwB1AGwAdAA=","producerScopeId":"cwBjAG8AcABlADoAcgBvAG8AdAA=","isOptional":true},"literalValue":null,"expression":null,"metadata":{}}""" },
        { "result", """{"inputName":"cgBlAHMAdQBsAHQA","inputKey":"cgBlAHMAdQBsAHQA","targetType":{"alias":"UwB0AHIAaQBuAGcA","collectionKind":"Single","schema":null,"schemaVersion":null},"effectivePolicy":{"lifecycle":"Instance","storage":"Inline","isSensitive":false,"requiresEncryption":false,"redactionMode":null,"retentionPolicy":null,"metadata":{}},"source":"ActivityResult","literal":null,"workflowRequest":null,"variable":null,"activityResult":{"producerExecutableNodeId":"cAByAG8AZAB1AGMAZQByAA==","projectionKey":"YwB1AHMAdABvAG0AZQByAA==","isOptional":false},"literalValue":null,"expression":null,"metadata":{},"conversionPlan":{"schemaVersion":1,"sourceRepresentation":"TextValue","sourceType":{"alias":"UwB0AHIAaQBuAGcA","collectionKind":"Single","schema":null,"schemaVersion":null},"targetType":{"alias":"UwB0AHIAaQBuAGcA","collectionKind":"Single","schema":null,"schemaVersion":null},"mode":"Auto","operation":"Identity","profile":null,"limits":{"maxDepth":64,"maxCollectionItems":10000,"maxPayloadBytes":1048576},"options":{},"fingerprint":"cwBoAGEAMgA1ADYAOgA4AGEAOAAwADgANQAxADUAYQAyAGQANgBjADIAYwA1AGQAYgBmAGUAYwA5ADgANABhADcAOABkADUAMAA3ADkAMwBiAGYAOABiADMAOAA0AGUAMgA1ADkAOQBlADIAOABhAGIAMgBkAGQAOQAyAGMAYgAwADMAMABjADMAYwBiAA=="}}""" },
        { "expression", """{"inputName":"ZQB4AHAAcgBlAHMAcwBpAG8AbgA=","inputKey":"ZQB4AHAAcgBlAHMAcwBpAG8AbgA=","targetType":{"alias":"UwB0AHIAaQBuAGcA","collectionKind":"Single","schema":null,"schemaVersion":null},"effectivePolicy":{"lifecycle":"Instance","storage":"Inline","isSensitive":false,"requiresEncryption":false,"redactionMode":null,"retentionPolicy":null,"metadata":{}},"source":"Expression","literal":null,"workflowRequest":null,"variable":null,"activityResult":null,"literalValue":null,"expression":{"language":"SgBhAHYAYQBTAGMAcgBpAHAAdAA=","expression":"YQByAGcAcwAuAHAA","resultType":null,"metadata":{},"parameters":{},"parameterConversionPlans":{},"options":{},"capabilityProfile":"YgBpAG4AZABpAG4AZwAtAHAAdQByAGUALQB2ADEA"},"metadata":{}}""" },
        { "secret", """{"inputName":"cwBlAGMAcgBlAHQA","inputKey":"cwBlAGMAcgBlAHQA","targetType":{"alias":"UwB0AHIAaQBuAGcA","collectionKind":"Single","schema":null,"schemaVersion":null},"effectivePolicy":{"lifecycle":"Instance","storage":"Inline","isSensitive":true,"requiresEncryption":true,"redactionMode":null,"retentionPolicy":null,"metadata":{}},"source":"SecretRead","literal":null,"workflowRequest":null,"variable":null,"activityResult":null,"literalValue":null,"expression":null,"metadata":{},"secret":{"name":"YQBwAGkALQBrAGUAeQA=","typeName":"dABlAHgAdAA="}}""" }
    };

    [Theory]
    [MemberData(nameof(WholeValueBindings))]
    public void A_whole_value_binding_keeps_its_stored_bytes(string kind, string expected)
    {
        var json = RuntimeArtifactJson.Serialize(WholeValue(kind));

        Assert.Equal(expected, json);
        Assert.Equal(json, RuntimeArtifactJson.Serialize(RuntimeArtifactJson.Deserialize<RuntimeInputBinding>(json)));
    }

    /// <summary>
    /// A member read of each kind round-trips through the store options: its source by name, its role payload and its
    /// selection. These bytes are new with #4078 slice A, so the stored form of the selection is pinned here.
    /// </summary>
    [Theory]
    [InlineData("WorkflowRequestMember")]
    [InlineData("VariableMember")]
    [InlineData("ActivityResultMember")]
    public void A_member_binding_round_trips_through_the_runtime_artifact_options(string kind)
    {
        var selection = new RuntimeReadSelection(1, ["customer", "a.b"], RuntimeReadSelectionMode.Known, Text, mayBeAbsent: true, mayBeNull: false,
            ValueRepresentation.TextValue, "elsa.activity-value-json/1");
        var binding = kind switch
        {
            "WorkflowRequestMember" => new RuntimeInputBinding("member", Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.WorkflowRequestMember,
                selection, workflowRequest: new RuntimeWorkflowRequestReference("customer")),
            "VariableMember" => new RuntimeInputBinding("member", Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.VariableMember,
                selection, variable: new RuntimeVariableReference("customer", "workflow")),
            _ => new RuntimeInputBinding("member", Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.ActivityResultMember,
                selection, activityResult: new RuntimeActivityResultReference("producer", "$result", producerScopeId: null))
        };

        var json = RuntimeArtifactJson.Serialize(binding);
        var restored = RuntimeArtifactJson.Deserialize<RuntimeInputBinding>(json);

        Assert.Contains($"\"source\":\"{kind}\"", json, StringComparison.Ordinal);
        Assert.EndsWith(""","selection":{"schemaVersion":1,"segments":["YwB1AHMAdABvAG0AZQByAA==","YQAuAGIA"],"mode":"Known","selectedType":{"alias":"UwB0AHIAaQBuAGcA","collectionKind":"Single","schema":null,"schemaVersion":null},"mayBeAbsent":true,"mayBeNull":false,"representation":"TextValue","profileRevision":"ZQBsAHMAYQAuAGEAYwB0AGkAdgBpAHQAeQAtAHYAYQBsAHUAZQAtAGoAcwBvAG4ALwAxAA=="}}""", json, StringComparison.Ordinal);
        Assert.Equal(binding.Source, restored.Source);
        Assert.Equal(["customer", "a.b"], restored.Selection!.Segments);
        Assert.Equal(json, RuntimeArtifactJson.Serialize(restored));
    }

    internal static RuntimeInputBinding WholeValue(string kind) => kind switch
    {
        "literal" => new("literal", Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(Text, JsonSerializer.SerializeToElement("hello"), ValueProtectionPolicy.InstanceInline)),
        "request" => new("request", Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.WorkflowRequest,
            workflowRequest: new RuntimeWorkflowRequestReference("customer", "customer.id")),
        "variable" => new("variable", Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.VariableRead,
            variable: new RuntimeVariableReference("customer", "workflow")),
        "scoped-result" => new("scoped-result", Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference("producer", "$result", "scope:root", isOptional: true)),
        "result" => new("result", Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference("producer", "customer", producerScopeId: null),
            conversionPlan: ValueConversionPlan.Identity(Text, ValueRepresentation.TextValue)),
        "expression" => new("expression", Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding("JavaScript", "args.p")),
        "secret" => new("secret", Text, new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: true),
            RuntimeInputBindingSource.SecretRead, secret: new RuntimeSecretReference("api-key", "text")),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
