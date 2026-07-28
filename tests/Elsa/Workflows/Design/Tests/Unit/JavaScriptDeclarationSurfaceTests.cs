using Elsa.Expressions.JavaScript.Rendering;
using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Expressions.JavaScript.Rendering.Core.Models;
using Elsa.Workflows.Design.JavaScript;
using Elsa.Workflows.Design.JavaScript.Contributors;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

public sealed class JavaScriptDeclarationSurfaceTests
{
    [Fact]
    public async Task Workflow_design_renders_only_the_readonly_args_global_for_binding_pure_expressions()
    {
        var services = new ServiceCollection();
        new JavaScriptWorkflowsDesignFeature().ConfigureServices(services);
        new JavaScriptRenderingFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        var workflowContributor = new BindingPureArgsDeclarationContributor();
        var context = new RecordingContributionContext();

        await workflowContributor.Contribute(context, CancellationToken.None);
        var rendered = provider.GetRequiredService<IJavaScriptDeclarationsDocumentRenderer>().Render(context.Document);

        Assert.Equal("declare const args: Readonly<Record<string, any>>;\n", rendered.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Workflow_design_does_not_register_removed_ambient_declaration_contributors()
    {
        var services = new ServiceCollection();
        new JavaScriptWorkflowsDesignFeature().ConfigureServices(services);

        var implementations = services
            .Where(descriptor => descriptor.ServiceType == typeof(IJavaScriptDeclarationContributor))
            .Select(descriptor => descriptor.ImplementationType)
            .ToArray();

        Assert.Equal([typeof(BindingPureArgsDeclarationContributor)], implementations);
    }

    [Fact]
    public void JavaScript_rendering_has_no_default_host_function_or_ambient_variable_declarations()
    {
        var feature = new JavaScriptRenderingFeature();

        Assert.Empty(feature.FunctionDeclarations);
        Assert.Empty(feature.VariableDeclarations);
    }

    private sealed class RecordingContributionContext : IJavaScriptDeclarationsContributionContext
    {
        private readonly List<JavaScriptFunctionDeclaration> _functions = [];
        private readonly List<JavaScriptTypeDeclaration> _types = [];
        private readonly List<JavaScriptVariableDeclaration> _variables = [];

        public JavaScriptDeclarationsDocument Document => new()
        {
            Functions = _functions,
            Types = _types,
            Variables = _variables
        };

        public void AddVariable(JavaScriptVariableDeclaration declaration) => _variables.Add(declaration);
        public void AddType(JavaScriptTypeDeclaration declaration) => _types.Add(declaration);
        public void AddFunction(JavaScriptFunctionDeclaration declaration) => _functions.Add(declaration);
    }
}
