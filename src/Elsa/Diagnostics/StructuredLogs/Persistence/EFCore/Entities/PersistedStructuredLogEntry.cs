namespace Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Entities;

/// <summary>
/// The durable row form of a captured log entry. Deliberately does NOT derive from
/// <c>Elsa.Primitives.Entities.Entity</c>: this is a high-volume append-only diagnostics table that wants
/// a provider-portable auto-increment cursor (<see cref="Id"/>) rather than the string-Id + RowNumber +
/// timestamp-stamping + entity-saving-event machinery the shared base applies. Complex members
/// (properties, scopes, exception) are persisted as JSON text columns.
/// </summary>
public sealed class PersistedStructuredLogEntry
{
    /// <summary>
    /// The durable, monotonically increasing cursor. Database-generated identity (an
    /// <c>INTEGER PRIMARY KEY</c> rowid on SQLite, a <c>bigint</c> identity on server providers), so it is
    /// reliable for ordering and resume on every provider — unlike the in-process <see cref="Sequence"/>,
    /// which resets per host process.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// The in-process live-feed sequence assigned by the sink. Informational once persisted (it can repeat
    /// across host restarts); durable ordering uses <see cref="Id"/>.
    /// </summary>
    public long Sequence { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    /// <summary>The <see cref="Microsoft.Extensions.Logging.LogLevel"/> stored as its integer value.</summary>
    public int Level { get; set; }

    public string Category { get; set; } = string.Empty;

    public int? EventId { get; set; }

    public string? EventName { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? MessageTemplate { get; set; }

    public string SourceId { get; set; } = string.Empty;

    /// <summary>JSON array of captured properties; null when none.</summary>
    public string? PropertiesJson { get; set; }

    /// <summary>JSON array of captured scopes; null when none.</summary>
    public string? ScopesJson { get; set; }

    /// <summary>JSON object of the captured exception chain; null when none.</summary>
    public string? ExceptionJson { get; set; }
}
