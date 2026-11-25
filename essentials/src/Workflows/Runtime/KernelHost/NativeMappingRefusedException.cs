namespace Elsa.Workflows.Runtime.KernelHost;

/// <summary>
/// A compiled executable the kernel does not run, met where <see cref="NativeExecutableMapper"/> turns it into a Native
/// workflow. It names the node and, for a binding, the input, so the publish that activates the executable can say which
/// part of the definition to change.
/// </summary>
public sealed class NativeMappingRefusedException(string code, string node, string? input, string reason)
    : InvalidOperationException(input is null ? $"Node '{node}': {reason}" : $"Node '{node}', input '{input}': {reason}")
{
    /// <summary>The node is an activity, an intrinsic or a structure the kernel does not run.</summary>
    public const string ActivityUnsupported = "activity-unsupported";

    /// <summary>An input, output capture or expression parameter reads its value in a way the kernel does not support.</summary>
    public const string BindingUnsupported = "binding-unsupported";

    /// <summary><see cref="ActivityUnsupported"/> or <see cref="BindingUnsupported"/>.</summary>
    public string Code { get; } = code;

    /// <summary>The executable node id, which is also the authored node id.</summary>
    public string Node { get; } = node;

    /// <summary>The input, output or variable the refusal is about, or null when it is about the node itself.</summary>
    public string? Input { get; } = input;

    /// <summary>Why the kernel refuses it.</summary>
    public string Reason { get; } = reason;

    internal static NativeMappingRefusedException Activity(string node, string reason) => new(ActivityUnsupported, node, null, reason);

    internal static NativeMappingRefusedException Binding(string node, string? input, string reason) => new(BindingUnsupported, node, input, reason);
}
