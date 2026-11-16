using System.Text.Json;
using CShells.Features;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Exceptions;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Test-only qualification seam (spec 1193, story 5). It is not product code and never ships.
// The driver selects this feature only for the workflow-authorization-unavailable inventory, through the
// ordinary native feature apply, and deselects it afterwards. It wraps the host's actual workflow
// definition authorizer and reads one private mode file on every decision:
//   {"mode":"native"}                     the actual owner decides, unchanged;
//   {"mode":"deny"}                       a replacement owner that refuses;
//   {"mode":"throw","cause":"<canary>"}   a replacement owner that cannot decide. The canary is the
//                                         private inner cause; the driver proves no surface discloses it.
// A missing, malformed or unknown mode is a fixture error, never a decision: it surfaces as an
// ordinary unexpected failure, which no qualified outcome accepts.
[ShellFeature(name: "QualificationWorkflowAuthority", DependsOn = new object[] { "WorkflowsDesignApi" })]
public sealed class QualificationWorkflowAuthorityFeature : IShellFeature
{
    public string ModeFile { get; set; } = string.Empty;

    public void ConfigureServices(IServiceCollection services)
    {
        var mode = new QualificationAuthorityMode(ModeFile);
        var native = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IWorkflowDefinitionAuthorizer) && !descriptor.IsKeyedService)
            ?? throw new InvalidOperationException("The qualification seam wraps the actual workflow definition authorizer; none is registered.");
        // Exactly one owner stays registered, so the native single-authority startup check still holds.
        services.RemoveAll<IWorkflowDefinitionAuthorizer>();
        services.AddScoped<IWorkflowDefinitionAuthorizer>(provider => new QualificationWorkflowAuthorizer(mode, () => Resolve(provider, native)));
    }

    private static IWorkflowDefinitionAuthorizer Resolve(IServiceProvider provider, ServiceDescriptor native) =>
        native.ImplementationInstance as IWorkflowDefinitionAuthorizer
        ?? native.ImplementationFactory?.Invoke(provider) as IWorkflowDefinitionAuthorizer
        ?? (native.ImplementationType is { } type ? (IWorkflowDefinitionAuthorizer)ActivatorUtilities.CreateInstance(provider, type) : null)
        ?? throw new InvalidOperationException("The actual workflow definition authorizer could not be constructed.");
}

sealed class QualificationWorkflowAuthorizer(QualificationAuthorityMode mode, Func<IWorkflowDefinitionAuthorizer> native) : IWorkflowDefinitionAuthorizer
{
    private IWorkflowDefinitionAuthorizer? actual;

    public ValueTask<bool> CanReadAsync(string definitionId, CancellationToken cancellationToken = default) =>
        DecideAsync(definitionId, "read", owner => owner.CanReadAsync(definitionId, cancellationToken));

    public ValueTask<bool> CanManageAsync(string definitionId, CancellationToken cancellationToken = default) =>
        DecideAsync(definitionId, "manage", owner => owner.CanManageAsync(definitionId, cancellationToken));

    private ValueTask<bool> DecideAsync(string definitionId, string operation, Func<IWorkflowDefinitionAuthorizer, ValueTask<bool>> decide)
    {
        var (selected, cause) = mode.Read();
        return selected switch
        {
            "native" => decide(actual ??= native()),
            "deny" => ValueTask.FromResult(false),
            "throw" => throw new WorkflowDefinitionAuthorizationException(definitionId, operation, new IOException(cause)),
            _ => throw new InvalidOperationException("Unsupported qualification authority mode.")
        };
    }
}

sealed class QualificationAuthorityMode
{
    private readonly string path;

    public QualificationAuthorityMode(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("Set QualificationWorkflowAuthority:ModeFile to an absolute private path.");
        this.path = Path.GetFullPath(path);
    }

    public (string Mode, string? Cause) Read()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(property => property.Name is not ("mode" or "cause")))
            throw new InvalidOperationException("The qualification authority mode file has unsupported members.");
        var mode = root.TryGetProperty("mode", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
        var cause = root.TryGetProperty("cause", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
        if (mode is not ("native" or "deny" or "throw") || (mode == "throw") != !string.IsNullOrWhiteSpace(cause))
            throw new InvalidOperationException("The qualification authority mode file is invalid.");
        return (mode, cause);
    }
}
