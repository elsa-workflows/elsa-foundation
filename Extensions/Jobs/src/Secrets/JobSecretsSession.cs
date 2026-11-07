using System.Globalization;
using System.Text.Json;
using Elsa.Jobs.Core;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Jobs.Secrets;

internal sealed class JobSecretsSession(JobSecretCatalog catalog, IServiceProvider services) : IJobDataPolicy, IJobSecrets, IDisposable
{
    private readonly object _gate = new();
    private readonly HashSet<string> _values = new(StringComparer.Ordinal);
    private JobExecutionContext? _context;
    private JobSecretInspection? _inspection;
    private bool _disposed;
    public void ValidateAdmission(JobScope scope, JobRequest request) => _ = catalog.Inspect(scope, request);
    public void BeginInvocation(JobExecutionContext context, JobRequest request)
    {
        var inspection = catalog.Inspect(context.Scope, request);
        lock (_gate)
        {
            if (_disposed || _context is not null || context.Handler != request.Handler) throw new JobDataPolicyException();
            _context = context; _inspection = inspection;
            foreach (var literal in inspection.ProtectedValues) Track(literal);
        }
    }
    public async ValueTask<string> ResolveAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        SecretReference normalized; string tenant;
        try { normalized = JobSecretCatalog.Normalize(reference); }
        catch (Exception error) when (error is ArgumentException or NullReferenceException) { throw new JobDataPolicyException(); }
        lock (_gate)
        {
            if (_disposed || _context is null || _inspection?.Declaration is null || !_inspection.References.Contains(normalized)
                || string.IsNullOrWhiteSpace(_context.Scope.TenantId)) throw new JobDataPolicyException();
            tenant = _context.Scope.TenantId;
        }
        ResolvedSecret resolved;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolver = services.GetService<ISecretValueResolver>() ?? throw new JobDataPolicyException();
            resolved = await resolver.ResolveAsync(tenant, normalized, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        catch (Exception) { throw new JobDataPolicyException(); }
        lock (_gate)
        {
            if (_disposed || _context?.Scope.TenantId != tenant) throw new JobDataPolicyException();
            if (resolved is { Succeeded: false, FailureCode: SecretResolutionFailureCode.StoreUnavailable }) throw new JobDataUnavailableException();
            if (resolved is not { Succeeded: true, FailureCode: SecretResolutionFailureCode.None, Value: { } value, Metadata: { } metadata }
                || !StringComparer.Ordinal.Equals(metadata.TenantId, tenant) || metadata.Status != SecretStatus.Active
                || !StringComparer.OrdinalIgnoreCase.Equals(metadata.Name, normalized.Name)
                || normalized.TypeName is not null && !StringComparer.OrdinalIgnoreCase.Equals(metadata.TypeName, normalized.TypeName)
                || normalized.Scope is not null && !StringComparer.OrdinalIgnoreCase.Equals(metadata.Scope, normalized.Scope)
                || metadata.ExpiresAt is { } expires && expires <= (services.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow()) throw new JobDataPolicyException();
            Track(value);
            return value;
        }
    }
    public void ValidateProgress(JobExecutionContext context, JobProgress progress)
    {
        lock (_gate)
        {
            Ensure(context);
            if (_inspection?.Declaration is null) return;
            if (progress is null) throw new JobDataPolicyException();
            Check(progress.Message);
            if (progress.Fraction is { } fraction) Check(fraction.ToString("R", CultureInfo.InvariantCulture));
            CheckPayload(progress.Data);
        }
    }
    public void ValidateResult(JobExecutionContext context, JobPayload? payload)
    {
        lock (_gate) { Ensure(context); if (_inspection?.Declaration is not null) CheckPayload(payload); }
    }
    private void Ensure(JobExecutionContext context) { if (_disposed || !ReferenceEquals(_context, context)) throw new JobDataPolicyException(); }
    private void Track(string value)
    {
        if (value.Length == 0) return;
        if (value.Length > 65536 || !_values.Contains(value) && _values.Count >= 128) throw new JobDataPolicyException();
        _values.Add(value);
    }
    private void Check(string? value)
    {
        if (value is not null && _values.Any(literal => value.Contains(literal, StringComparison.Ordinal))) throw new JobDataPolicyException();
    }
    private void CheckPayload(JobPayload? payload)
    {
        if (payload is null) return;
        Check(payload.Schema.Alias); CheckJson(payload.Value, 0);
    }
    private void CheckJson(JsonElement value, int depth)
    {
        if (depth > 32) throw new JobDataPolicyException();
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject()) { Check(property.Name); CheckJson(property.Value, depth + 1); }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) CheckJson(item, depth + 1);
        else foreach (var scalar in JobSecretCatalog.Scalar(value)) Check(scalar);
    }
    public void Dispose()
    {
        lock (_gate) { _disposed = true; _context = null; _inspection = null; _values.Clear(); }
    }
}
