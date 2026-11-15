using CShells.Features;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.TriggerEditor;

[ShellFeature("AcceptanceTriggerEditor", DisplayName = "Acme trigger configuration editor")]
[StudioModule("acceptance-trigger-editor", "Acme trigger configuration editor", "trigger-configuration-editor", HasStyles = false)]
public sealed class Feature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) { }
}
