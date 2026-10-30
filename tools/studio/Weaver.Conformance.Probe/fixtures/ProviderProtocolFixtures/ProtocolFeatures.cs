using CShells.Features;
using Elsa.Agent.Anthropic;
using Elsa.Agent.Anthropic.Services;
using Elsa.Agent.GitHubCopilot;
using Elsa.Agent.GitHubCopilot.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Acme.Weaver.ProtocolFixtures;

[ShellFeature(name: "AcmeCopilotProtocolFixture", DependsOn = new object[] { typeof(GitHubCopilotAgentFeature) })]
public class CopilotProtocolFixtureFeature : IShellFeature
{
    public string CommandFile { get; set; } = string.Empty;
    public string TraceFile { get; set; } = string.Empty;

    public virtual void ConfigureServices(IServiceCollection services)
    {
        var files = new ProtocolFiles(CommandFile, TraceFile);
        services.Replace(ServiceDescriptor.Singleton<IGitHubCopilotClientFactory>(new ProtocolCopilotFactory(files)));
    }
}

[ShellFeature(name: "AcmeAnthropicProtocolFixture", DependsOn = new object[] { typeof(AnthropicAgentFeature) })]
public class AnthropicProtocolFixtureFeature : IShellFeature
{
    public string CommandFile { get; set; } = string.Empty;
    public string TraceFile { get; set; } = string.Empty;

    public virtual void ConfigureServices(IServiceCollection services)
    {
        var files = new ProtocolFiles(CommandFile, TraceFile);
        services.Replace(ServiceDescriptor.Singleton<IAnthropicChatClientFactory>(new ProtocolAnthropicFactory(files)));
    }
}
