using CShells.Features;
using Microsoft.Extensions.DependencyInjection;

namespace StudioReuse.First;

[ShellFeature("StudioReuseProbe")]
public sealed class FirstFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) => services.AddSingleton("first");
}
