using Elsa.Agent.Anthropic.Options;
using Elsa.Agent.Anthropic.Services;
using Elsa.Agent.Core.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Anthropic.Extensions;

public static class AnthropicAgentServiceCollectionExtensions
{
    public static IServiceCollection AddAnthropicAgentProvider(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddLogging();
        services.AddOptions<AnthropicAgentOptions>();
        if (configuration is not null)
            services.Configure<AnthropicAgentOptions>(options => BindOptions(configuration.GetSection(AnthropicAgentOptions.SectionName), options));

        services.TryAddSingleton<IAnthropicChatClientFactory, AnthropicChatClientFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentProvider, AnthropicAgentProvider>());
        return services;
    }

    private static void BindOptions(IConfiguration section, AnthropicAgentOptions options)
    {
        options.Enabled = GetBool(section, nameof(options.Enabled), options.Enabled);
        options.ApiKey = GetString(section, nameof(options.ApiKey), options.ApiKey);
        options.ApiKeyEnvironmentVariable = GetString(section, nameof(options.ApiKeyEnvironmentVariable), options.ApiKeyEnvironmentVariable);
        options.Model = GetString(section, nameof(options.Model), options.Model) ?? options.Model;
        options.SystemMessage = GetString(section, nameof(options.SystemMessage), options.SystemMessage);
        options.MaxOutputTokens = GetInt(section, nameof(options.MaxOutputTokens), options.MaxOutputTokens);
        options.IncludeSensitiveContextContent = GetBool(section, nameof(options.IncludeSensitiveContextContent), options.IncludeSensitiveContextContent);
    }

    private static string? GetString(IConfiguration section, string key, string? fallback)
        => string.IsNullOrWhiteSpace(section[key]) ? fallback : section[key];

    private static bool GetBool(IConfiguration section, string key, bool fallback)
        => bool.TryParse(section[key], out var value) ? value : fallback;

    private static int GetInt(IConfiguration section, string key, int fallback)
        => int.TryParse(section[key], out var value) ? value : fallback;
}
