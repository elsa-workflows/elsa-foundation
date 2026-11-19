using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>A provider could not establish scoped cohort retention roots; executable material must be retained.</summary>
public sealed class StimulusDeliveryRetentionException : InvalidOperationException
{
    public StimulusDeliveryRetentionException(PersistenceScope? scope, Exception innerException)
        : base("Saved stimulus retention roots could not be established; executable material must be retained.", innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
        Scope = scope;
    }

    /// <summary>The actual read scope, or null when access failed before a scope could be established.</summary>
    public PersistenceScope? Scope { get; }
}
