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

    public static string RequireScope(IPersistenceAccessContextAccessor accessor) => accessor.Current.RequireScope().Value;

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
        if (!Enum.IsDefined(state.Status))
            throw new InvalidOperationException("The activity execution status is undefined.");
        if (state.ExecutionSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(state.ExecutionSequence));
        state.EnsureValueFlowCompatible();
        state.EnsureSupersessionCompatible();
        ValidateLineage(state.ExecutionScopeId, state.Attempt, state.Provenance, "activity execution");
        ValidateMetadata(state.Metadata, "activity execution metadata");
        ValidateMetadata(state.Provenance.Metadata, "activity execution provenance metadata");
        if (state.Provenance.SchedulingWorkflowExecutionId is { } schedulingWorkflow &&
            !StringComparer.Ordinal.Equals(schedulingWorkflow, state.Execution.WorkflowExecutionId))
            throw new InvalidOperationException("Activity execution scheduling provenance does not match its workflow.");
        ValidateIdentityLength(state.Execution.WorkflowExecutionId, nameof(state.Execution.WorkflowExecutionId));
        ValidateIdentityLength(state.Execution.ActivityExecutionId, nameof(state.Execution.ActivityExecutionId));
        ValidateIdentityLength(state.ExecutionScopeId, nameof(state.ExecutionScopeId));
        ValidateIdentityLength(state.Provenance.ExecutionScopeId, nameof(state.Provenance.ExecutionScopeId));
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
        if (!Enum.IsDefined(projection.Status))
            throw new InvalidOperationException("The activity inspection status is undefined.");
        if (projection.ExecutionSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(projection.ExecutionSequence));
        if (projection.Provenance.SchedulingWorkflowExecutionId is { } schedulingWorkflow &&
            !StringComparer.Ordinal.Equals(schedulingWorkflow, projection.WorkflowExecutionId))
            throw new InvalidOperationException("Activity inspection scheduling provenance does not match its workflow.");
        ValidateLineage(projection.ExecutionScopeId, projection.Attempt, projection.Provenance, "activity inspection");
        ValidateMetadata(projection.Metadata, "activity inspection metadata");
        ValidateMetadata(projection.Provenance.Metadata, "activity inspection provenance metadata");
        ValidateIdentityLength(projection.WorkflowExecutionId, nameof(projection.WorkflowExecutionId));
        ValidateIdentityLength(projection.ActivityExecutionId, nameof(projection.ActivityExecutionId));
        ValidateIdentityLength(projection.ExecutionScopeId, nameof(projection.ExecutionScopeId));
        ValidateIdentityLength(projection.Provenance.ExecutionScopeId, nameof(projection.Provenance.ExecutionScopeId));
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
        ValidateHierarchyItem(record.Item);
        ValidateIdentityLength(record.WorkflowExecutionId, nameof(record.WorkflowExecutionId));
        ValidateIdentityLength(record.ActivityExecutionId, nameof(record.ActivityExecutionId));
        ValidateIdentityLength(record.ExecutionScopeId, nameof(record.ExecutionScopeId));
        ValidateIdentityLength(record.ParentActivityExecutionId, nameof(record.ParentActivityExecutionId));
    }

    private static void ValidateLineage(string? executionScopeId, ActivityExecutionAttemptLineage? attempt, ActivitySchedulingProvenance provenance, string label)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        if (executionScopeId is not null && provenance.ExecutionScopeId is not null &&
            !StringComparer.Ordinal.Equals(executionScopeId, provenance.ExecutionScopeId))
            throw new InvalidOperationException($"{label} ExecutionScopeId must match its scheduling provenance when both are present.");
        if (attempt is not null && provenance.Attempt is not null && attempt != provenance.Attempt)
            throw new InvalidOperationException($"{label} Attempt must match its scheduling provenance when both are present.");
        ValidateAttempt(attempt, $"{label} attempt");
        if (provenance.Attempt is not null && provenance.Attempt.AttemptNumber <= 0)
            throw new InvalidOperationException($"{label} scheduling provenance attempt number must be positive.");
    }

    private static void ValidateMetadata(IReadOnlyDictionary<string, string>? metadata, string label)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        foreach (var entry in metadata)
        {
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value is null)
                throw new InvalidOperationException($"{label} contains a null or blank entry.");
        }
    }

    private static void ValidateHierarchyItem(ActivityExecutionHierarchyItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.ActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.ExecutableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.AuthoredActivityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.ActivityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.ActivityTypeVersion);
        if (!Enum.IsDefined(item.Status) || item.ExecutionSequence < 0 || item.RelativeDepth < 0 ||
            item.BookmarkCount < 0 || item.IncidentCount < 0 || item.BlockingIncidentCount < 0)
            throw new InvalidOperationException("The persisted activity execution hierarchy item contains an invalid scalar field.");
        ValidateNames(item.OutcomeNames, "activity execution hierarchy outcomes");
        ValidateMetadata(item.Metadata, "activity execution hierarchy metadata");
        ValidateIdentityLength(item.WorkflowExecutionId, nameof(item.WorkflowExecutionId));
        ValidateIdentityLength(item.ActivityExecutionId, nameof(item.ActivityExecutionId));
        ValidateIdentityLength(item.ParentActivityExecutionId, nameof(item.ParentActivityExecutionId));
        ValidateIdentityLength(item.SchedulingActivityExecutionId, nameof(item.SchedulingActivityExecutionId));
        ValidateIdentityLength(item.BranchId, nameof(item.BranchId));
        ValidateIdentityLength(item.IterationId, nameof(item.IterationId));
        ValidateAttempt(item.Attempt, "activity execution hierarchy attempt");
        if (item.Boundary is not null)
            ValidateBoundary(item.Boundary);
    }

    private static void ValidateNames(IReadOnlyCollection<string>? names, string label)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException($"The persisted {label} collection contains a blank name.");
    }

    private static void ValidateAttempt(ActivityExecutionAttemptLineage? attempt, string label)
    {
        if (attempt is null)
            return;
        if (attempt.AttemptNumber <= 0 || string.IsNullOrWhiteSpace(attempt.FirstAttemptActivityExecutionId) ||
            attempt.PreviousAttemptActivityExecutionId is { } previous && string.IsNullOrWhiteSpace(previous))
            throw new InvalidOperationException($"The persisted {label} is invalid.");
        ValidateIdentityLength(attempt.FirstAttemptActivityExecutionId, nameof(attempt.FirstAttemptActivityExecutionId));
        ValidateIdentityLength(attempt.PreviousAttemptActivityExecutionId, nameof(attempt.PreviousAttemptActivityExecutionId));
    }

    private static void ValidateBoundary(ActivityExecutionBoundary boundary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary.Kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary.DefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary.DefinitionVersionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary.Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary.TemplateHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary.ExecutionScopeId);
        ArgumentNullException.ThrowIfNull(boundary.InvocationOrigin);
        ArgumentNullException.ThrowIfNull(boundary.InvocationOrigin.Segments);
        foreach (var segment in boundary.InvocationOrigin.Segments)
        {
            ArgumentNullException.ThrowIfNull(segment);
            if (!Enum.IsDefined(segment.Kind) || string.IsNullOrWhiteSpace(segment.Id))
                throw new InvalidOperationException("The persisted activity execution boundary invocation origin is invalid.");
        }
        if (boundary.DirectChildCount < 0 || boundary.CommittedDescendantCount < 0)
            throw new InvalidOperationException("The persisted activity execution boundary contains a negative child count.");
        ValidateAggregate(boundary.Aggregate);
        ValidateIdentityLength(boundary.DefinitionId, nameof(boundary.DefinitionId));
        ValidateIdentityLength(boundary.DefinitionVersionId, nameof(boundary.DefinitionVersionId));
        ValidateIdentityLength(boundary.Version, nameof(boundary.Version));
        ValidateIdentityLength(boundary.TemplateHash, nameof(boundary.TemplateHash));
        ValidateIdentityLength(boundary.ExecutionScopeId, nameof(boundary.ExecutionScopeId));
        ValidateIdentityLength(boundary.ExecutableNodeId, nameof(boundary.ExecutableNodeId));
    }

    private static void ValidateAggregate(ActivityExecutionHierarchyAggregate aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        if (!Enum.IsDefined(aggregate.Status) || aggregate.Total < 0 || aggregate.Scheduled < 0 || aggregate.Running < 0 ||
            aggregate.Suspended < 0 || aggregate.Completed < 0 || aggregate.Faulted < 0 || aggregate.Cancelled < 0 ||
            aggregate.BlockingIncidentCount < 0 || aggregate.RetryCount < 0 || aggregate.LastExecutionSequence < 0)
            throw new InvalidOperationException("The persisted activity execution hierarchy aggregate is invalid.");
    }

    public static void ValidateIdentityLength(string? value, string parameterName)
    {
        if (value is null)
            return;
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
