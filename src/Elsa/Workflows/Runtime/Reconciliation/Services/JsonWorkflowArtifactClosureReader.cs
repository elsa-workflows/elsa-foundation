using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Reconciliation.Services;

/// <summary>
/// Default <see cref="IWorkflowArtifactClosureReader"/>: reads the file text and decodes it into a
/// <see cref="WorkflowArtifactClosure"/> through the shared <see cref="IWorkflowArtifactClosureSerializer"/>,
/// then applies the fail-loud format gate.
/// </summary>
/// <remarks>
/// <para>
/// <b>The format gate rejects rather than adapts.</b> An unknown or newer <c>formatVersion</c> is refused outright:
/// there is no silent upcast in v1, and no partial import of the members a newer envelope happens to share with
/// this build's shape. Guessing at a format written by a producer this build has never seen would import
/// behaviour nobody authored — and because artifacts are content-addressed and the store is create-only, a wrong
/// guess would become that id's content permanently.
/// </para>
/// <para>
/// §2.23.5: every <c>IOException</c> and <c>JsonException</c> is wrapped in an
/// <see cref="InvalidWorkflowArtifactClosureException"/> carrying the offending path, with the original preserved
/// as the inner exception. This is the boundary that owns the file path, which is why the codec itself lets
/// <c>JsonException</c> through rather than wrapping it identifier-less.
/// </para>
/// </remarks>
public sealed class JsonWorkflowArtifactClosureReader(
    IWorkflowArtifactClosureSerializer closureSerializer,
    ILogger<JsonWorkflowArtifactClosureReader> logger) : IWorkflowArtifactClosureReader
{
    public WorkflowArtifactClosure Read(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new InvalidWorkflowArtifactClosureException(filePath ?? string.Empty, "no file path was configured.");

        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(filePath))
            throw new InvalidWorkflowArtifactClosureException(filePath, "the file does not exist.");

        string json;
        try
        {
            json = File.ReadAllText(filePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidWorkflowArtifactClosureException(filePath, "the file could not be read.", exception);
        }

        WorkflowArtifactClosure? closure;
        try
        {
            closure = closureSerializer.Deserialize(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidWorkflowArtifactClosureException(filePath, "the file is not a valid closure envelope.", exception);
        }

        if (closure is null)
            throw new InvalidWorkflowArtifactClosureException(filePath, "the file deserialized to null.");

        if (!WorkflowArtifactClosureFormat.IsSupported(closure.FormatVersion))
        {
            throw new InvalidWorkflowArtifactClosureException(
                filePath,
                $"format version {closure.FormatVersion} is not supported by this runtime "
                + $"(supported: {string.Join(", ", WorkflowArtifactClosureFormat.SupportedVersions)}).");
        }

        logger.LogDebug(
            "Read workflow artifact closure '{RootArtifactId}' with {Count} artifact(s) from '{FilePath}'.",
            closure.RootArtifactId,
            closure.Artifacts.Count,
            filePath);

        return closure;
    }
}
