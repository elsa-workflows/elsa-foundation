using System.Text.Json;
using CShells.Features;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Acme.TriggerAuthoring;

/// <summary>Records actual composed services; it neither replaces nor starts any product operation.</summary>
[ShellFeature("AcceptanceTriggerCompositionProbe", DisplayName = "Owned trigger composition probe", DependsOn = new object[] { "Tasks" })]
public sealed class CompositionProbeFeature : IShellFeature, IPostConfigureShellServices
{
    public string OwnerToken { get; set; } = "";
    public string SourceRevision { get; set; } = "";
    public string Shape { get; set; } = "";
    public void ConfigureServices(IServiceCollection services) { }
    public void PostConfigureServices(IServiceCollection services)
    {
        if (string.IsNullOrWhiteSpace(OwnerToken) || SourceRevision.Length != 40 || SourceRevision.Any(character => !char.IsAsciiHexDigit(character)) ||
            Shape is not ("design-only" or "runtime-only" or "combined")) throw new InvalidOperationException("An exact owned topology proof is required.");
        var types = services.Select(item => item.ServiceType.FullName ?? "").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var runtime = services.Any(item => item.ServiceType == typeof(IRuntimePostCommitOutboxProcessor));
        var admission = services.Any(item => item.ServiceType == typeof(IStandaloneTriggerIngressFactory));
        var delivery = services.Any(item => item.ServiceType == typeof(IRuntimeIngressDeliveryStore));
        var design = types.Any(name => name == "Elsa.Workflows.Design.Persistence.Core.Stores.IWorkflowDefinitionStore");
        if (Shape == "design-only" && (runtime || admission || delivery) || Shape == "runtime-only" && (design || !runtime || !admission || !delivery) ||
            Shape == "combined" && (!design || !runtime || !admission || !delivery))
            throw new InvalidOperationException("Actual service composition contradicts the requested host shape.");
        services.AddSingleton<IStartupTask>(provider => new RecordComposition(provider.GetRequiredService<IHostEnvironment>(), OwnerToken, SourceRevision, Shape, runtime, admission, delivery, design, types));
    }
}
internal sealed class RecordComposition(IHostEnvironment environment, string token, string source, string shape, bool runtime, bool admission, bool delivery, bool design, string[] services) : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var root = environment.ContentRootPath;
        if (!Path.IsPathFullyQualified(root) || new DirectoryInfo(root).LinkTarget is not null) throw new InvalidOperationException("Owned composition root must be regular and absolute.");
        var marker = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, ".ux-acceptance-owner.json"), cancellationToken));
        using (marker)
            if (marker.RootElement.GetProperty("ownerToken").GetString() != token || !marker.RootElement.GetProperty("compositionFixture").GetBoolean())
                throw new InvalidOperationException("The composition proof does not own this host.");
        var path = Path.Combine(root, "composition-ready.json");
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new { processId = Environment.ProcessId, sourceSha = source, shape,
            runtimeProcessor = runtime, ingressFactory = admission, deliveryStore = delivery, designStore = design, services }), cancellationToken);
        File.Move(temporary, path, true);
    }
}
