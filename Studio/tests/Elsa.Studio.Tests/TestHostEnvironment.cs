using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.Tests;

internal sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
{
    public string ApplicationName { get; set; } = "Elsa.Studio.Tests";
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ContentRootPath { get; set; } = contentRootPath;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
