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

    public static ArgumentException SecretBindingRefused(string nodeId, string inputKey, SecretBindingRefusalReason reason) =>
        SecretBindingRefused(nodeId, inputKey, reason switch
        {
            SecretBindingRefusalReason.PersistedByActivity => "the activity copies its value into its own persisted state",
            SecretBindingRefusalReason.FixedAtPublish => "its value is read when the workflow is published",
            SecretBindingRefusalReason.EchoedToOutput => "the activity returns its value in its result or copies it into a fault",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown secret binding refusal reason.")
        });

    public static ArgumentException SecretBindingRefused(string nodeId, string inputKey, string reason) =>
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
