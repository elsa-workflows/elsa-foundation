using System.Collections.Immutable;
using Elsa.Cli.Worker;
using Elsa.Modularity.Planning.Bridge;
using Elsa.Modularity.Planning.Catalog;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;

namespace Elsa.Cli;

/// <summary>Owns one frozen source/input context and the candidate built from those exact inputs.</summary>
/// <remarks>No entry point accepts an independently built candidate or caller-supplied capture identity.</remarks>
public sealed class CompositionInspectionCapture
{
    private readonly CompositionFileSource _source;
    private readonly CompositionInputSnapshot _inputs;

    private CompositionInspectionCapture(CompositionFileSource source, CompositionInputSnapshot inputs,
        SelectionPlan plan, WorkerCandidatePayload payload)
    {
        _source = source;
        _inputs = inputs;
        Plan = plan;
        Payload = payload;
    }

    public SelectionPlan Plan { get; }

    /// <summary>Private transport payload; never render its file contents or correlation identities.</summary>
    public WorkerCandidatePayload Payload { get; }

    /// <summary>Captures, parses and builds once without publishing files or loading a host.</summary>
    /// <exception cref="CliRefusal">Captured inputs or accepted selection are unreadable, invalid or excessive.</exception>
    public static CompositionInspectionCapture Open(string hostDirectory, string shell, string environment,
        string compositionPath, string? catalogPath = null, string? reviewPath = null,
        IReadOnlyList<string>? workspaceProfilePaths = null, CompositionFileReader? reader = null)
    {
        var profilePaths = workspaceProfilePaths ?? [];
        if (profilePaths.Count > CompositionFileReader.MaximumFiles)
            throw CompositionFileReader.LimitExceeded();
        var source = CompositionFileSource.OpenForCandidate(hostDirectory, shell, environment, reader);
        var paths = new List<string> { compositionPath };
        if (catalogPath is not null)
            paths.Add(catalogPath);
        if (reviewPath is not null)
            paths.Add(reviewPath);
        paths.AddRange(profilePaths);
        var inputs = CompositionInputSnapshot.OpenForCandidate(paths, reader);

        try
        {
            var authored = SelectionJsonReader.ParseComposition(inputs.ReadText(compositionPath));
            if (!WorkerContract.IsValidCandidateSelection(authored.Accepted.FeatureIds, authored.Remove))
                throw CliRefusal.Usage("candidate-capture-invalid", "The candidate selection is not valid for inspection.");
            var catalog = catalogPath is null
                ? FoundationSelectionCatalog.LoadFor(authored.Catalog)
                : SelectionJsonReader.ParseCatalog(inputs.ReadText(catalogPath));
            var review = reviewPath is null ? null : SettingReviewReader.Parse(inputs.ReadText(reviewPath));
            var profiles = profilePaths.Select(path => SelectionJsonReader.ParseWorkspaceProfile(inputs.ReadText(path))).ToArray();
            var candidate = CompositionCandidateBuilder.Build(source.Snapshot, catalog, authored, review, profiles);

            var captureId = Guid.NewGuid().ToString("N");
            var selection = source.Snapshot.Selection;
            var names = new List<string> { "appsettings.json", "shells.json", selection.ShellOverlayFileName };
            if (selection.AppsettingsOverlayFileName is { } overlay)
                names.Add(overlay);
            var files = ImmutableArray.CreateBuilder<WorkerCandidateFile>(names.Count);
            foreach (var name in names.Order(StringComparer.Ordinal))
            {
                var bytes = candidate.Files[name];
                if (bytes.Length > CompositionFileReader.MaximumFileBytes)
                    throw CompositionFileReader.LimitExceeded();
                files.Add(new WorkerCandidateFile { Name = name, CaptureId = captureId, Content = Convert.ToBase64String(bytes) });
            }
            var payload = new WorkerCandidatePayload
            {
                Version = 1,
                Source = "captured-workbench-json-v1",
                InvocationId = Guid.NewGuid().ToString("N"),
                CaptureId = captureId,
                Shell = selection.ShellId,
                Environment = selection.Environment,
                AcceptedFeatureIds = authored.Accepted.FeatureIds.Order(StringComparer.Ordinal).ToImmutableArray(),
                RemovedFeatureIds = authored.Remove.Order(StringComparer.Ordinal).ToImmutableArray(),
                Files = files.MoveToImmutable()
            };
            return new CompositionInspectionCapture(source, inputs, candidate.Plan, payload);
        }
        catch (SelectionDocumentException exception)
        {
            throw CliRefusal.Usage(exception.Code, "A supplied composition document is invalid.");
        }
        catch (CompositionImportException exception)
        {
            throw CliRefusal.Usage(exception.Code, "The accepted candidate could not be built from the captured inputs.");
        }
    }

    /// <summary>Rechecks all owned source and intent files before launch or output.</summary>
    public void VerifyUnchanged()
    {
        _source.VerifyUnchanged();
        _inputs.VerifyUnchanged();
    }
}
