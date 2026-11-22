using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa3.Mapping.ActivityMap;
using Elsa3.Mapping.ActivityMap.Rewrites;
using Elsa3.Mapping.Contracts;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Internal;
using Elsa3.Mapping.Resolution;
using Elsa3.Mapping.Mappings;
using Elsa3.Mapping.Services;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa3.Mapping;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Elsa3")]
[ManifestFeatureCategory("Mapping")]
[ShellFeature(
    name: "Elsa3Mapping",
    DisplayName = "Elsa 3 Mapping",
    Description = "Provides converters for Elsa3 workflow models",
    // The converter checks converted scripts with the JavaScript strategy's .NET-usage analysis (spec 1223 FR-016).
    DependsOn = new object[] { "JavaScriptExpressions" }
)]
public class Elsa3MappingFeature : IShellFeature
{
    /// <summary>
    /// How Elsa 3 local date and time is read (spec 1223 FR-034, ADR 0046 Dates): <c>Refuse</c> (the default; nothing is
    /// assumed), <c>AssumeUtc</c> (convert as if the Elsa 3 server ran on UTC, with a warning per affected expression) or
    /// <c>SourceTimeZone</c> (convert with <see cref="SourceTimeZone"/> written into the converted code).
    /// </summary>
    [ManifestSetting(DisplayName = "Date interpretation", Description = "How the converter reads Elsa 3 local date and time, which Elsa 3 evaluated in its server's time zone: Refuse (default), AssumeUtc (convert as UTC, with a warning per expression) or SourceTimeZone (convert in the Elsa 3 server's time zone, written into the converted code).", Category = "Conversion")]
    public Elsa3DateInterpretation DateInterpretation { get; set; } = Elsa3DateInterpretation.Refuse;

    /// <summary>The Elsa 3 server's IANA time zone (for example <c>Europe/Amsterdam</c>), with <c>SourceTimeZone</c> only.</summary>
    [ManifestSetting(DisplayName = "Source time zone", Description = "The IANA time zone of the Elsa 3 server, for example Europe/Amsterdam; used only with the date interpretation SourceTimeZone.", Category = "Conversion")]
    public string? SourceTimeZone { get; set; }

    public virtual void ConfigureServices(IServiceCollection services)
    {
        var conversion = new Elsa3ConversionOptions { DateInterpretation = DateInterpretation, SourceTimeZone = SourceTimeZone };
        // A setting that cannot be applied fails shell activation, not the first conversion.
        conversion.Validate();
        services.Configure<Elsa3ConversionOptions>(options =>
        {
            options.DateInterpretation = conversion.DateInterpretation;
            options.SourceTimeZone = conversion.SourceTimeZone;
        });

        // IElsa3ActivityMap, IElsa3DefinitionConverter and IElsa3NodeConverter are replacement contracts (§2.6.2): the
        // defaults are registered with TryAdd, so a host's own registration wins, and composing more than one fails
        // shell activation. So does a host replacing only one of the two converters, when the registrations show it; a
        // definition-only converter registered through a factory fails when the node converter is resolved instead.
        Elsa3MappingCompositionCheck.Register(services);

        // The built-in map is a static table: one per application (§2.5.1). Resolution caches per conversion scope.
        services.TryAddSingleton<IElsa3ActivityMap, Elsa3BuiltInActivityMap>();
        services.AddScoped<Elsa3ReplacedActivityTypeIndex>();
        services.AddScoped<Elsa3ActivityResolver>();
        services.AddSingleton<IElsa3NodeRewrite, Elsa3SwitchValueSynthesisRewrite>();
        services.AddSingleton<IElsa3NodeRewrite, Elsa3VariableReferenceRewrite>();
        services.AddSingleton<IElsa3NodeRewrite, Elsa3TerminalPlacementRewrite>();
        services.AddSingleton<IElsa3NodeRewrite, Elsa3ParallelBranchNamesRewrite>();
        services.AddSingleton<IElsa3NodeRewrite, Elsa3LoopVariableReferencesRewrite>();
        services.AddSingleton<IElsa3NodeRewrite, Elsa3HttpTriggerResponseModeRewrite>();
        services.AddSingleton<IElsa3NodeRewrite, Elsa3ReusableOutputWriteRewrite>();
        services.AddScoped<Elsa3ActivityNormalizer>();
        services.AddScoped<Elsa3DefinitionConverter>();
        services.TryAddScoped(Elsa3DefaultConverters.DefinitionConverter);
        // The default node converter forwards to whichever definition converter is composed.
        services.TryAddScoped(Elsa3DefaultConverters.NodeConverter);
        services.AddScoped<Elsa3ArgumentDefinitionToInputOutput>();
        services.AddScoped<Elsa3MemoryReferenceGraph>();
        services.AddScoped(provider => new Elsa3ExpressionRewriter(
            provider.GetRequiredService<IJavaScriptDotNetUsageAnalyzer>(),
            provider.GetRequiredService<IOptions<Elsa3ConversionOptions>>().Value));
        services.AddScoped<Elsa3ValueFlowLowerer>();
        services.AddScoped<Elsa3ActivityToState>();
        services.AddScoped<Elsa3WorkflowDefinitionToState>();
        services.AddScoped<Elsa3WorkflowDefinitionToWorkflowDefinitionVersion>();
        services.AddScoped<IReusableActivityImportMaterializer, Elsa3ReusableActivityImportMaterializer>();
        // Lets the import analyzer refuse only real start triggers in a reusable workflow (spec 246 D-6).
        services.TryAddSingleton<IReusableActivityTriggerClassifier, Elsa3ReusableActivityTriggerClassifier>();
        // The converter's revision numbers and identifies imported versions ({n}.{R}.0, #3921); a host or test may
        // register its own first.
        services.TryAddSingleton<IElsa3ConverterRevision, Elsa3ConverterRevision>();
        services.AddScoped<IElsa3WorkflowDefinitionImporter, Elsa3WorkflowDefinitionImporter>();
    }
}
