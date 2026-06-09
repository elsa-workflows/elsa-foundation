using Elsa.Expressions.JavaScript.Primitives.Constants;
using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Expressions.JavaScript.Rendering.Core.Options;
using Microsoft.Extensions.Options;
using System.Dynamic;

namespace Elsa.Expressions.JavaScript.Rendering.Contributors
{
    public sealed class CommonDeclarationContributor(IOptions<JavaScriptDeclarationOptions> options, IJavaScriptTypeDeclarationFactory factory)
        : IJavaScriptDeclarationContributor
    {
        public ValueTask Contribute(IJavaScriptDeclarationsContributionContext context, CancellationToken cancellationToken)
        {
            RegisterTypeDeclarations(context);

            options.Value.Functions
                .ToList()
                .ForEach(context.AddFunction);

            options.Value.VariableDeclarations
                .ToList()
                .ForEach(context.AddVariable);

            return ValueTask.CompletedTask;
        }

        internal void RegisterTypeDeclarations(IJavaScriptDeclarationsContributionContext context)
        {
            // Custom type declarations
            options.Value.TypeDeclarations
                .ToList()
                .ForEach(context.AddType);

            // Common .NET type declarations
            var types = new (Type type, string? alias)[]
            {
                (typeof(DateTime), WellKnownTypeNames.Date),
                (typeof(DateTimeOffset),WellKnownTypeNames.Date),
                (typeof(TimeSpan),null),
                (typeof(Guid), null),
                (typeof(Random), null),
                (typeof(object), WellKnownTypeNames.Any),
                (typeof(ExpandoObject), WellKnownTypeNames.Any),
                (typeof(string), WellKnownTypeNames.String),
                (typeof(bool), WellKnownTypeNames.Bool),
                (typeof(short), WellKnownTypeNames.Number),
                (typeof(int), WellKnownTypeNames.Number),
                (typeof(long), WellKnownTypeNames.Number),
                (typeof(decimal), WellKnownTypeNames.Decimal),
                (typeof(float), WellKnownTypeNames.Single),
                (typeof(double), WellKnownTypeNames.Double),
                (typeof(byte[]), WellKnownTypeNames.Buffer),
                (typeof(Stream), WellKnownTypeNames.Stream),
                (typeof(Guid), WellKnownTypeNames.Guid),
                (typeof(DateOnly), WellKnownTypeNames.Date),
                (typeof(TimeOnly), WellKnownTypeNames.Date),
                (typeof(IDictionary<string, object>), WellKnownTypeNames.ObjectDictionary)
            };

            var result = types.Select(x =>
            {
                var declaration = factory.Create(x.type);
                declaration.Alias = x.alias;
                return declaration;
            });

            result.ToList().ForEach(context.AddType);
        }
    }
}
