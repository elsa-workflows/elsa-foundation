using CShells.Lifecycle;
using CShells.Lifecycle.Providers;
using Elsa.Studio.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Studio.Tests.Hosting;

public sealed class HostingConfigurationOwnershipTests
{
    [Theory]
    [InlineData("managed")]
    [InlineData("provider")]
    [InlineData("registry")]
    public async Task Applied_file_options_refuse_writes_when_the_host_replaces_shell_ownership(string source)
    {
        var root = Directory.CreateTempSubdirectory("studio-configuration-owner-").FullName;
        try
        {
            var file = Path.Combine(root, "selected-shells.json");
            await File.WriteAllTextAsync(file, """{"CShells":{"Shells":{"Authoring":{"Features":{"StudioApi":{}}}}}}""");
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = root, EnvironmentName = Environments.Production });
            builder.AddElsaStudio(options => { options.ShellName = "Authoring"; options.ShellsFile = file; });
            if (source == "provider")
            {
                // Even equal current values do not establish that a replacement provider follows this writable file.
                builder.Services.Replace(ServiceDescriptor.Singleton<IShellBlueprintProvider>(new ConfigurationShellBlueprintProvider(builder.Configuration.GetSection("CShells:Shells"))));
            }
            if (source == "registry")
            {
                var original = builder.Services.Last(item => item.ServiceType == typeof(IShellRegistry));
                builder.Services.Replace(ServiceDescriptor.Singleton<IShellRegistry>(provider => (IShellRegistry)original.ImplementationFactory!(provider)));
            }
            await using var app = builder.Build();
            var applied = app.Services.GetRequiredService<IOptions<StudioHostingOptions>>().Value;
            Assert.Equal("Authoring", applied.ShellName);
            Assert.Equal(source == "managed" ? file : null, applied.ShellsFile);
            Assert.True(File.Exists(file));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
