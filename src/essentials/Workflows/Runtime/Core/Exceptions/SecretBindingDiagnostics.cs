using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// The fixed diagnostics for secret bindings and withheld values, so publication, literal readers, value resolution
/// and activation refuse a secret the same way. No message carries a value or a reference payload. A
/// <see cref="SecretBindingRefusedCode"/> refusal names the node, the input and the reason; a
/// <see cref="WithheldInputCode"/> refusal names the input, the variable or the conversion target that met a withheld
/// value.
/// </summary>
public static class SecretBindingDiagnostics
{
    /// <summary>
    /// A withheld value reached a reader that needs it and cannot resolve it: an input's own value, a variable, a value an
    /// input reads, or a conversion. The refusal is thrown as a <see cref="WithheldValueException"/>; a reader that
    /// reports deterministic faults instead of throwing records a fault with this code and the same message.
    /// </summary>
    public const string WithheldInputCode = "VF-ACT-010";

    /// <summary>A secret reference is bound where the value would be persisted or must be known at publish time.</summary>
    public const string SecretBindingRefusedCode = "VF-ACT-012";

    /// <summary>The input's own value was withheld: a secret read, or a literal that stands for a withheld value.</summary>
    public static WithheldValueException WithheldInputNotResolved(string inputKey) =>
        new($"{WithheldInputCode}: Activity input '{inputKey}' was withheld and is not resolved in this host.");

    /// <summary>
    /// The input is not withheld itself, but the value it reads is: a workflow request member or an activity result.
    /// </summary>
    public static WithheldValueException WithheldSourceNotResolved(string inputKey) =>
        new($"{WithheldInputCode}: Activity input '{inputKey}' reads a withheld value that is not resolved in this host.");

    /// <summary>A withheld variable value reached a reader that needs its value and cannot resolve it.</summary>
    public static WithheldValueException WithheldVariableNotResolved(string variableName) =>
        new($"{WithheldInputCode}: Variable '{variableName}' holds a withheld value that is not resolved in this host.");

    /// <summary>A withheld value reached value conversion, which reads the value and cannot resolve it.</summary>
    public static WithheldValueException WithheldValueNotConverted(string targetTypeAlias) =>
        new($"{WithheldInputCode}: A withheld value cannot be converted to '{targetTypeAlias}': it is not resolved in this host.");

    /// <summary>
    /// A withheld value reached the reader of <paramref name="binding"/>, worded for what the binding reads: the variable
    /// for a variable read, the value it reads for a workflow request member or an activity result, and otherwise the
    /// input's own value.
    /// </summary>
    public static WithheldValueException WithheldBindingNotResolved(RuntimeInputBinding binding) =>
        binding.Source switch
        {
            RuntimeInputBindingSource.VariableRead => WithheldVariableNotResolved(binding.Variable!.VariableKey),
            RuntimeInputBindingSource.WorkflowRequest or RuntimeInputBindingSource.ActivityResult => WithheldSourceNotResolved(binding.InputName),
            _ => WithheldInputNotResolved(binding.InputName)
        };

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
    /// literal to read, so it is refused by name instead of being treated as unauthored or as a generic non-literal. A
    /// literal that stands for a withheld value has none either; it reads as no literal at all, so it is refused too.
    /// </summary>
    public static void ThrowIfSecretRead(RuntimeInputBinding? binding, string nodeId, string inputKey)
    {
        if (binding?.Source == RuntimeInputBindingSource.SecretRead)
            throw SecretBindingRefused(nodeId, inputKey, SecretBindingRefusalReason.FixedAtPublish);
        if (binding?.Literal?.Presence == ValuePresence.Withheld)
            throw WithheldInputNotResolved(inputKey);
    }
}
