using System.Collections.Concurrent;
using System.Text.Json;
using CShells.Features;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Test-only qualification seam (spec 1193, story 6, SC-008). It is not product code and never ships.
// The driver selects this feature only for the workflow-library inventory, through the ordinary native feature apply,
// and deselects it afterwards. It wraps the host's actual IWorkflowLibraryCommands and reads one private mode file on
// every keyed create:
//   {"drop":"none"}          the actual commands answer, unchanged;
//   {"drop":"create-tag"}    a keyed tag creation is committed by the actual owner, then its response is lost once;
//   {"drop":"save-view"}     a keyed view creation is committed by the actual owner, then its response is lost once.
// Each loss is appended to the private log file (kind, operation key and the committed identity), so the driver can
// prove that the response really was lost after the commit and that the reread by key resolved it to exactly one
// resource. Every other call delegates unchanged. A missing, malformed or unknown mode is a fixture error.
[ShellFeature(name: "QualificationLibraryResponses", DependsOn = new object[] { "WorkflowsDesignApi" })]
public sealed class QualificationLibraryResponsesFeature : IShellFeature
{
    public string ModeFile { get; set; } = string.Empty;
    public string LogFile { get; set; } = string.Empty;

    public void ConfigureServices(IServiceCollection services)
    {
        var state = new QualificationLibraryDropState(ModeFile, LogFile);
        var native = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IWorkflowLibraryCommands) && !descriptor.IsKeyedService)
            ?? throw new InvalidOperationException("The qualification seam wraps the actual workflow library commands; none is registered.");
        // Exactly one command service stays registered, so the native single-registration checks still hold.
        services.RemoveAll<IWorkflowLibraryCommands>();
        services.AddScoped<IWorkflowLibraryCommands>(provider => new QualificationLibraryCommands(state, Resolve(provider, native)));
    }

    private static IWorkflowLibraryCommands Resolve(IServiceProvider provider, ServiceDescriptor native) =>
        native.ImplementationInstance as IWorkflowLibraryCommands
        ?? native.ImplementationFactory?.Invoke(provider) as IWorkflowLibraryCommands
        ?? (native.ImplementationType is { } type ? (IWorkflowLibraryCommands)ActivatorUtilities.CreateInstance(provider, type) : null)
        ?? throw new InvalidOperationException("The actual workflow library commands could not be constructed.");
}

sealed class QualificationLibraryCommands(QualificationLibraryDropState state, IWorkflowLibraryCommands inner) : IWorkflowLibraryCommands
{
    public ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryPermissionFlags>> GetPermissionsAsync(WorkflowLibraryActor actor, CancellationToken cancellationToken = default) =>
        inner.GetPermissionsAsync(actor, cancellationToken);
    public ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryOrganizationSnapshot>> GetOrganizationAsync(WorkflowLibraryActor actor, string definitionId, CancellationToken cancellationToken = default) =>
        inner.GetOrganizationAsync(actor, definitionId, cancellationToken);
    public ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryTagSnapshot>>> ListTagsAsync(WorkflowLibraryActor actor, string? search, CancellationToken cancellationToken = default) =>
        inner.ListTagsAsync(actor, search, cancellationToken);

    public async ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryTagSnapshot>> CreateTagAsync(WorkflowLibraryActor actor, WorkflowLibraryTagCreate input,
        string? operationKey, CancellationToken cancellationToken = default)
    {
        var result = await inner.CreateTagAsync(actor, input, operationKey, cancellationToken);
        if (operationKey is not null && result.Succeeded && result.Value is { } tag) state.MaybeDrop("create-tag", operationKey, tag.Id);
        return result;
    }

    public ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryTagSnapshot>> FindTagByOperationKeyAsync(WorkflowLibraryActor actor, string operationKey, CancellationToken cancellationToken = default) =>
        inner.FindTagByOperationKeyAsync(actor, operationKey, cancellationToken);
    public ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryTargetOutcome>>> EditTagsAsync(WorkflowLibraryActor actor, WorkflowLibraryTagEdit edit, CancellationToken cancellationToken = default) =>
        inner.EditTagsAsync(actor, edit, cancellationToken);
    public ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryTargetOutcome>>> SetFavoritesAsync(WorkflowLibraryActor actor, IReadOnlyList<string> definitionIds, bool favorite, CancellationToken cancellationToken = default) =>
        inner.SetFavoritesAsync(actor, definitionIds, favorite, cancellationToken);
    public ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryTargetOutcome>>> MoveDefinitionsAsync(WorkflowLibraryActor actor, IReadOnlyList<string> definitionIds, string? folderId, string? operationKey, CancellationToken cancellationToken = default) =>
        inner.MoveDefinitionsAsync(actor, definitionIds, folderId, operationKey, cancellationToken);
    public ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryViewSnapshot>>> ListViewsAsync(WorkflowLibraryActor actor, CancellationToken cancellationToken = default) =>
        inner.ListViewsAsync(actor, cancellationToken);

    public async ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryViewSnapshot>> SaveViewAsync(WorkflowLibraryActor actor, string? viewId,
        WorkflowLibraryViewSave input, string? operationKey, CancellationToken cancellationToken = default)
    {
        var result = await inner.SaveViewAsync(actor, viewId, input, operationKey, cancellationToken);
        if (operationKey is not null && result.Succeeded && result.Value is { } view) state.MaybeDrop("save-view", operationKey, view.Id);
        return result;
    }

    public ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryViewSnapshot>> FindViewByOperationKeyAsync(WorkflowLibraryActor actor, string operationKey, CancellationToken cancellationToken = default) =>
        inner.FindViewByOperationKeyAsync(actor, operationKey, cancellationToken);
}

sealed class QualificationLibraryDropState
{
    private readonly string modeFile, logFile;
    private readonly ConcurrentDictionary<string, bool> dropped = new(StringComparer.Ordinal);
    private readonly object gate = new();

    public QualificationLibraryDropState(string modeFile, string logFile)
    {
        if (string.IsNullOrWhiteSpace(modeFile) || !Path.IsPathFullyQualified(modeFile) || string.IsNullOrWhiteSpace(logFile) || !Path.IsPathFullyQualified(logFile))
            throw new InvalidOperationException("Set QualificationLibraryResponses:ModeFile and LogFile to absolute private paths.");
        this.modeFile = Path.GetFullPath(modeFile);
        this.logFile = Path.GetFullPath(logFile);
    }

    /// <summary>Loses the committed create's response once per key when the mode selects this kind.</summary>
    public void MaybeDrop(string kind, string operationKey, string committedId)
    {
        if (Mode() != kind || !dropped.TryAdd(kind + "\0" + operationKey, true)) return;
        lock (gate)
            File.AppendAllText(logFile, JsonSerializer.Serialize(new { kind, operationKey, committedId }) + "\n");
        throw new TimeoutException("The qualification seam lost this response after the owner committed it.");
    }

    private string Mode()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(modeFile));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 || !root.TryGetProperty("drop", out var value)
            || value.ValueKind != JsonValueKind.String || value.GetString() is not ("none" or "create-tag" or "save-view"))
            throw new InvalidOperationException("The qualification library mode file must be exactly {\"drop\":\"none|create-tag|save-view\"}.");
        return value.GetString()!;
    }
}
