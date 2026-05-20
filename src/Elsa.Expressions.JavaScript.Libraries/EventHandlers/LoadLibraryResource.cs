using Elsa.Expressions.JavaScript.Core.Events;
using Elsa.Expressions.JavaScript.Libraries.Options;
using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace Elsa.Expressions.JavaScript.Libraries.EventHandlers
{
    internal sealed class LoadLibraryResource(IOptions<JavaScriptLibrariesFeatureOptions> options) : IDomainEventHandler<OnEvaluatingScript>
    {
        public ValueTask Handle(OnEvaluatingScript domainEvent, CancellationToken cancellationToken)
        {
            var resourceName = options.Value.FullModuleResourceName;
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)!;

            using var reader = new StreamReader(stream);
            var script = reader.ReadToEnd();
            domainEvent.ExecutionContext.Execute(script);

            return ValueTask.CompletedTask;
        }
    }
}
