using System.Security.Cryptography;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Shared lossless identity, projection and content checks for the R07-R09 EF adapters.
/// </summary>
internal static class ActivityExecutionEfSupport
{
    public static string Encode(string value) => EfRelationalIdentity.Encode(value);
    public static string Decode(string value) => EfRelationalIdentity.Decode(value);
    public static string Hash(string value) => EfRelationalIdentity.Hash(value);
    public static string OrderKey(string value) => Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(value, RuntimeActivityExecutionEfModule.IdentityMaximumLength));
    public static string CreateId(string kind, string scope, string workflowExecutionId, string activityExecutionId) =>
        Hash($"{kind.Length}:{kind}{scope.Length}:{scope}{workflowExecutionId.Length}:{workflowExecutionId}{activityExecutionId.Length}:{activityExecutionId}");

    public static string RequireScope(IPersistenceAccessContextAccessor accessor)
    {
        var current = accessor.Current;
        if (current.AccessPolicy != PersistenceAccessPolicy.Ordinary || current.Scope is null || current.AcrossScopes)
            throw new InvalidOperationException("EF activity-execution persistence requires one explicit persistence scope.");
        return current.Scope.Value;
    }

    public static string? EffectiveExecutionScope(ActivityExecutionState state) =>
        string.IsNullOrWhiteSpace(state.ExecutionScopeId) ? state.Provenance.ExecutionScopeId : state.ExecutionScopeId;

    public static string? EffectiveExecutionScope(ActivityExecutionInspectionProjection projection) =>
        string.IsNullOrWhiteSpace(projection.ExecutionScopeId) ? projection.Provenance.ExecutionScopeId : projection.ExecutionScopeId;

    public static long NewRevision()
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        RandomNumberGenerator.Fill(bytes);
        var value = BitConverter.ToInt64(bytes) & (long.MaxValue >> 1);
        return value == 0 ? 1 : value;
    }

    public static void Validate(ActivityExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(state.Execution);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.Execution.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.Execution.ActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.Execution.ExecutableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.Execution.AuthoredActivityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.Execution.ActivityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.Execution.ActivityTypeVersion);
        ArgumentNullException.ThrowIfNull(state.Provenance);
        ArgumentNullException.ThrowIfNull(state.BookmarkIds);
        ArgumentNullException.ThrowIfNull(state.IncidentIds);
        ArgumentNullException.ThrowIfNull(state.Metadata);
        if (state.ExecutionSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(state.ExecutionSequence));
        state.EnsureValueFlowCompatible();
        state.EnsureSupersessionCompatible();
        if (state.Provenance.SchedulingWorkflowExecutionId is { } schedulingWorkflow &&
            !StringComparer.Ordinal.Equals(schedulingWorkflow, state.Execution.WorkflowExecutionId))
            throw new InvalidOperationException("Activity execution scheduling provenance does not match its workflow.");
        ValidateIdentityLength(state.Execution.WorkflowExecutionId, nameof(state.Execution.WorkflowExecutionId));
        ValidateIdentityLength(state.Execution.ActivityExecutionId, nameof(state.Execution.ActivityExecutionId));
    }

    public static void Validate(ActivityExecutionInspectionProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentException.ThrowIfNullOrWhiteSpace(projection.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projection.ActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projection.ExecutableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projection.AuthoredActivityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projection.ActivityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(projection.ActivityTypeVersion);
        ArgumentNullException.ThrowIfNull(projection.Provenance);
        ArgumentNullException.ThrowIfNull(projection.OutcomeNames);
        ArgumentNullException.ThrowIfNull(projection.Bookmarks);
        ArgumentNullException.ThrowIfNull(projection.Incidents);
        ArgumentNullException.ThrowIfNull(projection.ValueSnapshots);
        ArgumentNullException.ThrowIfNull(projection.Metadata);
        if (projection.ExecutionSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(projection.ExecutionSequence));
        if (projection.Provenance.SchedulingWorkflowExecutionId is { } schedulingWorkflow &&
            !StringComparer.Ordinal.Equals(schedulingWorkflow, projection.WorkflowExecutionId))
            throw new InvalidOperationException("Activity inspection scheduling provenance does not match its workflow.");
        ValidateIdentityLength(projection.WorkflowExecutionId, nameof(projection.WorkflowExecutionId));
        ValidateIdentityLength(projection.ActivityExecutionId, nameof(projection.ActivityExecutionId));
    }

    public static void Validate(ActivityExecutionHierarchyRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.ExecutionScopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.ActivityExecutionId);
        ArgumentNullException.ThrowIfNull(record.Item);
        if (record.ExecutionSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(record.ExecutionSequence));
        if (!StringComparer.Ordinal.Equals(record.WorkflowExecutionId, record.Item.WorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(record.ActivityExecutionId, record.Item.ActivityExecutionId) ||
            record.ExecutionSequence != record.Item.ExecutionSequence ||
            !StringComparer.Ordinal.Equals(record.ParentActivityExecutionId, record.Item.ParentActivityExecutionId))
            throw new InvalidOperationException("Activity execution hierarchy record envelope does not match its item.");
        ValidateIdentityLength(record.WorkflowExecutionId, nameof(record.WorkflowExecutionId));
        ValidateIdentityLength(record.ActivityExecutionId, nameof(record.ActivityExecutionId));
        ValidateIdentityLength(record.ExecutionScopeId, nameof(record.ExecutionScopeId));
    }

    public static void ValidateIdentityLength(string value, string parameterName)
    {
        if (value.Length > RuntimeActivityExecutionEfModule.IdentityMaximumLength)
            throw new ArgumentException($"The {parameterName} value cannot exceed {RuntimeActivityExecutionEfModule.IdentityMaximumLength} characters.", parameterName);
    }

    public static int OffsetMinutes(DateTimeOffset value) => checked((int)value.Offset.TotalMinutes);

    public static void EnsureRowEnvelope(string schemaVersion, string scopeKey, string scope, string scopeHash, string expectedId, string actualId, long revision)
    {
        if (!StringComparer.Ordinal.Equals(schemaVersion, RuntimeActivityExecutionEfModule.SchemaVersion) ||
            !StringComparer.Ordinal.Equals(Decode(scopeKey), scope) ||
            !StringComparer.Ordinal.Equals(scopeHash, Hash(scope)) ||
            !StringComparer.Ordinal.Equals(expectedId, actualId) ||
            revision <= 0)
            throw new InvalidDataException("The persisted EF activity-execution row envelope is corrupt.");
    }

    public static void EnsureIdentityProjection(string encoded, string hash, string orderKey, string expected)
    {
        if (!StringComparer.Ordinal.Equals(Decode(encoded), expected) ||
            !StringComparer.Ordinal.Equals(hash, Hash(expected)) ||
            !StringComparer.Ordinal.Equals(orderKey, OrderKey(expected)))
            throw new InvalidDataException("The persisted EF activity-execution identity projection is corrupt.");
    }
}
