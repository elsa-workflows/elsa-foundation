using Elsa.Workflows.Design.Core.Contracts;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Mapping.Contracts;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Mappings;
using Elsa3.Models;

namespace Elsa3.Mapping.Services;

public sealed class Elsa3WorkflowDefinitionImporter : IElsa3WorkflowDefinitionImporter
{
    private readonly IElsa3DefinitionConverter _converter;
    private readonly Elsa3WorkflowDefinitionToWorkflowDefinitionVersion _mapper;
    private readonly IReusableActivityCollectionImporter? _reusableActivities;

    public Elsa3WorkflowDefinitionImporter(
        IElsa3DefinitionConverter converter,
        Elsa3WorkflowDefinitionToWorkflowDefinitionVersion mapper,
        IReusableActivityCollectionImporter? reusableActivities = null)
    {
        _converter = converter;
        _mapper = mapper;
        _reusableActivities = reusableActivities;
    }

    public async ValueTask<Elsa3MigrationResult<IWorkflowDefinitionVersion>> ImportAsync(
        Elsa3WorkflowDefinitionImportInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.Definition.Options?.UsableAsActivity == true)
        {
            return Elsa3MigrationResult<IWorkflowDefinitionVersion>.Failure(new Elsa3MigrationDiagnostic(
                Elsa3MigrationDiagnosticSeverity.Error,
                ReusableActivityImportDiagnosticCodes.SelectionInvalid,
                $"Elsa 3 reusable workflow '{input.Definition.DefinitionId}' requires collection-aware plan/apply import.",
                guidance: "Analyze the full reusable workflow collection so exact references, cycles, and direct-start wrappers can be reviewed before application.",
                metadata: BuildDiagnosticMetadata(input)));
        }

        var conversion = await _converter.ConvertAsync(
            new Elsa3ConversionRequest(input.Definition, diagnosticMetadata: BuildDiagnosticMetadata(input)),
            cancellationToken);
        if (!conversion.Succeeded)
            return Elsa3MigrationResult<IWorkflowDefinitionVersion>.Failure(conversion.Diagnostics);

        var version = _mapper.Map(input.Definition, conversion.Value!.State);
        return Elsa3MigrationResult<IWorkflowDefinitionVersion>.Success(version, conversion.Diagnostics);
    }

    public ValueTask<ReusableActivityImportPlan> AnalyzeReusableCollectionAsync(
        ReusableActivityImportCollection collection,
        CancellationToken cancellationToken = default) =>
        RequireReusableImporter().AnalyzeAsync(collection, cancellationToken);

    public ValueTask<ReusableActivityImportApplyResult> ApplyReusableCollectionAsync(
        ReusableActivityImportApplyRequest request,
        CancellationToken cancellationToken = default) =>
        RequireReusableImporter().ApplyAsync(request, cancellationToken);

    public Elsa3MigrationResult<IWorkflowDefinitionVersion> RejectUnsupportedInputKind(Elsa3MigrationInputKind inputKind, string? sourceName = null) =>
        Elsa3MigrationCompatibility.IsLiveInstanceStateInput(inputKind)
            ? RejectWorkflowInstanceState(sourceName)
            : Elsa3MigrationCompatibility.RejectUnsupportedInputKind<IWorkflowDefinitionVersion>(inputKind, sourceName);

    public Elsa3MigrationResult<IWorkflowDefinitionVersion> RejectWorkflowInstanceState(string? sourceName = null)
    {
        if (sourceName is not null && string.IsNullOrWhiteSpace(sourceName))
            throw new ArgumentException("Migration source name cannot be blank.", nameof(sourceName));

        var metadata = new Dictionary<string, string>
        {
            ["InputKind"] = Elsa3MigrationInputKind.WorkflowInstanceState.ToString(),
        };
        if (sourceName is not null)
            metadata["SourceName"] = sourceName;

        return Elsa3MigrationResult<IWorkflowDefinitionVersion>.Failure(new Elsa3MigrationDiagnostic(
            Elsa3MigrationDiagnosticSeverity.Error,
            Elsa3ImportDiagnosticCodes.LiveInstanceState,
            "Elsa 3 live workflow-instance state cannot be supplied to authored definition import.",
            guidance: "Drain Elsa 3 instances or use a separately governed live-state migration tool; definition import creates native Elsa 4 authored state only.",
            metadata: metadata));
    }

    private static Dictionary<string, string> BuildDiagnosticMetadata(Elsa3WorkflowDefinitionImportInput input)
    {
        var metadata = input.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

        metadata["InputKind"] = input.InputKind.ToString();

        if (input.SourceName is not null)
            metadata["SourceName"] = input.SourceName;

        return metadata;
    }

    private IReusableActivityCollectionImporter RequireReusableImporter() =>
        _reusableActivities ?? throw new InvalidOperationException(
            "Elsa 3 reusable collection import is not configured. Enable Elsa3ImportJsonActivities and an IReusableActivityImportCommand adapter.");
}
