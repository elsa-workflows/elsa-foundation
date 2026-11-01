using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Core.Extensions;
using Elsa.Pipelines.Core.Contracts;
using Elsa.Studio.Core.Events;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Implicitly_enabled_Backends_selects_the_same_immutable_host_authority_as_explicit_enablement(bool rawDisabled)
    {
        await using var app = await Start(true, false, customRuntime: true, configure: builder =>
        {
            builder.Configuration["Studio:BackendBaseUrl"] = null;
            builder.Configuration["CShells:Shells:Default:Features:ImplicitBackends"] = "true";
            if (rawDisabled) builder.Configuration["CShells:Shells:Default:Features:BackendsStudio"] = "false";
        });
        using var client = app.GetTestClient();
        Assert.Contains("Configured selection", await client.GetStringAsync("/studio-runtime.js"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/_elsa/studio/backends/profiles")).StatusCode);
        var authority = app.Services.GetRequiredService<IStudioBackendAuthorityAccessor>().Value;
        Assert.Equal("https://selected.example/tenant/", authority.BrowserBaseUrl);
        Assert.Equal(authority.BrowserBaseUrl, authority.ServerBaseUrl);
    }

    [ShellFeature("ImplicitBackends", DependsOn = new object[] { typeof(BackendsStudioFeature) })]
    public class ImplicitBackendsFeature : IShellFeature
    {
        public virtual void ConfigureServices(IServiceCollection services) { }
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
            services.AddScoped(_ => new RuntimeScopeMarker());
            services.AddScoped<IStudioRuntimeConfigurationSource>(provider=>new RuntimeSource(provider.GetRequiredService<IConfiguration>()["Test:DuplicateRuntime"]=="true" ? "backends" : "acme",new {label="Acme extension",scope=provider.GetRequiredService<RuntimeScopeMarker>().Id}));
            services.AddEventHandler<OnStudioRuntimeConfigurationCollecting, RuntimeObserver>();
        }
    }

    [Fact]
    public async Task Duplicate_runtime_namespaces_fail_closed()
    {
        await using var app=await Start(true,true,customRuntime:true,configure:builder=>builder.Configuration["Test:DuplicateRuntime"]="true");
        using var client=app.GetTestClient();
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.GetAsync("/studio-runtime.js")).StatusCode);
    }

    [Fact]
    public async Task Runtime_projection_awaits_the_selected_shell_event_pipeline_and_independent_subscribers()
    {
        await using var app = await Start(false, false, customRuntime: true);
        var shell = await app.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync("Default", CancellationToken.None);
        await using (var scope = shell.BeginScope())
            scope.ServiceProvider.GetRequiredService<IEventPipeline>().Setup(builder => builder.UseMiddleware<RuntimeProjectionMiddleware>());
        using var client = app.GetTestClient();
        var script = await client.GetStringAsync("/studio-runtime.js");
        using var json = JsonDocument.Parse(script[script.IndexOf('{')..(script.LastIndexOf('}') + 1)]);
        var extensions = json.RootElement.GetProperty("extensions");
        var marker = extensions.GetProperty("acme").GetProperty("scope").GetString();
        Assert.False(string.IsNullOrEmpty(marker));
        Assert.Equal(marker, extensions.GetProperty("pipeline").GetString());
        Assert.Equal(marker, extensions.GetProperty("observer").GetString());
    }

    private sealed class RuntimeScopeMarker { public string Id { get; } = Guid.NewGuid().ToString("N"); }

    private sealed class RuntimeObserver(RuntimeScopeMarker marker) : IEventHandler<OnStudioRuntimeConfigurationCollecting>
    {
        public Task Handle(OnStudioRuntimeConfigurationCollecting value, CancellationToken token)
        {
            value.Configuration.Add(new("observer", marker.Id));
            return Task.CompletedTask;
        }
    }

    private sealed class RuntimeProjectionMiddleware(PipelineDelegate<IEventContext> next) : IEventMiddleware
    {
        public async ValueTask Invoke(IEventContext context)
        {
            await next(context);
            await Task.Yield();
            if (context.Event is OnStudioRuntimeConfigurationCollecting value)
                value.Configuration.Add(new("pipeline", context.ServiceProvider.GetRequiredService<RuntimeScopeMarker>().Id));
        }
    }

    private sealed class RuntimeSource(string name,object value):IStudioRuntimeConfigurationSource
    {
        public IEnumerable<StudioRuntimeConfigurationEntry> GetConfiguration(string path) => [new(name,value)];
    }
}
