using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Http.JavaScript.Constants;

namespace Elsa.Http.JavaScript.Contributors;

public sealed class HttpTypeDeclarationContributor(IJavaScriptTypeDeclarationFactory typeDeclarationFactory)
    : IJavaScriptDeclarationContributor
{
    public ValueTask Contribute(IJavaScriptDeclarationsContributionContext context, CancellationToken cancellationToken)
    {
        HttpTypeDescriptors
            .GetTypes()
            .Select(typeDeclarationFactory.Create)
            .ToList()
            .ForEach(context.AddType);

        HttpTypeDescriptors
            .GetAliases()
            .Select(x =>
            {
                var result = typeDeclarationFactory.Create(x.type);
                result.Alias = x.alias;
                return result;
            })
            .ToList()
            .ForEach(context.AddType);

        return ValueTask.CompletedTask;
    }
}
