namespace Elsa.Workflows.Design.Persistence.Core.Exceptions;

/// <summary>Identifies the design domain whose public persistence contract failed.</summary>
public enum DesignPersistenceDomain
{
    Workflow,
    Activity
}

/// <summary>Classifies the provider-facing failure without leaking a persistence implementation type.</summary>
public enum DesignPersistenceFailureKind
{
    Provider,
    Serialization
}

/// <summary>
/// A provider-neutral persistence failure surfaced by a public workflow- or activity-design adapter.
/// Domain validation, conflicts, readiness outcomes, and cancellation retain their existing contracts.
/// </summary>
public sealed class DesignPersistenceException : InvalidOperationException
{
    public DesignPersistenceException(
        DesignPersistenceDomain domain,
        DesignPersistenceFailureKind failureKind,
        string operation,
        string? context,
        Exception innerException)
        : base(CreateMessage(domain, failureKind, operation, context), innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(innerException);
        Domain = domain;
        FailureKind = failureKind;
        Operation = operation;
        Context = context;
    }

    public DesignPersistenceDomain Domain { get; }
    public DesignPersistenceFailureKind FailureKind { get; }
    public string Operation { get; }
    public string? Context { get; }

    private static string CreateMessage(
        DesignPersistenceDomain domain,
        DesignPersistenceFailureKind failureKind,
        string operation,
        string? context) =>
        $"{domain} design persistence {failureKind.ToString().ToLowerInvariant()} failure during '{operation}'" +
        (string.IsNullOrWhiteSpace(context) ? "." : $" ({context}).");
}
