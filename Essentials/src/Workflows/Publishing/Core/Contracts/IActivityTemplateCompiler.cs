using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Contracts;

public interface IActivityTemplateCompiler
{
    ValueTask<ActivityTemplateCompilerResult> CompileAsync(
        ActivityTemplateCompilerRequest request,
        CancellationToken cancellationToken = default);
}

