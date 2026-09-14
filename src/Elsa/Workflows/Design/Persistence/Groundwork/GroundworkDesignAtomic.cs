using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Atomic;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Primitives.Entities;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Groundwork.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork;

public sealed record GroundworkDesignOperationIdentity(string OperationKind, string OperationKey);

public sealed record GroundworkDesignAtomicWriteRequest(
    GroundworkDesignOperationIdentity Operation,
    string RequestFingerprint,
    IReadOnlyCollection<string> MutatedUnits);

public sealed record GroundworkDesignAtomicWriteResult(
    DesignAtomicWriteStatus Status,
    string? AuthoritativeResultFingerprint = null,
    string? AuthoritativeResultJson = null)
{
    public static GroundworkDesignAtomicWriteResult Committed(string fingerprint, string json) =>
        new(DesignAtomicWriteStatus.Committed, fingerprint, json);

    public static GroundworkDesignAtomicWriteResult Rejected() =>
        new(DesignAtomicWriteStatus.Rejected);
}

public sealed record GroundworkDesignAtomicWriteStageResult(
    bool IsAccepted,
    string? AuthoritativeResultFingerprint = null,
    string? AuthoritativeResultJson = null)
{
    public static GroundworkDesignAtomicWriteStageResult Accepted(string fingerprint, string json) =>
        new(true, fingerprint, json);

    public static GroundworkDesignAtomicWriteStageResult Rejected() => new(false);
}

public sealed record GroundworkDesignSaveRequest(
    string UnitId,
    StorageValues Values,
    long? ExpectedVersion = null);

public sealed record GroundworkDesignDeleteRequest(
    string UnitId,
    string Id,
    long? ExpectedVersion = null);

public sealed class GroundworkDesignAtomicWriteContext : IDesignAtomicWriteContext
{
    private readonly GroundworkDesignStorage.DesignUnitOfWork unitOfWork;
    internal GroundworkDesignStorage Storage { get; }
    private bool stagedFailure;

    internal GroundworkDesignAtomicWriteContext(
        GroundworkDesignStorage.DesignUnitOfWork unitOfWork,
        GroundworkDesignStorage storage)
    {
        this.unitOfWork = unitOfWork;
        Storage = storage;
    }

    internal bool HasStagedFailure => stagedFailure;

    public Task SaveAsync(GroundworkDesignSaveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        unitOfWork.Stage(request.UnitId, request.Values, Options(request.ExpectedVersion));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(GroundworkDesignDeleteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        unitOfWork.StageDelete(request.UnitId, request.Id, Options(request.ExpectedVersion));
        return Task.CompletedTask;
    }

    internal void MarkFailure() => stagedFailure = true;

    private static WriteOptions Options(long? expectedVersion) =>
        expectedVersion is null
            ? WriteOptions.Unconditional
            : expectedVersion.Value == 0
                ? WriteOptions.CreateOnly
                : WriteOptions.IfVersion(expectedVersion.Value);
}

public sealed class GroundworkDesignAtomicWrite(
    GroundworkDesignStorage storage,
    TimeProvider? timeProvider = null,
    TimeSpan? reconciliationTimeout = null) : IDesignAtomicWriter
{
    private const string MarkerIdentityVersion = "elsa-design-operation:v1";
    private const int MarkerRaceAttemptBudget = 4;
    private static readonly TimeSpan MarkerRaceBackoffStep = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan DefaultReconciliationTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions MarkerOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan timeout = reconciliationTimeout ?? DefaultReconciliationTimeout;

    public async Task<DesignAtomicWriteResult<T>> ExecuteAsync<T>(
        DesignOperationKey operationKey,
        string operationKind,
        object requestMaterial,
        IReadOnlyCollection<string> mutatedUnits,
        Func<IDesignAtomicWriteContext, CancellationToken, Task<DesignAtomicWriteStage<T>>> stage,
        Func<CancellationToken, Task>? beforeAttempt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
        ArgumentNullException.ThrowIfNull(requestMaterial);
        ArgumentNullException.ThrowIfNull(stage);
        var request = GroundworkDesignAtomicWriteMaterial.Create(operationKind, "1", requestMaterial, MarkerOptions);
        var result = await ExecuteLegacyAsync(
            new GroundworkDesignAtomicWriteRequest(
                new GroundworkDesignOperationIdentity(operationKind, operationKey.Value),
                request.Fingerprint,
                mutatedUnits),
            beforeAttempt,
            async (context, token) =>
            {
                var staged = await stage(context, token);
                ArgumentNullException.ThrowIfNull(staged);
                if (!staged.IsAccepted)
                    return GroundworkDesignAtomicWriteStageResult.Rejected();
                ArgumentNullException.ThrowIfNull(staged.Value);
                if (!string.IsNullOrWhiteSpace(staged.ResultFingerprint) && !string.IsNullOrWhiteSpace(staged.ResultJson))
                    return GroundworkDesignAtomicWriteStageResult.Accepted(staged.ResultFingerprint, staged.ResultJson);
                var authoritative = GroundworkDesignAtomicWriteMaterial.Create(
                    $"{operationKind}.result", "1", staged.Value, MarkerOptions);
                return GroundworkDesignAtomicWriteStageResult.Accepted(authoritative.Fingerprint, authoritative.Json);
            },
            cancellationToken);

        var status = result.Status switch
        {
            DesignAtomicWriteStatus.Committed => DesignAtomicWriteStatus.Committed,
            DesignAtomicWriteStatus.Reconciled => DesignAtomicWriteStatus.Reconciled,
            DesignAtomicWriteStatus.Replayed => DesignAtomicWriteStatus.Replayed,
            DesignAtomicWriteStatus.Conflict => DesignAtomicWriteStatus.Conflict,
            DesignAtomicWriteStatus.Rejected => DesignAtomicWriteStatus.Rejected,
            _ => throw new ArgumentOutOfRangeException(nameof(result.Status))
        };
        var value = status is DesignAtomicWriteStatus.Committed or DesignAtomicWriteStatus.Reconciled or DesignAtomicWriteStatus.Replayed
            ? GroundworkDesignAtomicWriteMaterial.Deserialize<T>(
                result.AuthoritativeResultFingerprint!, result.AuthoritativeResultJson!,
                $"{operationKind}.result", "1", MarkerOptions)
            : default;
        return new DesignAtomicWriteResult<T>(status, value, result.AuthoritativeResultFingerprint, result.AuthoritativeResultJson);
    }

    // Compatibility overloads keep the existing Groundwork conformance harness source-compatible;
    // command composition resolves the provider-neutral Core interface above.
    public Task<GroundworkDesignAtomicWriteResult> ExecuteAsync(
        GroundworkDesignAtomicWriteRequest request,
        Func<GroundworkDesignAtomicWriteContext, CancellationToken, Task<GroundworkDesignAtomicWriteStageResult>> stage,
        CancellationToken cancellationToken = default) =>
        ExecuteLegacyAsync(request, null, stage, cancellationToken);

    public Task<GroundworkDesignAtomicWriteResult> ExecuteAsync(
        GroundworkDesignAtomicWriteRequest request,
        Func<CancellationToken, Task>? beforeAttempt,
        Func<GroundworkDesignAtomicWriteContext, CancellationToken, Task<GroundworkDesignAtomicWriteStageResult>> stage,
        CancellationToken cancellationToken = default) =>
        ExecuteLegacyAsync(request, beforeAttempt, stage, cancellationToken);

    private async Task<GroundworkDesignAtomicWriteResult> ExecuteLegacyAsync(
        GroundworkDesignAtomicWriteRequest request,
        Func<CancellationToken, Task>? beforeAttempt,
        Func<GroundworkDesignAtomicWriteContext, CancellationToken, Task<GroundworkDesignAtomicWriteStageResult>> stage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(stage);
        Validate(request);
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(reconciliationTimeout));

        var markerId = MarkerId(request.Operation);
        var lane = new DesignAtomicWriteLane<GroundworkDesignStorage.DesignUnitOfWork, GroundworkDesignOperationMarker, GroundworkDesignAtomicWriteStageResult, GroundworkDesignAtomicWriteResult>
        {
            MarkerId = markerId,
            LoadMarker = _ => Task.FromResult(ReadMarker(markerId)),
            BeginScope = () => storage.BeginUnitOfWork(request.MutatedUnits.Append(WorkflowsDesignStorageManifest.DesignOperationDocumentKind).ToArray()),
            SaveMarker = (unitOfWork, staged, _) =>
            {
                if (string.IsNullOrWhiteSpace(staged.AuthoritativeResultFingerprint) || string.IsNullOrWhiteSpace(staged.AuthoritativeResultJson))
                    throw new InvalidDataException("An accepted design operation must provide an authoritative result.");
                var marker = new GroundworkDesignOperationMarker(request.Operation.OperationKind, request.Operation.OperationKey, request.RequestFingerprint, staged.AuthoritativeResultFingerprint, staged.AuthoritativeResultJson, clock.GetUtcNow());
                unitOfWork.Stage(WorkflowsDesignStorageManifest.DesignOperationDocumentKind, MarkerValues(markerId, marker), WriteOptions.CreateOnly);
                return Task.CompletedTask;
            },
            Commit = (unitOfWork, _) =>
            {
                try
                {
                    var report = unitOfWork.Commit();
                    if (!report.IsSuccessful)
                    {
                        if (IsOperationMarkerConflict(report.Outcomes))
                            throw new GroundworkDesignOperationMarkerRaceException();
                        var failed = report.Outcomes.Where(item => item.Disposition == RowWriteDisposition.Applied && !item.Outcome.Succeeded).ToArray();
                        if (failed.Length != 0)
                            throw new GroundworkDesignWriteProviderException("Groundwork rejected the design-operation batch.", new BatchWriteException("Groundwork returned unsuccessful design-operation outcomes.", failed));
                        return Task.FromResult(DesignAtomicCommitDisposition.Rejected);
                    }
                    return Task.FromResult(DesignAtomicCommitDisposition.Committed);
                }
                catch (BatchWriteException exception)
                {
                    if (IsOperationMarkerConflict(exception.Outcomes))
                        throw new GroundworkDesignOperationMarkerRaceException();
                    throw new GroundworkDesignWriteProviderException("Groundwork rejected the design-operation batch.", exception);
                }
            },
            Rollback = TryRollback,
            ClassifyMarkerRace = exception => exception is GroundworkDesignOperationMarkerRaceException,
            ClassifyUncertainCommit = exception => exception is GroundworkDesignUncertainCommitException,
            OnUncertainCommit = (exception, token) => ReconcileAsync(markerId, request, token),
            DisposeBeforeReconcile = unitOfWork =>
            {
                unitOfWork.Dispose();
                return Task.CompletedTask;
            },
            TryReconcileAfterCommit = (exception, token) => ReconcileAfterCommitAsync(markerId, request, exception, token),
            Delay = (attempt, token) => Task.Delay(MarkerRaceBackoffStep * attempt, clock, token),
            IsAccepted = staged => staged.IsAccepted,
            OnCommitted = staged => GroundworkDesignAtomicWriteResult.Committed(staged.AuthoritativeResultFingerprint!, staged.AuthoritativeResultJson!),
            OnReplay = marker => Resolve(marker, request, DesignAtomicWriteStatus.Replayed),
            OnRejected = GroundworkDesignAtomicWriteResult.Rejected,
            MarkerRaceAttemptBudget = MarkerRaceAttemptBudget,
            CreateExhaustedMarkerRaceException = (_, id) => new GroundworkDesignUncertainCommitException($"Design operation marker '{id}' conflicted, but the winner could not be reloaded.")
        };
        return await DesignAtomicWriteProtocol.ExecuteAsync(lane, async (unitOfWork, token) => await stage(new GroundworkDesignAtomicWriteContext(unitOfWork, storage.ForUnitOfWork(unitOfWork)), token), beforeAttempt, cancellationToken);
    }

    private async Task<GroundworkDesignAtomicWriteResult> ReconcileAsync(string markerId, GroundworkDesignAtomicWriteRequest request, CancellationToken cancellationToken)
    {
        using var reconciliation = new CancellationTokenSource(timeout);
        var backoff = MarkerRaceBackoffStep;
        while (true)
        {
            try
            {
                var winner = ReadMarker(markerId);
                if (winner is not null)
                    return Resolve(winner, request, DesignAtomicWriteStatus.Reconciled);
            }
            catch (GroundworkDesignCorruptMarkerException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
                // Continue until the bounded recovery window classifies the outcome.
                _ = exception;
            }
            try { await Task.Delay(backoff, clock, reconciliation.Token); }
            catch (OperationCanceledException) when (reconciliation.IsCancellationRequested)
            { throw new DesignAtomicWriteUnknownOutcomeException($"Design operation marker '{markerId}' did not become visible within the reconciliation timeout."); }
            backoff = TimeSpan.FromMilliseconds(Math.Min(backoff.TotalMilliseconds * 2, 250));
        }
    }

    private async Task<GroundworkDesignAtomicWriteResult?> ReconcileAfterCommitAsync(
        string markerId,
        GroundworkDesignAtomicWriteRequest request,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ReconcileAsync(markerId, request, cancellationToken);
        }
        catch (DesignAtomicWriteUnknownOutcomeException)
        {
            throw new DesignAtomicWriteUnknownOutcomeException(
                $"The Groundwork commit acknowledgement for design operation '{markerId}' has an unknown outcome after bounded recovery.",
                exception);
        }
    }

    private GroundworkDesignOperationMarker? ReadMarker(string markerId)
    {
        var row = storage.Read(WorkflowsDesignStorageManifest.DesignOperationDocumentKind, markerId);
        if (row is null)
            return null;
        try
        {
            var content = row.Entry.Values.Values[WorkflowsDesignStorageManifest.ContentField];
            var marker = content switch
            {
                JsonElement element => element.Deserialize<GroundworkDesignOperationMarker>(MarkerOptions),
                JsonDocument document => document.Deserialize<GroundworkDesignOperationMarker>(MarkerOptions),
                string text => JsonSerializer.Deserialize<GroundworkDesignOperationMarker>(text, MarkerOptions),
                _ => null
            };
            return marker ?? throw new InvalidDataException("Design-operation marker content is empty.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidDataException)
        {
            throw new GroundworkDesignCorruptMarkerException(
                $"Design-operation marker '{markerId}' could not be deserialized.", exception);
        }
    }

    private static StorageValues MarkerValues(string markerId, GroundworkDesignOperationMarker marker)
    {
        var content = JsonSerializer.SerializeToElement(marker, MarkerOptions);
        return new StorageValues(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [WorkflowsDesignStorageManifest.IdField] = markerId,
            [WorkflowsDesignStorageManifest.SchemaVersionField] = WorkflowsDesignStorageManifest.SchemaVersion,
            [WorkflowsDesignStorageManifest.ContentField] = content,
            [WorkflowsDesignStorageManifest.OperationIdField] = markerId,
            [WorkflowsDesignStorageManifest.OperationKindField] = marker.OperationKind,
            [WorkflowsDesignStorageManifest.OperationKeyField] = marker.OperationKey,
            [WorkflowsDesignStorageManifest.OperationRequestFingerprintField] = marker.RequestFingerprint,
            [WorkflowsDesignStorageManifest.OperationResultFingerprintField] = marker.ResultFingerprint,
            [WorkflowsDesignStorageManifest.OperationResultJsonField] = JsonDocument.Parse(marker.ResultJson).RootElement.Clone(),
            ["createdAt"] = marker.CreatedAt,
            ["lastModifiedAt"] = marker.CreatedAt
        });
    }

    private static GroundworkDesignAtomicWriteResult Resolve(
        GroundworkDesignOperationMarker marker,
        GroundworkDesignAtomicWriteRequest request,
        DesignAtomicWriteStatus status)
    {
        if (!StringComparer.Ordinal.Equals(marker.OperationKind, request.Operation.OperationKind) ||
            !StringComparer.Ordinal.Equals(marker.OperationKey, request.Operation.OperationKey))
            throw new GroundworkDesignCorruptMarkerException("The design-operation marker identity does not match the requested operation.");
        // Reusing a stable operation key under a different request fingerprint is a conflict, and
        // Conflict is a status this API declares. Report it as one rather than throwing: the marker
        // is left untouched either way, but a caller that wants the outcome as data -- the design
        // persistence contract asserts a Conflict result carrying no authoritative fingerprint --
        // cannot recover it from an exception. Command callers are unaffected; every one of them
        // goes through GroundworkDesignAtomicCommand, which already turns this status into
        // GroundworkDesignOperationConflictException.
        if (!StringComparer.Ordinal.Equals(marker.RequestFingerprint, request.RequestFingerprint))
            return new GroundworkDesignAtomicWriteResult(DesignAtomicWriteStatus.Conflict);
        return new GroundworkDesignAtomicWriteResult(status, marker.ResultFingerprint, marker.ResultJson);
    }

    private static string MarkerId(GroundworkDesignOperationIdentity operation)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Concat(MarkerIdentityVersion, "\u001f", operation.OperationKind, "\u001f", operation.OperationKey)));
        return Convert.ToHexStringLower(bytes);
    }

    private static void Validate(GroundworkDesignAtomicWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Operation.OperationKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Operation.OperationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestFingerprint);
        ArgumentNullException.ThrowIfNull(request.MutatedUnits);
        if (request.MutatedUnits.Count == 0 || request.MutatedUnits.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A design operation must declare at least one mutated unit.", nameof(request));
    }

    private static bool IsOperationMarkerConflict(IReadOnlyCollection<RowWriteOutcome> outcomes) =>
        outcomes.Any(item =>
            StringComparer.Ordinal.Equals(item.Write.Unit.Id.Value, WorkflowsDesignStorageManifest.DesignOperationDocumentKind) &&
            item.Outcome.Status == WriteOutcomeStatus.ConcurrencyConflict);

    private static void TryRollback(GroundworkDesignStorage.DesignUnitOfWork unitOfWork)
    {
        try { unitOfWork.Rollback(); }
        catch { }
    }
}

public sealed record GroundworkDesignOperationMarker(
    string OperationKind,
    string OperationKey,
    string RequestFingerprint,
    string ResultFingerprint,
    string ResultJson,
    DateTimeOffset CreatedAt);

public sealed record GroundworkDesignAtomicCommandResult<TResult>(
    TResult Value,
    DesignAtomicWriteStatus Status)
    where TResult : notnull
{
    public bool ShouldPublishPostCommitOutcome =>
        Status is DesignAtomicWriteStatus.Committed or DesignAtomicWriteStatus.Reconciled;
}

public static class GroundworkDesignAtomicCommand
{
    private const string MaterialSchemaVersion = "1";

    public static async Task<GroundworkDesignAtomicCommandResult<TResult>> ExecuteAsync<TRequest, TResult>(
        IDesignAtomicWriter atomicWrite,
        DesignOperationKey operationKey,
        string operationKind,
        TRequest requestMaterial,
        IReadOnlyCollection<string> mutatedUnits,
        Func<GroundworkDesignAtomicWriteContext, CancellationToken, Task<TResult>> stage,
        JsonSerializerOptions? jsonOptions = null,
        CancellationToken cancellationToken = default,
        Func<CancellationToken, Task>? beforeAttempt = null,
        DesignPersistenceDomain? persistenceDomain = null,
        string? failureContext = null)
        where TRequest : notnull
        where TResult : notnull
    {
        var domain = persistenceDomain ?? (operationKind.StartsWith("workflow.", StringComparison.Ordinal)
            ? DesignPersistenceDomain.Workflow
            : (DesignPersistenceDomain?)null);
        try
        {
            var result = await atomicWrite.ExecuteAsync(
                operationKey,
                operationKind,
                requestMaterial,
                mutatedUnits,
                async (context, token) =>
                {
                    var value = await stage((GroundworkDesignAtomicWriteContext)context, token);
                    var authoritative = GroundworkDesignAtomicWriteMaterial.Create(
                        $"{operationKind}.result", MaterialSchemaVersion, value, jsonOptions);
                    return DesignAtomicWriteStage<TResult>.Accepted(value, authoritative.Fingerprint, authoritative.Json);
                },
                beforeAttempt,
                cancellationToken);
            return result.Status switch
            {
                DesignAtomicWriteStatus.Committed or DesignAtomicWriteStatus.Reconciled or DesignAtomicWriteStatus.Replayed =>
                    new GroundworkDesignAtomicCommandResult<TResult>(
                        result.Value!, result.Status),
                DesignAtomicWriteStatus.Conflict => throw new GroundworkDesignOperationConflictException(operationKind, operationKey.Value),
                DesignAtomicWriteStatus.Rejected => throw new GroundworkDesignOperationRejectedException(operationKind, operationKey.Value),
                _ => throw new ArgumentOutOfRangeException(nameof(result.Status))
            };
        }
        catch (Exception exception) when (domain is not null && TryMap(exception, domain.Value, operationKind, failureContext, out var mapped))
        {
            throw mapped!;
        }
    }

    private static bool TryMap(Exception exception, DesignPersistenceDomain domain, string operation, string? context, out DesignPersistenceException? mapped)
    {
        mapped = null;
        if (exception is OperationCanceledException or DesignPersistenceException)
            return false;
        var kind = exception switch
        {
            GroundworkDesignWriteProviderException => DesignPersistenceFailureKind.Provider,
            GroundworkProviderFailureException => DesignPersistenceFailureKind.Provider,
            GroundworkDesignCorruptMarkerException or GroundworkDesignSerializationException or GroundworkDesignCorruptResultException => DesignPersistenceFailureKind.Serialization,
            _ => (DesignPersistenceFailureKind?)null
        };
        if (kind is null)
            return false;
        mapped = new DesignPersistenceException(domain, kind.Value, operation, context, exception.InnerException ?? exception);
        return true;
    }
}

public static class GroundworkDesignSerialization
{
    public static T Execute<T>(DesignPersistenceDomain domain, string operation, string context, Func<T> serialize)
    {
        try { return serialize(); }
        catch (OperationCanceledException) { throw; }
        catch (DesignPersistenceException) { throw; }
        catch (Exception exception) { throw new DesignPersistenceException(domain, DesignPersistenceFailureKind.Serialization, operation, context, exception); }
    }
}

public sealed record GroundworkDesignAtomicWriteMaterial(string Json, string Fingerprint)
{
    private const string FingerprintIdentity = "elsa-design-material:v1";
    public static GroundworkDesignAtomicWriteMaterial Create<T>(string operationKind, string schema, T material, JsonSerializerOptions? options = null)
    {
        try
        {
            var element = JsonSerializer.SerializeToElement(material, options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var json = Canonical(element);
            var framed = string.Concat(Frame(FingerprintIdentity), Frame(operationKind), Frame(schema), Frame(json));
            return new(json, $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(framed)))}");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new GroundworkDesignSerializationException($"Design material for '{operationKind}' could not be serialized.", exception);
        }
    }

    public static T Deserialize<T>(string fingerprint, string json, string operationKind, string schema, JsonSerializerOptions? options = null)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var canonical = Canonical(document.RootElement);
            var framed = string.Concat(Frame(FingerprintIdentity), Frame(operationKind), Frame(schema), Frame(canonical));
            var expected = $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(framed)))}";
            if (!StringComparer.Ordinal.Equals(expected, fingerprint))
                throw new GroundworkDesignCorruptResultException("Authoritative design result fingerprint mismatch.");
            return JsonSerializer.Deserialize<T>(json, options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web))
                   ?? throw new GroundworkDesignCorruptResultException("Authoritative design result is null.");
        }
        catch (GroundworkDesignCorruptResultException) { throw; }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new GroundworkDesignCorruptResultException("Authoritative design result could not be deserialized.", exception);
        }
    }

    private static string Frame(string value) => $"{Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture)}:{value}";
    private static string Canonical(JsonElement element)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) WriteCanonical(writer, element);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item);
            writer.WriteEndArray();
        }
        else element.WriteTo(writer);
    }
}

public static class GroundworkDocumentWriter
{
    public static GroundworkDesignSaveRequest ToTenantScopedSaveRequest<TEntity>(
        string unitId,
        string collection,
        string schemaVersion,
        TEntity entity,
        JsonSerializerOptions jsonOptions,
        PersistenceAccessContext accessContext,
        DesignPersistenceDomain? persistenceDomain = null,
        string? failureContext = null)
        where TEntity : Entity
    {
        if (entity is TenantEntity tenantEntity)
            accessContext.EnsureTenantScope(tenantEntity.TenantId);
        var values = GroundworkDesignStorage.Values(unitId, entity, jsonOptions, collection);
        return new GroundworkDesignSaveRequest(unitId, values);
    }

    public static GroundworkDesignSaveRequest ToSaveRequest<TEntity>(
        string unitId,
        string collection,
        string schemaVersion,
        TEntity entity,
        JsonSerializerOptions jsonOptions)
        where TEntity : Entity =>
        new(unitId, GroundworkDesignStorage.Values(unitId, entity, jsonOptions, collection));

    public static GroundworkDesignDeleteRequest ToDeleteRequest(string unitId, string id) =>
        new(unitId, id);
}

public sealed class GroundworkDesignOperationConflictException(string operationKind, string operationKey)
    : InvalidOperationException($"Design operation key '{operationKey}' is already bound to different material for '{operationKind}'.");

public sealed class GroundworkDesignOperationRejectedException(string operationKind, string operationKey)
    : InvalidOperationException($"Groundwork rejected design operation '{operationKind}' with key '{operationKey}' and rolled it back.");

public sealed class GroundworkDesignOperationMarkerRaceException() : InvalidOperationException;
public sealed class GroundworkDesignUncertainCommitException(string message, Exception? inner = null) : InvalidOperationException(message, inner);
public sealed class GroundworkDesignWriteProviderException(string message, Exception inner) : InvalidOperationException(message, inner);
public sealed class GroundworkDesignSerializationException(string message, Exception inner) : InvalidOperationException(message, inner);
public sealed class GroundworkDesignCorruptResultException(string message, Exception? inner = null) : InvalidOperationException(message, inner);
public sealed class GroundworkDesignCorruptMarkerException(string message, Exception? inner = null) : InvalidOperationException(message, inner);
