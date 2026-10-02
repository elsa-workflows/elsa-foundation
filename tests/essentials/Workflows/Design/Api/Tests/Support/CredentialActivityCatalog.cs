using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Design.Validations.Internal;
using Elsa.Workflows.Design.Validations.Validators;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Api.Tests.Support;

/// <summary>
/// An activity catalog holding one activity whose <c>apiKey</c> input is declared a credential and whose <c>note</c>
/// input is sensitive but not a credential, for the credential-literal rule (spec 188, FR-008) at the Design API. Every
/// other activity version is unknown to it.
/// </summary>
public sealed class CredentialActivityCatalog : IActivityDefinitionLookup
{
    public const string ActivityVersionId = "av-credential";
    public const string CredentialKey = "apiKey";
    public const string SensitiveKey = "note";
    public const string NodeId = "send";

    /// <summary>A literal no refusal may echo. Built at run time, never credential-shaped.</summary>
    public static readonly string Literal = $"literal-value-{Guid.NewGuid():N}";

    private static readonly ActivityDefinitionVersion Version = new("1.0.0", "credential-definition")
    {
        Id = ActivityVersionId,
        Inputs =
        [
            Input(CredentialKey, "ApiKey") with { IsSensitive = true, IsCredential = true },
            Input(SensitiveKey, "Note") with { IsSensitive = true }
        ]
    };

    /// <summary>The real credential-literal validator, judging against this catalog.</summary>
    public static CredentialLiteralValidator Validator() => new(
        new CatalogVersionResolver(new CredentialActivityCatalog()),
        Options.Create(new WorkflowDesignValidatorOptions()),
        new ActivityTreeWalker(new DefaultActivityStructureService([])));

    /// <summary>A state whose root is a node of <paramref name="activityVersionId"/> bound with <paramref name="inputs"/>.</summary>
    public static WorkflowDefinitionState State(string activityVersionId = ActivityVersionId, params ArgumentState[] inputs) =>
        new([], new ActivityNode(NodeId, activityVersionId, inputs, []), [], [], null);

    /// <summary>A literal, or a secret reference when <paramref name="expressionType"/> is <c>Secret</c>, on <paramref name="inputKey"/>.</summary>
    public static ArgumentState Bind(string inputKey, string expressionType, string? literal = null) => new(
        inputKey,
        expressionType == "Secret"
            ? new ArgumentValue(JsonSerializer.SerializeToElement(new { name = "payments-reference" }), "Secret")
            : new ArgumentValue(JsonSerializer.SerializeToElement(literal ?? Literal), expressionType),
        null, null, null, null);

    public Task<IActivityDefinitionVersion?> FindVersion(string versionId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IActivityDefinitionVersion?>(versionId == ActivityVersionId ? Version : null);

    public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The credential-literal validator reads versions through FindVersion.");

    public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IEnumerable<IActivityDefinition>> ListDefinitions(
        string? id = null, string? category = null, string? searchTerm = null, string? displayName = null,
        string? description = null, bool? tenantAgnostic = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string definitionId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    private static InputDefinition Input(string key, string name) =>
        new(key, name, new TypeReference("String"), null, name, null, IsNullable: true);
}
