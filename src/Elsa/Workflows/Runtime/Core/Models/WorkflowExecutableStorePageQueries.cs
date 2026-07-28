namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>One finite page of source references that point at an artifact.</summary>
public sealed record WorkflowExecutableSourceReferenceArtifactPageQuery : RuntimeStorePageRequest
{
    public WorkflowExecutableSourceReferenceArtifactPageQuery(
        string artifactId,
        int limit = DefaultLimit,
        string? continuationToken = null)
        : base(limit, continuationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArtifactId = artifactId;
    }

    public string ArtifactId { get; }
}

/// <summary>One finite page of source references, optionally restricted to one scope and its live records.</summary>
public sealed record WorkflowExecutableSourceReferencePageQuery : RuntimeStorePageRequest
{
    public WorkflowExecutableSourceReferencePageQuery(
        WorkflowExecutableReferenceScope? scope = null,
        bool liveOnly = false,
        DateTimeOffset? now = null,
        int limit = DefaultLimit,
        string? continuationToken = null)
        : base(limit, continuationToken)
    {
        if (liveOnly && now is null)
            throw new ArgumentNullException(nameof(now), "A live source-reference page requires an explicit evaluation time.");

        Scope = scope;
        LiveOnly = liveOnly;
        Now = now;
    }

    public WorkflowExecutableReferenceScope? Scope { get; }
    public bool LiveOnly { get; }
    public DateTimeOffset? Now { get; }
}

/// <summary>A finite candidate set used by one source-reference garbage-collection decision.</summary>
public sealed record WorkflowExecutableArtifactCandidateBatch
{
    public WorkflowExecutableArtifactCandidateBatch(IEnumerable<string> artifactIds)
    {
        ArgumentNullException.ThrowIfNull(artifactIds);
        ArtifactIds = artifactIds
            .Select(artifactId =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
                return artifactId;
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (ArtifactIds.Count > RuntimeStorePageRequest.MaximumLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(artifactIds),
                $"A source-reference candidate batch cannot contain more than {RuntimeStorePageRequest.MaximumLimit} artifact ids.");
        }
    }

    public IReadOnlyList<string> ArtifactIds { get; }
}

/// <summary>A finite, non-continuable deletion batch for expired or retired source references.</summary>
public sealed record WorkflowExecutableSourceReferenceCleanupBatch
{
    public WorkflowExecutableSourceReferenceCleanupBatch(int limit = RuntimeStorePageRequest.DefaultLimit)
    {
        Limit = RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit));
    }

    public int Limit { get; }
}
