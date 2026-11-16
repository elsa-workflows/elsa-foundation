using System.Text.Json;
using Elsa.Activities.Sequence.Activities;
using Elsa.Activities.Primitives.Activities;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Runtime;
using Elsa.Rules.Workflows.Runtime.Activities;
using Elsa.Rules.Workflows.Runtime.Models;
using Elsa.Rules.Workflows.Runtime.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Rules.Tests;

/// <summary>
/// Source-build composition evidence for the registered compiler and persisted submit/publication services.
/// The synthetic Rules identity does not establish package qualification; HTTP routing and authorization
/// remain separate acceptance gates.
/// </summary>
public sealed class RulesCompiledPublicationTests
{
    [Fact]
    public async Task Registered_compiler_freezes_literal_program_pin_and_resume_target_in_sequence()
    {
        var executable = await RulesCompiledPublicationFixture.CompileAsync();

        Assert.StartsWith("sha256:", executable.Identity.ArtifactHash, StringComparison.Ordinal);
        var sequence = executable.RootActivity;
        Assert.Equal(RulesCompiledPublicationFixture.SequenceNodeId, sequence.ExecutableNodeId);
        Assert.Equal(typeof(Sequence).FullName, sequence.ActivityType);
        var slot = Assert.Single(sequence.ChildSlots);
        Assert.Equal(Sequence.ActivitiesSlotName, slot.Name);
        var children = slot.Activities.ToArray();
        Assert.Equal(2, children.Length);
        var rules = children[0];
        Assert.Equal(RulesCompiledPublicationFixture.RulesNodeId, rules.ExecutableNodeId);
        Assert.Equal(RunRules.ActivityType, rules.ActivityType);
        var afterRules = children[1];
        Assert.Equal("after-rules", afterRules.ExecutableNodeId);
        Assert.Equal(typeof(WriteLine).FullName, afterRules.ActivityType);
        Assert.Equal("after-rules", afterRules.InputBindings["text"].LiteralValue?.GetString());

        var programBinding = rules.InputBindings[nameof(RunRules.Program)];
        Assert.Equal(RuntimeInputBindingSource.Literal, programBinding.Source);
        Assert.Equal(RulesCompiledPublicationFixture.ProgramLiteral, programBinding.LiteralValue?.GetString());
        Assert.Equal(JsonValueKind.Array, rules.InputBindings[nameof(RunRules.InitialFacts)].LiteralValue?.ValueKind);
        var maxRounds = rules.InputBindings[nameof(RunRules.MaxRounds)].LiteralValue;
        Assert.Equal(JsonValueKind.Number, maxRounds?.ValueKind);
        Assert.Equal(4, maxRounds!.Value.GetInt32());

        Assert.True(rules.Metadata.TryGetValue(RunRules.PinMetadataKey, out var serializedPin));
        AssertProgramPin(serializedPin!);

        var target = Assert.Single(executable.ResumeTargets.Values,
            item => item.ExecutableNodeId == RulesCompiledPublicationFixture.RulesNodeId);
        Assert.Equal("ResumeAsync", target.HandlerKey);
        Assert.Equal(RulesNativeBoundary.ResumeTargetKey, target.LocalResumeTargetId);
        Assert.Equal(WorkflowExecutableResumeTarget.ComposeScopedId(rules.ExecutableNodeId, RulesNativeBoundary.ResumeTargetKey),
            target.ResumeTargetId);
    }

    [Fact]
    public async Task Persisted_submit_and_mediator_publish_save_compiled_rules_artifact_reference_and_active_record()
    {
        var evidence = await RulesCompiledPublicationFixture.SubmitAndPublishAsync();

        Assert.Equal(evidence.Submitted.VersionId, evidence.Version.Id);
        Assert.Equal(evidence.Submitted.DefinitionId, evidence.Version.DefinitionId);
        Assert.Equal(evidence.Submitted.DefinitionId, evidence.Version.Definition?.Id);
        Assert.Equal(evidence.Submitted.VersionId, evidence.Published.VersionId);
        Assert.Equal(evidence.Submitted.DefinitionId, evidence.Published.DefinitionId);

        Assert.Equal(PublicationStatusView.Active, evidence.Published.Status);
        Assert.Equal(evidence.Published.PublicationId, evidence.Publication.PublicationId);
        Assert.Equal(PublicationStatus.Active, evidence.Publication.Status);
        Assert.Equal(evidence.Submitted.VersionId, evidence.Publication.WorkflowDefinitionVersionId);
        Assert.Equal(evidence.Published.ArtifactId, evidence.Publication.ArtifactId);

        Assert.Equal(evidence.Published.ArtifactId, evidence.Executable.Identity.ArtifactId);
        Assert.Equal(evidence.Published.ArtifactHash, evidence.Executable.Identity.ArtifactHash);
        Assert.Equal(evidence.Submitted.VersionId, evidence.Executable.Identity.DefinitionVersionId);
        Assert.Equal(evidence.Published.SourceReferenceId, evidence.SourceReference.SourceReferenceId);
        Assert.Equal(evidence.Published.ArtifactId, evidence.SourceReference.ArtifactId);
        Assert.Equal(evidence.Submitted.DefinitionId, evidence.SourceReference.DefinitionId);
        Assert.Equal(evidence.Submitted.VersionId, evidence.SourceReference.DefinitionVersionId);
        Assert.Equal(WorkflowExecutableReferenceScope.Published, evidence.SourceReference.Scope);
        Assert.Equal(evidence.Published.PublicationId, evidence.SourceReference.ActivationId);
        Assert.Equal(evidence.Publication.SlotId, evidence.SourceReference.SlotId);

        var rules = Assert.Single(evidence.Executable.RootActivity.ChildSlots).Activities
            .Single(activity => activity.ExecutableNodeId == RulesCompiledPublicationFixture.RulesNodeId);
        Assert.True(rules.Metadata.TryGetValue(RunRules.PinMetadataKey, out var serializedPin));
        AssertProgramPin(serializedPin!);
    }

    private static void AssertProgramPin(string serializedPin)
    {
        var pin = RuleWorkflowProgramPin.Parse(serializedPin, RulesCompiledPublicationFixture.ProgramLiteral);
        var expectedProgram = RuleTestData.Program(["derived", "seed"],
            [RuleTestData.Rule("derive", ["seed"], "derived")]);
        var expectedArtifact = new RuleArtifactCompiler().Compile(expectedProgram);
        Assert.Equal(expectedArtifact.Artifact.ArtifactId, pin.Artifact.ArtifactId);
        Assert.Equal(expectedArtifact.Artifact.Sha256, pin.Artifact.Sha256);
        Assert.Equal(expectedArtifact.Artifact.CanonicalBytes.ToArray(), pin.Artifact.CanonicalBytes.ToArray());
        Assert.Equal(expectedArtifact.VocabularyCount, pin.VocabularyCount);
        var expectedImplementation = RuleTestData.Invocation().ExpectedImplementation;
        Assert.Equal(RuleRecordCodec.PinHash(expectedImplementation),
            RuleRecordCodec.PinHash(pin.Implementation));
    }
}
