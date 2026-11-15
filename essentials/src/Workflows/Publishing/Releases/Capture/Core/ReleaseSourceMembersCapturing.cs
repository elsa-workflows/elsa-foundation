using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Publishing.Releases.Core;

namespace Elsa.Workflows.Publishing.Releases.Capture.Core;

/// <summary>Inline source-member fan-in while all selected owner acquisitions remain retained.</summary>
/// <remarks>The single aggregating handler acquires registered member contributions, then awaits
/// CompleteAsync through independent verification and durable archive acceptance. Middleware and audit
/// handlers share the normal event pipeline. Skipping or repeating completion cannot acknowledge capture.</remarks>
public sealed class ReleaseSourceMembersCapturing(IReadOnlyList<ReleaseSourceCapture> roots,
    ReleaseSemanticVerificationOptions options, Func<IReleaseContentReader, CancellationToken, ValueTask> complete) : IEvent
{
    public IReadOnlyList<ReleaseSourceCapture> Roots { get; } = Array.AsReadOnly(roots.ToArray());
    public ReleaseSemanticVerificationOptions Options { get; } = options;

    /// <summary>Invoke exactly once and await while every contributed reader remains retained.</summary>
    public ValueTask CompleteAsync(IReleaseContentReader content, CancellationToken cancellationToken = default) => complete(content, cancellationToken);
}
