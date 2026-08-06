using System.Text;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>A workflow-bound bounded request for durable <c>Prepared</c> checkpoint reservations.</summary>
public sealed record RuntimeCheckpointPreparedQuery(string WorkflowExecutionId, int PageSize, string? Cursor = null)
{
    /// <summary>The opaque cursor protocol version emitted by this runtime.</summary>
    public const int CurrentCursorVersion = 1;
    /// <summary>The largest supported prepared-reservation page.</summary>
    public const int MaximumPageSize = 250;

    /// <summary>Validates the workflow, page size, and optional bound cursor.</summary>
    /// <exception cref="ArgumentException">The workflow identity is blank or the cursor is malformed/mismatched.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The page size is outside the supported range.</exception>
    public RuntimeCheckpointPreparedQuery Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(WorkflowExecutionId);
        if (PageSize is < 1 or > MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(PageSize), $"Prepared checkpoint page size must be between 1 and {MaximumPageSize}.");
        _ = DecodeCursor();
        return this;
    }

    /// <summary>Decodes and validates the opaque cursor against this request, or returns <see langword="null"/>.</summary>
    /// <exception cref="ArgumentException">The cursor is malformed or bound to different query authority.</exception>
    public RuntimeCheckpointPreparedCursor? DecodeCursor()
    {
        if (Cursor is null)
            return null;
        ArgumentException.ThrowIfNullOrWhiteSpace(Cursor);
        RuntimeCheckpointPreparedCursor decoded;
        try
        {
            var bytes = Convert.FromBase64String(Cursor.Replace('-', '+').Replace('_', '/').PadRight((Cursor.Length + 3) / 4 * 4, '='));
            decoded = JsonSerializer.Deserialize<RuntimeCheckpointPreparedCursor>(bytes)
                ?? throw new JsonException("The cursor payload is empty.");
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new ArgumentException("The prepared checkpoint cursor is malformed.", nameof(Cursor), exception);
        }

        if (decoded.Version != CurrentCursorVersion ||
            !StringComparer.Ordinal.Equals(decoded.WorkflowExecutionId, WorkflowExecutionId) ||
            decoded.Status != RuntimeLogicalCheckpointLedgerStatus.Prepared ||
            decoded.PageSize != PageSize ||
            decoded.WorkflowCheckpointOrder <= 0 ||
            string.IsNullOrWhiteSpace(decoded.CommitId))
        {
            throw new ArgumentException("The prepared checkpoint cursor does not belong to this version, workflow, status filter, or page size.", nameof(Cursor));
        }
        return decoded;
    }

    /// <summary>Creates an opaque cursor bound to this request and the supplied stable sort key.</summary>
    /// <exception cref="ArgumentException"><paramref name="commitId"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="workflowCheckpointOrder"/> is not positive.</exception>
    public string EncodeCursor(long workflowCheckpointOrder, string commitId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workflowCheckpointOrder, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(commitId);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new RuntimeCheckpointPreparedCursor(
            CurrentCursorVersion,
            WorkflowExecutionId,
            RuntimeLogicalCheckpointLedgerStatus.Prepared,
            PageSize,
            workflowCheckpointOrder,
            commitId));
        return Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

/// <summary>The decoded authority and stable sort key carried by an opaque prepared-reservation cursor.</summary>
public sealed record RuntimeCheckpointPreparedCursor(
    int Version,
    string WorkflowExecutionId,
    RuntimeLogicalCheckpointLedgerStatus Status,
    int PageSize,
    long WorkflowCheckpointOrder,
    string CommitId);

/// <summary>One bounded stable page of durable prepared reservations.</summary>
public sealed record RuntimeCheckpointPreparedPage(
    RuntimeCheckpointPreparedQuery Query,
    IReadOnlyList<RuntimeCheckpointPreparedReservation> Reservations,
    string? NextCursor);
