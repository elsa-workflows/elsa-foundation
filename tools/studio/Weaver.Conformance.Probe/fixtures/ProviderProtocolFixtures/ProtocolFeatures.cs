using CShells.Features;
using Elsa.Weaver.Anthropic;
using Elsa.Weaver.Anthropic.Services;
using Elsa.Weaver.GitHubCopilot;
using Elsa.Weaver.GitHubCopilot.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Acme.Weaver.ProtocolFixtures;

[ShellFeature(name: "AcmeCopilotProtocolFixture", DependsOn = new object[] { typeof(WeaverGitHubCopilotFeature) })]
public class CopilotProtocolFixtureFeature : IShellFeature
{
    public string CommandFile { get; set; } = string.Empty;
    public string TraceFile { get; set; } = string.Empty;
    public string GateFile { get; set; } = string.Empty;

    public virtual void ConfigureServices(IServiceCollection services)
    {
        var files = new ProtocolFiles(CommandFile, TraceFile, GateFile);
        services.Replace(ServiceDescriptor.Singleton<IGitHubCopilotClientFactory>(new ProtocolCopilotFactory(files)));
    }
}

[ShellFeature(name: "AcmeAnthropicProtocolFixture", DependsOn = new object[] { typeof(WeaverAnthropicFeature) })]
public class AnthropicProtocolFixtureFeature : IShellFeature
{
    public string CommandFile { get; set; } = string.Empty;
    public string TraceFile { get; set; } = string.Empty;
    public string GateFile { get; set; } = string.Empty;

    public virtual void ConfigureServices(IServiceCollection services)
    {
        var files = new ProtocolFiles(CommandFile, TraceFile, GateFile);
        services.Replace(ServiceDescriptor.Singleton<IAnthropicChatClientFactory>(new ProtocolAnthropicFactory(files)));
    }
}
