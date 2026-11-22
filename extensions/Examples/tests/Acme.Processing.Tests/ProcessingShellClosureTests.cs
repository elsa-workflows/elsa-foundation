using Acme.Processing.Core;
using CShells;
using CShells.Lifecycle;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Features;
using Elsa.Api.Capabilities;
using Elsa.Events;
using Elsa.Foundation.Identity;
using Elsa.Studio.Dashboard.Api;
using Elsa.Studio.Dashboard.Core.Resources;
using Elsa.Studio.Dashboard.Management;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Acme.Processing.Tests;

public sealed class ProcessingShellClosureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shell_cardinality_validation_runs_without_constructing_request_readers(bool duplicate)
    {
        var directory = Directory.CreateTempSubdirectory("acme-shell-closure-");
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = directory.FullName });
            builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
            builder.Services.AddCShellsAspNetCore(shells => shells
                .WithAssemblies(typeof(ProcessingFeature).Assembly, typeof(ProcessingGuardFixtureFeature).Assembly,
                    typeof(DashboardResourcesApiFeature).Assembly, typeof(DashboardManagementFeature).Assembly,
                    typeof(ApiCapabilitiesFeature).Assembly, typeof(FoundationIdentityAbstractionsFeature).Assembly, typeof(EventsFeature).Assembly)
                .AddShell("acme-closure", shell => shell.WithFeature<ProcessingGuardFixtureFeature>(feature => feature.DuplicateReader = duplicate)));
            await using var app = builder.Build(); app.MapShells(); await app.StartAsync();
            var registry = app.Services.GetRequiredService<IShellRegistry>();
            if (duplicate)
            {
                var error = await Assert.ThrowsAnyAsync<Exception>(() => registry.GetOrActivateAsync("acme-closure"));
                Assert.Contains(nameof(IProcessingRecordReader), error.ToString(), StringComparison.Ordinal);
            }
            else Assert.NotNull(await registry.GetOrActivateAsync("acme-closure"));
        }
        finally { directory.Delete(recursive: true); }
    }
}

[ShellFeature(name: "AcmeGuardFixture", DependsOn = [typeof(ProcessingFeature)])]
public sealed class ProcessingGuardFixtureFeature : IShellFeature
{
    public bool DuplicateReader { get; set; }
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IDashboardResourceStore>(_ => throw new InvalidOperationException("Do not construct request storage at activation."));
        services.RemoveAll<IProcessingRecordReader>();
        services.AddScoped<IProcessingRecordReader>(_ => throw new InvalidOperationException("Do not construct request readers at activation."));
        if (DuplicateReader) services.AddScoped<IProcessingRecordReader>(_ => throw new InvalidOperationException("Duplicate request reader."));
    }
}
