using System.Text.Json;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Features;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.Services;
using Elsa.Secrets.Expressions;
using Elsa.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class WorkflowManagementExpressionDescriptorApiTests
{
    [Fact]
    public async Task Expression_descriptors_project_required_editing_modes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddCShellsAspNetCore(shells => shells
            .WithAssemblies(typeof(ExpressionDescriptorProbeFeature).Assembly)
            .AddShell("default", shell => shell.WithFeature<ExpressionDescriptorProbeFeature>()));

        await using var app = builder.Build();
        app.MapShells();
        app.MapElsaWorkflowManagementApi();
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var response = await client.GetAsync("/_elsa/workflow-management/descriptors/expression-descriptors");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var modes = document.RootElement.GetProperty("items").EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("type").GetString()!,
                item => item.GetProperty("editingMode").GetString()!,
                StringComparer.Ordinal);

        Assert.Equal("literal", modes["Literal"]);
        Assert.Equal("text", modes["JavaScript"]);
        Assert.Equal("text", modes["Liquid"]);
        Assert.Equal("structured", modes["Object"]);
        Assert.Equal("reference", modes["Variable"]);
        Assert.Equal("reference", modes["Input"]);
        Assert.Equal("reference", modes["Secret"]);
        Assert.All(modes.Values, mode => Assert.Contains(mode, new[] { "literal", "text", "structured", "reference" }));
    }

    public sealed class ExpressionDescriptorProbeFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IExpressionDescriptorProvider, ProbeExpressionDescriptorProvider>();
            services.AddSingleton<IExpressionDescriptorRegistry>(provider =>
                new ExpressionDescriptorRegistry(provider.GetServices<IExpressionDescriptorProvider>()));
        }
    }

    private sealed class ProbeExpressionDescriptorProvider : IExpressionDescriptorProvider
    {
        public IEnumerable<IExpressionDescriptor> GetDescriptors()
        {
            yield return new JavaScriptExpressionDescriptor();
            yield return new VariableExpressionDescriptor();
            yield return new SecretExpressionDescriptor();
        }
    }
}
