namespace Elsa.Rules.Core;

/// <summary>Compiles a Rules program into its canonical immutable artifact and validated metadata.</summary>
public interface IRuleArtifactCompiler
{
    RuleArtifactCompilation Compile(RuleProgram program);
}

/// <summary>A canonical immutable artifact snapshot and its distinct finite vocabulary size.</summary>
public sealed record RuleArtifactCompilation
{
    public RuleArtifactCompilation(RuleArtifactSnapshot artifact, int vocabularyCount)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (vocabularyCount is < 0 or > RuleLimits.MaxVocabularyFacts)
            throw new ArgumentOutOfRangeException(nameof(vocabularyCount));

        Artifact = artifact;
        VocabularyCount = vocabularyCount;
    }

    public RuleArtifactSnapshot Artifact { get; }
    public int VocabularyCount { get; }
}
