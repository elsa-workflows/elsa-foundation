using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Elsa.Cli.Worker;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;
using Elsa.Modularity.Planning.Services;

namespace Elsa.Cli;

/// <summary>Plans a supplied authored selection without loading a host or entering the persistence worker.</summary>
internal static class CompositionPlanCommand
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static Command Build()
    {
        var catalog = new Option<string>("--catalog") { Description = "Pinned selection catalog JSON.", Required = true };
        var composition = new Option<string>("--composition") { Description = "Authored composition JSON.", Required = true };
        var inventory = new Option<string>("--inventory") { Description = "Optional supplied host-inventory snapshot JSON." };
        var persistenceEvidence = new Option<string>("--persistence-evidence") { Description = "Optional supplied safe resource-reference JSON." };
        var workspaceProfiles = new Option<string[]>("--workspace-profile") { Description = "Optional workspace-profile JSON file. Repeatable." };
        var format = new Option<string>("--format") { Description = "Output format: text or json. Defaults to text." };
        var command = new Command("plan", "Show the candidate feature selection and supplied evidence without starting a host.")
        {
            catalog, composition, inventory, persistenceEvidence, workspaceProfiles, format
        };

        command.SetAction((result, cancellationToken) => Guarded(
            () => RunAsync(result, catalog, composition, inventory, persistenceEvidence, workspaceProfiles, format, cancellationToken),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        ParseResult result,
        Option<string> catalogOption,
        Option<string> compositionOption,
        Option<string> inventoryOption,
        Option<string> persistenceOption,
        Option<string[]> workspaceProfileOption,
        Option<string> formatOption,
        CancellationToken cancellationToken)
    {
        var outputFormat = result.GetValue(formatOption) ?? "text";
        if (outputFormat is not ("text" or "json"))
            throw CliRefusal.Usage("composition-format-invalid", "The output format must be text or json.");

        var catalog = SelectionJsonReader.ParseCatalog(ReadInput(result.GetRequiredValue(catalogOption)));
        var authored = SelectionJsonReader.ParseComposition(ReadInput(result.GetRequiredValue(compositionOption)));
        var profiles = (result.GetValue(workspaceProfileOption) ?? [])
            .Select(path => SelectionJsonReader.ParseWorkspaceProfile(ReadInput(path)))
            .ToArray();
        var suppliedInventory = result.GetValue(inventoryOption) is { } inventoryPath
            ? SelectionEvidenceJsonReader.ParseInventory(ReadInput(inventoryPath))
            : null;
        var suppliedPersistence = result.GetValue(persistenceOption) is { } persistencePath
            ? SelectionEvidenceJsonReader.ParseResourceHints(ReadInput(persistencePath))
            : null;

        cancellationToken.ThrowIfCancellationRequested();
        var plan = SelectionPlanner.Plan(catalog, authored, suppliedInventory, profiles, suppliedPersistence);
        var projection = Project(plan, suppliedInventory);
        var rendered = outputFormat == "json" ? JsonSerializer.Serialize(projection, s_jsonOptions) : RenderText(projection);
        Console.Out.WriteLine(rendered);
        return ToolExitCode.Success;
    }

    private static string ReadInput(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw CliRefusal.Resolution("composition-input-unreadable", "A supplied composition input could not be read.");
        }
    }

    private static CompositionPlanOutput Project(SelectionPlan plan, HostInventory? suppliedInventory)
    {
        static void RequireSafe(string? value)
        {
            if (!SelectionValueRules.IsSafeReference(value))
                throw CliRefusal.Usage("composition-output-unsafe", "The plan contains an unsafe identity and cannot be rendered.");
        }

        RequireSafe(plan.Catalog.Id);
        RequireSafe(plan.Catalog.Version);
        if (!SelectionValueRules.IsDigest(plan.Catalog.Digest))
            throw CliRefusal.Usage("composition-output-unsafe", "The plan contains an unsafe identity and cannot be rendered.");

        foreach (var id in plan.SelectedFeatureIds.Concat(plan.Accepted.FeatureIds))
            RequireSafe(id);

        var reasons = plan.Reasons
            .Select(reason =>
            {
                RequireSafe(reason.FeatureId);
                RequireSafe(reason.SourceKind);
                RequireSafe(reason.SourceId);
                if (reason.SourceVersion is not null)
                    RequireSafe(reason.SourceVersion);
                if (reason.Action is not ("selected" or "removed"))
                    throw CliRefusal.Usage("composition-output-unsafe", "The plan contains an unsupported reason and cannot be rendered.");
                return new ReasonOutput(reason.FeatureId, reason.Action, reason.SourceKind, reason.SourceId, reason.SourceVersion);
            })
            .OrderBy(value => value.FeatureId, StringComparer.Ordinal)
            .ThenBy(value => value.Action, StringComparer.Ordinal)
            .ThenBy(value => value.SourceKind, StringComparer.Ordinal)
            .ThenBy(value => value.SourceId, StringComparer.Ordinal)
            .ThenBy(value => value.SourceVersion, StringComparer.Ordinal)
            .ToArray();

        var dependencies = plan.DependencyEvidence
            .Select(edge =>
            {
                RequireSafe(edge.FeatureId);
                RequireSafe(edge.DependencyId);
                RequireSafe(edge.EvidenceSource);
                if (edge.Mode is not ("required" or "optional") ||
                    edge.EvidenceKind is not ("reviewed-definition" or "runtime-descriptor" or "package-manifest"))
                    throw CliRefusal.Usage("composition-output-unsafe", "The plan contains unsupported dependency evidence and cannot be rendered.");
                return new DependencyOutput(edge.FeatureId, edge.DependencyId, edge.Mode, edge.EvidenceKind, edge.EvidenceSource, edge.TargetSelected);
            })
            .OrderBy(value => value.FeatureId, StringComparer.Ordinal)
            .ThenBy(value => value.DependencyId, StringComparer.Ordinal)
            .ThenBy(value => value.Mode, StringComparer.Ordinal)
            .ThenBy(value => value.EvidenceKind, StringComparer.Ordinal)
            .ThenBy(value => value.EvidenceSource, StringComparer.Ordinal)
            .ToArray();

        var findings = plan.Findings
            .Select(finding =>
            {
                RequireSafe(finding.Code);
                RequireSafe(finding.Severity);
                RequireSafe(finding.EvidenceSource);
                if (finding.FeatureId is not null)
                    RequireSafe(finding.FeatureId);
                if (finding.DependencyId is not null)
                    RequireSafe(finding.DependencyId);
                if (finding.Severity is not ("unresolved" or "advisory"))
                    throw CliRefusal.Usage("composition-output-unsafe", "The plan contains an unsupported finding and cannot be rendered.");
                return new FindingOutput(
                    finding.Code,
                    finding.Severity,
                    finding.FeatureId,
                    finding.DependencyId,
                    finding.EvidenceSource,
                    FindingDescription(finding.Code));
            })
            .OrderBy(value => value.Code, StringComparer.Ordinal)
            .ThenBy(value => value.FeatureId, StringComparer.Ordinal)
            .ThenBy(value => value.DependencyId, StringComparer.Ordinal)
            .ThenBy(value => value.EvidenceSource, StringComparer.Ordinal)
            .ToArray();

        var locks = plan.ObservedLocks
            .Select(item =>
            {
                RequireSafe(item.FeatureId);
                RequireSafe(item.Kind);
                RequireSafe(item.EvidenceSource);
                if (item.PackageId is not null)
                    RequireSafe(item.PackageId);
                if (item.PackageVersion is not null)
                    RequireSafe(item.PackageVersion);
                if (item.ManifestDigest is not null && !SelectionValueRules.IsDigest(item.ManifestDigest))
                    throw CliRefusal.Usage("composition-output-unsafe", "The plan contains an unsafe package digest and cannot be rendered.");
                return new LockOutput(item.FeatureId, item.Kind, item.PackageId, item.PackageVersion, item.ManifestDigest, item.EvidenceSource);
            })
            .OrderBy(value => value.FeatureId, StringComparer.Ordinal)
            .ToArray();

        var inventory = plan.InventoryId is null
            ? null
            : new InventoryOutput(
                Safe(plan.InventoryId), Safe(plan.TargetId), plan.InventoryObservedAt,
                Safe(suppliedInventory?.Source));
        var persistence = plan.Persistence;
        if (persistence.Status != "unchecked")
            throw CliRefusal.Usage("composition-output-unsafe", "The supplied persistence evidence cannot establish a checked runtime binding.");
        var persistenceOutput = new PersistenceOutput(
            "unchecked",
            Safe(persistence.Provenance),
            persistence.ResourceReferences.Select(Safe).OrderBy(value => value, StringComparer.Ordinal).ToArray());

        return new CompositionPlanOutput(
            "1",
            "composition-plan",
            "supplied-files",
            new FeatureSetOutput(plan.SelectedFeatureIds.OrderBy(value => value, StringComparer.Ordinal).ToArray()),
            new FeatureSetOutput(plan.Accepted.FeatureIds.OrderBy(value => value, StringComparer.Ordinal).ToArray()),
            reasons,
            dependencies,
            findings,
            locks,
            new CatalogOutput(plan.Catalog.Id, plan.Catalog.Version, plan.Catalog.Digest),
            inventory,
            persistenceOutput);

        string Safe(string? value)
        {
            RequireSafe(value);
            return value!;
        }
    }

    private static string RenderText(CompositionPlanOutput plan)
    {
        var unresolved = plan.Findings.Count(item => item.Severity == "unresolved");
        var advisory = plan.Findings.Count(item => item.Severity == "advisory");
        var lines = new List<string>
        {
            $"Candidate: {FormatIds(plan.Candidate.FeatureIds)} ({plan.Candidate.FeatureIds.Count} exact IDs); accepted: {FormatIds(plan.Accepted.FeatureIds)} ({plan.Accepted.FeatureIds.Count} IDs)",
            $"Findings: {unresolved} unresolved, {advisory} advisory",
            "Selection reasons:"
        };

        lines.AddRange(plan.Reasons.Select(reason =>
            $"  {reason.FeatureId}: {reason.Action} via {reason.SourceKind} {reason.SourceId}{(reason.SourceVersion is null ? "" : "@" + reason.SourceVersion)}"));

        lines.Add("Dependency evidence:");
        lines.AddRange(plan.DependencyEvidence.Select(edge =>
            $"  {edge.FeatureId} -> {edge.DependencyId} ({edge.Mode}; {edge.EvidenceKind}; {edge.EvidenceSource}; {(edge.TargetSelected ? "selected" : "not selected")})"));
        if (plan.DependencyEvidence.Count == 0)
            lines.Add("  none supplied");

        lines.Add("Findings:");
        lines.AddRange(plan.Findings.Select(finding =>
            $"  {finding.Severity}: {finding.Code}{(finding.FeatureId is null ? "" : " [" + finding.FeatureId + "]")}: {finding.Explanation}"));
        if (plan.Findings.Count == 0)
            lines.Add("  none");

        if (plan.Inventory is { } inventory)
            lines.Add($"Inventory: {inventory.TargetId} from {inventory.Source} at {inventory.ObservedAt?.ToString("O", CultureInfo.InvariantCulture)} (supplied snapshot)");
        else
            lines.Add("Inventory: not supplied; unverified");
        lines.Add($"Persistence: {plan.Persistence.Status}; {plan.Persistence.ResourceReferences.Count} supplied resource reference(s); not verified");
        lines.Add("Live host readiness: not assessed");
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatIds(IReadOnlyCollection<string> ids) => ids.Count == 0 ? "(none)" : string.Join(", ", ids);

    private static string FindingDescription(string code) => code switch
    {
        "inventory-unverified" => "No target-host inventory was supplied.",
        "feature-unknown" => "The target inventory does not establish this feature's availability.",
        "package-absent" => "The supplied inventory reports this package absent.",
        "manifest-unreadable" => "The supplied inventory could not read this package manifest.",
        "package-identity-missing" => "The supplied inventory has no package lock or host-bundled marker.",
        "package-incompatible" => "The supplied inventory reports this package incompatible with the target.",
        "compatibility-unknown" => "Package compatibility is unknown in the supplied inventory.",
        "descriptor-manifest-divergence" => "Runtime descriptor and manifest dependency evidence differ; the descriptor governs for a loaded feature.",
        "optional-companion" => "The supplied package manifest identifies an optional companion.",
        "required-dependency-missing" => "A supplied dependency observation names a required feature absent from the candidate.",
        "dependency-evidence-unavailable" => "The supplied inventory has no readable dependency evidence for this feature.",
        "catalog-pin-unresolved" => "The supplied catalog does not match the authored catalog pin.",
        "accepted-pin-unresolved" => "The accepted selection references a different catalog digest.",
        "candidate-re-resolution" => "The candidate differs from the previously accepted feature set.",
        "persistence-unverified" => "Provider, connection, schema, migration and physical layout have not been verified.",
        "definition-pin-unresolved" => "A pinned definition does not match a supplied immutable snapshot.",
        _ => "The planner reported a finding that requires review."
    };

    private static async Task<int> Guarded(Func<Task<int>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await action();
        }
        catch (CliRefusal refusal)
        {
            Report.WriteRefusal(Console.Error, refusal.Code, refusal.Message, refusal.Details);
            return refusal.ExitCode;
        }
        catch (SelectionDocumentException exception)
        {
            var code = SelectionValueRules.IsSafeReference(exception.Code) ? exception.Code : "composition-input-invalid";
            Report.WriteRefusal(Console.Error, code, "A supplied composition document is invalid.", []);
            return ToolExitCode.Refusal;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine("cancelled.");
            return ToolExitCode.Refusal;
        }
        catch (Exception)
        {
            Report.WriteRefusal(Console.Error, "composition-plan-failed", "The composition plan could not be produced.", []);
            return ToolExitCode.ResolutionFailure;
        }
    }

    private sealed record CompositionPlanOutput(
        string SchemaVersion,
        string Kind,
        string EvidenceScope,
        FeatureSetOutput Candidate,
        FeatureSetOutput Accepted,
        IReadOnlyList<ReasonOutput> Reasons,
        IReadOnlyList<DependencyOutput> DependencyEvidence,
        IReadOnlyList<FindingOutput> Findings,
        IReadOnlyList<LockOutput> ObservedLocks,
        CatalogOutput Catalog,
        InventoryOutput? Inventory,
        PersistenceOutput Persistence);

    private sealed record FeatureSetOutput(IReadOnlyList<string> FeatureIds)
    {
        public int Count => FeatureIds.Count;
    }

    private sealed record ReasonOutput(string FeatureId, string Action, string SourceKind, string SourceId, string? SourceVersion);
    private sealed record DependencyOutput(string FeatureId, string DependencyId, string Mode, string EvidenceKind, string EvidenceSource, bool TargetSelected);
    private sealed record FindingOutput(string Code, string Severity, string? FeatureId, string? DependencyId, string EvidenceSource, string Explanation);
    private sealed record LockOutput(string FeatureId, string Kind, string? PackageId, string? PackageVersion, string? ManifestDigest, string EvidenceSource);
    private sealed record CatalogOutput(string Id, string Version, string Digest);
    private sealed record InventoryOutput(string? InventoryId, string? TargetId, DateTimeOffset? ObservedAt, string Source);
    private sealed record PersistenceOutput(string Status, string Provenance, IReadOnlyList<string> ResourceReferences);
}
