using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using Elsa.Decisions.Core;
using Elsa.Decisions.Persistence.Core;

namespace Elsa.Decisions.Design;

/// <summary>Authorized decision authoring, publication and bounded synchronous case testing entry point.</summary>
public sealed class DecisionDesignService(IDecisionReader reader, IDecisionWriter writer, IDecisionCompiler compiler,
    IDecisionEvaluator evaluator, IDecisionAccessPolicy policy, DecisionDesignOptions options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> Statuses = ["Success", "NoMatch", "InvalidInput", "Unsupported", "HitPolicyViolation", "Unavailable"];

    public async ValueTask<IReadOnlyList<DecisionAssetView>> ListAsync(DecisionCaller caller, CancellationToken ct = default)
    {
        await Ensure(caller, DecisionPermissions.Read, null, ct);
        var result = new List<DecisionAssetView>();
        foreach (var asset in await reader.ListAssetsAsync(caller.TenantId, ct)) result.Add(await View(caller, asset, ct));
        return result;
    }
    public async ValueTask<DecisionAssetView> GetAsync(DecisionCaller caller, string id, CancellationToken ct = default)
    { await Ensure(caller, DecisionPermissions.Read, id, ct); return await View(caller, await Asset(caller, id, ct), ct); }

    public async ValueTask<DecisionAssetView> SaveAsync(DecisionCaller caller, string? id, SaveDecisionRequest request, CancellationToken ct = default)
    {
        await Ensure(caller, DecisionPermissions.Manage, id, ct);
        if (id is not null)
        {
            var existing = await Asset(caller, id, ct);
            await Ensure(caller, DecisionPermissions.Source, existing, ct);
            if (existing.IsSensitive) await Ensure(caller, DecisionPermissions.SensitiveValues, existing, ct);
        }
        if (request.IsSensitive) await Ensure(caller, DecisionPermissions.SensitiveValues, id, ct);
        ValidateXml(request.Xml, ct);
        var saved = await writer.SaveAssetAsync(Operation(caller, request.OperationId), new(id, request.ExpectedRevision,
            request.Name, request.Description, request.Xml, Hash(request.Xml), request.IsSensitive), ct);
        return await View(caller, await Asset(caller, saved.Id, ct), ct);
    }

    public async ValueTask<DecisionValidation> ValidateAsync(DecisionCaller caller, string id, ValidateDecisionRequest request, CancellationToken ct = default)
    {
        await Ensure(caller, DecisionPermissions.Manage, id, ct);
        var asset = await Asset(caller, id, ct); Revision(asset.Revision, request.ExpectedRevision);
        if (asset.IsSensitive) await Ensure(caller, DecisionPermissions.SensitiveValues, asset, ct);
        var result = compiler.Compile(asset.Xml, ct);
        return new(result.Success, result.Diagnostics, asset.SourceHash);
    }

    public async ValueTask<DecisionVersionView> PublishAsync(DecisionCaller caller, string id, PublishDecisionRequest request, CancellationToken ct = default)
    {
        await Ensure(caller, DecisionPermissions.Publish, id, ct); await Ensure(caller, DecisionPermissions.Read, id, ct);
        var operation = Operation(caller, request.OperationId);
        var fingerprint = Fingerprint(new { kind = "publish", command = new { AssetId = id, request.ExpectedRevision } });
        if (await reader.FindOperationAsync(operation, ct) is { } prior)
        {
            if (prior.Kind != "publish" || prior.Fingerprint != fingerprint) throw Conflict();
            return await VersionView(caller, await Version(caller, id, prior.Confirmation.Id, ct), ct);
        }
        var asset = await Asset(caller, id, ct);
        if (asset.IsSensitive) await Ensure(caller, DecisionPermissions.SensitiveValues, asset, ct);
        var compiled = compiler.Compile(asset.Xml, ct);
        if (!compiled.Success || compiled.Artifact is null) throw new DecisionDesignException("unsupported", "The decision cannot be published under the supported profile.");
        var saved = await writer.PublishAsync(Operation(caller, request.OperationId), new(id, request.ExpectedRevision, compiled.Artifact), ct);
        return await VersionView(caller, await Version(caller, id, saved.Id, ct), ct);
    }

    public async ValueTask<IReadOnlyList<DecisionVersionView>> VersionsAsync(DecisionCaller caller, string id, CancellationToken ct = default)
    {
        await Ensure(caller, DecisionPermissions.Read, id, ct); _ = await Asset(caller, id, ct);
        var result = new List<DecisionVersionView>();
        foreach (var version in await reader.ListVersionsAsync(caller.TenantId, id, ct)) result.Add(await VersionView(caller, version, ct));
        return result.OrderByDescending(v => v.Number).ToArray();
    }
    public async ValueTask<DecisionVersionView> GetVersionAsync(DecisionCaller caller, string id, string versionId, CancellationToken ct = default)
    { await Ensure(caller, DecisionPermissions.Read, id, ct); return await VersionView(caller, await Version(caller, id, versionId, ct), ct); }

    public async ValueTask<DecisionAssetView> CloneAsync(DecisionCaller caller, string id, CloneDecisionRequest request, CancellationToken ct = default)
    {
        await Ensure(caller, DecisionPermissions.Manage, id, ct); await Ensure(caller, DecisionPermissions.Read, id, ct); await Ensure(caller, DecisionPermissions.Source, id, ct);
        var asset = await Asset(caller, id, ct); var version = await Version(caller, id, request.VersionId, ct);
        if (asset.IsSensitive || version.IsSensitive) await Ensure(caller, DecisionPermissions.SensitiveValues, version, ct);
        return await SaveAsync(caller, id, new(request.OperationId, asset.Name, version.Xml, asset.Description, request.ExpectedRevision,
            asset.IsSensitive || version.IsSensitive), ct);
    }

    public async ValueTask<DecisionEvaluation> EvaluateAsync(DecisionCaller caller, string id, EvaluateDecisionRequest request, CancellationToken ct = default)
    {
        await Ensure(caller, DecisionPermissions.Evaluate, id, ct);
        var version = await Version(caller, id, request.VersionId, ct);
        if (version.IsSensitive) await Ensure(caller, DecisionPermissions.SensitiveValues, version, ct);
        if (request.Inputs is null || request.Inputs.Count > 32 || Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(request.Inputs, Json)) > 256 * 1024)
            throw new DecisionDesignException("invalid", "Decision inputs exceed their supported bound.");
        return evaluator.Evaluate(version.Artifact, new Dictionary<string, DecisionValue>(request.Inputs, StringComparer.Ordinal), request.Budget, ct);
    }

    public async ValueTask<string> ExportAsync(DecisionCaller caller, string id, string? versionId, CancellationToken ct = default)
    {
        await Ensure(caller, DecisionPermissions.Read, id, ct); await Ensure(caller, DecisionPermissions.Source, id, ct);
        if (versionId is not null)
        { var version = await Version(caller, id, versionId, ct); if (version.IsSensitive) await Ensure(caller, DecisionPermissions.SensitiveValues, version, ct); return version.Xml; }
        var asset = await Asset(caller, id, ct); if (asset.IsSensitive) await Ensure(caller, DecisionPermissions.SensitiveValues, asset, ct); return asset.Xml;
    }

    public async ValueTask<IReadOnlyList<DecisionCaseView>> CasesAsync(DecisionCaller caller, string id, CancellationToken ct = default)
    {
        await Ensure(caller, DecisionPermissions.Test, id, ct); await Ensure(caller, DecisionPermissions.Read, id, ct); _ = await Asset(caller, id, ct);
        var result = new List<DecisionCaseView>();
        foreach (var item in await reader.ListCasesAsync(caller.TenantId, id, ct)) result.Add(await CaseView(caller, item, ct));
        return result;
    }
    public async ValueTask<DecisionCaseView> SaveCaseAsync(DecisionCaller caller, string id, string? caseId, SaveDecisionCaseRequest request, CancellationToken ct = default)
    {
        await Ensure(caller, DecisionPermissions.Test, id, ct); await Ensure(caller, DecisionPermissions.Manage, id, ct);
        var asset = await Asset(caller, id, ct);
        var existing = caseId is null ? null : await reader.FindCaseAsync(caller.TenantId, id, caseId, ct) ?? throw Missing();
        if (asset.IsSensitive || existing?.IsSensitive == true || request.IsSensitive) await Ensure(caller, DecisionPermissions.SensitiveValues, id, ct);
        if (!Statuses.Contains(request.ExpectedStatus)) throw new DecisionDesignException("invalid", "The expected decision status is unsupported.");
        // Error outcomes carry a raw null result; successful and NoMatch outcomes use exact tagged values.
        if (request.ExpectedResult.ValueKind != JsonValueKind.Null || request.ExpectedStatus is "Success" or "NoMatch")
            ValidateExpected(request.ExpectedResult);
        var saved = await writer.SaveCaseAsync(Operation(caller, request.OperationId), new(id, caseId, request.ExpectedRevision, request.Name,
            request.Inputs, request.ExpectedStatus, request.ExpectedResult, asset.IsSensitive || request.IsSensitive), ct);
        return await CaseView(caller, await reader.FindCaseAsync(caller.TenantId, id, saved.Id, ct) ?? throw Missing(), ct);
    }
    public async ValueTask DeleteCaseAsync(DecisionCaller caller, string id, string caseId, string operationId, string expectedRevision, CancellationToken ct = default)
    {
        await Ensure(caller, DecisionPermissions.Test, id, ct); await Ensure(caller, DecisionPermissions.Manage, id, ct);
        _ = await writer.DeleteCaseAsync(Operation(caller, operationId), id, caseId, expectedRevision, ct);
    }

    public async ValueTask<DecisionTestReceipt> TestAsync(DecisionCaller caller, string id, TestDecisionRequest request, CancellationToken ct = default)
    {
        await Ensure(caller, DecisionPermissions.Test, id, ct); await Ensure(caller, DecisionPermissions.Read, id, ct);
        if (request.Cases is null || request.Cases.Count is < 1 or > 1000 || request.Cases.Any(x => x is null) || request.Cases.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != request.Cases.Count ||
            (request.VersionId is null) == (request.CandidateRevision is null)) throw new DecisionDesignException("invalid", "Choose one exact candidate and a bounded set of distinct cases.");
        var fingerprint = Fingerprint(new { assetId = id, request.CandidateHash, request.CandidateRevision, request.VersionId, request.Cases });
        var operation = Operation(caller, request.OperationId);
        if (await reader.FindCompletedOperationAsync(operation, id, ct) is { } prior)
        {
            if (prior.RequestFingerprint != fingerprint) throw Conflict();
            return await ReceiptView(caller, prior, ct);
        }
        var evaluatedAt = DateTimeOffset.UtcNow;
        DecisionArtifact artifact; bool sensitive; string candidateId;
        if (request.VersionId is { } versionId)
        {
            var version = await Version(caller, id, versionId, ct); Revision(version.SourceHash, request.CandidateHash);
            artifact = version.Artifact; sensitive = version.IsSensitive; candidateId = version.Id;
        }
        else
        {
            var asset = await Asset(caller, id, ct); Revision(asset.Revision, request.CandidateRevision!); Revision(asset.SourceHash, request.CandidateHash);
            var compilation = compiler.Compile(asset.Xml, ct);
            artifact = compilation.Success && compilation.Artifact is not null ? compilation.Artifact : throw new DecisionDesignException("unsupported", "The exact candidate cannot be evaluated.");
            sensitive = asset.IsSensitive; candidateId = asset.Revision;
        }
        var frozenCases = new List<DecisionCase>();
        foreach (var selected in request.Cases)
        {
            var item = await reader.FindCaseAsync(caller.TenantId, id, selected.Id, ct) ?? throw Conflict();
            Revision(item.Revision, selected.Revision); frozenCases.Add(item); sensitive |= item.IsSensitive;
        }
        if (sensitive) await Ensure(caller, DecisionPermissions.SensitiveValues, id, ct);
        var identities = new List<DecisionCaseIdentity>(); var comparisons = new List<DecisionCaseComparison>();
        var remainingOperations = 1_000_000;
        foreach (var item in frozenCases)
        {
            ct.ThrowIfCancellationRequested();
            identities.Add(new(item.Id, item.Revision, Fingerprint(item)));
            if (remainingOperations <= 0) throw new DecisionDesignException("budget", "The complete test exceeds its operation budget; no completed receipt was recorded.");
            var result = evaluator.Evaluate(artifact, item.Inputs, remainingOperations, ct);
            remainingOperations -= result.Operations;
            if (result.Status == "BudgetExceeded") throw new DecisionDesignException("budget", "The complete test exceeds its operation budget; no completed receipt was recorded.");
            var path = item.ExpectedStatus == result.Status ? Difference(item.ExpectedResult, result.Result, "$") : "$.status";
            comparisons.Add(new(item.Id, item.Name, path is null, result.Status, path, item.ExpectedResult.Clone(), result.Result.Clone(),
                result.Trace.Where(x => x.Matched).Select(x => x.RuleId).ToArray(),
                result.Trace.Where(x => x.Selected).Select(x => x.RuleId).ToArray(), result.Trace));
        }
        var runId = "run-" + Hash(JsonSerializer.Serialize(new[] { caller.TenantId, caller.SubjectId, request.OperationId }, Json));
        var transient = new DecisionTestReceipt(runId, id, caller.SubjectId, fingerprint, candidateId, request.CandidateHash, identities,
            comparisons, comparisons.All(x => x.Passed), true, sensitive, evaluatedAt, DateTimeOffset.UtcNow)
        { CaseInputs = frozenCases.ToDictionary(item => item.Id, item => (IReadOnlyDictionary<string, DecisionValue>)new Dictionary<string, DecisionValue>(item.Inputs, StringComparer.Ordinal), StringComparer.Ordinal) };
        var retainDetails = options.RetainDetailedTestHistory && await Allowed(caller, DecisionPermissions.SensitiveValues, id, ct);
        var retained = retainDetails ? transient : transient with
        { HasDetailedPayload = false, CaseInputs = null, Comparisons = comparisons.Select(Summary).ToArray() };
        var confirmation = await writer.SaveCompletedRunAsync(operation, retained, ct);
        if (confirmation.Replayed) return await ReceiptView(caller, await reader.FindRunAsync(caller.TenantId, id, confirmation.Id, ct) ?? throw Missing(), ct);
        return transient;
    }

    public async ValueTask<IReadOnlyList<DecisionTestReceipt>> RunsAsync(DecisionCaller caller, string id, CancellationToken ct = default)
    {
        await Ensure(caller, DecisionPermissions.History, id, ct); await Ensure(caller, DecisionPermissions.Read, id, ct);
        var result = new List<DecisionTestReceipt>();
        foreach (var run in await reader.ListRunsAsync(caller.TenantId, id, ct)) result.Add(await ReceiptView(caller, run, ct));
        return result.OrderByDescending(x => x.CompletedAt).ToArray();
    }
    public async ValueTask<DecisionTestReceipt> GetRunAsync(DecisionCaller caller, string id, string runId, CancellationToken ct = default)
    { await Ensure(caller, DecisionPermissions.History, id, ct); await Ensure(caller, DecisionPermissions.Read, id, ct); return await ReceiptView(caller, await reader.FindRunAsync(caller.TenantId, id, runId, ct) ?? throw Missing(), ct); }
    public async ValueTask DeleteRunAsync(DecisionCaller caller, string id, string runId, string operationId, CancellationToken ct = default)
    { await Ensure(caller, DecisionPermissions.History, id, ct); await Ensure(caller, DecisionPermissions.Manage, id, ct); _ = await writer.DeleteRunAsync(Operation(caller, operationId), id, runId, ct); }

    private async ValueTask<DecisionAssetView> View(DecisionCaller caller, DecisionAsset asset, CancellationToken ct)
    {
        var source = await Allowed(caller, DecisionPermissions.Source, asset, ct) && (!asset.IsSensitive || await Allowed(caller, DecisionPermissions.SensitiveValues, asset, ct));
        return new(asset.Id, asset.Name, asset.Description, asset.Revision, asset.SourceHash, source ? asset.Xml : null,
            asset.IsSensitive, asset.PublishedVersionId, asset.CreatedAt, asset.UpdatedAt);
    }
    private async ValueTask<DecisionVersionView> VersionView(DecisionCaller caller, DecisionVersion version, CancellationToken ct)
    {
        var source = await Allowed(caller, DecisionPermissions.Source, version, ct) && (!version.IsSensitive || await Allowed(caller, DecisionPermissions.SensitiveValues, version, ct));
        return new(version.Id, version.AssetId, version.Number, version.Name, version.SourceHash, version.Artifact.ContentHash,
            version.Artifact.ProfileId, version.Artifact.SemanticsId, source ? version.Xml : null, version.IsSensitive, version.PublishedAt);
    }
    private async ValueTask<DecisionCaseView> CaseView(DecisionCaller caller, DecisionCase item, CancellationToken ct)
    {
        var visible = !item.IsSensitive || await Allowed(caller, DecisionPermissions.SensitiveValues, item, ct);
        return new(item.Id, item.AssetId, item.Name, item.Revision, visible ? item.Inputs : null, item.ExpectedStatus,
            visible ? item.ExpectedResult : null, item.IsSensitive, item.CreatedAt, item.UpdatedAt);
    }
    private async ValueTask<DecisionTestReceipt> ReceiptView(DecisionCaller caller, DecisionTestReceipt run, CancellationToken ct)
    {
        var sensitiveAccess = await Allowed(caller, DecisionPermissions.SensitiveValues, run, ct);
        if (sensitiveAccess) return run;
        return run with
        {
            HasDetailedPayload = false, CaseInputs = null,
            Comparisons = run.Comparisons.Select(x => run.IsSensitive
                ? Summary(x) with { MatchedRules = [], SelectedRules = [], Trace = [] } : Summary(x)).ToArray()
        };
    }
    private static DecisionCaseComparison Summary(DecisionCaseComparison value) => value with { Expected = null, Actual = null };
    private async ValueTask<DecisionAsset> Asset(DecisionCaller caller, string id, CancellationToken ct) => await reader.FindAssetAsync(caller.TenantId, id, ct) ?? throw Missing();
    private async ValueTask<DecisionVersion> Version(DecisionCaller caller, string id, string versionId, CancellationToken ct) => await reader.FindVersionAsync(caller.TenantId, id, versionId, ct) ?? throw Missing();
    private ValueTask<bool> Allowed(DecisionCaller caller, string permission, object? resource, CancellationToken ct) => policy.IsAllowedAsync(caller, permission, resource, ct);
    private async ValueTask Ensure(DecisionCaller caller, string permission, object? resource, CancellationToken ct)
    { if (!await Allowed(caller, permission, resource, ct)) throw new DecisionDesignException("forbidden", "The requested decision operation is not permitted."); }
    private static DecisionOperation Operation(DecisionCaller caller, string operationId) => new(caller.TenantId, caller.SubjectId, operationId);
    private static void Revision(string actual, string expected) { if (actual != expected) throw Conflict(); }
    private static DecisionDesignException Conflict() => new("conflict", "The exact decision candidate, case revision or operation content is unavailable.");
    private static DecisionDesignException Missing() => new("not-found", "The decision resource is unavailable.");
    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static void ValidateXml(string xml, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(xml) || Encoding.UTF8.GetByteCount(xml) > 1024 * 1024) throw new DecisionDesignException("invalid", "The decision source exceeds its supported bound.");
        try
        {
            var parserXml = xml.Length > 0 && xml[0] == '\uFEFF' ? xml[1..] : xml;
            using var reader = XmlReader.Create(new StringReader(parserXml), new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
            var count = 0;
            while (reader.Read()) { ct.ThrowIfCancellationRequested(); if (reader.Depth > 64 || ++count > 100000) throw new DecisionDesignException("invalid", "The decision source exceeds its supported structural bound."); }
        }
        catch (XmlException ex) { throw new DecisionDesignException("invalid", "The decision source is not supported well-formed XML.", ex); }
    }
    private static string Fingerprint(object value)
    {
        var element = JsonSerializer.SerializeToElement(value, Json);
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) Canonical(element, writer);
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }
    private static void Canonical(JsonElement value, Utf8JsonWriter writer)
    {
        if (value.ValueKind == JsonValueKind.Object)
        { writer.WriteStartObject(); foreach (var property in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal)) { writer.WritePropertyName(property.Name); Canonical(property.Value, writer); } writer.WriteEndObject(); }
        else if (value.ValueKind == JsonValueKind.Array)
        { writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) Canonical(item, writer); writer.WriteEndArray(); }
        else value.WriteTo(writer);
    }
    private static string? Difference(JsonElement expected, JsonElement actual, string path)
    {
        if (expected.ValueKind != actual.ValueKind) return path;
        if (expected.ValueKind == JsonValueKind.Object)
        {
            if (expected.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == "number" && expected.TryGetProperty("value", out var number) &&
                actual.TryGetProperty("kind", out var actualKind) && actualKind.ValueKind == JsonValueKind.String && actualKind.GetString() == "number" && actual.TryGetProperty("value", out var actualNumber))
                return NormalizeNumber(number.GetString()!) == NormalizeNumber(actualNumber.GetString()!) ? null : path;
            var left = expected.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();
            var right = actual.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();
            if (!left.Select(x => x.Name).SequenceEqual(right.Select(x => x.Name))) return path;
            for (var index = 0; index < left.Length; index++) { var diff = Difference(left[index].Value, right[index].Value, path + "." + left[index].Name); if (diff is not null) return diff; }
            return null;
        }
        if (expected.ValueKind == JsonValueKind.Array)
        {
            if (expected.GetArrayLength() != actual.GetArrayLength()) return path;
            for (var index = 0; index < expected.GetArrayLength(); index++) { var diff = Difference(expected[index], actual[index], path + "[" + index + "]"); if (diff is not null) return diff; }
            return null;
        }
        return expected.GetRawText() == actual.GetRawText() ? null : path;
    }
    private static void ValidateExpected(JsonElement value, int depth = 0)
    {
        if (depth > 32) throw new DecisionDesignException("invalid", "The expected result exceeds its supported depth.");
        if (value.ValueKind == JsonValueKind.Array)
        {
            if (value.GetArrayLength() > 1000) throw new DecisionDesignException("invalid", "The expected result exceeds its supported size.");
            foreach (var item in value.EnumerateArray()) ValidateExpected(item, depth + 1);
            return;
        }
        if (value.ValueKind != JsonValueKind.Object) throw new DecisionDesignException("invalid", "Expected results use exact tagged decision values.");
        if (value.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String &&
            (kind.GetString() is "null" or "boolean" or "string" or "number") &&
            value.TryGetProperty("value", out var scalar) && value.EnumerateObject().Count() == 2)
        {
            var name = kind.GetString();
            var valid = name switch
            {
                "null" => scalar.ValueKind == JsonValueKind.Null,
                "boolean" => scalar.ValueKind == JsonValueKind.String && scalar.GetString() is "true" or "false",
                "string" => scalar.ValueKind == JsonValueKind.String && scalar.GetString()!.Length <= 4096,
                "number" => scalar.ValueKind == JsonValueKind.String && scalar.GetString()!.Length <= 256 &&
                    System.Text.RegularExpressions.Regex.IsMatch(scalar.GetString()!, @"^[+-]?[0-9]+(?:\.[0-9]+)?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
                _ => false
            };
            if (!valid) throw new DecisionDesignException("invalid", "An expected scalar is malformed or unsupported.");
            return;
        }
        if (value.EnumerateObject().Count() > 16) throw new DecisionDesignException("invalid", "The expected row has too many outputs.");
        foreach (var property in value.EnumerateObject()) ValidateExpected(property.Value, depth + 1);
    }
    private static string NormalizeNumber(string value)
    {
        var negative = value.StartsWith('-'); var body = value.TrimStart('+', '-'); var pieces = body.Split('.');
        var whole = pieces[0].TrimStart('0'); if (whole.Length == 0) whole = "0";
        var fraction = pieces.Length == 2 ? pieces[1].TrimEnd('0') : "";
        return (negative && (whole != "0" || fraction.Length > 0) ? "-" : "") + whole + (fraction.Length > 0 ? "." + fraction : "");
    }
}
