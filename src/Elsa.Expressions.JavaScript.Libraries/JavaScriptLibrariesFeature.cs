using CShells.Features;
using Elsa.Expressions.JavaScript.Core.Events;
using Elsa.Expressions.JavaScript.Libraries.EventHandlers;
using Elsa.Expressions.JavaScript.Libraries.Options;
using Elsa.Mediator.Core.Extensions;
using Elsa.Primitives.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.JavaScript.Libraries
{
    [ShellFeature(
        name: "JavaScriptLibraries",
        DisplayName = "JavaScript libraries such as 'lodash', 'lodashFp', or 'moment'"
    )]
    public class JavaScriptLibrariesFeature : IShellFeature
    {
        private const string ElsaClientLibResourceNameFormat = "Elsa.Expressions.JavaScript.Libraries.ClientLib.dist.{0}.js";

        public string? FullModuleResourceName { get; set; }

        public string? ModuleName { get; set; }

        public void ConfigureServices(IServiceCollection services)
        {
            ValidateFeatureOptions();

            services
                .AddDomainEventHandler<OnEvaluatingScript, LoadLibraryResource>()
                .Configure<JavaScriptLibrariesFeatureOptions>(o => o.FullModuleResourceName = GetFullModuleResourceName())
                ;
        }

        private void ValidateFeatureOptions()
        {
            var bothEmpty = string.IsNullOrWhiteSpace(FullModuleResourceName) && string.IsNullOrWhiteSpace(ModuleName);
            var bothSpecified = !string.IsNullOrWhiteSpace(FullModuleResourceName) && !string.IsNullOrWhiteSpace(ModuleName);

            if (bothEmpty || bothSpecified)
            {
                throw new FeatureConfigurationException($"You must specify one of the following optionws: '{nameof(FullModuleResourceName)}' or '{nameof(ModuleName)}'");
            }
        }

        private string GetFullModuleResourceName()
        {
            if (!string.IsNullOrWhiteSpace(FullModuleResourceName))
                return FullModuleResourceName;

            return string.Format(ElsaClientLibResourceNameFormat, ModuleName);
        }
    }
}
