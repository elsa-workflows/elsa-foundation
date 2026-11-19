using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Exceptions;
﻿using CShells.Features;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Services;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.JavaScript;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Expressions")]
[ManifestFeatureCategory("JavaScript")]
[ShellFeature(
    name: "JavaScriptExpressions",
    DisplayName = "JavaScript Expressions",
    Description = "Provides portable JavaScript expression evaluation")]
public class JavaScriptFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services
            .AddScoped<IPortableExpressionHandler, PortableJavaScriptExpressionHandler>()
            .AddScoped<IExpressionDescriptorProvider, JavaScriptExpressionDescriptorProvider>()
            .AddSingleton<IExpressionToolingProvider, JavaScriptExpressionToolingProvider>()
            .AddSingleton<IJavaScriptScriptOutcomeAnalyzer, JavaScriptScriptOutcomeAnalyzer>()
            .AddSingleton<IScriptVariableReadAnalyzer, JavaScriptVariableReadAnalyzer>();
        // Spec 1228 FR-028: publication pins this version into each node that uses JavaScript; the runtime checks it.
        if (!services.Any(descriptor => descriptor.ImplementationInstance is ExpressionValueAdapter { Language: "JavaScript" }))
            services.AddSingleton(new ExpressionValueAdapter("JavaScript", JavaScriptValueAdapter.Version));
    }
}
