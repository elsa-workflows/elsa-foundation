using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Contracts;

/// <summary>Replacement contract: one candidate template compiler per shell.</summary>
public interface IActivityTemplateCompiler
{
    ValueTask<ActivityTemplateCompilerResult> CompileAsync(
        ActivityTemplateCompilerRequest request,
        CancellationToken cancellationToken = default);
}

