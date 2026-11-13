namespace Elsa.Workflows.Publishing.Releases.Core;

/// <summary>A selected source member and its requested exact content/action pair, subject to owner verification.</summary>
public sealed class ReleaseBundleRoot(ReleaseMemberKey member, ReleaseMemberKey content, ReleaseRootAction action)
{
    public ReleaseMemberKey Member { get; } = member;
    public ReleaseMemberKey Content { get; } = content;
    public ReleaseRootAction Action { get; } = action;
}

/// <summary>A semantically verified portable bundle, or a located refusal. This is not durable source capture or target admission.</summary>
public sealed record ReleaseBundleBuildResult(IReleaseBundleTransport? bundle, ReleaseSemanticVerificationResult verification)
{
    public IReleaseBundleTransport? Bundle { get; } = bundle;
    public ReleaseSemanticVerificationResult Verification { get; } = verification;
}

/// <summary>Replacement contract: builds the manifest from complete actual owner facts over bounded copied content.</summary>
/// <remarks>The caller retains source content through durable capture. This service never reads catalogs or fills missing members.</remarks>
public interface IReleaseBundleBuilder
{
    ValueTask<ReleaseBundleBuildResult> BuildAsync(IReleaseContentReader content, IReadOnlyList<ReleaseBundleRoot> roots,
        ReleaseSemanticVerificationOptions? options = null, CancellationToken cancellationToken = default);
}
