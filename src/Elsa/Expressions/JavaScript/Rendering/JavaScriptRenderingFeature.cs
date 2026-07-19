using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Expressions.JavaScript.Rendering.Core.Events;
using Elsa.Expressions.JavaScript.Rendering.Core.Models;
using Elsa.Expressions.JavaScript.Rendering.Handlers;
using Elsa.Expressions.JavaScript.Rendering.Services;
using Elsa.Expressions.JavaScript.Rendering.Contributors;
using Elsa.Events.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.JavaScript.Rendering;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Expressions")]
[ManifestFeatureCategory("JavaScript")]
[ManifestFeatureCategory("Design")]
[ShellFeature(
    name: "JavaScriptRendering",
    DisplayName = "JavaScript rendering",
    Description = "Builds JavaScript declaration documents for design-time expression rendering."
)]
public class JavaScriptRenderingFeature : IShellFeature
{
    /// <summary>
    /// Additional declarations for non-binding authoring surfaces. The canonical binding-pure
    /// surface is closed by default and therefore exposes no ambient host functions.
    /// </summary>
    public IEnumerable<JavaScriptFunctionDeclaration> FunctionDeclarations { get; set; } = [];

    public IEnumerable<JavaScriptTypeDeclaration> TypeDeclarations { get; set; } = [];

    public IEnumerable<JavaScriptVariableDeclaration> VariableDeclarations { get; set; } = [];

    public void ConfigureServices(IServiceCollection services)
    {
        services
            .AddEventHandler<OnDeclarationsDocumentGenerating, BuildDeclarationsDocument>()
            .AddScoped<IJavaScriptDeclarationContributor, CommonDeclarationContributor>()

            .AddScoped<IJavaScriptDeclarationsDocumentFactory, JavaScriptDeclarationsDocumentFactory>()
            .AddScoped<IJavaScriptDeclarationsDocumentRenderer, JavaScriptTypeDefinitionDocumentRenderer>()
            .AddScoped<IJavaScriptTypeDeclarationFactory, JavaScriptTypeDeclarationFactory>()
            ;
    }
}
