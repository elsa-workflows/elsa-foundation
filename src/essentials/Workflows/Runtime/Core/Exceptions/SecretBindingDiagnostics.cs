using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// The fixed diagnostics for secret bindings. Every message names the node, the input and the reason, and never a
/// value or a reference payload, so publication, literal readers and activation refuse a secret the same way.
/// </summary>
public static class SecretBindingDiagnostics
{
    /// <summary>A withheld input reached a reader that needs its value and cannot resolve it.</summary>
    public const string WithheldInputCode = "VF-ACT-010";

    /// <summary>A secret reference is bound where the value would be persisted or must be known at publish time.</summary>
    public const string SecretBindingRefusedCode = "VF-ACT-012";

    public static InvalidOperationException WithheldInputNotResolved(string inputKey) =>
        new($"{WithheldInputCode}: Activity input '{inputKey}' was withheld and is not resolved in this host.");

    /// <summary>A withheld variable value reached a reader that needs its value and cannot resolve it.</summary>
    public static InvalidOperationException WithheldVariableNotResolved(string variableName) =>
        new($"{WithheldInputCode}: Variable '{variableName}' holds a withheld value that is not resolved in this host.");

    /// <summary>A withheld value reached value conversion, which reads the value and cannot resolve it.</summary>
    public static InvalidOperationException WithheldValueNotConverted(string targetTypeAlias) =>
        new($"{WithheldInputCode}: A withheld value cannot be converted to '{targetTypeAlias}': it is not resolved in this host.");

    /// <summary>The input is named by the activity's <see cref="RefusesSecretBindingAttribute"/>.</summary>
    public static ArgumentException SecretBindingRefused(string nodeId, string inputKey, SecretBindingRefusalReason reason) =>
        Refused(nodeId, inputKey, reason switch
        {
            SecretBindingRefusalReason.PersistedByActivity => "the activity copies its value into its own persisted state",
            SecretBindingRefusalReason.FixedAtPublish => "its value is read when the workflow is published",
            SecretBindingRefusalReason.EchoedToOutput => "the activity returns its value in its result or copies it into a fault",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown secret binding refusal reason.")
        });

    /// <summary>The node is a workflow intrinsic, which writes its value into a variable or workflow output.</summary>
    public static ArgumentException IntrinsicInputRefused(string nodeId, string inputKey) =>
        Refused(nodeId, inputKey, "workflow intrinsics write their values into persisted workflow state");

    /// <summary>The node's activity consumer is not CLR activation, so nothing would resolve the reference.</summary>
    public static ArgumentException NonClrConsumerRefused(string nodeId, string inputKey, string consumerKey) =>
        Refused(nodeId, inputKey, $"activity consumer '{consumerKey}' does not resolve inputs when the activity runs");

    /// <summary>The CLR activity type cannot be resolved, so its refusals cannot be read.</summary>
    public static ArgumentException UnresolvedActivityTypeRefused(string nodeId, string inputKey) =>
        Refused(nodeId, inputKey, "the activity type is not available to check whether the input accepts one");

    /// <summary>The activity is a checkpoint participant, which reads its inputs outside activation.</summary>
    public static ArgumentException CheckpointParticipantRefused(string nodeId, string inputKey) =>
        Refused(nodeId, inputKey, "the activity reads its inputs into checkpoint state outside activation");

    /// <summary>The input is the activity's value-outcomes input, which derives its outcome ports at publish.</summary>
    public static ArgumentException ValueOutcomesInputRefused(string nodeId, string inputKey) =>
        Refused(nodeId, inputKey, "its value derives the activity's outcome ports at publish");

    private static ArgumentException Refused(string nodeId, string inputKey, string reason) =>
        new($"{SecretBindingRefusedCode}: Activity node '{nodeId}' input '{inputKey}' cannot take a secret reference: {reason}.");

    public static ArgumentException VariableDefaultRefused(string nodeId, string variableKey) =>
        new($"{SecretBindingRefusedCode}: Variable '{variableKey}' on activity node '{nodeId}' cannot take a secret reference as its initial value: a variable's initial value is persisted in its variable frame.");

    /// <summary>
    /// The backstop every publish-time literal reader applies before reading a binding: a secret reference has no
    /// literal to read, so it is refused by name instead of being treated as unauthored or as a generic non-literal.
    /// </summary>
    public static void ThrowIfSecretRead(RuntimeInputBinding? binding, string nodeId, string inputKey)
    {
        if (binding?.Source == RuntimeInputBindingSource.SecretRead)
            throw SecretBindingRefused(nodeId, inputKey, SecretBindingRefusalReason.FixedAtPublish);
    }
}
