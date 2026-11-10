using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// An activity-result reference that declares a scope serializes, hashes as an executable and hashes as a template
/// behaviour exactly as it did before a scope became optional (#4053). The expected values were computed on the
/// commit before that change, so existing documents keep their bytes and their artifact and template identities.
/// </summary>
public sealed class ScopedActivityResultHashTests
{
    private static readonly RuntimeActivityResultReference Scoped = new("node-fetch", "customer-id", "scope-root", isOptional: true);

    [Fact]
    public void A_scoped_reference_keeps_its_stored_form()
    {
        Assert.Equal(
            """{"producerExecutableNodeId":"node-fetch","projectionKey":"customer-id","producerScopeId":"scope-root","isOptional":true}""",
            JsonSerializer.Serialize(Scoped, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public void A_scoped_reference_keeps_its_executable_hash()
    {
        Assert.Equal(ExpectedExecutableHash, new WorkflowExecutableHasher().ComputeHash(Node(Scoped)));
    }

    [Fact]
    public void A_scoped_reference_keeps_its_template_behavior_hash()
    {
        Assert.Equal(ExpectedTemplateHash, TemplateHash(Node(Scoped)));
    }

    // Computed on 6cf153dc94, the commit before the scope became optional.
    internal const string ExpectedExecutableHash = "sha256:d1f37f9aa352c6ada3256a4e4941e80bac9fa2a2ac79aae9ba4e63d878fa7f3e";
    internal const string ExpectedTemplateHash = "sha256:3903812abb4dc48ab50ca32bbde9783856b5807cda408c89f7c455dc83826355";

    /// <summary>
    /// A reference without a scope leaves the member out of its stored form, reads back with none, and hashes apart
    /// from every scoped reference, as no scoped reference could ever have an empty scope.
    /// </summary>
    [Fact]
    public void A_scope_free_reference_omits_the_scope_reads_back_without_one_and_hashes_apart()
    {
        var scopeFree = new RuntimeActivityResultReference("node-fetch", "customer-id", producerScopeId: null);
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var json = JsonSerializer.Serialize(scopeFree, web);
        Assert.Equal("""{"producerExecutableNodeId":"node-fetch","projectionKey":"customer-id","isOptional":false}""", json);
        var restored = JsonSerializer.Deserialize<RuntimeActivityResultReference>(json, web)!;
        Assert.Null(restored.ProducerScopeId);
        Assert.Equal("customer-id", restored.ProjectionKey);
        Assert.Equal("scope-root", JsonSerializer.Deserialize<RuntimeActivityResultReference>(JsonSerializer.Serialize(Scoped, web), web)!.ProducerScopeId);
        Assert.Throws<ArgumentException>(() => new RuntimeActivityResultReference("node-fetch", "customer-id", " "));

        var hasher = new WorkflowExecutableHasher();
        var optionalScopeFree = new RuntimeActivityResultReference("node-fetch", "customer-id", producerScopeId: null, isOptional: true);
        Assert.NotEqual(ExpectedExecutableHash, hasher.ComputeHash(Node(optionalScopeFree)));
        Assert.NotEqual(hasher.ComputeHash(Node(scopeFree)), hasher.ComputeHash(Node(optionalScopeFree)));
        Assert.NotEqual(ExpectedTemplateHash, TemplateHash(Node(optionalScopeFree)));
    }

    internal static string TemplateHash(ExecutableNode node) =>
        ExecutableActivityTemplateBehaviorHasher.Compute(node, new Dictionary<string, WorkflowExecutableResumeTarget>(),
            [], [], [], [], "provider", new Dictionary<string, string>());

    internal static ExecutableNode Node(RuntimeActivityResultReference reference) =>
        new(
            executableNodeId: "node-consumer",
            authoredActivityId: "consumer",
            activityType: "Tests.Consumer",
            activityTypeVersion: "1.0.0",
            descriptorType: "clr",
            descriptorPayload: JsonSerializer.SerializeToElement(new { typeAlias = "Tests.Consumer" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["customer-id"] = new(
                    inputKey: "customer-id",
                    targetType: new ValueTypeDescriptor("System.String"),
                    effectivePolicy: ValueProtectionPolicy.InstanceInline,
                    source: RuntimeInputBindingSource.ActivityResult,
                    activityResult: reference)
            },
            metadata: new Dictionary<string, string>());
}
