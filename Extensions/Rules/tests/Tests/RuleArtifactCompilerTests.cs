using System.Runtime.InteropServices;
using Elsa.Rules.Core;
using Elsa.Rules.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Rules.Tests;

public sealed class RuleArtifactCompilerTests
{
    [Fact]
    public void Equivalent_programs_compile_to_the_same_canonical_artifact()
    {
        var first = Program(["z", "a"],
        [
            new RuleDefinition("beta", [Fact("z"), Fact("a")], Fact("z")),
            new RuleDefinition("alpha", [], Fact("a"))
        ]);
        var reordered = Program(["a", "z"],
        [
            new RuleDefinition("alpha", [], Fact("a")),
            new RuleDefinition("beta", [Fact("a"), Fact("z")], Fact("z"))
        ]);
        var compiler = new RuleArtifactCompiler();

        var firstResult = compiler.Compile(first);
        var reorderedResult = compiler.Compile(reordered);

        Assert.Equal(firstResult.Artifact.ArtifactId, reorderedResult.Artifact.ArtifactId);
        Assert.Equal(firstResult.Artifact.Sha256, reorderedResult.Artifact.Sha256);
        Assert.Equal(firstResult.Artifact.CanonicalBytes.ToArray(), reorderedResult.Artifact.CanonicalBytes.ToArray());
        Assert.Equal(2, firstResult.VocabularyCount);
        Assert.Equal(2, reorderedResult.VocabularyCount);
    }

    [Fact]
    public void Vocabulary_count_is_validated_vocabulary_size_not_fact_usage_count()
    {
        var program = Program(["a", "A"],
        [
            new RuleDefinition("lower-to-upper", [Fact("a")], Fact("A")),
            new RuleDefinition("upper-to-lower", [Fact("A")], Fact("a"))
        ]);

        var result = new RuleArtifactCompiler().Compile(program);

        Assert.Equal(2, result.VocabularyCount);
        Assert.Equal("rules:" + result.Artifact.Sha256, result.Artifact.ArtifactId);
    }

    [Fact]
    public void Compiler_rejects_invalid_outside_vocabulary_and_unsupported_programs()
    {
        var outsideVocabulary = Program(["known"], [new RuleDefinition("missing", [], Fact("missing"))]);
        var duplicateVocabulary = Program(["known", "known"], []);
        var unsupportedSchema = new RuleProgram(2, RuleLanguage.Id, RuleLanguage.Version, [Fact("known")], []);
        var compiler = new RuleArtifactCompiler();

        Assert.Throws<InvalidDataException>(() => compiler.Compile(outsideVocabulary));
        Assert.Throws<InvalidDataException>(() => compiler.Compile(duplicateVocabulary));
        Assert.Throws<NotSupportedException>(() => compiler.Compile(unsupportedSchema));
        Assert.Throws<ArgumentNullException>(() => compiler.Compile(null!));
    }

    [Fact]
    public void Compilation_result_validates_count_and_exposes_immutable_artifact_data()
    {
        var compiler = new RuleArtifactCompiler();
        var artifact = compiler.Compile(Program(["seed"], [])).Artifact;
        var originalBytes = artifact.CanonicalBytes.ToArray();
        var result = new RuleArtifactCompilation(artifact, 1);

        Assert.True(MemoryMarshal.TryGetArray(result.Artifact.CanonicalBytes, out var exposedBytes));
        exposedBytes.Array![exposedBytes.Offset] ^= 0xff;

        Assert.Equal(originalBytes, result.Artifact.CanonicalBytes.ToArray());
        Assert.Equal(0, compiler.Compile(Program([], [])).VocabularyCount);
        Assert.Null(typeof(RuleArtifactCompilation).GetProperty(nameof(result.Artifact))!.SetMethod);
        Assert.Null(typeof(RuleArtifactCompilation).GetProperty(nameof(result.VocabularyCount))!.SetMethod);
        Assert.Throws<ArgumentNullException>(() => new RuleArtifactCompilation(null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuleArtifactCompilation(artifact, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuleArtifactCompilation(artifact, RuleLimits.MaxVocabularyFacts + 1));
        Assert.Equal(0, new RuleArtifactCompilation(artifact, 0).VocabularyCount);
        Assert.Equal(RuleLimits.MaxVocabularyFacts,
            new RuleArtifactCompilation(artifact, RuleLimits.MaxVocabularyFacts).VocabularyCount);
    }

    [Fact]
    public void Runtime_feature_registers_the_scoped_default_compiler()
    {
        var services = new ServiceCollection();
        new RulesRuntimeFeature().ConfigureServices(services);

        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(IRuleArtifactCompiler));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(RuleArtifactCompiler), descriptor.ImplementationType);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Runtime_feature_preserves_compiler_override_in_either_registration_order(bool registerBeforeFeature)
    {
        var services = new ServiceCollection();
        var replacement = new StubArtifactCompiler();
        if (registerBeforeFeature)
            services.AddSingleton<IRuleArtifactCompiler>(replacement);
        new RulesRuntimeFeature().ConfigureServices(services);
        if (!registerBeforeFeature)
            services.AddSingleton<IRuleArtifactCompiler>(replacement);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.Same(replacement, scope.ServiceProvider.GetRequiredService<IRuleArtifactCompiler>());
    }

    private static RuleProgram Program(string[] vocabulary, RuleDefinition[] rules) =>
        new(RuleLanguage.FormatVersion, RuleLanguage.Id, RuleLanguage.Version,
            vocabulary.Select(Fact).ToArray(), rules);

    private static RuleFact Fact(string atom) => new(atom);

    private sealed class StubArtifactCompiler : IRuleArtifactCompiler
    {
        public RuleArtifactCompilation Compile(RuleProgram program) => throw new NotSupportedException();
    }
}
