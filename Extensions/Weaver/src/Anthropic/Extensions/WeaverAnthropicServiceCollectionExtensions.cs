using Elsa.Weaver.Anthropic.Options;
using Elsa.Weaver.Anthropic.Services;
using Elsa.Weaver.Core.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Elsa.Weaver.Core.Extensions.AgentConfigurationBinding;

namespace Elsa.Weaver.Anthropic.Extensions;

public static class WeaverAnthropicServiceCollectionExtensions
{
    public static IServiceCollection AddWeaverAnthropicProvider(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddLogging();
        services.AddOptions<WeaverAnthropicOptions>();
        if (configuration is not null)
            services.Configure<WeaverAnthropicOptions>(options => BindOptions(configuration.GetSection(WeaverAnthropicOptions.SectionName), options));

        services.TryAddSingleton<IAnthropicChatClientFactory, AnthropicChatClientFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentProvider, WeaverAnthropicProvider>());
        return services;
    }

    private static void BindOptions(IConfiguration section, WeaverAnthropicOptions options)
    {
        options.Enabled = GetBool(section, nameof(options.Enabled), options.Enabled);
        options.ApiKey = GetString(section, nameof(options.ApiKey), options.ApiKey);
        options.ApiKeyEnvironmentVariable = GetString(section, nameof(options.ApiKeyEnvironmentVariable), options.ApiKeyEnvironmentVariable);
        options.Model = GetString(section, nameof(options.Model), options.Model) ?? options.Model;
        options.SystemMessage = GetString(section, nameof(options.SystemMessage), options.SystemMessage);
        options.MaxOutputTokens = GetInt(section, nameof(options.MaxOutputTokens), options.MaxOutputTokens);
        options.IncludeSensitiveContextContent = GetBool(section, nameof(options.IncludeSensitiveContextContent), options.IncludeSensitiveContextContent);
    }
}
