namespace Elsa.Etl.Runtime;

/// <summary>
/// A trusted host RabbitMQ revision that ETL pipelines may publish their main route to (spec 1200) or read a source queue
/// from (spec 1201).
/// </summary>
public sealed class EtlHostRabbitMqBindingOptions
{
    /// <summary>The trusted tenant identifier, or null for the explicit null-tenant scope.</summary>
    public string? TenantId { get; set; }

    /// <summary>The required opaque persistence partition for this binding.</summary>
    public string PersistencePartition { get; set; } = string.Empty;

    /// <summary>The bounded alias referenced by a published ETL pipeline.</summary>
    public string Alias { get; set; } = string.Empty;

    /// <summary>The connector role: <c>MainOutput</c> for a RabbitMQ destination or <c>Source</c> for a RabbitMQ source.</summary>
    public string Role { get; set; } = "MainOutput";

    /// <summary>The immutable host revision identifier.</summary>
    public string Revision { get; set; } = string.Empty;

    /// <summary>Whether new runs may pin this revision.</summary>
    public bool IsCurrent { get; set; } = true;

    /// <summary>The AMQP URI, supplied through host configuration; it is never pinned or exposed.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>The exchanges pipelines may publish to, compared exactly; an empty name allows the default exchange.</summary>
    public IList<string> AllowedExchanges { get; set; } = new List<string>();

    /// <summary>The queues pipelines may read, compared exactly.</summary>
    public IList<string> AllowedQueues { get; set; } = new List<string>();
}
