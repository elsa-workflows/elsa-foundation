using CShells.Features;
using Microsoft.Extensions.DependencyInjection;

namespace StudioReuse.Second;

[ShellFeature("StudioReuseProbe")]
public sealed class SecondFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) => services.AddSingleton("second");
}
