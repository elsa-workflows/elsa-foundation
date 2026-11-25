namespace Elsa.Workflows.Runtime.KernelHost;

/// <summary>
/// A compiled executable this host does not run, met where <see cref="NativeExecutableMapper"/> turns it into a Native
/// workflow. It names the node (by its display name too, where the compiled node has one) and, for a binding, the input,
/// and its message ends with what the author can change in Studio.
/// </summary>
public sealed class NativeMappingRefusedException(string code, string node, string? input, string reason, string? displayName = null)
    : InvalidOperationException($"{Subject(node, displayName, input)}: {reason}")
{
    /// <summary>The node is an activity, an intrinsic or a structure this host does not run.</summary>
    public const string ActivityUnsupported = "activity-unsupported";

    /// <summary>An input, output capture, variable or expression parameter reads its value in a way this host does not support.</summary>
    public const string BindingUnsupported = "binding-unsupported";

    /// <summary><see cref="ActivityUnsupported"/> or <see cref="BindingUnsupported"/>.</summary>
    public string Code { get; } = code;

    /// <summary>The executable node id, which is also the authored node id.</summary>
    public string Node { get; } = node;

    /// <summary>The node's display name, when the compiled node carries one.</summary>
    public string? DisplayName { get; } = displayName;

    /// <summary>The input, output or variable the refusal is about, or null when it is about the node itself.</summary>
    public string? Input { get; } = input;

    /// <summary>Why this host refuses it, and what to change.</summary>
    public string Reason { get; } = reason;

    internal static NativeMappingRefusedException Activity(string node, string reason) => new(ActivityUnsupported, node, null, reason);

    internal static NativeMappingRefusedException Binding(string node, string? input, string reason) => new(BindingUnsupported, node, input, reason);

    /// <summary>The same refusal, naming the node by its display name too.</summary>
    internal NativeMappingRefusedException Named(string displayName) => new(Code, Node, Input, Reason, displayName);

    private static string Subject(string node, string? displayName, string? input)
    {
        var subject = displayName is { Length: > 0 } name && name != node ? $"Node '{name}' ({node})" : $"Node '{node}'";
        return input is null ? subject : $"{subject}, input '{input}'";
    }
}
