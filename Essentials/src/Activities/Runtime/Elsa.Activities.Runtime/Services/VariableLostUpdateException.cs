using System.Globalization;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// A <c>Fault</c>-mode lost update (spec 1221 FR-012, FR-013): the step wrote a variable on a stale base whose value
/// changed. The step faults through the ordinary incident path; the message names the variable, the base and current
/// versions and both writers, and carries no value.
/// </summary>
internal sealed class VariableLostUpdateException(VariableWriteRecord record) : Exception(Describe(record))
{
    public const string SubStatus = "VariableLostUpdate";

    public VariableWriteRecord Record { get; } = record;

    private static string Describe(VariableWriteRecord record) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Lost update on variable '{record.VariableKey}' in scope '{record.ScopeId}': this step read version {record.BaseVersion}, " +
            $"but the variable is at version {record.CurrentVersion}, written by {Name(record.OverwrittenWriter)}. " +
            $"The step's write by {Name(record.Writer)} is refused because the node requires strict read-modify-write (Fault).");

    private static string Name(VariableWriter? writer) =>
        writer is null
            ? "an earlier writer"
            : $"{writer.Source} on node '{writer.ExecutableNodeId ?? "?"}' (invocation '{writer.InvocationId ?? "?"}')";
}
