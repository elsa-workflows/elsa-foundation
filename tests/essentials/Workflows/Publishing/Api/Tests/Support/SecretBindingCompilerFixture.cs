using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using VariableDefinition = Elsa.Expressions.Core.Models.VariableDefinition;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Compiles a one-node workflow through the real executable compiler so the secret binding rules (spec 188, slice 2)
/// are exercised where publication applies them. Activity versions are reflected from CLR activity types the way the
/// catalog scanner declares them: one input per <c>[ActivityInput]</c>, keyed and typed as the activity declares.
/// </summary>
internal static class SecretBindingCompilerFixture
{
    public const string NodeId = "secret-node";
    public const string SecretName = "payments.api-key";

    private static readonly IActivityStructureService StructureService = new DefaultActivityStructureService([]);

    /// <summary>A secret reference as the Studio secret picker authors it.</summary>
    public static WorkflowArgumentState Secret(string inputKey, string? typeName = null) =>
        new(inputKey, SecretValue(typeName), null, null, null, null);

    public static ArgumentValue SecretValue(string? typeName = null) =>
        new(JsonSerializer.SerializeToElement(new { name = SecretName, typeName }), "Secret");

    public static ActivityNode Node(Type activityType, params WorkflowArgumentState[] inputs) =>
        new(NodeId, activityType.FullName!, inputs, Outputs: []);

    public static ActivityDefinitionVersion ActivityVersion(
        string id,
        string consumerKey,
        JsonElement descriptorPayload,
        IReadOnlyCollection<InputDefinition> inputs) =>
        new("1.0.0", $"{id}-definition")
        {
            Id = id,
            Definition = new ActivityDefinition { Id = $"{id}-definition", ActivityTypeKey = id, Category = "Test" },
            ProviderKey = consumerKey,
            ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            ConsumerKey = consumerKey,
            ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            DescriptorPayload = descriptorPayload,
            Inputs = inputs
        };

    /// <summary>
    /// Compiles <paramref name="root"/> against a catalog holding one CLR version per <paramref name="activityTypes"/>
    /// (registered under its canonical alias) plus any <paramref name="otherVersions"/>.
    /// </summary>
    public static async Task<WorkflowExecutable> CompileAsync(
        ActivityNode root,
        IReadOnlyCollection<Type> activityTypes,
        IReadOnlyCollection<ActivityDefinitionVersion>? otherVersions = null,
        IReadOnlyCollection<VariableDefinition>? variables = null)
    {
        var registry = TestWellKnownTypeRegistry.Create();
        foreach (var activityType in activityTypes)
            registry.RegisterType(activityType, TypeAliasConvention.CanonicalAlias(activityType));

        var compiler = TestCompiler.Create(
            new FakeVersionStore(new WorkflowDefinitionVersion("definition-1", "1.0.0")
            {
                Id = "version-1",
                Definition = new WorkflowDefinition { Id = "definition-1", Name = "Secret bindings" },
                State = new WorkflowDefinitionState(variables ?? [], root, [], [], null)
            }),
            new FakeActivityVersionStore([.. activityTypes.Select(ClrActivityVersion), .. otherVersions ?? []]),
            StructureService,
            registry);
        return await compiler.CompileAsync(new WorkflowExecutableCompileRequest(
            "version-1",
            WorkflowExecutableReferenceScope.Published,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            "artifact-"));
    }

    /// <summary>A compiled secret read on <paramref name="inputKey"/>, as it reaches a reader in an artifact that skipped publication.</summary>
    public static RuntimeInputBinding SecretReadBinding(string inputKey) =>
        new(
            inputKey,
            new ValueTypeDescriptor("String"),
            new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: true),
            RuntimeInputBindingSource.SecretRead,
            secret: new RuntimeSecretReference(SecretName));

    /// <summary>Asserts a publish-time literal reader refuses a secret read with the fixed <c>VF-ACT-012</c> message.</summary>
    public static void AssertReaderRefusesSecretRead(Action read, string nodeId, string inputKey)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(read);
        Assert.Contains(
            SecretBindingDiagnostics.SecretBindingRefused(nodeId, inputKey, SecretBindingRefusalReason.FixedAtPublish).Message,
            exception.Message,
            StringComparison.Ordinal);
    }

    public static Task<WorkflowExecutableCompilationException> AssertRefusedAsync(
        ActivityNode root,
        IReadOnlyCollection<Type> activityTypes,
        IReadOnlyCollection<ActivityDefinitionVersion>? otherVersions = null,
        IReadOnlyCollection<VariableDefinition>? variables = null) =>
        Assert.ThrowsAsync<WorkflowExecutableCompilationException>(
            () => CompileAsync(root, activityTypes, otherVersions, variables));

    private static ActivityDefinitionVersion ClrActivityVersion(Type activityType) =>
        ActivityVersion(
            activityType.FullName!,
            WellKnownRuntimeActivityConsumers.ClrActivity,
            JsonSerializer.SerializeToElement(new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(activityType))),
            activityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => (Property: property, Attribute: property.GetCustomAttribute<ActivityInputAttribute>(inherit: true)))
                .Where(candidate => candidate.Attribute is not null)
                .Select(candidate => new InputDefinition(
                    candidate.Attribute!.Key ?? candidate.Property.Name,
                    candidate.Property.Name,
                    TypeReferenceFactory.FromClrType(candidate.Property.PropertyType, TypeAliasConvention.CanonicalAlias),
                    StorageDriverType: null,
                    DisplayName: candidate.Property.Name,
                    Category: null,
                    IsNullable: true))
                .ToArray());
}
