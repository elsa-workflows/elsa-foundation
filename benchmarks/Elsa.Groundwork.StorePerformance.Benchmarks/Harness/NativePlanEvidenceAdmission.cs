using System.Text.Json;
using System.Text.RegularExpressions;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

/// <summary>
/// Pre-flight admission of a captured native-plan evidence directory before the correctness or
/// measurement phase spends provider time on it (issue #1593). This is the only implementation of these
/// rules; the operator runner (<c>tools/groundwork/run-e3-medium-baseline.py</c>) invokes it through the
/// AdapterHost <c>admit-evidence</c> command instead of re-implementing them. It reads the document the
/// capture phase wrote, binds it to the current request's provenance, accounts for every required route
/// as captured or blocked, refuses blocked or incomplete diagnostics evidence for phases that need
/// complete provider-native routes, and checks every retained raw plan against its digest.
/// </summary>
public static class NativePlanEvidenceAdmission
{
    private static readonly Regex LowerSha256 = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    public static NativePlanEvidenceDocument ValidateCaptured(
        PerformanceWorkload workload,
        RunRequest request,
        string evidenceDirectory,
        BenchmarkPhase phase,
        bool requireComplete)
    {
        ArgumentNullException.ThrowIfNull(workload);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceDirectory);
        if (phase is not (BenchmarkPhase.Correctness or BenchmarkPhase.Measurement))
            throw new PerformanceContractException($"Captured evidence is admitted for correctness or measurement, not '{phase}'.");

        var path = ArtifactStore.EvidencePath(evidenceDirectory, request.NativePlanEvidenceReference);
        var name = Path.GetFileName(path);
        if (!File.Exists(path))
            throw new PerformanceContractException($"Native-plan evidence {name} is missing from {evidenceDirectory}.");
        NativePlanEvidenceDocument document;
        try
        {
            document = JsonSerializer.Deserialize<NativePlanEvidenceDocument>(File.ReadAllText(path), ArtifactStore.JsonOptions)
                ?? throw new PerformanceContractException($"{name} is empty.");
        }
        catch (JsonException exception)
        {
            throw new PerformanceContractException($"{name} is not a valid native-plan evidence document: {exception.Message}");
        }

        RequireProvenance(document, request, name);

        var routes = document.Routes ?? throw new PerformanceContractException($"{name} must contain a Routes array.");
        if (routes.Any(route => route is null || string.IsNullOrEmpty(route.RouteIdentity)))
            throw new PerformanceContractException($"{name} contains an invalid native route entry.");
        var blocked = document.BlockedRoutes ?? [];
        if (blocked.Any(string.IsNullOrEmpty))
            throw new PerformanceContractException($"{name} contains an invalid blocked route identity.");
        if (document.RouteContract == "provider-native-routes" && blocked.Count != 0)
            throw new PerformanceContractException($"{name} claims complete provider-native evidence but declares blocked routes.");
        var diagnosticsGroundwork = request.WorkloadId == DiagnosticsDurableHistoryWorkload.WorkloadId &&
                                    request.Adapter == DiagnosticsNativePlanContract.GroundworkAdapter;
        if (diagnosticsGroundwork)
        {
            if (document.RouteContract is not ("provider-native-routes" or DiagnosticsNativePlanContract.BlockedRouteContract))
                throw new PerformanceContractException($"{name} has an unknown diagnostics route contract.");
            if (document.RouteContract == DiagnosticsNativePlanContract.BlockedRouteContract && blocked.Count == 0)
                throw new PerformanceContractException($"{name} claims a blocked route contract without blocked routes.");
        }

        var constituents = document.TraceDetailConstituents ?? [];
        var admitted = routes.Select(route => route.RouteIdentity).Concat(constituents.Count == 0 ? [] : ["trace-detail"]).ToList();
        var required = workload.RequiredNativeRoutes.Order(StringComparer.Ordinal).ToArray();
        var timing = phase == BenchmarkPhase.Measurement;
        if (timing && !admitted.Order(StringComparer.Ordinal).SequenceEqual(required, StringComparer.Ordinal))
            throw new PerformanceContractException($"{name} does not capture every required native route for timing.");
        var accounted = admitted.Concat(blocked).ToList();
        if (!accounted.Order(StringComparer.Ordinal).SequenceEqual(required, StringComparer.Ordinal))
            throw new PerformanceContractException($"{name} does not account for every required route as captured or blocked.");
        if (accounted.Count != accounted.Distinct(StringComparer.Ordinal).Count())
            throw new PerformanceContractException($"{name} contains duplicate captured/blocked route identities.");
        if ((timing || requireComplete) && diagnosticsGroundwork &&
            (document.RouteContract != "provider-native-routes" || blocked.Count != 0))
            throw new PerformanceContractException(
                $"{name} contains blocked or incomplete provider-native diagnostics evidence; the workflow cannot promote it to correctness or measurement.");

        var references = new List<string>();
        foreach (var route in routes)
        {
            if (ArtifactAdmission.IsStructuredEvidenceRoute(request, route))
            {
                // Mirrors ArtifactAdmission.InvalidOptionalRawPlanPair: the migrated SQLite structured-log
                // routes may omit the raw plan only as a paired-empty reference and digest backed by typed
                // structured execution evidence.
                if (route.RawPlanReference is null || route.RawPlanSha256 is null)
                    throw new PerformanceContractException($"{name} route {route.RouteIdentity} must carry raw-plan reference and digest fields.");
                if ((route.RawPlanReference.Length != 0) != (route.RawPlanSha256.Length != 0))
                    throw new PerformanceContractException($"{name} route {route.RouteIdentity} must provide both optional raw-plan reference and digest, or neither.");
                if (route.RawPlanReference.Length == 0)
                {
                    if (route.StructuredEvidence is null)
                        throw new PerformanceContractException($"{name} route {route.RouteIdentity} cannot omit its raw plan without structured execution evidence.");
                    continue;
                }
            }
            references.Add(RequireRawPlan(evidenceDirectory, name, route.RawPlanReference, route.RawPlanSha256, "raw-plan"));
        }

        if (constituents.Count != 0)
        {
            var specifications = DiagnosticsNativePlanContract.TraceDetailConstituents(request.Adapter);
            var pointReads = specifications
                .Where(specification => specification.OperationKind == DiagnosticsTraceDetailOperationKind.PrimaryKeyRead)
                .Select(specification => specification.RouteIdentity)
                .ToHashSet(StringComparer.Ordinal);
            var expectedScopePredicate = DiagnosticsNativePlanContract.ExpectedStorageScopePredicate(request.Provider, storageScopeRequired: true);
            var names = new List<string>();
            foreach (var constituent in constituents)
            {
                if (constituent is null ||
                    string.IsNullOrEmpty(constituent.RouteIdentity) ||
                    constituent.RawPlanReference is null ||
                    constituent.RawPlanSha256 is null ||
                    string.IsNullOrWhiteSpace(constituent.PlanClassification) ||
                    constituent.PhysicalIndexName is null ||
                    string.IsNullOrWhiteSpace(constituent.CommandText))
                    throw new PerformanceContractException($"{name} contains an invalid trace-detail constituent entry.");
                if (constituent.PhysicalCardinality <= 0 || constituent.FiniteLimit <= 0 || constituent.PublicRowBound <= 0 ||
                    constituent.MaterializedCandidateCount <= 0 || constituent.ObservedCommandCount <= 0 || constituent.MaxInvocationCount <= 0)
                    throw new PerformanceContractException($"{name} contains invalid trace-detail constituent bounds or predicates.");
                // Relational providers inject a synthetic __groundwork_scope equality; MongoDB isolates scopes
                // with a provider-owned physical collection and must not expose one.
                if (constituent.HasStorageScopePredicate != expectedScopePredicate || !constituent.HasRoutePredicate)
                    throw new PerformanceContractException($"{name} contains a trace-detail constituent without required predicates.");
                names.Add(constituent.RouteIdentity);
                var pages = constituent.Pages ?? [];
                var pointRead = pointReads.Contains(constituent.RouteIdentity);
                if (pointRead)
                {
                    if (constituent.PlanClassification != "primary-key-read" || constituent.PhysicalIndexName.Length != 0)
                        throw new PerformanceContractException($"{name} contains an invalid trace-detail point-read classification.");
                    if (constituent.RawPlanReference.Length != 0 || constituent.RawPlanSha256.Length != 0 || pages.Count != 0)
                        throw new PerformanceContractException($"{name} contains a trace-detail point read with an explain artifact or continuation page.");
                }
                else
                {
                    if (constituent.PlanClassification != "index-search" || string.IsNullOrWhiteSpace(constituent.PhysicalIndexName))
                        throw new PerformanceContractException($"{name} contains an unsafe or undigested trace-detail raw-plan reference.");
                    references.Add(RequireRawPlan(evidenceDirectory, name, constituent.RawPlanReference, constituent.RawPlanSha256, "trace-detail raw-plan"));
                }
                var indices = new List<int>();
                foreach (var page in pages)
                {
                    if (page is null || page.PageIndex <= 0 || string.IsNullOrWhiteSpace(page.CommandText))
                        throw new PerformanceContractException($"{name} contains an invalid trace-detail continuation page entry.");
                    references.Add(RequireRawPlan(evidenceDirectory, name, page.RawPlanReference, page.RawPlanSha256, $"trace-detail page {page.PageIndex} raw-plan"));
                    indices.Add(page.PageIndex);
                }
                var expectedPages = pointRead ? 1 : (constituent.PublicRowBound + constituent.FiniteLimit - 1) / constituent.FiniteLimit;
                if (!indices.SequenceEqual(Enumerable.Range(1, Math.Max(expectedPages - 1, 0))))
                    throw new PerformanceContractException($"{name} contains non-sequential trace-detail continuation page indexes.");
            }
            if (!names.Order(StringComparer.Ordinal).SequenceEqual(
                    specifications.Select(specification => specification.RouteIdentity).Order(StringComparer.Ordinal),
                    StringComparer.Ordinal))
                throw new PerformanceContractException($"{name} does not account for every trace-detail constituent exactly once.");
        }

        if (references.Count != references.Distinct(StringComparer.Ordinal).Count())
            throw new PerformanceContractException($"{name} contains duplicate raw provider-plan references.");
        return document;
    }

    private static void RequireProvenance(NativePlanEvidenceDocument document, RunRequest request, string name)
    {
        var mismatches = new List<string>();
        void Check(string field, bool matches) { if (!matches) mismatches.Add(field); }
        Check("SchemaVersion", document.SchemaVersion == 2);
        Check("ComparisonCohortId", document.ComparisonCohortId == request.ComparisonCohortId);
        Check("MeasurementSetId", document.MeasurementSetId == request.MeasurementSetId);
        Check("WorkloadId", document.WorkloadId == request.WorkloadId);
        Check("WorkloadVersion", document.WorkloadVersion == request.WorkloadVersion);
        Check("Provider", document.Provider == request.Provider);
        Check("Adapter", document.Adapter == request.Adapter);
        Check("PhysicalForm", document.PhysicalForm == request.PhysicalForm);
        Check("Scale", document.Scale == request.Scale);
        Check("CommitSha", document.CommitSha == request.CommitSha);
        Check("HarnessAssemblySha256", document.HarnessAssemblySha256 == request.HarnessAssemblySha256);
        Check("CompositionFingerprint", document.CompositionFingerprint == request.CompositionFingerprint);
        Check("HostFingerprintSha256", document.HostFingerprintSha256 == request.HostFingerprintSha256);
        Check("ProviderVersion", document.ProviderVersion == request.ProviderVersion);
        Check("ProviderTopology", document.ProviderTopology == request.ProviderTopology);
        Check("ProviderConfiguration", document.ProviderConfiguration is not null && request.ProviderConfiguration is not null &&
            document.ProviderConfiguration.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .SequenceEqual(request.ProviderConfiguration.OrderBy(pair => pair.Key, StringComparer.Ordinal)));
        Check("Seed", document.Seed == request.Seed);
        Check("InputFingerprintSha256", document.InputFingerprintSha256 == request.InputFingerprintSha256);
        Check("Identity", document.Identity == request.NativePlanIdentity);
        if (mismatches.Count != 0)
            throw new PerformanceContractException($"{name} does not match current request provenance: {string.Join(", ", mismatches)}");
    }

    private static string RequireRawPlan(string evidenceDirectory, string name, string? reference, string? digest, string kind)
    {
        if (reference is null || !ArtifactStore.SafeRawPlanReference(reference) || digest is null || !LowerSha256.IsMatch(digest))
            throw new PerformanceContractException($"{name} contains an unsafe {kind} reference.");
        var path = ArtifactStore.RawPlanPath(evidenceDirectory, reference);
        if (!File.Exists(path) || ArtifactStore.HashFile(path) != digest)
            throw new PerformanceContractException($"{kind} {reference} is missing or does not match its digest.");
        return reference;
    }
}
