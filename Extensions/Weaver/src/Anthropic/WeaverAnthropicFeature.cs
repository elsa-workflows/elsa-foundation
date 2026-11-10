using CShells.Features;
using Elsa.Weaver.Anthropic.Extensions;
using Elsa.Weaver.Anthropic.Options;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Anthropic;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Weaver")]
[ManifestFeatureCategory("Provider")]
[ShellFeature(
    name: "WeaverAnthropic",
    DisplayName = "Anthropic (Claude) Agent Provider",
    Description = "Registers the Anthropic Claude agent provider, driven by the host orchestrator over the shared tool currency. Enabling this excludes other agent harnesses (single active harness).",
    DependsOn = new object[] { typeof(WeaverFeature) }
)]
public class WeaverAnthropicFeature : WeaverFeature
{
    public bool Enabled { get; set; }

    [ManifestSetting(Secret = true)]
    public string? ApiKey { get; set; }

    public string? ApiKeyEnvironmentVariable { get; set; } = "ANTHROPIC_API_KEY";

    public string Model { get; set; } = "claude-opus-4-8";

    public string? SystemMessage { get; set; }

    public int MaxOutputTokens { get; set; } = 4096;

    public bool IncludeSensitiveContextContent { get; set; }

    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddWeaverAnthropicProvider();
        services.Configure<WeaverAnthropicOptions>(options =>
        {
            options.Enabled = Enabled;
            options.ApiKey = ApiKey;
            options.ApiKeyEnvironmentVariable = ApiKeyEnvironmentVariable;
            options.Model = Model;
            options.SystemMessage = SystemMessage;
            options.MaxOutputTokens = MaxOutputTokens;
            options.IncludeSensitiveContextContent = IncludeSensitiveContextContent;
        });
    }
}
