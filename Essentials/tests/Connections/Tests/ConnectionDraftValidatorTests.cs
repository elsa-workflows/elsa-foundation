using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Connections.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Design.Validators;
using Elsa.Connections.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;
using Xunit;

namespace Elsa.Connections.Tests;

public sealed class ConnectionDraftValidatorTests
{
    [Fact]
    public async Task MissingAndWrongType_Block_NeedsAuthorization_DoesNot()
    {
        var tenant = "t1";
        var repository = new InMemoryConnectionRepository();
        await repository.TryAddAsync(Document(tenant, "ready-http", ConnectionTypeNames.Http, ConnectionStatuses.Ready));
        await repository.TryAddAsync(Document(tenant, "smtp-one", "smtp", ConnectionStatuses.Ready));
        await repository.TryAddAsync(Document(tenant, "needs-auth", ConnectionTypeNames.Http, ConnectionStatuses.NeedsAuthorization));

        var manager = new RecordingManager(repository);
        var validator = new ConnectionDraftValidator(
            new EmptyStructureService(),
            new Catalog(httpConnectionType: ConnectionTypeNames.Http),
            manager,
            new FixedConnectionPublishTenantAccessor(tenant));

        var missing = (await validator.Validate(new FakeDraft(Node("missing-one")), CancellationToken.None)).ToArray();
        Assert.Contains(missing, error => error.Type == ConnectionDraftValidator.MissingType);

        var wrong = await validator.Validate(new FakeDraft(Node("smtp-one")), CancellationToken.None);
        Assert.Contains(wrong, error => error.Type == ConnectionDraftValidator.TypeMismatchType);

        var needsAuth = (await validator.Validate(new FakeDraft(Node("needs-auth")), CancellationToken.None)).ToArray();
        Assert.DoesNotContain(needsAuth, error => error.Type == ConnectionDraftValidator.NeedsAuthorizationType);
        Assert.Empty(needsAuth);
    }

    [Fact]
    public async Task A_literal_on_a_connection_input_and_a_blank_tenant_both_block()
    {
        var tenant = "t1";
        var repository = new InMemoryConnectionRepository();
        var manager = new RecordingManager(repository);
        var catalog = new Catalog(httpConnectionType: ConnectionTypeNames.Http);

        var malformed = new ConnectionDraftValidator(
            new EmptyStructureService(),
            catalog,
            manager,
            new FixedConnectionPublishTenantAccessor(tenant));
        var literalNode = new ActivityNode(
            "n1",
            "ver-1",
            [
                new ArgumentState(
                    "Connection",
                    new Elsa.Expressions.Core.Models.ArgumentValue("Bearer sk-live", "Literal"),
                    null,
                    null,
                    null,
                    null)
            ],
            []);
        var malformedErrors = (await malformed.Validate(new FakeDraft(literalNode), CancellationToken.None)).ToArray();
        Assert.Contains(malformedErrors, error => error.Type == ConnectionDraftValidator.MissingType);
        Assert.DoesNotContain("sk-live", string.Join('|', malformedErrors.Select(error => error.Message)), StringComparison.Ordinal);

        var blankTenant = new ConnectionDraftValidator(
            new EmptyStructureService(),
            catalog,
            manager,
            new NullConnectionPublishTenantAccessor());
        var blank = (await blankTenant.Validate(new FakeDraft(Node("ready-http")), CancellationToken.None)).ToArray();
        Assert.Contains(blank, error => error.Type == ConnectionDraftValidator.MissingType);
        Assert.Contains(blank, error => error.Message.Contains("tenant", StringComparison.OrdinalIgnoreCase));
    }

    private static ActivityNode Node(string connectionName) =>
        new("n1", "ver-1", [Bound("Connection", connectionName)], []);

    private static ArgumentState Bound(string key, string connectionName) =>
        new(
            key,
            new Elsa.Expressions.Core.Models.ArgumentValue(
                new Dictionary<string, string?> { ["connection"] = connectionName, ["type"] = "http" },
                "Connection"),
            null,
            null,
            null,
            null);

    private static ConnectionDocument Document(string tenant, string name, string type, string status) => new()
    {
        Definition = new ConnectionDefinition
        {
            TenantId = tenant,
            Name = name,
            TypeName = type,
            AuthScheme = HttpAuthSchemes.Bearer,
            Owner = ConnectionOwners.Tenant
        },
        State = new ConnectionState { Status = status, CreatedAt = DateTimeOffset.UtcNow }
    };

    private sealed class EmptyStructureService : IActivityStructureService
    {
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => [];

        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) =>
            activity;

        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => null;

        public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];

        public bool SupportsScopedVariables(ActivityNode activity) => false;
    }

    private sealed class Catalog(string httpConnectionType) : IActivityDefinitionLookup
    {
        public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IEnumerable<IActivityDefinition>> ListDefinitions(
            string? id = null,
            string? category = null,
            string? searchTerm = null,
            string? displayName = null,
            string? description = null,
            bool? tenantAgnostic = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<IActivityDefinition>>([]);

        public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IActivityDefinitionVersion>(new FakeVersion(httpConnectionType));

        public Task<IActivityDefinitionVersion?> FindVersion(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IActivityDefinitionVersion?>(new FakeVersion(httpConnectionType));

        public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<ActivityDefinitionVersionSummary>>([]);
    }

    private sealed class FakeVersion(string connectionType) : IActivityDefinitionVersion
    {
        public string Id => "ver-1";
        public string Version => "1";
        public string DefinitionId => "send-http";
        public string ProviderKey => "test";
        public string ProviderSchemaVersion => "1";
        public string ConsumerKey => "test";
        public string ConsumerSchemaVersion => "1";
        public System.Text.Json.JsonElement DescriptorPayload => default;
        public string SourceKind => "test";
        public string SourceId => "test";
        public IActivityDefinition Definition => throw new NotSupportedException();
        public IEnumerable<InputDefinition> Inputs { get; } =
        [
            new InputDefinition("Connection", "Connection", new TypeReference("ConnectionReference"), null, "Connection", null, true)
            {
                ConnectionType = connectionType
            }
        ];
        public IEnumerable<OutputDefinition> Outputs => [];
        public IEnumerable<ActivityDesignFacet> DesignFacets => [];
        public ActivityExecutionType ExecutionType => default;
        public string? Hash => null;
    }

    private sealed class FakeDraft(ActivityNode root) : IWorkflowDefinitionDraft
    {
        public string Id => "d1";
        public string WorkflowDefinitionId => "wf1";
        public WorkflowDefinitionState State { get; } = new([], root, [], [], null);
        public DateTimeOffset CreatedAt => DateTimeOffset.UtcNow;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UtcNow;
    }

    private sealed class RecordingManager(IConnectionRepository repository) : IConnectionManager
    {
        public async ValueTask<ConnectionMetadata?> FindAsync(string tenantId, string name, CancellationToken cancellationToken = default)
        {
            var document = await repository.FindAsync(tenantId, name, cancellationToken);
            return document is null
                ? null
                : new ConnectionMetadata
                {
                    TenantId = document.Definition.TenantId,
                    Name = document.Definition.Name,
                    TypeName = document.Definition.TypeName,
                    Status = document.State.Status
                };
        }

        public ValueTask<ConnectionMetadata> CreateAsync(string tenantId, CreateConnectionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ConnectionMetadata>> ListAsync(string tenantId, ConnectionQuery? query = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<ConnectionMetadata?> UpdateAsync(string tenantId, string name, UpdateConnectionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<ConnectionTestResult> TestAsync(string tenantId, string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<ConnectionMetadata?> RevokeAsync(string tenantId, string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
