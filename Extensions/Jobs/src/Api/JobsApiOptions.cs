namespace Elsa.Jobs.Api;

/// <summary>Host-selected logical Jobs partition; HTTP callers cannot select or replace it.</summary>
public sealed class JobsApiOptions
{
    public string PersistencePartition { get; set; } = "default";
}
