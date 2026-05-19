using CShells.Features;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.JavaScript.Core.Options;
using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Expressions.JavaScript.Rendering.Core.Events;
using Elsa.Expressions.JavaScript.Rendering.Core.Options;
using Elsa.Http.JavaScript.Constants;
using Elsa.Http.JavaScript.EventHandlers;
using Elsa.Mediator.Core;
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

            services.AddScoped<IDomainEventHandler<OnDeclarationsDocumentGenerating>, AddHttpTypeDeclarations>();
        } 
    }
}
