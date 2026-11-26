using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>
/// The one failure a member read (#4078) raises while an input is materialized, before the input snapshot is written
/// and before the consumer is constructed. The message starts with its <see cref="Code"/>, and the scheduler records
/// the code and input key in the poisoned work item's metadata, as it does for expression input failures. Messages
/// name the input, the node, the source and the segment, never a value.
/// </summary>
internal sealed class RuntimeReadSelectionException : InvalidOperationException
{
    /// <summary>A selected member is absent and the input requires a value.</summary>
    public const string AbsentMember = "VF-SEL-001";

    /// <summary>The source, a prefix or the selected member is null and the input does not accept null.</summary>
    public const string NullMember = "VF-SEL-002";

    /// <summary>A segment was applied to a value that is not an object, or the source cannot be selected from.</summary>
    public const string NotSelectable = "VF-SEL-003";

    /// <summary>The selected member does not fit the input's type.</summary>
    public const string Mismatch = "VF-SEL-004";

    private RuntimeReadSelectionException(string code, string inputKey, string message, Exception? innerException = null)
        : base($"{code}: {message}", innerException)
    {
        Code = code;
        InputKey = inputKey;
    }

    public string Code { get; }
    public string InputKey { get; }

    public static RuntimeReadSelectionException Absent(RuntimeInputBinding binding, string nodeId, int segmentIndex) => new(AbsentMember, binding.InputName,
        $"Input '{binding.InputName}' on node '{nodeId}' reads {Describe(binding)}, and " +
        (segmentIndex < 0 ? "the source is absent" : $"member {Segment(binding, segmentIndex)} is absent") +
        "; the input requires a value.");

    public static RuntimeReadSelectionException Null(RuntimeInputBinding binding, string nodeId, int segmentIndex) => new(NullMember, binding.InputName,
        $"Input '{binding.InputName}' on node '{nodeId}' reads {Describe(binding)}, and " +
        (segmentIndex < 0
            ? "the source is null (or an optional producer did not run)"
            : segmentIndex == binding.Selection!.Segments.Count - 1
                ? $"member {Segment(binding, segmentIndex)} is null"
                : $"member {Segment(binding, segmentIndex)} before the selected member is null") +
        "; the input does not accept null.");

    public static RuntimeReadSelectionException NotAnObject(RuntimeInputBinding binding, string nodeId, int segmentIndex, JsonValueKind found) => new(NotSelectable, binding.InputName,
        $"Input '{binding.InputName}' on node '{nodeId}' reads {Describe(binding)}, but member {Segment(binding, segmentIndex)} is looked up in " +
        $"a {DescribeKind(found)}, which has no members.");

    public static RuntimeReadSelectionException Unselectable(RuntimeInputBinding binding, string nodeId, string reason) => new(NotSelectable, binding.InputName,
        $"Input '{binding.InputName}' on node '{nodeId}' reads {Describe(binding)}, but {reason}.");

    public static RuntimeReadSelectionException DoesNotFit(RuntimeInputBinding binding, string nodeId, Exception? innerException = null) => new(Mismatch, binding.InputName,
        $"Input '{binding.InputName}' on node '{nodeId}' reads {Describe(binding)}, and the selected member does not fit '{binding.TargetType.Alias}'.",
        innerException);

    /// <summary>
    /// Whether <paramref name="exception"/>, raised while a present member is converted or checked against the input's
    /// type, means the member does not fit (<see cref="Mismatch"/>). One predicate for every member-read consumer, so
    /// the activity and intrinsic paths classify the same failures alike.
    /// </summary>
    public static bool IsFitFailure(Exception exception) =>
        exception is JsonException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException or OverflowException;

    private static string DescribeKind(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Array => "collection",
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        _ => "value"
    };

    private static string Describe(RuntimeInputBinding binding) => binding.Source switch
    {
        RuntimeInputBindingSource.ActivityResultMember => $"a member of result '{binding.ActivityResult!.ProjectionKey}' of node '{binding.ActivityResult.ProducerExecutableNodeId}'",
        RuntimeInputBindingSource.VariableMember => $"a member of variable '{binding.Variable!.VariableKey}'",
        RuntimeInputBindingSource.WorkflowRequestMember => $"a member of request member '{binding.WorkflowRequest!.MemberKey}'",
        _ => $"a member of its {binding.Source} source"
    };

    private static string Segment(RuntimeInputBinding binding, int index) =>
        $"{index + 1} of {binding.Selection!.Segments.Count}";
}
