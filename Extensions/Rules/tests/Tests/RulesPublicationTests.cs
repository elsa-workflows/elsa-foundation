using System.Text;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Runtime;
using Elsa.Rules.Workflows.Design;
using Elsa.Rules.Workflows.Design.Services;
using Elsa.Rules.Workflows.Runtime.Activities;
using Elsa.Rules.Workflows.Runtime.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Rules.Tests;

public sealed class RulesPublicationTests
{
    private const string ProgramJson = "{\n  \"formatVersion\": 1, \"languageId\": \"elsa.rules.finite-positive\", " +
                                       "\"languageVersion\": 1, \"vocabulary\": [\"seed\", \"next\"], " +
                                       "\"rules\": [{\"id\": \"derive\", \"premises\": [\"seed\"], \"conclusion\": \"next\"}]\n}";

    [Fact]
    public async Task Contribution_compiles_literal_program_and_binds_source_and_installed_identity_once()
    {
        var identity = new RecordingIdentitySource(RuleTestData.Invocation().ExpectedImplementation);
        var compiler = new RecordingCompiler();
        var source = new RulesPublishedProgramPinSource(compiler, identity);
        var first = RunRulesNode("z-node", ProgramJson);
        var second = RunRulesNode("a-node", ProgramJson);
        var root = CompositeNode(first, second);

        var contribution = await source.GetContributionAsync(Context(root));

        Assert.Empty(contribution.Dependencies);
        var claims = contribution.NodeMetadata.OrderBy(item => item.ExecutableNodeId, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "a-node", "z-node" }, claims.Select(item => item.ExecutableNodeId));
        Assert.All(claims, claim => Assert.Equal(RunRules.PinMetadataKey, claim.Key));
        Assert.Equal(2, compiler.Calls);
        Assert.Equal(1, identity.Calls);

        foreach (var claim in claims)
        {
            Assert.True(Encoding.UTF8.GetByteCount(claim.Value) <= RuleLimits.MaxEnvelopeBytes);
            var pin = RuleWorkflowProgramPin.Parse(claim.Value, ProgramJson);
            var expected = new RuleArtifactCompiler().Compile(Program()).Artifact;
            Assert.Equal(expected.ArtifactId, pin.Artifact.ArtifactId);
            Assert.Equal(expected.Sha256, pin.Artifact.Sha256);
            Assert.Equal(expected.CanonicalBytes.ToArray(), pin.Artifact.CanonicalBytes.ToArray());
            Assert.Equal(RuleWorkflowProgramPin.SourceHash(ProgramJson), pin.SourceSha256);
            Assert.Equal(RuleRecordCodec.PinHash(identity.Pin!), RuleRecordCodec.PinHash(pin.Implementation));
            Assert.Equal(2, pin.VocabularyCount);
        }
    }

    [Fact]
    public async Task Contribution_without_rules_nodes_is_empty_and_does_not_read_deployment_identity()
    {
        var identity = new RecordingIdentitySource(null);
        var compiler = new RecordingCompiler();
        var source = new RulesPublishedProgramPinSource(compiler, identity);

        var contribution = await source.GetContributionAsync(Context(OtherNode("not-rules")));

        Assert.Empty(contribution.NodeMetadata);
        Assert.Empty(contribution.Dependencies);
        Assert.Equal(0, compiler.Calls);
        Assert.Equal(0, identity.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_unsupported_installed_identity_refuses_publication_without_fallback(bool unsupported)
    {
        var supported = RuleTestData.Invocation().ExpectedImplementation;
        var identityPin = unsupported
            ? new RuleSemanticPin(supported.PinSchemaVersion, supported.Packages, supported.LanguageId,
                supported.LanguageVersion + 1, supported.ReceiptSchemaVersion, supported.StateSchemaVersion)
            : null;
        var identity = new RecordingIdentitySource(identityPin);
        var compiler = new RecordingCompiler();
        var source = new RulesPublishedProgramPinSource(compiler, identity);

        var error = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await source.GetContributionAsync(Context(RunRulesNode("rules-node", ProgramJson))));

        Assert.Contains(unsupported ? "supported installed Rules identity" : "installed Rules implementation identity",
            error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ProgramJson, error.Message, StringComparison.Ordinal);
        Assert.Equal(1, compiler.Calls);
        Assert.Equal(1, identity.Calls);
    }

    [Fact]
    public async Task Dynamic_and_secret_program_bindings_are_refused_without_resolving_them()
    {
        const string secretName = "private-program-secret-sentinel";
        var identity = new RecordingIdentitySource(RuleTestData.Invocation().ExpectedImplementation);
        var compiler = new RecordingCompiler();
        var source = new RulesPublishedProgramPinSource(compiler, identity);
        var dynamicNode = RunRulesNode("dynamic-node", secretName, RuntimeInputBindingSource.Expression);
        var secretNode = RunRulesSecretNode("secret-node", secretName);

        var dynamicError = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await source.GetContributionAsync(Context(dynamicNode)));
        var secretError = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await source.GetContributionAsync(Context(secretNode)));

        Assert.Contains("literal", dynamicError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secretName, dynamicError.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secretName, secretError.Message, StringComparison.Ordinal);
        Assert.Equal(0, compiler.Calls);
        Assert.Equal(0, identity.Calls);
    }

    [Fact]
    public async Task Malformed_unknown_duplicate_and_source_supplied_pin_fields_are_rejected_before_compilation()
    {
        var malformed = new[]
        {
            ProgramJson.Replace("\"languageVersion\": 1,", "\"languageVersion\": 1, \"languageVersion\": 1,", StringComparison.Ordinal),
            ProgramJson.Replace("\"languageVersion\": 1,", "\"languageVersion\": 1, \"secret\": \"private-program-sentinel\",", StringComparison.Ordinal),
            ProgramJson.Replace("\"id\": \"derive\",", "\"id\": \"derive\", \"unexpected\": \"private-program-sentinel\",", StringComparison.Ordinal),
            ProgramJson.Replace("\"languageVersion\": 1,", "\"languageVersion\": 1, \"implementation\": {},", StringComparison.Ordinal)
        };

        foreach (var invalidSource in malformed)
        {
            var identity = new RecordingIdentitySource(RuleTestData.Invocation().ExpectedImplementation);
            var compiler = new RecordingCompiler();
            var source = new RulesPublishedProgramPinSource(compiler, identity);

            var error = await Assert.ThrowsAsync<ArgumentException>(async () =>
                await source.GetContributionAsync(Context(RunRulesNode("invalid-node", invalidSource))));

            Assert.DoesNotContain("private-program-sentinel", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(invalidSource, error.Message, StringComparison.Ordinal);
            Assert.Equal(0, compiler.Calls);
            Assert.Equal(0, identity.Calls);
        }
    }

    [Fact]
    public async Task Oversized_literal_is_refused_before_parsing_or_compilation()
    {
        var identity = new RecordingIdentitySource(RuleTestData.Invocation().ExpectedImplementation);
        var compiler = new RecordingCompiler();
        var source = new RulesPublishedProgramPinSource(compiler, identity);
        var tooLarge = new string('x', RuleLimits.MaxArtifactBytes + 1);

        var error = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await source.GetContributionAsync(Context(RunRulesNode("oversized-node", tooLarge))));

        Assert.Contains("oversized", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(tooLarge, error.Message, StringComparison.Ordinal);
        Assert.Equal(0, compiler.Calls);
        Assert.Equal(0, identity.Calls);
    }

    [Fact]
    public void Design_feature_registers_one_scoped_compilation_source()
    {
        var services = new ServiceCollection();
        var feature = new RulesWorkflowsDesignFeature();
        feature.ConfigureServices(services);
        feature.ConfigureServices(services);

        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(IExecutableCompilationSource));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(RulesPublishedProgramPinSource), descriptor.ImplementationType);
    }

    private static RuleProgram Program() => RuleTestData.Program(["seed", "next"], [RuleTestData.Rule("derive", ["seed"], "next")]);

    private static ExecutableCompilationContext Context(ExecutableNode root, string? tenantId = null)
    {
        var request = new WorkflowExecutableCompileRequest(
            "parent-version",
            WorkflowExecutableReferenceScope.Published,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            "artifact-parent-",
            TenantId: tenantId);
        var compileSource = new WorkflowExecutableCompileSource(
            "parent-definition",
            "parent-version",
            "1.0.0",
            WorkflowDefinitionState.Empty,
            "WorkflowDefinitionVersion",
            "parent-version",
            "1.0.0");
        return new ExecutableCompilationContext(request, compileSource, root);
    }

    private static ExecutableNode RunRulesNode(string id, string program, RuntimeInputBindingSource source = RuntimeInputBindingSource.Literal)
    {
        var type = new ValueTypeDescriptor("String");
        var binding = source == RuntimeInputBindingSource.Literal
            ? new RuntimeInputBinding("Program", type, ValueProtectionPolicy.InstanceInline, source,
                literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(program), ValueProtectionPolicy.InstanceInline))
            : new RuntimeInputBinding("Program", type, ValueProtectionPolicy.InstanceInline, source,
                expression: new RuntimeExpressionBinding("JavaScript", program));
        return Node(id, RunRules.ActivityType, new Dictionary<string, RuntimeInputBinding> { ["Program"] = binding });
    }

    private static ExecutableNode RunRulesSecretNode(string id, string secretName)
    {
        var type = new ValueTypeDescriptor("String");
        var binding = new RuntimeInputBinding("Program", type, ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.SecretRead, secret: new RuntimeSecretReference(secretName));
        return Node(id, RunRules.ActivityType, new Dictionary<string, RuntimeInputBinding> { ["Program"] = binding });
    }

    private static ExecutableNode CompositeNode(params ExecutableNode[] children) =>
        Node("composite-root", "test.composite", new Dictionary<string, RuntimeInputBinding>(),
            [new ExecutableChildSlot("activities", children)]);

    private static ExecutableNode OtherNode(string id) => Node(id, "test.other", new Dictionary<string, RuntimeInputBinding>());

    private static ExecutableNode Node(string id, string activityType,
        IReadOnlyDictionary<string, RuntimeInputBinding> bindings, IReadOnlyCollection<ExecutableChildSlot>? childSlots = null) =>
        new(id, "authored-" + id, activityType, "1.0.0",
            new RuntimeActivityDescriptor("test.clr-activity", RuntimeActivityDescriptor.InitialSchemaVersion,
                JsonSerializer.SerializeToElement(new { type = activityType })),
            bindings, new Dictionary<string, string>(), childSlots: childSlots);

    private sealed class RecordingIdentitySource(RuleSemanticPin? pin) : IRuleImplementationIdentitySource
    {
        public int Calls { get; private set; }
        public RuleSemanticPin? Pin => pin;

        public ValueTask<RuleSemanticPin?> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return ValueTask.FromResult(pin);
        }
    }

    private sealed class RecordingCompiler : IRuleArtifactCompiler
    {
        private readonly RuleArtifactCompiler _inner = new();
        public int Calls { get; private set; }

        public RuleArtifactCompilation Compile(RuleProgram program)
        {
            Calls++;
            return _inner.Compile(program);
        }
    }
}
