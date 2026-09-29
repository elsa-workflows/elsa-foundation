using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Foundation.Host.ModuleManagement;
using Elsa.Persistence.Schema;
using Elsa.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Nuplane.Admin;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>
/// <c>POST /_module-management/reload</c> on <c>Elsa.Foundation.Host</c>: CShells reports a shell it could not activate in its
/// <see cref="ReloadResult"/> and keeps the previous generation serving, so the endpoint has to read the results rather than
/// count them. The refusal here is a type this assembly's host never references, as an EF module package's is: it is
/// recognised by <see cref="IEfModuleRefusal"/> alone.
/// </summary>
public sealed class FoundationHostReloadEndpointTests : IAsyncLifetime
{
    private const string ApiKey = "expected-key";
    private readonly ScriptedShellRegistry _registry = new();
    private WebApplication? _app;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IShellRegistry>(_registry);
        builder.Services.AddSingleton<IRuntimeFeatureCatalog>(new FakeRuntimeFeatureCatalog());
        builder.Services.AddSingleton<INuplaneAdminOperations>(new FakeNuplaneAdminOperations());
        _app = builder.Build();
        _app.MapModuleManagementApi(new ModuleManagementOptions { Enabled = true, ApiKey = ApiKey });
        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add(ModuleManagementOptions.ApiKeyHeader, ApiKey);
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
            await _app.DisposeAsync();
    }

    private async Task<HttpResponseMessage> ReloadAsync() => await _client!.PostAsync("/_module-management/reload", content: null);

    [Fact]
    public async Task Answers_200_when_every_shell_reloaded()
    {
        _registry.Results = [new ReloadResult("default", null, null, null), new ReloadResult("tenant-a", null, null, null)];

        using var response = await ReloadAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("reloaded").GetInt32());
    }

    private static readonly string HostDirectory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

    [Fact]
    public async Task Answers_a_409_naming_the_shell_the_module_the_migrations_and_the_command_when_a_module_refuses()
    {
        _registry.Results =
        [
            new ReloadResult("tenant-a", null, null, null),
            // Wrapped, as an initializer's failure can arrive out of a shell's activation.
            new ReloadResult("default", null, null, new InvalidOperationException("Shell 'default' failed to activate.", new PendingRefusal()))
        ];

        using var response = await ReloadAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        Assert.Equal(1, body.GetProperty("reloaded").GetInt32());
        AssertOrdersRefusal(body, PendingRefusal.Text);
    }

    [Fact]
    public async Task Finds_a_refusal_however_deep_the_inner_exceptions_nest_it()
    {
        _registry.Results = [new ReloadResult("default", null, null, new InvalidOperationException("outer", new InvalidOperationException("middle", new PendingRefusal())))];

        using var response = await ReloadAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        AssertOrdersRefusal(await response.Content.ReadFromJsonAsync<JsonElement>(), PendingRefusal.Text);
    }

    [Fact]
    public async Task Finds_a_refusal_inside_an_AggregateException_as_CShells_reports_one()
    {
        _registry.Results = [new ReloadResult("default", null, null, new AggregateException(new InvalidOperationException("unrelated"), new InvalidOperationException("wrapper", new PendingRefusal())))];

        using var response = await ReloadAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        AssertOrdersRefusal(await response.Content.ReadFromJsonAsync<JsonElement>(), PendingRefusal.Text);
    }

    [Fact]
    public async Task Recognises_a_refusal_by_the_full_name_of_its_interface_when_that_is_a_private_copy_of_the_shared_assembly()
    {
        var foreign = ForeignRefusal.Create(PendingRefusal.Text);
        Assert.False(foreign is IEfModuleRefusal, "The refusal must not be this assembly's interface, or the fast path answers instead of the name path.");
        _registry.Results = [new ReloadResult("default", null, null, new InvalidOperationException("Shell 'default' failed to activate.", foreign))];

        using var response = await ReloadAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        AssertOrdersRefusal(await response.Content.ReadFromJsonAsync<JsonElement>(), PendingRefusal.Text);
    }

    [Fact]
    public async Task Answers_a_500_for_any_other_failed_shell_without_carrying_its_message()
    {
        _registry.Results = [new ReloadResult("default", null, null, new InvalidOperationException("Host=db;Password=hunter2 refused"))];

        using var response = await ReloadAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains(nameof(InvalidOperationException), text, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        var shell = Assert.Single(JsonDocument.Parse(text).RootElement.GetProperty("shells").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, shell.GetProperty("code").ValueKind);
    }

    [Fact]
    public async Task Answers_a_500_when_a_refused_shell_is_reloaded_beside_one_that_failed_for_another_reason()
    {
        _registry.Results =
        [
            new ReloadResult("default", null, null, new PendingRefusal()),
            new ReloadResult("tenant-a", null, null, new InvalidOperationException("boom"))
        ];

        using var response = await ReloadAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("shells").GetArrayLength());
        Assert.Equal(0, body.GetProperty("reloaded").GetInt32());
    }

    /// <summary>The one shell named <c>default</c> is refused for <c>Orders</c>, and its command names this host's directory rather than a placeholder.</summary>
    private static void AssertOrdersRefusal(JsonElement body, string message)
    {
        var detail = body.GetProperty("detail").GetString();
        Assert.Contains("Shell 'default'", detail, StringComparison.Ordinal);
        Assert.Contains(message.Replace(IEfModuleRefusal.HostPlaceholder, $"\"{HostDirectory}\""), detail, StringComparison.Ordinal);
        Assert.DoesNotContain("<host directory>", detail, StringComparison.Ordinal);
        var shell = Assert.Single(body.GetProperty("shells").EnumerateArray());
        Assert.Equal("default", shell.GetProperty("shell").GetString());
        Assert.Equal("Orders", shell.GetProperty("module").GetString());
        Assert.Equal(IEfModuleRefusal.PendingMigrationsCode, shell.GetProperty("code").GetString());
        Assert.Equal(["20260930_One", "20260930_Two"], shell.GetProperty("pendingMigrations").EnumerateArray().Select(id => id.GetString()));
        Assert.Equal(
            $"dotnet elsa persistence apply --host \"{HostDirectory}\" --modules Orders --provider Sqlite --connection-env ELSA_EF_CONNECTION",
            shell.GetProperty("command").GetString());
    }

    /// <summary>An EF module's refusal as this host meets it: a type of the module's own, known here only by the shared interface.</summary>
    private sealed class PendingRefusal() : InvalidOperationException(Text), IEfModuleRefusal
    {
        public const string ApplyCommand = $"dotnet elsa persistence apply --host {IEfModuleRefusal.HostPlaceholder} --modules Orders --provider Sqlite --connection-env ELSA_EF_CONNECTION";
        public const string Text = $"EF module 'Orders' has pending migrations: 20260930_One, 20260930_Two. Apply them out of process with `{ApplyCommand}`.";

        public string Module => "Orders";

        public string Code => IEfModuleRefusal.PendingMigrationsCode;

        public IReadOnlyList<string> PendingMigrations => ["20260930_One", "20260930_Two"];

        public string? Command => ApplyCommand;
    }

    /// <summary>Answers <see cref="ReloadActiveAsync"/> with what a test scripts; nothing else of the registry is reached.</summary>
    private sealed class ScriptedShellRegistry : IShellRegistry
    {
        public IReadOnlyList<ReloadResult> Results { get; set; } = [];

        public Task<IReadOnlyList<ReloadResult>> ReloadActiveAsync(ReloadOptions? options = null, CancellationToken cancellationToken = default) => Task.FromResult(Results);

        public Task<IShell> GetOrActivateAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IShell> ActivateAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReloadResult> ReloadAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IDrainOperation> DrainAsync(IShell shell, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UnregisterBlueprintAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProvidedBlueprint?> GetBlueprintAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IShellBlueprintManager?> GetManagerAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShellPage> ListAsync(ShellListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IShell? GetActive(string name) => null;
        public IReadOnlyCollection<IShell> GetAll(string name) => [];
        public IReadOnlyCollection<IShell> GetActiveShells() => [];
        public void Subscribe(IShellLifecycleSubscriber subscriber) { }
        public void Unsubscribe(IShellLifecycleSubscriber subscriber) { }
    }

    /// <summary>
    /// A refusal built against a private copy of <c>Elsa.Persistence.Schema</c>: a dynamic assembly declares its own
    /// <c>Elsa.Persistence.Schema.IEfModuleRefusal</c>, the same full name as the host's and not the same type, as a module
    /// package's copy is in a load context of its own, and an exception that implements that.
    /// </summary>
    private static class ForeignRefusal
    {
        public static InvalidOperationException Create(string message)
        {
            var module = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("PrivateCopyOfElsaPersistenceSchema"), AssemblyBuilderAccess.RunAndCollect).DefineDynamicModule("main");
            var contract = module.DefineType(typeof(IEfModuleRefusal).FullName!, TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
            var members = new (string Name, Type Type)[]
            {
                (nameof(IEfModuleRefusal.Module), typeof(string)),
                (nameof(IEfModuleRefusal.Code), typeof(string)),
                (nameof(IEfModuleRefusal.PendingMigrations), typeof(IReadOnlyList<string>)),
                (nameof(IEfModuleRefusal.Command), typeof(string))
            };
            foreach (var (name, type) in members)
            {
                var getter = contract.DefineMethod($"get_{name}", MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.SpecialName, type, Type.EmptyTypes);
                contract.DefineProperty(name, PropertyAttributes.None, type, null).SetGetMethod(getter);
            }

            var contractType = contract.CreateType();

            var refusal = module.DefineType("PrivateCopyRefusal", TypeAttributes.Public | TypeAttributes.Sealed, typeof(InvalidOperationException), [contractType]);
            var constructor = refusal.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(string)]);
            var il = constructor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
            il.Emit(OpCodes.Ret);
            foreach (var (name, type) in members)
            {
                var field = refusal.DefineField($"_{name}", type, FieldAttributes.Public);
                var getter = refusal.DefineMethod($"get_{name}", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.SpecialName, type, Type.EmptyTypes);
                il = getter.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, field);
                il.Emit(OpCodes.Ret);
                refusal.DefineMethodOverride(getter, contractType.GetMethod($"get_{name}")!);
            }

            var created = refusal.CreateType();
            var instance = (InvalidOperationException)Activator.CreateInstance(created, message)!;
            created.GetField($"_{nameof(IEfModuleRefusal.Module)}")!.SetValue(instance, "Orders");
            created.GetField($"_{nameof(IEfModuleRefusal.Code)}")!.SetValue(instance, IEfModuleRefusal.PendingMigrationsCode);
            created.GetField($"_{nameof(IEfModuleRefusal.PendingMigrations)}")!.SetValue(instance, new[] { "20260930_One", "20260930_Two" });
            created.GetField($"_{nameof(IEfModuleRefusal.Command)}")!.SetValue(instance, PendingRefusal.ApplyCommand);
            return instance;
        }
    }
}
