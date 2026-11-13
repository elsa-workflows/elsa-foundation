using Elsa.Workflows.Publishing.Releases.Core;

namespace Elsa.Workflows.Publishing.Releases.Capture.Core;

/// <summary>An exact owner selector. Kind determines identity/version interpretation; no latest/draft selection is implied.</summary>
public sealed class ReleaseCaptureSelection(string kind, string identity, ReleaseOwnerVersion? version, ReleaseRootAction action)
{
    public string Kind { get; } = kind;
    public string Identity { get; } = identity;
    public ReleaseOwnerVersion? Version { get; } = version;
    public ReleaseRootAction Action { get; } = action;
}

/// <summary>Exact captured owner content and selected roots, valid only within the source callback.</summary>
public sealed class ReleaseSourceCapture(IReleaseContentReader content, IReadOnlyList<ReleaseBundleRoot> roots)
{
    public IReleaseContentReader Content { get; } = content;
    public IReadOnlyList<ReleaseBundleRoot> Roots { get; } = Array.AsReadOnly(roots.ToArray());
}

/// <summary>Exact-kind source strategies. Duplicate claims fail composition; source authorization is supplied by host context.</summary>
/// <remarks>Resolve only exact immutable selections and their owned associations, and invoke the callback exactly once
/// while required source retention is held. Await it through durable capture; never retry a callback or expose lazy reads
/// outside its lifetime. Registered member contributors complete the cross-owner closure before independent verification.
/// Return roots in selection order with each selection's exact requested action.
/// Cancellation/retention loss propagates; it cannot establish successful creation.</remarks>
public interface IReleaseCaptureSource
{
    ReleaseSemanticAdapterIdentity Identity { get; }
    IReadOnlyCollection<string> SupportedSelectionKinds { get; }
    ValueTask ExecuteAsync(IReadOnlyList<ReleaseCaptureSelection> selections, ReleaseBundleLimits limits,
        Func<ReleaseSourceCapture, CancellationToken, ValueTask> capture, CancellationToken cancellationToken = default);
}

/// <summary>Source-side fan-in for module-owned members embedded in, or exactly referenced by, captured containers.</summary>
/// <remarks>Contributors may share a container codec; their identities must be unique. Process each contributor/container
/// key and digest once, in bounded batches. Return members only: no selected roots, edges or requirements. The final
/// bundle builder discovers those facts independently. Await the callback through durable capture when additional
/// source retention is required. Never look up a latest version or rewrite a retained container.</remarks>
public interface IReleaseSourceMemberContributor
{
    ReleaseSemanticAdapterIdentity Identity { get; }
    IReadOnlyCollection<ReleaseCodecKey> SupportedContainerCodecs { get; }
    ValueTask ExecuteAsync(IReadOnlyList<ReleaseContentMember> containers, IReleaseContentReader captured,
        ReleaseBundleLimits limits, Func<IReleaseContentReader, CancellationToken, ValueTask> capture,
        CancellationToken cancellationToken = default);
}

public enum ReleaseCaptureOutcome { Unknown = 0, Created = 1, Existing = 2 }

/// <summary>Confirmed archive acceptance. An ambiguous commit is an exception and must be reconciled by capture ID.</summary>
public sealed class ReleaseCaptureReceipt(string captureId, string releaseDigest, ReleaseCaptureOutcome outcome)
{
    public string CaptureId { get; } = captureId;
    public string ReleaseDigest { get; } = releaseDigest;
    public ReleaseCaptureOutcome Outcome { get; } = outcome;
}

/// <summary>Replacement contract: captures all selected owners under retention, verifies actual facts and archives the complete bundle.</summary>
public interface IReleaseCaptureService
{
    ValueTask<ReleaseCaptureReceipt> CreateAsync(string captureId, IReadOnlyList<ReleaseCaptureSelection> selections,
        ReleaseSemanticVerificationOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>A stable value-free source capture refusal; provider/ambiguous commit exceptions retain their own boundaries.</summary>
public sealed class ReleaseCaptureException(string code, ReleaseSemanticVerificationResult? verification = null)
    : Exception("Release capture was refused (" + code + ").")
{
    public string Code { get; } = code;
    public ReleaseSemanticVerificationResult? Verification { get; } = verification;
}
