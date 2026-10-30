using CShells.Features;
using Elsa.Agent.Anthropic.Extensions;
using Elsa.Agent.Anthropic.Options;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Anthropic;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ManifestFeatureCategory("Provider")]
[ShellFeature(
    name: "AnthropicAgent",
    DisplayName = "Anthropic (Claude) Agent Provider",
    Description = "Registers the Anthropic Claude agent provider, driven by the host orchestrator over the shared tool currency. Enabling this excludes other agent harnesses (single active harness).",
    DependsOn = new object[] { typeof(FoundationAgentAbstractionsFeature) }
)]
public class AnthropicAgentFeature : FoundationAgentAbstractionsFeature
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
        services.AddAnthropicAgentProvider();
        services.Configure<AnthropicAgentOptions>(options =>
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
