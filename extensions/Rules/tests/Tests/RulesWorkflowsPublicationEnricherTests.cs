using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Events;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Runtime;
using Elsa.Rules.Workflows.Design;
using Elsa.Rules.Workflows.Runtime.Activities;
using Elsa.Rules.Workflows.Runtime.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Rules.Tests;

/// <summary>
/// Exercises the actual registered event/enrichment path. The implementation identity is intentionally supplied by
/// the test because this source-built test host has no deployment receipt; this is composition evidence, not package
/// identity evidence.
/// </summary>
public sealed class RulesWorkflowsPublicationEnricherTests
{
    private const string ProgramJson = "{\"formatVersion\":1,\"languageId\":\"elsa.rules.finite-positive\",\"languageVersion\":1,\"vocabulary\":[\"seed\",\"next\"],\"rules\":[{\"id\":\"derive\",\"premises\":[\"seed\"],\"conclusion\":\"next\"}]}";

    [Fact]
    public async Task Registered_publication_enricher_pins_a_literal_RunRules_program_through_the_event_pipeline()
    {
        var identity = RuleTestData.Invocation().ExpectedImplementation;
        var identitySource = new TestImplementationIdentitySource(identity);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRuleImplementationIdentitySource>(identitySource);
        new EventsFeature().ConfigureServices(services);
        new WorkflowsPublishingFeature().ConfigureServices(services);
        new RulesRuntimeFeature().ConfigureServices(services);
        new RulesWorkflowsDesignFeature().ConfigureServices(services);

        await using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = root.CreateAsyncScope();
        var enricher = scope.ServiceProvider.GetRequiredService<IExecutableNodeMetadataEnricher>();
        Assert.IsType<ExecutableNodeMetadataEnricher>(enricher);

        var request = new WorkflowExecutableCompileRequest(
            "parent-version", WorkflowExecutableReferenceScope.Published, DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, null, "artifact-parent-");
        var source = new WorkflowExecutableCompileSource(
            "parent-definition", "parent-version", "1.0.0", WorkflowDefinitionState.Empty,
            "WorkflowDefinitionVersion", "parent-version", "1.0.0");

        var enriched = await enricher.EnrichCompilationAsync(request, source, LiteralRunRulesNode());

        Assert.True(enriched.RootActivity.Metadata.TryGetValue(RunRules.PinMetadataKey, out var serializedPin));
        var pin = RuleWorkflowProgramPin.Parse(serializedPin, ProgramJson);
        var expectedArtifact = new RuleArtifactCompiler().Compile(Program()).Artifact;
        Assert.Equal(expectedArtifact.ArtifactId, pin.Artifact.ArtifactId);
        Assert.Equal(expectedArtifact.Sha256, pin.Artifact.Sha256);
        Assert.Equal(expectedArtifact.CanonicalBytes.ToArray(), pin.Artifact.CanonicalBytes.ToArray());
        Assert.Equal(RuleWorkflowProgramPin.SourceHash(ProgramJson), pin.SourceSha256);
        Assert.Equal(2, pin.VocabularyCount);
        Assert.Equal(RuleRecordCodec.PinHash(identity), RuleRecordCodec.PinHash(pin.Implementation));
        Assert.Equal(1, identitySource.ReadCount);
    }

    private static ExecutableNode LiteralRunRulesNode()
    {
        var type = new ValueTypeDescriptor("String");
        var binding = new RuntimeInputBinding(nameof(RunRules.Program), type, ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(ProgramJson), ValueProtectionPolicy.InstanceInline));
        return new ExecutableNode("rules-node", "authored-rules", RunRules.ActivityType, "1.0.0",
            new RuntimeActivityDescriptor("test.clr-activity", RuntimeActivityDescriptor.InitialSchemaVersion,
                JsonSerializer.SerializeToElement(new { type = RunRules.ActivityType })),
            new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal) { [nameof(RunRules.Program)] = binding },
            new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private static RuleProgram Program() => RuleTestData.Program(["seed", "next"],
        [RuleTestData.Rule("derive", ["seed"], "next")]);

    private sealed class TestImplementationIdentitySource(RuleSemanticPin pin) : IRuleImplementationIdentitySource
    {
        public int ReadCount { get; private set; }

        public ValueTask<RuleSemanticPin?> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            return ValueTask.FromResult<RuleSemanticPin?>(pin);
        }
    }
}
