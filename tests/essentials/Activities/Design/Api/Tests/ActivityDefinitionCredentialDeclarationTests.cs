using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Primitives.Diagnostics;
using Elsa.Primitives.Identity;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Xunit;

namespace Elsa.Activities.Design.Api.Tests;

/// <summary>
/// In phase 0 the credential declaration is reserved for <c>[ActivityInput(IsCredential = true)]</c> on a CLR activity,
/// which CLR reconciliation checks can be bound to a secret reference. An activity definition or version added through
/// the Activities Design API has no such check, so it may declare an input sensitive but not a credential (spec 188,
/// T102, research R5). The commands are read from request JSON with the API's own wire options, so the wire name
/// <c>isCredential</c> is what is refused.
/// </summary>
public sealed class ActivityDefinitionCredentialDeclarationTests
{
    private static readonly GuidIdentityGenerator Identities = new();

    private static readonly ActivityDefinitionVersionFactory VersionFactory = new(Identities, new DefaultActivityDefinitionHasher());

    private readonly RecordingCatalog _catalog = new();

    [Fact]
    public async Task Adding_a_definition_with_a_credential_input_is_refused_and_nothing_is_stored()
    {
        var exception = await Assert.ThrowsAsync<ActivityAuthoringException>(() => AddDefinitionAsync("\"isCredential\": true"));

        AssertRefusesTheCredentialInput(exception);
        Assert.Empty(_catalog.Added);
    }

    [Fact]
    public async Task Adding_a_version_with_a_credential_input_is_refused_and_nothing_is_stored()
    {
        var exception = await Assert.ThrowsAsync<ActivityAuthoringException>(() => AddVersionAsync("\"isSensitive\": true, \"isCredential\": true"));

        AssertRefusesTheCredentialInput(exception);
        Assert.Empty(_catalog.Added);
    }

    [Fact]
    public async Task A_definition_may_declare_a_sensitive_input()
    {
        await AddDefinitionAsync("\"isSensitive\": true");

        AssertStoredSensitiveInput();
    }

    [Fact]
    public async Task A_version_may_declare_a_sensitive_input()
    {
        await AddVersionAsync("\"isSensitive\": true, \"isCredential\": false");

        AssertStoredSensitiveInput();
    }

    private Task<ActivityDefinitionVersionDetailsView> AddDefinitionAsync(string declaration) =>
        new AddDefinitionCommandHandler(new ActivityDefinitionFactory(Identities), VersionFactory, _catalog, _catalog)
            .Handle(
                Read<AddDefinition>($$"""
                    {
                      "operationKey": "add-definition-1",
                      "activityTypeKey": "Acme.Activities.CallService",
                      "sourceKind": "Api",
                      "sourceId": "Acme.Activities.CallService",
                      {{CommonMembers(declaration)}},
                      "category": "Acme",
                      "displayName": "Call Service"
                    }
                    """),
                CancellationToken.None);

    private Task<ActivityDefinitionVersionDetailsView> AddVersionAsync(string declaration) =>
        new AddVersionCommandHandler(VersionFactory, _catalog, _catalog, _catalog)
            .Handle(
                Read<AddVersion>($$"""
                    {
                      "operationKey": "add-version-1",
                      "definitionId": "{{RecordingCatalog.DefinitionId}}",
                      "version": "2.0.0",
                      {{CommonMembers(declaration)}}
                    }
                    """),
                CancellationToken.None);

    private static string CommonMembers(string declaration) =>
        $$"""
        "providerKey": "acme.provider",
        "providerSchemaVersion": "1",
        "consumerKey": "elsa.clr-activity",
        "consumerSchemaVersion": "1",
        "descriptorPayload": {},
        "inputs": [
          { "referenceKey": "endpoint", "name": "Endpoint", "type": { "alias": "String" }, "displayName": "Endpoint", "isNullable": true },
          { "referenceKey": "apiKey", "name": "ApiKey", "type": { "alias": "String" }, "displayName": "API key", "isNullable": true, {{declaration}} }
        ]
        """;

    private void AssertStoredSensitiveInput()
    {
        var inputs = Assert.Single(_catalog.Added).Inputs.ToDictionary(input => input.ReferenceKey, StringComparer.Ordinal);
        Assert.True(inputs["apiKey"].IsSensitive);
        Assert.NotEqual(true, inputs["apiKey"].IsCredential);
        Assert.Null(inputs["endpoint"].IsSensitive);
        Assert.Null(inputs["endpoint"].IsCredential);
    }

    private static void AssertRefusesTheCredentialInput(ActivityAuthoringException exception)
    {
        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(ActivityErrorCodes.RequestInvalid, exception.ErrorCode);
        Assert.Contains("Input 'apiKey' declares isCredential", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'endpoint'", exception.Message, StringComparison.Ordinal);
    }

    private static T Read<T>(string json) => (T)JsonSerializer.Deserialize(json, ActivitiesDesignWire.Context.GetTypeInfo(typeof(T))!)!;

    /// <summary>
    /// The catalog behind both handlers: the add commands record what they are asked to store, and the version store
    /// returns it, so a refused request is seen to store nothing.
    /// </summary>
    private sealed class RecordingCatalog : IAddActivityDefinitionCommand, IAddActivityDefinitionVersionCommand, IActivityDefinitionVersionStore, IActivityDefinitionStore
    {
        public const string DefinitionId = "definition-1";

        private static readonly ActivityDefinition Definition = new() { Id = DefinitionId, ActivityTypeKey = "Acme.Activities.CallService", Category = "Acme" };

        public List<ActivityDefinitionVersion> Added { get; } = [];

        public Task<ActivityDefinitionCreated> Execute(DesignOperationKey operationKey, ActivityDefinition definition, ActivityDefinitionVersion version, CancellationToken cancellation = default)
        {
            Added.Add(version);
            return Task.FromResult(new ActivityDefinitionCreated(definition.Id, version.Id, version.Version, version.Hash));
        }

        public Task<ActivityDefinitionVersionAdded> Execute(DesignOperationKey operationKey, ActivityDefinitionVersion version, CancellationToken cancellationToken = default)
        {
            Added.Add(version);
            return Task.FromResult(new ActivityDefinitionVersionAdded(version.DefinitionId, version.Id, version.Version, version.Hash));
        }

        public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default)
        {
            var version = Added.Single(candidate => candidate.Id == versionId);
            version.Definition ??= Definition;
            return Task.FromResult(version);
        }

        public Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(Definition);

        Task<ActivityDefinitionVersion> IActivityDefinitionVersionStore.GetAsync(string versionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinition>> ListAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
