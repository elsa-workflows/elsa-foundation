using CShells;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Contracts;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Activities.Http.Tests;

public sealed class HttpActivityRequestExecutorCompositionTests
{
    private const string ShellName = "http-executor-selection";
    private const string ValidatorName = "HttpActivityRequestExecutorCompositionValidator";

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Shell_Prepare_refuses_keyed_only_or_late_duplicate_unkeyed_executors_before_constructor_or_transport(int unkeyedCount)
    {
        var selection = new ExecutorSelection(unkeyedCount);
        await using var root = ShellServices(selection).BuildServiceProvider();

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => root.GetRequiredService<IShellRegistry>()
            .GetOrActivateAsync(ShellName, CancellationToken.None));

        var diagnostic = Chain(failure).Single(x => x.Message.StartsWith("HTTP activities require exactly one unkeyed", StringComparison.Ordinal));
        Assert.Contains($"{nameof(IHttpActivityRequestExecutor)} registration; found {unkeyedCount}.", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(0, selection.Constructions);
        Assert.Equal(0, selection.Exchanges);
    }

    [Fact]
    public async Task Deliberate_replacement_activates_the_shell_and_the_actual_activity_constructor_uses_it()
    {
        var selection = new ExecutorSelection(1);
        await using var root = ShellServices(selection).BuildServiceProvider();
        var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName, CancellationToken.None);
        Assert.Equal(0, selection.Constructions); // Prepare counts descriptors without instantiating providers.

        await using var scope = shell.BeginScope();
        var activity = ActivatorUtilities.CreateInstance<SendHttpRequest>(scope.ServiceProvider);
        Assert.Equal(1, selection.Constructions);
        activity.Url = new Uri("https://unused-original.test/resource");
        activity.Connection = new("unavailable-original", "http");
        var transition = await ((IActivity)activity).ExecuteAsync(
            new("workflow", "invocation", "attempt", "selected", CancellationToken.None));

        var completion = Assert.IsAssignableFrom<IActivityCompletionTransition<SendHttpRequestResult>>(transition);
        Assert.Equal(202, completion.Result.StatusCode);
        Assert.Equal("selected replacement", completion.Result.ResponseBody);
        Assert.Equal(1, selection.Exchanges);
        Assert.Equal("unavailable-original", selection.Request!.Connection!.Name);
    }

    [Fact]
    public async Task Preselected_single_executor_is_preserved_when_the_HTTP_feature_is_composed()
    {
        IServiceCollection services = new ServiceCollection();
        var selection = new ExecutorSelection(1);
        services.AddScoped<IHttpActivityRequestExecutor>(_ => selection.Create());
        new ActivitiesHttpFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();

        await InitializeHttpAsync(provider);
        using var scope = provider.CreateScope();
        _ = ActivatorUtilities.CreateInstance<SendHttpRequest>(scope.ServiceProvider);
        Assert.Equal(1, selection.Constructions);
        Assert.Equal(0, selection.Exchanges);
    }

    [Fact]
    public async Task Copied_collection_rebinds_the_check_and_repeated_composition_keeps_one_initializer()
    {
        IServiceCollection parent = new ServiceCollection();
        new ActivitiesHttpFeature().ConfigureServices(parent);
        IServiceCollection child = new ServiceCollection();
        foreach (var descriptor in parent) child.Add(descriptor);
        new ActivitiesHttpFeature().ConfigureServices(child);
        new ActivitiesHttpFeature().ConfigureServices(child);
        child.AddScoped<IHttpActivityRequestExecutor>(_ => throw new Xunit.Sdk.XunitException("Provider must not construct."));
        await using var provider = child.BuildServiceProvider();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => InitializeHttpAsync(provider));
        Assert.Contains("registration; found 2.", failure.Message, StringComparison.Ordinal);
    }

    internal static Task InitializeHttpAsync(IServiceProvider provider) => Assert.Single(provider.GetServices<IShellInitializer>(),
        initializer => initializer.GetType().Name == ValidatorName).InitializeAsync(CancellationToken.None);

    private static IServiceCollection ShellServices(ExecutorSelection selection)
    {
        var services = new ServiceCollection();
        services.AddSingleton(selection);
        services.AddCShells(builder => builder.WithAssemblies(typeof(HttpModuleFeature).Assembly)
            .AddShell(ShellName, shell => shell.WithFeature<HttpModuleFeature>().WithFeature<ExecutorSelectionFeature>()));
        return services;
    }

    private static IEnumerable<Exception> Chain(Exception? failure)
    {
        while (failure is not null) { yield return failure; failure = failure.InnerException; }
    }

    // Exercise the real HTTP module registration under CShells Prepare. Inbound web middleware and its
    // dependency closure are outside this executor-selection fixture; fresh runtime CLR activation is separate.
    [ShellFeature(name: "TestHttpExecutorModule")]
    public sealed class HttpModuleFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services) => new ActivitiesHttpFeature().ConfigureServices(services);
    }

    [ShellFeature(name: "TestHttpExecutorSelection", DependsOn = new object[] { "TestHttpExecutorModule" })]
    public sealed class ExecutorSelectionFeature(ExecutorSelection selection) : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            if (selection.UnkeyedCount == 0) services.RemoveAll<IHttpActivityRequestExecutor>();
            else if (selection.UnkeyedCount == 1)
                services.Replace(ServiceDescriptor.Scoped<IHttpActivityRequestExecutor>(_ => selection.Create()));
            else services.AddScoped<IHttpActivityRequestExecutor>(_ => selection.Create());
            services.AddKeyedScoped<IHttpActivityRequestExecutor>("independent", (_, _) => selection.Create());
        }
    }

    public sealed class ExecutorSelection(int unkeyedCount)
    {
        public int UnkeyedCount { get; } = unkeyedCount;
        public int Constructions { get; private set; }
        public int Exchanges { get; private set; }
        public HttpActivityRequest? Request { get; private set; }
        public IHttpActivityRequestExecutor Create() { Constructions++; return new SelectedExecutor(this); }

        private sealed class SelectedExecutor(ExecutorSelection selection) : IHttpActivityRequestExecutor
        {
            public ValueTask<SendHttpRequestResult> ExecuteAsync(HttpActivityRequest request, ActivityExecutionContext context,
                CancellationToken cancellationToken = default)
            {
                selection.Exchanges++;
                selection.Request = request;
                return ValueTask.FromResult(new SendHttpRequestResult(202, "selected replacement", new Dictionary<string, string[]>()));
            }
        }
    }
}
