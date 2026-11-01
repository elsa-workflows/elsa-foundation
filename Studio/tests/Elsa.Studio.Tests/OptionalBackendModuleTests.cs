using System.Net;
using System.Net.Http.Json;
using Elsa.Studio.Api.Models;
using Elsa.Studio.Core.Models;
using CShells.DependencyInjection;
using CShells.Features;
using Elsa.Studio.Backends;
using Elsa.Studio.Core.Contracts;
using Elsa.Studio.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.Tests;

public sealed class OptionalBackendModuleTests
{
    private static async Task<WebApplication> Start(bool installed, bool enabled, string path = "", bool suppressClient = false, bool customRuntime = false, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production, ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["Studio:Auth:Enabled"] = "false",
            ["Studio:BackendBaseUrl"] = "https://fixed.example/tenant/",
            ["Studio:Backends:Profiles:0:Id"] = "configured",
            ["Studio:Backends:Profiles:0:Name"] = "Configured selection",
            ["Studio:Backends:Profiles:0:BaseUrl"] = "https://selected.example/tenant/",
            ["CShells:Shells:Default:Features:StudioApi"] = "true",
            ["CShells:Shells:Default:Configuration:WebRouting:Path"] = path,
            ["Studio:Api:DisabledModuleIds:0"] = suppressClient ? "Elsa.Studio.Backends" : null
        });
        if (enabled) builder.Configuration["CShells:Shells:Default:Features:BackendsStudio"] = "true";
        var studio = builder.AddElsaStudio(options => options.ShellsFile = null);
        if (installed)
        {
            builder.Services.AddStudioBackendsHostConfiguration(builder.Configuration);
            studio.ConfigureShells(shells => shells.WithAssemblies(typeof(BackendsStudioFeature).Assembly));
        }
        if(customRuntime) {builder.Configuration["CShells:Shells:Default:Features:AcmeRuntime"]="true";studio.ConfigureShells(shells=>shells.WithAssemblies(typeof(AcmeRuntimeFeature).Assembly));}
        configure?.Invoke(builder);
        var app = builder.Build();
        app.UseElsaStudio();
        app.MapElsaStudio();
        try { await app.StartAsync(); return app; }
        catch { await app.DisposeAsync(); throw; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Absent_or_disabled_module_ignores_invalid_optional_configuration(bool installed)
    {
        await using var app = await Start(installed,false,configure:builder => builder.Configuration["Studio:Backends:Profiles:0:BaseUrl"] = "not-a-url");
        using var client = app.GetTestClient();
        var script = await client.GetStringAsync("/studio-runtime.js");
        Assert.Contains("https://fixed.example/tenant/",script);
        Assert.DoesNotContain("Configured selection",script);
        Assert.DoesNotContain("\"backends\"",script);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/_elsa/studio/backends/profiles")).StatusCode);
        var modules = await client.GetStringAsync("/_elsa/studio/modules");
        Assert.DoesNotContain((await client.GetFromJsonAsync<StudioModulesResponse>("/_elsa/studio/modules"))!.Modules,item=>item.Id=="Elsa.Studio.Backends");
    }

    [Theory]
    [InlineData("")]
    [InlineData("/studio-shell")]
    public async Task Enabled_module_projects_its_actual_shell_profile_api_path(string path)
    {
        await using var app = await Start(true,true,path);
        using var client = app.GetTestClient();
        var script = await client.GetStringAsync("/studio-runtime.js");
        Assert.Contains("\"extensions\":{\"backends\":",script);
        Assert.Contains($"\"profileApiPath\":\"{path}/_elsa/studio/backends/profiles\"",script);
        Assert.Contains("Configured selection",script);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(path+"/_elsa/studio/backends/profiles")).StatusCode);
        if (path.Length>0) Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/_elsa/studio/backends/profiles")).StatusCode);
    }

    [Fact]
    public async Task Client_suppression_omits_projection_and_manifest_but_does_not_disable_server_feature()
    {
        await using var app = await Start(true,true,suppressClient:true);
        using var client=app.GetTestClient();
        Assert.DoesNotContain("\"backends\"",await client.GetStringAsync("/studio-runtime.js"));
        Assert.DoesNotContain((await client.GetFromJsonAsync<StudioModulesResponse>("/_elsa/studio/modules"))!.Modules,item=>item.Id=="Elsa.Studio.Backends");
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/_elsa/studio/backends/profiles")).StatusCode);
    }

    [Fact]
    public async Task Custom_authority_and_runtime_source_work_without_backends_module()
    {
        var source = new AuthoritySource();
        await using var app=await Start(false,false,customRuntime:true,configure:builder => {
            builder.Services.AddSingleton<IStudioBackendAuthoritySource>(source);
        });
        using var client=app.GetTestClient();
        var snapshot=app.Services.GetRequiredService<IStudioBackendAuthorityAccessor>().Value;
        Assert.NotSame(source.Authority,snapshot);
        Assert.Contains("StudioApi",source.Context!.EnabledFeatures);
        app.Configuration["Studio:BackendBaseUrl"]="https://changed.example/";
        var script=await client.GetStringAsync("/studio-runtime.js");
        Assert.Contains("https://custom.example/",script);
        Assert.Contains("Acme extension",script);
        Assert.DoesNotContain("https://changed.example/",script);
        Assert.Same(snapshot,app.Services.GetRequiredService<IStudioBackendAuthorityAccessor>().Value);
    }

    [Fact]
    public async Task Null_authority_url_remains_same_origin_after_configuration_reload()
    {
        await using var app=await Start(false,false,configure:builder=>builder.Configuration["Studio:BackendBaseUrl"]=null);
        var snapshot=app.Services.GetRequiredService<IStudioBackendAuthorityAccessor>().Value;
        Assert.Null(snapshot.BrowserBaseUrl);
        app.Configuration["Studio:BackendBaseUrl"]="https://changed.example/";
        using var client=app.GetTestClient();
        var script=await client.GetStringAsync("/studio-runtime.js");
        Assert.Contains("\"backendBaseUrl\":\"\"",script);
        Assert.DoesNotContain("changed.example",script);
    }

    [Fact]
    public async Task Duplicate_authority_sources_fail_startup_explicitly()
    {
        var error=await Assert.ThrowsAsync<StudioHostingException>(()=>Start(false,false,configure:builder=> {
            builder.Services.AddSingleton<IStudioBackendAuthoritySource>(new AuthoritySource());
            builder.Services.AddSingleton<IStudioBackendAuthoritySource>(new AuthoritySource());
        }));
        Assert.Contains("Only one",error.ToString());
    }

    private sealed class AuthoritySource : IStudioBackendAuthoritySource
    {
        public StudioBackendAuthority Authority {get;}=new("https://custom.example/","https://private.example/");
        public StudioBackendAuthorityContext? Context {get;private set;}
        public StudioBackendAuthority Resolve(StudioBackendAuthorityContext context) {Context=context;return Authority;}
    }
    [ShellFeature("AcmeRuntime")]
    public class AcmeRuntimeFeature : IShellFeature
    {
        public virtual void ConfigureServices(IServiceCollection services) {
            services.AddSingleton<IStudioRuntimeConfigurationSource>(provider=>new RuntimeSource(provider.GetRequiredService<IConfiguration>()["Test:DuplicateRuntime"]=="true" ? "backends" : "acme",new {label="Acme extension"}));
        }
    }

    [Fact]
    public async Task Duplicate_runtime_namespaces_fail_closed()
    {
        await using var app=await Start(true,true,customRuntime:true,configure:builder=>builder.Configuration["Test:DuplicateRuntime"]="true");
        using var client=app.GetTestClient();
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.GetAsync("/studio-runtime.js")).StatusCode);
    }

    private sealed class RuntimeSource(string name,object value):IStudioRuntimeConfigurationSource
    {
        public IEnumerable<StudioRuntimeConfigurationEntry> GetConfiguration(string path) => [new(name,value)];
    }
}
