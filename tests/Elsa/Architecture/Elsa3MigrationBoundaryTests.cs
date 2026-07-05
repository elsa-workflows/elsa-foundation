using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.Mappings;
using Elsa3.Mapping.Services;
using Elsa3.Models;
using System.Collections.ObjectModel;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class Elsa3MigrationBoundaryTests
{
    private readonly Elsa3WorkflowDefinitionImporter _importer = new(new Elsa3WorkflowDefinitionToWorkflowDefinitionVersion(
        new Elsa3WorkflowDefinitionToState(
            new StubWellKnownTypeRegistry(),
            new Elsa3ActivityToState(new ThrowingActivityDefinitionLookup()),
            new Elsa3ArgumentDefinitionToInputOutput(new StubWellKnownTypeRegistry())),
        new StubWorkflowDefinitionFactory(),
        new ThrowingWorkflowDefinitionVersionFactory()));

    [Theory]
    [InlineData(Elsa3MigrationInputKind.WorkflowDefinitionExportJson)]
    [InlineData(Elsa3MigrationInputKind.WorkflowDefinitionOriginalSource)]
    [InlineData(Elsa3MigrationInputKind.WorkflowDefinitionStringDataRoot)]
    public void AuthoredWorkflowDefinitionInputs_AreAccepted(Elsa3MigrationInputKind inputKind)
    {
        var definition = new Elsa3WorkflowDefinition
        {
            Id = "version-1",
            DefinitionId = "definition-1",
            Name = "Imported workflow",
            Description = "Imported from Elsa 3.",
            Root = new Elsa3Activity
            {
                Id = "root",
                NodeId = "root-node",
                Name = "Root",
                Type = "Flowchart"
            }
        };

        var input = new Elsa3WorkflowDefinitionImportInput(inputKind, definition, sourceName: "workflow.json");

        Assert.Equal(inputKind, input.InputKind);
        Assert.Same(definition, input.Definition);
        Assert.True(Elsa3MigrationCompatibility.IsAuthoredDefinitionInput(inputKind));
    }

    [Fact]
    public void WorkflowInstanceState_IsRejectedForLiveResume()
    {
        var result = Elsa3MigrationCompatibility.RejectLiveInstanceResume<object>("workflow-instance.json");
        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Equal(Elsa3MigrationDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(Elsa3MigrationDiagnosticCodes.LiveInstanceResumeUnsupported, diagnostic.Code);
        Assert.Contains("cannot be resumed", diagnostic.Message);
        Assert.Contains("Drain or complete", diagnostic.Guidance);
        Assert.Equal("workflow-instance.json", diagnostic.Metadata["SourceName"]);
    }

    [Fact]
    public void DefinitionImportInput_RejectsWorkflowInstanceStateKind()
    {
        var exception = Assert.Throws<ArgumentException>(() => new Elsa3WorkflowDefinitionImportInput(
            Elsa3MigrationInputKind.WorkflowInstanceState,
            new Elsa3WorkflowDefinition()));

        Assert.Contains("authored workflow definition", exception.Message);
    }

    [Fact]
    public void DefinitionImportInput_RejectsReservedDiagnosticMetadataKeys()
    {
        var exception = Assert.Throws<ArgumentException>(() => new Elsa3WorkflowDefinitionImportInput(
            Elsa3MigrationInputKind.WorkflowDefinitionExportJson,
            new Elsa3WorkflowDefinition(),
            metadata: new Dictionary<string, string> { ["InputKind"] = "caller-value" }));

        Assert.Contains("reserved diagnostic keys", exception.Message);
        Assert.Contains("InputKind", exception.Message);
    }

    [Fact]
    public void UnsupportedInputKind_CanBeRejectedWithDiagnostics()
    {
        var result = Elsa3MigrationCompatibility.RejectUnsupportedInputKind<object>(
            (Elsa3MigrationInputKind)999,
            "workflow-instance.json");
        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.False(result.Succeeded);
        Assert.Equal(Elsa3MigrationDiagnosticCodes.UnsupportedInputKind, diagnostic.Code);
        Assert.Equal("999", diagnostic.Metadata["InputKind"]);
        Assert.Equal("workflow-instance.json", diagnostic.Metadata["SourceName"]);
    }

    [Fact]
    public void MigrationResult_RequiresErrorDiagnosticsForFailure()
    {
        var warning = new Elsa3MigrationDiagnostic(
            Elsa3MigrationDiagnosticSeverity.Warning,
            "ELS3-WARNING",
            "A warning.");
        var error = new Elsa3MigrationDiagnostic(
            Elsa3MigrationDiagnosticSeverity.Error,
            Elsa3MigrationDiagnosticCodes.UnsupportedInputKind,
            "An error.");

        Assert.Throws<ArgumentException>(() => Elsa3MigrationResult<object>.Failure(warning));

        var failure = Elsa3MigrationResult<object>.Failure(error);

        Assert.False(failure.Succeeded);
        Assert.IsType<ReadOnlyCollection<Elsa3MigrationDiagnostic>>(failure.Diagnostics);
        Assert.Same(error, Assert.Single(failure.Diagnostics));
    }

    [Fact]
    public void MigrationResult_FailureRequiresDiagnosticsCollection()
    {
        Assert.Throws<ArgumentNullException>(() => Elsa3MigrationResult<object>.Failure((IEnumerable<Elsa3MigrationDiagnostic>)null!));
    }

    [Fact]
    public void MigrationResult_SuccessCannotCarryErrors()
    {
        var error = new Elsa3MigrationDiagnostic(
            Elsa3MigrationDiagnosticSeverity.Error,
            Elsa3MigrationDiagnosticCodes.DefinitionMappingFailed,
            "Mapping failed.");

        Assert.Throws<ArgumentException>(() => Elsa3MigrationResult<object>.Success(new object(), [error]));
    }

    [Fact]
    public void MetadataSnapshots_AreReadOnlyAndDetachedFromInput()
    {
        var metadata = new Dictionary<string, string> { ["Source"] = "source-a" };
        var input = new Elsa3WorkflowDefinitionImportInput(
            Elsa3MigrationInputKind.WorkflowDefinitionExportJson,
            new Elsa3WorkflowDefinition
            {
                Id = "version-1",
                DefinitionId = "definition-1",
                Name = "Imported workflow",
                Root = new Elsa3Activity
                {
                    Id = "root",
                    Type = "Flowchart"
                }
            },
            metadata: metadata);

        metadata["Source"] = "source-b";

        Assert.IsType<ReadOnlyDictionary<string, string>>(input.Metadata);
        Assert.Equal("source-a", input.Metadata["Source"]);
    }

    [Fact]
    public async Task WorkflowDefinitionImporter_ReturnsDiagnosticsForMalformedDefinitions()
    {
        var input = new Elsa3WorkflowDefinitionImportInput(
            Elsa3MigrationInputKind.WorkflowDefinitionExportJson,
            new Elsa3WorkflowDefinition
            {
                Id = "version-1",
                DefinitionId = "definition-1",
                Name = "Broken workflow",
                Description = "Missing root."
            },
            sourceName: "workflow.json",
            metadata: new Dictionary<string, string> { ["TenantId"] = "tenant-1" });

        var result = await _importer.ImportAsync(input);
        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.False(result.Succeeded);
        Assert.Equal(Elsa3MigrationDiagnosticCodes.DefinitionMappingFailed, diagnostic.Code);
        Assert.Contains("definition-1", diagnostic.Message);
        Assert.Equal("WorkflowDefinitionExportJson", diagnostic.Metadata["InputKind"]);
        Assert.Equal("workflow.json", diagnostic.Metadata["SourceName"]);
        Assert.Equal("tenant-1", diagnostic.Metadata["TenantId"]);
    }

    [Fact]
    public async Task WorkflowDefinitionImporter_ReturnsDiagnosticsWhenMalformedDefinitionIdIsMissing()
    {
        var input = new Elsa3WorkflowDefinitionImportInput(
            Elsa3MigrationInputKind.WorkflowDefinitionExportJson,
            new Elsa3WorkflowDefinition
            {
                Id = "version-1",
                DefinitionId = null!,
                Name = "Broken workflow",
                Description = "Missing root and definition ID."
            });

        var result = await _importer.ImportAsync(input);
        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.False(result.Succeeded);
        Assert.Equal(Elsa3MigrationDiagnosticCodes.DefinitionMappingFailed, diagnostic.Code);
        Assert.Contains("<unspecified>", diagnostic.Message);
        Assert.Equal("WorkflowDefinitionExportJson", diagnostic.Metadata["InputKind"]);
        Assert.False(diagnostic.Metadata.ContainsKey("DefinitionId"));
    }

    private sealed class StubWorkflowDefinitionFactory : IWorkflowDefinitionFactory
    {
        public IWorkflowDefinition Create(string name, string? description = null, string? id = null) =>
            new WorkflowDefinitionModel(id ?? "definition-1", name, description);
    }

    private sealed class ThrowingWorkflowDefinitionVersionFactory : IWorkflowDefinitionVersionFactory
    {
        public IWorkflowDefinitionVersion Create(
            IWorkflowDefinition definition,
            string version,
            WorkflowDefinitionState state,
            DateTimeOffset? sourceCreatedAt = null,
            string? id = null) =>
            throw new InvalidOperationException("The malformed definition test should fail before creating a workflow definition version.");
    }

    private sealed class StubWellKnownTypeRegistry : IWellKnownTypeRegistry
    {
        public void RegisterType(Type type, string alias)
        {
        }

        public bool TryGetAlias(Type type, out string alias)
        {
            alias = type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
            return true;
        }

        public bool TryGetType(string alias, out Type type)
        {
            type = typeof(object);
            return true;
        }

        public IEnumerable<Type> ListTypes() => [typeof(object)];

        public string GetAliasOrDefault(Type type) => type.AssemblyQualifiedName ?? type.FullName ?? type.Name;

        public Type GetTypeOrDefault(string alias) => typeof(object);

        public bool TryGetTypeOrDefault(string alias, out Type type)
        {
            type = typeof(object);
            return true;
        }
    }

    private sealed class ThrowingActivityDefinitionLookup : IActivityDefinitionLookup
    {
        public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The malformed definition test should fail before resolving activity definitions.");

        public Task<IEnumerable<IActivityDefinition>> ListDefinitions(
            string? id = null,
            string? category = null,
            string? searchTerm = null,
            string? displayName = null,
            string? description = null,
            bool? tenantAgnostic = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Enumerable.Empty<IActivityDefinition>());

        public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The malformed definition test should fail before resolving activity versions.");

        public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Enumerable.Empty<ActivityDefinitionVersionSummary>());
    }
}
