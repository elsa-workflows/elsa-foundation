using CShells.Features;
using Elsa.Expressions.JavaScript.Core.Options;
using Elsa.Expressions.JavaScript.Rendering.Core.Events;
using Elsa.Http.JavaScript.Constants;
using Elsa.Http.JavaScript.EventHandlers;
using Elsa.Mediator.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Http.JavaScript
{
    [ShellFeature(
        name: "HttpJavaScript",
        DisplayName = "HTTP JavaScript services"
    )]
    public class HttpJavaScriptFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<JavaScriptOptions>(o =>
            {
                HttpTypeDescriptors.GetDescriptors().ToList().ForEach(o.TypeDescriptors.Add);
            });

            services.AddDomainEventHandler<OnDeclarationsDocumentGenerating, AddHttpTypeDeclarations>();
        }
    }
}
