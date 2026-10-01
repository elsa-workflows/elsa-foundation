using System.Collections.Immutable;
using Elsa.Cli.Worker;
using Elsa.Modularity.Planning.Bridge;
using Elsa.Modularity.Planning.Catalog;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;

namespace Elsa.Cli;

/// <summary>Owns one frozen source/input context and the candidate built from those exact inputs.</summary>
/// <remarks>No entry point accepts an independently built candidate or caller-supplied capture identity.</remarks>
public sealed class CompositionInspectionCapture : IDisposable
{
    private readonly CompositionFileSource _source;
    private readonly CompositionInputSnapshot _inputs;
    private readonly HostLayout? _host;
    private readonly string? _environmentInputPath;
    private readonly object _lifecycleGate = new();
    private bool _environmentInspectionBegun;
    private bool _disposed;

    private CompositionInspectionCapture(CompositionFileSource source, CompositionInputSnapshot inputs,
        SelectionPlan plan, WorkerCandidatePayload payload, HostLayout? host, string? environmentInputPath)
    {
        _source = source;
        _inputs = inputs;
        _host = host;
        _environmentInputPath = environmentInputPath;
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
        return OpenCore(hostDirectory, shell, environment, compositionPath, catalogPath, reviewPath,
            workspaceProfilePaths, reader, host: null, environmentInputPath: null);
    }

    /// <summary>
    /// Captures one explicit private environment document alongside the file-only candidate. The supplied
    /// <paramref name="host"/> is the installed host closure; <paramref name="hostDirectory"/> remains the
    /// separately supplied Workbench source directory.
    /// </summary>
    public static CompositionInspectionCapture OpenWithEnvironmentInput(HostLayout host, string hostDirectory,
        string shell, string environment, string compositionPath, string environmentInputPath,
        string? catalogPath = null, string? reviewPath = null,
        IReadOnlyList<string>? workspaceProfilePaths = null, CompositionFileReader? reader = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        return OpenCore(hostDirectory, shell, environment, compositionPath, catalogPath, reviewPath,
            workspaceProfilePaths, reader, host, environmentInputPath);
    }

    private static CompositionInspectionCapture OpenCore(string hostDirectory, string shell, string environment,
        string compositionPath, string? catalogPath, string? reviewPath,
        IReadOnlyList<string>? workspaceProfilePaths, CompositionFileReader? reader, HostLayout? host,
        string? environmentInputPath)
    {
        var profilePaths = workspaceProfilePaths is null ? Array.Empty<string>() : workspaceProfilePaths.ToArray();
        if (profilePaths.Length > CompositionFileReader.MaximumFiles)
            throw CompositionFileReader.LimitExceeded();
        var capturedEnvironmentPath = environmentInputPath is null
            ? null
            : CompositionInputSnapshot.NormalizePath(environmentInputPath);
        var source = CompositionFileSource.OpenForCandidate(hostDirectory, shell, environment, reader);
        var paths = new List<string> { compositionPath };
        if (catalogPath is not null)
            paths.Add(catalogPath);
        if (reviewPath is not null)
            paths.Add(reviewPath);
        paths.AddRange(profilePaths);
        if (capturedEnvironmentPath is not null)
            paths.Add(capturedEnvironmentPath);
        var inputs = capturedEnvironmentPath is null
            ? CompositionInputSnapshot.OpenForCandidate(paths, reader)
            : CompositionInputSnapshot.OpenForCandidateWithEnvironmentInput(paths, capturedEnvironmentPath, reader);
        var ownsInputs = true;

        try
        {
            if (capturedEnvironmentPath is not null)
            {
                var raw = inputs.ReadBytes(capturedEnvironmentPath);
                try
                {
                    _ = ExplicitEnvironmentInput.Parse(raw);
                }
                finally
                {
                    Array.Clear(raw);
                }
            }

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
            var capture = new CompositionInspectionCapture(source, inputs, candidate.Plan, payload, host, capturedEnvironmentPath);
            ownsInputs = false;
            return capture;
        }
        catch (SelectionDocumentException exception)
        {
            throw CliRefusal.Usage(exception.Code, "A supplied composition document is invalid.");
        }
        catch (CompositionImportException exception)
        {
            throw CliRefusal.Usage(exception.Code, "The accepted candidate could not be built from the captured inputs.");
        }
        finally
        {
            if (ownsInputs)
                inputs.Dispose();
        }
    }

    /// <summary>Whether this capture owns one admitted explicit environment document.</summary>
    public bool HasEnvironmentInput
    {
        get
        {
            lock (_lifecycleGate)
                return !_disposed && _environmentInputPath is not null;
        }
    }

    /// <summary>
    /// Creates the single-use additive worker request from this capture's frozen host, candidate and raw
    /// environment bytes. Package roots are copied before the request is returned.
    /// </summary>
    public CandidateEnvironmentWorkerRequestV2 BeginEnvironmentInspection(IReadOnlyList<string> packageRoots)
    {
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            if (_host is null || _environmentInputPath is null || _environmentInspectionBegun)
                throw CaptureInvalid();
            ArgumentNullException.ThrowIfNull(packageRoots);

            var roots = packageRoots.ToArray();
            if (roots.Any(root => root is null))
                throw CaptureInvalid();

            // An observed drift consumes this invocation even if a caller later restores the file.
            // Recovery requires a fresh capture with fresh correlation identities.
            _environmentInspectionBegun = true;
            VerifyUnchangedCore();
            var raw = _inputs.ReadBytes(_environmentInputPath);
            try
            {
                var content = Convert.ToBase64String(raw);
                return new CandidateEnvironmentWorkerRequestV2
                {
                    Version = WorkerContract.Version,
                    Command = WorkerCommands.InspectCandidateEnvironment,
                    HostDirectory = _host.Directory,
                    HostName = _host.Name,
                    DepsFile = _host.DepsFile,
                    PackageRoots = Array.AsReadOnly(roots),
                    Candidate = ClonePayload(Payload),
                    EnvironmentInput = new WorkerEnvironmentInput
                    {
                        Version = 1,
                        CaptureId = Payload.CaptureId,
                        Content = content
                    }
                };
            }
            finally
            {
                Array.Clear(raw);
            }
        }
    }

    /// <summary>Rechecks all owned source and intent files before launch or output.</summary>
    public void VerifyUnchanged()
    {
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            VerifyUnchangedCore();
        }
    }

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _inputs.Dispose();
        }
    }

    private void VerifyUnchangedCore()
    {
        try
        {
            _source.VerifyUnchanged();
            _inputs.VerifyUnchanged();
        }
        catch (CliRefusal) when (_environmentInputPath is not null)
        {
            // Invalidate and release private bytes after observed drift, including a prelaunch recheck.
            Dispose();
            throw;
        }
    }

    private static WorkerCandidatePayload ClonePayload(WorkerCandidatePayload payload) => new()
    {
        Version = payload.Version,
        Source = payload.Source,
        InvocationId = payload.InvocationId,
        CaptureId = payload.CaptureId,
        Shell = payload.Shell,
        Environment = payload.Environment,
        AcceptedFeatureIds = payload.AcceptedFeatureIds?.ToImmutableArray(),
        RemovedFeatureIds = payload.RemovedFeatureIds?.ToImmutableArray(),
        Files = payload.Files?.Select(file => new WorkerCandidateFile
        {
            Name = file.Name,
            CaptureId = file.CaptureId,
            Content = file.Content
        }).ToImmutableArray()
    };

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw CaptureInvalid();
    }

    private static CliRefusal CaptureInvalid() =>
        CliRefusal.Usage("candidate-capture-invalid", "The captured candidate is no longer available.");
}
