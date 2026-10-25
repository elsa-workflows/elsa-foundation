using Elsa.Rules.Core;

namespace Elsa.Rules.Runtime;

internal sealed class RuleArtifactCompiler : IRuleArtifactCompiler
{
    public RuleArtifactCompilation Compile(RuleProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var artifact = RulesCodec.CreateArtifact(program);
        return new RuleArtifactCompilation(artifact, program.Vocabulary.Count);
    }
}
