using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Api.Services;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class GetWorkflowDefinitionSubmitSchemaHandler
    : IRequestHandler<GetWorkflowDefinitionSubmitSchema, WorkflowDefinitionSubmitSchemaView>
{
    /// <summary>Version of the published submit-schema document contract.</summary>
    internal const string SchemaVersion = "1";

    public Task<WorkflowDefinitionSubmitSchemaView> Handle(
        GetWorkflowDefinitionSubmitSchema request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Opaque members of the submit graph (ArgumentValue.Value, structure payloads) deliberately
        // export as unconstrained schemas; the structure registry endpoint documents payloads per kind.
        var schema = AuthoringSchemaExporter.ExportSchema(typeof(SubmitDefinition));
        var fingerprint = AuthoringSchemaExporter.ComputeFingerprint(schema);
        return Task.FromResult(new WorkflowDefinitionSubmitSchemaView(SchemaVersion, fingerprint, schema));
    }
}
