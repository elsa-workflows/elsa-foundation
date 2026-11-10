using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.GitHubCopilot.Options;
using Elsa.Weaver.GitHubCopilot.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using static Elsa.Weaver.Core.Extensions.AgentConfigurationBinding;

namespace Elsa.Weaver.GitHubCopilot.Extensions;

public static class WeaverGitHubCopilotServiceCollectionExtensions
{
    public static IServiceCollection AddWeaverGitHubCopilotProvider(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddLogging();
        services.AddOptions<WeaverGitHubCopilotOptions>();
        if (configuration is not null)
            services.Configure<WeaverGitHubCopilotOptions>(options => BindOptions(configuration.GetSection(WeaverGitHubCopilotOptions.SectionName), options));

        services.TryAddSingleton<IGitHubCopilotClientFactory, GitHubCopilotSdkClientFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentProvider, WeaverGitHubCopilotProvider>());
        return services;
    }

    private static void BindOptions(IConfiguration section, WeaverGitHubCopilotOptions options)
    {
        options.Enabled = GetBool(section, nameof(options.Enabled), options.Enabled);
        options.GitHubToken = GetString(section, nameof(options.GitHubToken), options.GitHubToken);
        options.GitHubTokenEnvironmentVariable = GetString(section, nameof(options.GitHubTokenEnvironmentVariable), options.GitHubTokenEnvironmentVariable);
        options.UseLoggedInUser = GetBool(section, nameof(options.UseLoggedInUser), options.UseLoggedInUser);
        options.RuntimeUrl = GetString(section, nameof(options.RuntimeUrl), options.RuntimeUrl);
        options.RuntimeConnectionToken = GetString(section, nameof(options.RuntimeConnectionToken), options.RuntimeConnectionToken);
        options.BaseDirectory = GetString(section, nameof(options.BaseDirectory), options.BaseDirectory);
        options.WorkingDirectory = GetString(section, nameof(options.WorkingDirectory), options.WorkingDirectory);
        options.Model = GetString(section, nameof(options.Model), options.Model) ?? options.Model;
        options.ReasoningEffort = GetString(section, nameof(options.ReasoningEffort), options.ReasoningEffort);
        options.Streaming = GetBool(section, nameof(options.Streaming), options.Streaming);
        options.SystemMessage = GetString(section, nameof(options.SystemMessage), options.SystemMessage);
        options.IncludeSensitiveContextContent = GetBool(section, nameof(options.IncludeSensitiveContextContent), options.IncludeSensitiveContextContent);
        ReplaceList(section, nameof(options.AvailableTools), options.AvailableTools);
        ReplaceList(section, nameof(options.ExcludedTools), options.ExcludedTools);
    }

    private static void ReplaceList(IConfiguration section, string key, IList<string> target)
    {
        var values = section.GetSection(key).GetChildren().Select(x => x.Value).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (values.Count == 0)
            return;

        target.Clear();
        foreach (var value in values)
            target.Add(value!);
    }
}
