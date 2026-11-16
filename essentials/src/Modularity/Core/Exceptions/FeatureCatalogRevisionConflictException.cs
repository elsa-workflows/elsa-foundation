namespace Elsa.Modularity.Core.Exceptions;

public sealed class FeatureCatalogRevisionConflictException(string expectedRevision, string actualRevision)
    : InvalidOperationException($"Feature catalog revision '{expectedRevision}' is stale. Current revision is '{actualRevision}'.")
{
    public string ExpectedRevision { get; } = expectedRevision;

    public string ActualRevision { get; } = actualRevision;
}
