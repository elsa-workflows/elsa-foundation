using System.Text.Json;
using CShells;
using CShells.Configuration;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using Elsa.Agent.Anthropic;
using Elsa.Agent.Anthropic.Services;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.GitHubCopilot;
using Elsa.Events;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var observations = new List<string>();
foreach (var leaf in new[] { typeof(AnthropicAgentFeature), typeof(GitHubCopilotAgentFeature) })
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    services.AddCShells(builder => builder.WithAssemblies(leaf.Assembly, typeof(EventsFeature).Assembly)
        .AddShell("compatibility", shell => shell.WithFeature(leaf)));
    await using var root = services.BuildServiceProvider();
    var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync("compatibility");
    await using var scope = shell.BeginScope();
    var provider = scope.ServiceProvider.GetRequiredService<IAgentProviderRegistry>().Active;
    if (provider is null) throw new InvalidOperationException("Provider not composed: " + leaf.Name);
    scope.ServiceProvider.GetRequiredService<IAgentStreamingService>();
    scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
    observations.Add(leaf.Name + " activated and resolves current Agent contracts");
    if (leaf == typeof(AnthropicAgentFeature))
    {
        using var unusedClient = scope.ServiceProvider.GetRequiredService<IAnthropicChatClientFactory>().Create("fixture-unused", "fixture-model");
        observations.Add("Current Anthropic SDK factory constructs IChatClient under resolved abstractions without network calls");
    }
}
Console.WriteLine(JsonSerializer.Serialize(new
{
    observations,
    maf = typeof(ChatClientAgent).Assembly.GetName().Version?.ToString(),
    abstractions = typeof(IChatClient).Assembly.GetName().Version?.ToString(),
    sourceProjectComposition = true,
    packagedHost = false,
    providerRequests = false
}, new JsonSerializerOptions { WriteIndented = true }));
